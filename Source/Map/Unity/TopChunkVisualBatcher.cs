using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace SeoulPlayup.Map.Unity
{
    /// <summary>One batched copy of a top prefab: where it lands, in map-local space.</summary>
    internal readonly struct TopChunkInstance
    {
        public TopChunkInstance(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }

        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
    }

    /// <summary>
    /// A chunk-safe top prefab reduced to the four things batching needs. Creation doubles as the
    /// chunk-safety test: <see cref="TryCreate"/> refuses anything that cannot be merged into a shared
    /// mesh, and <see cref="AtlasTilePresentationView.IsTopPrefabChunkSafeForTests"/> is exactly that
    /// refusal exposed to the gates. The rules are mirrored (deliberately, on scene GameObjects rather
    /// than prefabs) by the board-chunk-audit editor tool — keep the two in step.
    /// </summary>
    internal sealed class TopChunkDescriptor
    {
        private TopChunkDescriptor(Mesh mesh, Material material, Matrix4x4 meshToPrefabMatrix, float topSurfaceLocalY)
        {
            Mesh = mesh;
            Material = material;
            MeshToPrefabMatrix = meshToPrefabMatrix;
            TopSurfaceLocalY = topSurfaceLocalY;
        }

        public Mesh Mesh { get; }
        public Material Material { get; }
        public Matrix4x4 MeshToPrefabMatrix { get; }
        public float TopSurfaceLocalY { get; }

        public static bool TryCreate(GameObject prefab, out TopChunkDescriptor descriptor)
        {
            descriptor = null;
            if (prefab == null)
            {
                return false;
            }

            var renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
            var meshFilters = prefab.GetComponentsInChildren<MeshFilter>(true);
            if (renderers.Length != 1 || meshFilters.Length != 1)
            {
                return false;
            }

            if (prefab.GetComponentsInChildren<Collider>(true).Length > 0)
            {
                return false;
            }

            if (prefab.GetComponentsInChildren<Component>(true).Any(component => component != null &&
                !(component is Transform) && !(component is MeshFilter) && !(component is MeshRenderer)))
            {
                return false;
            }

            var renderer = renderers[0];
            var meshFilter = meshFilters[0];
            var mesh = meshFilter.sharedMesh;
            if (renderer.sharedMaterials == null || renderer.sharedMaterials.Length != 1 || renderer.sharedMaterial == null || mesh == null)
            {
                return false;
            }

            // Animated water needs a per-tile property block so it can keep its Shader Graph
            // animation while receiving the tile's visibility-lighting value.
            //
            // This one rule is read off the view rather than a host member: the same predicate also
            // drives the visibility-lighting material path, which stays on the view, and TryCreate is
            // static (the chunk-safety gates call it without a view instance), so a host interface
            // cannot carry it. Deliberate back-reference, not an oversight.
            if (AtlasTilePresentationView.IsVisibilityPreservingWaterMaterial(renderer.sharedMaterial))
            {
                return false;
            }

            if (!CanReadMesh(mesh))
            {
                return false;
            }

            var meshToPrefab = prefab.transform.worldToLocalMatrix * meshFilter.transform.localToWorldMatrix;
            descriptor = new TopChunkDescriptor(mesh, renderer.sharedMaterial, meshToPrefab, ResolveTopSurfaceLocalY(mesh.bounds, meshToPrefab));
            return true;
        }

        public bool Matches(TopChunkDescriptor other)
        {
            return other != null && Mesh == other.Mesh && Material == other.Material && MatrixApproximately(MeshToPrefabMatrix, other.MeshToPrefabMatrix);
        }

        private static bool CanReadMesh(Mesh mesh)
        {
            try
            {
                using (Mesh.AcquireReadOnlyMeshData(mesh))
                {
                    return true;
                }
            }
            catch (UnityException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static float ResolveTopSurfaceLocalY(Bounds bounds, Matrix4x4 meshToPrefab)
        {
            var min = bounds.min;
            var max = bounds.max;
            var corners = new[]
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(max.x, max.y, max.z)
            };

            var maxY = 0f;
            for (var i = 0; i < corners.Length; i++)
            {
                var localY = meshToPrefab.MultiplyPoint3x4(corners[i]).y;
                if (i == 0 || localY > maxY)
                {
                    maxY = localY;
                }
            }

            return maxY;
        }

        private static bool MatrixApproximately(Matrix4x4 left, Matrix4x4 right)
        {
            for (var row = 0; row < 4; row++)
            {
                for (var column = 0; column < 4; column++)
                {
                    if (!Mathf.Approximately(left[row, column], right[row, column]))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }

    /// <summary>Every instance sharing one descriptor. Chunks are split out of a single group.</summary>
    internal sealed class TopChunkGroup
    {
        private readonly List<TopChunkInstance> instances = new List<TopChunkInstance>();

        public TopChunkGroup(TopChunkDescriptor descriptor)
        {
            Descriptor = descriptor;
        }

        public TopChunkDescriptor Descriptor { get; }
        public IReadOnlyList<TopChunkInstance> Instances => instances;

        public void Add(Vector3 position, Quaternion rotation)
        {
            instances.Add(new TopChunkInstance(position, rotation));
        }
    }

    /// <summary>
    /// What <see cref="TopChunkVisualBatcher"/> needs from <see cref="AtlasTilePresentationView"/>.
    ///
    /// The split mirrors the side-visual one: geometry-and-policy on the view, mesh-and-material-lifetime
    /// in the collaborator. The view decides *which* cells are chunk-eligible and *where* each instance
    /// goes (it owns the hex projection and the catalog resolution); the batcher owns the merged meshes
    /// and the chunk GameObjects from creation to destruction.
    ///
    /// <see cref="TopChunkMaxCells"/> is read through the host rather than moved because it is a
    /// [SerializeField] authored in four shipping scenes — and the board-chunk-audit editor tool reads it
    /// off the view by *name* (`serialized.FindProperty("topChunkMaxCells")`), so moving it would break a
    /// tool that no compiler check can catch.
    /// </summary>
    internal interface ITopChunkVisualHost
    {
        Transform MapTransform { get; }

        /// <summary>Authored cap on cells merged into one chunk. Clamped again against the 16-bit index limit.</summary>
        int TopChunkMaxCells { get; }

        /// <summary>
        /// Enrolls the chunk renderer in the fog-of-war lighting mask so batched tops are lit like
        /// per-cell tops. In LightingMask mode this swaps to Visibility Lit material variants.
        /// </summary>
        void RegisterVisibilityLightingRenderer(Renderer renderer);

        void DestroyVisualRoot(GameObject root);

        void DestroyGeneratedAsset(UnityEngine.Object generatedAsset);
    }

    /// <summary>
    /// Merges chunk-safe tile tops into shared meshes so a map renders a handful of renderers instead of
    /// one per cell. This is the map renderer's single biggest draw-call win, and the deterministic
    /// complexity gate (`MapRenderPerformanceRegressionTests`) asserts its output exactly — see
    /// docs/performance-testing.md.
    ///
    /// One render pass is: <see cref="BeginPlan"/>, one <see cref="Add"/> per eligible cell, then
    /// <see cref="BuildChunks"/>. The accumulated plan is dropped once built; the chunks and meshes it
    /// produced live until <see cref="DestroyAll"/>.
    /// </summary>
    internal sealed class TopChunkVisualBatcher
    {
        private readonly ITopChunkVisualHost host;
        private readonly List<GameObject> chunks = new List<GameObject>();
        private readonly List<Mesh> chunkMeshes = new List<Mesh>();
        private readonly List<TopChunkGroup> groups = new List<TopChunkGroup>();

        public TopChunkVisualBatcher(ITopChunkVisualHost host)
        {
            this.host = host;
        }

        /// <summary>Starts a fresh plan. Any instances accumulated but never built are discarded.</summary>
        public void BeginPlan()
        {
            groups.Clear();
        }

        public void Add(TopChunkDescriptor descriptor, Vector3 position, Quaternion rotation)
        {
            var group = groups.FirstOrDefault(candidate => candidate.Descriptor.Matches(descriptor));
            if (group == null)
            {
                group = new TopChunkGroup(descriptor);
                groups.Add(group);
            }

            group.Add(position, rotation);
        }

        public void BuildChunks()
        {
            foreach (var group in groups)
            {
                CreateChunks(group);
            }

            groups.Clear();
        }

        private void CreateChunks(TopChunkGroup group)
        {
            var cells = group.Instances;
            if (cells.Count == 0)
            {
                return;
            }

            var descriptor = group.Descriptor;
            var maxByIndex = Mathf.Max(1, 65535 / Mathf.Max(1, descriptor.Mesh.vertexCount));
            var maxCells = Mathf.Max(1, Mathf.Min(Mathf.Max(1, host.TopChunkMaxCells), maxByIndex));
            for (var start = 0; start < cells.Count; start += maxCells)
            {
                var count = Mathf.Min(maxCells, cells.Count - start);
                var mesh = BuildChunkMesh(descriptor, cells, start, count);
                var chunk = new GameObject($"Atlas_TopChunk_{chunks.Count:0000}_{descriptor.Mesh.name}");
                chunk.transform.SetParent(host.MapTransform, false);
                var meshFilter = chunk.AddComponent<MeshFilter>();
                meshFilter.sharedMesh = mesh;
                var renderer = chunk.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = descriptor.Material;
                host.RegisterVisibilityLightingRenderer(renderer);
                chunks.Add(chunk);
                chunkMeshes.Add(mesh);
            }
        }

        private static Mesh BuildChunkMesh(TopChunkDescriptor descriptor, IReadOnlyList<TopChunkInstance> instances, int start, int count)
        {
            var sourceMesh = descriptor.Mesh;
            using (var meshDataArray = Mesh.AcquireReadOnlyMeshData(sourceMesh))
            {
                var meshData = meshDataArray[0];
                var sourceVertices = new NativeArray<Vector3>(meshData.vertexCount, Allocator.Temp);
                var sourceUv = meshData.HasVertexAttribute(VertexAttribute.TexCoord0)
                    ? new NativeArray<Vector2>(meshData.vertexCount, Allocator.Temp)
                    : default;
                var sourceNormals = meshData.HasVertexAttribute(VertexAttribute.Normal)
                    ? new NativeArray<Vector3>(meshData.vertexCount, Allocator.Temp)
                    : default;
                try
                {
                    meshData.GetVertices(sourceVertices);
                    if (sourceUv.IsCreated)
                    {
                        meshData.GetUVs(0, sourceUv);
                    }

                    if (sourceNormals.IsCreated)
                    {
                        meshData.GetNormals(sourceNormals);
                    }

                    var sourceTriangles = ReadIndices(meshData);
                    var vertices = new List<Vector3>(sourceVertices.Length * count);
                    var uvs = sourceUv.IsCreated ? new List<Vector2>(sourceUv.Length * count) : null;
                    var normals = sourceNormals.IsCreated ? new List<Vector3>(sourceNormals.Length * count) : null;
                    var triangles = new List<int>(sourceTriangles.Count * count);

                    for (var i = 0; i < count; i++)
                    {
                        var instance = instances[start + i];
                        var transformMatrix = Matrix4x4.TRS(instance.Position, instance.Rotation, Vector3.one) * descriptor.MeshToPrefabMatrix;
                        var vertexOffset = vertices.Count;
                        for (var vertex = 0; vertex < sourceVertices.Length; vertex++)
                        {
                            vertices.Add(transformMatrix.MultiplyPoint3x4(sourceVertices[vertex]));
                            if (uvs != null)
                            {
                                uvs.Add(sourceUv[vertex]);
                            }

                            if (normals != null)
                            {
                                normals.Add(transformMatrix.MultiplyVector(sourceNormals[vertex]).normalized);
                            }
                        }

                        for (var triangle = 0; triangle < sourceTriangles.Count; triangle++)
                        {
                            triangles.Add(vertexOffset + sourceTriangles[triangle]);
                        }
                    }

                    var mesh = new Mesh { name = $"Atlas Top Chunk {descriptor.Mesh.name}" };
                    if (vertices.Count > 65535)
                    {
                        mesh.indexFormat = IndexFormat.UInt32;
                    }

                    mesh.SetVertices(vertices);
                    if (uvs != null)
                    {
                        mesh.SetUVs(0, uvs);
                    }

                    if (normals != null)
                    {
                        mesh.SetNormals(normals);
                    }

                    mesh.SetTriangles(triangles, 0);
                    mesh.RecalculateBounds();
                    if (normals == null)
                    {
                        mesh.RecalculateNormals();
                    }

                    return mesh;
                }
                finally
                {
                    if (sourceVertices.IsCreated)
                    {
                        sourceVertices.Dispose();
                    }

                    if (sourceUv.IsCreated)
                    {
                        sourceUv.Dispose();
                    }

                    if (sourceNormals.IsCreated)
                    {
                        sourceNormals.Dispose();
                    }
                }
            }
        }

        private static List<int> ReadIndices(Mesh.MeshData meshData)
        {
            var indices = new List<int>();
            for (var subMesh = 0; subMesh < meshData.subMeshCount; subMesh++)
            {
                var descriptor = meshData.GetSubMesh(subMesh);
                var subMeshIndices = new NativeArray<int>((int)descriptor.indexCount, Allocator.Temp);
                try
                {
                    meshData.GetIndices(subMeshIndices, subMesh);
                    for (var index = 0; index < subMeshIndices.Length; index++)
                    {
                        indices.Add(subMeshIndices[index]);
                    }
                }
                finally
                {
                    subMeshIndices.Dispose();
                }
            }

            return indices;
        }

        /// <summary>Destroys every chunk object and its generated mesh. Safe to call when nothing was built.</summary>
        public void DestroyAll()
        {
            foreach (var chunk in chunks)
            {
                if (chunk != null)
                {
                    host.DestroyVisualRoot(chunk);
                }
            }

            chunks.Clear();
            foreach (var mesh in chunkMeshes)
            {
                if (mesh != null)
                {
                    host.DestroyGeneratedAsset(mesh);
                }
            }

            chunkMeshes.Clear();
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;
using UnityEngine;
using UnityEngine.Rendering;

namespace SeoulPlayup.Map.Unity
{
    /// <summary>One exposed side face of a tile, in map-local space.</summary>
    internal readonly struct SideQuad
    {
        public SideQuad(Vector3 bottomLeft, Vector3 topLeft, Vector3 topRight, Vector3 bottomRight)
        {
            BottomLeft = bottomLeft;
            TopLeft = topLeft;
            TopRight = topRight;
            BottomRight = bottomRight;
        }

        public Vector3 BottomLeft { get; }
        public Vector3 TopLeft { get; }
        public Vector3 TopRight { get; }
        public Vector3 BottomRight { get; }
    }

    /// <summary>Color + texture a generated side material is built from.</summary>
    internal readonly struct SideMaterialAppearance
    {
        public SideMaterialAppearance(Color color, Texture texture)
        {
            Color = color;
            Texture = texture;
        }

        public Color Color { get; }
        public Texture Texture { get; }
    }

    /// <summary>
    /// What <see cref="TileSideVisualBuilder"/> needs from <see cref="AtlasTilePresentationView"/>.
    ///
    /// The split is geometry-and-policy on the view, mesh-and-material-lifetime in the builder. Hex
    /// projection (<see cref="ResolveSideQuad"/>) stays with the view because it is the map projector, and
    /// the side appearance is derived by sampling the tile's *top* prefab materials — logic that belongs
    /// with the tile catalog resolution, not with a mesh builder.
    /// </summary>
    internal interface ITileSideVisualHost
    {
        Transform MapTransform { get; }

        /// <summary>Corner geometry of one side face. Pure projection — see the view's ResolveSideQuad.</summary>
        SideQuad ResolveSideQuad(HexCellData cell, int direction, int heightLevel);

        /// <summary>Clamped height of the neighbouring cell, or 0 when the map has no cell there.</summary>
        int ResolveNeighborHeight(HexCoord neighborCoord);

        /// <summary>Color/texture for a generated side, derived from the entry's top prefab appearance.</summary>
        SideMaterialAppearance ResolveGeneratedSideAppearance(AtlasTileCatalog.Entry entry);

        Material CreateSideMaterial(string name, Color color, Texture texture);

        /// <summary>
        /// Enrolls the side chunk renderer in the fog-of-war lighting mask so exposed side faces are lit
        /// like the tile tops. In LightingMask mode this swaps to Visibility Lit material variants; in
        /// OverlayTint mode the original side materials are kept.
        /// </summary>
        void RegisterVisibilityLightingRenderer(Renderer renderer);

        void DestroyVisualRoot(GameObject root);

        /// <summary>Edit-mode-safe destroy for the generated mesh and materials this builder owns.</summary>
        void DestroyGeneratedAsset(UnityEngine.Object generatedAsset);
    }

    /// <summary>
    /// Builds and owns the procedurally generated tile side faces: one batched chunk mesh for the whole
    /// map, the per-catalog-entry material cache behind it, and the disposal of both.
    ///
    /// Extracted from <see cref="AtlasTilePresentationView"/>. The five fields this owns (chunk object,
    /// chunk mesh, and three material-cache collections) were previously reachable from every line of a
    /// 3,300-line file while having a strict create/destroy pairing — leaking a mesh or a generated
    /// material is invisible until a profiler run, so a single owner with one teardown path is worth more
    /// here than the line count suggests.
    /// </summary>
    internal sealed class TileSideVisualBuilder
    {
        private static readonly IReadOnlyList<HexCoord> NeighborDirections = HexCoord.Directions;

        private readonly ITileSideVisualHost host;

        // Materials this builder created and must destroy. Entries whose catalog row authors an explicit
        // SideMaterial are NOT in here — those are shared assets and destroying them would corrupt the project.
        private readonly Dictionary<AtlasTileCatalog.Entry, Material> generatedMaterialsByEntry =
            new Dictionary<AtlasTileCatalog.Entry, Material>();

        private readonly List<Material> ownedGeneratedMaterials = new List<Material>();

        private GameObject chunk;
        private Mesh chunkMesh;

        public TileSideVisualBuilder(ITileSideVisualHost host)
        {
            this.host = host ?? throw new System.ArgumentNullException(nameof(host));
        }

        /// <summary>Number of side quads in the current chunk. Zero when sides are off or nothing is exposed.</summary>
        public int SegmentCount { get; private set; }

        /// <summary>
        /// True while side building has not run at all — sides are disabled or no map is loaded. It does
        /// NOT mean "no sides exist": a flat height-0 map legitimately produces zero quads and is still not
        /// deferred, which is what <c>HeightZeroCellRendersTopOnlyWithoutSideVisuals</c> pins down.
        /// Surfaced by the view as SideVisualsDeferred.
        /// </summary>
        public bool Deferred { get; private set; } = true;

        /// <summary>
        /// Rebuilds the whole side chunk from the currently rendered tiles. Callers pass the (cell,
        /// resolution) pairs rather than a map, so the builder never needs the view's tile dictionary.
        /// </summary>
        public void Rebuild(IEnumerable<(HexCellData Cell, AtlasTileCatalogResolution Resolution)> renderedCells)
        {
            SegmentCount = 0;
            // Null means the caller decided sides are off / there is no map — the only deferred case.
            Deferred = renderedCells == null;
            if (renderedCells == null)
            {
                return;
            }

            var mesh = new GeneratedSideChunkMesh();
            foreach (var (cell, resolution) in renderedCells)
            {
                AddSegments(cell, resolution, mesh);
            }

            SegmentCount = mesh.SegmentCount;
            if (mesh.SegmentCount <= 0)
            {
                // Nothing exposed (flat map). Not deferred — the build ran and produced nothing.
                return;
            }

            chunk = CreateChunkVisual(mesh);
        }

        private void AddSegments(HexCellData cell, AtlasTileCatalogResolution resolution, GeneratedSideChunkMesh mesh)
        {
            if (ShouldHideSides(resolution))
            {
                return;
            }

            var height = HexCellData.ClampHeight(cell.HeightLevel);
            var sideMaterial = ResolveMaterial(resolution.Entry);
            for (var direction = 0; direction < NeighborDirections.Count; direction++)
            {
                var neighborHeight = host.ResolveNeighborHeight(cell.Coord + NeighborDirections[direction]);
                // Only the part of this tile standing above its neighbour is exposed; one quad per height step.
                var exposedSegments = Mathf.Max(0, height - neighborHeight);
                for (var segment = 0; segment < exposedSegments; segment++)
                {
                    mesh.AddSegment(host.ResolveSideQuad(cell, direction, neighborHeight + segment), sideMaterial);
                }
            }
        }

        private GameObject CreateChunkVisual(GeneratedSideChunkMesh mesh)
        {
            chunkMesh = mesh.BuildMesh("Atlas Generated Side Chunk Mesh");
            var root = new GameObject("Atlas_SideChunk_Generated");
            root.transform.SetParent(host.MapTransform, false);
            var meshFilter = root.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = chunkMesh;
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = mesh.Materials.ToArray();
            host.RegisterVisibilityLightingRenderer(renderer);
            return root;
        }

        private static bool ShouldHideSides(AtlasTileCatalogResolution resolution)
        {
            return resolution.Entry != null && resolution.Entry.SideVisualMode == AtlasSideVisualMode.Hidden;
        }

        private Material ResolveMaterial(AtlasTileCatalog.Entry entry)
        {
            // Authored side material wins and is never owned by us — it is a project asset.
            if (entry != null && entry.SideMaterial != null)
            {
                return entry.SideMaterial;
            }

            if (entry != null && generatedMaterialsByEntry.TryGetValue(entry, out var cached) && cached != null)
            {
                return cached;
            }

            var appearance = host.ResolveGeneratedSideAppearance(entry);
            var material = host.CreateSideMaterial(
                $"Atlas Generated Side {entry?.AtlasVisualId ?? "Default"}",
                appearance.Color,
                appearance.Texture);
            ownedGeneratedMaterials.Add(material);
            if (entry != null)
            {
                generatedMaterialsByEntry[entry] = material;
            }

            return material;
        }

        /// <summary>
        /// Tears down everything this builder owns — chunk object, chunk mesh, and every material it
        /// created — and resets the counters. The view called the chunk and the materials down separately
        /// before, but both of its call sites (OnDestroy and ClearAll) always did the two together, so the
        /// pairing is enforced here instead of being a convention two callers have to remember.
        /// </summary>
        public void DestroyAll()
        {
            if (chunk != null)
            {
                host.DestroyVisualRoot(chunk);
                chunk = null;
            }

            if (chunkMesh != null)
            {
                host.DestroyGeneratedAsset(chunkMesh);
                chunkMesh = null;
            }

            foreach (var material in ownedGeneratedMaterials)
            {
                if (material != null)
                {
                    host.DestroyGeneratedAsset(material);
                }
            }

            ownedGeneratedMaterials.Clear();
            generatedMaterialsByEntry.Clear();
            SegmentCount = 0;
            Deferred = true;
        }

        /// <summary>
        /// Accumulates side quads into one multi-material mesh: vertices and UVs in a single stream, one
        /// submesh per distinct material. Double-sided by design — each quad emits both winding orders, so a
        /// side face stays visible from inside a tile stack as the camera orbits.
        /// </summary>
        private sealed class GeneratedSideChunkMesh
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector2> uvs = new List<Vector2>();
            private readonly List<Material> materials = new List<Material>();
            private readonly List<List<int>> trianglesByMaterial = new List<List<int>>();

            public int SegmentCount { get; private set; }
            public IReadOnlyList<Material> Materials => materials;

            public void AddSegment(SideQuad quad, Material material)
            {
                var index = vertices.Count;
                vertices.Add(quad.BottomLeft);
                vertices.Add(quad.TopLeft);
                vertices.Add(quad.TopRight);
                vertices.Add(quad.BottomRight);
                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(0f, 1f));
                uvs.Add(new Vector2(1f, 1f));
                uvs.Add(new Vector2(1f, 0f));

                var triangles = ResolveTriangles(material);
                triangles.Add(index);
                triangles.Add(index + 1);
                triangles.Add(index + 2);
                triangles.Add(index);
                triangles.Add(index + 2);
                triangles.Add(index + 3);
                triangles.Add(index);
                triangles.Add(index + 2);
                triangles.Add(index + 1);
                triangles.Add(index);
                triangles.Add(index + 3);
                triangles.Add(index + 2);
                SegmentCount++;
            }

            private List<int> ResolveTriangles(Material material)
            {
                var materialIndex = materials.IndexOf(material);
                if (materialIndex >= 0)
                {
                    return trianglesByMaterial[materialIndex];
                }

                materials.Add(material);
                var triangles = new List<int>();
                trianglesByMaterial.Add(triangles);
                return triangles;
            }

            public Mesh BuildMesh(string name)
            {
                var mesh = new Mesh { name = name };
                if (vertices.Count > 65535)
                {
                    mesh.indexFormat = IndexFormat.UInt32;
                }

                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uvs);
                mesh.subMeshCount = Mathf.Max(1, trianglesByMaterial.Count);
                for (var subMesh = 0; subMesh < trianglesByMaterial.Count; subMesh++)
                {
                    mesh.SetTriangles(trianglesByMaterial[subMesh], subMesh);
                }

                mesh.RecalculateBounds();
                mesh.RecalculateNormals();
                return mesh;
            }
        }
    }
}

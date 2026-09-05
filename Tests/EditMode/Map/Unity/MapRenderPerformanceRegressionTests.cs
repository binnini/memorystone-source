#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Map.Unity.Tests.EditMode
{
    /// <summary>
    /// Deterministic render-complexity regression gate for the chunked Atlas map renderer.
    ///
    /// The committed baseline (<see cref="BaselineRelativePath"/>) is the source of truth for the
    /// exact renderer/mesh/vertex/triangle counts produced by <see cref="AtlasTilePresentationView"/>
    /// at the standard cell/prop matrix, both with chunking on and off. The always-on
    /// <see cref="RenderComplexityMatchesCommittedBaseline"/> test fails closed if the current code
    /// produces different counts at the representative case; the full 18-case matrix lives in the
    /// explicit <see cref="RenderComplexityFullMatrixMatchesCommittedBaseline"/> because rendering
    /// up to 10k cells per case dominates the whole suite's wall-clock. When an intentional change
    /// moves the numbers, regenerate the baseline via the explicit <see cref="RegenerateBaseline"/>
    /// test and commit the updated JSON.
    /// </summary>
    public sealed class MapRenderPerformanceRegressionTests
    {
        private const string BaselineRelativePath = "Tests/EditMode/Map/Unity/map-render-performance-baseline.json";

        private static readonly int[] CellCounts = { 1000, 5000, 10000 };
        private static readonly int[] BuildingCounts = { 25, 100, 250 };

        private const int RepresentativeCellCount = 1000;
        private const int RepresentativeBuildingCount = 25;

        [Test]
        public void RenderComplexityMatchesCommittedBaseline([Values(true, false)] bool useChunks)
        {
            AssertCaseMatchesBaseline(RepresentativeCellCount, RepresentativeBuildingCount, useChunks);
        }

        [Test, Explicit("Full 18-case render matrix (up to 10k cells per case). The always-on gate covers the representative case; run this before shipping renderer changes.")]
        public void RenderComplexityFullMatrixMatchesCommittedBaseline(
            [ValueSource(nameof(CellCounts))] int cellCount,
            [ValueSource(nameof(BuildingCounts))] int buildingCount,
            [Values(true, false)] bool useChunks)
        {
            // [Explicit] alone does not protect this project's test workflow: namespace-filtered
            // runs (the only way tests-run finds tests here) execute Explicit tests anyway, so an
            // env-var opt-in is the real gate.
            RequireOptIn("MAP_RENDER_PERF_FULL", "full render-matrix gate");
            AssertCaseMatchesBaseline(cellCount, buildingCount, useChunks);
        }

        private static void RequireOptIn(string environmentVariable, string what)
        {
            if (Environment.GetEnvironmentVariable(environmentVariable) != "1")
            {
                Assert.Ignore($"Set {environmentVariable}=1 to run the {what}. Skipped so routine namespace-filtered runs stay fast and side-effect free.");
            }
        }

        private static void AssertCaseMatchesBaseline(int cellCount, int buildingCount, bool useChunks)
        {
            var document = LoadBaselineOrFail();
            var key = BaselineEntry.KeyFor(cellCount, buildingCount, useChunks);
            var expected = document.entries.SingleOrDefault(entry => entry.Key == key);
            Assert.That(expected, Is.Not.Null,
                $"No baseline entry for {key}. Run the explicit {nameof(RegenerateBaseline)} test and commit the updated baseline JSON.");

            var measured = MeasureMatrixCase(cellCount, buildingCount, useChunks);

            Assert.That(measured.renderers, Is.EqualTo(expected.renderers), $"{key}: renderer count regressed.");
            Assert.That(measured.meshFilters, Is.EqualTo(expected.meshFilters), $"{key}: mesh filter count regressed.");
            Assert.That(measured.uniqueMeshes, Is.EqualTo(expected.uniqueMeshes), $"{key}: unique mesh count regressed.");
            Assert.That(measured.vertices, Is.EqualTo(expected.vertices), $"{key}: vertex count regressed.");
            Assert.That(measured.triangles, Is.EqualTo(expected.triangles), $"{key}: triangle count regressed.");
            Assert.That(measured.topVisuals, Is.EqualTo(expected.topVisuals), $"{key}: top visual count regressed.");
            Assert.That(measured.mapObjectVisuals, Is.EqualTo(expected.mapObjectVisuals), $"{key}: map object visual count regressed.");
            Assert.That(measured.clickColliders, Is.EqualTo(expected.clickColliders),
                $"{key}: generated click-collider count changed. P6 removed tile click colliders entirely; Render() must not create Atlas_ClickCollider_ children.");
        }

        [Test]
        public void ChunkingReducesRenderersWithoutChangingGeometry()
        {
            const int cellCount = 5000;
            const int buildingCount = 100;

            var chunked = MeasureMatrixCase(cellCount, buildingCount, useChunks: true);
            var unchunked = MeasureMatrixCase(cellCount, buildingCount, useChunks: false);

            Assert.That(unchunked.renderers, Is.GreaterThan(cellCount),
                "Unchunked rendering should produce at least one renderer per cell.");
            Assert.That(chunked.renderers * 10, Is.LessThan(unchunked.renderers),
                "Top-chunk batching must cut renderer count by more than 10x.");
            Assert.That(chunked.vertices, Is.EqualTo(unchunked.vertices),
                "Chunk batching must preserve total vertex count.");
            Assert.That(chunked.triangles, Is.EqualTo(unchunked.triangles),
                "Chunk batching must preserve total triangle count.");
            Assert.That(chunked.topVisuals, Is.EqualTo(unchunked.topVisuals),
                "Logical top-visual count is independent of chunk batching.");
        }

        [Test, Explicit("Regenerates the committed render-complexity baseline from current code. Run intentionally, then commit the updated JSON.")]
        public void RegenerateBaseline()
        {
            // Without this guard every namespace-filtered run silently rewrote the committed
            // baseline JSON (timestamp churn, and a self-fulfilling gate within the same run).
            RequireOptIn("MAP_RENDER_BASELINE_REGEN", "baseline regeneration (writes the committed JSON)");

            var document = new BaselineDocument
            {
                generatedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                unityVersion = Application.unityVersion,
                entries = new List<BaselineEntry>(),
            };

            foreach (var cellCount in CellCounts)
            {
                foreach (var buildingCount in BuildingCounts)
                {
                    foreach (var useChunks in new[] { true, false })
                    {
                        document.entries.Add(MeasureMatrixCase(cellCount, buildingCount, useChunks));
                    }
                }
            }

            var path = BaselineAbsolutePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllText(path, JsonUtility.ToJson(document, prettyPrint: true));
            UnityEditor.AssetDatabase.Refresh();
            UnityEngine.Debug.Log($"Wrote render-performance baseline ({document.entries.Count} entries) to {path}");
        }

        private static BaselineEntry MeasureMatrixCase(int cellCount, int buildingCount, bool useChunks)
        {
            using (var fixture = GeneratedMapFixture.Create(cellCount, buildingCount))
            {
                Assert.That(fixture.Source.TryToHexMapData(out var map, out var error), Is.True, error);

                var root = new GameObject($"PerfGate_{cellCount}_{buildingCount}_{(useChunks ? "chunked" : "unchunked")}");
                var view = root.AddComponent<AtlasTilePresentationView>();
                view.ConfigureForTests(fixture.AtlasCatalog, tileRadius: 0.25f, tileSpacing: 0.25f, heightStep: 0.1f, mapObjectCatalogSet: fixture.ObjectCatalog, useTopChunkMeshes: useChunks);
                try
                {
                    view.Render(map);
                    Assert.That(view.MissingVisualDiagnostics, Is.Empty,
                        $"Unexpected missing visuals for {cellCount}/{buildingCount}: {string.Join(", ", view.MissingVisualDiagnostics)}");
                    return BaselineEntry.Capture(root, view, cellCount, buildingCount, useChunks);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        private static BaselineDocument LoadBaselineOrFail()
        {
            var path = BaselineAbsolutePath();
            if (!File.Exists(path))
            {
                Assert.Fail($"Missing baseline JSON at {path}. Run the explicit {nameof(RegenerateBaseline)} test to create it.");
            }

            var document = JsonUtility.FromJson<BaselineDocument>(File.ReadAllText(path));
            Assert.That(document?.entries, Is.Not.Null.And.Not.Empty, $"Baseline JSON at {path} is empty or malformed.");
            return document;
        }

        private static string BaselineAbsolutePath()
        {
            return Path.Combine(Application.dataPath, BaselineRelativePath);
        }

        [Serializable]
        private sealed class BaselineDocument
        {
            public string generatedAtUtc;
            public string unityVersion;
            public List<BaselineEntry> entries;
        }

        [Serializable]
        private sealed class BaselineEntry
        {
            public int cells;
            public int props;
            public bool chunked;
            public int renderers;
            public int meshFilters;
            public int uniqueMeshes;
            public long vertices;
            public long triangles;
            public int topVisuals;
            public int mapObjectVisuals;
            public int clickColliders;

            public string Key => KeyFor(cells, props, chunked);

            public static string KeyFor(int cells, int props, bool chunked) =>
                $"{cells}c_{props}p_{(chunked ? "chunked" : "unchunked")}";

            public static BaselineEntry Capture(GameObject root, AtlasTilePresentationView view, int cells, int props, bool chunked)
            {
                var meshFilters = root.GetComponentsInChildren<MeshFilter>(includeInactive: true);
                var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
                var clickColliders = 0;
                foreach (var transform in root.GetComponentsInChildren<Transform>(includeInactive: true))
                {
                    if (transform.name.StartsWith("Atlas_ClickCollider_", StringComparison.Ordinal))
                    {
                        clickColliders++;
                    }
                }
                var uniqueMeshes = new HashSet<Mesh>();
                long vertices = 0;
                long triangles = 0;
                foreach (var meshFilter in meshFilters)
                {
                    var mesh = meshFilter.sharedMesh;
                    if (mesh == null)
                    {
                        continue;
                    }

                    uniqueMeshes.Add(mesh);
                    vertices += mesh.vertexCount;
                    for (var subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                    {
                        triangles += mesh.GetIndexCount(subMesh) / 3;
                    }
                }

                return new BaselineEntry
                {
                    cells = cells,
                    props = props,
                    chunked = chunked,
                    renderers = renderers.Length,
                    meshFilters = meshFilters.Length,
                    uniqueMeshes = uniqueMeshes.Count,
                    vertices = vertices,
                    triangles = triangles,
                    topVisuals = view.TopVisualCount,
                    mapObjectVisuals = view.MapObjectVisualCount,
                    clickColliders = clickColliders,
                };
            }
        }

        private sealed class GeneratedMapFixture : IDisposable
        {
            // Must start with "tile-" so AtlasTilePresentationView.IsChunkEligibleAtlasVisualId accepts it.
            private const string AtlasVisualId = "tile-perf-gate";
            private const string BuildingObjectRef = "perf-gate-building";

            private GeneratedMapFixture(
                HexSparseMapAuthoringSource source,
                AtlasTileCatalog atlasCatalog,
                MapObjectCatalogSet objectCatalog,
                GameObject tilePrefab,
                GameObject buildingPrefab)
            {
                Source = source;
                AtlasCatalog = atlasCatalog;
                ObjectCatalog = objectCatalog;
                this.tilePrefab = tilePrefab;
                this.buildingPrefab = buildingPrefab;
            }

            private readonly GameObject tilePrefab;
            private readonly GameObject buildingPrefab;

            public HexSparseMapAuthoringSource Source { get; }
            public AtlasTileCatalog AtlasCatalog { get; }
            public MapObjectCatalogSet ObjectCatalog { get; }

            public static GeneratedMapFixture Create(int cellCount, int buildingCount)
            {
                var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
                var cells = GenerateCells(cellCount).ToArray();
                var objectRefs = GenerateBuildingRefs(cells, buildingCount).ToArray();
                source.ConfigureForTests(cells, HexMapPurpose.PlayableMap, objectRefs: objectRefs);

                var tilePrefab = CreateChunkSafeTopPrefab();
                var buildingPrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                buildingPrefab.name = "PerfGateBuildingCube";

                var atlasCatalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
                atlasCatalog.ConfigureForTests(new[] { new AtlasTileCatalog.Entry(AtlasVisualId, tilePrefab) });
                var typedCatalog = ScriptableObject.CreateInstance<MapObjectTypedCatalog>();
                typedCatalog.ConfigureForTests(HexMapObjectType.Building, new[] { new RuntimeMapObjectPrefabCatalog.Entry(BuildingObjectRef, buildingPrefab) });
                var objectCatalog = ScriptableObject.CreateInstance<MapObjectCatalogSet>();
                objectCatalog.ConfigureForTests(new[] { typedCatalog });

                return new GeneratedMapFixture(source, atlasCatalog, objectCatalog, tilePrefab, buildingPrefab);
            }

            public void Dispose()
            {
                if (tilePrefab != null)
                {
                    var meshFilter = tilePrefab.GetComponent<MeshFilter>();
                    if (meshFilter != null && meshFilter.sharedMesh != null)
                    {
                        UnityEngine.Object.DestroyImmediate(meshFilter.sharedMesh);
                    }

                    var renderer = tilePrefab.GetComponent<MeshRenderer>();
                    if (renderer != null && renderer.sharedMaterial != null)
                    {
                        UnityEngine.Object.DestroyImmediate(renderer.sharedMaterial);
                    }
                }

                UnityEngine.Object.DestroyImmediate(Source);
                UnityEngine.Object.DestroyImmediate(AtlasCatalog);
                foreach (var typedCatalog in ObjectCatalog.Catalogs)
                {
                    UnityEngine.Object.DestroyImmediate(typedCatalog);
                }

                UnityEngine.Object.DestroyImmediate(ObjectCatalog);
                UnityEngine.Object.DestroyImmediate(tilePrefab);
                UnityEngine.Object.DestroyImmediate(buildingPrefab);
            }

            // Builds a top prefab that satisfies AtlasTilePresentationView's chunk-eligibility:
            // exactly one MeshFilter + MeshRenderer, single material, readable mesh, no collider
            // or other components. A code-built mesh is readable by default.
            private static GameObject CreateChunkSafeTopPrefab()
            {
                var prefab = new GameObject("PerfGateTileTop", typeof(MeshFilter), typeof(MeshRenderer));
                prefab.GetComponent<MeshFilter>().sharedMesh = CreateReadableQuadMesh();
                prefab.GetComponent<MeshRenderer>().sharedMaterial = CreateUnlitMaterial();
                return prefab;
            }

            private static Mesh CreateReadableQuadMesh()
            {
                var mesh = new Mesh { name = "PerfGateQuadMesh" };
                mesh.SetVertices(new[]
                {
                    new Vector3(-0.5f, 0f, -0.5f),
                    new Vector3(0.5f, 0f, -0.5f),
                    new Vector3(0.5f, 0f, 0.5f),
                    new Vector3(-0.5f, 0f, 0.5f),
                });
                mesh.SetUVs(0, new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up });
                mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }

            private static Material CreateUnlitMaterial()
            {
                foreach (var shaderName in new[] { "Universal Render Pipeline/Unlit", "Universal Render Pipeline/Lit", "Unlit/Color", "Sprites/Default" })
                {
                    var shader = Shader.Find(shaderName);
                    if (shader != null)
                    {
                        return new Material(shader);
                    }
                }

                throw new InvalidOperationException("No usable shader found for chunk-safe top prefab material.");
            }

            private static IEnumerable<HexSparseMapAuthoringCell> GenerateCells(int cellCount)
            {
                var width = Mathf.CeilToInt(Mathf.Sqrt(cellCount));
                for (var index = 0; index < cellCount; index++)
                {
                    var coord = new HexCoord(index % width, index / width);
                    var eventId = index == 0 ? "mvp-start" : string.Empty;
                    yield return new HexSparseMapAuthoringCell(coord, "perf-tile", "urban", AtlasVisualId, eventId, string.Empty, heightLevel: index % 3, rotationSteps: index % 6);
                }
            }

            private static IEnumerable<HexMapObjectRef> GenerateBuildingRefs(IReadOnlyList<HexSparseMapAuthoringCell> cells, int buildingCount)
            {
                if (buildingCount == 0)
                {
                    yield break;
                }

                var step = Math.Max(1, cells.Count / buildingCount);
                var used = new HashSet<HexCoord>();
                for (var i = 0; i < buildingCount; i++)
                {
                    var cell = cells[Math.Min(cells.Count - 1, i * step)];
                    if (!used.Add(cell.Coord))
                    {
                        cell = cells.First(candidate => used.Add(candidate.Coord));
                    }

                    yield return new HexMapObjectRef(
                        $"perf-gate-building-{i:0000}",
                        HexMapObjectType.Building,
                        BuildingObjectRef,
                        cell.Q,
                        cell.R,
                        role: "building_prop",
                        enabledForPurpose: HexMapPurpose.PlayableMap,
                        blocksMovement: false,
                        blocksVision: true,
                        interactable: false,
                        rotationSteps: i % 6);
                }
            }
        }
    }
}
#endif


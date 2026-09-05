using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.MapDesign.Editor;
using Unity.PerformanceTesting;
using UnityEngine;

namespace SeoulPlayup.Map.Unity.Tests.EditMode
{
    public sealed class HexSparseMapAuthoringSourcePerformanceTests
    {
        private const string AtlasVisualId = "perf-atlas-building-test";
        private const string BuildingObjectRef = "perf-building-prop";
        private const string ObjectiveLandmarkId = "perf-objective-landmark";

        private static readonly int[] CellCounts = { 1000, 5000, 10000 };
        private static readonly int[] BuildingCounts = { 25, 100, 250 };

        [Test]
        public void GeneratedPerformanceFixtureConvertsAndReportsExpectedBuildingRefs()
        {
            using (var fixture = GeneratedMapFixture.Create(1000, 25))
            {
                Assert.That(fixture.Source.TryToHexMapData(out var map, out var error), Is.True, error);
                Assert.That(map.Count, Is.EqualTo(1000));
                Assert.That(map.ObjectRefs.Count, Is.EqualTo(25));
                Assert.That(map.ObjectRefs.All(objectRef => objectRef.ObjectType == HexMapObjectType.Building.ToString()), Is.True);

                var report = ValidateSource(fixture.Source);
                Assert.That(report.HasErrors, Is.False, string.Join("\n", report.Errors.Select(item => item.Message)));
            }
        }

        [Test, Explicit("Large generated map performance baseline; run intentionally when collecting map-source performance data."), Performance]
        public void TryToHexMapData_PerformanceMatrix([ValueSource(nameof(CellCounts))] int cellCount, [ValueSource(nameof(BuildingCounts))] int buildingCount)
        {
            using (var fixture = GeneratedMapFixture.Create(cellCount, buildingCount))
            {
                Assert.That(fixture.Source.TryToHexMapData(out var warmupMap, out var warmupError), Is.True, warmupError);
                Assert.That(warmupMap.ObjectRefs.Count, Is.EqualTo(buildingCount));

                Measure.Custom(CountGroup("cell_count"), cellCount);
                Measure.Custom(CountGroup("prop_count"), buildingCount);

                Measure.Method(() =>
                    {
                        if (!fixture.Source.TryToHexMapData(out _, out var error))
                        {
                            throw new InvalidOperationException(error);
                        }
                    })
                    .SampleGroup(new SampleGroup($"TryToHexMapData_{cellCount}_cells_{buildingCount}_props", SampleUnit.Millisecond))
                    .WarmupCount(1)
                    .MeasurementCount(5)
                    .Run();
            }
        }

        [Test, Explicit("Large generated map validation baseline; run intentionally when collecting map-source performance data."), Performance]
        public void Validation_PerformanceMatrix([ValueSource(nameof(CellCounts))] int cellCount, [ValueSource(nameof(BuildingCounts))] int buildingCount)
        {
            using (var fixture = GeneratedMapFixture.Create(cellCount, buildingCount))
            {
                var warmupReport = ValidateSource(fixture.Source);
                Assert.That(warmupReport.HasErrors, Is.False, string.Join("\n", warmupReport.Errors.Select(item => item.Message)));

                Measure.Custom(CountGroup("cell_count"), cellCount);
                Measure.Custom(CountGroup("prop_count"), buildingCount);

                Measure.Method(() =>
                    {
                        var report = ValidateSource(fixture.Source);
                        if (report.HasErrors)
                        {
                            throw new InvalidOperationException(string.Join("\n", report.Errors.Select(item => item.Message)));
                        }
                    })
                    .SampleGroup(new SampleGroup($"Validate_{cellCount}_cells_{buildingCount}_props", SampleUnit.Millisecond))
                    .WarmupCount(1)
                    .MeasurementCount(3)
                    .Run();
            }
        }

        [Test, Explicit("Instantiates thousands of map/object visuals; run intentionally when collecting render and polygon baselines."), Performance]
        public void RenderHexMapData_RecordsVisualAndMeshComplexity(
            [ValueSource(nameof(CellCounts))] int cellCount,
            [ValueSource(nameof(BuildingCounts))] int buildingCount,
            [Values(true, false)] bool useChunks)
        {
            using (var fixture = GeneratedMapFixture.Create(cellCount, buildingCount))
            {
                Assert.That(fixture.Source.TryToHexMapData(out var map, out var error), Is.True, error);

                var chunkSuffix = useChunks ? "chunked" : "unchunked";
                var root = new GameObject($"PerfRender_{cellCount}_{buildingCount}_{chunkSuffix}");
                var view = root.AddComponent<AtlasTilePresentationView>();
                view.ConfigureForTests(fixture.AtlasCatalog, tileRadius: 0.25f, tileSpacing: 0.25f, heightStep: 0.1f, mapObjectCatalogSet: fixture.ObjectCatalog, useTopChunkMeshes: useChunks);
                try
                {
                    var stopwatch = Stopwatch.StartNew();
                    view.Render(map);
                    stopwatch.Stop();

                    var metrics = MeshComplexityMetrics.From(root, view);
                    Assert.That(view.TopVisualCount, Is.EqualTo(cellCount));
                    Assert.That(view.MapObjectVisualCount, Is.EqualTo(buildingCount));
                    Assert.That(view.MissingVisualDiagnostics, Is.Empty);
                    Assert.That(metrics.TotalTriangles, Is.GreaterThan(0));

                    var suffix = $"{cellCount}_cells_{buildingCount}_props_{chunkSuffix}";
                    Measure.Custom(new SampleGroup($"RenderHexMapData_{suffix}", SampleUnit.Millisecond), stopwatch.Elapsed.TotalMilliseconds);
                    Measure.Custom(CountGroup("cell_count"), cellCount);
                    Measure.Custom(CountGroup("prop_count"), buildingCount);
                    Measure.Custom(CountGroup("use_chunks"), useChunks ? 1 : 0);
                    metrics.RecordSamples(suffix);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        private static SampleGroup CountGroup(string name)
        {
            return new SampleGroup(name, SampleUnit.Undefined, false);
        }

        private static HexMapValidationReport ValidateSource(HexSparseMapAuthoringSource source)
        {
            return HexMapValidationUtility.Validate(source, new[] { "urban" });
        }

        private sealed class GeneratedMapFixture : IDisposable
        {
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
                TilePrefab = tilePrefab;
                BuildingPrefab = buildingPrefab;
            }

            public HexSparseMapAuthoringSource Source { get; }
            public AtlasTileCatalog AtlasCatalog { get; }
            public MapObjectCatalogSet ObjectCatalog { get; }
            private GameObject TilePrefab { get; }
            private GameObject BuildingPrefab { get; }

            public static GeneratedMapFixture Create(int cellCount, int buildingCount)
            {
                if (cellCount <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(cellCount));
                }

                if (buildingCount < 0 || buildingCount > cellCount)
                {
                    throw new ArgumentOutOfRangeException(nameof(buildingCount));
                }

                var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
                var cells = GenerateCells(cellCount).ToArray();
                var objectiveCoord = cells[cells.Length - 1].Coord;
                var objectRefs = new[]
                    {
                        new HexMapObjectRef("perf-objective", HexMapObjectType.ObjectiveMarker, ObjectiveLandmarkId, objectiveCoord.Q, objectiveCoord.R, displayName: "Performance Objective")
                    }
                    .Concat(GenerateBuildingRefs(cells, buildingCount))
                    .ToArray();
                source.ConfigureForTests(
                    cells,
                    HexMapPurpose.PlayableMap,
                    objectRefs: objectRefs);

                var atlasCatalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
                var typedCatalog = ScriptableObject.CreateInstance<MapObjectTypedCatalog>();
                var objectCatalog = ScriptableObject.CreateInstance<MapObjectCatalogSet>();
                var tilePrefab = CreateTilePrefab();
                var buildingPrefab = CreateBuildingPrefab();
                atlasCatalog.ConfigureForTests(new[] { new AtlasTileCatalog.Entry(AtlasVisualId, tilePrefab) });
                typedCatalog.ConfigureForTests(HexMapObjectType.Building, new[] { new RuntimeMapObjectPrefabCatalog.Entry(BuildingObjectRef, buildingPrefab) });
                objectCatalog.ConfigureForTests(new[] { typedCatalog });

                return new GeneratedMapFixture(source, atlasCatalog, objectCatalog, tilePrefab, buildingPrefab);
            }

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(Source);
                UnityEngine.Object.DestroyImmediate(AtlasCatalog);
                foreach (var typedCatalog in ObjectCatalog.Catalogs)
                {
                    UnityEngine.Object.DestroyImmediate(typedCatalog);
                }

                UnityEngine.Object.DestroyImmediate(ObjectCatalog);
                UnityEngine.Object.DestroyImmediate(TilePrefab);
                UnityEngine.Object.DestroyImmediate(BuildingPrefab);
            }

            private static IEnumerable<HexSparseMapAuthoringCell> GenerateCells(int cellCount)
            {
                var width = Mathf.CeilToInt(Mathf.Sqrt(cellCount));
                for (var index = 0; index < cellCount; index++)
                {
                    var q = index % width;
                    var r = index / width;
                    var coord = new HexCoord(q, r);
                    var eventId = index == 0 ? "mvp-start" : string.Empty;
                    var landmarkId = index == cellCount - 1 ? ObjectiveLandmarkId : string.Empty;
                    yield return new HexSparseMapAuthoringCell(coord, "perf-tile", "urban", AtlasVisualId, eventId, landmarkId, heightLevel: index % 3, rotationSteps: index % 6);
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
                        $"perf-building-{i:0000}",
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

            private static GameObject CreateTilePrefab()
            {
                var prefab = GameObject.CreatePrimitive(PrimitiveType.Quad);
                prefab.name = "PerfTileQuadPrefab";
                return prefab;
            }

            private static GameObject CreateBuildingPrefab()
            {
                var prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                prefab.name = "PerfBuildingCubePrefab";
                return prefab;
            }
        }

        private readonly struct MeshComplexityMetrics
        {
            private MeshComplexityMetrics(
                int rendererCount,
                int meshFilterCount,
                int uniqueMeshCount,
                int materialSlotCount,
                long totalVertices,
                long totalTriangles,
                int topVisualCount,
                int mapObjectVisualCount,
                int activeMapObjectVisualCount)
            {
                RendererCount = rendererCount;
                MeshFilterCount = meshFilterCount;
                UniqueMeshCount = uniqueMeshCount;
                MaterialSlotCount = materialSlotCount;
                TotalVertices = totalVertices;
                TotalTriangles = totalTriangles;
                TopVisualCount = topVisualCount;
                MapObjectVisualCount = mapObjectVisualCount;
                ActiveMapObjectVisualCount = activeMapObjectVisualCount;
            }

            public int RendererCount { get; }
            public int MeshFilterCount { get; }
            public int UniqueMeshCount { get; }
            public int MaterialSlotCount { get; }
            public long TotalVertices { get; }
            public long TotalTriangles { get; }
            public int TopVisualCount { get; }
            public int MapObjectVisualCount { get; }
            public int ActiveMapObjectVisualCount { get; }

            public static MeshComplexityMetrics From(GameObject root, AtlasTilePresentationView view)
            {
                var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
                var meshFilters = root.GetComponentsInChildren<MeshFilter>(includeInactive: true);
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

                    vertices += mesh.vertexCount;
                    for (var subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                    {
                        triangles += mesh.GetIndexCount(subMesh) / 3;
                    }

                    uniqueMeshes.Add(mesh);
                }

                var materialSlots = renderers.Sum(renderer => renderer.sharedMaterials?.Length ?? 0);
                return new MeshComplexityMetrics(
                    renderers.Length,
                    meshFilters.Length,
                    uniqueMeshes.Count,
                    materialSlots,
                    vertices,
                    triangles,
                    view.TopVisualCount,
                    view.MapObjectVisualCount,
                    view.ActiveMapObjectVisualCount);
            }

            public void RecordSamples(string suffix)
            {
                Measure.Custom(new SampleGroup($"renderers_{suffix}", SampleUnit.Undefined), RendererCount);
                Measure.Custom(new SampleGroup($"mesh_filters_{suffix}", SampleUnit.Undefined), MeshFilterCount);
                Measure.Custom(new SampleGroup($"unique_meshes_{suffix}", SampleUnit.Undefined), UniqueMeshCount);
                Measure.Custom(new SampleGroup($"material_slots_{suffix}", SampleUnit.Undefined), MaterialSlotCount);
                Measure.Custom(new SampleGroup($"vertices_{suffix}", SampleUnit.Undefined), TotalVertices);
                Measure.Custom(new SampleGroup($"triangles_{suffix}", SampleUnit.Undefined), TotalTriangles);
                Measure.Custom(new SampleGroup($"top_visuals_{suffix}", SampleUnit.Undefined), TopVisualCount);
                Measure.Custom(new SampleGroup($"map_object_visuals_{suffix}", SampleUnit.Undefined), MapObjectVisualCount);
                Measure.Custom(new SampleGroup($"active_map_object_visuals_{suffix}", SampleUnit.Undefined), ActiveMapObjectVisualCount);
            }
        }
    }
}


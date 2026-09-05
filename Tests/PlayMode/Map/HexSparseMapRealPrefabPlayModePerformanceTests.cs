using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using Unity.PerformanceTesting;
using UnityEngine;
using UnityEngine.TestTools;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SeoulPlayup.Map.Tests.PlayMode
{
    public sealed class HexSparseMapRealPrefabPlayModePerformanceTests
    {
        private const string ArtifactDirectory = ".omx/artifacts/perf-map-source/playmode-real-prefab";
        private const string CsvPath = ArtifactDirectory + "/playmode-real-prefab-summary.csv";
        private const string AtlasCatalogPath = "Assets/Data/Map/Catalogs/AtlasTileCatalog.asset";
        private const string ObjectPrefabFolder = "Assets/Prefabs/Object";
        private const string PreferredAtlasVisualId = "tile-r01_base";
        private const int WarmupFrames = 10;
        private const int MeasurementFrames = 60;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            Directory.CreateDirectory(ArtifactDirectory);
            File.WriteAllText(
                CsvPath,
                "cells,props,side_visuals_enabled,side_visuals,render_ms,steady_avg_ms,steady_p95_ms,pan_avg_ms,pan_p95_ms,zoom_avg_ms,zoom_p95_ms,fade_hover_avg_ms,fade_hover_p95_ms,path_ms,visibility_ms,renderers,mesh_filters,vertices,triangles,active_object_visuals\n");
        }

        [UnityTest]
        [Explicit("PlayMode frame-time matrix for generated large maps using the real building prefabs."), Performance]
        public IEnumerator RealPrefabGeneratedMap_FrameTimeMatrix(
            [Values(1000, 5000, 10000)] int cellCount,
            [Values(25, 100, 250)] int buildingCount,
            [Values(true, false)] bool renderSideVisuals)
        {
            using (var fixture = RealPrefabMapFixture.Create(cellCount, buildingCount))
            {
                var sideMode = renderSideVisuals ? "SidesOn" : "SidesOff";
                var root = new GameObject($"PlayModePerf_{cellCount}_{buildingCount}_{sideMode}");
                var view = root.AddComponent<AtlasTilePresentationView>();
                view.ConfigureForTests(fixture.AtlasCatalog, tileRadius: 0.25f, tileSpacing: 0.25f, heightStep: 0.1f, mapObjectCatalogSet: fixture.ObjectCatalog, renderSideVisuals: renderSideVisuals);

                var cameraObject = new GameObject($"PlayModePerfCamera_{cellCount}_{buildingCount}_{sideMode}");
                var camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = Mathf.Max(20f, Mathf.Sqrt(cellCount) * 0.5f);
                camera.transform.position = new Vector3(Mathf.Sqrt(cellCount) * 0.18f, 35f, -Mathf.Sqrt(cellCount) * 0.18f);
                camera.transform.rotation = Quaternion.Euler(60f, 0f, 0f);

                try
                {
                    var renderStopwatch = Stopwatch.StartNew();
                    view.Render(fixture.Map);
                    renderStopwatch.Stop();

                    Assert.That(view.TopVisualCount, Is.EqualTo(cellCount));
                    Assert.That(view.MapObjectVisualCount, Is.EqualTo(buildingCount));
                    Assert.That(view.RenderSideVisuals, Is.EqualTo(renderSideVisuals));
                    if (!renderSideVisuals)
                    {
                        Assert.That(view.SideVisualCount, Is.EqualTo(0));
                    }
                    Assert.That(view.MissingVisualDiagnostics, Is.Empty);

                    yield return null;
                    for (var i = 0; i < WarmupFrames; i++)
                    {
                        yield return null;
                    }

                    var steadyStats = default(FrameStats);
                    var steady = MeasureFrameDeltas(MeasurementFrames, null, stats => steadyStats = stats);
                    while (steady.MoveNext())
                    {
                        yield return steady.Current;
                    }

                    var panStats = default(FrameStats);
                    var pan = MeasureFrameDeltas(MeasurementFrames, frame =>
                    {
                        camera.transform.position += new Vector3(0.03f, 0f, -0.02f);
                    }, stats => panStats = stats);
                    while (pan.MoveNext())
                    {
                        yield return pan.Current;
                    }

                    var zoomStats = default(FrameStats);
                    var zoom = MeasureFrameDeltas(MeasurementFrames, frame =>
                    {
                        camera.orthographicSize += frame % 2 == 0 ? 0.04f : -0.02f;
                    }, stats => zoomStats = stats);
                    while (zoom.MoveNext())
                    {
                        yield return zoom.Current;
                    }

                    var fadeHoverStats = default(FrameStats);
                    var fadeHoverRay = new Ray(camera.transform.position, camera.transform.forward);
                    var fadeHover = MeasureFrameDeltas(MeasurementFrames, frame =>
                    {
                        view.RefreshMapObjectFade(camera, fixture.StartCoord);
                        view.RefreshMapObjectHoverRay(fadeHoverRay);
                    }, stats => fadeHoverStats = stats);
                    while (fadeHover.MoveNext())
                    {
                        yield return fadeHover.Current;
                    }

                    var pathStopwatch = Stopwatch.StartNew();
                    var reachable = HexPathfinder.GetReachableCells(fixture.Map, new MovementQuery(fixture.StartCoord, 12, includeStart: true));
                    pathStopwatch.Stop();
                    Assert.That(reachable.Count, Is.GreaterThan(0));

                    var visibilityStopwatch = Stopwatch.StartNew();
                    var visibility = new HexVisibilityRuntime(fixture.Map, fixture.StartCoord, 8);
                    visibility.RefreshTemporaryRevealArea(fixture.MidCoord, 8);
                    visibilityStopwatch.Stop();
                    Assert.That(visibility.States.Count, Is.GreaterThan(0));

                    var metrics = MeshComplexityMetrics.From(root, view);
                    Assert.That(metrics.TotalTriangles, Is.GreaterThan(0));

                    RecordSamples(cellCount, buildingCount, renderSideVisuals, view.SideVisualCount, renderStopwatch.Elapsed.TotalMilliseconds, steadyStats, panStats, zoomStats, fadeHoverStats, pathStopwatch.Elapsed.TotalMilliseconds, visibilityStopwatch.Elapsed.TotalMilliseconds, metrics);
                }
                finally
                {
                    UnityEngine.Object.Destroy(root);
                    UnityEngine.Object.Destroy(cameraObject);
                }
            }
        }

        private static IEnumerator MeasureFrameDeltas(int frameCount, Action<int> beforeYield, Action<FrameStats> onComplete)
        {
            var samples = new List<float>(frameCount);
            for (var frame = 0; frame < frameCount; frame++)
            {
                beforeYield?.Invoke(frame);
                yield return null;
                samples.Add(Time.unscaledDeltaTime * 1000f);
            }

            onComplete?.Invoke(FrameStats.From(samples));
        }

        private static void RecordSamples(
            int cellCount,
            int buildingCount,
            bool renderSideVisuals,
            int sideVisualCount,
            double renderMilliseconds,
            FrameStats steady,
            FrameStats pan,
            FrameStats zoom,
            FrameStats fadeHover,
            double pathMilliseconds,
            double visibilityMilliseconds,
            MeshComplexityMetrics metrics)
        {
            var sideSuffix = renderSideVisuals ? "sides_on" : "sides_off";
            Measure.Custom(new SampleGroup($"playmode_render_{cellCount}_cells_{buildingCount}_props_{sideSuffix}", SampleUnit.Millisecond), renderMilliseconds);
            Measure.Custom(new SampleGroup($"playmode_steady_avg_{cellCount}_cells_{buildingCount}_props_{sideSuffix}", SampleUnit.Millisecond), steady.AverageMilliseconds);
            Measure.Custom(new SampleGroup($"playmode_steady_p95_{cellCount}_cells_{buildingCount}_props_{sideSuffix}", SampleUnit.Millisecond), steady.P95Milliseconds);
            Measure.Custom(new SampleGroup($"playmode_pan_avg_{cellCount}_cells_{buildingCount}_props_{sideSuffix}", SampleUnit.Millisecond), pan.AverageMilliseconds);
            Measure.Custom(new SampleGroup($"playmode_pan_p95_{cellCount}_cells_{buildingCount}_props_{sideSuffix}", SampleUnit.Millisecond), pan.P95Milliseconds);
            Measure.Custom(new SampleGroup($"playmode_zoom_avg_{cellCount}_cells_{buildingCount}_props_{sideSuffix}", SampleUnit.Millisecond), zoom.AverageMilliseconds);
            Measure.Custom(new SampleGroup($"playmode_zoom_p95_{cellCount}_cells_{buildingCount}_props_{sideSuffix}", SampleUnit.Millisecond), zoom.P95Milliseconds);
            Measure.Custom(new SampleGroup($"playmode_fade_hover_avg_{cellCount}_cells_{buildingCount}_props_{sideSuffix}", SampleUnit.Millisecond), fadeHover.AverageMilliseconds);
            Measure.Custom(new SampleGroup($"playmode_fade_hover_p95_{cellCount}_cells_{buildingCount}_props_{sideSuffix}", SampleUnit.Millisecond), fadeHover.P95Milliseconds);
            Measure.Custom(new SampleGroup($"playmode_path_{cellCount}_cells_{buildingCount}_props_{sideSuffix}", SampleUnit.Millisecond), pathMilliseconds);
            Measure.Custom(new SampleGroup($"playmode_visibility_{cellCount}_cells_{buildingCount}_props_{sideSuffix}", SampleUnit.Millisecond), visibilityMilliseconds);
            Measure.Custom(new SampleGroup($"playmode_side_visuals_{cellCount}_cells_{buildingCount}_props_{sideSuffix}", SampleUnit.Undefined), sideVisualCount);

            var line = string.Join(",", new[]
            {
                cellCount.ToString(CultureInfo.InvariantCulture),
                buildingCount.ToString(CultureInfo.InvariantCulture),
                renderSideVisuals ? "true" : "false",
                sideVisualCount.ToString(CultureInfo.InvariantCulture),
                renderMilliseconds.ToString("0.###", CultureInfo.InvariantCulture),
                steady.AverageMilliseconds.ToString("0.###", CultureInfo.InvariantCulture),
                steady.P95Milliseconds.ToString("0.###", CultureInfo.InvariantCulture),
                pan.AverageMilliseconds.ToString("0.###", CultureInfo.InvariantCulture),
                pan.P95Milliseconds.ToString("0.###", CultureInfo.InvariantCulture),
                zoom.AverageMilliseconds.ToString("0.###", CultureInfo.InvariantCulture),
                zoom.P95Milliseconds.ToString("0.###", CultureInfo.InvariantCulture),
                fadeHover.AverageMilliseconds.ToString("0.###", CultureInfo.InvariantCulture),
                fadeHover.P95Milliseconds.ToString("0.###", CultureInfo.InvariantCulture),
                pathMilliseconds.ToString("0.###", CultureInfo.InvariantCulture),
                visibilityMilliseconds.ToString("0.###", CultureInfo.InvariantCulture),
                metrics.RendererCount.ToString(CultureInfo.InvariantCulture),
                metrics.MeshFilterCount.ToString(CultureInfo.InvariantCulture),
                metrics.TotalVertices.ToString(CultureInfo.InvariantCulture),
                metrics.TotalTriangles.ToString(CultureInfo.InvariantCulture),
                metrics.ActiveMapObjectVisualCount.ToString(CultureInfo.InvariantCulture),
            });
            File.AppendAllText(CsvPath, line + Environment.NewLine);
        }

        private readonly struct FrameStats
        {
            private FrameStats(float averageMilliseconds, float p95Milliseconds)
            {
                AverageMilliseconds = averageMilliseconds;
                P95Milliseconds = p95Milliseconds;
            }

            public float AverageMilliseconds { get; }
            public float P95Milliseconds { get; }

            public static FrameStats From(IReadOnlyList<float> samples)
            {
                if (samples == null || samples.Count == 0)
                {
                    return new FrameStats(0f, 0f);
                }

                var sorted = samples.OrderBy(sample => sample).ToArray();
                var p95Index = Mathf.Clamp(Mathf.CeilToInt(sorted.Length * 0.95f) - 1, 0, sorted.Length - 1);
                return new FrameStats(samples.Average(), sorted[p95Index]);
            }
        }

        private sealed class RealPrefabMapFixture : IDisposable
        {
            private RealPrefabMapFixture(HexSparseMapAuthoringSource source, HexMapData map, AtlasTileCatalog atlasCatalog, MapObjectCatalogSet objectCatalog, HexCoord startCoord, HexCoord midCoord)
            {
                Source = source;
                Map = map;
                AtlasCatalog = atlasCatalog;
                ObjectCatalog = objectCatalog;
                StartCoord = startCoord;
                MidCoord = midCoord;
            }

            private HexSparseMapAuthoringSource Source { get; }
            public HexMapData Map { get; }
            public AtlasTileCatalog AtlasCatalog { get; }
            public MapObjectCatalogSet ObjectCatalog { get; }
            public HexCoord StartCoord { get; }
            public HexCoord MidCoord { get; }

            public static RealPrefabMapFixture Create(int cellCount, int buildingCount)
            {
#if UNITY_EDITOR
                var atlasCatalog = AssetDatabase.LoadAssetAtPath<AtlasTileCatalog>(AtlasCatalogPath);
                Assert.That(atlasCatalog, Is.Not.Null, $"Missing atlas catalog at {AtlasCatalogPath}.");
                var atlasVisualId = atlasCatalog.Entries.FirstOrDefault(entry => entry.AtlasVisualId == PreferredAtlasVisualId)?.AtlasVisualId
                    ?? atlasCatalog.Entries.First(entry => entry.TopPrefab != null).AtlasVisualId;

                var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { ObjectPrefabFolder });
                var prefabs = prefabGuids
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .Select(path => AssetDatabase.LoadAssetAtPath<GameObject>(path))
                    .Where(prefab => prefab != null)
                    .ToArray();
                Assert.That(prefabs.Length, Is.GreaterThan(0), $"No object prefabs found in {ObjectPrefabFolder}.");

                var typedCatalog = ScriptableObject.CreateInstance<MapObjectTypedCatalog>();
                typedCatalog.ConfigureForTests(HexMapObjectType.Building, prefabs.Select(prefab => new RuntimeMapObjectPrefabCatalog.Entry(prefab.name, prefab)));
                var objectCatalog = ScriptableObject.CreateInstance<MapObjectCatalogSet>();
                objectCatalog.ConfigureForTests(new[] { typedCatalog });

                var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
                var cells = GenerateCells(cellCount, atlasVisualId).ToArray();
                var objectRefs = GenerateBuildingRefs(cells, buildingCount, prefabs).ToArray();
                source.ConfigureForTests(cells, HexMapPurpose.PlayableMap, objectRefs: objectRefs);
                Assert.That(source.TryToHexMapData(out var map, out var error), Is.True, error);
                return new RealPrefabMapFixture(source, map, atlasCatalog, objectCatalog, cells[0].Coord, cells[cells.Length / 2].Coord);
#else
                Assert.Ignore("Real prefab performance test requires UnityEditor AssetDatabase access.");
                return null;
#endif
            }

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(Source);
                foreach (var typedCatalog in ObjectCatalog.Catalogs)
                {
                    UnityEngine.Object.DestroyImmediate(typedCatalog);
                }

                UnityEngine.Object.DestroyImmediate(ObjectCatalog);
            }
        }

        private static IEnumerable<HexSparseMapAuthoringCell> GenerateCells(int cellCount, string atlasVisualId)
        {
            var width = Mathf.CeilToInt(Mathf.Sqrt(cellCount));
            for (var index = 0; index < cellCount; index++)
            {
                var q = index % width;
                var r = index / width;
                yield return new HexSparseMapAuthoringCell(new HexCoord(q, r), "perf-tile", "urban", atlasVisualId, index == 0 ? "mvp-start" : string.Empty, string.Empty, index % 3, index % 6);
            }
        }

        private static IEnumerable<HexMapObjectRef> GenerateBuildingRefs(IReadOnlyList<HexSparseMapAuthoringCell> cells, int buildingCount, IReadOnlyList<GameObject> prefabs)
        {
            var step = Math.Max(1, cells.Count / Math.Max(1, buildingCount));
            var used = new HashSet<HexCoord>();
            for (var i = 0; i < buildingCount; i++)
            {
                var cell = cells[Math.Min(cells.Count - 1, i * step)];
                if (!used.Add(cell.Coord))
                {
                    cell = cells.First(candidate => used.Add(candidate.Coord));
                }

                var prefab = prefabs[i % prefabs.Count];
                yield return new HexMapObjectRef($"real-prefab-building-{i:0000}", HexMapObjectType.Building, prefab.name, cell.Q, cell.R, role: "building_prop", enabledForPurpose: HexMapPurpose.PlayableMap, blocksMovement: false, blocksVision: true, interactable: false, rotationSteps: i % 6);
            }
        }

        private readonly struct MeshComplexityMetrics
        {
            private MeshComplexityMetrics(int rendererCount, int meshFilterCount, long totalVertices, long totalTriangles, int activeMapObjectVisualCount)
            {
                RendererCount = rendererCount;
                MeshFilterCount = meshFilterCount;
                TotalVertices = totalVertices;
                TotalTriangles = totalTriangles;
                ActiveMapObjectVisualCount = activeMapObjectVisualCount;
            }

            public int RendererCount { get; }
            public int MeshFilterCount { get; }
            public long TotalVertices { get; }
            public long TotalTriangles { get; }
            public int ActiveMapObjectVisualCount { get; }

            public static MeshComplexityMetrics From(GameObject root, AtlasTilePresentationView view)
            {
                var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
                var meshFilters = root.GetComponentsInChildren<MeshFilter>(includeInactive: true);
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
                        triangles += (long)mesh.GetIndexCount(subMesh) / 3L;
                    }
                }

                return new MeshComplexityMetrics(renderers.Length, meshFilters.Length, vertices, triangles, view.ActiveMapObjectVisualCount);
            }
        }
    }
}


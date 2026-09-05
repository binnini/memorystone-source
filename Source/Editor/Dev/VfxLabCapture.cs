using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Combat.Unity.Dev;
using SeoulPlayup.Map.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SeoulPlayup.EditorTools.Dev
{
    /// <summary>
    /// VFX 랩의 <b>에이전트 출구</b> — 패턴별 스틸(PNG) + 기계가 읽는 리포트(JSON)를 굽는다.
    ///
    /// <para>왜 필요한가: 랩이 플레이 모드 IMGUI뿐이면 <b>결과를 본 사람만</b> 판정할 수 있다.
    /// 에이전트는 "무엇이 잘못됐는지"를 사용자 구술에 의존하게 되고, 색이 초록으로 튀거나 모듈이
    /// 한 칸을 넘는 것 같은 결함을 스스로 잡을 수 없다(이번 갈래 1에서 실제로 별도 스크립트를 짜서야
    /// 잡았다). 이 도구가 그 루프를 닫는다.</para>
    ///
    /// <para>🔑 <b>판정 함수는 프로덕션 것을 그대로 쓴다</b>:
    /// <see cref="EffectVfxCatalog.ResolveAll"/>(어느 큐가 뜨는가 · 폴백인가 · 하이브리드인가) ·
    /// <see cref="EffectPresentationController.ResolveSpawnPose"/>(어디에 어떤 크기로) ·
    /// <see cref="EffectPresentationController.ResolvePerTileSpawnDelay"/>(칸마다 언제) ·
    /// <see cref="AttackShapeLibrary.GetAffectedCells"/>(어느 칸을 덮는가).</para>
    ///
    /// <para>⚠️ 한 가지만 프로덕션과 다르다: 런타임은 코루틴으로 시간에 맞춰 스폰하지만, 여기서는
    /// 시각 <c>t</c>를 주면 각 칸을 <c>t − delay</c>만큼 <see cref="ParticleSystem.Simulate"/>로
    /// 감아 <b>정지 화면</b>을 만든다. 플레이 모드가 필요 없고 프레임 운에 좌우되지 않는 것이 목적이며,
    /// 스폰 위치·크기·타이밍 계산 자체는 위의 프로덕션 함수들이 한다.</para>
    /// </summary>
    public static class VfxLabCapture
    {
        private const string CatalogPath = "Assets/Resources/Combat/DefaultEffectVfxCatalog.asset";
        private const string DefaultOutputFolder = "Temp/VfxLabCaptures";

        /// <summary>캡처 전 씬이 저장되지 않은 상태였을 때 돌아갈 알려진 씬.</summary>
        private const string FallbackScenePath = "Assets/Scenes/Dev/PrototypeTest.unity";

        public enum View
        {
            /// <summary>비스듬한 측면 — 높이와 발광이 보인다.</summary>
            Side,

            /// <summary>바로 위 — 전방(+Z)이 화면 위로 곧게 뻗어 방향·형상이 애매해지지 않는다.</summary>
            Top,
        }

        [MenuItem("Tools/Seoul Playup/Combat/Capture VFX Lab Stills (All Monster Patterns)")]
        public static void CaptureAllMenu()
        {
            var report = CaptureAllPatterns(DefaultOutputFolder, new[] { 0.25f }, new[] { View.Side, View.Top });
            Debug.Log(report);
            EditorUtility.RevealInFinder(Path.Combine(DefaultOutputFolder, "report.json"));
        }

        /// <summary>
        /// 바인딩 CSV가 커버하는 <b>모든</b> 몬스터 패턴을 굽는다. 반환 문자열은 사람이 읽는 요약이고,
        /// 기계가 읽는 정본은 <c>{outputFolder}/report.json</c>이다.
        /// </summary>
        public static string CaptureAllPatterns(string outputFolder, float[] times, View[] views)
        {
            // 바인딩이 있는 패턴만 굽지 않는다 — <b>미저작 패턴이야말로 봐야 할 것</b>이다.
            // 그쪽은 범용 폴백으로 해소되며 리포트에 genericFallback=true로 찍히므로, 이 캡처가
            // 곧 "아직 아트가 필요한 목록"이 된다.
            var patternIds = VfxLabPatternIndex.Build()
                .Select(entry => entry.PatternId)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            return Capture(patternIds, outputFolder, times, views);
        }

        /// <summary>패턴 몇 개만 굽는다. 에이전트가 <c>script-execute</c>로 부르는 진입점이다.</summary>
        public static string Capture(string[] patternIds, string outputFolder, float[] times, View[] views)
        {
            if (patternIds == null || patternIds.Length == 0)
            {
                return "VfxLabCapture: patternIds가 비었다.";
            }

            times = times != null && times.Length > 0 ? times : new[] { 0.25f };
            views = views != null && views.Length > 0 ? views : new[] { View.Side };
            outputFolder = string.IsNullOrWhiteSpace(outputFolder) ? DefaultOutputFolder : outputFolder;
            Directory.CreateDirectory(outputFolder);

            var catalog = AssetDatabase.LoadAssetAtPath<EffectVfxCatalog>(CatalogPath);
            if (catalog == null)
            {
                return $"VfxLabCapture: 카탈로그를 못 찾았다 — {CatalogPath}";
            }

            // ⚠️ patternId는 몬스터를 가로질러 유일하지 않다 — 터렛 공용 패턴 A900은 M901·M905 둘 다
            // 들고 있다. 그냥 ToDictionary 하면 중복 키로 던진다(실측으로 밟음). 먼저 나온 것을 쓴다:
            // 스틸은 "이 패턴이 어떻게 보이나"를 묻는 것이고 소유 몬스터는 그림을 바꾸지 않는다.
            var patterns = VfxLabPatternIndex.Build()
                .GroupBy(entry => entry.PatternId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var previousScene = SceneManager.GetActiveScene().path;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var results = new List<PatternResult>();
            try
            {
                var stageGo = new GameObject("VFX Lab Capture Stage");
                var stage = stageGo.AddComponent<VfxLabStage>();
                stage.EnsureBoard();

                var camGo = new GameObject("Capture Camera");
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = ParseColor("#0B111F");
                cam.fieldOfView = 45f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 200f;

                foreach (var patternId in patternIds)
                {
                    // 패턴 하나가 넘어져도 나머지는 굽는다 — 전량 캡처가 통째로 날아가면 리포트도
                    // 안 남아 무엇이 문제인지조차 알 수 없다(A900 중복 키로 실제로 그렇게 잃었다).
                    try
                    {
                        results.Add(CapturePattern(catalog, patterns, stage, cam, patternId, outputFolder, times, views));
                    }
                    catch (Exception ex)
                    {
                        results.Add(new PatternResult { PatternId = patternId, Error = ex.GetType().Name + ": " + ex.Message });
                    }
                }

                UnityEngine.Object.DestroyImmediate(camGo);
                UnityEngine.Object.DestroyImmediate(stageGo);
            }
            finally
            {
                // 🔴 이름 없는 씬으로는 돌아갈 수 없다. 그대로 두면 에디터가 Untitled 상태로 남고
                // tests-run이 "No tests found"로 넘어진다(실측으로 밟음) — 알려진 씬으로 복귀시킨다.
                var restoreTo = !string.IsNullOrEmpty(previousScene) && File.Exists(previousScene)
                    ? previousScene
                    : FallbackScenePath;
                if (File.Exists(restoreTo))
                {
                    EditorSceneManager.OpenScene(restoreTo, OpenSceneMode.Single);
                }
                else
                {
                    _ = scene;
                }
            }

            var jsonPath = Path.Combine(outputFolder, "report.json");
            File.WriteAllText(jsonPath, BuildJson(results), new UTF8Encoding(false));
            return BuildSummary(results, outputFolder, jsonPath);
        }

        private static PatternResult CapturePattern(
            EffectVfxCatalog catalog,
            IReadOnlyDictionary<string, VfxLabPatternIndex.Entry> patterns,
            VfxLabStage stage,
            Camera cam,
            string patternId,
            string outputFolder,
            float[] times,
            View[] views)
        {
            var result = new PatternResult { PatternId = patternId };
            if (!patterns.TryGetValue(patternId, out var info))
            {
                result.Error = "monster_attack_patterns.csv에 없는 패턴";
                return result;
            }

            result.ShapeId = info.Pattern.ShapeId;
            result.DisplayName = info.Pattern.DisplayName;

            var origin = new HexCoord(0, 0);
            const HexDirection direction = HexDirection.East;
            var cells = info.ResolveCells(origin, direction);
            result.CellCount = cells.Count;

            var resultEvent = info.BuildEvent(origin, cells);
            var entries = catalog.ResolveAll(resultEvent);
            if (entries.Length == 0)
            {
                result.Error = "해소되는 카탈로그 엔트리가 없다";
                return result;
            }

            stage.HighlightCells(cells);
            var facing = VfxLabStage.ResolveFacing(direction);
            var centerWorld = VfxLabStage.CellToWorld(origin);

            foreach (var entry in entries)
            {
                result.Entries.Add(new EntryInfo
                {
                    CueId = string.IsNullOrEmpty(entry.CueId) ? "<none>" : entry.CueId,
                    SourceRef = string.IsNullOrEmpty(entry.SourceRef) ? "<GENERIC-FALLBACK>" : entry.SourceRef,
                    GenericFallback = string.IsNullOrEmpty(entry.SourceRef),
                    Prefab = entry.Prefabs != null && entry.Prefabs.Length > 0 && entry.Prefabs[0] != null
                        ? entry.Prefabs[0].name
                        : "<null>",
                    AreaSpawnMode = entry.AreaSpawnMode.ToString(),
                    PerTileDelaySeconds = entry.PerTileDelaySeconds,
                    PlaybackDelaySeconds = entry.PlaybackDelaySeconds,
                    ScaleWithRadius = entry.ScaleWithRadius,
                    SpawnAnchor = entry.SpawnAnchor.ToString(),
                    LastSpawnSeconds = entry.AreaSpawnMode == EffectVfxAreaSpawnMode.PerTile
                        ? EffectPresentationController.ResolvePerTileSpawnDelay(entry, Mathf.Max(0, cells.Count - 1))
                        : entry.PlaybackDelaySeconds,
                });
            }

            foreach (var time in times)
            {
                var spawned = new List<GameObject>();
                foreach (var entry in entries)
                {
                    SpawnFrozen(entry, resultEvent, cells, centerWorld, facing, time, spawned, out var extent);
                    result.MaxWorldExtent = Mathf.Max(result.MaxWorldExtent, extent);
                }

                foreach (var view in views)
                {
                    var file = $"{patternId}_{time.ToString("0.00", CultureInfo.InvariantCulture)}_{view}".Replace('.', 'p') + ".png";
                    var path = Path.Combine(outputFolder, file);
                    Render(cam, view, path);
                    result.Images.Add(path);
                }

                foreach (var go in spawned)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }

            stage.ClearHighlight();
            return result;
        }

        /// <summary>
        /// 시각 <paramref name="time"/>의 정지 화면을 만든다. 칸별 지연은
        /// <see cref="EffectPresentationController.ResolvePerTileSpawnDelay"/>가, 놓일 자리는
        /// <see cref="EffectPresentationController.ResolveSpawnPose"/>가 결정한다 — 둘 다 프로덕션 함수다.
        /// </summary>
        private static void SpawnFrozen(
            EffectVfxCatalog.Entry entry,
            EffectResultEvent resultEvent,
            IReadOnlyList<HexCoord> cells,
            Vector3 centerWorld,
            Quaternion facing,
            float time,
            List<GameObject> spawned,
            out float worldExtent)
        {
            worldExtent = 0f;
            var prefab = entry.Prefabs != null && entry.Prefabs.Length > 0 ? entry.Prefabs[0] : null;
            if (prefab == null)
            {
                return;
            }

            var perTile = entry.AreaSpawnMode == EffectVfxAreaSpawnMode.PerTile && cells.Count > 0;
            var count = perTile ? cells.Count : 1;

            for (var i = 0; i < count; i++)
            {
                var delay = perTile
                    ? EffectPresentationController.ResolvePerTileSpawnDelay(entry, i)
                    : entry.PlaybackDelaySeconds;

                // 아직 뜨지 않은 칸은 화면에 없어야 한다 — 스태거를 스틸로 판정할 수 있는 이유다.
                var localTime = time - (delay - entry.PlaybackDelaySeconds);
                if (localTime < 0f)
                {
                    continue;
                }

                var worldPosition = perTile ? VfxLabStage.CellToWorld(cells[i]) : centerWorld;
                var pose = EffectPresentationController.ResolveSpawnPose(entry, resultEvent, worldPosition, facing);

                GameObject go;
                if (!pose.HasAxisScale)
                {
                    go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    go.transform.SetPositionAndRotation(pose.Position, pose.Rotation);
                    go.transform.localScale = Vector3.Scale(go.transform.localScale, Vector3.one * pose.UniformScale);
                }
                else
                {
                    go = new GameObject("VFX " + prefab.name);
                    go.transform.SetPositionAndRotation(pose.Position, pose.Rotation);
                    go.transform.localScale = Vector3.one * pose.UniformScale;
                    var pivot = new GameObject("Axis Scale").transform;
                    pivot.SetParent(go.transform, false);
                    pivot.localScale = pose.AxisScale;
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    instance.transform.SetParent(pivot, false);
                    instance.transform.localPosition = Vector3.zero;
                    instance.transform.localRotation = Quaternion.identity;
                }

                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (ps.transform.parent != null && ps.transform.parent.GetComponentInParent<ParticleSystem>() != null)
                    {
                        continue;
                    }

                    ps.Simulate(Mathf.Max(0.0001f, localTime), true, true);
                }

                worldExtent = Mathf.Max(worldExtent, pose.Position.magnitude + pose.UniformScale);
                spawned.Add(go);
            }
        }

        private static void Render(Camera cam, View view, string path)
        {
            const float side = 9f;
            if (view == View.Top)
            {
                var center = new Vector3(0f, 0f, 2f);
                cam.transform.position = center + new Vector3(0f, 11f, -0.001f);
                cam.transform.LookAt(center);
            }
            else
            {
                var center = new Vector3(0f, 0f, 3f);
                cam.transform.position = center + new Vector3(0f, side, -side * 1.15f);
                cam.transform.LookAt(center);
            }

            var rt = new RenderTexture(960, 640, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
        }

        private sealed class EntryInfo
        {
            public string CueId;
            public string SourceRef;
            public bool GenericFallback;
            public string Prefab;
            public string AreaSpawnMode;
            public float PerTileDelaySeconds;
            public float PlaybackDelaySeconds;
            public bool ScaleWithRadius;
            public string SpawnAnchor;
            public float LastSpawnSeconds;
        }

        private sealed class PatternResult
        {
            public string PatternId = string.Empty;
            public string DisplayName = string.Empty;
            public string ShapeId = string.Empty;
            public int CellCount;
            public float MaxWorldExtent;
            public string Error = string.Empty;
            public readonly List<EntryInfo> Entries = new List<EntryInfo>();
            public readonly List<string> Images = new List<string>();
        }

        private static string BuildJson(List<PatternResult> results)
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"cellPitchUnits\": ").Append(VfxLabStage.CellPitch.ToString("0.####", CultureInfo.InvariantCulture));
            sb.Append(",\n  \"patterns\": [\n");
            for (var i = 0; i < results.Count; i++)
            {
                var r = results[i];
                sb.Append("    {");
                sb.Append("\"patternId\":").Append(Json(r.PatternId));
                sb.Append(",\"displayName\":").Append(Json(r.DisplayName));
                sb.Append(",\"shapeId\":").Append(Json(r.ShapeId));
                sb.Append(",\"cellCount\":").Append(r.CellCount);
                sb.Append(",\"maxWorldExtent\":").Append(r.MaxWorldExtent.ToString("0.###", CultureInfo.InvariantCulture));
                sb.Append(",\"error\":").Append(Json(r.Error));
                sb.Append(",\"entries\":[");
                for (var e = 0; e < r.Entries.Count; e++)
                {
                    var entry = r.Entries[e];
                    sb.Append("{\"cueId\":").Append(Json(entry.CueId));
                    sb.Append(",\"sourceRef\":").Append(Json(entry.SourceRef));
                    sb.Append(",\"genericFallback\":").Append(entry.GenericFallback ? "true" : "false");
                    sb.Append(",\"prefab\":").Append(Json(entry.Prefab));
                    sb.Append(",\"areaSpawnMode\":").Append(Json(entry.AreaSpawnMode));
                    sb.Append(",\"spawnAnchor\":").Append(Json(entry.SpawnAnchor));
                    sb.Append(",\"scaleWithRadius\":").Append(entry.ScaleWithRadius ? "true" : "false");
                    sb.Append(",\"perTileDelaySeconds\":").Append(entry.PerTileDelaySeconds.ToString("0.####", CultureInfo.InvariantCulture));
                    sb.Append(",\"playbackDelaySeconds\":").Append(entry.PlaybackDelaySeconds.ToString("0.####", CultureInfo.InvariantCulture));
                    sb.Append(",\"lastSpawnSeconds\":").Append(entry.LastSpawnSeconds.ToString("0.####", CultureInfo.InvariantCulture));
                    sb.Append('}');
                    if (e < r.Entries.Count - 1)
                    {
                        sb.Append(',');
                    }
                }

                sb.Append("],\"images\":[");
                sb.Append(string.Join(",", r.Images.Select(Json)));
                sb.Append("]}");
                if (i < results.Count - 1)
                {
                    sb.Append(',');
                }

                sb.Append('\n');
            }

            sb.Append("  ]\n}\n");
            return sb.ToString();
        }

        private static string Json(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static string BuildSummary(List<PatternResult> results, string outputFolder, string jsonPath)
        {
            var sb = new StringBuilder();
            sb.Append("[VfxLabCapture] ").Append(results.Count).Append("개 패턴 · 셀 피치 ")
                .Append(VfxLabStage.CellPitch.ToString("0.###", CultureInfo.InvariantCulture)).Append("u\n");
            sb.Append("  출력 ").Append(outputFolder).Append("  리포트 ").Append(jsonPath).Append('\n');
            foreach (var r in results)
            {
                if (!string.IsNullOrEmpty(r.Error))
                {
                    sb.Append("  ! ").Append(r.PatternId).Append(": ").Append(r.Error).Append('\n');
                    continue;
                }

                var fallback = r.Entries.Any(e => e.GenericFallback) ? " ⚠ 범용 폴백" : string.Empty;
                sb.Append("  ").Append(r.PatternId).Append(' ').Append(r.ShapeId)
                    .Append(" · 칸 ").Append(r.CellCount)
                    .Append(" · 큐 ").Append(string.Join("+", r.Entries.Select(e => e.CueId)))
                    .Append(fallback).Append('\n');
            }

            return sb.ToString();
        }

        private static Color ParseColor(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out var color) ? color : Color.magenta;
        }
    }
}

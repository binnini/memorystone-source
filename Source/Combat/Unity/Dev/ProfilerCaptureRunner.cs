#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;
using Profiler = UnityEngine.Profiling.Profiler;

namespace SeoulPlayup.Combat.Unity.Dev
{
    /// <summary>
    /// Editor-only deep-profile capture (perf-profiling P3). Records a scenario in play mode and
    /// emits two artifacts under <see cref="ArtifactDirectory"/>:
    ///   • <c>&lt;scene&gt;-&lt;yyyymmdd-hhmmss&gt;-&lt;label&gt;.data</c> — a Unity Profiler capture a
    ///     human opens in the Profiler window (Load) for the full hierarchy/timeline.
    ///   • <c>…-markers.json</c> — top-N main-thread markers by self time, so an agent can read the
    ///     numbers directly without the binary .data.
    /// Scene-neutral (lookdev + game). The lookdev REAL MAP renders on play Start, so capture after
    /// entering play. Drive it via <see cref="BeginScenarioCapture"/> from a script-execute/reflection
    /// call while already in play mode; poll the console for the "PROFILER_CAPTURE_DONE|" line.
    /// </summary>
    public static class ProfilerCapture
    {
        public const string ArtifactDirectory = ".omx/artifacts/profiler";
        public const string DoneLogPrefix = "PROFILER_CAPTURE_DONE|";
        private const int MainThreadIndex = 0;
        private const int WarmupFrames = 12;

        /// <summary>
        /// Spawn a hidden runtime runner that records the scenario and writes the artifacts, then
        /// self-destructs. Safe to call once per capture while in play mode. When <paramref name="moveCount"/>
        /// &gt; 0 it debug-moves the player that many turns (per-turn cost); otherwise it records
        /// <paramref name="steadyFrames"/> idle frames (steady/lookdev cost).
        /// </summary>
        public static void BeginScenarioCapture(string sceneLabel, string label, int steadyFrames, int moveCount, int topN)
        {
            var go = new GameObject("~ProfilerCaptureRunner") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            var runner = go.AddComponent<ProfilerCaptureRunner>();
            runner.Configure(sceneLabel, label, Mathf.Max(1, steadyFrames), Mathf.Max(0, moveCount), Mathf.Clamp(topN, 1, 100));
        }

        /// <summary>
        /// Save the frames currently in the profiler buffer to a .data file and aggregate the last
        /// <paramref name="window"/> completed main-thread frames into a top-N self-time markers JSON.
        /// Returns the markers JSON path. Must run while those frames are still in the buffer.
        /// </summary>
        public static string CaptureRecordedFrames(string sceneLabel, string label, int window, int topN)
        {
            Directory.CreateDirectory(ArtifactDirectory);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var stem = ArtifactDirectory + "/" + Sanitize(sceneLabel) + "-" + stamp + "-" + Sanitize(label);
            var dataPath = stem + ".data";
            var jsonPath = stem + "-markers.json";

            var savedData = false;
            try { savedData = ProfilerDriver.SaveProfile(dataPath); }
            catch (Exception e) { Debug.LogWarning("ProfilerCapture: SaveProfile failed: " + e.Message); }

            var last = ProfilerDriver.lastFrameIndex;
            var first = ProfilerDriver.firstFrameIndex;
            // The very last frame may be mid-flight; aggregate completed frames only.
            var hi = last - 1;
            var lo = Mathf.Max(first, hi - Mathf.Max(1, window) + 1);

            var selfByName = new Dictionary<string, double>();
            var callsByName = new Dictionary<string, double>();
            var frames = 0;
            var frameMsSum = 0.0;
            var kids = new List<int>();

            for (var f = lo; f <= hi && f >= 0; f++)
            {
                HierarchyFrameDataView view = null;
                try
                {
                    view = ProfilerDriver.GetHierarchyFrameDataView(
                        f, MainThreadIndex, HierarchyFrameDataView.ViewModes.Default,
                        HierarchyFrameDataView.columnSelfTime, false);
                    if (view == null || !view.valid)
                    {
                        continue;
                    }

                    frames++;
                    frameMsSum += view.frameTimeMs;

                    var stack = new Stack<int>();
                    stack.Push(view.GetRootItemID());
                    while (stack.Count > 0)
                    {
                        var id = stack.Pop();
                        var name = view.GetItemName(id);
                        if (!string.IsNullOrEmpty(name))
                        {
                            var self = view.GetItemColumnDataAsSingle(id, HierarchyFrameDataView.columnSelfTime);
                            var calls = view.GetItemColumnDataAsSingle(id, HierarchyFrameDataView.columnCalls);
                            selfByName.TryGetValue(name, out var s);
                            selfByName[name] = s + self;
                            callsByName.TryGetValue(name, out var c);
                            callsByName[name] = c + calls;
                        }

                        view.GetItemChildren(id, kids);
                        for (var i = 0; i < kids.Count; i++)
                        {
                            stack.Push(kids[i]);
                        }
                    }
                }
                finally
                {
                    view?.Dispose();
                }
            }

            var divisor = Math.Max(1, frames);
            var top = selfByName
                .Select(kv => new MarkerRow(kv.Key, kv.Value / divisor, callsByName[kv.Key] / divisor))
                .OrderByDescending(r => r.SelfMsPerFrame)
                .Take(topN)
                .ToList();

            WriteJson(jsonPath, sceneLabel, label, stamp, dataPath, savedData, lo, hi, frames, frameMsSum / divisor, top);
            return jsonPath;
        }

        private static void WriteJson(
            string jsonPath, string sceneLabel, string label, string stamp, string dataPath, bool savedData,
            int lo, int hi, int frames, double avgFrameMs, IReadOnlyList<MarkerRow> top)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"scene\": ").Append(Quote(sceneLabel)).Append(",\n");
            sb.Append("  \"label\": ").Append(Quote(label)).Append(",\n");
            sb.Append("  \"timestamp\": ").Append(Quote(stamp)).Append(",\n");
            sb.Append("  \"dataFile\": ").Append(Quote(dataPath)).Append(",\n");
            sb.Append("  \"dataFileSaved\": ").Append(savedData ? "true" : "false").Append(",\n");
            sb.Append("  \"frameRange\": [").Append(lo).Append(", ").Append(hi).Append("],\n");
            sb.Append("  \"framesAggregated\": ").Append(frames).Append(",\n");
            sb.Append("  \"avgFrameMs\": ").Append(Num(avgFrameMs)).Append(",\n");
            sb.Append("  \"thread\": \"main\",\n");
            sb.Append("  \"topMarkersBySelfMs\": [\n");
            for (var i = 0; i < top.Count; i++)
            {
                var r = top[i];
                sb.Append("    { \"name\": ").Append(Quote(r.Name))
                  .Append(", \"selfMsPerFrame\": ").Append(Num(r.SelfMsPerFrame))
                  .Append(", \"callsPerFrame\": ").Append(Num(r.CallsPerFrame))
                  .Append(" }").Append(i < top.Count - 1 ? "," : "").Append("\n");
            }
            sb.Append("  ]\n");
            sb.Append("}\n");
            File.WriteAllText(jsonPath, sb.ToString());
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "unnamed";
            }
            var chars = value.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-').ToArray();
            return new string(chars);
        }

        private static string Num(double value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private static string Quote(string value)
        {
            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private readonly struct MarkerRow
        {
            public MarkerRow(string name, double selfMsPerFrame, double callsPerFrame)
            {
                Name = name;
                SelfMsPerFrame = selfMsPerFrame;
                CallsPerFrame = callsPerFrame;
            }

            public string Name { get; }
            public double SelfMsPerFrame { get; }
            public double CallsPerFrame { get; }
        }
    }

    /// <summary>Runtime driver spawned by <see cref="ProfilerCapture.BeginScenarioCapture"/>.</summary>
    internal sealed class ProfilerCaptureRunner : MonoBehaviour
    {
        private string sceneLabel;
        private string label;
        private int steadyFrames;
        private int moveCount;
        private int topN;

        public void Configure(string sceneLabelValue, string labelValue, int steadyFramesValue, int moveCountValue, int topNValue)
        {
            sceneLabel = sceneLabelValue;
            label = labelValue;
            steadyFrames = steadyFramesValue;
            moveCount = moveCountValue;
            topN = topNValue;
        }

        private IEnumerator Start()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("ProfilerCaptureRunner requires play mode; aborting.");
                Destroy(gameObject);
                yield break;
            }

            ProfilerDriver.ClearAllFrames();
            ProfilerDriver.enabled = true;
            Profiler.enabled = true;

            for (var i = 0; i < 12; i++)
            {
                yield return null;
            }

            var controller = UnityEngine.Object.FindFirstObjectByType<MapCombatController>();
            var window = Mathf.Max(steadyFrames, moveCount) + 8;

            if (moveCount > 0 && controller != null)
            {
                controller.ConfigurePresentationForTests(immediateSequences: true);
                var iteration = 0;
                for (var i = 0; i < moveCount; i++)
                {
                    DriveOneMove(controller, ref iteration);
                    yield return null;
                }
            }
            else
            {
                for (var i = 0; i < steadyFrames; i++)
                {
                    yield return null;
                }
            }

            // One settle frame so the final scenario frame is completed before we read the buffer.
            yield return null;

            var jsonPath = ProfilerCapture.CaptureRecordedFrames(sceneLabel, label, window, topN);
            Debug.Log(ProfilerCapture.DoneLogPrefix + jsonPath);

            ProfilerDriver.enabled = false;
            Destroy(gameObject);
        }

        private static void DriveOneMove(MapCombatController controller, ref int iteration)
        {
            iteration++;
            controller.BeginMoveSelection();
            var current = controller.State.PlayerCoord;
            foreach (var neighbour in current.NeighborsInDirectionOrder())
            {
                if (controller.TryDebugMoveTo(neighbour))
                {
                    return;
                }
            }
        }
    }
}
#endif

#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SeoulPlayup.Map.Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SeoulPlayup.Combat.Unity.Dev
{
    /// <summary>
    /// Editor-only A/B cost-attribution sweep for the ArtLookdev REAL MAP (perf-profiling P4).
    /// Sweeps every on/off combination of four art elements — fog-of-war (암시야), post FX (Bloom
    /// volume), prop lights (<see cref="PropLight"/>), and atmospheric fog (<see cref="RenderSettings.fog"/>)
    /// — measuring frame avg/p95 and peak UnityStats render counters for each, then writes a per-combo
    /// CSV plus a marginal main-effect table ("this element costs ~N ms"). Drives it from a runtime
    /// runner spawned by <see cref="Begin"/>; run it while the ArtLookdev scene is in play mode (the
    /// REAL MAP renders on Start). Artifacts under <see cref="ArtifactDirectory"/> (VCS-ignored).
    /// </summary>
    public static class ArtLookdevAbSweep
    {
        public const string ArtifactDirectory = ".omx/artifacts/perf-map-source/lookdev-ab";
        public const string DoneLogPrefix = "LOOKDEV_AB_DONE|";

        /// <summary>Spawn the sweep runner. <paramref name="measureFrames"/> frames are averaged per combo.</summary>
        public static void Begin(string label, int measureFrames)
        {
            var go = new GameObject("~ArtLookdevAbSweepRunner") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            var runner = go.AddComponent<ArtLookdevAbSweepRunner>();
            runner.Configure(label, Mathf.Clamp(measureFrames, 10, 240));
        }
    }

    internal sealed class ArtLookdevAbSweepRunner : MonoBehaviour
    {
        private const int SettleFrames = 15;
        private const int AxisCount = 4;
        private static readonly string[] AxisNames = { "fogOfWar", "postFx", "propLights", "atmosphericFog" };

        private string label;
        private int measureFrames;

        public void Configure(string labelValue, int measureFramesValue)
        {
            label = labelValue;
            measureFrames = measureFramesValue;
        }

        private IEnumerator Start()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("ArtLookdevAbSweepRunner requires play mode; aborting.");
                Destroy(gameObject);
                yield break;
            }

            // The REAL MAP renders on the controller's Start; wait for it before toggling anything.
            var controller = UnityEngine.Object.FindFirstObjectByType<ArtLookdevRealMapController>();
            for (var i = 0; i < 60 && (controller == null || controller.View == null); i++)
            {
                yield return null;
                if (controller == null)
                {
                    controller = UnityEngine.Object.FindFirstObjectByType<ArtLookdevRealMapController>();
                }
            }
            if (controller == null)
            {
                Debug.LogWarning("ArtLookdevAbSweepRunner: ArtLookdevRealMapController not found; aborting.");
                Destroy(gameObject);
                yield break;
            }
            for (var i = 0; i < 20; i++)
            {
                yield return null;
            }

            var volumes = UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsSortMode.None);
            var propLights = UnityEngine.Object.FindObjectsByType<PropLight>(FindObjectsSortMode.None)
                .Select(p => p.GetComponent<Light>())
                .Where(l => l != null)
                .ToArray();
            var camera = Camera.main;
            var cameraData = camera != null ? camera.GetUniversalAdditionalCameraData() : null;

            // Snapshot originals for restore.
            var originalFog = controller.FogEnabled;
            var originalVolumeEnabled = volumes.Select(v => v.enabled).ToArray();
            var originalPropEnabled = propLights.Select(l => l.enabled).ToArray();
            var originalPost = cameraData != null && cameraData.renderPostProcessing;
            var originalRenderFog = RenderSettings.fog;
            var visibilityMode = controller.View != null ? controller.View.VisibilityPresentationMode.ToString() : "unknown";

            void ApplyAxis(int axis, bool on)
            {
                switch (axis)
                {
                    case 0:
                        controller.SetFogEnabled(on);
                        break;
                    case 1:
                        foreach (var v in volumes)
                        {
                            v.enabled = on;
                        }
                        if (cameraData != null)
                        {
                            cameraData.renderPostProcessing = on;
                        }
                        break;
                    case 2:
                        foreach (var l in propLights)
                        {
                            l.enabled = on;
                        }
                        break;
                    case 3:
                        RenderSettings.fog = on;
                        break;
                }
            }

            var rows = new List<ComboRow>();
            for (var combo = 0; combo < (1 << AxisCount); combo++)
            {
                var state = new bool[AxisCount];
                for (var axis = 0; axis < AxisCount; axis++)
                {
                    state[axis] = (combo & (1 << axis)) != 0;
                    ApplyAxis(axis, state[axis]);
                }

                for (var s = 0; s < SettleFrames; s++)
                {
                    yield return null;
                }

                var deltas = new List<float>(measureFrames);
                int dc = 0, batches = 0, setpass = 0, tris = 0;
                for (var f = 0; f < measureFrames; f++)
                {
                    yield return null;
                    deltas.Add(Time.unscaledDeltaTime * 1000f);
                    dc = Mathf.Max(dc, UnityStats.drawCalls);
                    batches = Mathf.Max(batches, UnityStats.batches);
                    setpass = Mathf.Max(setpass, UnityStats.setPassCalls);
                    tris = Mathf.Max(tris, UnityStats.triangles);
                }

                rows.Add(new ComboRow(state, deltas.Count, Average(deltas), P95(deltas), dc, batches, setpass, tris));
            }

            // Restore originals.
            controller.SetFogEnabled(originalFog);
            for (var i = 0; i < volumes.Length; i++)
            {
                volumes[i].enabled = originalVolumeEnabled[i];
            }
            for (var i = 0; i < propLights.Length; i++)
            {
                propLights[i].enabled = originalPropEnabled[i];
            }
            if (cameraData != null)
            {
                cameraData.renderPostProcessing = originalPost;
            }
            RenderSettings.fog = originalRenderFog;

            var summaryPath = Write(rows, visibilityMode, volumes.Length, propLights.Length);
            Debug.Log(ArtLookdevAbSweep.DoneLogPrefix + summaryPath);
            Destroy(gameObject);
        }

        private string Write(IReadOnlyList<ComboRow> rows, string visibilityMode, int volumeCount, int propLightCount)
        {
            Directory.CreateDirectory(ArtLookdevAbSweep.ArtifactDirectory);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var stem = ArtLookdevAbSweep.ArtifactDirectory + "/lookdev-ab-" + stamp + "-" + Sanitize(label);
            var summaryPath = stem + "-summary.csv";
            var effectsPath = stem + "-effects.csv";

            var sb = new StringBuilder();
            sb.Append("# visibility_mode=").Append(visibilityMode)
              .Append(" volumes=").Append(volumeCount)
              .Append(" prop_lights=").Append(propLightCount)
              .Append(" measure_frames=").Append(measureFrames).Append('\n');
            sb.Append(string.Join(",", AxisNames)).Append(",frames,frame_avg_ms,frame_p95_ms,draw_calls,batches,setpass_calls,triangles\n");
            foreach (var row in rows)
            {
                sb.Append(string.Join(",", row.State.Select(b => b ? "1" : "0")))
                  .Append(',').Append(row.Frames)
                  .Append(',').Append(F(row.AvgMs))
                  .Append(',').Append(F(row.P95Ms))
                  .Append(',').Append(row.DrawCalls)
                  .Append(',').Append(row.Batches)
                  .Append(',').Append(row.SetPass)
                  .Append(',').Append(row.Triangles).Append('\n');
            }
            File.WriteAllText(summaryPath, sb.ToString());

            // Marginal main effect per axis = mean(frame_avg | axis ON) - mean(frame_avg | axis OFF).
            var fx = new StringBuilder();
            fx.Append("axis,mean_on_ms,mean_off_ms,delta_ms,mean_on_setpass,mean_off_setpass\n");
            for (var axis = 0; axis < AxisCount; axis++)
            {
                var on = rows.Where(r => r.State[axis]).ToList();
                var off = rows.Where(r => !r.State[axis]).ToList();
                var onMs = on.Average(r => r.AvgMs);
                var offMs = off.Average(r => r.AvgMs);
                var onSp = on.Average(r => (double)r.SetPass);
                var offSp = off.Average(r => (double)r.SetPass);
                fx.Append(AxisNames[axis])
                  .Append(',').Append(F(onMs))
                  .Append(',').Append(F(offMs))
                  .Append(',').Append(F(onMs - offMs))
                  .Append(',').Append(F(onSp))
                  .Append(',').Append(F(offSp)).Append('\n');
            }
            File.WriteAllText(effectsPath, fx.ToString());
            return summaryPath;
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "unnamed";
            }
            return new string(value.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-').ToArray());
        }

        private static string F(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static float Average(IReadOnlyList<float> values)
        {
            if (values.Count == 0)
            {
                return 0f;
            }
            var sum = 0f;
            for (var i = 0; i < values.Count; i++)
            {
                sum += values[i];
            }
            return sum / values.Count;
        }

        private static float P95(IReadOnlyList<float> values)
        {
            if (values.Count == 0)
            {
                return 0f;
            }
            var sorted = values.OrderBy(v => v).ToArray();
            var index = Mathf.Clamp(Mathf.CeilToInt(sorted.Length * 0.95f) - 1, 0, sorted.Length - 1);
            return sorted[index];
        }

        private readonly struct ComboRow
        {
            public ComboRow(bool[] state, int frames, float avgMs, float p95Ms, int drawCalls, int batches, int setPass, int triangles)
            {
                State = state;
                Frames = frames;
                AvgMs = avgMs;
                P95Ms = p95Ms;
                DrawCalls = drawCalls;
                Batches = batches;
                SetPass = setPass;
                Triangles = triangles;
            }

            public bool[] State { get; }
            public int Frames { get; }
            public float AvgMs { get; }
            public float P95Ms { get; }
            public int DrawCalls { get; }
            public int Batches { get; }
            public int SetPass { get; }
            public int Triangles { get; }
        }
    }
}
#endif

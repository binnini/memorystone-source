#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using Unity.PerformanceTesting;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SeoulPlayup.Combat.Tests.PlayMode
{
    /// <summary>
    /// Shared whole-scene frame-time and rendering-cost measurement harness, parameterised by
    /// scene and artifact naming so the same measurement contract (steady + move_turn +
    /// MapCombat.* marker breakdown) applies to any combat scene. UnityStats counters are
    /// editor-global (all cameras summed), so multi-camera stacks report their combined cost.
    /// </summary>
    internal static class ScenePlayModePerformanceHarness
    {
        private const int WarmupFrames = 20;
        private const int SteadyFrames = 120;
        private const int MoveWarmupIterations = 4;
        private const int MoveIterations = 24;

        private static readonly string[] MarkerNames =
        {
            "MapCombat.Pathfind",
            "MapCombat.StateMove",
            "MapCombat.RefreshVisibilityHighlights",
            "MapCombat.CommitPresentation",
            "MapCombat.RefreshHud",
            "MapCombat.Vis.ApplyVisibility",
            "MapCombat.Vis.OverlayBuild",
            "MapCombat.Vis.OverlayApply",
            "MapCombat.Vis.PhysicsSync",
            "Atlas.Vis.CellLoop",
            "Atlas.Vis.ChunkRebuild",
        };

        /// <param name="scenePath">Scene asset path loaded directly via <see cref="EditorSceneManager.LoadSceneInPlayMode"/>.</param>
        /// <param name="artifactDirectory">Directory receiving the summary and turn-breakdown CSVs.</param>
        /// <param name="csvBaseName">File-name stem, e.g. "prototype-scene" → prototype-scene-summary.csv.</param>
        /// <param name="sampleGroupPrefix">Prefix for Unity Performance Testing sample groups, e.g. "prototype_scene".</param>
        /// <param name="sceneLoadGuardFrames">Frames to wait for scene load before failing (large scenes need thousands).</param>
        public static IEnumerator RunFrameTimeAndRenderCost(
            string scenePath,
            string artifactDirectory,
            string csvBaseName,
            string sampleGroupPrefix,
            int sceneLoadGuardFrames)
        {
            var summaryCsvPath = artifactDirectory + "/" + csvBaseName + "-summary.csv";
            var breakdownCsvPath = artifactDirectory + "/" + csvBaseName + "-turn-breakdown.csv";

            var scene = EditorSceneManager.LoadSceneInPlayMode(scenePath, new LoadSceneParameters(LoadSceneMode.Single));
            Assert.That(scene.IsValid(), Is.True, $"Failed to start loading {scenePath} in play mode.");
            for (var guard = 0; !scene.isLoaded && guard < sceneLoadGuardFrames; guard++)
            {
                yield return null;
            }
            Assert.That(scene.isLoaded, Is.True, $"{scenePath} did not finish loading in play mode.");

            // Let Start()/InitializeIntegration() and first-frame presentation settle.
            for (var i = 0; i < WarmupFrames; i++)
            {
                yield return null;
            }

            var controller = UnityEngine.Object.FindFirstObjectByType<MapCombatController>();
            Assert.That(controller, Is.Not.Null, $"{scenePath} must contain a MapCombatController.");
            Assert.That(controller.LoadedMap, Is.Not.Null.And.Property("Count").GreaterThan(0),
                "Controller should have loaded the real map by play-mode start.");
            controller.ConfigurePresentationForTests(immediateSequences: true);

            Directory.CreateDirectory(artifactDirectory);
            if (!File.Exists(summaryCsvPath))
            {
                File.WriteAllText(summaryCsvPath,
                    "phase,frames,frame_avg_ms,frame_p95_ms,draw_calls,batches,setpass_calls,triangles,vertices\n");
            }

            // Steady state: scene idling with everything rendered.
            var steady = default(PhaseSample);
            var steadyRoutine = MeasurePhase(SteadyFrames, null, sample => steady = sample);
            while (steadyRoutine.MoveNext())
            {
                yield return steadyRoutine.Current;
            }
            Record(summaryCsvPath, sampleGroupPrefix, "steady", steady);

            // Warm up the interaction path (first move pays one-time costs: cold overlay/fog build,
            // pathfinder buffers) so the measured window reflects the recurring per-turn cost.
            var warmupIteration = 0;
            for (var i = 0; i < MoveWarmupIterations; i++)
            {
                DriveOneMove(controller, ref warmupIteration);
                yield return null;
            }

            // Per-turn interaction: rebuild reachable overlay + move, exercising overlay/visibility/HUD churn.
            // Profiler markers in MapCombatController.RefreshView let us attribute the per-turn cost.
            var recorders = MarkerNames.ToDictionary(
                name => name,
                name => ProfilerRecorder.StartNew(ProfilerCategory.Scripts, name, 1, ProfilerRecorderOptions.SumAllSamplesInFrame));
            var markerTotalsNs = MarkerNames.ToDictionary(name => name, _ => 0.0);

            try
            {
                var interaction = default(PhaseSample);
                var iteration = 0;
                var interactionRoutine = MeasurePhase(
                    MoveIterations,
                    _ => DriveOneMove(controller, ref iteration),
                    sample => interaction = sample,
                    () =>
                    {
                        foreach (var name in MarkerNames)
                        {
                            markerTotalsNs[name] += recorders[name].LastValue;
                        }
                    });
                while (interactionRoutine.MoveNext())
                {
                    yield return interactionRoutine.Current;
                }
                Record(summaryCsvPath, sampleGroupPrefix, "move_turn", interaction);
                RecordTurnBreakdown(breakdownCsvPath, markerTotalsNs, MoveIterations);
            }
            finally
            {
                foreach (var recorder in recorders.Values)
                {
                    recorder.Dispose();
                }
            }
        }

        private static void RecordTurnBreakdown(string breakdownCsvPath, IReadOnlyDictionary<string, double> totalsNs, int turns)
        {
            if (!File.Exists(breakdownCsvPath))
            {
                File.WriteAllText(breakdownCsvPath, "marker,turns,total_ms,per_turn_ms\n");
            }

            foreach (var name in MarkerNames)
            {
                var totalMs = totalsNs[name] / 1_000_000.0;
                var perTurnMs = turns > 0 ? totalMs / turns : 0.0;
                Measure.Custom(new SampleGroup($"turn_breakdown_{name}_per_turn", SampleUnit.Millisecond), perTurnMs);
                File.AppendAllText(breakdownCsvPath, string.Join(",", new[]
                {
                    name,
                    turns.ToString(CultureInfo.InvariantCulture),
                    totalMs.ToString("0.###", CultureInfo.InvariantCulture),
                    perTurnMs.ToString("0.###", CultureInfo.InvariantCulture),
                }) + Environment.NewLine);
            }
        }

        private static void DriveOneMove(MapCombatController controller, ref int iteration)
        {
            iteration++;

            // Build the reachable overlay (a real per-turn cost) when a Move card is available.
            controller.BeginMoveSelection();

            // Actually relocate the player every iteration so each turn re-computes visibility.
            // A debug move bypasses card/phase consumption, so BeginMoveSelection keeps succeeding
            // and we measure the recurring per-move cost rather than a single first move.
            var current = controller.State.PlayerCoord;
            foreach (var neighbour in current.NeighborsInDirectionOrder())
            {
                if (controller.TryDebugMoveTo(neighbour))
                {
                    return;
                }
            }
        }

        private static IEnumerator MeasurePhase(int frameCount, Action<int> beforeYield, Action<PhaseSample> onComplete, Action afterFrame = null)
        {
            var deltas = new List<float>(frameCount);
            var stats = new RenderStatsAccumulator();
            for (var frame = 0; frame < frameCount; frame++)
            {
                beforeYield?.Invoke(frame);
                yield return null;
                deltas.Add(Time.unscaledDeltaTime * 1000f);
                stats.Sample();
                afterFrame?.Invoke();
            }

            onComplete?.Invoke(new PhaseSample(FrameStats.From(deltas), stats));
        }

        private static void Record(string summaryCsvPath, string sampleGroupPrefix, string phase, PhaseSample sample)
        {
            Measure.Custom(new SampleGroup($"{sampleGroupPrefix}_{phase}_frame_avg", SampleUnit.Millisecond), sample.Frame.AverageMilliseconds);
            Measure.Custom(new SampleGroup($"{sampleGroupPrefix}_{phase}_frame_p95", SampleUnit.Millisecond), sample.Frame.P95Milliseconds);
            Measure.Custom(new SampleGroup($"{sampleGroupPrefix}_{phase}_draw_calls", SampleUnit.Undefined), sample.Render.DrawCalls);
            Measure.Custom(new SampleGroup($"{sampleGroupPrefix}_{phase}_batches", SampleUnit.Undefined), sample.Render.Batches);
            Measure.Custom(new SampleGroup($"{sampleGroupPrefix}_{phase}_setpass", SampleUnit.Undefined), sample.Render.SetPassCalls);
            Measure.Custom(new SampleGroup($"{sampleGroupPrefix}_{phase}_triangles", SampleUnit.Undefined), sample.Render.Triangles);

            var line = string.Join(",", new[]
            {
                phase,
                sample.Frame.SampleCount.ToString(CultureInfo.InvariantCulture),
                sample.Frame.AverageMilliseconds.ToString("0.###", CultureInfo.InvariantCulture),
                sample.Frame.P95Milliseconds.ToString("0.###", CultureInfo.InvariantCulture),
                sample.Render.DrawCalls.ToString(CultureInfo.InvariantCulture),
                sample.Render.Batches.ToString(CultureInfo.InvariantCulture),
                sample.Render.SetPassCalls.ToString(CultureInfo.InvariantCulture),
                sample.Render.Triangles.ToString(CultureInfo.InvariantCulture),
                sample.Render.Vertices.ToString(CultureInfo.InvariantCulture),
            });
            File.AppendAllText(summaryCsvPath, line + Environment.NewLine);
        }

        private readonly struct PhaseSample
        {
            public PhaseSample(FrameStats frame, RenderStatsAccumulator render)
            {
                Frame = frame;
                Render = render;
            }

            public FrameStats Frame { get; }
            public RenderStatsAccumulator Render { get; }
        }

        // UnityStats reports the last rendered frame's counters; we keep the peak across the phase
        // so a single noisy frame is visible rather than averaged away.
        private sealed class RenderStatsAccumulator
        {
            public int DrawCalls { get; private set; }
            public int Batches { get; private set; }
            public int SetPassCalls { get; private set; }
            public int Triangles { get; private set; }
            public int Vertices { get; private set; }

            public void Sample()
            {
                DrawCalls = Mathf.Max(DrawCalls, UnityStats.drawCalls);
                Batches = Mathf.Max(Batches, UnityStats.batches);
                SetPassCalls = Mathf.Max(SetPassCalls, UnityStats.setPassCalls);
                Triangles = Mathf.Max(Triangles, UnityStats.triangles);
                Vertices = Mathf.Max(Vertices, UnityStats.vertices);
            }
        }

        private readonly struct FrameStats
        {
            private FrameStats(int sampleCount, float averageMilliseconds, float p95Milliseconds)
            {
                SampleCount = sampleCount;
                AverageMilliseconds = averageMilliseconds;
                P95Milliseconds = p95Milliseconds;
            }

            public int SampleCount { get; }
            public float AverageMilliseconds { get; }
            public float P95Milliseconds { get; }

            public static FrameStats From(IReadOnlyList<float> samples)
            {
                if (samples == null || samples.Count == 0)
                {
                    return new FrameStats(0, 0f, 0f);
                }

                var sorted = samples.OrderBy(sample => sample).ToArray();
                var p95Index = Mathf.Clamp(Mathf.CeilToInt(sorted.Length * 0.95f) - 1, 0, sorted.Length - 1);
                return new FrameStats(samples.Count, samples.Average(), sorted[p95Index]);
            }
        }
    }
}
#endif

#if UNITY_EDITOR
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEditor.SceneManagement;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SeoulPlayup.Combat.Tests.PlayMode
{
    /// <summary>
    /// Scene load-time and memory-footprint measurement for the real combat scenes
    /// (MainGameplay canonical + PrototypeTest legacy sandbox). Complements
    /// <see cref="PrototypeTestScenePlayModePerformanceTests"/>, which measures frame time but
    /// not load cost. Added for P6 scene surgery (baked click-collider removal) so the
    /// before/after win in load time and memory is recorded, not just scene bytes.
    /// </summary>
    public sealed class SceneLoadMemoryPerformanceTests
    {
        private const string ArtifactDirectory = ".omx/artifacts/perf-map-source/scene-load";
        private const string CsvPath = ArtifactDirectory + "/scene-load-summary.csv";
        private const int SettleFrames = 10;

        [UnityTest]
        [Explicit("Loads the full MainGameplay scene in play mode; run intentionally to record load time + memory."), Performance]
        public IEnumerator MainGameplayScene_LoadTimeAndMemory()
        {
            return MeasureSceneLoad("Assets/Scenes/Game/MainGameplay.unity", "MainGameplay");
        }

        [UnityTest]
        [Explicit("Loads the full PrototypeTest scene in play mode; run intentionally to record load time + memory."), Performance]
        public IEnumerator PrototypeTestScene_LoadTimeAndMemory()
        {
            return MeasureSceneLoad("Assets/Scenes/Dev/PrototypeTest.unity", "PrototypeTest");
        }

        private static IEnumerator MeasureSceneLoad(string scenePath, string label)
        {
            var stopwatch = Stopwatch.StartNew();
            var scene = EditorSceneManager.LoadSceneInPlayMode(scenePath, new LoadSceneParameters(LoadSceneMode.Single));
            Assert.That(scene.IsValid(), Is.True, $"Failed to start loading {scenePath} in play mode.");
            for (var guard = 0; !scene.isLoaded && guard < 3600; guard++)
            {
                yield return null;
            }

            stopwatch.Stop();
            Assert.That(scene.isLoaded, Is.True, $"{scenePath} did not finish loading in play mode.");

            // Let Start()/first-frame presentation settle so memory reflects the playable scene.
            for (var i = 0; i < SettleFrames; i++)
            {
                yield return null;
            }

            var loadMs = stopwatch.Elapsed.TotalMilliseconds;
            var reservedMb = Profiler.GetTotalReservedMemoryLong() / (1024.0 * 1024.0);
            var allocatedMb = Profiler.GetTotalAllocatedMemoryLong() / (1024.0 * 1024.0);
            var monoMb = Profiler.GetMonoUsedSizeLong() / (1024.0 * 1024.0);

            Measure.Custom(new SampleGroup($"scene_load_{label}_ms", SampleUnit.Millisecond), loadMs);
            Measure.Custom(new SampleGroup($"scene_load_{label}_reserved_mb", SampleUnit.Megabyte), reservedMb);
            Measure.Custom(new SampleGroup($"scene_load_{label}_allocated_mb", SampleUnit.Megabyte), allocatedMb);

            Directory.CreateDirectory(ArtifactDirectory);
            if (!File.Exists(CsvPath))
            {
                File.WriteAllText(CsvPath, "scene,load_ms,reserved_mb,allocated_mb,mono_mb\n");
            }

            File.AppendAllText(CsvPath, string.Join(",", new[]
            {
                label,
                loadMs.ToString("0.#", CultureInfo.InvariantCulture),
                reservedMb.ToString("0.#", CultureInfo.InvariantCulture),
                allocatedMb.ToString("0.#", CultureInfo.InvariantCulture),
                monoMb.ToString("0.#", CultureInfo.InvariantCulture),
            }) + System.Environment.NewLine);
            UnityEngine.Debug.Log(
                $"[SceneLoadMemory] {label}: load={loadMs:0.#}ms reserved={reservedMb:0.#}MB allocated={allocatedMb:0.#}MB mono={monoMb:0.#}MB");
        }
    }
}
#endif

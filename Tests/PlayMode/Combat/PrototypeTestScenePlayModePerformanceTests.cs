#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine.TestTools;

namespace SeoulPlayup.Combat.Tests.PlayMode
{
    /// <summary>
    /// Whole-scene frame-time and rendering-cost baseline for the legacy PrototypeTest sandbox
    /// scene (real GwangjinGuSource map + Nacre HUD + overlays + VFX + audio + combat), kept for
    /// trend continuity with the pre-MainGameplay measurement history. Measurement contract lives
    /// in <see cref="ScenePlayModePerformanceHarness"/>.
    /// </summary>
    public sealed class PrototypeTestScenePlayModePerformanceTests
    {
        [UnityTest]
        [Explicit("Loads the full PrototypeTest scene in play mode; run intentionally to collect whole-scene frame-time baselines."), Performance]
        public IEnumerator PrototypeTestScene_FrameTimeAndRenderCost()
        {
            return ScenePlayModePerformanceHarness.RunFrameTimeAndRenderCost(
                scenePath: "Assets/Scenes/Dev/PrototypeTest.unity",
                artifactDirectory: ".omx/artifacts/perf-map-source/prototype-scene",
                csvBaseName: "prototype-scene",
                sampleGroupPrefix: "prototype_scene",
                sceneLoadGuardFrames: 600);
        }
    }
}
#endif

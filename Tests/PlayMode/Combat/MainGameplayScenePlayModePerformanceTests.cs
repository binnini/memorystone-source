#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine.TestTools;

namespace SeoulPlayup.Combat.Tests.PlayMode
{
    /// <summary>
    /// Whole-scene frame-time and rendering-cost baseline for the canonical MainGameplay scene
    /// (NightRig lighting + LightingMask visibility + two-camera stack + PropLight + Nacre HUD).
    /// First run establishes the budget baseline that replaces the PrototypeTest 2026-07-18
    /// numbers; recording-only, no gate. Scene-specific notes: the scene's serialized lighting
    /// gates (applyBaselineWarmLightingOnStart / applyInitialLightingPresetOnStart = off) keep the
    /// night art direction intact under direct play-mode load, LightingMask is the serialized
    /// default so measurements represent shipping visuals, and UnityStats sums both cameras of the
    /// Base Skybox + Overlay Main stack. Measurement contract lives in
    /// <see cref="ScenePlayModePerformanceHarness"/>.
    /// </summary>
    public sealed class MainGameplayScenePlayModePerformanceTests
    {
        [UnityTest]
        [Explicit("Loads the full MainGameplay scene (~50MB, load alone takes tens of seconds) in play mode; run intentionally to collect whole-scene frame-time baselines."), Performance]
        public IEnumerator MainGameplayScene_FrameTimeAndRenderCost()
        {
            return ScenePlayModePerformanceHarness.RunFrameTimeAndRenderCost(
                scenePath: "Assets/Scenes/Game/MainGameplay.unity",
                artifactDirectory: ".omx/artifacts/perf-map-source/maingameplay-scene",
                csvBaseName: "maingameplay-scene",
                sampleGroupPrefix: "maingameplay_scene",
                sceneLoadGuardFrames: 3600);
        }
    }
}
#endif

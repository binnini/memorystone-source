using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 컨트롤러의 시네마틱 공개 API가 정말 <c>CombatCinematics</c>로 위임되고, 협력자가 쓴 상태가 호스트 표면(디버그 파사드의
    /// <c>ICombatDebugHost</c> 구현·<c>LastInputMessage</c>)으로 되돌아오는지(4B-C). 협력자 단위 테스트는 스텁 호스트라
    /// 위임 한 줄이 끊겨도 모른다 — 여기서 컨트롤러 표면으로 한 번 통과시킨다. 룩 크로스페이더 경로는
    /// <c>RenderSettings</c>를 만지므로 EditMode에서 돌리지 않는다.
    /// </summary>
    public sealed class MapCombatControllerCinematicsDelegationTests
    {
        [Test]
        public void StageIntroLookPresetsRoundTripThroughTheCinematicsCollaborator()
        {
            var root = new GameObject("Cinematics Delegation Fixture");
            var day = ScriptableObject.CreateInstance<EnvironmentLookPreset>();
            var night = ScriptableObject.CreateInstance<EnvironmentLookPreset>();
            try
            {
                var controller = root.AddComponent<MapCombatController>();

                controller.SetStageIntroLookPresets(day, night);

                var debugHost = (ICombatDebugHost)controller;
                Assert.That(debugHost.StageIntroDayLookPreset, Is.SameAs(day), "공개 세터 → 협력자 필드 → 디버그 파사드 읽기가 한 상태를 봐야 한다.");
                Assert.That(debugHost.StageIntroNightLookPreset, Is.SameAs(night));
                Assert.That(debugHost.IsStageIntroActive, Is.False);
                Assert.That(controller.IsStageIntroPlaying, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(day);
                Object.DestroyImmediate(night);
            }
        }

        [Test]
        public void StageIntroReplayGuardMessageReachesTheControllerSurface()
        {
            var root = new GameObject("Cinematics Replay Guard Fixture");
            try
            {
                var controller = root.AddComponent<MapCombatController>();
                controller.ConfigureMapForTests(TestMaps.Line(4));
                controller.InitializeIntegration();

                controller.DebugReplayStageIntro();

                Assert.That(controller.LastInputMessage, Does.Contain("replay failed"),
                    "EditMode(비플레이)에서는 가드가 걸리고, 협력자가 쓴 메시지가 호스트 LastInputMessage(private set)로 돌아와야 한다.");
                Assert.That(controller.IsStageIntroPlaying, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}

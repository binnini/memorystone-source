using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 컨트롤러의 디버그 공개 API가 정말 <c>CombatDebugFacade</c>로 위임되는지(2-B). 파사드 단위 테스트는 스텁 호스트를
    /// 쓰므로 위임 한 줄이 끊겨도 모른다 — 여기서 컨트롤러 표면으로 한 번 통과시킨다. 씬 없이 <c>AddComponent</c> +
    /// <c>ConfigureMapForTests</c>로 굴리는 VisibilityRuntimeTests 선례.
    /// </summary>
    public sealed class MapCombatControllerDebugFacadeDelegationTests
    {
        [Test]
        public void DebugGrantMoneyReachesTheLiveStateThroughTheFacade()
        {
            var root = new GameObject("Debug Facade Delegation Fixture");
            try
            {
                var controller = root.AddComponent<MapCombatController>();
                controller.UseDemoCardCatalogForTests();
                controller.ConfigureMapForTests(TestMaps.Line(4));
                controller.InitializeIntegration();
                var before = controller.State.PlayerInventory.Wallet.Balance;

                var balance = controller.DebugGrantMoney(5);

                Assert.That(balance, Is.EqualTo(before + 5));
                Assert.That(controller.State.PlayerInventory.Wallet.Balance, Is.EqualTo(before + 5));
                Assert.That(controller.LastInputMessage, Does.Contain("엽전"), "파사드가 쓴 메시지가 호스트 LastInputMessage로 돌아와야 한다.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SandboxDamageThroughTheControllerHonoursTheSequenceGuard()
        {
            var root = new GameObject("Debug Facade Guard Fixture");
            try
            {
                var controller = root.AddComponent<MapCombatController>();
                controller.UseDemoCardCatalogForTests();
                controller.ConfigureMapForTests(TestMaps.Line(4));
                controller.InitializeIntegration();
                var hp = controller.State.Player.Hp;

                Assert.That(controller.DebugSandboxDamagePlayer(2), Is.EqualTo(2));
                Assert.That(controller.State.Player.Hp, Is.EqualTo(hp - 2));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}

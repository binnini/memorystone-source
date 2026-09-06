#if UNITY_EDITOR
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 컨트롤러의 보상·상점 배선이 정말 <c>CombatRewardFlow</c>로 위임되는지(4B-B). 4B-0 조사가 짚은 구멍 — 상점 모달 묶음
    /// (<c>TryTriggerShopAtPlayerCoord → OpenShop → CloseShopAndConsume</c>)은 컨트롤러 경유 테스트가 0건이라 돌연변이가
    /// 조용히 초록이었다. 여기서 「밟으면 열리고, 떠나면 소비된다」(CR-6)를 컨트롤러 표면으로 한 번 통과시킨다.
    /// 씬 없이 <c>AddComponent</c> + <c>ConfigureMapForTests</c>로 굴리는 CamperWorkshopObjectTests 선례.
    /// </summary>
    public sealed class MapCombatControllerRewardFlowDelegationTests
    {
        private const string ShopObjectId = "shop-wiring-test";

        [Test]
        public void MovingOntoTheShopOpensTheModalAndLeavingConsumesItThroughTheRewardFlow()
        {
            var host = new GameObject("Shop wiring test");
            var layers = new GameObject(GameplaySceneContract.GameplayLayerRootName, typeof(RectTransform));
            try
            {
                var controller = host.AddComponent<MapCombatController>();
                controller.UseDemoCardCatalogForTests();
                controller.ConfigurePresentationForTests(immediateSequences: true);
                controller.ConfigureMapForTests(CreateShopMap());
                controller.InitializeIntegration();

                Assert.That(controller.BeginMoveSelection(), Is.True);
                Assert.That(controller.TryMoveTo(new HexCoord(1, 0)), Is.True);

                var view = Object.FindFirstObjectByType<ShopPopupView>(FindObjectsInactive.Include);
                Assert.That(view, Is.Not.Null, "잡화점을 밟으면 상점 모달이 생성돼야 한다(TryTriggerShopAtPlayerCoord 위임).");
                Assert.That(view.IsOpen, Is.True);
                Assert.That(controller.LastInputMessage, Does.Contain("잡화점"), "협력자가 쓴 메시지가 호스트 LastInputMessage로 돌아와야 한다.");
                Assert.That(controller.State.ClaimedEventObjectIds, Does.Not.Contain(ShopObjectId),
                    "여는 것만으로는 소비되지 않는다 — 소비는 떠나는 순간이다(CR-6).");

                ClickButtonByLabel(view, "떠나기");

                Assert.That(view.IsOpen, Is.False);
                Assert.That(controller.State.ClaimedEventObjectIds, Does.Contain(ShopObjectId),
                    "구매 여부와 무관하게 떠나면 소비된다(CloseShopAndConsume → State.TryConsumeShopObject).");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(layers);
            }
        }

        private static HexMapData CreateShopMap()
        {
            var cells = new[]
            {
                new HexCellData(new HexCoord(0, 0), "plain", "plain", 1, true, false),
                new HexCellData(new HexCoord(1, 0), "plain", "plain", 1, true, false),
                new HexCellData(new HexCoord(2, 0), "plain", "plain", 1, true, false)
            };
            var objects = new[]
            {
                new HexMapObjectData(ShopObjectId, "Shop", "shop_tmp", new HexCoord(1, 0), interactable: true)
            };
            return new HexMapData(cells, objectRefs: objects);
        }

        private static void ClickButtonByLabel(ShopPopupView view, string labelFragment)
        {
            var button = view.GetComponentsInChildren<Button>(includeInactive: true)
                .FirstOrDefault(candidate =>
                    candidate.GetComponentsInChildren<TMP_Text>(includeInactive: true)
                        .Any(text => text.text.Contains(labelFragment)));
            Assert.That(button, Is.Not.Null, $"'{labelFragment}' 버튼이 있어야 한다.");
            button.onClick.Invoke();
        }
    }
}
#endif

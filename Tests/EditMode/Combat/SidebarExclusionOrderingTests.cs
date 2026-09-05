#if UNITY_EDITOR
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 사이드바를 비워 두고 뜨는 모달(상점·캠핑카)이 <b>사이드바 콜아웃 판까지 덮어선 안 된다</b>는 계약
    /// (2026-09-05 「캠핑카·잡화점에서 가방·유물이 안 열린다」).
    ///
    /// <para>출하 계층은 <c>Gameplay UI Layers</c> 아래에 <c>SidebarSystem</c>(사이드바 + 콜아웃 판 층)이
    /// 형제로 있고, 모달 루트도 같은 부모 아래 런타임 생성된다. 모달이 <c>SetAsLastSibling</c>으로 앞에
    /// 서는 순간 콜아웃 판이 뒤로 들어가므로, 모달을 연 뒤에는 사이드바 시스템이 모달보다 <b>뒤 형제
    /// (위 그리기 순서)</b>여야 한다.</para>
    /// </summary>
    public sealed class SidebarExclusionOrderingTests
    {
        private GameObject layersObject;

        [TearDown]
        public void TearDown()
        {
            if (layersObject != null)
            {
                Object.DestroyImmediate(layersObject);
                layersObject = null;
            }
        }

        [Test]
        public void ShopPopupShowKeepsSidebarSystemAboveThePopup()
        {
            var layers = CreateLayersWithSidebarSystem(out var sidebarSystem);
            var view = ShopPopupView.FindOrCreate(layers);

            view.Show(
                new[] { new ShopOfferSlotModel(ShopItemKind.CardRemoval, "removal", "카드 제거", "덱에서 카드 한 장", 60) },
                () => 500,
                _ => true,
                () => System.Array.Empty<ShopRemovalCandidate>(),
                _ => true,
                () => { });

            AssertSidebarAbove(view.transform, sidebarSystem);
        }

        [Test]
        public void ServicePopupShowKeepsSidebarSystemAboveThePopup()
        {
            var layers = CreateLayersWithSidebarSystem(out var sidebarSystem);
            var view = ServiceObjectPopupView.FindOrCreate(layers);

            view.Show(
                ServiceObjectScreen.CamperVan,
                "캠핑카",
                string.Empty,
                new[] { new ServiceObjectOptionModel("heal", "체력 회복", string.Empty, ServiceOptionFlow.Instant) },
                _ => true, _ => System.Array.Empty<ServiceCardCandidate>(), (_, _) => default, (_, _) => true, () => { });

            AssertSidebarAbove(view.transform, sidebarSystem);
        }

        [Test]
        public void RaiseSidebarAboveIgnoresSidebarOutsideThePopupParent()
        {
            layersObject = new GameObject("Gameplay UI Layers", typeof(RectTransform));
            var popup = new GameObject("Popup", typeof(RectTransform)).transform;
            popup.SetParent(layersObject.transform, false);
            var elsewhere = new GameObject("Elsewhere", typeof(RectTransform)).transform;
            elsewhere.SetParent(layersObject.transform.parent, false);
            var sidebar = new GameObject("Sidebar", typeof(RectTransform), typeof(SidebarRootMarker)).transform;
            sidebar.SetParent(elsewhere, false);
            try
            {
                Assert.That(SidebarExclusionOrdering.RaiseSidebarAbove(popup, sidebar), Is.Null,
                    "부모를 공유하지 않는 사이드바는 건드리지 않아야 한다.");
            }
            finally
            {
                Object.DestroyImmediate(elsewhere.gameObject);
            }
        }

        // ── helpers ───────────────────────────────────────────────────────────────────

        /// <summary>출하 계층의 축소판: 부모 아래 SidebarSystem(마커가 붙은 Sidebar + 콜아웃 판 층)만 둔다.</summary>
        private RectTransform CreateLayersWithSidebarSystem(out Transform sidebarSystem)
        {
            layersObject = new GameObject("Gameplay UI Layers", typeof(RectTransform));
            var layers = (RectTransform)layersObject.transform;

            sidebarSystem = new GameObject("SidebarSystem", typeof(RectTransform)).transform;
            sidebarSystem.SetParent(layers, false);
            var sidebar = new GameObject("Sidebar", typeof(RectTransform), typeof(SidebarRootMarker)).transform;
            sidebar.SetParent(sidebarSystem, false);
            var calloutLayer = new GameObject("Sidebar Callout Panel Layer", typeof(RectTransform)).transform;
            calloutLayer.SetParent(sidebarSystem, false);
            return layers;
        }

        private static void AssertSidebarAbove(Transform popup, Transform sidebarSystem)
        {
            Assert.That(popup.parent, Is.SameAs(sidebarSystem.parent), "모달과 사이드바 시스템은 같은 부모의 형제여야 한다.");
            Assert.That(sidebarSystem.GetSiblingIndex(), Is.GreaterThan(popup.GetSiblingIndex()),
                "모달을 연 뒤 사이드바 시스템(콜아웃 판 층 포함)은 모달보다 위에 그려져야 한다.");
        }
    }
}
#endif

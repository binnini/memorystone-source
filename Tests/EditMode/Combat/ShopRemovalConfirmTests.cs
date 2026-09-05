using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 잡화점 카드 제거의 <b>되돌릴 틈</b>을 잠근다(2026-08-31 개정 — 캠핑카 연마와 같은 문법).
    ///
    /// <para>
    /// 🔴 이 화면의 계약은 「몇 번 누르는가」가 아니라 <b>카드 한 장을 누른 것만으로는 사라지지
    /// 않는다</b>이다. 종전에는 「선택 → 결정」 2단계가 그것을 지켰고, 지금은 확인 화면이 지킨다 —
    /// 자리가 옮겨졌을 뿐이라 시험도 그 사실만 본다. 이 시험이 없으면 확인 화면을 지우거나
    /// 버튼을 잘못 이어도 <b>아무도 알려 주지 않는다</b>(제거는 되돌릴 수 없는 조작이다).
    /// </para>
    /// </summary>
    public sealed class ShopRemovalConfirmTests
    {
        private const string RemovalKey = "hand:0";
        private const string ConfirmPanelName = "Shop Removal Confirm Panel";

        [Test]
        public void PickingACardOpensConfirmAndRemovesNothingYet()
        {
            RunShopScenario((view, removed) =>
            {
                ClickByLabel(view, "카드 제거");
                Assert.That(FindActiveByLabel(view, "제거할 카드 선택"), Is.Not.Null, "카드 제거를 누르면 고르기 화면이 선다.");

                ClickFirstRemovalTile(view);

                Assert.That(
                    FindActiveByLabel(view, "제거 확인"), Is.Not.Null,
                    "카드를 고르면 확인 화면이 떠야 한다.");
                Assert.That(
                    removed, Is.Empty,
                    "🔴 카드를 누른 것만으로 제거되면 안 된다 — 되돌릴 틈이 사라진다.");
            });
        }

        [Test]
        public void BackFromConfirmRemovesNothing()
        {
            RunShopScenario((view, removed) =>
            {
                ClickByLabel(view, "카드 제거");
                ClickFirstRemovalTile(view);
                // 🔴 「돌아가기」는 두 곳에 있다(고르기 푸터·확인 화면) — 확인 화면 쪽을 눌러야
                //    이 시험이 재는 것(확인만 닫히고 고르기는 남는다)을 실제로 잰다.
                ClickByLabelWithin(view, ConfirmPanelName, "돌아가기");

                Assert.That(removed, Is.Empty, "돌아가기는 아무것도 지우지 않는다.");
                Assert.That(
                    FindActiveByLabel(view, "제거할 카드 선택"), Is.Not.Null,
                    "확인만 닫히고 고르기 화면은 남아야 한다 — 다시 고를 수 있어야 하기 때문이다.");
            });
        }

        [Test]
        public void ConfirmRemovesThePickedCard()
        {
            RunShopScenario((view, removed) =>
            {
                ClickByLabel(view, "카드 제거");
                ClickFirstRemovalTile(view);
                ClickByLabelWithin(view, ConfirmPanelName, "이 부적을 제거");

                Assert.That(removed, Is.EqualTo(new[] { RemovalKey }), "확정한 카드 한 장만 지워진다.");
            });
        }

        // ── helpers ───────────────────────────────────────────────────────────────────

        private static void RunShopScenario(Action<ShopPopupView, List<string>> scenario)
        {
            var layers = new GameObject("Shop test layers", typeof(RectTransform));
            try
            {
                var view = ShopPopupView.FindOrCreate((RectTransform)layers.transform);
                Assert.That(view, Is.Not.Null);

                var removed = new List<string>();
                var slots = new[]
                {
                    new ShopOfferSlotModel(ShopItemKind.CardRemoval, "removal", "카드 제거", "덱에서 카드 한 장", 60),
                };
                var candidates = new[]
                {
                    new ShopRemovalCandidate(RemovalKey, "공격의 기초 (손패)"),
                };

                view.Show(
                    slots,
                    () => 500,
                    _ => true,
                    () => candidates,
                    candidate =>
                    {
                        removed.Add(candidate.Key);
                        return true;
                    },
                    () => { });

                scenario(view, removed);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(layers);
            }
        }

        /// <summary>고르기 격자의 첫 카드 타일을 누른다(타일은 라벨이 아니라 이름으로 찾는다).</summary>
        private static void ClickFirstRemovalTile(ShopPopupView view)
        {
            var tile = view.GetComponentsInChildren<Button>(includeInactive: false)
                .FirstOrDefault(button => button.name == "Shop Removal Tile");
            Assert.That(tile, Is.Not.Null, "고르기 격자에 카드 타일이 서 있어야 한다.");
            tile.onClick.Invoke();
        }

        /// <summary>같은 라벨이 여러 화면에 있을 때, 지정한 패널 안의 것만 누른다.</summary>
        private static void ClickByLabelWithin(ShopPopupView view, string panelName, string label)
        {
            var panel = view.GetComponentsInChildren<RectTransform>(includeInactive: true)
                .FirstOrDefault(rect => rect.name == panelName);
            Assert.That(panel, Is.Not.Null, $"'{panelName}' 패널을 찾지 못했다.");
            Assert.That(panel.gameObject.activeInHierarchy, Is.True, $"'{panelName}'이 떠 있어야 한다.");

            var button = panel.GetComponentsInChildren<TMP_Text>(includeInactive: false)
                .Where(text => text.text != null && text.text.Contains(label))
                .Select(text => text.GetComponentInParent<Button>())
                .FirstOrDefault(candidate => candidate != null && candidate.isActiveAndEnabled);
            Assert.That(button, Is.Not.Null, $"'{panelName}' 안에서 '{label}' 버튼을 찾지 못했다.");
            button.onClick.Invoke();
        }

        private static void ClickByLabel(ShopPopupView view, string label)
        {
            var button = FindActiveButtonByLabel(view, label);
            Assert.That(button, Is.Not.Null, $"'{label}' 버튼을 찾지 못했다.");
            button.onClick.Invoke();
        }

        private static Button FindActiveButtonByLabel(ShopPopupView view, string label)
        {
            return view.GetComponentsInChildren<TMP_Text>(includeInactive: false)
                .Where(text => text.text != null && text.text.Contains(label))
                .Select(text => text.GetComponentInParent<Button>())
                .FirstOrDefault(button => button != null && button.isActiveAndEnabled);
        }

        private static TMP_Text FindActiveByLabel(ShopPopupView view, string label)
        {
            return view.GetComponentsInChildren<TMP_Text>(includeInactive: false)
                .FirstOrDefault(text => text.text != null && text.text.Contains(label));
        }
    }
}

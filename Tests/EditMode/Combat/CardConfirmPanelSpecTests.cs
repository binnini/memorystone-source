using System;
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
    /// 「카드 한 장을 골랐다 → 정말 할 것인가」 확인 화면 <b>두 개가 같아 보이는지</b> 잠근다
    /// (캠핑카 연마 확인 · 잡화점 제거 확인).
    ///
    /// <para>
    /// 🔴 <b>사용자가 「돌아가기 버튼 크기가 다르다」로 발견한 결함</b>이 이 시험의 존재 이유다.
    /// 두 화면은 다른 뷰 클래스에 각각 살아서, 한쪽 패널 크기를 내용에 맞게 바꾸는 것만으로
    /// 버튼이 갈렸다(폭이 패널 폭을 나눠 갖는 구조였다 — 414 ↔ 344). 눈으로만 보면
    /// <b>둘을 나란히 놓기 전까지 아무도 모른다</b>.
    /// </para>
    /// </summary>
    public sealed class CardConfirmPanelSpecTests
    {
        [Test]
        public void BothConfirmScreensUseTheSameButtonSize()
        {
            var shopButton = MeasureShopConfirmButton();
            var serviceButton = MeasureServiceConfirmButton();

            Assert.That(
                shopButton, Is.EqualTo(new Vector2(CardConfirmPanelSpec.ButtonWidth, CardConfirmPanelSpec.ButtonHeight)),
                "잡화점 제거 확인의 버튼이 공용 규격을 벗어났다.");
            Assert.That(
                serviceButton, Is.EqualTo(new Vector2(CardConfirmPanelSpec.ButtonWidth, CardConfirmPanelSpec.ButtonHeight)),
                "캠핑카 연마 확인의 버튼이 공용 규격을 벗어났다.");
            Assert.That(
                shopButton, Is.EqualTo(serviceButton),
                "두 확인 화면의 버튼은 같은 크기여야 한다 — 같은 물건으로 읽혀야 하기 때문이다.");
        }

        [Test]
        public void ConfirmButtonsStaySmallerThanTheCardTheyConfirm()
        {
            // 2026-09-01 #13: 「돌아가기 버튼이 너무 크다」. 절대 수치를 박으면 다음 조정 때 또 갈리므로
            // <b>비례</b>를 잠근다 — 확인 화면의 주인공은 고른 카드이고 버튼은 그보다 작아야 한다.
            Assert.That(CardConfirmPanelSpec.ButtonWidth, Is.LessThan(CardConfirmPanelSpec.CardHolderSize.x),
                "버튼이 카드 자리보다 넓으면 고른 카드보다 버튼이 커 보인다.");

            // 두 버튼 + 사이 여백이 가장 좁은 확인 패널(잡화점 760, 안쪽 여백 제외) 안에 들어와야 한다.
            var rowWidth = (CardConfirmPanelSpec.ButtonWidth * 2f) + CardConfirmPanelSpec.ButtonSpacing;
            var narrowestInnerWidth = 760f - CardConfirmPanelSpec.Padding.horizontal;
            Assert.That(rowWidth, Is.LessThanOrEqualTo(narrowestInnerWidth),
                "버튼 줄이 가장 좁은 확인 패널을 넘친다.");
        }

        /// <summary>
        /// 버튼 폭이 <b>패널 폭을 따라가지 않는지</b> 본다. 이것이 실제 결함의 원인이었다:
        /// 두 화면의 패널 폭이 다른 것 자체는 옳다(카드 한 장 ↔ 두 장 + 화살표).
        /// </summary>
        [Test]
        public void ConfirmButtonsDoNotStretchWithTheirPanel()
        {
            foreach (var (name, row) in new[]
                     {
                         ("잡화점 제거 확인", FindShopConfirmButtonRow()),
                         ("캠핑카 연마 확인", FindServiceConfirmButtonRow()),
                     })
            {
                Assert.That(row, Is.Not.Null, $"{name}의 버튼 줄을 찾지 못했다.");
                Assert.That(
                    row.childForceExpandWidth, Is.False,
                    $"{name}: 버튼을 늘리면 폭이 패널 폭을 따라가 다른 화면과 갈린다.");
                Assert.That(row.spacing, Is.EqualTo(CardConfirmPanelSpec.ButtonSpacing));
            }
        }

        // ── helpers ───────────────────────────────────────────────────────────────────

        private static Vector2 MeasureShopConfirmButton()
        {
            return WithShopView(view => MeasureBackButton(view, "Shop Removal Confirm Panel"));
        }

        private static Vector2 MeasureServiceConfirmButton()
        {
            return WithServiceView(view => MeasureBackButton(view, "Service Compare Panel"));
        }

        private static HorizontalLayoutGroup FindShopConfirmButtonRow()
        {
            return WithShopView(view => FindButtonRow(view, "Shop Removal Confirm Panel"));
        }

        private static HorizontalLayoutGroup FindServiceConfirmButtonRow()
        {
            return WithServiceView(view => FindButtonRow(view, "Service Compare Panel"));
        }

        /// <summary>
        /// 두 확인 패널은 <b>레이아웃을 세울 때 함께 지어진다</b>(열려면 클릭이 필요하지만 존재는 그 전부터다) —
        /// 그래서 화면을 띄우지 않고도 규격을 잴 수 있다.
        /// </summary>
        private static Vector2 MeasureBackButton(Component view, string panelName)
        {
            var panel = FindPanel(view, panelName);
            Assert.That(panel, Is.Not.Null, $"'{panelName}'을 찾지 못했다.");

            var label = panel.GetComponentsInChildren<TMP_Text>(includeInactive: true)
                .FirstOrDefault(text => text.text != null && text.text.Contains("돌아가기"));
            Assert.That(label, Is.Not.Null, $"'{panelName}'에 돌아가기 버튼이 없다.");

            // 🔴 확인 패널은 꺼진 채로 지어져 있다 — GetComponentInParent는 기본이 「켜진 것만」이라
            //    includeInactive를 켜지 않으면 null이 돌아온다(실측).
            var element = label.GetComponentInParent<LayoutElement>(includeInactive: true);
            Assert.That(element, Is.Not.Null, "버튼에 LayoutElement가 있어야 크기가 못 박힌다.");
            return new Vector2(element.preferredWidth, element.preferredHeight);
        }

        private static HorizontalLayoutGroup FindButtonRow(Component view, string panelName)
        {
            var panel = FindPanel(view, panelName);
            if (panel == null)
            {
                return null;
            }

            var label = panel.GetComponentsInChildren<TMP_Text>(includeInactive: true)
                .FirstOrDefault(text => text.text != null && text.text.Contains("돌아가기"));
            return label != null ? label.GetComponentInParent<HorizontalLayoutGroup>(includeInactive: true) : null;
        }

        private static RectTransform FindPanel(Component view, string panelName)
        {
            return view.GetComponentsInChildren<RectTransform>(includeInactive: true)
                .FirstOrDefault(rect => rect.name == panelName);
        }

        private static T WithShopView<T>(Func<ShopPopupView, T> read)
        {
            var layers = new GameObject("Shop spec layers", typeof(RectTransform));
            try
            {
                var view = ShopPopupView.FindOrCreate((RectTransform)layers.transform);
                view.Show(
                    new[] { new ShopOfferSlotModel(ShopItemKind.CardRemoval, "removal", "카드 제거", string.Empty, 60) },
                    () => 0,
                    _ => true,
                    Array.Empty<ShopRemovalCandidate>,
                    _ => true,
                    () => { });
                return read(view);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(layers);
            }
        }

        private static T WithServiceView<T>(Func<ServiceObjectPopupView, T> read)
        {
            var layers = new GameObject("Service spec layers", typeof(RectTransform));
            try
            {
                var view = ServiceObjectPopupView.FindOrCreate((RectTransform)layers.transform);
                view.Show(
                    ServiceObjectScreen.CamperVan,
                    "캠핑카",
                    string.Empty,
                    new[]
                    {
                        new ServiceObjectOptionModel("refine", "카드 연마", string.Empty, ServiceOptionFlow.CardPickCompare),
                    },
                    _ => true,
                    _ => Array.Empty<ServiceCardCandidate>(),
                    (_, _) => default,
                    (_, _) => true,
                    () => { });
                return read(view);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(layers);
            }
        }
    }
}

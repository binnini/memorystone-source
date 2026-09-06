#if UNITY_EDITOR
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Runtime.Cards;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 캠핑카·공작소 공용 서비스 모달(camper-workshop-plan.md P1). 뷰는 표시·입력만 담당하고 판단은
    /// 컨트롤러 콜백이라는 계약, 연마 2단 플로우(목록 → 전/후 비교 → 확정), Q-3(비교 취소 = 목록
    /// 복귀)를 감시한다.
    /// </summary>
    public sealed class ServiceObjectPopupViewTests
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
        public void ShowRendersOptionsWithEnabledStateAndLeaveButton()
        {
            var view = CreateView();
            view.Show(
                ServiceObjectScreen.CamperVan,
                "캠핑카",
                "서비스 하나를 고르면 이용이 끝납니다.",
                new[]
                {
                    new ServiceObjectOptionModel("heal", "체력 회복", "최대 체력의 30%를 회복합니다", ServiceOptionFlow.Instant),
                    new ServiceObjectOptionModel("refine", "카드 연마", string.Empty, ServiceOptionFlow.CardPickCompare,
                        enabled: false, disabledDetail: "연마할 수 있는 카드가 없습니다"),
                },
                _ => true, _ => System.Array.Empty<ServiceCardCandidate>(), (_, _) => default, (_, _) => true, () => { });

            Assert.That(view.IsOpen, Is.True);
            var optionButtons = FindRect(view, "Service Option List").GetComponentsInChildren<Button>(includeInactive: true);
            Assert.That(optionButtons, Has.Length.EqualTo(2));
            Assert.That(optionButtons[0].interactable, Is.True);
            Assert.That(optionButtons[1].interactable, Is.False, "비활성 선택지는 눌리지 않아야 한다.");
            Assert.That(FindButtonByLabel(view, "떠나기"), Is.Not.Null, "떠나기 버튼은 항상 있어야 한다.");
        }

        [Test]
        public void InstantOptionRoutesToControllerCallbackOnly()
        {
            var view = CreateView();
            ServiceObjectOptionModel invoked = null;
            view.Show(
                ServiceObjectScreen.CamperVan,
                "캠핑카", string.Empty,
                new[] { new ServiceObjectOptionModel("heal", "체력 회복", string.Empty, ServiceOptionFlow.Instant) },
                option =>
                {
                    invoked = option;
                    return true;
                },
                _ => System.Array.Empty<ServiceCardCandidate>(), (_, _) => default, (_, _) => true, () => { });

            ClickButtonByLabel(view, "체력 회복");
            Assert.That(invoked, Is.Not.Null, "즉시 플로우는 판단 없이 컨트롤러 콜백으로 위임돼야 한다.");
            Assert.That(invoked.Id, Is.EqualTo("heal"));
        }

        [Test]
        public void RefineFlowShowsCandidateListThenRealCardFramePairWithAccentDiff()
        {
            var state = CreateRefineState();
            var view = CreateView();
            ShowRefine(view, state);

            ClickButtonByLabel(view, "카드 연마");
            var pickPanel = FindRect(view, "Service Card Pick Panel");
            Assert.That(pickPanel.gameObject.activeSelf, Is.True);
            var candidateButtons = FindRect(view, "Content").GetComponentsInChildren<Button>(includeInactive: true);
            Assert.That(candidateButtons.Length, Is.GreaterThan(0), "CanRefine 통과 카드가 목록에 나와야 한다.");

            candidateButtons[0].onClick.Invoke();
            var comparePanel = FindRect(view, "Service Compare Panel");
            Assert.That(comparePanel.gameObject.activeSelf, Is.True);
            Assert.That(pickPanel.gameObject.activeSelf, Is.False);

            var cardViews = comparePanel.GetComponentsInChildren<DeckPileOverlayCardView>(includeInactive: true);
            Assert.That(cardViews, Has.Length.EqualTo(2), "전/후로 실제 카드 프레임 2장이 서야 한다.");

            var diffText = comparePanel.GetComponentsInChildren<TMP_Text>(includeInactive: true)
                .FirstOrDefault(text => text.text.Contains("→") && text.text.Contains("<color=#"));
            Assert.That(diffText, Is.Not.Null, "D-6: 변경 수치는 강조색으로 「전 → 후」 요약이 있어야 한다.");
        }

        [Test]
        public void CompareBackReturnsToCandidateListInsteadOfClosing()
        {
            var state = CreateRefineState();
            var view = CreateView();
            ShowRefine(view, state);
            ClickButtonByLabel(view, "카드 연마");
            FindRect(view, "Content").GetComponentsInChildren<Button>(includeInactive: true)[0].onClick.Invoke();

            ClickButtonByLabel(view, "돌아가기", requireActive: true);

            Assert.That(FindRect(view, "Service Compare Panel").gameObject.activeSelf, Is.False);
            Assert.That(FindRect(view, "Service Card Pick Panel").gameObject.activeSelf, Is.True,
                "Q-3: 비교 화면 취소는 모달 닫기가 아니라 목록 복귀다.");
            Assert.That(view.IsOpen, Is.True);
        }

        [Test]
        public void CompareConfirmExecutesRefineThroughTheCallback()
        {
            var state = CreateRefineState();
            var view = CreateView();
            ShowRefine(view, state);
            ClickButtonByLabel(view, "카드 연마");
            FindRect(view, "Content").GetComponentsInChildren<Button>(includeInactive: true)[0].onClick.Invoke();

            ClickButtonByLabel(view, "연마 확정", requireActive: true);

            Assert.That(state.PlayerDeck.ActionCards.Any(card => card.UpgradeLevel == 1), Is.True,
                "확정은 컨트롤러 콜백(TryRefineCard)을 실행해야 한다.");
            Assert.That(FindRect(view, "Service Compare Panel").gameObject.activeSelf, Is.False);
        }

        // ── helpers ───────────────────────────────────────────────────────────────────

        private ServiceObjectPopupView CreateView()
        {
            layersObject = new GameObject("Gameplay UI Layers", typeof(RectTransform));
            return ServiceObjectPopupView.FindOrCreate((RectTransform)layersObject.transform);
        }

        private static void ShowRefine(ServiceObjectPopupView view, CombatState state)
        {
            var refine = new ServiceObjectOptionModel(
                "refine", "카드 연마", "카드 한 장을 골라 한 단계 연마합니다", ServiceOptionFlow.CardPickCompare);
            view.Show(
                ServiceObjectScreen.Workshop,
                "공작소", string.Empty,
                new[] { refine },
                _ => true,
                _ => state.GetDeckListCards()
                    .Where(card => state.CanRefineCard(card.SelectionKey))
                    .Select(card => new ServiceCardCandidate(card.SelectionKey, card.Name))
                    .ToArray(),
                (option, candidate) => state.TryPreviewRefinedCardSnapshots(candidate.Key, out var before, out var after, out var reason)
                    && state.TryPreviewRefinedCard(candidate.Key, out var current, out var refined, out reason)
                    ? new ServiceCardComparePair(before, after, current, refined)
                    : default,
                (option, candidate) => state.TryRefineCard(candidate.Key, out var reason),
                () => { });
        }

        private static CombatState CreateRefineState()
        {
            // 연마 값은 카드 클래스(A01_Sweep.Upgrade)가 준다.
            var catalog = new CardCatalogDefinition(
                "service-view-test",
                "Service view test catalog",
                new[]
                {
                    new CardCatalogEntry(
                        // 클래스 없는 필러 이동 카드 — A01이 유일한 연마 후보여야 하는 픽스처. 출하 이동 카드 8장은 효과 연마 2차(DEC-2026-09-06-08)부터 전부 연마 가능이다.
                        "MOVE-FILLER", "Move", CardCategory.Movement, CardEffectType.Move,
                        1, 2, 2, "reachable_known_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(
                        CardIds.Sweep, "테스트 공격", CardCategory.Action, CardEffectType.Attack,
                        1, 1, 3, "living_monster_in_range",
                        status: CardCatalogStatus.Approved),
                });
            return new CombatState(
                CombatState.CreateDemoMap(2),
                new HexCoord(0, 0),
                new HexCoord(1, 0),
                CombatConfig.Default,
                cardCatalog: catalog);
        }

        private static RectTransform FindRect(ServiceObjectPopupView view, string name)
        {
            var rect = view.GetComponentsInChildren<RectTransform>(includeInactive: true)
                .FirstOrDefault(candidate => candidate.name == name);
            Assert.That(rect, Is.Not.Null, $"'{name}' 노드가 있어야 한다.");
            return rect;
        }

        private static Button FindButtonByLabel(ServiceObjectPopupView view, string labelFragment, bool requireActive = false)
        {
            return view.GetComponentsInChildren<Button>(includeInactive: !requireActive)
                .FirstOrDefault(button =>
                    button.GetComponentsInChildren<TMP_Text>(includeInactive: true)
                        .Any(text => text.text.Contains(labelFragment)));
        }

        private static void ClickButtonByLabel(ServiceObjectPopupView view, string labelFragment, bool requireActive = false)
        {
            var button = FindButtonByLabel(view, labelFragment, requireActive);
            Assert.That(button, Is.Not.Null, $"'{labelFragment}' 버튼이 있어야 한다.");
            button.onClick.Invoke();
        }
    }
}
#endif

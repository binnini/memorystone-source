using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Runtime.Cards;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// U02 부적 끌어오기 (docs/new-cards-plan.md §11) — the first 갈림길 card that is not an attack card, and
    /// the first use of the draw / recover choice vocabulary.
    ///
    /// Recovery is random rather than player-picked (DEC-2026-07-23-07): the 소멸 더미 overlay is a read-only
    /// viewer, so "골라서" would have meant a whole new selection UI.
    /// </summary>
    public sealed class DrawOrRecoverChoiceCardTests
    {
        [Test]
        public void DrawOptionDrawsTheAuthoredCountAndSpendsTheCard()
        {
            var state = CreateState();
            AdvanceToPlayerAction(state);
            var handBefore = state.ActionDeck.HandCount;

            Assert.That(state.TryPlayerChoiceOption(CardIds.DrawOrRecover, "draw"), Is.True, state.LastFailureReason);

            // -1 for the card itself, +2 from drawCount:2.
            Assert.That(state.ActionDeck.HandCount, Is.EqualTo(handBefore - 1 + 2));
            Assert.That(
                state.ActionDeck.Hand.Any(card => card.Id == CardIds.DrawOrRecover),
                Is.False,
                "The played card must not be one of the cards it draws back.");
        }

        [Test]
        public void RefinedDrawOptionDrawsOneMore()
        {
            // 부적 끌어오기+ (효과 연마 2차): 2장 → 3장. 리터럴 기대값(상수 되읽기 금지).
            var state = CreateState();
            Assert.That(state.TryRefineCard(CardIds.DrawOrRecover, out var reason), Is.True, reason);
            AdvanceToPlayerAction(state);
            var handBefore = state.ActionDeck.HandCount;

            Assert.That(state.TryPlayerChoiceOption(CardIds.DrawOrRecover, "draw"), Is.True, state.LastFailureReason);

            Assert.That(state.ActionDeck.HandCount, Is.EqualTo(handBefore - 1 + 3));
        }

        [Test]
        public void RecoverOptionPullsAnExiledCardBackIntoTheHand()
        {
            var state = CreateState();
            AdvanceToPlayerAction(state);
            var exiled = state.ActionDeck.Hand.First(card => card.Id.StartsWith("FILLER"));
            Assert.That(state.ActionDeck.PermanentRemoveFromHand(exiled), Is.True);
            var handBefore = state.ActionDeck.HandCount;

            Assert.That(state.TryPlayerChoiceOption(CardIds.DrawOrRecover, "recover"), Is.True, state.LastFailureReason);

            Assert.That(state.GetExilePileCards(), Is.Empty, "The recovered card leaves the 소멸 더미 for good.");
            Assert.That(state.ActionDeck.Hand, Does.Contain(exiled));
            // -1 for the played card, +1 for the recovered one.
            Assert.That(state.ActionDeck.HandCount, Is.EqualTo(handBefore - 1 + 1));
        }

        [Test]
        public void RecoverOptionOnAnEmptyExilePileStillResolves()
        {
            // D8-consistent: the option never refuses, it just returns nothing.
            var state = CreateState();
            AdvanceToPlayerAction(state);
            var handBefore = state.ActionDeck.HandCount;

            Assert.That(state.GetExilePileCards(), Is.Empty);
            Assert.That(state.TryPlayerChoiceOption(CardIds.DrawOrRecover, "recover"), Is.True, state.LastFailureReason);

            Assert.That(state.ActionDeck.HandCount, Is.EqualTo(handBefore - 1));
        }

        [Test]
        public void RecoverReachesTheMovementExilePileToo()
        {
            var state = CreateState();
            AdvanceToPlayerAction(state);
            var exiled = state.MovementDeck.Hand.First();
            Assert.That(state.MovementDeck.PermanentRemoveFromHand(exiled), Is.True);

            Assert.That(state.TryPlayerChoiceOption(CardIds.DrawOrRecover, "recover"), Is.True, state.LastFailureReason);

            Assert.That(state.MovementDeck.Hand, Does.Contain(exiled), "A recovered movement card returns to the movement hand.");
            Assert.That(state.GetExilePileCards(), Is.Empty);
        }

        [Test]
        public void TheCardIsPlayableOnlyThroughTheChoicePath()
        {
            // utility.draw_or_recover has no utility handler on purpose: playing it as a plain utility card
            // must fail rather than fall through to U01's whole-hand redraw.
            var state = CreateState();
            AdvanceToPlayerAction(state);
            var kiBefore = state.ActionCostRemaining;

            Assert.That(state.TryPlayerUtility(CardIds.DrawOrRecover), Is.False);
            Assert.That(state.ActionCostRemaining, Is.EqualTo(kiBefore), "A refused card spends nothing.");
        }

        [Test]
        public void ClickingTheCardOpensTheChoicePanelEvenThoughItIsNotAnAttackCard()
        {
            // The HUD dispatcher used to open the choice panel only for attack cards, which left a utility
            // 갈림길 card falling through to UseUtility — unplayable from the hand.
            var card = new CombatCardSnapshot(
                CardIds.DrawOrRecover,
                CombatCardKind.Utility,
                "부적 끌어오기",
                "갈림길",
                0,
                true,
                false,
                "usable",
                1,
                0,
                "Action hand",
                playMode: CardPlayMode.Choice,
                targetMode: CardTargetMode.OptionThenTarget);
            var host = new RecordingCardHudHost(CreateState());

            Assert.That(new CombatHudCardSelectionBridge().PlayCard(host, card), Is.True);

            Assert.That(host.ChoiceCardKey, Is.EqualTo(card.SelectionKey));
            Assert.That(host.UtilityCardKey, Is.Empty, "It must not fall through to the utility path.");
        }

        [Test]
        public void AnOrdinaryUtilityCardStillRoutesToTheUtilityPath()
        {
            // The choice branch is hoisted above the kind switch, so it must not swallow U01/U03.
            var card = new CombatCardSnapshot(
                CardIds.Redraw,
                CombatCardKind.Utility,
                "다시 뽑기",
                "손패를 다시 뽑습니다",
                0,
                true,
                false,
                "usable",
                1,
                0,
                "Action hand",
                playMode: CardPlayMode.Self,
                targetMode: CardTargetMode.Self);
            var host = new RecordingCardHudHost(CreateState());

            Assert.That(new CombatHudCardSelectionBridge().PlayCard(host, card), Is.True);

            Assert.That(host.UtilityCardKey, Is.EqualTo(card.SelectionKey));
            Assert.That(host.ChoiceCardKey, Is.Empty);
        }

        // --- helpers ------------------------------------------------------------------------------

        private static CombatState CreateState()
        {
            // actionHandSize 4 leaves cards in the draw pile for the draw option to pull.
            var config = TestCombatConfigs.Standard(actionBudget: 4, movementHandSize: 1, actionHandSize: 4);
            return new CombatState(
                CombatState.CreateDemoMap(3),
                new HexCoord(0, 0),
                new HexCoord(3, 0),
                config,
                cardCatalog: CreateCatalog());
        }

        // Mirrors the cards.csv row: cost 1 / self / choiceOptions draw + recover / behaviorParams drawCount:2.
        private static CardCatalogDefinition CreateCatalog()
        {
            var fillers = Enumerable.Range(1, 6).Select(index => new CardCatalogEntry(
                "FILLER" + index, "Filler " + index, CardCategory.Action, CardEffectType.Defend,
                1, 0, 1, "self", status: CardCatalogStatus.Approved));
            return new CardCatalogDefinition(
                "draw-or-recover-test",
                "Draw or recover test catalog",
                new[]
                {
                    new CardCatalogEntry(
                        CardIds.Move1Hex, "Move 1", CardCategory.Movement, CardEffectType.Move,
                        1, 1, 1, "reachable_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(
                        CardIds.DrawOrRecover, "부적 끌어오기", CardCategory.Action, CardEffectType.Utility,
                        1, 0, 0, "self",
                        playMode: CardPlayMode.Choice, targetMode: CardTargetMode.OptionThenTarget, status: CardCatalogStatus.Approved)
                }.Concat(fillers));
        }

        private static void AdvanceToPlayerAction(CombatState state)
        {
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction));
        }

        // Records which routing call the bridge made. Everything else is inert: the bridge only needs
        // State, the tutorial gate, and the play methods.
        private sealed class RecordingCardHudHost : ICombatCardHudHost
        {
            public RecordingCardHudHost(CombatState state)
            {
                State = state;
            }

            public string ChoiceCardKey { get; private set; } = string.Empty;
            public string UtilityCardKey { get; private set; } = string.Empty;

            public CombatState State { get; }
            public bool IsSequencePlaying => false;
            public string StatusText => string.Empty;
            public string MonsterStatusText => string.Empty;
            public string ObjectiveStatusText => string.Empty;
            public string EnemyIntentDisplayText => string.Empty;
            public string LastInputMessage => string.Empty;
            public string SelectedCardId => string.Empty;
            public string SelectedCardName => string.Empty;
            public string SelectedCardKey => string.Empty;
            public CombatCardKind? SelectedTargetCardKind => null;
            public string LastTargetInfoText => string.Empty;
            public string DeckStatusText => string.Empty;
            public bool IsMoveSelectionActive => false;
            public bool IsAttackSelectionActive => false;
            public bool IsScoutSelectionActive => false;
            public bool ShowOverlayDebugControls => false;
            public bool IsFogDebugVisible => false;
            public bool IsClickMoveDebugModeEnabled => false;
            public string OverlayRendererStatusText => string.Empty;
            public string TutorialHighlightCardId => string.Empty;
            public HandCardSelectionPanelModel HandCardSelectionPanel => default;
            public CombatTimingProfile TimingProfile => null;

            public void InitializeIntegration() { }
            public bool EndAction() => false;
            public void RestartDemo() { }
            public void ToggleOverlayDebugLayer(CombatOverlayDebugLayer layer) { }
            public bool IsOverlayDebugLayerVisible(CombatOverlayDebugLayer layer) => false;
            public void ToggleClickMoveDebugMode() { }
            public void ToggleFogDebugVisibility() { }
            public void RequestCardHoverAudio(string cardId) { }
            public HandCardSelectionVisualState GetHandCardSelectionVisualState(CombatCardSnapshot card) => HandCardSelectionVisualState.Normal;
            public bool ToggleHandCardSelection(string cardKey) => false;
            public bool ConfirmHandCardSelection() => false;
            public bool CancelHandCardSelection() => false;
            public void NotifyTutorialCardHover(CombatCardSnapshot card) { }
            public void NotifyTutorialCardSelected(CombatCardSnapshot card) { }

            public bool CanSelectCardForTutorial(CombatCardSnapshot card, out string reason)
            {
                reason = string.Empty;
                return true;
            }

            public void ShowTutorialBlockedFeedback(string reason) { }

            public bool PlayMovementSelf(string cardId) => false;
            public bool PlayMovementRandom(string cardId) => false;
            public bool BeginMoveSelection(string cardId) => false;

            public bool BeginChoiceCardSelection(string cardId)
            {
                ChoiceCardKey = cardId;
                return true;
            }

            public bool PlaySelfAreaAttack(string cardId) => false;
            public bool BeginAttackSelection(string cardId) => false;
            public bool UseDefense(string cardId) => false;
            public bool BeginScoutSelection(string cardId) => false;
            public bool BeginInvestigateSelection() => false;
            public bool PlayTorchAtPlayerForDev(string cardId) => false;
            public bool BeginFieldObjectSelection(string cardId) => false;

            public bool UseUtility(string cardId)
            {
                UtilityCardKey = cardId;
                return true;
            }
        }
    }
}

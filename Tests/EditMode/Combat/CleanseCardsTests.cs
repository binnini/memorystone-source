using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// The three P2 cards of docs/new-cards-plan.md — U03 정화 뽑기, D06 입원, D04 무거운 갑옷 — and the
    /// delayed self-속박 D04/D06 pay for their block with.
    ///
    /// The booking is what makes those two cards work at all: an immediately applied self-속박 could be
    /// cleansed away by playing U03 straight after, in the same action phase, for a free block.
    /// </summary>
    public sealed class CleanseCardsTests
    {
        [Test]
        public void CleanseDrawStripsDebuffsAndDrawsOnePerRemoval()
        {
            var state = CreateState();
            AdvanceToPlayerAction(state);
            Inject(state, StatusEffectKind.Poison, state.Player.Id);
            Inject(state, StatusEffectKind.Slow, state.Player.Id);
            Inject(state, StatusEffectKind.Agility, state.Player.Id);
            var handBefore = state.ActionDeck.HandCount;

            Assert.That(state.TryPlayerUtility(ApprovedCardCatalogFactory.UtilityCleanseDrawId), Is.True, state.LastFailureReason);

            Assert.That(
                state.ActiveEffects.Select(effect => effect.Kind),
                Is.EqualTo(new[] { StatusEffectKind.Agility }),
                "Only the buff survives the cleanse.");
            // -1 for the played card, +2 for the two stripped debuffs.
            Assert.That(state.ActionDeck.HandCount, Is.EqualTo(handBefore - 1 + 2));
            Assert.That(
                state.ActionDeck.Hand.Any(card => card.Id == ApprovedCardCatalogFactory.UtilityCleanseDrawId),
                Is.False,
                "The played card must leave the hand — unlike U01 there is no whole-hand redraw to sweep it away.");
        }

        [Test]
        public void CleanseDrawOnACleanPlayerIsStillPlayableAndDrawsNothing()
        {
            // D8: U03 never refuses; with nothing to remove it simply draws 0.
            var state = CreateState();
            AdvanceToPlayerAction(state);
            var handBefore = state.ActionDeck.HandCount;

            Assert.That(state.TryPlayerUtility(ApprovedCardCatalogFactory.UtilityCleanseDrawId), Is.True, state.LastFailureReason);

            Assert.That(state.ActionDeck.HandCount, Is.EqualTo(handBefore - 1));
        }

        [Test]
        public void CleanseDrawIsPlayableOutOfAStunAndRemovesIt()
        {
            // The whole point of usableWhileStunned: a cleanse locked behind 기절 could never undo one.
            var state = CreateState();
            AdvanceToPlayerAction(state);
            Inject(state, StatusEffectKind.Stun, state.Player.Id);

            Assert.That(state.TryPlayerUtility(ApprovedCardCatalogFactory.UtilityCleanseDrawId), Is.True, state.LastFailureReason);
            Assert.That(state.ActiveEffects.Any(effect => effect.Kind == StatusEffectKind.Stun), Is.False);
        }

        [Test]
        public void RedrawStillDiscardsAndRefillsBothHands()
        {
            // U01 keeps the no-handler default path; the new utility dispatcher must not have hijacked it.
            var state = CreateState();
            AdvanceToPlayerAction(state);
            var actionHand = state.ActionDeck.HandCount;
            var movementHand = state.MovementDeck.HandCount;

            Assert.That(state.TryPlayerUtility(ApprovedCardCatalogFactory.UtilityRedrawId), Is.True, state.LastFailureReason);

            Assert.That(state.ActionDeck.HandCount, Is.EqualTo(actionHand));
            Assert.That(state.MovementDeck.HandCount, Is.EqualTo(movementHand));
        }

        [Test]
        public void HospitalizationCleansesGrantsBlockAndBooksTheImmobilize()
        {
            var state = CreateState();
            AdvanceToPlayerAction(state);
            Inject(state, StatusEffectKind.Poison, state.Player.Id);

            Assert.That(state.TryPlayerDefend(ApprovedCardCatalogFactory.DefendHospitalizationId), Is.True, state.LastFailureReason);

            Assert.That(state.ActiveEffects.Any(effect => effect.Kind == StatusEffectKind.Poison), Is.False);
            Assert.That(state.Player.Block, Is.EqualTo(5), "shield=5 is the authored block.");
            Assert.That(
                state.ActiveEffects.Any(effect => effect.Kind == StatusEffectKind.Immobilize),
                Is.False,
                "D6: the 속박 is booked for next turn, not applied now.");
            Assert.That(PendingSelfImmobilizeTurns(state), Is.EqualTo(1));
        }

        [Test]
        public void HeavyArmorIsFreeGrantsBlockAndBooksTheImmobilize()
        {
            var state = CreateState();
            AdvanceToPlayerAction(state);
            var kiBefore = state.ActionCostRemaining;

            Assert.That(state.TryPlayerDefend(ApprovedCardCatalogFactory.DefendHeavyArmorId), Is.True, state.LastFailureReason);

            Assert.That(state.ActionCostRemaining, Is.EqualTo(kiBefore), "cost=0 spends no Ki.");
            Assert.That(state.Player.Block, Is.EqualTo(8));
            Assert.That(PendingSelfImmobilizeTurns(state), Is.EqualTo(1));
        }

        [Test]
        public void BookedImmobilizeLandsAtTheNextTurnStartAndCannotBeCleansedInAdvance()
        {
            // The exploit this design exists to close: block now, U03 away the penalty in the same phase.
            var state = CreateState();
            AdvanceToPlayerAction(state);

            Assert.That(state.TryPlayerDefend(ApprovedCardCatalogFactory.DefendHeavyArmorId), Is.True, state.LastFailureReason);
            Assert.That(state.TryPlayerUtility(ApprovedCardCatalogFactory.UtilityCleanseDrawId), Is.True, state.LastFailureReason);
            Assert.That(PendingSelfImmobilizeTurns(state), Is.EqualTo(1), "A booking is not an ActiveEffect, so a cleanse cannot see it.");

            AdvanceToNextOverallTurn(state);

            Assert.That(
                state.ActiveEffects.Any(effect =>
                    effect.Kind == StatusEffectKind.Immobilize && effect.SourceRef == CardEffectRefs.SelfDelayedImmobilizeApply),
                Is.True,
                "The booked 속박 must land at the start of the next turn.");
            Assert.That(PendingSelfImmobilizeTurns(state), Is.EqualTo(0), "The booking is consumed once paid.");
        }

        [Test]
        public void RepeatBookingsMergeByTakingTheLongerOne()
        {
            // D10: two heavy armours in one turn are one 속박, not two stacked ones.
            var state = CreateState();
            AdvanceToPlayerAction(state);

            Schedule(state, 1);
            Schedule(state, 3);
            Schedule(state, 2);

            Assert.That(PendingSelfImmobilizeTurns(state), Is.EqualTo(3));
        }

        [Test]
        public void BookedImmobilizeIgnoresTheControlStatusImmunityWindow()
        {
            // A 속박 that just wore off grants a short immunity so monsters cannot chain-lock the player.
            // That window must not swallow a penalty the player took on themselves, or D04 becomes free
            // block for anyone who was immobilized the turn before.
            var state = CreateState();
            AdvanceToPlayerAction(state);
            Inject(state, StatusEffectKind.Immobilize, state.Player.Id, remainingTurns: 1);
            Assert.That(state.TryPlayerDefend(ApprovedCardCatalogFactory.DefendHeavyArmorId), Is.True, state.LastFailureReason);

            AdvanceToNextOverallTurn(state);

            Assert.That(
                state.ActiveEffects.Any(effect =>
                    effect.Kind == StatusEffectKind.Immobilize && effect.SourceRef == CardEffectRefs.SelfDelayedImmobilizeApply),
                Is.True,
                "The self-inflicted 속박 must land even in the immunity window left by the expiring one.");
        }

        [Test]
        public void BookedImmobilizeSurvivesSuspendAndResume()
        {
            // Losing the booking across a suspend would let the player bank the block and save-scum the cost.
            var state = CreateState();
            AdvanceToPlayerAction(state);
            Assert.That(state.TryPlayerDefend(ApprovedCardCatalogFactory.DefendHeavyArmorId), Is.True, state.LastFailureReason);

            var data = state.CreateSuspendSnapshot();
            Assert.That(data.PendingSelfImmobilizeTurns, Is.EqualTo(1));

            var resumed = CreateState();
            resumed.RestoreFromSuspend(data);

            Assert.That(PendingSelfImmobilizeTurns(resumed), Is.EqualTo(1));
        }

        // --- helpers ------------------------------------------------------------------------------

        private static CombatState CreateState()
        {
            // actionHandSize 5 opens with the four cards under test plus one filler, leaving three fillers in
            // the draw pile — U03's "draw one per removal" needs something left to draw.
            var config = new CombatConfig(20, 10, 2, 1, 4, 4, 0, 1, 0, actionBudget: 6, movementHandSize: 1, actionHandSize: 5);
            return CombatStateFixture.Arena(3)
                .WithConfig(config)
                .WithEnemyEastAt(3)
                .WithCardCatalog(CreateCatalog())
                .Build();
        }

        // Mirrors what cards.csv authors for the three P2 rows: shield → amount, duration → booked turns.
        private static CardCatalogDefinition CreateCatalog()
        {
            var filler = Enumerable.Range(1, 4).Select(index => new CardCatalogEntry(
                "FILLER" + index, "Filler " + index, CardCategory.Action, CardEffectType.Defend,
                1, 0, 1, CardEffectRefs.DefendBlock, "self", status: CardCatalogStatus.Approved));
            return new CardCatalogDefinition(
                "cleanse-cards-test",
                "Cleanse cards test catalog",
                new[]
                {
                    new CardCatalogEntry(
                        ApprovedCardCatalogFactory.Move1HexId, "Move 1", CardCategory.Movement, CardEffectType.Move,
                        1, 1, 1, CardEffectRefs.MoveBasic, "reachable_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(
                        ApprovedCardCatalogFactory.UtilityCleanseDrawId, "정화 뽑기", CardCategory.Action, CardEffectType.Utility,
                        2, 0, 0, CardEffectRefs.UtilityCleanseDraw, "self", status: CardCatalogStatus.Approved,
                        usableWhileStunned: true),
                    new CardCatalogEntry(
                        ApprovedCardCatalogFactory.UtilityRedrawId, "다시 뽑기", CardCategory.Action, CardEffectType.Utility,
                        1, 0, 0, CardEffectRefs.UtilityRedraw, "current_action_hand_except_self", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(
                        ApprovedCardCatalogFactory.DefendHospitalizationId, "입원", CardCategory.Action, CardEffectType.Defend,
                        2, 0, 5, CardEffectRefs.DefendCleanseBlock, "self", status: CardCatalogStatus.Approved,
                        durationTurns: 1, usableWhileStunned: true),
                    new CardCatalogEntry(
                        ApprovedCardCatalogFactory.DefendHeavyArmorId, "무거운 갑옷", CardCategory.Action, CardEffectType.Defend,
                        0, 0, 8, CardEffectRefs.DefendBlockDelayedImmobilize, "self", status: CardCatalogStatus.Approved,
                        durationTurns: 1)
                }.Concat(filler));
        }

        private static void AdvanceToPlayerAction(CombatState state)
        {
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction));
        }

        private static void AdvanceToNextOverallTurn(CombatState state)
        {
            var turn = state.OverallTurnNumber;
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();
            Assert.That(state.OverallTurnNumber, Is.GreaterThan(turn));
        }

        private static void Inject(CombatState state, StatusEffectKind kind, string unitId, int remainingTurns = 2)
        {
            ActiveEffectProbe.Registry(state).Add(new ActiveEffect(EffectType.Duration, kind, unitId, remainingTurns, amount: 1, "test"));
        }

        // 2026-08-31 T6: 예약이 PendingEffects 타입으로 올라가면서 필드 직접 조회가 탐침으로 바뀌었다.
        // 단언은 그대로다 — 무엇을 재는지가 아니라 어디서 읽는지만 바뀌었다.
        private static int PendingSelfImmobilizeTurns(CombatState state)
        {
            return PendingEffectsProbe.SelfImmobilizeTurns(state);
        }

        private static void Schedule(CombatState state, int turns)
        {
            typeof(CombatState)
                .GetMethod("SchedulePlayerDelayedImmobilize", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(state, new object[] { turns });
        }
    }
}

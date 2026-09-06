using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class CardZoneDiffTests
    {
        private static Dictionary<string, CardZone> Map(params (string id, CardZone zone)[] entries)
        {
            var map = new Dictionary<string, CardZone>(StringComparer.Ordinal);
            foreach (var (id, zone) in entries)
            {
                map[id] = zone;
            }

            return map;
        }

        [Test]
        public void Compute_IdenticalSnapshots_ReturnsNoTransitions()
        {
            var previous = Map(("a", CardZone.Hand), ("b", CardZone.Draw));
            var current = Map(("a", CardZone.Hand), ("b", CardZone.Draw));

            var result = CardZoneDiff.Compute(previous, current);

            Assert.That(result.HasAny, Is.False);
            Assert.That(result.Reshuffled, Is.False);
        }

        [Test]
        public void Compute_FirstSnapshot_OnlyHandCardsDealIn_DrawPileIsSilent()
        {
            // No previous snapshot: starting deck has cards in draw + opening hand.
            var current = Map(
                ("hand-1", CardZone.Hand),
                ("hand-2", CardZone.Hand),
                ("deck-1", CardZone.Draw),
                ("deck-2", CardZone.Draw));

            var result = CardZoneDiff.Compute(null, current);

            Assert.That(result.Transitions.Select(t => t.InstanceId),
                Is.EquivalentTo(new[] { "hand-1", "hand-2" }));
            Assert.That(result.Transitions, Has.All.Matches<CardZoneTransition>(t => t.IsInitialDeal));
            Assert.That(result.Transitions, Has.All.Matches<CardZoneTransition>(t => t.PlaysAsDrawFlight));
        }

        [Test]
        public void Compute_DrawFromPileToHand_EmitsDrawFlight()
        {
            var previous = Map(("c", CardZone.Draw));
            var current = Map(("c", CardZone.Hand));

            var result = CardZoneDiff.Compute(previous, current);

            var t = result.Transitions.Single();
            Assert.That(t.InstanceId, Is.EqualTo("c"));
            Assert.That(t.IsDraw, Is.True);
            Assert.That(t.PlaysAsDrawFlight, Is.True);
            Assert.That(result.Reshuffled, Is.False);
        }

        [Test]
        public void Compute_HandToDiscard_EmitsDiscard()
        {
            var previous = Map(("c", CardZone.Hand));
            var current = Map(("c", CardZone.Discard));

            var t = CardZoneDiff.Compute(previous, current).Transitions.Single();

            Assert.That(t.IsDiscard, Is.True);
            Assert.That(t.From, Is.EqualTo(CardZone.Hand));
            Assert.That(t.To, Is.EqualTo(CardZone.Discard));
        }

        [Test]
        public void Compute_HandToDraw_EmitsReturnToDraw()
        {
            var previous = Map(("c", CardZone.Hand));
            var current = Map(("c", CardZone.Draw));

            var t = CardZoneDiff.Compute(previous, current).Transitions.Single();

            Assert.That(t.IsReturnToDraw, Is.True);
            Assert.That(t.PlaysAsDrawFlight, Is.False);
        }

        [Test]
        public void Compute_DiscardToDraw_FlagsReshuffle()
        {
            var previous = Map(("a", CardZone.Discard), ("b", CardZone.Discard));
            var current = Map(("a", CardZone.Draw), ("b", CardZone.Draw));

            var result = CardZoneDiff.Compute(previous, current);

            Assert.That(result.Reshuffled, Is.True);
            Assert.That(result.Transitions, Has.Count.EqualTo(2));
        }

        [Test]
        public void Compute_EffectMidPlay_OneDiscardAndTwoDrawsInSameBatch()
        {
            // A card was played (hand->discard) and its effect drew two cards (draw->hand).
            var previous = Map(
                ("played", CardZone.Hand),
                ("new-1", CardZone.Draw),
                ("new-2", CardZone.Draw));
            var current = Map(
                ("played", CardZone.Discard),
                ("new-1", CardZone.Hand),
                ("new-2", CardZone.Hand));

            var result = CardZoneDiff.Compute(previous, current);

            Assert.That(result.Transitions.Count(t => t.IsDiscard), Is.EqualTo(1));
            Assert.That(result.Transitions.Count(t => t.IsDraw), Is.EqualTo(2));
            Assert.That(result.Transitions.Single(t => t.IsDiscard).InstanceId, Is.EqualTo("played"));
        }

        [Test]
        public void Compute_NewCardInjectedIntoDrawPile_IsSilentUntilDrawn()
        {
            var previous = Map(("a", CardZone.Hand));
            var current = Map(("a", CardZone.Hand), ("injected", CardZone.Draw));

            var result = CardZoneDiff.Compute(previous, current);

            Assert.That(result.HasAny, Is.False, "A card appearing directly in the draw pile should not animate.");
        }

        // --- Integration with CombatState: proves the snapshot+diff catches real card movement ---

        [Test]
        public void Snapshot_OpeningDeal_AllHandCardsAreInitialDeal()
        {
            var state = CreateDuplicateDefendState(actionHandSize: 2);

            var result = CardZoneDiff.Compute(null, CombatCardZoneSnapshot.Build(state));

            // 1 movement + 2 action opening-hand cards fly in; draw-pile remainder stays silent.
            Assert.That(result.Transitions, Has.Count.EqualTo(3));
            Assert.That(result.Transitions, Has.All.Matches<CardZoneTransition>(t => t.PlaysAsDrawFlight));
        }

        [Test]
        public void Snapshot_PlayCard_DiffYieldsSingleHandToDiscardForThatInstance()
        {
            var state = CreateDuplicateDefendState(actionHandSize: 2);
            var before = CombatCardZoneSnapshot.Build(state);

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement(); // DEC-2026-07-03-02: 몬스터 이동 해석 후 PlayerAction 도달.
            var played = state.GetHandCards().First(card => card.Id == "defend-test");
            Assert.That(state.TryPlayerDefend(played.InstanceId), Is.True);

            var result = CardZoneDiff.Compute(before, CombatCardZoneSnapshot.Build(state));

            var discards = result.Transitions.Where(t => t.IsDiscard).ToArray();
            Assert.That(discards, Has.Length.EqualTo(1));
            Assert.That(discards[0].InstanceId, Is.EqualTo(played.InstanceId));
        }

        private static CombatState CreateDuplicateDefendState(int actionHandSize)
        {
            var catalog = new CardCatalogDefinition(
                "zone-diff-catalog",
                "Zone Diff Catalog",
                new[]
                {
                    new CardCatalogEntry("move-test", "Move Test", CardCategory.Movement, CardEffectType.Move, 0, 1, 1, "reachable_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry("defend-test", "Defend Test", CardCategory.Action, CardEffectType.Defend, 0, 0, 2, "self", playMode: CardPlayMode.Self, status: CardCatalogStatus.Approved)
                });
            var playerDeck = new PlayerDeckData(
                new[] { new PlayerCardInstanceData("move-instance-a", "move-test") },
                new[]
                {
                    new PlayerCardInstanceData("defend-instance-a", "defend-test"),
                    new PlayerCardInstanceData("defend-instance-b", "defend-test")
                });
            var config = TestCombatConfigs.Standard(actionBudget: 4, movementHandSize: 1, actionHandSize: actionHandSize);
            return new CombatState(CombatState.CreateDemoMap(3), new HexCoord(0, 0), new HexCoord(3, 0), config, cardCatalog: catalog, playerDeck: playerDeck);
        }
    }
}

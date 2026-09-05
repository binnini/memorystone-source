using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class CombatCardZoneStateTests
    {
        [Test]
        public void CombatStartDrawsConfiguredHandFromPlayerDeckRuntimeInstances()
        {
            var state = CreateDuplicateDefendState(actionHandSize: 2);

            Assert.That(state.MovementDeck.HandCount, Is.EqualTo(1));
            Assert.That(state.ActionDeck.HandCount, Is.EqualTo(2));
            Assert.That(state.GetHandCards(), Has.Count.EqualTo(3));
            Assert.That(state.GetCombatCards().Count(card => !card.IsDiscarded), Is.EqualTo(3));
            Assert.That(state.GetHandCards().Select(card => card.InstanceId).Distinct().Count(), Is.EqualTo(3));
            Assert.That(state.GetHandCards().Count(card => card.Id == "defend-test"), Is.EqualTo(2),
                "Duplicate card definitions must stay distinct through runtime instance ids.");

            AssertDeckZoneTotal(state, expectedMovement: 1, expectedAction: 2);
        }

        [Test]
        public void SuccessfulCardUseMovesRuntimeInstanceFromHandToDiscard()
        {
            var state = CreateDuplicateDefendState(actionHandSize: 2);
            Assert.That(state.EndAction(), Is.True, "Defend cards are playable in action phase.");
            state.ResolveMonsterMovement(); // DEC-2026-07-03-02: 몬스터 이동 해석 후 PlayerAction 도달.
            var played = state.GetHandCards().First(card => card.Id == "defend-test");

            Assert.That(state.TryPlayerDefend(played.InstanceId), Is.True);

            Assert.That(state.GetHandCards().Any(card => card.InstanceId == played.InstanceId), Is.False);
            var discarded = state.GetCombatCards().Single(card => card.InstanceId == played.InstanceId);
            Assert.That(discarded.IsDiscarded, Is.True);
            Assert.That(discarded.Pile, Does.Contain("discard"));
            Assert.That(state.ActionDeck.HandCount, Is.EqualTo(1));
            Assert.That(state.ActionDeck.DiscardCount, Is.EqualTo(1));
            AssertDeckZoneTotal(state, expectedMovement: 1, expectedAction: 2);
        }

        [Test]
        public void DrawPileShortageReshufflesDiscardIntoHandWithoutLosingInstances()
        {
            var first = new CardDefinition("first", "First", CardCategory.Action, CardEffectType.Defend, 0, 0, 1, instanceId: "first-instance");
            var second = new CardDefinition("second", "Second", CardCategory.Action, CardEffectType.Defend, 0, 0, 1, instanceId: "second-instance");
            var deck = new CardDeckState(new[] { first, second }, ReverseShuffle);
            deck.Draw(2);
            Assert.That(deck.DiscardFromHand(first), Is.True);
            Assert.That(deck.DiscardFromHand(second), Is.True);

            deck.Draw(3);

            Assert.That(deck.Hand.Select(card => card.InstanceId), Is.EquivalentTo(new[] { "first-instance", "second-instance" }));
            Assert.That(deck.DrawCount + deck.HandCount + deck.DiscardCount + deck.RemovedCount, Is.EqualTo(2));
        }

        [Test]
        public void PermanentRemovalMovesHandCardToRemovedZone()
        {
            var temporary = new CardDefinition("copy", "Copy", CardCategory.Action, CardEffectType.Attack, 0, 1, 1, instanceId: "copy-instance", isTemporary: true);
            var deck = new CardDeckState(new[] { temporary });
            deck.Draw(1);

            Assert.That(deck.PermanentRemoveFromHand(temporary), Is.True);

            Assert.That(deck.HandCount, Is.EqualTo(0));
            Assert.That(deck.RemovedCount, Is.EqualTo(1));
            Assert.That(deck.RemovedPile.Single().InstanceId, Is.EqualTo("copy-instance"));
            Assert.That(deck.DrawCount + deck.HandCount + deck.DiscardCount + deck.RemovedCount, Is.EqualTo(1));
        }

        [Test]
        public void ZoneDiffReportsHandToRemovedAsExileTransition()
        {
            var previous = new System.Collections.Generic.Dictionary<string, CardZone>
            {
                ["copy-instance"] = CardZone.Hand
            };
            var current = new System.Collections.Generic.Dictionary<string, CardZone>
            {
                ["copy-instance"] = CardZone.Removed
            };

            var diff = CardZoneDiff.Compute(previous, current);

            Assert.That(diff.HasAny, Is.True);
            var transition = diff.Transitions.Single();
            Assert.That(transition.From, Is.EqualTo(CardZone.Hand));
            Assert.That(transition.To, Is.EqualTo(CardZone.Removed));
            Assert.That(transition.IsExile, Is.True);
        }

        private static CombatState CreateDuplicateDefendState(int actionHandSize)
        {
            var catalog = new CardCatalogDefinition(
                "zone-test-catalog",
                "Zone Test Catalog",
                new[]
                {
                    new CardCatalogEntry("move-test", "Move Test", CardCategory.Movement, CardEffectType.Move, 0, 1, 1, CardEffectRefs.MoveBasic, "reachable_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry("defend-test", "Defend Test", CardCategory.Action, CardEffectType.Defend, 0, 0, 2, CardEffectRefs.DefendBlock, "self", playMode: CardPlayMode.Self, status: CardCatalogStatus.Approved)
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

        private static void AssertDeckZoneTotal(CombatState state, int expectedMovement, int expectedAction)
        {
            Assert.That(state.MovementDeck.DrawCount + state.MovementDeck.HandCount + state.MovementDeck.DiscardCount + state.MovementDeck.RemovedCount,
                Is.EqualTo(expectedMovement));
            Assert.That(state.ActionDeck.DrawCount + state.ActionDeck.HandCount + state.ActionDeck.DiscardCount + state.ActionDeck.RemovedCount,
                Is.EqualTo(expectedAction));
        }

        private static void ReverseShuffle(System.Collections.Generic.IList<CardDefinition> cards)
        {
            var left = 0;
            var right = cards.Count - 1;
            while (left < right)
            {
                (cards[left], cards[right]) = (cards[right], cards[left]);
                left++;
                right--;
            }
        }
    }
}


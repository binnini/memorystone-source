using System.Collections.Generic;
using NUnit.Framework;
using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class CardCoreDeckStateTests
    {
        [Test]
        public void DrawDiscardAndReshuffleUseDeterministicShuffle()
        {
            var first = new CardDefinition("first", "First", CardCategory.Movement, CardEffectType.Move, 0, 1, 1);
            var second = new CardDefinition("second", "Second", CardCategory.Movement, CardEffectType.Move, 0, 2, 2);
            var deck = new CardDeckState(new[] { first, second }, ReverseShuffle);

            deck.Draw(2);
            Assert.That(deck.Hand, Is.EquivalentTo(new[] { first, second }));
            Assert.That(deck.DiscardFromHand(first), Is.True);
            Assert.That(deck.DiscardFromHand(second), Is.True);

            deck.Draw(1);
            Assert.That(deck.Hand[0], Is.EqualTo(second));
            Assert.That(deck.DrawPile[0], Is.EqualTo(first));
        }

        [Test]
        public void SeparateDeckInstancesDoNotCrossContaminate()
        {
            var move = new CardDefinition("move", "Move", CardCategory.Movement, CardEffectType.Move, 0, 1, 1);
            var attack = new CardDefinition("attack", "Attack", CardCategory.Action, CardEffectType.Attack, 1, 1, 4);
            var movement = new CardDeckState(new[] { move });
            var action = new CardDeckState(new[] { attack });

            movement.Draw(1);
            movement.DiscardFromHand(move);

            Assert.That(action.DrawCount, Is.EqualTo(1));
            Assert.That(action.DiscardCount, Is.EqualTo(0));
        }

        [Test]
        public void NullInputsAreIgnoredAndRejected()
        {
            var deck = new CardDeckState(new CardDefinition[] { null });
            deck.Draw(1);
            Assert.That(deck.HandCount, Is.EqualTo(0));
            Assert.That(deck.DiscardFromHand(null), Is.False);
        }

        private static void ReverseShuffle(IList<CardDefinition> cards)
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


using System;
using System.Collections.Generic;
using System.Linq;

namespace SeoulPlayup.CardCore
{
    public class DeckState<TCard> where TCard : class
    {
        private readonly Action<IList<TCard>> shuffle;
        private readonly List<TCard> drawPile = new List<TCard>();
        private readonly List<TCard> hand = new List<TCard>();
        private readonly List<TCard> discardPile = new List<TCard>();
        private readonly List<TCard> removedPile = new List<TCard>();

        public DeckState(IEnumerable<TCard> cards, Action<IList<TCard>> shuffle = null)
        {
            this.shuffle = shuffle ?? CreateShuffle(new Random());
            if (cards != null)
            {
                drawPile.AddRange(cards.Where(card => card != null));
            }
        }

        public DeckState(
            IEnumerable<TCard> drawPile,
            IEnumerable<TCard> hand,
            IEnumerable<TCard> discardPile,
            IEnumerable<TCard> removedPile,
            Action<IList<TCard>> shuffle = null)
        {
            this.shuffle = shuffle ?? CreateShuffle(new Random());
            AddCards(this.drawPile, drawPile);
            AddCards(this.hand, hand);
            AddCards(this.discardPile, discardPile);
            AddCards(this.removedPile, removedPile);
        }

        public IReadOnlyList<TCard> DrawPile => drawPile;
        public IReadOnlyList<TCard> Hand => hand;
        public IReadOnlyList<TCard> DiscardPile => discardPile;
        public IReadOnlyList<TCard> RemovedPile => removedPile;
        public int DrawCount => drawPile.Count;
        public int HandCount => hand.Count;
        public int DiscardCount => discardPile.Count;
        public int RemovedCount => removedPile.Count;

        public void Draw(int count)
        {
            for (var i = 0; i < count; i++)
            {
                if (drawPile.Count == 0)
                {
                    ReshuffleDiscardIntoDraw();
                }

                if (drawPile.Count == 0)
                {
                    return;
                }

                var card = drawPile[0];
                drawPile.RemoveAt(0);
                hand.Add(card);
            }
        }

        public bool DiscardFromHand(TCard card)
        {
            if (card == null || !hand.Remove(card))
            {
                return false;
            }

            discardPile.Add(card);
            return true;
        }

        public void DiscardHand()
        {
            discardPile.AddRange(hand);
            hand.Clear();
        }

        /// <summary>
        /// 턴말 손패 버림의 유지(retain) 게이트(T5-2). <paramref name="keep"/>이 true인 카드는 손에
        /// 남고 나머지만 버림 더미로 간다(손 순서 보존). U01류 명시적 전체 재드로우는 유지와 무관하게
        /// <see cref="DiscardHand()"/>를 그대로 쓴다 — 유지는 "턴 종료"의 규칙이지 버림의 규칙이 아니다.
        /// </summary>
        public int DiscardHandExcept(Func<TCard, bool> keep)
        {
            if (keep == null)
            {
                var count = hand.Count;
                DiscardHand();
                return count;
            }

            var discarded = 0;
            for (var i = 0; i < hand.Count;)
            {
                var card = hand[i];
                if (keep(card))
                {
                    i++;
                    continue;
                }

                hand.RemoveAt(i);
                discardPile.Add(card);
                discarded++;
            }

            return discarded;
        }

        public int ReturnHandToDrawExcept(TCard excluded)
        {
            var returned = 0;
            for (var i = hand.Count - 1; i >= 0; i--)
            {
                var card = hand[i];
                if (ReferenceEquals(card, excluded))
                {
                    continue;
                }

                hand.RemoveAt(i);
                drawPile.Add(card);
                returned++;
            }

            shuffle(drawPile);
            return returned;
        }

        public void InjectIntoDrawPile(TCard card)
        {
            if (card == null)
            {
                return;
            }

            drawPile.Add(card);
            shuffle(drawPile);
        }

        public void InjectIntoHand(TCard card)
        {
            if (card == null)
            {
                return;
            }

            hand.Add(card);
        }

        /// <summary>
        /// Pulls a card back out of the removed (소멸) pile and into the hand — the inverse of
        /// <see cref="PermanentRemoveFromHand"/>. Returns false when the card is not in that pile, so a
        /// caller can never conjure a card the run never exiled.
        /// </summary>
        public bool TryRecoverFromRemovedPile(TCard card)
        {
            if (card == null || !removedPile.Remove(card))
            {
                return false;
            }

            hand.Add(card);
            return true;
        }

        public bool PermanentRemoveFromHand(TCard card)
        {
            if (card == null || !hand.Remove(card))
            {
                return false;
            }

            removedPile.Add(card);
            return true;
        }

        /// <summary>
        /// Permanently removes a card no matter which pile currently holds it — hand, draw, or
        /// discard. The shop's card-removal service needs this because a purchasable removal must
        /// target the whole deck, not just what happens to be in hand this turn. Cards already in
        /// the removed pile are not removable again (returns false), same as a card this deck never
        /// held.
        /// </summary>
        public bool PermanentRemoveAnywhere(TCard card)
        {
            if (card == null)
            {
                return false;
            }

            if (!hand.Remove(card) && !drawPile.Remove(card) && !discardPile.Remove(card))
            {
                return false;
            }

            removedPile.Add(card);
            return true;
        }

        /// <summary>
        /// 카드 한 장을 지금 있는 더미의 같은 자리에서 다른 카드로 바꿔친다(손패·뽑을 더미·버림
        /// 더미 대상). 연마처럼 "같은 인스턴스의 값만 달라지는" 치환용이라 순서를 보존한다 —
        /// 제거 후 재삽입이면 손패 위치가 흔들려 화면이 이유 없이 재배열된다. 소멸 더미는 대상이
        /// 아니다(소멸된 카드는 이 전투에서 다시 만질 수 없다).
        /// </summary>
        public bool TryReplaceAnywhere(TCard existing, TCard replacement)
        {
            if (existing == null || replacement == null)
            {
                return false;
            }

            return ReplaceInPile(hand, existing, replacement)
                || ReplaceInPile(drawPile, existing, replacement)
                || ReplaceInPile(discardPile, existing, replacement);
        }

        private static bool ReplaceInPile(List<TCard> pile, TCard existing, TCard replacement)
        {
            var index = pile.IndexOf(existing);
            if (index < 0)
            {
                return false;
            }

            pile[index] = replacement;
            return true;
        }

        /// <summary>
        /// Shuffles the current draw pile in place using the configured shuffle strategy.
        /// Used at combat start so the opening hand differs every play. The base constructors
        /// intentionally keep the supplied order (so saved-state restores and deterministic tests
        /// stay stable); callers that want a randomized opening deck invoke this explicitly.
        /// </summary>
        public void ShuffleDrawPile()
        {
            shuffle(drawPile);
        }

        public void ReshuffleDiscardIntoDraw()
        {
            if (discardPile.Count == 0)
            {
                return;
            }

            drawPile.AddRange(discardPile);
            discardPile.Clear();
            shuffle(drawPile);
        }

        /// <summary>
        /// 시드 셔플(seed-determinism-handoff P2). 같은 시드로 만든 셔플은 같은 호출 순서에서 같은
        /// 순서를 낸다 — 「같은 시드 → 같은 판」의 덱 축이다. 시드 파생(스트림 분리)은 호출부의
        /// 몫이다: 이 층은 순수 C#이라 <c>PlacementRandomizer.DeriveSeed</c>를 볼 수 없고, 덱마다
        /// 다른 스트림을 줘야 하는 것도 호출부만 아는 사실이다.
        /// </summary>
        public static Action<IList<TCard>> CreateSeededShuffle(int seed)
        {
            return CreateShuffle(new Random(seed));
        }

        /// <summary>
        /// 호출부가 소유한 난수원으로 셔플을 만든다(P5). 호출부가 <see cref="CountingRandom"/>을 넘기면 셔플이
        /// 몇 칸 썼는지 읽을 수 있어 저장·재개 뒤 같은 자리에서 이어 섞는다. 이 층은 여전히 시드 파생을 모른다.
        /// </summary>
        public static Action<IList<TCard>> CreateSeededShuffle(Random rng)
        {
            if (rng == null)
            {
                throw new ArgumentNullException(nameof(rng));
            }

            return CreateShuffle(rng);
        }

        // Fisher–Yates. 셔플 인자를 안 준 덱은 무시드 Random을 쓴다(테스트·랩·구세이브 폴백) —
        // 재현이 필요한 호출부는 CreateSeededShuffle을 넘긴다.
        private static Action<IList<TCard>> CreateShuffle(Random rng)
        {
            return cards =>
            {
                for (var i = cards.Count - 1; i > 0; i--)
                {
                    var j = rng.Next(i + 1);
                    (cards[i], cards[j]) = (cards[j], cards[i]);
                }
            };
        }

        private static void AddCards(ICollection<TCard> target, IEnumerable<TCard> cards)
        {
            if (cards == null)
            {
                return;
            }

            foreach (var card in cards)
            {
                if (card != null)
                {
                    target.Add(card);
                }
            }
        }
    }

    public sealed class CardDeckState : DeckState<CardDefinition>
    {
        public CardDeckState(IEnumerable<CardDefinition> cards, Action<IList<CardDefinition>> shuffle = null)
            : base(cards, shuffle)
        {
        }

        public CardDeckState(
            IEnumerable<CardDefinition> drawPile,
            IEnumerable<CardDefinition> hand,
            IEnumerable<CardDefinition> discardPile,
            IEnumerable<CardDefinition> removedPile,
            Action<IList<CardDefinition>> shuffle = null)
            : base(drawPile, hand, discardPile, removedPile, shuffle)
        {
        }
    }
}

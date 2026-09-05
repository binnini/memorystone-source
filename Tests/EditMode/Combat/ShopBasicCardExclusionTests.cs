using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 잡화점 상품에 「기본 카드」가 등장하지 않는다는 계약(2026-08-20 #8, 사용자 확정 = rarity Basic 전부).
    ///
    /// <para>🔑 조사 결과 이 규칙은 <b>이미 이중으로 막혀 있었다</b>: ①<c>shop_prices.csv</c>에 Basic
    /// 등급 행이 없고 ②<see cref="ShopPrices"/> 생성자가 Basic 가격 저작 자체를 예외로 거부한다.
    /// 그래서 이번 라운드의 몫은 새 필터가 아니라 <b>그 규칙이 조용히 풀리지 않게 하는 게이트</b>다 —
    /// 가격표에 Basic 행 하나만 생기면 규칙이 통째로 무너지기 때문이다.</para>
    /// </summary>
    public sealed class ShopBasicCardExclusionTests
    {
        [Test]
        public void ShippedShopPriceTableHasNoBasicRow()
        {
            var prices = ShopPrices.Default;
            Assert.That(prices.TryGetCardPrice(CardRarity.Basic, out _), Is.False,
                "가격표에 Basic 행이 생기면 기본 카드가 진열된다 — 가격표가 곧 허용 목록이다.");
        }

        [Test]
        public void AuthoringABasicCardPriceIsRejected()
        {
            // 저작으로 규칙을 뚫으려는 시도는 예외로 막힌다(이중 차단의 안쪽 겹).
            Assert.That(
                () => new ShopPrices(new[] { new ShopPriceEntry(ShopItemKind.Card, CardRarity.Basic, 10) }),
                Throws.ArgumentException);
        }

        [Test]
        public void RolledStockNeverContainsABasicCard()
        {
            // 후보 풀에 Basic을 섞어 넣어도 추첨 결과에 나오지 않아야 한다 — 풀이 아니라 가격표가
            // 거르므로, 풀 저작이 바뀌어도 규칙이 유지된다.
            var candidates = new List<CardRewardCandidate>
            {
                new CardRewardCandidate("M01", CardRarity.Basic),
                new CardRewardCandidate("X04", CardRarity.Basic),
                new CardRewardCandidate("A11", CardRarity.Rare),
                new CardRewardCandidate("A12", CardRarity.Legendary),
            };

            for (var seed = 0; seed < 40; seed++)
            {
                var inventory = ShopInventoryRoller.Roll(
                    candidates,
                    CardRewardRarityWeights.Default,
                    ShopPrices.Default,
                    Array.Empty<string>(),
                    new SeededRewardRandom(seed));

                Assert.That(inventory.Cards.Select(card => card.ItemId), Has.No.Member("M01"), $"seed {seed}");
                Assert.That(inventory.Cards.Select(card => card.ItemId), Has.No.Member("X04"), $"seed {seed}");
            }
        }

        private sealed class SeededRewardRandom : IRewardRandom
        {
            private readonly Random random;
            public SeededRewardRandom(int seed) { random = new Random(seed); }
            public int Next(int maxExclusive) => random.Next(maxExclusive);
        }
    }
}

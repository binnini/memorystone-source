using System;
using System.Collections.Generic;
using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>상점 재고 한 칸: 팔 것의 id와 가격. 카드면 카드 id, 유물이면 유물 id다.</summary>
    public readonly struct ShopStockItem
    {
        public ShopStockItem(string itemId, int price)
        {
            ItemId = itemId ?? string.Empty;
            Price = Math.Max(0, price);
        }

        public string ItemId { get; }
        public int Price { get; }
    }

    /// <summary>
    /// 상점 한 곳의 확정된 재고. 진입(트리거) 시 한 번 추첨해 고정된다(D-12). 상점은 1회 방문
    /// 소비라 이 재고는 팝업이 열려 있는 동안만 살아 있고 저장되지 않는다 — 닫으면 오브젝트가
    /// 소비되므로 되돌아올 재고가 없다.
    /// </summary>
    public sealed class ShopInventory
    {
        public ShopInventory(
            IReadOnlyList<ShopStockItem> cards,
            IReadOnlyList<ShopStockItem> relics,
            bool cardRemovalOffered,
            int cardRemovalPrice,
            IReadOnlyList<ShopStockItem> items = null)
        {
            Cards = cards ?? Array.Empty<ShopStockItem>();
            Relics = relics ?? Array.Empty<ShopStockItem>();
            CardRemovalOffered = cardRemovalOffered;
            CardRemovalPrice = Math.Max(0, cardRemovalPrice);
            Items = items ?? Array.Empty<ShopStockItem>();
        }

        public IReadOnlyList<ShopStockItem> Cards { get; }
        public IReadOnlyList<ShopStockItem> Relics { get; }
        public bool CardRemovalOffered { get; }
        public int CardRemovalPrice { get; }

        /// <summary>소모품 재고(T4-3, SH-11). 가격은 단일가(<see cref="ShopItemKind.Item"/>).</summary>
        public IReadOnlyList<ShopStockItem> Items { get; }

        public bool IsEmpty => Cards.Count == 0 && Relics.Count == 0 && Items.Count == 0 && !CardRemovalOffered;
    }

    /// <summary>
    /// 상점 재고를 뽑는다. 순수 C# — Unity도 표현 타입도 없다(<see cref="GachaRewardRoller"/> 선례).
    /// 반환된 재고는 항상 구매 가능해야 한다: 유물은 보유분을 추첨 전에 풀에서 빼고(D-9와 같은
    /// 원칙), 카드는 가격표에 있는 등급만 후보로 둔다 — 사고 나서 지급이 거부되는 상황을 만들지
    /// 않는 것이 이 함수의 계약이다.
    /// </summary>
    public static class ShopInventoryRoller
    {
        public const int CardSlotCount = 3;
        public const int RelicSlotCount = 2;

        /// <summary>
        /// 소모품 슬롯 수. 2026-08-31 사용자 확정으로 <b>3</b> — 잡화 격자가 3열 2줄이 되면서
        /// 윗줄이 통째로 소모품이다(<see cref="ShopPopupView"/>). 🔴 진열 배치와 이 값은 한 쌍이라
        /// 여기만 줄이면 격자 윗줄에 빈칸이 생긴다.
        /// </summary>
        public const int ItemSlotCount = 3;

        /// <summary>
        /// 한 상점의 재고를 뽑는다. <paramref name="relicPool"/>에는 <b>이미 보유한 유물을 뺀</b>
        /// 후보만 넘긴다(<see cref="GachaRewardRoller.BuildRelicPool"/> 재사용). 카드 등급 분포는
        /// 일반 보상 가중치(<paramref name="rarityWeights"/>의 normal 표)를 그대로 쓴다 — 상점이
        /// 엘리트 분포를 쓸 이유가 없고, 표를 하나 더 만들면 저작만 늘어난다.
        /// </summary>
        public static ShopInventory Roll(
            IReadOnlyList<CardRewardCandidate> cardCandidates,
            CardRewardRarityWeights rarityWeights,
            ShopPrices prices,
            IReadOnlyList<string> relicPool,
            IRewardRandom random,
            IReadOnlyList<string> itemPool = null)
        {
            if (rarityWeights == null)
            {
                throw new ArgumentNullException(nameof(rarityWeights));
            }

            if (prices == null)
            {
                throw new ArgumentNullException(nameof(prices));
            }

            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            var cards = RollCards(cardCandidates, rarityWeights, prices, random);
            var relics = RollRelics(relicPool, prices, random);
            var items = RollItems(itemPool, prices, random);
            var removalOffered = prices.TryGetPrice(ShopItemKind.CardRemoval, out var removalPrice);
            return new ShopInventory(cards, relics, removalOffered, removalOffered ? removalPrice : 0, items);
        }

        /// <summary>
        /// 이미 확정된 재고를 <b>새 가격표로 다시 매긴다</b>(DEC-2026-08-31-01 N3 — 단골 도장을
        /// 그 자리에서 사면 남은 재고가 즉시 싸진다). 뽑기는 다시 하지 않는다: 같은 물건이
        /// 같은 자리에 남고 값만 바뀐다.
        /// <para>
        /// 🔑 <see cref="Roll"/>과 <b>같은 가격 함수</b>(<see cref="RelicPrice"/>·<see cref="ItemPrice"/>)를
        /// 쓴다. 진열가와 결제가가 같은 표에서 나온다는 계약은 "재계산도 같은 코드를 탄다"로만
        /// 지켜진다 — 여기에 값을 따로 계산하는 줄을 쓰면 그 순간 둘이 갈라진다.
        /// </para>
        /// </summary>
        /// <param name="cardCandidates">
        /// 카드 등급의 출처. 재고 카드가 여기 없으면 <b>종전 가격을 유지한다</b> —
        /// 등급을 모르는 채로 값을 지어내지 않는다.
        /// </param>
        public static ShopInventory Reprice(
            ShopInventory inventory,
            ShopPrices prices,
            IReadOnlyList<CardRewardCandidate> cardCandidates = null)
        {
            if (inventory == null)
            {
                throw new ArgumentNullException(nameof(inventory));
            }

            if (prices == null)
            {
                throw new ArgumentNullException(nameof(prices));
            }

            var rarityById = new Dictionary<string, CardRarity>(StringComparer.Ordinal);
            if (cardCandidates != null)
            {
                foreach (var candidate in cardCandidates)
                {
                    if (!string.IsNullOrEmpty(candidate.CardId) && !rarityById.ContainsKey(candidate.CardId))
                    {
                        rarityById[candidate.CardId] = candidate.Rarity;
                    }
                }
            }

            var cards = new ShopStockItem[inventory.Cards.Count];
            for (var i = 0; i < cards.Length; i++)
            {
                var stock = inventory.Cards[i];
                cards[i] = rarityById.TryGetValue(stock.ItemId, out var rarity) && prices.TryGetCardPrice(rarity, out var price)
                    ? new ShopStockItem(stock.ItemId, price)
                    : stock;
            }

            var relics = new ShopStockItem[inventory.Relics.Count];
            for (var i = 0; i < relics.Length; i++)
            {
                relics[i] = new ShopStockItem(inventory.Relics[i].ItemId, RelicPrice(inventory.Relics[i].ItemId, prices));
            }

            var items = new ShopStockItem[inventory.Items.Count];
            for (var i = 0; i < items.Length; i++)
            {
                items[i] = new ShopStockItem(inventory.Items[i].ItemId, ItemPrice(inventory.Items[i].ItemId, prices));
            }

            var removalOffered = inventory.CardRemovalOffered;
            var removalPrice = inventory.CardRemovalPrice;
            if (removalOffered && prices.TryGetPrice(ShopItemKind.CardRemoval, out var repricedRemoval))
            {
                removalPrice = repricedRemoval;
            }

            return new ShopInventory(cards, relics, removalOffered, removalPrice, items);
        }

        private static IReadOnlyList<ShopStockItem> RollCards(
            IReadOnlyList<CardRewardCandidate> cardCandidates,
            CardRewardRarityWeights rarityWeights,
            ShopPrices prices,
            IRewardRandom random)
        {
            if (cardCandidates == null || cardCandidates.Count == 0)
            {
                return Array.Empty<ShopStockItem>();
            }

            // 가격표가 곧 허용 목록이다: 가격이 없는 등급은 추첨 후보에서 미리 뺀다. 등급을 뽑아놓고
            // 가격 조회가 실패하면 "살 수 없는 재고"가 진열되기 때문이다.
            var priced = new List<CardRewardCandidate>(cardCandidates.Count);
            var rarityById = new Dictionary<string, CardRarity>(StringComparer.Ordinal);
            foreach (var candidate in cardCandidates)
            {
                if (string.IsNullOrEmpty(candidate.CardId) || !prices.TryGetCardPrice(candidate.Rarity, out _))
                {
                    continue;
                }

                priced.Add(candidate);
                if (!rarityById.ContainsKey(candidate.CardId))
                {
                    rarityById[candidate.CardId] = candidate.Rarity;
                }
            }

            if (priced.Count == 0)
            {
                return Array.Empty<ShopStockItem>();
            }

            var chosenIds = CardRewardRoller.SelectRewardCardIds(
                priced,
                rarityWeights,
                eliteOnly: false,
                random,
                CardSlotCount);

            var stock = new List<ShopStockItem>(chosenIds.Count);
            foreach (var cardId in chosenIds)
            {
                if (rarityById.TryGetValue(cardId, out var rarity) && prices.TryGetCardPrice(rarity, out var price))
                {
                    stock.Add(new ShopStockItem(cardId, price));
                }
            }

            return stock;
        }

        private static IReadOnlyList<ShopStockItem> RollRelics(
            IReadOnlyList<string> relicPool,
            ShopPrices prices,
            IRewardRandom random)
        {
            if (relicPool == null || relicPool.Count == 0 || !prices.TryGetPrice(ShopItemKind.Relic, out _))
            {
                return Array.Empty<ShopStockItem>();
            }

            // 서로 다른 유물을 최대 RelicSlotCount개. 풀이 슬롯보다 작으면 모자란 채로 진열한다
            // (CR-1의 "빈 슬롯을 억지로 채우지 않는다"와 같은 원칙).
            var remaining = new List<string>(relicPool);
            var count = Math.Min(RelicSlotCount, remaining.Count);
            var stock = new List<ShopStockItem>(count);
            for (var i = 0; i < count; i++)
            {
                var index = random.Next(remaining.Count);
                var relicId = remaining[index];
                stock.Add(new ShopStockItem(relicId, RelicPrice(relicId, prices)));
                remaining.RemoveAt(index);
            }

            return stock;
        }

        /// <summary>
        /// 유물 한 행의 값(DEC-2026-08-31-01 Q1) = <b>등급 기준가 + 그 행의 priceDelta</b>.
        /// 카탈로그에 없는 id(테스트·샌드박스)는 종류의 가장 싼 티어로 떨어진다 — 출하 id는
        /// 언제나 등급이 해소되므로 그 폴백을 타지 않는다.
        /// </summary>
        private static int RelicPrice(string relicId, ShopPrices prices)
        {
            if (PlayerPermanentItemCatalog.TryGet(relicId, out var definition)
                && prices.TryGetPrice(ShopItemKind.Relic, definition.Rarity, out var tierPrice))
            {
                return Math.Max(1, tierPrice + definition.PriceDelta);
            }

            prices.TryGetPrice(ShopItemKind.Relic, out var fallback);
            return Math.Max(1, fallback);
        }

        /// <summary>소모품 한 행의 값 — 유물과 같은 규칙(등급 기준가 + priceDelta).</summary>
        private static int ItemPrice(string itemId, ShopPrices prices)
        {
            if (ConsumableItemCatalog.TryGet(itemId, out var definition)
                && prices.TryGetPrice(ShopItemKind.Item, definition.Rarity, out var tierPrice))
            {
                return Math.Max(1, tierPrice + definition.PriceDelta);
            }

            prices.TryGetPrice(ShopItemKind.Item, out var fallback);
            return Math.Max(1, fallback);
        }

        /// <summary>소모품 재고(T4-3, SH-11): 서로 다른 아이템을 최대 <see cref="ItemSlotCount"/>개.</summary>
        private static IReadOnlyList<ShopStockItem> RollItems(
            IReadOnlyList<string> itemPool,
            ShopPrices prices,
            IRewardRandom random)
        {
            if (itemPool == null || itemPool.Count == 0 || !prices.TryGetPrice(ShopItemKind.Item, out _))
            {
                return Array.Empty<ShopStockItem>();
            }

            var remaining = new List<string>(itemPool);
            var count = Math.Min(ItemSlotCount, remaining.Count);
            var stock = new List<ShopStockItem>(count);
            for (var i = 0; i < count; i++)
            {
                var index = random.Next(remaining.Count);
                var itemId = remaining[index];
                stock.Add(new ShopStockItem(itemId, ItemPrice(itemId, prices)));
                remaining.RemoveAt(index);
            }

            return stock;
        }
    }
}

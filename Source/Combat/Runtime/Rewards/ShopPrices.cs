using System;
using System.Collections.Generic;
using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 상점이 팔 수 있는 품목의 종류. <see cref="GachaRewardKind"/>와 같은 원칙으로 저주(Curse)가
    /// 없는 것은 의도다 — "저주는 상점에서 팔지 않는다"는 게임 규칙이라 저작으로 표현할 수 있는
    /// 표면을 두지 않았다.
    /// </summary>
    public enum ShopItemKind
    {
        Card,
        Relic,
        CardRemoval,

        /// <summary>소모품(가방 아이템, T4-3 — SH-11). 단일가. 직렬화 안정을 위해 끝에 추가.</summary>
        Item
    }

    /// <summary>
    /// 저작된 한 행: 품목 종류와 가격. 등급으로 갈리는 종류는 <b>카드·유물·소모품 셋</b>이다
    /// (DEC-2026-08-31-01 Q1 · -02 Q2 — 유물·소모품에도 등급을 도입했다). 등급이 없는 것은
    /// 카드 제거뿐이다. ⚠️ 유물·소모품의 등급은 <b>플레이어에게 노출되지 않는다</b> — 가격만
    /// 정하고, 어떤 UI 문자열에도 등급이 새면 안 된다.
    /// </summary>
    public readonly struct ShopPriceEntry
    {
        public ShopPriceEntry(ShopItemKind kind, CardRarity rarity, int price)
        {
            Kind = kind;
            Rarity = rarity;
            Price = Math.Max(0, price);
        }

        public ShopItemKind Kind { get; }

        /// <summary>카드·유물·소모품 행에서 의미가 있다. 카드 제거 행은 비운다.</summary>
        public CardRarity Rarity { get; }

        public int Price { get; }
    }

    /// <summary>
    /// 상점 가격표. <c>shop_prices.csv</c>가 정본이고, TextAsset이 없는 컨텍스트에서는
    /// <see cref="Default"/>가 폴백이다(<see cref="GachaRewardWeights"/>와 같은 관례).
    ///
    /// 이 표는 곧 허용 목록이다(CR-2·DEC-2026-07-28-04 선례): 가격이 없는 품목은 재고 추첨에서
    /// 아예 후보가 되지 않는다 — "추첨된 재고는 항상 구매 가능해야 한다"는 계약을 지키는 방법이
    /// 가격 조회 실패를 판매 시점이 아니라 추첨 시점에 막는 것이기 때문이다.
    /// </summary>
    public sealed class ShopPrices
    {
        private readonly ShopPriceEntry[] entries;

        /// <summary>
        /// 단골 도장(T2 페이즈 B): 전 품목 percent% 할인한 새 가격표. 재고 추첨 <b>전에</b> 적용해
        /// 진열가·결제가가 같은 표에서 나오게 한다(가격 하한 1, 0~90% 클램프).
        /// </summary>
        public ShopPrices WithDiscountPercent(int percent)
        {
            var clamped = Math.Min(90, Math.Max(0, percent));
            if (clamped == 0)
            {
                return this;
            }

            var discounted = new ShopPriceEntry[entries.Length];
            for (var i = 0; i < entries.Length; i++)
            {
                discounted[i] = new ShopPriceEntry(
                    entries[i].Kind,
                    entries[i].Rarity,
                    Math.Max(1, entries[i].Price * (100 - clamped) / 100));
            }

            return new ShopPrices(discounted);
        }

        public ShopPrices(IReadOnlyList<ShopPriceEntry> priceEntries)
        {
            if (priceEntries == null)
            {
                throw new ArgumentNullException(nameof(priceEntries));
            }

            var copy = new ShopPriceEntry[priceEntries.Count];
            var seenTiered = new HashSet<(ShopItemKind, CardRarity)>();
            var seenSingletonKinds = new HashSet<ShopItemKind>();
            for (var i = 0; i < priceEntries.Count; i++)
            {
                var entry = priceEntries[i];
                if (IsTiered(entry.Kind))
                {
                    // 기본 등급은 보상에서도 상점에서도 나오지 않는다 — CR-2와 같은 게임 규칙이라
                    // 저작이 아니라 코드가 막는다(이중 차단의 안쪽 겹). 유물·소모품에는 애초에
                    // Basic 티어를 두지 않았으므로 같은 문장이 셋 모두를 지킨다.
                    if (entry.Rarity == CardRarity.Basic)
                    {
                        throw new ArgumentException("Basic rarity cannot be priced; Basic tier never appears in the shop.", nameof(priceEntries));
                    }

                    if (!seenTiered.Add((entry.Kind, entry.Rarity)))
                    {
                        throw new ArgumentException($"Duplicate {entry.Kind} price for rarity '{entry.Rarity}'.", nameof(priceEntries));
                    }
                }
                else if (!seenSingletonKinds.Add(entry.Kind))
                {
                    throw new ArgumentException($"Duplicate shop price for '{entry.Kind}'.", nameof(priceEntries));
                }

                copy[i] = entry;
            }

            entries = copy;
        }

        /// <summary>코드 폴백. <c>shop_prices.csv</c>의 출하 값과 일치해야 한다(테스트가 감시).</summary>
        public static ShopPrices Default { get; } = new ShopPrices(
            new[]
            {
                new ShopPriceEntry(ShopItemKind.Card, CardRarity.Rare, 50),
                new ShopPriceEntry(ShopItemKind.Card, CardRarity.Epic, 80),
                new ShopPriceEntry(ShopItemKind.Card, CardRarity.Legendary, 120),
                new ShopPriceEntry(ShopItemKind.Relic, CardRarity.Rare, 70),
                new ShopPriceEntry(ShopItemKind.Relic, CardRarity.Epic, 100),
                new ShopPriceEntry(ShopItemKind.Relic, CardRarity.Legendary, 140),
                new ShopPriceEntry(ShopItemKind.Item, CardRarity.Rare, 30),
                new ShopPriceEntry(ShopItemKind.Item, CardRarity.Epic, 45),
                new ShopPriceEntry(ShopItemKind.Item, CardRarity.Legendary, 60),
                new ShopPriceEntry(ShopItemKind.CardRemoval, default, 60)
            });

        /// <summary>등급별 기준가를 갖는 품목 종류. 카드 제거만 단일가다.</summary>
        public static bool IsTiered(ShopItemKind kind) =>
            kind == ShopItemKind.Card || kind == ShopItemKind.Relic || kind == ShopItemKind.Item;

        public IReadOnlyList<ShopPriceEntry> Entries => entries;

        public bool TryGetCardPrice(CardRarity rarity, out int price) =>
            TryGetPrice(ShopItemKind.Card, rarity, out price);

        /// <summary>등급별 기준가 조회(카드·유물·소모품). 유물·소모품은 여기에 행별 priceDelta가 더해진다.</summary>
        public bool TryGetPrice(ShopItemKind kind, CardRarity rarity, out int price)
        {
            for (var i = 0; i < entries.Length; i++)
            {
                if (entries[i].Kind == kind && entries[i].Rarity == rarity)
                {
                    price = entries[i].Price;
                    return true;
                }
            }

            price = 0;
            return false;
        }

        /// <summary>
        /// 종류 단위 가격. 단일가 품목(카드 제거)의 정규 조회이고, 등급형 품목에는 <b>가장 싼 티어</b>를
        /// 돌려준다 — 카탈로그에 없는 id(테스트·샌드박스)로 재고를 굴릴 때의 폴백이며,
        /// 출하 id는 언제나 등급이 해소되므로 이 경로를 타지 않는다.
        /// </summary>
        public bool TryGetPrice(ShopItemKind kind, out int price)
        {
            var found = false;
            price = 0;
            for (var i = 0; i < entries.Length; i++)
            {
                if (entries[i].Kind != kind)
                {
                    continue;
                }

                if (!found || entries[i].Price < price)
                {
                    price = entries[i].Price;
                    found = true;
                }
            }

            return found;
        }
    }
}

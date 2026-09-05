using System;
using System.Collections.Generic;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>대상 지정 방식(T4). 무대상 8종은 None, 화염 구슬·결계 구슬은 Tile, 혼미 구슬는 Enemy.</summary>
    public enum ConsumableItemTargeting
    {
        None,
        Enemy,
        Tile,
    }

    /// <summary>
    /// 소모품 1종의 저작 정의(T4-1). 효과의 실체는 <c>effectRef</c>로 코드 핸들러
    /// (<c>CombatState.TryUseBagItem</c>)에 배선되고, 수치(amount·durationTurns·radius)는 전부
    /// CSV가 정본이다 — 카드의 behaviorId 규약과 같은 결.
    /// </summary>
    public sealed class ConsumableItemDefinition
    {
        public ConsumableItemDefinition(
            string id,
            string displayName,
            string description,
            string category,
            ConsumableItemTargeting targeting,
            string effectRef,
            int amount,
            int durationTurns,
            int radius,
            SeoulPlayup.CardCore.CardRarity rarity = SeoulPlayup.CardCore.CardRarity.Rare,
            int priceDelta = 0,
            string iconId = null)
        {
            Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Consumable item id is required.", nameof(id)) : id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Id : displayName;
            Description = description ?? string.Empty;
            Category = category ?? string.Empty;
            Targeting = targeting;
            EffectRef = string.IsNullOrWhiteSpace(effectRef) ? throw new ArgumentException("Consumable item effectRef is required.", nameof(effectRef)) : effectRef;
            Amount = Math.Max(0, amount);
            DurationTurns = Math.Max(0, durationTurns);
            Radius = Math.Max(0, radius);
            Rarity = rarity;
            PriceDelta = priceDelta;
            IconId = iconId ?? string.Empty;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public string Category { get; }
        public ConsumableItemTargeting Targeting { get; }
        public string EffectRef { get; }
        public int Amount { get; }
        public int DurationTurns { get; }
        public int Radius { get; }

        /// <summary>
        /// 등급(DEC-2026-08-31-02 Q2). 🔴<b>플레이어에게 노출되지 않는다</b> — 유물과 같은 규칙으로,
        /// 하는 일은 상점 기준가를 고르는 것뿐이다.
        /// </summary>
        public SeoulPlayup.CardCore.CardRarity Rarity { get; }

        /// <summary>등급 기준가에 더하는 행별 변주(−5 … +5). 실제 가격대 25~65.</summary>
        public int PriceDelta { get; }

        /// <summary>아이콘 스프라이트 키(<c>consumable_icon_*</c>). 비면 아직 아트가 없다는 뜻이다.</summary>
        public string IconId { get; }
    }

    public sealed class ConsumableItemCatalogDefinition
    {
        private readonly Dictionary<string, ConsumableItemDefinition> byId =
            new Dictionary<string, ConsumableItemDefinition>(StringComparer.Ordinal);
        private readonly List<ConsumableItemDefinition> entries = new List<ConsumableItemDefinition>();

        public ConsumableItemCatalogDefinition(IEnumerable<ConsumableItemDefinition> items)
        {
            foreach (var item in items ?? Array.Empty<ConsumableItemDefinition>())
            {
                if (item == null)
                {
                    continue;
                }

                byId[item.Id] = item;
                entries.Add(item);
            }
        }

        public IReadOnlyList<ConsumableItemDefinition> Entries => entries;

        public bool TryGet(string id, out ConsumableItemDefinition definition)
        {
            definition = null;
            return !string.IsNullOrWhiteSpace(id) && byId.TryGetValue(id, out definition);
        }
    }

    /// <summary>
    /// 소모품 정의의 조회 façade — <see cref="PlayerPermanentItemCatalog"/>(유물)와 같은 패턴.
    /// 에디터/테스트는 소스 CSV를 지연 로딩하고, 플레이어 빌드는 부팅 시 <see cref="Register"/>로
    /// TextAsset 파싱 카탈로그를 주입한다(CSV만 고치면 빌드에 안 붙는다 — 씬 직렬화 바인딩이 경로).
    /// </summary>
    public static class ConsumableItemCatalog
    {
        private static ConsumableItemCatalogDefinition catalog;

        public static void Register(ConsumableItemCatalogDefinition itemCatalog)
        {
            catalog = itemCatalog ?? throw new ArgumentNullException(nameof(itemCatalog));
        }

        public static System.Collections.Generic.IReadOnlyList<ConsumableItemDefinition> Definitions => EnsureLoaded().Entries;

        public static bool TryGet(string id, out ConsumableItemDefinition definition)
        {
            return EnsureLoaded().TryGet(id, out definition);
        }

        private static ConsumableItemCatalogDefinition EnsureLoaded()
        {
            return catalog ??= LoadFromSource();
        }

        private static ConsumableItemCatalogDefinition LoadFromSource()
        {
            try
            {
                if (System.IO.File.Exists(CombatCsvPaths.ConsumableItemsCsv))
                {
                    return ConsumableItemCatalogCsv.ConvertFile(CombatCsvPaths.ConsumableItemsCsv);
                }
            }
            catch
            {
                // 소스 CSV가 없는 컨텍스트(Register 이전의 플레이어 빌드)는 빈 카탈로그로 폴백한다 —
                // 플레이어 빌드는 부팅 Register가 정본 경로다(PlayerPermanentItemCatalog와 동일 계약).
            }

            return new ConsumableItemCatalogDefinition(Array.Empty<ConsumableItemDefinition>());
        }
    }
}

using System;
using System.Collections.Generic;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>뽑기 한 번의 결과. 금액은 <see cref="GachaRewardKind.Money"/>일 때만 의미가 있다.</summary>
    public readonly struct GachaOutcome
    {
        public GachaOutcome(GachaRewardKind kind, int amount = 0, string relicId = "", string itemId = "")
        {
            Kind = kind;
            Amount = Math.Max(0, amount);
            RelicId = relicId ?? string.Empty;
            ItemId = itemId ?? string.Empty;
        }

        public GachaRewardKind Kind { get; }
        public int Amount { get; }
        public string RelicId { get; }

        /// <summary>소모품 결과(T4-3)의 아이템 id. <see cref="GachaRewardKind.Item"/>일 때만 의미가 있다.</summary>
        public string ItemId { get; }
    }

    /// <summary>
    /// 인형뽑기 결과를 뽑는다. 순수 C# — Unity도 표현 타입도 없다(<see cref="CardRewardRoller"/> 선례).
    /// 연출은 이 결과를 받아 재생만 하고, 무엇이 나올지는 여기서 이미 정해진다.
    /// </summary>
    public static class GachaRewardRoller
    {
        /// <summary>
        /// 한 번 뽑는다. <paramref name="relicPool"/>에는 <b>이미 보유한 유물을 뺀</b> 후보만 넘긴다 —
        /// 중복 유물은 추첨 풀에서 제외한다는 결정(D-9) 때문이다. 풀이 비면 유물 항목 자체가 후보에서
        /// 빠지고 남은 종류로 가중치가 재분배된다(D의 "유물 풀 고갈 시 등장하지 않음").
        ///
        /// 연출을 다 보고 나서 지급이 거부되는 상황을 만들지 않는 것이 이 함수의 계약이다: 여기서
        /// 반환된 결과는 항상 지급 가능해야 한다.
        /// </summary>
        public static GachaOutcome Roll(
            GachaRewardWeights weights,
            IReadOnlyList<string> relicPool,
            IRewardRandom random,
            IReadOnlyList<string> itemPool = null)
        {
            if (weights == null)
            {
                throw new ArgumentNullException(nameof(weights));
            }

            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            var relicAvailable = relicPool != null && relicPool.Count > 0;
            // 소모품(T4-3)도 유물과 같은 규칙: 풀이 비면 항목이 빠지고 나머지로 재분배된다.
            // 가방 만원은 여기서 보지 않는다 — 그건 지급 지점의 돈 폴백 규칙이다(항상 지급 가능 계약 유지).
            var itemAvailable = itemPool != null && itemPool.Count > 0;

            var available = new List<GachaRewardWeight>(weights.Entries.Count);
            foreach (var entry in weights.Entries)
            {
                if (entry.Kind == GachaRewardKind.Relic && !relicAvailable)
                {
                    continue;
                }

                if (entry.Kind == GachaRewardKind.Item && !itemAvailable)
                {
                    continue;
                }

                available.Add(entry);
            }

            // 저작이 전부 비었거나 가중치가 전부 0인 극단은 카드팩으로 떨어뜨린다 — 뽑기를 눌렀는데
            // 아무 일도 안 일어나는 것이 최악이고, 카드팩은 유물과 달리 풀이 마르지 않는다.
            if (available.Count == 0)
            {
                return new GachaOutcome(GachaRewardKind.CardPack);
            }

            var picked = PickWeighted(available, random);
            switch (picked.Kind)
            {
                case GachaRewardKind.Money:
                    return new GachaOutcome(GachaRewardKind.Money, RollAmount(picked, random));
                case GachaRewardKind.Relic:
                    return new GachaOutcome(GachaRewardKind.Relic, relicId: relicPool[random.Next(relicPool.Count)]);
                case GachaRewardKind.Item:
                    // 12종 균등(2026-08-07 사용자 확정) — 아이템별 가중치는 필요해지면 CSV 컬럼으로 승격.
                    return new GachaOutcome(GachaRewardKind.Item, itemId: itemPool[random.Next(itemPool.Count)]);
                default:
                    return new GachaOutcome(GachaRewardKind.CardPack);
            }
        }

        /// <summary>
        /// 아직 보유하지 않은 유물만 남긴 추첨 풀. 저주는 <paramref name="definitions"/>에 섞여 있어도
        /// 걸러진다 — 뽑기에서 저주가 나오지 않는 것은 게임 규칙이라 저작 실수로 뚫려선 안 된다.
        /// </summary>
        public static IReadOnlyList<string> BuildRelicPool(
            IReadOnlyList<PlayerPermanentItemDefinition> definitions,
            PlayerRelicCurseInventory owned)
        {
            var pool = new List<string>();
            if (definitions == null)
            {
                return pool;
            }

            foreach (var definition in definitions)
            {
                if (definition == null || definition.Kind != PlayerPermanentItemKind.Relic)
                {
                    continue;
                }

                if (owned != null && owned.Contains(definition.Id))
                {
                    continue;
                }

                pool.Add(definition.Id);
            }

            return pool;
        }

        private static GachaRewardWeight PickWeighted(List<GachaRewardWeight> available, IRewardRandom random)
        {
            var total = 0;
            foreach (var entry in available)
            {
                total += entry.Weight;
            }

            // 가중치가 전부 0인 표도 저작 가능하므로 균등 추첨으로 폴백한다(0으로 나누지 않기 위해).
            if (total <= 0)
            {
                return available[random.Next(available.Count)];
            }

            var roll = random.Next(total);
            var cumulative = 0;
            foreach (var entry in available)
            {
                cumulative += entry.Weight;
                if (roll < cumulative)
                {
                    return entry;
                }
            }

            return available[available.Count - 1];
        }

        private static int RollAmount(GachaRewardWeight entry, IRewardRandom random)
        {
            var span = entry.MaxAmount - entry.MinAmount;
            return span <= 0 ? entry.MinAmount : entry.MinAmount + random.Next(span + 1);
        }
    }
}

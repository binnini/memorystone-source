using System;
using System.Collections.Generic;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 인형뽑기 한 번이 내놓을 수 있는 결과. 카드팩은 기존 카드 3택 UI로 이어지고, 나머지 둘은
    /// 선택 없이 즉시 지급된다(설계: docs/design/relic-curse-economy.md §3).
    /// </summary>
    public enum GachaRewardKind
    {
        CardPack,
        Money,
        Relic,

        /// <summary>소모품(T4-3). 가방이 가득이면 지급 지점이 돈 +15로 폴백한다(CR-10의 정신).</summary>
        Item
    }

    /// <summary>저작된 한 행: 결과 종류의 가중치와, 금액형일 때의 지급 범위.</summary>
    public readonly struct GachaRewardWeight
    {
        public GachaRewardWeight(GachaRewardKind kind, int weight, int minAmount = 0, int maxAmount = 0)
        {
            Kind = kind;
            Weight = weight < 0 ? 0 : weight;
            var low = Math.Max(0, minAmount);
            var high = Math.Max(0, maxAmount);
            // 저작이 뒤집혀 들어와도 받아준다 — 범위가 뒤집힌 채로 추첨에 들어가면 Next()에
            // 음수 길이가 전달돼 던진다.
            MinAmount = Math.Min(low, high);
            MaxAmount = Math.Max(low, high);
        }

        public GachaRewardKind Kind { get; }
        public int Weight { get; }
        public int MinAmount { get; }
        public int MaxAmount { get; }
    }

    /// <summary>
    /// 인형뽑기 결과 분포. <c>gacha_rewards.csv</c>가 정본이고, TextAsset이 없는 컨텍스트에서는
    /// <see cref="Default"/>가 폴백이다(<see cref="CardRewardRarityWeights"/>와 같은 관례).
    ///
    /// ⚠️ <see cref="GachaRewardKind"/>에 저주가 없는 것은 의도다. "저주는 뽑기에서 나오지 않는다"는
    /// 저작 관례가 아니라 <b>게임 규칙</b>이므로(DEC-2026-07-28-05 트랙의 D-8), 저작으로 켤 수 있는
    /// 컬럼이 아니라 아예 표현할 수 없는 형태로 막았다. 저주는 별도 경로로만 붙는다.
    /// </summary>
    public sealed class GachaRewardWeights
    {
        private readonly GachaRewardWeight[] entries;

        public GachaRewardWeights(IReadOnlyList<GachaRewardWeight> weights)
        {
            if (weights == null)
            {
                throw new ArgumentNullException(nameof(weights));
            }

            var copy = new GachaRewardWeight[weights.Count];
            var seen = new HashSet<GachaRewardKind>();
            for (var i = 0; i < weights.Count; i++)
            {
                if (!seen.Add(weights[i].Kind))
                {
                    throw new ArgumentException($"Duplicate gacha outcome '{weights[i].Kind}' in reward weights.", nameof(weights));
                }

                copy[i] = weights[i];
            }

            entries = copy;
        }

        /// <summary>코드 폴백. <c>gacha_rewards.csv</c>의 출하 값과 일치해야 한다(테스트가 감시).
        /// T4-3(2026-08-07): 50/40/10 → 50/35/10/5. 2026-09-01 사용자 확정: 100/0/0/0 —
        /// 보상뽑기는 「부적 추가」 하나로 고정한다. 꺼진 행을 지우지 않는 것은 되살릴 때
        /// 금액 범위를 다시 정하지 않기 위해서다.</summary>
        public static GachaRewardWeights Default { get; } = new GachaRewardWeights(
            new[]
            {
                new GachaRewardWeight(GachaRewardKind.CardPack, 100),
                new GachaRewardWeight(GachaRewardKind.Money, 0, 15, 40),
                new GachaRewardWeight(GachaRewardKind.Relic, 0),
                new GachaRewardWeight(GachaRewardKind.Item, 0)
            });

        public IReadOnlyList<GachaRewardWeight> Entries => entries;

        public bool TryGet(GachaRewardKind kind, out GachaRewardWeight weight)
        {
            for (var i = 0; i < entries.Length; i++)
            {
                if (entries[i].Kind == kind)
                {
                    weight = entries[i];
                    return true;
                }
            }

            weight = default;
            return false;
        }
    }
}

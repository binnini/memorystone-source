using SeoulPlayup.CardCore;
using System.Collections.Generic;
namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>A03 성스러운 빛 — 갈림길 — 자신을 {Heal} 회복하거나, 선택한 적에게 피해 {Damage}를 줍니다. (옛 behaviorId `attack.damage`)</summary>
    public sealed class A03_HolyLight : BasicAttackCard
    {
        public override string Id => "A03";

        /// <summary>연마(DEC-2026-09-06-08, 효과 연마 2차): 회복 4→6·피해 3→5 — 갈림길 두 축 모두.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With(amount: 5, healAmount: 6);

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "갈림길" };

        /// <summary>갈림길: 자신 회복(heal 축) 또는 적 공격. 문안은 cards.csv 선택지 컬럼(D-2).</summary>
        public override IReadOnlyList<CardBehaviorMetadata.ChoiceOption> Choices { get; } = new[]
        {
            new CardBehaviorMetadata.ChoiceOption("heal", CardBehaviorMetadata.ChoiceEffectHealPlayer, CardBehaviorMetadata.ChoiceTargetSelf),
            new CardBehaviorMetadata.ChoiceOption("attack", CardBehaviorMetadata.ChoiceEffectAttackDamage, "enemy")
        };
    }
}

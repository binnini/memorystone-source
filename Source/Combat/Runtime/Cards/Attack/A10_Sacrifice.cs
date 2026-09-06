using SeoulPlayup.CardCore;
using System.Collections.Generic;
namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>A10 제물을 바쳐서 — 손패의 행동 부적을 원하는 만큼 소멸시킵니다. 선택한 적에게 소멸시킨 부적 수만큼 피해 {Damage}를 반복합니다. (옛 behaviorId `attack.damage`)</summary>
    public sealed class A10_Sacrifice : BasicAttackCard
    {
        public override string Id => "A10";

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "소멸" };

        /// <summary>연마(옛 card_upgrades.csv): 피해 4→6.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With(amount: 6);

        /// <summary>추가 비용: 손패의 행동 부적을 원하는 만큼 골라 소멸시킨다. 반복 횟수(HitCount)는 고른 장수 — CombatState의 제물 경로.</summary>
        public override string AdditionalCost => CardBehaviorMetadata.AdditionalCostExileSelectedHandCards;
    }
}

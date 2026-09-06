using System.Collections.Generic;
using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>A14 으름장 — 선택한 적에게 피해 {Damage}를 주고 2턴 동안 쇠약하게 만듭니다. (옛 behaviorId `attack.damage`)</summary>
    public sealed class A14_Intimidate : BasicAttackCard
    {
        public override string Id => "A14";

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "쇠약" };

        /// <summary>연마: 피해 2→3 + 쇠약 30→50. 옛 card_upgrades.csv의 「쇠약 50」은 buff_debuff 컬럼에 저작돼 한 번도 적용된 적 없던 죽은 값이었고(쇠약은 stateEffect), DEC-2026-09-06-08에서 사용자가 적용을 확정했다.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With(amount: 3, stateEffect: "Weaken:50");
    }
}

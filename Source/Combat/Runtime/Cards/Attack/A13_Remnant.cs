using System.Collections.Generic;
using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>A13 잔혼 공격 — 선택한 적에게 피해 {Damage}를 소멸된 부적 수({HitCount}번)만큼 반복합니다. (옛 behaviorId `attack.damage`)</summary>
    public sealed class A13_Remnant : BasicAttackCard
    {
        public override string Id => "A13";

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "소멸" };

        /// <summary>연마(옛 card_upgrades.csv): 잔혼 반복 기본 피해 3→5.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With(amount: 5);
    }
}

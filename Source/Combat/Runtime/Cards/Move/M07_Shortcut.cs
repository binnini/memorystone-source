using System.Collections.Generic;
using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>M07 지름길 — 최대 {Range}칸 이동합니다. 유지 (옛 behaviorId `move.basic`)</summary>
    public sealed class M07_Shortcut : BasicMoveCard
    {
        public override string Id => "M07";

        /// <summary>유지(T5-2): 안 쓰면 턴이 끝나도 손에 남고, 다음 턴 정원을 깎지 않는다.</summary>
        public override bool RetainOnTurnEnd => true;

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "유지" };

        /// <summary>연마(옛 card_upgrades.csv): 지름길 3→4칸(#14). 유지는 연마와 무관한 축이라 그대로.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With(range: 4, amount: 4);
    }
}

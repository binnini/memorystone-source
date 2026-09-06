using System.Collections.Generic;
using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>D01 낡은 방어구 — 방어막 {Shield}를 얻습니다. (옛 behaviorId `defend.block`)</summary>
    public sealed class D01_OldArmor : BasicBlockCard
    {
        public override string Id => "D01";

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "방어막" };

        /// <summary>연마(옛 card_upgrades.csv): 방어막 5→8.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With(amount: 8);
    }
}

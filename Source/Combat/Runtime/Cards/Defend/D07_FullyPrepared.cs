using System.Collections.Generic;
using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>D07 만반의 준비 — 방어막 {Shield}를 얻습니다. 유지 (옛 behaviorId `defend.block`)</summary>
    public sealed class D07_FullyPrepared : BasicBlockCard
    {
        public override string Id => "D07";

        /// <summary>유지(T5-2): 안 쓰면 턴이 끝나도 손에 남고, 다음 턴 정원을 깎지 않는다.</summary>
        public override bool RetainOnTurnEnd => true;

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "방어막", "유지" };

        /// <summary>연마(옛 card_upgrades.csv): 유지 카드 방어막 4→6.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With(amount: 6);
    }
}

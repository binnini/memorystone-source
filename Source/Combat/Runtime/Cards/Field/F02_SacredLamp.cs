using System.Collections.Generic;
using SeoulPlayup.CardCore;
namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>F02 신성한 램프 — 턴 시작 시 {Shape}에 {Duration}턴 동안 회복 {Heal} 장판을 배치합니다. (옛 behaviorId `field.heal`)</summary>
    public sealed class F02_SacredLamp : FieldObjectCard
    {
        public override string Id => "F02";

        /// <summary>연마(DEC-2026-09-06-08, 효과 연마 2차): 장판 회복 5→7 — 장판은 Amount, {Heal} 토큰은 HealAmount.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With(amount: 7, healAmount: 7);

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "장판" };

        public override CardFieldObjectKind FieldKind => CardFieldObjectKind.ConditionalHeal;
    }
}

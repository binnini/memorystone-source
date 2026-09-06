using System.Collections.Generic;
using SeoulPlayup.CardCore;
namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>F04 흡수진 — 턴 시작 시 {Shape}에 {Duration}턴 동안 피해 {Damage} 흡혈 장판을 배치합니다. (옛 behaviorId `field.lifesteal`)</summary>
    public sealed class F04_LifestealZone : FieldObjectCard
    {
        public override string Id => "F04";

        /// <summary>연마(DEC-2026-09-06-08, 효과 연마 2차): 흡혈 장판 피해 2→3.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With(amount: 3);

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "흡혈", "장판" };

        public override CardFieldObjectKind FieldKind => CardFieldObjectKind.LifestealDamage;
    }
}

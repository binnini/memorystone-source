using System.Collections.Generic;
using SeoulPlayup.CardCore;
namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>F01 폭탄 투하 — 턴 시작 시 {Shape}에 {Duration}턴 동안 피해 {Damage} 장판을 배치합니다. (옛 behaviorId `field.damage`)</summary>
    public sealed class F01_Firebomb : FieldObjectCard
    {
        public override string Id => "F01";

        /// <summary>연마(DEC-2026-09-06-08, 효과 연마 2차): 장판 피해 6→8 — 정의 시점 치환이라 배치되는 장판이 그대로 받는다.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With(amount: 8);

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "장판" };

        public override CardFieldObjectKind FieldKind => CardFieldObjectKind.FieldDamage;
    }
}

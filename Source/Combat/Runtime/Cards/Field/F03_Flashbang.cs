using System.Collections.Generic;
using SeoulPlayup.CardCore;
namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>F03 섬광 — 턴 시작 시 {Shape}에 {Duration}턴 동안 속박 장판을 배치합니다. (옛 behaviorId `field.immobilize.flashbang`)</summary>
    public sealed class F03_Flashbang : FieldObjectCard
    {
        public override string Id => "F03";

        /// <summary>연마(DEC-2026-09-06-08, 효과 연마 2차): 속박 장판 지속 2→3턴.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With(durationTurns: 3);

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "속박", "장판" };

        public override CardFieldObjectKind FieldKind => CardFieldObjectKind.MassImmobilize;
    }
}

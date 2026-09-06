using System.Collections.Generic;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>X12 원귀 — 사용 불가. 손에 있는 동안 허점을 1턴 간 부여받습니다. (옛 behaviorId `status.vengeful_ghost`)</summary>
    public sealed class X12_VengefulGhost : StatusCard
    {
        public override string Id => "X12";

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "사용 불가", "허점" };
    }
}

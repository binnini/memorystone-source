using System.Collections.Generic;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>X03 정전 — 사용 불가. 봉인. (옛 behaviorId `status.blackout`)</summary>
    public sealed class X03_Blackout : StatusCard
    {
        public override string Id => "X03";

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "사용 불가", "봉인" };
    }
}

using System.Collections.Generic;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>X07 지각 — 사용 불가. 손에 있는 동안 이동력이 1 줄어듭니다. (옛 behaviorId `status.tardiness`)</summary>
    public sealed class X07_Tardiness : StatusCard
    {
        public override string Id => "X07";

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "사용 불가" };
    }
}

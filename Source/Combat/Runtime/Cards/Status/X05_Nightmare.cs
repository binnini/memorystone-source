using System.Collections.Generic;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>X05 악몽 — 사용 불가. 손에 있는 동안 시야가 1 줄어듭니다. (옛 behaviorId `status.nightmare`)</summary>
    public sealed class X05_Nightmare : StatusCard
    {
        public override string Id => "X05";

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "사용 불가" };
    }
}

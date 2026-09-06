using System.Collections.Generic;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>X01 미세먼지 — 사용 불가. 아지랑이. (옛 behaviorId `status.fine_dust`)</summary>
    public sealed class X01_FineDust : StatusCard
    {
        public override string Id => "X01";

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "사용 불가", "아지랑이" };
    }
}

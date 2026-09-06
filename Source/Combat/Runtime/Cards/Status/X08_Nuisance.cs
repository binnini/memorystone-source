using System.Collections.Generic;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>X08 골칫거리 — 사용 불가. (옛 behaviorId `status.nuisance`)</summary>
    public sealed class X08_Nuisance : StatusCard
    {
        public override string Id => "X08";

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "사용 불가" };
    }
}

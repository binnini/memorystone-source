using System.Collections.Generic;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>X11 도깨비 장난 — 사용 불가. 봉인. 아지랑이. (옛 behaviorId `status.goblin_prank`)</summary>
    public sealed class X11_GoblinPrank : StatusCard
    {
        public override string Id => "X11";

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "사용 불가", "봉인", "아지랑이" };
    }
}

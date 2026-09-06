using System.Collections.Generic;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>X02 깨진 유리 — 사용 불가. 턴 종료 시 손패에 있으면 피해 {Damage}를 받습니다. (옛 behaviorId `status.broken_glass`)</summary>
    public sealed class X02_BrokenGlass : StatusCard
    {
        public override string Id => "X02";

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "사용 불가" };
    }
}

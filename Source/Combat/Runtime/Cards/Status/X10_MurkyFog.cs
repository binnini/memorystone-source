using System.Collections.Generic;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>X10 궂은 안개 — 사용 불가. 턴 종료 시 손패에 있으면 실명 1턴을 받습니다. (옛 behaviorId `status.murky_fog`)</summary>
    public sealed class X10_MurkyFog : StatusCard
    {
        public override string Id => "X10";

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "사용 불가", "실명" };
    }
}

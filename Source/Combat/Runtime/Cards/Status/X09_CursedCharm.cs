using System.Collections.Generic;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>X09 부정 탄 부적 — 사용 불가. 턴 종료 시 손패에 있으면 쇠약 1턴을 받습니다. (옛 behaviorId `status.cursed_charm`)</summary>
    public sealed class X09_CursedCharm : StatusCard
    {
        public override string Id => "X09";

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "사용 불가", "쇠약" };
    }
}

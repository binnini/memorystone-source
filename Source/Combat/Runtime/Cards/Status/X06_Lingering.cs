using System.Collections.Generic;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>X06 미련 — 사용 불가. 이 부적을 뽑을 시 사용 가능한 기력이 1 줄어듭니다. (옛 behaviorId `status.lingering`)</summary>
    public sealed class X06_Lingering : StatusCard
    {
        public override string Id => "X06";

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "사용 불가" };
    }
}

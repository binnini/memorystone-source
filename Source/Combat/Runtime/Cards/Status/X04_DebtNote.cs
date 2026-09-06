using System.Collections.Generic;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>X04 빚 문서 — 낼 수 있는 유일한 저주: 기 1을 내고 자신을 소멸시킨다. (옛 behaviorId `status.debt_note`)</summary>
    public sealed class X04_DebtNote : StatusCard
    {
        public override string Id => "X04";

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "소멸" };

        /// <summary>효과가 「자신을 소멸」이 전부다 — 소멸 더미로 가므로 회수(U02)·소멸 스케일(A13)과 이어진다.</summary>
        public override CardDisposal Disposal => CardDisposal.Exile;
    }
}

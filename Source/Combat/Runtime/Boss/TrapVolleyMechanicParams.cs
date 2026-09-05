namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 함정 배치 기믹(trap-volley · §21.5)의 <c>mechanicParams</c> 키 정본. 철조각·전멸기와 같은 평평한
    /// 키 맵을 공유하므로 전 키에 <c>trapVolley</c> 접두를 붙여 충돌을 막는다(기존 규약).
    /// </summary>
    internal static class TrapVolleyMechanicParams
    {
        /// <summary>배치가 시작되는 페이즈(1-based).</summary>
        public const string PhaseMin = "trapVolleyPhaseMin";

        /// <summary>배치 주기(턴). 전멸기 시퀀스에 양보한 턴은 쿨다운을 소모하지 않는다.</summary>
        public const string IntervalTurns = "trapVolleyIntervalTurns";

        /// <summary>페이즈별 배치 개수(<c>|</c> 구분 · 페이즈당 1항).</summary>
        public const string CountByPhase = "trapVolleyByPhase";

        /// <summary>배치 링 반경(아레나 중심 기준 — 기물 볼리와 같은 기준점).</summary>
        public const string RingRadius = "trapVolleyRingRadius";

        /// <summary>배치 함정 간 최소 간격. 자리가 모자라면 간격이 아니라 개수를 줄인다.</summary>
        public const string MinSpacing = "trapVolleyMinSpacing";

        /// <summary>
        /// 함정 효과 종류(<see cref="SeoulPlayup.Map.Runtime.HexTrapEffectKind"/> 멤버명).
        /// 🔴 <c>Stun</c>은 저작 불가 — 전멸기 안전지대 도달 보장이 경로탐색이 아니라 그냥 거리
        /// (<c>safeReach</c>)라서 이동을 통째로 뺏는 함정은 그 계약을 조용히 깬다(§18.5 판정).
        /// </summary>
        public const string EffectKind = "trapVolleyEffectKind";

        /// <summary>효과 수치(피해량·스택 등 — kind별 의미는 함정 효과 계약과 동일).</summary>
        public const string EffectAmount = "trapVolleyEffectAmount";

        /// <summary>효과 지속 턴(상태이상형 kind 전용 · 0 허용).</summary>
        public const string EffectDurationTurns = "trapVolleyEffectDurationTurns";

        /// <summary>
        /// 배치가 실제로 일어난 턴에 보스가 얻는 방어막(Block · §21.8 제안 4 — "함정을 심으며 몸을
        /// 굳힌다"). 0 = 방어막 없음. 몬스터 Block은 턴 시작에 소거되지 않으므로 <b>부술 때까지 남는다</b> —
        /// "다음 배치 전에 방어막을 깎아라"는 퍼즐이 이 지속 규칙에서 나온다.
        /// </summary>
        public const string GuardBlock = "trapVolleyGuardBlock";

        /// <summary>
        /// 판에 동시에 무장 상태로 존재할 수 있는 런타임 함정 상한. 게임플레이 노브가 아니라
        /// 저작 사고 가드다(철조각 <c>maxAlive</c>와 같은 성격) — 짧은 주기 + 큰 개수 저작이
        /// 아레나를 함정으로 도배하는 것을 파싱 없이도 런타임에서 한 번 더 막는다.
        /// </summary>
        public const string MaxArmed = "trapVolleyMaxArmed";
    }
}

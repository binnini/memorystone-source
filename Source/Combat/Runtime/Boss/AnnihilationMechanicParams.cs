namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 전멸기(annihilation) 기믹의 <c>mechanicParams</c> 키 정본(계획 §13.5).
    /// 철조각과 같은 프로필의 평평한 키 맵을 공유하므로 전 키에 <c>annihilation</c> 접두를 붙여
    /// 충돌을 막는다. 키 상수를 한 곳에 모으는 이유는 <see cref="IronScrapMechanicParams"/>와 같다 —
    /// 파서 검증·기믹 소비·테스트가 같은 문자열을 봐야 한다.
    /// </summary>
    internal static class AnnihilationMechanicParams
    {
        /// <summary>전멸기가 활성화되는 최소 페이즈(1-based). 1이면 처음부터.</summary>
        public const string PhaseMin = "annihilationPhaseMin";

        /// <summary>발동 주기(몬스터 페이즈 수). 예고 턴수보다 길어야 한다.</summary>
        public const string IntervalTurns = "annihilationIntervalTurns";

        /// <summary>예고에서 폭발까지의 몬스터 페이즈 수(1 = 다음 몬스터 행동에 폭발).</summary>
        public const string TelegraphTurns = "annihilationTelegraphTurns";

        /// <summary>폭발 피해(예고된 칸에 서 있는 플레이어에게).</summary>
        public const string Damage = "annihilationDamage";

        /// <summary>
        /// 안전지대 <b>후보</b> 칸 수(§20-B). 개편 전 <c>annihilationSafeCells</c>("안전한 칸 수")에서
        /// 의미가 바뀌었다 — 이제 이 중 일부만 진짜다. 키 이름을 함께 갈아야 옛 저작이 새 의미로
        /// 조용히 재해석되지 않는다.
        /// </summary>
        public const string CandidateCells = "annihilationCandidateCells";

        /// <summary>
        /// 후보 중 <b>진짜</b> 안전지대 칸 수. <c>candidateCells - realSafeCells &lt;= 1</c>이 계약이다:
        /// 가짜가 정확히 하나일 때만 "정찰 1장이면 회피 확률 100%"가 성립한다(§20-B-2) —
        /// 진짜로 판명되면 거기로 가고, 가짜로 판명되면 남은 둘이 전부 진짜다.
        /// </summary>
        public const string RealSafeCells = "annihilationRealSafeCells";

        /// <summary>
        /// 후보끼리 유지하려는 최소 이격의 <b>시작값</b>. 자리가 없으면 3→2→1로 완화하고,
        /// 그래도 안 되면 함정 겹침을 허용한다(§20-B-4 계단식 완화 — 공짜 완화를 먼저 소진한다).
        /// </summary>
        public const string CandidateSpacing = "annihilationCandidateSpacing";

        /// <summary>중앙 점프 착지 시 <b>인접 링</b>에 피해를 주는 기준 반경(보스 footprint 반경).</summary>
        public const string LandingBlastRadius = "annihilationLandingBlastRadius";

        /// <summary>착지 인접 링에 서 있던 플레이어가 받는 피해.</summary>
        public const string LandingBlastDamage = "annihilationLandingBlastDamage";

        /// <summary>
        /// 플레이어 위치에서 가장 가까운 안전지대 칸까지 허용되는 최대 거리. 이 거리 안에 안전지대
        /// 앵커를 세울 수 없으면 <b>발동 자체를 포기</b>한다 — "전멸기 = 이동 강제 퍼즐"이지
        /// 즉사 체크가 아니라는 계약의 방어선이다.
        /// </summary>
        public const string SafeReach = "annihilationSafeReach";

        /// <summary>봉인된 아레나가 없는 보스전(고정형 등)에서 쓰는 폭발 반경(보스 중심).</summary>
        public const string FallbackRadius = "annihilationFallbackRadius";
    }
}

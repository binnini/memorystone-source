namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 철조각 사슬 기믹(scrap-chain · §21.8 제안 2)의 <c>mechanicParams</c> 키 정본.
    /// 철조각·전멸기와 같은 평평한 키 맵을 공유하므로 전 키에 <c>scrapChain</c> 접두를 붙인다.
    /// </summary>
    internal static class ScrapChainMechanicParams
    {
        /// <summary>사슬이 꽂히는 기물의 정의 id(철조각 M901 — iron-scrap의 propId와 별도 저작이다:
        /// 기믹끼리 서로의 키를 읽기 시작하면 한쪽 개명이 다른 쪽을 조용히 끊는다).</summary>
        public const string PropId = "scrapChainPropId";

        /// <summary>사슬이 활성화되는 페이즈(1-based).</summary>
        public const string PhaseMin = "scrapChainPhaseMin";

        /// <summary>발동 주기(턴). 철조각 창(살포~흡수 3턴)은 10턴에 한 번이므로 이 값이 창보다 짧으면
        /// 실효 빈도는 "볼리마다 한 번"으로 수렴한다.</summary>
        public const string IntervalTurns = "scrapChainIntervalTurns";

        /// <summary>명중한 사슬 경로 칸의 피해(필드 피해 계약 — Block 먼저·전부 막히면 "방어!").</summary>
        public const string Damage = "scrapChainDamage";

        /// <summary>명중 시 속박(Immobilize) 지속 턴. 0 = 속박 없음. 하드 CC 면역창·수호 관문은
        /// 기존 부여 게이트를 그대로 탄다.</summary>
        public const string RootTurns = "scrapChainRootTurns";
    }
}

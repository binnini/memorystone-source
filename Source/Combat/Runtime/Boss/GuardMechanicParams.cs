namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 수호 기믹(guard · DEC-2026-09-03-03)의 <c>mechanicParams</c> 키. 상태이상 무효 충전(수호 —
    /// <see cref="StatusEffectKind.Guard"/>)을 페이즈 진입마다 보스에게 저작량만큼 부여한다.
    /// </summary>
    internal static class GuardMechanicParams
    {
        /// <summary>CSV <c>mechanicId</c> 디스패치 키. 페이즈 진입 부여(<c>ApplyBossPhaseEntry</c>)가
        /// "이 보스에 수호가 저작되어 있는가"를 이 값으로 판정한다.</summary>
        public const string MechanicId = "guard";

        /// <summary>
        /// 페이즈별 부여 충전 수(<c>|</c> 구분 — 예: <c>1|2|2</c>). 페이즈 수와 길이가 같아야 하며
        /// 0은 "그 페이즈에는 부여 없음"이다.
        ///
        /// 왜 <c>boss_phases.csv</c> 컬럼이 아닌가: 충전 수는 이 기믹에만 있는 개념이라 페이즈 표에
        /// 넣으면 수호와 무관한 <b>모든</b> 보스가 그 컬럼을 갖게 된다(volleyByPhase와 같은 판단).
        /// </summary>
        public const string ChargesByPhase = "guardChargesByPhase";
    }
}

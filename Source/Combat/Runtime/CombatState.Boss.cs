using System;
using System.Collections.Generic;
using System.Linq;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 보스 페이즈 트랙과 보스 기믹 결의. 규칙 계층에 보스 개념이 들어오는 유일한 지점이며,
    /// 특정 보스("불가살")를 이름으로 아는 코드는 여기에도 없다 — 모든 동작은
    /// <c>bossId</c>(=monsterId)와 <see cref="BossCatalogDefinition"/> 저작 데이터로만 결정된다.
    /// </summary>
    public sealed partial class CombatState
    {
        // ── 3-B(2026-09-04): 본문은 BossEncounterState.cs. 여기는 시그니처 불변 위임과 중첩 타입만 남는다.
        public bool HasBossPhaseTrack => Boss.HasBossPhaseTrack;
        public IReadOnlyList<BossPhaseState> BossPhases => Boss.BossPhases;
        public IReadOnlyList<BossPhaseTransition> LastBossPhaseTransitions => Boss.LastBossPhaseTransitions;
        public bool TryGetBossPhaseState(string bossUnitId, out BossPhaseState state) => Boss.TryGetBossPhaseState(bossUnitId, out state);
        public bool DebugAdvanceBossPhase(string bossUnitId, out int fromPhase, out int toPhase) => Boss.DebugAdvanceBossPhase(bossUnitId, out fromPhase, out toPhase);
        private void EnsureBossPhaseTracks() => Boss.EnsureBossPhaseTracks();
        private BossPhaseTrack FindBossPhaseTrack(string bossUnitId) => Boss.FindBossPhaseTrack(bossUnitId);
        private void ResolveBossMechanicsStep() => Boss.ResolveBossMechanicsStep();
        internal bool HadLivingPropsAtMonsterPhaseStart(BossPhaseTrack track, MonsterRuntime boss) => Boss.HadLivingPropsAtMonsterPhaseStart(track, boss);
        internal void TryGrantBossGuardChargesForCurrentPhase(BossPhaseTrack track, BossProfileDefinition profile, MonsterRuntime boss) => Boss.TryGrantBossGuardChargesForCurrentPhase(track, profile, boss);
        internal void GrantBossGuardCharges(MonsterRuntime boss, int amount) => Boss.GrantBossGuardCharges(boss, amount);
        public void GetBossGimmickCountdowns(string bossUnitId, out int propVolleyTurnsRemaining, out int trapVolleyTurnsRemaining) => Boss.GetBossGimmickCountdowns(bossUnitId, out propVolleyTurnsRemaining, out trapVolleyTurnsRemaining);
        private int GetBossPhaseStrengthBonusPercent(string bossUnitId) => Boss.GetBossPhaseStrengthBonusPercent(bossUnitId);
        private int GetMonsterPatternPhaseGateInternal(MonsterRuntime monster) => Boss.GetMonsterPatternPhaseGateInternal(monster);

        /// <summary>
        /// 보스 페이즈가 올라간 직후 발화한다(bossUnitId, from, to). 표현 전용이며,
        /// 규칙 변경(최대 체력·강화·패턴 게이트)은 발화 <b>전에</b> 이미 적용을 마친 상태다.
        /// 페이즈는 하강하지 않으므로 <c>to &gt; from</c>이 항상 성립한다.
        /// </summary>
        public event Action<string, int, int> BossPhaseChanged;
    }

    /// <summary>
    /// 보스 페이즈 상태의 공개 투영(표현 계층용 읽기 전용 스냅샷). 규칙 계층의
    /// <c>BossPhaseTrack</c>은 internal로 남기고, HUD·연출은 이 값만 본다.
    /// </summary>
    public readonly struct BossPhaseState
    {
        public BossPhaseState(
            string bossUnitId,
            string bossDefinitionId,
            string displayName,
            BossPhaseMetricKind phaseMetric,
            int currentPhase,
            int phaseCount,
            int metricProgress,
            int nextPhaseThreshold,
            int absorbedStacks,
            float visualScale,
            int footprintRadius,
            string bgmCueId,
            StatusEffectKind? auraStatusKind)
        {
            BossUnitId = bossUnitId ?? string.Empty;
            BossDefinitionId = bossDefinitionId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            PhaseMetric = phaseMetric;
            CurrentPhase = currentPhase;
            PhaseCount = phaseCount;
            MetricProgress = metricProgress;
            NextPhaseThreshold = nextPhaseThreshold;
            AbsorbedStacks = absorbedStacks;
            VisualScale = visualScale;
            FootprintRadius = footprintRadius;
            BgmCueId = bgmCueId ?? string.Empty;
            AuraStatusKind = auraStatusKind;
        }

        public string BossUnitId { get; }
        public string BossDefinitionId { get; }

        /// <summary>보스 표시명(프로필 저작). HUD가 카탈로그를 다시 뒤지지 않도록 여기에 실어 보낸다.</summary>
        public string DisplayName { get; }

        /// <summary>지표 종류. HUD가 게이지 캡션("철조각" / "체력" / "경과 턴")을 고르는 데만 쓴다.</summary>
        public BossPhaseMetricKind PhaseMetric { get; }

        public int CurrentPhase { get; }
        public int PhaseCount { get; }

        /// <summary>정규화된 지표 진행값(<see cref="BossPhaseMetricKind"/> 참고).</summary>
        public int MetricProgress { get; }

        /// <summary>다음 페이즈 진입 임계값. 마지막 페이즈면 0.</summary>
        public int NextPhaseThreshold { get; }

        public int AbsorbedStacks { get; }

        /// <summary>보스 모델 배율(표현 전용).</summary>
        public float VisualScale { get; }

        /// <summary>물리 점유 반경. P6까지 소비되지 않는다(저작 선반영).</summary>
        public int FootprintRadius { get; }

        /// <summary>현재 페이즈의 BGM 큐 id. 프로필에 베이스가 없으면 빈 문자열.</summary>
        public string BgmCueId { get; }

        /// <summary>
        /// 이 페이즈에서 보스가 두르는 아우라 상태 종류(없으면 null). <b>ActiveEffect가 아니다</b> —
        /// 표현 계층(상태 루프 VFX 리컨실러)이 이 값을 읽어 <c>status.loop.{kind}</c> 루프를 붙이며,
        /// 상태이상 파이프라인(만료·아이콘·정화)을 전혀 타지 않는다(docs/boss-raid-plan.md §8-2와 같은 원리).
        /// </summary>
        public StatusEffectKind? AuraStatusKind { get; }

        public bool IsFinalPhase => CurrentPhase >= PhaseCount;
    }

    /// <summary>
    /// 한 결의에서 보스가 올라간 페이즈 전환의 표현용 스냅샷(bossUnitId, from, to). 페이즈는 하강하지 않으므로
    /// 항상 <c>ToPhase &gt; FromPhase</c>다. 규칙 계층은 이 값으로 아무것도 하지 않는다 — 오직 타임라인 조립기가
    /// 연출 비트를 만드는 데만 쓴다.
    /// </summary>
    public readonly struct BossPhaseTransition
    {
        public BossPhaseTransition(string bossUnitId, int fromPhase, int toPhase)
        {
            BossUnitId = bossUnitId ?? string.Empty;
            FromPhase = fromPhase;
            ToPhase = toPhase;
        }

        public string BossUnitId { get; }
        public int FromPhase { get; }
        public int ToPhase { get; }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 전멸기(annihilation · §20-B)의 보드 측 규칙: 중앙 점프, 안전지대 후보 산출(계단식 완화),
    /// 예고 영역, 폭발 해소, 표현 투영. 순서·수치는 <see cref="AnnihilationMechanic"/>이,
    /// 보드 조회와 피해 적용은 여기가 갖는다.
    ///
    /// <para>개편의 축(§20-B-2): 후보 <b>3곳 중 진짜 2 · 가짜 1</b>. 정찰로 한 곳만 판별하면
    /// 진짜면 거기로 가고(2/3), 가짜면 남은 둘이 전부 진짜다(1/3) — <b>어느 쪽이든 회피가 확정</b>이다.
    /// 이 성질은 가짜가 정확히 하나일 때만 성립하므로 저작 가드
    /// (<c>candidateCells - realSafeCells &lt;= 1</c>)가 계약의 방어선이다.</para>
    /// </summary>
    public sealed partial class CombatState
    {
        // ── 3-B(2026-09-04): 본문은 BossEncounterState.Annihilation.cs. 여기는 시그니처 불변 위임과 중첩 타입만 남는다.
        internal int BeginBossAnnihilation(BossPhaseTrack track, MonsterRuntime boss, BossAnnihilationRequest request) => Boss.BeginBossAnnihilation(track, boss, request);
        internal bool CanBeginBossAnnihilation(MonsterRuntime boss, BossAnnihilationRequest request) => Boss.CanBeginBossAnnihilation(boss, request);
        private void RevealBossAnnihilationCandidatesInScoutArea(HexCoord target, int revealRadius) => Boss.RevealBossAnnihilationCandidatesInScoutArea(target, revealRadius);
        internal void ResolveBossAnnihilationBlast(BossPhaseTrack track, MonsterRuntime boss, int damage) => Boss.ResolveBossAnnihilationBlast(track, boss, damage);
        public IReadOnlyList<HexCoord> GetBossAnnihilationTelegraphCells() => Boss.GetBossAnnihilationTelegraphCells();
        public IReadOnlyList<BossAnnihilationTelegraphState> GetBossAnnihilationTelegraphs() => Boss.GetBossAnnihilationTelegraphs();
        public IReadOnlyList<BossSafeZoneCandidateState> GetBossSafeZoneCandidates() => Boss.GetBossSafeZoneCandidates();

        /// <summary>
        /// 전멸기 발동 한 번의 저작 입력. 인자가 여덟 개로 늘어나 순서 실수가 조용한 버그가 되므로
        /// 묶는다(값 전달이라 기믹이 규칙 상태를 건드릴 표면도 늘지 않는다).
        /// </summary>
        internal readonly struct BossAnnihilationRequest
        {
            public BossAnnihilationRequest(
                int candidateCells,
                int realSafeCells,
                int candidateSpacing,
                int safeReach,
                int fallbackRadius,
                int landingBlastRadius,
                int landingBlastDamage)
            {
                CandidateCells = Math.Max(1, candidateCells);
                RealSafeCells = Math.Max(1, realSafeCells);
                CandidateSpacing = Math.Max(1, candidateSpacing);
                SafeReach = Math.Max(1, safeReach);
                FallbackRadius = Math.Max(1, fallbackRadius);
                LandingBlastRadius = Math.Max(0, landingBlastRadius);
                LandingBlastDamage = Math.Max(0, landingBlastDamage);
            }

            public int CandidateCells { get; }
            public int RealSafeCells { get; }
            public int CandidateSpacing { get; }
            public int SafeReach { get; }
            public int FallbackRadius { get; }
            public int LandingBlastRadius { get; }
            public int LandingBlastDamage { get; }
        }
    }

    /// <summary>안전지대 후보 한 칸의 판별 상태.</summary>
    public enum BossSafeZoneCandidateKind
    {
        /// <summary>미판별. <c>?</c>가 붙는다 — 이 축의 유일한 신규 기호다.</summary>
        Unknown = 0,

        /// <summary>정찰로 진짜라고 판명. 예고가 없으므로 그대로 안전하다.</summary>
        Real,

        /// <summary>정찰로 가짜라고 판명. 아래 깔린 붉은 예고가 드러난다.</summary>
        Fake
    }

    /// <summary>안전지대 후보 한 칸의 공개 투영(표현 전용).</summary>
    public readonly struct BossSafeZoneCandidateState
    {
        public BossSafeZoneCandidateState(string bossUnitId, HexCoord coord, BossSafeZoneCandidateKind kind)
        {
            BossUnitId = bossUnitId ?? string.Empty;
            Coord = coord;
            Kind = kind;
        }

        public string BossUnitId { get; }
        public HexCoord Coord { get; }
        public BossSafeZoneCandidateKind Kind { get; }
    }

    /// <summary>
    /// 전멸기 연출이 VFX/SFX 카탈로그에서 해석될 때 쓰는 <c>sourceRef</c> 스탬프
    /// (<see cref="BossPropSourceRefs"/>와 같은 규약 — <see cref="EffectKind"/>를 늘리지 않고
    /// 전용 큐를 고를 수 있게 한다).
    /// </summary>
    public static class BossAnnihilationSourceRefs
    {
        /// <summary>보스가 아레나 중앙으로 내리꽂히는 순간(착지 좌표).</summary>
        public const string Jump = "boss.annihilation.jump";

        /// <summary>착지 인접 링에 퍼지는 충격(플레이어 위치).</summary>
        public const string LandingBlast = "boss.annihilation.landing";
    }
}

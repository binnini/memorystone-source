using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 보스 취약 부위(§20-A). 몸이 여러 칸인 보스는 가장자리 링 중 한 칸이 취약 부위이고,
    /// <b>정찰로 확인한 동안에만</b> 그 칸에 들어간 피해가 2배가 된다.
    ///
    /// 이 축은 처벌이 아니라 <b>보상</b>이다 — 못 찾은 공격은 감쇠 없이 평소 그대로 100%다.
    /// 그래서 보스 체력·페이즈 임계를 다시 조율할 필요가 없고(지표는 흡수 스택이지 피해가 아니다),
    /// 정찰이 손에 없는 턴에 억울할 일도 없다. 대신 "정찰을 계속 뽑을 수 있는 덱"이 DPS에서 앞선다.
    ///
    /// 상태는 <see cref="BossPhaseTrack"/>의 세 필드(오프셋 2 + 남은 판명 턴 1)가 전부이고,
    /// <b>남은 판명 턴 하나가 자리와 판명을 함께 지배</b>한다 — bool과 좌표를 따로 들면 "자리는
    /// 바뀌었는데 판명이 남아 있는" 정의되지 않은 상태가 표현 가능해진다.
    /// </summary>
    public sealed partial class CombatState
    {
        // ── 3-B(2026-09-04): 본문은 BossEncounterState.WeakSpot.cs. 여기는 시그니처 불변 위임과 중첩 타입만 남는다.
        internal const int BossWeakSpotDamagePercent = BossEncounterState.BossWeakSpotDamagePercent;
        internal const int BossWeakSpotKnownTurns = BossEncounterState.BossWeakSpotKnownTurns;
        internal bool TryGetBossWeakSpotCoord(MonsterRuntime monster, out HexCoord coord) => Boss.TryGetBossWeakSpotCoord(monster, out coord);
        internal bool TryGetKnownBossWeakSpotCoord(MonsterRuntime monster, out HexCoord coord) => Boss.TryGetKnownBossWeakSpotCoord(monster, out coord);
        private int ResolveBossWeakSpotDamagePercent(MonsterRuntime monster, AttackCoverage coverage) => Boss.ResolveBossWeakSpotDamagePercent(monster, coverage);
        public bool MonsterHasBossWeakSpot(string monsterId) => Boss.MonsterHasBossWeakSpot(monsterId);
        public IReadOnlyList<BossWeakSpotState> GetBossWeakSpots() => Boss.GetBossWeakSpots();
        public IReadOnlyList<HexCoord> BossWeakSpotCoords => Boss.BossWeakSpotCoords;
        internal void AdvanceBossWeakSpot(BossPhaseTrack track, MonsterRuntime boss) => Boss.AdvanceBossWeakSpot(track, boss);
        internal void ReselectBossWeakSpot(BossPhaseTrack track, MonsterRuntime boss) => Boss.ReselectBossWeakSpot(track, boss);
        private void RevealBossWeakSpotsInScoutArea(HexCoord target, int revealRadius) => Boss.RevealBossWeakSpotsInScoutArea(target, revealRadius);
    }

    /// <summary>
    /// 판명된 취약 부위 하나의 공개 투영(표현 전용). 남은 턴을 실어 보내는 이유는 판명이 2턴짜리라
    /// "이번 턴이 마지막인가"가 곧 플레이 결정(지금 정찰을 다시 쓸 것인가)이기 때문이다 —
    /// 아무 예고 없이 사라지면 버그로 읽힌다(§20-A-7).
    /// </summary>
    public readonly struct BossWeakSpotState
    {
        public BossWeakSpotState(string bossUnitId, HexCoord coord, int knownTurnsRemaining, int damagePercent)
        {
            BossUnitId = bossUnitId ?? string.Empty;
            Coord = coord;
            KnownTurnsRemaining = knownTurnsRemaining;
            DamagePercent = damagePercent;
        }

        public string BossUnitId { get; }
        public HexCoord Coord { get; }

        /// <summary>이번 턴을 포함해 판명이 유지되는 몬스터 페이즈 수. 1이면 이번 턴이 마지막이다.</summary>
        public int KnownTurnsRemaining { get; }

        /// <summary>취약타 배수(%). 상수 200이지만 툴팁이 카탈로그를 다시 뒤지지 않도록 실어 보낸다.</summary>
        public int DamagePercent { get; }

        /// <summary>이번 턴이 판명의 마지막 턴인가(오버레이가 만료 임박을 저채도로 알린다).</summary>
        public bool IsExpiringThisTurn => KnownTurnsRemaining <= 1;
    }
}

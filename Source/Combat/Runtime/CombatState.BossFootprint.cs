using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 보스 물리 footprint(P6 · 계획 §13.4). 보스는 중심 좌표 하나가 아니라 반경 N의 원판을 점유한다.
    ///
    /// 전부 <b>유도값</b>이다 — 반경은 보스 페이즈 트랙(저작: boss_phases.footprintRadius)에서 매번
    /// 읽고, 점유 칸은 중심+반경에서 계산한다. MonsterRuntime에 상태를 더하지 않으므로 서스펜드
    /// 스키마 변경이 없고, 원판(방향 무관)이라 맵 오브젝트 모델(FootprintOffsets+회전)보다 단순하다.
    /// </summary>
    public sealed partial class CombatState
    {
        // ── 3-B(2026-09-04): 본문은 BossEncounterState.Footprint.cs. 여기는 시그니처 불변 위임과 중첩 타입만 남는다.
        public string LastBossGrowthRepositionReport => Boss.LastBossGrowthRepositionReport;
        internal int GetMonsterFootprintRadius(MonsterRuntime monster) => Boss.GetMonsterFootprintRadius(monster);
        public IReadOnlyList<HexCoord> BossFootprintCoords => Boss.BossFootprintCoords;
        public IReadOnlyList<HexCoord> GetBossFootprintCoords(Func<string, bool> includeBossUnit) => Boss.GetBossFootprintCoords(includeBossUnit);
    }
}

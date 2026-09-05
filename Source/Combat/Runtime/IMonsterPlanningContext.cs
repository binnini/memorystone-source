using System.Collections.Generic;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// <see cref="MonsterAiPlanner"/>가 <see cref="CombatState"/>에서 읽어야 하는 최소 표면.
    /// 계획 레이어를 클래스로 떼어내면서 "무엇에 의존하는가"를 명시적으로 고정한 계약이며,
    /// 여기에 없는 것(RaiseEffect·activeEffects·상태이상 부여 등 해소부 표면)은 계획부가 만지지 않는다.
    /// 구현은 <see cref="CombatState"/> 하나뿐이고, 이미 public인 멤버를 제외한 나머지는
    /// 명시적 구현으로 노출해 CombatState의 public API를 넓히지 않는다.
    /// </summary>
    internal interface IMonsterPlanningContext
    {
        HexMapData Map { get; }

        HexCoord PlayerCoord { get; }

        CombatConfig Config { get; }

        HexTerrainTraits TerrainTraits { get; }

        IReadOnlyDictionary<HexCoord, HexCellRuntimeState> RuntimeStates { get; }

        bool IsPlayerDead { get; }

        /// <summary>죽은 몬스터까지 포함한 살아있는 목록의 원본(계획은 사망 몬스터 초기화도 담당한다).</summary>
        IReadOnlyList<MonsterRuntime> PlanningMonsters { get; }

        /// <summary>공격속도 기준 행동 순서. 해소부와 동일한 순서를 써야 예약 타일 경쟁이 일치한다.</summary>
        IEnumerable<MonsterRuntime> MonsterActionOrder();

        MonsterActivityState ClassifyMonsterActivity(MonsterRuntime monster);

        bool IsMonsterMovementBlocked(MonsterRuntime monster);

        /// <summary>
        /// 몸이 묶여 있는가(속박·기절·섬광 장판). <see cref="IsMonsterMovementBlocked"/>의 부분집합이며,
        /// 차집합은 "걷기만 막힌 멀티셀 봉인 보스"다. 도약(§17)은 경로를 걷지 않으므로 이쪽에만 걸린다.
        /// </summary>
        bool IsMonsterMovementRooted(MonsterRuntime monster);

        bool IsMonsterAttackBlocked(MonsterRuntime monster);

        /// <summary>
        /// 이 몬스터의 공격 패턴 게이트 레벨. <see cref="MonsterAttackPattern.PhaseMin"/>이 이 값
        /// 이하인 패턴만 선택 후보가 된다. 보스 페이즈가 없는 몬스터는 0이라, phaseMin을 저작하지 않은
        /// 기존 몬스터는 전부 그대로 통과한다.
        /// </summary>
        int GetMonsterPatternPhaseGate(MonsterRuntime monster);

        /// <summary>
        /// 이 패턴이 지금 <b>규칙상</b> 후보에서 빠지는가(요괴 §4-4 — 소환 동시 상한이 첫 사용자).
        /// 쿨다운·페이즈·거리 창처럼 패턴 자체가 들고 있는 게이트가 아니라, 판의 상태를 봐야 하는
        /// 게이트를 위한 자리다. 계획이 이 술어를 봐야 "예고는 떴는데 아무 일도 없는 턴"이 생기지 않는다.
        /// </summary>
        bool IsAttackPatternSuppressed(MonsterRuntime monster, MonsterAttackPattern pattern);

        /// <summary>
        /// 이 몬스터의 물리 점유 반경(P6 §13.4). 0 = 단일 칸(보스 페이즈가 없는 모든 기존 몬스터).
        /// 계획부는 이 값으로 공격 shape 원점을 몸통 가장자리로 보정하고(C-6), 비-shape 사거리를
        /// 가장 가까운 점유 칸 기준으로 잰다(C-3).
        /// </summary>
        int GetMonsterFootprintRadius(MonsterRuntime monster);

        /// <summary>
        /// 형상 footprint 오프셋(2026-09-03 · 삼각형 정예). 기본 구현이 null(한 칸)이라 기존 테스트 더블은 안 바뀐다 —
        /// 원판 반경과 별개 축이고 경로탐색 클리어런스에만 쓰인다.
        /// </summary>
        IReadOnlyList<HexCoord> GetMonsterFootprintOffsets(MonsterRuntime monster) => null;

        /// <summary>몸 기하 한 값(원판 반경 + 형상 오프셋). 플래너의 거리·형상 원점·사거리는 전부 이것에서 유도한다.</summary>
        MonsterBodyShape GetMonsterBody(MonsterRuntime monster) =>
            new MonsterBodyShape(GetMonsterFootprintRadius(monster), GetMonsterFootprintOffsets(monster));

        /// <summary>은신(T2 페이즈 C): 플레이어가 <b>모든</b> 몬스터에게 안 보이는가. 계획기가 감지 마스킹 비트로 접는다.</summary>
        bool IsPlayerHiddenFromMonsters => false;

        /// <summary>실명(T4-2): 이 좌표의 몬스터 <b>하나</b>만 감지가 꺼졌는가(점유 유일 → 좌표=신원).</summary>
        bool IsMonsterSenseBlindedAt(HexCoord monsterCoord) => false;

        /// <summary>
        /// 이 몬스터의 <c>behaviorProfileRef</c>(monster_catalog.csv). 비면 기본 프로파일. 계획기의 의도 선택이
        /// 매복(B002) 분기를 이 값으로 고른다(<see cref="MonsterBehaviorProfileRegistry"/>).
        /// </summary>
        string GetBehaviorProfileRef(MonsterRuntime monster) => MonsterBehaviorProfileRegistry.DefaultProfileRef;
    }
}

using System.Collections.Generic;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    public readonly struct MonsterFsmContext
    {
        public MonsterFsmContext(
            HexCoord monsterCoord,
            HexCoord playerCoord,
            HexCoord spawnCoord,
            int attackRange,
            int chaseRange,
            int disengageRange,
            HexMapData map,
            IReadOnlyDictionary<HexCoord, HexCellRuntimeState> runtimeStates,
            bool playerIsDead,
            IReadOnlyList<HexCoord> patrolArea = null,
            bool playerHidden = false,
            int? bodyDistanceToPlayer = null)
        {
            MonsterCoord = monsterCoord;
            PlayerCoord = playerCoord;
            SpawnCoord = spawnCoord;
            AttackRange = attackRange < 0 ? 0 : attackRange;
            ChaseRange = chaseRange < 0 ? 0 : chaseRange;
            DisengageRange = disengageRange < 0 ? ChaseRange : disengageRange;
            Map = map;
            RuntimeStates = runtimeStates;
            PlayerIsDead = playerIsDead;
            PatrolArea = patrolArea ?? System.Array.Empty<HexCoord>();
            PlayerHidden = playerHidden;
            BodyDistanceToPlayer = bodyDistanceToPlayer;
        }

        /// <summary>
        /// 몸(원판·형상)에서 잰 플레이어 거리(2026-09-03). 값이 있으면 FSM이 중심 거리 대신 이것으로 추격·공격 전이를
        /// 판단한다 — 플래너의 패턴 커버 판정(<c>MonsterBodyShape.DistanceFrom</c>)과 같은 자여야 「몸통 칸에 붙었는데
        /// 몬스터는 다가오려 한다」가 안 생긴다. 한 칸 몬스터는 중심 거리와 같다.
        /// </summary>
        public readonly int? BodyDistanceToPlayer;

        /// <summary>AI들이 쓰는 플레이어 거리. 몸 기준 값이 있으면 그것, 없으면 중심 거리 — 모든 IMonsterAi가 이 한 값을 본다.</summary>
        public int DistanceToPlayer => BodyDistanceToPlayer ?? MonsterCoord.DistanceTo(PlayerCoord);

        /// <summary>
        /// 은신 마스킹 사본. 출하 경로는 계획기가 생성 시점에 <see cref="PlayerHidden"/>을 직접 채우므로
        /// (DEC-2026-09-05-03) 이 사본은 테스트·랩이 컨텍스트 하나를 두 조건으로 돌릴 때 쓴다.
        /// </summary>
        public MonsterFsmContext WithPlayerHidden(bool playerHidden)
        {
            return new MonsterFsmContext(
                MonsterCoord, PlayerCoord, SpawnCoord, AttackRange, ChaseRange, DisengageRange,
                Map, RuntimeStates, PlayerIsDead, PatrolArea, playerHidden, BodyDistanceToPlayer);
        }

        public readonly HexCoord MonsterCoord;
        public readonly HexCoord PlayerCoord;
        public readonly HexCoord SpawnCoord;
        public readonly int AttackRange;
        public readonly int ChaseRange;
        public readonly int DisengageRange;
        public readonly HexMapData Map;
        public readonly IReadOnlyDictionary<HexCoord, HexCellRuntimeState> RuntimeStates;
        public readonly bool PlayerIsDead;
        public readonly IReadOnlyList<HexCoord> PatrolArea;

        /// <summary>
        /// 은신(T2 페이즈 C): true면 몬스터가 플레이어를 <b>감지하지 못한다</b> — 감지·추격·공격 전이가
        /// 전부 불성립하고, 마지막 목격 좌표(LastKnownPlayerCoord)도 갱신되지 않는다. 규칙(이동·순찰)은
        /// 그대로 굴러간다. 안 보일 뿐이다(미지의 역방향 대칭).
        /// </summary>
        public readonly bool PlayerHidden;
    }
}

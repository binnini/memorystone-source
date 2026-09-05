using System.Collections.Generic;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 계획기의 이동 의도 선택(<see cref="MonsterAiPlanner.SelectMovementIntent"/>) 계약. 2026-09-05
    /// (DEC-2026-09-05-03)에 6노드 트리를 if/else 한 함수로 접었다 — 아래 단언은 트리 시절 그대로다:
    /// 규칙이 바뀌지 않았음을 이 파일이 잠근다.
    /// </summary>
    public sealed class MonsterIntentSelectionTests
    {
        [Test]
        public void IntentSelection_AttacksBeforeChasing_WhenPlayerInAttackRange()
        {
            var memory = new MonsterFsmMemory { State = MonsterFsmState.Patrol };

            var intent = Step(new HexCoord(0, 0), new HexCoord(1, 0), memory, attackRange: 1, chaseRange: 5);

            Assert.That(intent.Type, Is.EqualTo(EnemyIntentType.Attack));
            Assert.That(memory.State, Is.EqualTo(MonsterFsmState.Attack));
        }

        [Test]
        public void IntentSelection_TransitionsFromChaseToSearch_WhenPlayerLeavesChaseRange()
        {
            var memory = new MonsterFsmMemory { State = MonsterFsmState.Chase };
            var player = new HexCoord(6, 0);

            var intent = Step(new HexCoord(0, 0), player, memory, chaseRange: 3);

            Assert.That(intent.Type, Is.EqualTo(EnemyIntentType.Search));
            Assert.That(memory.State, Is.EqualTo(MonsterFsmState.Search));
            Assert.That(memory.LastKnownPlayerCoord, Is.EqualTo(player));
        }

        [Test]
        public void IntentSelection_IgnoresDisengageRange_LeavesChaseAtChaseRange()
        {
            // 출하 규칙 보존(DEC-2026-09-05-03): 추격을 놓는 임계는 감지 범위 그 자체다. 옛 MonsterFsm은
            // disengage(더 넓은 값)까지 따라갔지만 출하 트리는 그 값을 읽지 않았고, 통일된 함수도 읽지 않는다.
            var memory = new MonsterFsmMemory { State = MonsterFsmState.Chase };

            var intent = Step(new HexCoord(0, 0), new HexCoord(4, 0), memory, chaseRange: 3, disengageRange: 6);

            Assert.That(intent.Type, Is.EqualTo(EnemyIntentType.Search), "거리 4 > 감지 3 — disengage 6 안이어도 놓는다.");
        }

        [Test]
        public void IntentSelection_HiddenPlayerBlocksDetection_AndKeepsLastKnownCoord()
        {
            // 은신·실명 마스킹은 컨텍스트의 PlayerHidden 한 비트다(종전 데코레이터 둘의 계약).
            var lastSeen = new HexCoord(2, 0);
            var memory = new MonsterFsmMemory { State = MonsterFsmState.Chase, LastKnownPlayerCoord = lastSeen };

            var intent = Step(new HexCoord(0, 0), new HexCoord(1, 0), memory, attackRange: 1, chaseRange: 5, playerHidden: true);

            Assert.That(intent.Type, Is.EqualTo(EnemyIntentType.Search), "사거리 안이어도 안 보이면 공격·추격 전이가 불성립.");
            Assert.That(memory.LastKnownPlayerCoord, Is.EqualTo(lastSeen), "숨은 동안의 현재 위치는 새지 않는다.");
        }

        [Test]
        public void IntentSelection_AmbushProfileHoldsGroundWithoutTouchingMemory()
        {
            // 매복(B002): 사거리 밖이면 기억을 건드리지 않고 Return(계획기가 목적지를 스폰으로 잡아 제자리).
            var memory = new MonsterFsmMemory { State = MonsterFsmState.Patrol, SearchTurnsRemaining = 0 };

            var far = Step(new HexCoord(0, 0), new HexCoord(6, 0), memory, chaseRange: 8, profile: MonsterBehaviorProfileRegistry.AmbushProfileRef);
            Assert.That(far.Type, Is.EqualTo(EnemyIntentType.Return), "거리 6 > 4 — 자리를 지킨다.");
            Assert.That(memory.State, Is.EqualTo(MonsterFsmState.Patrol), "사거리 밖에서는 아래 규칙을 아예 돌리지 않는다(기억 무변경).");

            var near = Step(new HexCoord(0, 0), new HexCoord(3, 0), memory, chaseRange: 8, profile: MonsterBehaviorProfileRegistry.AmbushProfileRef);
            Assert.That(near.Type, Is.EqualTo(EnemyIntentType.Chase), "사거리 안이면 평소 규칙이 그대로 돈다.");
        }

        [Test]
        public void CombatPatrol_MovesOnlyInsideAssignedPatrolArea_WhenPlayerOutsideChaseRange()
        {
            var baseMap = CombatState.CreateDemoMap(6);
            var spawn = new HexCoord(0, 0);
            var patrolStep = new HexCoord(1, 0);
            var patrolArea = new[] { spawn, patrolStep };
            var map = new HexMapData(
                baseMap.AllCells,
                monsterSpawnRefs: new[] { new HexMonsterSpawnRef("spawn-a", "M001", spawn, "patrol", HexMapPurpose.Unspecified, "area-a") },
                patrolAreas: new[] { new HexPatrolAreaRef("area-a", patrolArea) });
            var config = new CombatConfig(20, 10, 2, 1, 4, 4, 2, 1, 3, 2, 1, 4);
            var state = new CombatState(map, new HexCoord(6, 0), (IEnumerable<MonsterConfig>)null, config);

            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            Assert.That(state.EndAction(), Is.True); // PlayerMovement -> MonsterMovement
            state.ResolveMonsterMovement(); // patrol movement resolves in the MonsterMovement phase

            Assert.That(state.Monsters[0].Intent.Type, Is.EqualTo(EnemyIntentType.Patrol));
            Assert.That(state.Monsters[0].Coord, Is.EqualTo(patrolStep));
            Assert.That(patrolArea, Does.Contain(state.Monsters[0].Coord));
        }

        private static EnemyIntent Step(
            HexCoord monsterCoord,
            HexCoord playerCoord,
            MonsterFsmMemory memory,
            int attackRange = 1,
            int chaseRange = 3,
            int disengageRange = -1,
            bool playerHidden = false,
            string profile = MonsterBehaviorProfileRegistry.DefaultProfileRef)
        {
            var ctx = new MonsterFsmContext(
                monsterCoord,
                playerCoord,
                monsterCoord,
                attackRange,
                chaseRange,
                disengageRange < 0 ? chaseRange : disengageRange,
                CombatState.CreateDemoMap(6),
                new Dictionary<HexCoord, HexCellRuntimeState>(),
                playerIsDead: false,
                playerHidden: playerHidden);
            return MonsterAiPlanner.SelectMovementIntent(ctx, memory, profile);
        }
    }
}


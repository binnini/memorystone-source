using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 몬스터 턴 "계획" 레이어 — 규칙 기반 턴 계획기. 몬스터마다 ①컨텍스트(거리·감지·마스킹) →
    /// ②이동 의도(<see cref="SelectMovementIntent"/>, if/else 한 함수) → ③이동 목적지 → ④공격 패턴 추첨 →
    /// ⑤도약 → ⑥<see cref="MonsterRuntime.TurnPlan"/> 커밋을 위에서 아래로 한 번 흐른다.
    ///
    /// <para>2026-09-05(DEC-2026-09-05-03): 의도 선택은 종전에 <c>IMonsterAi</c> 구현 둘(<c>MonsterFsm</c> 상태기계 ·
    /// <c>MonsterBehaviorTreeAi</c> 6노드 선택기)과 감지 마스킹 데코레이터 셋(은신·실명·매복)으로 나뉘어 있었다.
    /// 출하는 트리만 썼고 FSM은 테스트 전용 잔재였으며, 트리는 의도 enum 하나만 내고 실제 결정(칸·패턴·조준)은
    /// 전부 이 클래스의 절차 코드였다. 형식이 둘 섞인 것을 하나로 통일했다 — <b>규칙은 출하 트리 의미 그대로</b>
    /// (Patrol에서 사거리 안이면 즉시 Attack · 추격 이탈 임계 = 감지 범위, 히스테리시스 없음). 마스킹은 컨텍스트의
    /// <see cref="MonsterFsmContext.PlayerHidden"/> 한 비트로, 매복은 프로파일 분기로 들어왔다.</para>
    ///
    /// 의도적으로 여기에 없는 것: 계획을 실제로 굴리는 해소부(피해·넉백·상태이상·연출 이벤트)는
    /// CombatState에 남는다. 이 클래스는 <see cref="IMonsterPlanningContext"/> 밖의 상태를 건드리지 않으며,
    /// 유일하게 소유한 가변 상태는 공격 패턴 추첨 RNG다.
    /// </summary>
    internal sealed class MonsterAiPlanner
    {
        private readonly IMonsterPlanningContext context;
        private readonly int attackPatternSeed;
        // 소비 횟수를 세는 파생(P5) — 재개 시 저장된 커서까지 되감는다(RestoreRngCursors).
        private CountingRandom monsterAttackPatternRng;

        // 턴별 피해 변주(damageJitter) 전용 스트림. 선택 RNG와 분리해야 변주 저작이 늘어도
        // 패턴 선택 시퀀스(기존 결정성 테스트의 전제)가 흔들리지 않는다.
        private CountingRandom attackDamageJitterRng;

        internal MonsterAiPlanner(IMonsterPlanningContext context, int attackPatternSeed = 0)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            this.attackPatternSeed = attackPatternSeed;
            monsterAttackPatternRng = new CountingRandom(attackPatternSeed);
            attackDamageJitterRng = new CountingRandom(DamageJitterSeed(attackPatternSeed));
        }

        private static int DamageJitterSeed(int attackPatternSeed) => unchecked(attackPatternSeed * 31 + 17);

        /// <summary>스트림 4 커서(패턴 선택). 스냅샷이 싣는다.</summary>
        internal int AttackPatternRngCursor => monsterAttackPatternRng.Consumed;

        /// <summary>스트림 4' 커서(피해 변주).</summary>
        internal int DamageJitterRngCursor => attackDamageJitterRng.Consumed;

        /// <summary>
        /// 두 스트림을 같은 시드로 다시 만들어 저장된 커서까지 되감는다(P5). 생성자·복원 전에 소비한 칸은 버려진다.
        /// </summary>
        internal void RestoreRngCursors(int attackPatternCursor, int damageJitterCursor)
        {
            monsterAttackPatternRng = new CountingRandom(attackPatternSeed);
            monsterAttackPatternRng.FastForward(attackPatternCursor);
            attackDamageJitterRng = new CountingRandom(DamageJitterSeed(attackPatternSeed));
            attackDamageJitterRng.FastForward(damageJitterCursor);
        }

        /// <summary>
        /// 패턴 인덱스 기록의 <b>단일 지점</b>(DEC-2026-08-19-02). 인덱스와 함께 이번 의도의 피해
        /// 굴림을 확정한다: 패턴에 <c>DamageJitter</c>가 저작돼 있으면 [-J, +J] 균일 1회, 없으면 0으로
        /// 되돌린다(이전 변주 패턴의 굴림이 다음 패턴에 새는 것 방지). 「굴린 값이 상태」 —
        /// 예고와 집행이 <c>AttackDamageRollOffset</c> 저장값을 함께 소비하므로(R-8) 여기 말고
        /// 다른 곳에서 인덱스를 직접 쓰면 굴림 없는 의도가 생긴다.
        /// </summary>
        private void CommitAttackPattern(MonsterRuntime monster, int patternIndex)
        {
            monster.AttackPatternIndex = patternIndex;
            var pattern = monster.CurrentAttackPattern;
            monster.AttackDamageRollOffset = pattern.HasDamageJitter
                ? attackDamageJitterRng.Next(-pattern.DamageJitter, pattern.DamageJitter + 1)
                : 0;
        }

        private HexMapData Map => context.Map;

        private HexCoord PlayerCoord => context.PlayerCoord;

        private CombatConfig Config => context.Config;

        private HexTerrainTraits terrainTraits => context.TerrainTraits;

        private IReadOnlyDictionary<HexCoord, HexCellRuntimeState> runtimeStates => context.RuntimeStates;

        private IReadOnlyList<MonsterRuntime> monsters => context.PlanningMonsters;

        private bool PlayerIsDead => context.IsPlayerDead;

        // ---------------------------------------------------------------------------------------
        // 턴 계획 갱신
        // ---------------------------------------------------------------------------------------

        /// <param name="preserveCommittedAttackRolls">
        /// 서스펜드 복원 전용(P5). 패턴 인덱스와 피해 변주는 「굴린 값이 상태」라 저장·복원되는데, 복원 뒤 갱신이
        /// 다시 굴리면 저장된 의도가 덮이고 커서까지 밀린다. true면 이동 계획·조준·도약 같은 미저장 기하만
        /// 다시 세우고 <b>굴림은 하나도 하지 않는다</b>.
        /// </param>
        internal void RefreshAllIntents(bool preserveCommittedAttackRolls = false)
        {
            foreach (var deadMonster in monsters.Where(monster => monster.Combatant.IsDead))
            {
                var intent = new EnemyIntent(EnemyIntentType.Patrol, PlayerCoord.DistanceTo(deadMonster.Coord), deadMonster.Coord, PlayerCoord);
                deadMonster.Intent = intent;
                deadMonster.LockedFacingIntent = intent;
                deadMonster.IntentPredictedMoveCoord = deadMonster.Coord;
                deadMonster.PendingAttackIntent = false;
                deadMonster.PlannedLeap = false;
                deadMonster.TurnPlan = MonsterTurnPlan.Inactive(deadMonster.Coord);
            }

            var reservedDestinations = new HashSet<HexCoord>();
            foreach (var monster in context.MonsterActionOrder())
            {
                monster.ActivityState = context.ClassifyMonsterActivity(monster);
                if (monster.Combatant.IsDead || monster.ActivityState == MonsterActivityState.Dormant)
                {
                    var intent = new EnemyIntent(EnemyIntentType.Patrol, PlayerCoord.DistanceTo(monster.Coord), monster.Coord, PlayerCoord);
                    monster.Intent = intent;
                    monster.LockedFacingIntent = intent;
                    monster.IntentPredictedMoveCoord = monster.Coord;
                    monster.PendingAttackIntent = false;
                    monster.PlannedLeap = false;
                    monster.TurnPlan = MonsterTurnPlan.Inactive(monster.Coord);
                    continue;
                }

                RefreshTurnPlan(monster, reservedDestinations, preserveCommittedAttackRolls);
                if (monster.TurnPlan.IsActive)
                {
                    reservedDestinations.Add(monster.TurnPlan.PlannedMoveCoord);
                }
            }
        }

        internal void RefreshTurnPlan(MonsterRuntime monster, ISet<HexCoord> reservedDestinations = null, bool preserveCommittedAttackRolls = false)
        {
            if (monster == null || monster.Combatant.IsDead || monster.ActivityState == MonsterActivityState.Dormant)
            {
                return;
            }

            monster.PlannedLeap = false;
            var movementIntent = ChooseEnemyIntent(monster, commitPatternSelection: !preserveCommittedAttackRolls);
            var plannedMove = IsMovementIntent(movementIntent.Type)
                ? ChooseEnemyMovementStep(monster, movementIntent.Type, reservedDestinations) ?? monster.Coord
                : monster.Coord;
            if (!preserveCommittedAttackRolls)
            {
                SelectAttackPatternForOrigin(monster, plannedMove);
            }

            // 도약(§17)은 <b>최후 수단</b>이다: 걷기 계획을 먼저 세우고, 그러고도 플레이어를 못 덮을 때만 본다.
            // 매 턴 뛰면 플레이어의 위치 선점이 무의미해지므로, 걸어서 이미 덮을 수 있으면 도약하지 않는다.
            // 걷기가 막힌 보스(멀티셀)의 실효 원점은 걷기 계획이 아니라 제자리다 — 어차피 아래
            // ApplyControlStatusConstraintToPlan이 걷기를 걷어낸다.
            var leapOrigin = context.IsMonsterMovementBlocked(monster) ? monster.Coord : plannedMove;
            if (TryPlanLeapAttack(monster, leapOrigin, out var landing, out var leapPatternIndex))
            {
                if (preserveCommittedAttackRolls)
                {
                    // 복원 모드: 저장된 커밋이 곧 이 도약 패턴이면 착지만 되살린다(굴림 없음). 다른 패턴이면
                    // 기하가 저장 뒤 달라진 것이라 저장된 커밋(걷기)을 그대로 둔다 — 여기서 새로 굴리지 않는다.
                    if (leapPatternIndex == monster.AttackPatternIndex)
                    {
                        plannedMove = landing;
                        monster.PlannedLeap = true;
                    }
                }
                else
                {
                    plannedMove = landing;
                    CommitAttackPattern(monster, leapPatternIndex);
                    monster.PlannedLeap = true;
                }
            }

            var attackFacingIntent = new EnemyIntent(
                EnemyIntentType.Attack,
                plannedMove.DistanceTo(PlayerCoord),
                plannedMove,
                PlayerCoord);

            monster.Intent = movementIntent;
            monster.LockedFacingIntent = attackFacingIntent;
            monster.IntentPredictedMoveCoord = plannedMove;
            monster.PendingAttackIntent = IsActiveMonsterAttackAreaCoveringPlayer(monster, plannedMove);
            monster.TurnPlan = new MonsterTurnPlan(
                movementIntent,
                plannedMove,
                attackFacingIntent,
                true,
                canAttack: true);
            // Control status (속박/기절) is applied to the committed plan WITHOUT running the FSM, so a
            // controlled monster is telegraphed as 대기 제자리 instead of re-committing a fresh attack.
            ApplyControlStatusConstraintToPlan(monster);
        }

        /// <summary>
        /// 정찰 카드 등으로 몬스터를 각성시킨다. 대상 조회는 호출자(CombatState)가 하고, 여기서는
        /// FSM 기억을 바꾼 뒤 예고(preview)만 다시 계산한다 — 커밋된 계획은 건드리지 않는다.
        /// </summary>
        internal void ApplyAlert(MonsterRuntime monster, int rangeBonus, int durationTurns)
        {
            if (monster == null)
            {
                return;
            }

            monster.FsmMemory.PreAlertState = monster.FsmMemory.State;
            monster.FsmMemory.AlertRangeBonus = Math.Max(0, rangeBonus);
            monster.FsmMemory.AlertTurnsRemaining = Math.Max(0, durationTurns);
            monster.Intent = PreviewEnemyIntent(monster);
            monster.IntentPredictedMoveCoord = IsMovementIntent(monster.Intent.Type)
                ? ChooseEnemyMovementStep(monster, monster.Intent.Type) ?? monster.Coord
                : monster.Coord;
        }

        private EnemyIntent ChooseEnemyIntent(MonsterRuntime monster, bool commitPatternSelection = true)
        {
            var fsmContext = CreateMonsterFsmContext(monster);
            var intent = SelectMovementIntent(fsmContext, monster.FsmMemory, context.GetBehaviorProfileRef(monster));
            return NormalizeMonsterAttackIntent(monster, fsmContext, monster.FsmMemory, intent, commitPatternSelection);
        }

        private EnemyIntent PreviewEnemyIntent(MonsterRuntime monster)
        {
            var fsmContext = CreateMonsterFsmContext(monster);
            var memory = monster.FsmMemory.Clone();
            var intent = SelectMovementIntent(fsmContext, memory, context.GetBehaviorProfileRef(monster));
            return NormalizeMonsterAttackIntent(monster, fsmContext, memory, intent, commitPatternSelection: false);
        }

        /// <summary>
        /// 이번 턴의 <b>이동 의도</b> 하나를 고른다 — 규칙 기반 계획기의 ② 단계. 종전 6노드 선택기
        /// (DeadPlayerPatrol → AttackIfInRange → ChaseIfInRange → ContinueSearch → ContinueReturn → PatrolArea)를
        /// 같은 순서·같은 부작용의 if/else로 옮긴 것이다. 첫 성립 갈래에서 끝난다(위가 아래를 덮는다).
        ///
        /// <para>감지 마스킹(은신·실명)은 <see cref="MonsterFsmContext.PlayerHidden"/> 한 비트다 — 「거리 ≤ 범위」 전이가
        /// 전부 불성립하고 마지막 목격 좌표를 갱신하지 않는다. 매복(B002)은 사거리 밖이면 <b>기억을 건드리지 않고</b>
        /// <see cref="EnemyIntentType.Return"/>을 돌려준다(계획 레이어가 Return의 목적지를 스폰으로 잡으므로 제자리 =
        /// 매복 지점 사수). 종전 데코레이터 셋이 하던 일 그대로다.</para>
        ///
        /// <para>🔴 규칙 보존: Patrol에서 사거리 안이면 <b>즉시</b> Attack이고, 추격을 놓는 임계는 감지 범위와 같다
        /// (<see cref="MonsterFsmContext.DisengageRange"/>는 여기서 읽지 않는다 — 옛 FSM만 읽던 값). 둘 다 출하 트리의
        /// 의미이며 이 통일에서 바꾸지 않았다. 바꾸려면 결정 기록부터.</para>
        /// </summary>
        internal static EnemyIntent SelectMovementIntent(MonsterFsmContext ctx, MonsterFsmMemory memory, string behaviorProfileRef)
        {
            memory = memory ?? new MonsterFsmMemory();
            var distance = ctx.DistanceToPlayer;

            // 매복(요괴 §4-6 ③): 사거리 밖·은신·플레이어 사망이면 아래를 아예 돌리지 않는다(기억 무변경·경계 감쇠 없음).
            if (MonsterBehaviorProfileRegistry.IsAmbush(behaviorProfileRef)
                && (ctx.PlayerIsDead || ctx.PlayerHidden || distance > MonsterBehaviorProfileRegistry.AmbushRange))
            {
                return new EnemyIntent(EnemyIntentType.Return, distance, ctx.MonsterCoord, ctx.PlayerCoord);
            }

            if (memory.State == MonsterFsmState.Alert)
            {
                memory.State = memory.PreAlertState;
            }

            MonsterFsmState next;
            if (ctx.PlayerIsDead)
            {
                next = MonsterFsmState.Patrol;
            }
            else if (!ctx.PlayerHidden && distance <= ctx.AttackRange)
            {
                next = MonsterFsmState.Attack;
                memory.LastKnownPlayerCoord = ctx.PlayerCoord;
            }
            else if (!ctx.PlayerHidden && distance <= GetEffectiveChaseRange(ctx, memory))
            {
                next = MonsterFsmState.Chase;
                memory.LastKnownPlayerCoord = ctx.PlayerCoord;
            }
            else if (memory.State == MonsterFsmState.Chase || memory.State == MonsterFsmState.Attack)
            {
                // 추격/공격 중 놓쳤다 — 수색으로. 은신 중이면 현재 위치를 새지 않게 마지막 목격 좌표를 두고 간다.
                next = MonsterFsmState.Search;
                if (!ctx.PlayerHidden)
                {
                    memory.LastKnownPlayerCoord = ctx.PlayerCoord;
                }

                memory.SearchTurnsRemaining = DefaultSearchTurns;
            }
            else if (memory.State == MonsterFsmState.Search)
            {
                if (memory.SearchTurnsRemaining <= 0)
                {
                    next = MonsterFsmState.Return;
                }
                else
                {
                    memory.SearchTurnsRemaining--;
                    next = MonsterFsmState.Search;
                }
            }
            else if (memory.State == MonsterFsmState.Return)
            {
                next = ctx.MonsterCoord == ctx.SpawnCoord ? MonsterFsmState.Patrol : MonsterFsmState.Return;
            }
            else
            {
                next = MonsterFsmState.Patrol;
            }

            memory.State = next;
            ApplyAlertDecay(memory);
            return new EnemyIntent(ToIntentType(next), distance, ctx.MonsterCoord, ctx.PlayerCoord);
        }

        private const int DefaultSearchTurns = 3;

        /// <summary>정찰 경계(<see cref="ApplyAlert"/>)가 남아 있는 동안 감지 범위에 더해지는 보너스.</summary>
        internal static int GetEffectiveChaseRange(MonsterFsmContext ctx, MonsterFsmMemory memory)
        {
            var bonus = memory != null && memory.AlertTurnsRemaining > 0 ? memory.AlertRangeBonus : 0;
            return ctx.ChaseRange + Math.Max(0, bonus);
        }

        /// <summary>경계 보너스가 붙은 이탈 범위 — <see cref="NormalizeMonsterAttackIntent"/>의 Chase/Search 폴백만 읽는다.</summary>
        internal static int GetEffectiveDisengageRange(MonsterFsmContext ctx, MonsterFsmMemory memory)
        {
            var bonus = memory != null && memory.AlertTurnsRemaining > 0 ? memory.AlertRangeBonus : 0;
            return ctx.DisengageRange + Math.Max(0, bonus);
        }

        private static void ApplyAlertDecay(MonsterFsmMemory memory)
        {
            if (memory.AlertTurnsRemaining > 0)
            {
                memory.AlertTurnsRemaining--;
            }

            if (memory.AlertTurnsRemaining <= 0)
            {
                memory.AlertTurnsRemaining = 0;
                memory.AlertRangeBonus = 0;
            }
        }

        internal static EnemyIntentType ToIntentType(MonsterFsmState state)
        {
            switch (state)
            {
                case MonsterFsmState.Chase:
                    return EnemyIntentType.Chase;
                case MonsterFsmState.Attack:
                    return EnemyIntentType.Attack;
                case MonsterFsmState.Search:
                    return EnemyIntentType.Search;
                case MonsterFsmState.Alert:
                    return EnemyIntentType.Alert;
                case MonsterFsmState.Return:
                    return EnemyIntentType.Return;
                default:
                    return EnemyIntentType.Patrol;
            }
        }

        private MonsterFsmContext CreateMonsterFsmContext(MonsterRuntime monster)
        {
            // 감지 마스킹 두 겹을 여기서 한 비트로 접는다: 은신(전역 — 전 몬스터가 나를 못 본다) ∪ 실명(이 몬스터만).
            // 종전에는 데코레이터 둘이 Step 직전에 컨텍스트 사본을 만들어 씌웠다 — 같은 값, 같은 자리.
            var playerHidden = context.IsPlayerHiddenFromMonsters || context.IsMonsterSenseBlindedAt(monster.Coord);
            return new MonsterFsmContext(
                monster.Coord,
                PlayerCoord,
                monster.SpawnCoord,
                GetMonsterMaxAttackRange(monster),
                Config.EnemyChaseRange,
                Config.EnemyDisengageRange,
                Map,
                runtimeStates,
                PlayerIsDead,
                monster.PatrolArea,
                playerHidden,
                ResolveFsmBodyDistance(monster));
        }

        /// <summary>
        /// FSM(추격·공격 전이)에 넘길 플레이어 거리. <b>형상 footprint</b>(삼각형 정예)만 몸통 칸 기준이다 —
        /// 원판 보스는 중심 거리를 유지한다. 보스의 걷기·전이 계약(§21.3 이동 테스트)이 중심 거리 위에 서 있고,
        /// 보스는 사거리가 길어 몸통 기준이 필요 없다. 패턴 커버 판정은 둘 다 몸 기준이다(<see cref="MonsterBodyShape"/>).
        /// </summary>
        private int? ResolveFsmBodyDistance(MonsterRuntime monster)
        {
            var body = context.GetMonsterBody(monster);
            return body.Offsets != null ? body.DistanceFrom(monster.Coord, PlayerCoord) : (int?)null;
        }

        private EnemyIntent NormalizeMonsterAttackIntent(
            MonsterRuntime monster,
            MonsterFsmContext fsmContext,
            MonsterFsmMemory memory,
            EnemyIntent intent,
            bool commitPatternSelection)
        {
            if (monster == null || intent.Type != EnemyIntentType.Attack)
            {
                return intent;
            }

            if (TryGetAttackPatternCoveringPlayerFrom(monster, monster.Coord, out var patternIndex))
            {
                if (commitPatternSelection
                    && monster.Intent.Type != EnemyIntentType.Attack
                    && !CanAttackPatternCoverPlayerFrom(monster, monster.Coord, monster.CurrentAttackPattern))
                {
                    CommitAttackPattern(monster, patternIndex);
                }

                return intent;
            }

            var distance = monster.Coord.DistanceTo(PlayerCoord);
            var nextState = distance <= GetEffectiveDisengageRange(fsmContext, memory)
                ? MonsterFsmState.Chase
                : MonsterFsmState.Search;
            memory.State = nextState;
            if (nextState == MonsterFsmState.Chase || nextState == MonsterFsmState.Search)
            {
                memory.LastKnownPlayerCoord = PlayerCoord;
            }

            return new EnemyIntent(ToIntentType(nextState), distance, monster.Coord, PlayerCoord);
        }

        internal static bool IsMovementIntent(EnemyIntentType type)
        {
            return type == EnemyIntentType.Chase || type == EnemyIntentType.Search || type == EnemyIntentType.Return || type == EnemyIntentType.Patrol;
        }

        private static int GetMonsterMaxAttackRange(MonsterRuntime monster)
        {
            return monster?.AttackPatterns == null || monster.AttackPatterns.Length == 0
                ? 1
                : monster.AttackPatterns.Max(pattern => pattern.Range);
        }

        // ---------------------------------------------------------------------------------------
        // 제어 상태(속박/기절)에 따른 계획 제약
        // ---------------------------------------------------------------------------------------

        // 제어상태(속박/기절)를 계획에 반영한다: 기절(공격 차단)은 무조건 대기, 속박(이동 차단)은 현재 타일에서의
        // 제자리 공격으로 전환한다. FSM을 다시 돌리지 않고 이미 커밋된 계획만 제약한다.
        internal void ApplyControlStatusConstraintToPlan(MonsterRuntime monster)
        {
            if (monster == null || monster.Combatant.IsDead)
            {
                return;
            }

            if (context.IsMonsterAttackBlocked(monster))
            {
                monster.PlannedLeap = false;
                SetMonsterWaitingIntent(monster);
                return;
            }

            // 🔴 도약(§17)이 여기서 죽어 있었다. 봉인된 멀티셀 보스는 속박과 <b>같은 이 경로</b>를 타고
            // 제자리 공격으로 collapse되는데(CombatState.IsMultiCellSealedArenaBoss), collapse는 계획된
            // 이동 좌표를 monster.Coord로 되돌린다 — 착지 지점을 아무리 잘 골라도 같은 RefreshTurnPlan
            // 말미에서 지워졌다. 걷기 금지의 근거(원판 클리어런스 경로탐색 부재)는 경로를 걷지 않는
            // 도약에는 성립하지 않으므로, 도약은 <b>rooted(몸이 묶임)에만</b> 걸린다.
            var rooted = context.IsMonsterMovementRooted(monster);
            if (rooted)
            {
                monster.PlannedLeap = false;
            }

            if (rooted || (context.IsMonsterMovementBlocked(monster) && !monster.PlannedLeap))
            {
                SetMonsterStationaryAttackIntent(monster);
            }
        }

        // Patrol and idle attacks keep their committed attack forecast while the monster remains in place.
        private void SetMonsterWaitingIntent(MonsterRuntime monster)
        {
            if (monster == null)
            {
                return;
            }

            monster.PendingAttackIntent = false;
            monster.Intent = new EnemyIntent(EnemyIntentType.Patrol, PlayerCoord.DistanceTo(monster.Coord), monster.Coord, PlayerCoord);
            monster.LockedFacingIntent = monster.Intent;
            monster.IntentPredictedMoveCoord = monster.Coord;
            monster.TurnPlan = MonsterTurnPlan.Inactive(monster.Coord);
        }

        // Stationary attack planning lets immobilized monsters still attack from their current tile.
        private void SetMonsterStationaryAttackIntent(MonsterRuntime monster)
        {
            if (monster == null)
            {
                return;
            }

            // 속박(Immobilize)은 오직 이동만 막는다. 공격 패턴·사거리·범위에는 어떤 영향도 주면 안 된다.
            // 취소할 이동 예고가 없는(이미 제자리로 확정된) 몬스터는 손대지 않는다 — 그래야 페이즈 순서상
            // 몬스터 이동이 이미 끝난 플레이어 액션 페이즈에 속박을 걸어도 이미 예고된 공격(패턴/범위/조준)이
            // 전혀 바뀌지 않는다. (공격까지 막아야 하는 건 기절이며, 그건 SetMonsterWaitingIntent가 처리한다.)
            var hasPendingMove = monster.IntentPredictedMoveCoord != monster.Coord
                || (monster.TurnPlan.IsActive && monster.TurnPlan.PlannedMoveCoord != monster.Coord);
            if (!hasPendingMove)
            {
                return;
            }

            // 이동 예고가 남아 있을 때만(예: 다회 지속 속박의 리프레시 시점) 이동을 취소하고 제자리 공격으로
            // collapse한다. 이때도 공격 패턴은 이미 선택된 것을 그대로 유지한다(SelectAttackPatternForOrigin
            // 호출 금지 — 속박이 사거리/패턴을 바꾸지 못하도록). 조준만 현재 타일 기준으로 맞춘다.
            var stationaryIntent = new EnemyIntent(EnemyIntentType.Attack, monster.Coord.DistanceTo(PlayerCoord), monster.Coord, PlayerCoord);
            monster.Intent = stationaryIntent;
            monster.LockedFacingIntent = stationaryIntent;
            monster.IntentPredictedMoveCoord = monster.Coord;
            monster.PendingAttackIntent = IsActiveMonsterAttackAreaCoveringPlayer(monster, monster.Coord);
            monster.TurnPlan = new MonsterTurnPlan(stationaryIntent, monster.Coord, stationaryIntent, isActive: true, canAttack: true);
        }

        // ---------------------------------------------------------------------------------------
        // 공격 패턴 선택 / 커버리지 판정
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// 현재 보스 페이즈 게이트로 해금된 패턴만(인덱스 보존). 배열을 교체하지 않고 <b>선택 단계에서만</b>
        /// 거르는 것이 핵심이다 — <see cref="MonsterRuntime.AttackPatterns"/>는 쿨다운 딕셔너리 키와
        /// 서스펜드 왕복이 인덱스로 묶여 있어 불변이어야 한다.
        /// 창은 [phaseMin, phaseMax]다: phaseMax(기본 무한)를 지나면 저페이즈 약패턴이 은퇴해
        /// 상위 변형으로 세대 교체된다(파서가 "은퇴하지 않는 쿨다운-0 기본 패턴" 하나를 강제한다).
        /// 저작 실수로 해금된 패턴이 하나도 없으면(있어서는 안 되는 상태) 게이트를 무시하고 전량을 후보로
        /// 돌려서 몬스터가 아무 행동도 못 하는 정지 상태에 빠지지 않게 한다.
        /// <para>
        /// 페이즈 창을 통과한 뒤 <b>거리 창</b> [distMin, distMax]을 한 번 더 건다. 보스는 봉인 중 이동이
        /// 0이라 "플레이어를 실제로 덮는 패턴"을 우선하는 기존 규칙만으로는 플레이어가 멀 때 원거리기만
        /// 커버 판정을 통과해 근접기가 영원히 안 뽑힌다 — 거리 창이 "플레이어 위치가 곧 무엇과 싸우는가"를
        /// 만드는 축이다. 거리는 <see cref="IMonsterPlanningContext.GetMonsterFootprintRadius"/>를 뺀
        /// <b>몸통 가장자리 기준</b>이라 보스가 커져도 저작 의미가 흔들리지 않는다.
        /// 거리 적격 집합이 비면 <b>거리 필터만</b> 무시하고 페이즈 필터 결과를 쓴다(페이즈 폴백은 그대로).
        /// </para>
        /// </summary>
        private List<(int Index, MonsterAttackPattern Pattern)> UnlockedPatterns(MonsterRuntime monster, HexCoord origin)
        {
            var unlocked = PhaseUnlockedPatterns(monster);
            var inBand = unlocked
                .Where(candidate => IsPatternInDistanceBand(monster, origin, candidate.Pattern))
                .ToList();
            return inBand.Count > 0 ? inBand : unlocked;
        }

        /// <summary>페이즈 창만 통과시킨 후보(거리 창은 걸지 않는다). 도약은 거리 창을 <b>착지 지점</b>에서
        /// 재야 하므로(§17) 두 필터를 갈라 놓는다 — 현재 좌표에서 재면 "멀리 있을 때 쓰는 도약"이
        /// 정작 멀 때 후보에서 빠져 영원히 안 나온다.</summary>
        private List<(int Index, MonsterAttackPattern Pattern)> PhaseUnlockedPatterns(MonsterRuntime monster)
        {
            var gate = context.GetMonsterPatternPhaseGate(monster);
            var all = monster.AttackPatterns
                .Select((pattern, index) => (Index: index, Pattern: pattern))
                .ToList();
            var unlocked = all
                .Where(candidate => candidate.Pattern.PhaseMin <= gate && gate <= candidate.Pattern.PhaseMax)
                .Where(candidate => !context.IsAttackPatternSuppressed(monster, candidate.Pattern))
                .ToList();
            // 전부 빠지면 예전처럼 전체로 되돌린다 — 몬스터가 아무것도 못 뽑는 정지 상태를 만드는 것보다
            // 억제를 한 번 어기는 편이 낫다(모든 몬스터는 쿨다운 0 기본 패턴을 하나씩 갖고 있어 실제로는
            // 여기까지 오지 않는다).
            return unlocked.Count == 0 ? all : unlocked;
        }

        /// <summary>거리 창 [distMin, distMax] 판정. 거리는 <b>몸통 가장자리 기준</b>이다.</summary>
        private bool IsPatternInDistanceBand(MonsterRuntime monster, HexCoord origin, MonsterAttackPattern pattern)
        {
            var distance = context.GetMonsterBody(monster).DistanceFrom(origin, PlayerCoord);
            return pattern.DistMin <= distance && distance <= pattern.DistMax;
        }

        private void SelectAttackPatternForOrigin(MonsterRuntime monster, HexCoord origin)
        {
            if (monster?.AttackPatterns == null || monster.AttackPatterns.Length == 0)
            {
                return;
            }

            // 랩 전용 강제 핀(§25). 계획이 매 턴 다시 서므로 핀이 없으면 다음 리프레시가 곧바로 덮는다 —
            // 그래서 "패턴을 하나 지정해 본다"는 여기 한 줄이어야 성립한다. 실게임에서는 항상 -1이다.
            if (monster.ForcedAttackPatternIndex >= 0 && monster.ForcedAttackPatternIndex < monster.AttackPatterns.Length)
            {
                CommitAttackPattern(monster, monster.ForcedAttackPatternIndex);
                return;
            }

            // Patterns currently on cooldown are NEVER selected — a status-effect attack (stun/slow/poison/…)
            // must not be reused on consecutive monster turns. Every monster is guaranteed at least one
            // cooldown-0 pattern, so the off-cooldown set is never empty.
            var offCooldown = UnlockedPatterns(monster, origin)
                .Where(candidate => !monster.IsAttackPatternOnCooldown(candidate.Index))
                .ToList();

            // Prefer off-cooldown patterns that actually cover the player → a real hit lands.
            // 자기부여 패턴(§21.8 제안 8)은 커버 판정에서 빠지지만(위 주석) 언제나 "성립하는 행동"이므로
            // 1군 후보에 함께 넣는다 — 가중치 경쟁으로 공격과 버프가 한 로테이션을 이룬다. 커버 패턴이
            // 하나도 없는 턴에는 헛스윙 대신 버프가 자연스럽게 뽑힌다.
            var coveringOffCooldown = offCooldown
                .Where(candidate => candidate.Pattern.IsSelfTargeted
                                    || CanAttackPatternCoverPlayerFrom(monster, origin, candidate.Pattern))
                .ToList();
            if (coveringOffCooldown.Count > 0)
            {
                SelectWeightedAttackPattern(monster, coveringOffCooldown);
                return;
            }

            // No off-cooldown pattern covers the player (e.g. the only attack that would reach is on cooldown).
            // The monster must not rest and must not reuse the cooldown attack, so it swings an available
            // pattern that whiffs into empty space. Prefer one whose range reaches the player; otherwise any
            // off-cooldown pattern. (ShouldPresentMissedMonsterAttack turns this into a visible miss swing.)
            var distance = context.GetMonsterBody(monster).DistanceFrom(origin, PlayerCoord);
            var offCooldownInRange = offCooldown
                .Where(candidate => distance <= candidate.Pattern.Range)
                .ToList();
            var swingCandidates = offCooldownInRange.Count > 0 ? offCooldownInRange : offCooldown;
            SelectWeightedAttackPattern(monster, swingCandidates);
        }

        private bool TryGetAttackPatternCoveringPlayerFrom(MonsterRuntime monster, HexCoord origin, out int patternIndex)
        {
            patternIndex = -1;
            if (monster?.AttackPatterns == null || monster.AttackPatterns.Length == 0)
            {
                return false;
            }

            var candidates = UnlockedPatterns(monster, origin)
                .Where(candidate => CanAttackPatternCoverPlayerFrom(monster, origin, candidate.Pattern))
                .ToList();
            if (candidates.Count == 0)
            {
                return false;
            }

            // Prefer patterns that are off cooldown; fall back to the full covering set if all are on cooldown.
            var available = candidates.Where(candidate => !monster.IsAttackPatternOnCooldown(candidate.Index)).ToList();
            if (available.Count > 0)
            {
                candidates = available;
            }

            patternIndex = candidates
                .OrderBy(candidate => candidate.Pattern.Range)
                .ThenByDescending(candidate => candidate.Pattern.Weight)
                .ThenBy(candidate => candidate.Index)
                .First()
                .Index;
            return patternIndex >= 0;
        }

        private bool CanAttackPatternCoverPlayerFrom(MonsterRuntime monster, HexCoord origin, MonsterAttackPattern pattern)
        {
            if (monster == null || PlayerIsDead)
            {
                return false;
            }

            // 🔴 자기부여 패턴은 플레이어를 "덮지" 않는다. 이 술어는 걷기 목적지 선정과 도약 발동
            // (AnyAttackPatternCanCoverPlayerFrom)이 "이동으로 플레이어에게 닿는가"의 뜻으로 읽는다 —
            // 버프가 참을 돌려주면 보스가 어디서든 닿는다고 믿고 추격을 멈춘다.
            if (pattern.IsSelfTargeted)
            {
                return false;
            }

            // P6(§13.4): 멀티셀 보스는 shape 원점을 몸통 가장자리로 보정하고(C-6), 비-shape 사거리는
            // 가장 가까운 점유 칸 기준으로 잰다(C-3). 반경 0 몬스터는 기존과 동일하다.
            var body = context.GetMonsterBody(monster);
            if (!string.IsNullOrEmpty(pattern.ShapeId))
            {
                var attackDir = origin.ApproximateDirection(PlayerCoord);
                return AttackShapeLibrary.GetAffectedCells(pattern.ShapeId, origin, attackDir, body)
                    .Contains(PlayerCoord);
            }

            return body.IsWithinRange(origin, PlayerCoord, pattern.Range);
        }

        private void SelectWeightedAttackPattern(MonsterRuntime monster, IReadOnlyList<(int Index, MonsterAttackPattern Pattern)> candidates)
        {
            var selectedIndex = SelectWeightedAttackPatternIndex(candidates);
            if (selectedIndex >= 0)
            {
                CommitAttackPattern(monster, selectedIndex);
            }
        }

        private int SelectWeightedAttackPatternIndex(IReadOnlyList<(int Index, MonsterAttackPattern Pattern)> candidates)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return -1;
            }

            var totalWeight = candidates.Sum(candidate => Math.Max(1, candidate.Pattern.Weight));
            var roll = monsterAttackPatternRng.Next(totalWeight);
            foreach (var candidate in candidates)
            {
                roll -= Math.Max(1, candidate.Pattern.Weight);
                if (roll < 0)
                {
                    return candidate.Index;
                }
            }

            return candidates[0].Index;
        }

        /// <summary>
        /// 현재 선택된 패턴이, 커밋된 조준(이동 종료 시점에 잠긴 LockedFacing/TurnPlan)을 기준으로
        /// 플레이어를 덮는가. 해소부와 계획부가 같은 술어를 써야 예고와 실제 명중이 어긋나지 않는다.
        /// </summary>
        internal bool IsActiveMonsterAttackAreaCoveringPlayer(MonsterRuntime monster, HexCoord origin)
        {
            if (monster == null || monster.Combatant.IsDead || PlayerIsDead)
            {
                return false;
            }

            var pattern = monster.CurrentAttackPattern;
            // 자기부여 패턴은 조준할 대상이 없다 — 선택됐다면 항상 집행된다(플레이어가 어디로 밀려나든
            // 빗맞음이 성립하지 않는다). 여기서 false면 커밋이 안 잡혀 버프가 영영 발동하지 않는다.
            if (pattern.IsSelfTargeted)
            {
                return true;
            }
            var body = context.GetMonsterBody(monster);
            if (!string.IsNullOrEmpty(pattern.ShapeId))
            {
                var facingIntent = monster.TurnPlan.IsActive ? monster.TurnPlan.AttackFacingIntent : monster.LockedFacingIntent;
                var attackDir = origin.ApproximateDirection(facingIntent.PlayerCoord);
                return AttackShapeLibrary.GetAffectedCells(pattern.ShapeId, origin, attackDir, body)
                    .Contains(PlayerCoord);
            }

            return body.IsWithinRange(origin, PlayerCoord, pattern.Range);
        }

        // True when the player sits inside the reach of ANY of this monster's attack patterns from origin,
        // regardless of cooldown. Used to keep a would-be attacker swinging (a whiff) instead of resting when
        // its only covering pattern is on cooldown. Mirrors CanAttackPatternCoverPlayerFrom's player facing.
        internal bool AnyAttackPatternCoversPlayerFrom(MonsterRuntime monster, HexCoord origin)
        {
            if (monster?.AttackPatterns == null || PlayerIsDead)
            {
                return false;
            }

            foreach (var pattern in monster.AttackPatterns)
            {
                if (CanAttackPatternCoverPlayerFrom(monster, origin, pattern))
                {
                    return true;
                }
            }

            return false;
        }

        private bool AnyAttackPatternCanCoverPlayerFrom(MonsterRuntime monster, HexCoord origin)
        {
            return monster?.AttackPatterns != null &&
                   monster.AttackPatterns.Any(pattern => CanAttackPatternCoverPlayerFrom(monster, origin, pattern));
        }

        // ---------------------------------------------------------------------------------------
        // 도약 공격 (§17)
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// 도약 착지 지점을 고른다. <b>경로를 걷지 않고 착지 지점만 검사</b>하는 것이 설계의 전부다 —
        /// 멀티셀 보스를 고정한 이유(§14.3)가 원판 클리어런스 <b>경로탐색</b>의 부재이므로, 경로를 걷지
        /// 않는 도약에는 그 이유가 성립하지 않는다. 착지 지점을 그대로 <see cref="MonsterTurnPlan.PlannedMoveCoord"/>로
        /// 삼으면 예고 오버레이·연출 footprint·해소가 전부 같은 좌표를 공유하므로 <b>예고=명중 계약이 자동 유지된다</b>.
        ///
        /// 거리 창은 <b>착지 지점에서</b> 잰다: 도약 패턴은 "붙어서 내려찍는" 근접기라 distMax가 짧은데,
        /// 현재 좌표로 재면 정작 멀 때 후보에서 빠져 도약이 영원히 안 나온다.
        /// </summary>
        private bool TryPlanLeapAttack(MonsterRuntime monster, HexCoord leapOrigin, out HexCoord landing, out int patternIndex)
        {
            return TryPlanLeapAttack(monster, leapOrigin, out landing, out patternIndex, out _);
        }

        /// <summary>
        /// <paramref name="reason"/>은 <b>실패했을 때만</b> 채워지는 진단 문자열이다(성공 시 빈 문자열).
        /// 도약은 조건이 여섯 갈래라 "왜 안 뛰는가"를 눈으로 알 수 없어서, 랩이 그걸 물어볼 창구를 둔다.
        /// 계획 핫패스는 <c>out _</c> 오버로드를 쓰므로 문자열을 만들지 않는다.
        /// </summary>
        private bool TryPlanLeapAttack(
            MonsterRuntime monster, HexCoord leapOrigin, out HexCoord landing, out int patternIndex, out string reason)
        {
            landing = leapOrigin;
            patternIndex = -1;
            reason = string.Empty;
            if (monster?.AttackPatterns == null || PlayerIsDead)
            {
                reason = "보스가 없거나 플레이어가 죽었다.";
                return false;
            }

            if (context.IsMonsterAttackBlocked(monster))
            {
                reason = "기절 상태 — 공격 자체가 막혀 있다.";
                return false;
            }

            if (context.IsMonsterMovementRooted(monster))
            {
                reason = "속박/섬광 장판 — 몸이 묶여 있어 도약도 막힌다(걷기 금지와는 다른 축이다).";
                return false;
            }

            // 걸어서(또는 제자리에서) 이미 덮을 수 있으면 뛰지 않는다. 매 턴 뛰면 플레이어의 위치 선점이
            // 아무 의미가 없어지고, 도약이 "다가오는 압박"이 아니라 상시 이동 수단이 되어 버린다.
            if (AnyAttackPatternCanCoverPlayerFrom(monster, leapOrigin))
            {
                reason = "이미 지금 자리에서 플레이어를 덮는다 — 도약은 최후 수단이라 이때는 안 뛴다. 더 멀리 설 것.";
                return false;
            }

            var unlocked = PhaseUnlockedPatterns(monster);
            var leapPatterns = unlocked
                .Where(candidate => candidate.Pattern.LeapRange > 0)
                .Where(candidate => !monster.IsAttackPatternOnCooldown(candidate.Index))
                .ToList();
            if (leapPatterns.Count == 0)
            {
                var lockedByPhase = monster.AttackPatterns.Any(pattern => pattern.LeapRange > 0)
                    && !unlocked.Any(candidate => candidate.Pattern.LeapRange > 0);
                reason = lockedByPhase
                    ? "도약 패턴이 현재 페이즈 게이트에서 잠겨 있다 — 페이즈를 올릴 것."
                    : monster.AttackPatterns.Any(pattern => pattern.LeapRange > 0)
                        ? "도약 패턴이 쿨다운 중이다 — 턴을 넘길 것."
                        : "이 보스에게 도약 패턴(leapRange > 0)이 저작되어 있지 않다.";
                return false;
            }

            var footprint = context.GetMonsterFootprintRadius(monster);
            var ownCells = new HashSet<HexCoord>(HexArea.CellsWithin(monster.Coord, footprint));

            // 착지는 플레이어에게 최대한 붙는다(§28 W7 — 가장자리 거리 1, 막히면 2…). 이전 기준
            // (보스 기준 최단 도약)은 커버만 되면 공격 범위 끝자락에 멈춰 "닿기만 하고 안 오는" 그림이
            // 됐다. 동률은 도약 거리 짧은 순 → 좌표 순 → 패턴 순으로 끊어 재현 가능하게 한다.
            var bestKey = (int.MaxValue, int.MaxValue, 0, 0, int.MaxValue);
            var found = false;
            foreach (var candidate in leapPatterns)
            {
                foreach (var coord in HexArea.CellsWithin(monster.Coord, candidate.Pattern.LeapRange))
                {
                    if (coord == monster.Coord
                        || !IsPatternInDistanceBand(monster, coord, candidate.Pattern)
                        || !CanAttackPatternCoverPlayerFrom(monster, coord, candidate.Pattern)
                        || !IsLeapLandingClear(monster, coord, footprint, ownCells))
                    {
                        continue;
                    }

                    var edgeDistanceToPlayer = Math.Max(0, coord.DistanceTo(PlayerCoord) - footprint);
                    var key = (edgeDistanceToPlayer, monster.Coord.DistanceTo(coord), coord.Q, coord.R, candidate.Index);
                    if (key.CompareTo(bestKey) >= 0)
                    {
                        continue;
                    }

                    bestKey = key;
                    landing = coord;
                    patternIndex = candidate.Index;
                    found = true;
                }
            }

            if (!found)
            {
                reason = "도약 사거리 안에 유효한 착지가 없다 — 원판이 통째로 들어가면서 거기서 플레이어를 "
                    + "덮는 칸이 필요하다(너무 멀거나, 착지 자리가 막혀 있다).";
            }

            return found;
        }

        /// <summary>
        /// 이 보스가 <b>지금</b> 도약할 수 있는가, 없다면 왜 없는가(랩 진단용 · §18).
        /// 계획을 커밋하지 않는다 — 같은 술어를 같은 순서로 다시 물을 뿐이다.
        /// </summary>
        /// <summary>
        /// 이 몬스터의 패턴마다 <b>지금 왜 후보인가/아닌가</b>를 한 줄씩(§25 · 랩 표시용).
        ///
        /// <para>🔑 게이트 술어를 <b>복제하지 않는다</b> — 실제 선택기가 쓰는 같은 함수
        /// (<see cref="PhaseUnlockedPatterns"/>·<see cref="IsPatternInDistanceBand"/>·
        /// <c>IsAttackPatternOnCooldown</c>·<see cref="CanAttackPatternCoverPlayerFrom"/>)를 그대로 묻는다.
        /// §18.4가 도약 진단에서 세운 규칙이다: 술어를 복제하면 랩과 실게임이 갈라진다.</para>
        ///
        /// <para>기준 좌표는 <b>이번 턴 계획된 이동 지점</b>이다 — 선택기가 그 자리에서 고르므로
        /// (§15.4) 현재 좌표로 재면 랩이 실제와 다른 답을 준다.</para>
        /// </summary>
        internal IReadOnlyList<string> DescribeAttackPatternGates(MonsterRuntime monster)
        {
            if (monster?.AttackPatterns == null || monster.AttackPatterns.Length == 0)
            {
                return new[] { "공격 패턴이 없다." };
            }

            var origin = monster.TurnPlan.IsActive ? monster.TurnPlan.PlannedMoveCoord : monster.Coord;
            var phaseGate = context.GetMonsterPatternPhaseGate(monster);
            var unlocked = new HashSet<int>(PhaseUnlockedPatterns(monster).Select(candidate => candidate.Index));
            var lines = new List<string>(monster.AttackPatterns.Length);

            for (var index = 0; index < monster.AttackPatterns.Length; index++)
            {
                var pattern = monster.AttackPatterns[index];
                var head = $"{pattern.Id} {pattern.DisplayName} · {(string.IsNullOrEmpty(pattern.ShapeId) ? "single" : pattern.ShapeId)}"
                           + $" · 거리 {pattern.DistMin}~{(pattern.DistMax == int.MaxValue ? "∞" : pattern.DistMax.ToString())}"
                           + $" · 페이즈 {pattern.PhaseMin}~{(pattern.PhaseMax == int.MaxValue ? "∞" : pattern.PhaseMax.ToString())}"
                           + $" · 쿨 {pattern.CooldownTurns}"
                           + (pattern.LeapRange > 0 ? $" · 도약 {pattern.LeapRange}" : string.Empty);

                string verdict;
                if (index == monster.AttackPatternIndex)
                {
                    verdict = monster.ForcedAttackPatternIndex == index ? "★ 강제 커밋됨" : "★ 이번 턴 선택됨";
                }
                else if (!unlocked.Contains(index))
                {
                    verdict = $"페이즈 게이트 밖 (현재 게이트 {phaseGate})";
                }
                else if (monster.IsAttackPatternOnCooldown(index))
                {
                    verdict = "쿨다운 중";
                }
                else if (!IsPatternInDistanceBand(monster, origin, pattern))
                {
                    verdict = "거리 창 밖";
                }
                else if (!CanAttackPatternCoverPlayerFrom(monster, origin, pattern))
                {
                    verdict = "후보이나 플레이어를 못 덮음";
                }
                else
                {
                    verdict = "후보 (추첨 대상)";
                }

                var line = $"{head}\n      → {verdict}";

                // 도약형 패턴(leapRange > 0)은 도약 상태를 덧붙인다(§28 후속 T3). 도약은 패턴이 아니라
                // 패턴의 <b>이동 속성</b>이라 위 게이트 판정에 안 나타나고, 그래서 "커밋했는데 왜 안
                // 뛰나"가 화면만 봐서는 풀리지 않았다. 술어는 복제하지 않는다(§18.4) —
                // <see cref="DescribeLeapEligibility"/>가 실제 계획 함수를 그대로 다시 묻는다.
                if (pattern.LeapRange > 0)
                {
                    var leapState = monster.PlannedLeap
                        ? $"이번 턴 도약 예정 → {monster.TurnPlan.PlannedMoveCoord}"
                        : DescribeLeapEligibility(monster);
                    line += $"\n      → 도약 상태: {leapState}\n      → 도약은 위치 관계로만 발동한다 — 아래 「도약」 절의 「도약 조건 만들기」로 확인할 것.";
                }

                lines.Add(line);
            }

            return lines;
        }

        /// <summary>
        /// 패턴 형상 미리보기(§25 · 랩 표시용). 게이트 진단과 같은 원점(이번 턴 계획 지점)과
        /// 실제 조준 방향(원점→플레이어)으로 <see cref="AttackShapeLibrary.GetAffectedCells"/>를
        /// 그대로 편다 — 형상 계산을 랩에 복제하지 않는다(§18.4). shape 없는 패턴은 그릴 것이 없다.
        /// </summary>
        internal bool TryDescribeAttackPatternShape(
            MonsterRuntime monster,
            int patternIndex,
            out string shapeId,
            out HexCoord origin,
            out HexDirection attackDirection,
            out int footprintRadius,
            out IReadOnlyList<HexCoord> affectedCells)
        {
            shapeId = string.Empty;
            origin = default;
            attackDirection = default;
            footprintRadius = 0;
            affectedCells = null;
            if (monster?.AttackPatterns == null || patternIndex < 0 || patternIndex >= monster.AttackPatterns.Length)
            {
                return false;
            }

            var pattern = monster.AttackPatterns[patternIndex];
            if (string.IsNullOrEmpty(pattern.ShapeId))
            {
                return false;
            }

            shapeId = pattern.ShapeId;
            origin = monster.TurnPlan.IsActive ? monster.TurnPlan.PlannedMoveCoord : monster.Coord;
            footprintRadius = context.GetMonsterFootprintRadius(monster);
            attackDirection = origin.ApproximateDirection(PlayerCoord);
            affectedCells = AttackShapeLibrary
                .GetAffectedCells(pattern.ShapeId, origin, attackDirection, context.GetMonsterBody(monster))
                .ToList();
            return true;
        }

        /// <summary>
        /// 이 패턴이 <b>지금 추첨 후보인가</b>(§25 · 랩 전용). 「조건 만들기」가 후보 칸을 실제로 대입한 뒤
        /// 이 술어로 판정한다 — 선택은 가중치 추첨이라 "뽑혔는가"로 물으면 조건이 맞아도 실패로 읽힌다.
        ///
        /// <para>🔑 선택기가 쓰는 함수를 그대로 쓴다(복제 금지 · §18.4).</para>
        /// </summary>
        internal bool IsAttackPatternCandidateNow(MonsterRuntime monster, int patternIndex, out string reason)
        {
            reason = string.Empty;
            if (monster?.AttackPatterns == null || patternIndex < 0 || patternIndex >= monster.AttackPatterns.Length)
            {
                reason = "패턴 인덱스가 범위 밖이다.";
                return false;
            }

            if (!PhaseUnlockedPatterns(monster).Any(candidate => candidate.Index == patternIndex))
            {
                reason = "페이즈 게이트에 잠겨 있다 — 위치로는 풀 수 없다. 페이즈를 올릴 것.";
                return false;
            }

            if (monster.IsAttackPatternOnCooldown(patternIndex))
            {
                reason = "쿨다운 중이다 — 위치로는 풀 수 없다. 턴을 넘길 것.";
                return false;
            }

            var origin = monster.TurnPlan.IsActive ? monster.TurnPlan.PlannedMoveCoord : monster.Coord;
            var pattern = monster.AttackPatterns[patternIndex];
            if (!IsPatternInDistanceBand(monster, origin, pattern))
            {
                reason = "거리 창 밖이다.";
                return false;
            }

            if (!CanAttackPatternCoverPlayerFrom(monster, origin, pattern))
            {
                reason = "거리 창 안이지만 형상이 플레이어를 덮지 못한다.";
                return false;
            }

            return true;
        }

        internal string DescribeLeapEligibility(MonsterRuntime monster)
        {
            if (monster == null)
            {
                return "보스가 없다.";
            }

            var leapOrigin = context.IsMonsterMovementBlocked(monster)
                ? monster.Coord
                : monster.TurnPlan.IsActive ? monster.TurnPlan.PlannedMoveCoord : monster.Coord;
            return TryPlanLeapAttack(monster, leapOrigin, out var landing, out var index, out var reason)
                ? $"가능 — 착지 {landing} · 패턴 {monster.AttackPatterns[index].Id} {monster.AttackPatterns[index].DisplayName}"
                : reason;
        }

        /// <summary>
        /// 착지 지점에 <b>원판 전체</b>가 들어가는가. 자기 몸이 지금 깔고 있는 칸은 비운 것으로 친다
        /// (뛰고 나면 비므로). 플레이어 칸은 절대 착지 대상이 아니다 — 삼키는 사고를 도약으로 만들지 않는다.
        /// </summary>
        private bool IsLeapLandingClear(MonsterRuntime monster, HexCoord center, int footprint, ISet<HexCoord> ownCells)
        {
            foreach (var coord in HexArea.CellsWithin(center, footprint))
            {
                if (coord == PlayerCoord
                    || !Map.TryGetCell(coord, out var cell)
                    || !cell.BaseWalkable
                    || !terrainTraits.IsWalkable(cell.TerrainTypeId)
                    || Map.HasMovementBlockingObject(coord))
                {
                    return false;
                }

                if (!ownCells.Contains(coord) && runtimeStates.ContainsKey(coord))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 해소부가 착지 직전에 다시 부르는 검증(계획과 해소 사이에 플레이어가 움직였을 수 있다).
        /// 계획부와 <b>같은 술어</b>를 써야 예고=명중 계약이 깨지지 않는다.
        /// </summary>
        internal bool IsLeapLandingStillClear(MonsterRuntime monster, HexCoord center)
        {
            if (monster == null)
            {
                return false;
            }

            var footprint = context.GetMonsterFootprintRadius(monster);
            var ownCells = new HashSet<HexCoord>(HexArea.CellsWithin(monster.Coord, footprint));
            return IsLeapLandingClear(monster, center, footprint, ownCells);
        }

        // ---------------------------------------------------------------------------------------
        // 이동 목적지 선택 (추격 / 탐색 / 귀환 / 순찰)
        // ---------------------------------------------------------------------------------------

        private HexCoord? ChooseEnemyMovementStep(MonsterRuntime monster, EnemyIntentType intentType, ISet<HexCoord> reservedDestinations = null)
        {
            if (intentType == EnemyIntentType.Chase)
            {
                var attackSetupStep = ChooseAttackSetupStep(monster, reservedDestinations);
                if (attackSetupStep.HasValue)
                {
                    return attackSetupStep;
                }
            }

            HexCoord destination;
            switch (intentType)
            {
                case EnemyIntentType.Return:
                    destination = monster.SpawnCoord;
                    break;
                case EnemyIntentType.Search:
                    destination = monster.FsmMemory.LastKnownPlayerCoord ?? PlayerCoord;
                    break;
                case EnemyIntentType.Patrol:
                    return ChoosePatrolStep(monster);
                default:
                    destination = PlayerCoord;
                    break;
            }

            if (destination == monster.Coord)
            {
                return null;
            }

            var statesForMovement = runtimeStates.ToDictionary(pair => pair.Key, pair => pair.Value);
            statesForMovement.Remove(destination);
            if (intentType == EnemyIntentType.Chase || destination == PlayerCoord)
            {
                statesForMovement.Remove(PlayerCoord);
            }
            AddReservedDestinationBlocks(statesForMovement, reservedDestinations, monster.Coord);

            var maxRange = Map.AllCells.Count();
            var path = HexPathfinder.FindPath(
                Map,
                BuildMovementQuery(monster, maxRange),
                destination,
                statesForMovement,
                terrainTraits);

            // No full path (e.g. walled off by a field object): still press toward the destination as far
            // as reachable this turn, instead of freezing in place.
            if (path.Count < 2)
                return ChooseGreedyApproachStep(monster, destination, statesForMovement);

            // 0 하한: 이동력 0(고정 포탑, D-5)은 경로를 세워도 한 칸도 나아가지 않는다.
            var stepBudget = Math.Max(0, monster.MovePerTurn);
            var stepIndex = Math.Min(stepBudget, path.Count - 1);
            // Multi-step monsters never project onto the player's tile; they stop on the tile
            // adjacent to the player. A single-step move onto the player tile (stepIndex == 1)
            // is left intact so the existing chase collision bump still fires.
            if (stepIndex > 1 && path[stepIndex] == PlayerCoord)
                stepIndex = path.Count - 2 >= 1 ? path.Count - 2 : 1;

            stepIndex = BackOffUntilFootprintClearsPlayer(monster, path, stepIndex);
            return path[stepIndex];
        }

        // Fallback when no full path to the destination exists: pick the reachable tile (this turn's move
        // budget) closest to the destination ??but only if it actually gets closer ??so a blocked monster
        // advances as far as it can rather than freezing. Never steps onto the player's tile.
        private HexCoord? ChooseGreedyApproachStep(MonsterRuntime monster, HexCoord destination, IReadOnlyDictionary<HexCoord, HexCellRuntimeState> statesForMovement)
        {
            // 0 하한: 이동력 0(고정 포탑, D-5)은 경로를 세워도 한 칸도 나아가지 않는다.
            var stepBudget = Math.Max(0, monster.MovePerTurn);
            var reachable = HexPathfinder.GetReachableCells(
                Map,
                BuildMovementQuery(monster, stepBudget),
                statesForMovement,
                terrainTraits);
            if (reachable.Count == 0)
            {
                return null;
            }

            var best = reachable.Keys
                .Where(coord => coord != PlayerCoord)
                .Where(coord => !WouldFootprintCoverPlayer(monster, coord))
                .OrderBy(coord => coord.DistanceTo(destination))
                .ThenBy(coord => coord.Q)
                .ThenBy(coord => coord.R)
                .Cast<HexCoord?>()
                .FirstOrDefault();
            if (!best.HasValue)
            {
                return null;
            }

            if (best.Value.DistanceTo(destination) >= monster.Coord.DistanceTo(destination))
            {
                return null;
            }

            return best;
        }

        private static void AddReservedDestinationBlocks(
            IDictionary<HexCoord, HexCellRuntimeState> statesForMovement,
            ISet<HexCoord> reservedDestinations,
            HexCoord currentCoord)
        {
            if (statesForMovement == null || reservedDestinations == null)
            {
                return;
            }

            foreach (var reserved in reservedDestinations)
            {
                if (reserved == currentCoord)
                {
                    continue;
                }

                statesForMovement[reserved] = new HexCellRuntimeState(null, temporaryBlocked: true);
            }
        }

        /// <summary>
        /// 이 몬스터의 이동 질의. 원판 클리어런스(§21.3)를 태우는 <b>단일 지점</b>이다 — 경로탐색 호출부가
        /// 여섯 곳이라 각자 반경을 넘기게 두면 그중 하나는 반드시 빠지고, 그러면 계획과 해소가 갈라진다.
        /// </summary>
        private MovementQuery BuildMovementQuery(MonsterRuntime monster, int movePoints, bool includeStart = false)
        {
            return new MovementQuery(
                monster.Coord,
                movePoints,
                includeStart,
                monster.Id,
                context.GetMonsterFootprintRadius(monster),
                context.GetMonsterFootprintOffsets(monster));
        }

        /// <summary>
        /// 이 칸에 섰을 때 <b>몸이 플레이어를 덮는가</b>. 반경 0이면 "그 칸이 곧 플레이어 칸인가"와 같아
        /// 기존 동작 그대로다(플레이어 칸 진입은 충돌 넉백 경로가 따로 다룬다).
        /// </summary>
        private bool WouldFootprintCoverPlayer(MonsterRuntime monster, HexCoord coord)
        {
            if (coord.DistanceTo(PlayerCoord) <= context.GetMonsterFootprintRadius(monster))
            {
                return true;
            }

            // 형상 footprint(삼각형 정예): 오프셋 중 하나가 플레이어 칸이면 몸이 덮는다.
            var offsets = context.GetMonsterFootprintOffsets(monster);
            if (offsets != null)
            {
                for (var i = 0; i < offsets.Count; i++)
                {
                    if (coord + offsets[i] == PlayerCoord)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 멀티셀 몬스터는 <b>플레이어를 삼키지 않는다</b> — 원판이 플레이어를 덮지 않는 마지막 칸까지
        /// 물러선다. 인접 링은 모든 공격 shape에 항상 포함되므로 여기까지 와도 공격은 성립한다.
        ///
        /// <para>🔑 이것을 경로탐색 쪽 하드 조건으로 만들지 않은 이유: 추격 경로는 플레이어 칸을 점유에서
        /// <b>빼고</b> 탐색한다(안 그러면 목적지가 막혀 매 턴 그리디 폴백으로 떨어진다). 그래서 "덮지
        /// 않는다"는 경로가 아니라 <b>어디서 멈추는가</b>의 규칙이고, 여기 한 줄이 그 자리다.</para>
        /// </summary>
        private int BackOffUntilFootprintClearsPlayer(MonsterRuntime monster, IReadOnlyList<HexCoord> path, int stepIndex)
        {
            if (context.GetMonsterFootprintRadius(monster) <= 0)
            {
                return stepIndex;
            }

            while (stepIndex > 0 && WouldFootprintCoverPlayer(monster, path[stepIndex]))
            {
                stepIndex--;
            }

            return stepIndex;
        }

        private HexCoord? ChooseAttackSetupStep(MonsterRuntime monster, ISet<HexCoord> reservedDestinations = null)
        {
            if (monster == null)
            {
                return null;
            }

            return monster.Coord
                .NeighborsInDirectionOrder()
                .Where(coord => coord != PlayerCoord)
                .Where(coord => !WouldFootprintCoverPlayer(monster, coord))
                .Where(coord => reservedDestinations == null || !reservedDestinations.Contains(coord))
                .Where(coord => HexPathfinder.CanEnter(Map, monster.Coord, coord, BuildMovementQuery(monster, 1), runtimeStates, terrainTraits, out _))
                .Where(coord => AnyAttackPatternCanCoverPlayerFrom(monster, coord))
                .OrderBy(coord => coord.DistanceTo(PlayerCoord))
                .ThenBy(coord => coord.Q)
                .ThenBy(coord => coord.R)
                .Cast<HexCoord?>()
                .FirstOrDefault();
        }

        private HexCoord? ChoosePatrolStep(MonsterRuntime monster)
        {
            if (monster.PatrolArea == null || monster.PatrolArea.Count == 0)
            {
                return null;
            }

            var allowed = new HashSet<HexCoord>(monster.PatrolArea);
            if (!allowed.Contains(monster.Coord))
            {
                return ChooseStepTowardPatrolArea(monster, allowed);
            }

            var candidates = monster.Coord
                .NeighborsInDirectionOrder()
                .Where(coord => allowed.Contains(coord))
                .Where(coord => HexPathfinder.CanEnter(Map, monster.Coord, coord, BuildMovementQuery(monster, 1), runtimeStates, terrainTraits, out _))
                .OrderBy(coord => DistanceFromPatrolCursor(monster, coord))
                .ThenBy(coord => coord.Q)
                .ThenBy(coord => coord.R)
                .ToList();

            if (candidates.Count == 0)
            {
                return null;
            }

            var selected = candidates[0];
            monster.FsmMemory.PatrolCursor = NextPatrolCursor(monster, selected);
            return selected;
        }

        private HexCoord? ChooseStepTowardPatrolArea(MonsterRuntime monster, ISet<HexCoord> allowed)
        {
            var destination = allowed
                .OrderBy(coord => coord.DistanceTo(monster.Coord))
                .ThenBy(coord => coord.Q)
                .ThenBy(coord => coord.R)
                .FirstOrDefault();
            if (destination == default && !allowed.Contains(destination))
            {
                return null;
            }

            var maxRange = Map.AllCells.Count();
            var path = HexPathfinder.FindPath(
                Map,
                BuildMovementQuery(monster, maxRange),
                destination,
                runtimeStates,
                terrainTraits);

            return path.Count >= 2 ? path[1] : (HexCoord?)null;
        }

        private static int DistanceFromPatrolCursor(MonsterRuntime monster, HexCoord coord)
        {
            var index = IndexOfPatrolCoord(monster, coord);
            if (index < 0)
            {
                return int.MaxValue;
            }

            var count = monster.PatrolArea.Count;
            var cursor = ((monster.FsmMemory.PatrolCursor % count) + count) % count;
            return index >= cursor ? index - cursor : count - cursor + index;
        }

        private static int NextPatrolCursor(MonsterRuntime monster, HexCoord coord)
        {
            var index = IndexOfPatrolCoord(monster, coord);
            return index < 0 || monster.PatrolArea.Count == 0 ? 0 : (index + 1) % monster.PatrolArea.Count;
        }

        private static int IndexOfPatrolCoord(MonsterRuntime monster, HexCoord coord)
        {
            for (var i = 0; i < monster.PatrolArea.Count; i++)
            {
                if (monster.PatrolArea[i] == coord)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}

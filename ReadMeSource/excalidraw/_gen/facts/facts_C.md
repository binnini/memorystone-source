# VERIFIED FACT SHEET — 몬스터 AI: 계획·예고·해소

Repo root: /Users/yebin/game/memorystone-source (read-only)

## C1. Source/Combat/Runtime/MonsterAiPlanner.cs

File header comment (lines 9-24), verbatim:

```
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
```

Note: the "①~⑥" stage list appears inline in this doc comment (lines 10-12) — there is no separate standalone "①~⑥" block elsewhere in the file. This is the full verbatim text of the class header.

Public/internal methods with signatures + line numbers:
- internal MonsterAiPlanner(IMonsterPlanningContext context, int attackPatternSeed = 0) — line 36
- internal int AttackPatternRngCursor (property) — line 47
- internal int DamageJitterRngCursor (property) — line 50
- internal void RestoreRngCursors(int attackPatternCursor, int damageJitterCursor) — line 55
- internal void RefreshAllIntents(bool preserveCommittedAttackRolls = false) — line 102
- internal void RefreshTurnPlan(MonsterRuntime monster, ISet<HexCoord> reservedDestinations = null, bool preserveCommittedAttackRolls = false) — line 139
- internal void ApplyAlert(MonsterRuntime monster, int rangeBonus, int durationTurns) — line 206
- internal static EnemyIntent SelectMovementIntent(MonsterFsmContext ctx, MonsterFsmMemory memory, string behaviorProfileRef) — line 251
- internal static int GetEffectiveChaseRange(MonsterFsmContext ctx, MonsterFsmMemory memory) — line 323
- internal static int GetEffectiveDisengageRange(MonsterFsmContext ctx, MonsterFsmMemory memory) — line 330
- internal static EnemyIntentType ToIntentType(MonsterFsmState state) — line 350
- internal static bool IsMovementIntent(EnemyIntentType type) — line 437
- internal void ApplyControlStatusConstraintToPlan(MonsterRuntime monster) — line 455
- internal bool IsActiveMonsterAttackAreaCoveringPlayer(MonsterRuntime monster, HexCoord origin) — line 729
- internal bool AnyAttackPatternCoversPlayerFrom(MonsterRuntime monster, HexCoord origin) — line 758
- internal IReadOnlyList<string> DescribeAttackPatternGates(MonsterRuntime monster) — line 912
- internal bool TryDescribeAttackPatternShape(MonsterRuntime monster, int patternIndex, out string shapeId, out HexCoord origin, out HexDirection attackDirection, out int footprintRadius, out IReadOnlyList<HexCoord> affectedCells) — line 984
- internal bool IsAttackPatternCandidateNow(MonsterRuntime monster, int patternIndex, out string reason) — line 1025
- internal string DescribeLeapEligibility(MonsterRuntime monster) — line 1063
- internal bool IsLeapLandingStillClear(MonsterRuntime monster, HexCoord center) — line 1108

FindPath usage: HexPathfinder.FindPath is called in ChooseEnemyMovementStep (line 1165) and ChooseStepTowardPatrolArea (line 1380).

How reservedDestinations / temporaryBlocked is passed:
- RefreshAllIntents builds a HashSet<HexCoord> reservedDestinations (line 115) and, per monster in MonsterActionOrder(), calls RefreshTurnPlan(monster, reservedDestinations, preserveCommittedAttackRolls) (line 131), then adds monster.TurnPlan.PlannedMoveCoord to the set if TurnPlan.IsActive (lines 132-135).
- RefreshTurnPlan forwards reservedDestinations into ChooseEnemyMovementStep(monster, movementIntent.Type, reservedDestinations) (line 149).
- ChooseEnemyMovementStep forwards it to ChooseAttackSetupStep(monster, reservedDestinations) (line 1128) and to AddReservedDestinationBlocks(statesForMovement, reservedDestinations, monster.Coord) (line 1162).
- AddReservedDestinationBlocks (static, lines 1228-1247) marks each reserved coord (except the monster's own current coord) in statesForMovement as new HexCellRuntimeState(null, temporaryBlocked: true) (line 1245), then that dictionary is fed into HexPathfinder.FindPath (line 1169).

Pattern selection: method SelectWeightedAttackPattern (line 695) calls SelectWeightedAttackPatternIndex (line 704), which draws monsterAttackPatternRng.Next(totalWeight) (line 712). The csv name source is monster_attack_patterns.csv (confirmed by MonsterCatalogCsv.cs:835 — CsvTable.Parse(source.AttackPatternsCsv, "monster_attack_patterns.csv")). The RNG/seed stream used is monsterAttackPatternRng (field, line 30), a CountingRandom seeded by attackPatternSeed (line 40) — described in the field comment as "스트림 4 커서(패턴 선택)" (line 46). A separate stream attackDamageJitterRng seeded by DamageJitterSeed(attackPatternSeed) (line 41; formula at line 44: unchecked(attackPatternSeed * 31 + 17)) handles damage jitter only ("스트림 4'", line 49), consumed inside CommitAttackPattern (line 75).

Leap/도약 step: section header comment "도약 공격 (§17)" at line 783. Core method TryPlanLeapAttack(MonsterRuntime monster, HexCoord leapOrigin, out HexCoord landing, out int patternIndex, out string reason) — line 805 (overload without reason at line 795). Called from RefreshTurnPlan at line 161 with leapOrigin computed at line 160 (context.IsMonsterMovementBlocked(monster) ? monster.Coord : plannedMove). If successful and not in preserve mode: plannedMove = landing; CommitAttackPattern(monster, leapPatternIndex); monster.PlannedLeap = true; (lines 175-177).

How MonsterRuntime.TurnPlan is committed: in RefreshTurnPlan, lines 191-196:
```
monster.TurnPlan = new MonsterTurnPlan(
    movementIntent,
    plannedMove,
    attackFacingIntent,
    true,
    canAttack: true);
```
followed by ApplyControlStatusConstraintToPlan(monster) (line 199), which can override the commit via SetMonsterWaitingIntent (line 465) or SetMonsterStationaryAttackIntent (line 482/528).

MonsterTurnPlan type fields (Source/Combat/Runtime/MonsterRuntime.cs, struct at line 241):
```
241	    internal readonly struct MonsterTurnPlan
242	    {
243	        public MonsterTurnPlan(
244	            EnemyIntent movementIntent,
245	            HexCoord plannedMoveCoord,
246	            EnemyIntent attackFacingIntent,
247	            bool isActive,
248	            bool canAttack)
249	        {
250	            MovementIntent = movementIntent;
251	            PlannedMoveCoord = plannedMoveCoord;
252	            AttackFacingIntent = attackFacingIntent;
253	            IsActive = isActive;
254	            CanAttack = canAttack;
255	        }
256	
257	        public EnemyIntent MovementIntent { get; }
258	        public HexCoord PlannedMoveCoord { get; }
259	        public EnemyIntent AttackFacingIntent { get; }
260	        public bool IsActive { get; }
261	        public bool CanAttack { get; }
```
Fields: MovementIntent (EnemyIntent), PlannedMoveCoord (HexCoord), AttackFacingIntent (EnemyIntent), IsActive (bool), CanAttack (bool). Also has static MonsterTurnPlan Inactive(HexCoord coord) (line 263) and instance method WithResolvedMove(HexCoord resolvedCoord, bool canAttack) (line 269).

## C2. IMonsterPlanningContext

File: Source/Combat/Runtime/IMonsterPlanningContext.cs (interface declared line 13).

Member list (all in this file):
- HexMapData Map { get; } — line 15
- HexCoord PlayerCoord { get; } — line 17
- CombatConfig Config { get; } — line 19
- HexTerrainTraits TerrainTraits { get; } — line 21
- IReadOnlyDictionary<HexCoord, HexCellRuntimeState> RuntimeStates { get; } — line 23
- bool IsPlayerDead { get; } — line 25
- IReadOnlyList<MonsterRuntime> PlanningMonsters { get; } — line 28
- IEnumerable<MonsterRuntime> MonsterActionOrder() — line 31
- MonsterActivityState ClassifyMonsterActivity(MonsterRuntime monster) — line 33
- bool IsMonsterMovementBlocked(MonsterRuntime monster) — line 35
- bool IsMonsterMovementRooted(MonsterRuntime monster) — line 41
- bool IsMonsterAttackBlocked(MonsterRuntime monster) — line 43
- int GetMonsterPatternPhaseGate(MonsterRuntime monster) — line 50
- bool IsAttackPatternSuppressed(MonsterRuntime monster, MonsterAttackPattern pattern) — line 57
- int GetMonsterFootprintRadius(MonsterRuntime monster) — line 64
- IReadOnlyList<HexCoord> GetMonsterFootprintOffsets(MonsterRuntime monster) => null (default impl) — line 70
- MonsterBodyShape GetMonsterBody(MonsterRuntime monster) => new MonsterBodyShape(...) (default impl) — line 73

Distance/detection predicates, PlayerHidden, profile:
- bool IsPlayerHiddenFromMonsters => false (default) — line 77 (은신 — 전 몬스터에게 안 보임)
- bool IsMonsterSenseBlindedAt(HexCoord monsterCoord) => false (default) — line 80 (실명 — 이 몬스터만)
- string GetBehaviorProfileRef(MonsterRuntime monster) => MonsterBehaviorProfileRegistry.DefaultProfileRef (default) — line 86

(No literal member named "PlayerHidden"/"profile" on this interface itself — those live on MonsterFsmContext, built by MonsterAiPlanner.CreateMonsterFsmContext at lines 373-386 from IsPlayerHiddenFromMonsters + IsMonsterSenseBlindedAt.)

## C3. MonsterFsmMemory

File: Source/Combat/Runtime/MonsterFsmMemory.cs. Class MonsterFsmMemory (line 5), properties: State, PreAlertState, LastKnownPlayerCoord, SearchTurnsRemaining, AlertTurnsRemaining, AlertRangeBonus, PatrolCursor (lines 7-13), plus Clone() (line 15).

State enum values are defined in a separate file: Source/Combat/Runtime/MonsterFsmState.cs, lines 3-11:
```
    public enum MonsterFsmState
    {
        Patrol,
        Chase,
        Attack,
        Search,
        Alert,
        Return
    }
```

## C4. CombatState.MonsterAi.cs

File: Source/Combat/Runtime/CombatState.MonsterAi.cs.

- GetMonsterIntentPreviews — NOT in this file; actually defined in Source/Combat/Runtime/CombatState.cs:669: public IReadOnlyList<MonsterIntentPreview> GetMonsterIntentPreviews(bool includeUnrevealed = false)
- ResolveMonsterMovementStep — NOT in CombatState.MonsterAi.cs either; defined in Source/Combat/Runtime/CombatState.cs:1412: private void ResolveMonsterMovementStep(IReadOnlyDictionary<string, MonsterActionResolutionBuilder> actionRecords = null)
- ResolveMonsterAttackStep — IS in CombatState.MonsterAi.cs:198: private void ResolveMonsterAttackStep(IReadOnlyDictionary<string, MonsterActionResolutionBuilder> actionRecords = null)
- RefreshAllIntents — no wrapper method with this exact name in CombatState.MonsterAi.cs; the file calls the planner via private void RefreshMonsterIntentStep() at line 193, whose body (line 195) is planner.RefreshAllIntents(); (the real RefreshAllIntents implementation lives in MonsterAiPlanner.cs:102, see C1/C6).

Previews are projections, not re-planning — quoted lines (CombatState.cs lines 686-689):
```
                var plan = monster.TurnPlan;
                var currentIntent = plan.IsActive ? plan.MovementIntent : monster.Intent;
                var predictedMove = plan.IsActive ? plan.PlannedMoveCoord : monster.IntentPredictedMoveCoord;
                var attackPattern = monster.CurrentAttackPattern;
```
Confirming comment, also in CombatState.MonsterAi.cs:57-60:
```
            // 은폐된 예고(미지·은신)는 이동 경로도 없다(2026-09-05 실플레이: 어둑시니가 숨어 있는데
            // 이동 예정 잔상이 그려졌다). GetMonsterIntentPreviews는 이미 예측 좌표를 제자리로 되돌려
            // 이동 예고를 지우지만, 이 경로 술어는 예측 좌표를 직접 읽어 다른 답을 냈다 — 같은 은폐
```
And README.md:380 (repo doc) states the same contract explicitly: "예고는 재계산이 아니라 투영. GetMonsterIntentPreviews는 AI를 다시 돌리지 않고 TurnPlan의 목적지와 잠긴 조준을 읽어 집행과 같은 함수로 칸을 폅니다."

## C5. AttackShapeLibrary.cs / AttackShapeDefinition.cs

Both in Source/Combat/Runtime/.

attack_shapes.csv reference: comment in AttackShapeLibrary.cs:16-18: "§27(안 A): 형상 저작 원본은 attack_shapes.csv다 — 이 클래스는 하드코딩 테이블이 아니라 그 카탈로그를 담는 로더이며..." Referenced again via CombatCsvPaths.AttackShapesCsv (line 81).

RotateSteps + (6-dir)%6 expression:
- AttackShapeLibrary.cs:158: var bodySteps = (6 - (int)attackDirection) % 6;
- AttackShapeLibrary.cs:168: var rotationSteps = (6 - (int)attackDirection) % 6; with comment (166-167): "RotateSteps(n) rotates clockwise; direction enum is counter-clockwise, so (6 - d) % 6 clockwise steps align canonical East offsets to direction d."
- Same pattern echoed in CombatState.MonsterAi.cs:852-853: "RotateSteps는 시계 방향, 방향 enum은 반시계라 (6-d)%6 — AttackShapeLibrary와 같은 식이다." → var rotationSteps = (6 - (int)attackDir) % 6;

adjacency 5 values — AttackShapeAdjacency enum in AttackShapeDefinition.cs lines 9-32:
```
    public enum AttackShapeAdjacency
    {
        Full = 0,
        None = 1,
        Open = 2,
        Body = 3,
        BodyShell = 4,
    }
```
Names: Full, None, Open, Body, BodyShell.

## C6. Verbatim line-numbered excerpts

RefreshAllIntents reservation loop — Source/Combat/Runtime/MonsterAiPlanner.cs lines 102-137:
```
102	        internal void RefreshAllIntents(bool preserveCommittedAttackRolls = false)
103	        {
104	            foreach (var deadMonster in monsters.Where(monster => monster.Combatant.IsDead))
105	            {
106	                var intent = new EnemyIntent(EnemyIntentType.Patrol, PlayerCoord.DistanceTo(deadMonster.Coord), deadMonster.Coord, PlayerCoord);
107	                deadMonster.Intent = intent;
108	                deadMonster.LockedFacingIntent = intent;
109	                deadMonster.IntentPredictedMoveCoord = deadMonster.Coord;
110	                deadMonster.PendingAttackIntent = false;
111	                deadMonster.PlannedLeap = false;
112	                deadMonster.TurnPlan = MonsterTurnPlan.Inactive(deadMonster.Coord);
113	            }
114	
115	            var reservedDestinations = new HashSet<HexCoord>();
116	            foreach (var monster in context.MonsterActionOrder())
117	            {
118	                monster.ActivityState = context.ClassifyMonsterActivity(monster);
119	                if (monster.Combatant.IsDead || monster.ActivityState == MonsterActivityState.Dormant)
120	                {
121	                    var intent = new EnemyIntent(EnemyIntentType.Patrol, PlayerCoord.DistanceTo(monster.Coord), monster.Coord, PlayerCoord);
122	                    monster.Intent = intent;
123	                    monster.LockedFacingIntent = intent;
124	                    monster.IntentPredictedMoveCoord = monster.Coord;
125	                    monster.PendingAttackIntent = false;
126	                    monster.PlannedLeap = false;
127	                    monster.TurnPlan = MonsterTurnPlan.Inactive(monster.Coord);
128	                    continue;
129	                }
130	
131	                RefreshTurnPlan(monster, reservedDestinations, preserveCommittedAttackRolls);
132	                if (monster.TurnPlan.IsActive)
133	                {
134	                    reservedDestinations.Add(monster.TurnPlan.PlannedMoveCoord);
135	                }
136	            }
137	        }
```

First ~20 lines of SelectMovementIntent — same file, lines 251-272:
```
251	        internal static EnemyIntent SelectMovementIntent(MonsterFsmContext ctx, MonsterFsmMemory memory, string behaviorProfileRef)
252	        {
253	            memory = memory ?? new MonsterFsmMemory();
254	            var distance = ctx.DistanceToPlayer;
255	
256	            // 매복(요괴 §4-6 ③): 사거리 밖·은신·플레이어 사망이면 아래를 아예 돌리지 않는다(기억 무변경·경계 감쇠 없음).
257	            if (MonsterBehaviorProfileRegistry.IsAmbush(behaviorProfileRef)
258	                && (ctx.PlayerIsDead || ctx.PlayerHidden || distance > MonsterBehaviorProfileRegistry.AmbushRange))
259	            {
260	                return new EnemyIntent(EnemyIntentType.Return, distance, ctx.MonsterCoord, ctx.PlayerCoord);
261	            }
262	
263	            if (memory.State == MonsterFsmState.Alert)
264	            {
265	                memory.State = memory.PreAlertState;
266	            }
267	
268	            MonsterFsmState next;
269	            if (ctx.PlayerIsDead)
270	            {
271	                next = MonsterFsmState.Patrol;
272	            }
```

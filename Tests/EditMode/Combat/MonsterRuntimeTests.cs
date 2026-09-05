using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class MonsterRuntimeTests
    {
        [Test]
        public void MonsterOutsideDetectionRangeRemainsPatrol()
        {
            // Detection is inclusive (distance <= EnemyChaseRange -> Chase), so place the monster
            // one hex beyond the configured boundary instead of hardcoding a distance.
            var outsideDetection = CombatConfig.Default.EnemyChaseRange + 1;
            var state = new CombatState(
                CombatState.CreateDemoMap(outsideDetection),
                new HexCoord(0, 0),
                new HexCoord(outsideDetection, 0),
                CombatConfig.Default);

            Assert.That(state.Monsters[0].Intent.Type, Is.EqualTo(EnemyIntentType.Patrol));
            Assert.That(state.Monsters.Single().Intent.Type, Is.EqualTo(EnemyIntentType.Patrol));
        }

        [Test]
        public void MonsterWithinDetectionRangeRecordsChaseIntent()
        {
            var state = new CombatState(
                CombatState.CreateDemoMap(5),
                new HexCoord(0, 0),
                new HexCoord(5, 0),
                CombatConfig.Default);

            Assert.That(state.Monsters[0].Intent.Type, Is.EqualTo(EnemyIntentType.Chase));
            Assert.That(state.Monsters.Single().Intent.Distance, Is.EqualTo(5));
        }

        [Test]
        public void DetectedMonsterPreviewsChaseStepBeforePlayerAction()
        {
            var state = new CombatState(
                CombatState.CreateDemoMap(4),
                new HexCoord(0, 0),
                new HexCoord(3, 0),
                CombatConfig.Default);

            // The chase step is previewed up front and then resolves in the MonsterMovement phase,
            // so by the time the player acts the monster already sits on the previewed cell.
            Assert.That(state.Monsters.Single().Coord, Is.EqualTo(new HexCoord(3, 0)));
            Assert.That(state.GetMonsterIntentPreviews(includeUnrevealed: true).Single().PredictedMoveCoord, Is.EqualTo(new HexCoord(2, 0)));

            EnterActionPhase(state);

            Assert.That(state.Monsters[0].Coord, Is.EqualTo(new HexCoord(2, 0)));
            Assert.That(state.Monsters.Single().Coord, Is.EqualTo(new HexCoord(2, 0)));
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction));
        }

        [Test]
        public void MonsterDoesNotPreviewStepThroughBlockedOrUnwalkableCells()
        {
            var state = new CombatState(
                CreateLineMapWithBlockedMiddle(),
                new HexCoord(0, 0),
                new HexCoord(2, 0),
                CombatConfig.Default);
            EnterActionPhase(state);

            Assert.That(state.Monsters[0].Coord, Is.EqualTo(new HexCoord(2, 0)));
            Assert.That(state.Monsters.Single().Coord, Is.EqualTo(new HexCoord(2, 0)));
            Assert.That(state.GetMonsterIntentPreviews(includeUnrevealed: true).Single().PredictedMoveCoord, Is.EqualTo(new HexCoord(2, 0)));
        }

        [Test]
        public void AdjacentMonsterRecordsAttackIntentBeforeAttacking()
        {
            var state = new CombatState(
                CombatState.CreateDemoMap(2),
                new HexCoord(0, 0),
                new HexCoord(1, 0),
                CombatConfig.Default);
            EnterActionPhase(state);
            var hpBefore = state.Player.Hp;

            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore));
            Assert.That(state.Monsters[0].Intent.Type, Is.EqualTo(EnemyIntentType.Attack));
            Assert.That(state.Monsters.Single().HasAttackIntent, Is.True);
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction));
        }

        [Test]
        public void MonsterAttacksOnNextMonsterActionWhenAdjacentIntentIsValid()
        {
            var config = CombatConfig.Default;
            var state = new CombatState(
                CombatState.CreateDemoMap(2),
                new HexCoord(0, 0),
                new HexCoord(1, 0),
                config,
                monsterCatalog: CreateConfigMonsterCatalog(config));
            EnterActionPhase(state);
            var hpBefore = state.Player.Hp;

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore - state.Config.EnemyAttackDamage));
            Assert.That(state.Monsters.Single().HasAttackIntent, Is.True);
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerMovement));
        }

        [Test]
        public void MonsterAttackStatusEffectsBecomePlayerActiveEffects()
        {
            var pattern = new MonsterAttackPattern(
                "status-hit",
                "Status Hit",
                1,
                0,
                0,
                statusEffects: new[] { StatusEffectKind.Poison, StatusEffectKind.Slow },
                statusEffectDurationTurns: 4,
                statusEffectAmount: 3);
            var state = CreateShapeOnlyMonsterState(
                CombatState.CreateDemoMap(2),
                new HexCoord(0, 0),
                new HexCoord(1, 0),
                pattern);

            EnterActionPhase(state);
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            var playerEffects = state.ActiveEffects
                .Where(effect => effect.TargetUnitId == "player")
                .ToList();
            Assert.That(playerEffects.Select(effect => effect.Kind), Is.EquivalentTo(new[] { StatusEffectKind.Poison, StatusEffectKind.Slow }));

            var poison = playerEffects.Single(effect => effect.Kind == StatusEffectKind.Poison);
            var slow = playerEffects.Single(effect => effect.Kind == StatusEffectKind.Slow);
            // Fresh status effects keep their first turn-boundary so duration-1 effects survive into
            // the affected actor's next actionable turn.
            Assert.That(poison.RemainingTurns, Is.EqualTo(4), "Fresh Poison keeps its full duration until the next status tick.");
            // Slow is presence-based and was applied during the monster action that just resolved, so it
            // receives a one-turn decrement grace; its full pattern duration survives into the player's
            // upcoming turn (otherwise a duration-1 debuff would expire before it ever gated a player turn).
            Assert.That(slow.RemainingTurns, Is.EqualTo(4), "Presence-based debuffs applied during the monster action keep their full duration for the player's next turn.");
            Assert.That(poison.Amount, Is.EqualTo(StatusEffectInfo.DefaultAmount(StatusEffectKind.Poison)));
            Assert.That(slow.Amount, Is.EqualTo(StatusEffectInfo.DefaultAmount(StatusEffectKind.Slow)));
            Assert.That(state.Player.Hp, Is.EqualTo(state.Config.PlayerMaxHp), "Fresh Poison should not tick immediately at the same turn boundary it was applied.");
        }

        /// <summary>
        /// 🔴 <b>중단 저장이 되감기 창 안에서 일어나면 무엇이 저장되는가.</b>
        ///
        /// 연출이 임팩트에 닿기 전까지 규칙 상태는 <b>공격 전으로 되감겨</b> 있다
        /// (<c>HoldDeferredMonsterActionStateUntilPresentation</c>). 그런데 일시정지 메뉴는
        /// 전투 페이즈·연출 진행 여부를 <b>보지 않고</b>(<c>CombatPauseMenuController.Update</c>의
        /// 게이트는 더미·상점·서비스 모달 셋뿐) 열리고, 거기서 「로비로」가
        /// <c>MainGameplayController.SaveCombatSuspend</c>를 부른다.
        /// 그 가드는 <c>state.IsTerminal</c>인데 되감긴 상태에서는 플레이어가 살아 있어 통과한다.
        ///
        /// 즉 몬스터 연출 도중에 나가면 <b>그 공격이 없던 일이 된 세이브</b>가 남을 수 있다.
        /// <c>SaveCombatSuspend</c>의 주석은 「턴 경계라 상태가 이미 정리돼 있다」고 단언하지만,
        /// 되감기 창은 정확히 그 가정을 깨는 구간이다.
        /// </summary>
        [Test]
        public void SuspendingInsideTheDeferredPresentationWindowKeepsTheResolvedOutcome()
        {
            var pattern = new MonsterAttackPattern("hit", "Hit", 1, 0, 5);
            var state = CreateShapeOnlyMonsterState(
                CombatState.CreateDemoMap(2), new HexCoord(0, 0), new HexCoord(1, 0), pattern);
            EnterActionPhase(state);
            var hpBeforeAttack = state.Player.Hp;

            Assert.That(state.EndAction(), Is.True);
            state.BeginEffectBuffering();
            state.BeginDeferredMonsterActionState();
            state.ResolveMonsterAction(drawPlayerTurnHands: false);

            var hpAfterRules = state.Player.Hp;
            Assert.That(hpAfterRules, Is.LessThan(hpBeforeAttack), "전제: 규칙은 이미 피해를 적용했다.");

            // 연출이 임팩트에 닿기 전 — 되감기 창이 열린 구간.
            state.HoldDeferredMonsterActionStateUntilPresentation();
            Assert.That(state.Player.Hp, Is.EqualTo(hpBeforeAttack), "전제: 되감기 창이 열려 있다.");

            var snapshot = state.CreateSuspendSnapshot();
            var restored = CreateShapeOnlyMonsterState(
                CombatState.CreateDemoMap(2), new HexCoord(0, 0), new HexCoord(1, 0), pattern);
            restored.RestoreFromSuspend(snapshot);

            Assert.That(restored.Player.Hp, Is.EqualTo(hpAfterRules),
                "연출 도중에 중단 저장하면 이미 해결된 몬스터 공격이 환불된다 — "
                + "복원된 플레이어가 맞지 않은 것이 되고, 치명타였다면 죽었어야 할 판이 살아서 돌아온다.");
        }

        [Test]
        public void MonsterMovesBeforeAttackJudgementAndAttacksIfPlayerIsInActivePattern()
        {
            var state = new CombatState(
                CombatState.CreateDemoMap(4),
                new HexCoord(0, 0),
                new HexCoord(2, 0),
                CombatConfig.Default,
                monsterCatalog: CreateConfigMonsterCatalog(CombatConfig.Default));
            // Monster movement resolves inside EnterActionPhase (MonsterMovement phase): (2,0) -> (1,0).
            EnterActionPhase(state);
            var hpBefore = state.Player.Hp;
            var expectedDamage = state.Monsters.Single().SelectedAttackPatternDamage;

            Assert.That(state.Monsters.Single().Coord, Is.EqualTo(new HexCoord(1, 0)));
            Assert.That(state.Monsters.Single().HasAttackIntent, Is.True);
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            Assert.That(state.Monsters.Single().Coord, Is.EqualTo(new HexCoord(1, 0)));
            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore - expectedDamage));
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerMovement));
            var record = state.LastMonsterActionRecords.Single();
            Assert.That(record.Moved, Is.True, "The monster turn record must still capture the MonsterMovement-phase movement leg.");
            Assert.That(record.AttackedPlayer, Is.True, "The monster turn record must still capture the attack leg.");
            Assert.That(record.MovePath, Is.EqualTo(new[] { new HexCoord(2, 0), new HexCoord(1, 0) }));
        }

        [Test]
        public void MultipleMonstersDoNotResolveMovementIntoSameTile()
        {
            var state = new CombatState(
                CreateSharedChokeMap(),
                new HexCoord(0, 0),
                new[]
                {
                    new MonsterConfig("monster-a", new HexCoord(2, 0), 10),
                    new MonsterConfig("monster-b", new HexCoord(1, 1), 10)
                },
                CombatConfig.Default);
            // Both monsters compete for the shared choke tile (1,0), but destination reservation now
            // happens at plan time (RefreshMonsterTurnPlan), so exactly one preview claims the tile
            // and the loser holds position instead of stacking.
            var predictedMoves = state.GetMonsterIntentPreviews(includeUnrevealed: true)
                .Select(preview => preview.PredictedMoveCoord)
                .ToList();
            Assert.That(predictedMoves.Count(coord => coord == new HexCoord(1, 0)), Is.EqualTo(1), "Plan-time reservation gives the shared choke tile to exactly one monster.");
            Assert.That(predictedMoves.Distinct().Count(), Is.EqualTo(predictedMoves.Count));

            // Movement resolves in the MonsterMovement phase (inside EnterActionPhase) as planned.
            EnterActionPhase(state);

            var livingCoords = state.Monsters
                .Where(monster => !monster.IsDead)
                .Select(monster => monster.Coord)
                .ToList();
            Assert.That(livingCoords.Distinct().Count(), Is.EqualTo(livingCoords.Count));
            Assert.That(livingCoords.Count(coord => coord == new HexCoord(1, 0)), Is.EqualTo(1));
        }

        [Test]
        public void MonsterAttacksAfterChaseStepWhenPlayerIsInActivePattern()
        {
            var state = new CombatState(
                CombatState.CreateDemoMap(4),
                new HexCoord(0, 0),
                new HexCoord(2, 0),
            CombatConfig.Default);
            EnterActionPhase(state);
            var hpBefore = state.Player.Hp;
            var expectedDamage = state.Monsters.Single().SelectedAttackPatternDamage;

            Assert.That(state.Monsters.Single().HasAttackIntent, Is.True);
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            Assert.That(state.Monsters.Single().Coord, Is.EqualTo(new HexCoord(1, 0)));
            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore - expectedDamage));
            Assert.That(state.LastMonsterActionRecords.Single().AttackedPlayer, Is.True);
        }

        [Test]
        public void MonsterChasesWhenPlayerIsInsideMaxRangeButOutsideActualAttackShape()
        {
            var pattern = new MonsterAttackPattern("pincer-only", "Pincer Only", 3, 0, 1, shapeId: AttackShapeLibrary.Pincer);
            var playerCoord = new HexCoord(0, 0);
            var map = CombatState.CreateDemoMap(4);
            var monsterCoord = FindShapeChaseStart(map, playerCoord, pattern, requireNeighborCoverage: true);
            var state = CreateShapeOnlyMonsterState(map, playerCoord, monsterCoord, pattern);

            var monster = state.Monsters.Single();
            var preview = state.GetMonsterIntentPreviews(includeUnrevealed: true).Single();

            Assert.That(monster.Intent.Type, Is.EqualTo(EnemyIntentType.Chase));
            Assert.That(preview.WillMove, Is.True);
            Assert.That(preview.AttackRangeCoords, Does.Contain(playerCoord), "The chase step should prefer a cell that can attack the player next turn when one is reachable.");
        }

        [Test]
        public void MonsterKeepsAttackIntentWhenActualAttackShapeCoversPlayer()
        {
            var pattern = new MonsterAttackPattern("line-only", "Line Only", 3, 0, 1, shapeId: AttackShapeLibrary.Line3);
            var playerCoord = new HexCoord(0, 0);
            var monsterCoord = new HexCoord(2, 0);
            Assert.That(PatternCovers(pattern, monsterCoord, playerCoord), Is.True);
            var state = CreateShapeOnlyMonsterState(CombatState.CreateDemoMap(3), playerCoord, monsterCoord, pattern);

            Assert.That(state.Monsters.Single().Intent.Type, Is.EqualTo(EnemyIntentType.Attack));
            Assert.That(state.Monsters.Single().HasAttackIntent, Is.True);
        }

        [Test]
        public void MonsterAttackShapesAlwaysIncludeAdjacentRing()
        {
            var origin = new HexCoord(0, 0);
            var attackDirection = origin.ApproximateDirection(new HexCoord(1, 0));

            // cone-near는 2026-08-18 미사용 정리로 카탈로그에서 삭제됐다 — 링 전용 대표는 single이 맡는다.
            var singleCells = AttackShapeLibrary.GetAffectedCells(AttackShapeLibrary.Single, origin, attackDirection).ToArray();
            var lineCells = AttackShapeLibrary.GetAffectedCells(AttackShapeLibrary.Line2, origin, attackDirection).ToArray();

            var adjacentRing = new[]
            {
                new HexCoord(1, 0),
                new HexCoord(1, -1),
                new HexCoord(0, -1),
                new HexCoord(-1, 0),
                new HexCoord(-1, 1),
                new HexCoord(0, 1)
            };

            Assert.That(singleCells, Is.EquivalentTo(adjacentRing));
            Assert.That(lineCells, Is.EquivalentTo(adjacentRing.Concat(new[] { new HexCoord(2, 0) })));
        }

        [Test]
        public void BossTierShapesCoverTheAuthoredCellsAndKeepTheirSafeGaps()
        {
            var origin = new HexCoord(0, 0);
            var east = origin.ApproximateDirection(new HexCoord(1, 0));

            // ring-full-2: 인접 6 + 반경2 전체 12 = 완전 원판 18칸. 회전 대칭이라 조준 방향과 무관하다.
            var ringCells = AttackShapeLibrary.GetAffectedCells(AttackShapeLibrary.RingFull2, origin, east).ToArray();
            Assert.That(ringCells, Has.Length.EqualTo(18));
            Assert.That(ringCells.All(cell => cell != origin && origin.DistanceTo(cell) <= 2), Is.True);
            var south = origin.ApproximateDirection(new HexCoord(0, 1));
            Assert.That(
                AttackShapeLibrary.GetAffectedCells(AttackShapeLibrary.RingFull2, origin, south).ToArray(),
                Is.EquivalentTo(ringCells),
                "전방위 원판은 어느 방향을 조준해도 같아야 한다.");

            // spiral-3: 인접 6 + 갈래 15 = 21칸(2026-08-18 웹 에디터 재저작 — 갈래를 길고 휘게 확장).
            // 갈래 사이 쐐기((0,2)·(2,-2))가 비어야 옆걸음 회피가 성립한다는 계약은 그대로다.
            var spiralCells = AttackShapeLibrary.GetAffectedCells(AttackShapeLibrary.Spiral3, origin, east).ToArray();
            Assert.That(spiralCells, Has.Length.EqualTo(21));
            Assert.That(spiralCells, Does.Contain(new HexCoord(3, 0)));
            Assert.That(spiralCells, Has.No.Member(new HexCoord(0, 2)), "갈래 사이 반경2 쐐기는 안전해야 한다.");
            Assert.That(spiralCells, Has.No.Member(new HexCoord(2, -2)), "갈래 사이 반경2 쐐기는 안전해야 한다.");

            // cone-long: 최장 도달 4의 전방 브레스. 후방·측면은 인접 링뿐이다.
            var coneCells = AttackShapeLibrary.GetAffectedCells(AttackShapeLibrary.ConeLong, origin, east).ToArray();
            Assert.That(coneCells, Has.Length.EqualTo(15));
            Assert.That(coneCells.Max(cell => origin.DistanceTo(cell)), Is.EqualTo(4));
            Assert.That(coneCells, Does.Contain(new HexCoord(4, 0)));
            Assert.That(coneCells, Has.No.Member(new HexCoord(-2, 0)), "브레스의 후방은 인접 링 밖이 비어야 한다.");
        }

        [Test]
        public void RangedBandShapesExcludeTheAdjacentRingWhileEveryLegacyShapeKeepsIt()
        {
            var origin = new HexCoord(0, 0);
            var east = origin.ApproximateDirection(new HexCoord(1, 0));
            var adjacentRing = origin.NeighborsInDirectionOrder().ToArray();

            // §13.3 opt-out 계약(2026-08-18 갱신): 링 제외는 밴드형 둘(donut/artillery)과 스와이프 둘
            // (front-right-swipe·wide-swipe)이고, 나머지 shape 전부는(플레이어 카드 포함 공유
            // 라이브러리이므로) 인접 링을 그대로 포함해야 한다.
            // cone-near·t-forward·cross-near·ring-near는 같은 날 미사용 정리로 삭제됐다.
            var legacyShapeIds = new[]
            {
                AttackShapeLibrary.Single, AttackShapeLibrary.Line2, AttackShapeLibrary.Line3,
                AttackShapeLibrary.Line4, AttackShapeLibrary.ConeMid, AttackShapeLibrary.ConeWide,
                AttackShapeLibrary.CrossFar, AttackShapeLibrary.VSplit,
                AttackShapeLibrary.Pincer, AttackShapeLibrary.Tremor, AttackShapeLibrary.RingFull2,
                AttackShapeLibrary.Spiral3, AttackShapeLibrary.ConeLong
            };
            foreach (var shapeId in legacyShapeIds)
            {
                var cells = AttackShapeLibrary.GetAffectedCells(shapeId, origin, east).ToArray();
                Assert.That(cells, Is.SupersetOf(adjacentRing), $"'{shapeId}'는 인접 링을 포함해야 한다.");
            }

            // donut-2: 정확히 반경2 링 12칸 — 인접(1)과 원거리(3+)가 안전하다.
            var donutCells = AttackShapeLibrary.GetAffectedCells(AttackShapeLibrary.Donut2, origin, east).ToArray();
            Assert.That(donutCells, Has.Length.EqualTo(12));
            Assert.That(donutCells.All(cell => origin.DistanceTo(cell) == 2), Is.True, "donut-2는 반경 2 링만 때린다.");

            // artillery-3: 반경3 링 18칸 + 안쪽 반경2 4칸(2026-08-18) — 인접(1)만이 유일한 회피다.
            var artilleryCells = AttackShapeLibrary.GetAffectedCells(AttackShapeLibrary.Artillery3, origin, east).ToArray();
            Assert.That(artilleryCells, Has.Length.EqualTo(22));
            Assert.That(artilleryCells.All(cell => origin.DistanceTo(cell) >= 2 && origin.DistanceTo(cell) <= 3), Is.True,
                "artillery-3는 거리 2~3만 때리고 인접 링은 안전해야 한다.");

            // 2026-08-18 신설 스와이프 둘도 링 opt-out — 붙으면 안전해야 한다.
            foreach (var swipeId in new[] { "front-right-swipe", "wide-swipe" })
            {
                var swipeCells = AttackShapeLibrary.GetAffectedCells(swipeId, origin, east).ToArray();
                Assert.That(swipeCells, Is.Not.Empty, $"'{swipeId}'가 형상 카탈로그에 있어야 한다.");
                Assert.That(swipeCells.All(cell => origin.DistanceTo(cell) >= 2), Is.True,
                    $"'{swipeId}'는 링 opt-out — 인접 칸이 없어야 '붙으면 안전'이 성립한다.");
            }
        }

        [Test]
        public void PlayerMovingIntoActiveAttackPatternIsHitOnMonsterAction()
        {
            var config = new CombatConfig(
                playerMaxHp: CombatConfig.Default.PlayerMaxHp,
                enemyMaxHp: CombatConfig.Default.EnemyMaxHp,
                playerMovePoints: CombatConfig.Default.PlayerMovePoints,
                attackRange: CombatConfig.Default.AttackRange,
                attackDamage: CombatConfig.Default.AttackDamage,
                defenseBlock: CombatConfig.Default.DefenseBlock,
                enemyChaseRange: CombatConfig.Default.EnemyChaseRange,
                enemyAttackRange: 2,
                enemyAttackDamage: CombatConfig.Default.EnemyAttackDamage,
                actionBudget: CombatConfig.Default.ActionBudget);
            var state = new CombatState(
                CombatState.CreateDemoMap(4),
                new HexCoord(0, 0),
                new HexCoord(3, 0),
                config,
                monsterCatalog: CreateConfigMonsterCatalog(config));
            var hpBefore = state.Player.Hp;

            Assert.That(state.Monsters.Single().HasAttackIntent, Is.True);
            Assert.That(state.TryPlayerMove(new HexCoord(1, 0)), Is.True);
            Assert.That(state.EndAction(), Is.True); // PlayerMovement -> MonsterMovement
            state.ResolveMonsterMovement(); // -> PlayerAction
            Assert.That(state.EndAction(), Is.True); // PlayerAction -> MonsterAction
            state.ResolveMonsterAction();

            Assert.That(state.Player.Hp, Is.LessThan(hpBefore));
            Assert.That(state.LastMonsterActionRecords.Single().AttackedPlayer, Is.True);
        }

        [Test]
        public void PlayerLeavingTelegraphedAttackRangeAvoidsCommittedAttack()
        {
            var state = new CombatState(
                CombatState.CreateDemoMap(4),
                new HexCoord(0, 0),
                new HexCoord(1, 0),
                CombatConfig.Default);
            var hpBefore = state.Player.Hp;

            Assert.That(state.Monsters.Single().HasAttackIntent, Is.True);
            // The committed pattern covers ring-1 around the monster plus the facing cell (-1,0),
            // so the dodge must land outside the telegraphed cells: (-1,1) is reachable and safe.
            Assert.That(
                state.GetMonsterIntentPreviews(includeUnrevealed: true).Single().AttackRangeCoords,
                Has.No.Member(new HexCoord(-1, 1)));
            Assert.That(state.TryPlayerMove(new HexCoord(-1, 1)), Is.True);
            Assert.That(state.EndAction(), Is.True); // PlayerMovement -> MonsterMovement
            state.ResolveMonsterMovement(); // -> PlayerAction
            Assert.That(state.EndAction(), Is.True); // PlayerAction -> MonsterAction
            state.ResolveMonsterAction();

            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore));
            Assert.That(state.LastMonsterActionRecords.Single().AttackedPlayer, Is.False);
        }

        [Test]
        public void OneOrTwoMonstersCanBeRepresentedDeterministically()
        {
            var state = new CombatState(
                CombatState.CreateDemoMap(4),
                new HexCoord(0, 0),
                new[]
                {
                    new MonsterConfig("monster-a", new HexCoord(3, 0), 8),
                    new MonsterConfig("monster-b", new HexCoord(4, 0), 6)
                },
                CombatConfig.Default);

            Assert.That(state.Monsters.Count, Is.EqualTo(2));
            Assert.That(state.Monsters[0].Id, Is.EqualTo("monster-a"));
            Assert.That(state.Monsters[1].Id, Is.EqualTo("monster-b"));
            Assert.That(state.RuntimeStates.ContainsKey(new HexCoord(3, 0)), Is.True);
            Assert.That(state.RuntimeStates.ContainsKey(new HexCoord(4, 0)), Is.True);
        }


        [Test]
        public void AttackTargetValidationAcceptsLivingMonsterInRange()
        {
            var state = new CombatState(CombatState.CreateDemoMap(2), new HexCoord(0, 0), new HexCoord(1, 0), CombatConfig.Default);
            EnterActionPhase(state);

            var validation = state.ValidateAttackTarget(new HexCoord(1, 0));

            Assert.That(validation.IsValid, Is.True);
            Assert.That(validation.FailureReason, Is.Empty);
        }

        [Test]
        public void AttackTargetValidationRejectsMonsterOutsidePlayerVision()
        {
            var config = new CombatConfig(
                20,
                10,
                1,
                3,
                4,
                4,
                0,
                1,
                3,
                actionBudget: 3,
                actionHandSize: 15,
                playerVisionRange: 1);
            var state = new CombatState(
                CombatState.CreateDemoMap(4),
                new HexCoord(0, 0),
                new HexCoord(2, 0),
                config,
                cardCatalog: ApprovedCardCatalogFactory.CreateApprovedCatalog(config));
            EnterActionPhase(state);
            var costBefore = state.ActionCostRemaining;

            Assert.That(state.GetVisibility(new HexCoord(2, 0)), Is.Not.EqualTo(HexCellVisibility.Revealed));
            Assert.That(state.TryPlayerAttack(new HexCoord(2, 0), ApprovedCardCatalogFactory.AttackHolyLightId), Is.False);

            Assert.That(state.LastFailureReason, Does.Contain("visible"));
            Assert.That(state.ActionCostRemaining, Is.EqualTo(costBefore));
            Assert.That(state.Monsters.Single().Hp, Is.EqualTo(state.Monsters.Single().MaxHp));
        }

        [Test]
        public void AttackTargetValidationRejectsEmptyCell()
        {
            var state = new CombatState(CombatState.CreateDemoMap(2), new HexCoord(0, 0), new HexCoord(1, 0), CombatConfig.Default);
            EnterActionPhase(state);
            var costBefore = state.ActionCostRemaining;

            Assert.That(state.TryPlayerAttack(new HexCoord(0, 1)), Is.False);

            Assert.That(state.LastFailureReason, Does.Contain("living monster"));
            Assert.That(state.ActionCostRemaining, Is.EqualTo(costBefore));
            Assert.That(state.LastDiscardedCard, Is.EqualTo(CombatCardKind.Move));
        }

        [Test]
        public void AttackTargetValidationRejectsOutOfRangeMonster()
        {
            var state = new CombatState(CombatState.CreateDemoMap(4), new HexCoord(0, 0), new HexCoord(3, 0), CombatConfig.Default);
            EnterActionPhase(state);
            var target = state.Monsters.Single().Coord;
            var costBefore = state.ActionCostRemaining;

            Assert.That(state.TryPlayerAttack(target, ApprovedCardCatalogFactory.AttackSweepId), Is.False);

            Assert.That(state.LastFailureReason, Does.Contain("range"));
            Assert.That(state.ActionCostRemaining, Is.EqualTo(costBefore));
            Assert.That(state.Monsters.Single().Hp, Is.EqualTo(state.Monsters.Single().MaxHp));
        }

        [Test]
        public void ValidSelectedMonsterAttackDamagesOnlyClickedMonster()
        {
            var state = new CombatState(
                CombatState.CreateDemoMap(2),
                new HexCoord(0, 0),
                new[]
                {
                    new MonsterConfig("monster-a", new HexCoord(1, 0), 10),
                    new MonsterConfig("monster-b", new HexCoord(0, 1), 10)
                },
                CombatConfig.Default);
            EnterActionPhase(state);

            Assert.That(state.TryPlayerAttack(new HexCoord(0, 1)), Is.True);

            Assert.That(state.Monsters.Single(monster => monster.Id == "monster-a").Hp, Is.EqualTo(10));
            Assert.That(state.Monsters.Single(monster => monster.Id == "monster-b").Hp, Is.EqualTo(6));
            Assert.That(state.LastDiscardedCard, Is.EqualTo(CombatCardKind.Attack));
        }


        [Test]
        public void SelectedDoubleHitUsesSelectedCardRangeAndConsumesSelectedCardOnly()
        {
            var state = new CombatState(
                CombatState.CreateDemoMap(3),
                new HexCoord(0, 0),
                new HexCoord(2, 0),
                new CombatConfig(20, 10, 1, 1, 4, 4, 0, 1, 3, actionBudget: 3, actionHandSize: 5));
            EnterActionPhase(state);

            Assert.That(state.ValidateAttackTarget(new HexCoord(2, 0), ApprovedCardCatalogFactory.AttackSweepId).IsValid, Is.False);
            Assert.That(state.ValidateAttackTarget(new HexCoord(2, 0), ApprovedCardCatalogFactory.AttackDoubleHitId).IsValid, Is.True);

            Assert.That(state.TryPlayerAttack(new HexCoord(2, 0), ApprovedCardCatalogFactory.AttackDoubleHitId), Is.True);

            Assert.That(state.Monsters.Single().Hp, Is.EqualTo(6));
            Assert.That(state.GetCombatCards().Any(card => card.Id == ApprovedCardCatalogFactory.AttackSweepId && !card.IsDiscarded), Is.True);
            Assert.That(state.GetCombatCards().Any(card => card.Id == ApprovedCardCatalogFactory.AttackDoubleHitId && card.IsDiscarded), Is.True);
        }

        [Test]
        public void DeadMonsterIsRemovedFromOccupancyAndNoLongerAttacks()
        {
            var state = new CombatState(
                CombatState.CreateDemoMap(2),
                new HexCoord(0, 0),
                new HexCoord(1, 0),
                new CombatConfig(20, 1, 1, 1, 4, 4, 5, 1, 3));
            EnterActionPhase(state);
            var hpBefore = state.Player.Hp;

            Assert.That(state.TryPlayerAttack(new HexCoord(1, 0)), Is.True);
            Assert.That(state.Monsters.Single().IsDead, Is.True);
            Assert.That(state.RuntimeStates.ContainsKey(new HexCoord(1, 0)), Is.False);
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore));
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerMovement));
        }

        [Test]
        public void KillingAllMonstersDoesNotCompleteObjectiveOrTriggerVictory()
        {
            var state = new CombatState(
                CombatState.CreateDemoMap(2),
                new HexCoord(0, 0),
                new HexCoord(1, 0),
                new CombatConfig(20, 1, 1, 1, 4, 4, 5, 1, 3));
            EnterActionPhase(state);

            Assert.That(state.TryPlayerAttack(new HexCoord(1, 0)), Is.True);

            Assert.That(state.Monsters.All(monster => monster.IsDead), Is.True);
            Assert.That(state.ObjectiveCompleted, Is.False);
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction));
            Assert.That(state.IsTerminal, Is.False);
        }

        [Test]
        public void RepresentativeLivingMonsterTracksLivingMonsterAfterFirstMonsterDies()
        {
            var state = new CombatState(
                CombatState.CreateDemoMap(4),
                new HexCoord(0, 0),
                new[]
                {
                    new MonsterConfig("monster-a", new HexCoord(1, 0), 1),
                    new MonsterConfig("monster-b", new HexCoord(3, 0), 10)
                },
                CombatConfig.Default);
            EnterActionPhase(state);

            Assert.That(state.TryPlayerAttack(new HexCoord(1, 0)), Is.True);

            Assert.That(state.Monsters.Single(monster => monster.Id == "monster-a").IsDead, Is.True);
            var living = state.Monsters.Single(monster => monster.Id == "monster-b");
            var representative = state.RepresentativeLivingMonster;
            Assert.That(representative.HasValue, Is.True);
            Assert.That(representative.Value.Id, Is.EqualTo("monster-b"));
            Assert.That(representative.Value.Coord, Is.EqualTo(living.Coord));
            Assert.That(representative.Value.Intent.Type, Is.EqualTo(living.Intent.Type));
        }

        [Test]
        public void DefendBlockAppliesAcrossMultipleMonsterAttacksInDeterministicOrder()
        {
            var config = CombatConfig.Default;
            var state = new CombatState(
                CombatState.CreateDemoMap(2),
                new HexCoord(0, 0),
                new[]
                {
                    new MonsterConfig("monster-b", new HexCoord(0, 1), 10),
                    new MonsterConfig("monster-a", new HexCoord(1, 0), 10)
                },
                config,
                monsterCatalog: CreateConfigMonsterCatalog(config));
            EnterActionPhase(state);
            Assert.That(state.TryPlayerDefend(), Is.True);
            var hpBefore = state.Player.Hp;

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore - 5));
            Assert.That(state.Player.Block, Is.EqualTo(0));
            Assert.That(state.Monsters.All(monster => monster.HasAttackIntent), Is.True);
        }

        [Test]
        public void DeferredMonsterActionCommitsPlayerDeathOnlyOnFinalAttackImpact()
        {
            var config = new CombatConfig(
                playerMaxHp: 11,
                enemyMaxHp: 10,
                playerMovePoints: 1,
                attackRange: 1,
                attackDamage: 4,
                defenseBlock: 0,
                enemyChaseRange: 5,
                enemyAttackRange: 1,
                enemyAttackDamage: 4,
                actionBudget: 1);
            var state = new CombatState(
                CombatState.CreateDemoMap(2),
                new HexCoord(0, 0),
                new[]
                {
                    new MonsterConfig("monster-a", new HexCoord(1, 0), 10),
                    new MonsterConfig("monster-b", new HexCoord(0, 1), 10),
                    new MonsterConfig("monster-c", new HexCoord(-1, 1), 10),
                },
                config,
                monsterCatalog: CreateConfigMonsterCatalog(config));
            EnterActionPhase(state);

            Assert.That(state.EndAction(), Is.True);
            state.BeginEffectBuffering();
            state.BeginDeferredMonsterActionState();
            state.ResolveMonsterAction(drawPlayerTurnHands: false);
            var records = state.LastMonsterActionRecords
                .Where(record => record.AttackedPlayer)
                .OrderBy(record => record.AttackOrder)
                .ToList();
            Assert.That(records, Has.Count.EqualTo(3));
            Assert.That(state.Player.IsDead, Is.True, "Rules simulation still computes the full outcome before it is held for presentation.");

            state.HoldDeferredMonsterActionStateUntilPresentation();
            Assert.That(state.Player.Hp, Is.EqualTo(11));
            Assert.That(state.Player.IsDead, Is.False);
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.MonsterAction));

            state.CommitDeferredMonsterActionImpact(records[0].PresentationGroupId);
            Assert.That(state.Player.Hp, Is.EqualTo(7));
            Assert.That(state.Player.IsDead, Is.False);

            state.CommitDeferredMonsterActionImpact(records[1].PresentationGroupId);
            Assert.That(state.Player.Hp, Is.EqualTo(3));
            Assert.That(state.Player.IsDead, Is.False);

            state.CommitDeferredMonsterActionImpact(records[2].PresentationGroupId);
            Assert.That(state.Player.Hp, Is.Zero);
            Assert.That(state.Player.IsDead, Is.True);
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.Defeat));
        }

        /// <summary>
        /// 🔴 되감기가 <b>상태이상까지</b> 되감는다는 계약. 기존 잠금은 HP·페이즈만 봤는데,
        /// <c>RestoreDeferredMonsterActionSnapshot</c>은 <c>activeEffects</c>를 <b>통째로 교체</b>한다
        /// (리스트를 비우고 스냅샷 사본을 다시 채운다). 즉 연출 비트가 「플레이어가 언제 중독되는가」도
        /// 쥐고 있다 — 그물이 HP 한 축뿐이면 이 축이 조용히 깨져도 아무도 모른다.
        ///
        /// 2026-08-31 P2 조사에서 확인: 되감기를 통째로 지우는 돌연변이에 빨개지는 테스트가 단 1건이었다.
        /// </summary>
        [Test]
        public void DeferredMonsterActionRewindsAppliedStatusEffectsUntilPresentationCommits()
        {
            var state = CreateDeferredStatusAttackState();

            Assert.That(state.EndAction(), Is.True);
            state.BeginEffectBuffering();
            state.BeginDeferredMonsterActionState();
            state.ResolveMonsterAction(drawPlayerTurnHands: false);

            Assert.That(
                PlayerEffectKinds(state),
                Is.EquivalentTo(new[] { StatusEffectKind.Poison, StatusEffectKind.Slow }),
                "전제: 규칙은 연출과 무관하게 이미 상태이상을 다 적용해 뒀다.");

            state.HoldDeferredMonsterActionStateUntilPresentation();
            Assert.That(
                PlayerEffectKinds(state),
                Is.Empty,
                "되감기는 HP만이 아니라 상태이상도 공격 전으로 돌려놓아야 한다 — "
                + "안 그러면 연출이 아직 안 때렸는데 중독 아이콘이 먼저 뜬다.");

            state.FinishDeferredMonsterActionState();
            Assert.That(
                PlayerEffectKinds(state),
                Is.EquivalentTo(new[] { StatusEffectKind.Poison, StatusEffectKind.Slow }),
                "연출이 끝나면 규칙이 계산해 둔 최종 상태로 정확히 되돌아와야 한다.");
        }

        /// <summary>
        /// 🔴🔴 <b>수명 사고는 두 번째 턴에서만 보인다.</b> 되감기는 상태이상을 "복사했다가 되돌리는"
        /// 일이라, 남은 턴 수가 한 번 더 깎이거나 덜 깎여도 <b>그 턴 안에서는 안 보인다</b>.
        /// 그래서 연출 경로(되감기 있음)와 직행 경로(되감기 없음)를 <b>한 턴 더 굴린 뒤</b> 비교한다 —
        /// 두 경로의 상태이상 수명이 갈리면 그건 연출이 규칙을 바꿨다는 뜻이다.
        ///
        /// 선행 트랙 T5의 「동치 계약을 먼저 잠근다」와 같은 문법이다: P3(activeEffects를 타입으로
        /// 올리기)이 바로 이 리스트를 건드리므로, 옮기기 <b>전에</b> 잠가 둔다.
        /// </summary>
        [Test]
        public void DeferredPresentationPathLeavesTheSameStatusLifetimesAsTheDirectPath()
        {
            var deferred = CreateDeferredStatusAttackState();
            Assert.That(deferred.EndAction(), Is.True);
            deferred.BeginEffectBuffering();
            deferred.BeginDeferredMonsterActionState();
            deferred.ResolveMonsterAction(drawPlayerTurnHands: false);
            deferred.HoldDeferredMonsterActionStateUntilPresentation();
            deferred.FinishDeferredMonsterActionState();

            var direct = CreateDeferredStatusAttackState();
            Assert.That(direct.EndAction(), Is.True);
            direct.ResolveMonsterAction(drawPlayerTurnHands: false);

            Assert.That(
                DescribePlayerEffects(deferred),
                Is.EqualTo(DescribePlayerEffects(direct)),
                "첫 턴: 되감기를 거친 경로와 직행 경로의 상태이상이 같아야 한다.");

            AdvanceOneFullTurn(deferred);
            AdvanceOneFullTurn(direct);

            Assert.That(
                DescribePlayerEffects(deferred),
                Is.EqualTo(DescribePlayerEffects(direct)),
                "🔴 두 번째 턴: 되감기가 상태이상 수명을 한 번 더(혹은 덜) 깎았다 — "
                + "연출 경로가 규칙을 바꾼 것이다.");
        }



        [Test]
        public void MonsterIntentPreviewShowsRevealedChaseMoveAndAttackRange()
        {
            var state = new CombatState(
                CombatState.CreateDemoMap(4),
                new HexCoord(0, 0),
                new HexCoord(3, 0),
                CombatConfig.Default);
            state.RevealForTests(new HexCoord(3, 0));

            var preview = state.GetMonsterIntentPreviews().Single();

            Assert.That(preview.MonsterId, Is.EqualTo("normal-enemy"));
            Assert.That(preview.CurrentCoord, Is.EqualTo(new HexCoord(3, 0)));
            Assert.That(preview.PredictedMoveCoord, Is.EqualTo(new HexCoord(2, 0)));
            Assert.That(preview.IntentType, Is.EqualTo(EnemyIntentType.Chase));
            Assert.That(preview.AttackRangeCoords, Does.Contain(new HexCoord(1, 0)));
            Assert.That(preview.AttackRangeCoords, Has.No.Member(new HexCoord(2, 0)),
                "The CSV monster baseline currently previews the selected ThreeEyeDog cone, not the legacy radial basic attack.");
        }

        [Test]
        public void MonsterIntentPreviewHidesUnrevealedMonstersUnlessExplicitlyIncluded()
        {
            var state = new CombatState(
                CombatState.CreateDemoMap(4),
                new HexCoord(0, 0),
                new HexCoord(3, 0),
                new CombatConfig(20, 10, 2, 1, 4, 4, 5, 1, 3, playerVisionRange: 2));

            Assert.That(state.GetMonsterIntentPreviews(), Is.Empty);
            Assert.That(state.GetMonsterIntentPreviews(includeUnrevealed: true).Single().CurrentCoord, Is.EqualTo(new HexCoord(3, 0)));
        }

        [Test]
        public void CommittedAttackPatternStaysLockedWhenPlayerPositionRefreshesIntent()
        {
            var state = CreateTwoPatternAttackState();
            var committed = state.Monsters.Single();
            Assert.That(committed.HasAttackIntent, Is.True);
            Assert.That(committed.SelectedAttackPatternId, Is.EqualTo("weighted-close"));
            Assert.That(committed.SelectedAttackPatternRange, Is.EqualTo(1));

            Assert.That(state.TryDebugMovePlayer(new HexCoord(-1, 0)), Is.True);

            var afterRefresh = state.Monsters.Single();
            Assert.That(afterRefresh.HasAttackIntent, Is.True);
            Assert.That(afterRefresh.SelectedAttackPatternId, Is.EqualTo("fallback-long"));
            Assert.That(afterRefresh.SelectedAttackPatternRange, Is.EqualTo(3));
        }

        [Test]
        public void CommittedAttackPatternStaysLockedThroughPlayerMoveActionAndEndAction()
        {
            var state = CreateTwoPatternAttackState();
            var committed = state.Monsters.Single();

            Assert.That(state.TryPlayerMove(new HexCoord(-1, 0)), Is.True);
            Assert.That(state.EndAction(), Is.True); // PlayerMovement -> MonsterMovement
            state.ResolveMonsterMovement(); // -> PlayerAction
            Assert.That(state.TryPlayerDefend(), Is.True);
            Assert.That(state.EndAction(), Is.True);

            var afterPlayerTurn = state.Monsters.Single();
            Assert.That(afterPlayerTurn.HasAttackIntent, Is.True);
            Assert.That(afterPlayerTurn.SelectedAttackPatternId, Is.EqualTo(committed.SelectedAttackPatternId));
            Assert.That(afterPlayerTurn.SelectedAttackPatternRange, Is.EqualTo(committed.SelectedAttackPatternRange));
        }

        private static CombatState CreateShapeOnlyMonsterState(
            HexMapData map,
            HexCoord playerCoord,
            HexCoord monsterCoord,
            MonsterAttackPattern pattern)
        {
            var catalog = new MonsterCatalogDefinition(
                "shape-test-monsters",
                "Shape Test Monsters",
                new[]
                {
                    new MonsterCatalogEntry(
                        "shape-only",
                        "Shape Only Monster",
                        "test",
                        "test.shape",
                        6,
                        1,
                        10,
                        attackPatterns: new[] { pattern })
                });

            return new CombatState(
                map,
                playerCoord,
                new[] { new MonsterConfig("shape-monster", monsterCoord, 10, "shape-test-monsters", "shape-only") },
                CombatConfig.Default,
                monsterCatalog: catalog);
        }

        private static HexCoord FindShapeChaseStart(
            HexMapData map,
            HexCoord playerCoord,
            MonsterAttackPattern pattern,
            bool requireNeighborCoverage)
        {
            foreach (var cell in map.AllCells.OrderBy(cell => cell.Coord.DistanceTo(playerCoord)).ThenBy(cell => cell.Coord.Q).ThenBy(cell => cell.Coord.R))
            {
                var coord = cell.Coord;
                // Adjacent tiles (distance <= 1) are always covered by the new melee-range rule, so skip them.
                if (coord == playerCoord || coord.DistanceTo(playerCoord) <= 1 || coord.DistanceTo(playerCoord) > pattern.Range || PatternCovers(pattern, coord, playerCoord))
                {
                    continue;
                }

                var hasCoveringNeighbor = coord.NeighborsInDirectionOrder()
                    .Any(neighbor => map.Contains(neighbor) && neighbor != playerCoord && PatternCovers(pattern, neighbor, playerCoord));
                if (!requireNeighborCoverage || hasCoveringNeighbor)
                {
                    return coord;
                }
            }

            Assert.Fail($"No non-covering start coordinate was found for test shape '{pattern.ShapeId}'.");
            return playerCoord;
        }

        private static bool PatternCovers(MonsterAttackPattern pattern, HexCoord origin, HexCoord playerCoord)
        {
            if (!string.IsNullOrEmpty(pattern.ShapeId))
            {
                var attackDir = origin.ApproximateDirection(playerCoord);
                return AttackShapeLibrary.GetAffectedCells(pattern.ShapeId, origin, attackDir).Contains(playerCoord);
            }

            return origin.DistanceTo(playerCoord) <= pattern.Range;
        }

        // ------------------------------------------------------------------ 지연 몬스터 행동 픽스처

        /// <summary>독+둔화를 거는 공격 하나만 가진 몬스터가 플레이어에 붙어 있는 상태(행동 페이즈).</summary>
        private static CombatState CreateDeferredStatusAttackState()
        {
            var pattern = new MonsterAttackPattern(
                "status-hit",
                "Status Hit",
                1,
                0,
                0,
                statusEffects: new[] { StatusEffectKind.Poison, StatusEffectKind.Slow },
                statusEffectDurationTurns: 4,
                statusEffectAmount: 3);
            var state = CreateShapeOnlyMonsterState(
                CombatState.CreateDemoMap(2),
                new HexCoord(0, 0),
                new HexCoord(1, 0),
                pattern);
            EnterActionPhase(state);
            return state;
        }

        private static IEnumerable<StatusEffectKind> PlayerEffectKinds(CombatState state)
        {
            return state.ActiveEffects
                .Where(effect => effect.TargetUnitId == "player")
                .Select(effect => effect.Kind)
                .ToList();
        }

        /// <summary>종류·남은 턴·수치를 한 문자열로 — 한 축만 어긋나도 비교가 갈린다.</summary>
        private static string DescribePlayerEffects(CombatState state)
        {
            return string.Join(
                " | ",
                state.ActiveEffects
                    .Where(effect => effect.TargetUnitId == "player")
                    .Select(effect => $"{effect.Kind}:{effect.RemainingTurns}:{effect.Amount}")
                    .OrderBy(text => text, StringComparer.Ordinal));
        }

        /// <summary>플레이어 행동 페이즈에서 다시 플레이어 행동 페이즈까지 한 바퀴.</summary>
        private static void AdvanceOneFullTurn(CombatState state)
        {
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction(drawPlayerTurnHands: false);
        }

        private static void EnterActionPhase(CombatState state)
        {
            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            Assert.That(state.EndAction(), Is.True);
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.MonsterMovement));
            state.ResolveMonsterMovement();
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction));
        }

        private static MonsterCatalogDefinition CreateConfigMonsterCatalog(CombatConfig config)
        {
            return new MonsterCatalogDefinition(
                "monster-runtime-config-monsters",
                "Monster Runtime Config Monsters",
                new[]
                {
                    new MonsterCatalogEntry(
                        CombatCatalogFactory.ThreeEyeDogMonsterId,
                        "Config Monster",
                        "test",
                        "test.config",
                        config.EnemyMaxHp,
                        config.EnemyChaseRange,
                        10,
                        attackPatterns: new[]
                        {
                            new MonsterAttackPattern("config-attack", "Config Attack", config.EnemyAttackRange, 0, config.EnemyAttackDamage)
                        })
                });
        }

        private static CombatState CreateTwoPatternAttackState()
        {
            var catalog = new MonsterCatalogDefinition(
                "test-monsters",
                "Test Monsters",
                new[]
                {
                    new MonsterCatalogEntry(
                        "two-pattern",
                        "Two Pattern Monster",
                        "test",
                        "test.behavior",
                        5,
                        1,
                        10,
                        attackPatterns: new[]
                        {
                            new MonsterAttackPattern("weighted-close", "Weighted Close", 1, 0, 1, weight: 1000),
                            new MonsterAttackPattern("fallback-long", "Fallback Long", 3, 0, 1, weight: 1)
                        })
                });

            return new CombatState(
                CombatState.CreateDemoMap(3),
                new HexCoord(0, 0),
                new[] { new MonsterConfig("monster", new HexCoord(1, 0), 10, "test-monsters", "two-pattern") },
                CombatConfig.Default,
                monsterCatalog: catalog);
        }

        private static HexMapData CreateLineMapWithBlockedMiddle()
        {
            return new HexMapData(new[]
            {
                new HexCellData(new HexCoord(0, 0), "player", "street", 1, true, false),
                new HexCellData(new HexCoord(1, 0), "blocked", "street", 1, false, false),
                new HexCellData(new HexCoord(2, 0), "monster", "street", 1, true, false)
            });
        }

        private static HexMapData CreateSharedChokeMap()
        {
            return new HexMapData(new[]
            {
                new HexCellData(new HexCoord(0, 0), "player", "street", 1, true, false),
                new HexCellData(new HexCoord(1, 0), "shared-choke", "street", 1, true, false),
                new HexCellData(new HexCoord(2, 0), "monster-a", "street", 1, true, false),
                new HexCellData(new HexCoord(1, 1), "monster-b", "street", 1, true, false)
            });
        }
    }
}

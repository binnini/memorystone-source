using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 속박(Immobilize)/기절(Stun)을 몬스터 의도에 반영하는 규칙을 고정한다.
    ///   - 속박 → 이동만 차단하고 공격은 유지한다. 이동 의도였던 몬스터도 제자리 공격으로 collapse한다
    ///     (공격이 기본값 — 닿지 않아도 시전; Intent=Attack, 제자리, HasAttackIntent=true).
    ///   - 속박 + 이미 제자리 공격 → 그대로 유지.
    ///   - 기절 → 이동/공격 모두 불가 → 항상 대기(HasAttackIntent=false, Intent=Patrol).
    /// 반영은 부여 시점에 즉시 일어나고, 이후 의도 리프레시(RefreshMonsterTurnPlan)에도 되살아나지 않아야 한다.
    /// 부여 경로(F03 ApplyOrRefreshFieldImmobilize)와 제약 헬퍼는 private이라 기존 테스트(Inject)와 동일하게
    /// 리플렉션으로 구동하고, 결과는 public 투영(MonsterRuntimeState)으로 관찰한다.
    /// </summary>
    public sealed class MonsterIntentControlStatusTests
    {
        [Test]
        public void AMonsterThatWalksInAndAttacksStillPublishesItsAttackIntent()
        {
            // 🔴🔴 2026-09-01 #1의 실증. IntentType은 <b>이동</b> 의도(plan.MovementIntent)라, 걸어와서
            //      때리는 몬스터는 Chase로 남는다 — 이름표 배지가 IntentType==Attack만 보고 있어서
            //      「순수 피해 공격」의 의도 배지가 통째로 빠졌다. 상태이상 유무는 상관관계였을 뿐이다
            //      (상태이상을 주는 패턴은 대개 제자리 원거리라 Attack으로 남았다).
            //      규칙층은 답을 이미 들고 있었다(PendingAttackIntent) — 예고가 그것을 싣기만 하면 된다.
            var state = BuildStateWithMonster(mapSize: 4, monsterAt: new HexCoord(2, 0));

            var preview = state.GetMonsterIntentPreviews(includeUnrevealed: true).Single();

            Assert.That(preview.WillMove, Is.True, "전제: 이번 턴 걸어온다.");
            Assert.That(preview.IntentType, Is.Not.EqualTo(EnemyIntentType.Attack),
                "전제이자 결함의 뿌리 — 걸어오는 턴의 IntentType은 Attack이 아니다.");
            Assert.That(preview.WillAttackPlayer, Is.True,
                "걸어와서 때리는 턴에도 「이번 턴 나를 때린다」는 예고에 실려야 한다.");
            Assert.That(preview.AttackPatternDamage, Is.GreaterThan(0),
                "순수 피해 공격이라 배지가 띄울 수치가 있다.");
        }

        [Test]
        public void AStationaryAttackerStillPublishesItsAttackIntent()
        {
            // 대조군: 제자리에서 때리는 쪽은 종전에도 배지가 떴다. 새 축이 그 경우를 깨지 않는지 본다.
            var state = BuildStateWithMonster(mapSize: 4, monsterAt: new HexCoord(1, 0));

            var preview = state.GetMonsterIntentPreviews(includeUnrevealed: true).Single();

            Assert.That(preview.WillMove, Is.False, "전제: 이미 붙어 있어 걷지 않는다.");
            Assert.That(preview.WillAttackPlayer, Is.True);
        }

        [Test]
        public void Immobilize_OnMovingMonster_CollapsesToStationaryAttack()
        {
            // 플레이어와 거리 2 → 공격 사거리(1) 밖이라 몬스터는 접근(이동)을 계획한다.
            var state = BuildStateWithMonster(mapSize: 4, monsterAt: new HexCoord(2, 0));
            var monster = FirstMonster(state);

            Assert.That(SnapshotOf(state).HasAttackIntent, Is.True, "사전조건: 활성 의도를 가진 몬스터.");
            Assert.That(PredictedMove(monster), Is.Not.EqualTo(CoordOf(monster)), "사전조건: 이동을 포함한 의도여야 한다.");

            // F03 섬광 장판 부여 경로(제약 헬퍼를 그 안에서 호출).
            ApplyFieldImmobilize(state, monster);

            // 속박은 이동만 차단하고 공격은 유지한다 → 이동 의도였던 몬스터도 제자리 공격으로 collapse한다.
            var after = SnapshotOf(state);
            Assert.That(after.HasAttackIntent, Is.True, "속박된 몬스터는 대기가 아니라 제자리 공격으로 전환된다(공격이 기본값).");
            Assert.That(after.Intent.Type, Is.EqualTo(EnemyIntentType.Attack), "제자리 공격 의도는 Attack으로 표현된다.");
            Assert.That(PredictedMove(monster), Is.EqualTo(CoordOf(monster)), "이동은 취소되어 제자리에 머문다.");

            // 회귀 방지: 리프레시 후에도 이동을 되살리지 않고 제자리 공격을 유지한다.
            RefreshIntents(state);
            var refreshed = SnapshotOf(state);
            Assert.That(refreshed.HasAttackIntent, Is.True, "속박이 유지되는 한 리프레시 후에도 제자리 공격을 유지한다.");
            Assert.That(PredictedMove(monster), Is.EqualTo(CoordOf(monster)), "리프레시 후에도 이동이 부활하지 않는다.");
        }

        [Test]
        public void FieldImmobilize_OnMovingMonster_EmitsMoveCancelCue()
        {
            var state = BuildStateWithMonster(mapSize: 4, monsterAt: new HexCoord(2, 0));
            var monster = FirstMonster(state);
            Assert.That(PredictedMove(monster), Is.Not.EqualTo(CoordOf(monster)), "Precondition: monster has a move intent to cancel.");

            state.BeginEffectBuffering();
            ApplyFieldImmobilize(state, monster);

            AssertIntentCancelBuffered(state, expectedMoveCancelled: true, expectedAttackCancelled: false);
        }

        [Test]
        public void TrapStun_OnMovingMonster_EmitsMoveCancelCue()
        {
            var state = BuildStateWithMonster(mapSize: 4, monsterAt: new HexCoord(2, 0));
            var monster = FirstMonster(state);
            Assert.That(PredictedMove(monster), Is.Not.EqualTo(CoordOf(monster)), "Precondition: monster has a move intent to cancel.");

            state.BeginEffectBuffering();
            ApplyTrapStun(state, monster);

            AssertIntentCancelBuffered(state, expectedMoveCancelled: true, expectedAttackCancelled: true);
        }

        [Test]
        public void Immobilize_OnStationaryAttacker_KeepsIntent()
        {
            // 인접(거리 1) → 제자리에서 공격, 이동 없음.
            var state = BuildStateWithMonster(mapSize: 3, monsterAt: new HexCoord(1, 0));
            var monster = FirstMonster(state);

            Assert.That(SnapshotOf(state).HasAttackIntent, Is.True, "사전조건: 활성 공격 의도.");
            Assert.That(PredictedMove(monster), Is.EqualTo(CoordOf(monster)), "사전조건: 제자리 공격(이동 없음)이어야 한다.");

            ApplyFieldImmobilize(state, monster);

            Assert.That(SnapshotOf(state).HasAttackIntent, Is.True,
                "제자리 공격 의도 + 속박은 유지된다(속박=이동만 차단, 공격은 허용).");

            // 회귀 방지: 리프레시 후에도 제자리 공격을 유지한다(현재 타일서 사거리 안).
            RefreshIntents(state);
            Assert.That(SnapshotOf(state).HasAttackIntent, Is.True,
                "사거리 안 제자리 공격자는 속박돼도 리프레시 후 공격 예고를 유지한다.");
        }

        [Test]
        public void Immobilize_OnCommittedAttacker_DoesNotRerollAttackPattern()
        {
            // 페이즈 순서: 플레이어 이동 → 몬스터 이동 → 플레이어 액션 → 몬스터 액션. 플레이어 액션 시점에는
            // 몬스터 이동이 이미 끝났으므로, 플레이어가 거는 상태이상으로 몬스터 행동이 바뀌어도 되는 것은
            // 공격을 무효화하는 기절(Stun)뿐이다. 속박(Immobilize)은 이동만 막을 뿐 이미 확정·예고된 공격
            // 패턴을 RNG로 다시 추첨해 바꿔서는 안 된다(회귀: SetMonsterStationaryAttackIntent의 재선택).
            // 첫 패턴 가중치를 압도적으로 높여, 재추첨이 일어났다면 거의 확실히 index 0으로 바뀌도록 한다.
            var state = BuildStateWithTwoPatternMonster(monsterAt: new HexCoord(1, 0));
            var monster = FirstMonster(state);

            SetAttackPatternIndex(monster, 1);
            Assert.That(SelectedPatternId(state), Is.EqualTo("p1"), "사전조건: 두 번째 패턴으로 공격을 확정한 상태.");
            Assert.That(SnapshotOf(state).HasAttackIntent, Is.True, "사전조건: 활성 공격 의도.");
            Assert.That(PredictedMove(monster), Is.EqualTo(CoordOf(monster)), "사전조건: 제자리 공격(이동 없음).");

            ApplyFieldImmobilize(state, monster);

            Assert.That(SelectedPatternId(state), Is.EqualTo("p1"),
                "속박은 이동만 막는다 — 현재 타일에서 닿는 확정 공격 패턴을 다시 추첨해 바꾸면 안 된다.");
            Assert.That(SnapshotOf(state).HasAttackIntent, Is.True, "속박돼도 제자리 공격은 그대로 유지된다.");
            Assert.That(SnapshotOf(state).Intent.Type, Is.EqualTo(EnemyIntentType.Attack));
        }

        [Test]
        public void Stun_OnStationaryAttacker_BecomesWaiting()
        {
            // 인접한 제자리 공격자라도 기절이면 이동/공격 모두 불가 → 대기.
            var state = BuildStateWithMonster(mapSize: 3, monsterAt: new HexCoord(1, 0));
            var monster = FirstMonster(state);

            Assert.That(SnapshotOf(state).HasAttackIntent, Is.True, "사전조건: 활성 공격 의도.");
            Assert.That(PredictedMove(monster), Is.EqualTo(CoordOf(monster)), "사전조건: 제자리 공격(이동 없음).");

            InjectEffect(state, StatusEffectKind.Stun, SnapshotOf(state).Id, remainingTurns: 1);
            ApplyConstraint(state, monster);

            var after = SnapshotOf(state);
            Assert.That(after.HasAttackIntent, Is.False, "기절은 이동/공격 모두 막아 대기로 만든다.");
            Assert.That(after.Intent.Type, Is.EqualTo(EnemyIntentType.Patrol));

            // 회귀 방지: 기절이 유지되는 한 리프레시 후에도 대기.
            RefreshIntents(state);
            Assert.That(SnapshotOf(state).HasAttackIntent, Is.False, "기절 중에는 리프레시 후에도 대기를 유지한다.");
        }

        [Test]
        public void Stun_ClearsTheAttackRangePreviewTheOverlayIsBuiltFrom()
        {
            // 회귀: 규칙 쪽은 기절한 몬스터의 공격을 막고 계획까지 비웠는데, 예고(preview)만 패턴 사거리를 계속
            // 뱉어서 공격 범위 오버레이가 화면에 그대로 남아 있었다. 오버레이·상태 아이콘 모두 이 좌표 목록에서
            // 파생되므로, 목록이 비는 것이 "아예 안 보인다"의 규칙 쪽 표현이다.
            var state = BuildStateWithMonster(mapSize: 3, monsterAt: new HexCoord(1, 0));
            var monster = FirstMonster(state);

            var before = state.GetMonsterIntentPreviews(includeUnrevealed: true).Single();
            Assert.That(before.AttackRangeCoords, Is.Not.Empty, "사전조건: 기절 전에는 공격 범위를 예고한다.");
            var signatureBefore = state.ComputeMonsterIntentOverlaySignature(includeUnrevealed: true);

            InjectEffect(state, StatusEffectKind.Stun, SnapshotOf(state).Id, remainingTurns: 1);
            ApplyConstraint(state, monster);

            Assert.That(
                state.GetMonsterIntentPreviews(includeUnrevealed: true).Single().AttackRangeCoords,
                Is.Empty,
                "기절한 몬스터는 공격 범위를 예고하지 않는다.");
            // 서명이 안 바뀌면 오버레이 캐시가 갱신되지 않아 화면에는 예전 범위가 그대로 남는다. 제자리 기절은
            // 좌표·패턴·조준을 하나도 바꾸지 않으므로, 기절 비트가 서명에 들어가야만 이 단언이 성립한다.
            Assert.That(
                state.ComputeMonsterIntentOverlaySignature(includeUnrevealed: true),
                Is.Not.EqualTo(signatureBefore),
                "기절 여부가 오버레이 서명에 반영돼야 캐시가 갱신된다.");
        }

        // --- Setup -------------------------------------------------------------------------------

        private static CombatState BuildStateWithMonster(int mapSize, HexCoord monsterAt)
        {
            var state = new CombatState(
                CombatState.CreateDemoMap(mapSize),
                new HexCoord(0, 0),
                monsterAt,
                CombatConfig.Default,
                monsterCatalog: CreateConfigMonsterCatalog(CombatConfig.Default));
            // 이동 페이즈 분리(PlayerMovement→MonsterMovement→PlayerAction) 이후 TryPlayerMove만으로는
            // PlayerAction에 도달하지 않는다. 이 테스트들은 몬스터 의도/TurnPlan만 필요하므로 턴 시작 리프레시를
            // 직접 구동해 활성 의도를 가진 몬스터를 만든다(거리 1=제자리 공격, 거리 2=이동 의도).
            RefreshIntents(state);
            return state;
        }

        private static CombatState BuildStateWithTwoPatternMonster(HexCoord monsterAt)
        {
            var config = CombatConfig.Default;
            var state = new CombatState(
                CombatState.CreateDemoMap(3),
                new HexCoord(0, 0),
                monsterAt,
                config,
                monsterCatalog: CreateTwoPatternMonsterCatalog(config));
            RefreshIntents(state);
            return state;
        }

        private static MonsterCatalogDefinition CreateTwoPatternMonsterCatalog(CombatConfig config)
        {
            // 두 패턴 모두 사거리 1 → 인접(거리 1) 타일에서 둘 다 플레이어에게 닿는다(둘 다 재선택 후보).
            // p0의 가중치를 압도적으로 높여, 재추첨이 일어난다면 거의 확실히 p0이 선택되도록 한다.
            return new MonsterCatalogDefinition(
                "intent-control-two-pattern-monsters",
                "Intent Control Two-Pattern Monsters",
                new[]
                {
                    new MonsterCatalogEntry(
                        CombatCatalogFactory.ThreeEyeDogMonsterId,
                        "Two Pattern Monster",
                        "test",
                        "test.config",
                        config.EnemyMaxHp,
                        config.EnemyChaseRange,
                        10,
                        attackPatterns: new[]
                        {
                            new MonsterAttackPattern("p0", "P0", range: 1, areaRadius: 0, damage: 1, weight: 1_000_000),
                            new MonsterAttackPattern("p1", "P1", range: 1, areaRadius: 0, damage: 1, weight: 1)
                        })
                });
        }

        private static MonsterCatalogDefinition CreateConfigMonsterCatalog(CombatConfig config)
        {
            return new MonsterCatalogDefinition(
                "intent-control-config-monsters",
                "Intent Control Config Monsters",
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

        // --- Reflection bridges (mirror the existing tests' private-state access) -----------------

        private static object FirstMonster(CombatState state)
        {
            var field = typeof(CombatState).GetField("monsters", BindingFlags.Instance | BindingFlags.NonPublic);
            var list = (IEnumerable)field.GetValue(state);
            return list.Cast<object>().First();
        }

        private static MonsterRuntimeState SnapshotOf(CombatState state)
        {
            return state.Monsters.Single();
        }

        private static HexCoord CoordOf(object monster)
        {
            return (HexCoord)monster.GetType().GetProperty("Coord").GetValue(monster);
        }

        private static HexCoord PredictedMove(object monster)
        {
            return (HexCoord)monster.GetType().GetProperty("IntentPredictedMoveCoord").GetValue(monster);
        }

        private static void SetAttackPatternIndex(object monster, int index)
        {
            monster.GetType().GetProperty("AttackPatternIndex").SetValue(monster, index);
        }

        private static string SelectedPatternId(CombatState state)
        {
            var monster = FirstMonster(state);
            var pattern = monster.GetType().GetProperty("CurrentAttackPattern").GetValue(monster);
            return (string)pattern.GetType().GetProperty("Id").GetValue(pattern);
        }

        private static void ApplyFieldImmobilize(CombatState state, object monster)
        {
            var method = typeof(CombatState).GetMethod("ApplyOrRefreshFieldImmobilize", BindingFlags.Instance | BindingFlags.NonPublic);
            method.Invoke(state, new[] { monster, (object)"test.field.immobilize" });
        }

        private static void ApplyTrapStun(CombatState state, object monster)
        {
            var method = typeof(CombatState).GetMethod("ApplyTrapEffect", BindingFlags.Instance | BindingFlags.NonPublic);
            var combatant = (CombatantState)monster.GetType().GetProperty("Combatant").GetValue(monster);
            var trap = new HexTrapData(
                "test-stun-trap",
                CoordOf(monster),
                radius: 0,
                effects: new[] { new HexTrapEffectData(HexTrapEffectKind.Stun, amount: 1, durationTurns: 1) },
                affectsPlayer: false,
                affectsMonsters: true);
            var effect = new HexTrapEffectData(HexTrapEffectKind.Stun, amount: 1, durationTurns: 1);
            var target = new FieldObjectTarget(combatant, CoordOf(monster), FieldObjectTargetKind.Monster);
            method.Invoke(state, new object[] { trap, effect, target });
        }

        private static void AssertIntentCancelBuffered(CombatState state, bool expectedMoveCancelled, bool expectedAttackCancelled)
        {
            var cue = state.BufferedEffects.Single(effect => effect.Kind == EffectKind.AttackCancelled);
            Assert.That((cue.Amount & 1) != 0, Is.EqualTo(expectedMoveCancelled), "Move cancel bit should match.");
            Assert.That((cue.Amount & 2) != 0, Is.EqualTo(expectedAttackCancelled), "Attack cancel bit should match.");
        }

        private static void ApplyConstraint(CombatState state, object monster)
        {
            var method = typeof(CombatState).GetMethod("ApplyControlStatusConstraintToPlan", BindingFlags.Instance | BindingFlags.NonPublic);
            method.Invoke(state, new[] { monster });
        }

        private static void RefreshIntents(CombatState state)
        {
            var method = typeof(CombatState).GetMethod("RefreshMonsterIntentStep", BindingFlags.Instance | BindingFlags.NonPublic);
            method.Invoke(state, null);
        }

        private static void InjectEffect(CombatState state, StatusEffectKind kind, string unitId, int remainingTurns)
        {
            ActiveEffectProbe.Registry(state).Add(new ActiveEffect(EffectType.Duration, kind, unitId, remainingTurns, 0, "test"));
        }
    }
}

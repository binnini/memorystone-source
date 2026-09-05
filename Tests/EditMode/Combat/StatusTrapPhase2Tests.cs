using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// P2(묶음 B) 계약: 스폰 함정(C-7 터렛 · C-10 사이렌) · 무장 해제(C-5) · 쇠약/허점(C-2/C-3).
    ///
    /// 단언은 **관찰 가능한 결과**로 쓴다(몬스터 수·좌표·체력·카드 상태 라벨) — 내부 술어를 부르면
    /// 같은 코드를 두 번 부르는 것이라 아무것도 증명하지 못한다. 계획 §10의 R-6·R-8이 여기서 닫힌다.
    /// </summary>
    public sealed class StatusTrapPhase2Tests
    {
        private const string TurretDefinitionId = "test-turret";
        private const string PackDefinitionId = "test-pack";

        // ------------------------------------------------------------------ 6.1 스폰 함정

        [Test]
        public void SpawnTrapRaisesTrapMonstersSpawnedWithThePlacedCount()
        {
            var state = CreateSpawnTrapState(amount: 2, definitionId: PackDefinitionId);
            var raised = new List<(string TrapId, int Count)>();
            state.TrapMonstersSpawned += (trapId, count) => raised.Add((trapId, count));

            Assert.That(state.TryPlayerMove(TrapCoord), Is.True, state.LastFailureReason);

            Assert.That(raised, Is.EqualTo(new[] { ("spawn-trap", 2) }), "스폰음 신호는 함정당 한 번, 세운 수를 싣는다.");
        }

        [Test]
        public void SteppingOnSpawnTrapPlacesTheAuthoredMonsterCount()
        {
            var state = CreateSpawnTrapState(amount: 2, definitionId: PackDefinitionId);
            Assert.That(state.Monsters, Is.Empty, "전제: 저작된 몬스터가 없어야 스폰만 세어진다.");

            Assert.That(state.TryPlayerMove(TrapCoord), Is.True, state.LastFailureReason);

            Assert.That(state.Monsters.Count, Is.EqualTo(2), state.LastTrapSpawnReport);
            Assert.That(
                state.Monsters.All(monster => monster.Coord.DistanceTo(TrapCoord) <= 2),
                Is.True,
                "밟은 칸 주변에 서야 '밟아서 불렀다'가 읽힌다.");
        }

        /// <summary>
        /// 함정이 부른 몬스터는 <b>정식 위협으로 센다</b> — 보스 기물처럼 승리 판정에서 빠지면
        /// 밟아서 부른 적을 무시해도 이기게 되고, 그러면 함정이 아니다.
        /// </summary>
        [Test]
        public void TrapSpawnedMonstersCarryTheTrapSpawnRoleAndCountAsLivingThreats()
        {
            var state = CreateSpawnTrapState(amount: 1, definitionId: PackDefinitionId);

            Assert.That(state.TryPlayerMove(TrapCoord), Is.True, state.LastFailureReason);

            var spawned = state.Monsters.Single();
            Assert.That(MonsterSpawnRoles.IsTrapSpawn(spawned.SpawnRole), Is.True);
            Assert.That(MonsterSpawnRoles.IsBossProp(spawned.SpawnRole), Is.False, "기물이 아니라 위협이다.");
            Assert.That(state.IsTerminal, Is.False, "살아 있는 스폰이 있는 한 전투가 끝나면 안 된다.");
        }

        /// <summary>
        /// R-6: 스폰 함정이 이동 목적지에서 발동해도 <b>이동은 완주한다</b>. 실측 확인 —
        /// 플레이어 이동은 경로를 한 칸씩 밟지 않고 목적지에 도착한 뒤 함정을 해소하므로,
        /// 스폰이 경로를 막아 이동이 되돌려지는 경로 자체가 없다. 그 사실을 계약으로 굳힌다.
        /// </summary>
        [Test]
        public void MovementCompletesEvenWhenTheDestinationTrapSpawnsMonsters()
        {
            var state = CreateSpawnTrapState(amount: 2, definitionId: PackDefinitionId);

            Assert.That(state.TryPlayerMove(TrapCoord), Is.True, state.LastFailureReason);

            Assert.That(state.PlayerCoord, Is.EqualTo(TrapCoord), "이동은 완주한다(R-6 기본안).");
            Assert.That(
                state.Monsters.Any(monster => monster.Coord == state.PlayerCoord),
                Is.False,
                "스폰이 플레이어 칸을 차지하면 안 된다.");
        }

        [Test]
        public void SpawnTrapPlacesAsManyAsFitAndReportsTheTruncation()
        {
            // 반경 2 안에 놓을 수 있는 칸보다 훨씬 많이 요청한다.
            var state = CreateSpawnTrapState(amount: 99, definitionId: PackDefinitionId);

            Assert.That(state.TryPlayerMove(TrapCoord), Is.True, state.LastFailureReason);

            Assert.That(state.Monsters.Count, Is.GreaterThan(0));
            Assert.That(state.Monsters.Count, Is.LessThan(99));
            Assert.That(
                state.LastTrapSpawnReport,
                Does.Contain("TRUNCATED"),
                "덜 놓였다는 사실이 조용히 사라지면 '왜 2마리가 아니라 1마리지'를 추적할 수 없다.");
        }

        [Test]
        public void SpawnTrapWithAnUnknownMonsterIdSpawnsNothingAndSaysSo()
        {
            var state = CreateSpawnTrapState(amount: 1, definitionId: "no-such-monster");

            Assert.That(state.TryPlayerMove(TrapCoord), Is.True, state.LastFailureReason);

            Assert.That(state.Monsters, Is.Empty);
            Assert.That(state.LastTrapSpawnReport, Does.Contain("TRUNCATED"));
        }

        /// <summary>D-4: 함정은 플레이어 전용. 몬스터가 반경 안에 있어도 증원이 나오지 않는다.</summary>
        [Test]
        public void MonstersDoNotTriggerSpawnTrapsEvenWhenAuthoredToAffectThem()
        {
            // 반경 1이라 플레이어가 밟는 순간 옆 칸의 몬스터가 대상으로 잡히지만, 스폰 효과는
            // 플레이어 대상일 때만 실행된다.
            var state = CreateSpawnTrapState(
                amount: 1,
                definitionId: PackDefinitionId,
                radius: 1,
                affectsPlayer: false,
                affectsMonsters: true,
                monsters: new[] { new MonsterConfig("bystander", new HexCoord(2, 0), 10, definitionId: PackDefinitionId) });

            Assert.That(state.TryPlayerMove(TrapCoord), Is.True, state.LastFailureReason);

            Assert.That(state.Monsters.Count, Is.EqualTo(1), "구경꾼 하나뿐 — 증원은 없다.");
        }

        /// <summary>
        /// D-5: 터렛은 이동 0이다. 예전에는 런타임이 저작 0을 조용히 1로 끌어올려
        /// <b>이동 0을 저작할 방법 자체가 없었다</b>.
        /// </summary>
        [Test]
        public void ZeroMoveMonsterHoldsItsTileInsteadOfBeingPromotedToOne()
        {
            var state = CreateSpawnTrapState(
                amount: 0,
                definitionId: TurretDefinitionId,
                monsters: new[] { new MonsterConfig("turret", new HexCoord(3, 0), 10, definitionId: TurretDefinitionId) });
            var turretCoord = state.Monsters.Single().Coord;

            Assert.That(state.TryPlayerMove(new HexCoord(1, 0)), Is.True, state.LastFailureReason);
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();

            Assert.That(state.Monsters.Single().Coord, Is.EqualTo(turretCoord), "이동력 0은 제자리다.");
        }

        // ------------------------------------------------------------------ 6.2 무장 해제

        /// <summary>
        /// 3면 일치(<c>ScoutCardsTests</c> 선례): UI usable 플래그 · 회색 라벨 · 실제 실행 거부가
        /// 같은 술어를 지나야 한다. 한쪽만 고치면 "버튼은 살아 있는데 실행은 거부"가 된다.
        /// </summary>
        [Test]
        public void DisarmAgreesAcrossUsableFlagLabelAndExecutionForAttackCards()
        {
            var state = EnterActionPhaseOnDemo();
            var attack = state.GetCombatCards().FirstOrDefault(card => card.Kind == CombatCardKind.Attack);
            Assert.That(attack.Id, Is.Not.Null.And.Not.Empty, "전제: 손에 공격 카드가 있어야 한다.");

            Inject(state, StatusEffectKind.Disarm, state.Player.Id, remainingTurns: 2, amount: 0);

            var disarmed = state.GetCombatCards().Single(card => card.Id == attack.Id);
            Assert.That(disarmed.IsUsable, Is.False, "① UI usable 플래그");
            Assert.That(disarmed.Status, Is.EqualTo(CombatCardStatusText.Disarmed), "② 회색 라벨");
            Assert.That(state.TryPlayerAttack(), Is.False, "③ 실행 거부");
            Assert.That(state.LastFailureReason, Does.Contain("무장 해제"));
        }

        [Test]
        public void DisarmLeavesNonAttackCardsAlone()
        {
            var state = EnterActionPhaseOnDemo();
            Inject(state, StatusEffectKind.Disarm, state.Player.Id, remainingTurns: 2, amount: 0);

            Assert.That(
                state.GetCombatCards().Where(card => card.Kind != CombatCardKind.Attack).All(card =>
                    card.Status != CombatCardStatusText.Disarmed),
                Is.True,
                "무장 해제는 공격 카드만 막는다 — 이동·방어·정찰은 그대로다.");
            Assert.That(state.TryPlayerDefend(), Is.True, state.LastFailureReason);
        }

        [Test]
        public void DisarmDoesNotBlockMovement()
        {
            var state = new CombatState(CombatState.CreateDemoMap(3), new HexCoord(0, 0), new HexCoord(3, 0), CombatConfig.Default);
            Inject(state, StatusEffectKind.Disarm, state.Player.Id, remainingTurns: 2, amount: 0);

            Assert.That(state.TryPlayerMove(new HexCoord(1, 0)), Is.True, state.LastFailureReason);
        }

        /// <summary>
        /// 둘 다 걸렸으면 <b>더 넓게 막는 기절</b>이 원인으로 읽혀야 한다 — 무장 해제만 지워도
        /// 여전히 못 쓰기 때문이다.
        /// </summary>
        [Test]
        public void StunOutranksDisarmInTheGreyLabel()
        {
            var state = EnterActionPhaseOnDemo();
            var attack = state.GetCombatCards().First(card => card.Kind == CombatCardKind.Attack);
            Inject(state, StatusEffectKind.Disarm, state.Player.Id, remainingTurns: 2, amount: 0);
            Inject(state, StatusEffectKind.Stun, state.Player.Id, remainingTurns: 2, amount: 0);

            Assert.That(
                state.GetCombatCards().Single(card => card.Id == attack.Id).Status,
                Is.EqualTo(CombatCardStatusText.Stunned));
        }

        // ------------------------------------------------------------------ 6.3 쇠약 · 허점

        [Test]
        public void WeakenCutsThePlayersOutgoingCardDamageByTheAuthoredPercent()
        {
            var baseline = MeasurePlayerAttack(state => { });

            var weakened = MeasurePlayerAttack(state =>
                Inject(state, StatusEffectKind.Weaken, state.Player.Id, remainingTurns: 2, amount: 30));

            Assert.That(baseline.PerHit, Is.GreaterThan(0), "전제: 기준 공격이 실제로 피해를 준다.");
            Assert.That(
                weakened.PerHit,
                Is.EqualTo(baseline.PerHit * 70 / 100),
                "쇠약 30%는 나가는 피해를 30% 깎는다(O-10).");
        }

        /// <summary>
        /// 허점은 <b>배율이 아니라 타격당 고정치</b>다(D-10) — 그래서 다타 카드와 시너지가 난다.
        /// 기준 공격이 실제로 다타라는 점이 이 계약을 재는 데 유리하다: 총 피해 증가분이
        /// amount × 타격 수여야 하고, 배율이었다면 그런 모양이 나오지 않는다.
        /// </summary>
        [Test]
        public void VulnerableAddsAFlatAmountToEveryIncomingHitOnTheMonster()
        {
            var baseline = MeasurePlayerAttack(state => { });

            var vulnerable = MeasurePlayerAttack(state =>
                Inject(state, StatusEffectKind.Vulnerable, state.Monsters.First().Id, remainingTurns: 2, amount: 2));

            Assert.That(vulnerable.PerHit, Is.EqualTo(baseline.PerHit + 2), "타격 하나마다 고정 +2.");
            Assert.That(
                vulnerable.Total - baseline.Total,
                Is.EqualTo(2 * baseline.HitCount),
                $"다타({baseline.HitCount}회)에서는 타격마다 붙어야 한다 — 배율이면 이 모양이 나오지 않는다.");
        }

        [Test]
        public void WeakenAndStrengthCancelThroughTheSameOutgoingAxis()
        {
            var baseline = MeasurePlayerAttack(state => { });

            var both = MeasurePlayerAttack(state =>
            {
                Inject(state, StatusEffectKind.Weaken, state.Player.Id, remainingTurns: 2, amount: 30);
                Inject(state, StatusEffectKind.Strength, state.Player.Id, remainingTurns: 2, amount: 30);
            });

            Assert.That(
                both.PerHit,
                Is.EqualTo(baseline.PerHit),
                "부호는 축 이름이 들고 있으므로 같은 크기의 강화·쇠약은 상쇄돼야 한다.");
        }

        /// <summary>
        /// R-8: 몬스터 의도 예고 수치 = 실제 피해. 예고와 집행이 같은 해소 함수를 부르므로
        /// 갈라질 수 없지만, 그 사실 자체를 종류별로 잠근다.
        /// </summary>
        [TestCase(StatusEffectKind.Weaken, 30, TestName = "IntentMatchesActual_MonsterWeakened")]
        [TestCase(StatusEffectKind.Vulnerable, 2, TestName = "IntentMatchesActual_PlayerVulnerable")]
        public void MonsterIntentPreviewDamageMatchesWhatActuallyLands(StatusEffectKind kind, int amount)
        {
            var state = new CombatState(
                CombatState.CreateDemoMap(4),
                new HexCoord(0, 0),
                new HexCoord(2, 0),
                CombatConfig.Default,
                monsterCatalog: CreateConfigMonsterCatalog(CombatConfig.Default));
            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();

            var monster = state.Monsters.Single();
            var targetUnitId = kind == StatusEffectKind.Vulnerable ? state.Player.Id : monster.Id;
            Inject(state, kind, targetUnitId, remainingTurns: 3, amount: amount);

            var previewed = state.GetMonsterIntentPreviews(includeUnrevealed: true)
                .Single(preview => preview.MonsterId == monster.Id)
                .AttackPatternDamage;
            Assert.That(previewed, Is.GreaterThan(0), "전제: 이 몬스터는 실제로 때릴 참이다.");

            var hpBefore = state.Player.Hp;
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            Assert.That(
                hpBefore - state.Player.Hp,
                Is.EqualTo(previewed),
                $"{kind}: 예고한 숫자와 실제로 깎인 체력이 달라졌다(R-8).");
        }

        // ------------------------------------------------------------------ helpers

        // 시작 칸(0,0)에서 한 칸 — 기본 이동력 안이라 이동 실패로 계약이 흐려지지 않는다.
        private static readonly HexCoord TrapCoord = new HexCoord(1, 0);

        private static CombatState CreateSpawnTrapState(
            int amount,
            string definitionId,
            int radius = 0,
            bool affectsPlayer = true,
            bool affectsMonsters = false,
            IEnumerable<MonsterConfig> monsters = null)
        {
            var trap = new HexTrapData(
                "spawn-trap",
                TrapCoord,
                radius: radius,
                effects: new[] { new HexTrapEffectData(HexTrapEffectKind.SpawnMonsters, amount, 0, definitionId) },
                affectsPlayer: affectsPlayer,
                affectsMonsters: affectsMonsters);

            // 반경 3 디스크(37칸): 스폰 후보가 넉넉히 있으면서도 99마리를 다 받지는 못한다
            // (스폰 탐색 반경이 2라 후보 상한이 18칸) — 부분 스폰 계약을 잰다.
            var map = new HexMapData(BuildDisc(3), trapRefs: new[] { trap });
            return new CombatState(
                map,
                new HexCoord(0, 0),
                monsters ?? Array.Empty<MonsterConfig>(),
                CombatConfig.Default,
                monsterCatalog: CreateSpawnTestCatalog());
        }

        private static IEnumerable<HexCellData> BuildDisc(int radius)
        {
            for (var q = -radius; q <= radius; q++)
            {
                for (var r = -radius; r <= radius; r++)
                {
                    var coord = new HexCoord(q, r);
                    if (Math.Abs(coord.S) <= radius)
                    {
                        yield return new HexCellData(coord, $"cell-{q}-{r}", "street", 1, true, false);
                    }
                }
            }
        }

        private static MonsterCatalogDefinition CreateSpawnTestCatalog()
        {
            return new MonsterCatalogDefinition(
                "spawn-trap-test-monsters",
                "Spawn Trap Test Monsters",
                new[]
                {
                    // 터렛: 이동 0(D-5).
                    new MonsterCatalogEntry(TurretDefinitionId, "Test Turret", "test", "test.turret", 6, 0, 10),
                    new MonsterCatalogEntry(PackDefinitionId, "Test Pack", "test", "test.pack", 6, 1, 10)
                });
        }

        private static MonsterCatalogDefinition CreateConfigMonsterCatalog(CombatConfig config)
        {
            return new MonsterCatalogDefinition(
                "phase2-config-monsters",
                "Phase 2 Config Monsters",
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

        private static CombatState EnterActionPhaseOnDemo()
        {
            var state = CombatState.CreateDefaultDemo();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction));
            return state;
        }

        /// <summary>
        /// 같은 초기 상태에서 <paramref name="arrange"/>만 다르게 걸고 공격 한 번을 잰다.
        /// 카탈로그의 절대 수치에 의존하지 않으려고 기준선과의 차이로 계약을 쓴다.
        ///
        /// <b>타격당 값</b>은 연출 이벤트에서 읽는다 — 그래야 "실제로 깎인 체력"과 "화면에 뜬 숫자"가
        /// 같은지도 함께 잠긴다(허점은 맞는 쪽에 걸리므로 숫자를 대상마다 다시 풀어야 한다).
        /// </summary>
        private static (int PerHit, int Total, int HitCount) MeasurePlayerAttack(Action<CombatState> arrange)
        {
            var state = EnterActionPhaseOnDemo();
            Assert.That(state.Monsters, Is.Not.Empty, "전제: 때릴 몬스터가 있어야 한다.");
            arrange(state);

            var hits = new List<int>();
            state.EffectResolved += resultEvent =>
            {
                if (resultEvent.Kind == EffectKind.Damage
                    && string.Equals(resultEvent.SourceActorKind, "player", StringComparison.Ordinal))
                {
                    hits.Add(resultEvent.Amount);
                }
            };

            var hpBefore = state.Monsters.Sum(monster => monster.Hp);
            Assert.That(state.TryPlayerAttack(), Is.True, state.LastFailureReason);
            var total = hpBefore - state.Monsters.Sum(monster => monster.Hp);

            Assert.That(hits, Is.Not.Empty, "전제: 공격이 피해 연출을 하나는 낸다.");
            Assert.That(hits.Distinct().Count(), Is.EqualTo(1), "이 픽스처는 단일 대상이라 타격값이 갈리면 안 된다.");
            Assert.That(total, Is.EqualTo(hits.Sum()), "연출에 뜬 숫자의 합이 실제로 깎인 체력과 같아야 한다.");
            return (hits[0], total, hits.Count);
        }

        private static void Inject(CombatState state, StatusEffectKind kind, string unitId, int remainingTurns, int amount)
        {
            // AddDurationStatusEffect는 오버로드가 여러 개라 시그니처를 명시해야 Ambiguous가 나지 않는다.
            typeof(CombatState)
                .GetMethod(
                    "AddDurationStatusEffect",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null,
                    types: new[] { typeof(StatusEffectKind), typeof(string), typeof(int), typeof(int), typeof(string) },
                    modifiers: null)
                .Invoke(state, new object[] { kind, unitId, remainingTurns, amount, "test" });
        }
    }
}

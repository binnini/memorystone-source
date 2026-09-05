using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class TrapEventTests
    {
        [Test]
        public void MovingOntoDamageTrapAppliesDamageAndConsumesOneShotTrap()
        {
            var state = CreateState(new HexTrapData(
                "spike-a",
                new HexCoord(1, 0),
                radius: 0,
                effects: new[] { new HexTrapEffectData(HexTrapEffectKind.Damage, 3) }));

            Assert.That(state.TryPlayerMove(new HexCoord(1, 0)), Is.True);
            Assert.That(state.Player.Hp, Is.EqualTo(17));
            Assert.That(state.ConsumedTrapIds, Does.Contain("spike-a"));

            AdvanceTurn(state);
            Assert.That(state.TryPlayerMove(new HexCoord(0, 0)), Is.True);
            AdvanceTurn(state);
            Assert.That(state.TryPlayerMove(new HexCoord(1, 0)), Is.True);

            Assert.That(state.Player.Hp, Is.EqualTo(17), "One-shot trap should not fire a second time.");
        }

        [Test]
        public void TrapRadiusCanDamagePlayerAndMonsterTargets()
        {
            var state = CreateState(new HexTrapData(
                "blast-a",
                new HexCoord(1, 0),
                radius: 1,
                effects: new[] { new HexTrapEffectData(HexTrapEffectKind.Damage, 4) },
                affectsPlayer: true,
                affectsMonsters: true));

            Assert.That(state.TryPlayerMove(new HexCoord(1, 0)), Is.True);

            Assert.That(state.Player.Hp, Is.EqualTo(16));
            Assert.That(state.Monsters.Single().Hp, Is.EqualTo(6));
        }

        [Test]
        public void TrapEffectEventCarriesTrapSourceCenterAndRadiusForPresentation()
        {
            var state = CreateState(new HexTrapData(
                "blast-a",
                new HexCoord(1, 0),
                radius: 1,
                effects: new[] { new HexTrapEffectData(HexTrapEffectKind.Damage, 4) },
                affectsPlayer: true,
                affectsMonsters: true));
            EffectResultEvent captured = default;
            var count = 0;
            state.EffectResolved += resultEvent =>
            {
                if (count == 0)
                {
                    captured = resultEvent;
                }

                count++;
            };

            Assert.That(state.TryPlayerMove(new HexCoord(1, 0)), Is.True);

            Assert.That(count, Is.GreaterThanOrEqualTo(1));
            Assert.That(captured.SourceRef, Is.EqualTo("trap.blast-a"));
            Assert.That(captured.Center, Is.EqualTo(new HexCoord(1, 0)));
            Assert.That(captured.Radius, Is.EqualTo(1));
        }

        [TestCase(HexTrapEffectKind.Poison, StatusEffectKind.Poison)]
        public void DamageOverTimeTrapRegistersStatusAndTicksOnNextPlayerTurns(HexTrapEffectKind trapKind, StatusEffectKind effectKind)
        {
            var state = CreateState(new HexTrapData(
                $"{trapKind}-a",
                new HexCoord(1, 0),
                radius: 0,
                effects: new[] { new HexTrapEffectData(trapKind, amount: 2, durationTurns: 2) }));

            Assert.That(state.TryPlayerMove(new HexCoord(1, 0)), Is.True);
            Assert.That(state.ActiveEffects.Single().Kind, Is.EqualTo(effectKind));
            Assert.That(state.Player.Hp, Is.EqualTo(20), "Damage-over-time should not tick immediately on trap entry.");

            // 적용 턴의 첫 틱은 SkipNextTick으로 건너뛴다 — "N턴 동안"에 적용 턴은 포함되지 않는다.
            AdvanceTurn(state);
            Assert.That(state.Player.Hp, Is.EqualTo(20), "The application turn's tick is skipped.");
            Assert.That(state.ActiveEffects.Single().RemainingTurns, Is.EqualTo(2));

            AdvanceTurn(state);
            Assert.That(state.Player.Hp, Is.EqualTo(18));
            Assert.That(state.ActiveEffects.Single().RemainingTurns, Is.EqualTo(1));

            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            AdvanceTurn(state);
            Assert.That(state.Player.Hp, Is.EqualTo(16));
            Assert.That(state.ActiveEffects, Is.Empty);
        }

        [Test]
        public void StunTrapBlocksPlayerActionCardsWhileActive()
        {
            var state = CreateState(new HexTrapData(
                "stun-a",
                new HexCoord(1, 0),
                radius: 0,
                effects: new[] { new HexTrapEffectData(HexTrapEffectKind.Stun, amount: 1, durationTurns: 2) }));

            Assert.That(state.TryPlayerMove(new HexCoord(1, 0)), Is.True);
            // DEC-2026-07-03-02: 액션 카드는 EndAction + 몬스터 이동 해석 후에야 사용 가능.
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();

            Assert.That(state.TryPlayerAttack(), Is.False);
            Assert.That(state.LastFailureReason, Does.Contain("기절"));
            Assert.That(state.ActiveEffects.Single().Kind, Is.EqualTo(StatusEffectKind.Stun));
        }

        [Test]
        public void RadiusStunTrapCanHoldMonsterInPlace()
        {
            var state = CreateStateWithEnemy(new HexCoord(3, 0), new HexTrapData(
                "stun-burst-a",
                new HexCoord(1, 0),
                radius: 2,
                effects: new[] { new HexTrapEffectData(HexTrapEffectKind.Stun, amount: 1, durationTurns: 2) },
                affectsPlayer: false,
                affectsMonsters: true));

            Assert.That(state.TryPlayerMove(new HexCoord(1, 0)), Is.True);
            Assert.That(state.ActiveEffects.Single().TargetUnitId, Is.EqualTo(state.Monsters.Single().Id));

            // 몬스터 이동은 MonsterMovement 페이즈에서 해석된다 — 기절 몬스터는 제자리 유지.
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();

            Assert.That(state.Monsters.Single().Coord, Is.EqualTo(new HexCoord(3, 0)));
        }

        [Test]
        public void SlowTrapReducesNextPlayerMovementRange()
        {
            var state = CreateStateWithEnemy(new HexCoord(5, 0), new HexTrapData(
                "slow-a",
                new HexCoord(1, 0),
                radius: 0,
                effects: new[] { new HexTrapEffectData(HexTrapEffectKind.Slow, amount: 4, durationTurns: 2) }));

            Assert.That(state.TryPlayerMove(new HexCoord(1, 0)), Is.True);
            AdvanceTurn(state);

            Assert.That(state.PlayerCoord, Is.EqualTo(new HexCoord(1, 0)));
            Assert.That(state.ActiveEffects.Single(effect => effect.Kind == StatusEffectKind.Slow).Amount, Is.EqualTo(4));
            Assert.That(state.TryPlayerMove(new HexCoord(0, 0)), Is.False, "Slow should reduce movement range after existing movement bonuses are applied.");
            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
        }

        [Test]
        public void DebugMoveCanTriggerTrapWithoutMovementCardRangeOrPhaseChange()
        {
            var state = CreateStateWithEnemy(new HexCoord(5, 0), new HexTrapData(
                "debug-spike-a",
                new HexCoord(4, 0),
                radius: 0,
                effects: new[] { new HexTrapEffectData(HexTrapEffectKind.Damage, 5) }));

            Assert.That(state.TryPlayerMove(new HexCoord(4, 0)), Is.False, "Normal movement should still respect card range.");
            Assert.That(state.PlayerCoord, Is.EqualTo(new HexCoord(0, 0)));
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerMovement));

            Assert.That(state.TryDebugMovePlayer(new HexCoord(4, 0)), Is.True);

            Assert.That(state.PlayerCoord, Is.EqualTo(new HexCoord(4, 0)));
            Assert.That(state.Player.Hp, Is.EqualTo(15));
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerMovement), "Debug movement should not spend or advance the real movement card phase.");
            Assert.That(state.ConsumedTrapIds, Does.Contain("debug-spike-a"));
        }

        /// <summary>
        /// C-6: 저작 의도(시야 감소 함정)는 원래부터 맵에 있었지만 대응하는 상태이상이 없어 밟으면
        /// 크래시했다. 실명이 생기면서 저작이 그대로 살아난다.
        /// </summary>
        [Test]
        public void VisionDownTrapAppliesBlindAndShrinksVision()
        {
            var state = CreateState(new HexTrapData(
                "blind-a",
                new HexCoord(1, 0),
                radius: 0,
                effects: new[] { new HexTrapEffectData(HexTrapEffectKind.VisionDown, amount: 1, durationTurns: 2) }));

            // 픽스처 시야 반경은 2 — 밟기 전에는 (3,0)이 보인다.
            Assert.That(state.TryPlayerMove(new HexCoord(1, 0)), Is.True);

            var effect = state.ActiveEffects.Single();
            Assert.That(effect.Kind, Is.EqualTo(StatusEffectKind.Blind));
            Assert.That(effect.Amount, Is.EqualTo(1));
            Assert.That(
                state.GetVisibility(new HexCoord(3, 0)),
                Is.Not.EqualTo(HexCellVisibility.Revealed),
                "실명 함정을 밟은 순간 안개가 좁혀져야 한다.");
            Assert.That(state.GetVisibility(new HexCoord(2, 0)), Is.EqualTo(HexCellVisibility.Revealed));
        }

        [Test]
        public void TeleportTrapMovesPlayerToAValidTileWithinItsRadius()
        {
            var state = CreateStateWithEnemy(new HexCoord(5, 0), new HexTrapData(
                "teleport-a",
                new HexCoord(1, 0),
                radius: 0,
                // Teleport는 Amount가 지속 턴이 아니라 반경이다.
                effects: new[] { new HexTrapEffectData(HexTrapEffectKind.Teleport, amount: 2) }));

            Assert.That(state.TryPlayerMove(new HexCoord(1, 0)), Is.True);

            Assert.That(state.PlayerCoord, Is.Not.EqualTo(new HexCoord(1, 0)), "텔레포트가 실제로 플레이어를 옮겨야 한다.");
            Assert.That(new HexCoord(1, 0).DistanceTo(state.PlayerCoord), Is.LessThanOrEqualTo(2));
            Assert.That(state.PlayerCoord, Is.Not.EqualTo(new HexCoord(5, 0)), "몬스터가 선 칸에는 착지하지 않는다.");
            Assert.That(state.ConsumedTrapIds, Does.Contain("teleport-a"));
        }

        [Test]
        public void TeleportTrapWithNoValidLandingTileIsANoOpButStillConsumesTheTrap()
        {
            // 반경 0 = 후보 없음(현재 칸은 항상 제외된다).
            var state = CreateState(new HexTrapData(
                "teleport-dead",
                new HexCoord(1, 0),
                radius: 0,
                effects: new[] { new HexTrapEffectData(HexTrapEffectKind.Teleport, amount: 0, durationTurns: 1) }));

            Assert.That(state.TryPlayerMove(new HexCoord(1, 0)), Is.True);

            Assert.That(state.PlayerCoord, Is.EqualTo(new HexCoord(1, 0)));
            Assert.That(state.ConsumedTrapIds, Does.Contain("teleport-dead"), "후보가 없어도 발동 소모는 유지된다.");
        }

        /// <summary>D-3: 착지 칸의 <b>다른</b> 함정은 연쇄 발동한다.</summary>
        [Test]
        public void TeleportChainsIntoOtherTrapsAtTheLandingTile()
        {
            // 착지 후보를 (2,0) 하나로 좁히기 위해 반경 1 + 출발 칸 제외 + (0,0)은 플레이어 직전 위치가 아니라
            // 실제로 비어 있으므로, 두 후보 모두에 같은 피해 함정을 깔아 결정론을 만든다.
            var state = CreateStateWithEnemy(new HexCoord(5, 0),
                new HexTrapData(
                    "teleport-b",
                    new HexCoord(1, 0),
                    radius: 0,
                    effects: new[] { new HexTrapEffectData(HexTrapEffectKind.Teleport, amount: 1) }),
                new HexTrapData(
                    "spike-left",
                    new HexCoord(0, 0),
                    radius: 0,
                    effects: new[] { new HexTrapEffectData(HexTrapEffectKind.Damage, 3) }),
                new HexTrapData(
                    "spike-right",
                    new HexCoord(2, 0),
                    radius: 0,
                    effects: new[] { new HexTrapEffectData(HexTrapEffectKind.Damage, 3) }));

            Assert.That(state.TryPlayerMove(new HexCoord(1, 0)), Is.True);

            Assert.That(state.Player.Hp, Is.EqualTo(17), "착지 칸의 피해 함정이 연쇄 발동해야 한다.");
            Assert.That(new[] { new HexCoord(0, 0), new HexCoord(2, 0) }, Does.Contain(state.PlayerCoord));
        }

        /// <summary>D-3: 텔레포트 → 텔레포트만 차단한다(무한 순환 방지).</summary>
        [Test]
        public void TeleportDoesNotChainIntoAnotherTeleportTrap()
        {
            // 재사용형(one-shot 아님) 텔레포트 두 장을 마주 보게 깔아 순환 조건을 만든다.
            var state = CreateStateWithEnemy(new HexCoord(5, 0),
                new HexTrapData(
                    "teleport-loop-a",
                    new HexCoord(1, 0),
                    radius: 0,
                    effects: new[] { new HexTrapEffectData(HexTrapEffectKind.Teleport, amount: 1) },
                    affectsPlayer: true,
                    affectsMonsters: false,
                    oneShot: false),
                new HexTrapData(
                    "teleport-loop-b",
                    new HexCoord(2, 0),
                    radius: 0,
                    effects: new[] { new HexTrapEffectData(HexTrapEffectKind.Teleport, amount: 1) },
                    affectsPlayer: true,
                    affectsMonsters: false,
                    oneShot: false));

            // 무한 루프면 여기서 스택 오버플로가 난다 — 반환된다는 사실 자체가 계약의 절반이다.
            Assert.That(state.TryPlayerMove(new HexCoord(1, 0)), Is.True);

            Assert.That(new[] { new HexCoord(0, 0), new HexCoord(2, 0) }, Does.Contain(state.PlayerCoord));
        }

        [Test]
        public void ConsumingAOneShotTrapClearsItsDiscoveryMarker()
        {
            // 🔴 「함정이 사용됐는데 사라지지 않음」(2026-09-01 #10)의 이음매. 규칙(consumedTrapIds)과
            //    화면(trapRevealed)은 <b>다른 저장소</b>다 — 소진만 찍고 발견 표시를 안 지우면 규칙은
            //    맞는데 표식만 남는다. 기존 테스트는 소진과 "두 번 안 터짐"만 재고 있어서 이 갈래를
            //    아무도 보지 않았다. 화면이 읽는 술어(GetVisibilitySafeCellInfo.TrapRevealed)로 잰다.
            var coord = new HexCoord(1, 0);
            var state = CreateState(new HexTrapData(
                "spike-marker",
                coord,
                radius: 0,
                effects: new[] { new HexTrapEffectData(HexTrapEffectKind.Damage, 3) }));

            VisibilityRuntimeOf(state).RevealTrapAt(coord);
            Assert.That(state.GetVisibilitySafeCellInfo(coord).TrapRevealed, Is.True,
                "전제: 정찰로 발견된 함정은 표식이 켜져 있다.");

            Assert.That(state.TryPlayerMove(coord), Is.True);

            Assert.That(state.ConsumedTrapIds, Does.Contain("spike-marker"), "규칙은 소진을 찍는다.");
            Assert.That(state.GetVisibilitySafeCellInfo(coord).TrapRevealed, Is.False,
                "소진된 일회성 함정의 표식은 같은 순간에 꺼져야 한다 — 규칙만 끝나면 화면에 유령이 남는다.");
        }

        private static HexVisibilityRuntime VisibilityRuntimeOf(CombatState state)
        {
            return (HexVisibilityRuntime)typeof(CombatState)
                .GetField("visibilityRuntime", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(state);
        }

        private static CombatState CreateState(params HexTrapData[] traps)
        {
            return CreateStateWithEnemy(new HexCoord(2, 0), traps);
        }

        private static CombatState CreateStateWithEnemy(HexCoord enemyCoord, params HexTrapData[] traps)
        {
            var config = new CombatConfig(20, 10, 2, 1, 4, 4, 0, 1, 0, 3, 1, 4, 2);
            var map = new HexMapData(
                Enumerable.Range(-1, 7).Select(q => new HexCellData(new HexCoord(q, 0), $"cell-{q}", "street", 1, true, false)),
                trapRefs: traps);
            return new CombatState(
                map,
                new HexCoord(0, 0),
                enemyCoord,
                config,
                monsterCatalog: CreateConfigMonsterCatalog(config));
        }

        private static MonsterCatalogDefinition CreateConfigMonsterCatalog(CombatConfig config)
        {
            return new MonsterCatalogDefinition(
                "trap-test-config-monsters",
                "Trap Test Config Monsters",
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

        private static void AdvanceTurn(CombatState state)
        {
            // DEC-2026-07-03-02: 한 턴 완주 = EndAction → 몬스터 이동 해석 → PlayerAction →
            // EndAction → 몬스터 행동 해석 → 다음 턴 PlayerMovement.
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerMovement));
        }
    }
}






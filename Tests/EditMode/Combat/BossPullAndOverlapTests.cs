using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// §16의 두 계약:
    ///  · §16.1 <b>끌어당김</b> — 넉백 거리의 부호가 방향이다(음수 = 소스 쪽으로).
    ///  · §16.2 <b>겹침 해소</b> — 보스가 커지며 플레이어를 삼키면 몸통 밖으로 밀어낸다.
    /// </summary>
    public sealed class BossPullAndOverlapTests
    {
        private const string BossSpawnRefId = "boss-spawn";
        private const string ArenaId = "boss-arena-1";
        private const string BossUnitId = "boss-01";
        private const string BossDefinitionId = "M002";
        private static readonly HexCoord ArenaCenter = new HexCoord(6, 0);
        private const int ArenaRadius = 4;

        // -----------------------------------------------------------------------------------------
        // §16.1 끌어당김
        // -----------------------------------------------------------------------------------------

        [Test]
        public void NegativeKnockbackPullsTheTargetTowardTheSource()
        {
            var state = CreateOpenState();
            var source = new HexCoord(0, 0);
            var before = source.DistanceTo(state.PlayerCoord);

            state.ApplyDirectionalKnockback(source, "player", distance: -2, impactDamage: 0);

            Assert.That(source.DistanceTo(state.PlayerCoord), Is.EqualTo(before - 2), "음수 거리는 소스 쪽으로 당긴다.");
        }

        [Test]
        public void PositiveKnockbackStillPushesAway()
        {
            // 양성 대조: 부호만 뒤집으면 방향도 뒤집힌다.
            var state = CreateOpenState();
            var source = new HexCoord(0, 0);
            var before = source.DistanceTo(state.PlayerCoord);

            state.ApplyDirectionalKnockback(source, "player", distance: 2, impactDamage: 0);

            Assert.That(source.DistanceTo(state.PlayerCoord), Is.EqualTo(before + 2));
        }

        [Test]
        public void TheKnockbackEventCarriesTheDirectionInItsSign()
        {
            // 🔴 표현층이 끌어당김과 밀치기를 구별할 유일한 단서다(2026-09-01 #5: 둘 다 「밀려남」으로 떴다).
            //    부호를 버리면 아트를 아무리 넣어도 「미는 그림」이 남는다 — 2026-08-11 배지 트랙의 재현.
            List<EffectResultEvent> Capture(int distance)
            {
                var state = CreateOpenState();
                var seen = new List<EffectResultEvent>();
                state.EffectResolved += resultEvent =>
                {
                    if (resultEvent.Kind == EffectKind.Knockback)
                    {
                        seen.Add(resultEvent);
                    }
                };
                state.ApplyDirectionalKnockback(new HexCoord(0, 0), "player", distance, impactDamage: 0);
                return seen;
            }

            var pulled = Capture(-2).Single();
            Assert.That(pulled.AppliedAmount, Is.EqualTo(-2), "끌어당김은 음수로 나간다(§16.1).");

            var pushed = Capture(2).Single();
            Assert.That(pushed.AppliedAmount, Is.EqualTo(2), "밀치기는 양수 그대로다.");
        }

        [Test]
        public void PullStopsAtOccupiedCellsInsteadOfLandingInsideTheSource()
        {
            // 소스 칸은 점유돼 있어 도달할 수 없다 — 당겨서 몸에 박히는 사고가 구조적으로 불가능하다는 계약.
            var state = CreateOpenState(monsterAt: new HexCoord(0, 0));
            var monsterCoord = new HexCoord(0, 0);

            state.ApplyDirectionalKnockback(monsterCoord, "player", distance: -99, impactDamage: 0);

            Assert.That(state.PlayerCoord, Is.Not.EqualTo(monsterCoord));
            Assert.That(monsterCoord.DistanceTo(state.PlayerCoord), Is.EqualTo(1), "몸 바로 앞에서 멈춘다.");
        }

        // -----------------------------------------------------------------------------------------
        // §16.2 겹침 해소
        // -----------------------------------------------------------------------------------------

        [Test]
        public void GrowingBossPushesTheSwallowedPlayerOutOfItsBody()
        {
            // 겹침을 <b>강제</b>한다: 1페이즈(반경 0) 보스 바로 옆에 서면 2페이즈 원판이 그 칸을 덮는다.
            // 강제하지 않으면 보스가 멀리 있을 때 시험이 아무것도 관찰하지 못하고 통과한다.
            var state = CreateSealedArenaState();
            var adjacent = BossCoord(state).NeighborsInDirectionOrder().First(coord => state.Map.Contains(coord));
            Assert.That(state.TryDebugMovePlayer(adjacent), Is.True);
            Assert.That(BossCoord(state).DistanceTo(state.PlayerCoord), Is.EqualTo(1), "전환 전에는 몸통 밖(반경 0)이다.");

            AdvanceToPhaseTwo(state);

            Assert.That(
                BossCoord(state).DistanceTo(state.PlayerCoord),
                Is.GreaterThan(1),
                "전환이 끝난 뒤 플레이어는 보스 몸통(반경 1) 밖에 있어야 한다.");
            Assert.That(
                state.BossFootprintCoords.Contains(state.PlayerCoord),
                Is.False,
                "플레이어는 몸통 칸 위에 남지 않는다.");
        }

        // -----------------------------------------------------------------------------------------
        // 픽스처
        // -----------------------------------------------------------------------------------------

        private static HexCoord BossCoord(CombatState state) =>
            state.Monsters.Single(monster => monster.Id == BossUnitId).Coord;

        private static void RunFullTurn(CombatState state)
        {
            state.EndAction();
            state.ResolveMonsterMovement();
            state.EndAction();
            state.ResolveMonsterAction();
        }

        private static void AdvanceToPhaseTwo(CombatState state)
        {
            for (var turn = 0; turn < 4 && state.BossPhases.Single().CurrentPhase < 2; turn++)
            {
                RunFullTurn(state);
            }

            Assert.That(state.BossPhases.Single().CurrentPhase, Is.EqualTo(2));
        }

        /// <summary>몬스터 없는(또는 하나만 있는) 열린 판 — 변위 규칙만 관찰한다.</summary>
        private static CombatState CreateOpenState(HexCoord? monsterAt = null)
        {
            var cells = HexArea.CellsWithin(new HexCoord(0, 0), 10)
                .Select(coord => new HexCellData(coord, "tile", "street", 1, true, false))
                .ToList();
            var monsters = new List<MonsterConfig>();
            if (monsterAt.HasValue)
            {
                monsters.Add(new MonsterConfig("m1", monsterAt.Value, 50, definitionId: "M001"));
            }

            return new CombatState(
                new HexMapData(cells),
                new HexCoord(4, 0),
                monsters,
                new CombatConfig(500, 50, 2, 1, 0, 8, 1, 1, 0, playerVisionRange: 12),
                drawOpeningHands: false);
        }

        private static CombatState CreateSealedArenaState()
        {
            var state = CreateArenaState();
            Assert.That(state.TryPlayerMove(new HexCoord(2, 0)), Is.True);
            Assert.That(state.IsBossArenaBarrierActive, Is.True);
            return state;
        }

        private static CombatState CreateArenaState()
        {
            var move = new CardDefinition(
                "arena-move", "Arena Move", CardCategory.Movement, CardEffectType.Move, 0, 2, 0, targeting: "walkable_in_range",
                status: CardCatalogStatus.Approved, instanceId: "arena-move-instance");
            var strike = new CardDefinition(
                "arena-strike", "Arena Strike", CardCategory.Action, CardEffectType.Attack, 1, 1, 0, targeting: "living_monster_in_range",
                status: CardCatalogStatus.Approved, instanceId: "arena-strike-instance");
            var catalog = new CardCatalogDefinition(
                "test.boss-overlap", "Boss overlap test catalog",
                new[]
                {
                    new CardCatalogEntry(move.Id, move.DisplayName, move.Category, move.EffectType, move.Cost, move.Range, move.Amount, move.Targeting, status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(strike.Id, strike.DisplayName, strike.Category, strike.EffectType, strike.Cost, strike.Range, strike.Amount, strike.Targeting, status: CardCatalogStatus.Approved, targetMode: CardTargetMode.Enemy)
                });

            var cells = HexArea.CellsWithin(new HexCoord(4, 0), 10)
                .Select(coord => new HexCellData(coord, "tile", "street", 1, true, false))
                .ToList();
            var map = new HexMapData(
                cells,
                monsterSpawnRefs: new[] { new HexMonsterSpawnRef(BossSpawnRefId, BossDefinitionId, ArenaCenter, MonsterSpawnRoles.Boss) },
                areas: new[]
                {
                    new HexMapAreaRef(ArenaId, HexArea.CellsWithin(ArenaCenter, ArenaRadius), HexMapAreaRef.BossArenaPurpose, BossSpawnRefId)
                });

            var hand = Enumerable.Range(0, 8).Select(_ => move).ToArray();
            return new CombatState(
                map,
                new HexCoord(1, 0),
                new[] { new MonsterConfig(BossUnitId, ArenaCenter, 500, definitionId: BossDefinitionId, spawnRefId: BossSpawnRefId, spawnRole: MonsterSpawnRoles.Boss) },
                new CombatConfig(5000, 500, 2, 1, 0, 8, 14, 1, 0, playerVisionRange: 14, enemyDisengageRange: 14),
                cardCatalog: catalog,
                monsterCatalog: CreateMonsterCatalog(),
                movementDeck: new CardDeckState(null, hand, null, null),
                actionDeck: new CardDeckState(null, Enumerable.Range(0, 8).Select(_ => strike).ToArray(), null, null),
                drawOpeningHands: false,
                bossCatalog: CreateBossCatalog());
        }

        /// <summary>피해 0 근접 패턴 하나 — 관찰 대상은 위치뿐이라 플레이어가 죽으면 안 된다.</summary>
        private static MonsterCatalogDefinition CreateMonsterCatalog()
        {
            return new MonsterCatalogDefinition(
                "boss-overlap-test-catalog",
                "Boss Overlap Test Catalog",
                new[]
                {
                    new MonsterCatalogEntry(
                        BossDefinitionId, "테스트 보스", "test-melee", "B001",
                        detectionRange: 14, movePerTurn: 2, hp: 500, attackSpeed: 1,
                        attackPatterns: new[]
                        {
                            new MonsterAttackPattern("A100", "근접", 1, 0, 0, cooldownTurns: 0, phaseMin: 0)
                        })
                });
        }

        /// <summary>1페이즈 반경 0 → 2페이즈 반경 1. 출하 저작과 같은 모양이다.</summary>
        private static BossCatalogDefinition CreateBossCatalog()
        {
            const string profiles =
                BossCsv.ProfilesHeader + "\n" +
                BossDefinitionId + ",테스트 보스,TurnCount,,,,,music.boss.test,\n";
            const string phases =
                BossCsv.PhasesHeader + "\n" +
                BossDefinitionId + ",1,0,0,0,0,0.6,0,,\n" +
                BossDefinitionId + ",2,1,0,0,0,1.1,1,,\n";

            return BossCatalogCsvConverter.Convert(
                new BossCatalogCsvSource(profiles, phases, "boss-overlap-test", "Boss Overlap Test"));
        }
    }
}

using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// P6 물리 footprint(계획 §13.4)의 규칙 계약: 점유 반경은 보스 페이즈에서 유도되고(C-1),
    /// 히트테스트는 점유 칸 멤버십이며(C-2), 멀티셀 보스는 넉백 면역이고(C-5), 공격 shape는
    /// 몸통 가장자리에 앵커된다(C-6). 반경 0 몬스터(기존 전부)는 어느 계약도 달라지지 않는다.
    /// </summary>
    public sealed class BossFootprintTests
    {
        private const string BossDefinitionId = "M002";
        private const string BossUnitId = "boss-01";
        private const int BossBaseHp = 40;

        [Test]
        public void FootprintCoordsFollowThePhaseAuthoredRadius()
        {
            // 페이즈 1은 반경 0(중심 한 칸), 페이즈 2 진입과 함께 반경 1(7칸)로 커진다 — 전부 유도값이라
            // 서스펜드 스키마가 늘지 않는 설계의 관찰 지점이다.
            //
            // §18에서 개정: 예전에는 반경 0이면 <b>빈 목록</b>이었다(멀티셀만 그렸다). 그러면 하필 보스가
            // 움직이는 1페이즈에만 피격 범위 표시가 사라져 "어디까지가 몸인지" 읽을 수 없다.
            // 이제 조건은 "보스인가"이지 "몸이 여러 칸인가"가 아니다.
            var state = CreateBossState();
            var bossStart = state.Monsters.Single(m => m.Id == BossUnitId).Coord;
            Assert.That(
                state.BossFootprintCoords, Is.EqualTo(new[] { bossStart }),
                "1페이즈(반경 0)에도 중심 한 칸은 그려져야 한다.");

            RunFullTurn(state); // 턴 1: progress 0 유지
            RunFullTurn(state); // 턴 2: 2페이즈 진입(footprintRadius 1)
            var footprint = state.BossFootprintCoords;
            Assert.That(footprint, Has.Count.EqualTo(7), "반경 1 = 중심 + 인접 6칸.");
            var bossCoord = state.Monsters.Single(m => m.Id == BossUnitId).Coord;
            Assert.That(footprint, Does.Contain(bossCoord));
            Assert.That(footprint.All(coord => bossCoord.DistanceTo(coord) <= 1), Is.True);
        }

        [Test]
        public void FootprintCoordsCanBeFilteredPerBossUnit()
        {
            // #22: 프레젠테이션이 "이 보스가 지금 보이는가"를 술어로 넘기면 안 보이는 보스의 점유
            // 좌표가 통째로 빠져야 한다(조우 전 위치 누설 방지). null 술어는 기존 전량과 같아야 한다.
            var state = CreateBossState();

            Assert.That(
                state.GetBossFootprintCoords(null),
                Is.EqualTo(state.BossFootprintCoords),
                "null 술어 = 게이트 없음(기존 표면과 동일).");
            Assert.That(
                state.GetBossFootprintCoords(bossUnitId => bossUnitId == BossUnitId),
                Is.EqualTo(state.BossFootprintCoords),
                "보이는 보스는 전량 유지.");
            Assert.That(
                state.GetBossFootprintCoords(_ => false),
                Is.Empty,
                "안 보이는 보스의 점유 좌표는 한 칸도 나가면 안 된다.");
        }

        [Test]
        public void AnyOccupiedCellCountsAsTheBossForHitTesting()
        {
            // TriggerMonsterAlert는 FindLivingMonsterAt을 그대로 쓰는 공개 표면이다 — 중심이 아닌
            // 가장자리 점유 칸을 겨냥해도 보스가 잡혀야 한다(C-2 멤버십 계약).
            var state = CreateBossState();
            RunFullTurn(state);
            RunFullTurn(state); // 2페이즈: 반경 1
            var bossCoord = state.Monsters.Single(m => m.Id == BossUnitId).Coord;
            var edge = bossCoord.NeighborsInDirectionOrder().First();

            Assert.That(state.TriggerMonsterAlert(edge, rangeBonus: 0, durationTurns: 1), Is.True,
                "가장자리 점유 칸을 겨냥해도 보스가 잡혀야 한다.");
        }

        [Test]
        public void FieldObjectHitsTheBossWhenItOverlapsTheBodyButNotTheCentre()
        {
            // §18: 장판·폭탄류는 <b>중심 칸</b>만 보고 있어서, 7칸짜리 보스가 폭발 반경에 몸을 절반 담그고
            // 서 있어도 중심이 밖이면 아무 일도 일어나지 않았다 — "가운데를 정확히 맞춰야만 통한다"로
            // 체감된다. 히트테스트·사거리는 이미 원판 기준이었고 장판만 남아 있었다.
            var state = CreateBossState();
            RunFullTurn(state);
            RunFullTurn(state); // 2페이즈: 반경 1
            var boss = state.Monsters.Single(m => m.Id == BossUnitId);
            Assert.That(state.BossFootprintCoords, Has.Count.EqualTo(7), "전제: 몸이 7칸이어야 한다.");

            // 몸통 가장자리 칸 하나만 덮는 반경 0 장판 — 중심에서는 한 칸 벗어나 있다.
            var edge = boss.Coord.NeighborsInDirectionOrder().First();
            Assert.That(edge, Is.Not.EqualTo(boss.Coord));
            var hpBefore = state.Monsters.Single(m => m.Id == BossUnitId).Hp;

            state.FieldObjects.Add(new FieldObject(edge, 0, 2, FieldObjectKind.FieldDamage, value: 5, sourceUnitId: "player"));
            RunFullTurn(state);

            Assert.That(
                state.Monsters.Single(m => m.Id == BossUnitId).Hp, Is.LessThan(hpBefore),
                "몸통에 걸친 장판은 중심이 밖이어도 보스를 때려야 한다.");
        }

        [Test]
        public void FieldObjectStillMissesWhenItTouchesNoOccupiedCell()
        {
            // 양성 대조: 위 시험이 "장판이면 무조건 맞는다"를 보고 통과하는 게 아님을 고정한다.
            var state = CreateBossState();
            RunFullTurn(state);
            RunFullTurn(state);
            var boss = state.Monsters.Single(m => m.Id == BossUnitId);
            var hpBefore = boss.Hp;

            // 몸통(반경 1) 바깥 링보다 한 칸 더 먼 칸 — 어느 점유 칸도 건드리지 않는다.
            var away = boss.Coord;
            var direction = boss.Coord.ApproximateDirection(state.PlayerCoord);
            for (var i = 0; i < 3; i++)
            {
                away = away.Neighbor(direction);
            }

            Assert.That(state.BossFootprintCoords, Has.No.Member(away), "전제: 이 칸은 몸이 아니어야 한다.");
            state.FieldObjects.Add(new FieldObject(away, 0, 2, FieldObjectKind.FieldDamage, value: 5, sourceUnitId: "player"));
            RunFullTurn(state);

            Assert.That(state.Monsters.Single(m => m.Id == BossUnitId).Hp, Is.EqualTo(hpBefore));
        }

        [Test]
        public void MultiCellBossIsImmuneToKnockback()
        {
            var state = CreateBossState();
            RunFullTurn(state);
            RunFullTurn(state); // 2페이즈: 반경 1 → 면역
            var before = state.Monsters.Single(m => m.Id == BossUnitId).Coord;

            state.ApplyDirectionalKnockback(state.PlayerCoord, BossUnitId, distance: 2, impactDamage: 0);

            var after = state.Monsters.Single(m => m.Id == BossUnitId).Coord;
            Assert.That(after, Is.EqualTo(before), "멀티셀 보스는 넉백으로 밀리지 않는다(§13.4 C-5).");
        }

        [Test]
        public void IntentPreviewAnchorsTheShapeAtTheBodyEdge()
        {
            // line-2의 캐논 오프셋 최전방은 중심 기준 2칸이다. 반경 1 보스면 원점이 몸통 가장자리로
            // 한 칸 전진해 최전방이 중심 기준 3칸이 되어야 한다(C-6). 예고 오버레이가 그 계산의 공개 표면이다.
            var state = CreateBossState(patterns: new[]
            {
                new MonsterAttackPattern("A100", "직선", 3, 0, 0, shapeId: AttackShapeLibrary.Line2)
            });
            RunFullTurn(state);
            RunFullTurn(state); // 2페이즈: 반경 1
            var boss = state.Monsters.Single(m => m.Id == BossUnitId);
            var toPlayer = boss.Coord.ApproximateDirection(state.PlayerCoord);
            var expectedTip = boss.Coord;
            for (var i = 0; i < 3; i++)
            {
                expectedTip = expectedTip.Neighbor(toPlayer);
            }

            var preview = state.GetMonsterIntentPreviews(includeUnrevealed: true)
                .Single(candidate => candidate.MonsterId == BossUnitId);
            Assert.That(preview.AttackRangeCoords, Does.Contain(expectedTip),
                "shape 원점이 몸통 가장자리로 보정되어 최전방이 중심+3이어야 한다.");
        }

        [Test]
        public void TriangleFootprintPhaseGrowsTheBodyAndPushesTheSwallowedPlayerOut()
        {
            // 2026-09-04 §12: 불가살 P2는 반경 축이 아니라 <b>형상 축(tri)</b>으로 자란다. 점유 3칸과
            // 「삼킨 플레이어 밀어내기」(§16.2)가 원판 성장과 같은 술어(점유 멤버십)로 돌아야 한다 —
            // 반경 비교로 남아 있으면 tri 성장(반경 0 그대로)이 아무도 밀어내지 못한다.
            var playerCoord = new HexCoord(4, 0); // tri가 자라며 점유할 앵커+1:0 자리
            var state = CreateBossState(playerCoord: playerCoord, bossCatalog: CreateTriangleBossCatalog());

            RunFullTurn(state);
            RunFullTurn(state); // 2페이즈 진입(footprintShape=tri)

            var boss = state.Monsters.Single(m => m.Id == BossUnitId);
            var footprint = state.BossFootprintCoords;
            Assert.That(footprint, Is.EquivalentTo(new[]
            {
                boss.Coord,
                boss.Coord + new HexCoord(1, 0),
                boss.Coord + new HexCoord(0, 1),
            }), "tri = 앵커+동+남동 3칸 — 방향 고정(회전 없음 · MonsterFootprints 규약).");
            Assert.That(footprint, Has.No.Member(state.PlayerCoord),
                "커진 몸이 삼킨 플레이어는 몸 밖으로 밀려난다(§16.2 — 원판 성장과 같은 규약).");
            Assert.That(state.PlayerCoord, Is.Not.EqualTo(playerCoord), "실제로 자리가 옮겨졌다.");
        }

        private static void RunFullTurn(CombatState state)
        {
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();
        }

        /// <summary>
        /// TurnCount 지표의 인접 보스. 페이즈 1/2 footprintRadius = 0/1 — 반경 유도를 관찰한다.
        /// 보스 공격은 피해 0이라 장기 실행에도 플레이어가 죽지 않는다.
        /// </summary>
        private static CombatState CreateBossState(
            MonsterAttackPattern[] patterns = null,
            HexCoord? playerCoord = null,
            BossCatalogDefinition bossCatalog = null)
        {
            var resolvedPlayerCoord = playerCoord ?? new HexCoord(0, 0);
            var bossCoord = new HexCoord(3, 0);
            return new CombatState(
                CombatState.CreateDemoMap(6),
                resolvedPlayerCoord,
                new[] { new MonsterConfig(BossUnitId, bossCoord, BossBaseHp, definitionId: BossDefinitionId, spawnRole: "boss") },
                new CombatConfig(200, BossBaseHp, 2, 1, 4, 4, 5, 1, 3, playerVisionRange: 6),
                monsterCatalog: new MonsterCatalogDefinition(
                    "boss-footprint-test-catalog",
                    "Boss Footprint Test Catalog",
                    new[]
                    {
                        new MonsterCatalogEntry(
                            BossDefinitionId,
                            "테스트 보스",
                            "test-melee",
                            "B001",
                            detectionRange: 8,
                            movePerTurn: 0,
                            hp: BossBaseHp,
                            attackSpeed: 1,
                            attackPatterns: patterns ?? new[]
                            {
                                new MonsterAttackPattern("A100", "기본", 1, 0, 0, cooldownTurns: 0, phaseMin: 0)
                            })
                    }),
                bossCatalog: bossCatalog ?? CreateFootprintBossCatalog(),
                drawOpeningHands: false);
        }

        /// <summary>페이즈 2가 tri(2026-09-04 §12)인 변주 — footprintShape 컬럼이 있는 신 스키마 저작.</summary>
        private static BossCatalogDefinition CreateTriangleBossCatalog()
        {
            const string profiles =
                BossCsv.ProfilesHeader + "\n" +
                BossDefinitionId + ",테스트 보스,TurnCount,,,,,music.boss.test,\n";
            const string phases =
                "bossId,phaseIndex,threshold,strengthBonusPercent,maxHpBonus,patternPhaseMin,visualScale,footprintRadius,footprintShape,auraStatusKind,designerNote\n" +
                BossDefinitionId + ",1,0,0,0,0,1,0,,,\n" +
                BossDefinitionId + ",2,1,0,0,0,1.25,0,tri,,\n";

            return BossCatalogCsvConverter.Convert(new BossCatalogCsvSource(profiles, phases, "boss-tri-footprint-test", "Boss Tri Footprint Test"));
        }

        private static BossCatalogDefinition CreateFootprintBossCatalog()
        {
            const string profiles =
                BossCsv.ProfilesHeader + "\n" +
                BossDefinitionId + ",테스트 보스,TurnCount,,,,,music.boss.test,\n";
            const string phases =
                BossCsv.PhasesHeader + "\n" +
                BossDefinitionId + ",1,0,0,0,0,1,0,,\n" +
                BossDefinitionId + ",2,1,0,0,0,1.25,1,,\n";

            return BossCatalogCsvConverter.Convert(new BossCatalogCsvSource(profiles, phases, "boss-footprint-test", "Boss Footprint Test"));
        }
    }
}

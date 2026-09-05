using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 공격 패턴 <b>거리 창</b>(distMin/distMax)의 규칙 계약. phaseMin/phaseMax와 같은 모양의 바인딩 소유
    /// 값이며, 존재 이유는 밸런스가 아니라 구조다: 보스는 봉인 중 이동이 0이라 "플레이어를 실제로 덮는
    /// 패턴"을 우선하는 기존 규칙만으로는 플레이어가 멀 때 원거리기만 커버 판정을 통과하고 근접기는
    /// 영원히 뽑히지 않는다. 거리 창은 "플레이어 위치가 곧 무엇과 싸우는가"를 만드는 축이다.
    ///
    /// 관측 대상은 오직 <c>SelectedAttackPatternId</c>다 — 모든 픽스처 패턴의 피해는 0이라 장기 실행에서도
    /// 플레이어가 죽지 않는다. 보스는 <c>movePerTurn: 0</c>으로 고정해(봉인 중 이동 0과 같은 조건) 거리가
    /// 턴마다 흔들리지 않게 한다.
    /// </summary>
    public sealed class MonsterPatternDistanceBandTests
    {
        private const string BossDefinitionId = "M002";
        private const string BossUnitId = "boss-01";
        private const int BossBaseHp = 20;

        // -----------------------------------------------------------------------------------------
        // 거리 창 필터
        // -----------------------------------------------------------------------------------------

        [Test]
        public void MeleeBandPatternIsNeverSelectedWhileThePlayerStandsBeyondIt()
        {
            // A200 = 거리 0~1 근접기, A201 = 거리 무제한 기본기. 플레이어가 거리 3에 서 있으면
            // 가중치가 압도적이어도 A200은 단 한 번도 뽑혀서는 안 된다.
            var state = CreateBandState(bossDistance: 3);

            var selected = RunTurnsCollectingSelections(state, turns: 12);

            Assert.That(selected, Does.Not.Contain("A200"), "거리 창 밖 근접기는 후보에서 빠져야 한다.");
            Assert.That(selected, Does.Contain("A201"));
        }

        [Test]
        public void MeleeBandPatternBecomesSelectableOnceThePlayerIsInsideTheBand()
        {
            // 같은 저작, 거리만 1로 바꾸면 압도적 가중치의 A200이 사실상 항상 선택된다.
            var state = CreateBandState(bossDistance: 1);

            var selected = RunTurnsCollectingSelections(state, turns: 12);

            Assert.That(selected, Does.Contain("A200"), "거리 창 안이면 근접기가 후보에 들어와야 한다.");
        }

        [Test]
        public void RangedBandPatternIsOnlySelectedWhileThePlayerStandsInsideIt()
        {
            // A202 = 거리 3~4 원거리 징벌기(가중치 99). 거리 1에서는 절대, 거리 3에서는 사실상 항상.
            var near = CreateRangedBandState(bossDistance: 1);
            var far = CreateRangedBandState(bossDistance: 3);

            Assert.That(RunTurnsCollectingSelections(near, turns: 12), Does.Not.Contain("A202"));
            Assert.That(RunTurnsCollectingSelections(far, turns: 12), Does.Contain("A202"));
        }

        [TestCase(0, false)]
        [TestCase(1, true)]
        [TestCase(2, true)]
        [TestCase(3, true)]
        [TestCase(4, false)]
        public void DistanceWindowIsInclusiveOnBothEnds(int bossDistance, bool expectedInBand)
        {
            // 창은 [distMin, distMax] — 양 끝 포함이다. A203 = 1~3.
            var state = CreateMidBandState(bossDistance);

            var selected = RunTurnsCollectingSelections(state, turns: 12);

            Assert.That(
                selected.Contains("A203"),
                Is.EqualTo(expectedInBand),
                $"거리 {bossDistance}에서 [1,3] 창의 포함 판정이 어긋났다.");
        }

        [Test]
        public void DistanceIsMeasuredFromTheBossBodyEdgeNotItsCenter()
        {
            // footprint 반경 1인 보스가 중심 거리 4에 서면 몸통 가장자리 거리는 3이다.
            // A203(창 1~3)은 중심 기준이라면 빠지고, 가장자리 기준이라야 들어온다.
            var state = CreateMidBandState(bossDistance: 4, footprintRadius: 1);

            var selected = RunTurnsCollectingSelections(state, turns: 12);

            Assert.That(
                selected,
                Does.Contain("A203"),
                "거리는 몸통 가장자리(중심 거리 - footprint 반경) 기준이어야 한다.");
        }

        [Test]
        public void WhenNoPatternIsDistanceEligibleTheDistanceFilterAloneIsIgnored()
        {
            // 모든 패턴이 거리 창 밖인 저작 사고(파서 가드가 막지만 런타임 안전망은 별도로 성립해야 한다).
            // 거리 필터만 무시되고 몬스터는 계속 무언가를 뽑는다 — 정지 상태에 빠지지 않는다.
            var state = CreateAllBandedState(bossDistance: 4);

            var selected = RunTurnsCollectingSelections(state, turns: 8);

            Assert.That(selected, Is.Not.Empty);
            Assert.That(selected.All(id => id == "A200" || id == "A202"), Is.True);
        }

        [Category("ShippingData")]
        [Test]
        public void PhaseGateStillRetiresAPatternEvenWhileItSitsInsideItsDistanceBand()
        {
            // 거리 축을 얹어도 페이즈 축(은퇴)이 무력화되면 안 된다. A204는 거리 창 안이지만 phaseMax=0이라
            // 2페이즈(게이트 1) 진입 후에는 가중치 99여도 다시 뽑히지 않아야 한다.
            var state = CreatePhaseAndBandState(bossDistance: 1);

            RunFullTurn(state); // 턴 1: 게이트 0 — A204가 사실상 항상 선택된다
            RunFullTurn(state); // 턴 2: 2페이즈 진입(게이트 1)
            Assert.That(state.BossPhases.Single().CurrentPhase, Is.EqualTo(2));

            for (var turn = 0; turn < 8; turn++)
            {
                RunFullTurn(state);
                Assert.That(
                    SelectedPatternId(state),
                    Is.EqualTo("A201"),
                    "거리 창 안이어도 은퇴한 패턴은 다시 선택되지 않는다.");
            }
        }

        // -----------------------------------------------------------------------------------------
        // 파서 (저작 표면)
        // -----------------------------------------------------------------------------------------

        [Test]
        public void BindingCsvCarriesTheAuthoredDistanceWindowOntoThePattern()
        {
            var catalog = ConvertBindings(
                "M001,A001,1,true,,0,,1,3,중거리 창\n" +
                "M001,A002,2,true,,0,,,,거리 무제한 기본기\n");

            var patterns = catalog.Entries.Single().AttackPatterns;
            var banded = patterns.Single(pattern => pattern.Id == "A001");
            var unbanded = patterns.Single(pattern => pattern.Id == "A002");

            Assert.That(banded.DistMin, Is.EqualTo(1));
            Assert.That(banded.DistMax, Is.EqualTo(3));
            Assert.That(unbanded.DistMin, Is.EqualTo(0), "공란 distMin은 0(제한 없음)이다.");
            Assert.That(unbanded.DistMax, Is.EqualTo(int.MaxValue), "공란 distMax는 무한(제한 없음)이다.");
        }

        [Test]
        public void BindingCsvWithoutDistanceColumnsKeepsEveryPatternUnbanded()
        {
            // 거리 컬럼이 아예 없는 옛 저작도 그대로 읽혀야 한다(컬럼은 선택적).
            var catalog = ConvertBindings(
                "M001,A001,1,true,,0,,기본기\n",
                header: "monsterId,patternId,order,enabled,overrideWeight,phaseMin,phaseMax,designerNote\n");

            var pattern = catalog.Entries.Single().AttackPatterns.Single();
            Assert.That(pattern.DistMin, Is.EqualTo(0));
            Assert.That(pattern.DistMax, Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void NegativeDistMinIsRejected()
        {
            var error = Assert.Throws<ArgumentException>(() => ConvertBindings(
                "M001,A001,1,true,,0,,-1,3,음수 distMin\n"));

            Assert.That(error.Message, Does.Contain("distMin cannot be negative"));
        }

        [Test]
        public void DistMaxBelowDistMinIsRejected()
        {
            var error = Assert.Throws<ArgumentException>(() => ConvertBindings(
                "M001,A001,1,true,,0,,3,1,역전된 창\n"));

            Assert.That(error.Message, Does.Contain("distMax (1) cannot be lower than distMin (3)"));
        }

        [Test]
        public void MonsterWhoseOnlyCooldownZeroPatternIsDistanceBandedIsRejected()
        {
            // 확장된 가드: 기본 패턴은 페이즈 축과 거리 축 <b>양쪽 모두</b>에서 무제한이어야 한다.
            // 그렇지 않으면 어떤 거리 구간에서 후보가 0이 될 수 있다.
            var error = Assert.Throws<ArgumentException>(() => ConvertBindings(
                "M001,A001,1,true,,0,,0,1,쿨다운 0이지만 거리 창이 걸린 유일한 기본기\n"));

            Assert.That(error.Message, Does.Contain("no never-retiring cooldown-0 attack pattern"));
            Assert.That(error.Message, Does.Contain("distMin/distMax"));
        }

        [Test]
        public void MonsterKeepingOneFullyUnrestrictedCooldownZeroPatternPassesTheGuard()
        {
            // 양성 대조: 거리 창이 걸린 패턴이 있어도 무제한 기본기가 하나 남아 있으면 통과한다.
            Assert.DoesNotThrow(() => ConvertBindings(
                "M001,A001,1,true,,0,,,,무제한 기본기\n" +
                "M001,A002,2,true,,0,,2,4,원거리 전용\n"));
        }

        // -----------------------------------------------------------------------------------------
        // 픽스처
        // -----------------------------------------------------------------------------------------

        /// <summary>A200 = 근접 창 0~1(가중치 99) · A201 = 거리 무제한 기본기.</summary>
        private static CombatState CreateBandState(int bossDistance) =>
            CreateState(bossDistance, new[]
            {
                new MonsterAttackPattern("A200", "근접 전용", 4, 0, 0, weight: 99, cooldownTurns: 0, phaseMin: 0, distMin: 0, distMax: 1),
                new MonsterAttackPattern("A201", "기본", 4, 0, 0, cooldownTurns: 0, phaseMin: 0)
            });

        /// <summary>A202 = 원거리 창 3~4(가중치 99) · A201 = 거리 무제한 기본기.</summary>
        private static CombatState CreateRangedBandState(int bossDistance) =>
            CreateState(bossDistance, new[]
            {
                new MonsterAttackPattern("A202", "원거리 전용", 4, 0, 0, weight: 99, cooldownTurns: 0, phaseMin: 0, distMin: 3, distMax: 4),
                new MonsterAttackPattern("A201", "기본", 4, 0, 0, cooldownTurns: 0, phaseMin: 0)
            });

        /// <summary>A203 = 중거리 창 1~3(가중치 99) · A201 = 거리 무제한 기본기.</summary>
        private static CombatState CreateMidBandState(int bossDistance, int footprintRadius = 0) =>
            CreateState(bossDistance, new[]
            {
                new MonsterAttackPattern("A203", "중거리 전용", 4, 0, 0, weight: 99, cooldownTurns: 0, phaseMin: 0, distMin: 1, distMax: 3),
                new MonsterAttackPattern("A201", "기본", 4, 0, 0, cooldownTurns: 0, phaseMin: 0)
            }, footprintRadius);

        /// <summary>전 패턴이 거리 창에 갇힌 저작 사고 — 거리 4에서는 어느 것도 적격이 아니다.</summary>
        private static CombatState CreateAllBandedState(int bossDistance) =>
            CreateState(bossDistance, new[]
            {
                new MonsterAttackPattern("A200", "근접 전용", 4, 0, 0, cooldownTurns: 0, phaseMin: 0, distMin: 0, distMax: 1),
                new MonsterAttackPattern("A202", "중거리 전용", 4, 0, 0, cooldownTurns: 0, phaseMin: 0, distMin: 2, distMax: 3)
            });

        /// <summary>A204 = 거리 창 0~1 안에 있으면서 phaseMax=0으로 은퇴하는 패턴(가중치 99).</summary>
        private static CombatState CreatePhaseAndBandState(int bossDistance) =>
            CreateState(bossDistance, new[]
            {
                new MonsterAttackPattern("A204", "은퇴 예정", 4, 0, 0, weight: 99, cooldownTurns: 0, phaseMin: 0, phaseMax: 0, distMin: 0, distMax: 1),
                new MonsterAttackPattern("A201", "기본", 4, 0, 0, cooldownTurns: 0, phaseMin: 0)
            }, retireAtPhaseTwo: true);

        private static CombatState CreateState(
            int bossDistance,
            MonsterAttackPattern[] patterns,
            int footprintRadius = 0,
            bool retireAtPhaseTwo = false)
        {
            var playerCoord = new HexCoord(0, 0);
            var bossCoord = new HexCoord(bossDistance, 0);
            // 보스가 서 있을 자리와 그 footprint까지 모두 걸을 수 있어야 한다 — 여유 있게 한 줄을 깐다.
            var corridor = Enumerable.Range(-2, 12).SelectMany(q => Enumerable.Range(-2, 5).Select(r => new HexCoord(q, r))).ToArray();

            return new CombatState(
                CreateMap(corridor),
                playerCoord,
                new[] { new MonsterConfig(BossUnitId, bossCoord, BossBaseHp, definitionId: BossDefinitionId, spawnRole: "boss") },
                CreateConfig(),
                monsterCatalog: CreateMonsterCatalog(patterns),
                bossCatalog: CreateBossCatalog(footprintRadius, retireAtPhaseTwo),
                drawOpeningHands: false);
        }

        /// <summary>
        /// TurnCount 지표 2페이즈 프로필. <paramref name="retireAtPhaseTwo"/>가 참이면 임계 1(=턴 2)에
        /// 게이트가 1로 올라 phaseMax=0 패턴이 은퇴하고, 거짓이면 임계를 도달 불가로 두어 게이트 0에 머문다.
        /// </summary>
        private static BossCatalogDefinition CreateBossCatalog(int footprintRadius, bool retireAtPhaseTwo)
        {
            var phaseTwoThreshold = retireAtPhaseTwo ? 1 : 99;
            var profiles =
                "bossId,displayName,phaseMetric,mechanicId,mechanicParam1,mechanicParam2,mechanicParam3,introCinematic,deathCinematic,bgmCueBase,designerNote\n" +
                BossDefinitionId + ",테스트 보스,TurnCount,,,,,,,music.boss.test,\n";
            var phases =
                BossCsv.PhasesHeader + "\n" +
                BossDefinitionId + $",1,0,0,0,0,1,{footprintRadius},,\n" +
                BossDefinitionId + $",2,{phaseTwoThreshold},0,0,1,1,{footprintRadius},,\n";

            return BossCatalogCsvConverter.Convert(
                new BossCatalogCsvSource(profiles, phases, "boss-band-test", "Boss Band Test"));
        }

        private static MonsterCatalogDefinition CreateMonsterCatalog(MonsterAttackPattern[] patterns)
        {
            return new MonsterCatalogDefinition(
                "boss-band-test-catalog",
                "Boss Band Test Catalog",
                new[]
                {
                    new MonsterCatalogEntry(
                        BossDefinitionId,
                        "테스트 보스",
                        "test-melee",
                        "B001",
                        detectionRange: 12,
                        // 0 = 봉인된 보스와 같은 조건. 거리가 턴마다 흔들리지 않아야 관측이 성립한다.
                        movePerTurn: 0,
                        hp: BossBaseHp,
                        attackSpeed: 1,
                        attackPatterns: patterns)
                });
        }

        private static CombatConfig CreateConfig() =>
            new CombatConfig(200, BossBaseHp, 2, 1, 4, 4, 12, 4, 0, playerVisionRange: 12, enemyDisengageRange: 12);

        private static HexMapData CreateMap(params HexCoord[] coords) =>
            new HexMapData(coords.Select(coord => new HexCellData(coord, "tile", "street", 1, true, false)).ToList());

        // -----------------------------------------------------------------------------------------
        // 헬퍼
        // -----------------------------------------------------------------------------------------

        private static HashSet<string> RunTurnsCollectingSelections(CombatState state, int turns)
        {
            var selected = new HashSet<string>();
            for (var turn = 0; turn < turns; turn++)
            {
                RunFullTurn(state);
                var id = SelectedPatternId(state);
                if (!string.IsNullOrEmpty(id))
                {
                    selected.Add(id);
                }
            }

            return selected;
        }

        private static void RunFullTurn(CombatState state)
        {
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();
        }

        private static string SelectedPatternId(CombatState state) =>
            state.Monsters.Single(monster => monster.Id == BossUnitId).SelectedAttackPatternId;

        private static MonsterCatalogDefinition ConvertBindings(
            string bindingRows,
            string header = "monsterId,patternId,order,enabled,overrideWeight,phaseMin,phaseMax,distMin,distMax,designerNote\n")
        {
            const string monsters =
                "monsterId,displayName,archetype,behaviorProfileRef,hp,detectionRange,movePerTurn,attackSpeed,status,visualPrefabPath\n" +
                "M001,Tester,test-melee,B001,30,6,2,1,prototype,\n";
            const string patterns =
                "patternId,displayName,range,damage,effectRef,targeting,vfxCueId,shapeId,statusEffects,statusEffectDurationTurns,cooldownTurns\n" +
                "A001,Basic,1,3,attack.damage,player_in_range,V001,single,,2,0\n" +
                "A002,Ranged,2,3,attack.damage,player_in_range,V001,single,,2,0\n";

            var source = new MonsterCatalogCsvSource(
                monsters,
                patterns,
                header + bindingRows,
                ReadCsv(CombatCsvPaths.PresentationDirectory, "combat_vfx_cues.csv"),
                ReadCsv(CombatCsvPaths.PresentationDirectory, "combat_sound_cues.csv"));

            return MonsterCatalogCsvConverter.Convert(source).MonsterCatalog;
        }

        private static string ReadCsv(string directory, string fileName) =>
            System.IO.File.ReadAllText(System.IO.Path.Combine(directory, fileName));
    }
}

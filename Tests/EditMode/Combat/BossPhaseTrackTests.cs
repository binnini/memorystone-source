using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 보스 페이즈 트랙의 규칙 계약: 지표 평가·단조 전환·페이즈 진입 효과(누적 목표치)·패턴 게이트·
    /// 서스펜드 왕복. 지표는 전투 안에서 굴리기 쉬운 <see cref="BossPhaseMetricKind.TurnCount"/>를 쓰고,
    /// 지표별 환산 자체는 <c>BossCatalogCsvConverterTests</c>가 순수 함수로 전수 검증한다.
    /// </summary>
    public sealed class BossPhaseTrackTests
    {
        private const string BossDefinitionId = "M002";
        private const string BossUnitId = "boss-01";
        private const int BossBaseHp = 20;
        private const int BossAttackDamage = 4;

        [Test]
        public void BossWithoutProfileGetsNoPhaseTrack()
        {
            var state = CreateAdjacentBossState(bossCatalog: BossCatalogDefinition.Empty);

            Assert.That(state.HasBossPhaseTrack, Is.False);
            Assert.That(state.BossPhases, Is.Empty);
            Assert.That(state.TryGetBossPhaseState(BossUnitId, out _), Is.False);
        }

        [Test]
        public void NonBossMonsterGetsNoPhaseTrackEvenWhenItsDefinitionHasAProfile()
        {
            // 같은 정의(M002)를 role 없이 배치하면 보스가 아니다 — 페이즈는 SpawnRole로만 붙는다.
            var state = CreateAdjacentBossState(spawnRole: string.Empty);

            Assert.That(state.HasBossPhaseTrack, Is.False);
        }

        [Test]
        public void BossStartsAtPhaseOneWithTheAuthoredProfile()
        {
            var state = CreateAdjacentBossState();

            Assert.That(state.HasBossPhaseTrack, Is.True);
            Assert.That(state.TryGetBossPhaseState(BossUnitId, out var phase), Is.True);
            Assert.That(phase.BossDefinitionId, Is.EqualTo(BossDefinitionId));
            Assert.That(phase.CurrentPhase, Is.EqualTo(1));
            Assert.That(phase.PhaseCount, Is.EqualTo(3));
            Assert.That(phase.MetricProgress, Is.EqualTo(0));
            Assert.That(phase.NextPhaseThreshold, Is.EqualTo(1));
            Assert.That(phase.VisualScale, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(phase.BgmCueId, Is.EqualTo("music.boss.test.p1"));
            Assert.That(phase.IsFinalPhase, Is.False);
        }

        [Test]
        public void PhaseAuraStatusKindSurfacesInTheProjectionOnlyWhereAuthored()
        {
            // 아우라는 페이즈별 저작이라 저작된 페이즈에서만 투영에 실린다: 표현 계층(status.loop 리컨실러)이
            // 이 값을 읽어 아우라 루프를 붙인다. 1페이즈는 공란 → null, 2페이즈는 BossAura 저작 → 값이 실린다.
            // 아우라는 ActiveEffect가 아니므로 여기에 실린다고 상태이상이 부여되는 것은 아니다(§8-2).
            var state = CreateAdjacentBossState(bossCatalog: CreateAuraBossCatalog());

            Assert.That(state.TryGetBossPhaseState(BossUnitId, out var phaseOne), Is.True);
            Assert.That(phaseOne.CurrentPhase, Is.EqualTo(1));
            Assert.That(phaseOne.AuraStatusKind, Is.Null, "1페이즈는 아우라 미저작 → null.");

            RunFullTurn(state); // 턴 1: progress 0 유지
            RunFullTurn(state); // 턴 2: progress 1 → 2페이즈 진입
            var phaseTwo = state.BossPhases.Single();
            Assert.That(phaseTwo.CurrentPhase, Is.EqualTo(2));
            Assert.That(phaseTwo.AuraStatusKind, Is.EqualTo(StatusEffectKind.BossAura));
        }

        [Test]
        public void MetricReachingTheThresholdRaisesThePhaseOnceWithFromAndTo()
        {
            var state = CreateAdjacentBossState();
            var transitions = new List<(string BossUnitId, int From, int To)>();
            state.BossPhaseChanged += (bossUnitId, from, to) => transitions.Add((bossUnitId, from, to));

            RunFullTurn(state); // 턴 1 결의 시점 progress 0 → 1페이즈 유지
            Assert.That(transitions, Is.Empty);

            RunFullTurn(state); // 턴 2 결의 시점 progress 1 → 2페이즈
            Assert.That(transitions, Has.Count.EqualTo(1));
            Assert.That(transitions[0], Is.EqualTo((BossUnitId, 1, 2)));
            Assert.That(state.BossPhases.Single().CurrentPhase, Is.EqualTo(2));
        }

        [Test]
        public void LastBossPhaseTransitionsHoldsOnlyTheRaisesFromTheMostRecentResolution()
        {
            // 표현 계층(타임라인 조립기)이 이 목록을 읽어 BossPhaseTransition 비트를 만든다. 결의 진입 시마다
            // 비워지므로 항상 "직전 결의에서 올라간 전환"만 담는다.
            var state = CreateAdjacentBossState();

            RunFullTurn(state); // 턴 1: 진행값 0 → 전환 없음
            Assert.That(state.LastBossPhaseTransitions, Is.Empty, "전환이 없는 결의는 비어 있다.");

            RunFullTurn(state); // 턴 2: 진행값 1 → 2페이즈 전환
            Assert.That(state.LastBossPhaseTransitions.Count, Is.EqualTo(1));
            var t = state.LastBossPhaseTransitions[0];
            Assert.That(t.BossUnitId, Is.EqualTo(BossUnitId));
            Assert.That(t.FromPhase, Is.EqualTo(1));
            Assert.That(t.ToPhase, Is.EqualTo(2));

            RunFullTurn(state); // 턴 3: 3페이즈 전환 — 이전 결의의 전환은 비워지고 이번 것만 남는다
            Assert.That(state.LastBossPhaseTransitions.Count, Is.EqualTo(1));
            Assert.That(state.LastBossPhaseTransitions[0].ToPhase, Is.EqualTo(3));
        }

        [Test]
        public void DebugAdvanceBossPhaseWalksEveryPhaseWithTheRealContractAndRefusesAtTheFinalOne()
        {
            // 랩 원클릭 버튼의 규칙 뒷면. 실제 전환의 단일 변이점(SetBossPhase)을 그대로 타므로
            // 이벤트·직전 전환 기록·단조 계약이 지표 기반 전환과 동일해야 한다.
            var state = CreateAdjacentBossState();
            var transitions = new List<(string BossUnitId, int From, int To)>();
            state.BossPhaseChanged += (bossUnitId, from, to) => transitions.Add((bossUnitId, from, to));

            // 빈 id는 첫 트랙으로 폴백한다(랩은 트랙이 하나뿐).
            Assert.That(state.DebugAdvanceBossPhase(null, out var from1, out var to1), Is.True);
            Assert.That((from1, to1), Is.EqualTo((1, 2)));
            Assert.That(state.BossPhases.Single().CurrentPhase, Is.EqualTo(2));
            Assert.That(transitions, Is.EqualTo(new[] { (BossUnitId, 1, 2) }));
            Assert.That(state.LastBossPhaseTransitions.Count, Is.EqualTo(1), "실제 결의와 같은 '직전 전환만' 기록 규약.");
            Assert.That(state.LastBossPhaseTransitions[0].ToPhase, Is.EqualTo(2));

            Assert.That(state.DebugAdvanceBossPhase(BossUnitId, out var from2, out var to2), Is.True);
            Assert.That((from2, to2), Is.EqualTo((2, 3)));
            Assert.That(state.LastBossPhaseTransitions.Count, Is.EqualTo(1), "두 번째 전환이 첫 기록을 밀어낸다.");
            Assert.That(state.LastBossPhaseTransitions[0].ToPhase, Is.EqualTo(3));

            Assert.That(state.DebugAdvanceBossPhase(BossUnitId, out _, out _), Is.False, "최종 페이즈에서는 거부(단조 계약).");
            Assert.That(state.BossPhases.Single().CurrentPhase, Is.EqualTo(3));
            Assert.That(transitions.Count, Is.EqualTo(2));
        }

        [Test]
        public void PhaseNeverRepeatsOrGoesBackWhileTheMetricKeepsRising()
        {
            var state = CreateAdjacentBossState();
            var transitions = new List<(int From, int To)>();
            state.BossPhaseChanged += (_, from, to) => transitions.Add((from, to));

            for (var turn = 0; turn < 6 && !state.IsTerminal; turn++)
            {
                RunFullTurn(state);
            }

            // 임계 1/3 → 2페이즈, 3페이즈로 각각 한 번씩만 올라가고, 그 뒤로는 마지막 페이즈에 머문다.
            Assert.That(transitions, Is.EqualTo(new[] { (1, 2), (2, 3) }));
            Assert.That(state.BossPhases.Single().IsFinalPhase, Is.True);
        }

        [Test]
        public void PhaseEntryAppliesMaxHpBonusAsACumulativeTargetAndHealsToIt()
        {
            var state = CreateAdjacentBossState();
            Assert.That(BossMaxHp(state), Is.EqualTo(BossBaseHp));

            RunFullTurn(state);
            Assert.That(BossMaxHp(state), Is.EqualTo(BossBaseHp), "1페이즈는 보너스 0.");

            RunFullTurn(state); // → 2페이즈 (maxHpBonus 10)
            Assert.That(BossMaxHp(state), Is.EqualTo(BossBaseHp + 10));
            Assert.That(BossHp(state), Is.EqualTo(BossBaseHp + 10), "IncreaseMaxHp는 현재 체력도 같이 올린다.");

            RunFullTurn(state); // → 3페이즈 (maxHpBonus 25 = 누적 목표치)
            Assert.That(BossMaxHp(state), Is.EqualTo(BossBaseHp + 25), "누적 목표치이므로 10+25가 아니라 25다.");
        }

        [Test]
        public void PhaseStrengthScalesTheBossDamageOnTheVeryTurnItRises()
        {
            var state = CreateAdjacentBossState();
            var playerMaxHp = state.Player.MaxHp;

            RunFullTurn(state);
            var damageInPhaseOne = playerMaxHp - state.Player.Hp;
            Assert.That(damageInPhaseOne, Is.EqualTo(BossAttackDamage), "1페이즈 강화 0%.");

            var hpBeforePhaseTwoTurn = state.Player.Hp;
            RunFullTurn(state); // 기믹 스텝에서 2페이즈로 올라간 뒤 같은 턴의 공격이 해소된다
            Assert.That(state.BossPhases.Single().CurrentPhase, Is.EqualTo(2));
            Assert.That(
                hpBeforePhaseTwoTurn - state.Player.Hp,
                Is.EqualTo(BossAttackDamage * 2),
                "2페이즈 강화 +100%가 전환된 턴의 공격에 바로 반영되어야 한다.");
        }

        [Test]
        public void GatedPatternIsNeverSelectedWhileThePhaseGateIsBelowIt()
        {
            // 게이트가 열리지 않는 한 A101(phaseMin=2)은 단 한 번도 선택되어서는 안 된다.
            // 임계값을 도달 불가로 두어 전투 내내 게이트 0에 머물게 한다.
            var state = CreateGateBossState(phaseOnePatternGate: 0, phaseTwoThreshold: 99);

            for (var turn = 0; turn < 8; turn++)
            {
                RunFullTurn(state);
                Assert.That(state.BossPhases.Single().CurrentPhase, Is.EqualTo(1));
                Assert.That(SelectedPatternId(state), Is.EqualTo("A100"));
            }
        }

        [Test]
        public void GatedPatternBecomesSelectableOnceTheGateIsOpen()
        {
            // 같은 두 패턴, 1페이즈의 patternPhaseMin만 2로 저작하면 처음부터 게이트가 열린다.
            var state = CreateGateBossState(phaseOnePatternGate: 2, phaseTwoThreshold: 99);

            var selected = new HashSet<string>();
            for (var turn = 0; turn < 8; turn++)
            {
                RunFullTurn(state);
                selected.Add(SelectedPatternId(state));
            }

            Assert.That(selected, Does.Contain("A101"), "게이트가 열리면 A101이 후보에 들어와야 한다.");
        }

        [Test]
        public void RetiredPatternLeavesThePoolOnceTheGatePassesItsPhaseMax()
        {
            // A102(phaseMax=0)는 게이트 0에서만 후보다. 2페이즈(게이트 1) 진입 후에는 가중치가 압도적이어도
            // 다시는 선택되지 않아야 한다 — phaseMax 세대 교체(은퇴)의 규칙 계약. 게이트 반영은 phaseMin과
            // 같은 이유로 전환 다음 턴 계획부터다.
            var state = CreateRetireBossState();

            RunFullTurn(state); // 턴 1: 게이트 0 — A102(가중치 99)가 사실상 항상 선택된다
            RunFullTurn(state); // 턴 2: 2페이즈 진입(게이트 1)
            Assert.That(state.BossPhases.Single().CurrentPhase, Is.EqualTo(2));

            for (var turn = 0; turn < 8; turn++)
            {
                RunFullTurn(state);
                Assert.That(SelectedPatternId(state), Is.EqualTo("A100"), "은퇴한 패턴은 다시 선택되지 않는다.");
            }
        }

        [Test]
        public void SuspendRoundTripPreservesPhaseAndDoesNotReapplyTheMaxHpBonus()
        {
            var state = CreateAdjacentBossState();
            RunFullTurn(state);
            RunFullTurn(state); // → 2페이즈
            Assert.That(state.BossPhases.Single().CurrentPhase, Is.EqualTo(2));
            var maxHpBeforeSuspend = BossMaxHp(state);

            var snapshot = state.CreateSuspendSnapshot();
            Assert.That(snapshot.BossPhaseTracks, Has.Count.EqualTo(1));
            Assert.That(snapshot.BossPhaseTracks[0].CurrentPhase, Is.EqualTo(2));
            Assert.That(snapshot.BossPhaseTracks[0].AppliedMaxHpBonus, Is.EqualTo(10));

            var resumed = CreateAdjacentBossState();
            resumed.RestoreFromSuspend(snapshot);

            Assert.That(resumed.BossPhases.Single().CurrentPhase, Is.EqualTo(2));
            Assert.That(resumed.BossPhases.Single().MetricProgress, Is.EqualTo(1));
            Assert.That(BossMaxHp(resumed), Is.EqualTo(maxHpBeforeSuspend), "저장된 MaxHp에 보너스가 이미 포함되어 있다.");

            RunFullTurn(resumed); // → 3페이즈, 차분(25-10=15)만 얹혀야 한다
            Assert.That(resumed.BossPhases.Single().CurrentPhase, Is.EqualTo(3));
            Assert.That(BossMaxHp(resumed), Is.EqualTo(BossBaseHp + 25));
        }

        [Test]
        public void SuspendSnapshotWithoutBossTracksStillRebuildsThemFromTheBoard()
        {
            // 세이브가 만들어진 뒤 보스 프로필이 저작된 경우: 트랙이 없더라도 1페이즈로 보강된다.
            var state = CreateAdjacentBossState();
            var snapshot = state.CreateSuspendSnapshot();
            snapshot.BossPhaseTracks.Clear();

            var resumed = CreateAdjacentBossState();
            resumed.RestoreFromSuspend(snapshot);

            Assert.That(resumed.BossPhases.Single().CurrentPhase, Is.EqualTo(1));
        }

        // ------------------------------------------------------------------------------------------

        private static void RunFullTurn(CombatState state)
        {
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();
        }

        private static string SelectedPatternId(CombatState state) =>
            state.Monsters.Single(monster => monster.Id == BossUnitId).SelectedAttackPatternId;

        private static int BossMaxHp(CombatState state) =>
            state.Monsters.Single(monster => monster.Id == BossUnitId).MaxHp;

        private static int BossHp(CombatState state) =>
            state.Monsters.Single(monster => monster.Id == BossUnitId).Hp;

        private static CombatState CreateAdjacentBossState(
            BossCatalogDefinition bossCatalog = null,
            string spawnRole = "boss")
        {
            var playerCoord = new HexCoord(0, 0);
            var bossCoord = new HexCoord(1, 0);
            return new CombatState(
                CreateMap(playerCoord, bossCoord),
                playerCoord,
                new[] { new MonsterConfig(BossUnitId, bossCoord, BossBaseHp, definitionId: BossDefinitionId, spawnRole: spawnRole) },
                CreateConfig(),
                monsterCatalog: CreateMonsterCatalog(new[]
                {
                    new MonsterAttackPattern("A100", "근접", 1, 0, BossAttackDamage, cooldownTurns: 0, phaseMin: 0)
                }),
                bossCatalog: bossCatalog ?? CreateBossCatalog(),
                drawOpeningHands: false);
        }

        /// <summary>
        /// 게이트 0 패턴(A100)과 게이트 2 패턴(A101)을 가진 인접 보스. 두 패턴 모두 피해 0이라
        /// 장기 실행에서도 플레이어가 죽지 않고, 관찰 대상은 오직 "무엇이 선택되었는가"다.
        /// </summary>
        private static CombatState CreateGateBossState(int phaseOnePatternGate, int phaseTwoThreshold)
        {
            var playerCoord = new HexCoord(0, 0);
            var bossCoord = new HexCoord(1, 0);
            return new CombatState(
                CreateMap(playerCoord, bossCoord),
                playerCoord,
                new[] { new MonsterConfig(BossUnitId, bossCoord, BossBaseHp, definitionId: BossDefinitionId, spawnRole: "boss") },
                CreateConfig(),
                monsterCatalog: CreateMonsterCatalog(new[]
                {
                    new MonsterAttackPattern("A100", "기본", 1, 0, 0, cooldownTurns: 0, phaseMin: 0),
                    new MonsterAttackPattern("A101", "해금", 1, 0, 0, cooldownTurns: 0, phaseMin: 2)
                }),
                bossCatalog: CreateGateBossCatalog(phaseOnePatternGate, phaseTwoThreshold),
                drawOpeningHands: false);
        }

        /// <summary>
        /// 은퇴 검증용 인접 보스: A100(기본·은퇴 없음)과 A102(가중치 99·phaseMax 0). 2페이즈(임계 1 = 턴 2)에
        /// 게이트가 1로 올라 A102가 은퇴한다. 두 패턴 모두 피해 0 — 관찰 대상은 오직 선택이다.
        /// </summary>
        private static CombatState CreateRetireBossState()
        {
            var playerCoord = new HexCoord(0, 0);
            var bossCoord = new HexCoord(1, 0);
            return new CombatState(
                CreateMap(playerCoord, bossCoord),
                playerCoord,
                new[] { new MonsterConfig(BossUnitId, bossCoord, BossBaseHp, definitionId: BossDefinitionId, spawnRole: "boss") },
                CreateConfig(),
                monsterCatalog: CreateMonsterCatalog(new[]
                {
                    new MonsterAttackPattern("A100", "기본", 1, 0, 0, cooldownTurns: 0, phaseMin: 0),
                    new MonsterAttackPattern("A102", "은퇴 예정", 1, 0, 0, weight: 99, cooldownTurns: 0, phaseMin: 0, phaseMax: 0)
                }),
                bossCatalog: CreateGateBossCatalog(phaseOnePatternGate: 0, phaseTwoThreshold: 1),
                drawOpeningHands: false);
        }

        private static CombatConfig CreateConfig()
        {
            return new CombatConfig(200, BossBaseHp, 2, 1, 4, 4, 5, 1, 3, playerVisionRange: 4);
        }

        private static HexMapData CreateMap(params HexCoord[] coords)
        {
            return new HexMapData(coords.Select(coord => new HexCellData(coord, "tile", "street", 1, true, false)).ToList());
        }

        private static MonsterCatalogDefinition CreateMonsterCatalog(MonsterAttackPattern[] patterns)
        {
            return new MonsterCatalogDefinition(
                "boss-phase-test-catalog",
                "Boss Phase Test Catalog",
                new[]
                {
                    new MonsterCatalogEntry(
                        BossDefinitionId,
                        "테스트 보스",
                        "test-melee",
                        "B001",
                        detectionRange: 8,
                        movePerTurn: 1,
                        hp: BossBaseHp,
                        attackSpeed: 1,
                        attackPatterns: patterns)
                });
        }

        /// <summary>
        /// TurnCount 지표 3페이즈 프로필: 임계 0/1/2(= 턴 1/2/3에 각각 진입), 최대 체력 누적 목표
        /// 0/10/25, 강화 0/100/200%, 패턴 게이트 0/1/2.
        /// </summary>
        private static BossCatalogDefinition CreateBossCatalog()
        {
            const string profiles =
                "bossId,displayName,phaseMetric,mechanicId,mechanicParam1,mechanicParam2,mechanicParam3,introCinematic,deathCinematic,bgmCueBase,designerNote\n" +
                BossDefinitionId + ",테스트 보스,TurnCount,,,,,,,music.boss.test,\n";
            const string phases =
                BossCsv.PhasesHeader + "\n" +
                BossDefinitionId + ",1,0,0,0,0,1,0,,\n" +
                BossDefinitionId + ",2,1,100,10,1,1.25,1,,\n" +
                BossDefinitionId + ",3,2,200,25,2,1.5,2,,\n";

            return BossCatalogCsvConverter.Convert(new BossCatalogCsvSource(profiles, phases, "boss-phase-test", "Boss Phase Test"));
        }

        /// <summary>
        /// TurnCount 지표 2페이즈 프로필: 1페이즈는 아우라 미저작, 2페이즈(임계 1 = 턴 2 진입)에 BossAura 아우라.
        /// </summary>
        private static BossCatalogDefinition CreateAuraBossCatalog()
        {
            const string profiles =
                "bossId,displayName,phaseMetric,mechanicId,mechanicParam1,mechanicParam2,mechanicParam3,introCinematic,deathCinematic,bgmCueBase,designerNote\n" +
                BossDefinitionId + ",테스트 보스,TurnCount,,,,,,,music.boss.test,\n";
            const string phases =
                BossCsv.PhasesHeader + "\n" +
                BossDefinitionId + ",1,0,0,0,0,1,0,,\n" +
                BossDefinitionId + ",2,1,0,0,0,1,0,BossAura,\n";

            return BossCatalogCsvConverter.Convert(new BossCatalogCsvSource(profiles, phases, "boss-aura-test", "Boss Aura Test"));
        }

        private static BossCatalogDefinition CreateGateBossCatalog(int phaseOnePatternGate, int phaseTwoThreshold)
        {
            var profiles =
                "bossId,displayName,phaseMetric,mechanicId,mechanicParam1,mechanicParam2,mechanicParam3,introCinematic,deathCinematic,bgmCueBase,designerNote\n" +
                BossDefinitionId + ",테스트 보스,TurnCount,,,,,,,music.boss.test,\n";
            var phases =
                BossCsv.PhasesHeader + "\n" +
                BossDefinitionId + $",1,0,0,0,{phaseOnePatternGate},1,0,,\n" +
                BossDefinitionId + $",2,{phaseTwoThreshold},0,0,{phaseOnePatternGate + 1},1,0,,\n";

            return BossCatalogCsvConverter.Convert(new BossCatalogCsvSource(profiles, phases, "boss-gate-test", "Boss Gate Test"));
        }
    }
}

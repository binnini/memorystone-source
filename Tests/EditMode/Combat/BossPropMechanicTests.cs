using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 철조각 기믹(iron-scrap)과 보스 기물(<c>boss-prop</c>) 규약.
    ///
    /// 압박의 형태는 <b>주기적인 큰 파도</b>다: N턴에 한 번 여러 개가 간격을 두고 한꺼번에 깔리고,
    /// 플레이어는 성숙까지 남은 몇 턴 안에 그 무더기를 처리해야 한다. 매 턴 한 개씩 새던 초기 형태와
    /// 달리 "주기·개수·간격"이 전부 저작값이므로, 그 셋이 실제로 지켜지는지가 이 파일의 앞 절반이다.
    ///
    /// 뒤 절반은 <b>제외 필터 전수 감사</b>다: 기물은 HP·피격·사망·세이브를 얻기 위해 몬스터로
    /// 호스팅되지만 "몬스터"로 세어져서는 안 된다. 새는 지점이 하나라도 생기면(목표가 영원히 각성하지 않거나,
    /// 카드 보상이 무한해지거나, 예고 UI가 기물로 덮이거나) 즉시 버그이므로 감사 목록 자체를 테스트로 고정한다.
    /// (기절 게이트 전수 감사 선례를 따른다.)
    /// </summary>
    public sealed class BossPropMechanicTests
    {
        private const string BossDefinitionId = "M002";
        private const string PropDefinitionId = "M901";
        private const string BossUnitId = "boss-01";
        private const int BossBaseHp = 40;
        private const int PropHp = 10;
        private const int StackPerProp = 15;
        private const int MaturityTurns = 2;
        private const int VolleyIntervalTurns = 5;
        private const int PhaseOneVolley = 2;
        private const int RingRadius = 2;
        private const int MinSpacing = 2;
        private const int MaxAlive = 8;

        [Test]
        public void BossLaysAWholeVolleyOnItsFirstActionAndNotOnTopOfAnyone()
        {
            var state = CreateBossState();
            Assert.That(LivingProps(state), Is.Empty);

            RunFullTurn(state);

            var props = LivingProps(state);
            Assert.That(props, Has.Count.EqualTo(PhaseOneVolley), "1페이즈 살포 개수 저작값만큼 한 번에 놓여야 한다.");
            foreach (var prop in props)
            {
                Assert.That(prop.DefinitionId, Is.EqualTo(PropDefinitionId));
                Assert.That(prop.MaxHp, Is.EqualTo(PropHp));
                Assert.That(prop.Coord, Is.Not.EqualTo(state.PlayerCoord));
                Assert.That(prop.Coord, Is.Not.EqualTo(BossCoord(state)));
                Assert.That(state.Map.TryGetCell(prop.Coord, out var cell), Is.True);
                Assert.That(cell.BaseWalkable, Is.True);
            }

            Assert.That(state.LastBossPropVolleyReport, Does.Not.Contain("TRUNCATED"));
        }

        [Test]
        public void PropsInAVolleyKeepTheAuthoredMinimumSpacing()
        {
            // 뭉쳐 놓이면 한 번의 광역기로 같이 부서져 기믹의 압박이 통째로 사라진다.
            var state = CreateBossState(volleyByPhase: "3|3|3");
            RunFullTurn(state);

            var coords = LivingProps(state).Select(prop => prop.Coord).ToList();
            Assert.That(coords, Has.Count.EqualTo(3));
            for (var i = 0; i < coords.Count; i++)
            {
                for (var j = i + 1; j < coords.Count; j++)
                {
                    Assert.That(coords[i].DistanceTo(coords[j]), Is.GreaterThanOrEqualTo(MinSpacing));
                }
            }
        }

        [Test]
        public void VolleysRepeatOnlyOnTheAuthoredInterval()
        {
            var state = CreateBossState();
            RunFullTurn(state);
            var firstVolleyIds = new HashSet<string>(LivingProps(state).Select(prop => prop.Id));
            Assert.That(firstVolleyIds, Has.Count.EqualTo(PhaseOneVolley));

            // 주기 사이의 턴에는 새 기물이 나지 않는다(기존 기물은 성숙해 흡수되어 사라진다).
            var spawnedBetween = 0;
            for (var turn = 2; turn < VolleyIntervalTurns + 1; turn++)
            {
                RunFullTurn(state);
                spawnedBetween += LivingProps(state).Count(prop => !firstVolleyIds.Contains(prop.Id));
            }

            Assert.That(spawnedBetween, Is.Zero, "살포는 주기적이어야 한다 — 사이 턴에는 한 개도 나지 않는다.");

            RunFullTurn(state); // 주기가 돌아온 턴
            Assert.That(LivingProps(state), Has.Count.EqualTo(PhaseOneVolley), "주기가 돌아오면 다음 무더기가 나야 한다.");
        }

        [Test]
        public void PropIsAbsorbedOnlyAfterItMaturesAndAddsTheAuthoredStackValue()
        {
            var state = CreateBossState();

            RunFullTurn(state); // 턴 1: 볼리 소환 (나이 0)
            Assert.That(AbsorbedStacks(state), Is.EqualTo(0));

            RunFullTurn(state); // 턴 2: 나이 1 — 아직 성숙 전이라 흡수되지 않는다
            Assert.That(AbsorbedStacks(state), Is.EqualTo(0), "maturityTurns 이전에는 흡수되지 않는다.");

            RunFullTurn(state); // 턴 3: 나이 2 — 성숙 → 볼리 전체가 흡수된다
            Assert.That(AbsorbedStacks(state), Is.EqualTo(PhaseOneVolley * StackPerProp));
        }

        [Test]
        public void AbsorbedStacksDriveThePhaseTransition()
        {
            // 임계 0/30/60 = 철조각 2개/4개. 흡수 스택이 페이즈 지표라는 것을 실제 전투 루프로 확인한다.
            var state = CreateBossState(phaseTwoThreshold: 30, phaseThreeThreshold: 60);
            var transitions = new List<(int From, int To)>();
            state.BossPhaseChanged += (_, from, to) => transitions.Add((from, to));

            for (var turn = 0; turn < 20 && !state.IsTerminal; turn++)
            {
                RunFullTurn(state);
            }

            Assert.That(AbsorbedStacks(state), Is.GreaterThanOrEqualTo(60));
            Assert.That(transitions, Is.EqualTo(new[] { (1, 2), (2, 3) }));
        }

        [Test]
        public void VolleySizeFollowsTheCurrentPhase()
        {
            // 페이즈별 개수는 boss_phases.csv가 아니라 mechanicParams 리스트에 있다(스키마 오염 방지).
            var state = CreateBossState(phaseTwoThreshold: 30, phaseThreeThreshold: 10000, volleyByPhase: "2|4|4");

            RunFullTurn(state);
            Assert.That(LivingProps(state), Has.Count.EqualTo(2), "1페이즈 볼리는 2개다.");

            // 2개 흡수 = 30스택 = 2페이즈. 그 뒤 주기가 돌아오면 볼리가 4개로 늘어야 한다.
            for (var turn = 0; turn < VolleyIntervalTurns && !state.IsTerminal; turn++)
            {
                RunFullTurn(state);
            }

            Assert.That(state.BossPhases.Single().CurrentPhase, Is.EqualTo(2));
            Assert.That(LivingProps(state), Has.Count.EqualTo(4), "2페이즈 볼리는 4개다.");
        }

        [Test]
        public void DestroyingAPropBeforeItMaturesDeniesTheStacks()
        {
            var state = CreateBossState(volleyByPhase: "1|1|1");
            RunFullTurn(state); // 철조각 1개 소환

            var prop = LivingProps(state).Single();
            KillProp(state, prop);

            RunFullTurn(state);
            RunFullTurn(state);

            // 파괴된 철조각은 성숙해도 흡수되지 않는다.
            Assert.That(state.Monsters.Single(monster => monster.Id == prop.Id).IsDead, Is.True);
            Assert.That(AbsorbedStacks(state), Is.Zero, "부순 기물은 스택을 주지 않는다.");
        }

        [Test]
        public void LivingPropCountNeverExceedsTheAuthoredGuard()
        {
            // maxAlive는 이제 게임플레이 노브가 아니라 저작 사고 방지 가드다. 정상 저작(주기 > 성숙)에서는
            // 살포 시점의 생존 기물이 항상 0이므로 실질 상한은 볼리 개수이며, 가드에는 닿지 않는다.
            var state = CreateBossState();

            for (var turn = 0; turn < 15 && !state.IsTerminal; turn++)
            {
                RunFullTurn(state);
                Assert.That(LivingProps(state), Has.Count.LessThanOrEqualTo(MaxAlive), "maxAlive 가드를 넘어서는 안 된다.");
                Assert.That(LivingProps(state), Has.Count.LessThanOrEqualTo(PhaseOneVolley), "실질 상한은 볼리 개수다.");
            }
        }

        [Test]
        public void VolleyPlacementIsDeterministicForAGivenRandomAndRotatesWithIt()
        {
            // "간격 유지 + 어느 정도 고정 + 항상 같지는 않게" = 고정 링을 볼리마다 임의 각도로 회전.
            // 같은 회전값이면 같은 자리, 다른 회전값이면 다른 자리여야 그 설계가 성립한다.
            var first = CreateBossState(volleyByPhase: "3|3|3", random: new FixedRotationRandom(0d));
            var same = CreateBossState(volleyByPhase: "3|3|3", random: new FixedRotationRandom(0d));
            var rotated = CreateBossState(volleyByPhase: "3|3|3", random: new FixedRotationRandom(0.1d));

            RunFullTurn(first);
            RunFullTurn(same);
            RunFullTurn(rotated);

            Assert.That(PropCoords(same), Is.EqualTo(PropCoords(first)), "같은 회전값이면 배치도 같아야 한다.");
            Assert.That(PropCoords(rotated), Is.Not.EqualTo(PropCoords(first)), "회전값이 다르면 자리가 달라져야 한다.");
        }

        [Test]
        public void TruncatedVolleyLeavesATrace()
        {
            // 최소 간격이 링 둘레보다 크면 요청한 개수를 다 놓을 수 없다. 조용히 넘어가면
            // "왜 5개가 아니라 3개지"를 추적할 방법이 없으므로 흔적이 남아야 한다.
            var state = CreateBossState(volleyByPhase: "5|5|5", minSpacing: 6);

            RunFullTurn(state);

            Assert.That(LivingProps(state), Has.Count.LessThan(5));
            Assert.That(state.LastBossPropVolleyReport, Does.Contain("TRUNCATED"));
        }

        [Test]
        public void PropAgeAndOwnerSurviveASuspendRoundTrip()
        {
            var state = CreateBossState();
            RunFullTurn(state);
            RunFullTurn(state); // 철조각 나이 1 (성숙 직전)

            var snapshot = state.CreateSuspendSnapshot();
            var savedProp = snapshot.Monsters
                .Where(monster => monster.DefinitionId == PropDefinitionId)
                .OrderByDescending(monster => monster.AgeTurns)
                .First();
            Assert.That(savedProp.AgeTurns, Is.EqualTo(1));
            Assert.That(savedProp.OwnerUnitId, Is.EqualTo(BossUnitId));
            Assert.That(savedProp.SpawnRole, Is.EqualTo(MonsterSpawnRoles.BossProp));

            var resumed = CreateBossState();
            resumed.RestoreFromSuspend(snapshot);

            Assert.That(AbsorbedStacks(resumed), Is.EqualTo(0));
            RunFullTurn(resumed); // 나이 2 → 성숙 → 흡수. 나이를 잃었다면 여기서 흡수되지 않는다.
            Assert.That(
                AbsorbedStacks(resumed),
                Is.EqualTo(PhaseOneVolley * StackPerProp),
                "나이를 왕복하지 않으면 성숙이 초기화되어 세이브 스컴이 된다.");
        }

        [Test]
        public void VolleyCooldownSurvivesASuspendRoundTrip()
        {
            var state = CreateBossState();
            RunFullTurn(state); // 볼리 1회 → 쿨다운 시작

            var snapshot = state.CreateSuspendSnapshot();
            Assert.That(snapshot.BossPhaseTracks.Single().MechanicCooldownTurns, Is.GreaterThan(0));

            var resumed = CreateBossState();
            resumed.RestoreFromSuspend(snapshot);
            var idsAfterResume = new HashSet<string>(LivingProps(resumed).Select(prop => prop.Id));

            RunFullTurn(resumed);

            Assert.That(
                LivingProps(resumed).Any(prop => !idsAfterResume.Contains(prop.Id)),
                Is.False,
                "쿨다운을 왕복하지 않으면 재개할 때마다 볼리가 한 번 더 터진다.");
        }

        // --- 제외 필터 전수 감사 ------------------------------------------------------------------

        [Test]
        public void PropIsNotCountedAsABossMonster()
        {
            var state = CreateBossState();
            RunFullTurn(state);
            Assert.That(LivingProps(state), Is.Not.Empty);

            var bossIds = state.Monsters.Where(monster => monster.SpawnRole == MonsterSpawnRoles.Boss).Select(monster => monster.Id);
            Assert.That(bossIds, Is.EqualTo(new[] { BossUnitId }));
            Assert.That(state.HasLivingBossMonster, Is.True);
        }

        [Test]
        public void PropDoesNotBlockTheAllMonstersDefeatedAwakeningGate()
        {
            // 보스 없이 기물만 남은 판: 기물이 "몬스터 전멸" 판정에 세어지면 목표가 영원히 각성하지 않는다.
            var state = CreateMemoryStoneStateWithPropOnly();

            Assert.That(LivingProps(state), Has.Count.EqualTo(1));
            Assert.That(state.HasBossMonster, Is.False);
            Assert.That(state.IsMemoryStoneAwakened, Is.True);
        }

        [Test]
        public void PropProducesNoIntentPreview()
        {
            var state = CreateBossState();
            RunFullTurn(state);
            var propIds = LivingProps(state).Select(prop => prop.Id).ToList();
            Assert.That(propIds, Is.Not.Empty);

            var previewIds = state.GetMonsterIntentPreviews(includeUnrevealed: true).Select(preview => preview.MonsterId).ToList();
            Assert.That(previewIds.Intersect(propIds), Is.Empty, "기물은 예고·공격 범위 오버레이에 등장해서는 안 된다.");
        }

        [Test]
        public void PropStaysDormantSoItNeverMovesOrAttacks()
        {
            var state = CreateBossState(volleyByPhase: "1|1|1");
            RunFullTurn(state);
            var prop = LivingProps(state).Single();
            var coordBefore = prop.Coord;

            RunFullTurn(state);

            var propAfter = state.Monsters.Single(monster => monster.Id == prop.Id);
            Assert.That(propAfter.ActivityState, Is.EqualTo(MonsterActivityState.Dormant));
            Assert.That(propAfter.Coord, Is.EqualTo(coordBefore));
            Assert.That(propAfter.HasAttackIntent, Is.False);
        }

        [Test]
        public void PropAbsorptionIsNotADeathSoItLeavesNoCorpseToRewardOrPresent()
        {
            var state = CreateBossState(volleyByPhase: "1|1|1");
            RunFullTurn(state);
            var propId = LivingProps(state).Single().Id;

            RunFullTurn(state);
            RunFullTurn(state); // 성숙 → 흡수

            // 흡수는 사망이 아니라 제거다: 죽은 기물로 남아 보상 추첨/사망 연출 후보가 되지 않는다.
            Assert.That(state.Monsters.Any(monster => monster.Id == propId), Is.False);
        }

        // ------------------------------------------------------------------------------------------

        /// <summary>회전각만 고정하는 RNG. 배치가 결정적이 되어 "같은 회전 = 같은 자리"를 시험할 수 있다.</summary>
        private sealed class FixedRotationRandom : System.Random
        {
            private readonly double value;

            public FixedRotationRandom(double value)
            {
                this.value = value;
            }

            public override double NextDouble() => value;
        }

        // --- 살포/흡수의 연출 계약(§철조각 임시 연출). 규칙이 표현 계층에 "무엇이 일어났는가"를
        // 정확히 넘기지 못하면 연출은 조용히 비어 버린다 — 화면을 보지 않고 잡을 수 있는 유일한 지점이다.

        [Test]
        public void AVolleyReportsEveryPlacedCoordToThePresentationLayer()
        {
            var state = CreateBossState();
            RunFullTurn(state);

            var casts = state.LastBossPropVolleyCasts;
            Assert.That(casts, Has.Count.EqualTo(1), "한 보스의 한 살포는 캐스트 한 건이다.");
            Assert.That(casts[0].BossUnitId, Is.EqualTo(BossUnitId));
            Assert.That(casts[0].BossCoord, Is.EqualTo(BossCoord(state)));
            Assert.That(
                casts[0].PlacedCoords.OrderBy(coord => coord).ToList(),
                Is.EqualTo(PropCoords(state)),
                "연출이 꽂을 칸은 실제로 기물이 놓인 칸과 같아야 한다.");
        }

        [Test]
        public void AbsorptionIsReportedWithItsBlastAndDamagesThePlayerStandingInIt()
        {
            const int BlastDamage = 5;
            var state = CreateBossState(blastRadius: 1, blastDamage: BlastDamage);
            RunFullTurn(state);

            var prop = LivingProps(state)[0];
            StandNextTo(state, prop.Coord);

            // maturityTurns = 2 → 살포된 턴을 0으로 세어 두 번의 몬스터 페이즈가 더 지나면 흡수된다.
            RunFullTurn(state);
            RunFullTurn(state);

            var absorption = state.LastBossPropAbsorptions.SingleOrDefault(record => record.PropUnitId == prop.Id);
            Assert.That(absorption.PropUnitId, Is.EqualTo(prop.Id), "흡수는 표현 계층에 기록되어야 한다.");
            Assert.That(absorption.PropCoord, Is.EqualTo(prop.Coord), "폭발 원점은 기물이 서 있던 칸이다.");
            Assert.That(absorption.BossCoord, Is.EqualTo(BossCoord(state)), "힘이 빨려 들어갈 목적지는 보스다.");
            Assert.That(absorption.BlastCoords, Does.Contain(state.PlayerCoord), "반경 1 폭발은 옆 칸을 덮는다.");
            Assert.That(absorption.DamageAppliedToPlayer, Is.EqualTo(BlastDamage));
        }

        [Test]
        public void AbsorptionWithoutAuthoredBlastDamageLeavesThePlayerAlone()
        {
            // blastRadius/blastDamage는 필수 키가 아니다 — 저작하지 않은 보스는 예전처럼 무피해로 흡수한다.
            var state = CreateBossState();
            RunFullTurn(state);
            var prop = LivingProps(state)[0];
            StandNextTo(state, prop.Coord);

            RunFullTurn(state);
            RunFullTurn(state);

            var absorption = state.LastBossPropAbsorptions.SingleOrDefault(record => record.PropUnitId == prop.Id);
            Assert.That(absorption.PropUnitId, Is.EqualTo(prop.Id));
            // 플레이어 HP로 재지 않는다 — 기물 옆은 보스 사거리 안이기도 해서 같은 턴의 일반 공격 피해가
            // 섞인다. 이 테스트가 고정하려는 것은 "흡수 폭발이 0을 준다"는 축 하나다.
            Assert.That(absorption.DamageAppliedToPlayer, Is.Zero, "저작이 없으면 흡수는 연출뿐이다.");
        }

        [Test]
        public void TheVolleyReplacesTheBossAttackOnThatTurnButNotOnTheNext()
        {
            // 살포는 그 턴의 보스 행동이다(사용자 확정). 예고 커밋과 공격 결의가 같은 술어를 읽지 않으면
            // "예고는 떴는데 공격이 안 나간다" 또는 그 반대가 되므로, 두 턴을 연속으로 본다.
            var state = CreateBossState();
            StandNextTo(state, BossCoord(state));

            var hpBeforeVolleyTurn = state.Player.Hp;
            RunFullTurn(state);
            Assert.That(state.LastBossPropVolleyCasts, Is.Not.Empty, "이 턴이 살포 턴이어야 한다.");
            Assert.That(
                state.Player.Hp,
                Is.EqualTo(hpBeforeVolleyTurn),
                "살포한 턴에는 보스가 공격하지 않는다.");

            var hpBeforeNextTurn = state.Player.Hp;
            RunFullTurn(state);
            Assert.That(state.LastBossPropVolleyCasts, Is.Empty, "다음 턴은 살포 턴이 아니다(주기 5).");
            Assert.That(
                state.Player.Hp,
                Is.LessThan(hpBeforeNextTurn),
                "살포하지 않는 턴에는 평소대로 공격한다 — 억제가 영구화되면 보스가 무해해진다.");
        }

        /// <summary>플레이어를 <paramref name="target"/> 옆의 빈 칸으로 옮긴다(폭발 반경 안에 세우기 위해).</summary>
        private static void StandNextTo(CombatState state, HexCoord target)
        {
            var occupied = new HashSet<HexCoord>(state.Monsters.Where(monster => !monster.IsDead).Select(monster => monster.Coord));
            var stand = HexArea.CellsWithin(target, 1)
                .Where(coord => coord != target
                                && !occupied.Contains(coord)
                                && state.Map.TryGetCell(coord, out var cell)
                                && cell.BaseWalkable)
                .OrderBy(coord => coord)
                .ToList();
            Assert.That(stand, Is.Not.Empty, $"{target} 옆에 설 자리가 있어야 한다.");
            Assert.That(state.TryDebugMovePlayer(stand[0]), Is.True, state.LastFailureReason);
        }

        private static void RunFullTurn(CombatState state)
        {
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();
        }

        /// <summary>
        /// 특정 기물을 확실히 죽인다. 공격 리플레이는 "가장 가까운 몬스터"를 치므로, 먼저 플레이어를 그 기물의
        /// 옆 타일로 옮겨 그 기물이 유일한 최근접이 되도록 만든다.
        /// </summary>
        private static void KillProp(CombatState state, MonsterRuntimeState prop)
        {
            var bossCoord = BossCoord(state);
            var occupied = new HashSet<HexCoord>(state.Monsters.Where(monster => !monster.IsDead).Select(monster => monster.Coord));
            var stand = HexArea.CellsWithin(prop.Coord, 1)
                .Where(coord => coord != prop.Coord
                                && !occupied.Contains(coord)
                                && bossCoord.DistanceTo(coord) > 1
                                && state.Map.TryGetCell(coord, out var cell)
                                && cell.BaseWalkable)
                .OrderBy(coord => coord)
                .ToList();
            Assert.That(stand, Is.Not.Empty, "기물 옆에 설 자리가 있어야 한다.");
            Assert.That(state.TryDebugMovePlayer(stand[0]), Is.True, state.LastFailureReason);

            Assert.That(state.DebugReplayPlayerAttack(lethal: true, out _, out var targetId), Is.True);
            Assert.That(targetId, Is.EqualTo(prop.Id), "옆에 선 기물이 최근접 대상이어야 한다.");
            Assert.That(state.Monsters.Single(monster => monster.Id == prop.Id).IsDead, Is.True);
        }

        private static List<MonsterRuntimeState> LivingProps(CombatState state) =>
            state.Monsters
                .Where(monster => !monster.IsDead && monster.SpawnRole == MonsterSpawnRoles.BossProp)
                .ToList();

        private static List<HexCoord> PropCoords(CombatState state) =>
            LivingProps(state).Select(prop => prop.Coord).OrderBy(coord => coord).ToList();

        private static int AbsorbedStacks(CombatState state) => state.BossPhases.Single().AbsorbedStacks;

        private static HexCoord BossCoord(CombatState state) =>
            state.Monsters.Single(monster => monster.Id == BossUnitId).Coord;

        /// <summary>
        /// 반경 4 원판 맵에 보스 하나. 보스는 플레이어에게서 멀리 두어 기물 소환 자리가 충분하게 만든다.
        /// 배치 RNG는 기본으로 고정 회전값을 꽂아 테스트가 무작위에 흔들리지 않게 한다.
        /// </summary>
        private static CombatState CreateBossState(
            int phaseTwoThreshold = 1000,
            int phaseThreeThreshold = 2000,
            string volleyByPhase = null,
            int minSpacing = MinSpacing,
            System.Random random = null,
            int blastRadius = 0,
            int blastDamage = 0)
        {
            var playerCoord = new HexCoord(0, 0);
            var bossCoord = new HexCoord(4, 0);
            var state = new CombatState(
                CreateDiskMap(new HexCoord(2, 0), 4),
                playerCoord,
                new[] { new MonsterConfig(BossUnitId, bossCoord, BossBaseHp, definitionId: BossDefinitionId, spawnRole: MonsterSpawnRoles.Boss) },
                CreateConfig(),
                monsterCatalog: CreateMonsterCatalog(),
                bossCatalog: CreateIronScrapCatalog(
                    phaseTwoThreshold,
                    phaseThreeThreshold,
                    volleyByPhase ?? $"{PhaseOneVolley}|{PhaseOneVolley}|{PhaseOneVolley}",
                    minSpacing,
                    blastRadius,
                    blastDamage),
                drawOpeningHands: false);
            state.ConfigureBossPropRandomForTests(random ?? new FixedRotationRandom(0d));
            return state;
        }

        private static CombatState CreateMemoryStoneStateWithPropOnly()
        {
            var memoryStoneCoord = new HexCoord(0, 0);
            var propCoord = new HexCoord(2, 0);
            var map = new HexMapData(
                CreateDiskMap(new HexCoord(1, 0), 3).AllCells.ToList(),
                objectRefs: new[]
                {
                    new HexMapObjectData("memory-main", "MemoryStone", "memorystone", memoryStoneCoord, role: "objective", interactable: true)
                });

            return new CombatState(
                map,
                memoryStoneCoord,
                new[] { new MonsterConfig("scrap-01", propCoord, PropHp, definitionId: PropDefinitionId, spawnRole: MonsterSpawnRoles.BossProp) },
                CreateConfig(),
                monsterCatalog: CreateMonsterCatalog(),
                drawOpeningHands: false);
        }

        private static CombatConfig CreateConfig()
        {
            return new CombatConfig(200, BossBaseHp, 2, 1, 4, 4, 5, 1, 3, playerVisionRange: 8);
        }

        private static HexMapData CreateDiskMap(HexCoord center, int radius)
        {
            return new HexMapData(HexArea.CellsWithin(center, radius)
                .Select(coord => new HexCellData(coord, "tile", "street", 1, true, false))
                .ToList());
        }

        private static MonsterCatalogDefinition CreateMonsterCatalog()
        {
            return new MonsterCatalogDefinition(
                "boss-prop-test-catalog",
                "Boss Prop Test Catalog",
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
                        attackPatterns: new[]
                        {
                            new MonsterAttackPattern("A100", "근접", 1, 0, 2)
                        }),
                    new MonsterCatalogEntry(
                        PropDefinitionId,
                        "철조각",
                        "boss-prop",
                        "B001",
                        detectionRange: 0,
                        movePerTurn: 1,
                        hp: PropHp,
                        attackPatterns: new[]
                        {
                            new MonsterAttackPattern("A900", "없음", 1, 0, 0)
                        })
                });
        }

        private static BossCatalogDefinition CreateIronScrapCatalog(
            int phaseTwoThreshold,
            int phaseThreeThreshold,
            string volleyByPhase,
            int minSpacing,
            int blastRadius = 0,
            int blastDamage = 0)
        {
            var mechanicParams = string.Join(";", new[]
            {
                $"{IronScrapMechanicParams.PropId}={PropDefinitionId}",
                $"{IronScrapMechanicParams.StackPerProp}={StackPerProp}",
                $"{IronScrapMechanicParams.MaturityTurns}={MaturityTurns}",
                $"{IronScrapMechanicParams.VolleyIntervalTurns}={VolleyIntervalTurns}",
                $"{IronScrapMechanicParams.VolleyByPhase}={volleyByPhase}",
                $"{IronScrapMechanicParams.RingRadius}={RingRadius}",
                $"{IronScrapMechanicParams.MinSpacing}={minSpacing}",
                $"{IronScrapMechanicParams.MaxAlive}={MaxAlive}",
                $"{IronScrapMechanicParams.BlastRadius}={blastRadius}",
                $"{IronScrapMechanicParams.BlastDamage}={blastDamage}"
            });

            var profiles =
                BossCsv.ProfilesHeader + "\n" +
                $"{BossDefinitionId},테스트 보스,AbsorbedStacks,iron-scrap,{mechanicParams},,,music.boss.test,\n";
            var phases =
                BossCsv.PhasesHeader + "\n" +
                $"{BossDefinitionId},1,0,0,0,0,1,0,,\n" +
                $"{BossDefinitionId},2,{phaseTwoThreshold},0,0,0,1,0,,\n" +
                $"{BossDefinitionId},3,{phaseThreeThreshold},0,0,0,1,0,,\n";

            return BossCatalogCsvConverter.Convert(new BossCatalogCsvSource(profiles, phases, "iron-scrap-test", "Iron Scrap Test"));
        }
    }
}

using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 보스 아레나 결계(P3). 이 기능의 위험은 전부 "봉쇄가 한쪽으로만 걸리는 것"에 있다:
    /// 플레이어만 막히면 몬스터가 자유롭게 드나들고, 몬스터만 막히면 플레이어가 보스전을 걸어 나간다.
    /// 그래서 <b>출입 금지의 대칭</b>을 양방향으로 따로 고정한다.
    ///
    /// 결계가 <c>UpdateOccupancy</c> 재구축 안에 있어야 한다는 것도 여기서 잡힌다: 밖에서 한 번
    /// 찍은 <c>TemporaryBlocked</c>였다면 첫 번째 상태 변화 뒤 테스트가 무너진다.
    /// </summary>
    public sealed class BossArenaBarrierTests
    {
        private const string BossSpawnRefId = "boss-spawn";
        private const string ArenaId = "boss-arena-1";
        private const string BossUnitId = "boss-01";

        private static readonly HexCoord ArenaCenter = new HexCoord(6, 0);
        private static readonly HexCoord PlayerStart = new HexCoord(2, 0);

        // 아레나 = ArenaCenter 반경 2. 경계 링(막히는 셀)은 그 바깥 한 겹, 즉 중심에서 거리 3.
        private const int ArenaRadius = 2;

        [Test]
        public void ArenaIsOpenBeforeTheEncounter()
        {
            var state = CreateArenaState();

            Assert.That(state.IsBossArenaBarrierActive, Is.False);
            Assert.That(state.SealedBossArenaId, Is.Empty);
            Assert.That(state.SealedBossArenaBoundaryCoords, Is.Empty);
            // 열려 있는 동안에는 아레나 밖으로 나가는 칸이 정상 이동 후보다.
            Assert.That(IsReachableForPlayer(state, new HexCoord(3, 0)), Is.True);
        }

        [Test]
        public void SteppingIntoTheArenaSealsItAndStopsTheWalkAtTheCrossingCell()
        {
            // 이동 사거리 4로 아레나를 관통해 (6,-1)까지 노린다. 경로가 아레나에 처음 닿는 칸은 (4,0)이므로
            // 걷기는 거기서 멈춰야 한다 — 긴 이동 카드로 조우를 건너뛰고 보스 옆에 착지할 수 없다. 전투 시작
            // 좌표(중앙 전방)는 그것과 분리된다(§8-9 P5) — 여기서는 걷기가 경계 칸에서 잘리는 것을 고정한다.
            var state = CreateArenaState(moveRange: 4);
            var deepTarget = new HexCoord(6, -1);
            Assert.That(ArenaCenter.DistanceTo(deepTarget), Is.LessThanOrEqualTo(ArenaRadius), "목표는 아레나 안쪽이어야 시험이 성립한다.");

            Assert.That(state.TryPlayerMove(deepTarget), Is.True);

            var crossing = new HexCoord(4, 0);
            Assert.That(state.LastBossArenaEntryStopCoord, Is.EqualTo(crossing), "강제 정지 좌표는 경계를 넘은 그 칸이어야 한다.");
            Assert.That(state.LastPlayerMovePath.Last(), Is.EqualTo(crossing), "연출 경로도 정지 지점에서 잘려야 한다.");
            Assert.That(state.IsBossArenaBarrierActive, Is.True);
            Assert.That(state.SealedBossArenaId, Is.EqualTo(ArenaId));
            // 전투는 강제 정지 좌표가 아니라 중앙 전방에서 시작한다(관통 이동으로 조우를 건너뛰지 못하되,
            // 멈춘 자리 그대로 싸우지도 않는다).
            Assert.That(state.PlayerCoord, Is.Not.EqualTo(crossing), "전투 시작 좌표는 강제 정지 좌표와 분리된다.");
            Assert.That(state.Map.Areas.Single(a => a.IsBossArena).Contains(state.PlayerCoord), Is.True, "전투 시작 좌표는 아레나 안이다.");
        }

        [Test]
        public void SealedBarrierBlocksThePlayerFromLeaving()
        {
            var state = CreateArenaState(moveRange: 4);
            SealArena(state);

            // 링 셀(중심 거리 3)은 물론, 그 너머 어떤 칸도 도달 후보가 될 수 없다.
            var ringCell = new HexCoord(3, 0);
            Assert.That(ArenaCenter.DistanceTo(ringCell), Is.EqualTo(ArenaRadius + 1));
            Assert.That(IsReachableForPlayer(state, ringCell), Is.False, "결계 링은 이동 후보에서 빠져야 한다.");
            Assert.That(IsReachableForPlayer(state, PlayerStart), Is.False, "아레나 밖으로 나가는 경로가 없어야 한다.");
            Assert.That(state.TryPlayerMove(PlayerStart), Is.False);

            // 아레나 내부 이동은 그대로 자유롭다 — 결계는 싸움터를 좁히는 것이 아니라 가두는 것이다.
            Assert.That(IsReachableForPlayer(state, new HexCoord(5, 1)), Is.True);
        }

        [Test]
        public void SealedBarrierBlocksMonstersFromEnteringTheSameCells()
        {
            var state = CreateArenaState(moveRange: 4, outsiderCoord: new HexCoord(0, 0));
            SealArena(state);

            var outsider = state.Monsters.Single(monster => monster.Id == "outsider");
            Assert.That(state.IsBossArenaBarrierActive, Is.True);

            // 플레이어가 아레나 안에 있으므로 추격형 몬스터는 아레나 쪽으로 붙으려 한다. 여러 턴을 돌려도
            // 링을 넘지 못해야 한다(플레이어 봉쇄와 같은 셀이 막히므로 대칭이 성립한다).
            for (var turn = 0; turn < 8 && !state.IsTerminal; turn++)
            {
                state.EndAction();
                state.ResolveMonsterMovement();
                state.EndAction();
                state.ResolveMonsterAction();
            }

            var current = state.Monsters.Single(monster => monster.Id == outsider.Id);
            Assert.That(ArenaCenter.DistanceTo(current.Coord), Is.GreaterThan(ArenaRadius),
                "결계 밖 몬스터가 아레나 안으로 들어와서는 안 된다.");
        }

        [Test]
        public void ArenaEntryTriggersOnlyOnce()
        {
            var state = CreateArenaState(moveRange: 4);
            SealArena(state);
            var sealedId = state.SealedBossArenaId;

            // 아레나 안에서 계속 움직여도 조우가 다시 시작되거나 좌표가 다시 붙잡히지 않는다.
            for (var turn = 0; turn < 3 && !state.IsTerminal; turn++)
            {
                state.EndAction();
                state.ResolveMonsterMovement();
                state.EndAction();
                state.ResolveMonsterAction();
                state.TryPlayerMove(new HexCoord(5, 1));
            }

            Assert.That(state.SealedBossArenaId, Is.EqualTo(sealedId));
            Assert.That(state.IsBossArenaBarrierActive, Is.True);
        }

        [Test]
        public void BarrierOpensWhenTheBoundBossDies()
        {
            var state = CreateArenaState(moveRange: 4);
            SealArena(state);
            Assert.That(state.IsBossArenaBarrierActive, Is.True);

            KillBoss(state);

            Assert.That(state.IsBossArenaBarrierActive, Is.False, "보스가 죽으면 결계는 즉시 열린다.");
            Assert.That(state.SealedBossArenaBoundaryCoords, Is.Empty);
            Assert.That(IsReachableForPlayer(state, new HexCoord(3, 0)), Is.True, "나가는 길이 다시 열려야 한다.");
        }

        [Test]
        public void EncounterDoesNotStartWhenTheBoundBossIsAlreadyDead()
        {
            var state = CreateArenaState(moveRange: 4);
            KillBoss(state);

            Assert.That(state.TryPlayerMove(new HexCoord(4, 0)), Is.True);

            Assert.That(state.PlayerCoord, Is.EqualTo(new HexCoord(4, 0)), "강제 정지가 걸리지 않아야 한다.");
            Assert.That(state.IsBossArenaBarrierActive, Is.False);
            Assert.That(state.SealedBossArenaId, Is.Empty);
        }

        [Test]
        public void SealingTheArenaTelegraphsTheFirstVolleyAndTheVolleyLandsOnThoseCells()
        {
            // 2026-09-05 후속 #1: 조우 턴의 첫 살포도 예고된다. 봉인 직후(플레이어 행동 페이즈)에 예고 칸이 서 있고,
            // 그 턴 몬스터 페이즈의 살포는 그 칸에 그대로 놓인다(예고=배치). 예고는 플레이어 시작 칸을 피한다.
            var state = CreateArenaState(moveRange: 4, ironScrap: true);
            Assert.That(state.GetBossPropVolleyTelegraphCells(), Is.Empty, "조우 전엔 예고가 없다.");

            Assert.That(state.TryPlayerMove(new HexCoord(6, -1)), Is.True, state.LastFailureReason);
            Assert.That(state.IsBossArenaBarrierActive, Is.True);
            var telegraph = state.GetBossPropVolleyTelegraphCells().ToList();
            Assert.That(telegraph, Has.Count.EqualTo(2), "볼리 개수(2|2)만큼 봉인 순간에 예고된다.");
            Assert.That(telegraph, Has.No.Member(state.PlayerCoord), "예고는 확정된 플레이어 시작 칸을 피한다.");

            state.EndAction();
            state.ResolveMonsterMovement();
            state.EndAction();
            state.ResolveMonsterAction();

            var props = state.Monsters.Where(monster => monster.DefinitionId == "M901" && !monster.IsDead).Select(monster => monster.Coord).ToList();
            Assert.That(props, Is.EquivalentTo(telegraph), "첫 살포가 예고한 칸에 그대로 놓인다.");
            Assert.That(state.GetBossPropVolleyTelegraphCells(), Is.Empty, "살포가 예고를 소비한다.");
        }

        [Test]
        public void SealedArenaSurvivesSuspendRoundTrip()
        {
            var state = CreateArenaState(moveRange: 4);
            SealArena(state);
            Assert.That(state.IsBossArenaBarrierActive, Is.True);

            var snapshot = state.CreateSuspendSnapshot();
            Assert.That(snapshot.SealedBossArenaId, Is.EqualTo(ArenaId));

            var resumed = CreateArenaState(moveRange: 4);
            resumed.RestoreFromSuspend(snapshot);

            Assert.That(resumed.SealedBossArenaId, Is.EqualTo(ArenaId));
            Assert.That(resumed.IsBossArenaBarrierActive, Is.True);
            Assert.That(resumed.SealedBossArenaBoundaryCoords, Is.EquivalentTo(state.SealedBossArenaBoundaryCoords));
            // 재개 직후 점유 재구축이 결계를 다시 세웠는지 — 상태만 실려 오고 봉쇄가 빠지면 걸어 나갈 수 있다.
            Assert.That(IsReachableForPlayer(resumed, PlayerStart), Is.False);
        }

        [Test]
        public void BoundaryRingIsTheCellsJustOutsideTheArena()
        {
            var state = CreateArenaState(moveRange: 4);
            SealArena(state);

            var ring = state.SealedBossArenaBoundaryCoords;
            Assert.That(ring, Is.Not.Empty);
            foreach (var coord in ring)
            {
                Assert.That(ArenaCenter.DistanceTo(coord), Is.EqualTo(ArenaRadius + 1),
                    "링은 아레나 안쪽 테두리가 아니라 바깥 한 겹이다.");
                Assert.That(state.Map.Contains(coord), Is.True, "판에 없는 좌표는 링에서 빠져야 한다.");
            }
        }

        /// <summary>
        /// 결계가 닫힌 뒤 보스 기물은 아레나 안에만 난다. 기물 스폰은 "보스에서 가장 먼 고리"를
        /// 선호하므로(P1 결정) 보스가 플레이어를 쫓아 가장자리에 붙으면 그 반경이 결계를 넘어간다 —
        /// 그러면 갇힌 플레이어가 부술 수 없는 곳에서 기물이 자라 기믹의 압박이 사라진다.
        /// 보스 랩에서 실제로 관측된 결함이라 회귀로 고정한다.
        /// </summary>
        [Test]
        public void BossPropsNeverSpawnOutsideASealedArena()
        {
            var state = CreateArenaState(moveRange: 4, ironScrap: true);
            SealArena(state);

            var arena = state.Map.Areas.Single(area => area.IsBossArena);
            for (var turn = 0; turn < 10 && !state.IsTerminal; turn++)
            {
                state.EndAction();
                state.ResolveMonsterMovement();
                state.EndAction();
                state.ResolveMonsterAction();

                var strays = state.Monsters
                    .Where(monster => MonsterSpawnRoles.IsBossProp(monster.SpawnRole) && !monster.IsDead)
                    .Where(monster => !arena.Contains(monster.Coord))
                    .Select(monster => $"{monster.Id}@{monster.Coord}")
                    .ToList();
                Assert.That(strays, Is.Empty, $"턴 {turn + 1}: 결계 밖에 기물이 났다 — {string.Join(", ", strays)}");
            }
        }

        /// <summary>
        /// 🔴🔴 아레나에 들어가기 전의 보스는 <b>기믹도 돌지 않는다</b>(2026-09-02 #17 회귀).
        ///
        /// <para>2026-09-02 #4의 게이트는 행동(<c>ClassifyMonsterActivity</c>→<c>Dormant</c>)과 표시에만
        /// 걸렸고 기믹 결의는 활동 분류 <b>밖</b>이라 그대로 돌았다. 출하 Stage_1 실측: 플레이어가
        /// 보스에서 57칸 떨어져 제자리에 있어도 보스가 무대 뒤에서 철조각을 살포하고 스스로 흡수해
        /// <b>25턴 만에 3페이즈</b>(스택 165 · 최대 체력 48→84)가 됐다. 플레이어가 도착했을 때는
        /// 살포 주기가 한참 진행돼 「기믹이 한 번도 안 나오는」 보스전이 된다.</para>
        ///
        /// <para>「무대 뒤가 조용하다」와 「무대에 서면 시작한다」를 <b>한 시험에서 같이</b> 본다 —
        /// 앞만 보면 기믹을 통째로 죽여도 통과하기 때문이다(게이트를 과하게 걸어도 초록인 시험은
        /// 없는 것보다 나쁘다).</para>
        /// </summary>
        [Test]
        public void BossMechanicsStayIdleUntilTheArenaIsSealedAndStartOnEncounter()
        {
            var state = CreateArenaState(moveRange: 4, ironScrap: true);
            var bossMaxHpAtStart = state.Monsters.Single(monster => monster.Id == BossUnitId).MaxHp;

            // ① 무대 뒤: 플레이어는 아레나에 들어가지 않고 턴만 넘긴다.
            for (var turn = 0; turn < 12 && !state.IsTerminal; turn++)
            {
                state.EndAction();
                state.ResolveMonsterMovement();
                state.EndAction();
                state.ResolveMonsterAction();
            }

            Assert.That(state.IsBossArenaBarrierActive, Is.False, "아직 조우하지 않았어야 시험이 성립한다.");
            Assert.That(
                state.Monsters.Count(monster => MonsterSpawnRoles.IsBossProp(monster.SpawnRole) && !monster.IsDead),
                Is.Zero,
                "조우 전에 보스 기물이 났다 — 기믹이 무대 뒤에서 돌고 있다.");
            Assert.That(state.TryGetBossPhaseState(BossUnitId, out var idlePhase), Is.True);
            Assert.That(idlePhase.CurrentPhase, Is.EqualTo(1), "조우 전에 페이즈가 올라갔다.");
            Assert.That(idlePhase.AbsorbedStacks, Is.Zero, "조우 전에 스택을 흡수했다.");
            Assert.That(
                state.Monsters.Single(monster => monster.Id == BossUnitId).MaxHp,
                Is.EqualTo(bossMaxHpAtStart),
                "조우 전에 페이즈 보너스로 최대 체력이 늘었다.");

            // ② 무대 위: 봉인하면 그때부터 기믹이 돈다.
            SealArena(state);
            var sawProps = false;
            for (var turn = 0; turn < 8 && !state.IsTerminal && !sawProps; turn++)
            {
                state.EndAction();
                state.ResolveMonsterMovement();
                state.EndAction();
                state.ResolveMonsterAction();
                sawProps = state.Monsters.Any(monster =>
                    MonsterSpawnRoles.IsBossProp(monster.SpawnRole) && !monster.IsDead);
            }

            Assert.That(sawProps, Is.True, "조우 뒤에도 기믹이 돌지 않는다 — 게이트를 과하게 걸었다.");
        }

        /// <summary>
        /// 결계 안에서는 암시야가 걷힌다(§9.6). 나갈 수 없는 방의 지형을 감추는 것은 정보를 주지 않고
        /// 불편만 준다 — 레이드 문법상 아레나는 통째로 보여야 한다.
        /// </summary>
        [Test]
        public void SealingTheArenaRevealsEveryArenaCell()
        {
            // 시야를 좁게 잡아야 봉인 전에 아레나 안쪽이 실제로 가려져 있어 시험이 성립한다.
            var state = CreateArenaState(moveRange: 4, playerVisionRange: 2);
            var arena = state.Map.Areas.Single(area => area.IsBossArena);
            Assert.That(
                arena.Coords.Any(coord => state.GetVisibility(coord) != HexCellVisibility.Revealed),
                Is.True,
                "봉인 전에는 아레나 안쪽이 가려져 있어야 한다.");

            SealArena(state);

            Assert.That(
                arena.Coords.All(coord => state.GetVisibility(coord) == HexCellVisibility.Revealed),
                Is.True,
                "봉인 직후 아레나 전체가 밝아야 한다.");
        }

        /// <summary>
        /// 밝아진 아레나는 <b>다시 어두워지지 않는다</b>. 임시 공개(플레이어 시야)로 밝아진 칸을 그냥
        /// Reveal만 하면 다음 시야 갱신에서 Hinted로 강등되므로, 영구 공개 경로를 쓰는지 고정한다.
        /// </summary>
        [Test]
        public void RevealedArenaStaysRevealedAfterTheBossDies()
        {
            var state = CreateArenaState(moveRange: 4, playerVisionRange: 2);
            var arena = state.Map.Areas.Single(area => area.IsBossArena);
            SealArena(state);

            KillBoss(state);
            for (var turn = 0; turn < 3 && !state.IsTerminal; turn++)
            {
                state.EndAction();
                state.ResolveMonsterMovement();
                state.EndAction();
                state.ResolveMonsterAction();
            }

            Assert.That(
                arena.Coords.All(coord => state.GetVisibility(coord) == HexCellVisibility.Revealed),
                Is.True,
                "결계가 열려도 아레나는 밝은 채로 남아야 한다(가시성은 내려가지 않는다).");
        }

        /// <summary>
        /// 기물 배치의 기준점은 <b>아레나 중심</b>이지 보스가 아니다(§9.4). 보스 기준이면 보스가 움직일
        /// 때마다 배치가 통째로 따라다녀 "어느 정도 정해진 자리"라는 설계 의도가 사라진다.
        /// </summary>
        [Test]
        public void BossPropsAreLaidOutAroundTheArenaCentreNotTheBoss()
        {
            var state = CreateArenaState(moveRange: 4, ironScrap: true);
            SealArena(state);
            var arena = state.Map.Areas.Single(area => area.IsBossArena);

            state.EndAction();
            state.ResolveMonsterMovement();
            state.EndAction();
            state.ResolveMonsterAction();

            var props = state.Monsters
                .Where(monster => MonsterSpawnRoles.IsBossProp(monster.SpawnRole) && !monster.IsDead)
                .ToList();
            Assert.That(props, Is.Not.Empty, "봉인된 아레나 안에 기물이 놓여야 한다.");

            var centre = arena.GetCenter();
            Assert.That(centre, Is.EqualTo(ArenaCenter), "반경 대칭 아레나의 중심은 저작 중심과 같아야 한다.");
            foreach (var prop in props)
            {
                Assert.That(centre.DistanceTo(prop.Coord), Is.LessThanOrEqualTo(ArenaRadius));
            }

            // 최소 간격 저작값(2)이 실제로 지켜진다 — 뭉쳐 놓이면 한 방에 같이 부서져 압박이 사라진다.
            foreach (var left in props)
            {
                foreach (var right in props)
                {
                    if (left.Id != right.Id)
                    {
                        Assert.That(left.Coord.DistanceTo(right.Coord), Is.GreaterThanOrEqualTo(2));
                    }
                }
            }
        }

        [Test]
        public void MapWithoutAnArenaNeverSeals()
        {
            var state = CreateArenaState(moveRange: 4, includeArena: false);

            Assert.That(state.TryPlayerMove(new HexCoord(4, 0)), Is.True);

            Assert.That(state.PlayerCoord, Is.EqualTo(new HexCoord(4, 0)));
            Assert.That(state.IsBossArenaBarrierActive, Is.False);
        }

        /// <summary>
        /// 전투 시작 좌표는 강제 정지 좌표(경계 칸)와 <b>분리</b>된다(§8-9 P5 개정). 조우 뒤 플레이어는
        /// "경계를 넘은 그 칸"이 아니라 아레나 중앙 전방에 서고, 그 위치가 규칙상 <c>PlayerCoord</c>에
        /// 확정된다(마커 스냅만 표현 계층이 암전 뒤로 미룬다).
        /// </summary>
        [Test]
        public void BattleStartSeparatesFromTheForcedStopAndSitsInFrontOfCentre()
        {
            var state = CreateArenaState(moveRange: 4);
            SealArena(state);
            var arena = state.Map.Areas.Single(area => area.IsBossArena);

            var entry = new HexCoord(4, 0);
            Assert.That(state.LastBossArenaEntryStopCoord, Is.EqualTo(entry), "경계 칸(강제 정지 좌표)이 기록돼야 한다.");

            var start = state.ResolveSealedBossArenaBattleStart();
            Assert.That(state.PlayerCoord, Is.EqualTo(start), "전투 시작 좌표가 규칙상 위치에 확정돼야 한다.");
            Assert.That(arena.Contains(start), Is.True, "전투 시작 좌표는 아레나 안이어야 한다.");
            Assert.That(start, Is.Not.EqualTo(entry), "강제 정지 좌표와 달라야 분리의 의미가 있다.");
            Assert.That(start, Is.Not.EqualTo(ArenaCenter), "보스가 선 중심 위에 겹쳐 서면 안 된다.");
            Assert.That(
                state.Monsters.Any(monster => !monster.IsDead && monster.Coord == start), Is.False,
                "living 몬스터가 점유한 칸을 고르면 안 된다.");
            // 중앙 전방 = 진입 쪽에 더 가깝다. 진입 (4,0), 중심 대칭 반대편 (8,0)보다 진입에 가까워야 한다.
            Assert.That(
                start.DistanceTo(entry), Is.LessThan(start.DistanceTo(new HexCoord(8, 0))),
                "시작 좌표는 반대편이 아니라 진입 쪽(중앙 전방)이어야 한다.");
        }

        /// <summary>
        /// 봉인이 없으면 전투 시작 좌표는 진입 칸으로 폴백한다(현재 플레이어 좌표). 아레나 없는 보스전의
        /// 동작을 바꾸지 않기 위해서다.
        /// </summary>
        [Test]
        public void BattleStartFallsBackToTheEntryWhenNoArenaIsSealed()
        {
            var state = CreateArenaState(moveRange: 4);

            Assert.That(state.LastBossArenaEntryStopCoord, Is.Null);
            Assert.That(state.ResolveSealedBossArenaBattleStart(), Is.EqualTo(state.PlayerCoord));
        }

        /// <summary>
        /// 보스바 조우 게이트: 아레나에 묶인 보스는 결계가 <b>봉인(조우)</b>되기 전에는 조우로 치지 않는다 —
        /// 보스가 판에 존재만 해도 상단 바를 띄우면 결계로 처음 만나기 전에 보스가 노출된다(레이드 문법 위반).
        /// 아레나 없는 고정 보스는 존재만으로 조우로 보는 것은 <c>BossHudViewTests</c>(아레나 미저작)가 고정한다.
        /// </summary>
        [Test]
        public void BossIsNotEncounteredUntilTheArenaIsSealed()
        {
            var state = CreateArenaState(moveRange: 4);

            Assert.That(state.HasLivingBossMonster, Is.True, "보스는 판에 존재한다.");
            Assert.That(state.HasEncounteredLivingBoss, Is.False, "봉인 전에는 조우하지 않았다 — 보스바가 뜨면 안 된다.");

            SealArena(state);
            Assert.That(state.HasEncounteredLivingBoss, Is.True, "봉인(조우) 후에는 보스바가 뜬다.");

            KillBoss(state);
            Assert.That(state.HasEncounteredLivingBoss, Is.False, "보스가 죽으면 다시 조우 대상이 없다.");
        }

        // ---- helpers ------------------------------------------------------------------------

        private static void SealArena(CombatState state)
        {
            Assert.That(state.TryPlayerMove(new HexCoord(4, 0)), Is.True);
            Assert.That(state.IsBossArenaBarrierActive, Is.True, "조우가 시작되지 않으면 나머지 검증이 무의미하다.");
        }

        /// <summary>
        /// 보스를 처치한다. <c>DebugReplayPlayerAttack</c>이 <b>최근접</b>을 치므로 먼저 보스 옆에 서야 한다
        /// (BossPropMechanicTests.KillProp와 같은 함정).
        /// </summary>
        private static void KillBoss(CombatState state)
        {
            var occupied = new System.Collections.Generic.HashSet<HexCoord>(
                state.Monsters.Where(monster => !monster.IsDead).Select(monster => monster.Coord));
            var stand = HexArea.CellsWithin(ArenaCenter, 1)
                .Where(coord => coord != ArenaCenter
                                && !occupied.Contains(coord)
                                && state.Map.TryGetCell(coord, out var cell)
                                && cell.BaseWalkable)
                .OrderBy(coord => coord)
                .First();
            Assert.That(state.TryDebugMovePlayer(stand), Is.True, state.LastFailureReason);
            Assert.That(state.DebugReplayPlayerAttack(lethal: true, out _, out var targetId), Is.True);
            Assert.That(targetId, Is.EqualTo(BossUnitId), "옆에 선 보스가 최근접 대상이어야 한다.");
            Assert.That(state.Monsters.Single(monster => monster.Id == BossUnitId).IsDead, Is.True);
        }

        private static bool IsReachableForPlayer(CombatState state, HexCoord coord)
        {
            return state.GetReachablePlayerMoves().ContainsKey(coord);
        }

        private static CombatState CreateArenaState(int moveRange = 1, HexCoord? outsiderCoord = null, bool includeArena = true, bool ironScrap = false, int playerVisionRange = 12)
        {
            var move = new CardDefinition(
                "arena-move",
                "Arena Move",
                CardCategory.Movement,
                CardEffectType.Move,
                0,
                moveRange,
                0,
                targeting: "walkable_in_range",
                status: CardCatalogStatus.Approved,
                instanceId: "arena-move-instance");
            // KillBoss가 쓰는 근접 공격. 카탈로그는 액션 카드가 최소 1장 있어야 성립한다.
            var strike = new CardDefinition(
                "arena-strike",
                "Arena Strike",
                CardCategory.Action,
                CardEffectType.Attack,
                1,
                1,
                200,
                targeting: "living_monster_in_range",
                status: CardCatalogStatus.Approved,
                instanceId: "arena-strike-instance");
            var catalog = new CardCatalogDefinition(
                "test.boss-arena",
                "Boss arena test catalog",
                new[]
                {
                    new CardCatalogEntry(move.Id, move.DisplayName, move.Category, move.EffectType, move.Cost, move.Range, move.Amount, move.Targeting, status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(strike.Id, strike.DisplayName, strike.Category, strike.EffectType, strike.Cost, strike.Range, strike.Amount, strike.Targeting, status: CardCatalogStatus.Approved, targetMode: CardTargetMode.Enemy)
                });

            var monsters = new System.Collections.Generic.List<MonsterConfig>
            {
                new MonsterConfig(BossUnitId, ArenaCenter, 60, definitionId: "M002", spawnRefId: BossSpawnRefId, spawnRole: MonsterSpawnRoles.Boss)
            };
            if (outsiderCoord.HasValue)
            {
                monsters.Add(new MonsterConfig("outsider", outsiderCoord.Value, 20, definitionId: "M001", spawnRole: MonsterSpawnRoles.NormalEnemy));
            }

            // 이동 카드를 손패에 무한히 들고 있게 해서(같은 카드 8장) 여러 턴 이동 시험이 카드 고갈로
            // 흔들리지 않게 한다.
            var hand = Enumerable.Range(0, 8).Select(_ => move).ToArray();

            return new CombatState(
                CreateArenaMap(includeArena),
                PlayerStart,
                monsters,
                new CombatConfig(200, 60, 2, 1, 8, 8, 5, 1, 3, playerVisionRange: playerVisionRange),
                cardCatalog: catalog,
                monsterCatalog: ironScrap ? CreateScrapMonsterCatalog() : null,
                movementDeck: new CardDeckState(null, hand, null, null),
                actionDeck: new CardDeckState(null, Enumerable.Range(0, 8).Select(_ => strike).ToArray(), null, null),
                drawOpeningHands: false,
                bossCatalog: ironScrap ? CreateIronScrapCatalog() : null);
        }

        /// <summary>철조각 기믹이 실제로 도는 보스 카탈로그(출하 저작값과 같은 수치).</summary>
        private static BossCatalogDefinition CreateIronScrapCatalog()
        {
            var mechanicParams = string.Join(";", new[]
            {
                $"{IronScrapMechanicParams.PropId}=M901",
                $"{IronScrapMechanicParams.StackPerProp}=15",
                $"{IronScrapMechanicParams.MaturityTurns}=2",
                $"{IronScrapMechanicParams.VolleyIntervalTurns}=5",
                $"{IronScrapMechanicParams.VolleyByPhase}=2|2",
                $"{IronScrapMechanicParams.RingRadius}=3",
                $"{IronScrapMechanicParams.MinSpacing}=2",
                $"{IronScrapMechanicParams.MaxAlive}=8"
            });

            var profiles =
                BossCsv.ProfilesHeader + "\n" +
                $"M002,아레나 테스트 보스,AbsorbedStacks,iron-scrap,{mechanicParams},,,music.boss.test,\n";
            var phases =
                BossCsv.PhasesHeader + "\n" +
                "M002,1,0,0,0,0,1,0,,\n" +
                "M002,2,1000,0,0,0,1,0,,\n";

            return BossCatalogCsvConverter.Convert(new BossCatalogCsvSource(profiles, phases, "arena-iron-scrap", "Arena Iron Scrap"));
        }

        private static MonsterCatalogDefinition CreateScrapMonsterCatalog()
        {
            return new MonsterCatalogDefinition(
                "boss-arena-test-catalog",
                "Boss Arena Test Catalog",
                new[]
                {
                    new MonsterCatalogEntry(
                        "M002", "아레나 테스트 보스", "test-melee", "B001",
                        detectionRange: 12, movePerTurn: 1, hp: 60,
                        attackPatterns: new[] { new MonsterAttackPattern("A100", "근접", 1, 0, 2) }),
                    new MonsterCatalogEntry(
                        "M901", "철조각", "boss-prop", "B001",
                        detectionRange: 0, movePerTurn: 1, hp: 10,
                        attackPatterns: new[] { new MonsterAttackPattern("A900", "없음", 1, 0, 0) }),
                    new MonsterCatalogEntry(
                        "M001", "일반", "test-melee", "B001",
                        detectionRange: 12, movePerTurn: 1, hp: 20,
                        attackPatterns: new[] { new MonsterAttackPattern("A100", "근접", 1, 0, 2) })
                });
        }

        private static HexMapData CreateArenaMap(bool includeArena)
        {
            var cells = HexArea.CellsWithin(new HexCoord(3, 0), 8)
                .Select(coord => new HexCellData(coord, "tile", "street", 1, true, false))
                .ToList();
            var areas = includeArena
                ? new[]
                {
                    new HexMapAreaRef(
                        ArenaId,
                        HexArea.CellsWithin(ArenaCenter, ArenaRadius),
                        HexMapAreaRef.BossArenaPurpose,
                        BossSpawnRefId)
                }
                : System.Array.Empty<HexMapAreaRef>();

            return new HexMapData(
                cells,
                monsterSpawnRefs: new[]
                {
                    new HexMonsterSpawnRef(BossSpawnRefId, "M002", ArenaCenter, MonsterSpawnRoles.Boss)
                },
                areas: areas);
        }
    }
}

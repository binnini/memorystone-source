using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 시드 재현성 P5 게이트(docs/prompts/seed-determinism-handoff.md §4 P5): 「같은 시드로 중간에 몇 번을
    /// 저장·재개해도 무중단 판과 같은 기록이 나온다」. 관찰 대상은 서스펜드 스냅샷에 실리는 결과값
    /// (손패·더미 순서·몬스터 패턴·피해 굴림·취약 부위·기물 좌표·체력)이지 「같은 순서로 굴렸는가」가 아니다.
    /// 재개는 컨트롤러의 재개 부팅 경로를 그대로 흉내 낸다: 새 상태(<c>drawOpeningHands:false</c>) → <c>RestoreFromSuspend</c>.
    /// </summary>
    public sealed class RunSeedResumeDeterminismTests
    {
        private const int Seed = 20260905;
        private const int Turns = 8;

        // ── 원리: 커서 되감기 ─────────────────────────────────────────────

        [Test]
        public void CountingRandomFastForwardLandsOnTheSameSpotAcrossCallKinds()
        {
            // 호출 종류가 섞여도(Next/Next(max)/Next(min,max)/NextDouble) 커서는 내부 표본 단위라 되감기가 같은 자리에 선다.
            var live = new CountingRandom(7);
            live.Next();
            live.Next(10);
            live.Next(-3, 4);
            live.NextDouble();
            var cursor = live.Consumed;
            var expected = Enumerable.Range(0, 6).Select(_ => live.Next(1000)).ToArray();

            var resumed = new CountingRandom(7);
            resumed.FastForward(cursor);
            var actual = Enumerable.Range(0, 6).Select(_ => resumed.Next(1000)).ToArray();

            Assert.That(actual, Is.EqualTo(expected), "되감은 난수가 무중단 난수의 다음 값과 다르다.");
            Assert.That(resumed.Consumed, Is.EqualTo(live.Consumed), "되감기 뒤 커서는 무중단 커서와 같아야 다음 저장이 맞다.");
        }

        // ── 브롤러: 패턴 선택(4)·피해 변주(4')·이동덱(8)·행동덱(9) ─────────

        [TestCase(2)]
        [TestCase(4)]
        public void ResumingAtTurnKReproducesTheUninterruptedRun(int resumeBeforeTurn)
        {
            var uninterrupted = Trace(Brawler, Seed, Turns);
            var resumed = Trace(Brawler, Seed, Turns, resumeBeforeTurn);

            Assert.That(resumed, Is.EqualTo(uninterrupted),
                $"턴 {resumeBeforeTurn} 직전에 저장·재개한 판이 무중단 판과 갈렸다 — 어떤 난수 스트림의 커서가 되감기지 않는다.");
        }

        [Test]
        public void ResumingRightAfterAReshuffleReproducesTheUninterruptedRun()
        {
            var reshuffleTurn = FindFirstReshuffleTurn(Brawler, Seed, Turns);
            var uninterrupted = Trace(Brawler, Seed, Turns);
            var resumed = Trace(Brawler, Seed, Turns, reshuffleTurn + 1);

            Assert.That(resumed, Is.EqualTo(uninterrupted),
                $"재편성 직후(턴 {reshuffleTurn + 1} 직전) 재개한 판이 갈렸다 — 셔플 커서가 재편성 소비를 안 센다.");
        }

        [Test]
        public void ResumingTwiceReproducesTheUninterruptedRun()
        {
            var uninterrupted = Trace(Brawler, Seed, Turns);
            var resumed = Trace(Brawler, Seed, Turns, 2, 5);

            Assert.That(resumed, Is.EqualTo(uninterrupted),
                "두 번 저장·재개한 판이 갈렸다 — 두 번째 스냅샷의 커서에 생성자 소비가 섞였거나 되감기가 누적되지 않는다.");
        }

        [Test]
        public void RestoreLeavesEveryCursorExactlyWhereTheSnapshotPutIt()
        {
            // 생성자(개시 셔플·첫 계획)와 복원 자체(의도 갱신)가 스트림을 쓰면 커서가 저장값보다 앞서 두 번째 재개부터 어긋난다.
            var source = Brawler().WithRunSeed(Seed).Build();
            PlayTurn(source);
            PlayTurn(source);
            var snapshot = source.CreateSuspendSnapshot();
            var saved = snapshot.RngCursors;
            Assert.That(saved.AttackPattern + saved.DamageJitter + saved.ActionShuffle, Is.GreaterThan(0), "전제: 두 턴이면 스트림이 실제로 소비된다.");

            var resumed = Brawler().WithRunSeed(Seed).WithoutOpeningHands().Build();
            resumed.RestoreFromSuspend(snapshot);
            var after = resumed.CaptureRngCursors();

            Assert.That(Flatten(after), Is.EqualTo(Flatten(saved)),
                "복원 직후 커서가 저장값과 다르다 — 복원 경로가 스트림을 소비하거나 생성자 소비가 새 인스턴스에 남았다.");
        }

        // ── 형상 정예: 전투 판정 스트림(5, 취약 부위 재선정) ─────────────────

        [Test]
        public void WeakSpotReselectionAfterResumeMatchesTheUninterruptedRun()
        {
            var uninterrupted = Trace(TriangleElite, Seed, Turns);
            var resumed = Trace(TriangleElite, Seed, Turns, 3);

            Assert.That(uninterrupted.Distinct().Count(), Is.GreaterThan(1), "전제: 취약 부위가 턴마다 실제로 다시 굴려진다.");
            Assert.That(resumed, Is.EqualTo(uninterrupted), "재개 뒤 취약 부위 재선정이 갈렸다 — 판정 스트림(pushRng) 커서가 되감기지 않는다.");
        }

        // ── 보스 기물: 스트림 6(링 회전각) ─────────────────────────────────

        [Test]
        public void BossPropVolleyAfterResumeLandsOnTheSameCells()
        {
            // 볼리 주기 3: 1턴·4턴·7턴에 깔린다. 첫 볼리 뒤(3턴 직전)에 재개해 두 번째 볼리의 자리를 비교한다.
            var uninterrupted = Trace(IronScrapBoss, Seed, Turns);
            var resumed = Trace(IronScrapBoss, Seed, Turns, 3);

            Assert.That(uninterrupted.Last(), Does.Contain("prop@"), "전제: 두 번째 볼리가 실제로 깔렸다.");
            Assert.That(resumed, Is.EqualTo(uninterrupted), "재개 뒤 기물 볼리 자리가 갈렸다 — 보스 기물 스트림 커서가 되감기지 않는다.");
        }

        // ── 보상: 스트림 7(컨트롤러 소유, 봉투 RewardCursor) ─────────────────

        [Test]
        public void RewardOffersAfterResumeMatchTheUninterruptedController()
        {
            var uninterrupted = CollectRewardOffers(resumeAfterTwoOffers: false);
            var resumed = CollectRewardOffers(resumeAfterTwoOffers: true);

            Assert.That(resumed, Is.EqualTo(uninterrupted), "재개한 컨트롤러의 보상 후보가 갈렸다 — 봉투의 보상 커서가 보상 난수에 꽂히지 않는다.");
        }

        // ── 픽스처 ────────────────────────────────────────────────────────

        /// <summary>같은 가중치 두 패턴 + 피해 변주 → 선택·굴림이 둘 다 관찰된다. 덱은 섞어서 재편성이 실제로 일어나게 한다.</summary>
        private static CombatStateFixture.Builder Brawler()
        {
            const string definitionId = "resume-test-brawler";
            var catalog = new MonsterCatalogDefinition(
                "resume-determinism-test-catalog",
                "Resume Determinism Test Catalog",
                new[]
                {
                    new MonsterCatalogEntry(
                        definitionId, "굴림꾼", "test-melee", "B001",
                        detectionRange: 8, movePerTurn: 0, hp: 30, attackSpeed: 1,
                        attackPatterns: new[]
                        {
                            new MonsterAttackPattern("RD01", "왼손", range: 1, areaRadius: 0, damage: 3, weight: 1, damageJitter: 2),
                            new MonsterAttackPattern("RD02", "오른손", range: 1, areaRadius: 0, damage: 3, weight: 1, damageJitter: 2),
                        })
                });
            return CombatStateFixture.Arena(6)
                .WithMonsters(new MonsterConfig("resume-monster", new HexCoord(1, 0), 30, definitionId: definitionId))
                .WithMonsterCatalog(catalog)
                // 이동 손패 3: 이동덱도 8턴 안에 재편성돼 이동 셔플 스트림(8)이 관찰된다.
                .WithConfig(TestCombatConfigs.Standard(playerMaxHp: 200, enemyMaxHp: 30, movementHandSize: 3))
                .WithShuffledDecks();
        }

        /// <summary>삼각형 footprint 정예 — 몬스터 행동마다 취약 부위를 세 칸 중에서 다시 굴린다(pushRng).</summary>
        private static CombatStateFixture.Builder TriangleElite()
        {
            const string definitionId = "resume-test-elite";
            var catalog = new MonsterCatalogDefinition(
                "resume-elite-catalog",
                "Resume Elite Catalog",
                new[]
                {
                    new MonsterCatalogEntry(
                        definitionId, "테스트 정예", "test-melee", "B001",
                        detectionRange: 8, movePerTurn: 0, hp: 40, attackSpeed: 1,
                        attackPatterns: new[] { new MonsterAttackPattern("RE01", "기본", 1, 0, 3) },
                        footprintShape: MonsterFootprintShape.Triangle)
                });
            return CombatStateFixture.Arena(6)
                .WithPlayerAt(new HexCoord(-3, 0))
                .WithMonsters(new MonsterConfig("resume-elite", new HexCoord(2, 0), 40, definitionId: definitionId, spawnRole: "elite"))
                .WithMonsterCatalog(catalog)
                .WithConfig(TestCombatConfigs.Standard(playerMaxHp: 200, enemyMaxHp: 40, enemyChaseRange: 6));
        }

        /// <summary>철조각 보스(볼리 주기 3) — 볼리마다 링 회전각을 굴린다(bossPropRng).</summary>
        private static CombatStateFixture.Builder IronScrapBoss()
        {
            const string bossDefinitionId = "M002";
            const string propDefinitionId = "M901";
            var monsterCatalog = new MonsterCatalogDefinition(
                "resume-boss-catalog",
                "Resume Boss Catalog",
                new[]
                {
                    new MonsterCatalogEntry(bossDefinitionId, "테스트 보스", "test-melee", "B001", detectionRange: 8, movePerTurn: 1, hp: 40,
                        attackPatterns: new[] { new MonsterAttackPattern("A100", "근접", 1, 0, 2) }),
                    new MonsterCatalogEntry(propDefinitionId, "철조각", "boss-prop", "B001", detectionRange: 0, movePerTurn: 1, hp: 10,
                        attackPatterns: new[] { new MonsterAttackPattern("A900", "없음", 1, 0, 0) })
                });
            var mechanicParams = string.Join(";", new[]
            {
                $"{IronScrapMechanicParams.PropId}={propDefinitionId}",
                $"{IronScrapMechanicParams.StackPerProp}=15",
                $"{IronScrapMechanicParams.MaturityTurns}=2",
                $"{IronScrapMechanicParams.VolleyIntervalTurns}=3",
                $"{IronScrapMechanicParams.VolleyByPhase}=3|3|3",
                $"{IronScrapMechanicParams.RingRadius}=2",
                $"{IronScrapMechanicParams.MinSpacing}=2",
                $"{IronScrapMechanicParams.MaxAlive}=8",
                $"{IronScrapMechanicParams.BlastRadius}=0",
                $"{IronScrapMechanicParams.BlastDamage}=0"
            });
            var bossCatalog = BossCatalogCsvConverter.Convert(new BossCatalogCsvSource(
                BossCsv.ProfilesHeader + "\n" + $"{bossDefinitionId},테스트 보스,AbsorbedStacks,iron-scrap,{mechanicParams},,,music.boss.test,\n",
                BossCsv.PhasesHeader + "\n" + $"{bossDefinitionId},1,0,0,0,0,1,0,,\n" + $"{bossDefinitionId},2,1000,0,0,0,1,0,,\n" + $"{bossDefinitionId},3,2000,0,0,0,1,0,,\n",
                "resume-iron-scrap", "Resume Iron Scrap"));
            var map = new HexMapData(HexArea.CellsWithin(new HexCoord(2, 0), 4)
                .Select(coord => new HexCellData(coord, "tile", "street", 1, true, false))
                .ToList());
            return CombatStateFixture.OnMap(map)
                .WithPlayerAt(new HexCoord(0, 0))
                .WithMonsters(new MonsterConfig("boss-01", new HexCoord(4, 0), 40, definitionId: bossDefinitionId, spawnRole: MonsterSpawnRoles.Boss))
                .WithMonsterCatalog(monsterCatalog)
                .WithBossCatalog(bossCatalog)
                .WithConfig(TestCombatConfigs.Standard(playerMaxHp: 200, enemyMaxHp: 40, enemyChaseRange: 5, playerVisionRange: 8));
        }

        // ── 실행·기록 ─────────────────────────────────────────────────────

        /// <summary>
        /// 같은 입력 대본(카드를 내지 않고 턴만 넘김)으로 <paramref name="turns"/>턴을 돌리며 턴 끝 스냅샷을 기록한다.
        /// <paramref name="resumeBeforeTurns"/>에 든 턴은 그 턴 입력 <b>직전</b>에 저장 → 새 상태 → 복원한다.
        /// </summary>
        private static List<string> Trace(Func<CombatStateFixture.Builder> builder, int seed, int turns, params int[] resumeBeforeTurns)
        {
            var state = builder().WithRunSeed(seed).Build();
            var trace = new List<string>();
            for (var turn = 1; turn <= turns; turn++)
            {
                if (resumeBeforeTurns.Contains(turn))
                {
                    var snapshot = state.CreateSuspendSnapshot();
                    state = builder().WithRunSeed(seed).WithoutOpeningHands().Build();
                    state.RestoreFromSuspend(snapshot);
                }

                Assert.That(state.IsTerminal, Is.False, "전제: 전투가 도중에 끝나지 않는다.");
                PlayTurn(state);
                trace.Add(Describe(state.CreateSuspendSnapshot()));
            }

            return trace;
        }

        private static void PlayTurn(CombatState state)
        {
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();
        }

        private static string Describe(CombatSuspendData data)
        {
            var decks = data.Player.Decks;
            var monsters = data.Monsters
                .OrderBy(monster => monster.SpawnRole == MonsterSpawnRoles.BossProp ? 1 : 0)
                .ThenBy(monster => monster.Id, StringComparer.Ordinal)
                .ThenBy(monster => monster.Q).ThenBy(monster => monster.R)
                .Select(monster => monster.SpawnRole == MonsterSpawnRoles.BossProp
                    // 기물 id는 재개 뒤 카운터가 0부터 다시 세므로(사용 중 id만 건너뜀) 난수와 무관하게 갈릴 수 있다 — 자리만 본다.
                    ? $"prop@{monster.Q},{monster.R}"
                    : $"{monster.Id}@{monster.Q},{monster.R} p={monster.AttackPatternIndex} r={monster.AttackDamageRollOffset} hp={monster.Hp}"
                      + (monster.HasWeakSpot ? $" ws={monster.WeakSpotOffsetQ},{monster.WeakSpotOffsetR}" : string.Empty));
            return $"T{data.OverallTurn} hp={data.Player.Vitals.Hp}"
                + $" mh=[{Ids(decks.MovementZones.Hand)}] md=[{Ids(decks.MovementZones.DrawPile)}]"
                + $" ah=[{Ids(decks.ActionZones.Hand)}] ad=[{Ids(decks.ActionZones.DrawPile)}]"
                + " " + string.Join(" ", monsters);
        }

        private static string Ids(IEnumerable<PlayerCardInstanceSaveData> cards) =>
            string.Join(",", cards.Select(card => card.InstanceId));

        /// <summary>행동덱 더미가 턴 사이에 <b>늘어난</b> 첫 턴 = 그 턴에 버림 더미가 재편성됐다.</summary>
        private static int FindFirstReshuffleTurn(Func<CombatStateFixture.Builder> builder, int seed, int turns)
        {
            var state = builder().WithRunSeed(seed).Build();
            var previousDraw = state.ActionDeck.DrawCount;
            for (var turn = 1; turn <= turns; turn++)
            {
                PlayTurn(state);
                if (state.ActionDeck.DrawCount > previousDraw)
                {
                    return turn;
                }

                previousDraw = state.ActionDeck.DrawCount;
            }

            Assert.Fail($"전제: {turns}턴 안에 행동덱 재편성이 한 번은 일어나야 한다.");
            return -1;
        }

        private static int[] Flatten(RngCursorsSaveData cursors) => new[]
        {
            cursors.Judgement, cursors.AttackPattern, cursors.DamageJitter, cursors.BossProps, cursors.MovementShuffle, cursors.ActionShuffle
        };

        /// <summary>
        /// 보상 후보 두 벌을 뽑은 뒤(무중단이면 그대로 두 벌 더, 재개면 봉투를 거쳐 새 컨트롤러에서 두 벌) 네 벌을 돌려준다.
        /// </summary>
        private static List<string> CollectRewardOffers(bool resumeAfterTwoOffers)
        {
            const int seed = 555;
            var candidates = Enumerable.Range(0, 20)
                .Select(i => new CardRewardCandidate($"reward-{i:D2}", i % 3 == 0 ? CardRarity.Epic : CardRarity.Rare))
                .ToList();
            string Offer(IRewardRandom random) =>
                string.Join(",", CardRewardRoller.SelectRewardCardIds(candidates, CardRewardRarityWeights.Default, eliteOnly: false, random));

            var offers = new List<string>();
            var hosts = new List<GameObject>();
            try
            {
                var first = CreateController(hosts, seed, restore: null);
                offers.Add(Offer(first.ActiveRewardRandom));
                offers.Add(Offer(first.ActiveRewardRandom));

                var next = first;
                if (resumeAfterTwoOffers)
                {
                    var envelope = CombatSuspendEnvelope.Create(
                        "test", first.State.OverallTurnNumber, first.State.CreateSuspendSnapshot(),
                        hasPlacementSeed: true, placementSeed: seed, rewardCursor: first.RewardRandomCursor);
                    next = CreateController(hosts, seed, envelope);
                    Assert.That(next.CombatSuspendRestoreApplied, Is.True, "전제: 재개 부팅이 실제로 복원을 적용했다.");
                }

                offers.Add(Offer(next.ActiveRewardRandom));
                offers.Add(Offer(next.ActiveRewardRandom));
                return offers;
            }
            finally
            {
                foreach (var host in hosts)
                {
                    UnityObject.DestroyImmediate(host);
                }
            }
        }

        private static MapCombatController CreateController(List<GameObject> hosts, int seed, CombatSuspendEnvelope restore)
        {
            var host = new GameObject("resume reward test");
            hosts.Add(host);
            var controller = host.AddComponent<MapCombatController>();
            controller.SetPlacementRandomization(enabled: false, hasSeed: true, seed: seed, stageId: "test");
            if (restore != null)
            {
                controller.SetCombatSuspendRestore(restore);
            }

            controller.ConfigureMapForTests(TestMaps.Line(3));
            controller.InitializeIntegration();
            return controller;
        }
    }
}

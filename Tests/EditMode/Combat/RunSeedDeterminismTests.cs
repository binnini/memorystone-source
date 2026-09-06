using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using UnityObject = UnityEngine.Object;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 시드 재현성 트랙(docs/prompts/seed-determinism-handoff.md)의 게이트. 단언은 「같은 순서로
    /// 굴렸는가」가 아니라 <b>관찰 가능한 결과</b>(뽑힌 카드·배치·판정)가 같은가로 쓴다.
    /// </summary>
    public sealed class RunSeedDeterminismTests
    {
        // ── P1: 런 시드 정본화 ──────────────────────────────────────────────

        [Test]
        public void StreamTableHasNoCollisionAmongNewCombatStreams()
        {
            // 새 축(4~9)은 서로도, 기존 배치 축(0~3)과도 겹치면 안 된다. 3의 공유(서비스/체력 변주)는
            // 착수 전부터 있던 것이라 표에 그대로 적혀 있고 여기서는 새 축만 잰다.
            var newStreams = new[]
            {
                RunSeedStreams.MonsterAttackPattern, RunSeedStreams.CombatJudgement, RunSeedStreams.BossProps,
                RunSeedStreams.Rewards, RunSeedStreams.MovementDeckShuffle, RunSeedStreams.ActionDeckShuffle,
            };
            var legacy = new[] { RunSeedStreams.MonsterPlacement, RunSeedStreams.Traps, RunSeedStreams.Chests, RunSeedStreams.Services };

            Assert.That(newStreams.Distinct().Count(), Is.EqualTo(newStreams.Length), "새 스트림 번호가 서로 겹친다.");
            Assert.That(newStreams.Intersect(legacy), Is.Empty, "새 스트림 번호가 배치 축과 겹친다.");
        }

        [Test]
        public void DerivedStreamsFromOneRunSeedProduceDifferentSequences()
        {
            const int runSeed = 20260905;
            var first = new System.Random(RunSeedStreams.Derive(runSeed, RunSeedStreams.MovementDeckShuffle));
            var second = new System.Random(RunSeedStreams.Derive(runSeed, RunSeedStreams.ActionDeckShuffle));
            var a = Enumerable.Range(0, 8).Select(_ => first.Next(1000)).ToArray();
            var b = Enumerable.Range(0, 8).Select(_ => second.Next(1000)).ToArray();

            Assert.That(a, Is.Not.EqualTo(b), "이동덱·행동덱 스트림이 같은 시퀀스를 낸다 — 한쪽 소비가 다른 쪽을 민다.");
        }

        [Test]
        public void RunSeedIsReportedEvenWhenPlacementRandomizationIsOff()
        {
            var host = new GameObject("run seed off-randomization test");
            try
            {
                var controller = host.AddComponent<MapCombatController>();
                controller.UseDemoCardCatalogForTests();
                controller.SetPlacementRandomization(enabled: false, hasSeed: true, seed: 4242, stageId: "test");

                Assert.That(controller.TryGetPlacementSeed(out _), Is.False, "랜덤화가 꺼졌으면 배치 시드는 없어야 한다(맵 뷰 게이트).");
                Assert.That(controller.TryGetRunSeed(out var runSeed), Is.True, "런 시드는 랜덤화와 무관하게 세이브 봉투로 나가야 한다.");
                Assert.That(runSeed, Is.EqualTo(4242));
            }
            finally
            {
                UnityObject.DestroyImmediate(host);
            }
        }

        // ── P2: 덱 셔플 ────────────────────────────────────────────────────

        [Test]
        public void SameRunSeedDrawsTheSameCardsInTheSameOrderAcrossTurns()
        {
            var first = CollectDrawnCardSequence(runSeed: 1234, turns: 6);
            var second = CollectDrawnCardSequence(runSeed: 1234, turns: 6);

            Assert.That(first, Is.EqualTo(second),
                "같은 시드로 두 판을 돌렸는데 뽑힌 카드 순서가 다르다 — 덱 셔플이 런 시드를 안 탄다.");
        }

        [Test]
        public void DifferentRunSeedsDrawDifferentCardOrders()
        {
            // 「셔플이 아무것도 안 해서 항상 같다」는 퇴화를 막는 반대편 단언.
            var first = CollectDrawnCardSequence(runSeed: 1234, turns: 6);
            var second = CollectDrawnCardSequence(runSeed: 987654, turns: 6);

            Assert.That(first, Is.Not.EqualTo(second), "다른 시드인데 카드 순서가 같다 — 셔플이 시드를 무시하거나 아예 안 섞는다.");
        }

        /// <summary>
        /// 관찰 가능한 결과 = 턴마다 손에 들어온 카드(id 순서). 몬스터 없는 아레나라 전투가
        /// 도중에 끝나지 않고, 손패가 덱보다 작아 재셔플이 실제로 일어난다.
        /// </summary>
        private static List<string> CollectDrawnCardSequence(int runSeed, int turns)
        {
            var state = CombatStateFixture.Arena(3).WithRunSeed(runSeed).WithShuffledDecks().Build();
            var sequence = new List<string>();
            for (var i = 0; i < turns; i++)
            {
                Assert.That(state.IsTerminal, Is.False, "전제: 전투가 도중에 끝나지 않는다(몬스터 없음).");
                sequence.Add($"T{state.OverallTurnNumber}:" + string.Join(",",
                    state.MovementDeck.Hand.Select(card => card.Id).Concat(state.ActionDeck.Hand.Select(card => card.Id))));
                Assert.That(state.EndAction(), Is.True);
                state.ResolveMonsterMovement();
                Assert.That(state.EndAction(), Is.True);
                state.ResolveMonsterAction();
            }

            return sequence;
        }

        // ── P3: 보상 계열 ──────────────────────────────────────────────────

        [Test]
        public void ControllerFeedsTheRewardFlowASeedDerivedRandomWhenARunSeedExists()
        {
            var host = new GameObject("run seed reward wiring test");
            try
            {
                var controller = host.AddComponent<MapCombatController>();
                controller.UseDemoCardCatalogForTests();
                controller.SetPlacementRandomization(enabled: false, hasSeed: true, seed: 555, stageId: "test");
                controller.ConfigureMapForTests(TestMaps.Line(3));
                controller.InitializeIntegration();

                Assert.That(controller.State.RunSeed, Is.EqualTo(555), "런 시드가 전투 상태까지 내려가야 한다.");
                Assert.That(controller.ActiveRewardRandom, Is.InstanceOf<SeededRewardRandom>(),
                    "런 시드가 있는데 보상 흐름이 Unity 전역 난수를 쓴다 — 연출이 굴리면 보상이 밀린다.");
                Assert.That(((SeededRewardRandom)controller.ActiveRewardRandom).Seed,
                    Is.EqualTo(RunSeedStreams.Derive(555, RunSeedStreams.Rewards)), "보상 난수는 스트림 7에서 파생돼야 한다.");
            }
            finally
            {
                UnityObject.DestroyImmediate(host);
            }
        }

        [Test]
        public void SameRewardSeedOffersTheSameRewardCards()
        {
            var candidates = Enumerable.Range(0, 20)
                .Select(i => new CardRewardCandidate($"reward-{i:D2}", i % 3 == 0 ? CardRarity.Epic : CardRarity.Rare))
                .ToList();

            var first = CardRewardRoller.SelectRewardCardIds(candidates, CardRewardRarityWeights.Default, eliteOnly: false, new SeededRewardRandom(31));
            var second = CardRewardRoller.SelectRewardCardIds(candidates, CardRewardRarityWeights.Default, eliteOnly: false, new SeededRewardRandom(31));
            var other = CardRewardRoller.SelectRewardCardIds(candidates, CardRewardRarityWeights.Default, eliteOnly: false, new SeededRewardRandom(32));

            Assert.That(first, Is.EqualTo(second), "같은 보상 시드인데 제시된 카드가 다르다.");
            Assert.That(first, Is.Not.EqualTo(other), "다른 시드인데 같은 카드가 나왔다 — 난수가 시드를 안 읽는다.");
        }

        // ── P4: 전투 판정·몬스터 패턴·인스턴스 id ──────────────────────────

        [Test]
        public void SameRunSeedAndSameInputsProduceTheSameCombatOutcome()
        {
            var first = CollectCombatTrace(runSeed: 4242, turns: 8);
            var second = CollectCombatTrace(runSeed: 4242, turns: 8);

            Assert.That(first, Is.EqualTo(second),
                "같은 시드·같은 입력인데 몬스터 패턴 선택 또는 피해가 달랐다 — 몬스터 AI 난수가 런 시드를 안 탄다.");
        }

        [Test]
        public void DifferentRunSeedsProduceDifferentCombatOutcomes()
        {
            var first = CollectCombatTrace(runSeed: 4242, turns: 8);
            var second = CollectCombatTrace(runSeed: 777, turns: 8);

            Assert.That(first, Is.Not.EqualTo(second), "다른 시드인데 전투 결과가 같다 — 패턴 가중치·피해 변주 난수가 시드를 안 읽는다.");
        }

        [Test]
        public void RuntimeCardInstanceIdsAreDeterministicAndUnique()
        {
            var first = CollectRewardCardInstanceIds(runSeed: 11);
            var second = CollectRewardCardInstanceIds(runSeed: 11);

            Assert.That(first, Is.EqualTo(second), "같은 판인데 런타임 카드 id가 달랐다 — Guid가 남아 있다(봉인 순위가 이 id를 섞는다).");
            Assert.That(first.Distinct().Count(), Is.EqualTo(first.Count), "같은 카드를 두 번 받았는데 id가 겹친다.");
        }

        [Test]
        public void RuntimeCardInstanceIdsSkipIdsAlreadyOwnedByTheDeck()
        {
            // 세이브 왕복 뒤 카운터는 0부터 다시 시작한다 — 저장돼 있던 id를 다시 발급하면 두 장이 한 id를 나눠 갖는다.
            var probe = CollectRewardCardInstanceIds(runSeed: 11);
            var state = CombatStateFixture.Arena(3).WithRunSeed(11).Build();
            var cardId = probe[0].Split('.').Reverse().Skip(1).First();
            var existing = state.PlayerDeck.AddCard(state.CardCatalog, cardId, probe[0]);
            var restored = CombatStateFixture.Arena(3).WithRunSeed(11).WithPlayerDeck(existing).Build();

            Assert.That(restored.TryAddRewardCardToCurrentHand(cardId, out _), Is.True);
            var ids = restored.PlayerDeck.MovementCards.Concat(restored.PlayerDeck.ActionCards).Select(card => card.InstanceId).ToList();
            Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Count), $"복원된 덱의 id와 새 발급 id가 겹쳤다: {string.Join(", ", ids)}");
        }

        private static List<string> CollectRewardCardInstanceIds(int runSeed)
        {
            var state = CombatStateFixture.Arena(3).WithRunSeed(runSeed).Build();
            var cardId = state.CardCatalog.Entries.First(entry => entry.DeckType == CardCategory.Action && entry.IncludeInGameplayDecks).Id;
            var before = new HashSet<string>(state.PlayerDeck.ActionCards.Select(card => card.InstanceId));
            Assert.That(state.TryAddRewardCardToCurrentHand(cardId, out var reason), Is.True, reason);
            Assert.That(state.TryAddRewardCardToCurrentHand(cardId, out reason), Is.True, reason);
            return state.PlayerDeck.ActionCards.Select(card => card.InstanceId).Where(id => !before.Contains(id)).ToList();
        }

        /// <summary>
        /// 관찰 가능한 결과 = 턴마다 (몬스터가 고른 패턴, 플레이어 체력). 두 패턴이 같은 가중치라
        /// 선택이 실제로 굴려지고, 피해 변주(jitter)가 있어 피해 굴림도 관찰된다. 플레이어는
        /// 아무 카드도 내지 않고 턴만 넘긴다(같은 입력).
        /// </summary>
        private static List<string> CollectCombatTrace(int runSeed, int turns)
        {
            const string definitionId = "seed-test-brawler";
            var catalog = new MonsterCatalogDefinition(
                "seed-determinism-test-catalog",
                "Seed Determinism Test Catalog",
                new[]
                {
                    new MonsterCatalogEntry(
                        definitionId, "굴림꾼", "test-melee", "B001",
                        detectionRange: 8, movePerTurn: 0, hp: 30, attackSpeed: 1,
                        attackPatterns: new[]
                        {
                            new MonsterAttackPattern("SD01", "왼손", range: 1, areaRadius: 0, damage: 3, weight: 1, damageJitter: 2),
                            new MonsterAttackPattern("SD02", "오른손", range: 1, areaRadius: 0, damage: 3, weight: 1, damageJitter: 2),
                        })
                });
            var state = CombatStateFixture.Arena(6)
                .WithMonsters(new MonsterConfig("seed-monster", new HexCoord(1, 0), 30, definitionId: definitionId))
                .WithMonsterCatalog(catalog)
                .WithConfig(TestCombatConfigs.Standard(playerMaxHp: 200, enemyMaxHp: 30))
                .WithRunSeed(runSeed)
                .Build();

            var trace = new List<string>();
            for (var i = 0; i < turns; i++)
            {
                Assert.That(state.IsTerminal, Is.False, "전제: 전투가 도중에 끝나지 않는다(플레이어 체력 200).");
                trace.Add($"T{state.OverallTurnNumber}:pattern={state.Monsters[0].AttackPatternIndex},hp={state.Player.Hp}");
                Assert.That(state.EndAction(), Is.True);
                state.ResolveMonsterMovement();
                Assert.That(state.EndAction(), Is.True);
                state.ResolveMonsterAction();
            }

            return trace;
        }

        [Test]
        public void RequestedRunSeedIsConsumedExactlyOnce()
        {
            RunSeedRequest.Clear();
            RunSeedRequest.Set(77);

            Assert.That(RunSeedRequest.TryConsume(out var seed), Is.True);
            Assert.That(seed, Is.EqualTo(77));
            Assert.That(RunSeedRequest.TryConsume(out _), Is.False, "한 번 꺼낸 예약은 비워져야 다음 새 진입이 다시 무작위가 된다.");
        }
    }
}

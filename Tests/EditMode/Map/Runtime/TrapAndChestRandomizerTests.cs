using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SeoulPlayup.Map.Runtime.Tests.EditMode
{
    /// <summary>
    /// P2 함정 풀 추첨·상자 셔플·bans 순수 테스트(placement-randomization-plan §5·§8).
    /// PlacementRandomizerTests와 같은 합성 픽스처 문법 — 씬·에셋 로드 없음.
    /// </summary>
    public sealed class TrapAndChestRandomizerTests
    {
        private static StageRandomizationProfile MakeProfile(
            IReadOnlyList<StageRandomizationPoolEntry> pools,
            int trapBudget,
            int tolerancePct = 50,
            int rerollLimit = 30,
            IReadOnlyList<PlacementBanRule> bans = null,
            int trapMinDistance = 0)
        {
            return new StageRandomizationProfile(
                "stage-test", true,
                threatBudget: 10, budgetTolerancePct: tolerancePct, safeRadius: 0, rerollLimit: rerollLimit,
                densityRadius: 2, densityCap: 99, zoneEarlyMaxBfs: 5, zoneMidMaxBfs: 10,
                pools: pools, trapThreatBudget: trapBudget, chestMinDistance: -1, bans: bans,
                trapMinDistance: trapMinDistance);
        }

        private static StageRandomizationPoolEntry TrapEntry(
            string group, string presetId, int cost, int maxCount = 99, int weight = 10, PlacementZone zone = PlacementZone.Any)
        {
            return new StageRandomizationPoolEntry(group, "trapPreset", presetId, cost, maxCount, "", weight, zone, "");
        }

        private static IReadOnlyDictionary<string, TrapPresetTraits> Traits(params (string PresetId, string Kind, int Radius)[] presets)
        {
            return presets.ToDictionary(
                preset => preset.PresetId,
                preset => new TrapPresetTraits(preset.PresetId, new[] { preset.Kind }, preset.Radius),
                StringComparer.Ordinal);
        }

        [Test]
        public void TrapSameSeedProducesIdenticalAssignments()
        {
            var slots = new[]
            {
                new TrapSlotCandidate("t1", new HexCoord(0, 0), "g", true),
                new TrapSlotCandidate("t2", new HexCoord(3, 0), "g", true),
                new TrapSlotCandidate("t3", new HexCoord(6, 0), "g", false),
            };
            var profile = MakeProfile(new[] { TrapEntry("g", "poison", 2), TrapEntry("g", "snare", 1) }, trapBudget: 3);
            var traits = Traits(("poison", "Poison", 1), ("snare", "Slow", 0));

            var first = PlacementRandomizer.RandomizeTraps(slots, profile, null, traits, null, seed: 77);
            var second = PlacementRandomizer.RandomizeTraps(slots, profile, null, traits, null, seed: 77);

            Assert.That(first.Success, Is.True, first.FailureReason);
            Assert.That(second.Success, Is.True);
            Assert.That(first.Assignments.Select(a => (a.SlotId, a.PresetId)),
                Is.EqualTo(second.Assignments.Select(a => (a.SlotId, a.PresetId))));
            Assert.That(first.Assignments.Count, Is.EqualTo(2), "그룹 추첨 수 = 저작 점유 수(예비 슬롯은 위치 후보만).");
        }

        [Test]
        public void TrapBudgetIsRespectedAcrossSeeds()
        {
            var slots = Enumerable.Range(0, 6)
                .Select(i => new TrapSlotCandidate($"t{i}", new HexCoord(i * 3, 0), "g", true))
                .ToList();
            var profile = MakeProfile(
                new[] { TrapEntry("g", "cheap", 1), TrapEntry("g", "costly", 3) },
                trapBudget: 10, tolerancePct: 20);
            var traits = Traits(("cheap", "Slow", 0), ("costly", "SpawnMonsters", 0));

            var successes = 0;
            for (var seed = 0; seed < 120; seed++)
            {
                var result = PlacementRandomizer.RandomizeTraps(slots, profile, null, traits, null, seed);
                if (!result.Success)
                {
                    continue;
                }

                successes++;
                var costBy = new Dictionary<string, int> { ["cheap"] = 1, ["costly"] = 3 };
                var total = result.Assignments.Sum(a => costBy[a.PresetId]);
                Assert.That(total, Is.InRange(profile.TrapBudgetMin, profile.TrapBudgetMax));
            }

            Assert.That(successes, Is.GreaterThan(0), "예산 창이 모든 시드를 기각했다 — 검증이 빈 채로 통과하고 있다.");
        }

        [Test]
        public void TrapGroupWithoutPoolFails()
        {
            var slots = new[] { new TrapSlotCandidate("t1", new HexCoord(0, 0), "g-none", true) };
            var profile = MakeProfile(new[] { TrapEntry("other", "poison", 2) }, trapBudget: 2);

            var result = PlacementRandomizer.RandomizeTraps(
                slots, profile, null, Traits(("poison", "Poison", 1)), null, seed: 1);

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureReason, Does.Contain("g-none"));
        }

        [Test]
        public void BanAdjacentSteersAssignmentsAwayFromTaggedMonsters()
        {
            // 점유 1 + 예비 1: elite 인접 칸이 금지되면 모든 시드에서 예비 쪽 위치로만 배정돼야 한다.
            var nearElite = new HexCoord(0, 0);
            var farFromElite = new HexCoord(9, 0);
            var slots = new[]
            {
                new TrapSlotCandidate("near", nearElite, "g", true),
                new TrapSlotCandidate("far", farFromElite, "g", false),
            };
            var profile = MakeProfile(
                new[] { TrapEntry("g", "siren", 1) }, trapBudget: 1, tolerancePct: 100,
                bans: new[] { new PlacementBanRule("SpawnMonsters", "elite", PlacementBanRelation.Adjacent, 1) });
            var traits = Traits(("siren", "SpawnMonsters", 0));
            var monsters = new[] { new PlacementTaggedPoint(new HexCoord(1, 0), new[] { "elite" }) };

            for (var seed = 0; seed < 40; seed++)
            {
                var result = PlacementRandomizer.RandomizeTraps(slots, profile, null, traits, monsters, seed);
                Assert.That(result.Success, Is.True, result.FailureReason);
                Assert.That(result.Assignments.Single().Coord, Is.EqualTo(farFromElite),
                    "bans가 elite 인접 슬롯을 기각하지 못했다.");
            }
        }

        [Test]
        public void BanUnavoidableFailsInsteadOfViolating()
        {
            var slots = new[] { new TrapSlotCandidate("only", new HexCoord(0, 0), "g", true) };
            var profile = MakeProfile(
                new[] { TrapEntry("g", "siren", 1) }, trapBudget: 1, tolerancePct: 100, rerollLimit: 5,
                bans: new[] { new PlacementBanRule("SpawnMonsters", "elite", PlacementBanRelation.Adjacent, 1) });
            var result = PlacementRandomizer.RandomizeTraps(
                slots, profile, null, Traits(("siren", "SpawnMonsters", 0)),
                new[] { new PlacementTaggedPoint(new HexCoord(1, 0), new[] { "elite" }) }, seed: 3);

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureReason, Does.Contain("reroll limit"));
        }

        [Test]
        public void BanOverlapUsesTrapEffectRadiusAndDormantTagMatchesNothing()
        {
            var trap = new[] { new TrapAssignment(new TrapSlotCandidate("t", new HexCoord(0, 0), "g", true), "blind") };
            var traits = Traits(("blind", "VisionDown", 2));
            var bans = new[] { new PlacementBanRule("VisionDown", "stealth", PlacementBanRelation.Overlap, 0) };

            var stealthInside = new[] { new PlacementTaggedPoint(new HexCoord(2, 0), new[] { "stealth" }) };
            Assert.That(PlacementRandomizer.TryFindBanViolation(trap, traits, stealthInside, bans, out _), Is.True,
                "overlap은 함정 효과 반경(2) 안의 표적을 잡아야 한다.");

            var stealthOutside = new[] { new PlacementTaggedPoint(new HexCoord(3, 0), new[] { "stealth" }) };
            Assert.That(PlacementRandomizer.TryFindBanViolation(trap, traits, stealthOutside, bans, out _), Is.False);

            // 휴면 규칙(Q11): 태그가 아무 몬스터에도 없으면 규칙은 조용히 통과한다.
            var untagged = new[] { new PlacementTaggedPoint(new HexCoord(0, 0), new[] { "elite" }) };
            Assert.That(PlacementRandomizer.TryFindBanViolation(trap, traits, untagged, bans, out _), Is.False);
        }

        [Test]
        public void TrapZoneIsALowerBoundLikeMonsters()
        {
            // 2026-09-05 #30: 몬스터는 2026-09-01 #14부터 지대를 하한으로 보는데 함정만 정확히 일치로 남아
            // 두 파일의 의미론이 갈라져 있었다. late 슬롯에 mid 행만 있는 풀이 뽑혀야 하한이다.
            // 프로파일 zone 컷 5/10 → BFS 20은 Late, BFS 3은 Early.
            var lateSlot = new[] { new TrapSlotCandidate("t-late", new HexCoord(0, 0), "g", true) };
            var walk = new Dictionary<HexCoord, int> { { new HexCoord(0, 0), 20 } };
            var midOnlyPool = new[] { TrapEntry("g", "nuisance", 2, zone: PlacementZone.Mid) };
            var profile = MakeProfile(midOnlyPool, trapBudget: 2);
            var traits = Traits(("nuisance", "InjectStatusCard", 0));

            var late = PlacementRandomizer.RandomizeTraps(lateSlot, profile, walk, traits, null, seed: 1);
            Assert.That(late.Success, Is.True, late.FailureReason);
            Assert.That(late.Assignments.Single().PresetId, Is.EqualTo("nuisance"),
                "mid 행은 그 지대부터 뒤로 전부(late 포함) 설 수 있어야 한다.");

            // 반대로 저작 지대보다 이른 슬롯에는 여전히 못 선다.
            var earlyWalk = new Dictionary<HexCoord, int> { { new HexCoord(0, 0), 3 } };
            var early = PlacementRandomizer.RandomizeTraps(lateSlot, profile, earlyWalk, traits, null, seed: 1);
            Assert.That(early.Success, Is.False, "early 슬롯에 mid 행이 섰다 — 하한이 무너졌다.");
        }

        [Test]
        public void TrapMinDistanceMovesTrapsOntoSpareSlots()
        {
            // 점유 2개가 인접(거리 1) + 멀리 예비 1개(Q-A2): 최소 거리 2면 어떤 시드에서도
            // 인접 쌍이 함께 뽑힐 수 없다 — 한쪽은 반드시 예비 위치로 밀려난다.
            var slots = new[]
            {
                new TrapSlotCandidate("adj-a", new HexCoord(0, 0), "g", true),
                new TrapSlotCandidate("adj-b", new HexCoord(1, 0), "g", true),
                new TrapSlotCandidate("spare", new HexCoord(9, 0), "g", false),
            };
            var profile = MakeProfile(
                new[] { TrapEntry("g", "snare", 1) }, trapBudget: 2, tolerancePct: 100, trapMinDistance: 2);
            var traits = Traits(("snare", "Slow", 0));

            var layouts = new HashSet<string>();
            for (var seed = 0; seed < 60; seed++)
            {
                var result = PlacementRandomizer.RandomizeTraps(slots, profile, null, traits, null, seed);
                Assert.That(result.Success, Is.True, $"seed {seed}: {result.FailureReason}");
                Assert.That(result.Assignments.Count, Is.EqualTo(2), $"seed {seed}: 추첨 수 = 점유 수.");
                var coords = result.Assignments.Select(a => a.Coord).ToList();
                Assert.That(coords[0].DistanceTo(coords[1]), Is.GreaterThanOrEqualTo(2), $"seed {seed}");
                layouts.Add(string.Join("|", result.Assignments.Select(a => a.SlotId).OrderBy(id => id)));
            }

            Assert.That(layouts.Count, Is.GreaterThan(1), "위치 변주가 없다 — 예비 슬롯이 뽑히지 않았다.");
        }

        [Test]
        public void TrapMinDistanceCrossesGroupBoundaries()
        {
            // 그룹이 달라도 좌표는 같은 맵이다 — 최소 거리는 그룹 경계를 넘어 판정돼야 한다.
            // g1의 유일 슬롯과 인접한 g2 점유 슬롯은 기각되고, 항상 g2의 예비 쪽이 뽑혀야 한다.
            var slots = new[]
            {
                new TrapSlotCandidate("g1-only", new HexCoord(0, 0), "g1", true),
                new TrapSlotCandidate("g2-near", new HexCoord(1, 0), "g2", true),
                new TrapSlotCandidate("g2-far", new HexCoord(9, 0), "g2", false),
            };
            var profile = MakeProfile(
                new[] { TrapEntry("g1", "snare", 1), TrapEntry("g2", "snare", 1) },
                trapBudget: 2, tolerancePct: 100, trapMinDistance: 2);
            var traits = Traits(("snare", "Slow", 0));

            for (var seed = 0; seed < 40; seed++)
            {
                var result = PlacementRandomizer.RandomizeTraps(slots, profile, null, traits, null, seed);
                Assert.That(result.Success, Is.True, $"seed {seed}: {result.FailureReason}");
                Assert.That(result.Assignments.Select(a => a.SlotId).OrderBy(id => id),
                    Is.EqualTo(new[] { "g1-only", "g2-far" }), $"seed {seed}");
            }
        }

        [Test]
        public void TrapMinDistanceImpossibleFailsInsteadOfViolating()
        {
            var slots = new[]
            {
                new TrapSlotCandidate("a", new HexCoord(0, 0), "g", true),
                new TrapSlotCandidate("b", new HexCoord(1, 0), "g", true),
            };
            var profile = MakeProfile(
                new[] { TrapEntry("g", "snare", 1) }, trapBudget: 2, tolerancePct: 100, rerollLimit: 5,
                trapMinDistance: 2);

            var result = PlacementRandomizer.RandomizeTraps(
                slots, profile, null, Traits(("snare", "Slow", 0)), null, seed: 7);

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureReason, Does.Contain("reroll limit"));
        }

        [Test]
        public void ChestShuffleIsDeterministicAndKeepsCountAndDistance()
        {
            var chestIds = new[] { "chest-b", "chest-a", "chest-c" };
            var candidates = new List<HexCoord>();
            for (var q = 0; q < 10; q++)
            {
                for (var r = 0; r < 10; r++)
                {
                    candidates.Add(new HexCoord(q, r));
                }
            }

            var first = PlacementRandomizer.ShuffleChests(chestIds, candidates, minPairDistance: 4, rerollLimit: 20, seed: 5);
            var second = PlacementRandomizer.ShuffleChests(chestIds, candidates, minPairDistance: 4, rerollLimit: 20, seed: 5);

            Assert.That(first.Success, Is.True, first.FailureReason);
            Assert.That(first.Assignments.Select(a => (a.ObjectId, a.Coord)),
                Is.EqualTo(second.Assignments.Select(a => (a.ObjectId, a.Coord))));
            Assert.That(first.Assignments.Select(a => a.ObjectId),
                Is.EquivalentTo(chestIds), "objectId는 유지된다(개수 고정·위치만 셔플).");
            foreach (var pair in first.Assignments.SelectMany(
                (a, i) => first.Assignments.Skip(i + 1).Select(b => (a, b))))
            {
                Assert.That(pair.a.Coord.DistanceTo(pair.b.Coord), Is.GreaterThanOrEqualTo(4));
            }
        }

        [Test]
        public void ChestShuffleImpossibleConstraintsFail()
        {
            var tooFewCandidates = PlacementRandomizer.ShuffleChests(
                new[] { "a", "b", "c" }, new[] { new HexCoord(0, 0) }, 1, 5, seed: 1);
            Assert.That(tooFewCandidates.Success, Is.False);
            Assert.That(tooFewCandidates.FailureReason, Does.Contain("candidate"));

            var impossibleDistance = PlacementRandomizer.ShuffleChests(
                new[] { "a", "b", "c" },
                new[] { new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(0, 1) },
                minPairDistance: 5, rerollLimit: 5, seed: 1);
            Assert.That(impossibleDistance.Success, Is.False);
            Assert.That(impossibleDistance.FailureReason, Does.Contain("reroll limit"));
        }

        [Test]
        public void CsvParsesTrapPoolOptionalColumnsAndBans()
        {
            var stages = "stageId,enabled,monsterThreatBudget,budgetTolerancePct,safeRadius,rerollLimit,densityRadius,densityCap,zoneEarlyMaxBfs,zoneMidMaxBfs,trapThreatBudget,chestMinDistance,trapMinDistance,eliteMin\n" +
                         "s1,TRUE,8,25,3,10,2,9,5,10,12,4,2,2\n";
            var pools = "stageId,group,entryKind,entryRef,threatCost,maxCount,capGroup,weight,zone,role\n" +
                        "s1,g,monster,M001,1,4,,10,any,\n" +
                        "s1,tg,trapPreset,poison_cloud,2,9,,10,any,\n";
            var bans = "stageId,kindA,kindB,relation,radius\n" +
                       "s1,trap:SpawnMonsters,monster:elite,adjacent,1\n" +
                       "s1,monster:stealth,trap:VisionDown,overlap,0\n";

            Assert.That(StageRandomizationCsv.TryBuildProfile(stages, pools, bans, "s1", out var profile, out var error),
                Is.True, error);
            Assert.That(profile.TrapThreatBudget, Is.EqualTo(12));
            Assert.That(profile.ChestMinDistance, Is.EqualTo(4));
            Assert.That(profile.TrapMinDistance, Is.EqualTo(2));
            Assert.That(profile.EliteMin, Is.EqualTo(2));
            Assert.That(profile.Pools.Count(entry => entry.IsTrapPreset), Is.EqualTo(1));
            Assert.That(profile.Pools.Single(entry => entry.IsTrapPreset).EntryRef, Is.EqualTo("poison_cloud"));
            Assert.That(profile.Bans.Count, Is.EqualTo(2));
            Assert.That(profile.Bans[0].TrapEffectKind, Is.EqualTo("SpawnMonsters"));
            Assert.That(profile.Bans[0].MonsterTag, Is.EqualTo("elite"));
            Assert.That(profile.Bans[1].TrapEffectKind, Is.EqualTo("VisionDown"),
                "kindA/kindB 순서는 자유 — 파서가 방향을 정규화한다.");
            Assert.That(profile.Bans[1].Relation, Is.EqualTo(PlacementBanRelation.Overlap));
        }

        [Test]
        public void CsvWithoutP2ColumnsKeepsP2Disabled()
        {
            var stages = "stageId,enabled,monsterThreatBudget,budgetTolerancePct,safeRadius,rerollLimit,densityRadius,densityCap,zoneEarlyMaxBfs,zoneMidMaxBfs\n" +
                         "s1,TRUE,8,25,3,10,2,9,5,10\n";
            var pools = "stageId,group,entryKind,entryRef,threatCost,maxCount,capGroup,weight,zone,role\n" +
                        "s1,g,monster,M001,1,4,,10,any,\n";

            Assert.That(StageRandomizationCsv.TryBuildProfile(stages, pools, "s1", out var profile, out var error),
                Is.True, error);
            Assert.That(profile.TrapThreatBudget, Is.EqualTo(0), "컬럼 없음 = 함정 랜덤화 비활성(P0~P1 호환).");
            Assert.That(profile.ChestMinDistance, Is.EqualTo(-1), "컬럼 없음 = 상자 셔플 비활성.");
            Assert.That(profile.TrapMinDistance, Is.EqualTo(0), "컬럼 없음 = 함정 최소 거리 규칙 꺼짐.");
            Assert.That(profile.EliteMin, Is.EqualTo(0), "컬럼 없음 = elite 하한 없음.");
            Assert.That(profile.Bans, Is.Empty);
        }

        [Test]
        public void BansParserRejectsBadRelationAndPrefix()
        {
            var badRelation = "stageId,kindA,kindB,relation,radius\ns1,trap:Poison,monster:elite,near,1\n";
            Assert.That(StageRandomizationCsv.TryParseBans(badRelation, "s1", out _, out var relationError), Is.False);
            Assert.That(relationError, Does.Contain("relation"));

            var badPrefix = "stageId,kindA,kindB,relation,radius\ns1,trap:Poison,trap:Stun,adjacent,1\n";
            Assert.That(StageRandomizationCsv.TryParseBans(badPrefix, "s1", out _, out var prefixError), Is.False);
            Assert.That(prefixError, Does.Contain("monster:"));
        }
    }
}

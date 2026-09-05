using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Map.Runtime.Tests.EditMode
{
    /// <summary>
    /// P1 프로파일(placement-randomization-plan §4) 순수 계약: CSV 파서의 실패 모드와
    /// 풀 추첨의 불변식(예산·타입 상한·지대·밀도·결정성·role 정본).
    /// </summary>
    public sealed class StageRandomizationProfileTests
    {
        private const string StagesCsv =
            "stageId,enabled,monsterThreatBudget,budgetTolerancePct,safeRadius,rerollLimit,densityRadius,densityCap,zoneEarlyMaxBfs,zoneMidMaxBfs,designerNote\n" +
            "stage-t,TRUE,8,25,3,40,2,6,5,10,\"note, with comma\"\n";

        private const string PoolsCsv =
            "stageId,group,entryKind,entryRef,threatCost,maxCount,capGroup,weight,zone,role,designerNote\n" +
            "stage-t,mon-a,monster,M001,1,8,,60,any,,\n" +
            "stage-t,mon-a,monster,M006,3,2,,40,late,elite,\n" +
            "stage-t,mon-a,monster,M902,1,2,turret,10,any,,\n" +
            "stage-t,mon-a,monster,M903,2,2,turret,10,any,,\n" +
            "stage-t,mon-a,trapPreset,T001,1,1,,10,any,,\n";

        private static StageRandomizationProfile Parse(string stages = StagesCsv, string pools = PoolsCsv, string stageId = "stage-t")
        {
            Assert.That(StageRandomizationCsv.TryBuildProfile(stages, pools, stageId, out var profile, out var error), Is.True, error);
            return profile;
        }

        private static PlacementSlotCandidate Slot(string id, int q, int r, string group, string monsterId = "")
        {
            return new PlacementSlotCandidate(id, new HexCoord(q, r), group, monsterId);
        }

        private static IReadOnlyDictionary<HexCoord, int> FlatDistances(IEnumerable<PlacementSlotCandidate> slots, int distance)
        {
            return slots.ToDictionary(slot => slot.Coord, _ => distance);
        }

        [Test]
        public void CsvParsesHeaderByNameAndQuotedFields()
        {
            var profile = Parse();

            Assert.That(profile.Enabled, Is.True);
            Assert.That(profile.ThreatBudget, Is.EqualTo(8));
            Assert.That(profile.BudgetMin, Is.EqualTo(6));
            Assert.That(profile.BudgetMax, Is.EqualTo(10));
            Assert.That(profile.SafeRadius, Is.EqualTo(3));
            // trapPreset 행은 P2부터 소비된다 — 몬스터 풀(IsMonster)과는 갈래가 다르다.
            Assert.That(profile.Pools.Count, Is.EqualTo(5));
            Assert.That(profile.Pools.Count(entry => entry.IsMonster), Is.EqualTo(4));
            Assert.That(profile.Pools.Single(entry => entry.IsTrapPreset).EntryRef, Is.EqualTo("T001"));
            Assert.That(profile.Pools.Single(entry => entry.EntryRef == "M006").Role, Is.EqualTo("elite"));
            Assert.That(profile.Pools.Single(entry => entry.EntryRef == "M902").CapKey, Is.EqualTo("turret"));
        }

        [Test]
        public void CsvFailsOnMissingStageAndCapMismatchAndBadZone()
        {
            Assert.That(StageRandomizationCsv.TryBuildProfile(StagesCsv, PoolsCsv, "stage-x", out _, out var missingError), Is.False);
            Assert.That(missingError, Does.Contain("stage-x"));

            var mismatch = PoolsCsv.Replace("stage-t,mon-a,monster,M903,2,2,turret", "stage-t,mon-a,monster,M903,2,3,turret");
            Assert.That(StageRandomizationCsv.TryBuildProfile(StagesCsv, mismatch, "stage-t", out _, out var capError), Is.False);
            Assert.That(capError, Does.Contain("turret"));

            var badZone = PoolsCsv.Replace(",late,elite,", ",soon,elite,");
            Assert.That(StageRandomizationCsv.TryBuildProfile(StagesCsv, badZone, "stage-t", out _, out var zoneError), Is.False);
            Assert.That(zoneError, Does.Contain("zone"));
        }

        [Test]
        public void ProfileDrawIsDeterministicAndRespectsBudget()
        {
            var slots = new List<PlacementSlotCandidate>
            {
                Slot("a-1", 10, 0, "mon-a", "M001"),
                Slot("a-2", 12, 0, "mon-a", "M001"),
                Slot("a-3", 14, 0, "mon-a", "M001"),
                Slot("a-4", 16, 0, "mon-a"),
                Slot("a-5", 18, 0, "mon-a"),
            };
            var profile = Parse();
            var distances = FlatDistances(slots, 12);

            var first = PlacementRandomizer.RandomizeWithProfile(slots, new HexCoord(0, 0), profile, distances, 99);
            var second = PlacementRandomizer.RandomizeWithProfile(slots, new HexCoord(0, 0), profile, distances, 99);
            Assert.That(first.Success, Is.True, first.FailureReason);
            Assert.That(
                second.Assignments.Select(Describe), Is.EqualTo(first.Assignments.Select(Describe)));

            var compositions = new HashSet<string>();
            var poolCost = profile.Pools.ToDictionary(entry => entry.EntryRef, entry => entry.ThreatCost);
            for (var seed = 0; seed < 200; seed++)
            {
                var result = PlacementRandomizer.RandomizeWithProfile(slots, new HexCoord(0, 0), profile, distances, seed);
                Assert.That(result.Success, Is.True, $"seed {seed}: {result.FailureReason}");
                Assert.That(result.Assignments.Count, Is.EqualTo(3), $"seed {seed}");
                var totalThreat = result.Assignments.Sum(a => poolCost[a.MonsterId]);
                Assert.That(totalThreat, Is.InRange(profile.BudgetMin, profile.BudgetMax), $"seed {seed}");
                // 타입 상한: turret 합산 키 = M902+M903 합계 <= 2.
                Assert.That(
                    result.Assignments.Count(a => a.MonsterId == "M902" || a.MonsterId == "M903"),
                    Is.LessThanOrEqualTo(2), $"seed {seed}");
                // role은 풀 행이 정본이다.
                foreach (var assignment in result.Assignments.Where(a => a.MonsterId == "M006"))
                {
                    Assert.That(assignment.Role, Is.EqualTo("elite"), $"seed {seed}");
                }

                compositions.Add(string.Join("|", result.Assignments.Select(a => a.MonsterId).OrderBy(id => id)));
            }

            // 구성 변주가 P1의 존재 이유다 — 스윕에서 여러 구성이 나와야 한다.
            Assert.That(compositions.Count, Is.GreaterThan(1));
        }

        [Test]
        public void LateOnlyEntryNeverLandsOutsideLateZone()
        {
            var slots = new List<PlacementSlotCandidate>
            {
                Slot("a-1", 10, 0, "mon-a", "M001"),
                Slot("a-2", 12, 0, "mon-a", "M001"),
            };
            // 거리 4 = Early(<=5) — M006(late 전용)은 어떤 시드에서도 나오면 안 된다.
            // Early에선 저비용만 뽑히므로 예산 창을 넓혀 예산이 아닌 지대만 시험한다.
            var distances = FlatDistances(slots, 4);
            var profile = Parse(stages: StagesCsv.Replace("stage-t,TRUE,8,25,", "stage-t,TRUE,3,100,"));

            for (var seed = 0; seed < 100; seed++)
            {
                var result = PlacementRandomizer.RandomizeWithProfile(slots, new HexCoord(0, 0), profile, distances, seed);
                Assert.That(result.Success, Is.True, $"seed {seed}: {result.FailureReason}");
                Assert.That(result.Assignments.Any(a => a.MonsterId == "M006"), Is.False, $"seed {seed}");
            }
        }

        [Test]
        public void DensityCapIsNeverExceeded()
        {
            // 서로 2칸 간격의 촘촘한 슬롯 4개 — 반경 2 안에 전부 겹친다. cap 6이면 3코스트
            // 몬스터가 몰리는 조합은 기각되어야 한다.
            var slots = new List<PlacementSlotCandidate>
            {
                Slot("a-1", 10, 0, "mon-a", "M001"),
                Slot("a-2", 12, 0, "mon-a", "M001"),
                Slot("a-3", 10, 2, "mon-a", "M001"),
                Slot("a-4", 12, 2, "mon-a"),
            };
            var profile = Parse();
            var distances = FlatDistances(slots, 12);
            var poolCost = profile.Pools.ToDictionary(entry => entry.EntryRef, entry => entry.ThreatCost);

            var successes = 0;
            for (var seed = 0; seed < 100; seed++)
            {
                var result = PlacementRandomizer.RandomizeWithProfile(slots, new HexCoord(0, 0), profile, distances, seed);
                if (!result.Success)
                {
                    continue;
                }

                successes++;
                foreach (var center in result.Assignments)
                {
                    var nearby = result.Assignments
                        .Where(a => a.Coord.DistanceTo(center.Coord) <= profile.DensityRadius)
                        .Sum(a => poolCost[a.MonsterId]);
                    Assert.That(nearby, Is.LessThanOrEqualTo(profile.DensityCap), $"seed {seed}");
                }
            }

            Assert.That(successes, Is.GreaterThan(0), "밀도 상한이 모든 시드를 기각했다 — 검증이 빈 채로 통과하고 있다.");
        }

        [Test]
        public void EliteMinGuaranteesMinimumEliteCountAcrossSeeds()
        {
            // #18: capGroup은 상한만 걸어 elite 0인 판이 나온다 — eliteMin이 하한을 재롤로 보장한다.
            // 슬롯 5·풀 = M001(w60) + M006(elite, late, w40): 하한 없이는 0인 시드가 반드시 있다.
            var stages =
                "stageId,enabled,monsterThreatBudget,budgetTolerancePct,safeRadius,rerollLimit,densityRadius,densityCap,zoneEarlyMaxBfs,zoneMidMaxBfs,eliteMin\n" +
                "stage-t,TRUE,8,100,3,40,2,99,5,10,2\n";
            var profile = Parse(stages: stages);
            Assert.That(profile.EliteMin, Is.EqualTo(2));

            var slots = new List<PlacementSlotCandidate>
            {
                Slot("a-1", 10, 0, "mon-a", "M001"),
                Slot("a-2", 20, 0, "mon-a", "M001"),
                Slot("a-3", 30, 0, "mon-a", "M001"),
                Slot("a-4", 40, 0, "mon-a", "M001"),
                Slot("a-5", 50, 0, "mon-a", "M001"),
            };
            // 전 슬롯 late(거리 12 > zoneMid 10) — M006이 어느 슬롯에나 설 수 있다.
            var distances = FlatDistances(slots, 12);

            for (var seed = 0; seed < 100; seed++)
            {
                var result = PlacementRandomizer.RandomizeWithProfile(slots, new HexCoord(0, 0), profile, distances, seed);
                Assert.That(result.Success, Is.True, $"seed {seed}: {result.FailureReason}");
                Assert.That(result.Assignments.Count(a => a.Role == "elite"), Is.GreaterThanOrEqualTo(2), $"seed {seed}");
            }
        }

        [Test]
        public void MonsterMinKindsGuaranteesDiversityAcrossSeeds()
        {
            // 2026-08-20 #12: 가중치 추첨만으로는 무거운 가중치 하나(M001 w60)가 판을 독식해
            // "몬스터 종류가 단일하게 나온다"는 체감이 생긴다. capGroup은 종류별 상한만 걸어서
            // 이걸 못 막으므로, eliteMin과 같은 재롤 게이트로 <b>하한</b>을 보장한다.
            var stages =
                "stageId,enabled,monsterThreatBudget,budgetTolerancePct,safeRadius,rerollLimit,densityRadius,densityCap,zoneEarlyMaxBfs,zoneMidMaxBfs,monsterMinKinds\n" +
                "stage-t,TRUE,8,100,3,60,2,99,5,10,2\n";
            var profile = Parse(stages: stages);
            Assert.That(profile.MonsterMinKinds, Is.EqualTo(2));

            var slots = new List<PlacementSlotCandidate>
            {
                Slot("a-1", 10, 0, "mon-a", "M001"),
                Slot("a-2", 20, 0, "mon-a", "M001"),
                Slot("a-3", 30, 0, "mon-a", "M001"),
                Slot("a-4", 40, 0, "mon-a", "M001"),
            };
            var distances = FlatDistances(slots, 12);

            for (var seed = 0; seed < 60; seed++)
            {
                var result = PlacementRandomizer.RandomizeWithProfile(slots, new HexCoord(0, 0), profile, distances, seed);
                Assert.That(result.Success, Is.True, $"seed {seed}: {result.FailureReason}");
                var kinds = result.Assignments.Select(a => a.MonsterId).Distinct().Count();
                Assert.That(kinds, Is.GreaterThanOrEqualTo(2), $"seed {seed}: 종류가 {kinds}가지뿐이다.");
            }
        }

        [Test]
        public void MonsterMinKindsDefaultsToOffWhenTheColumnIsAbsent()
        {
            // 선택적 컬럼 계약(trapMinDistance·eliteMin과 같다): 없으면 규칙이 꺼진 채 종전과 같다.
            Assert.That(Parse().MonsterMinKinds, Is.EqualTo(0));
        }

        [Test]
        public void EliteStatMultipliersDefaultToOneHundredWhenAbsent()
        {
            // #12 엘리트 강화의 저작면. 컬럼이 없으면 100(배율 없음)이라 기존 스테이지는 무변경이다.
            var profile = Parse();
            Assert.That(profile.EliteHpPercent, Is.EqualTo(100));
            Assert.That(profile.EliteDamagePercent, Is.EqualTo(100));
        }

        [Test]
        public void EliteStatMultipliersReadFromTheStageRow()
        {
            var stages =
                "stageId,enabled,monsterThreatBudget,budgetTolerancePct,safeRadius,rerollLimit,densityRadius,densityCap,zoneEarlyMaxBfs,zoneMidMaxBfs,eliteHpPercent,eliteDamagePercent\n" +
                "stage-t,TRUE,8,100,3,40,2,99,5,10,160,150\n";
            var profile = Parse(stages: stages);
            Assert.That(profile.EliteHpPercent, Is.EqualTo(160));
            Assert.That(profile.EliteDamagePercent, Is.EqualTo(150));
        }

        [Test]
        public void EliteMinImpossibleFailsInsteadOfViolating()
        {
            // M006은 late 전용인데 전 슬롯이 Early면 elite 하한은 원리적으로 만족 불가 — 위반 배치
            // 대신 실패를 돌려주고 호출자가 저작 원본으로 폴백해야 한다.
            var stages =
                "stageId,enabled,monsterThreatBudget,budgetTolerancePct,safeRadius,rerollLimit,densityRadius,densityCap,zoneEarlyMaxBfs,zoneMidMaxBfs,eliteMin\n" +
                "stage-t,TRUE,8,100,3,10,2,99,5,10,1\n";
            var profile = Parse(stages: stages);
            var slots = new List<PlacementSlotCandidate> { Slot("a-1", 10, 0, "mon-a", "M001") };

            var result = PlacementRandomizer.RandomizeWithProfile(
                slots, new HexCoord(0, 0), profile, FlatDistances(slots, 4), 1);

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureReason, Does.Contain("reroll limit"));
        }

        [Test]
        public void ImpossibleBudgetFailsInsteadOfViolating()
        {
            var narrowStages = StagesCsv.Replace("stage-t,TRUE,8,25,", "stage-t,TRUE,50,5,");
            var profile = Parse(stages: narrowStages);
            var slots = new List<PlacementSlotCandidate> { Slot("a-1", 10, 0, "mon-a", "M001") };

            var result = PlacementRandomizer.RandomizeWithProfile(
                slots, new HexCoord(0, 0), profile, FlatDistances(slots, 12), 1);

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureReason, Does.Contain("reroll limit"));
        }

        // ── 반복 감쇠(2026-09-02 #5) ─────────────────────────────────────

        /// <summary>예산·상한·지대가 아무것도 막지 않는 판 — 오직 가중치만 남긴다.</summary>
        private const string DominantStagesCsv =
            "stageId,enabled,monsterThreatBudget,budgetTolerancePct,safeRadius,rerollLimit,densityRadius,densityCap,zoneEarlyMaxBfs,zoneMidMaxBfs,designerNote\n" +
            "stage-d,TRUE,8,100,3,40,1,99,5,10,\n";

        /// <summary>128 대 1. 감쇠가 없으면 여덟 칸이 전부 M001로 차는 것이 <b>정상</b>이다(≈94%).</summary>
        private const string DominantPoolsCsv =
            "stageId,group,entryKind,entryRef,threatCost,maxCount,capGroup,weight,zone,role,designerNote\n" +
            "stage-d,mon-a,monster,M001,1,8,,128,any,,\n" +
            "stage-d,mon-a,monster,M003,1,8,,1,any,,\n";

        private static List<PlacementSlotCandidate> EightSlots()
        {
            return Enumerable.Range(0, 8)
                .Select(index => Slot($"a-{index}", 10 + (index * 2), 0, "mon-a", "M001"))
                .ToList();
        }

        /// <summary>
        /// 🔴 <b>이 테스트가 무는 것</b>(2026-09-02 #5): 뽑힌 종의 가중치가 <b>뽑을 때마다 절반</b>으로
        /// 깎이는가. 감쇠를 지우면 128:1 풀에서 여덟 칸이 전부 한 종으로 차는 판이 <b>200시드 중 188판</b>
        /// 나오고 이 단언이 무너진다 — 사용자가 실플레이에서 본 「초반엔 삼목구만」이 바로 그 그림이다.
        ///
        /// <para>🔑 <b>맵 전체 종 수(<c>monsterMinKinds</c>)로는 이걸 못 잰다</b> — 그 하한은 판 전체에
        /// 세 종만 있으면 통과하므로 <b>한 밴드가 통째로 한 종</b>인 것을 그대로 통과시킨다.
        /// 그래서 여기서는 밴드 하나만 세워 놓고 그 안의 종 수를 센다.</para>
        /// </summary>
        [Test]
        public void RepeatedDrawsDecayTheirWeightSoOneKindCannotFillABand()
        {
            var slots = EightSlots();
            var profile = Parse(DominantStagesCsv, DominantPoolsCsv, "stage-d");
            var distances = FlatDistances(slots, 7);

            var mixedBands = 0;
            const int seedCount = 200;
            for (var seed = 0; seed < seedCount; seed++)
            {
                var result = PlacementRandomizer.RandomizeWithProfile(slots, new HexCoord(0, 0), profile, distances, seed);
                Assert.That(result.Success, Is.True, result.FailureReason);
                if (result.Assignments.Select(assignment => assignment.MonsterId).Distinct().Count() > 1)
                {
                    mixedBands++;
                }
            }

            // 감쇠가 살아 있으면 ~81%, 없으면 ~6%다. 경계는 그 사이에 넉넉히 둔다.
            Assert.That(mixedBands, Is.GreaterThan(seedCount / 2),
                $"밴드 여덟 칸이 한 종으로만 찬 판이 너무 많다({seedCount - mixedBands}/{seedCount}) — 반복 감쇠가 죽었다.");
        }

        /// <summary>
        /// 감쇠는 <b>상한이 아니다</b>: 가중치가 1 아래로 안 내려가므로, 한 종만 남은 밴드에서도
        /// 추첨이 성립한다(0으로 떨어뜨리면 총합 0으로 나눈다). 🔑 「몇 마리까지」는 <c>maxCount</c>의 몫이다.
        /// </summary>
        [Test]
        public void DecayNeverStarvesTheLastRemainingKind()
        {
            var slots = EightSlots();
            var singleKindPools =
                "stageId,group,entryKind,entryRef,threatCost,maxCount,capGroup,weight,zone,role,designerNote\n" +
                "stage-d,mon-a,monster,M001,1,8,,4,any,,\n";
            var profile = Parse(DominantStagesCsv, singleKindPools, "stage-d");
            var distances = FlatDistances(slots, 7);

            var result = PlacementRandomizer.RandomizeWithProfile(slots, new HexCoord(0, 0), profile, distances, 7);

            Assert.That(result.Success, Is.True, result.FailureReason);
            Assert.That(result.Assignments.Count, Is.EqualTo(8));
            Assert.That(result.Assignments.All(assignment => assignment.MonsterId == "M001"), Is.True);
        }

        private static string Describe(PlacementSpawnAssignment assignment)
        {
            return $"{assignment.SlotId}:{assignment.MonsterId}:{assignment.Role}:{assignment.Coord}";
        }
    }
}

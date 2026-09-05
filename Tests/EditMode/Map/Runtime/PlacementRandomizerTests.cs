using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Map.Runtime.Tests.EditMode
{
    /// <summary>
    /// 배치 랜덤라이저 P0 계약(placement-randomization-plan §3·§8): 결정성(같은 시드 = 같은 배치),
    /// 구성 보존(그룹 명단 멀티셋 불변), 안전 반경, 재롤 상한 초과 시 실패(호출자 폴백).
    /// </summary>
    public sealed class PlacementRandomizerTests
    {
        private static readonly HexCoord PlayerSpawn = new HexCoord(0, 0);

        private static PlacementSlotCandidate Slot(
            string id, int q, int r, string group, string monsterId = "", string role = "")
        {
            return new PlacementSlotCandidate(id, new HexCoord(q, r), group, monsterId, role, HexMapPurpose.Unspecified, $"patrol-{id}");
        }

        private static PlacementRandomizationConfig Config(int safeRadius = 3, int rerollLimit = 20)
        {
            return new PlacementRandomizationConfig(safeRadius, rerollLimit);
        }

        private static List<PlacementSlotCandidate> TwoGroupSlots()
        {
            return new List<PlacementSlotCandidate>
            {
                Slot("a-1", 10, 0, "mon-a", "M001"),
                Slot("a-2", 11, 0, "mon-a", "M003", "elite"),
                Slot("a-3", 12, 0, "mon-a"),
                Slot("a-4", 13, 0, "mon-a"),
                Slot("b-1", -10, 0, "mon-b", "M001"),
                Slot("b-2", -11, 0, "mon-b", "M001"),
                Slot("b-3", -12, 0, "mon-b"),
            };
        }

        [Test]
        public void SameSeedProducesIdenticalAssignments()
        {
            var slots = TwoGroupSlots();

            var first = PlacementRandomizer.Randomize(slots, PlayerSpawn, Config(), 12345);
            var second = PlacementRandomizer.Randomize(slots, PlayerSpawn, Config(), 12345);

            Assert.That(first.Success, Is.True);
            Assert.That(second.Success, Is.True);
            Assert.That(
                second.Assignments.Select(Describe),
                Is.EqualTo(first.Assignments.Select(Describe)));
        }

        [Test]
        public void InputOrderDoesNotChangeTheResult()
        {
            var slots = TwoGroupSlots();
            var reversed = slots.AsEnumerable().Reverse().ToList();

            var first = PlacementRandomizer.Randomize(slots, PlayerSpawn, Config(), 777);
            var second = PlacementRandomizer.Randomize(reversed, PlayerSpawn, Config(), 777);

            Assert.That(
                second.Assignments.Select(Describe).OrderBy(text => text),
                Is.EqualTo(first.Assignments.Select(Describe).OrderBy(text => text)));
        }

        [Test]
        public void GroupRosterMultisetIsPreservedAndRoleTravelsWithTheMonster()
        {
            var slots = TwoGroupSlots();

            for (var seed = 0; seed < 50; seed++)
            {
                var result = PlacementRandomizer.Randomize(slots, PlayerSpawn, Config(), seed);
                Assert.That(result.Success, Is.True, $"seed {seed}");

                var groupBySlot = slots.ToDictionary(slot => slot.SlotId, slot => slot.Group);
                var assignedA = result.Assignments.Where(a => groupBySlot[a.SlotId] == "mon-a").ToList();
                var assignedB = result.Assignments.Where(a => groupBySlot[a.SlotId] == "mon-b").ToList();

                Assert.That(
                    assignedA.Select(a => a.MonsterId).OrderBy(id => id),
                    Is.EqualTo(new[] { "M001", "M003" }), $"seed {seed}");
                Assert.That(
                    assignedB.Select(a => a.MonsterId),
                    Is.EqualTo(new[] { "M001", "M001" }), $"seed {seed}");
                // role은 몬스터를 따라간다 — elite 보상 티어·마커가 role을 소비한다.
                Assert.That(
                    assignedA.Single(a => a.MonsterId == "M003").Role,
                    Is.EqualTo("elite"), $"seed {seed}");
            }
        }

        [Test]
        public void AssignedSlotKeepsItsOwnPatrolAreaAndCoord()
        {
            var slots = TwoGroupSlots();
            var slotById = slots.ToDictionary(slot => slot.SlotId);

            var result = PlacementRandomizer.Randomize(slots, PlayerSpawn, Config(), 9);

            foreach (var assignment in result.Assignments)
            {
                Assert.That(assignment.Coord, Is.EqualTo(slotById[assignment.SlotId].Coord));
                Assert.That(assignment.PatrolAreaId, Is.EqualTo(slotById[assignment.SlotId].PatrolAreaId));
            }
        }

        [Test]
        public void SlotsInsideTheSafeRadiusAreNeverSelected()
        {
            var slots = new List<PlacementSlotCandidate>
            {
                Slot("near-1", 1, 0, "mon-a", "M001"),
                Slot("near-2", 2, 0, "mon-a"),
                Slot("far-1", 10, 0, "mon-a"),
                Slot("far-2", 11, 0, "mon-a"),
            };

            for (var seed = 0; seed < 100; seed++)
            {
                var result = PlacementRandomizer.Randomize(slots, PlayerSpawn, Config(safeRadius: 4), seed);
                Assert.That(result.Success, Is.True, $"seed {seed}");
                foreach (var assignment in result.Assignments)
                {
                    Assert.That(
                        assignment.Coord.DistanceTo(PlayerSpawn), Is.GreaterThanOrEqualTo(4),
                        $"seed {seed}: slot {assignment.SlotId}");
                }
            }
        }

        [Test]
        public void ImpossibleSafeRadiusFailsInsteadOfViolating()
        {
            var slots = new List<PlacementSlotCandidate>
            {
                Slot("near-1", 1, 0, "mon-a", "M001"),
                Slot("near-2", 2, 0, "mon-a"),
            };

            var result = PlacementRandomizer.Randomize(slots, PlayerSpawn, Config(safeRadius: 5), 1);

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureReason, Does.Contain("mon-a"));
        }

        [Test]
        public void DuplicateSlotIdsFail()
        {
            // 슬롯 id 충돌은 저작 오류다 — 조용히 배치를 겹치지 않고 실패해야 한다.
            var slots = new List<PlacementSlotCandidate>
            {
                new PlacementSlotCandidate("dup", new HexCoord(10, 0), "mon-a", "M001"),
                new PlacementSlotCandidate("dup", new HexCoord(11, 0), "mon-a", "M001"),
            };

            var result = PlacementRandomizer.Randomize(slots, PlayerSpawn, Config(), 1);

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureReason, Does.Contain("Duplicate slot id"));
        }

        [Test]
        public void UntaggedSlotsProduceNoAssignments()
        {
            var slots = new List<PlacementSlotCandidate>
            {
                Slot("fixed-1", 10, 0, group: "", monsterId: "M001"),
            };

            var result = PlacementRandomizer.Randomize(slots, PlayerSpawn, Config(), 1);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Assignments, Is.Empty);
        }

        [Test]
        public void SeedSweepNeverViolatesInvariants()
        {
            var slots = TwoGroupSlots();
            var distinctLayouts = new HashSet<string>();

            for (var seed = 0; seed < 200; seed++)
            {
                var result = PlacementRandomizer.Randomize(slots, PlayerSpawn, Config(), seed);
                Assert.That(result.Success, Is.True, $"seed {seed}");
                Assert.That(result.Assignments.Count, Is.EqualTo(4), $"seed {seed}");
                Assert.That(
                    result.Assignments.Select(a => a.SlotId).Distinct().Count(),
                    Is.EqualTo(4), $"seed {seed}: a slot was assigned twice");
                distinctLayouts.Add(string.Join("|", result.Assignments.Select(Describe)));
            }

            // 랜덤화가 실제로 다양한 배치를 만드는지(전부 같은 배치면 셔플이 죽은 것).
            Assert.That(distinctLayouts.Count, Is.GreaterThan(10));
        }

        private static string Describe(PlacementSpawnAssignment assignment)
        {
            return $"{assignment.SlotId}:{assignment.MonsterId}:{assignment.Role}:{assignment.Coord}";
        }
    }
}

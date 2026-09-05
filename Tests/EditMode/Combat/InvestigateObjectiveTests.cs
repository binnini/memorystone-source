using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class InvestigateObjectiveTests
    {
        [Test]
        public void MovingToObjectiveTileDoesNotAutoCompleteObjective()
        {
            var state = CreateObjectiveState(new HexCoord(1, 0));

            Assert.That(state.TryPlayerMove(new HexCoord(1, 0)), Is.True);
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerMovement)); // 이동만으로는 페이즈 유지
            AdvanceToActionPhase(state);

            Assert.That(state.ObjectiveCompleted, Is.False);
            Assert.That(state.LastInvestigateResult, Is.Empty);
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction));
        }

        [Test]
        public void RevealedObjectiveInRangeCompletesWithInvestigate()
        {
            var state = CreateObjectiveState(new HexCoord(1, 0));
            Assert.That(state.TryPlayerMove(new HexCoord(1, 0)), Is.True);
            AdvanceToActionPhase(state);

            Assert.That(state.TryPlayerInvestigate(new HexCoord(1, 0)), Is.True);

            Assert.That(state.ObjectiveCompleted, Is.True);
            Assert.That(state.LastInvestigateResult, Does.Contain("Objective complete"));
            Assert.That(state.LastDiscardedCard, Is.EqualTo(CombatCardKind.Investigate));
        }

        [Test]
        public void ObjectiveCompletionIsIdempotentWithoutAutoCompletingNextTurn()
        {
            var state = CreateObjectiveState(new HexCoord(1, 0));
            Assert.That(state.TryPlayerMove(new HexCoord(1, 0)), Is.True);
            AdvanceToActionPhase(state);
            Assert.That(state.TryPlayerInvestigate(new HexCoord(1, 0)), Is.True);
            Assert.That(state.ObjectiveCompleted, Is.True);

            state.EndAction();
            state.ResolveMonsterAction();

            Assert.That(state.TryPlayerMove(new HexCoord(1, 0)), Is.True);
            Assert.That(state.ObjectiveCompleted, Is.True);
        }

        [Test]
        public void HiddenObjectiveCannotBeCompletedBeforeReveal()
        {
            var state = new CombatState(
                CombatObjectiveMapBuilder.CreateObjectiveBehindSightBlockerMap(),
                new HexCoord(0, 0),
                new HexCoord(4, 0),
                new CombatConfig(20, 10, 2, 1, 4, 4, 5, 1, 3, playerVisionRange: 1));
            var objectiveCoord = new HexCoord(2, -1);

            Assert.That(state.GetVisibility(objectiveCoord), Is.Not.EqualTo(HexCellVisibility.Revealed));
            Assert.That(state.TryPlayerMove(new HexCoord(0, 0)), Is.True);
            AdvanceToActionPhase(state);
            Assert.That(state.ObjectiveCompleted, Is.False);
            Assert.That(state.TryPlayerInvestigate(objectiveCoord), Is.False);
            Assert.That(state.LastFailureReason, Does.Contain("revealed"));
        }

        [Test]
        public void ObjectiveStatusTextUpdatesOnInvestigateCompletion()
        {
            var state = CreateObjectiveState(new HexCoord(1, 0));

            Assert.That(state.ObjectiveStatusText, Does.Not.Contain("Objective complete:"));
            Assert.That(state.TryPlayerMove(new HexCoord(1, 0)), Is.True);
            AdvanceToActionPhase(state);
            Assert.That(state.TryPlayerInvestigate(new HexCoord(1, 0)), Is.True);

            Assert.That(state.ObjectiveStatusText, Does.Contain("complete"));
        }

        [Test]
        public void MonsterNearbyDoesNotCancelSuccessfulInvestigate()
        {
            var state = new CombatState(
                CombatObjectiveMapBuilder.CreateObjectiveMap(new HexCoord(1, 0)),
                new HexCoord(0, 0),
                new[]
                {
                    new MonsterConfig("nearby-monster", new HexCoord(2, 0), 10)
                },
                CombatConfig.Default);

            Assert.That(state.TryPlayerMove(new HexCoord(1, 0)), Is.True);
            AdvanceToActionPhase(state);
            Assert.That(state.TryPlayerInvestigate(new HexCoord(1, 0)), Is.True);

            Assert.That(state.ObjectiveCompleted, Is.True);
        }

        [Test]
        public void ObjectiveBindingFromMapDataCanTargetAuthoredNonLegacyLandmark()
        {
            var cells = new[]
            {
                new HexCellData(new HexCoord(0, 0), "start", "street", 1, true, false),
                new HexCellData(new HexCoord(1, 0), "custom-objective", "landmark", 1, true, false, landmarkId: "custom-objective-landmark"),
                new HexCellData(new HexCoord(3, 0), "enemy-start", "street", 1, true, false)
            };
            var map = new HexMapData(cells, new[]
            {
                new HexObjectiveBinding("custom-objective", "custom-objective-landmark", "custom landmark")
            });
            var state = new CombatState(map, new HexCoord(0, 0), new HexCoord(3, 0), CombatConfig.Default);

            Assert.That(state.HasObjective, Is.True);
            Assert.That(state.ObjectiveBinding.ObjectiveId, Is.EqualTo("custom-objective"));
            Assert.That(state.ObjectiveBindingEvidenceText, Does.Contain("custom-objective-landmark"));

            Assert.That(state.TryPlayerMove(new HexCoord(1, 0)), Is.True);
            AdvanceToActionPhase(state);
            Assert.That(state.TryPlayerInvestigate(new HexCoord(1, 0)), Is.True);
            Assert.That(state.ObjectiveCompleted, Is.True);
            Assert.That(state.LastInvestigateResult, Does.Contain("custom landmark"));
        }

        [Test]
        public void HiddenObjectiveDetailsNotLeakedByStatusText()
        {
            var state = CreateObjectiveState(new HexCoord(3, 0));
            Assert.That(state.ObjectiveStatusText, Does.Not.Contain("63"));
            Assert.That(state.ObjectiveStatusText, Does.Not.Contain("landmark-63"));
        }

        [Test]
        public void InvestigateRequiresCardRangeAndObjectiveTarget()
        {
            var state = CreateObjectiveState(new HexCoord(2, 0));
            Assert.That(state.TryPlayerMove(new HexCoord(0, 0)), Is.True);
            AdvanceToActionPhase(state);
            Assert.That(state.GetCombatCards().Any(card => card.Kind == CombatCardKind.Investigate), Is.True);

            Assert.That(state.TryPlayerInvestigate(new HexCoord(2, 0)), Is.False);
            Assert.That(state.LastFailureReason, Does.Contain("range"));

            Assert.That(state.TryPlayerInvestigate(new HexCoord(0, 0)), Is.False);
            Assert.That(state.LastFailureReason, Does.Contain("not the objective"));
        }

        [Test]
        public void MemoryStoneInteractionRequiresBossDefeat()
        {
            var state = CreateMemoryStoneState();

            Assert.That(state.HasMemoryStoneObjective, Is.True);
            Assert.That(state.HasBossMonster, Is.True);
            Assert.That(state.IsMemoryStoneAwakened, Is.False);
            Assert.That(state.CanInteractMemoryStoneAtPlayer(out var dormantReason), Is.False);
            Assert.That(dormantReason, Does.Contain("boss"));

            Assert.That(state.DebugReplayPlayerAttack(lethal: true, out _, out _), Is.True);

            Assert.That(state.IsMemoryStoneAwakened, Is.True);
            Assert.That(state.TryInteractMemoryStoneAtPlayer(out var reason), Is.True);
            Assert.That(reason, Does.Contain("Objective complete"));
            Assert.That(state.ObjectiveCompleted, Is.True);
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.Victory));
        }

        private static void AdvanceToActionPhase(CombatState state)
        {
            // DEC-2026-07-03-02: 확정된 턴 계약 — 이동만으로는 페이즈가 유지되고,
            // EndAction()이 MonsterMovement로 전환하며 몬스터 이동 해석 후 PlayerAction에 도달한다.
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction));
        }

        private static CombatState CreateObjectiveState(HexCoord objectiveCoord)
        {
            return new CombatState(
                CombatObjectiveMapBuilder.CreateObjectiveMap(objectiveCoord),
                new HexCoord(0, 0),
                new HexCoord(4, 0),
                new CombatConfig(20, 10, 2, 1, 4, 4, 5, 1, 3, playerVisionRange: 2));
        }

        private static CombatState CreateMemoryStoneState()
        {
            var memoryStoneCoord = new HexCoord(0, 0);
            var bossCoord = new HexCoord(1, 0);
            var map = new HexMapData(
                new[]
                {
                    new HexCellData(memoryStoneCoord, "tile", "street", 1, true, false),
                    new HexCellData(bossCoord, "tile", "street", 1, true, false)
                },
                objectRefs: new[]
                {
                    new HexMapObjectData("memory-main", "MemoryStone", "memorystone", memoryStoneCoord, role: "objective", interactable: true)
                });

            return new CombatState(
                map,
                memoryStoneCoord,
                new[]
                {
                    new MonsterConfig("boss-01", bossCoord, 10, spawnRole: "boss")
                },
                new CombatConfig(20, 10, 2, 1, 4, 4, 5, 1, 3, playerVisionRange: 1));
        }
    }
}


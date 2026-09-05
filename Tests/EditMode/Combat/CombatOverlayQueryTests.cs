using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class CombatOverlayQueryTests
    {
        [Test]
        public void SelectedTargetHighlightCellsReturnsEmptyWithoutSelectionOrState()
        {
            var query = new CombatOverlayQuery();
            var state = CreateActionState(out var map);

            Assert.That(query.GetSelectedTargetHighlightCells(null, map, CombatCardKind.Attack, ApprovedCardCatalogFactory.AttackDoubleHitId), Is.Empty);
            Assert.That(query.GetSelectedTargetHighlightCells(state, null, CombatCardKind.Attack, ApprovedCardCatalogFactory.AttackDoubleHitId), Is.Empty);
            Assert.That(query.GetSelectedTargetHighlightCells(state, map, null, ApprovedCardCatalogFactory.AttackDoubleHitId), Is.Empty);
        }

        [Test]
        public void AttackSelectionHighlightsSelectedCardRangeCells()
        {
            var query = new CombatOverlayQuery();
            var state = CreateActionState(out var map);
            var heavyStrike = state.GetCombatCards().Single(card => card.Id == ApprovedCardCatalogFactory.AttackDoubleHitId && !card.IsDiscarded);

            var cells = query.GetSelectedTargetHighlightCells(state, map, CombatCardKind.Attack, heavyStrike.Id).ToList();
            // Overlay highlights exclude unwalkable cells (same gate as movement/pathfinding).
            var expected = map.AllCells
                .Where(cell => cell.BaseWalkable && state.PlayerCoord.DistanceTo(cell.Coord) <= heavyStrike.Range)
                .Select(cell => cell.Coord)
                .ToList();

            CollectionAssert.AreEquivalent(expected, cells);
            Assert.That(cells, Does.Contain(state.PlayerCoord));
        }

        [Test]
        public void AttackSelectionDoesNotHighlightUnwalkableCells()
        {
            var map = new HexMapData(new[]
            {
                new HexCellData(new HexCoord(0, 0), "start", "street", 1, true, false),
                new HexCellData(new HexCoord(1, 0), "water", "water", 1, false, false),
                new HexCellData(new HexCoord(0, 1), "street", "street", 1, true, false),
                new HexCellData(new HexCoord(2, 0), "enemy", "street", 1, true, false)
            });
            var state = new CombatState(
                map,
                new HexCoord(0, 0),
                new HexCoord(2, 0),
                new CombatConfig(20, 10, 2, 1, 4, 4, 5, 1, 3, actionBudget: 2, movementHandSize: 1, actionHandSize: 5));
            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();

            // AttackSweep is now a self-centred area card (range 0), so use a ranged attack card
            // to exercise the unwalkable-exclusion contract of the target highlight.
            var cells = new CombatOverlayQuery()
                .GetSelectedTargetHighlightCells(state, map, CombatCardKind.Attack, ApprovedCardCatalogFactory.AttackDoubleHitId)
                .ToList();

            Assert.That(cells, Has.No.Member(new HexCoord(1, 0)));
            Assert.That(cells, Has.Member(new HexCoord(0, 1)));
        }

        [Test]
        public void SelfAreaAttackSelectionHighlightsYellowEffectArea()
        {
            var query = new CombatOverlayQuery();
            var state = CreateApprovedActionState(out var map);
            var selection = CombatSelectionState.Target(
                CombatCardKind.Attack,
                ApprovedCardCatalogFactory.AttackSweepId,
                "Sweep");

            var cells = query.GetSelectedEffectAreaHighlightCells(state, map, selection).ToList();

            Assert.That(cells, Is.Not.Empty);
            Assert.That(cells.All(coord => state.PlayerCoord.DistanceTo(coord) <= 1), Is.True);
            Assert.That(cells, Has.Member(state.PlayerCoord));
        }

        [Test]
        public void RandomMoveSelectionHighlightsYellowEffectArea()
        {
            var query = new CombatOverlayQuery();
            var map = CombatState.CreateDemoMap(4);
            var config = new CombatConfig(20, 10, 2, 1, 4, 4, 5, 1, 3, actionBudget: 3, movementHandSize: 6, actionHandSize: 1);
            var state = new CombatState(
                map,
                new HexCoord(0, 0),
                new HexCoord(4, 0),
                config,
                cardCatalog: ApprovedCardCatalogFactory.CreateApprovedCatalog(config));
            var card = state.GetCombatCards().Single(snapshot => snapshot.Id == ApprovedCardCatalogFactory.MoveRandomJourneyId && !snapshot.IsDiscarded);
            var selection = CombatSelectionState.Move(card.Id, card.InstanceId, card.Name);

            var cells = query.GetSelectedEffectAreaHighlightCells(state, map, selection).ToList();

            Assert.That(cells, Is.Not.Empty);
            Assert.That(cells.All(coord => state.PlayerCoord.DistanceTo(coord) <= card.AreaRadius), Is.True);
            Assert.That(cells, Has.Member(state.PlayerCoord));
        }

        [Test]
        public void TileAreaCardsHighlightHoveredEffectArea()
        {
            var query = new CombatOverlayQuery();
            var state = CreateApprovedActionState(out var map);
            var fieldCard = state.GetCombatCards().Single(card => card.Id == ApprovedCardCatalogFactory.FieldFlashbangId && !card.IsDiscarded);
            var selection = CombatSelectionState.Target(CombatCardKind.FieldObject, fieldCard.Id, fieldCard.InstanceId, fieldCard.Name);
            var hover = new HexCoord(1, 0);

            var cells = query.GetSelectedEffectAreaHighlightCells(state, map, selection, hover).ToList();

            Assert.That(cells, Is.Not.Empty);
            Assert.That(cells.All(coord => hover.DistanceTo(coord) <= fieldCard.AreaRadius), Is.True);
            Assert.That(cells, Has.Member(hover));
        }

        [Test]
        public void ScoutSelectionHighlightsCellsAcceptedByScoutValidation()
        {
            var query = new CombatOverlayQuery();
            var state = CreateActionState(out var map);

            var cells = query.GetSelectedTargetHighlightCells(state, map, CombatCardKind.Scout, ApprovedCardCatalogFactory.ScoutMinefinderId).ToList();
            var expected = map.AllCells
                .Where(cell => state.ValidateScoutTarget(cell.Coord).IsValid)
                .Select(cell => cell.Coord)
                .ToList();

            CollectionAssert.AreEquivalent(expected, cells);
            Assert.That(cells, Is.Not.Empty);
        }

        [Test]
        public void FieldObjectSelectionHighlightsCellsAcceptedByFieldObjectValidation()
        {
            var query = new CombatOverlayQuery();
            var state = CreateApprovedActionState(out var map);
            var fieldCard = state.GetCombatCards().Single(card => card.Id == ApprovedCardCatalogFactory.FieldFlashbangId && !card.IsDiscarded);

            var cells = query.GetSelectedTargetHighlightCells(state, map, CombatCardKind.FieldObject, fieldCard.Id).ToList();
            var expected = map.AllCells
                .Where(cell => state.ValidateFieldObjectTarget(cell.Coord, fieldCard.Id).IsValid)
                .Select(cell => cell.Coord)
                .ToList();

            CollectionAssert.AreEquivalent(expected, cells);
            Assert.That(cells, Is.Not.Empty);
        }

        [Test]
        public void SelectedTargetHighlightCellsReturnsEmptyForMismatchedStateAndLoadedMap()
        {
            var query = new CombatOverlayQuery();
            var state = CreateActionState(out _);
            var mismatchedMap = new HexMapData(new[]
            {
                new HexCellData(new HexCoord(0, 0), "other", "street", 1, true, false)
            });

            var cells = query.GetSelectedTargetHighlightCells(
                state,
                mismatchedMap,
                CombatCardKind.Scout,
                ApprovedCardCatalogFactory.ScoutMinefinderId);

            Assert.That(cells, Is.Empty);
        }


        [Test]
        public void SelectionStateOverloadMatchesKindAndIdQuery()
        {
            var query = new CombatOverlayQuery();
            var state = CreateActionState(out var map);
            var heavyStrike = state.GetCombatCards().Single(card => card.Id == ApprovedCardCatalogFactory.AttackDoubleHitId && !card.IsDiscarded);
            var selection = CombatSelectionState.Target(CombatCardKind.Attack, heavyStrike.Id, heavyStrike.Name);

            var viaSelection = query.GetSelectedTargetHighlightCells(state, map, selection).ToList();
            var viaCompatibilitySignature = query.GetSelectedTargetHighlightCells(
                state,
                map,
                CombatCardKind.Attack,
                heavyStrike.Id).ToList();

            CollectionAssert.AreEquivalent(viaCompatibilitySignature, viaSelection);
        }

        [Test]
        public void SelectionStateOverloadReturnsEmptyForMoveOrNoneSelection()
        {
            var query = new CombatOverlayQuery();
            var state = CreateActionState(out var map);

            Assert.That(query.GetSelectedTargetHighlightCells(state, map, CombatSelectionState.None), Is.Empty);
            Assert.That(query.GetSelectedTargetHighlightCells(state, map, CombatSelectionState.Move(ApprovedCardCatalogFactory.Move1HexId, "Move 1")), Is.Empty);
        }

        [Test]
        public void MonsterChaseHighlightCellsUseConfiguredChaseRange()
        {
            var query = new CombatOverlayQuery();
            var state = CreateActionState(out var map);

            var cells = query.GetMonsterChaseHighlightCells(state, map, includeUnrevealedMonsters: true).ToList();
            var monsterCoord = state.Monsters.First(monster => !monster.IsDead).Coord;
            // Overlay highlights exclude unwalkable cells (same gate as movement/pathfinding).
            var expected = map.AllCells
                .Where(cell => cell.BaseWalkable && monsterCoord.DistanceTo(cell.Coord) <= state.Config.EnemyChaseRange)
                .Select(cell => cell.Coord)
                .ToList();

            CollectionAssert.AreEquivalent(expected, cells);
        }

        private static CombatState CreateActionState(out HexMapData map)
        {
            map = CombatState.CreateDemoMap(4);
            var state = new CombatState(
                map,
                new HexCoord(0, 0),
                new HexCoord(4, 0),
                new CombatConfig(20, 10, 2, 1, 4, 4, 5, 1, 3, actionBudget: 2, movementHandSize: 1, actionHandSize: 5));
            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction));
            return state;
        }

        private static CombatState CreateApprovedActionState(out HexMapData map)
        {
            map = CombatState.CreateDemoMap(4);
            var config = new CombatConfig(20, 10, 2, 1, 4, 4, 5, 1, 3, actionBudget: 3, movementHandSize: 1, actionHandSize: 19);
            var state = new CombatState(
                map,
                new HexCoord(0, 0),
                new HexCoord(4, 0),
                config,
                cardCatalog: ApprovedCardCatalogFactory.CreateApprovedCatalog(config));
            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction));
            return state;
        }
    }
}


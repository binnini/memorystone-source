using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class CombatOverlayPresentationBuilderTests
    {
        [Test]
        public void MonsterIntentOverlaysAreVisibleOnlyDuringPlayerDecisionPhases()
        {
            var state = CreateState(out var map);
            var builder = new CombatOverlayPresentationBuilder();

            var playerPhasePresentation = builder.Build(CreateRequest(state, map));

            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerMovement));
            Assert.That(state.GetMonsterIntentPreviews(includeUnrevealed: true), Is.Not.Empty);
            Assert.That(
                playerPhasePresentation.Layers.Select(layer => layer.Layer),
                Does.Contain(HexOverlayLayer.MonsterAttackIntent));

            Assert.That(state.EndAction(), Is.True);
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.MonsterMovement));

            var monsterPhasePresentation = builder.Build(CreateRequest(state, map));

            Assert.That(
                monsterPhasePresentation.Layers.Select(layer => layer.Layer),
                Has.No.Member(HexOverlayLayer.MonsterAttackIntent));
            Assert.That(
                monsterPhasePresentation.Layers.Select(layer => layer.Layer),
                Has.No.Member(HexOverlayLayer.MonsterMoveIntent));
            Assert.That(
                monsterPhasePresentation.Layers.Select(layer => layer.Layer),
                Has.No.Member(HexOverlayLayer.MonsterChaseRange));
        }

        [Test]
        public void StatusIconAnnotationsMatchOverlayQueryDuringPlayerDecisionPhase()
        {
            var state = CreateState(out var map);
            var builder = new CombatOverlayPresentationBuilder();

            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerMovement));

            var presentation = builder.Build(CreateRequest(state, map));

            // Builder forwards exactly what the query telegraphs (gate: ShowMonsterAttackOverlay +
            // player decision phase), reusing the same inputs as the MonsterAttackIntent layer.
            var expected = new CombatOverlayQuery()
                .GetMonsterAttackStatusIconCells(state, map, includeUnrevealedMonsters: true);
            Assert.That(presentation.Annotations.Count, Is.EqualTo(expected.Count));
            CollectionAssert.AreEquivalent(
                expected.Select(annotation => annotation.Coord),
                presentation.Annotations.Select(annotation => annotation.Coord));
        }

        [Test]
        public void StatusIconAnnotationsAreEmptyDuringCardTargetSelection()
        {
            var state = CreateState(out var map);
            var builder = new CombatOverlayPresentationBuilder();

            var presentation = builder.Build(
                CreateRequest(state, map, selectedTargetCardKind: CombatCardKind.Attack));

            Assert.That(presentation.Annotations, Is.Empty);
        }

        [Test]
        public void StatusIconAnnotationsAreEmptyOutsidePlayerDecisionPhases()
        {
            var state = CreateState(out var map);
            var builder = new CombatOverlayPresentationBuilder();

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True);
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.MonsterAction));

            var presentation = builder.Build(CreateRequest(state, map));

            Assert.That(presentation.Annotations, Is.Empty);
        }

        [Test]
        public void StatusIconAnnotationsAreSuppressedWhenMonsterAttackOverlayDisabled()
        {
            var state = CreateState(out var map);
            var builder = new CombatOverlayPresentationBuilder();

            var presentation = builder.Build(
                CreateRequest(state, map, showMonsterAttackOverlay: false));

            Assert.That(
                presentation.Layers.Select(layer => layer.Layer),
                Has.No.Member(HexOverlayLayer.MonsterAttackIntent));
            Assert.That(presentation.Annotations, Is.Empty);
        }

        [Test]
        public void HoveredMonsterNarrowsMoveAndChaseIntentOverlays()
        {
            var map = CombatState.CreateDemoMap(5);
            var state = new CombatState(
                map,
                new HexCoord(0, 0),
                new[]
                {
                    new MonsterConfig("monster-a", new HexCoord(2, 0), 10),
                    new MonsterConfig("monster-b", new HexCoord(3, 1), 10)
                },
                new CombatConfig(20, 10, 3, 1, 4, 4, 5, 1, 3, movementHandSize: 1, actionHandSize: 1));
            var builder = new CombatOverlayPresentationBuilder();
            var hoveredId = "monster-b";

            var presentation = builder.Build(CreateRequest(state, map, hoveredMonsterId: hoveredId));

            var expectedMove = state.GetMonsterIntentPreviews(includeUnrevealed: true)
                .Where(preview => preview.MonsterId == hoveredId && preview.WillMove)
                .Select(preview => preview.PredictedMoveCoord)
                .Distinct()
                .ToArray();
            var moveLayer = presentation.Layers.Single(layer => layer.Layer == HexOverlayLayer.MonsterMoveIntent);
            CollectionAssert.AreEquivalent(expectedMove, moveLayer.Coords);

            var otherMoveCoords = state.GetMonsterIntentPreviews(includeUnrevealed: true)
                .Where(preview => preview.MonsterId != hoveredId && preview.WillMove)
                .Select(preview => preview.PredictedMoveCoord)
                .ToArray();
            Assert.That(moveLayer.Coords.Intersect(otherMoveCoords), Is.Empty);
        }

        [Test]
        public void ReachableFillKeepsTelegraphedThreatTiles()
        {
            var state = CreateState(out var map);
            var query = new CombatOverlayQuery();
            var threat = query.GetMonsterIntentAttackHighlightCells(state, map, includeUnrevealedMonsters: true).ToArray();
            Assert.That(threat, Is.Not.Empty, "Test needs at least one telegraphed threat tile.");

            // The reachable fill must keep threatened tiles (the danger hatch layers on top) so a
            // movable-but-dangerous tile still reads as movable.
            var safe = new HexCoord(99, 99);
            var reachable = new[] { safe, threat[0] };

            var request = new CombatOverlayPresentationRequest(
                state,
                map,
                query,
                isMoveSelectionActive: true,
                selectedTargetCardKind: null,
                reachableCoords: reachable,
                selectedTargetHighlightCoords: Enumerable.Empty<HexCoord>(),
                revealAllMapCellsInDebugMode: true,
                showPlayerMovementOverlay: true,
                showPlayerActionOverlay: true,
                showMonsterMoveOverlay: true,
                showMonsterAttackOverlay: true,
                showMonsterChaseOverlay: true);

            var presentation = new CombatOverlayPresentationBuilder().Build(request);
            var reachableLayer = presentation.Layers.Single(layer => layer.Layer == HexOverlayLayer.Reachable);

            Assert.That(reachableLayer.Coords, Has.Member(threat[0]), "Threatened tiles stay in the Reachable fill.");
            Assert.That(reachableLayer.Coords, Has.Member(safe), "Non-threat reachable tiles remain.");
        }

        [Test]
        public void BossFootprintLayerUsesTheGatedCoordsWhenProvided()
        {
            // #22: 컨트롤러는 몬스터 본체와 같은 가시성 게이트로 거른 좌표를 넘긴다. 빌더는 넘어온
            // 좌표를 그대로 써야 하고(State 전량으로 되돌아가면 게이트가 무력화된다), 걸러져 비면
            // 레이어 자체를 만들지 않아야 한다(암시야 관통 누설 방지).
            var state = CreateState(out var map);
            var builder = new CombatOverlayPresentationBuilder();
            var gated = new[] { new HexCoord(2, -1) };

            var gatedPresentation = builder.Build(CreateRequest(state, map, bossFootprintCoords: gated));
            var footprintLayer = gatedPresentation.Layers.Single(layer => layer.Layer == HexOverlayLayer.BossFootprint);
            CollectionAssert.AreEquivalent(gated, footprintLayer.Coords);

            var hiddenPresentation = builder.Build(
                CreateRequest(state, map, bossFootprintCoords: System.Array.Empty<HexCoord>()));
            Assert.That(
                hiddenPresentation.Layers.Select(layer => layer.Layer),
                Has.No.Member(HexOverlayLayer.BossFootprint),
                "게이트가 전부 걸러냈으면 점유 레이어가 나오면 안 된다.");
        }

        private static CombatOverlayPresentationRequest CreateRequest(
            CombatState state,
            HexMapData map,
            bool isMoveSelectionActive = false,
            CombatCardKind? selectedTargetCardKind = null,
            bool showMonsterAttackOverlay = true,
            string hoveredMonsterId = null,
            System.Collections.Generic.IReadOnlyList<HexCoord> bossFootprintCoords = null)
        {
            return new CombatOverlayPresentationRequest(
                state,
                map,
                new CombatOverlayQuery(),
                isMoveSelectionActive: isMoveSelectionActive,
                selectedTargetCardKind: selectedTargetCardKind,
                reachableCoords: Enumerable.Empty<HexCoord>(),
                selectedTargetHighlightCoords: Enumerable.Empty<HexCoord>(),
                revealAllMapCellsInDebugMode: true,
                showPlayerMovementOverlay: true,
                showPlayerActionOverlay: true,
                showMonsterMoveOverlay: true,
                showMonsterAttackOverlay: showMonsterAttackOverlay,
                showMonsterChaseOverlay: true,
                hoveredMonsterId: hoveredMonsterId,
                bossFootprintCoords: bossFootprintCoords);
        }

        private static CombatState CreateState(out HexMapData map)
        {
            map = CombatState.CreateDemoMap(4);
            return new CombatState(
                map,
                new HexCoord(0, 0),
                new HexCoord(2, 0),
                new CombatConfig(20, 10, 2, 1, 4, 4, 5, 1, 3, movementHandSize: 1, actionHandSize: 1));
        }
    }
}


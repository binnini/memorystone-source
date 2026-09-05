using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.CardCore;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class VisibilityRuntimeTests
    {
        [Test]
        public void CombatStateAllowsUnknownMovementDestinationWithinRange()
        {
            var state = CreateState();

            Assert.That(state.GetVisibility(new HexCoord(3, 0)), Is.EqualTo(HexCellVisibility.Unknown));
            // (3,0) is out of move range (distance 3 > range 2), so failure is range, not visibility
            Assert.That(state.TryPlayerMove(new HexCoord(3, 0)), Is.False);
            Assert.That(state.LastFailureReason, Does.Not.Contain("unknown"));
        }

        [Test]
        public void DefaultPlayerVisionRangeIsSevenCells()
        {
            // Single source of truth for the player's default sight radius. Update this number
            // intentionally if the design changes; CombatConfig.Default and the MapCombatController
            // serialized playerVisionRange should stay in sync with it.
            Assert.That(CombatConfig.Default.PlayerVisionRange, Is.EqualTo(7));
        }

        [Test]
        public void PlayerVisionDoesNotExpandWithMovementCardRange()
        {
            // Vision (2) is intentionally smaller than the movement points/range (3). After a long
            // move the revealed radius must follow the fixed vision range, not the move distance.
            var config = new CombatConfig(20, 10, 3, 1, 4, 4, 5, 1, 3, 3, 1, 4, playerVisionRange: 2);
            var catalog = ApprovedCardCatalogFactory.CreateApprovedCatalog(config);
            var deck = new PlayerDeckData(
                new[] { new PlayerCardInstanceData("vision-test.M03", ApprovedCardCatalogFactory.Move3HexId) },
                null);
            var state = new CombatState(CombatState.CreateDemoMap(5), new HexCoord(0, 0), new HexCoord(5, 0), config, cardCatalog: catalog, playerDeck: deck);

            Assert.That(state.TryPlayerMove(new HexCoord(3, 0)), Is.True);
            Assert.That(state.PlayerCoord, Is.EqualTo(new HexCoord(3, 0)));
            // (0,0) is 3 cells from the new position: outside vision 2, so it must drop to Hinted
            // (it would still be Revealed if vision tracked the move range of 3).
            Assert.That(state.GetVisibility(new HexCoord(0, 0)), Is.EqualTo(HexCellVisibility.Hinted));
        }

        [Test]
        public void CombatStateAllowsVisibleMovementAndRevealsDestination()
        {
            var state = CreateState();
            var destination = new HexCoord(1, 0);

            Assert.That(state.GetVisibility(destination), Is.EqualTo(HexCellVisibility.Revealed));
            Assert.That(state.TryPlayerMove(destination), Is.True);
            Assert.That(state.PlayerCoord, Is.EqualTo(destination));
            Assert.That(state.GetVisibility(destination), Is.EqualTo(HexCellVisibility.Revealed));
        }

        [Test]
        public void InvestigationTargetingRejectsUnknownAndRequiresRevealedCells()
        {
            var state = CreateState();

            Assert.That(state.CanTargetForInvestigation(new HexCoord(3, 0), out var unknownReason), Is.False);
            Assert.That(unknownReason, Does.Contain("unknown"));
            Assert.That(state.CanTargetForInvestigation(new HexCoord(0, 0), out var revealedReason), Is.True);
            Assert.That(revealedReason, Is.Empty);
        }

        [Test]
        public void ControllerRejectsOutOfRangeMoveRegardlessOfVisibility()
        {
            var root = new GameObject("WU07 Controller Fixture");
            try
            {
                var controller = root.AddComponent<MapCombatController>();
                controller.ConfigureMapForTests(CreateMap());
                controller.InitializeIntegration();

                Assert.That(controller.BeginMoveSelection(), Is.True);
                Assert.That(controller.Reachable.ContainsKey(new HexCoord(3, 0)), Is.False);
                Assert.That(controller.TryMoveTo(new HexCoord(3, 0)), Is.False);
                Assert.That(controller.LastInputMessage, Does.Contain("not reachable"));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ControllerStillMovesToVisibleRevealedOrHintedCells()
        {
            var root = new GameObject("WU07 Controller Visible Fixture");
            try
            {
                var controller = root.AddComponent<MapCombatController>();
                controller.ConfigureMapForTests(CreateMap());
                controller.InitializeIntegration();

                Assert.That(controller.BeginMoveSelection(), Is.True);
                Assert.That(controller.Reachable.ContainsKey(new HexCoord(1, 0)), Is.True);
                Assert.That(controller.TryMoveTo(new HexCoord(1, 0)), Is.True);
                Assert.That(controller.State.PlayerCoord, Is.EqualTo(new HexCoord(1, 0)));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }


        [Test]
        public void ControllerVisibilityTooltipDoesNotLeakHiddenLandmarkOrEventDetails()
        {
            var root = new GameObject("WU07 Visibility Tooltip Fixture");
            try
            {
                var controller = root.AddComponent<MapCombatController>();
                controller.ConfigureMapForTests(CreateSensitiveMap());
                controller.ConfigurePlayerVisionRangeForTests(2);
                controller.InitializeIntegration();

                Assert.That(controller.GetVisibilitySafeCellInfo(new HexCoord(3, 0)).Visibility, Is.EqualTo(HexCellVisibility.Unknown));

                var unknownTooltip = controller.GetVisibilitySafeTooltipText(new HexCoord(3, 0));

                Assert.That(unknownTooltip, Does.Not.Contain("secret-unknown-landmark"));
                Assert.That(unknownTooltip, Does.Not.Contain("secret-unknown-event"));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void PlayerVisionNeverRevealsTrapsButScoutDoesAndPersists()
        {
            var map = CreateTrapLineMap(new HexTrapData(
                "trap-far",
                new HexCoord(5, 0),
                0,
                new[] { new HexTrapEffectData(HexTrapEffectKind.Damage, 3) }));
            var runtime = new HexVisibilityRuntime(map, new HexCoord(0, 0), 2);

            // Player vision sweeping across the trap cell must reveal the fog but NOT the trap.
            runtime.RefreshTemporaryRevealArea(new HexCoord(5, 0), 2);
            Assert.That(runtime.GetVisibility(new HexCoord(5, 0)), Is.EqualTo(HexCellVisibility.Revealed));
            Assert.That(runtime.IsTrapRevealed(new HexCoord(5, 0)), Is.False);

            // Scouting the same area discovers the trap.
            runtime.RevealTrapsInArea(new HexCoord(5, 0), 2);
            Assert.That(runtime.IsTrapRevealed(new HexCoord(5, 0)), Is.True);

            // Moving away decays the cell fog to Hinted, but trap discovery is permanent.
            runtime.RefreshTemporaryRevealArea(new HexCoord(0, 0), 2);
            Assert.That(runtime.GetVisibility(new HexCoord(5, 0)), Is.EqualTo(HexCellVisibility.Hinted));
            Assert.That(runtime.IsTrapRevealed(new HexCoord(5, 0)), Is.True);
        }

        [Test]
        public void PermanentRevealSurvivesEnteringAndLeavingPlayerSight()
        {
            // 2026-09-05 실플레이 「아레나 안이 어둡다」: 영구 공개 칸이 시야에 들어왔다 나가면 Hinted로 강등됐다.
            var coord = new HexCoord(5, 0);
            var map = CreateTrapLineMap();
            var runtime = new HexVisibilityRuntime(map, new HexCoord(0, 0), 2);

            runtime.RevealPermanently(coord);
            Assert.That(runtime.GetVisibility(coord), Is.EqualTo(HexCellVisibility.Revealed));

            // 시야가 그 칸을 덮었다가(임시 목록에 실릴 자리) 떠난다.
            runtime.RefreshTemporaryRevealArea(coord, 2);
            runtime.RefreshTemporaryRevealArea(new HexCoord(0, 0), 2);
            Assert.That(runtime.GetVisibility(coord), Is.EqualTo(HexCellVisibility.Revealed),
                "영구 공개는 시야를 스친 뒤에도 Revealed로 남아야 한다.");
            Assert.That(runtime.GetVisibility(new HexCoord(4, 0)), Is.EqualTo(HexCellVisibility.Hinted),
                "이웃 일반 칸은 종전대로 강등된다(대조).");
            Assert.That(runtime.PermanentlyRevealedCoords, Has.Member(coord));

            // 복원 경로도 같은 성질을 지킨다.
            var resumed = new HexVisibilityRuntime(map, new HexCoord(0, 0), 2);
            resumed.SetVisibility(coord, HexCellVisibility.Revealed);
            resumed.RestorePermanentReveals(runtime.PermanentlyRevealedCoords);
            resumed.RefreshTemporaryRevealArea(coord, 2);
            resumed.RefreshTemporaryRevealArea(new HexCoord(0, 0), 2);
            Assert.That(resumed.GetVisibility(coord), Is.EqualTo(HexCellVisibility.Revealed));
        }

        [Test]
        public void ClearTrapRevealHidesDiscoveredTrapAndIsNoOpWhenUnrevealed()
        {
            var coord = new HexCoord(5, 0);
            var map = CreateTrapLineMap(new HexTrapData(
                "trap-x", coord, 0, new[] { new HexTrapEffectData(HexTrapEffectKind.Damage, 3) }));
            var runtime = new HexVisibilityRuntime(map, new HexCoord(0, 0), 2);

            runtime.RevealTrapsInArea(coord, 2);
            Assert.That(runtime.IsTrapRevealed(coord), Is.True);
            var versionAfterReveal = runtime.Version;

            // A consumed one-shot trap clears its reveal so the renderer (which folds TrapRevealed into
            // its per-cell visibility key) re-runs the cell pass and hides the marker.
            runtime.ClearTrapReveal(coord);
            Assert.That(runtime.IsTrapRevealed(coord), Is.False);
            Assert.That(runtime.Version, Is.GreaterThan(versionAfterReveal), "Clearing a discovered trap bumps Version so renderers rescan.");

            // Clearing an already-cleared (or never-revealed) coord is a no-op without Version churn.
            var versionAfterClear = runtime.Version;
            runtime.ClearTrapReveal(coord);
            Assert.That(runtime.Version, Is.EqualTo(versionAfterClear));
        }

        [Test]
        public void TriggeringScoutedOneShotTrapClearsItsRevealMarkerSource()
        {
            var trapCoord = new HexCoord(1, 0);
            var config = TestCombatConfigs.Standard(actionBudget: 4, movementHandSize: 1, actionHandSize: 15, playerVisionRange: 2);
            var map = CreateTrapLineMap(new HexTrapData(
                "trap-near", trapCoord, 0, new[] { new HexTrapEffectData(HexTrapEffectKind.Damage, 3) }));
            var state = new CombatState(
                map,
                new HexCoord(0, 0),
                new HexCoord(8, 0),
                config,
                cardCatalog: ApprovedCardCatalogFactory.CreateApprovedCatalog(config));

            // Discover the trap so its marker/tooltip source (TrapRevealed) is active.
            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            Assert.That(state.EndAction(), Is.True); // PlayerMovement -> MonsterMovement
            state.ResolveMonsterMovement(); // -> PlayerAction
            Assert.That(state.TryPlayerScout(trapCoord, ApprovedCardCatalogFactory.ScoutMinefinderId), Is.True);
            Assert.That(state.GetVisibilitySafeCellInfo(trapCoord).TrapRevealed, Is.True);

            // Walking onto the OneShot trap fires (and consumes) it; its reveal must clear so the marker disappears.
            Assert.That(state.TryDebugMovePlayer(trapCoord), Is.True);
            Assert.That(state.ConsumedTrapIds, Does.Contain("trap-near"));
            Assert.That(state.GetVisibilitySafeCellInfo(trapCoord).TrapRevealed, Is.False,
                "A consumed one-shot trap's reveal is cleared so the renderer hides its marker.");
        }

        [Test]
        public void ScoutCardRevealsTrapWhilePlayerVisionDoesNot()
        {
            var nearTrap = new HexCoord(1, 0);
            var farTrap = new HexCoord(6, 0);
            var config = TestCombatConfigs.Standard(actionBudget: 4, movementHandSize: 1, actionHandSize: 15, playerVisionRange: 2);
            var map = CreateTrapLineMap(
                new HexTrapData("trap-near", nearTrap, 0, new[] { new HexTrapEffectData(HexTrapEffectKind.Damage, 3) }),
                new HexTrapData("trap-far", farTrap, 0, new[] { new HexTrapEffectData(HexTrapEffectKind.Damage, 3) }));
            var state = new CombatState(
                map,
                new HexCoord(0, 0),
                new HexCoord(8, 0),
                config,
                cardCatalog: ApprovedCardCatalogFactory.CreateApprovedCatalog(config));

            // nearTrap sits inside player vision (Revealed) yet its trap stays hidden under sight alone.
            Assert.That(state.GetVisibility(nearTrap), Is.EqualTo(HexCellVisibility.Revealed));
            Assert.That(state.GetVisibilitySafeCellInfo(nearTrap).TrapRevealed, Is.False);

            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            Assert.That(state.EndAction(), Is.True); // PlayerMovement -> MonsterMovement
            state.ResolveMonsterMovement(); // -> PlayerAction
            Assert.That(state.TryPlayerScout(nearTrap, ApprovedCardCatalogFactory.ScoutMinefinderId), Is.True);

            Assert.That(state.GetVisibilitySafeCellInfo(nearTrap).TrapRevealed, Is.True);
            // A trap outside the scouted area remains hidden.
            Assert.That(state.GetVisibilitySafeCellInfo(farTrap).TrapRevealed, Is.False);
        }

        [Test]
        public void FieldObjectRevealsFootprintForItsDurationThenDecaysToHinted()
        {
            var config = TestCombatConfigs.Standard(actionBudget: 4, movementHandSize: 1, actionHandSize: 15, playerVisionRange: 2);
            var catalog = new CardCatalogDefinition(
                ApprovedCardCatalogFactory.SourceId + ".field-reveal-test",
                "Field reveal duration test catalog",
                new[]
                {
                    new CardCatalogEntry(ApprovedCardCatalogFactory.MoveBasicId, "Move", CardCategory.Movement, CardEffectType.Move, 1, 2, 2, CardEffectRefs.MoveBasic, "reachable_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry("F-REVEAL", "Sacred Lamp", CardCategory.Action, CardEffectType.FieldObject, 1, 5, 0, CardEffectRefs.FieldFogReveal, "walkable_map_cell", areaRadius: 1, fieldObjectKind: CardFieldObjectKind.FogReveal, durationTurns: 2, status: CardCatalogStatus.Approved)
                });
            var state = new CombatState(CreateTrapLineMap(), new HexCoord(0, 0), new HexCoord(8, 0), config, cardCatalog: catalog);

            var footprint = new HexCoord(4, 0);
            // Beyond the player's 2-cell sight, so only the field object can light it up.
            Assert.That(state.GetVisibility(footprint), Is.EqualTo(HexCellVisibility.Unknown));

            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            Assert.That(state.EndAction(), Is.True); // PlayerMovement -> MonsterMovement
            state.ResolveMonsterMovement(); // -> PlayerAction
            Assert.That(state.TryPlayerFieldObject(footprint, "F-REVEAL"), Is.True);

            // Casting installs the field object immediately, so its reveal footprint starts right away.
            Assert.That(state.GetVisibility(footprint), Is.EqualTo(HexCellVisibility.Revealed));
            Assert.That(state.PendingFieldObjects.Objects, Is.Empty);
            Assert.That(state.FieldObjects.Objects, Has.Count.EqualTo(1));

            // The next turn start ticks the active object but keeps its footprint revealed.
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();
            Assert.That(state.GetVisibility(footprint), Is.EqualTo(HexCellVisibility.Revealed));

            // After the duration elapses the object expires and the footprint decays to Hinted,
            // proving the reveal is temporary-for-duration rather than permanent.
            Assert.That(state.EndAction(), Is.True); // PlayerMovement -> MonsterMovement
            state.ResolveMonsterMovement(); // -> PlayerAction
            Assert.That(state.EndAction(), Is.True); // PlayerAction -> MonsterAction
            state.ResolveMonsterAction();
            Assert.That(state.GetVisibility(footprint), Is.EqualTo(HexCellVisibility.Hinted));
        }

        private static HexMapData CreateTrapLineMap(params HexTrapData[] traps)
        {
            var cells = new List<HexCellData>();
            for (var q = -1; q <= 8; q++)
            {
                cells.Add(new HexCellData(new HexCoord(q, 0), $"cell-{q}", "street", 1, true, false));
            }

            return new HexMapData(cells, trapRefs: traps);
        }

        private static CombatState CreateState()
        {
            // Pin the player vision range to the legacy 2-cell radius so this fog test stays
            // deterministic and independent of the game's default vision (now 7). See PlayerVisionRange.
            return new CombatState(CreateMap(), new HexCoord(0, 0), new HexCoord(2, 0), FogTestConfig);
        }

        private static CombatConfig FogTestConfig =>
            new CombatConfig(20, 10, 2, 1, 4, 4, 5, 1, 3, 3, 1, 4, playerVisionRange: 2);


        private static HexMapData CreateSensitiveMap()
        {
            return new HexMapData(new[]
            {
                new HexCellData(new HexCoord(0, 0), "start", "street", 1, true, false),
                new HexCellData(new HexCoord(1, 0), "near", "street", 1, true, false, eventId: "secret-hinted-event", landmarkId: "secret-hinted-landmark"),
                new HexCellData(new HexCoord(2, 0), "enemy", "street", 1, true, false),
                new HexCellData(new HexCoord(3, 0), "unknown", "street", 1, true, false, eventId: "secret-unknown-event", landmarkId: "secret-unknown-landmark")
            });
        }

        private static HexMapData CreateMap()
        {
            return new HexMapData(new[]
            {
                new HexCellData(new HexCoord(0, 0), "start", "street", 1, true, false),
                new HexCellData(new HexCoord(1, 0), "near", "street", 1, true, false),
                new HexCellData(new HexCoord(2, 0), "enemy", "street", 1, true, false),
                new HexCellData(new HexCoord(3, 0), "unknown", "street", 1, true, false)
            });
        }
    }
}

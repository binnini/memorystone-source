using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.MapDesign.Editor;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.Map.Unity.Tests.EditMode
{
    public sealed class HexSparseMapEditorSessionTests
    {
        [Test]
        public void PaletteFilterAllowsAtlasStylePrefixesAndHidesLegacyNames()
        {
            Assert.That(HexSparseMapEditorWindow.IsPaletteTilePresetId("b01_base1"), Is.True);
            Assert.That(HexSparseMapEditorWindow.IsPaletteTilePresetId("d01_base"), Is.True);
            Assert.That(HexSparseMapEditorWindow.IsPaletteTilePresetId("r02_curve"), Is.True);
            Assert.That(HexSparseMapEditorWindow.IsPaletteTilePresetId("w01-hanriver"), Is.True);
            Assert.That(HexSparseMapEditorWindow.IsPaletteTilePresetId("by01"), Is.True);

            Assert.That(HexSparseMapEditorWindow.IsPaletteTilePresetId("street"), Is.False);
            Assert.That(HexSparseMapEditorWindow.IsPaletteTilePresetId("start-neighborhood"), Is.False);
            Assert.That(HexSparseMapEditorWindow.IsPaletteTilePresetId("hanriver-water"), Is.False);
            Assert.That(HexSparseMapEditorWindow.IsPaletteTilePresetId("b_base"), Is.False);
        }

        [Test]
        public void PaintErasePickerAndValidateUseSparseSourceAndTilePresetCatalog()
        {
            using (var fixture = Fixture.Create())
            {
                var session = new HexSparseMapEditorSession
                {
                    Source = fixture.Source,
                    TilePresetCatalog = fixture.Presets,
                    ActiveTilePresetId = "street-tile",
                    DefaultFillTilePresetId = "street-tile"
                };

                Assert.That(session.Paint(new HexCoord(-1, 0), out var paintMessage), Is.True, paintMessage);
                Assert.That(fixture.Source.TryGetCell(new HexCoord(-1, 0), out var painted), Is.True);
                Assert.That(painted.TilePresetId, Is.EqualTo("street-tile"));

                Assert.That(session.Pick(new HexCoord(-1, 0), out var pickMessage), Is.True, pickMessage);
                Assert.That(session.ActiveTilePresetId, Is.EqualTo("street-tile"));

                Assert.That(session.Validate(out var validationMessages), Is.True, string.Join("\n", validationMessages));
                Assert.That(fixture.Source.GetPaintedBounds(), Is.EqualTo(new HexSparseMapBounds(-1, 0, -1, 0)));

                Assert.That(session.Erase(new HexCoord(-1, 0), out var eraseMessage), Is.True, eraseMessage);
                Assert.That(fixture.Source.CellCount, Is.EqualTo(0));
            }
        }

        [Test]
        public void PaintUsesActiveRotationStepsAndPickRestoresRotation()
        {
            using (var fixture = Fixture.Create())
            {
                var coord = new HexCoord(1, -1);
                var session = new HexSparseMapEditorSession
                {
                    Source = fixture.Source,
                    TilePresetCatalog = fixture.Presets,
                    ActiveTilePresetId = "street-tile",
                    ActiveRotationSteps = 5
                };

                Assert.That(session.Paint(coord, out var paintMessage), Is.True, paintMessage);
                Assert.That(fixture.Source.TryGetCell(coord, out var painted), Is.True);
                Assert.That(painted.RotationSteps, Is.EqualTo(5));

                session.ActiveRotationSteps = 0;
                Assert.That(session.Pick(coord, out var pickMessage), Is.True, pickMessage);

                Assert.That(session.ActiveTilePresetId, Is.EqualTo("street-tile"));
                Assert.That(session.ActiveRotationSteps, Is.EqualTo(5));
            }
        }

        [Test]
        public void MoveRotateAndCompositeStampPreserveTileData()
        {
            using (var fixture = Fixture.Create())
            {
                var origin = new HexCoord(0, 0);
                var east = new HexCoord(1, 0);
                var session = new HexSparseMapEditorSession
                {
                    Source = fixture.Source,
                    TilePresetCatalog = fixture.Presets,
                    ActiveTilePresetId = "street-tile",
                    ActiveRotationSteps = 1
                };
                Assert.That(session.Paint(origin, out _), Is.True);
                session.ActiveRotationSteps = 2;
                Assert.That(session.Paint(east, out _), Is.True);

                var captured = session.CaptureCompositeCells(new[] { origin, east }, origin);
                Assert.That(captured.Select(cell => cell.Coord), Is.EquivalentTo(new[] { new HexCoord(0, 0), new HexCoord(1, 0) }));

                Assert.That(session.MoveCells(new[] { origin, east }, new HexCoord(0, 1), out var moveMessage), Is.True, moveMessage);
                Assert.That(fixture.Source.TryGetCell(new HexCoord(0, 1), out _), Is.True);
                Assert.That(fixture.Source.TryGetCell(new HexCoord(1, 1), out _), Is.True);

                Assert.That(session.RotateCells(new[] { new HexCoord(0, 1), new HexCoord(1, 1) }, new HexCoord(0, 1), 1, out var rotateMessage), Is.True, rotateMessage);
                Assert.That(fixture.Source.TryGetCell(new HexCoord(0, 1), out var anchorCell), Is.True);
                Assert.That(fixture.Source.TryGetCell(new HexCoord(0, 2), out var rotatedCell), Is.True);
                Assert.That(anchorCell.RotationSteps, Is.EqualTo(2));
                Assert.That(rotatedCell.RotationSteps, Is.EqualTo(3));

                Assert.That(session.StampCompositeCells(new HexCoord(3, 0), captured, 1, out var stampMessage), Is.True, stampMessage);
                Assert.That(fixture.Source.TryGetCell(new HexCoord(3, 0), out var stampedAnchor), Is.True);
                Assert.That(fixture.Source.TryGetCell(new HexCoord(3, 1), out var stampedRotated), Is.True);
                Assert.That(stampedAnchor.RotationSteps, Is.EqualTo(2));
                Assert.That(stampedRotated.RotationSteps, Is.EqualTo(3));
            }
        }

        [Test]
        public void ValidateReportsMissingInputsAndUnresolvedPaintedCells()
        {
            var session = new HexSparseMapEditorSession();
            Assert.That(session.Validate(out var missingMessages), Is.False);
            Assert.That(missingMessages.Any(message => message.Contains("sparse map authoring source")), Is.True);
            Assert.That(missingMessages.Any(message => message.Contains("tile preset catalog")), Is.True);

            using (var fixture = Fixture.Create())
            {
                fixture.Source.SetCell(new HexCoord(0, 0), new HexSparseMapAuthoringCell(new HexCoord(0, 0), "missing", "street", "atlas-street"));
                session.Source = fixture.Source;
                session.TilePresetCatalog = fixture.Presets;

                Assert.That(session.Validate(out var messages), Is.False);
                Assert.That(messages.Any(message => message.Contains("no tile preset 'missing'")), Is.True);
            }
        }

        [Test]
        public void MonsterCatalogExposesImportedMonstersForEditorDropdown()
        {
            var session = new HexSparseMapEditorSession();

            Assert.That(session.MonsterCatalogSourceId, Does.Contain("designer-monster-csv"));
            Assert.That(session.MonsterCatalogMonsterIds, Is.SupersetOf(new[] { "M001", "M002", "M003", "M004", "M005", "M006" }));
            Assert.That(session.IsKnownMonsterId("M001"), Is.True);
            Assert.That(session.IsKnownMonsterId("M006"), Is.True);
        }

        [Test]
        public void PlaceObjectCreatesMonsterSpawnObjectRefOnPaintedCell()
        {
            using (var fixture = Fixture.Create())
            {
                var coord = new HexCoord(2, 0);
                var session = new HexSparseMapEditorSession
                {
                    Source = fixture.Source,
                    TilePresetCatalog = fixture.Presets,
                    ActiveTilePresetId = "street-tile",
                    ActiveObjectId = "enemy-spawn",
                    ActiveObjectType = HexMapObjectType.MonsterSpawn,
                    ActiveObjectRef = "M001",
                    ActiveObjectRole = "primary_pressure",
                    ActiveObjectPurpose = HexMapPurpose.PlayableMap
                };
                Assert.That(session.Paint(coord, out var paintMessage), Is.True, paintMessage);

                Assert.That(session.PlaceObject(coord, out var placeMessage), Is.True, placeMessage);

                var objectRef = fixture.Source.ObjectRefs.Single();
                Assert.That(objectRef.ObjectId, Is.EqualTo("enemy-spawn"));
                Assert.That(objectRef.ObjectType, Is.EqualTo(HexMapObjectType.MonsterSpawn));
                Assert.That(objectRef.ObjectRef, Is.EqualTo("M001"));
                Assert.That(objectRef.Coord, Is.EqualTo(coord));
                Assert.That(objectRef.Role, Is.EqualTo("primary_pressure"));
                Assert.That(objectRef.EnabledForPurpose, Is.EqualTo(HexMapPurpose.PlayableMap));
                Assert.That(objectRef.PatrolAreaId, Is.EqualTo("patrol-enemy-spawn"));
                Assert.That(fixture.Source.PatrolAreaRefs.Single().PatrolAreaId, Is.EqualTo("patrol-enemy-spawn"));
                Assert.That(fixture.Source.TryToHexMapData(out var map, out var error), Is.True, error);
                Assert.That(map.MonsterSpawnRefs.Single().Id, Is.EqualTo("enemy-spawn"));
            }
        }

        [Test]
        public void PlaceMonsterSpawnAutoGeneratesDefaultRadiusThreePatrolArea()
        {
            using (var fixture = Fixture.Create())
            {
                var center = new HexCoord(0, 0);
                var within = new HexCoord(3, 0);
                var outside = new HexCoord(4, 0);
                var session = new HexSparseMapEditorSession
                {
                    Source = fixture.Source,
                    TilePresetCatalog = fixture.Presets,
                    ActiveTilePresetId = "street-tile",
                    ActiveObjectType = HexMapObjectType.MonsterSpawn,
                    ActiveObjectRef = "M001"
                };
                Assert.That(session.Paint(center, out _), Is.True);
                Assert.That(session.Paint(within, out _), Is.True);
                Assert.That(session.Paint(outside, out _), Is.True);

                Assert.That(session.PlaceObject(center, out var placeMessage), Is.True, placeMessage);

                var spawn = fixture.Source.ObjectRefs.Single();
                Assert.That(spawn.ObjectId, Is.EqualTo("monster-spawn-0-0"));
                Assert.That(spawn.PatrolAreaId, Is.EqualTo("patrol-monster-spawn-0-0"));
                var area = fixture.Source.PatrolAreaRefs.Single();
                Assert.That(area.PatrolAreaId, Is.EqualTo(spawn.PatrolAreaId));
                Assert.That(area.Cells.Select(cell => cell.Coord), Is.EquivalentTo(new[] { center, within }));
                Assert.That(area.Cells.Select(cell => cell.Coord).Contains(outside), Is.False);
            }
        }

        [Test]
        public void PlaceMonsterSpawnUsesPalettePatrolAreaIdAndRadius()
        {
            using (var fixture = Fixture.Create())
            {
                var center = new HexCoord(0, 0);
                var within = new HexCoord(1, 0);
                var outside = new HexCoord(2, 0);
                var session = new HexSparseMapEditorSession
                {
                    Source = fixture.Source,
                    TilePresetCatalog = fixture.Presets,
                    ActiveTilePresetId = "street-tile",
                    ActiveObjectType = HexMapObjectType.MonsterSpawn,
                    ActiveObjectRef = "M001",
                    ActiveObjectPatrolAreaId = "custom-patrol",
                    ActivePatrolAreaRadius = 1
                };
                Assert.That(session.Paint(center, out _), Is.True);
                Assert.That(session.Paint(within, out _), Is.True);
                Assert.That(session.Paint(outside, out _), Is.True);

                Assert.That(session.PlaceObject(center, out var placeMessage), Is.True, placeMessage);

                var spawn = fixture.Source.ObjectRefs.Single();
                Assert.That(spawn.PatrolAreaId, Is.EqualTo("custom-patrol"));
                Assert.That(session.ActivePatrolAreaId, Is.EqualTo("custom-patrol"));
                var area = fixture.Source.PatrolAreaRefs.Single();
                Assert.That(area.PatrolAreaId, Is.EqualTo("custom-patrol"));
                Assert.That(area.Cells.Select(cell => cell.Coord), Is.EquivalentTo(new[] { center, within }));
                Assert.That(area.Cells.Select(cell => cell.Coord).Contains(outside), Is.False);
            }
        }

        [Test]
        public void PlaceObjectCarriesTheBrushFreeAngleYawWithoutMovingTheFootprint()
        {
            using (var fixture = Fixture.Create())
            {
                var center = new HexCoord(0, 0);
                var neighbour = new HexCoord(1, 0);
                var session = new HexSparseMapEditorSession
                {
                    Source = fixture.Source,
                    TilePresetCatalog = fixture.Presets,
                    ActiveTilePresetId = "street-tile",
                    ActiveObjectType = HexMapObjectType.Building,
                    ActiveObjectRef = "building-a",
                    ActiveObjectFootprintOffsets = new[] { new HexCoord(0, 0), new HexCoord(1, 0) },
                };
                Assert.That(session.Paint(center, out _), Is.True);
                Assert.That(session.Paint(neighbour, out _), Is.True);

                // 8° is not on the hex lattice: it must land as step 0 + 8° fine, leaving the footprint put.
                session.ActiveYawDegrees = 8f;
                Assert.That(session.ActiveRotationSteps, Is.EqualTo(0));
                Assert.That(session.PlaceObject(center, out var placeMessage), Is.True, placeMessage);

                var placed = fixture.Source.ObjectRefs.Single();
                Assert.That(placed.RotationSteps, Is.EqualTo(0));
                Assert.That(placed.RotationFineDegrees, Is.EqualTo(8f).Within(0.001f));
                Assert.That(placed.YawDegrees, Is.EqualTo(8f).Within(0.001f));
                Assert.That(
                    placed.OccupiedCoords,
                    Is.EquivalentTo(new[] { center, neighbour }),
                    "A free-angle nudge is visual only — the footprint must stay on the hex lattice.");
            }
        }

        [Test]
        public void SetObjectRotationStoresFreeAngleAndKeepsEveryOtherAuthoredField()
        {
            using (var fixture = Fixture.Create())
            {
                var center = new HexCoord(0, 0);
                var session = new HexSparseMapEditorSession
                {
                    Source = fixture.Source,
                    TilePresetCatalog = fixture.Presets,
                    ActiveTilePresetId = "street-tile",
                    ActiveObjectType = HexMapObjectType.Building,
                    ActiveObjectRef = "building-a",
                    ActiveObjectBlocksMovement = true,
                    ActiveObjectVisualScaleMultiplier = new Vector3(1.5f, 1.5f, 1.5f),
                };
                Assert.That(session.Paint(center, out _), Is.True);
                Assert.That(session.PlaceObject(center, out _), Is.True);
                var objectId = fixture.Source.ObjectRefs.Single().ObjectId;

                Assert.That(session.SetObjectRotation(objectId, 2, -12.5f, out var rotateMessage), Is.True, rotateMessage);

                var rotated = fixture.Source.ObjectRefs.Single();
                Assert.That(rotated.RotationSteps, Is.EqualTo(2));
                Assert.That(rotated.RotationFineDegrees, Is.EqualTo(-12.5f).Within(0.001f));
                Assert.That(rotated.YawDegrees, Is.EqualTo(107.5f).Within(0.001f));
                Assert.That(rotated.BlocksMovement, Is.True, "Rotating must not drop other authored fields.");
                Assert.That(rotated.VisualScaleMultiplier.x, Is.EqualTo(1.5f).Within(0.001f));
            }
        }

        [Test]
        public void MoveObjectRelocatesOntoPaintedCellsAndRefusesUnpaintedOnes()
        {
            using (var fixture = Fixture.Create())
            {
                var center = new HexCoord(0, 0);
                var target = new HexCoord(1, 0);
                var unpainted = new HexCoord(5, 0);
                var session = new HexSparseMapEditorSession
                {
                    Source = fixture.Source,
                    TilePresetCatalog = fixture.Presets,
                    ActiveTilePresetId = "street-tile",
                    ActiveObjectType = HexMapObjectType.Building,
                    ActiveObjectRef = "building-a",
                };
                Assert.That(session.Paint(center, out _), Is.True);
                Assert.That(session.Paint(target, out _), Is.True);
                Assert.That(session.PlaceObject(center, out _), Is.True);
                var objectId = fixture.Source.ObjectRefs.Single().ObjectId;

                Assert.That(session.MoveObject(objectId, target, out var moveMessage), Is.True, moveMessage);
                Assert.That(fixture.Source.ObjectRefs.Single().Coord, Is.EqualTo(target));

                Assert.That(
                    session.MoveObject(objectId, unpainted, out var rejectMessage),
                    Is.False,
                    "An object may not be moved onto an unpainted cell.");
                Assert.That(rejectMessage, Does.Contain(unpainted.ToString()));
                Assert.That(fixture.Source.ObjectRefs.Single().Coord, Is.EqualTo(target), "A refused move must not move anything.");
            }
        }

        [Test]
        public void PlaceObjectCreatesPlayerSpawnObjectRefWithoutManualIdOrObjectRef()
        {
            using (var fixture = Fixture.Create())
            {
                var coord = new HexCoord(1, -1);
                var session = new HexSparseMapEditorSession
                {
                    Source = fixture.Source,
                    TilePresetCatalog = fixture.Presets,
                    ActiveTilePresetId = "street-tile",
                    ActiveObjectId = " ",
                    ActiveObjectType = HexMapObjectType.PlayerSpawn,
                    ActiveObjectRef = "ignored-by-player-spawn",
                    ActiveObjectPurpose = HexMapPurpose.PlayableMap
                };
                Assert.That(session.Paint(coord, out _), Is.True);

                Assert.That(session.PlaceObject(coord, out var placeMessage), Is.True, placeMessage);

                var objectRef = fixture.Source.ObjectRefs.Single();
                Assert.That(objectRef.ObjectId, Is.EqualTo("player-spawn-1-n1"));
                Assert.That(objectRef.ObjectType, Is.EqualTo(HexMapObjectType.PlayerSpawn));
                Assert.That(objectRef.ObjectRef, Is.Empty);
                Assert.That(objectRef.Coord, Is.EqualTo(coord));
                Assert.That(objectRef.EnabledForPurpose, Is.EqualTo(HexMapPurpose.PlayableMap));
            }
        }

        [Test]
        public void PlaceVictoryCameraPointAssignsNextOrderAutomatically()
        {
            using (var fixture = Fixture.Create())
            {
                var first = new HexCoord(0, 0);
                var second = new HexCoord(1, 0);
                var session = new HexSparseMapEditorSession
                {
                    Source = fixture.Source,
                    TilePresetCatalog = fixture.Presets,
                    ActiveTilePresetId = "street-tile",
                    ActiveObjectType = HexMapObjectType.VictoryCameraPoint,
                    ActiveObjectRole = "manual-ignored",
                    ActiveObjectRef = string.Empty,
                    ActiveObjectPurpose = HexMapPurpose.PlayableMap
                };
                Assert.That(session.Paint(first, out _), Is.True);
                Assert.That(session.Paint(second, out _), Is.True);

                Assert.That(session.PlaceObject(first, out var firstMessage), Is.True, firstMessage);
                Assert.That(session.PlaceObject(second, out var secondMessage), Is.True, secondMessage);

                var points = fixture.Source.ObjectRefs
                    .Where(objectRef => objectRef.IsVictoryCameraPoint)
                    .OrderBy(objectRef => objectRef.Coord)
                    .ToArray();
                Assert.That(points.Select(point => point.Role), Is.EqualTo(new[] { "00", "01" }));
                Assert.That(points.Select(point => point.ObjectRef), Is.EqualTo(new[] { "victory-camera-point", "victory-camera-point" }));
                Assert.That(session.FormatNextVictoryCameraPointRole(), Is.EqualTo("02"));
                Assert.That(session.ActiveObjectRole, Is.EqualTo("02"));
            }
        }

        [Test]
        public void RemoveAllVictoryCameraPointsKeepsOtherObjects()
        {
            using (var fixture = Fixture.Create())
            {
                var first = new HexCoord(0, 0);
                var second = new HexCoord(1, 0);
                var building = new HexCoord(2, 0);
                fixture.Source.ConfigureForTests(
                    new[]
                    {
                        new HexSparseMapAuthoringCell(first, "street-tile", "street", "atlas-street"),
                        new HexSparseMapAuthoringCell(second, "street-tile", "street", "atlas-street"),
                        new HexSparseMapAuthoringCell(building, "street-tile", "street", "atlas-street")
                    },
                    HexMapPurpose.PlayableMap,
                    objectRefs: new[]
                    {
                        new HexMapObjectRef("camera-00", HexMapObjectType.VictoryCameraPoint, "victory-camera-point", first.Q, first.R, "00"),
                        new HexMapObjectRef("camera-01", HexMapObjectType.VictoryCameraPoint, "victory-camera-point", second.Q, second.R, "01"),
                        new HexMapObjectRef("building-a", HexMapObjectType.Building, "bench-a", building.Q, building.R)
                    });
                var session = new HexSparseMapEditorSession
                {
                    Source = fixture.Source,
                    ActiveObjectType = HexMapObjectType.VictoryCameraPoint
                };

                Assert.That(session.RemoveAllVictoryCameraPoints(out var removeMessage), Is.True, removeMessage);

                Assert.That(fixture.Source.ObjectRefs.Select(objectRef => objectRef.ObjectId), Is.EqualTo(new[] { "building-a" }));
                Assert.That(session.VictoryCameraPointCount, Is.Zero);
                Assert.That(session.ActiveObjectRole, Is.EqualTo("00"));
            }
        }

        [Test]
        public void PlaceObjectStoresPropPrefabIdWithoutMonsterPatrolArea()
        {
            using (var fixture = Fixture.Create())
            {
                var coord = new HexCoord(3, -1);
                var session = new HexSparseMapEditorSession
                {
                    Source = fixture.Source,
                    TilePresetCatalog = fixture.Presets,
                    ActiveTilePresetId = "street-tile",
                    ActiveObjectType = HexMapObjectType.Building,
                    ActiveObjectRef = "skyscraper_B_1952",
                    ActiveObjectRole = "building",
                    ActiveObjectPurpose = HexMapPurpose.PlayableMap,
                    ActiveObjectBlocksMovement = true,
                    ActiveObjectBlocksVision = true,
                    ActiveRotationSteps = 2
                };
                Assert.That(session.Paint(coord, out _), Is.True);

                Assert.That(session.PlaceObject(coord, out var placeMessage), Is.True, placeMessage);

                var objectRef = fixture.Source.ObjectRefs.Single();
                Assert.That(objectRef.ObjectId, Is.EqualTo("building-3-n1"));
                Assert.That(objectRef.ObjectType, Is.EqualTo(HexMapObjectType.Building));
                Assert.That(objectRef.ObjectRef, Is.EqualTo("skyscraper_B_1952"));
                Assert.That(objectRef.Coord, Is.EqualTo(coord));
                Assert.That(objectRef.Role, Is.EqualTo("building"));
                Assert.That(objectRef.BlocksMovement, Is.True);
                Assert.That(objectRef.BlocksVision, Is.True);
                Assert.That(objectRef.RotationSteps, Is.EqualTo(2));
                Assert.That(objectRef.PatrolAreaId, Is.Empty);
                Assert.That(fixture.Source.PatrolAreaRefs, Is.Empty);
            }
        }

        [Test]
        public void ObjectTypeDefaultsSetBuildingBlockingAndTreasureChestInteraction()
        {
            var session = new HexSparseMapEditorSession();

            session.ApplyObjectTypeDefaults(HexMapObjectType.Building);
            Assert.That(session.ActiveObjectBlocksMovement, Is.True);
            Assert.That(session.ActiveObjectBlocksVision, Is.True);
            Assert.That(session.ActiveObjectInteractable, Is.False);

            session.ApplyObjectTypeDefaults(HexMapObjectType.TreasureChest);
            Assert.That(session.ActiveObjectBlocksMovement, Is.False);
            Assert.That(session.ActiveObjectBlocksVision, Is.False);
            Assert.That(session.ActiveObjectInteractable, Is.True);
        }

        [Test]
        public void PlaceObjectStoresFootprintAndVisualScaleAndRequiresPaintedFootprint()
        {
            using (var fixture = Fixture.Create())
            {
                var anchor = new HexCoord(0, 0);
                var occupied = new HexCoord(1, 0);
                var session = new HexSparseMapEditorSession
                {
                    Source = fixture.Source,
                    TilePresetCatalog = fixture.Presets,
                    ActiveTilePresetId = "street-tile",
                    ActiveObjectType = HexMapObjectType.Building,
                    ActiveObjectRef = "wide-building",
                    ActiveObjectBlocksMovement = true,
                    ActiveObjectBlocksVision = true,
                    ActiveObjectFootprintOffsets = new[] { new HexCoord(0, 0), new HexCoord(1, 0) },
                    ActiveObjectVisualScaleMultiplier = new Vector3(1.25f, 1.5f, 0.75f)
                };
                Assert.That(session.Paint(anchor, out _), Is.True);

                Assert.That(session.PlaceObject(anchor, out var missingFootprintMessage), Is.False);
                Assert.That(missingFootprintMessage, Does.Contain(occupied.ToString()));

                Assert.That(session.Paint(occupied, out _), Is.True);
                Assert.That(session.PlaceObject(anchor, out var placeMessage), Is.True, placeMessage);

                var objectRef = fixture.Source.ObjectRefs.Single();
                Assert.That(objectRef.FootprintOffsets, Is.EquivalentTo(new[] { new HexCoord(0, 0), new HexCoord(1, 0) }));
                Assert.That(objectRef.OccupiedCoords, Is.EquivalentTo(new[] { anchor, occupied }));
                Assert.That(objectRef.VisualScaleMultiplier, Is.EqualTo(new Vector3(1.25f, 1.5f, 0.75f)));
            }
        }

        [Test]
        public void SetObjectRotationUpdatesExistingObjectRef()
        {
            using (var fixture = Fixture.Create())
            {
                var coord = new HexCoord(2, 1);
                fixture.Source.SetCell(coord, new HexSparseMapAuthoringCell(coord, "street-tile", "street", "atlas-street"));
                fixture.Source.SetObjectRef(new HexMapObjectRef("building-a", HexMapObjectType.Building, "bench-a", coord.Q, coord.R));
                var session = new HexSparseMapEditorSession { Source = fixture.Source };

                Assert.That(session.SetObjectRotation("building-a", 5, out var rotateMessage), Is.True, rotateMessage);

                Assert.That(fixture.Source.TryGetObjectRef("building-a", out var objectRef), Is.True);
                Assert.That(objectRef.RotationSteps, Is.EqualTo(5));
                Assert.That(objectRef.Coord, Is.EqualTo(coord));
                Assert.That(objectRef.ObjectRef, Is.EqualTo("bench-a"));
            }
        }

        [Test]
        public void ObjectPrefabCatalogFindsAndLoadsPrefabIdsFromObjectFolder()
        {
            const string prefabId = "zz_test_object_prefab_catalog";
            const string prefabPath = MapObjectPrefabCatalog.DefaultFolder + "/" + prefabId + ".prefab";
            var source = new GameObject(prefabId);

            try
            {
                if (!AssetDatabase.IsValidFolder(MapObjectPrefabCatalog.DefaultFolder))
                {
                    AssetDatabase.CreateFolder("Assets/Prefabs", "Object");
                }

                PrefabUtility.SaveAsPrefabAsset(source, prefabPath);
                AssetDatabase.Refresh();

                Assert.That(MapObjectPrefabCatalog.GetPrefabIds(), Does.Contain(prefabId));
                Assert.That(MapObjectPrefabCatalog.TryLoadPrefab(prefabId, out var prefab), Is.True);
                Assert.That(prefab, Is.Not.Null);
                Assert.That(prefab.name, Is.EqualTo(prefabId));
            }
            finally
            {
                Object.DestroyImmediate(source);
                AssetDatabase.DeleteAsset(prefabPath);
                AssetDatabase.Refresh();
            }
        }

        [Test]
        public void ObjectPrefabCatalogPreviewRulesIncludeMonsterSpawnObjects()
        {
            Assert.That(MapObjectPrefabCatalog.IsPreviewableObjectType(HexMapObjectType.Building), Is.True);
            Assert.That(MapObjectPrefabCatalog.IsPreviewableObjectType(HexMapObjectType.Landmark), Is.True);
            Assert.That(MapObjectPrefabCatalog.IsPreviewableObjectType(HexMapObjectType.TreasureChest), Is.True);
            Assert.That(MapObjectPrefabCatalog.IsPreviewableObjectType(HexMapObjectType.MonsterSpawn), Is.True);
            Assert.That(MapObjectPrefabCatalog.IsPreviewableObjectType(HexMapObjectType.PlayerSpawn), Is.False);
            Assert.That(MapObjectPrefabCatalog.IsPreviewableObjectType(HexMapObjectType.ObjectiveMarker), Is.False);
            Assert.That(MapObjectPrefabCatalog.IsPreviewableObjectType(HexMapObjectType.EventTrigger), Is.False);
            Assert.That(MapObjectPrefabCatalog.IsPreviewableObjectType(HexMapObjectType.Trap), Is.False);
        }

        [Test]
        public void ObjectPrefabCatalogExposesMonsterCatalogVisualDefinitions()
        {
            var definitions = MapObjectPrefabCatalog.GetDefinitions();

            var monsterDefinitions = definitions
                .Where(definition => definition.ObjectType == HexMapObjectType.MonsterSpawn)
                .ToArray();

            Assert.That(monsterDefinitions.Select(definition => definition.ObjectRef), Is.SupersetOf(new[] { "M001", "M002", "M003", "M004", "M005", "M006" }));
            foreach (var definition in monsterDefinitions.Where(definition => definition.ObjectRef.StartsWith("M00")))
            {
                Assert.That(definition.Prefab, Is.Not.Null, definition.ObjectRef);
                Assert.That(definition.VisualScaleMultiplier, Is.EqualTo(Vector3.one), "Monster catalog definitions should preserve prefab-authored scale and only apply explicit object multipliers on top.");
            }
        }

        [Test]
        public void MonsterCatalogVisualDefinitionsPreservePrefabAuthoredScale()
        {
            var definitions = MapObjectPrefabCatalog.GetDefinitionsForObjectType(HexMapObjectType.MonsterSpawn)
                .ToDictionary(definition => definition.ObjectRef);

            Assert.That(definitions["M002"].Prefab.transform.localScale, Is.EqualTo(new Vector3(5f, 5f, 5f)));
            Assert.That(definitions["M003"].Prefab.transform.localScale, Is.EqualTo(new Vector3(0.8f, 0.8f, 0.8f)));
            Assert.That(definitions["M004"].Prefab.transform.localScale, Is.EqualTo(new Vector3(0.8f, 0.8f, 0.8f)));
            Assert.That(definitions["M005"].Prefab.transform.localScale, Is.EqualTo(new Vector3(3f, 3f, 3f)));
            Assert.That(definitions["M006"].Prefab.transform.localScale, Is.EqualTo(new Vector3(3f, 3f, 3f)));
        }
        [Test]
        public void UpdateAndDeleteObjectRefSupportObjectListEditing()
        {
            using (var fixture = Fixture.Create())
            {
                var coord = new HexCoord(2, 0);
                var session = new HexSparseMapEditorSession
                {
                    Source = fixture.Source,
                    TilePresetCatalog = fixture.Presets,
                    ActiveTilePresetId = "street-tile",
                    ActiveObjectId = "enemy-spawn",
                    ActiveObjectType = HexMapObjectType.MonsterSpawn,
                    ActiveObjectRef = "M001",
                    ActiveObjectRole = "primary_pressure",
                    ActiveObjectPurpose = HexMapPurpose.PlayableMap
                };
                Assert.That(session.Paint(coord, out var paintMessage), Is.True, paintMessage);
                Assert.That(session.PlaceObject(coord, out var placeMessage), Is.True, placeMessage);

                Assert.That(session.UpdateObjectRef(
                    "enemy-spawn",
                    "edited-building",
                    HexMapObjectType.Building,
                    "bench-a",
                    "cover",
                    HexMapPurpose.SmokeMap,
                    blocksMovement: true,
                    blocksVision: false,
                    interactable: true,
                    out var updateMessage), Is.True, updateMessage);

                Assert.That(fixture.Source.ObjectRefs, Has.Count.EqualTo(1));
                var edited = fixture.Source.ObjectRefs.Single();
                Assert.That(edited.ObjectId, Is.EqualTo("edited-building"));
                Assert.That(edited.ObjectType, Is.EqualTo(HexMapObjectType.Building));
                Assert.That(edited.ObjectRef, Is.EqualTo("bench-a"));
                Assert.That(edited.Coord, Is.EqualTo(coord));
                Assert.That(edited.Role, Is.EqualTo("cover"));
                Assert.That(edited.EnabledForPurpose, Is.EqualTo(HexMapPurpose.SmokeMap));
                Assert.That(edited.BlocksMovement, Is.True);
                Assert.That(edited.BlocksVision, Is.False);
                Assert.That(edited.Interactable, Is.True);
                Assert.That(fixture.Source.TryGetObjectRef("edited-building", out var found), Is.True);
                Assert.That(found.ObjectRef, Is.EqualTo("bench-a"));

                Assert.That(session.DeleteObjectRef("edited-building", out var deleteMessage), Is.True, deleteMessage);
                Assert.That(fixture.Source.ObjectRefCount, Is.Zero);
            }
        }

        [Test]
        public void UpdateObjectRefRejectsMissingRequiredFieldsAndDuplicateIds()
        {
            using (var fixture = Fixture.Create())
            {
                var firstCoord = new HexCoord(1, 0);
                var secondCoord = new HexCoord(2, 0);
                fixture.Source.SetCell(firstCoord, new HexSparseMapAuthoringCell(firstCoord, "street-tile", "street", "atlas-street"));
                fixture.Source.SetCell(secondCoord, new HexSparseMapAuthoringCell(secondCoord, "street-tile", "street", "atlas-street"));
                fixture.Source.SetObjectRef(new HexMapObjectRef("first", HexMapObjectType.MonsterSpawn, "M001", firstCoord.Q, firstCoord.R));
                fixture.Source.SetObjectRef(new HexMapObjectRef("second", HexMapObjectType.Building, "bench-a", secondCoord.Q, secondCoord.R));
                var session = new HexSparseMapEditorSession { Source = fixture.Source };

                Assert.That(session.UpdateObjectRef("first", " ", HexMapObjectType.Building, "bench-a", "role", HexMapPurpose.PlayableMap, false, false, false, out var missingIdMessage), Is.False);
                Assert.That(missingIdMessage, Does.Contain("id is required"));

                Assert.That(session.UpdateObjectRef("first", "second", HexMapObjectType.Building, "bench-a", "role", HexMapPurpose.PlayableMap, false, false, false, out var duplicateMessage), Is.False);
                Assert.That(duplicateMessage, Does.Contain("already exists"));

                Assert.That(session.UpdateObjectRef("first", "first", HexMapObjectType.MonsterSpawn, " ", "role", HexMapPurpose.PlayableMap, false, false, false, out var missingRefMessage), Is.False);
                Assert.That(missingRefMessage, Does.Contain("requires an objectRef monster id"));
                Assert.That(fixture.Source.ObjectRefs.Select(objectRef => objectRef.ObjectId), Is.EquivalentTo(new[] { "first", "second" }));
            }
        }


        [Test]
        public void PatrolAreaBrushPaintsAndErasesPaintedCells()
        {
            using (var fixture = Fixture.Create())
            {
                var coord = new HexCoord(2, 0);
                var session = new HexSparseMapEditorSession
                {
                    Source = fixture.Source,
                    TilePresetCatalog = fixture.Presets,
                    ActiveTilePresetId = "street-tile",
                    ActivePatrolAreaId = "patrol-a"
                };
                Assert.That(session.Paint(coord, out var paintMessage), Is.True, paintMessage);

                Assert.That(session.PaintPatrolArea(coord, out var patrolMessage), Is.True, patrolMessage);
                Assert.That(fixture.Source.PatrolAreaRefs.Single().PatrolAreaId, Is.EqualTo("patrol-a"));
                Assert.That(fixture.Source.PatrolAreaRefs.Single().Cells.Single().Coord, Is.EqualTo(coord));

                Assert.That(session.ErasePatrolArea(coord, out var eraseMessage), Is.True, eraseMessage);
                Assert.That(fixture.Source.PatrolAreaRefs, Is.Empty);
            }
        }

        [Test]
        public void GeneratePatrolAreaAroundMonsterSpawnAssignsSpawnAndUsesPaintedRadius()
        {
            using (var fixture = Fixture.Create())
            {
                var center = new HexCoord(0, 0);
                var near = new HexCoord(1, 0);
                var far = new HexCoord(3, 0);
                var session = new HexSparseMapEditorSession
                {
                    Source = fixture.Source,
                    TilePresetCatalog = fixture.Presets,
                    ActiveTilePresetId = "street-tile",
                    ActivePatrolAreaId = "spawn-patrol",
                    ActiveObjectPatrolAreaId = "spawn-patrol",
                    ActiveObjectId = "spawn-a",
                    ActiveObjectType = HexMapObjectType.MonsterSpawn,
                    ActiveObjectRef = "M001"
                };
                Assert.That(session.Paint(center, out _), Is.True);
                Assert.That(session.Paint(near, out _), Is.True);
                Assert.That(session.Paint(far, out _), Is.True);
                Assert.That(session.PlaceObject(center, out var placeMessage), Is.True, placeMessage);

                Assert.That(session.GeneratePatrolAreaAroundObject("spawn-a", 1, out var generateMessage), Is.True, generateMessage);

                var area = fixture.Source.PatrolAreaRefs.Single();
                Assert.That(area.PatrolAreaId, Is.EqualTo("spawn-patrol"));
                Assert.That(area.Cells.Select(cell => cell.Coord), Is.EquivalentTo(new[] { center, near }));
                Assert.That(fixture.Source.TryGetObjectRef("spawn-a", out var spawn), Is.True);
                Assert.That(spawn.PatrolAreaId, Is.EqualTo("spawn-patrol"));
                Assert.That(far.DistanceTo(center), Is.GreaterThan(1));
            }
        }

        [Test]
        public void ValidateReportsMissingCatalogMonsterPurposeMismatchAndDuplicateObjectRefs()
        {
            using (var fixture = Fixture.Create())
            {
                var firstCoord = new HexCoord(1, 0);
                var secondCoord = new HexCoord(2, 0);
                fixture.Source.ConfigureForTests(
                    new[]
                    {
                        new HexSparseMapAuthoringCell(firstCoord, "street-tile", "street", "atlas-street"),
                        new HexSparseMapAuthoringCell(secondCoord, "street-tile", "street", "atlas-street")
                    },
                    HexMapPurpose.PlayableMap,
                    objectRefs: new[]
                    {
                        new HexMapObjectRef("duplicate", HexMapObjectType.MonsterSpawn, "missing-monster", firstCoord.Q, firstCoord.R, "primary_pressure", HexMapPurpose.SmokeMap),
                        new HexMapObjectRef("duplicate", HexMapObjectType.MonsterSpawn, "M001", firstCoord.Q, firstCoord.R, "secondary_pressure", HexMapPurpose.PlayableMap),
                        new HexMapObjectRef("unique", HexMapObjectType.MonsterSpawn, "M001", secondCoord.Q, secondCoord.R, "secondary_pressure", HexMapPurpose.PlayableMap)
                    });
                var session = new HexSparseMapEditorSession
                {
                    Source = fixture.Source,
                    TilePresetCatalog = fixture.Presets
                };

                Assert.That(session.Validate(out var messages), Is.False);
                Assert.That(messages.Any(message => message.Contains("Duplicate map object id 'duplicate'")), Is.True, string.Join("\n", messages));
                Assert.That(messages.Any(message => message.Contains("Duplicate monster spawn object coordinate") && message.Contains(firstCoord.ToString())), Is.True, string.Join("\n", messages));
                Assert.That(messages.Any(message => message.Contains("references missing catalog monster 'missing-monster'")), Is.True, string.Join("\n", messages));
                Assert.That(messages.Any(message => message.Contains("purpose SmokeMap does not match board purpose PlayableMap")), Is.True, string.Join("\n", messages));
            }
        }

        [Test]
        public void PlaceObjectRequiresPaintedCellAndCanEraseObjects()
        {
            using (var fixture = Fixture.Create())
            {
                var coord = new HexCoord(1, 0);
                var session = new HexSparseMapEditorSession
                {
                    Source = fixture.Source,
                    ActiveObjectType = HexMapObjectType.MonsterSpawn,
                    ActiveObjectRef = "M001"
                };

                Assert.That(session.PlaceObject(coord, out var missingCellMessage), Is.False);
                Assert.That(missingCellMessage, Does.Contain("Paint a sparse cell"));

                fixture.Source.SetCell(coord, new HexSparseMapAuthoringCell(coord, "street-tile", "street", "atlas-street"));
                Assert.That(session.PlaceObject(coord, out var placeMessage), Is.True, placeMessage);
                Assert.That(fixture.Source.ObjectRefCount, Is.EqualTo(1));

                Assert.That(session.EraseObject(coord, out var eraseMessage), Is.True, eraseMessage);
                Assert.That(fixture.Source.ObjectRefCount, Is.Zero);
            }
        }

        [Test]
        public void PlaceTrapCreatesRuntimeTrapRefOnPaintedCell()
        {
            using (var fixture = Fixture.Create())
            {
                var coord = new HexCoord(1, 0);
                var session = new HexSparseMapEditorSession
                {
                    Source = fixture.Source,
                    TilePresetCatalog = fixture.Presets,
                    ActiveTilePresetId = "street-tile",
                    ActiveTrapId = "burn-trap",
                    ActiveTrapRadius = 2,
                    ActiveTrapEffectKind = HexTrapEffectKind.Poison,
                    ActiveTrapEffectAmount = 3,
                    ActiveTrapEffectDurationTurns = 4,
                    ActiveTrapAffectsPlayer = true,
                    ActiveTrapAffectsMonsters = true,
                    ActiveTrapOneShot = false
                };
                Assert.That(session.Paint(coord, out var paintMessage), Is.True, paintMessage);

                Assert.That(session.PlaceTrap(coord, out var placeMessage), Is.True, placeMessage);

                Assert.That(fixture.Source.TrapRefCount, Is.EqualTo(1));
                var trapRef = fixture.Source.TrapRefs.Single();
                Assert.That(trapRef.TrapId, Is.EqualTo("burn-trap"));
                Assert.That(trapRef.Coord, Is.EqualTo(coord));
                Assert.That(trapRef.Radius, Is.EqualTo(2));
                Assert.That(trapRef.AffectsPlayer, Is.True);
                Assert.That(trapRef.AffectsMonsters, Is.True);
                Assert.That(trapRef.OneShot, Is.False);
                Assert.That(trapRef.Effects.Single().Kind, Is.EqualTo(HexTrapEffectKind.Poison));
                Assert.That(trapRef.Effects.Single().Amount, Is.EqualTo(3));
                Assert.That(trapRef.Effects.Single().DurationTurns, Is.EqualTo(4));

                Assert.That(fixture.Source.TryToHexMapData(out var map, out var error), Is.True, error);
                var runtimeTrap = map.TrapRefs.Single();
                Assert.That(runtimeTrap.TrapId, Is.EqualTo("burn-trap"));
                Assert.That(runtimeTrap.Effects.Single().Kind, Is.EqualTo(HexTrapEffectKind.Poison));
            }
        }

        [Test]
        public void PlaceObjectWithTrapTypeCreatesAutoIdTrapRef()
        {
            using (var fixture = Fixture.Create())
            {
                var coord = new HexCoord(3, -1);
                var session = new HexSparseMapEditorSession
                {
                    Source = fixture.Source,
                    TilePresetCatalog = fixture.Presets,
                    ActiveTilePresetId = "street-tile",
                    ActiveObjectType = HexMapObjectType.Trap,
                    ActiveTrapId = "manual-id-should-not-be-used",
                    ActiveTrapRadius = 1,
                    ActiveTrapEffectKind = HexTrapEffectKind.Poison,
                    ActiveTrapEffectAmount = 2,
                    ActiveTrapEffectDurationTurns = 3,
                    ActiveTrapAffectsPlayer = true,
                    ActiveTrapAffectsMonsters = false
                };
                Assert.That(session.Paint(coord, out var paintMessage), Is.True, paintMessage);

                Assert.That(session.PlaceObject(coord, out var placeMessage), Is.True, placeMessage);

                Assert.That(fixture.Source.ObjectRefCount, Is.Zero);
                Assert.That(fixture.Source.TrapRefCount, Is.EqualTo(1));
                var trapRef = fixture.Source.TrapRefs.Single();
                Assert.That(trapRef.TrapId, Is.EqualTo("trap-3-n1"));
                Assert.That(trapRef.TrapId, Is.Not.EqualTo("manual-id-should-not-be-used"));
                Assert.That(trapRef.Effects.Single().Kind, Is.EqualTo(HexTrapEffectKind.Poison));
                Assert.That(trapRef.Effects.Single().DurationTurns, Is.EqualTo(3));

                Assert.That(session.EraseObject(coord, out var eraseMessage), Is.True, eraseMessage);
                Assert.That(fixture.Source.TrapRefCount, Is.Zero);
            }
        }

        [Test]
        public void PlaceTrapRequiresPaintedCellAndCanEraseTraps()
        {
            using (var fixture = Fixture.Create())
            {
                var coord = new HexCoord(2, 0);
                var session = new HexSparseMapEditorSession
                {
                    Source = fixture.Source,
                    ActiveTrapEffectKind = HexTrapEffectKind.Damage,
                    ActiveTrapEffectAmount = 5
                };

                Assert.That(session.PlaceTrap(coord, out var missingCellMessage), Is.False);
                Assert.That(missingCellMessage, Does.Contain("Paint a sparse cell"));

                fixture.Source.SetCell(coord, new HexSparseMapAuthoringCell(coord, "street-tile", "street", "atlas-street"));
                Assert.That(session.PlaceTrap(coord, out var placeMessage), Is.True, placeMessage);
                Assert.That(fixture.Source.TrapRefCount, Is.EqualTo(1));

                Assert.That(session.EraseTrap(coord, out var eraseMessage), Is.True, eraseMessage);
                Assert.That(fixture.Source.TrapRefCount, Is.Zero);
            }
        }

        [Test]
        public void EraseObjectRemovesTrapEvenWhenTrapBrushIsNotSelected()
        {
            using (var fixture = Fixture.Create())
            {
                var coord = new HexCoord(2, 0);
                fixture.Source.SetTrapRef(new HexTrapRef(
                    "trap-a",
                    coord.Q,
                    coord.R,
                    radius: 0,
                    effects: new[] { new HexTrapEffectRef(HexTrapEffectKind.Damage, 1) }));
                var session = new HexSparseMapEditorSession
                {
                    Source = fixture.Source,
                    ActiveObjectType = HexMapObjectType.Building
                };

                Assert.That(session.EraseObject(coord, out var eraseMessage), Is.True, eraseMessage);
                Assert.That(fixture.Source.TrapRefCount, Is.Zero);
            }
        }

        [Test]
        public void DeleteObjectTypeRemovesSelectedObjectTypeAndTrapsUseTrapRefs()
        {
            using (var fixture = Fixture.Create())
            {
                fixture.Source.SetObjectRef(new HexMapObjectRef("building-a", HexMapObjectType.Building, "building", 0, 0));
                fixture.Source.SetObjectRef(new HexMapObjectRef("spawn-a", HexMapObjectType.MonsterSpawn, "M001", 1, 0, patrolAreaId: "patrol-a"));
                fixture.Source.SetTrapRef(new HexTrapRef(
                    "trap-a",
                    2,
                    0,
                    radius: 0,
                    effects: new[] { new HexTrapEffectRef(HexTrapEffectKind.Damage, 1) }));
                var session = new HexSparseMapEditorSession { Source = fixture.Source };

                Assert.That(session.CountPlacedObjectType(HexMapObjectType.Building), Is.EqualTo(1));
                Assert.That(session.CountPlacedObjectDefinition(HexMapObjectType.Building, "building"), Is.EqualTo(1));
                Assert.That(session.CountPlacedObjectType(HexMapObjectType.Trap), Is.EqualTo(1));

                Assert.That(session.DeleteObjectType(HexMapObjectType.Building, out var deleteBuildingMessage), Is.True, deleteBuildingMessage);
                Assert.That(fixture.Source.ObjectRefs.Any(objectRef => objectRef.ObjectType == HexMapObjectType.Building), Is.False);
                Assert.That(fixture.Source.ObjectRefs.Any(objectRef => objectRef.ObjectType == HexMapObjectType.MonsterSpawn), Is.True);

                Assert.That(session.DeleteObjectType(HexMapObjectType.Trap, out var deleteTrapMessage), Is.True, deleteTrapMessage);
                Assert.That(fixture.Source.TrapRefCount, Is.Zero);
            }
        }

        [Test]
        public void DeleteMonsterSpawnRemovesItsUnsharedPatrolArea()
        {
            using (var fixture = Fixture.Create())
            {
                var coord = new HexCoord(0, 0);
                fixture.Source.SetPatrolAreaRef(new HexMapPatrolAreaRef("patrol-spawn-a", new[] { coord }));
                fixture.Source.SetObjectRef(new HexMapObjectRef(
                    "spawn-a",
                    HexMapObjectType.MonsterSpawn,
                    "M001",
                    coord.Q,
                    coord.R,
                    patrolAreaId: "patrol-spawn-a"));
                var session = new HexSparseMapEditorSession { Source = fixture.Source };

                Assert.That(session.DeleteObjectRef("spawn-a", out var deleteMessage), Is.True, deleteMessage);

                Assert.That(fixture.Source.ObjectRefCount, Is.Zero);
                Assert.That(fixture.Source.PatrolAreaRefs.Count, Is.Zero);
            }
        }

        private sealed class Fixture : System.IDisposable
        {
            public HexSparseMapAuthoringSource Source { get; private set; }
            public AtlasTileCatalog Atlas { get; private set; }
            public HexTerrainPalette Terrain { get; private set; }
            public HexTilePresetCatalog Presets { get; private set; }

            public static Fixture Create()
            {
                var fixture = new Fixture
                {
                    Source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>(),
                    Atlas = ScriptableObject.CreateInstance<AtlasTileCatalog>(),
                    Terrain = ScriptableObject.CreateInstance<HexTerrainPalette>(),
                    Presets = ScriptableObject.CreateInstance<HexTilePresetCatalog>()
                };
                fixture.Atlas.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("atlas-street") });
                fixture.Terrain.ConfigureForTests(new[] { new HexTerrainPalette.Entry("street") });
                fixture.Presets.ConfigureForTests(fixture.Atlas, fixture.Terrain, new[] { new HexTilePresetCatalog.Entry("street-tile", "atlas-street", "street") });
                return fixture;
            }

            public void Dispose()
            {
                Object.DestroyImmediate(Source);
                Object.DestroyImmediate(Atlas);
                Object.DestroyImmediate(Terrain);
                Object.DestroyImmediate(Presets);
            }
        }
    }
}



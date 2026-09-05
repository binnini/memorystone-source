using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Map.Unity.Tests.EditMode
{
    public sealed class HexSparseMapAuthoringSourceAdditionalTests
    {
        [TestCase(-3, 0, 0, 0, 0)]
        [TestCase(-2, 1, 1, 1, 1)]
        [TestCase(-1, 2, 2, 2, 3)]
        [TestCase(-1, 3, -1, 3, 5)]
        [TestCase(0, -3, 3, 3, 7)]
        [TestCase(1, -2, 0, 4, 15)]
        [TestCase(2, -1, 1, 5, 31)]
        [TestCase(3, 0, 2, 0, 63)]
        [TestCase(0, 3, 3, 1, 5)]
        [TestCase(-4, 4, 1, 2, 9)]
        [TestCase(4, -4, 2, 3, 17)]
        public void TryToHexMapDataPreservesSparseAuthoredCellPresentation(int q, int r, int heightLevel, int rotationSteps, int edgeMask)
        {
            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            try
            {
                var coord = new HexCoord(q, r);
                source.ConfigureForTests(new[]
                {
                    new HexSparseMapAuthoringCell(coord, $"tile-{q}-{r}", "seoul-street", $"atlas-{q}-{r}", "event-a", "landmark-a", heightLevel, rotationSteps, edgeMask)
                });

                Assert.That(source.TryToHexMapData(out var map, out var error), Is.True, error);
                Assert.That(map.TryGetCell(coord, out var cell), Is.True);
                Assert.That(cell.TileDefinitionId, Is.EqualTo($"tile-{q}-{r}"));
                Assert.That(cell.TerrainTypeId, Is.EqualTo("seoul-street"));
                Assert.That(cell.AtlasVisualId, Is.EqualTo($"atlas-{q}-{r}"));
                Assert.That(cell.EventId, Is.EqualTo("event-a"));
                Assert.That(cell.LandmarkId, Is.EqualTo("landmark-a"));
                Assert.That(cell.HeightLevel, Is.EqualTo(heightLevel));
                Assert.That(cell.RotationSteps, Is.EqualTo(rotationSteps));
                Assert.That(cell.EdgeConnectionMask, Is.EqualTo(edgeMask));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [TestCase(0, 0, 0, 0, 1, 1)]
        [TestCase(-1, 0, 2, 0, 4, 1)]
        [TestCase(-2, -3, 2, 4, 5, 8)]
        [TestCase(3, -4, 6, -1, 4, 4)]
        [TestCase(-5, 5, -2, 8, 4, 4)]
        [TestCase(7, 7, 9, 10, 3, 4)]
        [TestCase(-8, -8, -6, -7, 3, 2)]
        [TestCase(1, -6, 1, 6, 1, 13)]
        [TestCase(-3, 2, 4, 2, 8, 1)]
        [TestCase(2, -3, 5, 1, 4, 5)]
        public void GetPaintedBoundsUsesOnlySparsePaintedCoordinates(int q1, int r1, int q2, int r2, int expectedWidth, int expectedHeight)
        {
            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            try
            {
                source.ConfigureForTests(new[]
                {
                    Cell(q1, r1, "tile-a"),
                    Cell(q2, r2, "tile-b")
                });

                var bounds = source.GetPaintedBounds();

                Assert.That(bounds.MinQ, Is.EqualTo(Mathf.Min(q1, q2)));
                Assert.That(bounds.MinR, Is.EqualTo(Mathf.Min(r1, r2)));
                Assert.That(bounds.MaxQ, Is.EqualTo(Mathf.Max(q1, q2)));
                Assert.That(bounds.MaxR, Is.EqualTo(Mathf.Max(r1, r2)));
                Assert.That(bounds.Width, Is.EqualTo(expectedWidth));
                Assert.That(bounds.Height, Is.EqualTo(expectedHeight));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [TestCase(0, 0, "tile-a", "tile-b")]
        [TestCase(-1, 2, "old-route", "new-route")]
        [TestCase(3, -2, "bridge-old", "bridge-new")]
        [TestCase(-4, -4, "water-old", "water-new")]
        [TestCase(5, 1, "goal-old", "goal-new")]
        public void SetCellReplacesExistingSparseCoordinateWithoutDensifying(int q, int r, string originalTile, string replacementTile)
        {
            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            try
            {
                var coord = new HexCoord(q, r);
                source.SetCell(coord, Cell(q, r, originalTile));
                source.SetCell(coord, Cell(q + 10, r + 10, replacementTile));

                Assert.That(source.CellCount, Is.EqualTo(1));
                Assert.That(source.TryGetCell(coord, out var cell), Is.True);
                Assert.That(cell.TilePresetId, Is.EqualTo(replacementTile));
                Assert.That(cell.Coord, Is.EqualTo(coord));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [TestCase(0, 0, true)]
        [TestCase(1, 0, true)]
        [TestCase(-1, 2, true)]
        [TestCase(5, -3, false)]
        [TestCase(-6, 6, false)]
        public void RemoveCellOnlyRemovesAuthoredSparseCoordinate(int q, int r, bool expectRemoved)
        {
            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            try
            {
                source.ConfigureForTests(new[]
                {
                    Cell(0, 0, "origin"),
                    Cell(1, 0, "east"),
                    Cell(-1, 2, "northwest")
                });

                Assert.That(source.RemoveCell(new HexCoord(q, r)), Is.EqualTo(expectRemoved));
                Assert.That(source.CellCount, Is.EqualTo(expectRemoved ? 2 : 3));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [TestCase(2, 0, -1, 0)]
        [TestCase(0, 2, 0, -1)]
        [TestCase(3, -1, -2, 4)]
        [TestCase(-1, 3, -1, -3)]
        [TestCase(4, 4, 2, 2)]
        [TestCase(-4, 1, 3, -2)]
        public void ConfigureForTestsSortsSparseCellsForStableAuthoringOrder(int q1, int r1, int q2, int r2)
        {
            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            try
            {
                source.ConfigureForTests(new[]
                {
                    Cell(q1, r1, "first"),
                    Cell(q2, r2, "second")
                });

                var coords = source.Cells.Select(cell => cell.Coord).ToArray();

                Assert.That(coords, Is.Ordered);
                Assert.That(coords, Does.Contain(new HexCoord(q1, r1)));
                Assert.That(coords, Does.Contain(new HexCoord(q2, r2)));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void ConfigureForTestsPreservesObjectiveAndSpawnObjectRefsForSparseRuntimeMap()
        {
            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            try
            {
                source.ConfigureForTests(
                    new[] { Cell(0, 0, "start", eventId: "mvp-start"), Cell(1, 0, "goal", landmarkId: "landmark-63") },
                    HexMapPurpose.PlayableMap,
                    objectRefs: new[]
                    {
                        new HexMapObjectRef("objective-a", HexMapObjectType.ObjectiveMarker, "landmark-63", 1, 0, displayName: "Landmark"),
                        new HexMapObjectRef("spawn-a", HexMapObjectType.MonsterSpawn, "monster-a", 0, 0, "pressure", HexMapPurpose.PlayableMap)
                    });

                Assert.That(source.TryToHexMapData(out var map, out var error), Is.True, error);
                Assert.That(source.BoardPurpose, Is.EqualTo(HexMapPurpose.PlayableMap));
                Assert.That(map.ObjectiveBindings.Single().ObjectiveId, Is.EqualTo("objective-a"));
                Assert.That(map.MonsterSpawnRefs.Single().Id, Is.EqualTo("spawn-a"));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }



        [Test]
        public void ConfigureForTestsPreservesObjectRefsAndConvertsMonsterSpawnObjects()
        {
            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            try
            {
                source.ConfigureForTests(
                    new[] { Cell(0, 0, "start"), Cell(1, 0, "spawn-cell") },
                    HexMapPurpose.PlayableMap,
                    objectRefs: new[]
                    {
                        new HexMapObjectRef("object-spawn-a", HexMapObjectType.MonsterSpawn, "M001", 1, 0, "primary_pressure", HexMapPurpose.PlayableMap)
                    });

                Assert.That(source.ObjectRefs.Single().ObjectId, Is.EqualTo("object-spawn-a"));
                Assert.That(source.TryToHexMapData(out var map, out var error), Is.True, error);
                Assert.That(map.MonsterSpawnRefs.Single().Id, Is.EqualTo("object-spawn-a"));
                Assert.That(map.MonsterSpawnRefs.Single().MonsterId, Is.EqualTo("M001"));
                Assert.That(map.MonsterSpawnRefs.Single().Coord, Is.EqualTo(new HexCoord(1, 0)));
                Assert.That(map.MonsterSpawnRefs.Single().SpawnRole, Is.EqualTo("primary_pressure"));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void TryToHexMapDataExportsBuildingObjectRefsForRuntimeVisualization()
        {
            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            try
            {
                var coord = new HexCoord(1, 0);
                source.ConfigureForTests(
                    new[] { Cell(0, 0, "start"), Cell(coord.Q, coord.R, "prop-cell") },
                    HexMapPurpose.PlayableMap,
                    objectRefs: new[]
                    {
                        new HexMapObjectRef("building-a", HexMapObjectType.Building, "bench-a", coord.Q, coord.R, "cover", HexMapPurpose.PlayableMap, rotationSteps: 4)
                    });

                Assert.That(source.TryToHexMapData(out var map, out var error), Is.True, error);

                var objectRef = map.ObjectRefs.Single();
                Assert.That(objectRef.ObjectId, Is.EqualTo("building-a"));
                Assert.That(objectRef.ObjectType, Is.EqualTo("Building"));
                Assert.That(objectRef.ObjectRef, Is.EqualTo("bench-a"));
                Assert.That(objectRef.Coord, Is.EqualTo(coord));
                Assert.That(objectRef.RotationSteps, Is.EqualTo(4));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void TryToHexMapDataExportsBuildingFootprintAndScale()
        {
            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            try
            {
                var anchor = new HexCoord(1, 0);
                var footprint = new[] { new HexCoord(0, 0), new HexCoord(1, 0) };
                source.ConfigureForTests(
                    new[] { Cell(0, 0, "start"), Cell(1, 0, "building-anchor"), Cell(2, 0, "building-footprint") },
                    HexMapPurpose.PlayableMap,
                    objectRefs: new[]
                    {
                        new HexMapObjectRef(
                            "building-wide",
                            HexMapObjectType.Building,
                            "wide-building",
                            anchor.Q,
                            anchor.R,
                            "cover",
                            HexMapPurpose.PlayableMap,
                            blocksMovement: true,
                            blocksVision: true,
                            footprintOffsets: footprint,
                            visualScaleMultiplier: new Vector3(1.2f, 1.4f, 0.8f))
                    });

                Assert.That(source.TryToHexMapData(out var map, out var error), Is.True, error);

                var objectRef = map.ObjectRefs.Single();
                Assert.That(objectRef.FootprintOffsets, Is.EquivalentTo(footprint));
                Assert.That(objectRef.OccupiedCoords, Is.EquivalentTo(new[] { new HexCoord(1, 0), new HexCoord(2, 0) }));
                Assert.That(objectRef.VisualScaleX, Is.EqualTo(1.2f).Within(0.001f));
                Assert.That(map.HasMovementBlockingObject(new HexCoord(2, 0)), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void TryToHexMapDataConvertsObjectiveMarkerObjectRefsToObjectiveBindings()
        {
            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            try
            {
                source.ConfigureForTests(
                    new[]
                    {
                        Cell(0, 0, "start"),
                        Cell(1, 0, "objective-marker", landmarkId: "landmark-63")
                    },
                    HexMapPurpose.PlayableMap,
                    objectRefs: new[]
                    {
                        new HexMapObjectRef(
                            "first-play-investigate-63",
                            HexMapObjectType.ObjectiveMarker,
                            "landmark-63",
                            1,
                            0,
                            "objective-role",
                            HexMapPurpose.PlayableMap,
                            interactable: true,
                            displayName: "63 landmark",
                            requiredAction: "Survey",
                            investigateRange: 2)
                    });

                Assert.That(source.TryToHexMapData(out var map, out var error), Is.True, error);
                var binding = map.ObjectiveBindings.Single();
                Assert.That(binding.ObjectiveId, Is.EqualTo("first-play-investigate-63"));
                Assert.That(binding.LandmarkId, Is.EqualTo("landmark-63"));
                Assert.That(binding.DisplayName, Is.EqualTo("63 landmark"));
                Assert.That(binding.RequiredAction, Is.EqualTo("Survey"));
                Assert.That(binding.InvestigateRange, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void TryToHexMapDataRejectsObjectiveMarkerMissingLandmarkRef()
        {
            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            try
            {
                source.ConfigureForTests(
                    new[] { Cell(0, 0, "objective-cell", landmarkId: "landmark-63") },
                    objectRefs: new[] { new HexMapObjectRef("objective-a", HexMapObjectType.ObjectiveMarker, " ", 0, 0) });

                Assert.That(source.TryToHexMapData(out _, out var error), Is.False);
                Assert.That(error, Does.Contain("objective_marker 'objective-a'"));
                Assert.That(error, Does.Contain("missing objectRef landmark id"));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void TryToHexMapDataRejectsObjectiveMarkerTargetingMissingLandmark()
        {
            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            try
            {
                source.ConfigureForTests(
                    new[] { Cell(0, 0, "objective-cell", landmarkId: "landmark-63") },
                    objectRefs: new[] { new HexMapObjectRef("objective-a", HexMapObjectType.ObjectiveMarker, "missing-landmark", 0, 0) });

                Assert.That(source.TryToHexMapData(out _, out var error), Is.False);
                Assert.That(error, Does.Contain("objective-a"));
                Assert.That(error, Does.Contain("missing-landmark"));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void TryToHexMapDataRejectsDuplicateMonsterSpawnIdsAcrossObjectRefs()
        {
            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            try
            {
                source.ConfigureForTests(
                    new[] { Cell(0, 0, "legacy-spawn"), Cell(1, 0, "object-spawn") },
                    objectRefs: new[]
                    {
                        new HexMapObjectRef("duplicate-spawn", HexMapObjectType.MonsterSpawn, "M001", 0, 0),
                        new HexMapObjectRef("duplicate-spawn", HexMapObjectType.MonsterSpawn, "M001", 1, 0)
                    });

                Assert.That(source.TryToHexMapData(out _, out var error), Is.False);
                Assert.That(error, Does.Contain("Duplicate monster spawn ref id 'duplicate-spawn'"));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void TryToHexMapDataRejectsMonsterSpawnObjectMissingCoordinate()
        {
            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            try
            {
                source.ConfigureForTests(
                    new[] { Cell(0, 0, "start") },
                    objectRefs: new[] { new HexMapObjectRef("missing-coord", HexMapObjectType.MonsterSpawn, "M001", 3, 0) });

                Assert.That(source.TryToHexMapData(out _, out var error), Is.False);
                Assert.That(error, Does.Contain("missing-coord"));
                Assert.That(error, Does.Contain("missing coordinate"));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void TryToHexMapDataRejectsMonsterSpawnOnMovementBlockingObject()
        {
            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            try
            {
                source.ConfigureForTests(
                    new[] { Cell(0, 0, "start"), Cell(1, 0, "spawn") },
                    objectRefs: new[]
                    {
                        new HexMapObjectRef("blocked-building", HexMapObjectType.Building, "building-a", 1, 0, blocksMovement: true),
                        new HexMapObjectRef("monster-spawn", HexMapObjectType.MonsterSpawn, "M001", 1, 0)
                    });

                Assert.That(source.TryToHexMapData(out _, out var error), Is.False);
                Assert.That(error, Does.Contain("monster-spawn"));
                Assert.That(error, Does.Contain("movement-blocked"));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void TryToHexMapDataRejectsPlayerSpawnOnMovementBlockingObject()
        {
            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            try
            {
                source.ConfigureForTests(
                    new[] { Cell(0, 0, "start"), Cell(1, 0, "spawn") },
                    objectRefs: new[]
                    {
                        new HexMapObjectRef("blocked-building", HexMapObjectType.Building, "building-a", 1, 0, blocksMovement: true),
                        new HexMapObjectRef("player-spawn", HexMapObjectType.PlayerSpawn, string.Empty, 1, 0)
                    });

                Assert.That(source.TryToHexMapData(out _, out var error), Is.False);
                Assert.That(error, Does.Contain("player-spawn"));
                Assert.That(error, Does.Contain("movement-blocked"));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }


        [Test]
        public void ConfigureForTestsPreservesPatrolAreasAndSpawnPatrolAreaBinding()
        {
            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            try
            {
                var patrolArea = new HexMapPatrolAreaRef(
                    "patrol-a",
                    new[] { new HexCoord(0, 0), new HexCoord(1, 0) });
                source.ConfigureForTests(
                    new[] { Cell(0, 0, "spawn"), Cell(1, 0, "patrol") },
                    HexMapPurpose.PlayableMap,
                    objectRefs: new[] { new HexMapObjectRef("spawn-a", HexMapObjectType.MonsterSpawn, "monster-a", 0, 0, "patrol", HexMapPurpose.PlayableMap, patrolAreaId: "patrol-a") },
                    patrolAreaRefs: new[] { patrolArea });

                Assert.That(source.TryToHexMapData(out var map, out var error), Is.True, error);
                Assert.That(map.PatrolAreas.Single().Id, Is.EqualTo("patrol-a"));
                Assert.That(map.PatrolAreas.Single().Coords, Is.EquivalentTo(new[] { new HexCoord(0, 0), new HexCoord(1, 0) }));
                Assert.That(map.MonsterSpawnRefs.Single().PatrolAreaId, Is.EqualTo("patrol-a"));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void HexMapPurposeKeepsSerializedEnumOrdinals()
        {
            Assert.That((int)HexMapPurpose.Unspecified, Is.EqualTo(0));
            Assert.That((int)HexMapPurpose.SmokeMap, Is.EqualTo(1));
            Assert.That((int)HexMapPurpose.PlayableMap, Is.EqualTo(2));
        }

        [Test]
        public void MonsterSpawnObjectRefUsesSparseCoordinatesDirectly()
        {
            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            try
            {
                source.ConfigureForTests(
                    new[] { Cell(3, 5, "spawn-cell") },
                    HexMapPurpose.PlayableMap,
                    objectRefs: new[] { new HexMapObjectRef("spawn-a", HexMapObjectType.MonsterSpawn, "monster-a", 3, 5) });

                Assert.That(source.TryToHexMapData(out var map, out var error), Is.True, error);
                Assert.That(map.MonsterSpawnRefs.Single().Coord, Is.EqualTo(new HexCoord(3, 5)));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        private static HexSparseMapAuthoringCell Cell(int q, int r, string tile, string eventId = "", string landmarkId = "")
        {
            return new HexSparseMapAuthoringCell(new HexCoord(q, r), tile, "seoul-street", "atlas-street", eventId, landmarkId);
        }
        [Test]
        public void ObjectRefPlayerSpawnBecomesRuntimeObjectFactWithoutMutatingCellEvent()
        {
            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            try
            {
                var coord = new HexCoord(2, -1);
                source.ConfigureForTests(
                    new[] { new HexSparseMapAuthoringCell(coord, "street-tile", "street", "atlas-street") },
                    objectRefs: new[] { new HexMapObjectRef("player-spawn-2-n1", HexMapObjectType.PlayerSpawn, string.Empty, coord.Q, coord.R) });

                Assert.That(source.TryToHexMapData(out var map, out var error), Is.True, error);
                Assert.That(map.TryGetCell(coord, out var cell), Is.True);
                Assert.That(cell.EventId, Is.Empty);
                var objectRef = map.ObjectRefs.Single();
                Assert.That(objectRef.IsPlayerSpawn, Is.True);
                Assert.That(objectRef.Coord, Is.EqualTo(coord));
                Assert.That(objectRef.ObjectRef, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

    }
}


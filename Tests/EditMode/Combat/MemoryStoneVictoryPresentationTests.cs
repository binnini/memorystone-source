using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class MemoryStoneVictoryPresentationTests
    {
        [Test]
        public void VictoryFocusCoordsUsePlayerSpawnOrderedCameraPointsAndMemoryStone()
        {
            var map = CreateLineMap(
                5,
                new[]
                {
                    Object("player-spawn", "PlayerSpawn", string.Empty, new HexCoord(0, 0)),
                    Object("victory-camera-point-02", "VictoryCameraPoint", string.Empty, new HexCoord(3, 0), role: "02"),
                    Object("victory-camera-point-01", "VictoryCameraPoint", string.Empty, new HexCoord(2, 0), role: "01"),
                    Object("memory-main", "MemoryStone", "memorystone", new HexCoord(4, 0), role: "main", interactable: true)
                });

            var focusCoords = InvokeFocusCoords(map, new HexCoord(0, 0), new HexCoord(4, 0));

            Assert.That(
                focusCoords,
                Is.EqualTo(new[] { new HexCoord(0, 0), new HexCoord(2, 0), new HexCoord(3, 0), new HexCoord(4, 0) }));
        }

        [Test]
        public void VictoryRevealWavesAdvanceAlongRouteFromStartToMemoryStone()
        {
            var map = CreateLineMap(5);
            var focusCoords = new[] { new HexCoord(0, 0), new HexCoord(2, 0), new HexCoord(4, 0) };
            var route = InvokeRevealRouteSamples(map, focusCoords);
            var waves = InvokeRevealWaves(map, route, revealRadius: 0, fallbackCoord: new HexCoord(4, 0));

            var revealedByWave = waves
                .Select(wave => GetWaveCells(wave).SingleOrDefault())
                .ToArray();

            Assert.That(route, Is.EqualTo(new[] { new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0), new HexCoord(3, 0), new HexCoord(4, 0) }));
            Assert.That(revealedByWave, Is.EqualTo(route));
        }

        [Test]
        public void VictoryEndCameraPointIsSingletonAndPicksTheLowestAuthoredOrder()
        {
            var map = CreateLineMap(
                5,
                new[]
                {
                    Object("victory-end-b", "VictoryEndCameraPoint", "victory-end-camera-point", new HexCoord(3, 0), role: "02"),
                    Object("victory-end-a", "VictoryEndCameraPoint", "victory-end-camera-point", new HexCoord(1, 0), role: "01"),
                    // Off the painted map: an authored point on a cell that no longer exists must not win.
                    Object("victory-end-off", "VictoryEndCameraPoint", "victory-end-camera-point", new HexCoord(9, 0), role: "00")
                });

            Assert.That(MemoryStoneVictoryCinematicPlanner.TryGetVictoryEndCameraPoint(map, out var point), Is.True);
            Assert.That(point.Coord, Is.EqualTo(new HexCoord(1, 0)));
        }

        [Test]
        public void VictoryEndCameraPointIsAbsentWhenNoneAuthored()
        {
            var map = CreateLineMap(3);

            Assert.That(MemoryStoneVictoryCinematicPlanner.TryGetVictoryEndCameraPoint(map, out _), Is.False);
        }

        [Test]
        public void SideViewSecondsFollowsTravelDistanceAndStaysWithinBounds()
        {
            // Stage_1's ~47u pass at the authored 9.4 u/s keeps its tuned ~5s length...
            Assert.That(
                MemoryStoneVictoryCinematicPlanner.ResolveVictorySideViewSeconds(47f, 9.4f, 1.5f, 5f),
                Is.EqualTo(5f).Within(0.01f));

            // ...while TutorialSource's ~21u pass no longer crawls through the same five seconds.
            Assert.That(
                MemoryStoneVictoryCinematicPlanner.ResolveVictorySideViewSeconds(21.5f, 9.4f, 1.5f, 5f),
                Is.EqualTo(2.29f).Within(0.01f));

            // Floor and ceiling both hold.
            Assert.That(
                MemoryStoneVictoryCinematicPlanner.ResolveVictorySideViewSeconds(1f, 9.4f, 1.5f, 5f),
                Is.EqualTo(1.5f).Within(0.01f));
            Assert.That(
                MemoryStoneVictoryCinematicPlanner.ResolveVictorySideViewSeconds(500f, 9.4f, 1.5f, 5f),
                Is.EqualTo(5f).Within(0.01f));
        }

        [Test]
        public void SideViewLateralOffsetIsCappedByMapSizeAndKeepsItsSide()
        {
            // Stage_1 (89u across) can hold the authored 15u offset unchanged.
            Assert.That(
                MemoryStoneVictoryCinematicPlanner.ResolveVictorySideViewLateralOffset(15f, 89.2f, 0.22f),
                Is.EqualTo(15f).Within(0.01f));

            // TutorialSource (50u across) cannot — the camera would fly past the edge of the map.
            Assert.That(
                MemoryStoneVictoryCinematicPlanner.ResolveVictorySideViewLateralOffset(15f, 50.2f, 0.22f),
                Is.EqualTo(11.04f).Within(0.01f));

            // The sign picks which side of the street the pass watches from; the cap must not flip it.
            Assert.That(
                MemoryStoneVictoryCinematicPlanner.ResolveVictorySideViewLateralOffset(-15f, 50.2f, 0.22f),
                Is.EqualTo(-11.04f).Within(0.01f));

            // No measurable map (or the cap disabled) leaves the authored value alone.
            Assert.That(
                MemoryStoneVictoryCinematicPlanner.ResolveVictorySideViewLateralOffset(15f, 0f, 0.22f),
                Is.EqualTo(15f).Within(0.01f));
        }

        private static IReadOnlyList<HexCoord> InvokeFocusCoords(HexMapData map, HexCoord startCoord, HexCoord memoryStoneCoord)
        {
            var method = typeof(MapCombatController).GetMethod(
                "BuildMemoryStoneVictoryFocusCoords",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (IReadOnlyList<HexCoord>)method.Invoke(null, new object[] { map, startCoord, memoryStoneCoord });
        }

        private static IReadOnlyList<HexCoord> InvokeRevealRouteSamples(HexMapData map, IReadOnlyList<HexCoord> focusCoords)
        {
            var method = typeof(MapCombatController).GetMethod(
                "BuildMemoryStoneVictoryRevealRouteSamples",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (IReadOnlyList<HexCoord>)method.Invoke(null, new object[] { map, focusCoords });
        }

        private static IReadOnlyList<object> InvokeRevealWaves(
            HexMapData map,
            IReadOnlyList<HexCoord> route,
            int revealRadius,
            HexCoord fallbackCoord)
        {
            var method = typeof(MapCombatController).GetMethod(
                "BuildMemoryStoneVictoryWavesAlongRoute",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return ((IEnumerable)method.Invoke(null, new object[] { map, route, revealRadius, fallbackCoord }))
                .Cast<object>()
                .ToArray();
        }

        private static IReadOnlyList<HexCoord> GetWaveCells(object wave)
        {
            var property = wave.GetType().GetProperty("Cells", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(property, Is.Not.Null);
            return (IReadOnlyList<HexCoord>)property.GetValue(wave);
        }

        private static HexMapData CreateLineMap(int length, IEnumerable<HexMapObjectData> objectRefs = null)
        {
            var cells = Enumerable.Range(0, length)
                .Select(q => new HexCellData(new HexCoord(q, 0), $"tile-{q}", "street", 1, true, false))
                .ToArray();
            return new HexMapData(cells, objectRefs: objectRefs);
        }

        private static HexMapObjectData Object(
            string id,
            string type,
            string objectRef,
            HexCoord coord,
            string role = "",
            bool interactable = false)
        {
            return new HexMapObjectData(id, type, objectRef, coord, role: role, interactable: interactable);
        }
    }
}

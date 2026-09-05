using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Map.Unity.Tests.EditMode
{
    public sealed class HexSparseMapAuthoringSourceTests
    {
        [Test]
        public void SetCellStoresNegativeAndPositiveAxialCoordinates()
        {
            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            try
            {
                source.SetCell(new HexCoord(-2, 3), CreateCell("tile-a"));
                source.SetCell(new HexCoord(4, -1), CreateCell("tile-b"));

                Assert.That(source.CellCount, Is.EqualTo(2));
                Assert.That(source.TryGetCell(new HexCoord(-2, 3), out var negative), Is.True);
                Assert.That(negative.Coord, Is.EqualTo(new HexCoord(-2, 3)));
                Assert.That(negative.TilePresetId, Is.EqualTo("tile-a"));
                Assert.That(source.TryGetCell(new HexCoord(4, -1), out var positive), Is.True);
                Assert.That(positive.Coord, Is.EqualTo(new HexCoord(4, -1)));
                Assert.That(positive.TilePresetId, Is.EqualTo("tile-b"));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void SetCellReplacesDuplicateCoordinateAndKeepsDeterministicOrder()
        {
            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            try
            {
                source.SetCell(new HexCoord(2, 0), CreateCell("tile-b"));
                source.SetCell(new HexCoord(-1, 0), CreateCell("tile-a"));
                source.SetCell(new HexCoord(2, 0), CreateCell("tile-c"));

                Assert.That(source.CellCount, Is.EqualTo(2));
                Assert.That(source.TryGetCell(new HexCoord(2, 0), out var replaced), Is.True);
                Assert.That(replaced.TilePresetId, Is.EqualTo("tile-c"));
                Assert.That(source.Cells.Select(cell => cell.Coord).ToArray(), Is.EqualTo(new[]
                {
                    new HexCoord(-1, 0),
                    new HexCoord(2, 0)
                }));
                Assert.That(source.HasDuplicateCoordinates(out var duplicates), Is.False);
                Assert.That(duplicates, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void RemoveCellDeletesAuthoredCoordinateOnly()
        {
            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            try
            {
                source.SetCell(new HexCoord(0, 0), CreateCell("tile-a"));
                source.SetCell(new HexCoord(1, 0), CreateCell("tile-b"));

                Assert.That(source.RemoveCell(new HexCoord(0, 0)), Is.True);
                Assert.That(source.RemoveCell(new HexCoord(7, 7)), Is.False);

                Assert.That(source.CellCount, Is.EqualTo(1));
                Assert.That(source.TryGetCell(new HexCoord(0, 0), out _), Is.False);
                Assert.That(source.TryGetCell(new HexCoord(1, 0), out var remaining), Is.True);
                Assert.That(remaining.TilePresetId, Is.EqualTo("tile-b"));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void GetPaintedBoundsHandlesEmptySingleAndMixedCoordinates()
        {
            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            try
            {
                Assert.That(source.GetPaintedBounds().IsEmpty, Is.True);

                source.SetCell(new HexCoord(-3, 5), CreateCell("tile-a"));
                Assert.That(source.GetPaintedBounds(), Is.EqualTo(new HexSparseMapBounds(-3, 5, -3, 5)));
                Assert.That(source.GetPaintedBounds().Area, Is.EqualTo(1));

                source.SetCell(new HexCoord(2, -1), CreateCell("tile-b"));
                var bounds = source.GetPaintedBounds();
                Assert.That(bounds.MinQ, Is.EqualTo(-3));
                Assert.That(bounds.MinR, Is.EqualTo(-1));
                Assert.That(bounds.MaxQ, Is.EqualTo(2));
                Assert.That(bounds.MaxR, Is.EqualTo(5));
                Assert.That(bounds.Width, Is.EqualTo(6));
                Assert.That(bounds.Height, Is.EqualTo(7));
                Assert.That(bounds.Area, Is.EqualTo(42));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void ConfigureForTestsReportsDuplicateSerializedCoordinates()
        {
            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            try
            {
                source.ConfigureForTests(new[]
                {
                    new HexSparseMapAuthoringCell(new HexCoord(0, 0), "tile-a", "street", "visual-a"),
                    new HexSparseMapAuthoringCell(new HexCoord(0, 0), "tile-b", "street", "visual-b"),
                    new HexSparseMapAuthoringCell(new HexCoord(1, 0), "tile-c", "street", "visual-c")
                });

                Assert.That(source.HasDuplicateCoordinates(out var duplicates), Is.True);
                Assert.That(duplicates, Is.EqualTo(new[] { new HexCoord(0, 0) }));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void MemoryStoneObjectRefBuildsRuntimeVisualInteractableObject()
        {
            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            try
            {
                var coord = new HexCoord(1, 0);
                source.ConfigureForTests(
                    new[] { new HexSparseMapAuthoringCell(coord, "tile-a", "street", "visual-street") },
                    objectRefs: new[]
                    {
                        new HexMapObjectRef(
                            "memory-main",
                            HexMapObjectType.MemoryStone,
                            "memorystone",
                            coord.Q,
                            coord.R,
                            role: "objective",
                            interactable: true)
                    });

                Assert.That(source.TryToHexMapData(out var map, out var error), Is.True, error);

                var obj = map.ObjectRefs.Single();
                Assert.That(obj.ObjectType, Is.EqualTo("MemoryStone"));
                Assert.That(obj.ObjectRef, Is.EqualTo("memorystone"));
                Assert.That(obj.Interactable, Is.True);
                Assert.That(obj.IsRuntimeVisualObject, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void VictoryCameraPointObjectRefsBuildRuntimePathMarkers()
        {
            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            try
            {
                var first = new HexCoord(0, 0);
                var second = new HexCoord(1, 0);
                source.ConfigureForTests(
                    new[]
                    {
                        new HexSparseMapAuthoringCell(first, "tile-a", "street", "visual-street"),
                        new HexSparseMapAuthoringCell(second, "tile-a", "street", "visual-street")
                    },
                    objectRefs: new[]
                    {
                        new HexMapObjectRef(
                            "victory-camera-point-00",
                            HexMapObjectType.VictoryCameraPoint,
                            string.Empty,
                            first.Q,
                            first.R,
                            role: "00"),
                        new HexMapObjectRef(
                            "victory-camera-point-01",
                            HexMapObjectType.VictoryCameraPoint,
                            string.Empty,
                            second.Q,
                            second.R,
                            role: "01")
                    });

                Assert.That(source.TryToHexMapData(out var map, out var error), Is.True, error);

                var points = map.ObjectRefs.Where(obj => obj.ObjectType == "VictoryCameraPoint").ToArray();
                Assert.That(points, Has.Length.EqualTo(2));
                Assert.That(points.Select(point => point.Role).ToArray(), Is.EqualTo(new[] { "00", "01" }));
                Assert.That(points.Select(point => point.Coord).ToArray(), Is.EqualTo(new[] { first, second }));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        private static HexSparseMapAuthoringCell CreateCell(string tilePresetId)
        {
            return new HexSparseMapAuthoringCell(new HexCoord(99, 99), tilePresetId, "street", "visual-street");
        }
    }
}


using NUnit.Framework;

namespace SeoulPlayup.Map.Runtime.Tests.EditMode
{
    public sealed class HexMapDataTests
    {
        [Test]
        public void StoresStaticCellsWithoutRuntimeStateMutation()
        {
            var cell = new HexCellData(new HexCoord(0, 0), "ground", "street", 2, true, false);
            var map = new HexMapData(new[] { cell });
            var runtime = new HexCellRuntimeState("player", temporaryBlocked: true);

            Assert.That(map.Contains(cell.Coord), Is.True);
            Assert.That(map.TryGetCell(cell.Coord, out var loaded), Is.True);
            Assert.That(loaded.BaseMoveCost, Is.EqualTo(2));
            Assert.That(loaded.BaseWalkable, Is.True);
            Assert.That(runtime.TemporaryBlocked, Is.True);
            Assert.That(loaded.BaseWalkable, Is.True, "Runtime state must remain separate from static authoring data.");
        }
    }
}


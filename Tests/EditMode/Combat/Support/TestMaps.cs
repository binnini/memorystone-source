using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// Builders for the hand-rolled map shapes the suite keeps reassembling from raw
    /// <see cref="HexCellData"/>. Tests that need extra map inputs (traps, objects) can pass the
    /// cell enumerable straight into their own HexMapData constructor call.
    /// </summary>
    public static class TestMaps
    {
        /// <summary>A walkable west-to-east corridor: cells (0,0) .. (length-1,0).</summary>
        public static HexMapData Line(int length)
        {
            return new HexMapData(LineCells(length));
        }

        /// <summary>The cells of <see cref="Line"/>, for tests that add trapRefs/objectRefs on top.</summary>
        public static HexCellData[] LineCells(int length)
        {
            return Enumerable.Range(0, length)
                .Select(q => new HexCellData(new HexCoord(q, 0), $"cell-{q}", "street", 1, true, false))
                .ToArray();
        }
    }
}

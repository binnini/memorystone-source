using System;

namespace SeoulPlayup.Map.Runtime
{
    /// <summary>
    /// Pure-math hex picking for the pointy-top axial layout shared by the tile
    /// presentation view, the minimap and the map authoring tools. Positions are
    /// local to the map presentation root: X/Z span the hex plane, Y is elevation
    /// (<c>VerticalOffset + HeightLevel * HeightStep</c>).
    /// The caller supplies the effective tile radius (base radius × spacing),
    /// which must be positive.
    /// </summary>
    public readonly struct HexAxialProjection
    {
        private static readonly float Sqrt3 = MathF.Sqrt(3f);

        public HexAxialProjection(float tileRadius, float verticalOffset = 0f, float heightStep = 0f)
        {
            TileRadius = tileRadius;
            VerticalOffset = verticalOffset;
            HeightStep = heightStep;
        }

        public float TileRadius { get; }
        public float VerticalOffset { get; }
        public float HeightStep { get; }

        public void CoordToWorld(HexCoord coord, out float x, out float z)
        {
            x = Sqrt3 * TileRadius * (coord.Q + coord.R * 0.5f);
            z = -1.5f * TileRadius * coord.R;
        }

        public HexCoord WorldToCoord(float x, float z)
        {
            var r = -z / (1.5f * TileRadius);
            var q = x / (Sqrt3 * TileRadius) - r * 0.5f;
            return RoundAxial(q, r);
        }

        public float HeightPlaneY(int heightLevel)
        {
            return VerticalOffset + heightLevel * HeightStep;
        }

        /// <summary>
        /// Sweeps the discrete height planes from <see cref="HexCellData.MaxHeightLevel"/>
        /// down to <see cref="HexCellData.MinHeightLevel"/>, intersecting the ray with each
        /// plane and accepting the first candidate whose cell exists at exactly that height.
        /// <paramref name="cellHeightLevel"/> returns the cell's raw height level, or null
        /// when no cell exists at the coordinate. Rays parallel to the planes fail.
        /// </summary>
        public bool TryRaycastHeightPlanes(
            float originX, float originY, float originZ,
            float directionX, float directionY, float directionZ,
            Func<HexCoord, int?> cellHeightLevel,
            out HexCoord coord)
        {
            coord = default;
            if (cellHeightLevel == null || Math.Abs(directionY) <= 0.0001f)
            {
                return false;
            }

            for (var height = HexCellData.MaxHeightLevel; height >= HexCellData.MinHeightLevel; height--)
            {
                var planeY = HeightPlaneY(height);
                var distance = (planeY - originY) / directionY;
                if (distance < 0f)
                {
                    continue;
                }

                var candidate = WorldToCoord(
                    originX + directionX * distance,
                    originZ + directionZ * distance);
                var heightLevel = cellHeightLevel(candidate);
                if (heightLevel == null || HexCellData.ClampHeight(heightLevel.Value) != height)
                {
                    continue;
                }

                coord = candidate;
                return true;
            }

            return false;
        }

        // Cube rounding. Tie-breaks follow the presentation view's picking variant:
        // fix Q only when its error is strictly largest, then prefer discarding the
        // S error (a no-op for the returned axial pair) over re-deriving R.
        public static HexCoord RoundAxial(float q, float r)
        {
            var s = -q - r;

            var roundedQ = (int)Math.Round(q);
            var roundedS = (int)Math.Round(s);
            var roundedR = (int)Math.Round(r);

            var qDiff = Math.Abs(roundedQ - q);
            var sDiff = Math.Abs(roundedS - s);
            var rDiff = Math.Abs(roundedR - r);

            if (qDiff > sDiff && qDiff > rDiff)
            {
                roundedQ = -roundedS - roundedR;
            }
            else if (sDiff <= rDiff)
            {
                roundedR = -roundedQ - roundedS;
            }

            return new HexCoord(roundedQ, roundedR);
        }
    }
}

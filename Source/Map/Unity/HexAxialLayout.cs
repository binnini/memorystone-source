using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Map.Unity
{
    public readonly struct HexAxialLayout
    {
        public HexAxialLayout(float tileRadius)
        {
            TileRadius = Mathf.Max(0.01f, tileRadius);
        }

        public float TileRadius { get; }

        // axial → world XZ (pointy-top). Math lives in Map.Runtime's HexAxialProjection,
        // shared with AtlasTilePresentationView picking.
        public Vector2 CoordToWorld(HexCoord coord)
        {
            new HexAxialProjection(TileRadius).CoordToWorld(coord, out var x, out var z);
            return new Vector2(x, z);
        }

        // world XZ → axial (inverse + cube rounding)
        public HexCoord WorldToCoord(Vector2 worldXZ)
        {
            // Vector2.y = world Z
            return new HexAxialProjection(TileRadius).WorldToCoord(worldXZ.x, worldXZ.y);
        }

        // 6 corners for Handles rendering (pointy-top: corner i at angle 30 + 60*i degrees)
        public void GetCorners(HexCoord coord, Vector3[] corners, float y = 0f)
        {
            Vector2 center = CoordToWorld(coord);
            for (int i = 0; i < 6; i++)
            {
                float angleDeg = 30f + 60f * i;
                float angleRad = Mathf.Deg2Rad * angleDeg;
                corners[i] = new Vector3(
                    center.x + TileRadius * Mathf.Cos(angleRad),
                    y,
                    center.y + TileRadius * Mathf.Sin(angleRad));
            }
        }

        // XZ AABB → axial range with padding cells
        public void GetAxialRangeForBounds(Vector2 min, Vector2 max, int padding,
            out int minQ, out int maxQ, out int minR, out int maxR)
        {
            HexCoord c00 = WorldToCoord(new Vector2(min.x, min.y));
            HexCoord c10 = WorldToCoord(new Vector2(max.x, min.y));
            HexCoord c01 = WorldToCoord(new Vector2(min.x, max.y));
            HexCoord c11 = WorldToCoord(new Vector2(max.x, max.y));

            minQ = Mathf.Min(c00.Q, Mathf.Min(c10.Q, Mathf.Min(c01.Q, c11.Q))) - padding;
            maxQ = Mathf.Max(c00.Q, Mathf.Max(c10.Q, Mathf.Max(c01.Q, c11.Q))) + padding;
            minR = Mathf.Min(c00.R, Mathf.Min(c10.R, Mathf.Min(c01.R, c11.R))) - padding;
            maxR = Mathf.Max(c00.R, Mathf.Max(c10.R, Mathf.Max(c01.R, c11.R))) + padding;
        }
    }
}

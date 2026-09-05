using System;
using System.Collections.Generic;

namespace SeoulPlayup.Map.Runtime
{
    /// <summary>Pure hex-area helpers (no Unity dependencies).</summary>
    public static class HexArea
    {
        /// <summary>
        /// Enumerate every hex coord within <paramref name="radius"/> steps of <paramref name="center"/>
        /// (a filled hex disk, inclusive). Radius 0 yields only the center; negative radius yields nothing.
        /// Each returned coord satisfies <c>center.DistanceTo(coord) &lt;= radius</c>.
        /// </summary>
        public static IEnumerable<HexCoord> CellsWithin(HexCoord center, int radius)
        {
            if (radius < 0)
            {
                yield break;
            }

            for (var dq = -radius; dq <= radius; dq++)
            {
                var rMin = Math.Max(-radius, -dq - radius);
                var rMax = Math.Min(radius, -dq + radius);
                for (var dr = rMin; dr <= rMax; dr++)
                {
                    yield return new HexCoord(center.Q + dq, center.R + dr);
                }
            }
        }

        /// <summary>
        /// <paramref name="center"/>에서 거리가 [<paramref name="minRadius"/>, <paramref name="maxRadius"/>]인
        /// 셀들(고리 띠). 중심 자신은 <c>minRadius</c>가 0일 때만 포함된다.
        /// </summary>
        public static IEnumerable<HexCoord> CellsInBand(HexCoord center, int minRadius, int maxRadius)
        {
            if (maxRadius < 0 || minRadius > maxRadius)
            {
                yield break;
            }

            var low = Math.Max(0, minRadius);
            foreach (var coord in CellsWithin(center, maxRadius))
            {
                if (center.DistanceTo(coord) >= low)
                {
                    yield return coord;
                }
            }
        }

        /// <summary>
        /// 두 칸을 잇는 헥스 직선 위의 칸들(양 끝 포함 · 시작→끝 순). 표준 큐브 보간 + 반올림이며,
        /// 정확히 경계에 걸린 지점은 미세 오프셋으로 한쪽으로 확정해 같은 입력이 항상 같은 선을 낸다
        /// (예고=명중 계약의 전제 — 결정적이지 않으면 예고와 명중이 다른 선을 그릴 수 있다).
        /// </summary>
        public static IEnumerable<HexCoord> CellsOnLine(HexCoord from, HexCoord to)
        {
            var steps = from.DistanceTo(to);
            if (steps <= 0)
            {
                yield return from;
                yield break;
            }

            for (var i = 0; i <= steps; i++)
            {
                var t = (double)i / steps;
                // 큐브 좌표(x=q, z=r, y=-x-z) 보간. 오프셋(1e-6·2e-6)은 표준 타이브레이크다.
                var x = from.Q + (to.Q - from.Q) * t + 1e-6;
                var z = from.R + (to.R - from.R) * t + 2e-6;
                var y = -x - z;

                var rx = Math.Round(x);
                var ry = Math.Round(y);
                var rz = Math.Round(z);
                var dx = Math.Abs(rx - x);
                var dy = Math.Abs(ry - y);
                var dz = Math.Abs(rz - z);
                if (dx > dy && dx > dz)
                {
                    rx = -ry - rz;
                }
                else if (dy > dz)
                {
                    // y는 유도 축이라 버린다 — x·z만 좌표가 된다.
                }
                else
                {
                    rz = -rx - ry;
                }

                yield return new HexCoord((int)rx, (int)rz);
            }
        }

        /// <summary>
        /// <paramref name="center"/>에서 본 <paramref name="coord"/>의 방위각(라디안, 0 이상 2π 미만).
        ///
        /// 방향 오프셋 배열의 순서에 기대지 않는 것이 요점이다: 축 좌표를 평면으로 펴서 각을 재므로,
        /// 링을 "회전 순서"로 정렬하거나 "이 각도에 가장 가까운 칸"을 고르는 데 그대로 쓸 수 있다.
        /// 중심 자신은 0을 돌려준다.
        /// </summary>
        public static double AngleFrom(HexCoord center, HexCoord coord)
        {
            var dq = coord.Q - center.Q;
            var dr = coord.R - center.R;
            if (dq == 0 && dr == 0)
            {
                return 0d;
            }

            // 축 좌표 → 평면(pointy-top 배치의 표준 변환). 실제 렌더 배치와 정확히 같을 필요는 없다 —
            // 필요한 것은 "일관되고 단조로운 각"뿐이다.
            var x = dq + (dr / 2d);
            var y = dr * Math.Sqrt(3d) / 2d;
            var angle = Math.Atan2(y, x);
            return angle < 0 ? angle + (2 * Math.PI) : angle;
        }

        /// <summary>두 방위각 사이의 최단 각거리(0 이상 π 이하).</summary>
        public static double AngleDistance(double left, double right)
        {
            var diff = Math.Abs(left - right) % (2 * Math.PI);
            return diff > Math.PI ? (2 * Math.PI) - diff : diff;
        }
    }
}

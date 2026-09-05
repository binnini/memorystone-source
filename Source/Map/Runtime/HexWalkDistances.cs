using System.Collections.Generic;

namespace SeoulPlayup.Map.Runtime
{
    /// <summary>
    /// 균일 비용 보행 BFS(placement-randomization-plan §4의 지대 계산용). 지형 비용·이동력을
    /// 무시한 "몇 칸 걸어야 닿는가"라, 이동 규칙이 바뀌어도 지대 커브가 흔들리지 않는다.
    /// 통행 조건 = 서 있을 수 있는 칸(<see cref="HexAccessibility.IsStandable"/> — 수역 제외) &&
    /// 인접 높이차 ≤ 1(<see cref="HexTerrainTraits.IsHeightTraversable"/>) && 이동 차단 오브젝트 없음.
    /// 🔴 물·높이를 안 보던 시절엔 물 타일(BaseWalkable=1 저작)이 지대에 섞여 배치 스윕까지
    /// 통과했다(2026-08-20 #4) — 이 BFS가 곧 배치 도달성 게이트라 이동 규칙과 같은 술어를 쓴다.
    /// </summary>
    public static class HexWalkDistances
    {
        public static IReadOnlyDictionary<HexCoord, int> FromCoord(HexMapData map, HexCoord start)
        {
            var distances = new Dictionary<HexCoord, int>();
            if (map == null || !map.TryGetCell(start, out _))
            {
                return distances;
            }

            var traits = HexTerrainTraits.Default;
            var queue = new Queue<HexCoord>();
            distances[start] = 0;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!map.TryGetCell(current, out var currentCell))
                {
                    continue;
                }

                var nextDistance = distances[current] + 1;
                foreach (var direction in HexCoord.Directions)
                {
                    var neighbor = current + direction;
                    if (distances.ContainsKey(neighbor) ||
                        !map.TryGetCell(neighbor, out var cell) ||
                        !HexAccessibility.IsStandable(cell, traits) ||
                        !traits.IsHeightTraversable(currentCell.HeightLevel, cell.HeightLevel) ||
                        map.HasMovementBlockingObject(neighbor))
                    {
                        continue;
                    }

                    distances[neighbor] = nextDistance;
                    queue.Enqueue(neighbor);
                }
            }

            return distances;
        }
    }
}

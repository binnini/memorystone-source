using System.Collections.Generic;
using System.Linq;

namespace SeoulPlayup.Map.Runtime
{
    public static class HexPathfinder
    {
        /// <summary>모든 칸의 진입 비용. 지형별 이동 비용 축 폐기(2026-09-05)로 상수가 됐다.</summary>
        private const int UniformStepCost = 1;

        public static IReadOnlyDictionary<HexCoord, int> GetReachableCells(
            HexMapData map,
            MovementQuery query,
            IReadOnlyDictionary<HexCoord, HexCellRuntimeState> runtimeStates = null,
            HexTerrainTraits terrainTraits = null)
        {
            var distances = CalculateDistances(map, query, runtimeStates, terrainTraits, null, out _);
            var reachable = new SortedDictionary<HexCoord, int>();
            foreach (var pair in distances)
            {
                if (pair.Key == query.Start && !query.IncludeStart)
                {
                    continue;
                }

                if (pair.Value <= query.MovePoints)
                {
                    reachable[pair.Key] = pair.Value;
                }
            }

            return reachable;
        }

        public static IReadOnlyList<HexCoord> FindPath(
            HexMapData map,
            MovementQuery query,
            HexCoord destination,
            IReadOnlyDictionary<HexCoord, HexCellRuntimeState> runtimeStates = null,
            HexTerrainTraits terrainTraits = null)
        {
            if (destination == query.Start)
            {
                return new[] { query.Start };
            }

            var distances = CalculateDistances(map, query, runtimeStates, terrainTraits, destination, out var previous);
            if (!distances.TryGetValue(destination, out var destinationCost) || destinationCost > query.MovePoints)
            {
                return System.Array.Empty<HexCoord>();
            }

            var path = new List<HexCoord> { destination };
            var current = destination;
            while (current != query.Start)
            {
                if (!previous.TryGetValue(current, out current))
                {
                    return System.Array.Empty<HexCoord>();
                }

                path.Add(current);
            }

            path.Reverse();
            return path;
        }

        public static bool CanEnter(
            HexMapData map,
            HexCoord destination,
            MovementQuery query,
            IReadOnlyDictionary<HexCoord, HexCellRuntimeState> runtimeStates,
            out int enterCost)
        {
            return CanEnter(map, null, destination, query, runtimeStates, null, out enterCost);
        }

        /// <summary>
        /// Whether <paramref name="destination"/> can be entered. When <paramref name="from"/>
        /// is supplied the configured height-traversal rule is applied between the two cells,
        /// and <paramref name="terrainTraits"/> blocks entry into impassable terrain (e.g. water).
        ///
        /// <para>원판 클리어런스(계획서 §21.3): <see cref="MovementQuery.FootprintRadius"/>가 0보다 크면
        /// 중심 칸만이 아니라 <b>중심 기준 반경 R 원판의 모든 칸</b>이 통과 가능해야 한다. 판정을
        /// 여기 한 곳에 두는 이유는 BFS(<see cref="CalculateDistances"/>)가 진입 가능성을 오직 이
        /// 함수로만 묻기 때문이다 — 새 경로탐색기를 만들지 않고 술어 한 겹으로 멀티셀을 얻는다.</para>
        /// </summary>
        public static bool CanEnter(
            HexMapData map,
            HexCoord? from,
            HexCoord destination,
            MovementQuery query,
            IReadOnlyDictionary<HexCoord, HexCellRuntimeState> runtimeStates,
            HexTerrainTraits terrainTraits,
            out int enterCost)
        {
            if (!CanEnterCell(map, from, destination, query, runtimeStates, terrainTraits, out enterCost))
            {
                return false;
            }

            if (query.FootprintOffsets != null && query.FootprintOffsets.Count > 1)
            {
                // 형상 footprint(삼각형 정예): 원판과 같은 규약 — 높이·비용은 중심 칸만, 나머지 칸은 통과 가능성만.
                for (var i = 0; i < query.FootprintOffsets.Count; i++)
                {
                    var offset = query.FootprintOffsets[i];
                    if (offset.Q == 0 && offset.R == 0)
                    {
                        continue;
                    }

                    if (!CanEnterCell(map, null, destination + offset, query, runtimeStates, terrainTraits, out _))
                    {
                        return false;
                    }
                }
            }

            if (query.FootprintRadius <= 0)
            {
                return true;
            }

            // 원판의 나머지 칸. 높이 규칙은 중심에만 적용한다(몸이 여러 칸이면 높이차를 걸치는 것이
            // 정상이고, 칸마다 다시 물으면 완만한 경사조차 통과 불가가 된다) — 그래서 from은 null이다.
            // 진입 비용도 중심 칸의 것을 그대로 쓴다: 몸이 커졌다고 한 칸 전진이 비싸지지는 않는다.
            foreach (var coord in HexArea.CellsWithin(destination, query.FootprintRadius))
            {
                if (coord == destination)
                {
                    continue;
                }

                if (!CanEnterCell(map, null, coord, query, runtimeStates, terrainTraits, out _))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 칸 하나의 진입 가능성. <see cref="CanEnter"/>의 원판 검사가 이것을 여러 번 부르므로
        /// <b>여기서 다시 클리어런스를 묻지 않는다</b>(재귀 방지).
        /// </summary>
        private static bool CanEnterCell(
            HexMapData map,
            HexCoord? from,
            HexCoord destination,
            MovementQuery query,
            IReadOnlyDictionary<HexCoord, HexCellRuntimeState> runtimeStates,
            HexTerrainTraits terrainTraits,
            out int enterCost)
        {
            enterCost = 0;
            if (!map.TryGetCell(destination, out var cell) || !cell.BaseWalkable)
            {
                return false;
            }

            if (terrainTraits != null && !terrainTraits.IsWalkable(cell.TerrainTypeId))
            {
                return false;
            }

            if (from.HasValue && map.TryGetCell(from.Value, out var fromCell))
            {
                var heightRule = terrainTraits ?? HexTerrainTraits.Default;
                if (!heightRule.IsHeightTraversable(fromCell.HeightLevel, cell.HeightLevel))
                {
                    return false;
                }
            }

            if (map.HasMovementBlockingObject(destination))
            {
                return false;
            }

            if (runtimeStates != null && runtimeStates.TryGetValue(destination, out var state))
            {
                if (state.TemporaryBlocked)
                {
                    return false;
                }

                if (!string.IsNullOrEmpty(state.OccupyingUnitId) && state.OccupyingUnitId != query.UnitId)
                {
                    return false;
                }
            }

            enterCost = cell.BaseMoveCost;
            return true;
        }

        /// <summary>
        /// 너비 우선 탐색(BFS). <b>모든 칸의 진입 비용이 1</b>이라는 전제 위에 선다.
        ///
        /// <para>지형별 이동 비용 축은 2026-09-05에 폐기했다. 출하 맵 생성부
        /// (<c>HexSparseMapAuthoringSource</c>)가 진입 비용에 리터럴 <c>1</c>을 넘기고,
        /// 프리셋 카탈로그(<c>SparseMapTilePresets</c>)에는 비용을 저작할 필드조차 없다.</para>
        ///
        /// <para>비용이 균일하면 <b>먼저 발견한 칸이 곧 최단</b>이라 "가장 싼 것"을 고를 필요가 없다.
        /// 그래서 우선순위 큐도 정렬도 없이 FIFO 큐 하나로 끝난다. 실측(Stage_1, 875칸 도달):
        /// 최원거리 경로가 12.8ms → 0.77ms, 경로는 78칸 전부 동일.</para>
        ///
        /// <para><b>결정성은 계약이다.</b> FIFO는 같은 거리 안에서 발견 순서로 꺼내고 이웃 확장이
        /// <see cref="HexCoord.NeighborsInDirectionOrder"/>로 고정이므로 같은 입력에 항상 같은 경로가 나온다.
        /// 예고와 실제 이동이 어긋나면 플레이어가 예고를 믿지 않게 된다.</para>
        ///
        /// <para>전제가 깨지면 <b>조용히 틀린 경로</b>가 나오므로 그 자리에서 던진다. 비용 축을 되살리려면
        /// 이 함수를 다익스트라로 되돌려야 하고, 그때는 선형 스캔이 아니라 <b>힙</b>을 쓰되 동점 순서를
        /// 비교자에 직접 넣어야 한다(힙은 동점 순서를 보장하지 않는다).</para>
        /// </summary>
        private static Dictionary<HexCoord, int> CalculateDistances(
            HexMapData map,
            MovementQuery query,
            IReadOnlyDictionary<HexCoord, HexCellRuntimeState> runtimeStates,
            HexTerrainTraits terrainTraits,
            HexCoord? stopAt,
            out Dictionary<HexCoord, HexCoord> previous)
        {
            previous = new Dictionary<HexCoord, HexCoord>();
            var distances = new Dictionary<HexCoord, int>();

            if (map == null || !map.Contains(query.Start))
            {
                return distances;
            }

            distances[query.Start] = 0;
            if (stopAt.HasValue && stopAt.Value == query.Start)
            {
                return distances;
            }

            var frontier = new Queue<HexCoord>();
            frontier.Enqueue(query.Start);

            while (frontier.Count > 0)
            {
                var current = frontier.Dequeue();
                var currentCost = distances[current];
                if (currentCost >= query.MovePoints)
                {
                    // 예산 경계에 닿은 칸은 목록에는 남지만 더 뻗지 않는다.
                    continue;
                }

                foreach (var neighbor in current.NeighborsInDirectionOrder())
                {
                    if (distances.ContainsKey(neighbor))
                    {
                        // 균일 비용이므로 첫 발견이 곧 최단이다 — 갱신(relaxation)이 필요 없다.
                        continue;
                    }

                    if (!CanEnter(map, current, neighbor, query, runtimeStates, terrainTraits, out var enterCost))
                    {
                        continue;
                    }

                    if (enterCost != UniformStepCost)
                    {
                        throw new System.InvalidOperationException(
                            $"HexPathfinder는 균일 진입 비용({UniformStepCost})을 전제로 BFS를 돈다. " +
                            $"{neighbor}의 진입 비용이 {enterCost}다. 지형별 이동 비용을 되살리려면 " +
                            "CalculateDistances를 힙 기반 다익스트라로 되돌려야 한다.");
                    }

                    var nextCost = currentCost + enterCost;
                    if (nextCost > query.MovePoints)
                    {
                        continue;
                    }

                    distances[neighbor] = nextCost;
                    previous[neighbor] = current;

                    if (stopAt.HasValue && neighbor == stopAt.Value)
                    {
                        return distances;
                    }

                    frontier.Enqueue(neighbor);
                }
            }

            return distances;
        }
    }
}

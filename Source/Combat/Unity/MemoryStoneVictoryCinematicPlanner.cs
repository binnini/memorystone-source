using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    // One reveal wave of the memory-stone victory cinematic: the cells that pop at a normalized
    // position along the camera route. Built by MemoryStoneVictoryCinematicPlanner, consumed by the
    // host's sweep coroutine.
    internal readonly struct MemoryStoneVictoryWave
    {
        public MemoryStoneVictoryWave(IReadOnlyList<HexCoord> cells, float normalizedProgress)
        {
            Cells = cells ?? System.Array.Empty<HexCoord>();
            NormalizedProgress = Mathf.Clamp01(normalizedProgress);
        }

        public IReadOnlyList<HexCoord> Cells { get; }
        public float NormalizedProgress { get; }
    }

    // Pure planning math for the memory-stone victory cinematic, extracted from MapCombatController
    // (P4 Stage 4): focus-coord ordering (PlayerSpawn -> authored VictoryCameraPoints -> MemoryStone),
    // BFS reveal-route sampling between focus coords, and reveal waves along that route. Operates on
    // map data only — camera pose math lives in CombatCameraController and the sequence pacing stays
    // on the host. MapCombatController keeps name-identical private static stubs for the members the
    // EditMode tests invoke by reflection.
    internal static class MemoryStoneVictoryCinematicPlanner
    {
        private const string VictoryCameraPointObjectType = "VictoryCameraPoint";

        internal static IReadOnlyList<HexCoord> BuildMemoryStoneVictoryFocusCoords(
            HexMapData map,
            HexCoord startCoord,
            HexCoord memoryStoneCoord)
        {
            if (map == null)
            {
                return new[] { memoryStoneCoord };
            }

            var coords = new List<HexCoord>();
            AddMemoryStoneVictoryFocusCoord(coords, map, startCoord);
            foreach (var point in GetOrderedVictoryCameraPoints(map))
            {
                AddMemoryStoneVictoryFocusCoord(coords, map, point.Coord);
            }
            AddMemoryStoneVictoryFocusCoord(coords, map, memoryStoneCoord);

            return coords.Count > 0
                ? coords.ToArray()
                : new[] { memoryStoneCoord };
        }

        private static void AddMemoryStoneVictoryFocusCoord(ICollection<HexCoord> coords, HexMapData map, HexCoord coord)
        {
            if (coords == null || map == null || !map.Contains(coord))
            {
                return;
            }

            if (coords.Count > 0 && coords.Last() == coord)
            {
                return;
            }

            coords.Add(coord);
        }

        private const string IntroCameraPointObjectType = "IntroCameraPoint";
        private const string VictoryEndCameraPointObjectType = "VictoryEndCameraPoint";

        private static bool IsVictoryCameraPointObject(HexMapObjectData objectData) =>
            string.Equals(objectData.ObjectType, VictoryCameraPointObjectType, System.StringComparison.Ordinal);

        private static bool IsIntroCameraPointObject(HexMapObjectData objectData) =>
            string.Equals(objectData.ObjectType, IntroCameraPointObjectType, System.StringComparison.Ordinal);

        // Authored stage-intro dolly points, in playback order. Same ordering rule as the victory path
        // (first integer found in role/objectRef/objectId, then coord, then id) so authoring reads the same
        // for both camera path types. Reused by the stage-intro trailer builder.
        internal static IEnumerable<HexMapObjectData> GetOrderedIntroCameraPoints(HexMapData map)
        {
            return map == null
                ? Enumerable.Empty<HexMapObjectData>()
                : map.ObjectRefs
                    .Where(IsIntroCameraPointObject)
                    .Where(obj => map.Contains(obj.Coord))
                    .OrderBy(ResolveVictoryCameraPointOrder)
                    .ThenBy(obj => obj.Coord.Q)
                    .ThenBy(obj => obj.Coord.R)
                    .ThenBy(obj => obj.ObjectId, System.StringComparer.Ordinal);
        }

        /// <summary>
        /// The single authored marker for where the victory cut-3 camera comes to REST around the memory
        /// stone. Unlike <see cref="GetOrderedVictoryCameraPoints"/> this is a singleton per map: the
        /// composition has exactly one resting place, so extra markers are a mistake rather than a chain.
        /// Ordering is applied anyway (same rule as the camera paths) so a map that ended up with several
        /// still resolves deterministically to the lowest-ordered one.
        /// </summary>
        /// <remarks>
        /// Exists because the previous framing was derived from the nearest photogenic Landmark, and a map
        /// with no landmark at all (TutorialSource) fell back to a scene-level yaw that had no relationship
        /// to that map — the camera orbited straight through a building two cells from the stone. The
        /// resting framing is a per-map authoring decision, so it belongs in the map.
        /// </remarks>
        internal static bool TryGetVictoryEndCameraPoint(HexMapData map, out HexMapObjectData point)
        {
            point = default;
            if (map == null)
            {
                return false;
            }

            var found = false;
            var bestOrder = int.MaxValue;
            foreach (var objectData in map.ObjectRefs)
            {
                if (!objectData.IsVictoryEndCameraPoint || !map.Contains(objectData.Coord))
                {
                    continue;
                }

                var order = ResolveVictoryCameraPointOrder(objectData);
                if (found && order >= bestOrder)
                {
                    continue;
                }

                found = true;
                bestOrder = order;
                point = objectData;
            }

            return found;
        }

        internal static bool HasUsableVictoryCameraPoints(HexMapData map) =>
            map != null && GetOrderedVictoryCameraPoints(map).Any();

        internal static IEnumerable<HexMapObjectData> GetOrderedVictoryCameraPoints(HexMapData map)
        {
            return map == null
                ? Enumerable.Empty<HexMapObjectData>()
                : map.ObjectRefs
                    .Where(IsVictoryCameraPointObject)
                    .Where(obj => map.Contains(obj.Coord))
                    .OrderBy(ResolveVictoryCameraPointOrder)
                    .ThenBy(obj => obj.Coord.Q)
                    .ThenBy(obj => obj.Coord.R)
                    .ThenBy(obj => obj.ObjectId, System.StringComparer.Ordinal);
        }

        internal static string FormatVictoryCameraPointDebugText(HexMapData map)
        {
            if (map == null)
            {
                return "VictoryCameraPoint: map not loaded";
            }

            var points = GetOrderedVictoryCameraPoints(map).ToArray();
            var endLine = TryGetVictoryEndCameraPoint(map, out var endPoint)
                ? $"VictoryEndCameraPoint: coord={endPoint.Coord} id={endPoint.ObjectId} (cut 3 resting framing)"
                : "VictoryEndCameraPoint: none (cut 3 falls back to backdrop landmark / authored yaw)";

            if (points.Length == 0)
            {
                return $"VictoryCameraPoint: 0 (fallback PlayerSpawn -> MemoryStone path)\n{endLine}";
            }

            var lines = new List<string> { $"VictoryCameraPoint: {points.Length} (PlayerSpawn -> points -> MemoryStone)" };
            for (var i = 0; i < points.Length; i++)
            {
                var point = points[i];
                var order = ResolveVictoryCameraPointOrder(point);
                var orderLabel = order == int.MaxValue ? "none" : order.ToString();
                lines.Add($"{i}: order={orderLabel} coord={point.Coord} id={point.ObjectId} role={point.Role}");
            }

            lines.Add(endLine);
            return string.Join("\n", lines);
        }

        internal static IReadOnlyList<HexCoord> BuildMemoryStoneVictoryRevealRouteSamples(
            HexMapData map,
            IReadOnlyList<HexCoord> focusCoords)
        {
            if (map == null || focusCoords == null || focusCoords.Count == 0)
            {
                return System.Array.Empty<HexCoord>();
            }

            var route = new List<HexCoord>();
            foreach (var focusCoord in focusCoords.Where(map.Contains))
            {
                if (route.Count == 0)
                {
                    route.Add(focusCoord);
                    continue;
                }

                var segment = FindMemoryStoneVictoryRevealSegment(map, route[route.Count - 1], focusCoord);
                foreach (var coord in segment.Skip(1))
                {
                    if (route.Count == 0 || route[route.Count - 1] != coord)
                    {
                        route.Add(coord);
                    }
                }
            }

            return route;
        }

        private static IReadOnlyList<HexCoord> FindMemoryStoneVictoryRevealSegment(
            HexMapData map,
            HexCoord start,
            HexCoord destination)
        {
            if (map == null || !map.Contains(start) || !map.Contains(destination))
            {
                return System.Array.Empty<HexCoord>();
            }

            if (start == destination)
            {
                return new[] { start };
            }

            var previous = new Dictionary<HexCoord, HexCoord>();
            var visited = new HashSet<HexCoord> { start };
            var queue = new Queue<HexCoord>();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var neighbor in current.NeighborsInDirectionOrder()
                    .Where(map.Contains)
                    .OrderBy(coord => coord.DistanceTo(destination))
                    .ThenBy(coord => coord.Q)
                    .ThenBy(coord => coord.R))
                {
                    if (!visited.Add(neighbor))
                    {
                        continue;
                    }

                    previous[neighbor] = current;
                    if (neighbor == destination)
                    {
                        return ReconstructMemoryStoneVictoryRevealSegment(previous, start, destination);
                    }

                    queue.Enqueue(neighbor);
                }
            }

            return new[] { start, destination };
        }

        private static IReadOnlyList<HexCoord> ReconstructMemoryStoneVictoryRevealSegment(
            IReadOnlyDictionary<HexCoord, HexCoord> previous,
            HexCoord start,
            HexCoord destination)
        {
            var path = new List<HexCoord> { destination };
            var current = destination;
            while (current != start)
            {
                if (!previous.TryGetValue(current, out current))
                {
                    return new[] { start, destination };
                }

                path.Add(current);
            }

            path.Reverse();
            return path;
        }

        /// <summary>
        /// Buckets EVERY map cell into a reveal wave, so the purification wipe covers the whole map instead
        /// of only a corridor around the route. Each cell is assigned to its nearest route sample; a cell
        /// off to the side is pushed later by <paramref name="lateralSpreadPercent"/> per hex of sideways
        /// distance, so the light reads as spreading outward from the sweep rather than switching on in a
        /// hard band. Wave progress is normalized against the last occupied bucket, so the final cell always
        /// lands exactly at progress 1 and nothing is left to pop after the sweep.
        /// </summary>
        /// <remarks>
        /// Replaced the previous radius-filtered version, which revealed only the cells within
        /// <c>revealRadius</c> of the route — measured on Stage_1 that covered 65% of 1938 cells and left
        /// 672 to appear in a single frame at the end (the "purification stops partway" playtest report).
        /// <paramref name="lateralSpreadPercent"/> 0 reproduces a pure along-route order.
        /// </remarks>
        internal static IReadOnlyList<MemoryStoneVictoryWave> BuildMemoryStoneVictoryWavesAlongRoute(
            HexMapData map,
            IReadOnlyList<HexCoord> route,
            int lateralSpreadPercent,
            HexCoord fallbackCoord)
        {
            if (map == null || route == null || route.Count == 0)
            {
                return new[] { new MemoryStoneVictoryWave(new[] { fallbackCoord }, 1f) };
            }

            var lateral = Mathf.Max(0, lateralSpreadPercent) / 100f;
            var buckets = new Dictionary<int, List<HexCoord>>();
            var maxBucket = 0;

            foreach (var coord in map.AllCells.Select(cell => cell.Coord))
            {
                var nearestIndex = 0;
                var nearestDistance = int.MaxValue;
                for (var i = 0; i < route.Count; i++)
                {
                    var distance = route[i].DistanceTo(coord);
                    if (distance >= nearestDistance)
                    {
                        continue;
                    }

                    nearestDistance = distance;
                    nearestIndex = i;
                    if (distance == 0)
                    {
                        break;
                    }
                }

                var bucket = Mathf.RoundToInt(nearestIndex + nearestDistance * lateral);
                maxBucket = Mathf.Max(maxBucket, bucket);
                if (!buckets.TryGetValue(bucket, out var cells))
                {
                    cells = new List<HexCoord>();
                    buckets[bucket] = cells;
                }

                cells.Add(coord);
            }

            var denominator = Mathf.Max(1, maxBucket);
            var waves = new List<MemoryStoneVictoryWave>();
            for (var bucket = 0; bucket <= maxBucket; bucket++)
            {
                if (!buckets.TryGetValue(bucket, out var cells))
                {
                    continue;
                }

                cells.Sort((a, b) => a.Q != b.Q ? a.Q.CompareTo(b.Q) : a.R.CompareTo(b.R));
                waves.Add(new MemoryStoneVictoryWave(cells, bucket / (float)denominator));
            }

            return waves.Count > 0
                ? waves.ToArray()
                : new[] { new MemoryStoneVictoryWave(new[] { fallbackCoord }, 1f) };
        }

        /// <summary>
        /// How long the cut-2 side pass runs, derived from how far the camera actually travels rather than
        /// being a flat number of seconds. Cut 1 already paces itself in world units per second; cut 2 doing
        /// the same is what makes both cuts feel the same on maps of different sizes.
        /// </summary>
        /// <remarks>
        /// The flat 5 seconds was tuned against Stage_1's 117u route, where the authored 0.05..0.45 window is
        /// ~47u — a 9.4 u/s drift. TutorialSource's route is 54u, so the same window is ~21u and the pass
        /// crawled at 4.3 u/s. Clamped at both ends so a very short route still reads as a shot rather than a
        /// flash, and a very long one does not stall the ending.
        /// </remarks>
        internal static float ResolveVictorySideViewSeconds(
            float travelDistance,
            float unitsPerSecond,
            float minSeconds,
            float maxSeconds)
        {
            var cappedMin = Mathf.Max(0f, minSeconds);
            var cappedMax = Mathf.Max(cappedMin, maxSeconds);
            if (unitsPerSecond <= 0.01f)
            {
                return cappedMax;
            }

            return Mathf.Clamp(Mathf.Max(0f, travelDistance) / unitsPerSecond, cappedMin, cappedMax);
        }

        /// <summary>
        /// Sideways distance the cut-2 camera actually flies at, capped against the map's own size. The
        /// authored offset is a framing distance and is used as-is wherever the map can hold it; on a small
        /// map it would put the camera past the edge, filming the void beside the city instead of the city.
        /// </summary>
        /// <remarks>
        /// The sign is preserved: it picks which side of the street the pass watches from, which is an
        /// authoring choice and not something a size cap should overturn.
        /// </remarks>
        internal static float ResolveVictorySideViewLateralOffset(
            float authoredOffset,
            float mapMinExtent,
            float maxExtentFraction)
        {
            if (mapMinExtent <= 0f || maxExtentFraction <= 0f)
            {
                return authoredOffset;
            }

            var cap = mapMinExtent * maxExtentFraction;
            return Mathf.Sign(authoredOffset) * Mathf.Min(Mathf.Abs(authoredOffset), cap);
        }

        private static int ResolveVictoryCameraPointOrder(HexMapObjectData objectData)
        {
            if (TryParseFirstInteger(objectData.Role, out var roleOrder))
            {
                return roleOrder;
            }

            if (TryParseFirstInteger(objectData.ObjectRef, out var refOrder))
            {
                return refOrder;
            }

            return TryParseFirstInteger(objectData.ObjectId, out var idOrder)
                ? idOrder
                : int.MaxValue;
        }

        private static bool TryParseFirstInteger(string value, out int result)
        {
            result = 0;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var start = -1;
            for (var i = 0; i < value.Length; i++)
            {
                if (char.IsDigit(value[i]))
                {
                    start = i;
                    break;
                }
            }

            if (start < 0)
            {
                return false;
            }

            var end = start;
            while (end < value.Length && char.IsDigit(value[end]))
            {
                end++;
            }

            return int.TryParse(value.Substring(start, end - start), out result);
        }
    }
}

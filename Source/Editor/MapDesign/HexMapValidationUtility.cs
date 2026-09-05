using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;

namespace SeoulPlayup.MapDesign.Editor
{
    public static class HexMapValidationUtility
    {
        public static HexMapValidationReport Validate(HexSparseMapAuthoringSource source, IEnumerable<string> knownTerrainIds)
        {
            var report = new HexMapValidationReport();
            if (source == null)
            {
                report.AddError("No HexSparseMapAuthoringSource is selected.");
                return report;
            }

            if (source.BoardPurpose == HexMapPurpose.Unspecified)
            {
                report.AddWarning("Board purpose is unspecified; set `smoke_board` or `playable_board` explicitly.");
            }
            else
            {
                report.AddInfo($"Board purpose: {source.BoardPurpose}.");
            }

            if (source.Cells == null || source.Cells.Any(cell => cell == null))
            {
                report.AddError("Sparse map contains one or more missing serialized cells.");
            }

            if (source.HasDuplicateCoordinates(out var duplicates))
            {
                report.AddError($"Sparse map contains duplicate coordinates: {string.Join(", ", duplicates)}.");
            }

            if (!source.TryToHexMapData(out var map, out var error))
            {
                report.AddError(error);
                return report;
            }

            report.AddInfo($"Board converts successfully: {map.Count} cells.");
            ValidateMonsterSpawnRefs(map, report);
            ValidateAreas(map, report);
            ValidateStartAndObjective(map, report);
            ValidateWalkableIslands(map, report);
            ValidateTerrainIds(map, knownTerrainIds, report);
            return report;
        }

        public static HexMapValidationReport Validate(HexSparseMapAuthoringSource source, HexTerrainPalette terrainPalette)
        {
            return Validate(source, BuildKnownTerrainIds(terrainPalette));
        }

        public static HexMapValidationReport Validate(HexSparseMapAuthoringSource source, AtlasTileCatalog atlasCatalog, IEnumerable<string> knownTerrainIds = null)
        {
            var report = Validate(source, knownTerrainIds);
            if (atlasCatalog == null)
            {
                report.AddError("Runtime atlas visual validation requires an AtlasTileCatalog reference.");
                return report;
            }

            if (source == null)
            {
                return report;
            }

            foreach (var message in atlasCatalog.Validate())
            {
                report.AddError(message);
            }

            if (!source.TryToHexMapData(out var map, out var error))
            {
                report.AddError(error);
                return report;
            }

            foreach (var cell in map.AllCells)
            {
                foreach (var message in atlasCatalog.ValidateCell(cell))
                {
                    report.AddError(message);
                }
            }

            return report;
        }

        public static IReadOnlyList<string> BuildKnownTerrainIds(HexTerrainPalette terrainPalette)
        {
            var known = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            if (terrainPalette != null)
            {
                foreach (var terrainId in terrainPalette.TerrainTypeIds)
                {
                    known.Add(terrainId);
                }
            }

            return known.ToArray();
        }

        private static void ValidateMonsterSpawnRefs(HexMapData map, HexMapValidationReport report)
        {
            if (map.MonsterSpawnRefs.Count == 0)
            {
                report.AddWarning("No monster spawn refs are configured; runtime monster pressure will require explicit manual/test placement.");
                return;
            }

            foreach (var spawnRef in map.MonsterSpawnRefs)
            {
                if (!map.TryGetCell(spawnRef.Coord, out var cell))
                {
                    report.AddError($"Monster spawn ref {spawnRef.Id} targets missing coordinate {spawnRef.Coord}.");
                    continue;
                }

                if (!cell.BaseWalkable)
                {
                    report.AddError($"Monster spawn ref {spawnRef.Id} targets unwalkable coordinate {spawnRef.Coord}.");
                    continue;
                }

                report.AddInfo($"Monster spawn ref: {spawnRef.Id} -> {spawnRef.MonsterId} at {spawnRef.Coord} ({spawnRef.SpawnRole}).");
            }
        }

        /// <summary>
        /// 저작 영역 규칙. 보스 아레나는 런타임에 <b>결계</b>가 되므로 저작 실수의 대가가 크다:
        /// 바인딩이 틀리면 결계가 영영 닫히지 않거나(보스 없음) 플레이어가 싸울 수 없는 주머니에
        /// 갇힌다(단절된 아레나). 그래서 바인딩·연결성·봉쇄 가능성을 여기서 모두 확인한다.
        /// </summary>
        private static void ValidateAreas(HexMapData map, HexMapValidationReport report)
        {
            if (map.Areas.Count == 0)
            {
                return;
            }

            var walkableBoard = new HashSet<HexCoord>(map.AllCells.Where(cell => cell.BaseWalkable).Select(cell => cell.Coord));
            var playerSpawns = map.ObjectRefs.Where(objectRef => objectRef.IsPlayerSpawn).ToArray();

            foreach (var area in map.Areas)
            {
                if (!area.IsBossArena)
                {
                    report.AddInfo($"Area: {area.Id} ({area.Coords.Count} cells, purpose `{area.Purpose}`).");
                    continue;
                }

                var spawnRef = map.MonsterSpawnRefs
                    .FirstOrDefault(candidate => string.Equals(candidate.Id, area.BossSpawnRefId, StringComparison.Ordinal));
                if (string.IsNullOrEmpty(spawnRef.Id))
                {
                    report.AddError($"Boss arena `{area.Id}` references missing monster spawn ref `{area.BossSpawnRefId}`; its barrier could never close.");
                    continue;
                }

                if (!area.Contains(spawnRef.Coord))
                {
                    report.AddError($"Boss arena `{area.Id}` does not contain its bound boss spawn `{spawnRef.Id}` at {spawnRef.Coord}.");
                }

                if (!MonsterSpawnRoles.IsBoss(spawnRef.SpawnRole))
                {
                    report.AddError($"Boss arena `{area.Id}` is bound to spawn `{spawnRef.Id}` with role `{spawnRef.SpawnRole}`; expected `{MonsterSpawnRoles.Boss}`.");
                }

                if (!IsAreaWalkableConnected(map, area))
                {
                    report.AddError($"Boss arena `{area.Id}` is not internally connected through walkable cells; sealing it would trap the player in a pocket away from the boss.");
                }

                // 결계는 아레나 바깥 링을 막아 성립한다. 링에 실제 셀이 하나도 없다면(= 아레나가 판 전체)
                // 봉인해도 아무것도 닫히지 않는다.
                var ringCellCount = area.EnumerateBoundaryRing().Count(coord => map.Contains(coord));
                if (ringCellCount == 0)
                {
                    report.AddWarning($"Boss arena `{area.Id}` has no board cells outside it; its barrier would seal nothing.");
                }

                if (playerSpawns.Any(spawn => area.Contains(spawn.Coord)))
                {
                    report.AddWarning($"Boss arena `{area.Id}` contains a PlayerSpawn; combat would begin inside the arena instead of the player walking into an encounter.");
                }

                if (walkableBoard.Count > 0 && area.Coords.Count(walkableBoard.Contains) == walkableBoard.Count)
                {
                    report.AddWarning($"Boss arena `{area.Id}` covers every walkable cell on the board.");
                }

                report.AddInfo($"Boss arena: {area.Id} ({area.Coords.Count} cells, ring {ringCellCount} cells) bound to spawn {spawnRef.Id} -> {spawnRef.MonsterId} at {spawnRef.Coord}.");
            }
        }

        private static bool IsAreaWalkableConnected(HexMapData map, HexMapAreaRef area)
        {
            var remaining = new HashSet<HexCoord>(area.Coords.Where(coord => map.TryGetCell(coord, out var cell) && cell.BaseWalkable));
            if (remaining.Count <= 1)
            {
                return true;
            }

            var open = new Queue<HexCoord>();
            var start = remaining.First();
            remaining.Remove(start);
            open.Enqueue(start);
            while (open.Count > 0)
            {
                var coord = open.Dequeue();
                foreach (var direction in HexCoord.Directions)
                {
                    var next = coord + direction;
                    if (remaining.Remove(next))
                    {
                        open.Enqueue(next);
                    }
                }
            }

            return remaining.Count == 0;
        }

        private static void ValidateStartAndObjective(HexMapData map, HexMapValidationReport report)
        {
            var starts = map.ObjectRefs.Where(objectRef => objectRef.IsPlayerSpawn).ToArray();
            var walkableStartCandidates = new List<HexCellData>();
            foreach (var start in starts)
            {
                if (map.TryGetCell(start.Coord, out var cell))
                {
                    walkableStartCandidates.Add(cell);
                }
            }

            if (starts.Length == 0)
            {
                walkableStartCandidates.AddRange(map.AllCells.Where(cell => cell.BaseWalkable));
            }

            if (starts.Length == 0)
            {
                if (walkableStartCandidates.Count == 0)
                {
                    report.AddError("Board has no walkable start candidate.");
                }
                else
                {
                    report.AddWarning("No PlayerSpawn objectRef is authored; validation used generic walkable cells as start candidates.");
                }
            }
            else if (starts.Any(start => !map.TryGetCell(start.Coord, out var cell) || !cell.BaseWalkable))
            {
                report.AddError("A PlayerSpawn objectRef targets a missing or unwalkable cell.");
            }

            var objectiveBinding = map.ObjectiveBindings.FirstOrDefault(binding => binding.IsConfigured);
            if (objectiveBinding.IsConfigured)
            {
                report.AddInfo($"Objective binding: {objectiveBinding.ObjectiveId} -> {objectiveBinding.LandmarkId}.");
            }
            else
            {
                // Authored `ObjectiveBindings` are only produced by ObjectiveMarker objects. Shipping stage
                // maps instead express the objective as a MemoryStone object, which the runtime resolves via
                // CombatObjectiveBindingResolver. Recognize that convention here so those maps are validated
                // (and not falsely warned) even without an authored binding.
                if (TryResolveMemoryStoneObjectiveCoord(map, out var memoryStoneCoord, out var memoryStoneObjectId))
                {
                    report.AddInfo($"Objective resolved at runtime from MemoryStone object `{memoryStoneObjectId}` at {memoryStoneCoord}; authored objective binding is not required.");
                    ValidateResolvedObjectiveReachability(map, memoryStoneCoord, "MemoryStone objective", starts, walkableStartCandidates, report);
                }
                else
                {
                    report.AddWarning("No authored objective binding or MemoryStone object is configured; objective landmark validation was skipped.");
                }

                return;
            }

            var objectiveLandmarkId = objectiveBinding.LandmarkId;
            var objectives = map.AllCells.Where(cell => string.Equals(cell.LandmarkId, objectiveLandmarkId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (objectives.Length == 0)
            {
                report.AddError($"Expected exactly one `{objectiveLandmarkId}` objective landmark from board objective binding, found none.");
                return;
            }

            if (objectives.Length != 1)
            {
                report.AddError($"Expected exactly one `{objectiveLandmarkId}` objective landmark from board objective binding, found {objectives.Length}.");
            }

            var objective = objectives[0];
            if (!objective.BaseWalkable)
            {
                report.AddError($"The `{objectiveLandmarkId}` objective cell is not walkable.");
            }

            var reachableFromAnyCandidate = walkableStartCandidates.Any(candidate => candidate.Coord != objective.Coord && CanReach(map, candidate.Coord, objective.Coord));
            if (!reachableFromAnyCandidate && walkableStartCandidates.Count == 1 && walkableStartCandidates[0].Coord == objective.Coord)
            {
                report.AddError("Objective is the only walkable cell; no separate walkable start candidate can reach it.");
            }
            else if (!reachableFromAnyCandidate)
            {
                var startDescription = starts.Length > 0 ? $"start {starts[0].Coord}" : "any walkable start candidate";
                report.AddError($"Objective {objective.Coord} is not reachable from {startDescription} through walkable cells.");
            }
        }

        // Mirrors CombatObjectiveBindingResolver.TryResolveMemoryStone selection (primary role first, then
        // coordinate order). The resolver is internal to the Combat.Runtime assembly, so the lightweight
        // lookup is replicated here rather than referenced.
        private static bool TryResolveMemoryStoneObjectiveCoord(HexMapData map, out HexCoord coord, out string objectId)
        {
            var memoryStone = map.ObjectRefs
                .Where(obj => obj.IsConfigured && string.Equals(obj.ObjectType, "MemoryStone", StringComparison.Ordinal))
                .OrderByDescending(obj => IsPrimaryMemoryStoneRole(obj.Role))
                .ThenBy(obj => obj.Coord)
                .FirstOrDefault();
            if (memoryStone.IsConfigured)
            {
                coord = memoryStone.Coord;
                objectId = memoryStone.ObjectId;
                return true;
            }

            coord = default;
            objectId = null;
            return false;
        }

        private static bool IsPrimaryMemoryStoneRole(string role)
        {
            return string.Equals(role, "main", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(role, "objective", StringComparison.OrdinalIgnoreCase);
        }

        private static void ValidateResolvedObjectiveReachability(
            HexMapData map,
            HexCoord objectiveCoord,
            string label,
            IReadOnlyList<HexMapObjectData> starts,
            IReadOnlyList<HexCellData> walkableStartCandidates,
            HexMapValidationReport report)
        {
            if (!map.TryGetCell(objectiveCoord, out var objective))
            {
                report.AddError($"The {label} at {objectiveCoord} targets a missing cell.");
                return;
            }

            if (!objective.BaseWalkable)
            {
                report.AddError($"The {label} cell at {objectiveCoord} is not walkable.");
            }

            var reachableFromAnyCandidate = walkableStartCandidates.Any(candidate => candidate.Coord != objective.Coord && CanReach(map, candidate.Coord, objective.Coord));
            if (!reachableFromAnyCandidate && walkableStartCandidates.Count == 1 && walkableStartCandidates[0].Coord == objective.Coord)
            {
                report.AddError($"The {label} is the only walkable cell; no separate walkable start candidate can reach it.");
            }
            else if (!reachableFromAnyCandidate)
            {
                var startDescription = starts.Count > 0 ? $"start {starts[0].Coord}" : "any walkable start candidate";
                report.AddError($"{label} {objective.Coord} is not reachable from {startDescription} through walkable cells.");
            }
        }

        private static void ValidateWalkableIslands(HexMapData map, HexMapValidationReport report)
        {
            var walkable = new HashSet<HexCoord>(map.AllCells.Where(cell => cell.BaseWalkable).Select(cell => cell.Coord));
            if (walkable.Count == 0)
            {
                report.AddError("Board has no walkable cells.");
                return;
            }

            var components = 0;
            while (walkable.Count > 0)
            {
                components++;
                FloodRemove(map, walkable.First(), walkable);
            }

            if (components > 1)
            {
                report.AddWarning($"Board contains {components} disconnected walkable islands.");
            }
        }

        private static void ValidateTerrainIds(HexMapData map, IEnumerable<string> knownTerrainIds, HexMapValidationReport report)
        {
            if (knownTerrainIds == null)
            {
                return;
            }

            var known = new HashSet<string>(knownTerrainIds.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
            if (known.Count == 0)
            {
                return;
            }

            var unknown = map.AllCells
                .Select(cell => cell.TerrainTypeId)
                .Where(id => !string.IsNullOrWhiteSpace(id) && !known.Contains(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (var terrainId in unknown)
            {
                report.AddWarning($"Unknown terrain id `{terrainId}` is not in the editor's known terrain list.");
            }
        }

        private static bool CanReach(HexMapData map, HexCoord start, HexCoord target)
        {
            if (!map.TryGetCell(start, out var startCell) || !startCell.BaseWalkable)
            {
                return false;
            }

            if (!map.TryGetCell(target, out var targetCell) || !targetCell.BaseWalkable)
            {
                return false;
            }

            var open = new Queue<HexCoord>();
            var visited = new HashSet<HexCoord> { start };
            open.Enqueue(start);
            while (open.Count > 0)
            {
                var coord = open.Dequeue();
                if (coord == target)
                {
                    return true;
                }

                foreach (var direction in HexCoord.Directions)
                {
                    var next = coord + direction;
                    if (visited.Contains(next) || !map.TryGetCell(next, out var cell) || !cell.BaseWalkable)
                    {
                        continue;
                    }

                    visited.Add(next);
                    open.Enqueue(next);
                }
            }

            return false;
        }

        private static void FloodRemove(HexMapData map, HexCoord start, ISet<HexCoord> remaining)
        {
            var open = new Queue<HexCoord>();
            remaining.Remove(start);
            open.Enqueue(start);
            while (open.Count > 0)
            {
                var coord = open.Dequeue();
                foreach (var direction in HexCoord.Directions)
                {
                    var next = coord + direction;
                    if (!remaining.Contains(next) || !map.TryGetCell(next, out var cell) || !cell.BaseWalkable)
                    {
                        continue;
                    }

                    remaining.Remove(next);
                    open.Enqueue(next);
                }
            }
        }
    }
}

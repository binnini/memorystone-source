using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Map.Unity
{
    [CreateAssetMenu(menuName = "Seoul Playup/Map/Hex Sparse Map Authoring Source")]
    public sealed class HexSparseMapAuthoringSource : ScriptableObject
    {
        [SerializeField] private List<HexSparseMapAuthoringCell> cells = new List<HexSparseMapAuthoringCell>();
        [SerializeField] private HexMapPurpose boardPurpose = HexMapPurpose.Unspecified;
        [SerializeField] private List<HexMapPatrolAreaRef> patrolAreaRefs = new List<HexMapPatrolAreaRef>();
        [SerializeField] private List<HexMapObjectRef> objectRefs = new List<HexMapObjectRef>();
        [SerializeField] private List<HexTrapRef> trapRefs = new List<HexTrapRef>();
        // 목적 영역(보스 아레나 등). append-only 저작 리스트 — 기존 에셋은 빈 리스트로 역직렬화된다.
        [SerializeField] private List<HexMapAreaAuthoringRef> areaRefs = new List<HexMapAreaAuthoringRef>();

        public IReadOnlyList<HexSparseMapAuthoringCell> Cells => cells ?? (IReadOnlyList<HexSparseMapAuthoringCell>)Array.Empty<HexSparseMapAuthoringCell>();
        public int CellCount => cells?.Count ?? 0;
        public HexMapPurpose BoardPurpose => boardPurpose;
        public IReadOnlyList<HexMapPatrolAreaRef> PatrolAreaRefs => patrolAreaRefs ?? (IReadOnlyList<HexMapPatrolAreaRef>)Array.Empty<HexMapPatrolAreaRef>();
        public IReadOnlyList<HexMapObjectRef> ObjectRefs => objectRefs ?? (IReadOnlyList<HexMapObjectRef>)Array.Empty<HexMapObjectRef>();
        public IReadOnlyList<HexTrapRef> TrapRefs => trapRefs ?? (IReadOnlyList<HexTrapRef>)Array.Empty<HexTrapRef>();
        public IReadOnlyList<HexMapAreaAuthoringRef> AreaRefs => areaRefs ?? (IReadOnlyList<HexMapAreaAuthoringRef>)Array.Empty<HexMapAreaAuthoringRef>();
        public int ObjectRefCount => objectRefs?.Count ?? 0;
        public int TrapRefCount => trapRefs?.Count ?? 0;

        public bool TryGetCell(HexCoord coord, out HexSparseMapAuthoringCell cell)
        {
            var index = IndexOf(coord);
            if (index < 0)
            {
                cell = null;
                return false;
            }

            cell = cells[index];
            return cell != null;
        }

        public void SetCell(HexCoord coord, HexSparseMapAuthoringCell cell)
        {
            if (cell == null)
            {
                RemoveCell(coord);
                return;
            }

            EnsureCells();
            var authoredCell = cell.WithCoord(coord);
            var index = IndexOf(coord);
            if (index >= 0)
            {
                cells[index] = authoredCell;
            }
            else
            {
                cells.Add(authoredCell);
            }

            SortCells();
        }

        public bool RemoveCell(HexCoord coord)
        {
            var index = IndexOf(coord);
            if (index < 0)
            {
                return false;
            }

            cells.RemoveAt(index);
            RemoveCoordFromAllPatrolAreas(coord);
            RemoveCoordFromAllAreas(coord);
            return true;
        }

        private void RemoveCoordFromAllAreas(HexCoord coord)
        {
            if (areaRefs == null || areaRefs.Count == 0)
                return;
            foreach (var areaId in areaRefs.Where(a => a != null).Select(a => a.AreaId).ToArray())
                EraseAreaCell(areaId, coord);
        }

        private void RemoveCoordFromAllPatrolAreas(HexCoord coord)
        {
            if (patrolAreaRefs == null || patrolAreaRefs.Count == 0)
                return;
            foreach (var areaId in patrolAreaRefs.Where(a => a != null).Select(a => a.PatrolAreaId).ToArray())
                ErasePatrolAreaCell(areaId, coord);
        }

        public void SetObjectRef(HexMapObjectRef objectRef)
        {
            if (objectRef == null)
            {
                return;
            }

            EnsureObjectRefs();
            var index = objectRefs.FindIndex(existing => existing != null && existing.ObjectId == objectRef.ObjectId);
            if (index >= 0)
            {
                objectRefs[index] = objectRef;
            }
            else
            {
                objectRefs.Add(objectRef);
            }

            SortObjectRefs();
        }

        public bool RemoveObjectRefsAt(HexCoord coord, HexMapObjectType? objectType = null)
        {
            if (objectRefs == null || objectRefs.Count == 0)
            {
                return false;
            }

            var removedRefs = objectRefs
                .Where(objectRef =>
                objectRef != null &&
                objectRef.Coord == coord &&
                (!objectType.HasValue || objectRef.ObjectType == objectType.Value))
                .ToArray();
            if (removedRefs.Length == 0)
            {
                return false;
            }

            var removed = objectRefs.RemoveAll(objectRef => removedRefs.Contains(objectRef));
            RemovePatrolAreasForRemovedMonsterSpawns(removedRefs);
            return removed > 0;
        }

        public bool RemoveObjectRef(string objectId, bool removePatrolAreas = true)
        {
            if (objectRefs == null || objectRefs.Count == 0 || string.IsNullOrWhiteSpace(objectId))
            {
                return false;
            }

            var removedRefs = objectRefs
                .Where(objectRef => objectRef != null && objectRef.ObjectId == objectId)
                .ToArray();
            if (removedRefs.Length == 0)
            {
                return false;
            }

            var removed = objectRefs.RemoveAll(objectRef => removedRefs.Contains(objectRef));
            if (removePatrolAreas)
            {
                RemovePatrolAreasForRemovedMonsterSpawns(removedRefs);
            }
            return removed > 0;
        }

        public bool RemoveObjectRefsOfType(HexMapObjectType objectType, out int removedCount)
        {
            removedCount = 0;
            if (objectRefs == null || objectRefs.Count == 0)
            {
                return false;
            }

            var removedRefs = objectRefs
                .Where(objectRef => objectRef != null && objectRef.ObjectType == objectType)
                .ToArray();
            if (removedRefs.Length == 0)
            {
                return false;
            }

            removedCount = objectRefs.RemoveAll(objectRef => removedRefs.Contains(objectRef));
            RemovePatrolAreasForRemovedMonsterSpawns(removedRefs);
            return removedCount > 0;
        }

        public void SetPatrolAreaRef(HexMapPatrolAreaRef patrolAreaRef)
        {
            if (patrolAreaRef == null || string.IsNullOrWhiteSpace(patrolAreaRef.PatrolAreaId))
            {
                return;
            }

            EnsurePatrolAreaRefs();
            var index = patrolAreaRefs.FindIndex(existing => existing != null && existing.PatrolAreaId == patrolAreaRef.PatrolAreaId);
            if (index >= 0)
            {
                patrolAreaRefs[index] = patrolAreaRef;
            }
            else
            {
                patrolAreaRefs.Add(patrolAreaRef);
            }

            SortPatrolAreaRefs();
        }

        public bool RemovePatrolAreaRef(string patrolAreaId)
        {
            if (patrolAreaRefs == null || patrolAreaRefs.Count == 0 || string.IsNullOrWhiteSpace(patrolAreaId))
            {
                return false;
            }

            return patrolAreaRefs.RemoveAll(area => area != null && area.PatrolAreaId == patrolAreaId) > 0;
        }

        public bool TryGetPatrolAreaRef(string patrolAreaId, out HexMapPatrolAreaRef patrolAreaRef)
        {
            patrolAreaRef = string.IsNullOrWhiteSpace(patrolAreaId)
                ? null
                : patrolAreaRefs?.FirstOrDefault(candidate => candidate != null && candidate.PatrolAreaId == patrolAreaId);
            return patrolAreaRef != null;
        }

        public bool PaintPatrolAreaCell(string patrolAreaId, HexCoord coord)
        {
            if (string.IsNullOrWhiteSpace(patrolAreaId))
            {
                return false;
            }

            TryGetPatrolAreaRef(patrolAreaId, out var existing);
            var coords = existing == null
                ? new List<HexCoord>()
                : existing.Cells.Select(cell => cell.Coord).ToList();
            if (!coords.Contains(coord))
            {
                coords.Add(coord);
            }

            SetPatrolAreaRef(new HexMapPatrolAreaRef(patrolAreaId.Trim(), coords));
            return true;
        }

        public bool ErasePatrolAreaCell(string patrolAreaId, HexCoord coord)
        {
            if (!TryGetPatrolAreaRef(patrolAreaId, out var existing))
            {
                return false;
            }

            var coords = existing.Cells.Select(cell => cell.Coord).Where(existingCoord => existingCoord != coord).ToList();
            if (coords.Count == existing.Cells.Count)
            {
                return false;
            }

            if (coords.Count == 0)
            {
                RemovePatrolAreaRef(patrolAreaId);
            }
            else
            {
                SetPatrolAreaRef(new HexMapPatrolAreaRef(patrolAreaId.Trim(), coords));
            }

            return true;
        }

        public void SetAreaRef(HexMapAreaAuthoringRef areaRef)
        {
            if (areaRef == null || string.IsNullOrWhiteSpace(areaRef.AreaId))
            {
                return;
            }

            EnsureAreaRefs();
            var index = areaRefs.FindIndex(existing => existing != null && existing.AreaId == areaRef.AreaId);
            if (index >= 0)
            {
                areaRefs[index] = areaRef;
            }
            else
            {
                areaRefs.Add(areaRef);
            }

            SortAreaRefs();
        }

        public bool RemoveAreaRef(string areaId)
        {
            if (areaRefs == null || areaRefs.Count == 0 || string.IsNullOrWhiteSpace(areaId))
            {
                return false;
            }

            return areaRefs.RemoveAll(area => area != null && area.AreaId == areaId) > 0;
        }

        public bool TryGetAreaRef(string areaId, out HexMapAreaAuthoringRef areaRef)
        {
            areaRef = string.IsNullOrWhiteSpace(areaId)
                ? null
                : areaRefs?.FirstOrDefault(candidate => candidate != null && candidate.AreaId == areaId);
            return areaRef != null;
        }

        /// <summary>
        /// 영역에 셀 하나를 더한다. 영역이 없으면 만든다. <paramref name="purpose"/>/<paramref name="bossSpawnRefId"/>는
        /// null이면 기존 값을 보존한다 — 셀 페인트가 이미 저작된 바인딩을 지우지 않도록.
        /// </summary>
        public bool PaintAreaCell(string areaId, HexCoord coord, string purpose = null, string bossSpawnRefId = null)
        {
            if (string.IsNullOrWhiteSpace(areaId))
            {
                return false;
            }

            TryGetAreaRef(areaId, out var existing);
            var coords = existing == null
                ? new List<HexCoord>()
                : existing.Cells.Select(cell => cell.Coord).ToList();
            if (!coords.Contains(coord))
            {
                coords.Add(coord);
            }

            SetAreaRef(new HexMapAreaAuthoringRef(
                areaId.Trim(),
                coords,
                purpose ?? existing?.Purpose,
                bossSpawnRefId ?? existing?.BossSpawnRefId));
            return true;
        }

        public bool EraseAreaCell(string areaId, HexCoord coord)
        {
            if (!TryGetAreaRef(areaId, out var existing))
            {
                return false;
            }

            var coords = existing.Cells.Select(cell => cell.Coord).Where(existingCoord => existingCoord != coord).ToList();
            if (coords.Count == existing.Cells.Count)
            {
                return false;
            }

            if (coords.Count == 0)
            {
                RemoveAreaRef(areaId);
            }
            else
            {
                SetAreaRef(new HexMapAreaAuthoringRef(areaId.Trim(), coords, existing.Purpose, existing.BossSpawnRefId));
            }

            return true;
        }

        public bool TryGetObjectRef(string objectId, out HexMapObjectRef objectRef)
        {
            objectRef = string.IsNullOrWhiteSpace(objectId)
                ? null
                : objectRefs?.FirstOrDefault(candidate => candidate != null && candidate.ObjectId == objectId);
            return objectRef != null;
        }

        public bool TryGetObjectRefAt(HexCoord coord, HexMapObjectType objectType, out HexMapObjectRef objectRef)
        {
            objectRef = objectRefs?.FirstOrDefault(candidate =>
                candidate != null &&
                candidate.Coord == coord &&
                candidate.ObjectType == objectType);
            return objectRef != null;
        }

        public void SetTrapRef(HexTrapRef trapRef)
        {
            if (trapRef == null)
            {
                return;
            }

            EnsureTrapRefs();
            var index = trapRefs.FindIndex(existing => existing != null && existing.TrapId == trapRef.TrapId);
            if (index >= 0)
            {
                trapRefs[index] = trapRef;
            }
            else
            {
                trapRefs.Add(trapRef);
            }

            SortTrapRefs();
        }

        public bool RemoveTrapRefsAt(HexCoord coord)
        {
            if (trapRefs == null || trapRefs.Count == 0)
            {
                return false;
            }

            return trapRefs.RemoveAll(trapRef => trapRef != null && trapRef.Coord == coord) > 0;
        }

        public bool RemoveTrapRef(string trapId)
        {
            if (trapRefs == null || trapRefs.Count == 0 || string.IsNullOrWhiteSpace(trapId))
            {
                return false;
            }

            return trapRefs.RemoveAll(trapRef => trapRef != null && trapRef.TrapId == trapId) > 0;
        }

        public bool RemoveAllTrapRefs(out int removedCount)
        {
            removedCount = 0;
            if (trapRefs == null || trapRefs.Count == 0)
            {
                return false;
            }

            removedCount = trapRefs.Count(trapRef => trapRef != null);
            if (removedCount == 0)
            {
                return false;
            }

            trapRefs.RemoveAll(trapRef => trapRef != null);
            return true;
        }

        public bool TryGetTrapRef(string trapId, out HexTrapRef trapRef)
        {
            trapRef = string.IsNullOrWhiteSpace(trapId)
                ? null
                : trapRefs?.FirstOrDefault(candidate => candidate != null && candidate.TrapId == trapId);
            return trapRef != null;
        }

        public bool TryGetTrapRefAt(HexCoord coord, out HexTrapRef trapRef)
        {
            trapRef = trapRefs?.FirstOrDefault(candidate => candidate != null && candidate.Coord == coord);
            return trapRef != null;
        }

        public HexSparseMapBounds GetPaintedBounds()
        {
            if (cells == null || cells.Count == 0)
            {
                return HexSparseMapBounds.Empty;
            }

            var found = false;
            var minQ = 0;
            var minR = 0;
            var maxQ = 0;
            var maxR = 0;
            for (var i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];
                if (cell == null)
                {
                    continue;
                }

                if (!found)
                {
                    minQ = maxQ = cell.Q;
                    minR = maxR = cell.R;
                    found = true;
                    continue;
                }

                minQ = Math.Min(minQ, cell.Q);
                minR = Math.Min(minR, cell.R);
                maxQ = Math.Max(maxQ, cell.Q);
                maxR = Math.Max(maxR, cell.R);
            }

            return found ? new HexSparseMapBounds(minQ, minR, maxQ, maxR) : HexSparseMapBounds.Empty;
        }

        public bool HasDuplicateCoordinates(out IReadOnlyList<HexCoord> duplicates)
        {
            var seen = new HashSet<HexCoord>();
            var duplicateSet = new SortedSet<HexCoord>();
            if (cells != null)
            {
                for (var i = 0; i < cells.Count; i++)
                {
                    var cell = cells[i];
                    if (cell == null)
                    {
                        continue;
                    }

                    if (!seen.Add(cell.Coord))
                    {
                        duplicateSet.Add(cell.Coord);
                    }
                }
            }

            duplicates = new List<HexCoord>(duplicateSet);
            return duplicates.Count > 0;
        }

        public void SortCells()
        {
            if (cells == null)
            {
                return;
            }

            cells.Sort(CompareCells);
        }

        public bool TryToHexMapData(out HexMapData map, out string error)
        {
            map = null;
            error = null;
            if (cells == null || cells.Count == 0)
            {
                error = "HexSparseMapAuthoringSource has no painted cells.";
                return false;
            }

            try
            {
                var hexCells = cells
                    .Where(c => c != null)
                    .Select(c => new HexCellData(
                        c.Coord, c.TilePresetId, c.TerrainTypeId,
                        1, c.BaseWalkable, false, c.EventId, c.LandmarkId, 0,
                        c.AtlasVisualId, c.HeightLevel, c.RotationSteps, c.EdgeConnectionMask));
                var hexCellList = hexCells.ToList();
                if (!TryBuildPatrolAreas(hexCellList, out var patrolAreas, out error))
                {
                    return false;
                }

                if (!TryValidatePlayerSpawnObjectRefs(hexCellList, out error))
                {
                    return false;
                }

                if (!TryBuildMonsterSpawnRefs(hexCellList, patrolAreas, out var spawnRefs, out error))
                {
                    return false;
                }

                if (!TryBuildObjectiveBindings(hexCellList, out var objBindings, out error))
                {
                    return false;
                }

                if (!TryBuildRuntimeObjectRefs(hexCellList, out var runtimeObjectRefs, out error))
                {
                    return false;
                }

                if (!TryBuildTrapRefs(hexCellList, out var runtimeTrapRefs, out error))
                {
                    return false;
                }

                if (!TryBuildAreas(hexCellList, spawnRefs, out var runtimeAreas, out error))
                {
                    return false;
                }

                map = new HexMapData(hexCellList, objBindings, spawnRefs, patrolAreas, runtimeObjectRefs, runtimeTrapRefs, runtimeAreas);
            }
            catch (Exception ex)
            {
                error = $"HexSparseMapAuthoringSource could not build map data: {ex.Message}";
                return false;
            }

            if (map == null || map.Count == 0)
            {
                error = "HexSparseMapAuthoringSource produced no map cells.";
                map = null;
                return false;
            }

            return true;
        }

        public void ConfigureForTests(
            IEnumerable<HexSparseMapAuthoringCell> authoredCells,
            HexMapPurpose boardPurpose = HexMapPurpose.Unspecified,
            IEnumerable<HexMapObjectRef> objectRefs = null,
            IEnumerable<HexMapPatrolAreaRef> patrolAreaRefs = null,
            IEnumerable<HexTrapRef> trapRefs = null,
            IEnumerable<HexMapAreaAuthoringRef> areaRefs = null)
        {
            cells = authoredCells == null ? new List<HexSparseMapAuthoringCell>() : new List<HexSparseMapAuthoringCell>(authoredCells);
            this.boardPurpose = boardPurpose;
            this.objectRefs = objectRefs == null ? new List<HexMapObjectRef>() : new List<HexMapObjectRef>(objectRefs);
            this.patrolAreaRefs = patrolAreaRefs == null ? new List<HexMapPatrolAreaRef>() : new List<HexMapPatrolAreaRef>(patrolAreaRefs);
            this.trapRefs = trapRefs == null ? new List<HexTrapRef>() : new List<HexTrapRef>(trapRefs);
            this.areaRefs = areaRefs == null ? new List<HexMapAreaAuthoringRef>() : new List<HexMapAreaAuthoringRef>(areaRefs);
            SortCells();
            SortObjectRefs();
            SortTrapRefs();
            SortAreaRefs();
        }

        private void RemovePatrolAreasForRemovedMonsterSpawns(IEnumerable<HexMapObjectRef> removedRefs)
        {
            UnbindAreasForRemovedMonsterSpawns(removedRefs);
            if (removedRefs == null || patrolAreaRefs == null || patrolAreaRefs.Count == 0)
            {
                return;
            }

            var patrolAreaIds = removedRefs
                .Where(objectRef => objectRef != null && objectRef.IsMonsterSpawn && !string.IsNullOrWhiteSpace(objectRef.PatrolAreaId))
                .Select(objectRef => objectRef.PatrolAreaId.Trim())
                .Distinct(StringComparer.Ordinal)
                .Where(patrolAreaId => objectRefs == null ||
                    !objectRefs.Any(objectRef =>
                        objectRef != null &&
                        objectRef.IsMonsterSpawn &&
                        string.Equals(objectRef.PatrolAreaId?.Trim(), patrolAreaId, StringComparison.Ordinal)))
                .ToArray();

            foreach (var patrolAreaId in patrolAreaIds)
            {
                RemovePatrolAreaRef(patrolAreaId);
            }
        }

        /// <summary>
        /// 바인딩된 보스 스폰이 사라진 보스 아레나를 지운다. 순찰 영역과 같은 처리를 하는 이유는
        /// 끊긴 바인딩이 <see cref="TryBuildAreas"/>에서 <b>맵 전체 로드 실패</b>가 되기 때문이다 —
        /// 스폰 하나를 지웠다고 맵이 안 열리면 저작이 막힌다. 페인트한 셀은 디스크 브러시로 즉시 복구된다.
        /// </summary>
        private void UnbindAreasForRemovedMonsterSpawns(IEnumerable<HexMapObjectRef> removedRefs)
        {
            if (removedRefs == null || areaRefs == null || areaRefs.Count == 0)
            {
                return;
            }

            var removedSpawnIds = removedRefs
                .Where(objectRef => objectRef != null && objectRef.IsMonsterSpawn && !string.IsNullOrWhiteSpace(objectRef.ObjectId))
                .Select(objectRef => objectRef.ObjectId.Trim())
                .Distinct(StringComparer.Ordinal)
                .Where(spawnId => objectRefs == null ||
                    !objectRefs.Any(objectRef =>
                        objectRef != null &&
                        objectRef.IsMonsterSpawn &&
                        string.Equals(objectRef.ObjectId?.Trim(), spawnId, StringComparison.Ordinal)));
            var removedSpawnIdSet = new HashSet<string>(removedSpawnIds, StringComparer.Ordinal);
            if (removedSpawnIdSet.Count == 0)
            {
                return;
            }

            var orphanedAreaIds = areaRefs
                .Where(area => area != null && !string.IsNullOrWhiteSpace(area.BossSpawnRefId) && removedSpawnIdSet.Contains(area.BossSpawnRefId.Trim()))
                .Select(area => area.AreaId)
                .ToArray();
            foreach (var areaId in orphanedAreaIds)
            {
                RemoveAreaRef(areaId);
            }
        }


        private bool TryBuildObjectiveBindings(IReadOnlyList<HexCellData> hexCells, out IReadOnlyList<HexObjectiveBinding> bindings, out string error)
        {
            var refs = new List<HexObjectiveBinding>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var landmarkIds = new HashSet<string>(
                hexCells
                    .Select(cell => cell.LandmarkId)
                    .Where(id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.Ordinal);
            var cellLookup = hexCells.ToDictionary(cell => cell.Coord);
            error = null;

            if (objectRefs != null)
            {
                foreach (var objectRef in objectRefs.Where(r => r != null && r.IsObjectiveMarker))
                {
                    if (string.IsNullOrWhiteSpace(objectRef.ObjectId))
                    {
                        error = $"HexMapObjectRef objective_marker at {objectRef.Coord} is missing objectId.";
                        bindings = Array.Empty<HexObjectiveBinding>();
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(objectRef.ObjectRef))
                    {
                        error = $"HexMapObjectRef objective_marker '{objectRef.ObjectId}' is missing objectRef landmark id.";
                        bindings = Array.Empty<HexObjectiveBinding>();
                        return false;
                    }

                    if (!cellLookup.ContainsKey(objectRef.Coord))
                    {
                        error = $"Objective marker '{objectRef.ObjectId}' from objectRefs targets missing coordinate {objectRef.Coord}.";
                        bindings = Array.Empty<HexObjectiveBinding>();
                        return false;
                    }

                    if (!TryAddObjectiveBinding(objectRef.ToObjectiveBinding(), "objectRefs", landmarkIds, ids, refs, out error))
                    {
                        bindings = Array.Empty<HexObjectiveBinding>();
                        return false;
                    }
                }
            }

            bindings = refs;
            return true;
        }

        private static bool TryAddObjectiveBinding(
            HexObjectiveBinding binding,
            string sourceLabel,
            ISet<string> landmarkIds,
            ISet<string> ids,
            ICollection<HexObjectiveBinding> bindings,
            out string error)
        {
            if (string.IsNullOrWhiteSpace(binding.ObjectiveId))
            {
                error = $"Objective binding from {sourceLabel} is missing objective id.";
                return false;
            }

            if (!ids.Add(binding.ObjectiveId))
            {
                error = $"Duplicate objective binding id '{binding.ObjectiveId}' found while converting {sourceLabel}.";
                return false;
            }

            if (!binding.IsConfigured)
            {
                error = $"Objective binding '{binding.ObjectiveId}' from {sourceLabel} is missing landmark id.";
                return false;
            }

            if (!landmarkIds.Contains(binding.LandmarkId))
            {
                error = $"Objective binding '{binding.ObjectiveId}' from {sourceLabel} targets missing landmark id '{binding.LandmarkId}'.";
                return false;
            }

            bindings.Add(binding);
            error = null;
            return true;
        }

        private bool TryBuildPatrolAreas(IReadOnlyList<HexCellData> hexCells, out IReadOnlyList<HexPatrolAreaRef> patrolAreas, out string error)
        {
            var refs = new List<HexPatrolAreaRef>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var cellLookup = hexCells.ToDictionary(cell => cell.Coord);
            error = null;

            if (patrolAreaRefs != null)
            {
                foreach (var authoredRef in patrolAreaRefs.Where(r => r != null))
                {
                    var runtimeRef = authoredRef.ToRuntimeRef();
                    if (!runtimeRef.IsConfigured)
                    {
                        continue;
                    }

                    if (!ids.Add(runtimeRef.Id))
                    {
                        error = $"Duplicate patrol area id '{runtimeRef.Id}' found while converting patrolAreaRefs.";
                        patrolAreas = Array.Empty<HexPatrolAreaRef>();
                        return false;
                    }

                    foreach (var coord in runtimeRef.Coords)
                    {
                        if (!cellLookup.TryGetValue(coord, out var cell))
                        {
                            error = $"Patrol area '{runtimeRef.Id}' targets missing coordinate {coord}.";
                            patrolAreas = Array.Empty<HexPatrolAreaRef>();
                            return false;
                        }

                        if (!cell.BaseWalkable)
                        {
                            error = $"Patrol area '{runtimeRef.Id}' targets unwalkable coordinate {coord}.";
                            patrolAreas = Array.Empty<HexPatrolAreaRef>();
                            return false;
                        }
                    }

                    refs.Add(runtimeRef);
                }
            }

            patrolAreas = refs;
            return true;
        }

        /// <summary>
        /// 목적 영역을 런타임 ref로 변환하며 저작 불변식을 강제한다. 순찰 영역과 같은 규칙(좌표 존재·walkable·
        /// id 중복 금지)에 더해, 보스 아레나는 <b>바인딩된 보스 스폰이 실재하고 아레나 안에 있어야</b> 한다 —
        /// 결계는 그 보스가 살아 있는 동안만 유지되므로, 바인딩이 끊긴 아레나는 영영 닫히지 않는 결계가 된다.
        /// </summary>
        private bool TryBuildAreas(
            IReadOnlyList<HexCellData> hexCells,
            IReadOnlyList<HexMonsterSpawnRef> spawnRefs,
            out IReadOnlyList<HexMapAreaRef> areas,
            out string error)
        {
            var refs = new List<HexMapAreaRef>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var cellLookup = hexCells.ToDictionary(cell => cell.Coord);
            error = null;

            if (areaRefs != null)
            {
                foreach (var authoredRef in areaRefs.Where(r => r != null))
                {
                    var runtimeRef = authoredRef.ToRuntimeRef();
                    if (!runtimeRef.IsConfigured)
                    {
                        continue;
                    }

                    if (!ids.Add(runtimeRef.Id))
                    {
                        error = $"Duplicate area id '{runtimeRef.Id}' found while converting areaRefs.";
                        areas = Array.Empty<HexMapAreaRef>();
                        return false;
                    }

                    foreach (var coord in runtimeRef.Coords)
                    {
                        if (!cellLookup.TryGetValue(coord, out var cell))
                        {
                            error = $"Area '{runtimeRef.Id}' targets missing coordinate {coord}.";
                            areas = Array.Empty<HexMapAreaRef>();
                            return false;
                        }

                        if (!cell.BaseWalkable)
                        {
                            error = $"Area '{runtimeRef.Id}' targets unwalkable coordinate {coord}.";
                            areas = Array.Empty<HexMapAreaRef>();
                            return false;
                        }
                    }

                    if (runtimeRef.IsBossArena && !TryValidateBossArenaBinding(runtimeRef, spawnRefs, out error))
                    {
                        areas = Array.Empty<HexMapAreaRef>();
                        return false;
                    }

                    refs.Add(runtimeRef);
                }
            }

            areas = refs;
            return true;
        }

        private static bool TryValidateBossArenaBinding(
            HexMapAreaRef area,
            IReadOnlyList<HexMonsterSpawnRef> spawnRefs,
            out string error)
        {
            if (string.IsNullOrWhiteSpace(area.BossSpawnRefId))
            {
                error = $"Boss arena '{area.Id}' is missing bossSpawnRefId.";
                return false;
            }

            var spawnRef = (spawnRefs ?? Array.Empty<HexMonsterSpawnRef>())
                .FirstOrDefault(candidate => string.Equals(candidate.Id, area.BossSpawnRefId, StringComparison.Ordinal));
            if (string.IsNullOrEmpty(spawnRef.Id))
            {
                error = $"Boss arena '{area.Id}' references missing monster spawn ref '{area.BossSpawnRefId}'.";
                return false;
            }

            if (!area.Contains(spawnRef.Coord))
            {
                error = $"Boss arena '{area.Id}' does not contain its bound boss spawn '{spawnRef.Id}' at {spawnRef.Coord}.";
                return false;
            }

            error = null;
            return true;
        }

        private bool TryValidatePlayerSpawnObjectRefs(IList<HexCellData> hexCells, out string error)
        {
            error = null;
            if (objectRefs == null)
            {
                return true;
            }

            var playerSpawns = objectRefs.Where(r => r != null && r.IsPlayerSpawn).ToArray();
            if (playerSpawns.Length == 0)
            {
                return true;
            }

            var byCoord = new Dictionary<HexCoord, HexCellData>();
            for (var i = 0; i < hexCells.Count; i++)
            {
                byCoord[hexCells[i].Coord] = hexCells[i];
            }

            foreach (var objectRef in playerSpawns)
            {
                if (string.IsNullOrWhiteSpace(objectRef.ObjectId))
                {
                    error = $"HexMapObjectRef player_spawn at {objectRef.Coord} is missing objectId.";
                    return false;
                }

                if (!byCoord.TryGetValue(objectRef.Coord, out var cell))
                {
                    error = $"Player spawn object '{objectRef.ObjectId}' targets missing coordinate {objectRef.Coord}.";
                    return false;
                }

                if (!cell.BaseWalkable)
                {
                    error = $"Player spawn object '{objectRef.ObjectId}' targets unwalkable coordinate {objectRef.Coord}.";
                    return false;
                }

                if (HasMovementBlockingObjectRefAt(objectRef.Coord))
                {
                    error = $"Player spawn object '{objectRef.ObjectId}' targets movement-blocked object coordinate {objectRef.Coord}.";
                    return false;
                }
            }

            return true;
        }

        private bool TryBuildMonsterSpawnRefs(IReadOnlyList<HexCellData> hexCells, IReadOnlyList<HexPatrolAreaRef> patrolAreas, out IReadOnlyList<HexMonsterSpawnRef> spawnRefs, out string error)
        {
            var refs = new List<HexMonsterSpawnRef>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var cellLookup = hexCells.ToDictionary(cell => cell.Coord);
            var patrolAreaIds = new HashSet<string>((patrolAreas ?? Array.Empty<HexPatrolAreaRef>()).Select(area => area.Id), StringComparer.Ordinal);
            error = null;

            if (objectRefs != null)
            {
                foreach (var objectRef in objectRefs.Where(r => r != null && r.IsMonsterSpawn))
                {
                    // 배치 랜덤화 예비 슬롯(§2-3a): 위치 후보일 뿐 점유 몬스터가 없다. 저작 원본
                    // 경로(룩뎁·프리뷰·랜덤화 off·폴백)에서는 스폰하지 않고 조용히 건너뛴다 —
                    // 랜덤화 브리지가 소스에서 직접 읽어 간다.
                    if (objectRef.IsRandomizationSpareSlot)
                    {
                        continue;
                    }

                    var runtimeRef = objectRef.ToMonsterSpawnRef();
                    if (string.IsNullOrWhiteSpace(objectRef.ObjectId))
                    {
                        error = $"HexMapObjectRef monster_spawn at {objectRef.Coord} is missing objectId.";
                        spawnRefs = Array.Empty<HexMonsterSpawnRef>();
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(objectRef.ObjectRef))
                    {
                        error = $"HexMapObjectRef monster_spawn '{objectRef.ObjectId}' is missing objectRef monster id.";
                        spawnRefs = Array.Empty<HexMonsterSpawnRef>();
                        return false;
                    }

                    if (HasMovementBlockingObjectRefAt(objectRef.Coord))
                    {
                        error = $"HexMapObjectRef monster_spawn '{objectRef.ObjectId}' targets movement-blocked object coordinate {objectRef.Coord}.";
                        spawnRefs = Array.Empty<HexMonsterSpawnRef>();
                        return false;
                    }

                    if (!TryAddMonsterSpawnRef(runtimeRef, "objectRefs", cellLookup, patrolAreaIds, ids, refs, out error))
                    {
                        spawnRefs = Array.Empty<HexMonsterSpawnRef>();
                        return false;
                    }
                }
            }

            spawnRefs = refs;
            return true;
        }

        private bool TryBuildRuntimeObjectRefs(IReadOnlyList<HexCellData> hexCells, out IReadOnlyList<HexMapObjectData> runtimeObjectRefs, out string error)
        {
            var refs = new List<HexMapObjectData>();
            error = null;

            if (objectRefs == null || objectRefs.Count == 0)
            {
                runtimeObjectRefs = refs;
                return true;
            }

            var cellLookup = hexCells.ToDictionary(cell => cell.Coord);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var objectRef in objectRefs.Where(r => r != null && (r.IsRuntimeVisualObject || r.IsPlayerSpawn ||
                r.IsVictoryCameraPoint || r.IsIntroCameraPoint || r.IsVictoryEndCameraPoint)))
            {
                // 배치 랜덤화 예비 슬롯(서비스 오브젝트): 위치 후보일 뿐 세울 물건이 없다. 저작 원본
                // 경로에서는 조용히 건너뛴다 — 랜덤화 브리지가 소스에서 직접 읽어 간다. 몬스터 스폰
                // 루프·함정 루프에 있는 것과 같은 규약이다.
                // 🔴 아래 "missing objectRef prefab id" 검증보다 반드시 앞에 있어야 한다 — 예비 슬롯은
                //    objectRef가 비어 있는 것이 정상이라, 순서가 뒤바뀌면 맵 빌드가 통째로 깨진다.
                if (objectRef.IsRandomizationSpareSlot)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(objectRef.ObjectId))
                {
                    error = $"HexMapObjectRef {objectRef.ObjectType} at {objectRef.Coord} is missing objectId.";
                    runtimeObjectRefs = Array.Empty<HexMapObjectData>();
                    return false;
                }

                if (!ids.Add(objectRef.ObjectId))
                {
                    error = $"Duplicate runtime visual object id '{objectRef.ObjectId}'.";
                    runtimeObjectRefs = Array.Empty<HexMapObjectData>();
                    return false;
                }

                if (objectRef.IsRuntimeVisualObject && string.IsNullOrWhiteSpace(objectRef.ObjectRef))
                {
                    error = $"HexMapObjectRef {objectRef.ObjectType} '{objectRef.ObjectId}' is missing objectRef prefab id.";
                    runtimeObjectRefs = Array.Empty<HexMapObjectData>();
                    return false;
                }

                foreach (var occupiedCoord in objectRef.OccupiedCoords)
                {
                    if (!cellLookup.ContainsKey(occupiedCoord))
                    {
                        error = $"Runtime object '{objectRef.ObjectId}' footprint targets missing coordinate {occupiedCoord}.";
                        runtimeObjectRefs = Array.Empty<HexMapObjectData>();
                        return false;
                    }
                }

                refs.Add(objectRef.ToRuntimeObjectData());
            }

            runtimeObjectRefs = refs;
            return true;
        }

        private bool TryBuildTrapRefs(IReadOnlyList<HexCellData> hexCells, out IReadOnlyList<HexTrapData> runtimeTrapRefs, out string error)
        {
            var refs = new List<HexTrapData>();
            error = null;

            if (trapRefs == null || trapRefs.Count == 0)
            {
                runtimeTrapRefs = refs;
                return true;
            }

            var cellLookup = hexCells.ToDictionary(cell => cell.Coord);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var trapPresetCatalog = TrapPresetCatalog.LoadDefault();
            foreach (var trapRef in trapRefs.Where(trapRef => trapRef != null))
            {
                // 배치 랜덤화 함정 예비 슬롯(Q10, §6-4): 위치 후보일 뿐 효과가 없다. 저작 원본
                // 경로에서는 조용히 건너뛴다 — 랜덤화 브리지가 소스에서 직접 읽어 간다. 태그 없는
                // 빈 함정은 아래의 「효과 0개」 에러로 여전히 막힌다(안전망 유지).
                if (trapRef.IsRandomizationSpareSlot)
                {
                    continue;
                }

                var trap = trapRef.ToRuntimeTrapData(trapPresetCatalog);
                if (string.IsNullOrWhiteSpace(trap.TrapId))
                {
                    error = $"Trap at {trap.Coord} is missing trapId.";
                    runtimeTrapRefs = Array.Empty<HexTrapData>();
                    return false;
                }

                if (!ids.Add(trap.TrapId))
                {
                    error = $"Duplicate trap id '{trap.TrapId}'.";
                    runtimeTrapRefs = Array.Empty<HexTrapData>();
                    return false;
                }

                if (!cellLookup.ContainsKey(trap.Coord))
                {
                    error = $"Trap '{trap.TrapId}' targets missing coordinate {trap.Coord}.";
                    runtimeTrapRefs = Array.Empty<HexTrapData>();
                    return false;
                }

                if (trap.Effects.Count == 0)
                {
                    error = $"Trap '{trap.TrapId}' must define at least one effect.";
                    runtimeTrapRefs = Array.Empty<HexTrapData>();
                    return false;
                }

                refs.Add(trap);
            }

            runtimeTrapRefs = refs;
            return true;
        }

        private bool TryAddMonsterSpawnRef(
            HexMonsterSpawnRef spawnRef,
            string sourceLabel,
            IReadOnlyDictionary<HexCoord, HexCellData> cellsByCoord,
            ISet<string> patrolAreaIds,
            ISet<string> ids,
            ICollection<HexMonsterSpawnRef> spawnRefs,
            out string error)
        {
            if (!ids.Add(spawnRef.Id))
            {
                error = $"Duplicate monster spawn ref id '{spawnRef.Id}' found while converting {sourceLabel}.";
                return false;
            }

            if (!cellsByCoord.TryGetValue(spawnRef.Coord, out var cell))
            {
                error = $"Monster spawn ref '{spawnRef.Id}' from {sourceLabel} targets missing coordinate {spawnRef.Coord}.";
                return false;
            }

            if (!cell.BaseWalkable)
            {
                error = $"Monster spawn ref '{spawnRef.Id}' from {sourceLabel} targets unwalkable coordinate {spawnRef.Coord}.";
                return false;
            }

            if (HasMovementBlockingObjectRefAt(spawnRef.Coord))
            {
                error = $"Monster spawn ref '{spawnRef.Id}' from {sourceLabel} targets movement-blocked object coordinate {spawnRef.Coord}.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(spawnRef.PatrolAreaId) && (patrolAreaIds == null || !patrolAreaIds.Contains(spawnRef.PatrolAreaId)))
            {
                error = $"Monster spawn ref '{spawnRef.Id}' from {sourceLabel} references missing patrol area '{spawnRef.PatrolAreaId}'.";
                return false;
            }

            spawnRefs.Add(spawnRef);
            error = null;
            return true;
        }

        private bool HasMovementBlockingObjectRefAt(HexCoord coord)
        {
            return objectRefs != null && objectRefs.Any(objectRef =>
                objectRef != null &&
                objectRef.OccupiedCoords.Contains(coord) &&
                objectRef.BlocksMovement);
        }

        private int IndexOf(HexCoord coord)
        {
            if (cells == null)
            {
                return -1;
            }

            for (var i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];
                if (cell != null && cell.Coord == coord)
                {
                    return i;
                }
            }

            return -1;
        }

        private void EnsureCells()
        {
            if (cells == null)
            {
                cells = new List<HexSparseMapAuthoringCell>();
            }
        }

        private void EnsureObjectRefs()
        {
            if (objectRefs == null)
            {
                objectRefs = new List<HexMapObjectRef>();
            }
        }

        private void EnsurePatrolAreaRefs()
        {
            if (patrolAreaRefs == null)
            {
                patrolAreaRefs = new List<HexMapPatrolAreaRef>();
            }
        }

        private void EnsureAreaRefs()
        {
            if (areaRefs == null)
            {
                areaRefs = new List<HexMapAreaAuthoringRef>();
            }
        }

        private void EnsureTrapRefs()
        {
            if (trapRefs == null)
            {
                trapRefs = new List<HexTrapRef>();
            }
        }

        private static int CompareCells(HexSparseMapAuthoringCell left, HexSparseMapAuthoringCell right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            return left.Coord.CompareTo(right.Coord);
        }

        private void SortObjectRefs()
        {
            if (objectRefs == null)
            {
                return;
            }

            objectRefs.Sort(CompareObjectRefs);
        }

        private void SortPatrolAreaRefs()
        {
            if (patrolAreaRefs == null)
            {
                return;
            }

            patrolAreaRefs.Sort((left, right) => string.Compare(left?.PatrolAreaId, right?.PatrolAreaId, StringComparison.Ordinal));
        }

        private void SortAreaRefs()
        {
            if (areaRefs == null)
            {
                return;
            }

            areaRefs.Sort((left, right) => string.Compare(left?.AreaId, right?.AreaId, StringComparison.Ordinal));
        }

        private void SortTrapRefs()
        {
            if (trapRefs == null)
            {
                return;
            }

            trapRefs.Sort(CompareTrapRefs);
        }

        private static int CompareTrapRefs(HexTrapRef left, HexTrapRef right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            var coordCompare = left.Coord.CompareTo(right.Coord);
            if (coordCompare != 0)
            {
                return coordCompare;
            }

            return string.Compare(left.TrapId, right.TrapId, StringComparison.Ordinal);
        }

        private static int CompareObjectRefs(HexMapObjectRef left, HexMapObjectRef right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            var coordCompare = left.Coord.CompareTo(right.Coord);
            if (coordCompare != 0)
            {
                return coordCompare;
            }

            var typeCompare = left.ObjectType.CompareTo(right.ObjectType);
            if (typeCompare != 0)
            {
                return typeCompare;
            }

            return string.Compare(left.ObjectId, right.ObjectId, StringComparison.Ordinal);
        }
    }

    [Serializable]
    public sealed class HexSparseMapAuthoringCell
    {
        [SerializeField] private int q;
        [SerializeField] private int r;
        [SerializeField] private string tilePresetId;
        [SerializeField] private string terrainTypeId;
        [SerializeField] private string atlasVisualId;
        [SerializeField] private string eventId;
        [SerializeField] private string landmarkId;
        [SerializeField] private int heightLevel;
        [SerializeField] private int rotationSteps;
        [SerializeField] private int edgeConnectionMask;
        [SerializeField] private bool baseWalkable = true;

        public HexSparseMapAuthoringCell()
        {
        }

        public HexSparseMapAuthoringCell(
            HexCoord coord,
            string tilePresetId,
            string terrainTypeId,
            string atlasVisualId,
            string eventId = null,
            string landmarkId = null,
            int heightLevel = 0,
            int rotationSteps = 0,
            int edgeConnectionMask = 0,
            bool baseWalkable = true)
        {
            q = coord.Q;
            r = coord.R;
            this.tilePresetId = tilePresetId;
            this.terrainTypeId = terrainTypeId;
            this.atlasVisualId = atlasVisualId;
            this.eventId = eventId;
            this.landmarkId = landmarkId;
            this.heightLevel = HexCellData.ClampHeight(heightLevel);
            this.rotationSteps = Mathf.Clamp(rotationSteps, 0, 5);
            this.edgeConnectionMask = Mathf.Clamp(edgeConnectionMask, 0, 63);
            this.baseWalkable = baseWalkable;
        }

        public int Q => q;
        public int R => r;
        public HexCoord Coord => new HexCoord(q, r);
        public string TilePresetId => tilePresetId ?? string.Empty;
        public string TerrainTypeId => terrainTypeId ?? string.Empty;
        public string AtlasVisualId => atlasVisualId ?? string.Empty;
        public string EventId => eventId ?? string.Empty;
        public string LandmarkId => landmarkId ?? string.Empty;
        public int HeightLevel => HexCellData.ClampHeight(heightLevel);
        public int RotationSteps => Mathf.Clamp(rotationSteps, 0, 5);
        public int EdgeConnectionMask => Mathf.Clamp(edgeConnectionMask, 0, 63);
        public bool BaseWalkable => baseWalkable;

        public HexSparseMapAuthoringCell WithCoord(HexCoord coord)
        {
            return new HexSparseMapAuthoringCell(coord, TilePresetId, TerrainTypeId, AtlasVisualId, EventId, LandmarkId, HeightLevel, RotationSteps, EdgeConnectionMask, BaseWalkable);
        }
    }

    public readonly struct HexSparseMapBounds : IEquatable<HexSparseMapBounds>
    {
        public static readonly HexSparseMapBounds Empty = new HexSparseMapBounds(0, 0, -1, -1, true);

        public HexSparseMapBounds(int minQ, int minR, int maxQ, int maxR)
            : this(minQ, minR, maxQ, maxR, false)
        {
        }

        private HexSparseMapBounds(int minQ, int minR, int maxQ, int maxR, bool isEmpty)
        {
            MinQ = minQ;
            MinR = minR;
            MaxQ = maxQ;
            MaxR = maxR;
            IsEmpty = isEmpty;
        }

        public int MinQ { get; }
        public int MinR { get; }
        public int MaxQ { get; }
        public int MaxR { get; }
        public bool IsEmpty { get; }
        public int Width => IsEmpty ? 0 : MaxQ - MinQ + 1;
        public int Height => IsEmpty ? 0 : MaxR - MinR + 1;
        public int Area => Width * Height;

        public bool Equals(HexSparseMapBounds other)
        {
            return MinQ == other.MinQ && MinR == other.MinR && MaxQ == other.MaxQ && MaxR == other.MaxR && IsEmpty == other.IsEmpty;
        }

        public override bool Equals(object obj) => obj is HexSparseMapBounds other && Equals(other);
        public override int GetHashCode() => (((MinQ * 397) ^ MinR) * 397 ^ MaxQ) * 397 ^ MaxR;
        public override string ToString() => IsEmpty ? "Empty" : $"({MinQ},{MinR})..({MaxQ},{MaxR})";
    }
}

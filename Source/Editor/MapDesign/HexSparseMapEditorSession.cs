using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;

namespace SeoulPlayup.MapDesign.Editor
{
    public sealed class HexSparseMapEditorSession
    {
        public const int DefaultMonsterPatrolRadius = 3;

        /// <summary>보스 아레나 기본 반경. 계획의 권장 범위(6~8) 하단 — 넓힐수록 결계 링이 길어진다.</summary>
        public const int DefaultArenaRadius = 6;

        public HexSparseMapAuthoringSource Source { get; set; }
        public HexTilePresetCatalog TilePresetCatalog { get; set; }
        public MonsterCatalogDefinition MonsterCatalog { get; set; } = CombatState.CreateMonsterCatalog(CombatConfig.Default);
        public string MonsterCatalogSourceId => MonsterCatalog?.SourceId ?? string.Empty;
        public IReadOnlyList<string> MonsterCatalogMonsterIds => MonsterCatalog == null
            ? Array.Empty<string>()
            : MonsterCatalog.Entries.Select(entry => entry.Id).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().OrderBy(id => id).ToArray();
        public string ActiveTilePresetId { get; set; }
        public string DefaultFillTilePresetId { get; set; }
        public HexMapObjectType ActiveObjectType { get; set; } = HexMapObjectType.MonsterSpawn;
        public string ActiveObjectId { get; set; }
        public string ActiveObjectRef { get; set; } = "M001";
        public string ActiveObjectRole { get; set; } = "primary_pressure";
        public HexMapPurpose ActiveObjectPurpose { get; set; } = HexMapPurpose.PlayableMap;
        public bool ActiveObjectBlocksMovement { get; set; }
        public bool ActiveObjectBlocksVision { get; set; }
        public bool ActiveObjectInteractable { get; set; }
        public IReadOnlyList<HexCoord> ActiveObjectFootprintOffsets { get; set; } = new[] { new HexCoord(0, 0) };
        public UnityEngine.Vector3 ActiveObjectVisualScaleMultiplier { get; set; } = UnityEngine.Vector3.one;
        public string ActiveObjectPatrolAreaId { get; set; }
        public float ActiveObjectCameraSpeedMultiplier { get; set; } = 1f;
        public float ActiveObjectCameraDwellSeconds { get; set; }
        public bool ActiveObjectCameraStartsNewSegment { get; set; }
        public string ActiveSpawnerPresetId { get; set; }
        public string ActivePatrolAreaId { get; set; } = "patrol-area-1";
        public int ActivePatrolAreaRadius { get; set; } = DefaultMonsterPatrolRadius;

        /// <summary>디스크 브러시 반경(셀). 0이면 단일 셀과 같다.</summary>
        public int ActiveDiskBrushRadius { get; set; } = DefaultArenaRadius;

        /// <summary>보스 아레나 브러시가 쓰는 영역 id.</summary>
        public string ActiveAreaId { get; set; } = "boss-arena-1";

        /// <summary>아레나가 묶일 보스 스폰 ref의 objectId. 비면 아레나가 결계를 만들지 못한다.</summary>
        public string ActiveAreaBossSpawnRefId { get; set; } = string.Empty;

        /// <summary>아레나 일괄 생성 반경.</summary>
        public int ActiveAreaRadius { get; set; } = DefaultArenaRadius;
        public int ActiveRotationSteps { get; set; }

        /// <summary>
        /// Free-angle yaw the object brush adds on top of <see cref="ActiveRotationSteps"/>. Tiles ignore it —
        /// their visuals and edge masks only exist on the 60° lattice — so it rides along on objects alone.
        /// </summary>
        public float ActiveRotationFineDegrees { get; set; }

        /// <summary>The brush's total yaw in [0, 360), the value the editor's rotation slider edits.</summary>
        public float ActiveYawDegrees
        {
            get => HexMapObjectData.Wrap360(ActiveRotationSteps * 60f + ActiveRotationFineDegrees);
            set
            {
                HexMapObjectData.SplitYaw(value, out var steps, out var fine);
                ActiveRotationSteps = steps;
                ActiveRotationFineDegrees = fine;
            }
        }

        public string ActiveTrapId { get; set; }
        public int ActiveTrapRadius { get; set; }
        public HexTrapEffectKind ActiveTrapEffectKind { get; set; } = HexTrapEffectKind.Damage;
        public int ActiveTrapEffectAmount { get; set; } = 1;
        public int ActiveTrapEffectDurationTurns { get; set; } = 1;
        public bool ActiveTrapAffectsPlayer { get; set; } = true;
        public bool ActiveTrapAffectsMonsters { get; set; }
        public bool ActiveTrapOneShot { get; set; } = true;
        public bool ActiveTrapTriggerOnEnter { get; set; } = true;
        public string ActiveTrapPresetId { get; set; }

        public bool Paint(HexCoord coord, out string message, int? heightOverride = null)
        {
            message = null;
            if (!TryResolveActivePreset(out var resolution, out message))
                return false;

            var e = resolution.Entry;
            var cell = new HexSparseMapAuthoringCell(
                coord,
                e.TilePresetId,
                e.TerrainTypeId,
                e.AtlasVisualId,
                heightLevel: heightOverride ?? e.HeightLevel,
                rotationSteps: HexCellData.ClampRotationSteps(ActiveRotationSteps),
                edgeConnectionMask: e.EdgeConnectionMask,
                baseWalkable: e.BaseWalkable);

            Source.SetCell(coord, cell);
            message = $"Painted tile preset '{e.TilePresetId}' at {coord} rot {cell.RotationSteps * 60}°.";
            return true;
        }

        /// <summary>
        /// 반경 <see cref="ActiveDiskBrushRadius"/>의 헥스 디스크를 한 번에 페인트한다. 아레나처럼 넓고
        /// 규칙적인 영역을 셀 단위로 찍는 것은 비현실적이라(반지름 6 = 127칸) 일괄 브러시가 필요하다.
        /// 좌표 열거는 런타임의 <see cref="HexArea.CellsWithin"/>을 그대로 쓴다 — 에디터가 자체 디스크
        /// 산식을 갖게 되면 결계/기믹이 보는 디스크와 저작이 보는 디스크가 갈라진다.
        /// </summary>
        public bool PaintDisk(HexCoord center, out string message, int? heightOverride = null)
        {
            message = null;
            if (!TryResolveActivePreset(out _, out message))
                return false;

            var radius = Math.Max(0, ActiveDiskBrushRadius);
            var painted = 0;
            foreach (var coord in HexArea.CellsWithin(center, radius))
            {
                if (Paint(coord, out _, heightOverride))
                {
                    painted++;
                }
            }

            if (painted == 0)
            {
                message = $"Disk brush painted nothing at {center} (radius {radius}).";
                return false;
            }

            message = $"Disk brush painted {painted} cell(s) within radius {radius} of {center}.";
            return true;
        }

        /// <summary>디스크 브러시로 반경 <see cref="ActiveDiskBrushRadius"/>의 셀을 한 번에 지운다.</summary>
        public bool EraseDisk(HexCoord center, out string message)
        {
            message = null;
            if (Source == null)
            {
                message = "Select a sparse map authoring source before erasing.";
                return false;
            }

            var radius = Math.Max(0, ActiveDiskBrushRadius);
            var erased = HexArea.CellsWithin(center, radius).Count(coord => Source.RemoveCell(coord));
            message = erased > 0
                ? $"Disk brush erased {erased} cell(s) within radius {radius} of {center}."
                : $"No sparse cells found within radius {radius} of {center}.";
            return erased > 0;
        }

        public bool Erase(HexCoord coord, out string message)
        {
            message = null;
            if (Source == null)
            {
                message = "Select a sparse map authoring source before erasing.";
                return false;
            }

            var removed = Source.RemoveCell(coord);
            message = removed ? $"Erased sparse cell at {coord}." : $"No sparse cell exists at {coord}.";
            return removed;
        }

        public bool Pick(HexCoord coord, out string message)
        {
            message = null;
            if (Source == null)
            {
                message = "Select a sparse map authoring source before picking.";
                return false;
            }

            if (!Source.TryGetCell(coord, out var cell))
            {
                message = $"No sparse cell exists at {coord}.";
                return false;
            }

            ActiveTilePresetId = cell.TilePresetId;
            ActiveRotationSteps = cell.RotationSteps;
            message = $"Picked tile preset '{ActiveTilePresetId}' from {coord}.";
            return true;
        }

        public IReadOnlyList<HexSparseMapAuthoringCell> CaptureCompositeCells(IEnumerable<HexCoord> coords, HexCoord anchorCoord)
        {
            if (Source == null || coords == null)
            {
                return Array.Empty<HexSparseMapAuthoringCell>();
            }

            return coords
                .Distinct()
                .OrderBy(coord => coord)
                .Select(coord => Source.TryGetCell(coord, out var cell)
                    ? cell.WithCoord(new HexCoord(coord.Q - anchorCoord.Q, coord.R - anchorCoord.R))
                    : null)
                .Where(cell => cell != null)
                .ToArray();
        }

        public bool StampCompositeCells(HexCoord anchorCoord, IEnumerable<HexSparseMapAuthoringCell> relativeCells, int rotationSteps, out string message)
        {
            message = null;
            if (Source == null)
            {
                message = "Select a sparse map authoring source before stamping composite presets.";
                return false;
            }

            if (relativeCells == null)
            {
                message = "Composite preset has no cells.";
                return false;
            }

            var cells = relativeCells.Where(cell => cell != null).ToArray();
            if (cells.Length == 0)
            {
                message = "Composite preset has no cells.";
                return false;
            }

            var steps = HexCellData.ClampRotationSteps(rotationSteps);
            foreach (var cell in cells)
            {
                var rotated = RotateOffset(cell.Coord, steps);
                var coord = new HexCoord(anchorCoord.Q + rotated.Q, anchorCoord.R + rotated.R);
                Source.SetCell(coord, CloneCell(cell, coord, cell.RotationSteps + steps));
            }

            message = $"Stamped composite preset with {cells.Length} cell(s) at {anchorCoord}.";
            return true;
        }

        public bool MoveCells(IEnumerable<HexCoord> coords, HexCoord delta, out string message)
        {
            message = null;
            if (!TryCollectCells(coords, out var cells, out message))
            {
                return false;
            }

            foreach (var cell in cells)
            {
                Source.RemoveCell(cell.Coord);
            }

            foreach (var cell in cells)
            {
                var coord = new HexCoord(cell.Q + delta.Q, cell.R + delta.R);
                Source.SetCell(coord, cell.WithCoord(coord));
            }

            message = $"Moved {cells.Count} selected tile(s) by {delta}.";
            return true;
        }

        public bool RotateCells(IEnumerable<HexCoord> coords, HexCoord anchorCoord, int rotationSteps, out string message)
        {
            message = null;
            if (!TryCollectCells(coords, out var cells, out message))
            {
                return false;
            }

            var steps = HexCellData.ClampRotationSteps(rotationSteps);
            if (steps == 0)
            {
                message = "Rotation is 0°; no selected tiles changed.";
                return false;
            }

            foreach (var cell in cells)
            {
                Source.RemoveCell(cell.Coord);
            }

            foreach (var cell in cells)
            {
                var offset = new HexCoord(cell.Q - anchorCoord.Q, cell.R - anchorCoord.R);
                var rotated = RotateOffset(offset, steps);
                var coord = new HexCoord(anchorCoord.Q + rotated.Q, anchorCoord.R + rotated.R);
                Source.SetCell(coord, CloneCell(cell, coord, cell.RotationSteps + steps));
            }

            message = $"Rotated {cells.Count} selected tile(s) around {anchorCoord} by {steps * 60}°.";
            return true;
        }

        public bool DeleteCells(IEnumerable<HexCoord> coords, out string message)
        {
            message = null;
            if (Source == null)
            {
                message = "Select a sparse map authoring source before deleting selected tiles.";
                return false;
            }

            if (coords == null)
            {
                message = "No selected tiles to delete.";
                return false;
            }

            var removed = 0;
            foreach (var coord in coords.Distinct().ToArray())
            {
                if (Source.RemoveCell(coord))
                {
                    removed++;
                }
            }

            message = removed > 0 ? $"Deleted {removed} selected tile(s)." : "No selected tiles to delete.";
            return removed > 0;
        }

        public bool PlaceObject(HexCoord coord, out string message)
        {
            if (ActiveObjectType == HexMapObjectType.Trap)
            {
                ActiveTrapId = string.Empty;
                return PlaceTrap(coord, out message);
            }

            message = null;
            if (Source == null)
            {
                message = "Select a sparse map authoring source before placing objects.";
                return false;
            }

            if (!Source.TryGetCell(coord, out _))
            {
                message = $"Paint a sparse cell at {coord} before placing a map object.";
                return false;
            }

            var footprintOffsets = NormalizeFootprintOffsets(ActiveObjectFootprintOffsets);
            var occupiedCoords = ResolveOccupiedCoords(coord, footprintOffsets, ActiveRotationSteps);
            foreach (var occupiedCoord in occupiedCoords)
            {
                if (!Source.TryGetCell(occupiedCoord, out _))
                {
                    message = $"Paint a sparse cell at footprint coordinate {occupiedCoord} before placing this map object.";
                    return false;
                }
            }

            if (ActiveObjectType == HexMapObjectType.MonsterSpawn && string.IsNullOrWhiteSpace(ActiveObjectRef))
            {
                message = "Monster spawn object requires an objectRef monster id.";
                return false;
            }

            if (IsCameraPointType(ActiveObjectType))
            {
                PrepareCameraPointBrushForNextOrder(ActiveObjectType);
            }

            var objectId = string.IsNullOrWhiteSpace(ActiveObjectId)
                ? CreateDefaultObjectId(ActiveObjectType, coord)
                : ActiveObjectId.Trim();
            var objectRef = ActiveObjectType == HexMapObjectType.PlayerSpawn
                ? string.Empty
                : IsCameraPointType(ActiveObjectType) && string.IsNullOrWhiteSpace(ActiveObjectRef)
                    ? DefaultCameraPointObjectRef(ActiveObjectType)
                    : ActiveObjectRef?.Trim();
            var patrolAreaId = ActiveObjectType == HexMapObjectType.MonsterSpawn
                ? ResolvePatrolAreaIdForMonsterSpawn(objectId)
                : string.Empty;
            var spawnerPresetId = ActiveObjectType == HexMapObjectType.MonsterSpawn
                ? ActiveSpawnerPresetId?.Trim() ?? string.Empty
                : string.Empty;
            var cameraSpeedMultiplier = ActiveObjectType == HexMapObjectType.IntroCameraPoint
                ? ActiveObjectCameraSpeedMultiplier
                : 1f;
            var cameraDwellSeconds = ActiveObjectType == HexMapObjectType.IntroCameraPoint
                ? ActiveObjectCameraDwellSeconds
                : 0f;
            var cameraStartsNewSegment = ActiveObjectType == HexMapObjectType.IntroCameraPoint &&
                ActiveObjectCameraStartsNewSegment;
            var replacedSingleton = IsSingletonCameraPointType(ActiveObjectType) &&
                Source.RemoveObjectRefsOfType(ActiveObjectType, out _);
            Source.RemoveObjectRefsAt(coord, ActiveObjectType);
            Source.SetObjectRef(new HexMapObjectRef(
                objectId,
                ActiveObjectType,
                objectRef,
                coord.Q,
                coord.R,
                ActiveObjectRole?.Trim(),
                ActiveObjectPurpose,
                ActiveObjectBlocksMovement,
                ActiveObjectBlocksVision,
                ActiveObjectInteractable,
                patrolAreaId,
                ActiveRotationSteps,
                footprintOffsets,
                ActiveObjectVisualScaleMultiplier,
                spawnerPresetId: spawnerPresetId,
                cameraSpeedMultiplier: cameraSpeedMultiplier,
                cameraDwellSeconds: cameraDwellSeconds,
                cameraStartsNewSegment: cameraStartsNewSegment,
                rotationFineDegrees: ActiveRotationFineDegrees));

            if (ActiveObjectType == HexMapObjectType.MonsterSpawn)
            {
                var radius = ActivePatrolAreaRadius > 0 ? ActivePatrolAreaRadius : GetDefaultPatrolRadiusForMonster(ActiveObjectRef);
                GeneratePatrolAreaAroundCoord(coord, radius, patrolAreaId, assignObjectId: null, message: out var patrolMessage);
                ActivePatrolAreaId = patrolAreaId;
                message = $"Placed {ActiveObjectType} object '{objectId}' at {coord}. {patrolMessage}";
            }
            else
            {
                message = $"Placed {ActiveObjectType} object '{objectId}' at {coord}.";
            }

            if (IsSingletonCameraPointType(ActiveObjectType))
            {
                if (replacedSingleton)
                {
                    message += " Replaced the previously authored point (only one is used).";
                }
            }
            else if (IsCameraPointType(ActiveObjectType))
            {
                PrepareCameraPointBrushForNextOrder(ActiveObjectType);
                message += $" Next camera point order is {ActiveObjectRole}.";
            }

            return true;
        }

        public bool EraseObject(HexCoord coord, out string message)
        {
            message = null;
            if (Source == null)
            {
                message = "Select a sparse map authoring source before erasing objects.";
                return false;
            }

            var removedObjects = Source.RemoveObjectRefsAt(coord);
            var removedTraps = Source.RemoveTrapRefsAt(coord);
            if (removedObjects && removedTraps)
            {
                message = $"Erased map object(s) and trap(s) at {coord}.";
            }
            else if (removedObjects)
            {
                message = $"Erased map object(s) at {coord}.";
            }
            else if (removedTraps)
            {
                message = $"Erased trap(s) at {coord}.";
            }
            else
            {
                message = $"No map object or trap exists at {coord}.";
            }

            return removedObjects || removedTraps;
        }

        public bool PlaceTrap(HexCoord coord, out string message)
        {
            message = null;
            if (Source == null)
            {
                message = "Select a sparse map authoring source before placing traps.";
                return false;
            }

            if (!Source.TryGetCell(coord, out _))
            {
                message = $"Paint a sparse cell at {coord} before placing a trap.";
                return false;
            }

            var trapId = string.IsNullOrWhiteSpace(ActiveTrapId)
                ? CreateDefaultTrapId(coord)
                : ActiveTrapId.Trim();
            var effect = new HexTrapEffectRef(
                ActiveTrapEffectKind,
                Math.Max(0, ActiveTrapEffectAmount),
                RequiresDuration(ActiveTrapEffectKind) ? Math.Max(1, ActiveTrapEffectDurationTurns) : 0);

            // 같은 칸을 다시 칠할 때 브러시에 없는 저작값(randomizationGroup·periodTurns)을 보존한다
            // (placement-randomization-plan §5 선행 배선). 예전에는 지우고 다시 만들며 기본값으로
            // 초기화해 — 함정 태깅이 페인트 한 번에 조용히 유실됐다(몬스터 쪽 보존 배선과 같은 계약).
            var previous = Source.TrapRefs.FirstOrDefault(existing => existing != null && existing.Coord == coord);
            Source.RemoveTrapRefsAt(coord);
            Source.SetTrapRef(new HexTrapRef(
                trapId,
                coord.Q,
                coord.R,
                Math.Max(0, ActiveTrapRadius),
                new[] { effect },
                ActiveTrapAffectsPlayer,
                ActiveTrapAffectsMonsters,
                ActiveTrapOneShot,
                ActiveTrapTriggerOnEnter,
                string.IsNullOrWhiteSpace(ActiveTrapPresetId) ? null : ActiveTrapPresetId.Trim(),
                previous?.PeriodTurns ?? 0,
                previous?.RandomizationGroup ?? string.Empty));
            message = $"Placed trap '{trapId}' at {coord} ({ActiveTrapEffectKind}, radius {Math.Max(0, ActiveTrapRadius)}).";
            return true;
        }

        public bool EraseTrap(HexCoord coord, out string message)
        {
            message = null;
            if (Source == null)
            {
                message = "Select a sparse map authoring source before erasing traps.";
                return false;
            }

            var removed = Source.RemoveTrapRefsAt(coord);
            message = removed ? $"Erased trap(s) at {coord}." : $"No trap exists at {coord}.";
            return removed;
        }

        public bool DeleteTrapRef(string trapId, out string message)
        {
            message = null;
            if (Source == null)
            {
                message = "Select a sparse map authoring source before deleting traps.";
                return false;
            }

            var removed = Source.RemoveTrapRef(trapId);
            message = removed ? $"Deleted trap '{trapId}'." : $"No trap '{trapId}' exists.";
            return removed;
        }

        public bool DeleteObjectRef(string objectId, out string message)
        {
            message = null;
            if (Source == null)
            {
                message = "Select a sparse map authoring source before deleting objects.";
                return false;
            }

            var removed = Source.RemoveObjectRef(objectId);
            message = removed ? $"Deleted map object '{objectId}'." : $"No map object '{objectId}' exists.";
            return removed;
        }

        public int CountPlacedObjectType(HexMapObjectType objectType)
        {
            if (Source == null)
            {
                return 0;
            }

            return objectType == HexMapObjectType.Trap
                ? Source.TrapRefs.Count(trapRef => trapRef != null)
                : Source.ObjectRefs.Count(objectRef => objectRef != null && objectRef.ObjectType == objectType);
        }

        public int CountPlacedObjectDefinition(HexMapObjectType objectType, string objectRef)
        {
            if (Source == null)
            {
                return 0;
            }

            var normalizedRef = objectRef?.Trim() ?? string.Empty;
            return Source.ObjectRefs.Count(candidate =>
                candidate != null &&
                candidate.ObjectType == objectType &&
                string.Equals(candidate.ObjectRef?.Trim() ?? string.Empty, normalizedRef, StringComparison.OrdinalIgnoreCase));
        }

        public bool DeleteObjectType(HexMapObjectType objectType, out string message)
        {
            message = null;
            if (Source == null)
            {
                message = "Select a sparse map authoring source before deleting objects.";
                return false;
            }

            if (objectType == HexMapObjectType.Trap)
            {
                var removed = Source.RemoveAllTrapRefs(out var trapCount);
                message = removed ? $"Deleted {trapCount} Trap object(s)." : "No Trap objects exist.";
                return removed;
            }

            var ok = Source.RemoveObjectRefsOfType(objectType, out var objectCount);
            message = ok ? $"Deleted {objectCount} {objectType} object(s)." : $"No {objectType} objects exist.";
            return ok;
        }

        public static bool IsCameraPointType(HexMapObjectType objectType) =>
            objectType == HexMapObjectType.VictoryCameraPoint ||
            objectType == HexMapObjectType.IntroCameraPoint ||
            objectType == HexMapObjectType.VictoryEndCameraPoint;

        // The victory ENDING framing is a single resting place, not a path: placing a second one would be
        // an authoring mistake, so a new placement replaces the old rather than adding to a chain.
        public static bool IsSingletonCameraPointType(HexMapObjectType objectType) =>
            objectType == HexMapObjectType.VictoryEndCameraPoint;

        public static string DefaultCameraPointObjectRef(HexMapObjectType objectType) =>
            objectType == HexMapObjectType.IntroCameraPoint
                ? "intro-camera-point"
                : objectType == HexMapObjectType.VictoryEndCameraPoint
                    ? "victory-end-camera-point"
                    : "victory-camera-point";

        public int CountCameraPoints(HexMapObjectType objectType) =>
            Source == null
                ? 0
                : Source.ObjectRefs.Count(objectRef => objectRef != null && objectRef.ObjectType == objectType);

        public int VictoryCameraPointCount => CountCameraPoints(HexMapObjectType.VictoryCameraPoint);
        public int IntroCameraPointCount => CountCameraPoints(HexMapObjectType.IntroCameraPoint);

        public string FormatNextVictoryCameraPointRole() =>
            FormatNextCameraPointRole(HexMapObjectType.VictoryCameraPoint);

        public string FormatNextCameraPointRole(HexMapObjectType objectType)
        {
            return FormatCameraPointOrder(ResolveNextCameraPointOrder(objectType));
        }

        public void PrepareVictoryCameraPointBrushForNextOrder() =>
            PrepareCameraPointBrushForNextOrder(HexMapObjectType.VictoryCameraPoint);

        public void PrepareCameraPointBrushForNextOrder(HexMapObjectType objectType)
        {
            if (!IsCameraPointType(objectType) || ActiveObjectType != objectType)
            {
                return;
            }

            ActiveObjectId = string.Empty;
            ActiveObjectRef = DefaultCameraPointObjectRef(objectType);
            ActiveObjectRole = IsSingletonCameraPointType(objectType)
                ? FormatCameraPointOrder(0)
                : FormatNextCameraPointRole(objectType);
            ActiveObjectBlocksMovement = false;
            ActiveObjectBlocksVision = false;
            ActiveObjectInteractable = false;
            ActiveObjectFootprintOffsets = new[] { new HexCoord(0, 0) };
            ActiveObjectVisualScaleMultiplier = UnityEngine.Vector3.one;
        }

        public bool RemoveAllVictoryCameraPoints(out string message) =>
            RemoveAllCameraPoints(HexMapObjectType.VictoryCameraPoint, out message);

        public bool RemoveAllIntroCameraPoints(out string message) =>
            RemoveAllCameraPoints(HexMapObjectType.IntroCameraPoint, out message);

        public bool RemoveAllCameraPoints(HexMapObjectType objectType, out string message)
        {
            message = null;
            if (Source == null)
            {
                message = "Select a sparse map authoring source before removing camera points.";
                return false;
            }

            var objectIds = Source.ObjectRefs
                .Where(objectRef => objectRef != null && objectRef.ObjectType == objectType)
                .Select(objectRef => objectRef.ObjectId)
                .Where(objectId => !string.IsNullOrWhiteSpace(objectId))
                .ToArray();
            foreach (var objectId in objectIds)
            {
                Source.RemoveObjectRef(objectId);
            }

            PrepareCameraPointBrushForNextOrder(objectType);
            message = objectIds.Length > 0
                ? $"Removed {objectIds.Length} {objectType} object(s)."
                : $"No {objectType} objects to remove.";
            return objectIds.Length > 0;
        }

        public bool UpdateObjectRef(
            string originalObjectId,
            string objectId,
            HexMapObjectType objectType,
            string objectRef,
            string role,
            HexMapPurpose enabledForPurpose,
            bool blocksMovement,
            bool blocksVision,
            bool interactable,
            out string message)
        {
            message = null;
            if (Source == null)
            {
                message = "Select a sparse map authoring source before editing objects.";
                return false;
            }

            if (!Source.TryGetObjectRef(originalObjectId, out var existing))
            {
                message = $"No map object '{originalObjectId}' exists.";
                return false;
            }

            var trimmedId = objectId?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedId))
            {
                message = "Map object id is required.";
                return false;
            }

            var trimmedRef = objectRef?.Trim();
            if (objectType == HexMapObjectType.PlayerSpawn)
            {
                trimmedRef = string.Empty;
            }

            if (objectType == HexMapObjectType.MonsterSpawn && string.IsNullOrWhiteSpace(trimmedRef))
            {
                message = "Monster spawn object requires an objectRef monster id.";
                return false;
            }

            if (trimmedId != originalObjectId && Source.ObjectRefs.Any(candidate => candidate != null && candidate.ObjectId == trimmedId))
            {
                message = $"Map object id '{trimmedId}' already exists.";
                return false;
            }

            Source.SetObjectRef(new HexMapObjectRef(
                trimmedId,
                objectType,
                trimmedRef,
                existing.Column,
                existing.Row,
                role?.Trim(),
                enabledForPurpose,
                blocksMovement,
                blocksVision,
                interactable,
                existing.PatrolAreaId,
                existing.RotationSteps,
                existing.FootprintOffsets,
                existing.VisualScaleMultiplier,
                spawnerPresetId: existing.SpawnerPresetId,
                cameraSpeedMultiplier: existing.CameraSpeedMultiplier,
                cameraDwellSeconds: existing.CameraDwellSeconds,
                cameraStartsNewSegment: existing.CameraStartsNewSegment,
                rotationFineDegrees: existing.RotationFineDegrees,
                cameraHeightOverride: existing.CameraHeightOverride,
                cameraYawOffsetDegrees: existing.CameraYawOffsetDegrees,
                cameraYawKeepsFraming: existing.CameraYawKeepsFraming,
                randomizationGroup: existing.RandomizationGroup));
            if (trimmedId != originalObjectId)
            {
                Source.RemoveObjectRef(originalObjectId, removePatrolAreas: false);
            }
            message = $"Updated map object '{trimmedId}' at {existing.Coord}.";
            return true;
        }

        public bool UpdateObjectRefWithPatrolArea(
            string originalObjectId,
            string objectId,
            HexMapObjectType objectType,
            string objectRef,
            string role,
            HexMapPurpose enabledForPurpose,
            bool blocksMovement,
            bool blocksVision,
            bool interactable,
            string patrolAreaId,
            out string message)
        {
            message = null;
            if (Source == null)
            {
                message = "Select a sparse map authoring source before editing objects.";
                return false;
            }

            if (!Source.TryGetObjectRef(originalObjectId, out var existing))
            {
                message = $"No map object '{originalObjectId}' exists.";
                return false;
            }

            var trimmedId = objectId?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedId))
            {
                message = "Map object id is required.";
                return false;
            }

            var trimmedRef = objectRef?.Trim();
            if (objectType == HexMapObjectType.PlayerSpawn)
            {
                trimmedRef = string.Empty;
            }

            if (objectType == HexMapObjectType.MonsterSpawn && string.IsNullOrWhiteSpace(trimmedRef))
            {
                message = "Monster spawn object requires an objectRef monster id.";
                return false;
            }

            if (trimmedId != originalObjectId && Source.ObjectRefs.Any(candidate => candidate != null && candidate.ObjectId == trimmedId))
            {
                message = $"Map object id '{trimmedId}' already exists.";
                return false;
            }

            Source.SetObjectRef(new HexMapObjectRef(
                trimmedId,
                objectType,
                trimmedRef,
                existing.Column,
                existing.Row,
                role?.Trim(),
                enabledForPurpose,
                blocksMovement,
                blocksVision,
                interactable,
                objectType == HexMapObjectType.MonsterSpawn ? patrolAreaId?.Trim() : string.Empty,
                existing.RotationSteps,
                existing.FootprintOffsets,
                existing.VisualScaleMultiplier,
                spawnerPresetId: existing.SpawnerPresetId,
                cameraSpeedMultiplier: existing.CameraSpeedMultiplier,
                cameraDwellSeconds: existing.CameraDwellSeconds,
                cameraStartsNewSegment: existing.CameraStartsNewSegment,
                rotationFineDegrees: existing.RotationFineDegrees,
                cameraHeightOverride: existing.CameraHeightOverride,
                cameraYawOffsetDegrees: existing.CameraYawOffsetDegrees,
                cameraYawKeepsFraming: existing.CameraYawKeepsFraming,
                randomizationGroup: existing.RandomizationGroup));
            if (trimmedId != originalObjectId)
            {
                Source.RemoveObjectRef(originalObjectId, removePatrolAreas: false);
            }
            message = $"Updated map object '{trimmedId}' at {existing.Coord}.";
            return true;
        }


        public bool UpdateObjectRefShape(
            string originalObjectId,
            string objectId,
            HexMapObjectType objectType,
            string objectRef,
            string role,
            HexMapPurpose enabledForPurpose,
            bool blocksMovement,
            bool blocksVision,
            bool interactable,
            string patrolAreaId,
            IEnumerable<HexCoord> footprintOffsets,
            UnityEngine.Vector3 visualScaleMultiplier,
            out string message,
            float cameraSpeedMultiplier = -1f,
            float cameraDwellSeconds = -1f,
            bool? cameraStartsNewSegment = null)
        {
            message = null;
            if (Source == null)
            {
                message = "Select a sparse map authoring source before editing objects.";
                return false;
            }

            if (!Source.TryGetObjectRef(originalObjectId, out var existing))
            {
                message = $"No map object '{originalObjectId}' exists.";
                return false;
            }

            var trimmedId = objectId?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedId))
            {
                message = "Map object id is required.";
                return false;
            }

            var trimmedRef = objectRef?.Trim();
            if (objectType == HexMapObjectType.PlayerSpawn)
            {
                trimmedRef = string.Empty;
            }

            if (objectType == HexMapObjectType.MonsterSpawn && string.IsNullOrWhiteSpace(trimmedRef))
            {
                message = "Monster spawn object requires an objectRef monster id.";
                return false;
            }

            if (trimmedId != originalObjectId && Source.ObjectRefs.Any(candidate => candidate != null && candidate.ObjectId == trimmedId))
            {
                message = $"Map object id '{trimmedId}' already exists.";
                return false;
            }

            var normalizedFootprint = NormalizeFootprintOffsets(footprintOffsets);
            foreach (var occupiedCoord in ResolveOccupiedCoords(existing.Coord, normalizedFootprint, existing.RotationSteps))
            {
                if (!Source.TryGetCell(occupiedCoord, out _))
                {
                    message = $"Object footprint targets missing painted coordinate {occupiedCoord}.";
                    return false;
                }
            }

            var resolvedCameraSpeed = cameraSpeedMultiplier < 0f ? existing.CameraSpeedMultiplier : cameraSpeedMultiplier;
            var resolvedCameraDwell = cameraDwellSeconds < 0f ? existing.CameraDwellSeconds : cameraDwellSeconds;
            var resolvedStartsNewSegment = cameraStartsNewSegment ?? existing.CameraStartsNewSegment;
            Source.SetObjectRef(new HexMapObjectRef(
                trimmedId,
                objectType,
                trimmedRef,
                existing.Column,
                existing.Row,
                role?.Trim(),
                enabledForPurpose,
                blocksMovement,
                blocksVision,
                interactable,
                objectType == HexMapObjectType.MonsterSpawn ? patrolAreaId?.Trim() : string.Empty,
                existing.RotationSteps,
                normalizedFootprint,
                visualScaleMultiplier,
                spawnerPresetId: existing.SpawnerPresetId,
                cameraSpeedMultiplier: resolvedCameraSpeed,
                cameraDwellSeconds: resolvedCameraDwell,
                cameraStartsNewSegment: resolvedStartsNewSegment,
                rotationFineDegrees: existing.RotationFineDegrees,
                cameraHeightOverride: existing.CameraHeightOverride,
                cameraYawOffsetDegrees: existing.CameraYawOffsetDegrees,
                cameraYawKeepsFraming: existing.CameraYawKeepsFraming,
                randomizationGroup: existing.RandomizationGroup));
            if (trimmedId != originalObjectId)
            {
                Source.RemoveObjectRef(originalObjectId, removePatrolAreas: false);
            }
            message = $"Updated map object '{trimmedId}' at {existing.Coord}.";
            return true;
        }

        public bool SetObjectRotation(string objectId, int rotationSteps, out string message)
        {
            return SetObjectRotation(objectId, rotationSteps, 0f, out message);
        }

        public bool SetObjectRotation(string objectId, int rotationSteps, float rotationFineDegrees, out string message)
        {
            message = null;
            if (Source == null)
            {
                message = "Select a sparse map authoring source before rotating objects.";
                return false;
            }

            if (!Source.TryGetObjectRef(objectId, out var existing))
            {
                message = $"No map object '{objectId}' exists.";
                return false;
            }

            var steps = HexCellData.ClampRotationSteps(rotationSteps);
            var rotated = existing.WithRotation(steps, rotationFineDegrees);
            Source.SetObjectRef(rotated);
            message = $"Rotated map object '{existing.ObjectId}' to {rotated.YawDegrees:0.#}°.";
            return true;
        }

        /// <summary>
        /// Moves a placed object to another painted coordinate, keeping every other authored field. The
        /// whole rotated footprint must land on painted cells, same rule placement enforces.
        /// </summary>
        public bool MoveObject(string objectId, HexCoord targetCoord, out string message)
        {
            message = null;
            if (Source == null)
            {
                message = "Select a sparse map authoring source before moving objects.";
                return false;
            }

            if (!Source.TryGetObjectRef(objectId, out var existing))
            {
                message = $"No map object '{objectId}' exists.";
                return false;
            }

            if (existing.Coord == targetCoord)
            {
                message = $"Map object '{objectId}' is already at {targetCoord}.";
                return false;
            }

            foreach (var occupiedCoord in ResolveOccupiedCoords(targetCoord, existing.FootprintOffsets, existing.RotationSteps))
            {
                if (!Source.TryGetCell(occupiedCoord, out _))
                {
                    message = $"Paint a sparse cell at {occupiedCoord} before moving this map object there.";
                    return false;
                }
            }

            Source.SetObjectRef(existing.WithCoord(targetCoord));
            message = $"Moved map object '{objectId}' to {targetCoord}.";
            return true;
        }

        public bool PaintPatrolArea(HexCoord coord, out string message)
        {
            message = null;
            if (Source == null)
            {
                message = "Select a sparse map authoring source before painting patrol areas.";
                return false;
            }

            if (!Source.TryGetCell(coord, out var patrolCell))
            {
                message = $"Paint a sparse cell at {coord} before adding it to a patrol area.";
                return false;
            }

            if (!patrolCell.BaseWalkable)
            {
                message = $"Coordinate {coord} is non-walkable; patrol areas cannot include it.";
                return false;
            }

            if (Source.ObjectRefs.Any(r => r != null && r.BlocksMovement && r.OccupiedCoords.Contains(coord)))
            {
                message = $"Coordinate {coord} has a movement-blocking object; patrol areas cannot include it.";
                return false;
            }

            var patrolAreaId = NormalizePatrolAreaId(ActivePatrolAreaId);
            Source.PaintPatrolAreaCell(patrolAreaId, coord);
            ActivePatrolAreaId = patrolAreaId;
            message = $"Added {coord} to patrol area '{patrolAreaId}'.";
            return true;
        }

        public bool ErasePatrolArea(HexCoord coord, out string message)
        {
            message = null;
            if (Source == null)
            {
                message = "Select a sparse map authoring source before erasing patrol areas.";
                return false;
            }

            var patrolAreaId = NormalizePatrolAreaId(ActivePatrolAreaId);
            var removed = Source.ErasePatrolAreaCell(patrolAreaId, coord);
            message = removed ? $"Removed {coord} from patrol area '{patrolAreaId}'." : $"Patrol area '{patrolAreaId}' does not contain {coord}.";
            return removed;
        }

        public bool PaintArea(HexCoord coord, out string message)
        {
            message = null;
            if (Source == null)
            {
                message = "Select a sparse map authoring source before painting areas.";
                return false;
            }

            if (!Source.TryGetCell(coord, out var areaCell))
            {
                message = $"Paint a sparse cell at {coord} before adding it to an area.";
                return false;
            }

            if (!areaCell.BaseWalkable)
            {
                message = $"Coordinate {coord} is non-walkable; areas cannot include it.";
                return false;
            }

            var areaId = NormalizeAreaId(ActiveAreaId);
            Source.PaintAreaCell(areaId, coord, HexMapAreaRef.BossArenaPurpose, NormalizeBossSpawnRefId(ActiveAreaBossSpawnRefId));
            ActiveAreaId = areaId;
            message = $"Added {coord} to area '{areaId}'.";
            return true;
        }

        public bool EraseArea(HexCoord coord, out string message)
        {
            message = null;
            if (Source == null)
            {
                message = "Select a sparse map authoring source before erasing areas.";
                return false;
            }

            var areaId = NormalizeAreaId(ActiveAreaId);
            var removed = Source.EraseAreaCell(areaId, coord);
            message = removed ? $"Removed {coord} from area '{areaId}'." : $"Area '{areaId}' does not contain {coord}.";
            return removed;
        }

        /// <summary>
        /// 보스 스폰을 중심으로 반경 <paramref name="radius"/>의 아레나를 통째로 만든다. 아레나는 보스 스폰
        /// 좌표를 <b>반드시 포함</b>해야 하므로(그렇지 않으면 맵 빌드가 거부한다) 중심을 스폰으로 잡는
        /// 이 경로가 안전한 기본 저작 수단이다. 걸을 수 없는 셀은 자동으로 빠진다.
        /// </summary>
        public bool GenerateBossArenaAroundObject(string objectId, int radius, out string message)
        {
            message = null;
            if (Source == null)
            {
                message = "Select a sparse map authoring source before generating arenas.";
                return false;
            }

            if (!Source.TryGetObjectRef(objectId, out var objectRef))
            {
                message = $"No map object '{objectId}' exists.";
                return false;
            }

            if (!objectRef.IsMonsterSpawn)
            {
                message = $"Map object '{objectId}' is not a monster spawn.";
                return false;
            }

            var areaId = NormalizeAreaId(ActiveAreaId);
            var clampedRadius = Math.Max(0, radius);
            var coords = HexArea.CellsWithin(objectRef.Coord, clampedRadius)
                .Where(coord => Source.TryGetCell(coord, out var cell) && cell.BaseWalkable)
                .ToList();
            if (!coords.Contains(objectRef.Coord))
            {
                message = $"Boss spawn '{objectId}' sits on a missing or non-walkable cell at {objectRef.Coord}; an arena cannot contain it.";
                return false;
            }

            Source.SetAreaRef(new HexMapAreaAuthoringRef(
                areaId,
                coords,
                HexMapAreaRef.BossArenaPurpose,
                objectRef.ObjectId));
            ActiveAreaId = areaId;
            ActiveAreaBossSpawnRefId = objectRef.ObjectId;
            message = $"Generated boss arena '{areaId}' with {coords.Count} cell(s) bound to spawn '{objectRef.ObjectId}'.";
            return true;
        }

        public bool GeneratePatrolAreaAroundObject(string objectId, int radius, out string message)
        {
            message = null;
            if (Source == null)
            {
                message = "Select a sparse map authoring source before generating patrol areas.";
                return false;
            }

            if (!Source.TryGetObjectRef(objectId, out var objectRef))
            {
                message = $"No map object '{objectId}' exists.";
                return false;
            }

            if (!objectRef.IsMonsterSpawn)
            {
                message = $"Map object '{objectId}' is not a monster spawn.";
                return false;
            }

            var patrolAreaId = string.IsNullOrWhiteSpace(objectRef.PatrolAreaId)
                ? NormalizePatrolAreaId(ActivePatrolAreaId)
                : objectRef.PatrolAreaId.Trim();
            return GeneratePatrolAreaAroundCoord(objectRef.Coord, radius, patrolAreaId, assignObjectId: objectId, message: out message);
        }

        public bool GeneratePatrolAreaAroundCoord(HexCoord center, int radius, string patrolAreaId, string assignObjectId, out string message)
        {
            message = null;
            if (Source == null)
            {
                message = "Select a sparse map authoring source before generating patrol areas.";
                return false;
            }

            if (!Source.TryGetCell(center, out _))
            {
                message = $"Paint a sparse cell at {center} before generating a patrol area.";
                return false;
            }

            var id = NormalizePatrolAreaId(patrolAreaId);
            var clampedRadius = Math.Max(0, radius);
            var coords = Source.Cells
                .Where(cell => cell != null && cell.BaseWalkable && center.DistanceTo(cell.Coord) <= clampedRadius)
                .Select(cell => cell.Coord)
                .Distinct()
                .OrderBy(coord => coord.DistanceTo(center))
                .ThenBy(coord => coord.Q)
                .ThenBy(coord => coord.R)
                .ToList();
            if (coords.Count == 0)
            {
                message = $"No painted cells found within radius {clampedRadius} of {center}.";
                return false;
            }

            Source.SetPatrolAreaRef(new HexMapPatrolAreaRef(id, coords));
            ActivePatrolAreaId = id;

            if (!string.IsNullOrWhiteSpace(assignObjectId))
            {
                AssignPatrolAreaToObject(assignObjectId, id, out _);
            }

            message = $"Generated patrol area '{id}' with {coords.Count} cell(s) around {center}.";
            return true;
        }

        public bool AssignPatrolAreaToObject(string objectId, string patrolAreaId, out string message)
        {
            message = null;
            if (Source == null)
            {
                message = "Select a sparse map authoring source before assigning patrol areas.";
                return false;
            }

            if (!Source.TryGetObjectRef(objectId, out var existing))
            {
                message = $"No map object '{objectId}' exists.";
                return false;
            }

            if (!existing.IsMonsterSpawn)
            {
                message = $"Map object '{objectId}' is not a monster spawn.";
                return false;
            }

            var id = NormalizePatrolAreaId(patrolAreaId);
            Source.SetObjectRef(new HexMapObjectRef(
                existing.ObjectId,
                existing.ObjectType,
                existing.ObjectRef,
                existing.Column,
                existing.Row,
                existing.Role,
                existing.EnabledForPurpose,
                existing.BlocksMovement,
                existing.BlocksVision,
                existing.Interactable,
                id,
                existing.RotationSteps,
                existing.FootprintOffsets,
                existing.VisualScaleMultiplier,
                spawnerPresetId: existing.SpawnerPresetId,
                randomizationGroup: existing.RandomizationGroup));
            message = $"Assigned patrol area '{id}' to monster spawn '{objectId}'.";
            return true;
        }

        public bool Validate(out IReadOnlyList<string> messages)
        {
            var validation = new List<string>();
            if (Source == null)
            {
                validation.Add("Select a sparse map authoring source.");
            }
            else
            {
                if (Source.CellCount == 0)
                {
                    validation.Add("Sparse map has no painted cells.");
                }

                if (Source.HasDuplicateCoordinates(out var duplicates))
                {
                    foreach (var duplicate in duplicates)
                    {
                        validation.Add($"Duplicate sparse coordinate {duplicate}.");
                    }
                }

                ValidateAuthoredPatrolAreaRefs(validation);
                ValidateAuthoredAreaRefs(validation);
                ValidateAuthoredObjectRefs(validation);
                ValidateAuthoredTrapRefs(validation);
            }

            if (TilePresetCatalog == null)
            {
                validation.Add("Select a tile preset catalog.");
            }
            else
            {
                validation.AddRange(TilePresetCatalog.Validate());
                if (Source != null)
                {
                    foreach (var cell in Source.Cells)
                    {
                        if (cell != null && !TilePresetCatalog.TryResolve(cell.TilePresetId, out var resolution))
                        {
                            validation.AddRange(resolution.Errors);
                        }
                    }
                }
            }

            messages = validation;
            return validation.Count == 0;
        }

        private bool TryCollectCells(IEnumerable<HexCoord> coords, out List<HexSparseMapAuthoringCell> cells, out string message)
        {
            cells = null;
            message = null;
            if (Source == null)
            {
                message = "Select a sparse map authoring source before editing selected tiles.";
                return false;
            }

            if (coords == null)
            {
                message = "No selected tiles.";
                return false;
            }

            cells = coords
                .Distinct()
                .OrderBy(coord => coord)
                .Select(coord => Source.TryGetCell(coord, out var cell) ? cell : null)
                .Where(cell => cell != null)
                .ToList();
            if (cells.Count == 0)
            {
                message = "No selected painted tiles.";
                return false;
            }

            return true;
        }

        private static HexSparseMapAuthoringCell CloneCell(HexSparseMapAuthoringCell cell, HexCoord coord, int rotationSteps)
        {
            return new HexSparseMapAuthoringCell(
                coord,
                cell.TilePresetId,
                cell.TerrainTypeId,
                cell.AtlasVisualId,
                cell.EventId,
                cell.LandmarkId,
                cell.HeightLevel,
                HexCellData.ClampRotationSteps(rotationSteps),
                cell.EdgeConnectionMask,
                cell.BaseWalkable);
        }

        public static HexCoord RotateOffset(HexCoord offset, int rotationSteps)
        {
            var steps = HexCellData.ClampRotationSteps(rotationSteps);
            var q = offset.Q;
            var r = offset.R;
            for (var i = 0; i < steps; i++)
            {
                var nextQ = -r;
                var nextR = q + r;
                q = nextQ;
                r = nextR;
            }

            return new HexCoord(q, r);
        }

        private void ValidateAuthoredPatrolAreaRefs(ICollection<string> validation)
        {
            foreach (var patrolAreaRef in Source.PatrolAreaRefs.Where(r => r != null))
            {
                if (string.IsNullOrWhiteSpace(patrolAreaRef.PatrolAreaId))
                {
                    validation.Add("Patrol area is missing an id.");
                    continue;
                }

                if (patrolAreaRef.Cells == null || patrolAreaRef.Cells.Count == 0)
                {
                    validation.Add($"Patrol area '{patrolAreaRef.PatrolAreaId}' has no cells.");
                    continue;
                }

                foreach (var cell in patrolAreaRef.Cells.Where(c => c != null))
                {
                    if (!Source.TryGetCell(cell.Coord, out var existingCell))
                    {
                        validation.Add($"Patrol area '{patrolAreaRef.PatrolAreaId}' targets missing painted coordinate {cell.Coord}.");
                    }
                    else if (!existingCell.BaseWalkable)
                    {
                        validation.Add($"Patrol area '{patrolAreaRef.PatrolAreaId}' targets non-walkable coordinate {cell.Coord}.");
                    }
                    else if (Source.ObjectRefs.Any(r => r != null && r.BlocksMovement && r.OccupiedCoords.Contains(cell.Coord)))
                    {
                        validation.Add($"Patrol area '{patrolAreaRef.PatrolAreaId}' targets movement-blocked coordinate {cell.Coord}.");
                    }
                }
            }
        }

        /// <summary>
        /// 저작 영역 검증. 순찰 영역과 같은 셀 규칙에 더해 보스 아레나의 바인딩(스폰 실재·아레나 포함)을
        /// 확인한다 — 여기서 잡지 못하면 맵 빌드 단계에서 <b>맵 전체 로드 실패</b>로 나타난다.
        /// </summary>
        private void ValidateAuthoredAreaRefs(ICollection<string> validation)
        {
            foreach (var areaRef in Source.AreaRefs.Where(r => r != null))
            {
                if (string.IsNullOrWhiteSpace(areaRef.AreaId))
                {
                    validation.Add("Area is missing an id.");
                    continue;
                }

                if (areaRef.Cells == null || areaRef.Cells.Count == 0)
                {
                    validation.Add($"Area '{areaRef.AreaId}' has no cells.");
                    continue;
                }

                foreach (var cell in areaRef.Cells.Where(c => c != null))
                {
                    if (!Source.TryGetCell(cell.Coord, out var existingCell))
                    {
                        validation.Add($"Area '{areaRef.AreaId}' targets missing painted coordinate {cell.Coord}.");
                    }
                    else if (!existingCell.BaseWalkable)
                    {
                        validation.Add($"Area '{areaRef.AreaId}' targets non-walkable coordinate {cell.Coord}.");
                    }
                }

                if (!areaRef.ToRuntimeRef().IsBossArena)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(areaRef.BossSpawnRefId))
                {
                    validation.Add($"Boss arena '{areaRef.AreaId}' is missing a bound boss spawn ref id.");
                    continue;
                }

                if (!Source.TryGetObjectRef(areaRef.BossSpawnRefId, out var boundSpawn) || !boundSpawn.IsMonsterSpawn)
                {
                    validation.Add($"Boss arena '{areaRef.AreaId}' references missing monster spawn '{areaRef.BossSpawnRefId}'.");
                }
                else if (!areaRef.Cells.Any(cell => cell != null && cell.Coord == boundSpawn.Coord))
                {
                    validation.Add($"Boss arena '{areaRef.AreaId}' does not contain its bound boss spawn '{boundSpawn.ObjectId}' at {boundSpawn.Coord}.");
                }
                else if (!MonsterSpawnRoles.IsBoss(boundSpawn.Role))
                {
                    validation.Add($"Boss arena '{areaRef.AreaId}' is bound to spawn '{boundSpawn.ObjectId}' whose role is '{boundSpawn.Role}', not '{MonsterSpawnRoles.Boss}'.");
                }
            }
        }

        private void ValidateAuthoredObjectRefs(ICollection<string> validation)
        {
            var objectRefs = Source.ObjectRefs.Where(objectRef => objectRef != null).ToArray();
            foreach (var duplicateId in objectRefs
                .Where(objectRef => !string.IsNullOrWhiteSpace(objectRef.ObjectId))
                .GroupBy(objectRef => objectRef.ObjectId)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key))
            {
                validation.Add($"Duplicate map object id '{duplicateId}'.");
            }

            foreach (var duplicateCoord in objectRefs
                .Where(objectRef => objectRef.IsMonsterSpawn)
                .GroupBy(objectRef => objectRef.Coord)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key))
            {
                validation.Add($"Duplicate monster spawn object coordinate {duplicateCoord}.");
            }

            foreach (var objectRef in objectRefs.Where(objectRef => objectRef.IsMonsterSpawn))
            {
                if (objectRef.EnabledForPurpose != HexMapPurpose.Unspecified &&
                    Source.BoardPurpose != HexMapPurpose.Unspecified &&
                    objectRef.EnabledForPurpose != Source.BoardPurpose)
                {
                    validation.Add($"Monster spawn object '{objectRef.ObjectId}' purpose {objectRef.EnabledForPurpose} does not match board purpose {Source.BoardPurpose}.");
                }

                if (!string.IsNullOrWhiteSpace(objectRef.ObjectRef) && !IsKnownMonsterId(objectRef.ObjectRef))
                {
                    validation.Add($"Monster spawn object '{objectRef.ObjectId}' references missing catalog monster '{objectRef.ObjectRef}' in {MonsterCatalogSourceId}.");
                }

                if (!string.IsNullOrWhiteSpace(objectRef.PatrolAreaId) && !Source.TryGetPatrolAreaRef(objectRef.PatrolAreaId, out _))
                {
                    validation.Add($"Monster spawn object '{objectRef.ObjectId}' references missing patrol area '{objectRef.PatrolAreaId}'.");
                }
            }

            foreach (var duplicatePurpose in objectRefs
                .Where(objectRef => objectRef.IsPlayerSpawn)
                .GroupBy(objectRef => objectRef.EnabledForPurpose)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key))
            {
                validation.Add($"Duplicate player spawn object for purpose {duplicatePurpose}.");
            }
        }

        private void ValidateAuthoredTrapRefs(ICollection<string> validation)
        {
            var trapRefs = Source.TrapRefs.Where(trapRef => trapRef != null).ToArray();
            foreach (var duplicateId in trapRefs
                .Where(trapRef => !string.IsNullOrWhiteSpace(trapRef.TrapId))
                .GroupBy(trapRef => trapRef.TrapId)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key))
            {
                validation.Add($"Duplicate trap id '{duplicateId}'.");
            }

            foreach (var trapRef in trapRefs)
            {
                if (string.IsNullOrWhiteSpace(trapRef.TrapId))
                {
                    validation.Add($"Trap at {trapRef.Coord} is missing trap id.");
                }

                if (!Source.TryGetCell(trapRef.Coord, out _))
                {
                    validation.Add($"Trap '{trapRef.TrapId}' targets missing painted coordinate {trapRef.Coord}.");
                }

                if (trapRef.Effects == null || trapRef.Effects.Count == 0)
                {
                    validation.Add($"Trap '{trapRef.TrapId}' has no effects.");
                }

                if (!trapRef.AffectsPlayer && !trapRef.AffectsMonsters)
                {
                    validation.Add($"Trap '{trapRef.TrapId}' affects no targets.");
                }
            }
        }

        public void ApplyObjectTypeDefaults(HexMapObjectType objectType)
        {
            ActiveObjectType = objectType;
            ActiveObjectBlocksMovement = HexMapObjectTypeDefaults.BlocksMovement(objectType);
            ActiveObjectBlocksVision = HexMapObjectTypeDefaults.BlocksVision(objectType);
            ActiveObjectInteractable = HexMapObjectTypeDefaults.Interactable(objectType);
            if (ActiveObjectFootprintOffsets == null || ActiveObjectFootprintOffsets.Count == 0)
            {
                ActiveObjectFootprintOffsets = new[] { new HexCoord(0, 0) };
            }

            if (ActiveObjectVisualScaleMultiplier.x <= 0f || ActiveObjectVisualScaleMultiplier.y <= 0f || ActiveObjectVisualScaleMultiplier.z <= 0f)
            {
                ActiveObjectVisualScaleMultiplier = UnityEngine.Vector3.one;
            }
        }

        public void ApplyObjectDefinition(RuntimeMapObjectPrefabCatalog.Entry definition)
        {
            if (definition == null)
            {
                return;
            }

            ActiveObjectType = definition.ObjectType;
            ActiveObjectRef = definition.ObjectRef;
            ActiveObjectBlocksMovement = definition.BlocksMovement;
            ActiveObjectBlocksVision = definition.BlocksVision;
            ActiveObjectInteractable = definition.Interactable;
            ActiveObjectFootprintOffsets = NormalizeFootprintOffsets(definition.FootprintOffsets);
            ActiveObjectVisualScaleMultiplier = definition.VisualScaleMultiplier;
        }

        public static IReadOnlyList<HexCoord> NormalizeFootprintOffsets(IEnumerable<HexCoord> offsets)
        {
            var normalized = offsets == null
                ? new[] { new HexCoord(0, 0) }
                : offsets.Distinct().OrderBy(coord => coord).ToArray();
            return normalized.Length == 0 ? new[] { new HexCoord(0, 0) } : normalized;
        }

        public static IReadOnlyList<HexCoord> ResolveOccupiedCoords(HexCoord anchor, IEnumerable<HexCoord> footprintOffsets, int rotationSteps)
        {
            return NormalizeFootprintOffsets(footprintOffsets)
                .Select(offset => anchor + offset.RotateSteps(rotationSteps))
                .Distinct()
                .OrderBy(coord => coord)
                .ToArray();
        }

        public bool IsKnownMonsterId(string monsterId)
        {
            return MonsterCatalog != null && MonsterCatalog.TryGetEntry(monsterId, out _);
        }

        private bool TryResolveActivePreset(out HexTilePresetResolution resolution, out string message)
        {
            resolution = default;
            if (Source == null)
            {
                message = "Select a sparse map authoring source before painting.";
                return false;
            }

            if (TilePresetCatalog == null)
            {
                message = "Select a tile preset catalog before painting.";
                return false;
            }

            if (!TilePresetCatalog.TryResolve(ActiveTilePresetId, out resolution))
            {
                message = string.Join("\n", resolution.Errors);
                return false;
            }

            message = null;
            return true;
        }

        private static string NormalizePatrolAreaId(string patrolAreaId)
        {
            return string.IsNullOrWhiteSpace(patrolAreaId) ? "patrol-area-1" : patrolAreaId.Trim();
        }

        private static string NormalizeAreaId(string areaId)
        {
            return string.IsNullOrWhiteSpace(areaId) ? "boss-arena-1" : areaId.Trim();
        }

        private static string NormalizeBossSpawnRefId(string bossSpawnRefId)
        {
            return string.IsNullOrWhiteSpace(bossSpawnRefId) ? string.Empty : bossSpawnRefId.Trim();
        }

        private string ResolvePatrolAreaIdForMonsterSpawn(string objectId)
        {
            return string.IsNullOrWhiteSpace(ActiveObjectPatrolAreaId)
                ? CreateDefaultPatrolAreaId(objectId)
                : ActiveObjectPatrolAreaId.Trim();
        }

        private int ResolveNextCameraPointOrder(HexMapObjectType objectType)
        {
            if (Source == null)
            {
                return 0;
            }

            var maxOrder = -1;
            foreach (var objectRef in Source.ObjectRefs.Where(objectRef => objectRef != null && objectRef.ObjectType == objectType))
            {
                if (TryParseFirstInteger(objectRef.Role, out var roleOrder) ||
                    TryParseFirstInteger(objectRef.ObjectRef, out roleOrder) ||
                    TryParseFirstInteger(objectRef.ObjectId, out roleOrder))
                {
                    maxOrder = Math.Max(maxOrder, roleOrder);
                }
            }

            return maxOrder + 1;
        }

        private static string FormatCameraPointOrder(int order)
        {
            return Math.Max(0, order).ToString("00");
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

        private static string CreateDefaultPatrolAreaId(string objectId)
        {
            return string.IsNullOrWhiteSpace(objectId) ? "patrol-area-1" : $"patrol-{objectId.Trim()}";
        }

        private static int GetDefaultPatrolRadiusForMonster(string monsterId)
        {
            return DefaultMonsterPatrolRadius;
        }

        private static string CreateDefaultObjectId(HexMapObjectType objectType, HexCoord coord)
        {
            var prefix = objectType == HexMapObjectType.MonsterSpawn
                ? "monster-spawn"
                : objectType == HexMapObjectType.PlayerSpawn
                    ? "player-spawn"
                : objectType == HexMapObjectType.VictoryEndCameraPoint
                    ? "victory-end-camera-point"
                    : objectType.ToString().ToLowerInvariant();
            return $"{prefix}-{FormatCoordPart(coord.Q)}-{FormatCoordPart(coord.R)}";
        }

        private static string CreateDefaultTrapId(HexCoord coord)
        {
            return $"trap-{FormatCoordPart(coord.Q)}-{FormatCoordPart(coord.R)}";
        }

        public static bool RequiresDuration(HexTrapEffectKind kind)
        {
            return kind == HexTrapEffectKind.Burn ||
                   kind == HexTrapEffectKind.Poison ||
                   kind == HexTrapEffectKind.Stun ||
                   kind == HexTrapEffectKind.Slow ||
                   kind == HexTrapEffectKind.VisionDown;
        }

        private static string FormatCoordPart(int value)
        {
            return value < 0 ? $"n{-value}" : value.ToString();
        }
    }
}

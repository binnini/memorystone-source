using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Map.Unity
{
    public enum HexMapObjectType
    {
        MonsterSpawn,
        Landmark,
        Building,
        TreasureChest,
        ObjectiveMarker,
        EventTrigger,
        PlayerSpawn,
        Trap,
        MemoryStone,
        VictoryCameraPoint,
        IntroCameraPoint,
        // Where the victory cut-3 camera comes to REST around the memory stone. Appended last so the
        // serialized integers of every existing authored object keep their meaning.
        VictoryEndCameraPoint,
        // 상점(잡화점 — 표시명, 내부 식별자는 Shop 유지): 밟으면 상점 UI가 열리는 1회 소비 오브젝트.
        // Appended last — see above.
        Shop,

        /// <summary>
        /// 저주받은 인형뽑기(T2, 2026-08-06). 유물 1 확정 + 저주 카드 1장 확정의 프리미엄 계약 오브젝트.
        /// 맨 뒤 append(직렬화 int 안정).
        /// </summary>
        CursedGachaMachine,

        /// <summary>
        /// 캠핑카(camper-workshop P2, 2026-08-18): (체력 30% 회복 ∥ 카드 연마) 택1의 1회 방문 소비
        /// 오브젝트. 맨 뒤 append(직렬화 int 안정).
        /// </summary>
        CamperVan,

        /// <summary>
        /// 공작소(camper-workshop P3, 2026-08-18): (카드 제거 ∥ 카드 연마) 택1의 1회 방문 소비
        /// 오브젝트. 맨 뒤 append(직렬화 int 안정).
        /// </summary>
        Workshop,
    }

    [Serializable]
    public sealed class HexMapObjectFootprintCellRef
    {
        [SerializeField] private int columnOffset;
        [SerializeField] private int rowOffset;

        public HexMapObjectFootprintCellRef()
        {
        }

        public HexMapObjectFootprintCellRef(int columnOffset, int rowOffset)
        {
            this.columnOffset = columnOffset;
            this.rowOffset = rowOffset;
        }

        public int ColumnOffset => columnOffset;
        public int RowOffset => rowOffset;
        public HexCoord Offset => new HexCoord(columnOffset, rowOffset);
    }

    public static class HexMapObjectTypeDefaults
    {
        public static bool BlocksMovement(HexMapObjectType objectType)
        {
            switch (objectType)
            {
                case HexMapObjectType.Building:
                    return true;
                default:
                    return false;
            }
        }

        public static bool BlocksVision(HexMapObjectType objectType)
        {
            switch (objectType)
            {
                case HexMapObjectType.Building:
                    return true;
                default:
                    return false;
            }
        }

        public static bool Interactable(HexMapObjectType objectType)
        {
            switch (objectType)
            {
                case HexMapObjectType.TreasureChest:
                case HexMapObjectType.ObjectiveMarker:
                case HexMapObjectType.EventTrigger:
                case HexMapObjectType.MemoryStone:
                case HexMapObjectType.Shop:
                case HexMapObjectType.CamperVan:
                case HexMapObjectType.Workshop:
                    return true;
                default:
                    return false;
            }
        }
    }

    [Serializable]
    public sealed class HexMapObjectRef
    {
        [SerializeField] private string objectId = "object-ref";
        [SerializeField] private HexMapObjectType objectType = HexMapObjectType.MonsterSpawn;
        [SerializeField] private string objectRef = "M001";
        [SerializeField] private int column;
        [SerializeField] private int row;
        [SerializeField] private string role = "primary_pressure";
        [SerializeField] private HexMapPurpose enabledForPurpose = HexMapPurpose.Unspecified;
        [SerializeField] private string patrolAreaId;
        [SerializeField] private int rotationSteps;
        [Tooltip("Free-angle yaw added on top of the 60-degree hex step, in signed degrees. The footprint " +
                 "stays on the hex lattice (it rotates with the step only), so this is a purely visual nudge " +
                 "that lets a prop sit off-axis without the tiles under it moving.")]
        [SerializeField] private float rotationFineDegrees;
        [SerializeField] private bool blocksMovement;
        [SerializeField] private bool blocksVision;
        [SerializeField] private bool interactable;
        [SerializeField] private List<HexMapObjectFootprintCellRef> footprintOffsets = new List<HexMapObjectFootprintCellRef>();
        [SerializeField] private Vector3 visualScaleMultiplier = Vector3.one;
        [SerializeField] private string displayName;
        [SerializeField] private string requiredAction;
        [SerializeField] private int investigateRange = 1;
        [Tooltip("Optional SpawnerPresetCatalog preset id (monster spawns). Authoring provenance: records " +
                 "which spawn preset filled this spawn so the editor can re-apply it. Monster id/role/purpose " +
                 "are still materialized on this ref, so runtime does not depend on the preset catalog.")]
        [SerializeField] private string spawnerPresetId;
        [Tooltip("배치 랜덤화 그룹 태그(placement-randomization-plan §2-3a). 빈 문자열 = 현행 고정 배치. " +
                 "같은 그룹의 몬스터 스폰들은 전투 진입 시 그룹 내 슬롯으로 셔플된다. 몬스터 스폰에서 " +
                 "objectRef가 비어 있으면 「예비 슬롯」— 저작 원본 경로에서는 스폰하지 않고 랜덤화 후보로만 쓴다.")]
        [SerializeField] private string randomizationGroup = "";
        [Tooltip("Intro camera point only: playback speed multiplier for the dolly segment that STARTS at " +
                 "this point (>0, default 1). Values <=0 are treated as 1.")]
        [SerializeField] private float cameraSpeedMultiplier = 1f;
        [Tooltip("Intro camera point only: seconds the camera dwells (holds still) when it ARRIVES at this " +
                 "point (>=0, default 0).")]
        [SerializeField] private float cameraDwellSeconds;
        [Tooltip("Intro camera point only: when true, the camera hard-cuts to this point and begins a NEW " +
                 "dolly segment here (no connecting move from the previous point). Use to break the path into " +
                 "separate zones. The first point is always a segment start regardless of this flag.")]
        [SerializeField] private bool cameraStartsNewSegment;
        [Tooltip("Intro camera point only: camera height (world units above this tile) for the dolly, " +
                 "overriding the global height. 0 or less = use the global height. Authoring two zones at " +
                 "different heights is what keeps consecutive runs over the same street from looking alike.")]
        [SerializeField] private float cameraHeightOverride;
        [Tooltip("Intro camera point only: which side the camera views from, in degrees off the direction of " +
                 "travel. 0 = look where it is going (a forward drone shot); +-90 = view from straight out " +
                 "the side while still flying forward (a lateral tracking shot). Negative is left, positive " +
                 "is right. A segment eases from ITS point's angle to the NEXT point's angle, so authoring " +
                 "different values on consecutive points swings the camera around the subject as it travels; " +
                 "equal values hold one angle.")]
        [SerializeField] private float cameraYawOffsetDegrees;
        [Tooltip("Intro camera point only: how the yaw above is applied. ON (default) swings the CAMERA " +
                 "around the point it is aiming at, so the same patch of street stays framed and only the " +
                 "viewing side changes — the lateral camera offset is derived from the angle, never authored " +
                 "by hand. OFF turns only the AIM and leaves the camera on the path, which points it away " +
                 "from the street as the angle grows. Identical either way at 0 degrees.")]
        [SerializeField] private bool cameraYawKeepsFraming = true;

        public HexMapObjectRef()
        {
        }

        public HexMapObjectRef(
            string objectId,
            HexMapObjectType objectType,
            string objectRef,
            int column,
            int row,
            string role = null,
            HexMapPurpose enabledForPurpose = HexMapPurpose.Unspecified,
            bool blocksMovement = false,
            bool blocksVision = false,
            bool interactable = false,
            string patrolAreaId = "",
            int rotationSteps = 0,
            IEnumerable<HexCoord> footprintOffsets = null,
            Vector3? visualScaleMultiplier = null,
            string displayName = "",
            string requiredAction = "",
            int investigateRange = 1,
            string spawnerPresetId = "",
            float cameraSpeedMultiplier = 1f,
            float cameraDwellSeconds = 0f,
            bool cameraStartsNewSegment = false,
            float rotationFineDegrees = 0f,
            float cameraHeightOverride = 0f,
            float cameraYawOffsetDegrees = 0f,
            bool cameraYawKeepsFraming = true,
            string randomizationGroup = "")
        {
            this.objectId = objectId;
            this.objectType = objectType;
            this.objectRef = objectRef;
            this.column = column;
            this.row = row;
            this.role = role;
            this.enabledForPurpose = enabledForPurpose;
            this.patrolAreaId = patrolAreaId;
            this.rotationSteps = SeoulPlayup.Map.Runtime.HexCellData.ClampRotationSteps(rotationSteps);
            this.blocksMovement = blocksMovement;
            this.blocksVision = blocksVision;
            this.interactable = interactable;
            this.footprintOffsets = NormalizeFootprintOffsets(footprintOffsets);
            this.visualScaleMultiplier = SanitizeScale(visualScaleMultiplier ?? Vector3.one);
            this.displayName = displayName;
            this.requiredAction = requiredAction;
            this.investigateRange = Mathf.Max(1, investigateRange);
            this.spawnerPresetId = spawnerPresetId;
            this.cameraSpeedMultiplier = cameraSpeedMultiplier > 0f ? cameraSpeedMultiplier : 1f;
            this.cameraDwellSeconds = cameraDwellSeconds > 0f ? cameraDwellSeconds : 0f;
            this.cameraStartsNewSegment = cameraStartsNewSegment;
            this.rotationFineDegrees = SeoulPlayup.Map.Runtime.HexMapObjectData.NormalizeSignedDegrees(rotationFineDegrees);
            this.cameraHeightOverride = cameraHeightOverride > 0f ? cameraHeightOverride : 0f;
            this.cameraYawOffsetDegrees = cameraYawOffsetDegrees;
            this.cameraYawKeepsFraming = cameraYawKeepsFraming;
            this.randomizationGroup = randomizationGroup ?? string.Empty;
        }

        /// <summary>
        /// Full-fidelity clone with a new rotation. Editor rotate/move paths used to rebuild this ref by
        /// hand from a subset of its fields, which silently dropped everything they forgot to pass (intro
        /// camera height/yaw among them) — clone instead of rebuilding.
        /// </summary>
        public HexMapObjectRef WithRotation(int nextRotationSteps, float nextRotationFineDegrees)
        {
            return CloneWith(Coord, nextRotationSteps, nextRotationFineDegrees);
        }

        /// <summary>Full-fidelity clone moved to another coordinate; see <see cref="WithRotation"/>.</summary>
        public HexMapObjectRef WithCoord(HexCoord coord)
        {
            return CloneWith(coord, RotationSteps, RotationFineDegrees);
        }

        /// <summary>Full-fidelity clone with a new randomization group tag; see <see cref="WithRotation"/>.</summary>
        public HexMapObjectRef WithRandomizationGroup(string nextRandomizationGroup)
        {
            var clone = CloneWith(Coord, RotationSteps, RotationFineDegrees);
            clone.randomizationGroup = nextRandomizationGroup ?? string.Empty;
            return clone;
        }

        private HexMapObjectRef CloneWith(HexCoord coord, int nextRotationSteps, float nextRotationFineDegrees)
        {
            return new HexMapObjectRef(
                objectId,
                objectType,
                objectRef,
                coord.Q,
                coord.R,
                role,
                enabledForPurpose,
                blocksMovement,
                blocksVision,
                interactable,
                patrolAreaId,
                nextRotationSteps,
                FootprintOffsets,
                visualScaleMultiplier,
                displayName,
                requiredAction,
                investigateRange,
                spawnerPresetId,
                cameraSpeedMultiplier,
                cameraDwellSeconds,
                cameraStartsNewSegment,
                nextRotationFineDegrees,
                cameraHeightOverride,
                cameraYawOffsetDegrees,
                cameraYawKeepsFraming,
                randomizationGroup);
        }

        public string ObjectId => objectId;
        public HexMapObjectType ObjectType => objectType;
        public string ObjectRef => objectRef;
        public int Column => column;
        public int Row => row;
        public string Role => role;
        public HexMapPurpose EnabledForPurpose => enabledForPurpose;
        public string PatrolAreaId => patrolAreaId;
        public int RotationSteps => SeoulPlayup.Map.Runtime.HexCellData.ClampRotationSteps(rotationSteps);
        /// <summary>Visual-only yaw nudge on top of the hex step; the footprint ignores it. Signed degrees.</summary>
        public float RotationFineDegrees => SeoulPlayup.Map.Runtime.HexMapObjectData.NormalizeSignedDegrees(rotationFineDegrees);
        /// <summary>Total visual yaw in [0, 360): hex step plus fine nudge.</summary>
        public float YawDegrees => SeoulPlayup.Map.Runtime.HexMapObjectData.Wrap360(RotationSteps * 60f + RotationFineDegrees);
        public bool BlocksMovement => blocksMovement;
        public bool BlocksVision => blocksVision;
        public bool Interactable => interactable;
        public IReadOnlyList<HexMapObjectFootprintCellRef> FootprintOffsetRefs => footprintOffsets ?? (IReadOnlyList<HexMapObjectFootprintCellRef>)Array.Empty<HexMapObjectFootprintCellRef>();
        public IReadOnlyList<HexCoord> FootprintOffsets => NormalizeFootprintOffsets(FootprintOffsetRefs.Select(cell => cell.Offset)).Select(cell => cell.Offset).ToArray();
        public Vector3 VisualScaleMultiplier => SanitizeScale(visualScaleMultiplier);
        public HexCoord Coord => new HexCoord(column, row);
        public IReadOnlyList<HexCoord> OccupiedCoords => FootprintOffsets
            .Select(offset => Coord + offset.RotateSteps(RotationSteps))
            .Distinct()
            .OrderBy(coord => coord)
            .ToArray();
        public string DisplayName => displayName ?? string.Empty;
        public string RequiredAction => requiredAction ?? string.Empty;
        public int InvestigateRange => Mathf.Max(1, investigateRange);
        public string SpawnerPresetId => spawnerPresetId ?? string.Empty;
        /// <summary>배치 랜덤화 그룹(빈 문자열 = 고정 배치). §2-3a.</summary>
        public string RandomizationGroup => randomizationGroup ?? string.Empty;
        /// <summary>
        /// 예비 슬롯: 그룹 태그는 있으나 점유물(프리팹 id)이 없는 슬롯. 저작 원본 경로(룩뎁·프리뷰·
        /// 랜덤화 off·폴백)에서는 스폰하지 않고, 랜덤화 브리지가 소스에서 직접 읽어 위치 후보로만 쓴다.
        ///
        /// <para>
        /// 몬스터 스폰과 <b>서비스 오브젝트</b>(잡화점·캠핑카) 둘 다에 붙는다. 서비스 예비 슬롯은
        /// 좌표를 나르는 그릇일 뿐이라 <see cref="HexMapObjectType.Shop"/> 하나로 저작한다 —
        /// 어느 종류가 그 자리에 설지는 추첨이 정하므로 슬롯의 타입은 의미가 없다(objectId를
        /// <c>svc-slot-</c>로 두어 그 중립성을 드러낸다).
        /// </para>
        /// </summary>
        public bool IsRandomizationSpareSlot =>
            (IsMonsterSpawn || IsServiceObject) &&
            !string.IsNullOrWhiteSpace(RandomizationGroup) &&
            string.IsNullOrWhiteSpace(objectRef);
        public float CameraSpeedMultiplier => cameraSpeedMultiplier > 0f ? cameraSpeedMultiplier : 1f;
        public float CameraDwellSeconds => cameraDwellSeconds > 0f ? cameraDwellSeconds : 0f;
        public bool CameraStartsNewSegment => cameraStartsNewSegment;
        public float CameraHeightOverride => cameraHeightOverride > 0f ? cameraHeightOverride : 0f;
        public float CameraYawOffsetDegrees => cameraYawOffsetDegrees;
        public bool CameraYawKeepsFraming => cameraYawKeepsFraming;

        public bool IsMonsterSpawn => objectType == HexMapObjectType.MonsterSpawn;
        public bool IsPlayerSpawn => objectType == HexMapObjectType.PlayerSpawn;
        public bool IsObjectiveMarker => objectType == HexMapObjectType.ObjectiveMarker;
        public bool IsVictoryCameraPoint => objectType == HexMapObjectType.VictoryCameraPoint;
        public bool IsIntroCameraPoint => objectType == HexMapObjectType.IntroCameraPoint;
        public bool IsVictoryEndCameraPoint => objectType == HexMapObjectType.VictoryEndCameraPoint;
        // ⚠️ 런타임에도 같은 이름의 문자열 기반 판정이 따로 있다(HexMapObjectData.IsRuntimeVisualObject).
        // 여기만 고치면 "에디터엔 보이는데 게임엔 안 나오는" 반쪽이 된다 — 둘을 함께 고칠 것.
        public bool IsRuntimeVisualObject =>
            objectType == HexMapObjectType.Landmark ||
            objectType == HexMapObjectType.Building ||
            objectType == HexMapObjectType.TreasureChest ||
            objectType == HexMapObjectType.MemoryStone ||
            objectType == HexMapObjectType.Shop ||
            objectType == HexMapObjectType.CursedGachaMachine ||
            objectType == HexMapObjectType.CamperVan ||
            objectType == HexMapObjectType.Workshop;

        /// <summary>
        /// 1회 소비 서비스 오브젝트 3종. ⚠️ 런타임에도 같은 판정이 따로 있다
        /// (<c>HexMapPlacementRandomization.IsServiceObject</c>) — 종류를 늘리면 둘을 함께 고칠 것.
        /// </summary>
        public bool IsServiceObject =>
            objectType == HexMapObjectType.Shop ||
            objectType == HexMapObjectType.CamperVan ||
            objectType == HexMapObjectType.Workshop;

        public HexMonsterSpawnRef ToMonsterSpawnRef()
        {
            return new HexMonsterSpawnRef(objectId, objectRef, Coord, role, enabledForPurpose, patrolAreaId);
        }

        public HexMapObjectData ToRuntimeObjectData()
        {
            return new HexMapObjectData(
                objectId,
                objectType.ToString(),
                objectRef,
                Coord,
                role,
                enabledForPurpose,
                blocksMovement,
                blocksVision,
                interactable,
                RotationSteps,
                FootprintOffsets,
                VisualScaleMultiplier.x,
                VisualScaleMultiplier.y,
                VisualScaleMultiplier.z,
                CameraSpeedMultiplier,
                CameraDwellSeconds,
                CameraStartsNewSegment,
                CameraHeightOverride,
                CameraYawOffsetDegrees,
                CameraYawKeepsFraming,
                RotationFineDegrees);
        }

        public HexObjectiveBinding ToObjectiveBinding()
        {
            var resolvedDisplayName = string.IsNullOrWhiteSpace(displayName)
                ? string.IsNullOrWhiteSpace(role) ? objectRef : role
                : displayName;
            var resolvedRequiredAction = string.IsNullOrWhiteSpace(requiredAction) ? "Investigate" : requiredAction;
            return new HexObjectiveBinding(objectId, objectRef, resolvedDisplayName, resolvedRequiredAction, InvestigateRange);
        }

        private static List<HexMapObjectFootprintCellRef> NormalizeFootprintOffsets(IEnumerable<HexCoord> offsets)
        {
            var normalized = offsets == null
                ? new[] { new HexCoord(0, 0) }
                : offsets.Distinct().OrderBy(coord => coord).ToArray();
            if (normalized.Length == 0)
            {
                normalized = new[] { new HexCoord(0, 0) };
            }

            return normalized.Select(coord => new HexMapObjectFootprintCellRef(coord.Q, coord.R)).ToList();
        }

        private static Vector3 SanitizeScale(Vector3 value)
        {
            return new Vector3(
                value.x > 0f ? value.x : 1f,
                value.y > 0f ? value.y : 1f,
                value.z > 0f ? value.z : 1f);
        }
    }

    [Serializable]
    public sealed class HexMapPatrolAreaCellRef
    {
        [SerializeField] private int column;
        [SerializeField] private int row;

        public HexMapPatrolAreaCellRef()
        {
        }

        public HexMapPatrolAreaCellRef(int column, int row)
        {
            this.column = column;
            this.row = row;
        }

        public int Column => column;
        public int Row => row;
        public HexCoord Coord => new HexCoord(column, row);
    }

    [Serializable]
    public sealed class HexMapPatrolAreaRef
    {
        [SerializeField] private string patrolAreaId = "patrol-area";
        [SerializeField] private List<HexMapPatrolAreaCellRef> cells = new List<HexMapPatrolAreaCellRef>();

        public HexMapPatrolAreaRef()
        {
        }

        public HexMapPatrolAreaRef(string patrolAreaId, IEnumerable<HexCoord> coords)
        {
            this.patrolAreaId = patrolAreaId;
            cells = coords == null
                ? new List<HexMapPatrolAreaCellRef>()
                : coords.Select(coord => new HexMapPatrolAreaCellRef(coord.Q, coord.R)).ToList();
        }

        public string PatrolAreaId => patrolAreaId;
        public IReadOnlyList<HexMapPatrolAreaCellRef> Cells => cells ?? (IReadOnlyList<HexMapPatrolAreaCellRef>)Array.Empty<HexMapPatrolAreaCellRef>();

        public HexPatrolAreaRef ToRuntimeRef()
        {
            return new HexPatrolAreaRef(patrolAreaId, Cells.Select(cell => cell.Coord));
        }
    }

    /// <summary>
    /// 목적 영역의 저작 표면(<see cref="HexMapAreaRef"/>의 직렬화 대응물). 셀 저장 형식은
    /// 순찰 영역과 같은 <see cref="HexMapPatrolAreaCellRef"/>를 재사용한다 — 좌표 쌍일 뿐이라
    /// 두 번째 사본을 만들 이유가 없다.
    /// </summary>
    [Serializable]
    public sealed class HexMapAreaAuthoringRef
    {
        [SerializeField] private string areaId = "area";
        [SerializeField] private string purpose = HexMapAreaRef.BossArenaPurpose;
        [SerializeField] private string bossSpawnRefId = string.Empty;
        [SerializeField] private List<HexMapPatrolAreaCellRef> cells = new List<HexMapPatrolAreaCellRef>();

        public HexMapAreaAuthoringRef()
        {
        }

        public HexMapAreaAuthoringRef(string areaId, IEnumerable<HexCoord> coords, string purpose = null, string bossSpawnRefId = null)
        {
            this.areaId = areaId;
            this.purpose = purpose ?? HexMapAreaRef.BossArenaPurpose;
            this.bossSpawnRefId = bossSpawnRefId ?? string.Empty;
            cells = coords == null
                ? new List<HexMapPatrolAreaCellRef>()
                : coords.Select(coord => new HexMapPatrolAreaCellRef(coord.Q, coord.R)).ToList();
        }

        public string AreaId => areaId;
        public string Purpose => purpose ?? string.Empty;
        public string BossSpawnRefId => bossSpawnRefId ?? string.Empty;
        public IReadOnlyList<HexMapPatrolAreaCellRef> Cells => cells ?? (IReadOnlyList<HexMapPatrolAreaCellRef>)Array.Empty<HexMapPatrolAreaCellRef>();

        public HexMapAreaRef ToRuntimeRef()
        {
            return new HexMapAreaRef(areaId, Cells.Select(cell => cell.Coord), Purpose, BossSpawnRefId);
        }
    }
}

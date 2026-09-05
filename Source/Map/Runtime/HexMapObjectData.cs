using System;
using System.Collections.Generic;
using System.Linq;

namespace SeoulPlayup.Map.Runtime
{
    [Serializable]
    public readonly struct HexMapObjectData
    {
        public HexMapObjectData(
            string objectId,
            string objectType,
            string objectRef,
            HexCoord coord,
            string role = null,
            HexMapPurpose enabledForPurpose = HexMapPurpose.Unspecified,
            bool blocksMovement = false,
            bool blocksVision = false,
            bool interactable = false,
            int rotationSteps = 0,
            IEnumerable<HexCoord> footprintOffsets = null,
            float visualScaleX = 1f,
            float visualScaleY = 1f,
            float visualScaleZ = 1f,
            float cameraSpeedMultiplier = 1f,
            float cameraDwellSeconds = 0f,
            bool cameraStartsNewSegment = false,
            float cameraHeightOverride = 0f,
            float cameraYawOffsetDegrees = 0f,
            bool cameraYawKeepsFraming = true,
            float rotationFineDegrees = 0f)
        {
            ObjectId = objectId ?? string.Empty;
            ObjectType = objectType ?? string.Empty;
            ObjectRef = objectRef ?? string.Empty;
            Coord = coord;
            Role = role ?? string.Empty;
            EnabledForPurpose = enabledForPurpose;
            BlocksMovement = blocksMovement;
            BlocksVision = blocksVision;
            Interactable = interactable;
            RotationSteps = HexCellData.ClampRotationSteps(rotationSteps);
            FootprintOffsets = NormalizeFootprintOffsets(footprintOffsets);
            VisualScaleX = ClampPositiveScale(visualScaleX);
            VisualScaleY = ClampPositiveScale(visualScaleY);
            VisualScaleZ = ClampPositiveScale(visualScaleZ);
            CameraSpeedMultiplier = cameraSpeedMultiplier > 0f ? cameraSpeedMultiplier : 1f;
            CameraDwellSeconds = cameraDwellSeconds > 0f ? cameraDwellSeconds : 0f;
            CameraStartsNewSegment = cameraStartsNewSegment;
            CameraHeightOverride = cameraHeightOverride > 0f ? cameraHeightOverride : 0f;
            CameraYawOffsetDegrees = cameraYawOffsetDegrees;
            CameraYawKeepsFraming = cameraYawKeepsFraming;
            RotationFineDegrees = NormalizeSignedDegrees(rotationFineDegrees);
        }

        public string ObjectId { get; }
        public string ObjectType { get; }
        public string ObjectRef { get; }
        public HexCoord Coord { get; }
        public string Role { get; }
        public HexMapPurpose EnabledForPurpose { get; }
        public bool BlocksMovement { get; }
        /// <summary>⚠️ 시야 계산에 쓰이지 않는다(DEC-2026-07-28-03: 차폐 미구현 확정). 저작해도 게임 동작은 바뀌지 않으며, 값은 이력 보존을 위해 남겨 둔다. 계약은 10-specs/systems/fog-of-war.md FW-3.</summary>
        public bool BlocksVision { get; }
        public bool Interactable { get; }
        public int RotationSteps { get; }
        public IReadOnlyList<HexCoord> FootprintOffsets { get; }
        public float VisualScaleX { get; }
        public float VisualScaleY { get; }
        public float VisualScaleZ { get; }
        public float CameraSpeedMultiplier { get; }
        public float CameraDwellSeconds { get; }
        public bool CameraStartsNewSegment { get; }
        /// <summary>Per-point dolly camera height above the tile; 0 means "use the global height".</summary>
        public float CameraHeightOverride { get; }
        /// <summary>
        /// Degrees the dolly's aim is turned off the direction of travel for the segment starting here.
        /// 0 = look ahead (forward drone shot), ±90 = look out the side (lateral tracking shot).
        /// </summary>
        public float CameraYawOffsetDegrees { get; }
        /// <summary>
        /// True: the yaw swings the camera around its aim point, so the framed ground stays put and the
        /// lateral camera offset is derived from the angle. False: only the aim turns. Equal at yaw 0.
        /// </summary>
        public bool CameraYawKeepsFraming { get; }

        /// <summary>
        /// Free-angle yaw added on top of the hex-snapped <see cref="RotationSteps"/>, signed degrees.
        /// Footprint occupancy stays on the 60° hex lattice — only the visual yaw is free — so a designer can
        /// nudge a building off-axis without the tiles it covers shifting underneath it.
        /// </summary>
        public float RotationFineDegrees { get; }

        /// <summary>Total visual yaw in [0, 360): the hex step plus the free-angle nudge.</summary>
        public float YawDegrees => Wrap360(RotationSteps * 60f + RotationFineDegrees);

        /// <summary>
        /// 좌표만 바꾼 전(全)필드 사본. P2 상자 셔플(placement-randomization-plan §5)이 쓴다 —
        /// objectId를 유지해야 <c>ClaimedEventObjectIds</c>(objectId 키) 세이브 정합이 성립한다.
        /// 필드를 골라 다시 조립하면 빠뜨린 필드가 조용히 초기화되므로 반드시 이 사본을 쓸 것
        /// (HexMapObjectRef.WithCoord와 같은 계약).
        /// </summary>
        public HexMapObjectData WithCoord(HexCoord coord)
        {
            return new HexMapObjectData(
                ObjectId,
                ObjectType,
                ObjectRef,
                coord,
                Role,
                EnabledForPurpose,
                BlocksMovement,
                BlocksVision,
                Interactable,
                RotationSteps,
                FootprintOffsets,
                VisualScaleX,
                VisualScaleY,
                VisualScaleZ,
                CameraSpeedMultiplier,
                CameraDwellSeconds,
                CameraStartsNewSegment,
                CameraHeightOverride,
                CameraYawOffsetDegrees,
                CameraYawKeepsFraming,
                RotationFineDegrees);
        }

        /// <summary>Splits a free 0–360° yaw into the nearest hex step plus the leftover fine angle.</summary>
        public static void SplitYaw(float yawDegrees, out int rotationSteps, out float rotationFineDegrees)
        {
            var wrapped = Wrap360(yawDegrees);
            rotationSteps = (int)Math.Round(wrapped / 60f) % 6;
            rotationFineDegrees = NormalizeSignedDegrees(wrapped - rotationSteps * 60f);
        }

        public static float Wrap360(float degrees)
        {
            var wrapped = degrees % 360f;
            return wrapped < 0f ? wrapped + 360f : wrapped;
        }

        // Signed (-180, 180] so the stored nudge reads the way a designer thinks about it ("8° left"),
        // and so a value that drifts past a whole turn folds back instead of accumulating.
        public static float NormalizeSignedDegrees(float value)
        {
            var wrapped = Wrap360(value);
            return wrapped > 180f ? wrapped - 360f : wrapped;
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(ObjectId) &&
                                    (!string.IsNullOrWhiteSpace(ObjectRef) || IsPlayerSpawn || IsVictoryCameraPoint ||
                                     IsIntroCameraPoint || IsVictoryEndCameraPoint);
        public bool IsPlayerSpawn => string.Equals(ObjectType, "PlayerSpawn", StringComparison.Ordinal);
        public bool IsVictoryCameraPoint => string.Equals(ObjectType, "VictoryCameraPoint", StringComparison.Ordinal);
        public bool IsIntroCameraPoint => string.Equals(ObjectType, "IntroCameraPoint", StringComparison.Ordinal);
        public bool IsVictoryEndCameraPoint => string.Equals(ObjectType, "VictoryEndCameraPoint", StringComparison.Ordinal);
        public bool IsBuilding =>
            string.Equals(ObjectType, "Building", StringComparison.Ordinal) ||
            string.Equals(ObjectType, "Prop", StringComparison.Ordinal);
        public bool IsMemoryStone =>
            string.Equals(ObjectType, "MemoryStone", StringComparison.Ordinal);
        public bool IsShop =>
            string.Equals(ObjectType, "Shop", StringComparison.Ordinal);
        public bool IsCamperVan =>
            string.Equals(ObjectType, "CamperVan", StringComparison.Ordinal);
        public bool IsWorkshop =>
            string.Equals(ObjectType, "Workshop", StringComparison.Ordinal);
        public bool IsTreasureChest =>
            string.Equals(ObjectType, "TreasureChest", StringComparison.Ordinal);
        public bool IsCursedGachaMachine =>
            string.Equals(ObjectType, "CursedGachaMachine", StringComparison.Ordinal);
        // ⚠️ 저작 쪽에도 같은 이름의 enum 기반 판정이 따로 있다(HexMapObjectRef.IsRuntimeVisualObject).
        // 여기만 고치면 비주얼이 스폰되지 않는 반쪽이 된다 — 둘을 함께 고칠 것.
        public bool IsRuntimeVisualObject =>
            string.Equals(ObjectType, "Landmark", StringComparison.Ordinal) ||
            IsBuilding ||
            IsTreasureChest ||
            IsMemoryStone ||
            IsShop ||
            IsCursedGachaMachine ||
            IsCamperVan ||
            IsWorkshop;

        /// <summary>
        /// 아직 밟아 보지 않은 칸(암시야 · <see cref="HexCellVisibility.Unknown"/>)에서도 그려지는가.
        ///
        /// <para>
        /// 건물·기억석은 랜드마크라 처음부터 보였고, 서비스 오브젝트(잡화점·캠핑카)가 2026-09-01
        /// 사용자 확정으로 합류했다 — footprint가 1칸으로 줄어 우연히 밟을 확률이 떨어진 만큼,
        /// 멀리서 보고 「일부러 찾아가는」 대상이 되어야 동선 측면 배치가 성립한다.
        /// </para>
        ///
        /// <para>
        /// 🔴 이건 <b>보이느냐</b>만 정한다. <b>또렷하냐</b>는 <see cref="IgnoresVisibilityDarkening"/>가
        /// 따로 정한다 — 두 축은 여전히 별개다(하나로 합치면 「보이지만 어두운」 저작이 불가능해진다).
        /// </para>
        /// </summary>
        public bool IsVisibleInUnexploredFog =>
            IsBuilding || IsMemoryStone || IsShop || IsCamperVan;

        /// <summary>
        /// 시야 밖에서 어둡게 깔리는 것을 면제받는가.
        ///
        /// <para>⚠️ <b>2026-09-01 「서비스는 보이되 어둡게」 확정은 2026-09-05 사용자 판정으로 뒤집혔다</b>
        /// — 실플레이에서 어두운 잡화점·캠핑카가 「찾아갈 것」으로 안 읽혔다. 이제 서비스도 기억석과
        /// 같이 또렷하다. 종전 주석이 「함께 넓히지 말 것」이라 경고했던 자리이므로, 되돌린 것이
        /// 실수가 아니라 <b>판정 결과</b>임을 여기 남긴다.</para>
        ///
        /// <para>🔑 그래도 두 축은 합치지 않는다: 상자·인형뽑기는 여전히 「보이면 어둡게」다
        /// (밟아서 만나는 물건이라 멀리서 또렷하면 탐색의 값이 사라진다).</para>
        /// </summary>
        public bool IgnoresVisibilityDarkening => IsMemoryStone || IsShop || IsCamperVan;

        public IReadOnlyList<HexCoord> OccupiedCoords
        {
            get
            {
                var coord = Coord;
                var rotationSteps = RotationSteps;
                return FootprintOffsets
                    .Select(offset => coord + offset.RotateSteps(rotationSteps))
                    .Distinct()
                    .OrderBy(value => value)
                    .ToArray();
            }
        }

        private static IReadOnlyList<HexCoord> NormalizeFootprintOffsets(IEnumerable<HexCoord> offsets)
        {
            var normalized = offsets == null
                ? new[] { new HexCoord(0, 0) }
                : offsets.Distinct().OrderBy(coord => coord).ToArray();
            return normalized.Length == 0 ? new[] { new HexCoord(0, 0) } : normalized;
        }

        private static float ClampPositiveScale(float value)
        {
            return value > 0f ? value : 1f;
        }
    }
}

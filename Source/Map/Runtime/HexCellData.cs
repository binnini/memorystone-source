using System;
using System.Collections.Generic;
using System.Linq;

namespace SeoulPlayup.Map.Runtime
{
    [Serializable]
    public readonly struct HexCellData
    {
        public const int MinHeightLevel = -1;
        public const int MaxHeightLevel = 3;

        public HexCellData(
            HexCoord coord,
            string tileDefinitionId,
            string terrainTypeId,
            int baseMoveCost,
            bool baseWalkable,
            bool baseBlocksVision,
            string eventId = null,
            string landmarkId = null,
            int visualFloor = 0,
            string atlasVisualId = null,
            int heightLevel = 0,
            int rotationSteps = 0,
            int edgeConnectionMask = 0,
            IEnumerable<string> installableObjectIds = null)
        {
            Coord = coord;
            TileDefinitionId = tileDefinitionId ?? string.Empty;
            TerrainTypeId = terrainTypeId ?? string.Empty;
            BaseMoveCost = Math.Max(1, baseMoveCost);
            BaseWalkable = baseWalkable;
            BaseBlocksVision = baseBlocksVision;
            EventId = eventId ?? string.Empty;
            LandmarkId = landmarkId ?? string.Empty;
            VisualFloor = ClampHeight(visualFloor);
            AtlasVisualId = atlasVisualId ?? string.Empty;
            HeightLevel = ClampHeight(heightLevel);
            RotationSteps = ClampRotationSteps(rotationSteps);
            EdgeConnectionMask = ClampEdgeConnectionMask(edgeConnectionMask);
            InstallableObjectIds = installableObjectIds == null
                ? Array.Empty<string>()
                : installableObjectIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).ToArray();
        }

        public HexCoord Coord { get; }
        public string TileDefinitionId { get; }
        public string TerrainTypeId { get; }
        public int BaseMoveCost { get; }
        public bool BaseWalkable { get; }
        /// <summary>⚠️ 시야 계산에 쓰이지 않는다(DEC-2026-07-28-03: 차폐 미구현 확정). 저작해도 게임 동작은 바뀌지 않으며, 값은 이력 보존을 위해 남겨 둔다. 계약은 10-specs/systems/fog-of-war.md FW-3.</summary>
        public bool BaseBlocksVision { get; }
        public string EventId { get; }
        public string LandmarkId { get; }
        public int VisualFloor { get; }
        public string AtlasVisualId { get; }
        public int HeightLevel { get; }
        public int RotationSteps { get; }
        public int EdgeConnectionMask { get; }
        public IReadOnlyList<string> InstallableObjectIds { get; }

        public HexCellData WithObjectOverlay(bool walkable, bool blocksVision, string eventId, string landmarkId)
        {
            return new HexCellData(
                Coord,
                TileDefinitionId,
                TerrainTypeId,
                BaseMoveCost,
                walkable,
                blocksVision,
                string.IsNullOrEmpty(eventId) ? EventId : eventId,
                string.IsNullOrEmpty(landmarkId) ? LandmarkId : landmarkId,
                VisualFloor,
                AtlasVisualId,
                HeightLevel,
                RotationSteps,
                EdgeConnectionMask,
                InstallableObjectIds);
        }

        public static int ClampHeight(int value) => Math.Max(MinHeightLevel, Math.Min(MaxHeightLevel, value));

        public static int ClampRotationSteps(int value) => Math.Max(0, Math.Min(5, value));

        public static int ClampEdgeConnectionMask(int value) => Math.Max(0, Math.Min(63, value));
    }
}

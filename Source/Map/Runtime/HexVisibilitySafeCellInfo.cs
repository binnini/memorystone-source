namespace SeoulPlayup.Map.Runtime
{
    public readonly struct HexVisibilitySafeCellInfo
    {
        public HexVisibilitySafeCellInfo(
            HexCoord coord,
            HexCellVisibility visibility,
            bool exists,
            bool exposesTerrain,
            bool exposesFullDetails,
            string tileDefinitionId,
            string terrainTypeId,
            int baseMoveCost,
            bool baseWalkable,
            bool baseBlocksVision,
            string eventId,
            string landmarkId,
            int visualFloor = 0,
            bool trapRevealed = false)
        {
            Coord = coord;
            Visibility = visibility;
            Exists = exists;
            ExposesTerrain = exposesTerrain;
            ExposesFullDetails = exposesFullDetails;
            TileDefinitionId = tileDefinitionId ?? string.Empty;
            TerrainTypeId = terrainTypeId ?? string.Empty;
            BaseMoveCost = baseMoveCost;
            BaseWalkable = baseWalkable;
            BaseBlocksVision = baseBlocksVision;
            EventId = eventId ?? string.Empty;
            LandmarkId = landmarkId ?? string.Empty;
            VisualFloor = HexCellData.ClampHeight(visualFloor);
            TrapRevealed = trapRevealed;
        }

        public HexCoord Coord { get; }
        public HexCellVisibility Visibility { get; }
        public bool Exists { get; }
        public bool ExposesTerrain { get; }
        public bool ExposesFullDetails { get; }
        public string TileDefinitionId { get; }
        public string TerrainTypeId { get; }
        public int BaseMoveCost { get; }
        public bool BaseWalkable { get; }
        // ⚠️ 안전 정보에 실려 나가지만 **읽는 소비자가 없다**. 시야 계산도 이 값을 보지 않는다
        // (DEC-2026-07-28-03: 차폐 미구현 확정). 계약은 10-specs/systems/fog-of-war.md FW-3.
        public bool BaseBlocksVision { get; }
        public string EventId { get; }
        public string LandmarkId { get; }
        public int VisualFloor { get; }

        // True when this cell's trap has been discovered by a Scout card. Orthogonal to Visibility:
        // a Hinted cell can still expose its trap. Renderers use this to draw the trap marker.
        public bool TrapRevealed { get; }

        public static HexVisibilitySafeCellInfo Missing(HexCoord coord)
        {
            return new HexVisibilitySafeCellInfo(coord, HexCellVisibility.Unknown, false, false, false, string.Empty, string.Empty, 0, false, false, string.Empty, string.Empty);
        }

        public static HexVisibilitySafeCellInfo Unknown(HexCoord coord)
        {
            return new HexVisibilitySafeCellInfo(coord, HexCellVisibility.Unknown, true, false, false, string.Empty, string.Empty, 0, false, false, string.Empty, string.Empty);
        }
    }
}

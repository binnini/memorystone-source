using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Unity
{
    public sealed class CombatVisibilityPresenter
    {
        public HexVisibilitySafeCellInfo GetSafeCellInfo(
            CombatState state,
            HexMapData loadedMap,
            bool revealAllMapCellsInDebugMode,
            HexCoord coord)
        {
            if (revealAllMapCellsInDebugMode && loadedMap != null)
            {
                return CreateFullyRevealedCellInfo(loadedMap, coord);
            }

            return state == null ? HexVisibilitySafeCellInfo.Missing(coord) : state.GetVisibilitySafeCellInfo(coord);
        }

        public string GetTooltipText(HexVisibilitySafeCellInfo info)
        {
            if (!info.Exists)
            {
                return $"Cell {info.Coord}: outside the known map.";
            }

            if (info.Visibility == HexCellVisibility.Unknown)
            {
                return $"Cell {info.Coord}: Unknown. Details are hidden until scouted or revealed.";
            }

            if (info.Visibility == HexCellVisibility.Hinted)
            {
                var terrain = string.IsNullOrEmpty(info.TerrainTypeId) ? "terrain hinted" : info.TerrainTypeId;
                var walkable = info.BaseWalkable ? "walkable" : "blocked";
                return $"Cell {info.Coord}: Hinted {terrain}, {walkable}. Reveal before investigating.";
            }

            var revealedTerrain = string.IsNullOrEmpty(info.TerrainTypeId) ? "terrain unknown" : info.TerrainTypeId;
            var landmark = string.IsNullOrEmpty(info.LandmarkId) ? "no revealed landmark" : $"landmark {info.LandmarkId}";
            var eventText = string.IsNullOrEmpty(info.EventId) ? "no revealed event" : $"event {info.EventId}";
            return $"Cell {info.Coord}: Revealed {revealedTerrain}, move cost {info.BaseMoveCost}, {landmark}, {eventText}.";
        }

        private static HexVisibilitySafeCellInfo CreateFullyRevealedCellInfo(HexMapData loadedMap, HexCoord coord)
        {
            if (!loadedMap.TryGetCell(coord, out var cell))
            {
                return HexVisibilitySafeCellInfo.Missing(coord);
            }

            return new HexVisibilitySafeCellInfo(
                coord,
                HexCellVisibility.Revealed,
                true,
                true,
                true,
                cell.TileDefinitionId,
                cell.TerrainTypeId,
                cell.BaseMoveCost,
                cell.BaseWalkable,
                cell.BaseBlocksVision,
                cell.EventId,
                cell.LandmarkId,
                cell.VisualFloor);
        }
    }
}

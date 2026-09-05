using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Map.Unity
{
    [CreateAssetMenu(menuName = "Seoul Playup/Map/Hex Tile Definition")]
    public sealed class HexTileDefinition : ScriptableObject
    {
        [SerializeField] private string id = "tile";
        [SerializeField] private string terrainTypeId = "terrain";
        [SerializeField] private int baseMoveCost = 1;
        [SerializeField] private bool baseWalkable = true;
        // ⚠️ 시야 계산에 쓰이지 않는다(DEC-2026-07-28-03: 차폐 미구현 확정). 저작해도 게임 동작은 바뀌지 않으며, 값은 이력 보존을 위해 남겨 둔다. 계약은 10-specs/systems/fog-of-war.md FW-3.
        [SerializeField] private bool baseBlocksVision;
        [SerializeField] private string eventId;
        [SerializeField] private string landmarkId;
        [SerializeField] private Color displayColor = Color.white;

        public string Id => id;
        public string TerrainTypeId => terrainTypeId;
        public int BaseMoveCost => Mathf.Max(1, baseMoveCost);
        public bool BaseWalkable => baseWalkable;
        public bool BaseBlocksVision => baseBlocksVision;
        public string EventId => eventId;
        public string LandmarkId => landmarkId;
        public Color DisplayColor => displayColor;

        public HexCellData ToCellData(HexCoord coord)
        {
            return new HexCellData(coord, id, terrainTypeId, BaseMoveCost, baseWalkable, baseBlocksVision, eventId, landmarkId);
        }

        public void Configure(string id, string terrainTypeId, int baseMoveCost, bool baseWalkable, bool baseBlocksVision, string eventId, string landmarkId, Color displayColor)
        {
            this.id = id;
            this.terrainTypeId = terrainTypeId;
            this.baseMoveCost = Mathf.Max(1, baseMoveCost);
            this.baseWalkable = baseWalkable;
            this.baseBlocksVision = baseBlocksVision;
            this.eventId = eventId;
            this.landmarkId = landmarkId;
            this.displayColor = displayColor;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace SeoulPlayup.Map.Runtime
{
    public sealed class HexMapData
    {
        private readonly Dictionary<HexCoord, HexCellData> cells;
        private readonly HexObjectiveBinding[] objectiveBindings;
        private readonly HexMonsterSpawnRef[] monsterSpawnRefs;
        private readonly HexPatrolAreaRef[] patrolAreas;
        private readonly HexMapObjectData[] objectRefs;
        private readonly HexTrapData[] trapRefs;
        private readonly HexMapAreaRef[] areas;
        private readonly Dictionary<HexCoord, HexMapObjectData[]> objectRefsByCoord;

        public HexMapData(
            IEnumerable<HexCellData> cells,
            IEnumerable<HexObjectiveBinding> objectiveBindings = null,
            IEnumerable<HexMonsterSpawnRef> monsterSpawnRefs = null,
            IEnumerable<HexPatrolAreaRef> patrolAreas = null,
            IEnumerable<HexMapObjectData> objectRefs = null,
            IEnumerable<HexTrapData> trapRefs = null,
            IEnumerable<HexMapAreaRef> areas = null)
        {
            if (cells == null)
            {
                throw new ArgumentNullException(nameof(cells));
            }

            this.cells = new Dictionary<HexCoord, HexCellData>();
            foreach (var cell in cells)
            {
                this.cells[cell.Coord] = cell;
            }

            this.objectiveBindings = objectiveBindings == null
                ? Array.Empty<HexObjectiveBinding>()
                : objectiveBindings.Where(binding => binding.IsConfigured).ToArray();
            this.monsterSpawnRefs = monsterSpawnRefs == null
                ? Array.Empty<HexMonsterSpawnRef>()
                : monsterSpawnRefs.Where(spawnRef => spawnRef.IsConfigured).ToArray();
            this.patrolAreas = patrolAreas == null
                ? Array.Empty<HexPatrolAreaRef>()
                : patrolAreas.Where(area => area.IsConfigured).ToArray();
            this.objectRefs = objectRefs == null
                ? Array.Empty<HexMapObjectData>()
                : objectRefs.Where(objectRef => objectRef.IsConfigured).ToArray();
            this.trapRefs = trapRefs == null
                ? Array.Empty<HexTrapData>()
                : trapRefs.Where(trapRef => trapRef.IsConfigured).ToArray();
            this.areas = areas == null
                ? Array.Empty<HexMapAreaRef>()
                : areas.Where(area => area.IsConfigured).ToArray();
            objectRefsByCoord = this.objectRefs
                .SelectMany(objectRef => objectRef.OccupiedCoords.Select(coord => new { coord, objectRef }))
                .GroupBy(pair => pair.coord)
                .ToDictionary(group => group.Key, group => group.Select(pair => pair.objectRef).Distinct().ToArray());
        }

        public int Count => cells.Count;
        public IEnumerable<HexCellData> AllCells => cells.Values.OrderBy(cell => cell.Coord);
        public IReadOnlyList<HexObjectiveBinding> ObjectiveBindings => objectiveBindings;
        public IReadOnlyList<HexMonsterSpawnRef> MonsterSpawnRefs => monsterSpawnRefs;
        public IReadOnlyList<HexPatrolAreaRef> PatrolAreas => patrolAreas;
        public IReadOnlyList<HexMapObjectData> ObjectRefs => objectRefs;
        public IReadOnlyList<HexTrapData> TrapRefs => trapRefs;

        /// <summary>목적이 붙은 저작 영역(보스 아레나 등). 용도별 필터는 소비자가 한다.</summary>
        public IReadOnlyList<HexMapAreaRef> Areas => areas;

        public bool Contains(HexCoord coord) => cells.ContainsKey(coord);
        public bool TryGetCell(HexCoord coord, out HexCellData cell) => cells.TryGetValue(coord, out cell);

        public IReadOnlyList<HexMapObjectData> GetObjectsAt(HexCoord coord)
        {
            return objectRefsByCoord.TryGetValue(coord, out var refs)
                ? refs
                : (IReadOnlyList<HexMapObjectData>)Array.Empty<HexMapObjectData>();
        }

        public bool HasMovementBlockingObject(HexCoord coord)
        {
            return objectRefsByCoord.TryGetValue(coord, out var refs) && refs.Any(objectRef => objectRef.BlocksMovement);
        }

        public HexCellData GetCellOrDefault(HexCoord coord)
        {
            return cells.TryGetValue(coord, out var cell) ? cell : default;
        }
    }
}

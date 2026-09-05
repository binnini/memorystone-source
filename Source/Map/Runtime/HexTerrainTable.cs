using System;
using System.Collections.Generic;

namespace SeoulPlayup.Map.Runtime
{
    public sealed class HexTerrainTable
    {
        private readonly Dictionary<string, HexTerrainStats> stats;

        public HexTerrainTable(IEnumerable<KeyValuePair<string, HexTerrainStats>> entries)
        {
            stats = new Dictionary<string, HexTerrainStats>(StringComparer.OrdinalIgnoreCase);
            if (entries == null)
            {
                return;
            }

            foreach (var pair in entries)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key))
                {
                    stats[pair.Key.Trim()] = pair.Value;
                }
            }
        }

        public static HexTerrainTable Empty { get; } = new HexTerrainTable(null);

        public bool TryGetStats(string terrainTypeId, out HexTerrainStats terrainStats)
        {
            if (!string.IsNullOrWhiteSpace(terrainTypeId))
            {
                return stats.TryGetValue(terrainTypeId.Trim(), out terrainStats);
            }

            terrainStats = HexTerrainStats.Default;
            return false;
        }

        public HexTerrainStats GetStats(string terrainTypeId)
        {
            return TryGetStats(terrainTypeId, out var s) ? s : HexTerrainStats.Default;
        }

        public int GetDefaultMoveCost(string terrainTypeId)
        {
            return TryGetStats(terrainTypeId, out var s) ? s.DefaultMoveCost : 1;
        }

        public int GetCombatDefenseBonus(string terrainTypeId)
        {
            return TryGetStats(terrainTypeId, out var s) ? s.CombatDefenseBonus : 0;
        }

        public int GetCombatAttackBonus(string terrainTypeId)
        {
            return TryGetStats(terrainTypeId, out var s) ? s.CombatAttackBonus : 0;
        }
    }
}

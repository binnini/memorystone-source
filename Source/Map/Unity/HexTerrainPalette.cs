using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Map.Unity
{
    [CreateAssetMenu(menuName = "Seoul Playup/Map/Hex Terrain Palette")]
    public sealed class HexTerrainPalette : ScriptableObject
    {
        [SerializeField] private List<Entry> entries = new List<Entry>();

        public IReadOnlyList<Entry> Entries => entries;

        public IEnumerable<string> TerrainTypeIds => entries
            .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.TerrainTypeId))
            .Select(entry => entry.TerrainTypeId.Trim());

        public bool ContainsTerrainTypeId(string terrainTypeId)
        {
            return TerrainTypeIds.Contains(terrainTypeId, StringComparer.OrdinalIgnoreCase);
        }

        public void ConfigureForTests(IEnumerable<Entry> entries)
        {
            this.entries = entries == null ? new List<Entry>() : new List<Entry>(entries);
        }

        public HexTerrainTable ToTerrainTable()
        {
            var kvps = entries
                .Where(e => e != null && !string.IsNullOrWhiteSpace(e.TerrainTypeId))
                .Select(e => new KeyValuePair<string, HexTerrainStats>(e.TerrainTypeId.Trim(), e.ToTerrainStats()));
            return new HexTerrainTable(kvps);
        }

        /// <summary>
        /// Builds the movement/selection trait set used by pathfinding and tile selection.
        /// Terrain ids are restricted via tags so designers can add future tiles without code
        /// changes: tag a terrain <c>water</c> (blocks movement and selection), <c>impassable</c>
        /// (blocks movement), or <c>unselectable</c> (blocks selection). The built-in water
        /// defaults from <see cref="HexTerrainTraits"/> are always included so existing boards
        /// behave correctly even when a terrain entry omits the tag.
        /// </summary>
        public HexTerrainTraits ToTerrainTraits()
        {
            var impassable = new List<string>(HexTerrainTraits.DefaultImpassableTerrainIds);
            var unselectable = new List<string>(HexTerrainTraits.DefaultUnselectableTerrainIds);

            foreach (var entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.TerrainTypeId))
                {
                    continue;
                }

                var id = entry.TerrainTypeId.Trim();
                var isWater = HasTag(entry, "water");
                if (isWater || HasTag(entry, "impassable"))
                {
                    impassable.Add(id);
                }

                if (isWater || HasTag(entry, "unselectable"))
                {
                    unselectable.Add(id);
                }
            }

            return new HexTerrainTraits(impassable, unselectable);
        }

        private static bool HasTag(Entry entry, string tag)
        {
            return entry.Tags != null && entry.Tags.Any(t => string.Equals(t?.Trim(), tag, StringComparison.OrdinalIgnoreCase));
        }

        [Serializable]
        public sealed class Entry
        {
            [SerializeField] private string terrainTypeId;
            [SerializeField] private string label;
            [SerializeField] private int order;
            [SerializeField] private Color colorHint = Color.clear;
            [SerializeField] private List<string> tags = new List<string>();
            [SerializeField] private string defaultBrushTooltip;
            [SerializeField] private int defaultMoveCost = 1;
            [SerializeField] private int combatDefenseBonus = 0;
            [SerializeField] private int combatAttackBonus = 0;

            public Entry()
            {
            }

            public Entry(
                string terrainTypeId,
                string label = null,
                int order = 0,
                Color? colorHint = null,
                IEnumerable<string> tags = null,
                string defaultBrushTooltip = null,
                int defaultMoveCost = 1,
                int combatDefenseBonus = 0,
                int combatAttackBonus = 0)
            {
                this.terrainTypeId = terrainTypeId;
                this.label = label;
                this.order = order;
                this.colorHint = colorHint ?? Color.clear;
                this.tags = tags == null ? new List<string>() : new List<string>(tags);
                this.defaultBrushTooltip = defaultBrushTooltip;
                this.defaultMoveCost = Math.Max(1, defaultMoveCost);
                this.combatDefenseBonus = Math.Max(0, combatDefenseBonus);
                this.combatAttackBonus = Math.Max(0, combatAttackBonus);
            }

            public string TerrainTypeId => terrainTypeId ?? string.Empty;
            public string Label => string.IsNullOrWhiteSpace(label) ? TerrainTypeId : label;
            public int Order => order;
            public Color ColorHint => colorHint;
            public IReadOnlyList<string> Tags => tags;
            public string DefaultBrushTooltip => defaultBrushTooltip ?? string.Empty;
            public int DefaultMoveCost => Math.Max(1, defaultMoveCost);
            public int CombatDefenseBonus => Math.Max(0, combatDefenseBonus);
            public int CombatAttackBonus => Math.Max(0, combatAttackBonus);

            public HexTerrainStats ToTerrainStats()
            {
                return new HexTerrainStats(DefaultMoveCost, CombatDefenseBonus, CombatAttackBonus);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace SeoulPlayup.Map.Unity
{
    [Serializable]
    public sealed class HexTileDefinitionCatalog
    {
        [SerializeField] private List<Entry> entries = new List<Entry>();
        private Dictionary<TileBase, HexTileDefinition> byTile;

        [Serializable]
        public sealed class Entry
        {
            public TileBase tile;
            public HexTileDefinition definition;
        }

        public IReadOnlyList<Entry> Entries => entries;

        public void Add(TileBase tile, HexTileDefinition definition)
        {
            entries.Add(new Entry { tile = tile, definition = definition });
            byTile = null;
        }

        public bool TryGetDefinition(TileBase tile, out HexTileDefinition definition)
        {
            EnsureCache();
            if (tile == null)
            {
                definition = null;
                return false;
            }

            return byTile.TryGetValue(tile, out definition) && definition != null;
        }

        public IEnumerable<string> Validate()
        {
            var seen = new HashSet<TileBase>();
            foreach (var entry in entries)
            {
                if (entry == null || entry.tile == null || entry.definition == null)
                {
                    yield return "Tile definition catalog contains an empty tile/definition entry.";
                    continue;
                }

                if (!seen.Add(entry.tile))
                {
                    yield return $"Tile definition catalog contains a duplicate mapping for tile '{entry.tile.name}'.";
                }
            }
        }

        private void EnsureCache()
        {
            if (byTile != null)
            {
                return;
            }

            byTile = new Dictionary<TileBase, HexTileDefinition>();
            foreach (var entry in entries)
            {
                if (entry?.tile == null || entry.definition == null)
                {
                    continue;
                }

                byTile[entry.tile] = entry.definition;
            }
        }
    }
}

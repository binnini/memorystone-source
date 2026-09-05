using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Map.Unity
{
    [CreateAssetMenu(menuName = "Seoul Playup/Map/Hex Tile Preset Catalog")]
    public sealed class HexTilePresetCatalog : ScriptableObject
    {
        [SerializeField] private AtlasTileCatalog atlasTileCatalog;
        [SerializeField] private HexTerrainPalette terrainPalette;
        [SerializeField] private List<Entry> entries = new List<Entry>();

        public AtlasTileCatalog AtlasTileCatalog => atlasTileCatalog;
        public HexTerrainPalette TerrainPalette => terrainPalette;
        public IReadOnlyList<Entry> Entries => entries ?? (IReadOnlyList<Entry>)Array.Empty<Entry>();

        public void ConfigureForTests(AtlasTileCatalog atlasTileCatalog, HexTerrainPalette terrainPalette, IEnumerable<Entry> entries)
        {
            this.atlasTileCatalog = atlasTileCatalog;
            this.terrainPalette = terrainPalette;
            this.entries = entries == null ? new List<Entry>() : new List<Entry>(entries);
        }

        public bool TryResolve(string tilePresetId, out HexTilePresetResolution resolution)
        {
            resolution = Resolve(tilePresetId);
            return resolution.IsResolved;
        }

        public HexTilePresetResolution Resolve(string tilePresetId)
        {
            var errors = new List<string>();
            var entry = FindEntry(tilePresetId);
            if (entry == null)
            {
                errors.Add($"Hex Tile Preset Catalog has no tile preset '{tilePresetId}'.");
                return HexTilePresetResolution.Unresolved(tilePresetId, errors);
            }

            var atlasEntry = FindAtlasEntry(entry.AtlasVisualId);
            if (atlasEntry == null)
            {
                errors.Add($"Hex Tile Preset '{entry.TilePresetId}' references missing Atlas visual ID '{entry.AtlasVisualId}'.");
            }

            var terrainEntry = FindTerrainEntry(entry.TerrainTypeId);
            if (terrainEntry == null)
            {
                errors.Add($"Hex Tile Preset '{entry.TilePresetId}' references missing terrain type ID '{entry.TerrainTypeId}'.");
            }

            return errors.Count == 0
                ? HexTilePresetResolution.Resolved(entry, atlasEntry, terrainEntry)
                : HexTilePresetResolution.Unresolved(entry, atlasEntry, terrainEntry, errors);
        }

        public IEnumerable<string> Validate()
        {
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in Entries)
            {
                if (entry == null)
                {
                    yield return "Hex Tile Preset Catalog contains an empty entry.";
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.TilePresetId))
                {
                    yield return "Hex Tile Preset Catalog entry is missing a tile preset ID.";
                }
                else if (!seenIds.Add(entry.TilePresetId.Trim()))
                {
                    yield return $"Hex Tile Preset Catalog contains a duplicate tile preset ID '{entry.TilePresetId}'.";
                }

                var resolution = Resolve(entry.TilePresetId);
                foreach (var error in resolution.Errors)
                {
                    yield return error;
                }
            }
        }

        private Entry FindEntry(string tilePresetId)
        {
            if (string.IsNullOrWhiteSpace(tilePresetId))
            {
                return null;
            }

            var trimmed = tilePresetId.Trim();
            return Entries.FirstOrDefault(entry => entry != null && string.Equals(entry.TilePresetId, trimmed, StringComparison.OrdinalIgnoreCase));
        }

        private AtlasTileCatalog.Entry FindAtlasEntry(string atlasVisualId)
        {
            if (atlasTileCatalog == null || string.IsNullOrWhiteSpace(atlasVisualId))
            {
                return null;
            }

            var trimmed = atlasVisualId.Trim();
            return atlasTileCatalog.Entries.FirstOrDefault(entry => entry != null && string.Equals(entry.AtlasVisualId, trimmed, StringComparison.OrdinalIgnoreCase));
        }

        private HexTerrainPalette.Entry FindTerrainEntry(string terrainTypeId)
        {
            if (terrainPalette == null || string.IsNullOrWhiteSpace(terrainTypeId))
            {
                return null;
            }

            var trimmed = terrainTypeId.Trim();
            return terrainPalette.Entries.FirstOrDefault(entry => entry != null && string.Equals(entry.TerrainTypeId, trimmed, StringComparison.OrdinalIgnoreCase));
        }

        [Serializable]
        public sealed class Entry
        {
            [SerializeField] private string tilePresetId;
            [SerializeField] private string label;
            [SerializeField] private string atlasVisualId;
            [SerializeField] private string terrainTypeId;
            [SerializeField] private int heightLevel;
            [SerializeField] private int rotationSteps;
            [SerializeField] private int edgeConnectionMask;
            [SerializeField] private bool baseWalkable = true;

            public Entry()
            {
            }

            public Entry(
                string tilePresetId,
                string atlasVisualId,
                string terrainTypeId,
                string label = null,
                int heightLevel = 0,
                int rotationSteps = 0,
                int edgeConnectionMask = 0,
                bool baseWalkable = true)
            {
                this.tilePresetId = tilePresetId;
                this.label = label;
                this.atlasVisualId = atlasVisualId;
                this.terrainTypeId = terrainTypeId;
                this.heightLevel = HexCellData.ClampHeight(heightLevel);
                this.rotationSteps = Mathf.Clamp(rotationSteps, 0, 5);
                this.edgeConnectionMask = Mathf.Clamp(edgeConnectionMask, 0, 63);
                this.baseWalkable = baseWalkable;
            }

            public string TilePresetId => tilePresetId ?? string.Empty;
            public string Label => string.IsNullOrWhiteSpace(label) ? TilePresetId : label;
            public string AtlasVisualId => atlasVisualId ?? string.Empty;
            public string TerrainTypeId => terrainTypeId ?? string.Empty;
            public int HeightLevel => HexCellData.ClampHeight(heightLevel);
            public int RotationSteps => Mathf.Clamp(rotationSteps, 0, 5);
            public int EdgeConnectionMask => Mathf.Clamp(edgeConnectionMask, 0, 63);
            public bool BaseWalkable => baseWalkable;

            public HexSparseMapAuthoringCell ToAuthoringCell(HexCoord coord)
            {
                return new HexSparseMapAuthoringCell(
                    coord,
                    TilePresetId,
                    TerrainTypeId,
                    AtlasVisualId,
                    heightLevel: HeightLevel,
                    rotationSteps: RotationSteps,
                    edgeConnectionMask: EdgeConnectionMask,
                    baseWalkable: BaseWalkable);
            }
        }
    }

    public readonly struct HexTilePresetResolution
    {
        private HexTilePresetResolution(
            HexTilePresetCatalog.Entry entry,
            AtlasTileCatalog.Entry atlasEntry,
            HexTerrainPalette.Entry terrainEntry,
            IReadOnlyList<string> errors,
            string requestedTilePresetId)
        {
            Entry = entry;
            AtlasEntry = atlasEntry;
            TerrainEntry = terrainEntry;
            Errors = errors ?? Array.Empty<string>();
            RequestedTilePresetId = requestedTilePresetId ?? entry?.TilePresetId ?? string.Empty;
        }

        public HexTilePresetCatalog.Entry Entry { get; }
        public AtlasTileCatalog.Entry AtlasEntry { get; }
        public HexTerrainPalette.Entry TerrainEntry { get; }
        public IReadOnlyList<string> Errors { get; }
        public string RequestedTilePresetId { get; }
        public bool IsResolved => Entry != null && AtlasEntry != null && TerrainEntry != null && Errors.Count == 0;

        public static HexTilePresetResolution Resolved(HexTilePresetCatalog.Entry entry, AtlasTileCatalog.Entry atlasEntry, HexTerrainPalette.Entry terrainEntry)
        {
            return new HexTilePresetResolution(entry, atlasEntry, terrainEntry, Array.Empty<string>(), null);
        }

        public static HexTilePresetResolution Unresolved(string requestedTilePresetId, IReadOnlyList<string> errors)
        {
            return new HexTilePresetResolution(null, null, null, errors, requestedTilePresetId);
        }

        public static HexTilePresetResolution Unresolved(
            HexTilePresetCatalog.Entry entry,
            AtlasTileCatalog.Entry atlasEntry,
            HexTerrainPalette.Entry terrainEntry,
            IReadOnlyList<string> errors)
        {
            return new HexTilePresetResolution(entry, atlasEntry, terrainEntry, errors, null);
        }
    }
}

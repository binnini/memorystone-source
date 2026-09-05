using System;
using System.Collections.Generic;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Map.Unity
{
    [CreateAssetMenu(menuName = "Seoul Playup/Atlas/Atlas Tile Catalog")]
    public sealed class AtlasTileCatalog : ScriptableObject
    {
        [SerializeField] private List<Entry> entries = new List<Entry>();
        private Dictionary<string, Entry> byAtlasVisualId;
        private int byAtlasVisualIdEntryCount = -1;

        public IReadOnlyList<Entry> Entries => entries;

        public void ConfigureForTests(IEnumerable<Entry> entries)
        {
            this.entries = entries == null ? new List<Entry>() : new List<Entry>(entries);
            ClearCache();
        }

        public AtlasTileCatalogResolution Resolve(HexCellData cell)
        {
            EnsureCache();

            return TryResolveByKey(byAtlasVisualId, cell.AtlasVisualId, out var atlasEntry)
                ? AtlasTileCatalogResolution.Resolved(atlasEntry, AtlasTileCatalogResolutionSource.AtlasVisualId)
                : AtlasTileCatalogResolution.Missing();
        }

        public IEnumerable<string> Validate()
        {
            var seenAtlasIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                foreach (var message in ValidateEntry(entry, seenAtlasIds, "entry"))
                {
                    yield return message;
                }
            }
        }

        public IEnumerable<string> ValidateCell(HexCellData cell)
        {
            var resolution = Resolve(cell);
            var entry = resolution.Entry;
            if (entry == null)
            {
                yield return $"Atlas Tile Catalog has no entry for explicit Atlas visual ID '{cell.AtlasVisualId}' on cell {cell.Coord}.";
                yield break;
            }

            if (!entry.SupportsRotationSteps && cell.RotationSteps != 0)
            {
                yield return $"Atlas Tile Catalog entry '{entry.AtlasVisualId}' does not support rotation steps, but cell {cell.Coord} requests rotation step {cell.RotationSteps}.";
            }

            if (cell.EdgeConnectionMask < 0 || cell.EdgeConnectionMask > 63)
            {
                yield return $"Atlas Tile Catalog cell {cell.Coord} has invalid edge connection mask {cell.EdgeConnectionMask}; expected 0..63.";
            }
        }

        private static IEnumerable<string> ValidateEntry(Entry entry, ISet<string> seenAtlasIds, string context)
        {
            if (entry == null)
            {
                yield return $"Atlas Tile Catalog contains an empty {context}.";
                yield break;
            }

            if (string.IsNullOrWhiteSpace(entry.AtlasVisualId))
            {
                yield return $"Atlas Tile Catalog {context} is missing an Atlas visual ID.";
            }
            else if (seenAtlasIds != null && !seenAtlasIds.Add(entry.AtlasVisualId.Trim()))
            {
                yield return $"Atlas Tile Catalog contains a duplicate Atlas visual ID '{entry.AtlasVisualId}'.";
            }

            if (!string.IsNullOrWhiteSpace(entry.AtlasVisualId) && entry.TopPrefab == null)
            {
                yield return $"Atlas Tile Catalog {context} '{entry.AtlasVisualId}' has no top prefab.";
            }

            if (entry.SideVisualMode == AtlasSideVisualMode.Prefab && entry.SidePrefab == null)
            {
                yield return $"Atlas Tile Catalog {context} '{entry.AtlasVisualId}' uses prefab side visuals but has no side prefab.";
            }
        }

        private static bool TryResolveByKey(IReadOnlyDictionary<string, Entry> map, string key, out Entry entry)
        {
            entry = null;
            return !string.IsNullOrWhiteSpace(key) && map.TryGetValue(key.Trim(), out entry) && entry != null;
        }

        private void EnsureCache()
        {
            var entryCount = entries?.Count ?? 0;
            if (byAtlasVisualId != null && byAtlasVisualIdEntryCount == entryCount)
            {
                return;
            }

            byAtlasVisualId = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                if (entry == null)
                {
                    continue;
                }

                AddIfPresent(byAtlasVisualId, entry.AtlasVisualId, entry);
            }

            byAtlasVisualIdEntryCount = entryCount;
        }

        private static void AddIfPresent(IDictionary<string, Entry> map, string key, Entry entry)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                map[key.Trim()] = entry;
            }
        }

        private void ClearCache()
        {
            byAtlasVisualId = null;
            byAtlasVisualIdEntryCount = -1;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ClearCache();
        }
#endif

        [Serializable]
        public sealed class Entry
        {
            [SerializeField] private string atlasVisualId;
            [SerializeField] private string label;
            [SerializeField] private GameObject topPrefab;
            [SerializeField] private bool supportsRotationSteps = true;
            [SerializeField] private bool sideVisualsDeferred = true;
            [SerializeField] private AtlasSideVisualMode sideVisualMode = AtlasSideVisualMode.Deferred;
            [SerializeField] private GameObject sidePrefab;
            [SerializeField] private Material sideMaterial;
            [SerializeField] private Texture sideTexture;
            [SerializeField] private Color hiddenTint = new Color(0.08f, 0.1f, 0.12f, 0.82f);
            [SerializeField] private Color hintedTint = new Color(0.28f, 0.32f, 0.36f, 0.58f);
            [SerializeField] private Color revealedTint = new Color(1f, 1f, 1f, 0f);

            public Entry()
            {
            }

            public Entry(
                string atlasVisualId,
                GameObject topPrefab = null,
                string label = null,
                bool supportsRotationSteps = true,
                bool sideVisualsDeferred = true,
                AtlasSideVisualMode sideVisualMode = AtlasSideVisualMode.Deferred,
                GameObject sidePrefab = null,
                Material sideMaterial = null,
                Texture sideTexture = null,
                Color? hiddenTint = null,
                Color? hintedTint = null,
                Color? revealedTint = null)
            {
                this.atlasVisualId = atlasVisualId;
                this.topPrefab = topPrefab;
                this.label = label;
                this.supportsRotationSteps = supportsRotationSteps;
                this.sideVisualMode = sideVisualMode;
                this.sideVisualsDeferred = sideVisualMode == AtlasSideVisualMode.Deferred && sideVisualsDeferred;
                this.sidePrefab = sidePrefab;
                this.sideMaterial = sideMaterial;
                this.sideTexture = sideTexture;
                this.hiddenTint = hiddenTint ?? this.hiddenTint;
                this.hintedTint = hintedTint ?? this.hintedTint;
                this.revealedTint = revealedTint ?? this.revealedTint;
            }

            public string AtlasVisualId => atlasVisualId ?? string.Empty;
            public string Label => string.IsNullOrWhiteSpace(label) ? AtlasVisualId : label;
            public GameObject TopPrefab => topPrefab;
            public bool SupportsRotationSteps => supportsRotationSteps;
            public bool SideVisualsDeferred => sideVisualsDeferred || sideVisualMode == AtlasSideVisualMode.Deferred;
            public AtlasSideVisualMode SideVisualMode => sideVisualMode;
            public GameObject SidePrefab => sidePrefab;
            public Material SideMaterial => sideMaterial;
            public Texture SideTexture => sideTexture;
            public Color HiddenTint => hiddenTint;
            public Color HintedTint => hintedTint;
            public Color RevealedTint => revealedTint;
        }
    }

    public enum AtlasSideVisualMode
    {
        Deferred,
        Generated,
        Prefab,
        Hidden
    }

    public enum AtlasTileCatalogResolutionSource
    {
        AtlasVisualId,
        MissingAtlasVisualId
    }

    public readonly struct AtlasTileCatalogResolution
    {
        private AtlasTileCatalogResolution(AtlasTileCatalog.Entry entry, AtlasTileCatalogResolutionSource source, bool isFallback)
        {
            Entry = entry;
            Source = source;
            IsFallback = isFallback;
        }

        public AtlasTileCatalog.Entry Entry { get; }
        public AtlasTileCatalogResolutionSource Source { get; }
        public bool IsFallback { get; }
        public bool HasTopPrefab => Entry != null && Entry.TopPrefab != null;

        public static AtlasTileCatalogResolution Resolved(AtlasTileCatalog.Entry entry, AtlasTileCatalogResolutionSource source)
        {
            return new AtlasTileCatalogResolution(entry, source, false);
        }

        public static AtlasTileCatalogResolution Missing()
        {
            return new AtlasTileCatalogResolution(null, AtlasTileCatalogResolutionSource.MissingAtlasVisualId, false);
        }
    }
}

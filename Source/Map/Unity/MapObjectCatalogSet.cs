using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SeoulPlayup.Map.Unity
{
    using Entry = RuntimeMapObjectPrefabCatalog.Entry;

    /// <summary>
    /// Aggregates the per-type <see cref="MapObjectTypedCatalog"/> assets under
    /// <c>Assets/Data/Object/</c> into a single resolver and treats them as the source of truth for
    /// placeable runtime visual objects. <c>objectRef</c> values are expected to be globally unique across
    /// every typed catalog; duplicates are surfaced through <see cref="FindDuplicateObjectRefs"/> for
    /// editor validation. This is the only resolution path for map object prefabs; the legacy
    /// <see cref="RuntimeMapObjectPrefabCatalog"/> single catalog was unified into this set (리팩토링 6단계).
    /// </summary>
    [CreateAssetMenu(menuName = "Seoul Playup/Map/Map Object Catalog Set")]
    public sealed class MapObjectCatalogSet : ScriptableObject
    {
        public const string DefaultResourcesPath = "MapObjectCatalogSet";
        public const string DefaultCatalogAssetPath = "Assets/Data/Object/Catalogs/Resources/MapObjectCatalogSet.asset";

        [SerializeField] private List<MapObjectTypedCatalog> catalogs = new List<MapObjectTypedCatalog>();

        private Dictionary<string, Entry> byObjectRef;
        private int cachedEntryCount = -1;

        public IReadOnlyList<MapObjectTypedCatalog> Catalogs =>
            catalogs == null ? Array.Empty<MapObjectTypedCatalog>() : catalogs.Where(catalog => catalog != null).ToArray();

        public IReadOnlyList<Entry> Entries => EnumerateEntries().ToArray();

        public void ConfigureForTests(IEnumerable<MapObjectTypedCatalog> catalogs)
        {
            this.catalogs = catalogs == null ? new List<MapObjectTypedCatalog>() : new List<MapObjectTypedCatalog>(catalogs);
            byObjectRef = null;
            cachedEntryCount = -1;
        }

        public bool TryResolve(string objectRef, out GameObject prefab)
        {
            prefab = null;
            if (!TryGetEntry(objectRef, out var entry))
            {
                return false;
            }

            prefab = entry.Prefab;
            return prefab != null;
        }

        public bool TryGetEntry(string objectRef, out Entry entry)
        {
            entry = null;
            if (string.IsNullOrWhiteSpace(objectRef))
            {
                return false;
            }

            EnsureCache();
            return byObjectRef.TryGetValue(objectRef.Trim(), out entry) && entry != null;
        }

        /// <summary>Alias kept symmetric with <see cref="MapObjectTypedCatalog.TryFindEntry"/>.</summary>
        public bool TryFindEntry(string objectRef, out Entry entry) => TryGetEntry(objectRef, out entry);

        public IReadOnlyList<Entry> GetEntriesForType(HexMapObjectType type)
        {
            return EnumerateEntries()
                .Where(entry => entry.ObjectType == type)
                .ToArray();
        }

        /// <summary>objectRef values that appear in more than one entry across the whole set.</summary>
        public IReadOnlyList<string> FindDuplicateObjectRefs()
        {
            return EnumerateEntries()
                .Select(entry => entry.ObjectRef?.Trim())
                .Where(objectRef => !string.IsNullOrWhiteSpace(objectRef))
                .GroupBy(objectRef => objectRef, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();
        }

        private IEnumerable<Entry> EnumerateEntries()
        {
            if (catalogs == null)
            {
                yield break;
            }

            foreach (var catalog in catalogs)
            {
                if (catalog == null)
                {
                    continue;
                }

                foreach (var entry in catalog.Entries)
                {
                    if (entry != null && !string.IsNullOrWhiteSpace(entry.ObjectRef))
                    {
                        yield return entry;
                    }
                }
            }
        }

        private void EnsureCache()
        {
            // Rebuild when the aggregated entry count changes so live edits to the typed catalogs (e.g.
            // SaveDefinitionDefaults adding an entry) are picked up within the same editor session.
            var count = EnumerateEntries().Count();
            if (byObjectRef != null && cachedEntryCount == count)
            {
                return;
            }

            byObjectRef = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in EnumerateEntries())
            {
                var key = entry.ObjectRef.Trim();
                if (!byObjectRef.ContainsKey(key))
                {
                    byObjectRef[key] = entry;
                }
            }

            cachedEntryCount = count;
        }

        public static MapObjectCatalogSet LoadDefault()
        {
            var set = Resources.Load<MapObjectCatalogSet>(DefaultResourcesPath);
            if (set != null)
            {
                return set;
            }

#if UNITY_EDITOR
            return AssetDatabase.LoadAssetAtPath<MapObjectCatalogSet>(DefaultCatalogAssetPath);
#else
            return null;
#endif
        }

        public static bool TryResolveDefault(string objectRef, out GameObject prefab)
        {
            prefab = null;
            var set = LoadDefault();
            return set != null && set.TryResolve(objectRef, out prefab);
        }
    }
}

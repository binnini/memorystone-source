using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Map.Unity
{
    using Entry = RuntimeMapObjectPrefabCatalog.Entry;

    /// <summary>
    /// Type-scoped catalog of placeable map object prefabs. Reuses
    /// <see cref="RuntimeMapObjectPrefabCatalog.Entry"/> so the entry layout stays compatible with the
    /// legacy single catalog and conversions are field-for-field. A typed catalog is the source of truth
    /// for one <see cref="HexMapObjectType"/> (Building, TreasureChest, ...); Prop-flavoured entries are
    /// stored here mapped onto an existing runtime object type (e.g. Building) until Prop gets a dedicated
    /// runtime type. Aggregated through <see cref="MapObjectCatalogSet"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Seoul Playup/Map/Map Object Typed Catalog")]
    public sealed class MapObjectTypedCatalog : ScriptableObject
    {
        [SerializeField] private HexMapObjectType objectType = HexMapObjectType.Building;
        [Tooltip("Editor-only palette grouping label. Empty falls back to the object type name. Lets a catalog " +
                 "(e.g. Prop) show as its own group in the object palette while entries still serialize as their objectType.")]
        [SerializeField] private string categoryLabel;
        [SerializeField] private List<Entry> entries = new List<Entry>();

        public HexMapObjectType ObjectType => objectType;

        /// <summary>Editor-only palette group label; defaults to the object type name when unset.</summary>
        public string CategoryLabel => string.IsNullOrWhiteSpace(categoryLabel) ? objectType.ToString() : categoryLabel.Trim();

        public IReadOnlyList<Entry> Entries => entries ?? (IReadOnlyList<Entry>)Array.Empty<Entry>();

        public void ConfigureForTests(HexMapObjectType objectType, IEnumerable<Entry> entries, string categoryLabel = null)
        {
            this.objectType = objectType;
            this.categoryLabel = categoryLabel;
            this.entries = entries == null ? new List<Entry>() : new List<Entry>(entries);
        }

        public bool TryFindEntry(string objectRef, out Entry entry)
        {
            entry = null;
            if (string.IsNullOrWhiteSpace(objectRef))
            {
                return false;
            }

            entry = Entries.FirstOrDefault(candidate =>
                candidate != null &&
                string.Equals(candidate.ObjectRef, objectRef.Trim(), StringComparison.OrdinalIgnoreCase));
            return entry != null;
        }

        public bool TryResolve(string objectRef, out GameObject prefab)
        {
            prefab = null;
            if (!TryFindEntry(objectRef, out var entry))
            {
                return false;
            }

            prefab = entry.Prefab;
            return prefab != null;
        }
    }
}

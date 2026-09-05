using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.MapDesign.Editor
{
    public static class MapObjectPrefabCatalog
    {
        public const string DefaultFolder = "Assets/Prefabs/Object";

        public static IReadOnlyList<string> GetPrefabIds(string folder = DefaultFolder)
        {
            return GetDefinitions(folder)
                .Where(definition => definition.ObjectType != HexMapObjectType.MonsterSpawn)
                .Select(definition => definition.ObjectRef)
                .ToArray();
        }

        public static IReadOnlyList<ObjectDefinition> GetDefinitions(string folder = DefaultFolder)
        {
            var definitions = new List<ObjectDefinition>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Source of truth: the per-type catalog set under Assets/Data/Object. Iterate per typed
            // catalog so each definition carries its editor palette category (e.g. Prop).
            if (TryLoadCatalogSet(out var catalogSet))
            {
                foreach (var typedCatalog in catalogSet.Catalogs)
                {
                    if (typedCatalog == null)
                    {
                        continue;
                    }

                    var category = typedCatalog.CategoryLabel;
                    foreach (var entry in typedCatalog.Entries)
                    {
                        if (entry == null || string.IsNullOrWhiteSpace(entry.ObjectRef) || seen.Contains(entry.ObjectRef))
                        {
                            continue;
                        }

                        definitions.Add(ObjectDefinition.FromCatalogEntry(entry, category));
                        seen.Add(entry.ObjectRef);
                    }
                }
            }

            if (AssetDatabase.IsValidFolder(folder))
            {
                foreach (var path in AssetDatabase.FindAssets("t:Prefab", new[] { folder })
                             .Select(AssetDatabase.GUIDToAssetPath)
                             .Where(path => path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                             .OrderBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase))
                {
                    var id = Path.GetFileNameWithoutExtension(path);
                    if (string.IsNullOrWhiteSpace(id) || seen.Contains(id))
                    {
                        continue;
                    }

                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    definitions.Add(ObjectDefinition.FromPrefab(id, prefab));
                    seen.Add(id);
                }
            }

            AddMonsterDefinitions(definitions, seen);

            return definitions.OrderBy(definition => definition.ObjectRef, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public static IReadOnlyList<ObjectDefinition> GetDefinitionsForObjectType(HexMapObjectType objectType, string folder = DefaultFolder)
        {
            return GetDefinitions(folder)
                .Where(definition => definition.ObjectType == objectType)
                .ToArray();
        }

        public static bool TryGetDefinition(string objectRef, out ObjectDefinition definition)
        {
            definition = GetDefinitions().FirstOrDefault(candidate =>
                string.Equals(candidate.ObjectRef, objectRef?.Trim(), StringComparison.OrdinalIgnoreCase));
            return definition != null;
        }

        public static bool TryLoadPrefab(string objectRef, out GameObject prefab, string folder = DefaultFolder)
        {
            prefab = null;
            if (TryLoadCatalogSet(out var catalogSet) && catalogSet.TryResolve(objectRef, out prefab))
            {
                return true;
            }

            if (TryGetDefinition(objectRef, out var definition) && definition.Prefab != null)
            {
                prefab = definition.Prefab;
                return true;
            }

            if (string.IsNullOrWhiteSpace(objectRef) || !AssetDatabase.IsValidFolder(folder))
            {
                return false;
            }

            var id = objectRef.Trim();
            var directPath = $"{folder}/{id}.prefab";
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(directPath);
            if (prefab != null)
            {
                return true;
            }

            foreach (var guid in AssetDatabase.FindAssets($"{id} t:Prefab", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.Equals(Path.GetFileNameWithoutExtension(path), id, StringComparison.OrdinalIgnoreCase))
                {
                    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    return prefab != null;
                }
            }

            return false;
        }

        public static bool IsPreviewableObjectType(HexMapObjectType objectType)
        {
            switch (objectType)
            {
                case HexMapObjectType.MonsterSpawn:
                case HexMapObjectType.Landmark:
                case HexMapObjectType.Building:
                case HexMapObjectType.TreasureChest:
                case HexMapObjectType.MemoryStone:
                case HexMapObjectType.Shop:
                case HexMapObjectType.CursedGachaMachine:
                case HexMapObjectType.CamperVan:
                case HexMapObjectType.Workshop:
                    return true;
                default:
                    return false;
            }
        }

        private static void AddMonsterDefinitions(List<ObjectDefinition> definitions, HashSet<string> seen)
        {
            var monsterCatalog = CombatState.CreateMonsterCatalog(CombatConfig.Default);
            foreach (var entry in monsterCatalog.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Id) ||
                    string.IsNullOrWhiteSpace(entry.VisualPrefabPath) ||
                    seen.Contains(entry.Id))
                {
                    continue;
                }

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.VisualPrefabPath);
                if (prefab == null)
                {
                    continue;
                }

                definitions.Add(ObjectDefinition.FromMonsterCatalogEntry(entry, prefab));
                seen.Add(entry.Id);
            }
        }

        public static string DrawPrefabIdPopup(string label, string currentObjectRef)
        {
            var ids = GetPrefabIds();
            var current = currentObjectRef?.Trim() ?? string.Empty;
            if (ids.Count == 0)
            {
                return EditorGUILayout.TextField("Object Ref", current);
            }

            var options = new List<string> { "<Custom / none>" };
            options.AddRange(ids);
            var index = string.IsNullOrWhiteSpace(current) ? 0 : Mathf.Max(0, options.IndexOf(current));
            index = EditorGUILayout.Popup(label, index, options.ToArray());
            if (index > 0)
            {
                current = options[Mathf.Clamp(index, 1, options.Count - 1)];
            }

            return EditorGUILayout.TextField("Object Ref", current);
        }

        public static bool SaveDefinitionDefaults(
            string objectRef,
            HexMapObjectType objectType,
            bool blocksMovement,
            bool blocksVision,
            bool interactable,
            IEnumerable<HexCoord> footprintOffsets,
            Vector3 visualScaleMultiplier,
            out string message)
        {
            message = null;
            if (string.IsNullOrWhiteSpace(objectRef))
            {
                message = "Select an object prefab before saving object defaults.";
                return false;
            }

            // Source of truth: write into the typed catalog that owns the objectRef (or the typed
            // catalog matching the object type when the entry is new).
            if (TryLoadCatalogSet(out var catalogSet) &&
                TryResolveTypedCatalogForSave(catalogSet, objectRef.Trim(), objectType, out var typedCatalog))
            {
                WriteDefinitionInto(typedCatalog, objectRef.Trim(), objectType, blocksMovement, blocksVision, interactable, footprintOffsets, visualScaleMultiplier);
                AssetDatabase.SaveAssets();
                message = $"Saved object defaults for '{objectRef.Trim()}' into '{typedCatalog.name}'.";
                return true;
            }

            message = $"Could not find a typed catalog for '{objectRef.Trim()}' ({objectType}) in the catalog set at {MapObjectCatalogSet.DefaultCatalogAssetPath}. Add a MapObjectTypedCatalog for this object type first.";
            return false;
        }

        /// <summary>
        /// Folder-watcher auto-registration: add-or-update an entry in a SPECIFIC typed catalog (resolved by
        /// the subfolder convention, so Building vs Prop — which share the same runtime object type — stay
        /// unambiguous). New entries inherit the catalog's object type and its default collision/footprint;
        /// variants only differ visually, so those defaults are the right starting point and can be tuned
        /// later. Returns true (with a message) when the catalog changed; false when nothing needed doing.
        /// </summary>
        public static bool AutoRegister(MapObjectTypedCatalog typedCatalog, string objectRef, GameObject prefab, out string message)
        {
            message = null;
            if (typedCatalog == null || prefab == null || string.IsNullOrWhiteSpace(objectRef))
            {
                return false;
            }

            objectRef = objectRef.Trim();
            var serialized = new SerializedObject(typedCatalog);
            var entries = serialized.FindProperty("entries");
            var index = FindEntryIndex(entries, objectRef);
            if (index >= 0)
            {
                // Existing entry: only refresh the prefab reference (settings are author-owned).
                var prefabProp = entries.GetArrayElementAtIndex(index).FindPropertyRelative("prefab");
                if (prefabProp.objectReferenceValue == prefab)
                {
                    return false;
                }

                prefabProp.objectReferenceValue = prefab;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(typedCatalog);
                message = $"updated prefab reference for '{objectRef}' in '{typedCatalog.name}'";
                return true;
            }

            var objectType = typedCatalog.ObjectType;
            entries.arraySize++;
            var entry = entries.GetArrayElementAtIndex(entries.arraySize - 1);
            entry.FindPropertyRelative("objectRef").stringValue = objectRef;
            entry.FindPropertyRelative("prefab").objectReferenceValue = prefab;
            WriteEntry(
                entry,
                objectType,
                HexMapObjectTypeDefaults.BlocksMovement(objectType),
                HexMapObjectTypeDefaults.BlocksVision(objectType),
                HexMapObjectTypeDefaults.Interactable(objectType),
                new[] { new HexCoord(0, 0) },
                Vector3.one);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(typedCatalog);
            message = $"registered '{objectRef}' into '{typedCatalog.name}'";
            return true;
        }

        /// <summary>Typed catalog whose palette category label matches <paramref name="category"/> (case-insensitive).</summary>
        public static MapObjectTypedCatalog ResolveCatalogByCategory(MapObjectCatalogSet catalogSet, string category)
        {
            if (catalogSet == null || string.IsNullOrWhiteSpace(category))
            {
                return null;
            }

            return catalogSet.Catalogs.FirstOrDefault(catalog =>
                catalog != null && string.Equals(catalog.CategoryLabel, category.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryResolveTypedCatalogForSave(MapObjectCatalogSet catalogSet, string objectRef, HexMapObjectType objectType, out MapObjectTypedCatalog typedCatalog)
        {
            typedCatalog = catalogSet.Catalogs.FirstOrDefault(candidate => candidate != null && candidate.TryFindEntry(objectRef, out _));
            if (typedCatalog != null)
            {
                return true;
            }

            typedCatalog = catalogSet.Catalogs.FirstOrDefault(candidate => candidate != null && candidate.ObjectType == objectType);
            return typedCatalog != null;
        }

        private static void WriteDefinitionInto(
            ScriptableObject catalog,
            string objectRef,
            HexMapObjectType objectType,
            bool blocksMovement,
            bool blocksVision,
            bool interactable,
            IEnumerable<HexCoord> footprintOffsets,
            Vector3 visualScaleMultiplier)
        {
            var serialized = new SerializedObject(catalog);
            var entries = serialized.FindProperty("entries");
            var index = FindEntryIndex(entries, objectRef);
            if (index < 0)
            {
                entries.arraySize++;
                index = entries.arraySize - 1;
                var entry = entries.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("objectRef").stringValue = objectRef;
                if (TryLoadPrefab(objectRef, out var prefab))
                {
                    entry.FindPropertyRelative("prefab").objectReferenceValue = prefab;
                }
            }

            WriteEntry(entries.GetArrayElementAtIndex(index), objectType, blocksMovement, blocksVision, interactable, footprintOffsets, visualScaleMultiplier);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
        }

        private static void WriteEntry(SerializedProperty entry, HexMapObjectType objectType, bool blocksMovement, bool blocksVision, bool interactable, IEnumerable<HexCoord> footprintOffsets, Vector3 visualScaleMultiplier)
        {
            entry.FindPropertyRelative("objectType").enumValueIndex = (int)objectType;
            entry.FindPropertyRelative("blocksMovement").boolValue = blocksMovement;
            entry.FindPropertyRelative("blocksVision").boolValue = blocksVision;
            entry.FindPropertyRelative("interactable").boolValue = interactable;
            entry.FindPropertyRelative("visualScaleMultiplier").vector3Value = SanitizeScale(visualScaleMultiplier);
            var footprint = entry.FindPropertyRelative("footprintOffsets");
            var offsets = HexSparseMapEditorSession.NormalizeFootprintOffsets(footprintOffsets);
            footprint.arraySize = offsets.Count;
            for (var i = 0; i < offsets.Count; i++)
            {
                var item = footprint.GetArrayElementAtIndex(i);
                item.FindPropertyRelative("columnOffset").intValue = offsets[i].Q;
                item.FindPropertyRelative("rowOffset").intValue = offsets[i].R;
            }
        }

        private static int FindEntryIndex(SerializedProperty entries, string objectRef)
        {
            for (var i = 0; i < entries.arraySize; i++)
            {
                var entry = entries.GetArrayElementAtIndex(i);
                if (string.Equals(entry.FindPropertyRelative("objectRef").stringValue, objectRef, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool TryLoadCatalogSet(out MapObjectCatalogSet catalogSet)
        {
            catalogSet = MapObjectCatalogSet.LoadDefault();
            return catalogSet != null;
        }

        private static Vector3 SanitizeScale(Vector3 value)
        {
            return new Vector3(
                value.x > 0f ? value.x : 1f,
                value.y > 0f ? value.y : 1f,
                value.z > 0f ? value.z : 1f);
        }

        public sealed class ObjectDefinition
        {
            private ObjectDefinition(string objectRef, GameObject prefab, HexMapObjectType objectType, bool blocksMovement, bool blocksVision, bool interactable, IReadOnlyList<HexCoord> footprintOffsets, Vector3 visualScaleMultiplier, string category = null)
            {
                ObjectRef = objectRef ?? string.Empty;
                Prefab = prefab;
                ObjectType = objectType;
                BlocksMovement = blocksMovement;
                BlocksVision = blocksVision;
                Interactable = interactable;
                FootprintOffsets = HexSparseMapEditorSession.NormalizeFootprintOffsets(footprintOffsets);
                VisualScaleMultiplier = SanitizeScale(visualScaleMultiplier);
                Category = string.IsNullOrWhiteSpace(category) ? objectType.ToString() : category.Trim();
            }

            public string ObjectRef { get; }
            public GameObject Prefab { get; }
            public HexMapObjectType ObjectType { get; }
            /// <summary>Editor-only palette grouping label (defaults to the object type name).</summary>
            public string Category { get; }
            public bool BlocksMovement { get; }
            public bool BlocksVision { get; }
            public bool Interactable { get; }
            public IReadOnlyList<HexCoord> FootprintOffsets { get; }
            public Vector3 VisualScaleMultiplier { get; }

            public static ObjectDefinition FromPrefab(string objectRef, GameObject prefab)
            {
                return new ObjectDefinition(
                    objectRef,
                    prefab,
                    HexMapObjectType.Building,
                    HexMapObjectTypeDefaults.BlocksMovement(HexMapObjectType.Building),
                    HexMapObjectTypeDefaults.BlocksVision(HexMapObjectType.Building),
                    HexMapObjectTypeDefaults.Interactable(HexMapObjectType.Building),
                    new[] { new HexCoord(0, 0) },
                    Vector3.one);
            }

            public static ObjectDefinition FromCatalogEntry(RuntimeMapObjectPrefabCatalog.Entry entry, string category = null)
            {
                return new ObjectDefinition(
                    entry.ObjectRef,
                    entry.Prefab,
                    entry.ObjectType,
                    entry.BlocksMovement,
                    entry.BlocksVision,
                    entry.Interactable,
                    entry.FootprintOffsets,
                    entry.VisualScaleMultiplier,
                    category);
            }

            public static ObjectDefinition FromMonsterCatalogEntry(MonsterCatalogEntry entry, GameObject prefab)
            {
                return new ObjectDefinition(
                    entry.Id,
                    prefab,
                    HexMapObjectType.MonsterSpawn,
                    HexMapObjectTypeDefaults.BlocksMovement(HexMapObjectType.MonsterSpawn),
                    HexMapObjectTypeDefaults.BlocksVision(HexMapObjectType.MonsterSpawn),
                    HexMapObjectTypeDefaults.Interactable(HexMapObjectType.MonsterSpawn),
                    new[] { new HexCoord(0, 0) },
                    Vector3.one);
            }
        }
    }

}

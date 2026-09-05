using System.IO;
using System.Linq;
using SeoulPlayup.Map.Unity;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.MapDesign.Editor
{
    /// <summary>
    /// Auto-registers placeable object prefabs into <see cref="MapObjectCatalogSet"/> when they are dropped
    /// under <c>Assets/Prefabs/Object/&lt;Category&gt;/</c>, so a designer only has to copy a prefab into the
    /// right category subfolder — no manual catalog editing — for it to appear in the Sparse Map Editor
    /// palette AND resolve at runtime. (Runtime resolution is catalog-only, and Unity strips unreferenced
    /// prefabs from player builds; the catalog entry is what actually ships the prefab. A plain folder copy
    /// with no catalog entry would place in the editor but vanish in a build — hence this.)
    ///
    /// The category subfolder disambiguates catalogs that share a runtime object type (Building vs Prop are
    /// both type 2) by matching the folder name to each typed catalog's <c>CategoryLabel</c>. Prefabs sitting
    /// directly under <c>Assets/Prefabs/Object/</c> (no category subfolder) are left alone — those are the
    /// existing hand-authored entries.
    ///
    /// Deletions/renames only warn (the entry is left in place by design); duplicate objectRefs across
    /// catalogs are refused rather than silently split.
    /// </summary>
    public sealed class MapObjectVariantAutoRegistrar : AssetPostprocessor
    {
        private const string WatchRoot = "Assets/Prefabs/Object/";
        private const string LogPrefix = "[MapObjectAutoRegister]";

        private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            var incoming = importedAssets.Concat(movedAssets)
                .Where(path => IsCategorizedObjectPrefab(path, out _, out _))
                .ToArray();
            var outgoing = deletedAssets.Concat(movedFromAssetPaths)
                .Where(path => IsCategorizedObjectPrefab(path, out _, out _))
                .ToArray();

            if (incoming.Length == 0 && outgoing.Length == 0)
            {
                return;
            }

            var catalogSet = MapObjectCatalogSet.LoadDefault();
            if (catalogSet == null)
            {
                if (incoming.Length > 0)
                {
                    Debug.LogWarning($"{LogPrefix} Could not load the catalog set at {MapObjectCatalogSet.DefaultCatalogAssetPath}; skipped auto-registration for {incoming.Length} prefab(s).");
                }

                return;
            }

            var changed = false;
            foreach (var path in incoming)
            {
                IsCategorizedObjectPrefab(path, out var category, out var objectRef);

                var typedCatalog = MapObjectPrefabCatalog.ResolveCatalogByCategory(catalogSet, category);
                if (typedCatalog == null)
                {
                    Debug.LogWarning($"{LogPrefix} No typed catalog has category '{category}' for prefab '{objectRef}' ({path}). " +
                                     $"Create a MapObjectTypedCatalog whose CategoryLabel is '{category}', or move the prefab under a recognized category folder.");
                    continue;
                }

                // Refuse a cross-catalog duplicate objectRef (the set requires globally-unique refs).
                if (!typedCatalog.TryFindEntry(objectRef, out _) && catalogSet.TryFindEntry(objectRef, out _))
                {
                    Debug.LogWarning($"{LogPrefix} '{objectRef}' already exists in another catalog — skipping to avoid a duplicate objectRef. Rename the prefab or place it under the owning category.");
                    continue;
                }

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    continue;
                }

                if (MapObjectPrefabCatalog.AutoRegister(typedCatalog, objectRef, prefab, out var message))
                {
                    Debug.Log($"{LogPrefix} {message} ({path}).");
                    changed = true;
                }
            }

            foreach (var path in outgoing)
            {
                IsCategorizedObjectPrefab(path, out _, out var objectRef);
                if (catalogSet.TryFindEntry(objectRef, out _))
                {
                    Debug.LogWarning($"{LogPrefix} Prefab '{objectRef}' was removed/moved but its catalog entry remains (left in place by design). Delete the entry manually if it is no longer used.");
                }
            }

            if (changed)
            {
                // Defer the write until the current import batch settles to avoid re-entrant imports.
                EditorApplication.delayCall += AssetDatabase.SaveAssets;
            }
        }

        /// <summary>
        /// True when <paramref name="path"/> is a prefab directly categorized under the watch root, i.e.
        /// <c>Assets/Prefabs/Object/&lt;Category&gt;/.../&lt;name&gt;.prefab</c>. The first segment below the
        /// root is the category; the file name (no extension) is the objectRef. Prefabs sitting directly in
        /// the watch root (no category segment) are not matched.
        /// </summary>
        private static bool IsCategorizedObjectPrefab(string path, out string category, out string objectRef)
        {
            category = null;
            objectRef = null;
            if (string.IsNullOrEmpty(path) ||
                !path.StartsWith(WatchRoot, System.StringComparison.OrdinalIgnoreCase) ||
                !path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var relative = path.Substring(WatchRoot.Length);
            var segments = relative.Split('/');
            if (segments.Length < 2)
            {
                return false; // directly under the watch root — legacy/manual, leave alone
            }

            category = segments[0];
            objectRef = Path.GetFileNameWithoutExtension(path);
            return !string.IsNullOrWhiteSpace(category) && !string.IsNullOrWhiteSpace(objectRef);
        }
    }
}

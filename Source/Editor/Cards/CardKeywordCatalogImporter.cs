using System.Collections.Generic;
using System.IO;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.Combat.Editor
{
    /// <summary>
    /// Bakes the designer-authored game_keywords.csv (the SOT) into the build-safe Resources
    /// <see cref="CardKeywordCatalog"/> ScriptableObject. The CSV stays the single source of truth;
    /// this just projects it where the runtime can load it.
    /// </summary>
    public static class CardKeywordCatalogImporter
    {
        private const string AssetPath = "Assets/Resources/" + CardKeywordCatalog.DefaultResourcesPath + ".asset";

        [MenuItem("Seoul Playup/Combat/Bake Game Keyword Catalog")]
        public static void Bake()
        {
            var catalog = KeywordCatalogCsv.ConvertFile(CombatCsvPaths.GameKeywordsCsv);

            var entries = new List<CardKeywordCatalog.Entry>();
            foreach (var def in catalog.Entries)
            {
                entries.Add(new CardKeywordCatalog.Entry
                {
                    category = def.Category,
                    keyword = def.Keyword,
                    effect = def.Effect,
                    valueKind = def.ValueKind,
                    value = def.Value,
                });
            }

            var directory = Path.GetDirectoryName(AssetPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var asset = AssetDatabase.LoadAssetAtPath<CardKeywordCatalog>(AssetPath);
            var created = asset == null;
            if (created)
            {
                asset = ScriptableObject.CreateInstance<CardKeywordCatalog>();
            }

            asset.SetEntries(entries);

            if (created)
            {
                AssetDatabase.CreateAsset(asset, AssetPath);
            }
            else
            {
                EditorUtility.SetDirty(asset);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Baked {entries.Count} game keywords into {AssetPath}.");
        }
    }
}

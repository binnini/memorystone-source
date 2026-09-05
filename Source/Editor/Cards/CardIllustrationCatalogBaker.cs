using System;
using System.IO;
using System.Linq;
using SeoulPlayup.Combat.Unity;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.Cards.Editor
{
    /// <summary>
    /// Bakes every sprite under <see cref="IllustrationFolder"/> into the
    /// <see cref="CardIllustrationCatalog"/> asset that ships through <c>Resources/</c>.
    /// Run after adding or renaming card illustration textures.
    /// </summary>
    public static class CardIllustrationCatalogBaker
    {
        public const string IllustrationFolder = "Assets/Art/UI/Cards/Illust";

        [MenuItem("Tools/Cards/Bake Card Illustration Catalog")]
        public static void Bake()
        {
            if (!AssetDatabase.IsValidFolder(IllustrationFolder))
            {
                Debug.LogError($"Card illustration folder not found: {IllustrationFolder}");
                return;
            }

            var sprites = AssetDatabase.FindAssets("t:Sprite", new[] { IllustrationFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
                .Select(AssetDatabase.LoadAssetAtPath<Sprite>)
                .Where(sprite => sprite != null)
                .ToArray();

            var catalog = AssetDatabase.LoadAssetAtPath<CardIllustrationCatalog>(CardIllustrationCatalog.DefaultCatalogAssetPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<CardIllustrationCatalog>();
                AssetDatabase.CreateAsset(catalog, CardIllustrationCatalog.DefaultCatalogAssetPath);
            }

            catalog.ConfigureForTests(sprites);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"Baked {sprites.Length} card illustration sprites into {CardIllustrationCatalog.DefaultCatalogAssetPath}.");
        }
    }
}

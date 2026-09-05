using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Card illustration sprites resolved by illustrationId (= sprite/file name, e.g.
    /// "card_illust_A01"). The textures live outside <c>Resources/</c>
    /// (<c>Assets/Art/UI/Cards/Illust/</c>); only this small catalog asset is loaded through
    /// <c>Resources.Load</c>, so the sprites ship as direct references instead of forcing the whole
    /// illustration folder into the build (리팩토링 6-2 Resources 다이어트). Rebake via
    /// <c>Tools/Cards/Bake Card Illustration Catalog</c> after adding or renaming illustrations.
    /// </summary>
    [CreateAssetMenu(menuName = "Seoul Playup/Cards/Card Illustration Catalog")]
    public sealed class CardIllustrationCatalog : ScriptableObject
    {
        public const string DefaultResourcesPath = "Cards/CardIllustrationCatalog";
        public const string DefaultCatalogAssetPath = "Assets/Resources/Cards/CardIllustrationCatalog.asset";

        [SerializeField] private List<Sprite> sprites = new List<Sprite>();

        private Dictionary<string, Sprite> byId;
        private int cachedSpriteCount = -1;

        public IReadOnlyList<Sprite> Sprites =>
            sprites == null ? Array.Empty<Sprite>() : sprites.Where(sprite => sprite != null).ToArray();

        public void ConfigureForTests(IEnumerable<Sprite> sprites)
        {
            this.sprites = sprites == null ? new List<Sprite>() : new List<Sprite>(sprites);
            byId = null;
            cachedSpriteCount = -1;
        }

        public bool TryResolve(string illustrationId, out Sprite sprite)
        {
            sprite = null;
            if (string.IsNullOrWhiteSpace(illustrationId))
            {
                return false;
            }

            EnsureCache();
            return byId.TryGetValue(illustrationId.Trim(), out sprite) && sprite != null;
        }

        public static Sprite ResolveDefault(string illustrationId)
        {
            var catalog = LoadDefault();
            return catalog != null && catalog.TryResolve(illustrationId, out var sprite) ? sprite : null;
        }

        public static CardIllustrationCatalog LoadDefault()
        {
            var catalog = Resources.Load<CardIllustrationCatalog>(DefaultResourcesPath);
            if (catalog != null)
            {
                return catalog;
            }

#if UNITY_EDITOR
            return AssetDatabase.LoadAssetAtPath<CardIllustrationCatalog>(DefaultCatalogAssetPath);
#else
            return null;
#endif
        }

        private void EnsureCache()
        {
            var count = sprites == null ? 0 : sprites.Count;
            if (byId != null && cachedSpriteCount == count)
            {
                return;
            }

            byId = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
            if (sprites != null)
            {
                foreach (var sprite in sprites)
                {
                    if (sprite == null || string.IsNullOrWhiteSpace(sprite.name))
                    {
                        continue;
                    }

                    var key = sprite.name.Trim();
                    if (!byId.ContainsKey(key))
                    {
                        byId[key] = sprite;
                    }
                }
            }

            cachedSpriteCount = count;
        }
    }
}

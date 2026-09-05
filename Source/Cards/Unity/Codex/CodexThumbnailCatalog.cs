using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SeoulPlayup.Codex
{
    /// <summary>
    /// 도감 1층(전용 아트) 해소기 — 항목 id로 구운 썸네일 스프라이트를 찾는다
    /// (<c>docs/codex-plan.md</c> §4.3). P5 이전에는 <b>이 경로가 존재하지 않아</b> 다섯 도메인이
    /// <c>CodexThumbnail.Resolve(null, …)</c>로 2층 폴백에 고정돼 있었다.
    /// <para>
    /// 🔴 스프라이트는 <c>Resources/</c> 밖(<see cref="ThumbnailFolder"/>)에 둔다. <c>Resources.Load</c>로
    /// 얻는 것은 이 작은 카탈로그 에셋 하나뿐이고 스프라이트는 그 에셋의 <b>직접 참조</b>로 실린다 —
    /// 안 그러면 썸네일 폴더 전체가 빌드에 딸려 온다(리팩토링 6-2 "Resources 다이어트").
    /// <see cref="Combat.Unity.CardIllustrationCatalog"/>가 같은 규약을 지킨다.
    /// </para>
    /// <para>
    /// 파일 이름 규약은 <c>codex_thumb_{항목id}</c>다. 도메인을 가리지 않으므로 P6 오브젝트·P7 발주
    /// 아트도 파일만 폴더에 넣고 다시 구우면 같은 경로로 화면에 뜬다.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "Seoul Playup/Codex/Codex Thumbnail Catalog")]
    public sealed class CodexThumbnailCatalog : ScriptableObject
    {
        /// <summary>구운 PNG가 사는 곳. <c>Resources/</c> 밖이어야 한다(위 주석).</summary>
        public const string ThumbnailFolder = "Assets/Art/UI/Codex/Thumbnails";

        public const string KeyPrefix = "codex_thumb_";
        public const string DefaultResourcesPath = "Codex/CodexThumbnailCatalog";
        public const string DefaultCatalogAssetPath = "Assets/Resources/Codex/CodexThumbnailCatalog.asset";

        [SerializeField] private List<Sprite> sprites = new List<Sprite>();

        private Dictionary<string, Sprite> byId;
        private int cachedSpriteCount = -1;

        public IReadOnlyList<Sprite> Sprites =>
            sprites == null ? Array.Empty<Sprite>() : sprites.Where(sprite => sprite != null).ToArray();

        /// <summary>항목 id에 붙는 스프라이트 파일 이름. 베이커와 해소기가 <b>같은 함수</b>를 봐야 한다.</summary>
        public static string KeyFor(string entryId) =>
            string.IsNullOrWhiteSpace(entryId) ? null : KeyPrefix + entryId.Trim();

        public void ConfigureForTests(IEnumerable<Sprite> sprites)
        {
            this.sprites = sprites == null ? new List<Sprite>() : new List<Sprite>(sprites);
            byId = null;
            cachedSpriteCount = -1;
        }

        /// <summary>
        /// 항목 id로 전용 아트를 찾는다. 없으면 <see langword="null"/> —
        /// 그 경우 <see cref="CodexThumbnail.Resolve"/>가 2층(이름 전체)으로 내려간다.
        /// </summary>
        public Sprite Resolve(string entryId)
        {
            var key = KeyFor(entryId);
            if (key == null)
            {
                return null;
            }

            EnsureCache();
            return byId.TryGetValue(key, out var sprite) ? sprite : null;
        }

        public bool TryResolve(string entryId, out Sprite sprite)
        {
            sprite = Resolve(entryId);
            return sprite != null;
        }

        /// <summary>
        /// ⚠️ 빌드에서 <see langword="null"/>이 될 수 있는 경로다(<c>TrapPresetCatalog.LoadDefault()</c>에서
        /// 기실측). 출하 경로는 <c>LobbyController</c>가 직렬화 참조로 물려주는 쪽이고,
        /// 이것은 그 참조가 비었을 때의 <b>보조</b>일 뿐이다.
        /// </summary>
        public static CodexThumbnailCatalog LoadDefault()
        {
            var catalog = Resources.Load<CodexThumbnailCatalog>(DefaultResourcesPath);
            if (catalog != null)
            {
                return catalog;
            }

#if UNITY_EDITOR
            return AssetDatabase.LoadAssetAtPath<CodexThumbnailCatalog>(DefaultCatalogAssetPath);
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

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SeoulPlayup.Cards.Unity
{
    /// <summary>
    /// 런타임 생성 UI(잡화점·캠핑카/공작소 팝업·덱 목록·선택지 슬롯)가 쓰는 아트 에셋의
    /// <b>빌드 안전 정본</b>. 이 뷰들은 씬에 저작되지 않고 FindOrCreate/AddComponent로
    /// 만들어지므로 SerializeField가 채워질 표면이 없고, 기존의 <c>AssetDatabase</c> 폴백은
    /// 에디터 전용이라 <b>빌드에서는 전부 null이었다</b>(2026-08-19 실플레이 #16 — 서비스
    /// 배경 미표시. cs:1175가 카탈로그 3종에서 고친 것과 같은 지뢰의 UI 판).
    ///
    /// <para>계약: 이 에셋은 <c>Assets/Resources/Combat/RuntimeUiAssetCatalog.asset</c>에
    /// 산다(Resources 직하 = 빌드 포함 보장). 뷰들은 자기 SerializeField가 비면 여기서
    /// 읽는다 — 저작을 한 곳에 모아 "빌드에 실을 때 저작 루트에 참조를 채운다"는 지켜질 수
    /// 없는 약속을 대체한다. 필드 전량 채움은 EditMode 게이트가 지킨다.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Seoul Playup/Runtime UI Asset Catalog", fileName = "RuntimeUiAssetCatalog")]
    public sealed class RuntimeUiAssetCatalog : ScriptableObject
    {
        public const string ResourcesPath = "Combat/RuntimeUiAssetCatalog";
        public const string AssetPath = "Assets/Resources/Combat/RuntimeUiAssetCatalog.asset";

        [Header("카드 프레임 (덱 목록·상점 진열·연마 비교가 공유)")]
        [SerializeField] private GameObject moveCardFrontPrefab;
        [SerializeField] private GameObject actionCardFrontPrefab;
        [SerializeField] private Sprite statusCardFrameSprite;

        [Header("서비스 배경 일러 (스타일 보드 67fe1d93 채택본)")]
        [SerializeField] private Sprite shopBackdropSprite;
        [SerializeField] private Sprite camperBackdropSprite;
        [SerializeField] private Sprite workshopBackdropSprite;

        [Header("서비스 아이콘·일러 버튼")]
        [SerializeField] private Sprite healOptionSprite;
        [SerializeField] private Sprite refineOptionSprite;
        [SerializeField] private Sprite removeOptionSprite;
        [SerializeField] private Sprite coinLightSprite;
        [SerializeField] private Sprite relicGoodsSprite;
        [Tooltip("부적 뒷면(card_frame_back). 전리품 목록의 「부적 추가」 줄이 쓴다 — 그 줄만 물건이 아니라 «부적 한 장»을 가리킨다.")]
        /// <summary>
        /// 체력 하트(<c>Assets/Art/UI/Icons/ui_health.png</c>) — 하단 HUD 체력 칸이 쓰는 그 그림이다
        /// (2026-09-01 #11 사용자 지정). 캠핑카 회복 선택지가 같은 그림을 써야 「체력」이 화면마다
        /// 다른 물건으로 읽히지 않는다.
        /// </summary>
        [SerializeField] private Sprite healthIconSprite;

        [SerializeField] private Sprite cardBackSprite;

        [Header("유물·소모품 아이콘 (relics.csv·consumable_items.csv의 iconId → 스프라이트)")]
        [Tooltip("Tools/UI/Bake Relic & Consumable Icons 로 굽는다. 손으로 채우지 말 것.")]
        [SerializeField] private List<Sprite> itemIcons = new List<Sprite>();

        public GameObject MoveCardFrontPrefab => moveCardFrontPrefab;
        public GameObject ActionCardFrontPrefab => actionCardFrontPrefab;
        public Sprite StatusCardFrameSprite => statusCardFrameSprite;
        public Sprite ShopBackdropSprite => shopBackdropSprite;
        public Sprite CamperBackdropSprite => camperBackdropSprite;
        public Sprite WorkshopBackdropSprite => workshopBackdropSprite;
        public Sprite HealOptionSprite => healOptionSprite;
        public Sprite RefineOptionSprite => refineOptionSprite;
        public Sprite RemoveOptionSprite => removeOptionSprite;
        public Sprite CoinLightSprite => coinLightSprite;
        public Sprite RelicGoodsSprite => relicGoodsSprite;

        /// <summary>부적 뒷면. 전리품 목록의 「부적 추가」 줄 표식이다.</summary>
        public Sprite HealthIconSprite => healthIconSprite;

        public Sprite CardBackSprite => cardBackSprite;

        /// <summary>구워진 유물·소모품 아이콘 전량(null 슬롯 제외). 게이트 테스트가 이걸 센다.</summary>
        public IReadOnlyList<Sprite> ItemIcons =>
            itemIcons == null ? Array.Empty<Sprite>() : itemIcons.Where(sprite => sprite != null).ToArray();

        private Dictionary<string, Sprite> itemIconsById;
        private int cachedItemIconCount = -1;

        private static RuntimeUiAssetCatalog cached;

        /// <summary>
        /// Resources 우선(빌드·에디터 공통), 에디터에선 AssetDatabase 폴백(TrapPresetCatalog.
        /// LoadDefault와 같은 계약). 없으면 null — 소비자는 기존 에디터 폴백/무표시로 떨어진다.
        /// </summary>
        public static RuntimeUiAssetCatalog LoadDefault()
        {
            if (cached != null)
            {
                return cached;
            }

            cached = Resources.Load<RuntimeUiAssetCatalog>(ResourcesPath);
#if UNITY_EDITOR
            if (cached == null)
            {
                cached = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeUiAssetCatalog>(AssetPath);
            }
#endif
            return cached;
        }

        /// <summary>도메인 리로드 없이 에셋을 갈아끼우는 테스트·에디터 도구용 캐시 무효화.</summary>
        public static void InvalidateCache()
        {
            cached = null;
        }

        /// <summary>
        /// <c>iconId</c>(예: <c>relic_icon_baton</c>)로 아이콘을 찾는다. 스프라이트 <b>파일 이름</b>이 키다 —
        /// 그래서 CSV에 행을 더하고 같은 이름의 PNG를 폴더에 넣고 다시 구우면 그것으로 끝이다.
        /// 없으면 <see langword="null"/>이고, 소비자는 각자의 현행 폴백(칩 2글자·복주머니·이름 전체)으로
        /// 떨어진다 — 아트가 없는 항목이 생겨도 화면이 깨지지 않는 것이 이 계약이다.
        /// </summary>
        public Sprite ResolveItemIcon(string iconId)
        {
            if (string.IsNullOrWhiteSpace(iconId))
            {
                return null;
            }

            EnsureItemIconCache();
            return itemIconsById.TryGetValue(iconId.Trim(), out var sprite) ? sprite : null;
        }

        /// <summary>
        /// 세 소비처(사이드바 칩 · 잡화점 잡화 타일 · 도감 썸네일)가 공유하는 <b>단 하나의 해소 통로</b>.
        /// 🔴 <c>AssetDatabase</c> 폴백을 여기에 새로 붙이지 말 것 — 에디터에서만 살아 있고 빌드에서
        /// 전부 null이 되는 지뢰다(cs:1175·cs:1178 선례). 카탈로그가 비면 폴백이 정답이다.
        /// </summary>
        public static Sprite LoadItemIcon(string iconId)
        {
            var catalog = LoadDefault();
            return catalog != null ? catalog.ResolveItemIcon(iconId) : null;
        }

        /// <summary>도메인 리로드 없이 아이콘 목록을 갈아끼우는 테스트·베이커용 통로.</summary>
        public void ConfigureItemIconsForTests(IEnumerable<Sprite> sprites)
        {
            itemIcons = sprites == null ? new List<Sprite>() : new List<Sprite>(sprites);
            itemIconsById = null;
            cachedItemIconCount = -1;
        }

        private void EnsureItemIconCache()
        {
            var count = itemIcons == null ? 0 : itemIcons.Count;
            if (itemIconsById != null && cachedItemIconCount == count)
            {
                return;
            }

            itemIconsById = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
            if (itemIcons != null)
            {
                foreach (var sprite in itemIcons)
                {
                    if (sprite == null || string.IsNullOrWhiteSpace(sprite.name))
                    {
                        continue;
                    }

                    var key = sprite.name.Trim();
                    if (!itemIconsById.ContainsKey(key))
                    {
                        itemIconsById[key] = sprite;
                    }
                }
            }

            cachedItemIconCount = count;
        }
    }
}

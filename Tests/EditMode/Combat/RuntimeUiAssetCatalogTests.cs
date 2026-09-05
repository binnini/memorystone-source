#if UNITY_EDITOR
using NUnit.Framework;
using SeoulPlayup.Cards.Unity;
using UnityEditor;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 런타임 생성 UI(잡화점·캠핑카/공작소·덱 목록·선택지 슬롯)의 빌드 안전 정본
    /// <see cref="RuntimeUiAssetCatalog"/> 저작 유실 게이트. 이 뷰들은 씬에 저작되지 않아
    /// SerializeField가 영원히 비고, 예전 AssetDatabase-전용 폴백은 빌드에서 전부 null이었다
    /// (2026-08-19 실플레이 #16 — 서비스 배경 미표시. cs:1175 카탈로그 3종과 같은 지뢰).
    /// 에디터에서는 폴백이 가려 절대 안 드러나는 종류라, 여기서 못을 박는다.
    /// </summary>
    public sealed class RuntimeUiAssetCatalogTests
    {
        [Test]
        public void CatalogLivesInResourcesAndEveryFieldIsAuthored()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<RuntimeUiAssetCatalog>(RuntimeUiAssetCatalog.AssetPath);
            Assert.That(catalog, Is.Not.Null,
                $"빌드 안전 정본이 사라졌다 — {RuntimeUiAssetCatalog.AssetPath}");

            Assert.That(catalog.MoveCardFrontPrefab, Is.Not.Null, "moveCardFrontPrefab 미저작 — 빌드에서 카드 진열이 빈다.");
            Assert.That(catalog.ActionCardFrontPrefab, Is.Not.Null, "actionCardFrontPrefab 미저작.");
            Assert.That(catalog.StatusCardFrameSprite, Is.Not.Null, "statusCardFrameSprite 미저작.");
            Assert.That(catalog.ShopBackdropSprite, Is.Not.Null, "잡화점 배경 미저작 — 빌드에서 배경이 빈다(#16).");
            Assert.That(catalog.CamperBackdropSprite, Is.Not.Null, "캠핑카 배경 미저작(#16).");
            Assert.That(catalog.WorkshopBackdropSprite, Is.Not.Null, "공작소 배경 미저작(#16).");
            Assert.That(catalog.HealOptionSprite, Is.Not.Null, "회복 일러 버튼 미저작.");
            Assert.That(catalog.RefineOptionSprite, Is.Not.Null, "연마 일러 버튼 미저작.");
            Assert.That(catalog.RemoveOptionSprite, Is.Not.Null, "제거(향로) 일러 버튼 미저작.");
            Assert.That(catalog.CoinLightSprite, Is.Not.Null, "엽전 글리프 미저작.");
            Assert.That(catalog.RelicGoodsSprite, Is.Not.Null, "유물 꾸러미 일러 미저작.");
            Assert.That(catalog.CardBackSprite, Is.Not.Null, "부적 뒷면 미저작 — 전리품 목록의 「부적 추가」 줄이 빈다.");
        }

        [Test]
        public void LoadDefaultResolvesThroughTheSameResourcesPathABuildUses()
        {
            // Resources.Load 경로가 깨지면 에디터는 AssetDatabase 폴백이 가려 준다 — 빌드가 쓰는
            // 경로 그대로를 따로 검증해야 "에디터에선 안 드러나는" 회귀를 잡는다.
            RuntimeUiAssetCatalog.InvalidateCache();
            try
            {
                Assert.That(
                    UnityEngine.Resources.Load<RuntimeUiAssetCatalog>(RuntimeUiAssetCatalog.ResourcesPath),
                    Is.Not.Null,
                    "Resources 경로 해소 실패 — 에셋이 Resources 밖으로 옮겨졌거나 이름이 바뀌었다.");
            }
            finally
            {
                RuntimeUiAssetCatalog.InvalidateCache();
            }
        }
    }
}
#endif

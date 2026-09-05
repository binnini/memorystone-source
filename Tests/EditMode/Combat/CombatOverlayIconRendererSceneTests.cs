#if UNITY_EDITOR
using System.Linq;
using System.IO;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class CombatOverlayIconRendererSceneTests
    {
        private const string LegacyStatusIconOverlayPath = "Assets/Scripts/Combat/Unity/MonsterAttackStatusIconOverlay.cs";

        private static readonly string[] ScenePaths =
        {
            TestAssetPaths.PrototypeTestScene
        };

        /// <summary>
        /// 아직 전용 아이콘 아트가 없는 상태이상. 상태이상 아이콘 9종은 이미지 생성 파이프라인
        /// (docs/image-gen-openai-pipeline-plan.md)으로 저작되는 유료 자산이라 코드 페이즈에서 같이 만들 수 없다.
        /// 스프라이트가 없으면 <c>StatusEffectIconCatalog</c> 계약대로 글리프+색 폴백으로 렌더된다.
        /// 여기 실려 있으면 "없어야 정상"이 되므로, 아트가 들어오는 순간 이 테스트가 먼저 실패해서
        /// 목록을 지우게 만든다 — 조용히 미저작으로 남는 경로가 없다.
        /// </summary>
        /// <remarks>
        /// 2026-08-02 아이콘 15종 리모델링이 끝나면서 <b>목록이 비었다.</b> 이제 BossAura를 뺀 모든
        /// kind가 아래 전수 커버 단언의 대상이다. 목록은 지우지 않고 빈 채로 남긴다 — 신규 kind가
        /// 생기면 아트가 오기 전까지 여기 넣어야 "미저작인데 조용히 통과"가 막힌다.
        /// </remarks>
        // 2026-08-05 힘(Might)·미지(Unknown) 아트가 들어와 카탈로그(kind 15·16)에 배선되면서
        // 목록이 다시 비었다. 목록은 지우지 않고 빈 채로 남긴다 — 신규 kind가 생기면 아트가
        // 오기 전까지 여기 넣어야 "미저작인데 조용히 통과"가 막힌다.
        // 2026-09-02 무적 아트(status_invincible.png · r2-wings-gold)가 들어와 카탈로그
        // kind 19에 배선되면서 목록이 다시 비었다. 목록은 지우지 않고 빈 채로 남긴다 —
        // 신규 kind가 생기면 아트가 오기 전까지 여기 넣어야 "미저작인데 조용히 통과"가 막힌다.
        private static readonly StatusEffectKind[] PendingArtStatusKinds =
        {
        };

        [Test]
        public void PrototypeSceneUsesPresentationDrivenStatusIconRendererWithoutLegacyScript()
        {
            Assert.That(File.Exists(LegacyStatusIconOverlayPath), Is.False,
                "The legacy MonsterAttackStatusIconOverlay script file should be deleted after Step 6.");
            Assert.That(AssetDatabase.LoadAssetAtPath<MonoScript>(LegacyStatusIconOverlayPath), Is.Null,
                "The legacy MonsterAttackStatusIconOverlay script asset should no longer load after Step 6.");

            var previousScene = EditorSceneManager.GetActiveScene();
            foreach (var scenePath in ScenePaths)
            {
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                try
                {
                    var roots = scene.GetRootGameObjects();
                    Assert.That(CountMissingScripts(roots), Is.Zero, $"{scenePath} should not contain missing scripts.");

                    var controller = FindSceneComponent<MapCombatController>(roots);
                    var atlasView = FindSceneComponent<AtlasTilePresentationView>(roots);
                    var renderer = FindSceneComponent<StatusIconOverlayRenderer>(roots);

                    Assert.That(controller, Is.Not.Null, $"{scenePath} should keep its MapCombatController.");
                    Assert.That(atlasView, Is.Not.Null, $"{scenePath} should keep its AtlasTilePresentationView.");
                    Assert.That(renderer, Is.Not.Null, $"{scenePath} should contain the new icon renderer.");
                    Assert.That(renderer.transform.parent, Is.EqualTo(atlasView.transform),
                        $"{scenePath} should keep the icon renderer isolated under the map presentation root.");
                    Assert.That(renderer.transform.localPosition, Is.EqualTo(Vector3.zero));
                    Assert.That(renderer.transform.localRotation, Is.EqualTo(Quaternion.identity));
                    Assert.That(renderer.transform.localScale, Is.EqualTo(Vector3.one));

                    var controllerSo = new SerializedObject(controller);
                    Assert.That(controllerSo.FindProperty("statusIconOverlayRenderer").objectReferenceValue,
                        Is.EqualTo(renderer), $"{scenePath} should wire the presenter icon renderer seam through the controller.");

                    var rendererSo = new SerializedObject(renderer);
                    // Status icons resolve through the shared StatusEffectIconCatalog first; the renderer's
                    // serialized effectKindSprites array is only a legacy fallback and is no longer authored
                    // in scenes, so the contract is that the wired catalog covers every StatusEffectKind.
                    var iconCatalog = rendererSo.FindProperty("iconCatalog").objectReferenceValue
                        as StatusEffectIconCatalog;
                    Assert.That(iconCatalog,
                        Is.Not.Null, $"{scenePath} should wire the shared status-effect icon catalog.");
                    foreach (StatusEffectKind kind in System.Enum.GetValues(typeof(StatusEffectKind)))
                    {
                        // BossAura는 상태이상 파이프라인을 타지 않는 페이즈 트랙 아우라라 상태 아이콘 렌더러에
                        // 절대 도달하지 않는다(ActiveEffect가 아님 — docs/boss-raid-plan.md §8-2). 그래서 아이콘
                        // 스프라이트 커버리지 대상이 아니다. 나머지 kind는 여전히 전수 커버를 강제한다.
                        if (kind == StatusEffectKind.BossAura)
                        {
                            continue;
                        }

                        if (System.Array.IndexOf(PendingArtStatusKinds, kind) >= 0)
                        {
                            Assert.That(iconCatalog.GetSprite(kind), Is.Null,
                                $"{kind} now has authored art — remove it from PendingArtStatusKinds so the gate covers it again.");
                            continue;
                        }

                        Assert.That(iconCatalog.GetSprite(kind), Is.Not.Null,
                            $"{scenePath} icon catalog should map {kind} to an overlay sprite.");
                    }
                }
                finally
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }

            if (previousScene.IsValid())
            {
                EditorSceneManager.SetActiveScene(previousScene);
            }
        }

        private static T FindSceneComponent<T>(GameObject[] roots) where T : Component
        {
            return roots.SelectMany(root => root.GetComponentsInChildren<T>(true)).SingleOrDefault();
        }

        private static int CountMissingScripts(GameObject[] roots)
        {
            return roots
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Sum(transform => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject));
        }
    }
}
#endif


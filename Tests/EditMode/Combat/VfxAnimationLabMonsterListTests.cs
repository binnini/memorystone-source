using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Unity.Dev;
using SeoulPlayup.Combat.Unity.Dev.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// VfxAnimationLab 씬의 <c>monsterPrefabs</c> 배열이 카탈로그 유도값과 갈라지지 않았는지 잠근다.
    ///
    /// <para>
    /// 🔴 이 게이트가 없으면 드리프트가 <b>조용하다</b>: 랩은 정상적으로 열리고, 없는 몬스터는
    /// 그냥 prev/next 순회에 안 나올 뿐이라 「랩에서 볼 수 없다」는 사실이 아무 데도 적히지 않는다.
    /// 실제로 두 번 갈라졌다 — 터렛 3종은 씬에만 손으로 들어가 빌더 목록엔 없었고(재생성하면 빠진다),
    /// 요괴 6종은 반입 후 아무도 채우지 않아 랩에서 확인할 방법이 없었다(2026-09-03).
    /// </para>
    ///
    /// <para>
    /// 실패하면 <c>Seoul Playup/Dev/Sync VFX Lab Monster List</c> 메뉴를 돌려 씬을 맞춘다
    /// (씬을 통째로 재생성하는 <c>Create VFX Animation Lab Scene</c>이 아니다 — 그쪽은 다른 저작을 날린다).
    /// </para>
    /// </summary>
    public sealed class VfxAnimationLabMonsterListTests
    {
        private const string ScenePath = "Assets/Scenes/Dev/VfxAnimationLab.unity";

        /// <summary>
        /// 에셋 경로는 <b>ASCII로 유지한다</b>. macOS에서 한글 경로는 같은 글자인데 바이트가 갈리고
        /// (CSV 저작은 NFC · <see cref="AssetDatabase.GetAssetPath"/>는 NFD — 두두리 경로가
        /// 55자 vs 58자로 나와 이 게이트가 실제로 물었다), 그래서 요괴 6종의 폴더·파일을
        /// 전부 로마자로 옮겼다(2026-09-03). 한글 경로가 다시 들어오면 이 시험이 먼저 깨진다.
        /// </summary>
        [Test]
        public void ResolvedVisualPathsStayAscii()
        {
            foreach (var path in VfxAnimationLabSceneBuilder.ResolveMonsterVisualPaths())
            {
                Assert.That(
                    path.All(c => c < 128),
                    Is.True,
                    $"에셋 경로에 비ASCII 문자가 있다 — macOS NFC/NFD로 문자열 비교가 깨진다: {path}");
            }
        }

        private static string[] ScenePrefabPaths()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var controller = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<VfxAnimationLabController>(true))
                    .FirstOrDefault();
                Assert.That(controller, Is.Not.Null, $"{ScenePath}에 VfxAnimationLabController가 없다.");

                var array = new SerializedObject(controller).FindProperty("monsterPrefabs");
                Assert.That(array, Is.Not.Null, "monsterPrefabs 필드가 사라졌다 — 랩 저작 표면이 바뀐 것이다.");

                return Enumerable.Range(0, array.arraySize)
                    .Select(i => array.GetArrayElementAtIndex(i).objectReferenceValue)
                    .Select(o => o == null ? "<NULL>" : AssetDatabase.GetAssetPath(o))
                    .ToArray();
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }

        [Test]
        public void ResolvedVisualPathsAreNonEmptyAndAllLoad()
        {
            var resolved = VfxAnimationLabSceneBuilder.ResolveMonsterVisualPaths();

            Assert.That(resolved, Is.Not.Empty, "카탈로그에서 몬스터 시각을 하나도 못 끌어왔다.");
            Assert.That(resolved.Distinct().Count(), Is.EqualTo(resolved.Count), "유도 목록에 중복 경로가 있다.");
            foreach (var path in resolved)
            {
                Assert.That(
                    AssetDatabase.LoadAssetAtPath<GameObject>(path),
                    Is.Not.Null,
                    $"유도된 프리팹을 로드할 수 없다: {path}");
            }
        }

        [Test]
        public void SceneMonsterListMatchesCatalogDerivedList()
        {
            var expected = VfxAnimationLabSceneBuilder.ResolveMonsterVisualPaths().ToArray();
            var actual = ScenePrefabPaths();

            Assert.That(
                actual,
                Is.EqualTo(expected),
                "VfxAnimationLab 씬의 몬스터 목록이 카탈로그 유도값과 다르다 — "
                + "'Seoul Playup/Dev/Sync VFX Lab Monster List'로 맞출 것.\n"
                + $"  씬({actual.Length}): {string.Join(", ", actual.Select(System.IO.Path.GetFileNameWithoutExtension))}\n"
                + $"  유도({expected.Length}): {string.Join(", ", expected.Select(System.IO.Path.GetFileNameWithoutExtension))}");
        }

        [Test]
        public void EveryShippedYogoeIsReachableInTheLab()
        {
            var resolved = VfxAnimationLabSceneBuilder.ResolveMonsterVisualPaths()
                .Select(System.IO.Path.GetFileNameWithoutExtension)
                .ToArray();

            // 랩에서 애니메이션을 볼 수 없는 요괴가 생기면 여기서 먼저 걸린다.
            foreach (var name in new[] { "Duduri", "Eodukshini", "Duokseokini", "Geogugwi", "Geuseunsae", "Yagwanggwi" })
            {
                Assert.That(resolved, Contains.Item(name), $"{name}이 랩 목록에서 빠졌다.");
            }
        }
    }
}

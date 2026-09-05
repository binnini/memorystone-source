#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 손패 부채가 <b>양옆 도크를 파고들지 않는가</b>(2026-09-05 실플레이 G5).
    ///
    /// <para>실측 결함: 손패 9장에서 바깥 카드가 왼쪽 기력 도크와 오른쪽 이동 종료 버튼을 동시에
    /// 덮었다. 8장에서는 7px 차이로 겨우 비켜갔다 — 즉 <b>우연히</b> 안 겹치고 있었을 뿐이고,
    /// 카드 크기·도크 위치·최대 반폭 중 무엇이 조금만 움직여도 다시 겹친다.</para>
    ///
    /// <para>🔑 그래서 값을 고정하지 않고 <b>관계</b>를 잰다: 씬에 저작된 부채 수치와 도크의 실제
    /// 사각형을 읽어 배치 산식을 그대로 다시 돌린다. 도크를 옮기거나 카드를 키우면 여기서 잡힌다.</para>
    /// </summary>
    [Category("ShippingData")]
    public sealed class CardFanSideDockClearanceTests
    {
        private const string ScenePath = "Assets/Scenes/Game/MainGameplay.unity";

        /// <summary>이보다 큰 손패는 반폭이 상한에 붙어 더 넓어지지 않으므로 여기까지면 전부 덮는다.</summary>
        private const int MaxHandToCheck = 14;

        [Test]
        public void CardFanDoesNotOverlapSideDocks()
        {
            var previous = EditorSceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var view = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<GameplayCardLaneView>(true))
                    .SingleOrDefault();
                Assert.That(view, Is.Not.Null, "MainGameplay에 카드 레인 뷰가 있어야 한다.");

                var so = new SerializedObject(view);
                var cardWidth = so.FindProperty("cardFanSize").vector2Value.x;
                var step = so.FindProperty("cardFanStep").floatValue;
                var gap = so.FindProperty("cardFanGroupGap").floatValue;
                var maxHalfSpan = so.FindProperty("cardFanMaxHalfSpan").floatValue;
                var rootX = so.FindProperty("moveCardsRootPosition").vector2Value.x;

                var lane = FindDescendant(scene, "CardLane");
                Assert.That(lane, Is.Not.Null, "CardLane을 찾지 못했다 — 게이트가 빈 채로 통과한다.");

                var energyRight = LocalXRange(lane, FindDescendant(scene, "EnergyDock")).max;
                var endButton = LocalXRange(lane, FindDescendant(scene, "BottomCombatEndActionButton"));

                var failures = new List<string>();
                for (var n = 2; n <= MaxHandToCheck; n++)
                {
                    // LayoutCardFan과 같은 산식. 두 무리(이동·행동)가 함께 있을 때가 가장 넓다.
                    var effectiveStep = Mathf.Min(step, (2f * maxHalfSpan - gap) / (n - 1));
                    var half = ((n - 1) * effectiveStep + gap) * 0.5f;
                    var left = rootX - half - cardWidth * 0.5f;
                    var right = rootX + half + cardWidth * 0.5f;

                    if (left < energyRight)
                    {
                        failures.Add($"손패 {n}장: 왼쪽 카드 끝 {left:0}이 기력 도크 오른쪽 끝 {energyRight:0}을 파고든다.");
                    }

                    if (right > endButton.min)
                    {
                        failures.Add($"손패 {n}장: 오른쪽 카드 끝 {right:0}이 이동 종료 버튼 왼쪽 끝 {endButton.min:0}을 파고든다.");
                    }
                }

                Assert.That(
                    failures, Is.Empty,
                    "부채 반폭(cardFanMaxHalfSpan)을 줄이거나 도크를 옮겨야 한다.\n" + string.Join("\n", failures));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid())
                {
                    EditorSceneManager.SetActiveScene(previous);
                }
            }
        }

        private static RectTransform FindDescendant(Scene scene, string name)
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<RectTransform>(true))
                .FirstOrDefault(rect => string.Equals(rect.gameObject.name, name, System.StringComparison.Ordinal));
        }

        /// <summary>대상 사각형을 <paramref name="lane"/>의 로컬 x 범위로 옮긴다(카드 좌표계와 같은 자).</summary>
        private static (float min, float max) LocalXRange(RectTransform lane, RectTransform target)
        {
            Assert.That(target, Is.Not.Null, "도크 사각형을 찾지 못했다 — 게이트가 빈 채로 통과한다.");
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            var a = lane.InverseTransformPoint(corners[0]).x;
            var b = lane.InverseTransformPoint(corners[2]).x;
            return (Mathf.Min(a, b), Mathf.Max(a, b));
        }
    }
}
#endif

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// 전체 화면 검정 커튼. 보스 조우 연출의 "화면 암전"에 쓴다(계획 §9.7·§12.3).
    ///
    /// 안개와 무관한 <b>별도 채널</b>이라는 점이 핵심이다 — 암시야를 다시 켜 가리는 방식은 MonsterLab 암시야
    /// 상시 OFF와 가시성 단조 계약(§9.6) 둘 다에 걸린다. 커튼은 그 위를 통째로 덮으므로 규칙 상태(가시성)를
    /// 건드리지 않고 화면만 검게 만든다.
    ///
    /// 솔리드 검정이라 저작할 레이아웃이 없어 런타임 생성한다. 알파는 <see cref="CanvasGroup"/>으로 제어하고,
    /// 알파가 0보다 크면 레이캐스트를 막아 암전 중 입력이 새지 않게 한다.
    /// </summary>
    public sealed class CombatScreenFadeCurtain : MonoBehaviour
    {
        private CanvasGroup group;

        /// <summary>지정한 부모 아래에 커튼을 만든다. ScreenSpaceOverlay라 렌더 위치는 부모와 무관하며,
        /// 부모는 수명 관리용일 뿐이다. sortingOrder는 다른 UI 위에 확실히 오도록 크게 잡는다.</summary>
        public static CombatScreenFadeCurtain Create(Transform parent, int sortingOrder = 30000)
        {
            var go = new GameObject("BossEncounterFadeCurtain");
            go.transform.SetParent(parent, false);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            go.AddComponent<CanvasScaler>();
            go.AddComponent<GraphicRaycaster>();

            var curtain = go.AddComponent<CombatScreenFadeCurtain>();
            curtain.group = go.AddComponent<CanvasGroup>();
            curtain.group.alpha = 0f;
            curtain.group.blocksRaycasts = false;
            curtain.group.interactable = false;

            var blackGo = new GameObject("Black", typeof(RectTransform));
            blackGo.transform.SetParent(go.transform, false);
            var rect = (RectTransform)blackGo.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var image = blackGo.AddComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = true;

            return curtain;
        }

        /// <summary>현재 암전 정도(0 = 완전 투명, 1 = 완전 암전).</summary>
        public float Alpha
        {
            get => group != null ? group.alpha : 0f;
            set
            {
                if (group == null)
                {
                    return;
                }

                group.alpha = Mathf.Clamp01(value);
                group.blocksRaycasts = group.alpha > 0.001f;
            }
        }

        /// <summary>
        /// 알파를 <paramref name="target"/>까지 <paramref name="seconds"/>에 걸쳐 이징한다. 시간 간격을
        /// 주입받아 호출자가 시간축을 정한다(시네마틱은 히트스톱·캡처와 겹쳐도 진행하도록 unscaled/캡처
        /// 델타를 넣는다 — 인트로/전환 연출과 같은 계약).
        /// </summary>
        public IEnumerator FadeTo(float target, float seconds, Func<float> deltaTime)
        {
            target = Mathf.Clamp01(target);
            if (seconds <= 0.0001f || deltaTime == null)
            {
                Alpha = target;
                yield break;
            }

            var from = Alpha;
            var elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += deltaTime();
                Alpha = Mathf.Lerp(from, target, Mathf.Clamp01(elapsed / seconds));
                yield return null;
            }

            Alpha = target;
        }
    }
}

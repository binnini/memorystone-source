using System.Collections;
using SeoulPlayup.CardCore;
using SeoulPlayup.Cards.Unity;
using SeoulPlayup.Combat.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// 저주 카드가 덱에 섞이는 순간의 연출(2026-09-01 #9): 카드를 화면 가운데에 <b>한 번 보여주고</b>
    /// 뽑을 더미로 빨아들인다.
    ///
    /// <para>🔴 <b>고치는 것은 「연출이 없다」가 아니라 「원인과 결과가 이어지지 않는다」이다.</b>
    /// 종전에는 <c>TryInjectStatusCard</c>가 조용히 덱만 오염시켜서, 플레이어는 몇 턴 뒤 그 카드를
    /// 뽑고 나서야 무슨 일이 있었는지 알았다. 그래서 이 연출의 핵심은 화려함이 아니라
    /// <b>지금·무엇이</b>를 그 자리에서 보여주는 것이다.</para>
    ///
    /// <para>🔑 새 기구를 만들지 않는다. 카드 세우기는 <see cref="CardFrontListInstance"/>(더미
    /// 오버레이·도감이 쓰는 그 경로), 빨아들이기는 <see cref="CardFlightAnimator.FlyDiscardGhost"/>
    /// (버린 카드가 더미로 빨려드는 그 비행)를 그대로 쓴다 — 카드가 나는 모습이 화면마다 다르면
    /// 플레이어는 「다른 종류의 일」로 읽는다.</para>
    ///
    /// <para>🔴 프리팹·프레임은 <see cref="RuntimeUiAssetCatalog"/>에서 온다. <c>AssetDatabase</c>
    /// 폴백은 에디터에서만 살아 있어 빌드에서 조용히 null이 된다(2026-08-19 #C의 전례).
    /// 무엇 하나라도 없으면 <b>연출만 접고</b> 규칙은 그대로 간다 — 전리품 비행과 같은 계약이다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CurseCardInjectionPresenter : MonoBehaviour
    {
        /// <summary>가운데에 세워 두는 시간. 「무엇이 들었는지」를 읽을 수 있어야 하되 턴을 끌면 안 된다.</summary>
        private const float HoldSeconds = 0.75f;

        /// <summary>등장(작게 → 제 크기) 시간.</summary>
        private const float PopSeconds = 0.18f;

        /// <summary>가운데에 세울 때의 카드 크기 배율 — 손패보다 조금 크게 세워 시선을 잡는다.</summary>
        private const float CenterScale = 1.15f;

        private CardFlightAnimator flightAnimator;
        private RectTransform drawPileDock;

        /// <summary>
        /// 비행 기구와 목적지를 물린다. 매 갱신마다 다시 불려도 싸다 —
        /// 하단 HUD는 런타임에 생성되므로 처음에는 둘 다 null일 수 있다.
        /// </summary>
        public void Configure(CardFlightAnimator animator, RectTransform drawPile)
        {
            flightAnimator = animator;
            drawPileDock = drawPile;
        }

        /// <summary>배선이 다 갖춰졌는가 — 아니면 호출자가 조용히 건너뛴다.</summary>
        public bool IsReady => flightAnimator != null && drawPileDock != null;

        /// <summary>
        /// 카드 한 장이 덱에 섞였음을 보여준다. 배선이나 카탈로그가 비면 <b>아무 일도 하지 않는다</b>
        /// (규칙은 이미 끝났고, 여기서 예외를 던지면 전투가 멈춘다).
        /// </summary>
        public void Play(CombatCardSnapshot card)
        {
            if (!IsReady || !isActiveAndEnabled)
            {
                return;
            }

            var catalog = RuntimeUiAssetCatalog.LoadDefault();
            if (catalog == null)
            {
                return;
            }

            // 저주는 행동 덱에 섞이므로 행동 카드 얼굴 + 상태 카드 프레임이다.
            var view = CardFrontListInstance.Create(
                catalog.ActionCardFrontPrefab,
                flightAnimator.Overlay,
                card,
                catalog.StatusCardFrameSprite);
            if (view == null)
            {
                return;
            }

            var rect = view.transform as RectTransform;
            if (rect == null)
            {
                Destroy(view.gameObject);
                return;
            }

            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.SetAsLastSibling();

            StartCoroutine(ShowThenFly(rect));
        }

        private IEnumerator ShowThenFly(RectTransform rect)
        {
            // 등장: 작게 나타나 제 크기로. 언스케일드 시간이라 히트스톱·일시정지에도 또렷하다
            // (카드 비행 기구가 쓰는 그 시간축).
            var elapsed = 0f;
            while (elapsed < PopSeconds)
            {
                if (rect == null)
                {
                    yield break;
                }

                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / PopSeconds);
                rect.localScale = Vector3.one * Mathf.Lerp(0.4f, CenterScale, 1f - (1f - t) * (1f - t));
                yield return null;
            }

            if (rect == null)
            {
                yield break;
            }

            rect.localScale = Vector3.one * CenterScale;
            yield return new WaitForSecondsRealtime(HoldSeconds);

            if (rect == null)
            {
                yield break;
            }

            // 빨아들이기는 버린 카드와 <b>같은 비행</b>이다. 목적지가 사라졌으면 그 함수가 알아서 치운다.
            if (flightAnimator != null)
            {
                flightAnimator.FlyDiscardGhost(rect, drawPileDock);
            }
            else
            {
                Destroy(rect.gameObject);
            }
        }
    }
}

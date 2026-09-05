using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    [DisallowMultipleComponent]
    public sealed class CardUnavailableFeedback : MonoBehaviour
    {
        private const string ReasonTextName = "UnavailableReason_TMP";
        private const string DimOverlayName = "UnavailableDim_Overlay";
        // 봉인된 카드에 얹는 빨간 X. dim과 **같은 규약**이다: 모양·색은 CardFront.prefab에 저작돼 있고
        // (SealedMark_Overlay + 45°/−45° 막대 두 개) 런타임은 SetActive로만 토글한다.
        private const string SealedMarkName = "SealedMark_Overlay";

        [SerializeField] private Image[] grayscaleTargets;
        // 비활성 카드 dim 이력 — 아래 런타임 방식은 전부 "빌드에서만" 실패했다:
        //   (1) 커스텀 흑백 셰이더 / (2) sprite 복제 wash → 빌드 크래시
        //   (3) Image.color 틴트 → 과거 mono JIT 트램펄린 크래시 이력
        //   (4) 기존 Image의 canvasRenderer=null → 매 프레임 NRE 폭주
        //   (5) 런타임 생성 spriteless Image + canvasRenderer.SetColor → 에디터에선 어두워지지만
        //       빌드에선 그 틴트가 렌더되지 않아 카드가 검게 안 됨(2026-07-26 라이브 확인).
        // 결론(현재): dim 오버레이를 "프리팹에 미리 저작"한다 — CardFront.prefab의 UnavailableDim_Overlay는
        // solid sprite + 직렬화된 검정색(기본 비활성)이다. 런타임은 색을 절대 건드리지 않고 SetActive로만
        // 토글한다: 직렬화된 색은 표준 UI 경로라 빌드에서 확실히 렌더되고, 런타임 set_color(크래시 위험)도
        // canvasRenderer.SetColor(빌드 미렌더)도 쓰지 않는다. 저작 자식이 없을 때만 fallback으로 생성한다.
        // grayscaleTargets는 프리팹 직렬화/.meta 보존을 위해 남겨둔 레거시 필드(현재 미사용).
        [SerializeField] private TMP_Text reasonText;
        [SerializeField] private CanvasGroup reasonCanvasGroup;
        [SerializeField] private TMP_FontAsset reasonFont;
        [SerializeField] private Vector2 reasonTextSize = new Vector2(184f, 58f);
        [SerializeField] private Vector2 reasonTextAnchoredPosition = new Vector2(0f, 78f);
        [SerializeField] private float reasonFontSize = 18f;
        [SerializeField] private FontStyles reasonFontStyle = FontStyles.Bold;
        [SerializeField] private Color reasonTextColor = new Color(1f, 0.93f, 0.62f, 1f);
        [SerializeField] private float reasonVisibleSeconds = 0.35f;
        [SerializeField] private float reasonFadeSeconds = 0.65f;

        // 프리팹에 저작된 UnavailableDim_Overlay가 SOT다. 이 색은 저작 자식이 없을 때의 fallback 런타임
        // 생성에만 쓴다(프리팹 저작 색과 동일하게 유지). alpha가 클수록 더 어둡다.
        private static readonly Color DimOverlayColor = new Color(0f, 0f, 0f, 0.85f);

        // dim 오버레이가 카드 상단 밖으로 아주 살짝 나오던 것을 억제한다. 프레임 높이의 이 비율만큼 "위쪽만"
        // 깎는다(아래 모서리는 고정). 더 줄이려면 값을 키우면 된다.
        private const float DimTopTrimFraction = 0.02f;

        private Image dimOverlay;
        private Transform sealedMark;
        private RectTransform cardFrameRect;
        private Coroutine fadeRoutine;
        private bool isUnavailable;
        private bool isSealed;
        private string currentReason = string.Empty;

        public bool IsUnavailable => isUnavailable;
        public bool IsSealed => isSealed;
        public string CurrentReason => currentReason;

        private void OnDisable()
        {
            HideReasonImmediate();
            ApplyUnavailableTint(false);
            SetSealed(false);
        }

        public void SetUnavailable(bool unavailable, string reason)
        {
            currentReason = reason ?? string.Empty;
            EnsureReasonText();
            if (isUnavailable == unavailable)
            {
                if (isUnavailable)
                {
                    ApplyUnavailableTint(true);
                }
                else
                {
                    HideReasonImmediate();
                }

                return;
            }

            isUnavailable = unavailable;
            if (isUnavailable)
            {
                ApplyUnavailableTint(true);
            }
            else
            {
                ApplyUnavailableTint(false);
                HideReasonImmediate();
            }
        }

        // 봉인 표시(빨간 X)를 켜고 끈다. ⚠️봉인은 매 턴 다른 카드로 옮겨 다니므로(O-11 재선정) 이 값은
        // "한 번 켜고 끝"이 아니라 손패 갱신마다 다시 계산돼 들어온다.
        public void SetSealed(bool sealedNow)
        {
            isSealed = sealedNow;

            // 켜는 쪽만 찾는다 — 저작 자식이 없으면 켤 것도 끌 것도 없다. dim과 달리 런타임 fallback을
            // 만들지 않는다: X는 색·기하 둘 다 저작물이라 코드로 복제하면 정본이 두 곳이 된다.
            var mark = FindSealedMark();
            if (mark == null)
            {
                return;
            }

            if (sealedNow)
            {
                // dim 오버레이보다 위에 와야 검은 막에 묻히지 않는다. ApplyUnavailableTint가 dim을
                // 최상단으로 올리므로 그 뒤에 다시 올린다(사유 텍스트는 아래에서 또 위로 올라간다).
                mark.SetAsLastSibling();
            }

            if (mark.gameObject.activeSelf != sealedNow)
            {
                mark.gameObject.SetActive(sealedNow);
            }

            if (sealedNow && reasonText != null)
            {
                reasonText.transform.SetAsLastSibling();
            }
        }

        private Transform FindSealedMark()
        {
            if (sealedMark != null)
            {
                return sealedMark;
            }

            return sealedMark = transform.Find(SealedMarkName);
        }

        public void ShowUnavailableReason(string reason = null)
        {
            if (!isUnavailable)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(reason))
            {
                currentReason = reason;
            }

            EnsureReasonText();
            if (reasonText == null || reasonCanvasGroup == null)
            {
                return;
            }

            reasonText.text = string.IsNullOrWhiteSpace(currentReason)
                ? "지금 사용할 수 없습니다"
                : currentReason;
            reasonText.gameObject.SetActive(true);
            reasonCanvasGroup.alpha = 1f;

            if (!Application.isPlaying || !gameObject.activeInHierarchy)
            {
                return;
            }

            if (fadeRoutine != null)
            {
                StopCoroutine(fadeRoutine);
            }

            fadeRoutine = StartCoroutine(FadeReasonRoutine());
        }

        private IEnumerator FadeReasonRoutine()
        {
            var visibleSeconds = Mathf.Max(0f, reasonVisibleSeconds);
            if (visibleSeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(visibleSeconds);
            }

            var fadeSeconds = Mathf.Max(0.01f, reasonFadeSeconds);
            var elapsed = 0f;
            while (elapsed < fadeSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                if (reasonCanvasGroup != null)
                {
                    reasonCanvasGroup.alpha = Mathf.Lerp(1f, 0f, elapsed / fadeSeconds);
                }

                yield return null;
            }

            HideReasonImmediate();
            fadeRoutine = null;
        }

        // 프리팹에 저작된 dim 오버레이를 SetActive로만 토글해 카드를 어둡게 만든다. 색은 프리팹에 직렬화돼
        // 있으므로 런타임에서 절대 건드리지 않는다(그래야 빌드에서 확실히 렌더되고, 크래시 위험도 없다).
        private void ApplyUnavailableTint(bool tinted)
        {
            // 끄는 쪽은 오버레이를 "생성"해선 안 된다. ApplyUnavailableTint(false)는 OnDisable에서도 불리는데,
            // 카드 GameObject가 비활성화되는 도중 자식을 새로 붙이면 Unity가 거부한다
            // ("Cannot set the parent of the GameObject ... while activating or deactivating the parent").
            // 애초에 오버레이가 없으면 끌 것도 없으므로 조회만 한다.
            var overlay = tinted ? EnsureDimOverlay() : FindDimOverlay();
            if (overlay != null)
            {
                if (tinted)
                {
                    // 보이는 카드(프레임) 모양에 정확히 덮이도록 dim의 RectTransform을 프레임에 맞춘다.
                    MatchCardFrameRect(overlay.rectTransform);
                    // 카드 아트/텍스트 위에 오도록 최상단 근처로. reasonText는 아래에서 다시 최상단으로 올린다.
                    // ※ 끌 때는 호출하지 않는다: ApplyUnavailableTint(false)는 OnDisable(부모 슬롯 비활성화 중)
                    //   에서도 불리는데, 그때 형제 순서를 바꾸면 Unity가 거부하며 에러를 뱉는다. 끌 땐
                    //   SetActive(false)만으로 충분하다.
                    overlay.transform.SetAsLastSibling();
                }

                if (overlay.gameObject.activeSelf != tinted)
                {
                    overlay.gameObject.SetActive(tinted);
                }
            }

            // 사용 불가 사유 툴팁은 dim 오버레이보다 위에 두어 또렷하게 보이도록.
            if (tinted && reasonText != null)
            {
                reasonText.transform.SetAsLastSibling();
            }
        }

        // dim 오버레이를 "보이는 카드(Card_Frame_Overlay)"의 RectTransform에 정확히 맞춘다. 슬롯 루트가
        // 프레임보다 작거나(cardFanSize) 변형별로 프레임 크기가 달라도(200x320 vs 240x384) 항상 카드
        // 모양에 딱 덮인다. transform 값만 복사하므로 sprite/color 대입 같은 빌드 렌더/크래시 위험이 없다.
        private void MatchCardFrameRect(RectTransform target)
        {
            if (target == null)
            {
                return;
            }

            if (cardFrameRect == null)
            {
                cardFrameRect = transform.Find("Card_Frame_Overlay") as RectTransform;
            }

            if (cardFrameRect == null)
            {
                return; // 프레임을 못 찾으면 저작된 anchor-stretch를 그대로 둔다.
            }

            target.anchorMin = cardFrameRect.anchorMin;
            target.anchorMax = cardFrameRect.anchorMax;
            target.pivot = cardFrameRect.pivot;

            // 프레임 높이에서 위쪽만 조금 깎아 dim이 카드 상단으로 삐져나오지 않게 한다. 아래 모서리는 고정
            // (bottom = pos.y - size.y*pivot.y 유지)이라 pos.y를 topTrim*pivot.y 만큼 내린다.
            var size = cardFrameRect.sizeDelta;
            var topTrim = size.y * DimTopTrimFraction;
            target.sizeDelta = new Vector2(size.x, size.y - topTrim);
            target.anchoredPosition = new Vector2(
                cardFrameRect.anchoredPosition.x,
                cardFrameRect.anchoredPosition.y - topTrim * cardFrameRect.pivot.y);
        }

        // 이미 있는 오버레이만 찾아 돌려준다(생성 없음). 클론된 카드처럼 dimOverlay 캐시가 비어 있어도
        // 자식으로 남아 있는 오버레이를 다시 붙잡는다.
        private Image FindDimOverlay()
        {
            if (dimOverlay != null)
            {
                return dimOverlay;
            }

            var existing = transform.Find(DimOverlayName);
            return existing != null ? (dimOverlay = existing.GetComponent<Image>()) : null;
        }

        // 프리팹에 저작된 오버레이가 있으면 그대로 쓴다(색은 직렬화돼 있으니 건드리지 않는다). 저작 자식이
        // 없는 슬롯(프리팹 미유래)에서만 fallback으로 런타임 생성한다 — 이 경로는 직렬화 색을 못 써서
        // Image.color로 1회 설정한다(CardFlightAnimator 등 기존 프로덕션과 동일). 정상 카드에선 안 탄다.
        private Image EnsureDimOverlay()
        {
            var found = FindDimOverlay();
            if (found != null)
            {
                return found;
            }

            var go = new GameObject(DimOverlayName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.localScale = Vector3.one;

            dimOverlay = go.GetComponent<Image>();
            dimOverlay.raycastTarget = false; // 카드 hover/클릭이 오버레이에 막히지 않게 통과시킨다.
            dimOverlay.color = DimOverlayColor; // fallback: 직렬화 색을 못 쓰므로 vertex color로 렌더.
            go.SetActive(false);
            return dimOverlay;
        }

        private void EnsureReasonText()
        {
            if (reasonText == null)
            {
                var existing = transform.Find(ReasonTextName);
                reasonText = existing != null ? existing.GetComponent<TMP_Text>() : null;
            }

            if (reasonText == null)
            {
                var textObject = new GameObject(ReasonTextName, typeof(RectTransform), typeof(CanvasGroup), typeof(TextMeshProUGUI));
                textObject.transform.SetParent(transform, false);
                var rect = (RectTransform)textObject.transform;
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = reasonTextSize;
                rect.anchoredPosition = reasonTextAnchoredPosition;
                rect.localScale = Vector3.one;

                reasonText = textObject.GetComponent<TMP_Text>();
                if (reasonFont != null)
                {
                    reasonText.font = reasonFont;
                }

                reasonText.alignment = TextAlignmentOptions.Center;
                reasonText.fontSize = reasonFontSize;
                reasonText.fontStyle = reasonFontStyle;
                reasonText.textWrappingMode = TextWrappingModes.Normal;
                reasonText.color = reasonTextColor;
                reasonText.raycastTarget = false;
            }

            reasonText.transform.SetAsLastSibling();
            reasonCanvasGroup = reasonCanvasGroup != null
                ? reasonCanvasGroup
                : reasonText.GetComponent<CanvasGroup>() ?? reasonText.gameObject.AddComponent<CanvasGroup>();
            reasonCanvasGroup.blocksRaycasts = false;
            reasonCanvasGroup.interactable = false;
            if (!reasonText.gameObject.activeSelf)
            {
                reasonCanvasGroup.alpha = 0f;
            }
        }

        private void HideReasonImmediate()
        {
            if (fadeRoutine != null)
            {
                StopCoroutine(fadeRoutine);
                fadeRoutine = null;
            }

            if (reasonCanvasGroup != null)
            {
                reasonCanvasGroup.alpha = 0f;
            }

            if (reasonText != null)
            {
                reasonText.gameObject.SetActive(false);
            }
        }
    }
}

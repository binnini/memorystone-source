using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SeoulPlayup.Flow.Unity.Intro
{
    /// <summary>
    /// 로비 「게임 소개」 오버레이 — 좌 그림 / 우 글 2분할 패널에 다섯 장을 넘긴다.
    /// 화면 문법·조립 방식은 도감(<c>CodexOverlayView</c>)을 그대로 따른다: 전체화면 딤 +
    /// 가장자리 여백을 남긴 판 + 헤더의 닫기 + ESC. 도감이 로비에서 이미 도는 문법이라
    /// 새로 검증할 것이 없다.
    /// <para>
    /// 🔑 <b>넘김이 두 층위다.</b> 1페이지는 비트 셋을 차례로 넘기는 컷씬이고, 그 뒤로는 페이지가
    /// 넘어간다. 점은 <b>페이지만</b> 센다 — 비트까지 세면 「5장」이 거짓말이 된다.
    /// </para>
    /// <para>
    /// 🔑 <b>이 패널은 아무 상태도 저장하지 않는다.</b> 첫 방문 자동 표시를 하지 않기로 확정돼
    /// (2026-09-01) 「봤음」을 기억할 이유가 없다. 매번 1페이지 첫 비트에서 시작한다.
    /// </para>
    /// </summary>
    public sealed class LobbyIntroOverlayView : MonoBehaviour
    {
        // 도감과 같은 승인 야경 팔레트. 두 화면이 나란히 로비에서 열리므로 색이 갈리면 곧바로 보인다.
        private static readonly Color Ground = new Color(0.059f, 0.067f, 0.141f, 1f);
        private static readonly Color Surface = new Color(0.098f, 0.110f, 0.220f, 1f);
        private static readonly Color Sunken = new Color(0.043f, 0.051f, 0.118f, 1f);
        private static readonly Color Line = new Color(0.169f, 0.184f, 0.333f, 1f);
        private static readonly Color TextPrimary = new Color(0.906f, 0.914f, 0.969f, 1f);
        private static readonly Color TextMuted = new Color(0.557f, 0.576f, 0.741f, 1f);
        private static readonly Color TextDim = new Color(0.365f, 0.384f, 0.569f, 1f);
        private static readonly Color Amber = new Color(0.910f, 0.639f, 0.239f, 1f);

        private const float PanelEdgeMargin = 28f;
        private const float HeaderHeight = 74f;
        private const float FooterHeight = 72f;
        /// <summary>그림 칸이 차지하는 가로 비율. 나머지가 글 칸이다.</summary>
        private const float ArtWidthRatio = 0.56f;

        private LobbyIntroPageAsset asset;
        private CanvasGroup group;

        private RectTransform artViewport;
        private RectTransform artFrame;
        private Image artImage;
        private TMP_Text artNote;

        private TMP_Text titleLabel;
        private TMP_Text speakerLabel;
        private RectTransform speakerPlate;
        private TMP_Text bodyLabel;

        private RectTransform dotRow;
        private readonly System.Collections.Generic.List<Image> dots = new System.Collections.Generic.List<Image>();
        private Button prevButton;
        private Button nextButton;
        private TMP_Text nextLabel;
        private TMP_Text stepLabel;

        private int pageIndex;
        private int beatIndex;

        public bool IsOpen => group != null && group.gameObject.activeSelf;
        public event Action Closed;

        /// <summary>저작 에셋 주입. 로비가 직렬화로 들고 있는 것을 그대로 넘긴다.</summary>
        public void Configure(LobbyIntroPageAsset pageAsset)
        {
            asset = pageAsset;
        }

        public void Open(Transform canvas)
        {
            if (group == null)
            {
                Build(canvas);
            }

            pageIndex = 0;
            beatIndex = 0;
            Refresh();

            group.gameObject.SetActive(true);
            group.alpha = 1f;
            group.interactable = true;
            group.blocksRaycasts = true;
        }

        public void Close()
        {
            if (group == null)
            {
                return;
            }

            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            group.gameObject.SetActive(false);
            Closed?.Invoke();
        }

        private void Update()
        {
            // ESC는 <b>패널 닫기 하나</b>로 통일한다(2026-09-01). 컷씬 도중에도 「비트 건너뛰기」로
            // 갈래를 내지 않는다 — 같은 손이 두 뜻을 가지면 어느 쪽이 일어날지 매번 헷갈린다.
            if (IsOpen && WasCancelPressed())
            {
                Close();
            }
        }

        private static bool WasCancelPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current?.escapeKey.wasPressedThisFrame == true;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        // ── 넘김 ───────────────────────────────────────────────────────────

        private LobbyIntroPageAsset.Page CurrentPage
        {
            get
            {
                var pages = asset != null ? asset.Pages : Array.Empty<LobbyIntroPageAsset.Page>();
                return pageIndex >= 0 && pageIndex < pages.Length ? pages[pageIndex] : null;
            }
        }

        private int PageCount => asset != null ? asset.Pages.Length : 0;

        /// <summary>
        /// 다음으로. 컷씬 페이지에서는 <b>비트를 먼저 다 넘긴 뒤</b>에야 다음 페이지로 간다 —
        /// 그래서 1페이지에서 「다음」을 세 번 눌러야 2페이지가 나온다.
        /// </summary>
        private void GoNext()
        {
            var page = CurrentPage;
            if (page != null && page.IsCutscene && beatIndex < page.Beats.Length - 1)
            {
                beatIndex++;
                Refresh();
                return;
            }

            if (pageIndex >= PageCount - 1)
            {
                Close();
                return;
            }

            pageIndex++;
            beatIndex = 0;
            Refresh();
        }

        /// <summary>
        /// 이전으로. 앞 페이지가 컷씬이면 <b>그 마지막 비트</b>로 돌아간다 — 첫 비트로 보내면
        /// 되돌아온 사람이 이미 읽은 두 비트를 다시 넘겨야 한다.
        /// </summary>
        private void GoPrev()
        {
            var page = CurrentPage;
            if (page != null && page.IsCutscene && beatIndex > 0)
            {
                beatIndex--;
                Refresh();
                return;
            }

            if (pageIndex <= 0)
            {
                return;
            }

            pageIndex--;
            var previous = CurrentPage;
            beatIndex = previous != null && previous.IsCutscene ? previous.Beats.Length - 1 : 0;
            Refresh();
        }

        // ── 그리기 ─────────────────────────────────────────────────────────

        private void Refresh()
        {
            var page = CurrentPage;
            if (page == null)
            {
                return;
            }

            titleLabel.text = page.Title;

            string body;
            string speaker;
            Vector2 focus = new Vector2(0.5f, 0.5f);
            var scale = 1f;

            if (page.IsCutscene)
            {
                var beat = page.Beats[Mathf.Clamp(beatIndex, 0, page.Beats.Length - 1)];
                body = beat.Body;
                speaker = beat.Speaker;
                focus = beat.Focus;
                scale = beat.Scale;
            }
            else
            {
                body = page.Body;
                speaker = null;
            }

            bodyLabel.text = body;

            var hasSpeaker = !string.IsNullOrWhiteSpace(speaker);
            speakerPlate.gameObject.SetActive(hasSpeaker);
            if (hasSpeaker)
            {
                speakerLabel.text = speaker;
            }

            ApplyArt(page, focus, scale);
            RefreshFooter(page);
        }

        /// <summary>
        /// 그림 칸. 스프라이트가 없으면 <b>빈 자리와 한 줄</b>만 남긴다 — 아트가 아직 없어도
        /// 페이지 전체가 성립해야 하고, 그래야 발주를 기다리는 동안 문안을 볼 수 있다.
        /// <para>
        /// 훑기는 <see cref="artFrame"/>의 <c>anchoredPosition</c>·<c>localScale</c>만 움직인다.
        /// 잘라내는 것은 뷰포트의 <see cref="RectMask2D"/>다.
        /// </para>
        /// </summary>
        private void ApplyArt(LobbyIntroPageAsset.Page page, Vector2 focus, float scale)
        {
            var sprite = page.Image;
            artImage.sprite = sprite;
            artImage.enabled = sprite != null;
            artImage.color = sprite != null ? Color.white : new Color(1f, 1f, 1f, 0f);

            var note = page.ImagePlaceholderNote;
            var showNote = sprite == null && !string.IsNullOrWhiteSpace(note);
            artNote.gameObject.SetActive(showNote);
            if (showNote)
            {
                artNote.text = note;
            }

            if (sprite == null)
            {
                artFrame.localScale = Vector3.one;
                artFrame.anchoredPosition = Vector2.zero;
                return;
            }

            artFrame.localScale = new Vector3(scale, scale, 1f);

            // focus는 그림을 1로 본 정규화 좌표다. 뷰포트 크기를 곱해 가운데에서 얼마나 밀지로 바꾼다.
            var size = artViewport.rect.size;
            var offset = new Vector2((0.5f - focus.x) * size.x * scale, (focus.y - 0.5f) * size.y * scale);
            artFrame.anchoredPosition = offset;
        }

        private void RefreshFooter(LobbyIntroPageAsset.Page page)
        {
            for (var i = 0; i < dots.Count; i++)
            {
                dots[i].color = i == pageIndex ? Amber : Line;
            }

            var isLastPage = pageIndex >= PageCount - 1;
            var isLastBeat = !page.IsCutscene || beatIndex >= page.Beats.Length - 1;

            // 마지막 장의 마지막 비트에서만 「시작하기」가 된다 — 그 전에 나오면 거짓말이다.
            nextLabel.text = isLastPage && isLastBeat ? "닫기" : "다음";
            prevButton.gameObject.SetActive(!(pageIndex == 0 && beatIndex == 0));

            stepLabel.text = page.IsCutscene
                ? $"{pageIndex + 1} / {PageCount}  ·  {beatIndex + 1} / {page.Beats.Length}"
                : $"{pageIndex + 1} / {PageCount}";
        }

        // ── 셸 조립 ────────────────────────────────────────────────────────

        private void Build(Transform canvas)
        {
            var root = CreateStretched("LobbyIntroOverlay", canvas);
            group = root.gameObject.AddComponent<CanvasGroup>();

            var dim = root.gameObject.AddComponent<Image>();
            dim.color = new Color(0.008f, 0.010f, 0.020f, 0.92f);

            var panel = CreateStretched("Panel", root);
            panel.offsetMin = new Vector2(PanelEdgeMargin, PanelEdgeMargin);
            panel.offsetMax = new Vector2(-PanelEdgeMargin, -PanelEdgeMargin);
            AddBackground(panel, Ground, Line);

            BuildHeader(panel);

            var body = CreateStretched("Body", panel);
            body.offsetMin = new Vector2(0f, FooterHeight);
            body.offsetMax = new Vector2(0f, -HeaderHeight);

            BuildArt(body);
            BuildText(body);
            BuildFooter(panel);
        }

        private void BuildHeader(RectTransform panel)
        {
            var header = CreateStretched("Header", panel);
            header.anchorMin = new Vector2(0f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.sizeDelta = new Vector2(0f, HeaderHeight);
            AddBackground(header, Sunken, Line);

            titleLabel = CreateText("Title", header, string.Empty, 34f, TextPrimary);
            var titleRect = (RectTransform)titleLabel.transform;
            titleRect.anchorMin = new Vector2(0f, 0.5f);
            titleRect.anchorMax = new Vector2(0f, 0.5f);
            titleRect.pivot = new Vector2(0f, 0.5f);
            titleRect.anchoredPosition = new Vector2(26f, 0f);
            titleRect.sizeDelta = new Vector2(900f, 48f);
            titleLabel.alignment = TextAlignmentOptions.Left;

            var close = CreateButton("CloseButton", header, "닫기", new Vector2(150f, 44f));
            var closeRect = (RectTransform)close.transform;
            closeRect.anchorMin = new Vector2(1f, 0.5f);
            closeRect.anchorMax = new Vector2(1f, 0.5f);
            closeRect.pivot = new Vector2(1f, 0.5f);
            closeRect.anchoredPosition = new Vector2(-20f, 0f);
            close.onClick.AddListener(Close);
        }

        private void BuildArt(RectTransform body)
        {
            artViewport = CreateStretched("Art", body);
            artViewport.anchorMax = new Vector2(ArtWidthRatio, 1f);
            AddBackground(artViewport, Sunken, Line);
            // 훑기가 프레임을 밀어내므로 넘치는 부분을 잘라야 한다.
            artViewport.gameObject.AddComponent<RectMask2D>();

            artFrame = CreateStretched("Frame", artViewport);
            artImage = artFrame.gameObject.AddComponent<Image>();
            artImage.preserveAspect = true;
            artImage.raycastTarget = false;

            artNote = CreateText("PlaceholderNote", artViewport, string.Empty, 21f, TextDim);
            artNote.alignment = TextAlignmentOptions.Center;
            artNote.textWrappingMode = TextWrappingModes.Normal;
            var noteRect = (RectTransform)artNote.transform;
            noteRect.offsetMin = new Vector2(40f, 40f);
            noteRect.offsetMax = new Vector2(-40f, -40f);
        }

        private void BuildText(RectTransform body)
        {
            var column = CreateStretched("Text", body);
            column.anchorMin = new Vector2(ArtWidthRatio, 0f);
            AddBackground(column, Surface, Line);

            var inner = CreateStretched("Inner", column);
            inner.offsetMin = new Vector2(44f, 44f);
            inner.offsetMax = new Vector2(-44f, -44f);

            speakerPlate = CreateRect("Speaker", inner, new Vector2(180f, 40f));
            speakerPlate.anchorMin = new Vector2(0f, 1f);
            speakerPlate.anchorMax = new Vector2(0f, 1f);
            speakerPlate.pivot = new Vector2(0f, 1f);
            speakerPlate.anchoredPosition = Vector2.zero;
            AddBackground(speakerPlate, Sunken, Amber);
            speakerLabel = CreateText("SpeakerLabel", speakerPlate, string.Empty, 22f, Amber);
            speakerLabel.alignment = TextAlignmentOptions.Center;

            bodyLabel = CreateText("Body", inner, string.Empty, 27f, TextMuted);
            bodyLabel.alignment = TextAlignmentOptions.TopLeft;
            bodyLabel.textWrappingMode = TextWrappingModes.Normal;
            bodyLabel.overflowMode = TextOverflowModes.Overflow;
            bodyLabel.lineSpacing = 18f;
            bodyLabel.paragraphSpacing = 16f;
            var bodyRect = (RectTransform)bodyLabel.transform;
            bodyRect.offsetMax = new Vector2(0f, -56f);
        }

        private void BuildFooter(RectTransform panel)
        {
            var footer = CreateStretched("Footer", panel);
            footer.anchorMin = new Vector2(0f, 0f);
            footer.anchorMax = new Vector2(1f, 0f);
            footer.pivot = new Vector2(0.5f, 0f);
            footer.sizeDelta = new Vector2(0f, FooterHeight);
            AddBackground(footer, Sunken, Line);

            // 점은 페이지만 센다(비트는 안 센다 — 위 주석).
            dotRow = CreateRect("Dots", footer, new Vector2(400f, 24f));
            dotRow.anchorMin = new Vector2(0f, 0.5f);
            dotRow.anchorMax = new Vector2(0f, 0.5f);
            dotRow.pivot = new Vector2(0f, 0.5f);
            dotRow.anchoredPosition = new Vector2(26f, 0f);
            var layout = dotRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = false;
            layout.childControlHeight = false;

            // 🔴 지금은 <b>네모</b>다. 유니티 내장 원형(UI/Skin/Knob.psd)은 런타임에 못 잡는다 —
            // Resources.GetBuiltinResource는 null을 돌려주고, 잡히는 쪽인 AssetDatabase는
            // 에디터 전용이라 빌드에서 null이 된다(이 저장소가 서비스 UI에서 이미 물린 함정).
            // 동그라미가 필요해지면 점 스프라이트를 아트로 받아 RuntimeUiAssetCatalog로 물릴 것.
            dots.Clear();
            for (var i = 0; i < PageCount; i++)
            {
                var dot = CreateRect($"Dot{i}", dotRow, new Vector2(12f, 12f));
                var image = dot.gameObject.AddComponent<Image>();
                image.color = Line;
                image.raycastTarget = false;
                dots.Add(image);
            }

            stepLabel = CreateText("Step", footer, string.Empty, 20f, TextDim);
            var stepRect = (RectTransform)stepLabel.transform;
            stepRect.anchorMin = new Vector2(0.5f, 0.5f);
            stepRect.anchorMax = new Vector2(0.5f, 0.5f);
            stepRect.pivot = new Vector2(0.5f, 0.5f);
            stepRect.sizeDelta = new Vector2(360f, 32f);
            stepRect.anchoredPosition = Vector2.zero;
            stepLabel.alignment = TextAlignmentOptions.Center;

            nextButton = CreateButton("NextButton", footer, "다음", new Vector2(160f, 46f));
            var nextRect = (RectTransform)nextButton.transform;
            nextRect.anchorMin = new Vector2(1f, 0.5f);
            nextRect.anchorMax = new Vector2(1f, 0.5f);
            nextRect.pivot = new Vector2(1f, 0.5f);
            nextRect.anchoredPosition = new Vector2(-24f, 0f);
            nextButton.onClick.AddListener(GoNext);
            nextLabel = nextButton.GetComponentInChildren<TMP_Text>();

            prevButton = CreateButton("PrevButton", footer, "이전", new Vector2(140f, 46f));
            var prevRect = (RectTransform)prevButton.transform;
            prevRect.anchorMin = new Vector2(1f, 0.5f);
            prevRect.anchorMax = new Vector2(1f, 0.5f);
            prevRect.pivot = new Vector2(1f, 0.5f);
            prevRect.anchoredPosition = new Vector2(-196f, 0f);
            prevButton.onClick.AddListener(GoPrev);
        }

        // ── UI 헬퍼 (도감과 같은 모양) ─────────────────────────────────────

        private static void AddBackground(RectTransform rect, Color fill, Color border)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = fill;
            var outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = border;
            outline.effectDistance = new Vector2(1f, 1f);
        }

        private static RectTransform CreateStretched(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static RectTransform CreateRect(string name, Transform parent, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        private static TMP_Text CreateText(string name, Transform parent, string value, float fontSize, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = fontSize;
            text.color = color;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }

        private static Button CreateButton(string name, Transform parent, string label, Vector2 size)
        {
            var rect = CreateRect(name, parent, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = Surface;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            if (!string.IsNullOrEmpty(label))
            {
                var text = CreateText("Label", rect, label, 20f, TextPrimary);
                text.alignment = TextAlignmentOptions.Center;
            }

            return button;
        }
    }
}

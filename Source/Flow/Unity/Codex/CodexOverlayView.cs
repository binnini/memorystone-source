using System;
using System.Collections.Generic;
using SeoulPlayup.Codex;
using SeoulPlayup.Combat.Unity;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SeoulPlayup.Flow.Unity
{
    /// <summary>
    /// 도감 오버레이 셸 — 좌 분류 레일 · 중앙 격자 목록 · 우 상세 패널(Q1 확정 3분할).
    /// 계획 정본은 <c>docs/codex-plan.md</c>이고 이 클래스는 P0의 뼈대다.
    /// <para>
    /// 화면을 런타임에 짓는 이유는 로비의 기존 관례를 따른 것이다(<c>LobbyController.BuildLoadingOverlay</c>).
    /// <b>저작된 오브젝트를 런타임이 재배치하는 것과는 다른 일이다</b> — 여기서는 아무도 저작하지 않은
    /// 화면을 새로 만든다. 디자이너가 손볼 지점이 생기면 그때 프리팹으로 뽑아
    /// <c>Assets/Prefabs/UI/Codex/</c>에 두고, 이 빌더는 폴백으로 남긴다.
    /// </para>
    /// </summary>
    public sealed class CodexOverlayView : MonoBehaviour
    {
        // 승인된 야경 팔레트. P2 이후 UiThemeAsset 토큰으로 옮긴다 — P0에서 테마 배선까지 끌고 오면
        // 셸이 도는지 확인하는 일이 테마 작업에 묶인다.
        private static readonly Color Ground = new Color(0.059f, 0.067f, 0.141f, 1f);
        private static readonly Color Surface = new Color(0.098f, 0.110f, 0.220f, 1f);
        private static readonly Color Sunken = new Color(0.043f, 0.051f, 0.118f, 1f);
        private static readonly Color Line = new Color(0.169f, 0.184f, 0.333f, 1f);
        private static readonly Color TextPrimary = new Color(0.906f, 0.914f, 0.969f, 1f);
        private static readonly Color TextMuted = new Color(0.557f, 0.576f, 0.741f, 1f);
        private static readonly Color TextDim = new Color(0.365f, 0.384f, 0.569f, 1f);

        /// <summary>
        /// 종전 고정 패널 규격. 이제 패널은 화면을 채우므로(#17) <b>셀 규격 주석의 기준 폭</b>으로만
        /// 남는다 — 좁은 화면에서 접히는 열 수를 계산할 때의 기준이다.
        /// </summary>
        private const float PanelWidth = 1840f;
        private const float PanelHeight = 1010f;

        /// <summary>풀스크린 패널의 가장자리 여백(#17). 딤 레이어가 얇은 테두리로 남아 "창"으로 읽힌다.</summary>
        private const float PanelEdgeMargin = 28f;
        private const float RailWidth = 250f;
        private const float DetailWidth = 470f;
        private const float HeaderHeight = 74f;
        private const float FilterHeight = 66f;

        /// <summary>
        /// 카드 셀 규격. 목록 폭(패널 <see cref="PanelWidth"/> − 레일 − 상세 = 1120)에서 좌우 여백 48을
        /// 뺀 1072 안에 <b>한 줄 4장</b>이 간격 28로 들어가도록 잡은 값이다 —
        /// <c>4×244 + 3×28 = 1060</c>. 폭을 더 키우면 3장으로 접힌다.
        /// </summary>
        private static readonly Vector2 CardCellSize = new Vector2(244f, 384f);

        /// <summary>썸네일 도메인은 한 줄 5장 — <c>5×190 + 4×28 = 1062</c>.</summary>
        private static readonly Vector2 ThumbCellSize = new Vector2(190f, 214f);

        private readonly List<ICodexDomain> domains = new List<ICodexDomain>();
        private readonly List<Button> railButtons = new List<Button>();
        private readonly List<TMP_Text> railTallies = new List<TMP_Text>();
        private readonly List<Button> chipButtons = new List<Button>();

        private CanvasGroup group;
        private RectTransform railRoot;
        private RectTransform listContent;
        private RectTransform chipRow;
        private RectTransform detailContent;
        private TMP_InputField searchField;
        private TMP_Text emptyLabel;

        private GameObject moveCardPrefab;
        private GameObject actionCardPrefab;
        private Sprite statusCardFrameSprite;

        /// <summary>
        /// 잠긴 칸 실루엣용 재질(Q39). 스프라이트의 <b>알파만</b> 읽어 평평한 색을 칠하므로
        /// 실루엣 밝기가 모델 밝기와 무관해진다. <c>null</c>이면 예전처럼 곱셈 틴트로 떨어진다 —
        /// 어두운 몬스터가 판과 붙지만 화면이 비지는 않는다.
        /// </summary>
        private Material silhouetteMaterial;

        /// <summary>
        /// 해금 진행도. <c>null</c>이면 전량 공개다 — 디버그 뷰(P4)와 진행도 배선이 빠진 랩이
        /// 그 경로로 돈다(<c>CodexListQuery</c>의 <c>isUnlocked: null</c> 규약과 같은 뜻).
        /// </summary>
        private CodexProgress progress;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// 디버그 뷰(P4). 🔴<b>런타임 플래그가 아니라 컴파일 게이트다</b> — 계획 §3-3이 요구하는 것은
        /// "출하 빌드에서 꺼져 있다"가 아니라 "출하 빌드에 <b>없다</b>"이므로 <c>Debug.isDebugBuild</c>로
        /// 대신할 수 없다. 아래 <c>IsDebugView</c>가 출하에서 <c>const false</c>가 되는 것도 같은 이유다 —
        /// 그래야 이 상태를 읽는 분기가 컴파일 단계에서 통째로 사라진다.
        /// </summary>
        private bool isDebugView;

        private Button debugToggle;

        private bool IsDebugView => isDebugView;

        /// <summary>디버그 표시(토글 켜짐 · ⚑ 경고)에 쓰는 주황. 야경 팔레트에서 유일하게 튀는 색이다.</summary>
        private static readonly Color DebugAccent = new Color(0.980f, 0.631f, 0.251f, 1f);
#else
        // 출하 빌드에서는 상수다 — 디버그 뷰를 읽는 분기가 컴파일러 단계에서 접힌다.
        private const bool IsDebugView = false;
#endif

        private ICodexDomain activeDomain;
        private string activeChip = string.Empty;
        private string search = string.Empty;
        private CodexEntry selected;

        /// <summary>범위 도해를 돌려 보는 축. 항목을 바꿀 때 첫 갈래·정동·몸 반경 0으로 되돌린다.</summary>
        private Map.Runtime.HexDirection rangeDirection = Map.Runtime.HexDirection.East;
        private int rangeBodyRadius;
        private int rangeVariant;

        private static readonly string[] DirectionLabels = { "동", "북동", "북서", "서", "남서", "남동" };

        public bool IsOpen => group != null && group.gameObject.activeSelf;

        /// <summary>도감이 닫힐 때 로비가 알아야 하므로(포커스 복귀·BGM 등) 이벤트로 알린다.</summary>
        public event Action Closed;

        /// <summary>
        /// 카드 도메인이 쓰는 프리팹·스프라이트. 🔴<b>반드시 직렬화 참조로 받아야 한다</b> —
        /// 기존 카드 목록 화면들은 비어 있으면 <c>AssetDatabase</c>로 떨어지는데 그 분기는
        /// 에디터에서만 살아 있어서 <b>빌드에서 카드가 빈다</b>(<c>docs/codex-plan.md</c> §2.2).
        /// </summary>
        public void ConfigureCardVisuals(
            GameObject moveCardPrefab,
            GameObject actionCardPrefab,
            Sprite statusCardFrame,
            Material silhouette = null)
        {
            this.moveCardPrefab = moveCardPrefab;
            this.actionCardPrefab = actionCardPrefab;
            statusCardFrameSprite = statusCardFrame;
            silhouetteMaterial = silhouette;
        }

        /// <param name="codexProgress">
        /// 해금 진행도. <c>null</c>을 넘기면 전량 공개로 뜬다 — P3 이전의 동작이자 디버그 뷰의 동작이다.
        /// </param>
        public void Configure(IEnumerable<ICodexDomain> codexDomains, CodexProgress codexProgress = null)
        {
            progress = codexProgress;
            domains.Clear();
            if (codexDomains != null)
            {
                foreach (var domain in codexDomains)
                {
                    if (domain != null && domain.Entries != null)
                    {
                        domains.Add(domain);
                    }
                }
            }

            activeDomain = domains.Count > 0 ? domains[0] : null;
            activeChip = string.Empty;
            search = string.Empty;
            selected = null;
        }

        public void Open(Transform canvas)
        {
            if (group == null)
            {
                Build(canvas);
            }

            RebuildRail();
            RebuildChips();
            RebuildList();
            RebuildDetail();

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
            // Esc로 닫힌다. 오버레이가 열려 있을 때만 읽으므로 로비 입력을 가로채지 않는다.
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

        // ── 셸 조립 ────────────────────────────────────────────────────────

        private void Build(Transform canvas)
        {
            var root = CreateStretched("CodexOverlay", canvas);
            group = root.gameObject.AddComponent<CanvasGroup>();

            var dim = root.gameObject.AddComponent<Image>();
            dim.color = new Color(0.008f, 0.010f, 0.020f, 0.92f);

            // 풀스크린 도감(2026-08-20 #17). 종전은 1840×1010 고정 패널이라 넓은 화면에서 사방에
            // 검은 여백이 남았다. 화면을 채우되 가장자리 여백 하나만 남겨 패널의 테두리가 계속
            // "창"으로 읽히게 한다 — 0으로 두면 딤 레이어가 통째로 사라져 배경과 경계가 없어진다.
            //
            // 🔑 목록 그리드가 GridLayoutGroup.Constraint.Flexible이라 폭이 넓어지면 열 수가 저절로
            // 늘어난다 — 셀 규격(CardCellSize·ThumbCellSize)은 손대지 않아도 되고, 좁은 화면에서는
            // 종전과 같은 4열/5열로 접힌다.
            var panel = CreateStretched("Panel", root);
            panel.offsetMin = new Vector2(PanelEdgeMargin, PanelEdgeMargin);
            panel.offsetMax = new Vector2(-PanelEdgeMargin, -PanelEdgeMargin);
            AddBackground(panel, Ground, Line);

            BuildHeader(panel);

            var body = CreateStretched("Body", panel);
            body.offsetMax = new Vector2(0f, -HeaderHeight);

            var rail = CreateColumn("Rail", body, edge: 0f, width: RailWidth);
            AddBackground(rail, Sunken, Line);
            BuildRail(rail);

            var detail = CreateColumn("Detail", body, edge: 1f, width: DetailWidth);
            AddBackground(detail, Sunken, Line);
            detailContent = BuildScroll(detail, verticalSpacing: 15f, padding: 26f);

            var main = CreateStretched("Main", body);
            main.offsetMin = new Vector2(RailWidth, 0f);
            main.offsetMax = new Vector2(-DetailWidth, 0f);
            AddBackground(main, Surface, Line);
            BuildFilters(main);

            var listHost = CreateStretched("ListHost", main);
            listHost.offsetMax = new Vector2(0f, -FilterHeight);
            listContent = BuildGrid(listHost);

            emptyLabel = CreateText("Empty", listHost, "해당하는 항목이 없습니다.", 24f, TextDim);
            var emptyRect = (RectTransform)emptyLabel.transform;
            emptyRect.anchorMin = new Vector2(0.5f, 0.5f);
            emptyRect.anchorMax = new Vector2(0.5f, 0.5f);
            emptyRect.sizeDelta = new Vector2(600f, 60f);
            emptyLabel.alignment = TextAlignmentOptions.Center;
        }

        private void BuildHeader(RectTransform panel)
        {
            var header = CreateStretched("Header", panel);
            header.anchorMin = new Vector2(0f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.sizeDelta = new Vector2(0f, HeaderHeight);
            AddBackground(header, Sunken, Line);

            var title = CreateText("Title", header, "도감", 34f, TextPrimary);
            var titleRect = (RectTransform)title.transform;
            titleRect.anchorMin = new Vector2(0f, 0.5f);
            titleRect.anchorMax = new Vector2(0f, 0.5f);
            titleRect.pivot = new Vector2(0f, 0.5f);
            titleRect.anchoredPosition = new Vector2(26f, 0f);
            titleRect.sizeDelta = new Vector2(300f, 48f);
            title.alignment = TextAlignmentOptions.Left;

            var close = CreateButton("CloseButton", header, "닫기", new Vector2(150f, 44f));
            var closeRect = (RectTransform)close.transform;
            closeRect.anchorMin = new Vector2(1f, 0.5f);
            closeRect.anchorMax = new Vector2(1f, 0.5f);
            closeRect.pivot = new Vector2(1f, 0.5f);
            closeRect.anchoredPosition = new Vector2(-20f, 0f);
            close.onClick.AddListener(Close);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            BuildDebugToggle(header);
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // ── 디버그 뷰 (P4) ─────────────────────────────────────────────────
        //
        // 이 절 전체가 컴파일 게이트 안이다. 여기 있는 것을 게이트 밖에서 부르면 출하 빌드가 깨지므로,
        // 바깥의 호출 지점도 모두 같은 게이트로 감싸 둔다(<c>BuildEntryCell</c> · <c>RebuildDetail</c>).

        /// <summary>
        /// 헤더 우측, 닫기 버튼 왼쪽에 앉는 토글. 좌표를 손으로 미는 자리이므로 닫기 버튼의
        /// 폭(150)과 오른쪽 여백(20)을 그대로 셈에 넣는다 — 그 둘이 바뀌면 여기도 같이 바뀐다.
        /// </summary>
        private void BuildDebugToggle(RectTransform header)
        {
            debugToggle = CreateButton("DebugToggle", header, "디버그", new Vector2(132f, 44f));
            var rect = (RectTransform)debugToggle.transform;
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-(20f + 150f + 12f), 0f);
            debugToggle.onClick.AddListener(ToggleDebugView);
            PaintDebugToggle();
        }

        private void ToggleDebugView()
        {
            isDebugView = !isDebugView;
            PaintDebugToggle();

            // ⚠️ 목록만 다시 그리면 안 된다 — 디버그에서 잠긴 항목을 고른 채 토글을 끄면 상세 패널이
            // 열린 모습으로 남는다. 레일(수집률)까지 셋을 함께 부른다.
            RebuildRail();
            RebuildList();
            RebuildDetail();
        }

        private void PaintDebugToggle()
        {
            if (debugToggle == null)
            {
                return;
            }

            debugToggle.targetGraphic.color = isDebugView ? DebugAccent : Surface;

            var label = debugToggle.GetComponentInChildren<TMP_Text>();
            if (label == null)
            {
                return;
            }

            label.text = isDebugView ? "디버그 ON" : "디버그";
            label.fontSize = 18f;
            label.color = isDebugView ? Sunken : TextMuted;
        }
#endif

        private void BuildRail(RectTransform rail)
        {
            var list = CreateStretched("RailList", rail);
            list.offsetMin = new Vector2(0f, 0f);
            list.offsetMax = new Vector2(0f, -14f);

            var layout = list.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.spacing = 2f;
            layout.padding = new RectOffset(0, 0, 6, 6);
            layout.childAlignment = TextAnchor.UpperLeft;

            railRoot = list;
        }

        private void BuildFilters(RectTransform main)
        {
            var bar = CreateStretched("Filters", main);
            bar.anchorMin = new Vector2(0f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.sizeDelta = new Vector2(0f, FilterHeight);
            AddBackground(bar, Sunken, Line);

            searchField = CreateSearchField(bar);
            searchField.onValueChanged.AddListener(value =>
            {
                search = value ?? string.Empty;
                RebuildList();
            });

            chipRow = CreateRect("Chips", bar, new Vector2(0f, 40f));
            chipRow.anchorMin = new Vector2(0f, 0.5f);
            chipRow.anchorMax = new Vector2(1f, 0.5f);
            chipRow.pivot = new Vector2(0f, 0.5f);
            chipRow.offsetMin = new Vector2(392f, -20f);
            chipRow.offsetMax = new Vector2(-16f, 20f);
            // 칩이 많은 도메인(카드 7종)에서는 줄이 넘친다 — 잘라내지 않으면 상세 패널 위에 겹쳐 그려진다.
            chipRow.gameObject.AddComponent<RectMask2D>();

            var layout = chipRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleLeft;
        }

        private static RectTransform BuildGrid(RectTransform host)
        {
            // 세로 레이아웃을 붙이지 않는다 — 같은 오브젝트에 레이아웃 그룹이 둘이면 서로 배치를 덮어쓴다.
            var content = BuildScroll(host, verticalSpacing: 0f, padding: 0f);
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = CardCellSize;
            grid.spacing = new Vector2(28f, 26f);
            grid.padding = new RectOffset(24, 24, 22, 28);
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.Flexible;
            return content;
        }

        private static RectTransform BuildScroll(RectTransform host, float verticalSpacing, float padding)
        {
            var viewport = CreateStretched("Viewport", host);
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = CreateStretched("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0f, 0f);

            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            if (verticalSpacing > 0f || padding > 0f)
            {
                var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.childControlHeight = true;
                layout.childControlWidth = true;
                layout.childForceExpandHeight = false;
                layout.childForceExpandWidth = true;
                layout.spacing = verticalSpacing;
                var pad = Mathf.RoundToInt(padding);
                layout.padding = new RectOffset(pad, pad, pad, pad);
            }

            var scroll = host.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32f;
            return content;
        }

        // ── 갱신 ───────────────────────────────────────────────────────────

        private void RebuildRail()
        {
            ClearChildren(railRoot);
            railButtons.Clear();
            railTallies.Clear();

            for (var i = 0; i < domains.Count; i++)
            {
                var domain = domains[i];
                var isActive = domain == activeDomain;

                var button = CreateButton($"Rail_{domain.Id}", railRoot, string.Empty, new Vector2(RailWidth, 48f));
                var image = button.GetComponent<Image>();
                image.color = isActive ? Surface : new Color(0f, 0f, 0f, 0f);

                var dot = CreateRect("Dot", (RectTransform)button.transform, new Vector2(8f, 8f));
                dot.anchorMin = new Vector2(0f, 0.5f);
                dot.anchorMax = new Vector2(0f, 0.5f);
                dot.pivot = new Vector2(0f, 0.5f);
                dot.anchoredPosition = new Vector2(16f, 0f);
                var dotImage = dot.gameObject.AddComponent<Image>();
                dotImage.color = domain.Accent;
                dotImage.raycastTarget = false;

                var label = CreateText("Label", (RectTransform)button.transform, domain.Label, 21f,
                    isActive ? TextPrimary : TextMuted);
                var labelRect = (RectTransform)label.transform;
                labelRect.anchorMin = new Vector2(0f, 0f);
                labelRect.anchorMax = new Vector2(1f, 1f);
                labelRect.offsetMin = new Vector2(34f, 0f);
                labelRect.offsetMax = new Vector2(-84f, 0f);
                label.alignment = TextAlignmentOptions.Left;

                var tally = CreateText("Tally", (RectTransform)button.transform, string.Empty, 17f, TextDim);
                var tallyRect = (RectTransform)tally.transform;
                tallyRect.anchorMin = new Vector2(1f, 0f);
                tallyRect.anchorMax = new Vector2(1f, 1f);
                tallyRect.pivot = new Vector2(1f, 0.5f);
                tallyRect.sizeDelta = new Vector2(76f, 0f);
                tallyRect.anchoredPosition = new Vector2(-14f, 0f);
                tally.alignment = TextAlignmentOptions.Right;

                var predicate = UnlockPredicate(domain);
                var counts = CodexListQuery.Tally(domain.Entries, predicate);
                // 항상 열린 도메인(상태이상 · 형상)에 "19/19"를 찍으면 모으는 것처럼 읽힌다 —
                // 모을 것이 없는 도메인은 총 수만 적는다.
                // 단 디버그 뷰에서는 해금을 타는 도메인도 술어가 없어지므로, 그때는 "n/n"을 찍어
                // 전량 공개가 레일에서도 읽히게 한다 — 여기서 총 수만 적으면 잠금이 사라진 것인지
                // 원래 안 잠기는 도메인인지 구별이 안 된다.
                tally.text = predicate == null && !(IsDebugView && !domain.AlwaysUnlocked)
                    ? counts.Total.ToString()
                    : $"{counts.Unlocked}/{counts.Total}";

                var captured = domain;
                button.onClick.AddListener(() => SelectDomain(captured));

                railButtons.Add(button);
                railTallies.Add(tally);
            }
        }

        private void SelectDomain(ICodexDomain domain)
        {
            if (domain == activeDomain)
            {
                return;
            }

            activeDomain = domain;
            activeChip = string.Empty;
            selected = null;
            if (searchField != null)
            {
                searchField.SetTextWithoutNotify(string.Empty);
            }

            search = string.Empty;
            RebuildRail();
            RebuildChips();
            RebuildList();
            RebuildDetail();
        }

        private void RebuildChips()
        {
            ClearChildren(chipRow);
            chipButtons.Clear();

            if (activeDomain == null)
            {
                return;
            }

            foreach (var chip in CodexListQuery.CollectChips(activeDomain.Entries))
            {
                var isActive = string.Equals(chip, activeChip, StringComparison.Ordinal);
                var button = CreateButton($"Chip_{chip}", chipRow, chip, new Vector2(92f, 34f));
                var image = button.GetComponent<Image>();
                image.color = isActive ? new Color(0.247f, 0.663f, 0.961f, 1f) : new Color(0f, 0f, 0f, 0f);

                var outline = button.gameObject.AddComponent<Outline>();
                outline.effectColor = isActive ? new Color(0f, 0f, 0f, 0f) : Line;
                outline.effectDistance = new Vector2(1f, 1f);

                var label = button.GetComponentInChildren<TMP_Text>();
                if (label != null)
                {
                    label.fontSize = 18f;
                    label.color = isActive ? Sunken : TextMuted;
                }

                var captured = chip;
                button.onClick.AddListener(() =>
                {
                    // 같은 칩을 다시 누르면 해제된다 — 칩 하나뿐인 도메인에서 갇히지 않게.
                    activeChip = string.Equals(activeChip, captured, StringComparison.Ordinal) ? string.Empty : captured;
                    RebuildChips();
                    RebuildList();
                });

                chipButtons.Add(button);
            }
        }

        private void RebuildList()
        {
            ClearChildren(listContent);

            if (activeDomain == null)
            {
                SetEmptyVisible(true);
                return;
            }

            // 셀 규격은 도메인이 정한다 — 카드만 카드 모양이라 격자 칸부터 달라진다.
            var grid = listContent.GetComponent<GridLayoutGroup>();
            if (grid != null)
            {
                grid.cellSize = CellSize(activeDomain);
            }

            // 잠긴 항목은 목록에 남지만 검색에는 안 걸린다 — 규칙은 CodexListQuery가 이미 안다(Q4·Q7).
            var rows = CodexListQuery.Apply(activeDomain.Entries, activeChip, search, UnlockPredicate(activeDomain));
            SetEmptyVisible(rows.Count == 0);

            foreach (var entry in rows)
            {
                BuildEntryCell(entry);
            }
        }

        private void SetEmptyVisible(bool visible)
        {
            if (emptyLabel != null)
            {
                emptyLabel.gameObject.SetActive(visible);
            }
        }

        /// <summary>
        /// 이 도메인의 해금 술어. <c>null</c>이면 전량 공개다 — 진행도가 없거나
        /// <c>AlwaysUnlocked</c> 도메인(Q6)이거나 <b>디버그 뷰</b>(P4)일 때 그렇다.
        /// <para>
        /// 🔑 이 함수 하나가 <c>RebuildRail</c>의 수집률 · <c>RebuildList</c>의 목록·검색 ·
        /// <c>IsUnlocked</c>의 잠긴 칸 분기를 모두 먹인다. 디버그 토글을 여기 한 곳에 물리면
        /// 그 셋이 저절로 따라온다 — 다른 자리에 조건을 하나라도 더 놓으면 셋이 갈라진다.
        /// </para>
        /// </summary>
        private Func<CodexEntry, bool> UnlockPredicate(ICodexDomain domain)
        {
            return CodexUnlockRules.PredicateFor(progress, domain, IsDebugView);
        }

        private bool IsUnlocked(CodexEntry entry)
        {
            var predicate = UnlockPredicate(activeDomain);
            return predicate == null || (entry != null && predicate(entry));
        }

        private void BuildEntryCell(CodexEntry entry)
        {
            if (!IsUnlocked(entry))
            {
                BuildLockedEntryCell(entry);
                return;
            }

            var cellSize = CellSize(activeDomain);
            var button = CreateButton($"Entry_{entry.Id}", listContent, string.Empty, cellSize);
            var image = button.GetComponent<Image>();
            var isSelected = entry == selected;
            image.color = isSelected ? Sunken : new Color(0.071f, 0.078f, 0.169f, 1f);

            var outline = button.gameObject.AddComponent<Outline>();
            outline.effectColor = isSelected ? entry.Thumbnail.Tone : Line;
            outline.effectDistance = new Vector2(1.5f, 1.5f);

            var cell = (RectTransform)button.transform;
            var captured = entry;
            void Select()
            {
                selected = captured;
                // 앞 항목에서 돌려 보던 방향이 다음 항목까지 따라오면 "이 형상의 기본 모습"으로 오해한다.
                rangeDirection = Map.Runtime.HexDirection.East;
                rangeBodyRadius = 0;
                rangeVariant = 0;
                RebuildList();
                RebuildDetail();
            }

            // 카드는 프레임째 그린다 — 카드 얼굴이 이름·기력·사거리를 이미 말하므로 셀에 덧붙이지 않는다.
            if (entry.CardVisual.HasValue)
            {
                image.color = new Color(0f, 0f, 0f, 0f);
                outline.effectColor = isSelected ? entry.Thumbnail.Tone : new Color(0f, 0f, 0f, 0f);
                PaintCard(cell, entry.CardVisual.Value, cellSize.x - 16f, entry.DisplayName, entry.Thumbnail.Tone);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                // 카드를 세운 뒤에 단다 — 형제 순서가 곧 그리는 순서라 먼저 달면 카드 밑에 깔린다.
                PaintDebugBadges(cell, entry);
#endif
                button.onClick.AddListener(Select);
                return;
            }

            var thumb = CreateRect("Thumb", cell, new Vector2(160f, 118f));
            thumb.anchorMin = new Vector2(0.5f, 1f);
            thumb.anchorMax = new Vector2(0.5f, 1f);
            thumb.pivot = new Vector2(0.5f, 1f);
            thumb.anchoredPosition = new Vector2(0f, -12f);
            PaintThumbnail(thumb, entry.Thumbnail, labelFontSize: 19f);

            var name = CreateText("Name", cell, entry.DisplayName, 21f, TextPrimary);
            var nameRect = name.rectTransform;
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.pivot = new Vector2(0.5f, 1f);
            nameRect.sizeDelta = new Vector2(-20f, 50f);
            nameRect.anchoredPosition = new Vector2(0f, -138f);
            name.alignment = TextAlignmentOptions.Top;
            name.textWrappingMode = TextWrappingModes.Normal;

            if (entry.MetaChips.Count > 0)
            {
                var meta = CreateText("Meta", cell, string.Join("   ", entry.MetaChips), 17f, TextMuted);
                var metaRect = meta.rectTransform;
                metaRect.anchorMin = new Vector2(0f, 0f);
                metaRect.anchorMax = new Vector2(1f, 0f);
                metaRect.pivot = new Vector2(0.5f, 0f);
                metaRect.sizeDelta = new Vector2(-20f, 28f);
                metaRect.anchoredPosition = new Vector2(0f, 10f);
                meta.alignment = TextAlignmentOptions.Center;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            PaintDebugBadges(cell, entry);
#endif

            button.onClick.AddListener(Select);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// 디버그 뷰의 셀 배지 — 폴백 썸네일에 <c>FB</c>, 저작 경고에 <c>!</c>.
        /// <para>
        /// 🔑<b>배지가 거의 모든 칸에 뜬다</b>(유물 28 · 소모품 12 · 몬스터 11이 사실상 전량 폴백이다).
        /// 그래서 판도 테두리도 두지 않고 <b>모서리에 흐린 글자만</b> 얹는다 — 배지 하나하나를
        /// 읽히게 만들면 격자가 배지의 벽이 되어 정작 "어디에 아트가 들어왔나"라는 그림이 안 보인다.
        /// 여기서 노리는 것은 개별 판독이 아니라 <b>훑었을 때의 밀도</b>다.
        /// </para>
        /// <para>
        /// ⚠️ 경고 배지에 <c>⚑</c> 대신 <c>!</c>를 쓰는 것은 폰트 때문이다 — 출하 폰트는
        /// DNFForgedBlade 하나이고 폴백을 비워 뒀으므로(폰트 단일화 트랙), 글리프가 없는 기호를
        /// 넣으면 두부(□)가 뜬다. 말뜻은 상세 패널의 경고 블록이 문장으로 적는다.
        /// </para>
        /// </summary>
        private void PaintDebugBadges(RectTransform cell, CodexEntry entry)
        {
            if (!isDebugView)
            {
                return;
            }

            // 🔴 카드는 <b>썸네일로 그려지지 않는다</b> — `CardVisual`이 있으면 뷰가 `CardFront`를
            // 통째로 세우고 `Thumbnail`은 쓰이지 않은 채 폴백 상태로 남는다. 그것을 그대로 읽으면
            // 일러가 다 들어온 카드 59장에 전부 `FB`가 붙어 **배지가 거짓말을 한다**.
            // 카드의 아트 유무는 카드 얼굴이 직접 보여 주므로 여기서는 세지 않는다.
            if (entry.Thumbnail.IsFallback && !entry.CardVisual.HasValue)
            {
                var badge = CreateText("DebugFallbackBadge", cell, "FB", 13f,
                    new Color(TextDim.r, TextDim.g, TextDim.b, 0.55f));
                var rect = badge.rectTransform;
                rect.anchorMin = new Vector2(1f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 1f);
                rect.sizeDelta = new Vector2(34f, 18f);
                rect.anchoredPosition = new Vector2(-7f, -6f);
                badge.alignment = TextAlignmentOptions.Right;
                badge.characterSpacing = 2f;
            }

            if (string.IsNullOrWhiteSpace(entry.AuthoringWarning))
            {
                return;
            }

            // 경고는 드물다(지금은 함정 하나뿐) — 드문 것은 눈에 띄어도 화면을 덮지 않으므로
            // 여기만 색을 살려 둔다.
            var warning = CreateText("DebugWarningBadge", cell, "!", 20f, DebugAccent);
            var warningRect = warning.rectTransform;
            warningRect.anchorMin = new Vector2(0f, 1f);
            warningRect.anchorMax = new Vector2(0f, 1f);
            warningRect.pivot = new Vector2(0f, 1f);
            warningRect.sizeDelta = new Vector2(20f, 22f);
            warningRect.anchoredPosition = new Vector2(8f, -5f);
            warning.alignment = TextAlignmentOptions.Left;
        }
#endif

        /// <summary>
        /// 잠긴 칸(Q7 확정 — 자리는 남기고 <c>？？？</c> + 실루엣). §7-1이 남겨 둔 디자인 판단을
        /// 여기서 내린다.
        /// <para>
        /// 🔑<b>카드는 프레임만, 아이콘 도메인은 진짜 실루엣.</b> 카드 도메인은 <c>CardFront</c> 프리팹을
        /// 통째로 세우는데, 그것을 세운 뒤 가리는 방식은 두 가지가 다 나쁘다 — 어둡게만 하면 이름과
        /// 기력이 비쳐 읽히고(해금이 무의미해진다), 완전히 덮으면 프리팹을 세우는 비용만 버린다.
        /// 그래서 <b>카드는 아예 세우지 않고</b> 카드 비율의 판에 갈래 색 키라인만 남긴다. 반대로
        /// 아이콘 도메인은 스프라이트가 있으므로 그것을 <b>단색으로 눌러</b> 실루엣을 만든다 —
        /// "무엇인지는 모르지만 모양은 보인다"가 도감이 잠금으로 하려는 말이다.
        /// </para>
        /// <para>
        /// 갈래 색을 남기는 것은 정보 누출이 아니다 — 이동/행동 구분은 칩이 이미 말하고 있고,
        /// 색까지 지우면 격자가 똑같은 빈 판의 벽이 돼 "몇 개나 남았나"조차 안 읽힌다.
        /// </para>
        /// </summary>
        private void BuildLockedEntryCell(CodexEntry entry)
        {
            var cellSize = CellSize(activeDomain);
            var button = CreateButton($"Locked_{entry.Id}", listContent, string.Empty, cellSize);
            var image = button.GetComponent<Image>();
            var isSelected = entry == selected;

            // 열린 칸보다 한 단 더 가라앉힌다 — 밝기 차이 하나로 "아직"이 읽힌다.
            image.color = isSelected ? Sunken : new Color(0.051f, 0.055f, 0.118f, 1f);

            var tone = entry.Thumbnail.Tone;
            var outline = button.gameObject.AddComponent<Outline>();
            outline.effectColor = isSelected
                ? new Color(tone.r, tone.g, tone.b, 0.55f)
                : new Color(tone.r, tone.g, tone.b, 0.22f);
            outline.effectDistance = new Vector2(1.5f, 1.5f);

            var cell = (RectTransform)button.transform;

            if (entry.CardVisual.HasValue)
            {
                // 카드 비율의 빈 판. 프리팹을 세우지 않으므로 이름·기력·일러가 샐 길이 없다.
                const float PlateInset = 8f;
                var plate = CreateRect("LockedPlate", cell, new Vector2(cellSize.x - PlateInset * 2f, cellSize.y - PlateInset * 2f));
                var plateImage = plate.gameObject.AddComponent<Image>();
                plateImage.color = new Color(
                    Mathf.Lerp(Sunken.r, tone.r, 0.10f),
                    Mathf.Lerp(Sunken.g, tone.g, 0.10f),
                    Mathf.Lerp(Sunken.b, tone.b, 0.10f),
                    1f);
                plateImage.raycastTarget = false;

                var plateLine = plate.gameObject.AddComponent<Outline>();
                plateLine.effectColor = new Color(tone.r, tone.g, tone.b, 0.30f);
                plateLine.effectDistance = new Vector2(1f, 1f);
            }
            else if (entry.Thumbnail.Sprite != null)
            {
                // 진짜 실루엣 — 같은 스프라이트를 거의 검게 눌러 모양만 남긴다.
                var thumb = CreateRect("Silhouette", cell, new Vector2(160f, 118f));
                thumb.anchorMin = new Vector2(0.5f, 1f);
                thumb.anchorMax = new Vector2(0.5f, 1f);
                thumb.pivot = new Vector2(0.5f, 1f);
                thumb.anchoredPosition = new Vector2(0f, -12f);

                var background = thumb.gameObject.AddComponent<Image>();
                background.color = new Color(0.035f, 0.039f, 0.086f, 1f);
                background.raycastTarget = false;

                var art = CreateStretched("Art", thumb);
                art.offsetMin = new Vector2(8f, 8f);
                art.offsetMax = new Vector2(-8f, -8f);
                var artImage = art.gameObject.AddComponent<Image>();
                artImage.sprite = entry.Thumbnail.Sprite;
                artImage.preserveAspect = entry.Thumbnail.PreserveAspect;
                artImage.raycastTarget = false;

                if (silhouetteMaterial != null)
                {
                    // 🔑 재질이 알파만 읽으므로 색을 <b>지정</b>할 수 있다 — 모델이 검든 희든 같은 세기다
                    // (Q39: 삼목구 중앙값 6/255 vs 사자탈 122/255였다).
                    artImage.material = silhouetteMaterial;
                    artImage.color = new Color(tone.r * 0.62f, tone.g * 0.62f, tone.b * 0.62f, 0.85f);
                }
                else
                {
                    // 재질이 없으면 예전 곱셈 틴트 — 어두운 모델은 묻히지만 화면이 비지는 않는다.
                    artImage.color = new Color(tone.r * 0.32f, tone.g * 0.32f, tone.b * 0.32f, 0.92f);
                }
            }

            // 스프라이트가 없는 항목(절차 폴백 썸네일)은 판만 남는다 — 이름 판을 그대로 두면
            // 폴백 썸네일이 이름 전체를 적으므로 잠금이 통째로 새어 나온다(CodexThumbnail Q9).

            var mark = CreateText("Unknown", cell, "？？？", entry.CardVisual.HasValue ? 40f : 32f, new Color(tone.r, tone.g, tone.b, 0.62f));
            var markRect = mark.rectTransform;
            markRect.anchorMin = Vector2.zero;
            markRect.anchorMax = Vector2.one;
            markRect.offsetMin = Vector2.zero;
            markRect.offsetMax = entry.CardVisual.HasValue ? Vector2.zero : new Vector2(0f, -128f);
            mark.alignment = TextAlignmentOptions.Center;
            mark.characterSpacing = -12f;

            var captured = entry;
            button.onClick.AddListener(() =>
            {
                // 잠긴 칸도 고를 수 있다 — 자리를 남긴다는 것은 "여기 무언가 있다"까지 말한다는 뜻이다.
                selected = captured;
                rangeDirection = Map.Runtime.HexDirection.East;
                rangeBodyRadius = 0;
                rangeVariant = 0;
                RebuildList();
                RebuildDetail();
            });
        }

        private void RebuildDetail()
        {
            ClearChildren(detailContent);

            if (selected == null)
            {
                var hint = CreateText("Hint", detailContent, "왼쪽에서 항목을 고르세요.", 20f, TextDim);
                SetPreferredHeight(hint.rectTransform, 44f);
                return;
            }

            if (!IsUnlocked(selected))
            {
                // 🔴잠긴 항목의 상세는 여기서 끝난다. 아래로 흘려보내면 제목·설명·수치·범위 도해가
                // 통째로 새어 나간다 — 목록에서 가려 놓고 상세에서 다 보여 주면 잠금이 장식이 된다.
                BuildLockedDetail();
                return;
            }

            if (selected.CardVisual.HasValue)
            {
                // 상세에서도 같은 카드다 — 크기만 다르다. 도감에서 본 모습과 전투에서 본 모습이
                // 달라지면 도감이 제 일을 못 한다.
                // Q22 확정: 「범위」 절은 지금 자리(설명 아래)를 지키되, 카드가 패널을 덜 먹도록
                // 조금 줄인다 — 그만큼 도해가 첫 화면 안으로 들어온다.
                const float HeroCardWidth = 262f;
                var cardHero = CreateRect("HeroCard", detailContent, new Vector2(0f, HeroCardWidth * 1.6f));
                SetPreferredHeight(cardHero, HeroCardWidth * 1.6f);
                PaintCard(cardHero, selected.CardVisual.Value, HeroCardWidth, selected.DisplayName, selected.Thumbnail.Tone);
            }
            else
            {
                var hero = CreateRect("Hero", detailContent, new Vector2(0f, 236f));
                SetPreferredHeight(hero, 236f);
                PaintThumbnail(hero, selected.Thumbnail, labelFontSize: 34f);
            }

            // 제목과 부제는 한 덩어리다 — 바깥 세로 간격(14)이 둘 사이에 끼면 부제가 딴 항목처럼 보인다.
            BuildTitleBlock();

            if (!string.IsNullOrWhiteSpace(selected.Description))
            {
                BuildDivider();

                var description = CreateText("Description", detailContent, selected.Description, 21f, TextPrimary);
                description.alignment = TextAlignmentOptions.TopLeft;
                description.textWrappingMode = TextWrappingModes.Normal;
                description.overflowMode = TextOverflowModes.Overflow;
                description.lineSpacing = 12f;
                // 높이를 고정하지 않는다 — 세로 레이아웃이 TMP의 선호 높이를 읽으므로 긴 설명도 안 잘린다.
            }

            var rows = new List<CodexDetailRow>();
            foreach (var row in selected.DetailRows)
            {
                if (!row.IsEmpty)
                {
                    rows.Add(row);
                }
            }

            BuildRangeSection();

            if (rows.Count > 0)
            {
                BuildDivider();
                for (var i = 0; i < rows.Count; i++)
                {
                    BuildDetailRow(rows[i]);
                }
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // 원값 표는 항상 맨 아래다 — 플레이어가 읽는 것들 위에 저작 값이 끼면 상세 패널이
            // 저작 화면으로 읽힌다.
            BuildDebugSection();
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// 상세 패널 맨 아래에 붙는 디버그 절 — 저작 경고 블록과 원값 표(<c>CodexEntry.DebugRows</c>).
        /// <para>
        /// ⚠️ 경고가 뜨는 항목은 지금 <b>함정의 폐기된 <c>Burn</c> 프리셋 하나뿐</b>이고, 그마저
        /// 출하 카탈로그에는 저작돼 있지 않다. 다른 도메인에서 안 보이는 것은 배선이 아니라
        /// 저작에 경고할 것이 없다는 뜻이다 — 안 보인다고 쫓지 말 것.
        /// </para>
        /// </summary>
        private void BuildDebugSection()
        {
            if (!isDebugView || selected == null)
            {
                return;
            }

            var hasWarning = !string.IsNullOrWhiteSpace(selected.AuthoringWarning);

            var debugRows = new List<CodexDetailRow>();
            foreach (var row in selected.DebugRows)
            {
                if (!row.IsEmpty)
                {
                    debugRows.Add(row);
                }
            }

            if (!hasWarning && debugRows.Count == 0)
            {
                return;
            }

            BuildDivider();

            var caption = CreateText("DebugCaption", detailContent, "원값", 16f, DebugAccent);
            caption.characterSpacing = 3.5f;
            SetPreferredHeight(caption.rectTransform, 22f);

            if (hasWarning)
            {
                BuildAuthoringWarning(selected.AuthoringWarning);
            }

            for (var i = 0; i < debugRows.Count; i++)
            {
                BuildDebugRow(debugRows[i]);
            }
        }

        /// <summary>저작 경고 한 덩어리. 색만으로는 무엇이 잘못됐는지 못 읽으므로 문장을 그대로 싣는다.</summary>
        private void BuildAuthoringWarning(string message)
        {
            var block = CreateRect("AuthoringWarning", detailContent, new Vector2(0f, 0f));
            var layout = block.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.padding = new RectOffset(12, 12, 8, 8);
            var fitter = block.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var background = block.gameObject.AddComponent<Image>();
            background.color = new Color(DebugAccent.r * 0.22f, DebugAccent.g * 0.16f, DebugAccent.b * 0.10f, 1f);
            background.raycastTarget = false;

            var text = CreateText("Text", block, $"!  {message}", 17f, DebugAccent);
            text.alignment = TextAlignmentOptions.TopLeft;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.lineSpacing = 8f;
        }

        /// <summary>
        /// 원값 한 줄. <c>BuildDetailRow</c>와 나란한 좌우 배치지만 높이를 고정하지 않는다 —
        /// 형상의 <c>offsets</c>처럼 긴 원값은 한 줄에 안 들어가고, 잘린 원값은 원값이 아니다.
        /// </summary>
        private void BuildDebugRow(CodexDetailRow row)
        {
            var host = CreateRect($"DebugRow_{row.Label}", detailContent, new Vector2(0f, 24f));
            var layout = host.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.spacing = 10f;
            var fitter = host.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var label = CreateText("Label", host, row.Label, 15f, TextDim);
            label.alignment = TextAlignmentOptions.TopLeft;
            var labelElement = label.gameObject.AddComponent<LayoutElement>();
            labelElement.preferredWidth = 172f;
            labelElement.flexibleWidth = 0f;
            labelElement.minHeight = 22f;

            var value = CreateText("Value", host, row.Value, 16f, TextMuted);
            value.alignment = TextAlignmentOptions.TopLeft;
            value.textWrappingMode = TextWrappingModes.Normal;
            value.overflowMode = TextOverflowModes.Overflow;
            var valueElement = value.gameObject.AddComponent<LayoutElement>();
            valueElement.flexibleWidth = 1f;
            valueElement.minHeight = 22f;
        }
#endif

        /// <summary>
        /// 잠긴 항목의 상세 패널. 말할 수 있는 것은 <b>어디서 열리는가</b>뿐이다 — 이름도 수치도
        /// 범위도 아직 플레이어의 것이 아니다.
        /// </summary>
        private void BuildLockedDetail()
        {
            var tone = selected.Thumbnail.Tone;

            var hero = CreateRect("LockedHero", detailContent, new Vector2(0f, 236f));
            SetPreferredHeight(hero, 236f);
            var background = hero.gameObject.AddComponent<Image>();
            background.color = new Color(
                Mathf.Lerp(Sunken.r, tone.r, 0.10f),
                Mathf.Lerp(Sunken.g, tone.g, 0.10f),
                Mathf.Lerp(Sunken.b, tone.b, 0.10f),
                1f);
            background.raycastTarget = false;

            if (selected.Thumbnail.Sprite != null)
            {
                var art = CreateStretched("Silhouette", hero);
                art.offsetMin = new Vector2(24f, 24f);
                art.offsetMax = new Vector2(-24f, -24f);
                var artImage = art.gameObject.AddComponent<Image>();
                artImage.sprite = selected.Thumbnail.Sprite;
                artImage.preserveAspect = selected.Thumbnail.PreserveAspect;
                artImage.raycastTarget = false;
                artImage.color = new Color(tone.r * 0.32f, tone.g * 0.32f, tone.b * 0.32f, 0.92f);
            }
            else
            {
                var mark = CreateText("Unknown", hero, "？？？", 56f, new Color(tone.r, tone.g, tone.b, 0.55f));
                var markRect = mark.rectTransform;
                markRect.anchorMin = Vector2.zero;
                markRect.anchorMax = Vector2.one;
                markRect.offsetMin = Vector2.zero;
                markRect.offsetMax = Vector2.zero;
                mark.alignment = TextAlignmentOptions.Center;
                mark.characterSpacing = -14f;
            }

            var title = CreateText("Title", detailContent, "？？？", 31f, TextMuted);
            title.alignment = TextAlignmentOptions.Left;
            title.characterSpacing = -1.5f;
            SetPreferredHeight(title.rectTransform, 42f);

            BuildDivider();

            var note = CreateText("LockedNote", detailContent, LockedHint(activeDomain), 20f, TextDim);
            note.alignment = TextAlignmentOptions.TopLeft;
            note.textWrappingMode = TextWrappingModes.Normal;
            note.overflowMode = TextOverflowModes.Overflow;
            note.lineSpacing = 10f;
        }

        /// <summary>
        /// 도메인마다 "어떻게 하면 열리는가"가 다르다. 한 문장이라도 적어야 잠긴 칸이 벽이 아니라
        /// 목표가 된다.
        /// </summary>
        private static string LockedHint(ICodexDomain domain)
        {
            if (domain == null)
            {
                return "아직 만나지 않았습니다.";
            }

            switch (domain.Id)
            {
                case CodexCardDomain.DomainId:
                    return "아직 손에 들어온 적 없는 카드입니다.";
                case CodexMonsterDomain.DomainId:
                    return "아직 마주친 적 없는 존재입니다.";
                case CodexRelicDomain.DomainId:
                    return "아직 손에 넣은 적 없는 물건입니다.";
                case CodexConsumableItemDomain.DomainId:
                    return "아직 가방에 들어온 적 없는 물건입니다.";
                case CodexTrapDomain.DomainId:
                    return "아직 발견하거나 밟은 적 없는 함정입니다.";
                default:
                    return "아직 만나지 않았습니다.";
            }
        }

        // ── 범위 도해 (P1) ─────────────────────────────────────────────────

        /// <summary>
        /// 상세 패널의 범위 절. <b>도해를 낼 수 있는 항목만</b> 이 절을 갖는다 — 자기 대상 카드에
        /// 빈 격자를 그리면 "범위가 0"으로 읽히므로, 그럴 때는 격자 대신 한 줄 문구만 적는다.
        /// </summary>
        private void BuildRangeSection()
        {
            var source = selected?.RangeSource;
            if (source == null)
            {
                return;
            }

            var variants = source.Variants;
            if (variants.Count > 0)
            {
                rangeVariant = Mathf.Clamp(rangeVariant, 0, variants.Count - 1);
            }

            var diagram = source.Resolve(rangeVariant, rangeDirection, rangeBodyRadius);

            BuildDivider();
            var caption = CreateText("RangeCaption", detailContent, "범위", 16f, TextDim);
            caption.characterSpacing = 3.5f;
            SetPreferredHeight(caption.rectTransform, 22f);

            // 갈래 줄은 도해가 비어 있어도 그린다 — 고른 패턴에 그릴 칸이 없다고 해서 다른 패턴으로
            // 옮길 길까지 사라지면 그 몬스터의 나머지 패턴을 영영 못 본다.
            if (variants.Count > 1)
            {
                BuildRangeVariantRow(
                    "Variant",
                    "패턴",
                    variants,
                    rangeVariant,
                    index =>
                    {
                        rangeVariant = index;
                        // 패턴마다 형상이 다르므로 방향·몸 반경은 그대로 두고 도해만 다시 푼다.
                        RebuildDetail();
                    });
            }

            if (diagram.IsEmpty)
            {
                var note = CreateText("RangeNote", detailContent, diagram.Note, 19f, TextMuted);
                note.alignment = TextAlignmentOptions.TopLeft;
                note.textWrappingMode = TextWrappingModes.Normal;
                note.overflowMode = TextOverflowModes.Overflow;
                return;
            }

            const float DiagramHeight = 236f;
            var host = CreateRect("RangeDiagram", detailContent, new Vector2(0f, DiagramHeight));
            SetPreferredHeight(host, DiagramHeight);
            var background = host.gameObject.AddComponent<Image>();
            background.color = Ground;
            background.raycastTarget = false;

            var canvasHost = CreateStretched("Hexes", host);
            canvasHost.offsetMin = new Vector2(8f, 8f);
            canvasHost.offsetMax = new Vector2(-8f, -8f);
            var graphic = canvasHost.gameObject.AddComponent<CodexHexDiagramGraphic>();
            graphic.raycastTarget = false;
            graphic.SetDiagram(diagram);

            BuildRangeLegend(diagram);
            BuildStepRamp(diagram);

            if (source.SupportsDirection)
            {
                BuildRangeVariantRow(
                    "Direction",
                    "방향",
                    DirectionLabels,
                    (int)rangeDirection,
                    index =>
                    {
                        rangeDirection = (Map.Runtime.HexDirection)index;
                        RebuildDetail();
                    });
            }

            if (source.MaxBodyRadius > 0)
            {
                var labels = new string[source.MaxBodyRadius + 1];
                for (var i = 0; i < labels.Length; i++)
                {
                    labels[i] = i.ToString();
                }

                BuildRangeVariantRow(
                    "BodyRadius",
                    "몸 반경",
                    labels,
                    Mathf.Clamp(rangeBodyRadius, 0, source.MaxBodyRadius),
                    index =>
                    {
                        rangeBodyRadius = index;
                        RebuildDetail();
                    });
            }

            if (!string.IsNullOrWhiteSpace(diagram.Note))
            {
                var note = CreateText("RangeNote", detailContent, diagram.Note, 17f, TextMuted);
                note.alignment = TextAlignmentOptions.TopLeft;
                note.textWrappingMode = TextWrappingModes.Normal;
                note.overflowMode = TextOverflowModes.Overflow;
            }
        }

        /// <summary>색이 무엇을 뜻하는지 도해 밑에 적는다 — 파랑/노랑만 두면 어느 쪽이 명중인지 모른다.</summary>
        private void BuildRangeLegend(CodexRangeDiagram diagram)
        {
            var row = CreateRect("RangeLegend", detailContent, new Vector2(0f, 24f));
            SetPreferredHeight(row, 24f);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.spacing = 14f;

            if (diagram.RangeCells.Count > 0)
            {
                AddLegendChip(
                    row,
                    // 띠로 칠했으면 범례 색도 띠의 첫 칸이어야 한다 — 여기만 단색이면 색이 어긋나 보인다.
                    diagram.MaxRangeStep > 0
                        ? CodexHexDiagramGraphic.StepColor(1, diagram.MaxRangeStep)
                        : CodexHexDiagramGraphic.LegendRange,
                    string.IsNullOrEmpty(diagram.RangeLabel) ? "사거리" : diagram.RangeLabel,
                    diagram.RangeCells.Count);
            }

            if (diagram.BodyCells.Count > 0)
            {
                AddLegendChip(row, CodexHexDiagramGraphic.LegendBody, "몸통", diagram.BodyCells.Count);
            }

            if (diagram.ShapeCells.Count > 0)
            {
                AddLegendChip(
                    row,
                    CodexHexDiagramGraphic.LegendShape,
                    string.IsNullOrEmpty(diagram.ShapeLabel) ? "착탄" : diagram.ShapeLabel,
                    diagram.ShapeCells.Count);
            }
        }

        /// <summary>
        /// 걸음 수 띠의 눈금(1·2·3…). 이동 도해에만 붙는다 — 열린 벌판에서는 도달 칸이 곧 원판이라
        /// 한 색으로 칠하면 "사거리 N"과 그림이 구분되지 않는다.
        /// </summary>
        private void BuildStepRamp(CodexRangeDiagram diagram)
        {
            if (diagram.MaxRangeStep <= 1)
            {
                return;
            }

            var row = CreateRect("RangeStepRamp", detailContent, new Vector2(0f, 26f));
            SetPreferredHeight(row, 26f);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.spacing = 4f;

            var caption = CreateText("Caption", row, "걸음", 15f, TextDim);
            caption.alignment = TextAlignmentOptions.Left;
            var captionElement = caption.gameObject.AddComponent<LayoutElement>();
            captionElement.preferredWidth = 40f;
            captionElement.preferredHeight = 22f;

            for (var step = 1; step <= diagram.MaxRangeStep; step++)
            {
                var swatch = CreateRect($"Step_{step}", row, new Vector2(30f, 22f));
                var element = swatch.gameObject.AddComponent<LayoutElement>();
                element.preferredWidth = 30f;
                element.preferredHeight = 22f;
                var image = swatch.gameObject.AddComponent<Image>();
                image.color = CodexHexDiagramGraphic.StepColor(step, diagram.MaxRangeStep);
                image.raycastTarget = false;

                var label = CreateText("Label", swatch, step.ToString(), 14f, Sunken);
                label.alignment = TextAlignmentOptions.Center;
            }
        }

        private static void AddLegendChip(RectTransform parent, Color swatch, string label, int count)
        {
            var chip = CreateRect($"Legend_{label}", parent, new Vector2(0f, 24f));
            var element = chip.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = 24f;
            element.preferredWidth = 12f + 6f + label.Length * 15f + 26f;

            var dot = CreateRect("Dot", chip, new Vector2(12f, 12f));
            dot.anchorMin = new Vector2(0f, 0.5f);
            dot.anchorMax = new Vector2(0f, 0.5f);
            dot.pivot = new Vector2(0f, 0.5f);
            dot.anchoredPosition = new Vector2(0f, 0f);
            var dotImage = dot.gameObject.AddComponent<Image>();
            dotImage.color = swatch;
            dotImage.raycastTarget = false;

            var text = CreateText("Label", chip, $"{label} {count}", 15f, TextMuted);
            var textRect = (RectTransform)text.transform;
            textRect.offsetMin = new Vector2(18f, 0f);
            textRect.offsetMax = Vector2.zero;
            text.alignment = TextAlignmentOptions.Left;
        }

        /// <summary>
        /// 방향·몸 반경·패턴처럼 도해를 돌려 보는 축 하나를 버튼 줄로 낸다.
        /// <para>
        /// 줄이 넘치면 <b>다음 줄로 접는다</b> — 패턴 이름은 저작된 한글이라 길이를 예측할 수 없고,
        /// Unity 레이아웃에는 자동 줄바꿈이 없어 넘친 버튼은 그냥 화면 밖으로 나간다.
        /// </para>
        /// </summary>
        private void BuildRangeVariantRow(string name, string caption, IReadOnlyList<string> labels, int active, Action<int> onPick)
        {
            // 상세 패널 폭에서 좌우 여백과 캡션 자리를 뺀 값 — 이 안에 못 들어가면 줄을 바꾼다.
            const float CaptionWidth = 62f;
            const float Spacing = 6f;
            var available = DetailWidth - 52f - CaptionWidth - Spacing;

            RectTransform row = null;
            var used = 0f;
            var rowIndex = 0;

            for (var i = 0; i < labels.Count; i++)
            {
                var width = Mathf.Max(34f, 16f + labels[i].Length * 13f);

                if (row == null || used + width + Spacing > available)
                {
                    row = CreateRect($"Range{name}_{rowIndex}", detailContent, new Vector2(0f, 34f));
                    SetPreferredHeight(row, 34f);
                    // ⚠️ childControlWidth를 끄면 레이아웃이 LayoutElement를 무시하고 rect 크기를 그대로
                    // 쓴다 — CreateButton이 폭 0으로 만들어 두므로 줄이 통째로 사라진다(실측).
                    var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                    layout.childControlWidth = true;
                    layout.childControlHeight = true;
                    layout.childForceExpandWidth = false;
                    layout.childAlignment = TextAnchor.MiddleLeft;
                    layout.spacing = Spacing;

                    // 캡션은 첫 줄에만 적고, 접힌 줄은 같은 폭만큼 비워 버튼이 세로로 맞춰지게 한다.
                    var label = CreateText("Caption", row, rowIndex == 0 ? caption : string.Empty, 15f, TextDim);
                    label.alignment = TextAlignmentOptions.Left;
                    var labelElement = label.gameObject.AddComponent<LayoutElement>();
                    labelElement.preferredWidth = CaptionWidth;
                    labelElement.preferredHeight = 30f;

                    used = 0f;
                    rowIndex++;
                }

                var index = i;
                var button = CreateButton($"{name}_{i}", row, labels[i], new Vector2(0f, 30f));
                var element = button.gameObject.AddComponent<LayoutElement>();
                element.preferredWidth = width;
                element.preferredHeight = 30f;
                button.targetGraphic.color = index == active ? Line : Surface;
                button.onClick.AddListener(() => onPick(index));
                used += width + Spacing;
            }
        }

        private void BuildTitleBlock()
        {
            var block = CreateRect("TitleBlock", detailContent, new Vector2(0f, 0f));
            var layout = block.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.spacing = 3f;
            var fitter = block.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var title = CreateText("Title", block, selected.DisplayName, 31f, TextPrimary);
            title.alignment = TextAlignmentOptions.Left;
            title.textWrappingMode = TextWrappingModes.Normal;
            title.characterSpacing = -1.5f;

            if (string.IsNullOrWhiteSpace(selected.Subtitle))
            {
                return;
            }

            var subtitle = CreateText("Subtitle", block, selected.Subtitle, 16f, TextDim);
            subtitle.alignment = TextAlignmentOptions.Left;
            subtitle.characterSpacing = 3.5f;
        }

        /// <summary>정보 덩어리 사이의 가는 선. 간격만으로 나누면 어디까지가 설명인지 흐려진다.</summary>
        private void BuildDivider()
        {
            var rule = CreateRect("Divider", detailContent, new Vector2(0f, 1f));
            SetPreferredHeight(rule, 1f);
            var image = rule.gameObject.AddComponent<Image>();
            image.color = Line;
            image.raycastTarget = false;
        }

        private void BuildDetailRow(CodexDetailRow row)
        {
            var host = CreateRect($"Row_{row.Label}", detailContent, new Vector2(0f, 30f));
            SetPreferredHeight(host, 30f);

            var label = CreateText("Label", host, row.Label, 16f, TextDim);
            var labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(0.46f, 1f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            label.alignment = TextAlignmentOptions.Left;
            label.characterSpacing = 2f;

            var value = CreateText("Value", host, row.Value, 19f, TextPrimary);
            var valueRect = (RectTransform)value.transform;
            valueRect.anchorMin = new Vector2(0.46f, 0f);
            valueRect.anchorMax = new Vector2(1f, 1f);
            valueRect.offsetMin = Vector2.zero;
            valueRect.offsetMax = Vector2.zero;
            value.alignment = TextAlignmentOptions.Left;
        }

        /// <summary>도메인마다 셀 규격이 다르다 — 카드만 카드 모양(2:3)이고 나머지는 정사각 썸네일이다.</summary>
        private static Vector2 CellSize(ICodexDomain domain)
        {
            return domain != null && domain.Id == CodexCardDomain.DomainId
                ? CardCellSize
                : ThumbCellSize;
        }

        /// <summary>
        /// <c>CardFront</c> 프리팹을 세워 카드 한 장을 그린다. 프리팹 rect는 200×320이고 자식들이
        /// 그 크기에 맞춰 고정 오프셋으로 배치돼 있으므로, 크기를 바꾸는 대신 <b>통째로 축척</b>한다 —
        /// <c>sizeDelta</c>를 건드리면 뱃지와 일러가 제자리를 잃는다.
        /// </summary>
        private void PaintCard(RectTransform cell, Combat.Runtime.CombatCardSnapshot card, float width, string displayName, Color tone)
        {
            const float CardUnitWidth = 200f;
            const float CardUnitHeight = 320f;

            var prefab = CardFrontListInstance.ChoosePrefab(card.Kind, moveCardPrefab, actionCardPrefab);
            if (prefab == null)
            {
                // 배선이 빠졌다는 사실을 숨기지 않는다 — 빈 칸이 뜨면 데이터가 없는 것으로 오해한다.
                var missing = CreateRect("CardMissing", cell, new Vector2(width, CardUnitHeight * (width / CardUnitWidth)));
                PaintThumbnail(missing, CodexThumbnail.Resolve(null, displayName, tone), labelFontSize: 19f);
                return;
            }

            var scale = width / CardUnitWidth;
            var host = CreateRect("Card", cell, new Vector2(width, CardUnitHeight * scale));
            var view = CardFrontListInstance.Create(prefab, host, card, statusCardFrameSprite);
            if (view == null)
            {
                return;
            }

            var cardRect = (RectTransform)view.transform;
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.anchoredPosition = Vector2.zero;
            cardRect.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>
        /// 3층 폴백을 화면에 옮긴다. 전용 아트는 이미지로, 절차 폴백은 <b>이름 전체</b>를 앉힌
        /// 색조 판으로 그린다(Q9).
        /// </summary>
        private static void PaintThumbnail(RectTransform host, CodexThumbnail thumbnail, float labelFontSize)
        {
            var background = host.gameObject.AddComponent<Image>();

            if (thumbnail.Sprite != null)
            {
                background.color = Sunken;
                var art = CreateStretched("Art", host);
                art.offsetMin = new Vector2(8f, 8f);
                art.offsetMax = new Vector2(-8f, -8f);
                var artImage = art.gameObject.AddComponent<Image>();
                artImage.sprite = thumbnail.Sprite;
                artImage.preserveAspect = thumbnail.PreserveAspect;
                artImage.raycastTarget = false;
                return;
            }

            var tone = thumbnail.Tone;
            background.color = new Color(
                Mathf.Lerp(Sunken.r, tone.r, 0.18f),
                Mathf.Lerp(Sunken.g, tone.g, 0.18f),
                Mathf.Lerp(Sunken.b, tone.b, 0.18f),
                1f);

            var label = CreateText("FallbackLabel", host, thumbnail.Label, labelFontSize, tone);
            var labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(10f, 10f);
            labelRect.offsetMax = new Vector2(-10f, -10f);
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.Normal;
            // 긴 이름도 칸 안에서 읽혀야 한다 — 자르지 말고 줄여 맞춘다.
            label.enableAutoSizing = true;
            label.fontSizeMin = 12f;
            label.fontSizeMax = labelFontSize;
        }

        // ── UI 프리미티브 ──────────────────────────────────────────────────

        private static void SetPreferredHeight(RectTransform rect, float height)
        {
            var element = rect.gameObject.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            element.flexibleHeight = 0f;
        }

        private static void ClearChildren(RectTransform parent)
        {
            if (parent == null)
            {
                return;
            }

            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                if (Application.isPlaying)
                {
                    // 🔴 Destroy는 프레임 끝까지 미뤄진다 — 부모에서 먼저 떼지 않으면 그 한 프레임 동안
                    // 레이아웃 그룹이 죽은 자식까지 배치해 새 목록이 밀려 보인다.
                    child.transform.SetParent(null, false);
                    Destroy(child);
                }
                else
                {
                    DestroyImmediate(child);
                }
            }
        }

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

        /// <summary>
        /// 세로로 꽉 차고 가로 폭만 고정인 기둥. <paramref name="edge"/> 0이면 왼쪽, 1이면 오른쪽에 붙는다.
        /// 앵커를 바꾼 뒤 <c>sizeDelta</c>·<c>anchoredPosition</c>을 <b>둘 다</b> 명시하는 이유는,
        /// 스트레치 앵커에서 값을 물려받으면 오프셋이 남아 폭이 0이 되기 때문이다.
        /// </summary>
        private static RectTransform CreateColumn(string name, Transform parent, float edge, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(edge, 0f);
            rect.anchorMax = new Vector2(edge, 1f);
            rect.pivot = new Vector2(edge, 0.5f);
            rect.sizeDelta = new Vector2(width, 0f);
            rect.anchoredPosition = Vector2.zero;
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

        private static TMP_InputField CreateSearchField(RectTransform parent)
        {
            var rect = CreateRect("Search", parent, new Vector2(360f, 40f));
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(16f, 0f);

            var background = rect.gameObject.AddComponent<Image>();
            background.color = Ground;
            var outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = Line;
            outline.effectDistance = new Vector2(1f, 1f);

            var viewport = CreateStretched("TextArea", rect);
            viewport.offsetMin = new Vector2(12f, 4f);
            viewport.offsetMax = new Vector2(-12f, -4f);
            viewport.gameObject.AddComponent<RectMask2D>();

            var placeholder = CreateText("Placeholder", viewport, "이름 · id 검색", 19f, TextDim);
            placeholder.alignment = TextAlignmentOptions.Left;

            var text = CreateText("Text", viewport, string.Empty, 19f, TextPrimary);
            text.alignment = TextAlignmentOptions.Left;
            text.raycastTarget = true;

            var field = rect.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = viewport;
            field.textComponent = (TextMeshProUGUI)text;
            field.placeholder = placeholder;
            field.targetGraphic = background;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.caretColor = TextPrimary;
            field.customCaretColor = true;
            return field;
        }
    }
}

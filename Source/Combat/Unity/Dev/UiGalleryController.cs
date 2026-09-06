using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using TMPro;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity.Dev
{
    /// <summary>
    /// Dev-only viewer that displays every shipping UI screen from a single scene, driven by an IMGUI
    /// button list. Each <see cref="GalleryEntry"/> assembles synthetic data (<see cref="UiGallerySampleData"/>)
    /// and calls a view's public API directly — no combat runtime, no <c>GameplayHudBridge</c>. Only one
    /// entry is active at a time; switching hides the previous one.
    ///
    /// Gated by <see cref="DebugUiAvailable"/> (Editor / development builds only). Intended for
    /// Assets/Scenes/Dev/UiGallery.unity, which is not registered in EditorBuildSettings.
    /// See docs/ui-gallery-plan.md.
    /// </summary>
    public sealed class UiGalleryController : MonoBehaviour
    {
        private const string KoreanFontResourcePath = "Fonts/DNFForgedBlade-Light SDF";
        private const string CardRewardResourcePath = "UI/Prototype/Card Reward Overlay Root";

        [Header("Scene groups (assigned by UiGallerySceneBuilder)")]
        [Tooltip("Root holding the gameplay Canvas with CardLane + Deck overlay + phase dock instances.")]
        [SerializeField] private GameObject gameplayGroup;
        [Tooltip("Gameplay Canvas RectTransform under which on-demand overlays (reward) are instantiated.")]
        [SerializeField] private RectTransform gameplayCanvasRoot;
        [Tooltip("Root holding the lobby Canvas + ScreenLobby instance.")]
        [SerializeField] private GameObject lobbyGroup;
        [Tooltip("Root holding the story cutscene Canvas instance.")]
        [SerializeField] private GameObject cutsceneGroup;

        [Tooltip("Choice (갈림길) overlay instance placed in the gameplay Canvas; hidden until its entry runs.")]
        [SerializeField] private GameObject choiceOverlayRoot;

        [Header("On-demand prefabs (optional; Resources fallback used when null)")]
        [SerializeField] private GameObject cardRewardPrefab;
        [SerializeField] private GameObject gameOverPrefab;
        [SerializeField] private GameObject victoryPrefab;

        private GameplayCardLaneView cardLane;
        private DeckPileListOverlayView deckOverlay;
        private TurnPhaseDockView phaseDock;
        private BottomCardHudView bottomHud;
        private CardLaneStatusEffectDockView statusDock;
        private SidebarRuntimeView sidebar;
        private SidebarCalloutPanelController sidebarPanels;
        private BossHudView bossHud;
        private CardRewardPopupView rewardPopup;
        private GameObject gameOverInstance;
        private GameObject victoryInstance;
        private TMP_FontAsset koreanFont;

        private readonly List<GalleryEntry> entries = new List<GalleryEntry>();
        private Vector2 scroll;
        private string activeLabel = "(none)";

        /// <summary>Dev-UI gate mirroring CombatDebugControlPanel.DebugUiAvailable.</summary>
        public static bool DebugUiAvailable =>
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            true;
#else
            false;
#endif

        private struct GalleryEntry
        {
            public string Category;
            public string Label;
            public Action Show;
        }

        private void Awake()
        {
            koreanFont = Resources.Load<TMP_FontAsset>(KoreanFontResourcePath);

            if (gameplayGroup != null)
            {
                cardLane = gameplayGroup.GetComponentInChildren<GameplayCardLaneView>(true);
                deckOverlay = gameplayGroup.GetComponentInChildren<DeckPileListOverlayView>(true);
                phaseDock = gameplayGroup.GetComponentInChildren<TurnPhaseDockView>(true);
                bottomHud = gameplayGroup.GetComponentInChildren<BottomCardHudView>(true);
                statusDock = gameplayGroup.GetComponentInChildren<CardLaneStatusEffectDockView>(true);
                sidebar = gameplayGroup.GetComponentInChildren<SidebarRuntimeView>(true);
                sidebarPanels = gameplayGroup.GetComponentInChildren<SidebarCalloutPanelController>(true);
                bossHud = gameplayGroup.GetComponentInChildren<BossHudView>(true);
            }

            BuildEntries();
        }

        private void Start()
        {
            HideAll();
        }

        private void BuildEntries()
        {
            entries.Clear();

            // --- Deck pile overlays ---
            Add("덱", "덱 목록", () => OpenDeck(v => v.OpenDeckList()));
            Add("덱", "뽑을 카드(드로우)", () => OpenDeck(v => v.OpenDrawPile()));
            Add("덱", "버린 카드", () => OpenDeck(v => v.OpenDiscardPile()));

            // --- Card lane (hand) variants ---
            Add("카드 레인", "손패 0장", () => ShowHand(0, 0));
            Add("카드 레인", "손패 5장", () => ShowHand(3, 2));
            Add("카드 레인", "손패 10장", () => ShowHand(6, 4));
            // 카드 UI 판정용. ⚠️상태 카드 아우라는 현재 OFF다(2026-07-31 확정) — 이 엔트리는 지금
            // "전용 프레임이 없을 때 상태 카드가 얼마나 안 읽히는가"를 보여 준다. 프레임이 들어오면
            // 같은 엔트리가 그대로 프레임 판정에 쓰인다.
            Add("카드 레인", "상태 카드 2장 (프레임 대기)", () => ShowAfflictedHand(statusCardCount: 2, sealedCount: 0));
            Add("카드 레인", "봉인 2장 (빨간 X)", () => ShowAfflictedHand(statusCardCount: 0, sealedCount: 2));
            Add("카드 레인", "상태 카드+봉인 혼합", () => ShowAfflictedHand(statusCardCount: 2, sealedCount: 2));

            // --- Bottom HUD (full) ---
            if (bottomHud != null)
            {
                Add("하단 HUD", "전체 (HP·기력·더미·손패)", () => ShowBottomHud(UiGallerySampleData.CreateState()));
                Add("손패 선택", "버릴 카드 고르기 (0/2 선택)", () => ShowHandCardSelection(0));
                Add("손패 선택", "버릴 카드 고르기 (2/2 선택)", () => ShowHandCardSelection(2));
            }

            // --- Status effect dock ---
            if (statusDock != null)
            {
                Add("상태이상 도크", "1종", () => ShowStatusEffects(1));
                Add("상태이상 도크", "5종 (가득)", () => ShowStatusEffects(5));
                Add("상태이상 도크", "8종 (오버플로 +3)", () => ShowStatusEffects(8));
            }

            // --- Sidebar panels + inventory variants ---
            if (sidebar != null)
            {
                Add("사이드바", "가방 (아이템)", () => ShowSidebar("bag", UiGallerySampleData.CreateInventoryState(3, 1, emptyBag: false)));
                Add("사이드바", "가방 (빈 상태)", () => ShowSidebar("bag", UiGallerySampleData.CreateInventoryState(3, 1, emptyBag: true)));
                Add("사이드바", "덱", () => ShowSidebar("deck", UiGallerySampleData.CreateState()));
                Add("사이드바", "유물·저주 0개", () => ShowSidebar("relic_curse", UiGallerySampleData.CreateInventoryState(0, 0, emptyBag: true)));
                Add("사이드바", "유물·저주 다수 (5+3)", () => ShowSidebar("relic_curse", UiGallerySampleData.CreateInventoryState(5, 3, emptyBag: false)));
                Add("사이드바", "설정", () => ShowSidebar("settings", UiGallerySampleData.CreateState()));
            }

            // --- Choice (갈림길) overlay ---
            if (choiceOverlayRoot != null)
            {
                Add("갈림길", "갈림길 선택 (회복/공격)", ShowChoice);
            }

            // --- Game over / victory ---
            Add("종료", "게임 오버", () => ShowOverlayPrefab(ref gameOverInstance, gameOverPrefab));
            Add("종료", "승리", () => ShowOverlayPrefab(ref victoryInstance, victoryPrefab));

            // --- Turn phase banners ---
            Add("페이즈 배너", "게임 시작", () => Announce(v => v.AnnounceGameStart()));
            Add("페이즈 배너", "턴 시작 (1턴)", () => Announce(v => v.AnnounceOverallTurnStart(1)));
            Add("페이즈 배너", "플레이어 턴 시작", () => Announce(v => v.AnnouncePlayerTurnStart()));
            Add("페이즈 배너", "페이즈 전환", () => Announce(v => v.Announce(CombatPhase.MonsterAction, CombatPhase.PlayerMovement)));

            // --- Boss HUD ---
            if (bossHud != null)
            {
                Add("보스 HUD", "1페이즈 (만피)", () => ShowBossHud(1));
                Add("보스 HUD", "2페이즈", () => ShowBossHud(2));
                Add("보스 HUD", "3페이즈 (마지막)", () => ShowBossHud(3));
                Add("보스 HUD", "보스 없음 (숨김)", () => bossHud.Refresh(null));
            }

            // --- Card reward ---
            Add("카드 보상", "보상 3장", () => ShowReward(rerollAvailable: false));
            Add("카드 보상", "보상 3장 + 리롤", () => ShowReward(rerollAvailable: true));

            // --- 서비스 오브젝트 (캠핑카·공작소 공용 모달) ---
            if (gameplayCanvasRoot != null)
            {
                Add("서비스", "캠핑카 (회복·연마 택1)", () => ShowServicePopup(camperVan: true));
                Add("서비스", "공작소 (제거·연마 택1)", () => ShowServicePopup(camperVan: false));
            }

            // --- Story cutscene ---
            if (cutsceneGroup != null)
            {
                Add("컷씬", "스토리 컷씬 (기본)", ShowCutscene);
            }

            // --- Lobby ---
            if (lobbyGroup != null)
            {
                Add("로비", "ScreenLobby (기본)", ShowLobby);
            }
        }

        private void Add(string category, string label, Action show)
        {
            entries.Add(new GalleryEntry { Category = category, Label = label, Show = show });
        }

        // --- Automation hooks (used by capture tooling / manual verification) ---

        /// <summary>Number of registered gallery entries.</summary>
        public int EntryCount => entries.Count;

        /// <summary>"Category / Label" for the entry at <paramref name="index"/>, or empty if out of range.</summary>
        public string EntryLabelAt(int index) =>
            index >= 0 && index < entries.Count ? $"{entries[index].Category} / {entries[index].Label}" : string.Empty;

        /// <summary>Activate the entry at <paramref name="index"/> (hides the previous one first).</summary>
        public void ShowEntryByIndex(int index)
        {
            if (index >= 0 && index < entries.Count)
            {
                Select(entries[index]);
            }
        }

        /// <summary>Hide every screen and reset the active label.</summary>
        public void HideAllEntries()
        {
            HideAll();
            activeLabel = "(none)";
        }

        // --- Group visibility ---------------------------------------------------

        private void HideAll()
        {
            deckOverlay?.Close();
            cardLane?.RefreshHandCards(Array.Empty<CombatCardSnapshot>(), null, null, NoOpPlay);
            bottomHud?.Refresh(null, default, null, koreanFont, NoOpPlay);
            statusDock?.Refresh(Array.Empty<ActiveEffect>(), koreanFont);
            sidebarPanels?.HideAllPanels();
            rewardPopup?.Hide();
            SetActiveSafe(choiceOverlayRoot, false);
            SetActiveSafe(gameOverInstance, false);
            SetActiveSafe(victoryInstance, false);

            SetActiveSafe(gameplayGroup, true);
            SetActiveSafe(lobbyGroup, false);
            SetActiveSafe(cutsceneGroup, false);
        }

        private void Select(GalleryEntry entry)
        {
            HideAll();
            try
            {
                entry.Show?.Invoke();
                activeLabel = $"{entry.Category} / {entry.Label}";
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UiGallery] Entry '{entry.Label}' failed: {ex}");
                activeLabel = $"(error) {entry.Label}";
            }
        }

        // --- Entry implementations ---------------------------------------------

        // 서비스 모달(캠핑카·공작소) 데모. 컨트롤러 배선과 같은 콜백 모양으로 실제 CombatState의
        // 연마·제거 API를 그대로 태운다 — 갤러리 전용 분기가 없어 비교 화면·후보 필터 판정이
        // 출하 코드 그대로다. 서비스 사용/떠나기는 데모에선 그냥 모달을 닫는다.
        private void ShowServicePopup(bool camperVan)
        {
            var view = ServiceObjectPopupView.FindOrCreate(gameplayCanvasRoot);
            if (view == null)
            {
                return;
            }

            var state = UiGallerySampleData.CreateRefineServiceState();
            // 만피면 「이미 가득 찼습니다」만 뜬다 — 갤러리는 <b>정보가 다 들어간 쪽</b>을 보여야
            // 하므로 체력을 깎아 둔다(회복량과 현재 체력이 함께 읽히는 화면).
            state.Player.ApplyDamage(Mathf.Max(1, state.Player.MaxHp / 3));
            var refineOption = new ServiceObjectOptionModel(
                "refine", "카드 연마", "카드 한 장을 골라 한 단계 연마합니다 (카드당 1회)", ServiceOptionFlow.CardPickCompare);
            var firstOption = camperVan
                ? new ServiceObjectOptionModel(
                    "heal",
                    "체력 회복",
                    // 🔑 합성 문자열을 쓰지 않는다(2026-09-01 #11) — 갤러리는 <b>출하 UI를 그대로</b>
                    //    보여주는 자리인데 여기만 손으로 쓴 문안을 들면, 캡처가 출하 화면과 갈린다.
                    //    실제 화면과 같은 함수를 부른다.
                    MapCombatController.FormatCamperHealDetail(
                        state.Player.Hp, state.Player.MaxHp, state.GetCamperHealAmount()),
                    ServiceOptionFlow.Instant)
                : new ServiceObjectOptionModel(
                    "remove", "카드 제거", "덱에서 카드 한 장을 영구히 제거합니다", ServiceOptionFlow.CardPick);

            view.Show(
                camperVan ? ServiceObjectScreen.CamperVan : ServiceObjectScreen.Workshop,
                camperVan ? "캠핑카" : "공작소",
                "서비스 하나를 고르면 이용이 끝납니다.",
                new[] { firstOption, refineOption },
                instantRequested: _ =>
                {
                    view.Hide();
                    return true;
                },
                candidatesProvider: option => state.GetDeckListCards()
                    .Where(card => option.Id != "refine" || state.CanRefineCard(card.SelectionKey))
                    .Select(card => new ServiceCardCandidate(card.SelectionKey, card.Name))
                    .ToArray(),
                comparePairs: (option, candidate) =>
                    state.TryPreviewRefinedCardSnapshots(candidate.Key, out var before, out var after, out var reason)
                    && state.TryPreviewRefinedCard(candidate.Key, out var current, out var refined, out reason)
                        ? new ServiceCardComparePair(before, after, current, refined)
                        : default,
                cardConfirmed: (option, candidate) =>
                {
                    if (option.Id == "refine" && !state.TryRefineCard(candidate.Key, out _))
                    {
                        return false;
                    }

                    view.Hide();
                    return true;
                },
                leaveRequested: view.Hide);
        }

        private void OpenDeck(Action<DeckPileListOverlayView> open)
        {
            if (deckOverlay == null)
            {
                return;
            }

            var state = UiGallerySampleData.CreateState();
            deckOverlay.Refresh(state, state.CreatePlayerStateSnapshot(), koreanFont);
            open(deckOverlay);
        }

        private void ShowHand(int moveCount, int actionCount)
        {
            if (cardLane == null)
            {
                return;
            }

            var state = UiGallerySampleData.CreateState();
            var hand = UiGallerySampleData.CreateHand(moveCount, actionCount);
            cardLane.RefreshHandCards(hand, state, null, NoOpPlay);
        }

        // 오염(상태 카드)·봉인이 섞인 손패. ShowHand와 같은 경로(RefreshHandCards)를 태우므로 아우라·X를
        // 켜는 판정도 출하 코드가 그대로 한다 — 갤러리 전용 분기가 없다.
        private void ShowAfflictedHand(int statusCardCount, int sealedCount)
        {
            if (cardLane == null)
            {
                return;
            }

            var state = UiGallerySampleData.CreateState();
            var hand = UiGallerySampleData.CreateAfflictedHand(statusCardCount, sealedCount);
            cardLane.RefreshHandCards(hand, state, null, NoOpPlay);
        }

        private void Announce(Action<TurnPhaseDockView> announce)
        {
            if (phaseDock == null)
            {
                return;
            }

            announce(phaseDock);
        }

        private void ShowBottomHud(CombatState state)
        {
            bottomHud?.Refresh(state, state.CreatePlayerStateSnapshot(), null, koreanFont, NoOpPlay);
        }

        // The 손패 선택 panel only appears while a card is mid-resolution asking the player to pick cards, so it
        // had no gallery coverage — there was no way to reach that state without a live combat controller.
        // UiGalleryStubHudHost supplies just that slice of the HUD seam.
        private void ShowHandCardSelection(int preselectedCount)
        {
            if (bottomHud == null)
            {
                return;
            }

            var state = UiGallerySampleData.CreateState();
            var handKeys = state.GetCombatCards()
                .Select(card => card.SelectionKey)
                .Where(key => !string.IsNullOrEmpty(key))
                .Take(preselectedCount)
                .ToArray();

            var host = new UiGalleryStubHudHost(
                state,
                promptText: "버릴 카드를 2장 고르세요",
                minSelectCount: 2,
                maxSelectCount: 2,
                preselectedKeys: handKeys,
                selectionActive: true);

            bottomHud.Refresh(state, state.CreatePlayerStateSnapshot(), host, koreanFont, NoOpPlay);
        }

        // The status dock only fills up mid-run, so its layout (chips, the +N overflow badge, the expanded
        // overflow panel) had no gallery coverage. Driven directly here: Refresh takes a plain effect list.
        private void ShowStatusEffects(int count)
        {
            var kinds = (StatusEffectKind[])Enum.GetValues(typeof(StatusEffectKind));
            var effects = Enumerable.Range(0, count)
                .Select(i => new ActiveEffect(
                    EffectType.Duration,
                    kinds[i % kinds.Length],
                    "player",
                    remainingTurns: (i % 4) + 1,
                    amount: 1,
                    sourceRef: "ui-gallery"))
                .ToArray();
            statusDock?.Refresh(effects, koreanFont);
        }

        private void ShowSidebar(string panelKey, CombatState state)
        {
            if (sidebar == null)
            {
                return;
            }

            sidebar.Refresh(state, state.CreatePlayerStateSnapshot(), koreanFont);
            sidebarPanels?.ShowPanel(panelKey);
        }

        private void ShowChoice()
        {
            if (choiceOverlayRoot == null)
            {
                return;
            }

            SetActiveSafe(choiceOverlayRoot, true);
            var panel = FindDescendant(choiceOverlayRoot.transform, "PlayerState Choice Card Panel");
            if (panel != null)
            {
                panel.gameObject.SetActive(true);
            }

            var card = UiGallerySampleData.CreateChoiceCard();
            var model = ChoiceCardPanelModel.ForCard(card);
            var slots = choiceOverlayRoot.GetComponentsInChildren<ChoiceCardOptionSlot>(true);
            for (var i = 0; i < slots.Length; i++)
            {
                if (i < model.Options.Count)
                {
                    slots[i].Bind(model.SourceCard, model.Options[i], () => { });
                }
                else
                {
                    slots[i].gameObject.SetActive(false);
                }
            }

            var title = FindDescendant(choiceOverlayRoot.transform, "PlayerState Choice Card Panel Title");
            var titleText = title != null ? title.GetComponent<TMP_Text>() : null;
            if (titleText != null)
            {
                if (koreanFont != null)
                {
                    titleText.font = koreanFont;
                }

                titleText.text = $"선택 카드: {model.SourceCardId}";
            }
        }

        /// <summary>
        /// 보스 HUD를 실제 전투 상태로 갱신한다. 프리뷰 전용 우회 경로를 만들지 않고 출하 경로
        /// (<see cref="BossHudView.Refresh"/>)를 그대로 호출하므로, 갤러리에서 보이는 모습이 실제와 같다.
        /// </summary>
        private void ShowBossHud(int phase)
        {
            if (bossHud == null)
            {
                Debug.LogWarning("[UiGallery] Boss HUD instance missing; entry disabled.");
                return;
            }

            bossHud.Refresh(UiGallerySampleData.CreateBossState(phase));
        }

        private void ShowOverlayPrefab(ref GameObject instance, GameObject prefab)
        {
            if (prefab == null || gameplayCanvasRoot == null)
            {
                Debug.LogWarning("[UiGallery] Overlay prefab or gameplay canvas root missing; entry disabled.");
                return;
            }

            if (instance == null)
            {
                instance = Instantiate(prefab, gameplayCanvasRoot);
                instance.name = prefab.name + " (Gallery)";
            }

            instance.SetActive(true);
            instance.transform.SetAsLastSibling();
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                {
                    return t;
                }
            }

            return null;
        }

        private void ShowReward(bool rerollAvailable)
        {
            EnsureRewardPopup();
            if (rewardPopup == null)
            {
                return;
            }

            var offers = UiGallerySampleData.CreateRewardOffers();
            rewardPopup.Show(
                offers,
                onSelected: _ => { },
                onSkip: () => { },
                onHover: () => { },
                onRerollRequested: rerollAvailable
                    ? () => rewardPopup.ReplaceOffers(UiGallerySampleData.CreateRewardOffers(), true)
                    : (Action)null,
                rerollAvailable: rerollAvailable);
        }

        private void ShowCutscene()
        {
            SetActiveSafe(gameplayGroup, false);
            SetActiveSafe(lobbyGroup, false);
            // Re-enabling replays the cutscene from the start via the controller's OnEnable/Start.
            SetActiveSafe(cutsceneGroup, false);
            SetActiveSafe(cutsceneGroup, true);
        }

        private void ShowLobby()
        {
            SetActiveSafe(gameplayGroup, false);
            SetActiveSafe(cutsceneGroup, false);
            SetActiveSafe(lobbyGroup, true);
        }

        private void EnsureRewardPopup()
        {
            if (rewardPopup != null)
            {
                return;
            }

            var prefab = cardRewardPrefab != null
                ? cardRewardPrefab
                : Resources.Load<GameObject>(CardRewardResourcePath);
            if (prefab == null || gameplayCanvasRoot == null)
            {
                Debug.LogWarning("[UiGallery] Card reward prefab or gameplay canvas root missing; reward entry disabled.");
                return;
            }

            var instance = Instantiate(prefab, gameplayCanvasRoot);
            instance.name = "Card Reward Overlay Root (Gallery)";
            rewardPopup = instance.GetComponent<CardRewardPopupView>();
            rewardPopup?.AutoBindFromHierarchy();
        }

        private static void SetActiveSafe(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active)
            {
                go.SetActive(active);
            }
        }

        private static void NoOpPlay(CombatCardSnapshot _)
        {
        }

        // --- IMGUI panel --------------------------------------------------------

        private void OnGUI()
        {
            if (!DebugUiAvailable)
            {
                return;
            }

            const float width = 260f;
            var rect = new Rect(Screen.width - width - 12f, 12f, width, Screen.height - 24f);
            GUILayout.BeginArea(rect, GUI.skin.box);
            GUILayout.Label("<b>UI 갤러리</b>", RichLabel());
            GUILayout.Label($"활성: {activeLabel}");
            if (GUILayout.Button("전부 숨기기"))
            {
                HideAll();
                activeLabel = "(none)";
            }

            GUILayout.Space(6f);
            scroll = GUILayout.BeginScrollView(scroll);
            string lastCategory = null;
            foreach (var entry in entries)
            {
                if (entry.Category != lastCategory)
                {
                    GUILayout.Space(6f);
                    GUILayout.Label($"— {entry.Category} —");
                    lastCategory = entry.Category;
                }

                if (GUILayout.Button(entry.Label))
                {
                    Select(entry);
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static GUIStyle richLabel;

        private static GUIStyle RichLabel()
        {
            richLabel ??= new GUIStyle(GUI.skin.label) { richText = true };
            return richLabel;
        }
    }
}

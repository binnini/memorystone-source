using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    [RequireComponent(typeof(GameplaySceneContract))]
    public sealed class GameplayHudBridge : MonoBehaviour
    {
        private static readonly Color PrimaryTextColor = new Color(0.88f, 0.95f, 1f, 1f);
        private const string ChoiceOverlayRootName = "Choice Overlay Root";

        [SerializeField] private MapCombatController controller;
        [SerializeField] private PlayerStateDebugPanel debugPanel;
        [SerializeField] private bool refreshEveryFrame = true;
        [SerializeField] private CardHandLayoutMode cardHandLayoutMode = CardHandLayoutMode.Drawer;
        [SerializeField] private bool devUiVisible = true;
        [SerializeField] private SidebarRuntimeView sidebarRuntimeView;

        /// <summary>무대상 소모품 사용 확인창(2026-09-02 #16). 런타임 생성이라 씬 저작이 없다.</summary>
        private BagItemUseConfirmView bagItemConfirmView;
        [Tooltip("When enabled, runtime re-applies generated layout/color to an existing scene-authored choice panel. Newly created panels always get defaults.")]
        [SerializeField] private bool applyGeneratedChoicePanelLayout;

        private GameplaySceneContract contract;
        private readonly CombatHudCardSelectionBridge cardSelectionBridge = new CombatHudCardSelectionBridge();
        private Button endActionButton;
        private Button restartButton;
        private Button cardLayoutToggleButton;
        private TMP_Text playerStateDebugText;
        private DeckPileListOverlayView deckPileListOverlayView;
        private BottomCardHudView bottomCardHudView;
        private TutorialUiGlow tutorialUiGlow;
        // The pile overlay is shared (deck/draw/discard/exile), but its Closed event doesn't say which was
        // open. Remember the close event for whichever pile was opened last so the tutorial can tell them
        // apart (e.g. close the draw pile vs. close the discard pile).
        private string activeOverlayCloseEvent = "panel.deck.close";
        // Optional explicit references for rename safety. When assigned, they are used directly; when
        // null, the choice-panel resolvers fall back to the authored object names (unchanged behaviour).
        [Header("Choice Panel (optional explicit refs; name lookup is the fallback)")]
        [SerializeField] private RectTransform choiceOverlayRootRef;
        [SerializeField] private RectTransform choicePanelRootRef;
        [SerializeField] private TMP_Text choicePanelTitleRef;
        [SerializeField] private Button choiceCancelButtonRef;

        private RectTransform choicePanelRoot;
        private TMP_Text choicePanelTitle;
        private Button choiceCancelButton;
        private readonly List<Button> choiceOptionButtons = new List<Button>();

        public MapCombatController Controller => controller;
        public TMP_Text PlayerStateDebugText => playerStateDebugText;
        public CardHandLayoutMode CurrentCardHandLayoutMode => cardHandLayoutMode;
        public bool IsDevUiVisible => devUiVisible;

        private void Awake()
        {
            ResolveReferences();
            EnsurePlaytestControls();
            contract?.ConfigureMapClickThroughRaycastTargets();
            Refresh();
        }

        private void OnEnable()
        {
            ResolveReferences();
            EnsurePlaytestControls();
            contract?.ConfigureMapClickThroughRaycastTargets();
            Refresh();
        }

        private void Update()
        {
            if (refreshEveryFrame)
            {
                Refresh();
            }
        }

        public void Bind(MapCombatController mapCombatController, PlayerStateDebugPanel playerStateDebugPanel = null)
        {
            controller = mapCombatController;
            debugPanel = playerStateDebugPanel;
            ResolveReferences();
            EnsurePlaytestControls();
            contract?.ConfigureMapClickThroughRaycastTargets();
            Refresh();
        }

        public void RefreshForTests()
        {
            Refresh();
        }

        public void SetDevUiVisibleForTests(bool visible)
        {
            ResolveReferences();
            devUiVisible = visible;
            ApplyDevUiVisibility();
        }

        public void Refresh()
        {
            ResolveReferences();
            EnsurePlaytestControls();
            if (contract == null)
            {
                return;
            }

            var state = controller == null ? null : controller.State;
            var snapshot = state == null ? default : state.CreatePlayerStateSnapshot();

            ApplyDevUiVisibility();
            RefreshPlaytestControls(state);
            RefreshDeckPileListOverlay(state, snapshot);
            RefreshExternalBottomCardHud(state, snapshot);
            ConfigureExternalGameplayUiRouting(state);
            RefreshSidebar(state, snapshot);
            RefreshChoicePanel();
        }

        private void ResolveReferences()
        {
            if (contract == null)
            {
                contract = GetComponent<GameplaySceneContract>();
            }

            if (controller == null)
            {
                controller = FindFirstObjectByType<MapCombatController>();
            }

            if (debugPanel == null)
            {
                debugPanel = FindFirstObjectByType<PlayerStateDebugPanel>();
            }

            if (sidebarRuntimeView == null && contract != null)
            {
                sidebarRuntimeView = contract.GetComponentsInChildren<SidebarRuntimeView>(includeInactive: true)
                    .FirstOrDefault();
            }

            if (sidebarRuntimeView == null && Application.isPlaying)
            {
                var sidebar = FindRectTransform("Sidebar");
                if (sidebar != null)
                {
                    sidebarRuntimeView = sidebar.GetComponent<SidebarRuntimeView>();
                }
            }


            var gameplayLayers = FindRectTransform(GameplaySceneContract.GameplayLayerRootName);
            if (deckPileListOverlayView == null)
            {
                deckPileListOverlayView = DeckPileListOverlayView.FindOrCreate(gameplayLayers);
            }
            if (bagItemConfirmView == null)
            {
                bagItemConfirmView = BagItemUseConfirmView.FindOrCreate(gameplayLayers);
                bagItemConfirmView?.Hide();
            }
            if (deckPileListOverlayView != null)
            {
                deckPileListOverlayView.DeckFontLoader ??= TooltipFontProvider.Load;
                deckPileListOverlayView.KoreanFontLoader ??= KoreanFontProvider.Load;
            }


            if (bottomCardHudView == null && contract != null)
            {
                bottomCardHudView = contract.GetComponentsInChildren<BottomCardHudView>(includeInactive: true)
                    .FirstOrDefault();
            }
            if (bottomCardHudView != null)
            {
                bottomCardHudView.KoreanFontApplier ??= KoreanFontProvider.Apply;
            }
        }

        private void RefreshExternalBottomCardHud(CombatState state, PlayerStateSnapshot snapshot)
        {
            if (bottomCardHudView == null)
            {
                return;
            }

            // 카드 수치 미리보기의 겨눔 대상을 매 프레임 다시 밀어 넣는다. 유도값(IsAttackSelectionActive
            // && lastHoveredCoord)이라 선택을 취소하면 다음 프레임에 저절로 null이 되고, 취소 경로를
            // 하나 빠뜨려 수치가 부풀어 남는 사고가 구조적으로 불가능해진다.
            if (state != null)
            {
                state.CardPreviewTargetCoord = controller != null ? controller.CardPreviewTargetCoord : null;
            }

            // 저주 주입 연출(2026-09-01 #9)의 배선을 매 갱신에 다시 물린다 — 하단 HUD는 런타임에 서고
            // 비행 기구·뽑을 더미 자리는 그 뒤에야 생긴다. 유도값이라 늦게 도착해도 저절로 맞고,
            // 사라지면 연출만 조용히 접힌다(전리품 비행과 같은 계약).
            controller?.CurseCardInjectionPresenter.Configure(
                bottomCardHudView.FlightAnimator, bottomCardHudView.DrawPileDock);

            bottomCardHudView.Refresh(state, snapshot, controller, ResolveUiFont(), card => PlayCard(card));
        }


        private void RefreshDeckPileListOverlay(CombatState state, PlayerStateSnapshot snapshot)
        {
            if (deckPileListOverlayView == null)
            {
                return;
            }

            deckPileListOverlayView.Refresh(state, snapshot, ResolveUiFont());
        }

        private void ConfigureExternalGameplayUiRouting(CombatState state)
        {
            ConfigureSidebarDeckRouting(state);
            ConfigureBottomPileRouting(state);
            ConfigureBottomCombatControlRouting(state);
            ConfigureBagRouting(state);
            EnsureTutorialUiGlow();
        }

        // T4-1: 가방 슬롯 클릭 → 아이템 사용. 핸들러는 교체 방식이라 라우팅 재구성이 중복을 쌓지 않는다.
        private void ConfigureBagRouting(CombatState state)
        {
            var bagView = sidebarRuntimeView != null ? sidebarRuntimeView.BagPanelView : null;
            if (bagView == null)
            {
                return;
            }

            bagView.SetUseItemHandler(itemId =>
            {
                var currentState = ResolveCurrentCombatState(state);
                if (currentState == null)
                {
                    return false;
                }

                ConsumableItemCatalog.TryGet(itemId, out var item);

                // 대상 지정형(T4-2)은 즉발 대신 타게팅 모드로 — 다음 맵 클릭이 대상이 된다.
                // 그 사이가 곧 취소 창이므로 확인창을 또 세우지 않는다(2026-09-02 #16).
                if (item != null && item.Targeting != ConsumableItemTargeting.None)
                {
                    // 🔴 패널을 <b>닫아야</b> 한다(2026-09-05 실플레이: "구슬 소모품이 사용 안 되는 것들이 있음").
                    // 타게팅은 「다음 맵 클릭이 대상」인데, 가방 패널이 열린 채로 남아 맵을 덮고 있어서
                    // 그 클릭이 영영 도착하지 않았다 — 무대상 아이템은 확인창이 떠서 멀쩡해 보였고,
                    // 대상 지정형 3종(화염·혼미·결계)만 조용히 아무 일도 안 일어났다.
                    if (controller == null || !controller.BeginBagItemTargeting(itemId))
                    {
                        return false;
                    }

                    sidebarRuntimeView?.PanelController?.HideAllPanels();
                    return true;
                }

                // 무대상 아이템만 확인을 묻는다 — 예전에는 클릭 한 번이 곧 소모라 되돌릴 수 없었다(#16).
                var confirmView = ResolveBagItemConfirmView();
                if (confirmView == null)
                {
                    // 확인창을 세울 자리가 없으면(계약 미배선) 예전 동작으로 떨어진다 — 물건을
                    // 못 쓰게 만드는 것보다 낫다.
                    return currentState.TryUseBagItem(itemId);
                }

                var capturedItemId = itemId;
                confirmView.Show(
                    item,
                    itemId,
                    ResolveBagItemIcon(item),
                    () =>
                    {
                        var liveState = ResolveCurrentCombatState(state);
                        return liveState != null && liveState.TryUseBagItem(capturedItemId);
                    });
                // 확인창을 열었다는 것 자체가 성공이다 — 실제 사용은 「사용」을 눌렀을 때 일어난다.
                return true;
            });
        }

        private BagItemUseConfirmView ResolveBagItemConfirmView()
        {
            if (bagItemConfirmView != null)
            {
                return bagItemConfirmView;
            }

            return bagItemConfirmView =
                BagItemUseConfirmView.FindOrCreate(FindRectTransform(GameplaySceneContract.GameplayLayerRootName));
        }

        /// <summary>
        /// 확인창에 세울 아이콘. 사이드바 칸과 <b>같은 해소 경로</b>를 쓴다 —
        /// 🔴 열쇠는 아이템 id가 아니라 저작된 <c>IconId</c>다(두 벌로 해소하면 조용히 갈린다).
        /// </summary>
        private static Sprite ResolveBagItemIcon(ConsumableItemDefinition item)
        {
            return item == null || string.IsNullOrWhiteSpace(item.IconId)
                ? null
                : SeoulPlayup.Cards.Unity.RuntimeUiAssetCatalog.LoadItemIcon(item.IconId);
        }

        // Runtime-hosts the tutorial UI-button glow on this bridge object (no scene/prefab authoring) and keeps
        // its injected references current. The glow polls MapCombatController.TutorialHighlightUiTargetId itself,
        // so nothing else needs to drive it per step.
        private void EnsureTutorialUiGlow()
        {
            if (tutorialUiGlow == null)
            {
                tutorialUiGlow = GetComponent<TutorialUiGlow>() ?? gameObject.AddComponent<TutorialUiGlow>();
            }

            tutorialUiGlow.Configure(controller, bottomCardHudView, contract, deckPileListOverlayView);
        }

        private void ConfigureSidebarDeckRouting(CombatState state)
        {
            if (contract == null || deckPileListOverlayView == null)
            {
                return;
            }

            // Forward deck-panel close (close button / backdrop / Esc) to the tutorial so a
            // "close the deck panel" step can advance. Re-subscribe defensively (routing can be
            // reconfigured) without stacking duplicate handlers.
            deckPileListOverlayView.Closed -= HandleDeckOverlayClosed;
            deckPileListOverlayView.Closed += HandleDeckOverlayClosed;

            foreach (var button in contract.GetComponentsInChildren<SidebarPanelButton>(includeInactive: true))
            {
                button.BindExternalClickHandler(key =>
                {
                    // Unify settings UX: while the pause menu exists (MainGameplay scene), the sidebar
                    // "settings" button opens that central menu instead of the small callout, so there is
                    // one settings surface. The callout panel object stays intact (never deleted) so the
                    // pause menu's runtime clone of SoundSettingsPanelView still resolves. In sandbox
                    // scenes with no pause menu (ActiveInstance == null) the default callout is preserved.
                    if (key == "settings" && CombatPauseMenuController.ActiveInstance != null)
                    {
                        button.Controller?.HideAllPanels();
                        CombatPauseMenuController.ActiveInstance.Open();
                        return true;
                    }

                    if (key != "deck")
                    {
                        return false;
                    }

                    var currentState = ResolveCurrentCombatState(state);
                    if (currentState == null)
                    {
                        return true;
                    }

                    deckPileListOverlayView.Refresh(currentState, currentState.CreatePlayerStateSnapshot(), ResolveUiFont());
                    button.Controller?.HideAllPanels();
                    deckPileListOverlayView.OpenDeckList();
                    activeOverlayCloseEvent = "panel.deck.close";
                    controller?.NotifyTutorialCombatEvent("panel.deck");
                    return true;
                });
            }
        }

        private void HandleDeckOverlayClosed()
        {
            controller?.NotifyTutorialCombatEvent(activeOverlayCloseEvent);
        }

        private void ConfigureBottomPileRouting(CombatState state)
        {
            if (bottomCardHudView == null || deckPileListOverlayView == null)
            {
                return;
            }

            bottomCardHudView.ConfigurePileClickActions(
                () =>
                {
                    var currentState = ResolveCurrentCombatState(state);
                    if (currentState == null)
                    {
                        return;
                    }

                    deckPileListOverlayView.Refresh(currentState, currentState.CreatePlayerStateSnapshot(), ResolveUiFont());
                    deckPileListOverlayView.OpenDrawPile();
                    activeOverlayCloseEvent = "panel.drawpile.close";
                    controller?.NotifyTutorialCombatEvent("panel.drawpile");
                },
                () =>
                {
                    var currentState = ResolveCurrentCombatState(state);
                    if (currentState == null)
                    {
                        return;
                    }

                    deckPileListOverlayView.Refresh(currentState, currentState.CreatePlayerStateSnapshot(), ResolveUiFont());
                    deckPileListOverlayView.OpenDiscardPile();
                    activeOverlayCloseEvent = "panel.discardpile.close";
                    controller?.NotifyTutorialCombatEvent("panel.discardpile");
                },
                () =>
                {
                    var currentState = ResolveCurrentCombatState(state);
                    if (currentState == null)
                    {
                        return;
                    }

                    deckPileListOverlayView.Refresh(currentState, currentState.CreatePlayerStateSnapshot(), ResolveUiFont());
                    deckPileListOverlayView.OpenExilePile();
                    activeOverlayCloseEvent = "panel.exilepile.close";
                    controller?.NotifyTutorialCombatEvent("panel.exilepile");
                });
        }

        private CombatState ResolveCurrentCombatState(CombatState fallback)
        {
            if (controller == null)
            {
                ResolveReferences();
            }

            if (controller != null && controller.State == null && Application.isPlaying)
            {
                controller.InitializeIntegration();
            }

            return controller == null ? fallback : controller.State ?? fallback;
        }

        private void ConfigureBottomCombatControlRouting(CombatState state)
        {
            if (bottomCardHudView == null)
            {
                return;
            }

            bottomCardHudView.ConfigureEndAction(state == null ? null : (System.Action)(() => controller?.EndAction()));
        }

        private void RefreshSidebar(CombatState state, PlayerStateSnapshot snapshot)
        {
            sidebarRuntimeView?.Refresh(state, snapshot, ResolveUiFont());
        }

        private void PlayCard(CombatCardSnapshot card)
        {
            if (controller == null || controller.State == null || controller.State.IsTerminal || controller.IsSequencePlaying)
            {
                return;
            }

            if (!IsCardInteractionEnabled(card, controller.State))
            {
                return;
            }

            cardSelectionBridge.PlayCard(controller, card);
            Refresh();
        }

        private void EnsurePlaytestControls()
        {
            if (contract == null)
            {
                return;
            }

            DestroyLegacyPlaytestButton("PlayerState Playtest End Action Button");
            DestroyLegacyPlaytestButton("PlayerState Playtest Restart Button");
            endActionButton = null;

            var devLayer = FindLayer(GameplaySceneContract.DevOnlyLayerName);
            if (devLayer != null)
            {
                restartButton = EnsureButton(
                    devLayer,
                    "PlayerState Dev Restart Button",
                    "Restart",
                    new Vector2(-118f, 10f),
                    restartButton,
                    () => controller?.RestartDemo());

                cardLayoutToggleButton = EnsureButton(
                    devLayer,
                    "PlayerState Dev Toggle Card Layout Button",
                    GetCardLayoutToggleButtonText(),
                    new Vector2(-14f, 10f),
                    cardLayoutToggleButton,
                    ToggleCardHandLayoutMode);
                SetButtonLabel(cardLayoutToggleButton, GetCardLayoutToggleButtonText());
            }

            if (devLayer != null && playerStateDebugText == null)
            {
                playerStateDebugText = devLayer.GetComponentsInChildren<TMP_Text>(includeInactive: true)
                    .FirstOrDefault(text => text.name == GameplaySceneContract.PlayerStateDebugTextName);
            }
        }

        private void DestroyLegacyPlaytestButton(string objectName)
        {
            if (string.IsNullOrEmpty(objectName) || contract == null)
            {
                return;
            }

            var target = contract.GetComponentsInChildren<Button>(includeInactive: true)
                .FirstOrDefault(button => button.name == objectName);
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target.gameObject);
            }
            else
            {
                DestroyImmediate(target.gameObject);
            }
        }

        private void ToggleCardHandLayoutMode()
        {
            cardHandLayoutMode = cardHandLayoutMode == CardHandLayoutMode.Drawer
                ? CardHandLayoutMode.Rail
                : CardHandLayoutMode.Drawer;
            Refresh();
        }

        private string GetCardLayoutToggleButtonText()
        {
            return cardHandLayoutMode == CardHandLayoutMode.Drawer ? "Use Rail" : "Use Drawer";
        }

        private static string GetEndActionButtonText(CombatState state)
        {
            if (state == null)
            {
                return "대기";
            }

            switch (state.Phase)
            {
                case CombatPhase.PlayerMovement:
                    return "이동 종료";
                case CombatPhase.MonsterMovement:
                    return "몬스터 이동";
                case CombatPhase.PlayerAction:
                    return "행동 종료";
                default:
                    return "대기";
            }
        }

        private void RefreshPlaytestControls(CombatState state)
        {
            if (endActionButton != null)
            {
                endActionButton.interactable = state != null && !state.IsTerminal && (state.Phase == CombatPhase.PlayerMovement || state.Phase == CombatPhase.PlayerAction);
                SetButtonLabel(endActionButton, GetEndActionButtonText(state));
            }

            if (restartButton != null)
            {
                restartButton.interactable = controller != null;
            }
        }



        private void RefreshChoicePanel()
        {
            if (contract == null)
            {
                return;
            }

            var model = controller == null
                ? new ChoiceCardPanelModel(string.Empty, System.Array.Empty<ChoiceCardOptionModel>())
                : controller.PendingChoicePanel;
            var hasChoices = model.Options != null && model.Options.Count > 0;
            if (!hasChoices)
            {
                if (choicePanelRoot != null)
                {
                    choicePanelRoot.gameObject.SetActive(false);
                }

                return;
            }

            EnsureChoicePanelRoot();
            if (choicePanelRoot == null)
            {
                return;
            }

            choicePanelRoot.gameObject.SetActive(true);

            if (choicePanelTitle != null)
            {
                KoreanFontProvider.ApplyTitle(choicePanelTitle);
                var sourceCardName = string.IsNullOrWhiteSpace(model.SourceCard.Name)
                    ? model.SourceCardId
                    : model.SourceCard.Name;
                choicePanelTitle.text = $"선택 카드: {sourceCardName}";
            }

            EnsureChoiceOptionButtons(model.Options.Count);
            for (var i = 0; i < choiceOptionButtons.Count; i++)
            {
                var button = choiceOptionButtons[i];
                var active = i < model.Options.Count;
                button.gameObject.SetActive(active);
                if (!active)
                {
                    continue;
                }

                var option = model.Options[i];
                var optionSlot = button.GetComponent<ChoiceCardOptionSlot>();
                if (optionSlot != null)
                {
                    optionSlot.Bind(model.SourceCard, option, () => PlayChoiceOption(option));
                }
                else if (!BindChoiceOptionCardFront(button.transform, model.SourceCard, option, () => PlayChoiceOption(option)))
                {
                    var labels = button.GetComponentsInChildren<TMP_Text>(includeInactive: true);
                    var label = labels.FirstOrDefault(text => text.name.Contains("Label"));
                    if (label != null)
                    {
                        // Bind content only; font size is owned by the authored/created label style.
                        KoreanFontProvider.Apply(label);
                        label.text = $"{option.DisplayName}\n{option.CardText}";
                    }
                }

                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => PlayChoiceOption(option));
            }
        }

        private void EnsureChoicePanelRoot()
        {
            if (choicePanelRoot != null)
            {
                WireChoiceCancelButton(choicePanelRoot.gameObject);
                return;
            }

            var parent = FindChoiceOverlayRoot();
            if (parent == null)
            {
                Debug.LogWarning("Choice Overlay Root is missing; choice-card panel cannot be rendered.");
                return;
            }
            parent.gameObject.SetActive(true);

            var existing = choicePanelRootRef != null
                ? choicePanelRootRef
                : contract.GetComponentsInChildren<RectTransform>(includeInactive: true)
                    .FirstOrDefault(rect => rect.name == "PlayerState Choice Card Panel");
            if (existing == null)
            {
                Debug.LogWarning("PlayerState Choice Card Panel is missing; authored Choice Overlay prefab is required.", this);
                return;
            }

            var rootObject = existing.gameObject;
            rootObject.transform.SetParent(parent, false);
            choicePanelRoot = rootObject.GetComponent<RectTransform>();
            if (applyGeneratedChoicePanelLayout)
            {
                choicePanelRoot.anchorMin = new Vector2(0.5f, 0f);
                choicePanelRoot.anchorMax = new Vector2(0.5f, 0f);
                choicePanelRoot.pivot = new Vector2(0.5f, 0f);
                choicePanelRoot.anchoredPosition = new Vector2(0f, 54f);
                choicePanelRoot.sizeDelta = new Vector2(360f, 118f);
            }
            var image = rootObject.GetComponent<Image>();
            if (image != null && applyGeneratedChoicePanelLayout)
            {
                image.color = new Color(0.04f, 0.09f, 0.12f, 0.94f);
            }
            if (image != null)
            {
                image.raycastTarget = true;
            }
            choicePanelTitle = choicePanelTitleRef != null
                ? choicePanelTitleRef
                : rootObject.GetComponentsInChildren<TMP_Text>(includeInactive: true)
                    .FirstOrDefault(text => text.name == "PlayerState Choice Card Panel Title");
            WireChoiceCancelButton(rootObject);
        }

        private void WireChoiceCancelButton(GameObject rootObject)
        {
            if (rootObject == null)
            {
                return;
            }

            choiceCancelButton = choiceCancelButtonRef != null
                ? choiceCancelButtonRef
                : rootObject.GetComponentsInChildren<Button>(includeInactive: true)
                    .FirstOrDefault(button => button.name == "CancelButton");
            if (choiceCancelButton == null)
            {
                return;
            }

            choiceCancelButton.onClick.RemoveAllListeners();
            choiceCancelButton.onClick.AddListener(CancelChoiceSelection);
        }

        private void CancelChoiceSelection()
        {
            if (controller == null)
            {
                return;
            }

            controller.CancelChoiceCardSelection();
            Refresh();
        }

        private RectTransform FindChoiceOverlayRoot()
        {
            if (contract == null)
            {
                return null;
            }

            var root = choiceOverlayRootRef;
            if (root == null)
            {
                root = contract.GetComponentsInChildren<RectTransform>(includeInactive: true)
                    .FirstOrDefault(rect => rect.name == ChoiceOverlayRootName);
            }
            if (root == null)
            {
                return null;
            }

            root.gameObject.SetActive(true);
            return root;
        }

        private void EnsureChoiceOptionButtons(int count)
        {
            if (choicePanelRoot == null)
            {
                return;
            }

            if (choiceOptionButtons.Count == 0)
            {
                choiceOptionButtons.AddRange(choicePanelRoot.GetComponentsInChildren<Button>(includeInactive: true)
                    .Where(button => button.name.StartsWith("PlayerState Choice Option ", System.StringComparison.Ordinal))
                    .OrderBy(button => button.name));
            }

            if (choiceOptionButtons.Count < count)
            {
                Debug.LogWarning($"Choice panel has {choiceOptionButtons.Count} authored option buttons but {count} options were requested.", this);
            }
        }

        private void PlayChoiceOption(ChoiceCardOptionModel option)
        {
            if (controller == null)
            {
                return;
            }

            if (option.TargetMode == CardTargetMode.Enemy)
            {
                controller.BeginChoiceOptionTargetSelection(option.OptionId);
            }
            else
            {
                controller.PlayChoiceOption(option.OptionId);
            }

            Refresh();
        }

        private static bool BindChoiceOptionCardFront(Transform optionRoot, CombatCardSnapshot sourceCard, ChoiceCardOptionModel option, Action onClicked)
        {
            if (optionRoot == null || string.IsNullOrWhiteSpace(sourceCard.Id))
            {
                return false;
            }

            var texts = optionRoot.GetComponentsInChildren<TMP_Text>(includeInactive: true);
            if (texts == null || texts.Length == 0)
            {
                return false;
            }

            var boundAny = false;
            boundAny |= SetCardFrontText(texts, "CardNameText_TMP", option.DisplayName);
            boundAny |= SetCardFrontText(texts, "DescriptionText_TMP", option.CardText);
            boundAny |= SetCardFrontText(texts, "CostText_TMP", sourceCard.KiCost.ToString(System.Globalization.CultureInfo.InvariantCulture));
            boundAny |= SetCardFrontText(texts, "TypeText_TMP", CardFrontPresentationFormatting.KindLabel(sourceCard));
            CardFrontPresentationFormatting.RefreshCardRange(optionRoot, sourceCard);
            boundAny |= SetCardFrontText(texts, "StatusText_TMP", string.Empty);
            var interaction = optionRoot.GetComponentInChildren<HandCardInteraction>(includeInactive: true);
            if (interaction != null)
            {
                interaction.Initialize(null, (_, __) => onClicked?.Invoke());
                interaction.Configure(
                    new CombatCardSnapshot(
                        sourceCard.Id,
                        sourceCard.Kind,
                        option.DisplayName,
                        option.CardText,
                        sourceCard.Value,
                        isUsable: true,
                        isDiscarded: false,
                        string.Empty,
                        sourceCard.KiCost,
                        sourceCard.Range,
                        sourceCard.Pile,
                        sourceCard.CatalogSourceId,
                        sourceCard.EffectRef,
                        sourceCard.PhaseAvailability,
                        sourceCard.PlayMode,
                        sourceCard.FieldObjectKind,
                        sourceCard.DurationTurns,
                        sourceCard.AreaRadius,
                        sourceCard.InstanceId,
                        sourceCard.UpgradeLevel,
                        sourceCard.IsTemporary,
                        sourceCard.ChoiceOptions,
                        sourceCard.ChoiceOptionTexts,
                        sourceCard.IllustrationId,
                        isStatusCard: sourceCard.IsStatusCard),
                    playable: true,
                    selected: false);
            }

            return boundAny;
        }

        private static bool SetCardFrontText(IEnumerable<TMP_Text> texts, string objectName, string value)
        {
            var text = texts.FirstOrDefault(candidate => candidate != null && candidate.name == objectName);
            if (text == null)
            {
                return false;
            }

            KoreanFontProvider.Apply(text);
            text.text = value ?? string.Empty;
            return true;
        }

        private RectTransform FindLayer(string layerName)
        {
            return contract.GameplayLayers == null
                ? null
                : contract.GameplayLayers.FirstOrDefault(layer => layer != null && layer.name == layerName);
        }

        private static Button EnsureButton(RectTransform parent, string name, string label, Vector2 anchoredPosition, Button existing, UnityEngine.Events.UnityAction action)
        {
            var buttonObject = existing == null
                ? parent.GetComponentsInChildren<Button>(includeInactive: true)
                    .FirstOrDefault(button => button.name == name)?.gameObject
                : existing.gameObject;
            if (buttonObject == null)
            {
                buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
                var image = buttonObject.GetComponent<Image>();
                image.color = new Color(0.08f, 0.16f, 0.2f, 0.92f);
                CreateText(label + " Label", buttonObject.transform, label, 12, FontStyles.Bold, PrimaryTextColor, new Vector2(8f, -6f), new Vector2(88f, 18f));
            }

            buttonObject.transform.SetParent(parent, false);
            var rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(96f, 26f);

            var button = buttonObject.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
            return button;
        }

        private static void SetButtonLabel(Button button, string label)
        {
            if (button == null)
            {
                return;
            }

            var text = button.GetComponentsInChildren<TMP_Text>(includeInactive: true)
                .FirstOrDefault();
            if (text != null)
            {
                text.text = label;
            }
        }

        private void ApplyDevUiVisibility()
        {
            var devRoot = contract == null ? null : contract.DevUiRoot;
            if (devRoot != null)
            {
                devRoot.gameObject.SetActive(devUiVisible);
            }
        }

        private TMP_FontAsset ResolveUiFont()
        {
            return contract?.TopHudText != null
                ? contract.TopHudText.font
                : contract?.BottomResourceText?.font;
        }

        private RectTransform FindRectTransform(string name)
        {
            return contract == null
                ? null
                : contract.GetComponentsInChildren<RectTransform>(includeInactive: true)
                    .FirstOrDefault(rect => rect.name == name);
        }

        private static TMP_Text CreateText(string name, Transform parent, string text, int fontSize, FontStyles style, Color color, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            var rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;

            var label = textObject.GetComponent<TMP_Text>();
            label.text = text;
            KoreanFontProvider.Apply(label);
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.color = color;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;
            return label;
        }

        private static bool IsCardInteractionEnabled(CombatCardSnapshot card, CombatState state)
        {
            if (state == null || card.IsDiscarded)
            {
                return false;
            }

            return card.IsUsable;
        }


    }
}




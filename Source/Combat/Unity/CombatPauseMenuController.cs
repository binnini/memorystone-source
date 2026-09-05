using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SeoulPlayup.Combat.Unity
{
    // P5 pause menu (A2): ESC toggles an in-game pause overlay with resume / sound settings /
    // return-to-lobby (with save notice) / quit. Pausing is input-block only — Time.timeScale is
    // never touched here so the CombatHitStopController save/restore pairs stay the single
    // timeScale authority; a presentation sequence that is already running settles naturally
    // behind the dim while MapCombatController.SetPauseMenuInputBlocked holds new input.
    // The overlay is built in code (GameplayLoadingCanvas precedent) so the scene needs no edits;
    // MainGameplayController creates this via GetOrCreate at scene start.
    [DisallowMultipleComponent]
    public sealed class CombatPauseMenuController : MonoBehaviour
    {
        private const string HostObjectName = "Pause Menu";
        // Above the gameplay HUD/overlays, below GameplayLoadingCanvas (5000).
        // Above the tutorial canvas (4500) so pausing mid-tutorial is not buried under the spotlight dim.
        private const int CanvasSortingOrder = 4600;
        private static readonly Color PanelColor = new Color(0.07f, 0.07f, 0.09f, 0.97f);
        private static readonly Color ButtonColor = new Color(0.13f, 0.13f, 0.16f, 1f);
        private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.62f);

        [SerializeField] private MapCombatController combatController;

        private Canvas canvas;
        private RectTransform mainPage;
        private RectTransform soundPage;
        private RectTransform soundContentRoot;
        private RectTransform displayPage;
        private TMP_Text fullscreenToggleLabel;
        private RectTransform confirmPage;
        private TMP_Text confirmMessageLabel;
        private System.Action pendingConfirmAction;
        private SoundSettingsPanelView soundSettingsClone;
        private PlayerRunSaveStore saveStore;

        // Lets SidebarRuntimeView cede its legacy ESC-opens-settings shortcut whenever a
        // pause menu is hosting ESC in this scene (MainGameplay flow); legacy scenes without one
        // (PrototypeTest sandbox) keep the old behavior.
        public static CombatPauseMenuController ActiveInstance { get; private set; }

        public bool IsOpen => canvas != null && canvas.gameObject.activeSelf;

        public static CombatPauseMenuController GetOrCreate(MapCombatController combat)
        {
            var existing = FindFirstObjectByType<CombatPauseMenuController>(FindObjectsInactive.Include);
            if (existing == null)
            {
                existing = new GameObject(HostObjectName).AddComponent<CombatPauseMenuController>();
            }

            if (combat != null)
            {
                existing.combatController = combat;
            }

            return existing;
        }

        private void OnEnable()
        {
            if (ActiveInstance == null)
            {
                ActiveInstance = this;
            }
        }

        private void OnDisable()
        {
            if (ActiveInstance == this)
            {
                ActiveInstance = null;
            }

            Close();
        }

        private void Update()
        {
            if (!CombatInputRouter.WasPauseMenuShortcutPressed())
            {
                return;
            }

            // A deck/discard pile overlay consumes ESC to close itself; keep that priority
            // (same guard as the legacy sidebar shortcut).
            if (DeckPileListOverlayView.EscConsumedFrame == Time.frameCount)
            {
                return;
            }

            var pileOverlay = FindFirstObjectByType<DeckPileListOverlayView>();
            if (pileOverlay != null && pileOverlay.IsOpen)
            {
                return;
            }

            // 상점 모달도 같은 우선순위: 열려 있는 동안의 ESC는 상점이 소비한다.
            if (ShopPopupView.EscConsumedFrame == Time.frameCount)
            {
                return;
            }

            var shopPopup = FindFirstObjectByType<ShopPopupView>();
            if (shopPopup != null && shopPopup.IsOpen)
            {
                return;
            }

            // 서비스 모달(캠핑카·공작소)도 같은 우선순위: 열려 있는 동안의 ESC는 모달이 소비한다.
            if (ServiceObjectPopupView.EscConsumedFrame == Time.frameCount)
            {
                return;
            }

            var servicePopup = FindFirstObjectByType<ServiceObjectPopupView>();
            if (servicePopup != null && servicePopup.IsOpen)
            {
                return;
            }

            if (IsOpen)
            {
                HandleEscapeWhileOpen();
                return;
            }

            Open();
        }

        public void Open()
        {
            ResolveCombatController();
            if (combatController == null || !combatController.IsInitialized)
            {
                return;
            }

            EnsureOverlay();
            ShowPage(mainPage);
            canvas.gameObject.SetActive(true);
            combatController.SetPauseMenuInputBlocked(true);
        }

        public void Close()
        {
            if (canvas != null)
            {
                canvas.gameObject.SetActive(false);
            }

            pendingConfirmAction = null;
            if (combatController != null)
            {
                combatController.SetPauseMenuInputBlocked(false);
            }
        }

        // ESC while open steps back one page before resuming, so a stray ESC never skips a
        // pending confirm dialog straight out of the menu.
        private void HandleEscapeWhileOpen()
        {
            if ((confirmPage != null && confirmPage.gameObject.activeSelf) ||
                (soundPage != null && soundPage.gameObject.activeSelf) ||
                (displayPage != null && displayPage.gameObject.activeSelf))
            {
                ShowPage(mainPage);
                return;
            }

            Close();
        }

        private void ResolveCombatController()
        {
            if (combatController == null)
            {
                combatController = FindFirstObjectByType<MapCombatController>();
            }
        }

        private void ShowPage(RectTransform page)
        {
            if (mainPage != null)
            {
                mainPage.gameObject.SetActive(page == mainPage);
            }

            if (soundPage != null)
            {
                soundPage.gameObject.SetActive(page == soundPage);
            }

            if (displayPage != null)
            {
                displayPage.gameObject.SetActive(page == displayPage);
            }

            if (confirmPage != null)
            {
                confirmPage.gameObject.SetActive(page == confirmPage);
            }

            if (page != confirmPage)
            {
                pendingConfirmAction = null;
            }
        }

        private void ShowSoundSettingsPage()
        {
            EnsureSoundSettingsClone();
            ShowPage(soundPage);
        }

        private void ShowDisplaySettingsPage()
        {
            UpdateFullscreenToggleLabel();
            ShowPage(displayPage);
        }

        private void ToggleFullscreen()
        {
            DisplaySettingsService.SetFullscreen(!DisplaySettingsService.Fullscreen);
            UpdateFullscreenToggleLabel();
        }

        private void UpdateFullscreenToggleLabel()
        {
            if (fullscreenToggleLabel != null)
            {
                fullscreenToggleLabel.text = DisplaySettingsService.ModeLabel;
            }
        }

        private void ShowConfirmPage(string message, System.Action confirmAction)
        {
            if (confirmMessageLabel != null)
            {
                confirmMessageLabel.text = message;
            }

            pendingConfirmAction = confirmAction;
            ShowPage(confirmPage);
        }

        private void ConfirmPendingAction()
        {
            var action = pendingConfirmAction;
            pendingConfirmAction = null;
            action?.Invoke();
        }

        private void RequestReturnToLobby()
        {
            ShowConfirmPage(BuildExitConfirmMessage("로비로 돌아가시겠습니까?"), () =>
            {
                Close();
                ResolveCombatController();
                combatController?.ReturnToLobby();
            });
        }

        private void RequestQuitGame()
        {
            ShowConfirmPage(BuildExitConfirmMessage("게임을 종료하시겠습니까?"), QuitGame);
        }

        // P2 save integration: the run auto-saves when each overall turn ends (G5 checkpoint), so
        // leaving mid-turn can only lose progress made after the last checkpoint. Surface which
        // case the player is in instead of silently discarding the run.
        private string BuildExitConfirmMessage(string leadLine)
        {
            saveStore ??= new PlayerRunSaveStore();
            var saveLine = saveStore.HasSave
                ? "마지막 자동 저장(턴 종료) 지점부터 이어할 수 있습니다."
                : "아직 자동 저장된 기록이 없어 이번 판의 진행은 사라집니다.";
            return leadLine + "\n" + saveLine;
        }

        private static void QuitGame()
        {
#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // Reuses the authored sound settings panel (sidebar callout content) by cloning it under
        // the pause menu. Sliders auto-bind within the clone and stay in sync through
        // SoundSettingsService.SettingsChanged; the clone's own return-to-lobby button/dialog are
        // hidden because the pause menu owns the lobby/quit flow (with save notice).
        private void EnsureSoundSettingsClone()
        {
            if (soundSettingsClone != null || soundContentRoot == null)
            {
                return;
            }

            var source = FindSceneSoundSettingsPanel();
            if (source == null)
            {
                Debug.LogWarning("Pause menu could not find a scene SoundSettingsPanelView to reuse.", this);
                return;
            }

            soundSettingsClone = Instantiate(source, soundContentRoot);
            soundSettingsClone.name = "Sound Settings Content (Pause)";
            DeactivateChildByName(soundSettingsClone.transform, "ReturnToLobbyButton");
            DeactivateChildByName(soundSettingsClone.transform, "ReturnToLobbyConfirmDialog");
            NormalizeCloneRect(soundSettingsClone.transform as RectTransform);
            soundSettingsClone.gameObject.SetActive(true);
            soundSettingsClone.AutoBindFromHierarchy();
            soundSettingsClone.Refresh();
        }

        private SoundSettingsPanelView FindSceneSoundSettingsPanel()
        {
            var candidates = FindObjectsByType<SoundSettingsPanelView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < candidates.Length; i++)
            {
                var candidate = candidates[i];
                if (candidate != null && candidate != soundSettingsClone &&
                    !candidate.transform.IsChildOf(transform))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static void DeactivateChildByName(Transform root, string childName)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == childName)
                {
                    all[i].gameObject.SetActive(false);
                }
            }
        }

        private static void NormalizeCloneRect(RectTransform rect)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            if (rect.rect.width <= 1f || rect.rect.height <= 1f)
            {
                rect.sizeDelta = new Vector2(
                    rect.rect.width <= 1f ? 388f : rect.sizeDelta.x,
                    rect.rect.height <= 1f ? 260f : rect.sizeDelta.y);
            }
        }

        private void EnsureOverlay()
        {
            if (canvas != null)
            {
                return;
            }

            var canvasObject = new GameObject("PauseMenuCanvas");
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            // Match the gameplay HUD reference resolution (GameplaySceneContract 2200x1238) so shared
            // sprites/tokens render at the same on-screen size across the HUD and this overlay.
            scaler.referenceResolution = new Vector2(2200f, 1238f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            // Full-screen dim that also swallows every pointer event behind the menu (the map
            // input controller already ignores clicks while the pointer is over UI).
            var dim = CreateStretchedImage(canvasObject.transform, "Dim", DimColor);
            dim.raycastTarget = true;

            var panel = CreateRect(canvasObject.transform, "Panel", new Vector2(460f, 560f));
            var panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.color = PanelColor;
            panelImage.raycastTarget = true;
            // P4 popup skin (approach A): procedural SDF rounded panel. The flat PanelColor image above is
            // the fallback look — the component leaves it untouched when the skin material is missing.
            panel.gameObject.AddComponent<UiProceduralPanel>();

            var title = CreateLabel(panel, "Title", "일시정지", 40f, FontStyles.Bold);
            title.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            title.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -32f);
            title.rectTransform.sizeDelta = new Vector2(400f, 56f);

            mainPage = CreatePageRoot(panel, "Main Page");
            var mainLayout = AddButtonColumn(mainPage);
            CreateButton(mainLayout, "ResumeButton", "이어하기", Close);
            CreateButton(mainLayout, "SoundSettingsButton", "사운드 설정", ShowSoundSettingsPage);
            CreateButton(mainLayout, "DisplaySettingsButton", "화면 설정", ShowDisplaySettingsPage);
            CreateButton(mainLayout, "ReturnToLobbyButton", "로비로 돌아가기", RequestReturnToLobby);
            CreateButton(mainLayout, "QuitGameButton", "게임 종료", RequestQuitGame);

            soundPage = CreatePageRoot(panel, "Sound Page");
            soundContentRoot = CreateRect(soundPage, "Sound Content Root", new Vector2(420f, 340f));
            soundContentRoot.anchorMin = new Vector2(0.5f, 1f);
            soundContentRoot.anchorMax = new Vector2(0.5f, 1f);
            soundContentRoot.pivot = new Vector2(0.5f, 1f);
            soundContentRoot.anchoredPosition = new Vector2(0f, -8f);
            var soundBack = CreateButton(soundPage, "BackButton", "뒤로", () => ShowPage(mainPage));
            var soundBackRect = soundBack.transform as RectTransform;
            soundBackRect.anchorMin = new Vector2(0.5f, 0f);
            soundBackRect.anchorMax = new Vector2(0.5f, 0f);
            soundBackRect.pivot = new Vector2(0.5f, 0f);
            soundBackRect.anchoredPosition = new Vector2(0f, 20f);

            displayPage = CreatePageRoot(panel, "Display Page");
            var fullscreenToggle = CreateButton(displayPage, "FullscreenToggleButton", DisplaySettingsService.ModeLabel, ToggleFullscreen, new Vector2(360f, 56f));
            var fullscreenRect = fullscreenToggle.transform as RectTransform;
            fullscreenRect.anchorMin = new Vector2(0.5f, 1f);
            fullscreenRect.anchorMax = new Vector2(0.5f, 1f);
            fullscreenRect.pivot = new Vector2(0.5f, 1f);
            fullscreenRect.anchoredPosition = new Vector2(0f, -48f);
            fullscreenToggleLabel = fullscreenToggle.GetComponentInChildren<TMP_Text>(true);
            var displayBack = CreateButton(displayPage, "BackButton", "뒤로", () => ShowPage(mainPage));
            var displayBackRect = displayBack.transform as RectTransform;
            displayBackRect.anchorMin = new Vector2(0.5f, 0f);
            displayBackRect.anchorMax = new Vector2(0.5f, 0f);
            displayBackRect.pivot = new Vector2(0.5f, 0f);
            displayBackRect.anchoredPosition = new Vector2(0f, 20f);

            confirmPage = CreatePageRoot(panel, "Confirm Page");
            confirmMessageLabel = CreateLabel(confirmPage, "Message", string.Empty, 26f, FontStyles.Normal);
            confirmMessageLabel.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            confirmMessageLabel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            confirmMessageLabel.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            confirmMessageLabel.rectTransform.anchoredPosition = new Vector2(0f, 60f);
            confirmMessageLabel.rectTransform.sizeDelta = new Vector2(400f, 160f);
            var confirmRow = CreateRect(confirmPage, "Confirm Buttons", new Vector2(400f, 56f));
            confirmRow.anchorMin = new Vector2(0.5f, 0f);
            confirmRow.anchorMax = new Vector2(0.5f, 0f);
            confirmRow.pivot = new Vector2(0.5f, 0f);
            confirmRow.anchoredPosition = new Vector2(0f, 48f);
            var rowLayout = confirmRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.spacing = 24f;
            rowLayout.childControlWidth = false;
            rowLayout.childControlHeight = false;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            CreateButton(confirmRow, "ConfirmButton", "확인", ConfirmPendingAction, new Vector2(150f, 52f));
            CreateButton(confirmRow, "CancelButton", "취소", () => ShowPage(mainPage), new Vector2(150f, 52f));

            canvas.gameObject.SetActive(false);
        }

        private static RectTransform CreatePageRoot(RectTransform panel, string name)
        {
            var page = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            page.SetParent(panel, false);
            page.anchorMin = Vector2.zero;
            page.anchorMax = Vector2.one;
            page.offsetMin = new Vector2(0f, 0f);
            page.offsetMax = new Vector2(0f, -96f);
            page.gameObject.SetActive(false);
            return page;
        }

        private static RectTransform AddButtonColumn(RectTransform page)
        {
            var column = new GameObject("Buttons", typeof(RectTransform)).GetComponent<RectTransform>();
            column.SetParent(page, false);
            column.anchorMin = Vector2.zero;
            column.anchorMax = Vector2.one;
            column.offsetMin = Vector2.zero;
            column.offsetMax = Vector2.zero;
            var layout = column.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 18f;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            return column;
        }

        private static RectTransform CreateRect(Transform parent, string name, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = size;
            return rect;
        }

        private static Image CreateStretchedImage(Transform parent, string name, Color color)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private static TMP_Text CreateLabel(Transform parent, string name, string text, float fontSize, FontStyles style)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;
            // 프로젝트 기본 TMP 설정이 NoWrap이라 확인 문구 둘째 줄("마지막 자동 저장(턴 종료) 지점부터…")이
            // 400px 라벨을 584px로 뚫고 나가 460px 패널 밖에서 잘렸다(2026-09-05 실플레이 #1).
            label.textWrappingMode = TextWrappingModes.Normal;
            return label;
        }

        private static Button CreateButton(Transform parent, string name, string label, UnityEngine.Events.UnityAction onClick)
        {
            return CreateButton(parent, name, label, onClick, new Vector2(320f, 56f));
        }

        private static Button CreateButton(Transform parent, string name, string label, UnityEngine.Events.UnityAction onClick, Vector2 size)
        {
            var rect = CreateRect(parent, name, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = ButtonColor;
            image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);
            // P4 unified button skin; the flat ButtonColor image above stays as the fallback look.
            UiButtonSkin.Apply(button);

            var text = CreateLabel(rect, "Label", label, 26f, FontStyles.Bold);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            return button;
        }
    }
}

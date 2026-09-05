using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Runtime.Presentation;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// IMGUI debug control window for trap/status testing. It draws in both Game View
    /// (OnGUI) and, in the editor, Scene View (SceneView.duringSceneGui), so it is not
    /// limited to the runtime UGUI HUD overlay.
    /// </summary>
    public sealed class CombatDebugControlPanel : MonoBehaviour
    {
        private enum DebugTab
        {
            Trap,
            Overlay,
            State,
            Camera,
            Light,
            Samples,
            Cards,
            Timing,
            Story,
            Sandbox,
            Service
        }

        private static readonly List<CombatDebugControlPanel> ActivePanels = new List<CombatDebugControlPanel>();

        /// <summary>
        /// True only in the editor or a development build. Release player builds never draw or auto-create
        /// the debug panel, so it cannot affect shipped play. Gate every entry point with this.
        /// </summary>
        public static bool DebugUiAvailable =>
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            true;
#else
            false;
#endif

        // touch: force AssetDatabase re-registration (compile-cache desync workaround)
        private const float PanelWidth = 390f;
        private const float PanelHeight = 300f;
        private const float ScenePanelWidth = 390f;
        private const float ScenePanelHeight = 300f;
        private const float ButtonHeight = 26f;
        // The panel used to be pinned to the constants above on every draw. The Sandbox tab does not fit in
        // 390x300, so the rect is now only defaulted (not overwritten) and the user can resize it.
        private const float MinPanelWidth = 300f;
        private const float MinPanelHeight = 220f;

        [SerializeField] private MapCombatController controller;
        [SerializeField] private CameraLightTestPanel cameraLightPanel;
        [SerializeField] private bool showPanel = true;
        [SerializeField] private bool showInGameView = true;
        [SerializeField] private bool showInSceneView = true;
        [SerializeField] private Rect gameViewPanelRect = new Rect(16f, 16f, PanelWidth, PanelHeight);
        [SerializeField] private Rect sceneViewPanelRect = new Rect(16f, 16f, ScenePanelWidth, ScenePanelHeight);

        private DebugTab activeTab = DebugTab.Trap;
        private Vector2 scrollPosition;
        private bool drawingSceneView;
        private string cardSearchFilter = string.Empty;
        // State 탭 — 런 시드 입력(seed-determinism-handoff P1-1). 새 진입에만 적용된다.
        private string runSeedInput = string.Empty;
        private Vector2 activePanelSize = new Vector2(PanelWidth, PanelHeight);
        private bool resizingPanel;

        // --- Sandbox tab state (docs/card-sandbox-scene-plan.md) ---
        private int sandboxMonsterIndex;
        private int sandboxMonsterHp = 30;
        private int sandboxSpawnDistance = 2;
        private int sandboxStatusIndex;
        private int sandboxStatusTurns = 2;
        private int sandboxStatusAmount = 1;
        private int sandboxExileCount = 3;
        private int sandboxPlayerDamage = 10;
        private string[] sandboxMonsterIds = System.Array.Empty<string>();
        private string sandboxScaleMessage = string.Empty;
        private string sandboxSquadMessage = string.Empty;
        private static readonly StatusEffectKind[] SandboxStatusKinds =
            (StatusEffectKind[])System.Enum.GetValues(typeof(StatusEffectKind));

        public MapCombatController Controller => controller;
        public bool ShowPanel => showPanel;
        public bool ShowInGameView => showInGameView;
        public bool ShowInSceneView => showInSceneView;

        private void OnEnable()
        {
            if (!ActivePanels.Contains(this))
            {
                ActivePanels.Add(this);
            }

#if UNITY_EDITOR
            SceneView.duringSceneGui -= OnSceneGui;
            SceneView.duringSceneGui += OnSceneGui;
#endif
        }

        private void Awake()
        {
            ResolveController();
        }

        private void OnDisable()
        {
            ActivePanels.Remove(this);
#if UNITY_EDITOR
            SceneView.duringSceneGui -= OnSceneGui;
#endif
        }

        private void OnDestroy()
        {
            ActivePanels.Remove(this);
#if UNITY_EDITOR
            SceneView.duringSceneGui -= OnSceneGui;
#endif
        }

        private void OnGUI()
        {
            // Release player builds never render the debug UI. The F1 debug UI ladder hides this
            // window too — it is IMGUI, so no CanvasGroup can reach it.
            if (!DebugUiAvailable || !showInGameView || DebugUiVisibilityState.ImmediateModeDebugUiHidden)
            {
                return;
            }

            DrawWindow(ref gameViewPanelRect, GetInstanceID(), false);
        }

#if UNITY_EDITOR
        private void OnSceneGui(SceneView sceneView)
        {
            if (!showInSceneView)
            {
                return;
            }

            Handles.BeginGUI();
            drawingSceneView = true;
            DrawWindow(ref sceneViewPanelRect, GetInstanceID() + 17391, true);
            drawingSceneView = false;
            Handles.EndGUI();
        }
#endif

        public void Bind(MapCombatController owner)
        {
            controller = owner;
        }

        public void ConfigureForTests(MapCombatController owner, Rect gameRect, Rect sceneRect, bool visible = true)
        {
            controller = owner;
            gameViewPanelRect = gameRect;
            sceneViewPanelRect = sceneRect;
            showPanel = visible;
            showInGameView = true;
            showInSceneView = true;
        }

        public bool IsScreenPointOverGameViewPanel(Vector2 screenPoint)
        {
            if (!isActiveAndEnabled || !showPanel || !showInGameView)
            {
                return false;
            }

            var guiPoint = new Vector2(screenPoint.x, Screen.height - screenPoint.y);
            return gameViewPanelRect.Contains(guiPoint);
        }

        public static bool IsPointerOverAnyGameViewPanel(Vector2 screenPoint)
        {
            for (var i = ActivePanels.Count - 1; i >= 0; i--)
            {
                var panel = ActivePanels[i];
                if (panel == null)
                {
                    ActivePanels.RemoveAt(i);
                    continue;
                }

                if (panel.IsScreenPointOverGameViewPanel(screenPoint))
                {
                    return true;
                }
            }

            return false;
        }

        private void DrawWindow(ref Rect rect, int windowId, bool sceneView)
        {
            if (!showPanel)
            {
                if (GUI.Button(new Rect(rect.x, rect.y, 168f, 26f), sceneView ? "Show Combat Debug" : "Show Combat Debug"))
                {
                    showPanel = true;
                }
                return;
            }

            EnsurePanelFits(ref rect, sceneView);
            var currentEvent = Event.current;
            var mouseInside = currentEvent != null && rect.Contains(currentEvent.mousePosition);

            // Track a live resize at this level (not inside the window callback) so the drag keeps working even
            // when the cursor runs outside the window, then fold the result back into the caller's rect.
            HandleResizeInput(ref rect, sceneView);

            activePanelSize = new Vector2(rect.width, rect.height);
            var drawn = GUILayout.Window(
                windowId,
                rect,
                DrawPanelContents,
                sceneView ? "Debug Control Window - Scene" : "Debug Control Window",
                GUILayout.Width(rect.width),
                GUILayout.Height(rect.height));
            // Only the dragged position is copied back: the returned rect would otherwise clobber the size the
            // resize grip just wrote.
            rect.x = drawn.x;
            rect.y = drawn.y;

            if (mouseInside && currentEvent != null && IsMouseCaptureEvent(currentEvent.type))
            {
                currentEvent.Use();
            }
        }

        private void HandleResizeInput(ref Rect rect, bool sceneView)
        {
            if (!resizingPanel)
            {
                return;
            }

            var e = Event.current;
            if (e == null)
            {
                return;
            }

            switch (e.type)
            {
                case EventType.MouseDrag:
                    rect.width += e.delta.x;
                    rect.height += e.delta.y;
                    EnsurePanelFits(ref rect, sceneView);
                    e.Use();
                    break;
                case EventType.MouseUp:
                    resizingPanel = false;
                    e.Use();
                    break;
            }
        }

        private void DrawResizeGrip()
        {
            var grip = new Rect(activePanelSize.x - 20f, activePanelSize.y - 20f, 16f, 16f);
            GUI.Box(grip, "//");

            var e = Event.current;
            if (e != null && e.type == EventType.MouseDown && grip.Contains(e.mousePosition))
            {
                resizingPanel = true;
                e.Use();
            }
        }

        private static void EnsurePanelFits(ref Rect rect, bool sceneView)
        {
            var maxWidth = sceneView ? 4096f : Mathf.Max(1f, Screen.width);
            var maxHeight = sceneView ? 4096f : Mathf.Max(1f, Screen.height);

            // A rect that was never sized (legacy serialized value, or a fresh auto-created panel) falls back to
            // the authored default; anything the user has since resized to is preserved and only clamped to fit.
            var width = rect.width <= 1f ? (sceneView ? ScenePanelWidth : PanelWidth) : rect.width;
            var height = rect.height <= 1f ? (sceneView ? ScenePanelHeight : PanelHeight) : rect.height;
            rect.width = Mathf.Clamp(width, MinPanelWidth, Mathf.Max(MinPanelWidth, maxWidth));
            rect.height = Mathf.Clamp(height, MinPanelHeight, Mathf.Max(MinPanelHeight, maxHeight));
            rect.x = Mathf.Clamp(rect.x, 0f, Mathf.Max(0f, maxWidth - rect.width));
            rect.y = Mathf.Clamp(rect.y, 0f, Mathf.Max(0f, maxHeight - 32f));
        }

        private void DrawPanelContents(int windowId)
        {
            ResolveController();

            GUILayout.BeginHorizontal();
            DrawTabButton(DebugTab.Trap, "Trap");
            DrawTabButton(DebugTab.Overlay, "Overlay");
            DrawTabButton(DebugTab.State, "State");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DrawTabButton(DebugTab.Camera, "Camera");
            DrawTabButton(DebugTab.Light, "Light");
            DrawTabButton(DebugTab.Samples, "Samples");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DrawTabButton(DebugTab.Cards, "Cards (전체 카드)");
            DrawTabButton(DebugTab.Timing, "Timing (타이밍)");
            // 스토리 컷씬 은퇴(2026-09-01). 누르면 아무 일도 안 나는 탭은 남기지 않는다.
            // 🔴이 판은 Combat 어셈블리라 Flow의 StoryCutsceneFeature.Enabled를 볼 수 없다(Flow→Combat 단방향).
            // 되살릴 때는 그 스위치와 여기 두 곳을 함께 되돌릴 것. enum 멤버는 append-only 규약대로 존치.
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DrawTabButton(DebugTab.Sandbox, "Sandbox (카드 샌드박스)");
            DrawTabButton(DebugTab.Service, "Service (서비스)");
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, true);
            switch (activeTab)
            {
                case DebugTab.Trap:
                    DrawTrapTab();
                    break;
                case DebugTab.Overlay:
                    DrawOverlayTab();
                    break;
                case DebugTab.State:
                    DrawStateTab();
                    break;
                case DebugTab.Camera:
                    DrawCameraTab();
                    break;
                case DebugTab.Light:
                    DrawLightTab();
                    break;
                case DebugTab.Samples:
                    DrawSamplesTab();
                    break;
                case DebugTab.Cards:
                    DrawCardsTab();
                    break;
                case DebugTab.Timing:
                    DrawTimingTab();
                    break;
                case DebugTab.Story:
                    DrawStoryTab();
                    break;
                case DebugTab.Sandbox:
                    DrawSandboxTab();
                    break;
                case DebugTab.Service:
                    DrawServiceTab();
                    break;
            }
            GUILayout.EndScrollView();

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Hide", GUILayout.Height(ButtonHeight)))
            {
                showPanel = false;
            }
            GUILayout.Label(drawingSceneView ? "Scene View overlay" : "Game View overlay");
            GUILayout.EndHorizontal();

            DrawResizeGrip();
            // Only the title strip drags the window, so the resize grip and buttons stay clickable.
            GUI.DragWindow(new Rect(0f, 0f, activePanelSize.x, 22f));
        }

        // ── Service 탭(#20, 실플레이 피드백 2026-08-19) ─────────────────────────────
        // 서비스 3종(잡화점/캠핑카/공작소)은 맵에 하나씩이라 실플레이 검증이 비쌌다 —
        // 순간이동·모달 즉시 열기·소비 원장 리셋을 한 화면에 모은다.

        private string serviceDebugMessage = string.Empty;
        private int lootRelicIndex;
        private int lootItemIndex;
        private string[] lootRelicIds = System.Array.Empty<string>();
        private string[] lootItemIds = System.Array.Empty<string>();

        private void DrawServiceTab()
        {
            if (controller == null)
            {
                GUILayout.Label("Controller not found.");
                return;
            }

            GUILayout.Label("이동 = 오브젝트 칸으로 순간이동(밟기 트리거는 다음 이동에).");
            GUILayout.Label("열기 = 좌표·소비 여부 무시하고 모달만 즉시 연다(UI 검증).");
            DrawServiceRow("잡화점", "shop");
            DrawServiceRow("캠핑카", "camper");
            if (SeoulPlayup.Map.Runtime.ServiceObjectAvailability.WorkshopEnabled)
            {
                DrawServiceRow("공작소", "workshop");
            }

            // 상자는 맵에 24개가 셔플돼 흩어져 있어 종류가 아니라 「가장 가까운 미개봉」으로 찾는다.
            GUILayout.BeginHorizontal();
            GUILayout.Label("상자", GUILayout.Width(70f));
            if (GUILayout.Button("이동", GUILayout.Height(ButtonHeight)))
            {
                controller.DebugTeleportToTreasureChest(out serviceDebugMessage);
            }

            if (GUILayout.Button("열기", GUILayout.Height(ButtonHeight)))
            {
                controller.DebugOpenTreasureChest(out serviceDebugMessage);
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            if (GUILayout.Button("소비 원장 리셋 (서비스 3종+상자 재방문 가능)", GUILayout.Height(ButtonHeight)))
            {
                var restored = controller.DebugResetServiceConsumption();
                serviceDebugMessage = restored > 0
                    ? $"{restored}개 오브젝트의 소비를 되돌렸습니다."
                    : "되돌릴 소비 기록이 없습니다.";
            }

            DrawLootSection();

            if (!string.IsNullOrEmpty(serviceDebugMessage))
            {
                GUILayout.Label(serviceDebugMessage);
            }
        }

        // ── 전리품·경제 섹션(2026-08-31 실플레이 판정 편의) ───────────────────────────
        // 전리품 목록의 흡입 연출·목록 클릭 흐름과 잡화점 가격 체감은 몬스터를 잡고 엽전을 모아야만
        // 볼 수 있었다 — 목록을 네 줄 채워 바로 열고, 지급은 전부 출하 경로를 지난다.

        private void DrawLootSection()
        {
            GUILayout.Space(8f);
            GUILayout.Label("── 전리품 · 경제 ──");

            if (GUILayout.Button("전리품 목록 열기 (엽전·소모품·유물·부적)", GUILayout.Height(ButtonHeight)))
            {
                controller.DebugOpenLootPopup(out serviceDebugMessage);
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("엽전", GUILayout.Width(70f));
            if (GUILayout.Button("+100", GUILayout.Height(ButtonHeight)))
            {
                var balance = controller.DebugGrantMoney(100);
                serviceDebugMessage = $"엽전 +100 (보유 {balance})";
            }

            if (GUILayout.Button("+500", GUILayout.Height(ButtonHeight)))
            {
                var balance = controller.DebugGrantMoney(500);
                serviceDebugMessage = $"엽전 +500 (보유 {balance})";
            }

            GUILayout.EndHorizontal();

            DrawLootGrantRow(
                "유물",
                RefreshLootRelicIds(),
                ref lootRelicIndex,
                id => controller.DebugGrantPermanentItem(id, out serviceDebugMessage));
            DrawLootGrantRow(
                "소모품",
                RefreshLootItemIds(),
                ref lootItemIndex,
                id => controller.DebugGrantConsumable(id, out serviceDebugMessage));
        }

        private void DrawLootGrantRow(string label, string[] ids, ref int index, System.Func<string, bool> grant)
        {
            if (ids.Length == 0)
            {
                GUILayout.Label($"{label} 카탈로그가 비어 있습니다.");
                return;
            }

            index = Mathf.Clamp(index, 0, ids.Length - 1);
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(50f));
            if (GUILayout.Button("◀", GUILayout.Width(28f), GUILayout.Height(ButtonHeight)))
            {
                index = (index - 1 + ids.Length) % ids.Length;
            }

            GUILayout.Label(ids[index], GUILayout.ExpandWidth(true));
            if (GUILayout.Button("▶", GUILayout.Width(28f), GUILayout.Height(ButtonHeight)))
            {
                index = (index + 1) % ids.Length;
            }

            if (GUILayout.Button("지급", GUILayout.Width(48f), GUILayout.Height(ButtonHeight)))
            {
                grant(ids[index]);
            }

            GUILayout.EndHorizontal();
        }

        private string[] RefreshLootRelicIds()
        {
            if (lootRelicIds.Length == 0)
            {
                lootRelicIds = PlayerPermanentItemCatalog.Definitions
                    .Select(definition => definition.Id)
                    .ToArray();
            }

            return lootRelicIds;
        }

        private string[] RefreshLootItemIds()
        {
            if (lootItemIds.Length == 0)
            {
                lootItemIds = ConsumableItemCatalog.Definitions
                    .Select(definition => definition.Id)
                    .ToArray();
            }

            return lootItemIds;
        }

        private void DrawServiceRow(string label, string kind)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(70f));
            if (GUILayout.Button("이동", GUILayout.Height(ButtonHeight)))
            {
                controller.DebugTeleportToServiceObject(kind, out serviceDebugMessage);
            }

            if (GUILayout.Button("열기", GUILayout.Height(ButtonHeight)))
            {
                controller.DebugOpenServiceModal(kind, out serviceDebugMessage);
            }

            GUILayout.EndHorizontal();
        }

        private void DrawTabButton(DebugTab tab, string label)
        {
            var previousColor = GUI.backgroundColor;
            GUI.backgroundColor = activeTab == tab ? new Color(0.2f, 0.45f, 0.55f, 1f) : previousColor;
            if (GUILayout.Button(label, GUILayout.Height(ButtonHeight)))
            {
                activeTab = tab;
            }
            GUI.backgroundColor = previousColor;
        }

        private void DrawTrapTab()
        {
            GUILayout.Label("Trap / status debug movement");
            if (controller == null)
            {
                GUILayout.Label("MapCombatController: missing");
                return;
            }

            if (GUILayout.Button(controller.IsClickMoveDebugModeEnabled ? "● Trap Debug Move ON" : "Trap Debug Move OFF", GUILayout.Height(ButtonHeight)))
            {
                controller.ToggleClickMoveDebugMode();
            }

            if (GUILayout.Button(controller.IsFogDebugVisible ? "Fog ON (normal visibility)" : "Fog OFF (reveal all)", GUILayout.Height(ButtonHeight)))
            {
                controller.SetFogDebugVisible(!controller.IsFogDebugVisible);
            }

            var previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && controller.IsClickMoveDebugModeEnabled && controller.HasClickMoveDebugSelection;
            var selectedLabel = controller.HasClickMoveDebugSelection
                ? $"Move Selected {controller.ClickMoveDebugSelectedCoord}"
                : "Move Selected Tile";
            if (GUILayout.Button(selectedLabel, GUILayout.Height(ButtonHeight)))
            {
                controller.ConfirmClickMoveDebugSelection();
            }
            GUI.enabled = previousEnabled;

            GUILayout.Space(6f);
            GUILayout.Label("ON: click a map tile to move there without cards/range/phase. Panel hover blocks map clicks and RMB orbit.");
            GUILayout.Label($"Status: {controller.LastInputMessage}");
        }

        private void DrawOverlayTab()
        {
            GUILayout.Label("Tactical overlay toggles");
            if (controller == null)
            {
                GUILayout.Label("MapCombatController: missing");
                return;
            }

            DrawOverlayToggle(CombatOverlayDebugLayer.PlayerMovement, "Move");
            DrawOverlayToggle(CombatOverlayDebugLayer.PlayerAction, "Action");
            DrawOverlayToggle(CombatOverlayDebugLayer.MonsterMove, "Monster Move");
            DrawOverlayToggle(CombatOverlayDebugLayer.MonsterAttack, "Monster Attack");
            DrawOverlayToggle(CombatOverlayDebugLayer.MonsterChase, "Monster Chase");
            DrawOverlayToggle(CombatOverlayDebugLayer.MonsterIntentArrows, "Intent Arrows (debug)");
            GUILayout.Space(6f);
            GUILayout.Label(controller.OverlayRendererStatusText);
        }

        private void DrawOverlayToggle(CombatOverlayDebugLayer layer, string label)
        {
            var visible = controller.IsOverlayDebugLayerVisible(layer);
            if (GUILayout.Button($"{label}: {(visible ? "ON" : "OFF")}", GUILayout.Height(ButtonHeight)))
            {
                controller.ToggleOverlayDebugLayer(layer);
            }
        }

        /// <summary>
        /// 런 시드 표시·지정. 「이 시드로 재시작」은 <see cref="RunSeedRequest"/>에 예약만 하고
        /// 재시작을 부른다 — 시드를 실제로 채택하는 곳은 플로우 계층의 새 진입 한 곳뿐이다
        /// (재개 경로는 세이브의 시드를 쓰므로 중단 세이브가 남아 있으면 예약이 그대로 대기한다).
        /// </summary>
        private void DrawRunSeedSection()
        {
            GUILayout.Label("Run Seed (재현용)");
            GUILayout.Label(controller.TryGetRunSeed(out var currentSeed) ? $"현재 시드: {currentSeed}" : "현재 시드: 없음(테스트/랩 진입)");
            GUILayout.BeginHorizontal();
            runSeedInput = GUILayout.TextField(runSeedInput, GUILayout.Width(140f));
            var parsed = int.TryParse(runSeedInput, out var requestedSeed);
            GUI.enabled = parsed;
            if (GUILayout.Button("이 시드로 재시작", GUILayout.Height(ButtonHeight)))
            {
                RunSeedRequest.Set(requestedSeed);
                controller.RestartDemo();
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (RunSeedRequest.HasPending)
            {
                GUILayout.Label("예약된 시드가 있음 — 다음 새 진입에 적용");
            }
        }

        private void DrawStateTab()
        {
            if (controller == null || controller.State == null)
            {
                GUILayout.Label("State: unavailable");
                return;
            }

            DrawRunSeedSection();
            GUILayout.Space(6f);

            GUILayout.Label("Victory Debug");
            if (GUILayout.Button("Play Victory Event (MemoryStone)", GUILayout.Height(ButtonHeight)))
            {
                controller.DebugPlayMemoryStoneVictoryPresentation();
            }
            GUILayout.Label("Debug: plays the MemoryStone purification camera/wave sequence, then shows the victory UI/SFX.");
            GUILayout.Space(6f);

            GUILayout.Label("Trailer Debug");
            if (GUILayout.Button("Replay Stage Intro", GUILayout.Height(ButtonHeight)))
            {
                controller.DebugReplayStageIntro();
            }
            GUILayout.Label("Debug: replays the stage intro cinematic (day dolly -> night -> monster spawns -> fog ripple) for a trailer take. Enter skips it. docs/trailer-capture-plan.md");
            GUILayout.Space(6f);
            GUILayout.TextArea(controller.MemoryStoneVictoryCameraPointDebugText);
            GUILayout.Space(6f);
            DrawVictoryCameraTuning();
            GUILayout.Space(6f);
            GUILayout.Label("PlayerStateSnapshot");
            GUILayout.TextArea(PlayerStateDebugPanel.FormatSnapshot(controller.State.CreatePlayerStateSnapshot()));
        }

        private void DrawVictoryCameraTuning()
        {
            GUILayout.Label("Victory Camera Tuning");
            GUILayout.Label("Route: PlayerSpawn -> ordered VictoryCameraPoint -> MemoryStone.");
            GUILayout.Label("Camera path is angular: each waypoint is tile center + Height.");
            if (controller.MemoryStoneVictoryCameraUsesLegacyBaseRotation)
            {
                controller.DebugResetMemoryStoneVictoryCameraBaseRotationToDefaults();
            }

            var followsRevealRoute = GUILayout.Toggle(
                controller.MemoryStoneVictoryCameraFollowsRevealRoute,
                "Compare Mode: Camera follows every brightening tile");
            if (followsRevealRoute != controller.MemoryStoneVictoryCameraFollowsRevealRoute)
            {
                controller.DebugSetMemoryStoneVictoryCameraFollowsRevealRoute(followsRevealRoute);
            }

            var height = DrawDebugFineSlider("Height", controller.MemoryStoneVictoryCameraHeight, 1f, 40f, 0.25f);
            var orthographicSize = DrawDebugFineSlider("Ortho Size", controller.MemoryStoneVictoryCameraOrthographicSize, 1f, 35f, 0.25f);
            var useFixedRotation = GUILayout.Toggle(
                controller.MemoryStoneVictoryUseFixedCameraRotation,
                "Follow Route Direction (live Yaw turns on corners)");
            GUILayout.Label("Base tuning values below are not route angles.");
            var pitch = DrawDebugFineSlider("Base Pitch", controller.MemoryStoneVictoryCameraPitchDegrees, 5f, 85f, 0.5f);
            var yaw = DrawDebugFineSlider("Yaw Offset", controller.MemoryStoneVictoryCameraYawDegrees, 0f, 360f, 0.5f);
            var focusLag = DrawDebugFineSlider("Focus Lag", controller.MemoryStoneVictoryCameraFocusLag, 0f, 0.35f, 0.01f);

            controller.DebugSetMemoryStoneVictoryCameraTuning(
                height,
                orthographicSize,
                useFixedRotation,
                pitch,
                yaw,
                focusLag);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Wide")) controller.DebugSetMemoryStoneVictoryCameraTuning(22f, 14f, true, 40f, yaw, focusLag);
            if (GUILayout.Button("Reset")) controller.DebugSetMemoryStoneVictoryCameraTuning(15f, 13f, true, 40f, 0f, 0.08f);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Route Pitch 40")) controller.DebugSetMemoryStoneVictoryCameraTuning(height, orthographicSize, true, 40f, yaw, focusLag);
            if (GUILayout.Button("Top-ish")) controller.DebugSetMemoryStoneVictoryCameraTuning(height, orthographicSize, true, 70f, yaw, focusLag);
            GUILayout.EndHorizontal();

            if (controller.MemoryStoneVictoryVirtualCameraActive)
            {
                var liveEuler = controller.MemoryStoneVictoryCameraLiveEulerAngles;
                GUILayout.Label($"Live Camera Rotation: Pitch {liveEuler.x:0.0}, Yaw {liveEuler.y:0.0}");
            }

            GUILayout.Label(controller.MemoryStoneVictoryCameraFollowsRevealRoute
                ? "Mode: camera follows the same tile-by-tile route used by the brightening wave."
                : "Mode: camera follows only PlayerSpawn -> VictoryCameraPoint -> MemoryStone.");
            GUILayout.Label("Tip: Focus Lag moves the viewed point behind the frontier. Values update during the event.");
        }

        private static float DrawDebugFineSlider(string label, float value, float min, float max, float step)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {value:0.00}", GUILayout.Width(118f));
            if (GUILayout.Button("-", GUILayout.Width(24f), GUILayout.Height(20f)))
            {
                value -= step;
            }
            var next = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(128f));
            if (GUILayout.Button("+", GUILayout.Width(24f), GUILayout.Height(20f)))
            {
                next += step;
            }
            GUILayout.EndHorizontal();
            return Mathf.Clamp(next, min, max);
        }

        private static float DrawDebugSlider(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {value:0.00}", GUILayout.Width(172f));
            var next = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.EndHorizontal();
            return next;
        }

        private void DrawCameraTab()
        {
            ResolveCameraLightPanel();
            if (cameraLightPanel == null)
            {
                GUILayout.Label("CameraLightTestPanel: missing in this scene");
                return;
            }

            cameraLightPanel.DrawCameraDebugPage();
        }

        private void DrawLightTab()
        {
            ResolveCameraLightPanel();
            if (cameraLightPanel == null)
            {
                GUILayout.Label("CameraLightTestPanel: missing in this scene");
                return;
            }

            cameraLightPanel.DrawLightingDebugPage();
        }

        private void DrawSamplesTab()
        {
            ResolveCameraLightPanel();
            if (cameraLightPanel == null)
            {
                GUILayout.Label("CameraLightTestPanel: missing in this scene");
                return;
            }

            cameraLightPanel.DrawLightSamplesDebugPage();
        }

        private void DrawCardsTab()
        {
            if (controller == null || controller.State == null)
            {
                GUILayout.Label("State: unavailable");
                return;
            }

            GUILayout.Label("카드 보상 팝업 (등급 연출 확인)");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("보상 (일반 60/30/10)", GUILayout.Height(ButtonHeight)))
            {
                controller.DebugShowCardReward(false);
            }
            if (GUILayout.Button("보상 (엘리트 70/30)", GUILayout.Height(ButtonHeight)))
            {
                controller.DebugShowCardReward(true);
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("선택 시 현재 손패에 추가됩니다. 다시 뽑기/넘기기 동작.");
            GUILayout.Space(6f);

            GUILayout.BeginHorizontal();
            GUILayout.Label("검색:", GUILayout.Width(36f));
            cardSearchFilter = GUILayout.TextField(cardSearchFilter ?? string.Empty, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("X", GUILayout.Width(22f), GUILayout.Height(ButtonHeight)))
            {
                cardSearchFilter = string.Empty;
            }
            GUILayout.EndHorizontal();

            var catalog = controller.State.CardCatalog;
            var filter = cardSearchFilter ?? string.Empty;
            var isFiltering = !string.IsNullOrWhiteSpace(filter);
            var lastDeckType = (CardCategory)(-1);

            foreach (var entry in catalog.Entries)
            {
                if (isFiltering
                    && entry.DisplayName.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0
                    && entry.Id.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (entry.DeckType != lastDeckType)
                {
                    lastDeckType = entry.DeckType;
                    GUILayout.Label(entry.DeckType == CardCategory.Movement ? "── 이동 덱 ──" : "── 행동 덱 ──");
                }

                GUILayout.BeginHorizontal();
                var typeLabel = entry.DeckType == CardCategory.Movement ? "이동" : GetActionTypeLabel(entry.ActionType);
                GUILayout.Label($"[{typeLabel}]", GUILayout.Width(38f));
                GUILayout.Label(entry.DisplayName, GUILayout.ExpandWidth(true));
                if (entry.Cost > 0)
                {
                    GUILayout.Label($"{entry.Cost}ki", GUILayout.Width(26f));
                }
                else
                {
                    GUILayout.Label(string.Empty, GUILayout.Width(26f));
                }
                if (GUILayout.Button("패 추가", GUILayout.Width(52f), GUILayout.Height(ButtonHeight)))
                {
                    controller.State.DebugInjectCardIntoHand(entry.Id);
                }
                GUILayout.EndHorizontal();

                // Whether the card's class overrides a rule hook. Most cards run the generic path and show
                // "기본", which is fine; this line exists so a card whose rule was written but never registered
                // (the P6 trap) is visible at the moment you add it to hand.
                GUILayout.Label($"    {entry.Id} · {controller.State.DebugDescribeCardHandling(entry)}");
            }
        }

        private static string GetActionTypeLabel(CardEffectType actionType)
        {
            switch (actionType)
            {
                case CardEffectType.Attack: return "공격";
                case CardEffectType.Defend: return "방어";
                case CardEffectType.Scout:  return "정찰";
                case CardEffectType.FieldObject: return "설치";
                case CardEffectType.Buff:   return "버프";
                case CardEffectType.Utility: return "유틸";
                default: return "행동";
            }
        }

        private void DrawTimingTab()
        {
            if (controller == null)
            {
                GUILayout.Label("MapCombatController: missing");
                return;
            }

            GUILayout.Label("공격 → 타격 타이밍 튜닝");
            GUILayout.Label(controller.IsSequencePlaying
                ? $"재생 중: {controller.PresentationPhase}"
                : "대기 중 (idle)");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("공격 재생", GUILayout.Height(ButtonHeight)))
            {
                controller.DebugReplayPlayerAttack(false);
            }
            if (GUILayout.Button("처치 재생", GUILayout.Height(ButtonHeight)))
            {
                controller.DebugReplayPlayerAttack(true);
            }
            if (GUILayout.Button("전투 리셋", GUILayout.Height(ButtonHeight)))
            {
                controller.DebugRestoreCombatants();
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("공격/처치 재생은 가장 가까운 몬스터를 풀피로 되돌린 뒤 실제 공격 시퀀스를 그대로 재생합니다.");

            GUILayout.Space(6f);
            var timeScale = controller.DebugPlaybackTimeScale;
            GUILayout.Label($"재생 속도(슬로모션): {timeScale:0.00}x");
            var nextTimeScale = GUILayout.HorizontalSlider(timeScale, 0.05f, 1f);
            if (!Mathf.Approximately(nextTimeScale, timeScale))
            {
                controller.DebugSetPlaybackTimeScale(nextTimeScale);
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("0.1x")) controller.DebugSetPlaybackTimeScale(0.1f);
            if (GUILayout.Button("0.25x")) controller.DebugSetPlaybackTimeScale(0.25f);
            if (GUILayout.Button("0.5x")) controller.DebugSetPlaybackTimeScale(0.5f);
            if (GUILayout.Button("1x")) controller.DebugSetPlaybackTimeScale(1f);
            GUILayout.EndHorizontal();

            // Drawn before the profile guard below so the measurement stays reachable even when no
            // CombatTimingProfile is wired — it needs none.
            DrawActionFocusMetrics();

            var profile = controller.TimingProfile;
            if (profile == null)
            {
                GUILayout.Space(6f);
                GUILayout.Label("CombatTimingProfile 미배선 — 슬라이더 비활성. 컨트롤러 timingProfile에 에셋을 할당하세요.");
                return;
            }

            GUILayout.Space(6f);
            GUILayout.Label($"전역 연출 속도: {profile.TunableGlobalSpeedMultiplier:0.00}x (전 구간 일괄 배율)");
            profile.TunableGlobalSpeedMultiplier = GUILayout.HorizontalSlider(profile.TunableGlobalSpeedMultiplier, 0.25f, 4f);
            profile.TunableEffectStaggerSeconds = DrawTimingSlider("효과 스태거 (타격음/VFX 간격)", profile.TunableEffectStaggerSeconds, 0f, 0.25f);
            profile.TunableAoeTargetIntervalSeconds = DrawTimingSlider("광역 대상 간격 (대상 사이)", profile.TunableAoeTargetIntervalSeconds, 0f, 0.5f);
            profile.TunableStatusEffectStaggerSeconds = DrawTimingSlider("상태이상 스태거 (상태 하나당)", profile.TunableStatusEffectStaggerSeconds, 0f, 1f);
            profile.TunableFloatingTextQueueStaggerSeconds = DrawTimingSlider("플로팅 텍스트 큐 간격", profile.TunableFloatingTextQueueStaggerSeconds, 0f, 1f);
            profile.TunableMonsterActionGapSeconds = DrawTimingSlider("몬스터별 행동 텀", profile.TunableMonsterActionGapSeconds, 0f, 1f);
            profile.TunablePostMonsterAttackPauseSeconds = DrawTimingSlider("몬스터 공격 종료 후 턴 시작 텀", profile.TunablePostMonsterAttackPauseSeconds, 0f, 3f);

            GUILayout.Space(6f);
            profile.TunableAlignImpactToAnimation = GUILayout.Toggle(
                profile.TunableAlignImpactToAnimation,
                "임팩트 동기화 (데미지/VFX/셰이크를 타격 프레임에 정렬)");
            profile.TunableAttackWindupDelay = DrawTimingSlider("Windup 선딜", profile.TunableAttackWindupDelay, 0f, 1.2f);
            profile.TunableAttackImpactDelay = DrawTimingSlider("Impact 타격까지", profile.TunableAttackImpactDelay, 0f, 1.2f);
            profile.TunableDeathDelay = DrawTimingSlider("Death 사망 후", profile.TunableDeathDelay, 0f, 1.2f);

            GUILayout.Space(6f);
            profile.TunableEnableHitStop = GUILayout.Toggle(profile.TunableEnableHitStop, "히트스탑 사용");
            profile.TunablePlayerAttackHitStopSeconds = DrawTimingSlider("HitStop 플레이어공격", profile.TunablePlayerAttackHitStopSeconds, 0f, 0.5f);
            profile.TunableMonsterAttackHitStopSeconds = DrawTimingSlider("HitStop 몬스터공격", profile.TunableMonsterAttackHitStopSeconds, 0f, 0.5f);
            profile.TunableLethalHitStopSeconds = DrawTimingSlider("HitStop 치명타", profile.TunableLethalHitStopSeconds, 0f, 0.6f);
            profile.TunableHitStopTimeScale = DrawTimingSlider("HitStop timeScale", profile.TunableHitStopTimeScale, 0f, 1f);
            profile.TunableHitStopAnimationSpeed = DrawTimingSlider("HitStop 애니속도", profile.TunableHitStopAnimationSpeed, 0f, 1f);

            GUILayout.Space(6f);
            profile.TunablePlayerMoveSeconds = DrawTimingSlider("이동 플레이어", profile.TunablePlayerMoveSeconds, 0f, 1.5f);
            profile.TunableEnemyMoveSeconds = DrawTimingSlider("이동 몬스터", profile.TunableEnemyMoveSeconds, 0f, 1.5f);
            profile.TunableEnemyMoveStartDelay = DrawTimingSlider("몬스터 이동 시작딜레이", profile.TunableEnemyMoveStartDelay, 0f, 0.5f);

#if UNITY_EDITOR
            GUILayout.Space(4f);
            if (GUILayout.Button("프로파일 에셋에 영구 저장", GUILayout.Height(ButtonHeight)))
            {
                UnityEditor.EditorUtility.SetDirty(profile);
                UnityEditor.AssetDatabase.SaveAssets();
            }
            GUILayout.Label("슬라이더는 즉시 반영. 만족스러우면 위 버튼으로 에셋에 영구 기록하세요.");
#endif
        }

        /// <summary>
        /// Arm / read out the action-focus measurement pass (docs/monster-action-camera-focus-plan.md §5 P0).
        /// Lives on the Timing tab because it measures the monster phase this tab already tunes.
        /// </summary>
        private void DrawActionFocusMetrics()
        {
            GUILayout.Space(8f);
            GUILayout.Label("■ 액션 포커스 계측 (P0) — 화면 밖 이벤트 실측");

            var measuring = controller.MeasureActionFocus;
            var nextMeasuring = GUILayout.Toggle(measuring, "계측 ON (연출 타이밍 영향 없음)");
            if (nextMeasuring != measuring)
            {
                controller.MeasureActionFocus = nextMeasuring;
            }

            GUILayout.Label($"누적 표본: {CombatActionFocusMetrics.SampleCount}건"
                + (CombatActionFocusMetrics.Truncated ? " (상한 초과)" : string.Empty));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("요약 출력", GUILayout.Height(ButtonHeight)))
            {
                Debug.Log(CombatActionFocusMetrics.FormatSummary());
            }
            if (GUILayout.Button("표본 초기화", GUILayout.Height(ButtonHeight)))
            {
                CombatActionFocusMetrics.Clear();
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("시나리오: (a) 필드 카드를 깔고 그 자리에서 멀어지기, (b) 정찰로 원거리 몬스터 밝히기 — 각 5~10턴.");
        }

        private void DrawStoryTab()
        {
            // 은퇴한 탭. 탭 버튼이 그려지지 않으므로 평소엔 도달하지 않지만, activeTab이 Story로 남은
            // 상태에서 판을 다시 열면 여기로 온다 — 빈 화면 대신 왜 비었는지를 적는다.
            GUILayout.Label("스토리 컷씬은 이 게임에서 사용하지 않습니다(2026-09-01 확정).");
            GUILayout.Label("되살리려면 SeoulPlayup.Flow.Unity.Story.StoryCutsceneFeature.Enabled 를 true 로 되돌리고,");
            GUILayout.Label("이 파일의 Story 탭 버튼과 미리보기 버튼(DebugPlayIntroCutscene/DebugPlayOutroCutscene 송신)을");
            GUILayout.Label("함께 되살리세요. 저작 에셋과 StoryCutscene 씬은 그대로 남아 있습니다.");
        }

        private static void SendToMainGameplayController(string methodName)
        {
            var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour == null || behaviour.GetType().FullName != "SeoulPlayup.Flow.Unity.MainGameplayController")
                {
                    continue;
                }

                behaviour.SendMessage(methodName, SendMessageOptions.DontRequireReceiver);
                return;
            }

            Debug.LogWarning($"Story cutscene preview: MainGameplayController not found in this scene ('{methodName}').");
        }

        /// <summary>
        /// 카드 샌드박스 (docs/card-sandbox-scene-plan.md). Sets up the *situations* a card needs inside the live
        /// game — a dummy to hit, a debuff on the target, a non-empty 소멸 더미 — so a card can then be played
        /// through the real HUD. Deliberately has no "play this card now" button: the whole reason this tool
        /// rides on the shipping HUD is that P5's U02 bug lived in the HUD click dispatcher, and a rules-path
        /// shortcut would step over exactly the layer worth testing. Add the card to hand, then click it.
        /// </summary>
        // ── 몸집 조절(2026-09-03) ────────────────────────────────────────
        // 몬스터 몸집의 저작면은 프리팹 루트 localScale뿐이다. 슬라이더는 그 값을 직접 다루고(배율이 아니라
        // 「넣을 루트 스케일」), 살아 있는 같은 정의의 유닛 전부에 실시간으로 얹힌다. 「저장」이 프리팹에
        // 굳힌다(에디터 전용). 보스는 boss_phases.csv의 visualScale이 크기 정본이라 여기서 빼고 안내만 한다.
        private void DrawSandboxMonsterScaleSection(CombatState state)
        {
            GUILayout.Space(8f);
            GUILayout.Label("── 몸집 조절 (프리팹 루트 스케일 · 실시간) ──");
            var groups = state.Monsters
                .Where(m => !m.IsDead)
                .GroupBy(m => m.DefinitionId, System.StringComparer.Ordinal)
                .OrderBy(g => g.Key, System.StringComparer.Ordinal)
                .ToList();
            if (groups.Count == 0)
            {
                GUILayout.Label("살아 있는 몬스터가 없습니다. 위에서 소환하세요.");
                return;
            }

            foreach (var group in groups)
            {
                var first = group.First();
                var displayName = state.TryGetMonsterCatalogEntry(group.Key, out var catalogEntry) ? catalogEntry.DisplayName : group.Key;
                if (state.TryGetBossPhaseState(first.Id, out _))
                {
                    GUILayout.Label($"{group.Key} {displayName}: 보스는 boss_phases.csv visualScale이 정본 — 여기서 안 다룹니다.");
                    continue;
                }

                if (!controller.DebugTryGetMonsterPrefabRootScale(first.Id, out var prefabRootScale) || prefabRootScale <= 0f)
                {
                    GUILayout.Label($"{group.Key} {displayName}: 마커가 아직 없습니다.");
                    continue;
                }

                var current = prefabRootScale * controller.DebugGetMonsterVisualScaleOverride(first.Id);
                GUILayout.Label($"{group.Key} {displayName} ×{group.Count()} · 프리팹 {prefabRootScale:0.00} → {current:0.00}");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("-", GUILayout.Width(24f), GUILayout.Height(20f)))
                {
                    current -= 0.05f;
                }

                var next = GUILayout.HorizontalSlider(current, 0.3f, 6f);
                if (GUILayout.Button("+", GUILayout.Width(24f), GUILayout.Height(20f)))
                {
                    next += 0.05f;
                }

                if (GUILayout.Button("↺", GUILayout.Width(28f), GUILayout.Height(20f)))
                {
                    next = prefabRootScale;
                }

                if (GUILayout.Button("프리팹 저장", GUILayout.Width(84f), GUILayout.Height(20f)))
                {
                    controller.DebugWriteMonsterVisualScaleToPrefab(group.Key, next, out sandboxScaleMessage);
                }

                GUILayout.EndHorizontal();

                next = Mathf.Clamp(next, 0.3f, 6f);
                if (!Mathf.Approximately(next, current))
                {
                    foreach (var monster in group)
                    {
                        controller.DebugSetMonsterVisualScaleOverride(monster.Id, next / prefabRootScale);
                    }
                }
            }

            if (!string.IsNullOrEmpty(sandboxScaleMessage))
            {
                GUILayout.Label(sandboxScaleMessage);
            }
        }

        private void DrawSandboxTab()
        {
            if (controller == null || controller.State == null)
            {
                GUILayout.Label("State: unavailable");
                return;
            }

            var state = controller.State;
            GUILayout.Label($"단계 {state.Phase} · 몬스터 {state.Monsters.Count(m => !m.IsDead)}마리 " +
                            $"(샌드박스 더미 {state.DebugSandboxMonsterIds.Count}) · 소멸 더미 {state.GetExilePileCards().Count}장");
            GUILayout.Label($"플레이어 HP {state.Player.Hp}/{state.Player.MaxHp} · 방어막 {state.Player.Block}");
            GUILayout.Label("카드는 [Cards] 탭에서 손패에 넣고, 실제 HUD에서 눌러 사용하세요.");

            GUILayout.Space(8f);
            GUILayout.Label("── 몬스터 더미 ──");
            RefreshSandboxMonsterIds(state);
            if (sandboxMonsterIds.Length == 0)
            {
                GUILayout.Label("몬스터 카탈로그가 비어 있습니다.");
            }
            else
            {
                sandboxMonsterIndex = Mathf.Clamp(sandboxMonsterIndex, 0, sandboxMonsterIds.Length - 1);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("◀", GUILayout.Width(28f), GUILayout.Height(ButtonHeight)))
                {
                    sandboxMonsterIndex = (sandboxMonsterIndex - 1 + sandboxMonsterIds.Length) % sandboxMonsterIds.Length;
                }
                GUILayout.Label(sandboxMonsterIds[sandboxMonsterIndex], GUILayout.ExpandWidth(true));
                if (GUILayout.Button("▶", GUILayout.Width(28f), GUILayout.Height(ButtonHeight)))
                {
                    sandboxMonsterIndex = (sandboxMonsterIndex + 1) % sandboxMonsterIds.Length;
                }
                GUILayout.EndHorizontal();

                sandboxMonsterHp = DrawSandboxIntField("HP (0=카탈로그 값)", sandboxMonsterHp, 0, 999, 10);
                sandboxSpawnDistance = DrawSandboxIntField("플레이어로부터 거리", sandboxSpawnDistance, 1, 12, 1);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("더미 소환", GUILayout.Height(ButtonHeight)))
                {
                    controller.DebugSandboxSpawnMonster(sandboxMonsterIds[sandboxMonsterIndex], sandboxMonsterHp, sandboxSpawnDistance);
                }
                if (GUILayout.Button("소환한 더미 제거", GUILayout.Height(ButtonHeight)))
                {
                    controller.DebugSandboxRemoveSpawnedMonsters();
                }
                GUILayout.EndHorizontal();
                GUILayout.Label("제거는 이 패널이 소환한 더미만 지웁니다(맵이 배치한 몬스터는 그대로).");
            }

            // ── 소환 프리셋(2026-08-31) ────────────────────────────────────────────
            // 모델·실루엣 판정은 「나란히 놓고 비교」가 전부다(터렛 3레벨·프롭 2종). 한 마리씩
            // 카탈로그를 훑던 것을 한 버튼으로 줄 세운다. HP는 0 = 카탈로그 값.
            GUILayout.Space(6f);
            GUILayout.Label("── 소환 프리셋 (판정용 줄 세우기) ──");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("터렛 LV1·2·3", GUILayout.Height(ButtonHeight)))
            {
                controller.DebugSandboxSpawnSquad(DebugSpawnPresets.Turrets, 2, out sandboxSquadMessage);
            }

            if (GUILayout.Button("프롭 (철조각·고깔)", GUILayout.Height(ButtonHeight)))
            {
                controller.DebugSandboxSpawnSquad(DebugSpawnPresets.Props, 2, out sandboxSquadMessage);
            }

            GUILayout.EndHorizontal();
            if (GUILayout.Button("요괴 6종 (거구귀·그슨새·두두리·두억시니·야광귀·어둑시니)", GUILayout.Height(ButtonHeight)))
            {
                controller.DebugSandboxSpawnSquad(DebugSpawnPresets.Yogoe, 2, out sandboxSquadMessage);
            }

            if (!string.IsNullOrEmpty(sandboxSquadMessage))
            {
                GUILayout.Label(sandboxSquadMessage);
            }

            DrawSandboxMonsterScaleSection(state);

            GUILayout.Space(8f);
            GUILayout.Label("── 상태이상 주입 ──");
            sandboxStatusIndex = Mathf.Clamp(sandboxStatusIndex, 0, SandboxStatusKinds.Length - 1);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀", GUILayout.Width(28f), GUILayout.Height(ButtonHeight)))
            {
                sandboxStatusIndex = (sandboxStatusIndex - 1 + SandboxStatusKinds.Length) % SandboxStatusKinds.Length;
            }
            var statusKind = SandboxStatusKinds[sandboxStatusIndex];
            GUILayout.Label($"{statusKind} ({(StatusEffectInfo.IsCleansable(statusKind) ? "정화 O" : "정화 X")})", GUILayout.ExpandWidth(true));
            if (GUILayout.Button("▶", GUILayout.Width(28f), GUILayout.Height(ButtonHeight)))
            {
                sandboxStatusIndex = (sandboxStatusIndex + 1) % SandboxStatusKinds.Length;
            }
            GUILayout.EndHorizontal();

            sandboxStatusTurns = DrawSandboxIntField("지속 턴", sandboxStatusTurns, 1, 20, 1);
            sandboxStatusAmount = DrawSandboxIntField("수치", sandboxStatusAmount, 0, 20, 1);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("플레이어에게", GUILayout.Height(ButtonHeight)))
            {
                controller.DebugSandboxApplyStatusToPlayer(statusKind, sandboxStatusTurns, sandboxStatusAmount);
            }
            if (GUILayout.Button("모든 몬스터에게", GUILayout.Height(ButtonHeight)))
            {
                controller.DebugSandboxApplyStatusToMonsters(statusKind, sandboxStatusTurns, sandboxStatusAmount);
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("A12 전염병은 '정화 O' 디버프에만 보너스/전염이 걸립니다. 강화(Strength)로 눌러 대조해 보세요.");

            GUILayout.Space(8f);
            GUILayout.Label("── 소멸 더미 / 피격 ──");
            sandboxExileCount = DrawSandboxIntField("소멸시킬 장수", sandboxExileCount, 1, 30, 1);
            if (GUILayout.Button("소멸 더미 채우기", GUILayout.Height(ButtonHeight)))
            {
                controller.DebugSandboxFillExilePile(sandboxExileCount);
            }
            GUILayout.Label("A13 잔혼 공격의 타격 수와 U02 회수 대상이 이 더미에서 나옵니다(손패는 건드리지 않음).");

            sandboxPlayerDamage = DrawSandboxIntField("플레이어 피해량", sandboxPlayerDamage, 1, 200, 5);
            if (GUILayout.Button("플레이어 피격", GUILayout.Height(ButtonHeight)))
            {
                controller.DebugSandboxDamagePlayer(sandboxPlayerDamage);
            }
            GUILayout.Label("방어막이 먼저 흡수합니다 — D02/D05 피해 무효 확인용.");

            GUILayout.Space(8f);
            DrawSandboxHandlerAudit(state);

            GUILayout.Space(6f);
            GUILayout.Label($"상태: {controller.LastInputMessage}");
        }

        /// <summary>
        /// Lists catalog cards whose class overrides no rule hook (generic path). Not an error list — most cards
        /// legitimately run the generic path (U01 redraw, S00 scout.reveal, plain attack/defend). It exists
        /// because the opposite mistake is invisible: a rule written for a card whose class never registers
        /// (or whose override signature is mistyped) compiles cleanly while the card silently runs the generic
        /// path. If a card you just authored a rule for shows up here, that is the bug.
        /// </summary>
        private void DrawSandboxHandlerAudit(CombatState state)
        {
            GUILayout.Label("── 카드 규칙 등록 현황 ──");
            var handled = state.DebugHandledCardIds;
            GUILayout.Label($"고유 규칙(override)을 가진 카드 클래스: {handled.Count}장");

            var catalog = state.CardCatalog;
            if (catalog == null)
            {
                GUILayout.Label("카드 카탈로그 없음.");
                return;
            }

            // 설치(field) cards are excluded here: they dispatch by FieldObjectKind, so their card class never
            // overrides a hook and listing them would be pure noise. Their coverage is reported separately
            // below, where a miss really is a bug.
            var generic = catalog.Entries
                .Where(entry => entry.FieldObjectKind == CardFieldObjectKind.None)
                .Select(entry => entry.Id)
                .Where(id => !state.DebugIsCardHandled(id))
                .OrderBy(id => id, System.StringComparer.Ordinal)
                .ToList();

            GUILayout.Label($"고유 규칙 없이 기본 경로로 도는 카드: {generic.Count}장");
            GUILayout.Label(generic.Count == 0 ? "(없음)" : string.Join(", ", generic));
            GUILayout.Label("※ 정상인 경우가 대부분입니다. 방금 규칙을 작성한 카드가 여기 보이면 등록/override 누락입니다.");

            // A 설치 card with no handler for its kind throws at resolve time, so this list must stay empty.
            var brokenFieldCards = catalog.Entries
                .Where(entry => entry.FieldObjectKind != CardFieldObjectKind.None)
                .Where(entry => state.DebugDescribeCardHandling(entry).Contains("없음"))
                .Select(entry => $"{entry.Id} ({entry.FieldObjectKind})")
                .ToList();
            GUILayout.Label(brokenFieldCards.Count == 0
                ? "설치 카드 FieldObjectKind 핸들러: 전부 등록됨"
                : "⚠ 설치 카드 핸들러 누락: " + string.Join(", ", brokenFieldCards));
        }

        private void RefreshSandboxMonsterIds(CombatState state)
        {
            var catalog = state.MonsterCatalog;
            // MonsterCatalogEntry is a struct, so there is nothing to null-check here — an unset row shows up as
            // a blank id instead.
            var ids = catalog?.Entries
                ?.Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
                .Select(entry => entry.Id)
                .ToArray() ?? System.Array.Empty<string>();
            if (ids.Length != sandboxMonsterIds.Length)
            {
                sandboxMonsterIds = ids;
            }
        }

        private static int DrawSandboxIntField(string label, int value, int min, int max, int step)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {value}", GUILayout.Width(168f));
            if (GUILayout.Button("-", GUILayout.Width(24f), GUILayout.Height(20f)))
            {
                value -= step;
            }
            if (GUILayout.Button("+", GUILayout.Width(24f), GUILayout.Height(20f)))
            {
                value += step;
            }
            GUILayout.EndHorizontal();
            return Mathf.Clamp(value, min, max);
        }

        private static float DrawTimingSlider(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {value:0.000}", GUILayout.Width(200f));
            var result = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.EndHorizontal();
            return result;
        }

        private static bool IsMouseCaptureEvent(EventType eventType)
        {
            return eventType == EventType.MouseDown
                || eventType == EventType.MouseUp
                || eventType == EventType.MouseDrag
                || eventType == EventType.ScrollWheel;
        }

        private void ResolveCameraLightPanel()
        {
            if (cameraLightPanel == null)
            {
                cameraLightPanel = FindFirstObjectByType<CameraLightTestPanel>();
            }
        }

        private void ResolveController()
        {
            if (controller == null)
            {
                controller = GetComponentInParent<MapCombatController>();
            }

            if (controller == null)
            {
                controller = FindFirstObjectByType<MapCombatController>();
            }

            ResolveCameraLightPanel();
        }
    }
}

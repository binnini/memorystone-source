// NOTE(공개 발췌): 이 파일은 서드파티 에셋 「Cartoon FX Remaster (JMO Assets)」의 프리팹·타입을 경로/이름으로만 참조한다. 해당 에셋은 이 리포에 포함되지 않는다.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SeoulPlayup.Combat.Unity.Dev
{
    /// <summary>
    /// Lightweight OnGUI dev lab for rapidly checking character animation triggers, VFX anchors,
    /// source-to-target facing, and EffectPresentationController catalog playback.
    ///
    /// <para>🔗 <b>사용 정본은 <c>docs/vfx-lab-guide.md</c>.</b> 여기는 <b>"이 큐가 제대로 보이나"</b>를
    /// 답하는 저작·반복 벤치이고, <b>"실게임에서 제대로 터지나"</b>는
    /// <see cref="MonsterLabController"/>가 예고→집행으로 답한다.</para>
    ///
    /// <para>🔴 <b>절이 둘로 갈려 있다.</b> 개편(2026-08-07)으로 신설된
    /// <c>Pattern Stage</c> 절(<see cref="DrawPatternStageSection"/>, 파일 <c>*.Stage.cs</c>)이
    /// 그림 판정 담당이다 — 프로덕션 <see cref="EffectPresentationController.PlayArea"/>를 타므로
    /// <b>모드 C(PerTile)가 거기서만 보인다</b>. 아래 <c>Monster Pattern VFX Tuning</c> 절은
    /// 튜닝·CSV 저장 담당으로 남았고, 그 절의 재생 버튼은 <b>옛 직접 스폰 경로</b>라 칸별 스폰을
    /// 그리지 못한다(수치는 거기서, 판정은 무대 절에서).</para>
    /// </summary>
    public sealed partial class VfxAnimationLabController : MonoBehaviour
    {
        private const string PlayerAssetPath = "Assets/Art/Characters/player/Prefabs/Player.prefab";
        private const string DefaultCatalogPath = "Assets/Resources/Combat/DefaultEffectVfxCatalog.asset";
        private const string CardVfxAssignmentPath = "Assets/Data/Combat/Cards/Source/card_vfx_assignment_working.csv";
        private const string CardVfxCuePath = "Assets/Data/Combat/Cards/Source/combat_card_vfx_cues.csv";
        private const string CardCatalogCsvPath = "Assets/Data/Combat/Cards/Source/cards.csv";
        private const string MonsterCatalogPath = "Assets/Data/Combat/Monsters/Source/monster_catalog.csv";
        private const string MonsterAttackPatternsPath = "Assets/Data/Combat/Monsters/Source/monster_attack_patterns.csv";
        private const string MonsterVfxCuePath = "Assets/Data/Combat/Presentation/Source/combat_vfx_cues.csv";
        private const string MonsterPatternVfxBindingPath = "Assets/Data/Combat/Presentation/Source/monster_pattern_vfx_bindings.csv";

        private static readonly string[] MonsterAssetPaths =
        {
            "Assets/Art/Characters/monster/ThreeEyeDog/Prefabs/ThreeEyeDog.prefab",
            "Assets/Art/Characters/monster/Bulgasal/Prefabs/Bulgasal.prefab",
            "Assets/Art/Characters/monster/Bull/Prefabs/Bull.prefab",
            "Assets/Art/Characters/monster/LionMask/Prefabs/LionMask.prefab",
            "Assets/Art/Characters/monster/Pig/Prefabs/Pig.prefab",
            "Assets/Art/Characters/monster/tiger/Prefabs/Tiger.prefab",
            "Assets/Art/Characters/TinyRex/Prefabs/EnemyTinyRex.prefab"
        };

#if UNITY_EDITOR
        private static readonly string[] VfxBrowserFolders =
        {
            // Owned/authored VFX first-class so cue repoints can target them, not just vendor packs.
            "Assets/Art/VFX",
            "Assets/Prefabs/Vfx",
            "Assets/ThirdParty/Hovl Studio/Magic effects pack/Prefabs",
            "Assets/ThirdParty/JMO Assets/Cartoon FX Remaster/CFXR Prefabs",
            "Assets/ThirdParty/Matthew Guz",
            "Assets/ThirdParty/NamuFX",
            "Assets/ThirdParty/Travis Game Assets/Status Effects/Prefabs"
        };
#endif

        [Header("Scene Slots")]
        [SerializeField] private Transform playerSlot;
        [SerializeField] private Transform monsterSlot;
        [SerializeField] private Transform fieldCenter;
        [SerializeField] private EffectPresentationController effectPresentation;

        [Header("Catalog / Prefabs")]
        [SerializeField] private GameObject playerPrefab;
        [SerializeField] private GameObject[] monsterPrefabs = Array.Empty<GameObject>();
        [SerializeField] private EffectVfxCatalog vfxCatalog;

        [Header("Lab Options")]
        [SerializeField] private bool spawnOnStart = true;
        [SerializeField] private bool createRuntimeGround = true;
        [SerializeField] private bool showAnchors = true;
        [SerializeField] private bool snapFacingToHexSides = true;
        [SerializeField] private float hexFacingYawOffset = CombatFacingUtility.DefaultHexSideYawOffset;
        [SerializeField] private float rotationStepDegrees = 15f;
        [SerializeField] private Vector3 playerSpawnPosition = new Vector3(-1.6f, 0f, 0f);
        [SerializeField] private Vector3 monsterSpawnPosition = new Vector3(1.6f, 0f, 0f);
        [Tooltip("Player visual scale multiplier applied on spawn so the lab matches the game. PrototypeTest spawns the player at AtlasTileView.playerVisualLocalScale = 2.5x; the lab must match for WYSIWYG VFX-vs-character sizing. Monsters use the game's enemyVisualLocalScale = 1x, so they keep the prefab scale.")]
        [SerializeField] private Vector3 playerVisualLocalScale = new Vector3(2.5f, 2.5f, 2.5f);
        [SerializeField] private CharacterVfxAnchorKind vfxBrowserAnchor = CharacterVfxAnchorKind.HitCenter;
        [SerializeField] private float directVfxLifetime = 4f;
        [SerializeField] private Vector3 directVfxRotationEulerOffset;

        [Header("Card VFX Tuning Steps")]
        [SerializeField] private float tuningScaleStep = 0.05f;
        [SerializeField] private float tuningOffsetStep = 0.05f;
        [SerializeField] private float tuningRotationStep = 15f;
        [SerializeField] private float tuningLifetimeStep = 0.25f;
        [SerializeField] private float cardPreviewVfxDelaySeconds = 0.2f;
        [SerializeField] private float monsterPreviewVfxDelaySeconds = 0.2f;
        [Tooltip("Preview radius fed to FieldCenter / Field cues so scaleWithRadius has a visible effect in the lab.")]
        [SerializeField] private int fieldPreviewRadius = 2;

        [Header("Camera Control")]
        [Tooltip("Optional orbit pivot. Falls back to FieldCenter, then world origin.")]
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private float cameraOrbitSpeed = 0.35f;
        [SerializeField] private float cameraZoomSpeed = 0.6f;
        [SerializeField] private float cameraPanSpeed = 1f;
        [SerializeField] private float cameraMinDistance = 1.5f;
        [SerializeField] private float cameraMaxDistance = 30f;
        [SerializeField] private float cameraMinPitch = 5f;
        [SerializeField] private float cameraMaxPitch = 85f;
        [Tooltip("Gameplay camera field of view (fallback). 'Match Game Camera' reads the live gameplay vcam (CM_Prototype_BoardView) lens when present; this is the fallback. Authored game value is 50.")]
        [SerializeField] private float gameCameraFieldOfView = 50f;
        [Tooltip("Gameplay camera offset from its focus, i.e. camera-minus-player (fallback). 'Match Game Camera' reads the CombatCinemachineCameraProfile asset (targetOffset + followOffset) when present; this is the fallback. Authored game value is (0,7,-9), |.|=11.4.")]
        [SerializeField] private Vector3 gameCameraOffsetFromFocus = new Vector3(0f, 7f, -9f);

        private readonly List<GameObject> previewMarkers = new List<GameObject>();
        private readonly List<GameObject> directVfxInstances = new List<GameObject>();
#if UNITY_EDITOR
        private readonly List<VfxBrowserEntry> vfxBrowserEntries = new List<VfxBrowserEntry>();
        private readonly List<CardVfxAssignmentRow> cardAssignmentRows = new List<CardVfxAssignmentRow>();
        private readonly List<CardVfxCueRow> cardVfxCueRows = new List<CardVfxCueRow>();
        private readonly List<MonsterPatternVfxRow> monsterPatternRows = new List<MonsterPatternVfxRow>();
        private readonly List<MonsterVfxCueRow> monsterVfxCueRows = new List<MonsterVfxCueRow>();
        // Maps catalog monsterId -> visual prefab file name (e.g. "M006" -> "Tiger"), built from monster_catalog.csv.
        // Used to switch the spawned model so it follows the selected monster pattern in the tuning panel.
        private readonly Dictionary<string, string> monsterPrefabNameById = new Dictionary<string, string>(StringComparer.Ordinal);
#endif
        private GameObject playerInstance;
        private GameObject monsterInstance;
        private CharacterActorVisual playerVisual;
        private CharacterActorVisual monsterVisual;
        private int monsterIndex;
        private Vector2 scrollPosition;
        private Material previewMarkerMaterial;
        private Transform runtimeGroundRoot;
        private bool hasLastVfxEvent;
        private EffectResultEvent lastVfxEvent;
        private CharacterActorVisual lastVfxSource;
        private CharacterActorVisual lastVfxTarget;
        private CharacterVfxAnchorKind lastVfxAnchorKind = CharacterVfxAnchorKind.Root;
        private string lastVfxSummary = "No VFX played yet.";
        private int vfxBrowserIndex;
        private string vfxBrowserSearch = string.Empty;
        private int cardAssignmentIndex;
        private int monsterPatternIndex;

        // In-memory card VFX tuning. Only the cue identified by tuningCueId carries live edits; every
        // other cue resolves its tuning straight from the CSV. Nothing is written back to disk here ??        // the designer copies values out via "Copy CSV Values" / "Log CSV Values".
        private string tuningCueId = string.Empty;
        private float tunedScale = 1f;
        private bool tunedScaleWithRadius;
        private Vector3 tunedOffset;
        private Vector3 tunedRotation;
        private float tunedLifetime;
        private EffectVfxSpawnAnchor tunedSpawnAnchor = EffectVfxSpawnAnchor.Auto;
        // Unsaved prefab repoint for the active tuning cue. Empty = keep the CSV prefabPath; set via
        // "Use Browser Prefab" in either tuning panel and persisted only by Save To CSV.
        private string tunedPrefabPath = string.Empty;
        private string monsterTuningCueId = string.Empty;
        private string monsterTuningPatternId = string.Empty;
        private int monsterTuningOrder;

        // Persistent status-loop VFX authoring. Each StatusEffectKind keeps its own prefab + tuning so the
        // designer can attach a looping cue to player/monster, tune it live, and (in the editor) save it
        // back into the EffectVfxCatalog as a loop entry. Active instances are tracked so Apply/Clear toggle
        // and live tuning updates can find the spawned, actor-parented handles.
        private static readonly StatusEffectKind[] StatusLoopKinds =
            (StatusEffectKind[])Enum.GetValues(typeof(StatusEffectKind));
        private int statusLoopKindIndex;
        private bool statusLoopLoadedFromCatalog;
        private readonly Dictionary<StatusEffectKind, GameObject> statusLoopPrefabByKind =
            new Dictionary<StatusEffectKind, GameObject>();
        private readonly Dictionary<StatusEffectKind, StatusLoopTuning> statusLoopTuningByKind =
            new Dictionary<StatusEffectKind, StatusLoopTuning>();
        private readonly List<StatusLoopActive> statusLoopActives = new List<StatusLoopActive>();

        private sealed class StatusLoopTuning
        {
            public CharacterVfxAnchorKind Anchor = CharacterVfxAnchorKind.HitCenter;
            public float Scale = 1f;
            public Vector3 Offset;
            public Vector3 Rotation;
        }

        private sealed class StatusLoopActive
        {
            public StatusEffectKind Kind;
            public bool IsPlayer;
            public GameObject Handle;
        }

        // Movable / resizable / scalable control panel.
        private const int PanelWindowId = 0x5FA1A;
        private const float PanelChromeHeight = 112f;
        private const float MinPanelWidth = 240f;
        private const float MinPanelHeight = 180f;
        private const float MinPanelUiScale = 0.6f;
        private const float MaxPanelUiScale = 2.5f;
        private Rect panelRect;
        private bool panelRectInitialized;
        private float panelUiScale = 1f;
        private bool resizingPanel;

        // Mouse-driven orbit camera rig (RMB orbit, wheel zoom, MMB pan).
        private Camera labCamera;
        private float cameraYaw;
        private float cameraPitch;
        private float cameraDistance;
        private Vector3 cameraPivotPoint;
        private bool cameraRigInitialized;

        public GameObject PlayerInstance => playerInstance;
        public GameObject MonsterInstance => monsterInstance;
        public CharacterActorVisual PlayerVisual => playerVisual;
        public CharacterActorVisual MonsterVisual => monsterVisual;
        public bool ShowAnchors => showAnchors;

        private void Reset()
        {
            ResolveSceneReferences();
            ResolveDefaultAssets();
        }

        private void Awake()
        {
            ResolveSceneReferences();
            ResolveDefaultAssets();
            ApplyCatalog();
            EnsureRuntimeGround();
        }

        private void Start()
        {
            if (spawnOnStart)
            {
                Respawn();
            }

            InitializeCameraRig();
        }

        private void OnValidate()
        {
            if (rotationStepDegrees <= 0f)
            {
                rotationStepDegrees = 15f;
            }

            cardPreviewVfxDelaySeconds = Mathf.Max(0f, cardPreviewVfxDelaySeconds);
            monsterPreviewVfxDelaySeconds = Mathf.Max(0f, monsterPreviewVfxDelaySeconds);

            ResolveDefaultAssets();
            ApplyAnchorVisibility();
        }

        public void Respawn()
        {
#if UNITY_EDITOR
            StopGrabMode();
#endif
            ClearCharacters();
            ApplyCatalog();
            playerInstance = SpawnCharacter(playerPrefab, playerSlot, playerSpawnPosition, "Lab Player", out playerVisual, playerVisualLocalScale);
            monsterInstance = SpawnCharacter(CurrentMonsterPrefab, monsterSlot, monsterSpawnPosition, "Lab Monster", out monsterVisual);
            FaceEachOther();
            ApplyAnchorVisibility();
        }

        public void PreviousMonster()
        {
            if (monsterPrefabs == null || monsterPrefabs.Length == 0)
            {
                return;
            }

            monsterIndex = (monsterIndex - 1 + monsterPrefabs.Length) % monsterPrefabs.Length;
            RespawnMonsterOnly();
        }

        public void NextMonster()
        {
            if (monsterPrefabs == null || monsterPrefabs.Length == 0)
            {
                return;
            }

            monsterIndex = (monsterIndex + 1) % monsterPrefabs.Length;
            RespawnMonsterOnly();
        }

        public void FaceEachOther()
        {
            if (playerInstance != null && monsterInstance != null)
            {
                FaceTransform(playerInstance.transform, monsterInstance.transform.position);
                FaceTransform(monsterInstance.transform, playerInstance.transform.position);
            }
        }

        public void ClearVfx()
        {
#if UNITY_EDITOR
            StopGrabMode();
#endif
            if (effectPresentation != null)
            {
                effectPresentation.ClearSpawnedEffects();
            }

            for (var i = 0; i < previewMarkers.Count; i++)
            {
                DestroyObjectSafely(previewMarkers[i]);
            }

            previewMarkers.Clear();
            ClearDirectVfx();
            ClearAllStatusLoops();
        }

        private void OnGUI()
        {
            EnsurePanelRect();

            // Camera control happens under the same GUI matrix so mouse positions line up with the
            // panel rect; events over the panel are ignored so dragging the window never moves the camera.
            var previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(panelUiScale, panelUiScale, 1f));

#if UNITY_EDITOR
            HandleGrabInput();
#endif
            HandleCameraInput();
            HandlePanelResizeInput();

            // Size is driven by panelRect (the resize grip writes width/height during the callback), so we
            // only copy back the dragged position; otherwise the returned rect would clobber a live resize.
            var drawn = GUILayout.Window(
                PanelWindowId,
                panelRect,
                DrawPanelWindow,
                "VFX / Animation Lab",
                GUILayout.Width(panelRect.width),
                GUILayout.Height(panelRect.height));
            panelRect.x = drawn.x;
            panelRect.y = drawn.y;

            GUI.matrix = previousMatrix;
        }

        private void EnsurePanelRect()
        {
            if (panelRectInitialized)
            {
                return;
            }

            panelRect = new Rect(12f, 12f, 360f, Mathf.Min(760f, Screen.height - 24f));
            panelRectInitialized = true;
        }

        private void DrawPanelWindow(int windowId)
        {
            DrawPanelChrome();

            var bodyHeight = Mathf.Max(80f, panelRect.height - PanelChromeHeight);
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Height(bodyHeight));
            DrawPanelBody();
            GUILayout.EndScrollView();

            DrawResizeGrip();

            // Only the title strip drags the window, so buttons and the resize grip stay clickable.
            GUI.DragWindow(new Rect(0f, 0f, panelRect.width, 20f));
        }

        private void DrawPanelChrome()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"UI Scale {panelUiScale:0.00}x", GUILayout.Width(110f));
            if (GUILayout.Button("-", GUILayout.Width(28f)))
            {
                panelUiScale = Mathf.Clamp(panelUiScale - 0.1f, MinPanelUiScale, MaxPanelUiScale);
            }

            if (GUILayout.Button("+", GUILayout.Width(28f)))
            {
                panelUiScale = Mathf.Clamp(panelUiScale + 0.1f, MinPanelUiScale, MaxPanelUiScale);
            }

            if (GUILayout.Button("Reset Panel"))
            {
                panelUiScale = 1f;
                panelRect = new Rect(12f, 12f, 360f, Mathf.Min(760f, Screen.height - 24f));
            }
            GUILayout.EndHorizontal();

            // Independent width / height sliders. Sliders give reliable drag-to-size on top of the corner
            // grip; the returned rect copies back x/y only, so writes to width/height here persist.
            GUILayout.BeginHorizontal();
            GUILayout.Label($"W {panelRect.width:0}", GUILayout.Width(64f));
            panelRect.width = Mathf.Round(GUILayout.HorizontalSlider(panelRect.width, MinPanelWidth, MaxPanelWidth()));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"H {panelRect.height:0}", GUILayout.Width(64f));
            panelRect.height = Mathf.Round(GUILayout.HorizontalSlider(panelRect.height, MinPanelHeight, MaxPanelHeight()));
            GUILayout.EndHorizontal();

            GUILayout.Label("Drag title to move ??W/H sliders or ??corner to resize ??RMB orbit / wheel zoom / MMB pan");
        }

        private float MaxPanelWidth()
        {
            return Mathf.Max(MinPanelWidth + 40f, Screen.width / Mathf.Max(0.01f, panelUiScale) - 24f);
        }

        private float MaxPanelHeight()
        {
            return Mathf.Max(MinPanelHeight + 40f, Screen.height / Mathf.Max(0.01f, panelUiScale) - 24f);
        }

        // Grip only flags the start of a resize here; the drag/up are handled at the OnGUI top level
        // (HandlePanelResizeInput) so the resize keeps tracking even when the cursor leaves the window.
        private void DrawResizeGrip()
        {
            var grip = new Rect(panelRect.width - 22f, panelRect.height - 22f, 18f, 18f);
            GUI.Box(grip, "Resize");

            var e = Event.current;
            if (e.type == EventType.MouseDown && grip.Contains(e.mousePosition))
            {
                resizingPanel = true;
                e.Use();
            }
        }

        private void HandlePanelResizeInput()
        {
            if (!resizingPanel)
            {
                return;
            }

            var e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDrag:
                    panelRect.width = Mathf.Clamp(panelRect.width + e.delta.x, MinPanelWidth, MaxPanelWidth());
                    panelRect.height = Mathf.Clamp(panelRect.height + e.delta.y, MinPanelHeight, MaxPanelHeight());
                    e.Use();
                    break;
                case EventType.MouseUp:
                    resizingPanel = false;
                    e.Use();
                    break;
            }
        }

        // Collapsible section state for the reorganized panel, so the many controls are grouped instead of
        // dumped in one flat list.
        private bool sectionStage = true;
        private bool sectionSetup = true;
        private bool sectionCamera;
        private bool sectionAnimation;
        private bool sectionVfxMatrix;
        private bool sectionDirectVfx;
        private bool sectionStatusLoop;
        private bool sectionMonsterPattern;
        private bool sectionCardTuning = true;
        private bool sectionAssetBrowser;

        private static bool DrawSectionHeader(string title, bool open)
        {
            var style = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold
            };
            GUILayout.Space(4f);
            return GUILayout.Toggle(open, (open ? "▼  " : "▶  ") + title, style);
        }

        private void DrawPanelBody()
        {
            // 개편(2026-08-07)으로 신설된 주 작업 절 — 프로덕션 경로 재생이라 모드 C가 여기서만 보인다.
            // 아래 "Monster Pattern VFX Tuning"은 튜닝·CSV 저장 담당으로 남는다(재생은 옛 경로).
            sectionStage = DrawSectionHeader("Pattern Stage (production path)", sectionStage);
            if (sectionStage)
            {
                DrawPatternStageSection();
            }

            sectionSetup = DrawSectionHeader("Scene / Setup", sectionSetup);
            if (sectionSetup)
            {
                GUILayout.Label($"Monster: {CurrentMonsterName}");
                if (GUILayout.Button("Respawn")) Respawn();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Previous Monster")) PreviousMonster();
                if (GUILayout.Button("Next Monster")) NextMonster();
                GUILayout.EndHorizontal();
                if (GUILayout.Button("Face Each Other")) FaceEachOther();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Rotate Player Left")) Rotate(playerInstance, -rotationStepDegrees);
                if (GUILayout.Button("Rotate Player Right")) Rotate(playerInstance, rotationStepDegrees);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Rotate Monster Left")) Rotate(monsterInstance, -rotationStepDegrees);
                if (GUILayout.Button("Rotate Monster Right")) Rotate(monsterInstance, rotationStepDegrees);
                GUILayout.EndHorizontal();
                var nextShowAnchors = GUILayout.Toggle(showAnchors, "Show Anchors");
                if (nextShowAnchors != showAnchors)
                {
                    showAnchors = nextShowAnchors;
                    ApplyAnchorVisibility();
                }
            }

            sectionCamera = DrawSectionHeader("Camera", sectionCamera);
            if (sectionCamera)
            {
                DrawCameraControls();
            }

            sectionAnimation = DrawSectionHeader("Animation", sectionAnimation);
            if (sectionAnimation)
            {
                DrawAnimationButtons("Player Animation", playerVisual);
                DrawAnimationButtons("Monster Animation", monsterVisual);
            }

            sectionVfxMatrix = DrawSectionHeader("VFX Matrix (status / generic)", sectionVfxMatrix);
            if (sectionVfxMatrix)
            {
                DrawVfxMatrix("Player VFX Matrix", playerVisual, isPlayer: true);
                DrawVfxMatrix("Monster VFX Matrix", monsterVisual, isPlayer: false);
            }

            sectionDirectVfx = DrawSectionHeader("VFX (direct)", sectionDirectVfx);
            if (sectionDirectVfx)
            {
                DrawVfxButtons();
            }

            sectionStatusLoop = DrawSectionHeader("Status Loop VFX", sectionStatusLoop);
            if (sectionStatusLoop)
            {
                DrawStatusLoopVfxPanel();
            }

            sectionMonsterPattern = DrawSectionHeader("Monster Pattern VFX", sectionMonsterPattern);
            if (sectionMonsterPattern)
            {
                DrawMonsterPatternVfxTuning();
            }

            sectionCardTuning = DrawSectionHeader("Card VFX (size / position tuning)", sectionCardTuning);
            if (sectionCardTuning)
            {
                DrawCardVfxAssignmentBrowser();
            }

            sectionAssetBrowser = DrawSectionHeader("VFX Asset Browser", sectionAssetBrowser);
            if (sectionAssetBrowser)
            {
                DrawVfxAssetBrowser();
            }

            GUILayout.Space(6f);
            if (GUILayout.Button("Clear VFX")) ClearVfx();
        }

        private void DrawCameraControls()
        {
            GUILayout.Space(8f);
            GUILayout.Label("Camera (RMB orbit / wheel zoom / MMB pan)");
            if (!cameraRigInitialized || labCamera == null)
            {
                if (GUILayout.Button("Bind Camera"))
                {
                    InitializeCameraRig();
                }

                return;
            }

            GUILayout.Label($"yaw={cameraYaw:0} pitch={cameraPitch:0} dist={cameraDistance:0.0}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Zoom In")) ZoomCamera(-1f);
            if (GUILayout.Button("Zoom Out")) ZoomCamera(1f);
            if (GUILayout.Button("Reset Camera")) ResetCameraToDefault();
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Match Game Camera (WYSIWYG)")) MatchGameCamera();
        }

        private void HandleCameraInput()
        {
            if (!cameraRigInitialized || labCamera == null)
            {
                InitializeCameraRig();
                if (!cameraRigInitialized)
                {
                    return;
                }
            }

            var e = Event.current;
            if (e == null)
            {
                return;
            }

            // Never steal input that belongs to the control panel.
            if (panelRectInitialized && panelRect.Contains(e.mousePosition))
            {
                return;
            }

            switch (e.type)
            {
                case EventType.MouseDrag when e.button == 1:
                    cameraYaw += e.delta.x * cameraOrbitSpeed;
                    cameraPitch = Mathf.Clamp(cameraPitch - e.delta.y * cameraOrbitSpeed, cameraMinPitch, cameraMaxPitch);
                    ApplyCameraRig();
                    e.Use();
                    break;
                case EventType.MouseDrag when e.button == 2:
                    PanCamera(e.delta);
                    ApplyCameraRig();
                    e.Use();
                    break;
                case EventType.ScrollWheel:
                    ZoomCamera(e.delta.y);
                    e.Use();
                    break;
            }
        }

        private void ZoomCamera(float amount)
        {
            cameraDistance = Mathf.Clamp(cameraDistance + amount * cameraZoomSpeed, cameraMinDistance, cameraMaxDistance);
            ApplyCameraRig();
        }

        private void PanCamera(Vector2 delta)
        {
            if (labCamera == null)
            {
                return;
            }

            var scale = Mathf.Max(0.0005f, cameraDistance * 0.0015f) * cameraPanSpeed;
            var right = labCamera.transform.right;
            var up = labCamera.transform.up;
            cameraPivotPoint += (-right * delta.x + up * delta.y) * scale;
        }

        private void InitializeCameraRig()
        {
            labCamera = Camera.main;
            if (labCamera == null)
            {
                cameraRigInitialized = false;
                return;
            }

            cameraPivotPoint = ResolveCameraPivot();
            var offset = labCamera.transform.position - cameraPivotPoint;
            if (offset.sqrMagnitude < 1e-4f)
            {
                offset = new Vector3(0f, 4.4f, -5.6f);
            }

            cameraDistance = Mathf.Clamp(offset.magnitude, cameraMinDistance, cameraMaxDistance);
            var dir = offset.normalized;
            cameraPitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg, cameraMinPitch, cameraMaxPitch);
            cameraYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            cameraRigInitialized = true;
            ApplyCameraRig();
        }

        private void ResetCameraToDefault()
        {
            if (labCamera == null)
            {
                InitializeCameraRig();
                return;
            }

            cameraPivotPoint = ResolveCameraPivot();
            cameraYaw = 0f;
            cameraPitch = Mathf.Clamp(45f, cameraMinPitch, cameraMaxPitch);
            cameraDistance = Mathf.Clamp(7.5f, cameraMinDistance, cameraMaxDistance);
            ApplyCameraRig();
        }

        // Frames the lab orbit rig like the gameplay camera (FOV + distance + angle from the action) so the
        // VFX apparent size you tune here matches what shows up in PrototypeTest. Yaw/distance can still be
        // adjusted afterwards; what matters for size parity is FOV + distance.
        private void MatchGameCamera()
        {
            if (labCamera == null)
            {
                InitializeCameraRig();
                if (labCamera == null)
                {
                    return;
                }
            }

            // Pull the real gameplay-camera values (vcam lens FOV + CombatCinemachineCameraProfile follow offset) so this
            // stays correct if the game camera is retuned, instead of relying on hand-copied constants. Falls back to the
            // serialized gameCamera* fields when the live vcam / profile asset can't be located.
            RefreshGameCameraReferenceValues();

            cameraPivotPoint = ResolveCameraPivot();
            var offset = gameCameraOffsetFromFocus;
            if (offset.sqrMagnitude < 1e-4f)
            {
                offset = new Vector3(0f, 7f, -9f);
            }

            cameraDistance = Mathf.Clamp(offset.magnitude, cameraMinDistance, cameraMaxDistance);
            var dir = offset.normalized;
            cameraPitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg, cameraMinPitch, cameraMaxPitch);
            cameraYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            labCamera.fieldOfView = Mathf.Max(1f, gameCameraFieldOfView);
            ApplyCameraRig();
        }

        // Resolves the gameplay camera's authored values into gameCameraFieldOfView / gameCameraOffsetFromFocus.
        // FOV comes from a live gameplay vcam if one is loaded; the offset comes from the CombatCinemachineCameraProfile
        // asset (camera-minus-player = targetOffset + followOffset). Anything not found keeps its current serialized value.
        private void RefreshGameCameraReferenceValues()
        {
            var vcam = ResolveGameplayVirtualCamera();
            if (vcam != null)
            {
                gameCameraFieldOfView = vcam.m_Lens.FieldOfView;
            }

#if UNITY_EDITOR
            var profile = LoadGameplayCameraProfile();
            if (profile != null)
            {
                gameCameraOffsetFromFocus = profile.TargetOffset + profile.FollowOffset;
            }
#endif
        }

        private static Cinemachine.CinemachineVirtualCamera ResolveGameplayVirtualCamera()
        {
            var cams = UnityEngine.Object.FindObjectsOfType<Cinemachine.CinemachineVirtualCamera>(true);
            if (cams == null || cams.Length == 0)
            {
                return null;
            }

            return cams.FirstOrDefault(c => c != null && c.name.IndexOf("BoardView", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? cams.FirstOrDefault(c => c != null && c.GetComponent<SeoulPlayup.Combat.Unity.CinemachineCombatCameraBinder>() != null);
        }

#if UNITY_EDITOR
        private static CombatCinemachineCameraProfile LoadGameplayCameraProfile()
        {
            var guids = AssetDatabase.FindAssets("t:CombatCinemachineCameraProfile");
            if (guids == null || guids.Length == 0)
            {
                return null;
            }

            var paths = guids
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !string.IsNullOrEmpty(p))
                .OrderByDescending(p => p.IndexOf("PrototypeTest", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToArray();

            foreach (var path in paths)
            {
                var profile = AssetDatabase.LoadAssetAtPath<CombatCinemachineCameraProfile>(path);
                if (profile != null)
                {
                    return profile;
                }
            }

            return null;
        }
#endif

        private Vector3 ResolveCameraPivot()
        {
            if (cameraPivot != null)
            {
                return cameraPivot.position;
            }

            return fieldCenter != null ? fieldCenter.position : Vector3.zero;
        }

        private void ApplyCameraRig()
        {
            if (labCamera == null)
            {
                return;
            }

            var pitchRad = cameraPitch * Mathf.Deg2Rad;
            var yawRad = cameraYaw * Mathf.Deg2Rad;
            var dir = new Vector3(
                Mathf.Cos(pitchRad) * Mathf.Sin(yawRad),
                Mathf.Sin(pitchRad),
                Mathf.Cos(pitchRad) * Mathf.Cos(yawRad));
            var position = cameraPivotPoint + dir * cameraDistance;
            labCamera.transform.position = position;
            labCamera.transform.rotation = Quaternion.LookRotation((cameraPivotPoint - position).normalized, Vector3.up);
        }

        private void DrawAnimationButtons(string title, CharacterActorVisual visual)
        {
            GUILayout.Space(8f);
            GUILayout.Label(title);
            GUILayout.Label(DescribeAnimator(visual));
            if (visual == null)
            {
                return;
            }

            var shown = 0;
            GUILayout.BeginHorizontal();
            if (visual.CanPlayMove)
            {
                shown++;
                if (GUILayout.Button("Move")) visual.SetMoveSpeed(1f);
                if (GUILayout.Button("PulseMove")) visual.PulseMove(1f, 0.35f);
                if (GUILayout.Button("Stop")) visual.SetMoveSpeed(0f);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            shown += DrawAttackButtons(visual);
            shown += DrawAnimationButton("Hit", visual.CanPlayHit, visual.TriggerHit);
            shown += DrawAnimationButton("Knockback", visual.CanPlayKnockback, visual.TriggerKnockback);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            shown += DrawAnimationButton("Dead", visual.CanPlayDead, visual.TriggerDead);
            shown += DrawAnimationButton("Shield", visual.CanPlayShield, visual.TriggerShield);
            shown += DrawAnimationButton("Buff", visual.CanPlayBuff, visual.TriggerBuff);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            shown += DrawAnimationButton("Field", visual.CanPlayField, visual.TriggerField);
            shown += DrawAnimationButton("Dance", visual.CanPlayDance, visual.TriggerDance);
            GUILayout.EndHorizontal();

            if (shown == 0)
            {
                GUILayout.Label("No playable animation parameters on this controller.");
            }
        }

        private static int DrawAttackButtons(CharacterActorVisual visual)
        {
            if (visual == null || !visual.CanPlayAttack)
            {
                return 0;
            }

            var animator = visual.Animator;
            if (animator == null || animator.parameters == null)
            {
                return DrawAnimationButton("Attack", true, visual.TriggerAttack);
            }

            var shown = 0;
            for (var i = 0; i < animator.parameters.Length; i++)
            {
                var parameter = animator.parameters[i];
                if (parameter.type != AnimatorControllerParameterType.Trigger &&
                    parameter.type != AnimatorControllerParameterType.Bool)
                {
                    continue;
                }

                if (!IsAttackParameter(parameter.name))
                {
                    continue;
                }

                var parameterName = parameter.name;
                var label = string.Equals(parameterName, "AttackTrigger", StringComparison.Ordinal) ||
                            string.Equals(parameterName, "Attack", StringComparison.Ordinal)
                    ? "Attack"
                    : parameterName;
                shown += DrawAnimationButton(label, true, () => visual.TriggerAttack(parameterName));
            }

            return shown > 0 ? shown : DrawAnimationButton("Attack", true, visual.TriggerAttack);
        }

        private static bool IsAttackParameter(string parameterName)
        {
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                return false;
            }

            if (string.Equals(parameterName, "Attack", StringComparison.Ordinal) ||
                string.Equals(parameterName, "AttackTrigger", StringComparison.Ordinal))
            {
                return true;
            }

            return parameterName.StartsWith("Attack", StringComparison.Ordinal) &&
                   parameterName.Length > "Attack".Length &&
                   char.IsDigit(parameterName["Attack".Length]);
        }

        private static int DrawAnimationButton(string label, bool canPlay, Action action)
        {
            if (!canPlay)
            {
                return 0;
            }

            if (GUILayout.Button(label))
            {
                action?.Invoke();
            }

            return 1;
        }

        private static string DescribeAnimator(CharacterActorVisual visual)
        {
            if (visual == null)
            {
                return "Animator: no visual";
            }

            var animator = visual.Animator;
            if (animator == null)
            {
                return "Animator: missing";
            }

            var controllerName = animator.runtimeAnimatorController != null
                ? animator.runtimeAnimatorController.name
                : "no controller";
            var avatarName = animator.avatar != null
                ? $"{animator.avatar.name}/{(animator.avatar.isValid ? "valid" : "invalid")}"
                : "no avatar";
            var stateName = "(not playing)";
            if (animator.isActiveAndEnabled && animator.runtimeAnimatorController != null && animator.layerCount > 0)
            {
                var state = animator.GetCurrentAnimatorStateInfo(0);
                stateName = $"stateHash={state.shortNameHash} t={state.normalizedTime:0.00}";
            }

            var builder = new StringBuilder();
            builder.Append("Animator: ")
                .Append(animator.gameObject.name)
                .Append(", ")
                .Append(animator.enabled ? "enabled" : "disabled")
                .Append(", speed=")
                .Append(animator.speed.ToString("0.00"))
                .Append(", ")
                .Append(controllerName)
                .Append(", ")
                .Append(avatarName)
                .Append(", ")
                .Append(stateName)
                .Append('\n')
                .Append("Last: ")
                .Append(string.IsNullOrEmpty(visual.LastAnimationCommand) ? "(none)" : visual.LastAnimationCommand)
                .Append(", trigger=")
                .Append(string.IsNullOrEmpty(visual.LastTriggerName) ? "(none)" : visual.LastTriggerName)
                .Append(", move=")
                .Append(visual.LastMoveSpeed.ToString("0.00"));

            if (!string.IsNullOrEmpty(visual.LastAnimationWarning))
            {
                builder.Append('\n').Append("Warning: ").Append(visual.LastAnimationWarning);
            }

            if (animator.parameters != null && animator.parameters.Length > 0)
            {
                builder.Append('\n').Append("Params: ");
                for (var i = 0; i < animator.parameters.Length; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(", ");
                    }

                    AppendParameterValue(builder, animator, animator.parameters[i]);
                }
            }

            return builder.ToString();
        }

        private static void AppendParameterValue(StringBuilder builder, Animator animator, AnimatorControllerParameter parameter)
        {
            builder.Append(parameter.name).Append('=');
            switch (parameter.type)
            {
                case AnimatorControllerParameterType.Float:
                    builder.Append(animator.GetFloat(parameter.name).ToString("0.00"));
                    break;
                case AnimatorControllerParameterType.Int:
                    builder.Append(animator.GetInteger(parameter.name));
                    break;
                case AnimatorControllerParameterType.Bool:
                    builder.Append(animator.GetBool(parameter.name));
                    break;
                case AnimatorControllerParameterType.Trigger:
                    builder.Append("trigger");
                    break;
                default:
                    builder.Append(parameter.type);
                    break;
            }
        }

        private void DrawVfxMatrix(string title, CharacterActorVisual visual, bool isPlayer)
        {
            GUILayout.Space(8f);
            GUILayout.Label(title);
            if (visual == null)
            {
                GUILayout.Label("No visual spawned.");
                return;
            }

            var rows = isPlayer ? BuildPlayerVfxMatrixRows(visual) : BuildMonsterVfxMatrixRows(visual);
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                GUILayout.Label(DescribeVfxMatrixRow(row));
            }
        }

        private List<VfxMatrixRow> BuildPlayerVfxMatrixRows(CharacterActorVisual visual)
        {
            return new List<VfxMatrixRow>
            {
                CreateMatrixRow("Move", visual.CanPlayMove, new EffectResultEvent(
                    EffectKind.Push,
                    targetUnitId: "player",
                    sourceUnitId: "player",
                    sourceRef: "player.move")),
                CreateMatrixRow("Attack", visual.CanPlayAttack, new EffectResultEvent(
                    EffectKind.Damage,
                    targetUnitId: "monster",
                    appliedAmount: 1,
                    sourceUnitId: "player",
                    sourceActorKind: "player",
                    targetActorKind: "monster",
                    sourceRef: "lab.player.attack")),
                CreateMatrixRow("Hit", visual.CanPlayHit, new EffectResultEvent(
                    EffectKind.Damage,
                    targetUnitId: "player",
                    appliedAmount: 1,
                    sourceRef: "lab.player.hit")),
                CreateMatrixRow("Knockback", visual.CanPlayKnockback, new EffectResultEvent(
                    EffectKind.Knockback,
                    targetUnitId: "player",
                    amount: 1,
                    appliedAmount: 1,
                    sourceRef: "player.knockback")),
                CreateMatrixRow("Dead", visual.CanPlayDead, new EffectResultEvent(
                    EffectKind.Damage,
                    targetUnitId: "player",
                    appliedAmount: 1,
                    sourceRef: "player.death")),
                CreateMatrixRow("Shield", visual.CanPlayShield, new EffectResultEvent(
                    EffectKind.Block,
                    targetUnitId: "player",
                    appliedAmount: 1,
                    sourceRef: "D01")),
                CreateMatrixRow("Buff", visual.CanPlayBuff, new EffectResultEvent(
                    EffectKind.StatusEffectApplied,
                    targetUnitId: "player",
                    appliedAmount: 1,
                    sourceRef: "lab.player.buff",
                    statusKind: StatusEffectKind.Agility)),
                CreateMatrixRow("Field", visual.CanPlayField, new EffectResultEvent(
                    EffectKind.FogReveal,
                    targetUnitId: "field",
                    amount: 1,
                    appliedAmount: 1,
                    radius: 1,
                    sourceRef: "field.lab")),
                CreateMatrixRow("Dance", visual.CanPlayDance, new EffectResultEvent(
                    EffectKind.StatusEffectApplied,
                    targetUnitId: "player",
                    appliedAmount: 1,
                    sourceRef: "player.dance",
                    statusKind: StatusEffectKind.Agility))
            };
        }

        private List<VfxMatrixRow> BuildMonsterVfxMatrixRows(CharacterActorVisual visual)
        {
            var rows = new List<VfxMatrixRow>();
            var attackParameters = GetAttackParameterNames(visual);
            if (attackParameters.Count == 0)
            {
                rows.Add(CreateMatrixRow("Attack", visual.CanPlayAttack, CreateMonsterAttackEvent("Attack")));
            }
            else
            {
                for (var i = 0; i < attackParameters.Count; i++)
                {
                    var attack = attackParameters[i];
                    rows.Add(CreateMatrixRow(attack, true, CreateMonsterAttackEvent(attack)));
                }
            }

            rows.Add(CreateMatrixRow("Hit", visual.CanPlayHit, new EffectResultEvent(
                EffectKind.Damage,
                targetUnitId: "monster",
                appliedAmount: 1,
                sourceRef: "lab.monster.hit")));
            rows.Add(CreateMatrixRow("Knockback", visual.CanPlayKnockback, new EffectResultEvent(
                EffectKind.Knockback,
                targetUnitId: "monster",
                amount: 1,
                appliedAmount: 1,
                sourceRef: "knockback")));
            rows.Add(CreateMatrixRow("Dead", visual.CanPlayDead, new EffectResultEvent(
                EffectKind.Damage,
                targetUnitId: "monster",
                appliedAmount: 1,
                sourceRef: "monster.death")));
            rows.Add(CreateMatrixRow("Shield", visual.CanPlayShield, new EffectResultEvent(
                EffectKind.Block,
                targetUnitId: "monster",
                appliedAmount: 1,
                sourceRef: "monster.shield")));
            rows.Add(CreateMatrixRow("Buff", visual.CanPlayBuff, new EffectResultEvent(
                EffectKind.StatusEffectApplied,
                targetUnitId: "monster",
                appliedAmount: 1,
                sourceRef: "lab.monster.buff",
                statusKind: StatusEffectKind.Agility)));
            rows.Add(CreateMatrixRow("Move", visual.CanPlayMove, new EffectResultEvent(
                EffectKind.Push,
                targetUnitId: "monster",
                amount: 1,
                appliedAmount: 1,
                sourceRef: "monster.move")));
            rows.Add(CreateMatrixRow("Dance", visual.CanPlayDance, new EffectResultEvent(
                EffectKind.StatusEffectApplied,
                targetUnitId: "monster",
                appliedAmount: 1,
                sourceRef: "monster.dance",
                statusKind: StatusEffectKind.Agility)));
            return rows;
        }

        private EffectResultEvent CreateMonsterAttackEvent(string attackParameter)
        {
            var sourceRef = ResolveMonsterAttackSourceRef(CurrentMonsterName, attackParameter);
            return new EffectResultEvent(
                EffectKind.Damage,
                targetUnitId: "player",
                appliedAmount: 1,
                sourceUnitId: "monster",
                sourceActorKind: "monster",
                targetActorKind: "player",
                sourceRef: sourceRef);
        }

        private VfxMatrixRow CreateMatrixRow(string action, bool canAnimate, EffectResultEvent resultEvent)
        {
            var anchor = EffectVfxAnchorPolicy.ResolveTargetAnchor(resultEvent);
            var catalog = vfxCatalog != null ? vfxCatalog : effectPresentation != null ? effectPresentation.VfxCatalog : null;
            if (catalog != null && catalog.TryResolve(resultEvent, out var entry))
            {
                return new VfxMatrixRow(action, canAnimate, resultEvent, anchor, true, DescribePrefabs(entry));
            }

            return new VfxMatrixRow(action, canAnimate, resultEvent, anchor, false, "Missing");
        }

        private static string DescribeVfxMatrixRow(VfxMatrixRow row)
        {
            var anim = row.CanAnimate ? "Anim OK" : "Anim Missing";
            var vfx = row.HasVfx ? "VFX OK" : "VFX Missing";
            var source = string.IsNullOrWhiteSpace(row.ResultEvent.SourceRef) ? "(kind fallback)" : row.ResultEvent.SourceRef;
            return $"{row.Action}: {anim} / {vfx} / {source} / {row.Anchor} / {row.PrefabSummary}";
        }

        private static List<string> GetAttackParameterNames(CharacterActorVisual visual)
        {
            var names = new List<string>();
            var animator = visual != null ? visual.Animator : null;
            if (animator == null || animator.parameters == null)
            {
                return names;
            }

            for (var i = 0; i < animator.parameters.Length; i++)
            {
                var parameter = animator.parameters[i];
                if ((parameter.type == AnimatorControllerParameterType.Trigger ||
                     parameter.type == AnimatorControllerParameterType.Bool) &&
                    IsAttackParameter(parameter.name))
                {
                    names.Add(parameter.name);
                }
            }

            return names;
        }

        private static string ResolveMonsterAttackSourceRef(string monsterName, string attackParameter)
        {
            if (string.Equals(monsterName, "ThreeEyeDog", StringComparison.Ordinal))
            {
                return string.Equals(attackParameter, "Attack2", StringComparison.Ordinal)
                    ? "monster.pattern.A002"
                    : "monster.pattern.A001";
            }

            if (string.Equals(monsterName, "Bulgasal", StringComparison.Ordinal))
            {
                switch (attackParameter)
                {
                    case "Attack2": return "monster.pattern.A004";
                    case "Attack3": return "monster.pattern.A005";
                    case "Attack4": return "monster.pattern.A006";
                    case "Attack5": return "monster.pattern.A007";
                    default: return "monster.pattern.A003";
                }
            }

            if (string.Equals(monsterName, "Bull", StringComparison.Ordinal))
            {
                return string.Equals(attackParameter, "Attack2", StringComparison.Ordinal)
                    ? "monster.pattern.A016"
                    : "monster.pattern.A015";
            }

            if (string.Equals(monsterName, "LionMask", StringComparison.Ordinal))
            {
                return string.Equals(attackParameter, "Attack2", StringComparison.Ordinal)
                    ? "monster.pattern.A012"
                    : "monster.pattern.A011";
            }

            if (string.Equals(monsterName, "Pig", StringComparison.Ordinal))
            {
                return string.Equals(attackParameter, "Attack2", StringComparison.Ordinal)
                    ? "monster.pattern.A014"
                    : "monster.pattern.A013";
            }

            if (string.Equals(monsterName, "Tiger", StringComparison.Ordinal))
            {
                switch (attackParameter)
                {
                    case "Attack2": return "monster.pattern.A009";
                    case "Attack3": return "monster.pattern.A010";
                    default: return "monster.pattern.A008";
                }
            }

            return "lab.monster.attack";
        }

        private void DrawVfxButtons()
        {
            GUILayout.Space(8f);
            GUILayout.Label("Representative VFX");
            if (GUILayout.Button("Player Attack -> Monster HitCenter"))
            {
                PlayActorVfx(playerVisual, monsterVisual, new EffectResultEvent(
                    EffectKind.Damage,
                    targetUnitId: "monster",
                    appliedAmount: 12,
                    sourceUnitId: "player",
                    sourceActorKind: "player",
                    targetActorKind: "monster",
                    sourceRef: "lab.player.attack"));
            }

            if (GUILayout.Button("Monster Attack -> Player HitCenter"))
            {
                PlayActorVfx(monsterVisual, playerVisual, new EffectResultEvent(
                    EffectKind.Damage,
                    targetUnitId: "player",
                    appliedAmount: 7,
                    sourceUnitId: "monster",
                    sourceActorKind: "monster",
                    targetActorKind: "player",
                    sourceRef: "lab.monster.attack"));
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Player Hit")) PlaySelfVfx(playerVisual, "player", EffectKind.Damage, "lab.player.hit", 5);
            if (GUILayout.Button("Monster Hit")) PlaySelfVfx(monsterVisual, "monster", EffectKind.Damage, "lab.monster.hit", 5);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Player Move/Ground")) PlaySelfVfx(playerVisual, "player", EffectKind.Push, "player.move", 1);
            if (GUILayout.Button("Monster Move/Ground")) PlaySelfVfx(monsterVisual, "monster", EffectKind.Push, "monster.move", 1);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Player Buff")) PlayBuffVfx(playerVisual, "player", sourceRef: "lab.player.buff");
            if (GUILayout.Button("Monster Buff")) PlayBuffVfx(monsterVisual, "monster", sourceRef: "lab.monster.buff");
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Area / Field Center")) PlayFieldCenterVfx();

            GUILayout.Label("Card VFX");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("A01 Attack")) PlayCardVfx("A01", EffectKind.Damage, monsterVisual, "monster");
            if (GUILayout.Button("A03 Heal")) PlayCardVfx("A03", EffectKind.Heal, playerVisual, "player");
            if (GUILayout.Button("D01 Shield")) PlayCardVfx("D01", EffectKind.Block, playerVisual, "player");
            if (GUILayout.Button("S02 Reward")) PlayCardVfx("S02", EffectKind.Heal, playerVisual, "player");
            GUILayout.EndHorizontal();

            GUILayout.Label("Status VFX");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Poison")) PlayStatusVfx(monsterVisual, "monster", "status.poison.apply", StatusEffectKind.Poison);
            if (GUILayout.Button("Slow")) PlayStatusVfx(monsterVisual, "monster", "status.slow.apply", StatusEffectKind.Slow);
            if (GUILayout.Button("Stun")) PlayStatusVfx(playerVisual, "player", "status.stun.apply", StatusEffectKind.Stun);
            if (GUILayout.Button("Buff")) PlayStatusVfx(playerVisual, "player", "status.buff.apply", StatusEffectKind.Agility);
            GUILayout.EndHorizontal();

            GUILayout.Label("Trap VFX");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Trap Damage")) PlayTrapVfx(playerVisual, "player", EffectKind.Damage, "trap.spike");
            if (GUILayout.Button("Trap Slow")) PlayTrapVfx(playerVisual, "player", EffectKind.StatusEffectApplied, "trap.slow", StatusEffectKind.Slow);
            GUILayout.EndHorizontal();

            GUILayout.Label("Monster Pattern VFX");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("A001 Slash")) PlayMonsterPatternVfx("monster.pattern.A001");
            if (GUILayout.Button("A002 Cleave")) PlayMonsterPatternVfx("monster.pattern.A002");
            if (GUILayout.Button("A003 Slow")) PlayMonsterPatternVfx("monster.pattern.A003", StatusEffectKind.Slow);
            GUILayout.EndHorizontal();

#if UNITY_EDITOR
            // 특성 발동 연출(패턴이 아니라 trait 전이로 뜨는 것 — 은신 재은신 등). 패턴 큐 목록에는 없으므로
            // 여기에 전용 버튼을 둔다. 애니(전용 트리거) + 카탈로그 특성 큐를 실제 발동 순서(애니 → 짧은 딜레이 → VFX)로
            // 재생한다. PlayPreviewAnimation이 에디터 전용이라 이 블록·헬퍼 모두 UNITY_EDITOR로 감싼다.
            GUILayout.Label("Trait / Special VFX (특성 발동)");
            GUILayout.BeginHorizontal();
            foreach (var trait in TraitVfxPreviews)
            {
                if (GUILayout.Button(trait.Label)) PlayTraitCuePreview(trait.SourceRef, trait.AnimationTrigger);
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("※ 해당 몬스터(은신=어둑시니 M009)를 선택한 상태에서 재생. 연막 색(검보라)은 Play/Bloom에서만 정확합니다.");
#endif

            GUILayout.Label("Object VFX");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Chest Open")) PlayObjectVfx("object.treasure_chest.open");
            if (GUILayout.Button("Chest Reward")) PlayObjectVfx("object.treasure_chest.reward");
            if (GUILayout.Button("Chest Claim")) PlayObjectVfx("object.treasure_chest.claim");
            GUILayout.EndHorizontal();

            DrawLastVfxDiagnostics();
        }

        private void PlayCardVfx(string sourceRef, EffectKind kind, CharacterActorVisual target, string targetUnitId)
        {
            PlayActorVfx(playerVisual, target, new EffectResultEvent(
                kind,
                targetUnitId: targetUnitId,
                appliedAmount: 1,
                sourceUnitId: "player",
                sourceActorKind: "player",
                targetActorKind: targetUnitId,
                sourceRef: sourceRef));
        }

#if UNITY_EDITOR
        private readonly struct TraitVfxPreview
        {
            public TraitVfxPreview(string label, string sourceRef, string animationTrigger)
            {
                Label = label;
                SourceRef = sourceRef;
                AnimationTrigger = animationTrigger;
            }

            public string Label { get; }
            public string SourceRef { get; }
            public string AnimationTrigger { get; }
        }

        // 특성 발동 연출 프리뷰 목록. 새 특성에 전용 VFX/애니가 붙으면 여기 한 줄 추가하면 랩 버튼이 생긴다.
        private static readonly TraitVfxPreview[] TraitVfxPreviews =
        {
            new TraitVfxPreview("어둑시니 은신 (Attack4 + 연막)", MonsterTraitAnnouncement.StealthHiddenRef, "Attack4"),
        };

        // 특성 발동을 실제 순서로 재생: 전용 애니 트리거 → 짧은 딜레이 → 카탈로그 특성 큐(self·MonsterTraitTriggered).
        // 게임에서 재은신은 DispatchEffect가 Attack4를 쏘고 같은 순간 trait.stealth.hidden VFX가 뜬다
        // (MapCombatController.Presentation). 여기서는 그 순서를 그대로 흉내 낸다.
        private void PlayTraitCuePreview(string sourceRef, string animationTrigger)
        {
            if (monsterVisual == null)
            {
                Debug.LogWarning("[Trait VFX] 몬스터가 스폰돼 있지 않다 — 먼저 몬스터를 선택/스폰하세요.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(animationTrigger))
            {
                PlayPreviewAnimation(monsterVisual, animationTrigger);
            }

            PlayAfterDelay(0.2f, () => PlaySelfVfx(monsterVisual, "monster", EffectKind.MonsterTraitTriggered, sourceRef, 0));
            Debug.Log($"[Trait VFX] sourceRef={sourceRef} animation={DisplayPreviewAnimation(animationTrigger)}");
        }
#endif

        private void PlayStatusVfx(CharacterActorVisual target, string targetUnitId, string sourceRef, StatusEffectKind statusKind)
        {
            PlayActorVfx(target, target, new EffectResultEvent(
                EffectKind.StatusEffectApplied,
                targetUnitId: targetUnitId,
                appliedAmount: 1,
                sourceUnitId: targetUnitId,
                sourceRef: sourceRef,
                statusKind: statusKind));
        }

        private void PlayTrapVfx(
            CharacterActorVisual target,
            string targetUnitId,
            EffectKind kind,
            string sourceRef,
            StatusEffectKind? statusKind = null)
        {
            PlayActorVfx(null, target, new EffectResultEvent(
                kind,
                targetUnitId: targetUnitId,
                appliedAmount: 1,
                sourceRef: sourceRef,
                statusKind: statusKind));
        }

        private void PlayMonsterPatternVfx(string sourceRef, StatusEffectKind? statusKind = null)
        {
            PlayActorVfx(monsterVisual, playerVisual, new EffectResultEvent(
                statusKind.HasValue ? EffectKind.StatusEffectApplied : EffectKind.Damage,
                targetUnitId: "player",
                appliedAmount: 1,
                sourceUnitId: "monster",
                sourceActorKind: "monster",
                targetActorKind: "player",
                sourceRef: sourceRef,
                statusKind: statusKind));
        }

        private void DrawStatusLoopVfxPanel()
        {
            GUILayout.Space(8f);
            GUILayout.Label("Status Loop VFX (persistent, attached to actor)");

            if (!statusLoopLoadedFromCatalog)
            {
                LoadStatusLoopFromCatalog();
                statusLoopLoadedFromCatalog = true;
            }

            PruneStatusLoopActives();

            statusLoopKindIndex = Mathf.Clamp(statusLoopKindIndex, 0, StatusLoopKinds.Length - 1);
            var kind = StatusLoopKinds[statusLoopKindIndex];
            var tuning = GetStatusLoopTuning(kind);
            statusLoopPrefabByKind.TryGetValue(kind, out var prefab);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Prev Status")) StepStatusLoopKind(-1);
            GUILayout.Label($"{statusLoopKindIndex + 1}/{StatusLoopKinds.Length}: {kind} ({StatusKindKo(kind)})");
            if (GUILayout.Button("Next Status")) StepStatusLoopKind(1);
            GUILayout.EndHorizontal();

            GUILayout.Label($"Prefab: {(prefab != null ? prefab.name : "(none assigned)")}");
            GUILayout.Label($"Anchor={tuning.Anchor}  scale={FormatFloat(tuning.Scale)}  offset={FormatVector(tuning.Offset)}  rot={FormatVector(tuning.Rotation)}");
            GUILayout.Label($"Active: player={IsStatusLoopActive(kind, true)}  monster={IsStatusLoopActive(kind, false)}");

#if UNITY_EDITOR
            var filtered = GetFilteredVfxBrowserEntries();
            if (filtered.Count > 0)
            {
                vfxBrowserIndex = Mathf.Clamp(vfxBrowserIndex, 0, filtered.Count - 1);
                var selected = filtered[vfxBrowserIndex];
                if (GUILayout.Button($"Assign Browser Prefab -> {kind}  ({selected.Prefab.name})"))
                {
                    statusLoopPrefabByKind[kind] = selected.Prefab;
                    ReapplyStatusLoop(kind);
                }
            }
            else
            {
                GUILayout.Label("Pick a prefab in the Project VFX Browser below, then assign it here.");
            }
#endif

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply Player")) ApplyStatusLoop(kind, true);
            if (GUILayout.Button("Apply Monster")) ApplyStatusLoop(kind, false);
            if (GUILayout.Button("Clear Player")) ClearStatusLoop(kind, true);
            if (GUILayout.Button("Clear Monster")) ClearStatusLoop(kind, false);
            GUILayout.EndHorizontal();

            GUILayout.Label("Anchor");
            GUILayout.BeginHorizontal();
            DrawStatusLoopAnchorButton(kind, tuning, "Root", CharacterVfxAnchorKind.Root);
            DrawStatusLoopAnchorButton(kind, tuning, "Hit", CharacterVfxAnchorKind.HitCenter);
            DrawStatusLoopAnchorButton(kind, tuning, "Ground", CharacterVfxAnchorKind.Ground);
            DrawStatusLoopAnchorButton(kind, tuning, "Head", CharacterVfxAnchorKind.Head);
            GUILayout.EndHorizontal();

            DrawStatusLoopScaleRow(kind, tuning);
            DrawStatusLoopVectorRow(kind, "Offset", tuning.Offset, value => tuning.Offset = value, tuningOffsetStep);
            DrawStatusLoopVectorRow(kind, "Rotation", tuning.Rotation, value => tuning.Rotation = value, tuningRotationStep);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset Tuning")) ResetStatusLoopTuning(kind);
            if (GUILayout.Button("Clear All Loops")) ClearAllStatusLoops();
            if (GUILayout.Button("Reload From Catalog")) LoadStatusLoopFromCatalog();
#if UNITY_EDITOR
            if (GUILayout.Button("Save To Catalog")) SaveStatusLoopToCatalog(kind);
#endif
            GUILayout.EndHorizontal();
        }

        private StatusLoopTuning GetStatusLoopTuning(StatusEffectKind kind)
        {
            if (!statusLoopTuningByKind.TryGetValue(kind, out var tuning) || tuning == null)
            {
                tuning = new StatusLoopTuning();
                statusLoopTuningByKind[kind] = tuning;
            }

            return tuning;
        }

        private void StepStatusLoopKind(int delta)
        {
            var count = StatusLoopKinds.Length;
            statusLoopKindIndex = (statusLoopKindIndex + delta + count) % count;
        }

        private bool IsStatusLoopActive(StatusEffectKind kind, bool isPlayer)
        {
            for (var i = 0; i < statusLoopActives.Count; i++)
            {
                var active = statusLoopActives[i];
                if (active.Kind == kind && active.IsPlayer == isPlayer && active.Handle != null)
                {
                    return true;
                }
            }

            return false;
        }

        private void ApplyStatusLoop(StatusEffectKind kind, bool isPlayer)
        {
            if (effectPresentation == null)
            {
                lastVfxSummary = "Status loop: EffectPresentationController missing.";
                return;
            }

            if (!statusLoopPrefabByKind.TryGetValue(kind, out var prefab) || prefab == null)
            {
                lastVfxSummary = $"Status loop: no prefab assigned for {kind}.";
                return;
            }

            var visual = isPlayer ? playerVisual : monsterVisual;
            if (visual == null)
            {
                lastVfxSummary = $"Status loop: no {(isPlayer ? "player" : "monster")} spawned.";
                return;
            }

            ClearStatusLoop(kind, isPlayer);

            var tuning = GetStatusLoopTuning(kind);
            TryGetAnchorTransform(visual, tuning.Anchor, out var anchorTf);
            var position = anchorTf != null ? anchorTf.position : ResolveAnchorPosition(visual, tuning.Anchor);
            var entry = BuildStatusLoopEntry(kind, prefab, tuning, isPlayer ? EffectVfxTargetFilter.Player : EffectVfxTargetFilter.Monster);
            var handle = effectPresentation.PlayLoop(entry, anchorTf, position, Quaternion.identity, 0);
            if (handle != null)
            {
                statusLoopActives.Add(new StatusLoopActive { Kind = kind, IsPlayer = isPlayer, Handle = handle });
                lastVfxSummary = $"Status loop {kind} applied to {(isPlayer ? "player" : "monster")} ({prefab.name}).";
            }
        }

        private void ReapplyStatusLoop(StatusEffectKind kind)
        {
            var hadPlayer = IsStatusLoopActive(kind, true);
            var hadMonster = IsStatusLoopActive(kind, false);
            if (hadPlayer) ApplyStatusLoop(kind, true);
            if (hadMonster) ApplyStatusLoop(kind, false);
        }

        private void UpdateStatusLoopLive(StatusEffectKind kind)
        {
            if (effectPresentation == null)
            {
                return;
            }

            var tuning = GetStatusLoopTuning(kind);
            // Flat arrow auras shear if scaled vertically, so non-Stun loops scale on X/Z only (Y stays 1);
            // Stun scales uniformly. Mirrors EffectPresentationController.ResolveLoopScaleVector at spawn time.
            var localScale = EffectPresentationController.ResolveLoopScaleVector(kind, tuning.Scale);
            var worldRotation = Quaternion.Euler(tuning.Rotation);
            for (var i = 0; i < statusLoopActives.Count; i++)
            {
                var active = statusLoopActives[i];
                if (active.Kind != kind || active.Handle == null)
                {
                    continue;
                }

                var visual = active.IsPlayer ? playerVisual : monsterVisual;
                TryGetAnchorTransform(visual, tuning.Anchor, out var anchorTf);
                effectPresentation.UpdateLoopTransform(active.Handle, anchorTf, tuning.Offset, worldRotation, localScale);
            }
        }

        private void ClearStatusLoop(StatusEffectKind kind, bool isPlayer)
        {
            for (var i = statusLoopActives.Count - 1; i >= 0; i--)
            {
                var active = statusLoopActives[i];
                if (active.Kind == kind && active.IsPlayer == isPlayer)
                {
                    effectPresentation?.StopLoop(active.Handle);
                    statusLoopActives.RemoveAt(i);
                }
            }
        }

        private void ClearAllStatusLoops()
        {
            for (var i = 0; i < statusLoopActives.Count; i++)
            {
                effectPresentation?.StopLoop(statusLoopActives[i].Handle);
            }

            statusLoopActives.Clear();
            effectPresentation?.StopAllLoops();
        }

        private void PruneStatusLoopActives()
        {
            statusLoopActives.RemoveAll(active => active == null || active.Handle == null);
        }

        private void ResetStatusLoopTuning(StatusEffectKind kind)
        {
            statusLoopTuningByKind[kind] = new StatusLoopTuning();
            UpdateStatusLoopLive(kind);
        }

        private bool TryGetAnchorTransform(CharacterActorVisual visual, CharacterVfxAnchorKind anchorKind, out Transform anchorTransform)
        {
            anchorTransform = null;
            if (visual == null)
            {
                return false;
            }

            if (visual.TryGetVfxAnchor(anchorKind, out var anchor) && anchor != null)
            {
                anchorTransform = anchor;
                return true;
            }

            return false;
        }

        private EffectVfxCatalog.Entry BuildStatusLoopEntry(
            StatusEffectKind kind,
            GameObject prefab,
            StatusLoopTuning tuning,
            EffectVfxTargetFilter targetFilter)
        {
            return new EffectVfxCatalog.Entry(
                EffectKind.StatusEffectApplied,
                new[] { prefab },
                targetFilter: targetFilter,
                scaleMultiplier: tuning.Scale,
                scaleWithRadius: false,
                positionOffset: tuning.Offset,
                rotationEulerOffset: tuning.Rotation,
                cueId: $"status.loop.{kind}",
                displayName: $"Status Loop {kind}",
                category: "Status Loop",
                loop: true,
                attachToActor: true,
                statusKind: kind,
                loopAnchor: tuning.Anchor);
        }

        private void LoadStatusLoopFromCatalog()
        {
            var catalog = vfxCatalog != null
                ? vfxCatalog
                : effectPresentation != null ? effectPresentation.VfxCatalog : null;
            if (catalog == null)
            {
                lastVfxSummary = "Status loop: no catalog to load from.";
                return;
            }

            var loaded = 0;
            for (var i = 0; i < StatusLoopKinds.Length; i++)
            {
                var kind = StatusLoopKinds[i];
                if (!catalog.TryResolveStatusLoop(kind, EffectVfxTargetFilter.Any, out var entry) || entry == null)
                {
                    continue;
                }

                GameObject prefab = null;
                foreach (var candidate in entry.Prefabs)
                {
                    if (candidate != null)
                    {
                        prefab = candidate;
                        break;
                    }
                }

                if (prefab == null)
                {
                    continue;
                }

                statusLoopPrefabByKind[kind] = prefab;
                var tuning = GetStatusLoopTuning(kind);
                tuning.Anchor = entry.LoopAnchor;
                tuning.Scale = entry.ScaleMultiplier;
                tuning.Offset = entry.PositionOffset;
                tuning.Rotation = entry.RotationOffset.eulerAngles;
                loaded++;
            }

            lastVfxSummary = $"Status loop: loaded {loaded} cue(s) from catalog.";
        }

        private void DrawStatusLoopAnchorButton(StatusEffectKind kind, StatusLoopTuning tuning, string label, CharacterVfxAnchorKind anchorKind)
        {
            var selected = tuning.Anchor == anchorKind;
            if (GUILayout.Toggle(selected, label, GUI.skin.button) != selected)
            {
                tuning.Anchor = anchorKind;
                UpdateStatusLoopLive(kind);
            }
        }

        private void DrawStatusLoopScaleRow(StatusEffectKind kind, StatusLoopTuning tuning)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Scale {FormatFloat(tuning.Scale)}", GUILayout.Width(160f));
            if (GUILayout.Button("-")) { tuning.Scale = Mathf.Max(0.01f, tuning.Scale - tuningScaleStep); UpdateStatusLoopLive(kind); }
            if (GUILayout.Button("+")) { tuning.Scale += tuningScaleStep; UpdateStatusLoopLive(kind); }
            if (GUILayout.Button("x0.9")) { tuning.Scale = Mathf.Max(0.01f, tuning.Scale * 0.9f); UpdateStatusLoopLive(kind); }
            if (GUILayout.Button("x1.1")) { tuning.Scale *= 1.1f; UpdateStatusLoopLive(kind); }
            GUILayout.EndHorizontal();
        }

        private void DrawStatusLoopVectorRow(StatusEffectKind kind, string label, Vector3 value, Action<Vector3> setter, float step)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label} {FormatVector(value)}", GUILayout.Width(160f));
            if (GUILayout.Button("X-")) { value.x -= step; setter(value); UpdateStatusLoopLive(kind); }
            if (GUILayout.Button("X+")) { value.x += step; setter(value); UpdateStatusLoopLive(kind); }
            if (GUILayout.Button("Y-")) { value.y -= step; setter(value); UpdateStatusLoopLive(kind); }
            if (GUILayout.Button("Y+")) { value.y += step; setter(value); UpdateStatusLoopLive(kind); }
            if (GUILayout.Button("Z-")) { value.z -= step; setter(value); UpdateStatusLoopLive(kind); }
            if (GUILayout.Button("Z+")) { value.z += step; setter(value); UpdateStatusLoopLive(kind); }
            GUILayout.EndHorizontal();
        }

        private static string StatusKindKo(StatusEffectKind kind) => StatusEffectInfo.DisplayName(kind);

#if UNITY_EDITOR
        private void SaveStatusLoopToCatalog(StatusEffectKind kind)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<EffectVfxCatalog>(DefaultCatalogPath);
            if (catalog == null)
            {
                lastVfxSummary = $"Status loop: catalog not found at {DefaultCatalogPath}.";
                return;
            }

            if (!statusLoopPrefabByKind.TryGetValue(kind, out var prefab) || prefab == null)
            {
                lastVfxSummary = $"Status loop: no prefab assigned for {kind}.";
                return;
            }

            var tuning = GetStatusLoopTuning(kind);
            var entry = BuildStatusLoopEntry(kind, prefab, tuning, EffectVfxTargetFilter.Any);
            var merged = catalog.Entries
                .Where(existing => !(existing != null && existing.Loop && existing.StatusKind == kind))
                .Concat(new[] { entry })
                .ToArray();
            catalog.SetEntries(merged);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            lastVfxSummary = $"Status loop {kind} saved to catalog ({prefab.name}).";
        }
#endif

        private void DrawMonsterPatternVfxTuning()
        {
            GUILayout.Space(8f);
            GUILayout.Label("Monster Pattern VFX Tuning (explicit save only)");
#if UNITY_EDITOR
            if (monsterPatternRows.Count == 0 || monsterVfxCueRows.Count == 0)
            {
                RefreshMonsterVfxAuthoringData();
            }

            if (monsterPatternRows.Count == 0)
            {
                GUILayout.Label("No monster pattern/VFX rows found.");
                if (GUILayout.Button("Reload Monster VFX CSV")) RefreshMonsterVfxAuthoringData();
                return;
            }

            monsterPatternIndex = Mathf.Clamp(monsterPatternIndex, 0, monsterPatternRows.Count - 1);
            var row = monsterPatternRows[monsterPatternIndex];
            if (!TryGetMonsterCueById(row.VfxCueId, out var cue))
            {
                GUILayout.Label($"{row.PatternId}: missing vfxCueId {row.VfxCueId}");
                if (GUILayout.Button("Reload Monster VFX CSV")) RefreshMonsterVfxAuthoringData();
                return;
            }

            GUILayout.Label($"{monsterPatternIndex + 1}/{monsterPatternRows.Count}: {row.PatternId} / {row.DisplayName}");
            GUILayout.Label($"Monster={row.MonsterId} {row.MonsterName}  Animation={row.AnimationTrigger}");
            GUILayout.Label($"vfxCueId={row.VfxCueId}  sourceRef=monster.pattern.{row.PatternId}  order={row.BindingOrder}");
            GUILayout.Label($"Kind={cue.EffectKind}  Target={cue.TargetFilter}  Prefab={Path.GetFileNameWithoutExtension(cue.PrefabPath)}");
            GUILayout.Label($"CSV scale={FormatFloat(cue.ScaleMultiplier)} radius={(cue.ScaleWithRadius ? "on" : "off")} offset={FormatVector(cue.Offset)}");
            GUILayout.Label($"CSV rot={FormatVector(cue.RotationEuler)} life={FormatFloat(cue.LifetimeOverride)} bindingAnchor={DisplaySpawnAnchor(row.BindingSpawnAnchor)} cueDefaultAnchor={DisplaySpawnAnchor(cue.SpawnAnchor)}");
            GUILayout.Label($"Anim+VFX preview: animation={DisplayPreviewAnimation(row.AnimationTrigger)} delay={FormatFloat(monsterPreviewVfxDelaySeconds)}s");
            DrawMonsterCatalogDrift(cue, row);

            var sharedPatterns = monsterPatternRows.Where(item => string.Equals(item.VfxCueId, cue.VfxCueId, StringComparison.Ordinal)).ToArray();
            if (sharedPatterns.Length > 1)
            {
                GUILayout.Label("Shared cue patterns: " + string.Join(", ", sharedPatterns.Select(item => $"{item.PatternId}/{item.MonsterId}")));
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reload CSV")) RefreshMonsterVfxAuthoringData();
            if (GUILayout.Button("Reload Catalog + CSV")) ReloadMonsterVfxAuthoringData();
            if (GUILayout.Button("Prev Pattern")) StepMonsterPattern(-1);
            if (GUILayout.Button("Next Pattern")) StepMonsterPattern(1);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"Tune {cue.VfxCueId}")) BeginMonsterTuning(row, cue);
            if (GUILayout.Button("Play CSV (Tuned)")) PlayMonsterCsvVfx(row, cue);
            if (GUILayout.Button("Play Anim + CSV")) PlayMonsterAnimationAndVfx(row, cue, useCurrentSettings: false);
            if (GUILayout.Button("Play Catalog")) PlayMonsterCatalogVfx(row, cue);
            GUILayout.EndHorizontal();

            DrawMonsterVfxTuningPanel(row, cue);
#else
            GUILayout.Label("Monster pattern VFX tuning requires the Unity Editor.");
#endif
        }

#if UNITY_EDITOR
        private void RefreshMonsterVfxAuthoringData()
        {
            monsterPatternRows.Clear();
            monsterVfxCueRows.Clear();

            var catalogRows = ReadCsvRows(MonsterCatalogPath);
            var monsterNames = catalogRows
                .ToDictionary(row => GetCsv(row, "monsterId"), row => GetCsv(row, "displayName"), StringComparer.Ordinal);
            monsterPrefabNameById.Clear();
            foreach (var row in catalogRows)
            {
                var monsterId = GetCsv(row, "monsterId");
                var prefabName = Path.GetFileNameWithoutExtension(GetCsv(row, "visualPrefabPath"));
                if (!string.IsNullOrWhiteSpace(monsterId) && !string.IsNullOrWhiteSpace(prefabName))
                {
                    monsterPrefabNameById[monsterId] = prefabName;
                }
            }
            var firstMonsterByPattern = ReadCsvRows(CombatCsvPaths.MonsterDirectory + "/monster_pattern_bindings.csv")
                .Where(row => ParseBool(GetCsv(row, "enabled")))
                .GroupBy(row => GetCsv(row, "patternId"), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => GetCsv(group.OrderBy(row => ParseFloat(GetCsv(row, "order"))).First(), "monsterId"), StringComparer.Ordinal);

            var attackRows = ReadCsvRows(MonsterAttackPatternsPath)
                .ToDictionary(row => GetCsv(row, "patternId"), row => row, StringComparer.Ordinal);
            var bindingRows = ReadCsvRows(MonsterPatternVfxBindingPath)
                .Where(row => ParseBool(GetCsv(row, "enabled")))
                .OrderBy(row => GetCsv(row, "patternId"), StringComparer.Ordinal)
                .ThenBy(row => ParseFloat(GetCsv(row, "order")))
                .ToList();
            if (bindingRows.Count == 0)
            {
                bindingRows = attackRows.Values
                    .Select(row => new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["patternId"] = GetCsv(row, "patternId"),
                        ["vfxCueId"] = GetCsv(row, "vfxCueId"),
                        ["spawnAnchor"] = string.Empty,
                        ["order"] = "1",
                        ["enabled"] = "true"
                    })
                    .ToList();
            }

            foreach (var binding in bindingRows)
            {
                var patternId = GetCsv(binding, "patternId");
                attackRows.TryGetValue(patternId, out var row);
                firstMonsterByPattern.TryGetValue(patternId, out var monsterId);
                monsterNames.TryGetValue(monsterId ?? string.Empty, out var monsterName);
                monsterPatternRows.Add(new MonsterPatternVfxRow(
                    patternId,
                    row != null ? GetCsv(row, "displayName") : string.Empty,
                    monsterId,
                    monsterName,
                    row != null ? GetCsv(row, "animationTrigger") : string.Empty,
                    GetCsv(binding, "vfxCueId"),
                    row != null ? (int)ParseFloat(GetCsv(row, "areaRadius")) : 0,
                   GetCsv(binding, "spawnAnchor"),
                   (int)ParseFloat(GetCsv(binding, "order"), 1f),
                   ParseFloat(GetCsv(binding, "delaySeconds"))));
            }

            foreach (var row in ReadCsvRows(MonsterVfxCuePath))
            {
                monsterVfxCueRows.Add(new MonsterVfxCueRow(
                    GetCsv(row, "vfxCueId"),
                    GetCsv(row, "effectKind"),
                    GetCsv(row, "targetFilter"),
                    GetCsv(row, "sourceRef"),
                    GetCsv(row, "matchSourceRefPrefix"),
                    GetCsv(row, "spawnAnchor"),
                    GetCsv(row, "prefabPath"),
                    ParseFloat(GetCsv(row, "scaleMultiplier"), 1f),
                    ParseBool(GetCsv(row, "scaleWithRadius")),
                    new Vector3(
                        ParseFloat(GetCsv(row, "offsetX")),
                        ParseFloat(GetCsv(row, "offsetY")),
                        ParseFloat(GetCsv(row, "offsetZ"))),
                   new Vector3(
                       ParseFloat(GetCsv(row, "rotationX")),
                       ParseFloat(GetCsv(row, "rotationY")),
                       ParseFloat(GetCsv(row, "rotationZ"))),
                   ParseFloat(GetCsv(row, "lifetimeOverride")),
                   ParseFloat(GetCsv(row, "delaySeconds")),
                   GetCsv(row, "designerNote"),
                   ParseBool(GetCsv(row, "followSourceAnchor"))));
            }

            monsterPatternIndex = Mathf.Clamp(monsterPatternIndex, 0, Mathf.Max(0, monsterPatternRows.Count - 1));
            SyncMonsterModelToCurrentPattern();
        }

        private void ReloadMonsterVfxAuthoringData()
        {
            AssetDatabase.Refresh();
            vfxCatalog = AssetDatabase.LoadAssetAtPath<EffectVfxCatalog>(DefaultCatalogPath);
            ApplyCatalog();
            RefreshMonsterVfxAuthoringData();
        }

        private void StepMonsterPattern(int delta)
        {
            if (monsterPatternRows.Count == 0)
            {
                monsterPatternIndex = 0;
                return;
            }

            monsterPatternIndex = (monsterPatternIndex + delta + monsterPatternRows.Count) % monsterPatternRows.Count;
            SyncMonsterModelToCurrentPattern();
        }

        // Switches the spawned monster model so it matches the monster owning the currently selected pattern.
        // Without this, the model is driven only by Previous/Next Monster (monsterIndex) and stays on the
        // default ThreeEyeDog while the tuning panel previews another monster's pattern.
        private void SyncMonsterModelToCurrentPattern()
        {
            if (monsterPatternRows.Count == 0 || monsterPrefabs == null || monsterPrefabs.Length == 0)
            {
                return;
            }

            var row = monsterPatternRows[Mathf.Clamp(monsterPatternIndex, 0, monsterPatternRows.Count - 1)];
            if (string.IsNullOrWhiteSpace(row.MonsterId) ||
                !monsterPrefabNameById.TryGetValue(row.MonsterId, out var prefabName) ||
                string.IsNullOrWhiteSpace(prefabName))
            {
                return;
            }

            var targetIndex = Array.FindIndex(
                monsterPrefabs,
                prefab => prefab != null && string.Equals(prefab.name, prefabName, StringComparison.OrdinalIgnoreCase));
            if (targetIndex < 0 || targetIndex == monsterIndex)
            {
                return;
            }

            monsterIndex = targetIndex;
            RespawnMonsterOnly();
        }

        private void DrawMonsterVfxTuningPanel(MonsterPatternVfxRow row, MonsterVfxCueRow cue)
        {
            if (!IsMonsterTuningRow(row, cue))
            {
                GUILayout.Label($"Press 'Tune {cue.VfxCueId}' to adjust this shared cue. Saving by vfxCueId affects every pattern listed for that cue.");
                return;
            }

            GUILayout.Label($"Tuning {cue.VfxCueId} for pattern {row.PatternId}");
            tunedScale = DrawAdjustRow("Scale", tunedScale, tuningScaleStep);
            tunedScaleWithRadius = GUILayout.Toggle(tunedScaleWithRadius, " scaleWithRadius");
            tunedOffset.x = DrawAdjustRow("Offset X", tunedOffset.x, tuningOffsetStep);
            tunedOffset.y = DrawAdjustRow("Offset Y", tunedOffset.y, tuningOffsetStep);
            tunedOffset.z = DrawAdjustRow("Offset Z", tunedOffset.z, tuningOffsetStep);
            tunedRotation.x = DrawAdjustRow("Rotation X", tunedRotation.x, tuningRotationStep);
            tunedRotation.y = DrawAdjustRow("Rotation Y", tunedRotation.y, tuningRotationStep);
            tunedRotation.z = DrawAdjustRow("Rotation Z", tunedRotation.z, tuningRotationStep);
            tunedLifetime = Mathf.Max(0f, DrawAdjustRow("Lifetime", tunedLifetime, tuningLifetimeStep));
            monsterPreviewVfxDelaySeconds = Mathf.Max(0f, DrawAdjustRow("Anim -> VFX Delay", monsterPreviewVfxDelaySeconds, 0.05f));

            GUILayout.Label($"SpawnAnchor: {tunedSpawnAnchor} (optional monster CSV column; blank is Auto)");
            GUILayout.BeginHorizontal();
            DrawSpawnAnchorButton(EffectVfxSpawnAnchor.Auto);
            DrawSpawnAnchorButton(EffectVfxSpawnAnchor.SourceAttack);
            DrawSpawnAnchorButton(EffectVfxSpawnAnchor.SourceGround);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DrawSpawnAnchorButton(EffectVfxSpawnAnchor.TargetHitCenter);
            DrawSpawnAnchorButton(EffectVfxSpawnAnchor.TargetGround);
            DrawSpawnAnchorButton(EffectVfxSpawnAnchor.FieldCenter);
            GUILayout.EndHorizontal();

            DrawPrefabOverrideControls(cue.PrefabPath);
            if (ResolveMonsterPrefabRepoint(row, cue) != null &&
                monsterPatternRows.Count(item => string.Equals(item.VfxCueId, cue.VfxCueId, StringComparison.Ordinal)) > 1)
            {
                GUILayout.Label($"WARNING: {cue.VfxCueId} is shared — saving the repoint changes every pattern using this cue.");
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset Tuning")) BeginMonsterTuning(row, cue);
            if (GUILayout.Button("Play Current Settings")) PlayMonsterCurrentSettings(row, cue);
            if (GUILayout.Button("Play Anim + Current")) PlayMonsterAnimationAndVfx(row, cue, useCurrentSettings: true);
            if (GUILayout.Button("Copy CSV Values")) CopyMonsterTuningCsvValues(row, cue);
            if (GUILayout.Button("Log CSV Values")) LogMonsterTuningCsvValues(row, cue);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save To CSV")) SaveMonsterTuningToCsv(row, cue, applyCatalog: false);
            if (GUILayout.Button("Save CSV + Apply Catalog")) SaveMonsterTuningToCsv(row, cue, applyCatalog: true);
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Apply To Catalog (surgical)"))
            {
                ApplyMonsterTuningToCatalog(row, cue, GetEffectiveMonsterTuning(row, cue), GetEffectiveMonsterPrefabPath(row, cue));
            }
        }

        private void BeginMonsterTuning(MonsterPatternVfxRow row, MonsterVfxCueRow cue)
        {
            monsterTuningCueId = cue.VfxCueId;
            monsterTuningPatternId = row.PatternId;
            monsterTuningOrder = row.BindingOrder;
            tunedPrefabPath = string.Empty;
            var baseTuning = MonsterVfxTuning.FromCue(cue, row);
            tunedScale = baseTuning.ScaleMultiplier;
            tunedScaleWithRadius = baseTuning.ScaleWithRadius;
            tunedOffset = baseTuning.Offset;
            tunedRotation = baseTuning.RotationEuler;
            tunedLifetime = baseTuning.LifetimeOverride;
            tunedSpawnAnchor = baseTuning.SpawnAnchor;
            monsterPreviewVfxDelaySeconds = baseTuning.DelaySeconds;
        }

        private void CopyMonsterTuningCsvValues(MonsterPatternVfxRow row, MonsterVfxCueRow cue)
        {
            var tuning = GetEffectiveMonsterTuning(row, cue);
            var cells = BuildCsvTuningCells(tuning);
            GUIUtility.systemCopyBuffer = cells;
            Debug.Log($"[Monster VFX Tuning] Copied CSV cells (scaleMultiplier..lifetimeOverride) for {cue.VfxCueId}: {cells} | spawnAnchor={tuning.SpawnAnchor} delaySeconds={FormatFloat(tuning.DelaySeconds)}");
        }

        private void LogMonsterTuningCsvValues(MonsterPatternVfxRow row, MonsterVfxCueRow cue)
        {
            var tuning = GetEffectiveMonsterTuning(row, cue);
            Debug.Log(
                $"[Monster VFX Tuning] patternId={row.PatternId} cueId={cue.VfxCueId}\n" +
                $"  scaleMultiplier={FormatFloat(tuning.ScaleMultiplier)} scaleWithRadius={(tuning.ScaleWithRadius ? "true" : "false")}\n" +
                $"  offset={FormatVector(tuning.Offset)} rotation={FormatVector(tuning.RotationEuler)}\n" +
                $"  lifetimeOverride={FormatFloat(tuning.LifetimeOverride)} spawnAnchor={tuning.SpawnAnchor} delaySeconds={FormatFloat(tuning.DelaySeconds)}");
        }

        private void SaveMonsterTuningToCsv(MonsterPatternVfxRow row, MonsterVfxCueRow cue, bool applyCatalog)
        {
            if (!monsterPatternRows.Any(item => string.Equals(item.PatternId, row.PatternId, StringComparison.Ordinal)))
            {
                Debug.LogError($"[Monster VFX Tuning] Refusing save: patternId '{row.PatternId}' no longer exists.");
                return;
            }

            if (!TryGetMonsterCueById(cue.VfxCueId, out _))
            {
                Debug.LogError($"[Monster VFX Tuning] Refusing save: vfxCueId '{cue.VfxCueId}' no longer exists.");
                return;
            }

            var prefabRepoint = ResolveMonsterPrefabRepoint(row, cue);
            if (prefabRepoint != null && AssetDatabase.LoadAssetAtPath<GameObject>(prefabRepoint) == null)
            {
                Debug.LogError($"[Monster VFX Tuning] Refusing save: prefab override does not resolve: {prefabRepoint}");
                return;
            }

            var tuning = GetEffectiveMonsterTuning(row, cue);
            var update = new CombatVfxCueTuningUpdate(
                tuning.ScaleMultiplier,
                tuning.ScaleWithRadius,
                tuning.Offset.x,
                tuning.Offset.y,
                tuning.Offset.z,
                tuning.RotationEuler.x,
                tuning.RotationEuler.y,
                tuning.RotationEuler.z,
                tuning.LifetimeOverride,
                prefabPath: prefabRepoint);

            Debug.Log(
                $"[Monster VFX Tuning] Saving patternId={row.PatternId} cueId={cue.VfxCueId} to {MonsterVfxCuePath} (backup: {MonsterVfxCuePath}.bak)\n" +
                $"  scaleMultiplier={FormatFloat(update.ScaleMultiplier)}, scaleWithRadius={update.ScaleWithRadius}\n" +
                $"  offset={FormatVector(update.OffsetX, update.OffsetY, update.OffsetZ)}, rotation={FormatVector(update.RotationX, update.RotationY, update.RotationZ)}\n" +
                $"  lifetimeOverride={FormatFloat(update.LifetimeOverride)}, bindingSpawnAnchor={tuning.SpawnAnchor}, delaySeconds={FormatFloat(tuning.DelaySeconds)}" +
                (prefabRepoint != null ? $"\n  prefabPath={cue.PrefabPath} -> {prefabRepoint}" : string.Empty));

            try
            {
                CombatVfxCueTuningCsvWriter.UpdateFile(MonsterVfxCuePath, cue.VfxCueId, update, createBackup: true);
                MonsterPatternVfxBindingCsvWriter.UpdateSpawnAnchor(
                    MonsterPatternVfxBindingPath,
                    row.PatternId,
                    cue.VfxCueId,
                    row.BindingOrder,
                    tuning.SpawnAnchor.ToString(),
                    createBackup: true,
                    delaySeconds: tuning.DelaySeconds);
                var reparsed = MonsterCatalogCsvConverter.ConvertDirectories(CombatCsvPaths.MonsterDirectory, CombatCsvPaths.PresentationDirectory)
                    .VfxCues.FirstOrDefault(item => string.Equals(item.VfxCueId, cue.VfxCueId, StringComparison.Ordinal));
                if (reparsed == null)
                {
                    throw new InvalidOperationException($"Saved CSV no longer contains vfxCueId '{cue.VfxCueId}'.");
                }

                ValidateSavedMonsterTuning(cue.VfxCueId, update, reparsed);
                AssetDatabase.Refresh();
                RefreshMonsterVfxAuthoringData();
                tunedPrefabPath = string.Empty;
                Debug.Log($"[Monster VFX Tuning] Saved {cue.VfxCueId} to CSV and verified parser round-trip.");

                if (applyCatalog && TryGetMonsterCueById(cue.VfxCueId, out var refreshedCue))
                {
                    ApplyMonsterTuningToCatalog(row, refreshedCue, GetEffectiveMonsterTuning(row, refreshedCue), refreshedCue.PrefabPath);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Monster VFX Tuning] Save To CSV failed for {cue.VfxCueId}: {ex.Message}");
            }
        }

        private static void ValidateSavedMonsterTuning(string cueId, CombatVfxCueTuningUpdate expected, CombatVfxCueDefinition actual)
        {
            const float tolerance = 0.0001f;
            if (Mathf.Abs(actual.ScaleMultiplier - expected.ScaleMultiplier) > tolerance ||
                actual.ScaleWithRadius != expected.ScaleWithRadius ||
                Mathf.Abs(actual.OffsetX - expected.OffsetX) > tolerance ||
                Mathf.Abs(actual.OffsetY - expected.OffsetY) > tolerance ||
                Mathf.Abs(actual.OffsetZ - expected.OffsetZ) > tolerance ||
                Mathf.Abs(actual.RotationX - expected.RotationX) > tolerance ||
                Mathf.Abs(actual.RotationY - expected.RotationY) > tolerance ||
                Mathf.Abs(actual.RotationZ - expected.RotationZ) > tolerance ||
                Mathf.Abs(actual.LifetimeOverride - expected.LifetimeOverride) > tolerance ||
                (!string.IsNullOrEmpty(expected.PrefabPath) &&
                 !string.Equals(actual.PrefabPath, expected.PrefabPath, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"Parser round-trip mismatch after saving vfxCueId '{cueId}'.");
            }
        }

        private void ApplyMonsterTuningToCatalog(MonsterPatternVfxRow row, MonsterVfxCueRow cue, MonsterVfxTuning tuning, string prefabPath = null)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<EffectVfxCatalog>(DefaultCatalogPath);
            if (catalog == null)
            {
                Debug.LogError($"[Monster VFX Tuning] Missing catalog asset: {DefaultCatalogPath}");
                return;
            }

            var serialized = new SerializedObject(catalog);
            var entries = serialized.FindProperty("entries");
            serialized.Update();
            var appliedCount = 0;
            for (var i = 0; i < entries.arraySize; i++)
            {
                var entry = entries.GetArrayElementAtIndex(i);
                if (!string.Equals(entry.FindPropertyRelative("cueId")?.stringValue ?? string.Empty, cue.VfxCueId, StringComparison.Ordinal))
                {
                    continue;
                }

                entry.FindPropertyRelative("scaleMultiplier").floatValue = tuning.ScaleMultiplier;
                entry.FindPropertyRelative("scaleWithRadius").boolValue = tuning.ScaleWithRadius;
                entry.FindPropertyRelative("positionOffset").vector3Value = tuning.Offset;
                entry.FindPropertyRelative("rotationEulerOffset").vector3Value = tuning.RotationEuler;
                entry.FindPropertyRelative("lifetimeOverride").floatValue = tuning.LifetimeOverride;
                entry.FindPropertyRelative("playbackDelaySeconds").floatValue = tuning.DelaySeconds;
                SyncCatalogEntryPrefab(entry, prefabPath, "[Monster VFX Tuning]");
                var sourceRef = entry.FindPropertyRelative("sourceRef")?.stringValue ?? string.Empty;
                var displayName = entry.FindPropertyRelative("displayName")?.stringValue ?? string.Empty;
                if (string.Equals(sourceRef, $"monster.pattern.{row.PatternId}", StringComparison.Ordinal) &&
                    (row.BindingOrder <= 0 || displayName.EndsWith($"#{row.BindingOrder}", StringComparison.Ordinal)))
                {
                    SetEnum(entry.FindPropertyRelative("spawnAnchor"), tuning.SpawnAnchor.ToString());
                }
                appliedCount++;
            }

            if (appliedCount == 0)
            {
                Debug.LogError($"[Monster VFX Tuning] Catalog does not contain vfxCueId '{cue.VfxCueId}'. Use explicit Rebuild VFX Catalog From Combat CSV first.");
                return;
            }

            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            vfxCatalog = catalog;
            ApplyCatalog();
            Debug.Log($"[Monster VFX Tuning] Applied {cue.VfxCueId} to {appliedCount} DefaultEffectVfxCatalog.asset entr{(appliedCount == 1 ? "y" : "ies")} (surgical entry update; no full rebuild).");
        }

        private void PlayMonsterCsvVfx(MonsterPatternVfxRow row, MonsterVfxCueRow cue)
        {
            var prefabPath = GetEffectiveMonsterPrefabPath(row, cue);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[Monster VFX Tuning] prefabPath does not resolve: {cue.VfxCueId} {prefabPath}");
                PlayMonsterCatalogVfx(row, cue);
                return;
            }

            var tuning = GetEffectiveMonsterTuning(row, cue);
            var resultEvent = CreateMonsterVfxResultEvent(row, cue);
            var context = ResolveMonsterTunedSpawnContext(tuning.SpawnAnchor, resultEvent);
            var rotation = context.Facing * Quaternion.Euler(tuning.RotationEuler);
            var position = context.BasePosition + context.Facing * tuning.Offset;
            var scale = ResolveTunedScale(tuning, resultEvent);
            var lifetime = tuning.LifetimeOverride > 0f ? tuning.LifetimeOverride : directVfxLifetime;
            var instance = SpawnTunedCsvVfx(prefab, position, rotation, scale, lifetime, context.AnchorKind);
            // followSourceAnchor cues track the monster's source anchor here too, so the tuning buttons
            // (Play CSV / Play Anim + CSV) preview the same head-follow the production path plays.
            if (instance != null && cue.FollowSourceAnchor && monsterVisual != null &&
                monsterVisual.TryGetVfxAnchor(EffectVfxAnchorPolicy.ResolveSourceAnchor(resultEvent), out var followAnchor) &&
                followAnchor != null)
            {
                instance.AddComponent<EffectPresentationController.AnchorDeltaFollower>().Configure(followAnchor);
            }

            RecordMonsterCsvDiagnostics(row, cue, tuning, resultEvent, prefab, position, scale, lifetime, context.AnchorKind);
        }

        private void PlayMonsterCurrentSettings(MonsterPatternVfxRow row, MonsterVfxCueRow cue)
        {
            if (!IsMonsterTuningRow(row, cue))
            {
                BeginMonsterTuning(row, cue);
            }

            PlayMonsterCsvVfx(row, cue);
            Debug.Log(
                $"[Monster VFX Tuning] Play Current Settings preview only (CSV not saved): " +
                $"patternId={row.PatternId} cueId={cue.VfxCueId} sourceRef=monster.pattern.{row.PatternId} " +
                $"spawnAnchor={GetEffectiveMonsterTuning(row, cue).SpawnAnchor}");
        }

        private void PlayMonsterCatalogVfx(MonsterPatternVfxRow row, MonsterVfxCueRow cue)
        {
            PlayActorVfx(monsterVisual, playerVisual, CreateMonsterVfxResultEvent(row, cue));
            AppendMonsterCatalogComparisonToSummary(row, cue);
        }

        private EffectResultEvent CreateMonsterVfxResultEvent(MonsterPatternVfxRow row, MonsterVfxCueRow cue)
        {
            var kind = TryParseEffectKind(cue.EffectKind, out var parsed) ? parsed : EffectKind.Damage;
            var targetUnitId = ResolveCardVfxTargetUnitId(cue.TargetFilter);
            return new EffectResultEvent(
                kind,
                targetUnitId: targetUnitId,
                appliedAmount: 1,
                radius: row.AreaRadius,
                sourceUnitId: "monster",
                sourceActorKind: "monster",
                targetActorKind: targetUnitId,
                sourceRef: $"monster.pattern.{row.PatternId}");
        }

        private TunedSpawnContext ResolveMonsterTunedSpawnContext(EffectVfxSpawnAnchor spawnAnchor, EffectResultEvent resultEvent)
        {
            var targetVisual = string.Equals(resultEvent.TargetUnitId, "monster", StringComparison.OrdinalIgnoreCase) ? monsterVisual : playerVisual;
            CharacterVfxAnchorKind anchorKind;
            Vector3 basePosition;
            var sourceAnchor = CharacterVfxAnchorKind.AttackSource;
            switch (spawnAnchor)
            {
                case EffectVfxSpawnAnchor.SourceAttack:
                    anchorKind = CharacterVfxAnchorKind.AttackSource;
                    sourceAnchor = CharacterVfxAnchorKind.AttackSource;
                    basePosition = ResolveAnchorPosition(monsterVisual, anchorKind);
                    break;
                case EffectVfxSpawnAnchor.SourceGround:
                    anchorKind = CharacterVfxAnchorKind.Ground;
                    sourceAnchor = CharacterVfxAnchorKind.Ground;
                    basePosition = ResolveAnchorPosition(monsterVisual, anchorKind);
                    break;
                case EffectVfxSpawnAnchor.TargetHitCenter:
                    anchorKind = CharacterVfxAnchorKind.HitCenter;
                    basePosition = ResolveAnchorPosition(targetVisual, anchorKind);
                    break;
                case EffectVfxSpawnAnchor.TargetGround:
                    anchorKind = CharacterVfxAnchorKind.Ground;
                    basePosition = ResolveAnchorPosition(targetVisual, anchorKind);
                    break;
                case EffectVfxSpawnAnchor.FieldCenter:
                    return new TunedSpawnContext(fieldCenter != null ? fieldCenter.position : transform.position, Quaternion.identity, CharacterVfxAnchorKind.Root);
                default:
                    anchorKind = EffectVfxAnchorPolicy.ResolveTargetAnchor(resultEvent);
                    sourceAnchor = EffectVfxAnchorPolicy.ResolveSourceAnchor(resultEvent);
                    basePosition = ResolveAnchorPosition(targetVisual, anchorKind);
                    break;
            }

            // Match the runtime bridge (PlayerStateEffectPresentationBridge): source-anchored VFX face the
            // actual target so authored offsets/rotations live in attack-direction space, not world space.
            var facingTarget = basePosition;
            if (spawnAnchor == EffectVfxSpawnAnchor.SourceAttack || spawnAnchor == EffectVfxSpawnAnchor.SourceGround)
            {
                facingTarget = ResolveAnchorPosition(targetVisual, CharacterVfxAnchorKind.HitCenter);
            }

            return new TunedSpawnContext(basePosition, ResolveFacingFromSource(monsterVisual, sourceAnchor, facingTarget), anchorKind);
        }

        private void RecordMonsterCsvDiagnostics(
            MonsterPatternVfxRow row,
            MonsterVfxCueRow cue,
            MonsterVfxTuning tuning,
            EffectResultEvent resultEvent,
            GameObject prefab,
            Vector3 position,
            float scale,
            float lifetime,
            CharacterVfxAnchorKind anchorKind)
        {
            var builder = new StringBuilder()
                .Append("Monster Play CSV (direct prefab)")
                .Append(IsMonsterTuningRow(row, cue) ? "  [LIVE TUNING]" : string.Empty)
                .Append('\n')
                .Append("patternId=").Append(row.PatternId).Append(" cueId=").Append(cue.VfxCueId)
                .Append(" monster=").Append(row.MonsterId).Append(' ').Append(row.MonsterName)
                .Append('\n')
                .Append("kind=").Append(cue.EffectKind).Append(" target=").Append(cue.TargetFilter)
                .Append(" sourceRef=").Append(resultEvent.SourceRef)
                .Append('\n')
                .Append("CSV prefab=").Append(prefab != null ? prefab.name : "(missing)")
                .Append('\n')
                .Append("spawnAnchor=").Append(tuning.SpawnAnchor).Append(" -> ").Append(anchorKind)
                .Append('\n')
                .Append("scale=").Append(FormatFloat(scale))
                .Append(" offset=").Append(FormatVector(tuning.Offset))
                .Append(" rotation=").Append(FormatVector(tuning.RotationEuler))
                .Append(" lifetime=").Append(FormatFloat(lifetime))
                .Append(" worldPos=").Append(FormatVector(position));

            var catalog = vfxCatalog != null ? vfxCatalog : effectPresentation != null ? effectPresentation.VfxCatalog : null;
            if (catalog != null && catalog.TryResolve(resultEvent, out var entry))
            {
                AppendMonsterCatalogCsvComparison(builder, cue, tuning, entry);
            }
            else
            {
                builder.Append('\n').Append("WARNING: catalog has no entry for this monster pattern sourceRef.");
            }

            lastVfxSummary = builder.ToString();
        }

        private void AppendMonsterCatalogComparisonToSummary(MonsterPatternVfxRow row, MonsterVfxCueRow cue)
        {
            var resultEvent = CreateMonsterVfxResultEvent(row, cue);
            var builder = new StringBuilder(lastVfxSummary).Append('\n').Append("--- Monster Play Catalog vs CSV ---");
            var catalog = vfxCatalog != null ? vfxCatalog : effectPresentation != null ? effectPresentation.VfxCatalog : null;
            var tuning = MonsterVfxTuning.FromCue(cue, row);
            var entry = catalog != null
                ? catalog.ResolveAll(resultEvent).FirstOrDefault(candidate => candidate.CueId == cue.VfxCueId && candidate.SpawnAnchor == tuning.SpawnAnchor)
                : null;
            if (entry != null)
            {
                AppendMonsterCatalogCsvComparison(builder, cue, tuning, entry);
            }
            else
            {
                builder.Append('\n').Append("WARNING: catalog has no entry for this monster pattern sourceRef.");
            }

            lastVfxSummary = builder.ToString();
        }

        private void DrawMonsterCatalogDrift(MonsterVfxCueRow cue, MonsterPatternVfxRow row)
        {
            var catalog = vfxCatalog != null ? vfxCatalog : effectPresentation != null ? effectPresentation.VfxCatalog : null;
            var tuning = MonsterVfxTuning.FromCue(cue, row);
            var entry = catalog != null
                ? catalog.ResolveAll(CreateMonsterVfxResultEvent(row, cue)).FirstOrDefault(candidate => candidate.CueId == cue.VfxCueId && candidate.SpawnAnchor == tuning.SpawnAnchor)
                : null;
            if (entry == null)
            {
                GUILayout.Label("Catalog: missing resolve (drift)");
                return;
            }

            var builder = new StringBuilder($"Catalog scale={FormatFloat(entry.ScaleMultiplier)} radius={(entry.ScaleWithRadius ? "on" : "off")} offset={FormatVector(entry.PositionOffset)}");
            builder.Append($" rot={FormatVector(entry.RotationOffset.eulerAngles)} life={FormatFloat(entry.LifetimeOverride)} anchor={entry.SpawnAnchor}");
            var drift = BuildMonsterCatalogWarnings(cue, tuning, entry);
            builder.Append(drift.Count == 0 ? " [MATCH]" : " [DRIFT]");
            GUILayout.Label(builder.ToString());
        }

        private static void AppendMonsterCatalogCsvComparison(StringBuilder builder, MonsterVfxCueRow cue, MonsterVfxTuning tuning, EffectVfxCatalog.Entry entry)
        {
            var warnings = BuildMonsterCatalogWarnings(cue, tuning, entry);
            if (warnings.Count == 0)
            {
                builder.Append('\n').Append("MATCH: monster CSV values equal the catalog entry.");
                return;
            }

            builder.Append('\n').Append("WARNING: monster CSV vs Catalog differs:");
            for (var i = 0; i < warnings.Count; i++)
            {
                builder.Append("\n  - ").Append(warnings[i]);
            }
        }

        private static List<string> BuildMonsterCatalogWarnings(MonsterVfxCueRow cue, MonsterVfxTuning tuning, EffectVfxCatalog.Entry entry)
        {
            var warnings = new List<string>();
            if (!string.Equals(FirstPrefabName(entry), Path.GetFileNameWithoutExtension(cue.PrefabPath), StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"prefab CSV={Path.GetFileNameWithoutExtension(cue.PrefabPath)} vs catalog={FirstPrefabName(entry)}");
            }

            if (entry.SpawnAnchor != tuning.SpawnAnchor) warnings.Add($"spawnAnchor CSV={tuning.SpawnAnchor} vs catalog={entry.SpawnAnchor}");
            if (!Mathf.Approximately(entry.ScaleMultiplier, tuning.ScaleMultiplier)) warnings.Add($"scaleMultiplier CSV={FormatFloat(tuning.ScaleMultiplier)} vs catalog={FormatFloat(entry.ScaleMultiplier)}");
            if (entry.ScaleWithRadius != tuning.ScaleWithRadius) warnings.Add($"scaleWithRadius CSV={tuning.ScaleWithRadius} vs catalog={entry.ScaleWithRadius}");
            if (!ApproximatelyVector(entry.PositionOffset, tuning.Offset, 0.01f)) warnings.Add($"offset CSV={FormatVector(tuning.Offset)} vs catalog={FormatVector(entry.PositionOffset)}");
            if (!ApproximatelyVector(entry.RotationOffset.eulerAngles, Quaternion.Euler(tuning.RotationEuler).eulerAngles, 0.5f)) warnings.Add($"rotation CSV={FormatVector(tuning.RotationEuler)} vs catalog={FormatVector(entry.RotationOffset.eulerAngles)}");
            if (!Mathf.Approximately(entry.LifetimeOverride, tuning.LifetimeOverride)) warnings.Add($"lifetime CSV={FormatFloat(tuning.LifetimeOverride)} vs catalog={FormatFloat(entry.LifetimeOverride)}");
            if (!Mathf.Approximately(entry.PlaybackDelaySeconds, tuning.DelaySeconds)) warnings.Add($"delaySeconds CSV={FormatFloat(tuning.DelaySeconds)} vs catalog={FormatFloat(entry.PlaybackDelaySeconds)}");
            return warnings;
        }

        private MonsterVfxTuning GetEffectiveMonsterTuning(MonsterPatternVfxRow row, MonsterVfxCueRow cue)
        {
            if (IsMonsterTuningRow(row, cue))
            {
                return new MonsterVfxTuning(tunedScale, tunedScaleWithRadius, tunedOffset, tunedRotation, tunedLifetime, tunedSpawnAnchor, monsterPreviewVfxDelaySeconds);
            }

            return MonsterVfxTuning.FromCue(cue, row);
        }

        private string GetEffectiveMonsterPrefabPath(MonsterPatternVfxRow row, MonsterVfxCueRow cue)
        {
            return IsMonsterTuningRow(row, cue) && !string.IsNullOrWhiteSpace(tunedPrefabPath)
                ? tunedPrefabPath
                : cue.PrefabPath;
        }

        // Null when the effective prefab equals the CSV value, i.e. nothing to repoint on save.
        private string ResolveMonsterPrefabRepoint(MonsterPatternVfxRow row, MonsterVfxCueRow cue)
        {
            var effective = GetEffectiveMonsterPrefabPath(row, cue);
            return string.Equals(effective, cue.PrefabPath, StringComparison.Ordinal) ? null : effective;
        }

        // Shared prefab-repoint row for both tuning panels. The pick comes from the Project VFX Browser
        // section (the lab's only runtime-OnGUI-safe asset picker); the override stays in memory until
        // Save To CSV persists it through the prefabPath column.
        private void DrawPrefabOverrideControls(string csvPrefabPath)
        {
            var overridden = !string.IsNullOrWhiteSpace(tunedPrefabPath) &&
                             !string.Equals(tunedPrefabPath, csvPrefabPath, StringComparison.Ordinal);
            GUILayout.Label(overridden
                ? $"Prefab: {Path.GetFileNameWithoutExtension(csvPrefabPath)} -> {Path.GetFileNameWithoutExtension(tunedPrefabPath)} (unsaved)"
                : $"Prefab: {Path.GetFileNameWithoutExtension(csvPrefabPath)} (CSV)");
            if (overridden && tunedPrefabPath.StartsWith("Assets/ThirdParty/", StringComparison.OrdinalIgnoreCase))
            {
                GUILayout.Label("WARNING: vendor (ThirdParty) path — prefer an owned copy under Assets/Art/VFX or Assets/Prefabs/Vfx.");
            }

            GUILayout.BeginHorizontal();
            var selectedPath = GetSelectedVfxBrowserPath();
            var selectedName = string.IsNullOrWhiteSpace(selectedPath) ? "(none)" : Path.GetFileNameWithoutExtension(selectedPath);
            if (GUILayout.Button($"Use Browser Prefab: {selectedName}"))
            {
                AssignBrowserPrefabOverride(selectedPath);
            }

            if (overridden && GUILayout.Button("Revert Prefab"))
            {
                tunedPrefabPath = string.Empty;
            }
            GUILayout.EndHorizontal();
        }

        private void AssignBrowserPrefabOverride(string selectedPath)
        {
            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                Debug.LogWarning("[VFX Lab] No prefab selected in the Project VFX Browser section.");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(selectedPath) == null)
            {
                Debug.LogWarning($"[VFX Lab] Selected browser path does not resolve to a prefab: {selectedPath}");
                return;
            }

            tunedPrefabPath = selectedPath;
            Debug.Log($"[VFX Lab] Prefab override set to {selectedPath} (preview only until Save To CSV).");
        }

        // Slot 0 is the CSV-owned prefab (the rebuild emits single-prefab entries); extra hand-authored
        // slots, if any, are left alone.
        private static void SyncCatalogEntryPrefab(SerializedProperty entry, string prefabPath, string logPrefix)
        {
            if (string.IsNullOrWhiteSpace(prefabPath))
            {
                return;
            }

            var prefabs = entry.FindPropertyRelative("prefabs");
            if (prefabs == null)
            {
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"{logPrefix} prefabPath does not resolve; catalog prefab left unchanged: {prefabPath}");
                return;
            }

            if (prefabs.arraySize == 0)
            {
                prefabs.arraySize = 1;
            }

            prefabs.GetArrayElementAtIndex(0).objectReferenceValue = prefab;
        }

        private bool IsMonsterTuningRow(MonsterPatternVfxRow row, MonsterVfxCueRow cue)
        {
            return string.Equals(monsterTuningCueId, cue.VfxCueId, StringComparison.Ordinal) &&
                   string.Equals(monsterTuningPatternId, row.PatternId, StringComparison.Ordinal) &&
                   monsterTuningOrder == row.BindingOrder;
        }

        private bool TryGetMonsterCueById(string vfxCueId, out MonsterVfxCueRow found)
        {
            for (var i = 0; i < monsterVfxCueRows.Count; i++)
            {
                if (string.Equals(monsterVfxCueRows[i].VfxCueId, vfxCueId, StringComparison.Ordinal))
                {
                    found = monsterVfxCueRows[i];
                    return true;
                }
            }

            found = default;
            return false;
        }
#endif

        private void DrawCardVfxAssignmentBrowser()
        {
            GUILayout.Space(8f);
            GUILayout.Label("Card VFX Assignment");
#if UNITY_EDITOR
            if (cardAssignmentRows.Count == 0)
            {
                RefreshCardVfxAssignmentRows();
            }

            if (cardAssignmentRows.Count == 0)
            {
                GUILayout.Label("No card rows resolved from cards.csv / combat_card_vfx_cues.csv.");
                if (GUILayout.Button("Reload Card VFX Work Table")) RefreshCardVfxAssignmentRows();
                return;
            }

            cardAssignmentIndex = Mathf.Clamp(cardAssignmentIndex, 0, cardAssignmentRows.Count - 1);
            var row = cardAssignmentRows[cardAssignmentIndex];
            GUILayout.Label($"{cardAssignmentIndex + 1}/{cardAssignmentRows.Count}: {row.CardId} / {row.Name}");
            GUILayout.Label($"Type={row.Type}, Target={row.Target}, Behavior={row.BehaviorId}");
            GUILayout.Label($"Status={row.Status}, Owner={row.DesiredOwner}");
            GUILayout.Label(string.IsNullOrWhiteSpace(row.CurrentPrefabPaths)
                ? "Current VFX: (none / candidate)"
                : $"Current VFX: {row.CurrentPrefabPaths}");
            GUILayout.Label($"Suggested search: {GetSuggestedCardVfxSearch(row)}");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reload CSV")) RefreshCardVfxAssignmentRows();
            if (GUILayout.Button("Reload Catalog + CSV")) ReloadCardVfxAuthoringData();
            if (GUILayout.Button("Prev Card")) StepCardAssignment(-1);
            if (GUILayout.Button("Next Card")) StepCardAssignment(1);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Use Search")) vfxBrowserSearch = GetSuggestedCardVfxSearch(row);
            if (GUILayout.Button("Copy Selected VFX Path")) CopySelectedVfxBrowserPath();
            GUILayout.EndHorizontal();

            var cues = GetCardVfxCues(row.CardId);
            if (cues.Count == 0)
            {
                GUILayout.Label("No existing card cue. Use Project VFX Browser to audition candidates, then record the selected path in the work table.");
            }
            else
            {
                GUILayout.Label("Existing Card Cue Playback");
                for (var i = 0; i < cues.Count; i++)
                {
                    var cue = cues[i];
                    var floatingText = string.IsNullOrWhiteSpace(cue.FloatingTextOverride)
                        ? $"FloatingText={cue.FloatingTextMode}"
                        : $"FloatingText={cue.FloatingTextMode} (\"{cue.FloatingTextOverride}\")";
                    GUILayout.Label($"  {cue.CueId}: {floatingText}");
                    GUILayout.Label(
                        $"  scale={FormatFloat(cue.ScaleMultiplier)} radius={(cue.ScaleWithRadius ? "on" : "off")} " +
                        $"offset={FormatVector(cue.Offset)} rot={FormatVector(cue.RotationEuler)} " +
                        $"life={FormatFloat(cue.LifetimeOverride)} anchor={cue.SpawnAnchor}");
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button($"Play CSV {cue.CueId} / {cue.EffectKind} / {cue.TargetFilter} / {cue.SpawnAnchor}"))
                    {
                        PlayCardVfxCue(cue);
                    }
                    if (GUILayout.Button("Play Anim + CSV"))
                    {
                        PlayCardAnimationAndVfx(row, cue, useCurrentSettings: false);
                    }
                    if (GUILayout.Button("Play Catalog"))
                    {
                        PlayCatalogCardVfxCue(cue);
                    }
                    GUILayout.EndHorizontal();
                    var tuneLabel = IsTuningCue(cue) ? $"Tuning {cue.CueId} (active)" : $"Tune {cue.CueId}";
                    if (GUILayout.Button(tuneLabel))
                    {
                        BeginTuning(cue);
                    }
                }

                GUILayout.Label($"Anim+VFX preview: animation={DisplayPreviewAnimation(ResolveCardAnimationCommand(row))} delay={FormatFloat(cardPreviewVfxDelaySeconds)}s");
                cardPreviewVfxDelaySeconds = Mathf.Max(0f, DrawAdjustRow("Card Anim -> VFX Delay", cardPreviewVfxDelaySeconds, 0.05f));
                if (GUILayout.Button("Play Card Anim + All CSV VFX"))
                {
                    PlayCardAnimationAndVfx(row, cues);
                }

                DrawCardVfxTuningPanel(row, cues);
            }

            if (GUILayout.Button("Mark Selected Path In Console"))
            {
                Debug.Log($"[Card VFX Assignment] {row.CardId} {row.Name} selectedPath={GetSelectedVfxBrowserPath()} status={row.Status}");
            }
#else
            GUILayout.Label("Card assignment browser requires the Unity Editor.");
#endif
        }

#if UNITY_EDITOR
        private void RefreshCardVfxAssignmentRows()
        {
            cardAssignmentRows.Clear();
            cardVfxCueRows.Clear();

            // Cue rows come straight from the authoritative card VFX cue CSV.
            foreach (var row in ReadCsvRows(CardVfxCuePath))
            {
                cardVfxCueRows.Add(new CardVfxCueRow(
                    GetCsv(row, "cueId"),
                    GetCsv(row, "cardId"),
                    GetCsv(row, "effectKind"),
                    GetCsv(row, "targetFilter"),
                    string.IsNullOrWhiteSpace(GetCsv(row, "sourceRef")) ? GetCsv(row, "cardId") : GetCsv(row, "sourceRef"),
                    GetCsv(row, "spawnAnchor"),
                    GetCsv(row, "prefabPath"),
                    GetCsv(row, "floatingTextMode"),
                    GetCsv(row, "floatingTextOverride"),
                    ParseFloat(GetCsv(row, "scaleMultiplier"), 1f),
                    ParseBool(GetCsv(row, "scaleWithRadius")),
                    new Vector3(
                        ParseFloat(GetCsv(row, "offsetX")),
                        ParseFloat(GetCsv(row, "offsetY")),
                        ParseFloat(GetCsv(row, "offsetZ"))),
                   new Vector3(
                       ParseFloat(GetCsv(row, "rotationX")),
                       ParseFloat(GetCsv(row, "rotationY")),
                       ParseFloat(GetCsv(row, "rotationZ"))),
                   ParseFloat(GetCsv(row, "lifetimeOverride")),
                   ParseFloat(GetCsv(row, "delaySeconds"))));
            }

            // Assignment rows are DERIVED live from the authoritative card catalog (cards.csv) joined with the
            // cue rows above, so the browser can never drift from the real cards/cues. The old hand-maintained
            // card_vfx_assignment_working.csv is no longer read.
            foreach (var row in ReadCsvRows(CardCatalogCsvPath))
            {
                var cardId = GetCsv(row, "id");
                if (string.IsNullOrWhiteSpace(cardId))
                {
                    continue;
                }

                var cues = GetCardVfxCues(cardId);
                var prefabPaths = string.Join(" | ", cues
                    .Select(cue => cue.PrefabPath)
                    .Where(path => !string.IsNullOrWhiteSpace(path)));
                cardAssignmentRows.Add(new CardVfxAssignmentRow(
                    cardId,
                    GetCsv(row, "name"),
                    GetCsv(row, "type"),
                    GetCsv(row, "target"),
                    GetCsv(row, "behaviorId"),
                    cues.Count > 0 ? "Assigned" : "None",
                    prefabPaths,
                    "Card"));
            }

            cardAssignmentIndex = Mathf.Clamp(cardAssignmentIndex, 0, Mathf.Max(0, cardAssignmentRows.Count - 1));
        }

        private void ReloadCardVfxAuthoringData()
        {
            AssetDatabase.Refresh();
            vfxCatalog = AssetDatabase.LoadAssetAtPath<EffectVfxCatalog>(DefaultCatalogPath);
            ApplyCatalog();
            RefreshCardVfxAssignmentRows();
        }

        private void StepCardAssignment(int delta)
        {
            if (cardAssignmentRows.Count == 0)
            {
                cardAssignmentIndex = 0;
                return;
            }

            cardAssignmentIndex = (cardAssignmentIndex + delta + cardAssignmentRows.Count) % cardAssignmentRows.Count;
        }

        private List<CardVfxCueRow> GetCardVfxCues(string cardId)
        {
            var result = new List<CardVfxCueRow>();
            for (var i = 0; i < cardVfxCueRows.Count; i++)
            {
                if (string.Equals(cardVfxCueRows[i].CardId, cardId, StringComparison.Ordinal))
                {
                    result.Add(cardVfxCueRows[i]);
                }
            }

            return result;
        }

        private void DrawCardVfxTuningPanel(CardVfxAssignmentRow row, List<CardVfxCueRow> cues)
        {
            GUILayout.Space(6f);
            GUILayout.Label("Card VFX Tuning (explicit save only)");
            if (string.IsNullOrEmpty(tuningCueId))
            {
                GUILayout.Label("Press 'Tune <cueId>' above to start adjusting a cue.");
                return;
            }

            if (!TryGetCueInList(cues, tuningCueId, out var cue))
            {
                GUILayout.Label($"Active tuning cue '{tuningCueId}' belongs to another card. Reselect a cue here to tune it.");
                return;
            }

            GUILayout.Label($"Tuning {cue.CueId} ({cue.CardId})");
            GUILayout.Label($"Scale  {FormatFloat(tunedScale)}   (mouse wheel over the view while Grab is on)");
            var nextRadius = GUILayout.Toggle(tunedScaleWithRadius, " scaleWithRadius (field/area)");
            if (nextRadius != tunedScaleWithRadius)
            {
                tunedScaleWithRadius = nextRadius;
            }

            GUILayout.Label($"Offset  X {FormatFloat(tunedOffset.x)}   Y {FormatFloat(tunedOffset.y)}   Z {FormatFloat(tunedOffset.z)}");
            var grabWanted = GUILayout.Toggle(
                IsGrabCue(cue),
                IsGrabCue(cue)
                    ? "■ GRAB ON — left-drag = move, wheel = size  (click to finish)"
                    : "Grab in view (drag = move, wheel = size)",
                GUI.skin.button);
            if (grabWanted != IsGrabCue(cue))
            {
                ToggleGrabMode(cue);
            }

            tunedRotation.x = DrawAdjustRow("Rotation X", tunedRotation.x, tuningRotationStep);
            tunedRotation.y = DrawAdjustRow("Rotation Y", tunedRotation.y, tuningRotationStep);
            tunedRotation.z = DrawAdjustRow("Rotation Z", tunedRotation.z, tuningRotationStep);

            tunedLifetime = Mathf.Max(0f, DrawAdjustRow("Lifetime", tunedLifetime, tuningLifetimeStep));
            cardPreviewVfxDelaySeconds = Mathf.Max(0f, DrawAdjustRow("Anim -> VFX Delay", cardPreviewVfxDelaySeconds, 0.05f));

            GUILayout.Label($"SpawnAnchor: {tunedSpawnAnchor}");
            GUILayout.BeginHorizontal();
            DrawSpawnAnchorButton(EffectVfxSpawnAnchor.Auto);
            DrawSpawnAnchorButton(EffectVfxSpawnAnchor.SourceAttack);
            DrawSpawnAnchorButton(EffectVfxSpawnAnchor.SourceGround);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DrawSpawnAnchorButton(EffectVfxSpawnAnchor.TargetHitCenter);
            DrawSpawnAnchorButton(EffectVfxSpawnAnchor.TargetGround);
            DrawSpawnAnchorButton(EffectVfxSpawnAnchor.FieldCenter);
            GUILayout.EndHorizontal();

            DrawPrefabOverrideControls(cue.PrefabPath);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset Tuning"))
            {
                BeginTuning(cue);
            }

            if (GUILayout.Button("Play CSV (Tuned)"))
            {
                PlayCardVfxCue(cue);
            }

            if (GUILayout.Button("Play Anim + Current VFX"))
            {
                PlayCardAnimationAndVfx(row, cue, useCurrentSettings: true);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy CSV Values"))
            {
                CopyTuningCsvValues(cue);
            }

            if (GUILayout.Button("Log CSV Values"))
            {
                LogTuningCsvValues(cue);
            }
            GUILayout.EndHorizontal();

            // Save always applies to the runtime catalog too: the game reads DefaultEffectVfxCatalog, not the
            // CSV, so a CSV-only save would look correct in the lab (CSV-driven preview) but stay unchanged
            // in-game. One button keeps the two in lockstep.
            if (GUILayout.Button("Save To CSV (+ Apply Catalog · in-game live)"))
            {
                SaveTuningToCsv(cue, applyCatalog: true);
            }

            if (GUILayout.Button("Apply To Catalog (surgical)"))
            {
                ApplyTuningToCatalog(cue, GetEffectiveTuning(cue), GetEffectiveCardPrefabPath(cue));
            }
        }

        private float DrawAdjustRow(string label, float value, float step)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label} {FormatFloat(value)}", GUILayout.Width(150f));
            if (GUILayout.Button("-"))
            {
                value -= step;
            }

            if (GUILayout.Button("+"))
            {
                value += step;
            }
            GUILayout.EndHorizontal();
            return value;
        }

        private void DrawSpawnAnchorButton(EffectVfxSpawnAnchor anchor)
        {
            var selected = tunedSpawnAnchor == anchor;
            if (GUILayout.Toggle(selected, anchor.ToString(), GUI.skin.button) != selected)
            {
                tunedSpawnAnchor = anchor;
            }
        }

        private void BeginTuning(CardVfxCueRow cue)
        {
            if (grabModeActive && !string.Equals(grabCueId, cue.CueId, StringComparison.Ordinal))
            {
                StopGrabMode();
            }

            tuningCueId = cue.CueId;
            tunedPrefabPath = string.Empty;
            var baseTuning = CardVfxTuning.FromCue(cue);
            tunedScale = baseTuning.ScaleMultiplier;
            tunedScaleWithRadius = baseTuning.ScaleWithRadius;
            tunedOffset = baseTuning.Offset;
            tunedRotation = baseTuning.RotationEuler;
            tunedLifetime = baseTuning.LifetimeOverride;
            tunedSpawnAnchor = baseTuning.SpawnAnchor;
            cardPreviewVfxDelaySeconds = baseTuning.DelaySeconds;
        }

        private void CopyTuningCsvValues(CardVfxCueRow cue)
        {
            var tuning = GetEffectiveTuning(cue);
            var cells = BuildCsvTuningCells(tuning);
            GUIUtility.systemCopyBuffer = cells;
            Debug.Log(
                $"[Card VFX Tuning] Copied CSV cells (scaleMultiplier..lifetimeOverride) for {cue.CueId}: {cells}" +
                $"  | spawnAnchor=\"{tuning.SpawnAnchor}\" delaySeconds={FormatFloat(tuning.DelaySeconds)} (paste separately)");
        }

        private void LogTuningCsvValues(CardVfxCueRow cue)
        {
            var tuning = GetEffectiveTuning(cue);
            Debug.Log(
                $"[Card VFX Tuning] {cue.CueId} ({cue.CardId})\n" +
                $"  scaleMultiplier={FormatFloat(tuning.ScaleMultiplier)}\n" +
                $"  scaleWithRadius={(tuning.ScaleWithRadius ? "true" : "false")}\n" +
                $"  offsetX={FormatFloat(tuning.Offset.x)} offsetY={FormatFloat(tuning.Offset.y)} offsetZ={FormatFloat(tuning.Offset.z)}\n" +
                $"  rotationX={FormatFloat(tuning.RotationEuler.x)} rotationY={FormatFloat(tuning.RotationEuler.y)} rotationZ={FormatFloat(tuning.RotationEuler.z)}\n" +
                $"  lifetimeOverride={FormatFloat(tuning.LifetimeOverride)}\n" +
                $"  spawnAnchor={tuning.SpawnAnchor} delaySeconds={FormatFloat(tuning.DelaySeconds)}");
        }


        private void SaveTuningToCsv(CardVfxCueRow cue, bool applyCatalog)
        {
            var prefabRepoint = ResolveCardPrefabRepoint(cue);
            if (prefabRepoint != null && AssetDatabase.LoadAssetAtPath<GameObject>(prefabRepoint) == null)
            {
                Debug.LogError($"[Card VFX Tuning] Refusing save: prefab override does not resolve: {prefabRepoint}");
                return;
            }

            var tuning = GetEffectiveTuning(cue);
            var update = new CombatCardVfxCueTuningUpdate(
                tuning.ScaleMultiplier,
                tuning.ScaleWithRadius,
                tuning.Offset.x,
                tuning.Offset.y,
                tuning.Offset.z,
                tuning.RotationEuler.x,
                tuning.RotationEuler.y,
                tuning.RotationEuler.z,
                tuning.LifetimeOverride,
                tuning.SpawnAnchor.ToString(),
                tuning.DelaySeconds,
                prefabPath: prefabRepoint);

            Debug.Log(
                $"[Card VFX Tuning] Saving {cue.CueId} to {CardVfxCuePath} (backup: {CardVfxCuePath}.bak)\n" +
                $"  scaleMultiplier={FormatFloat(update.ScaleMultiplier)}, scaleWithRadius={update.ScaleWithRadius}\n" +
                $"  offset={FormatVector(update.OffsetX, update.OffsetY, update.OffsetZ)}, rotation={FormatVector(update.RotationX, update.RotationY, update.RotationZ)}\n" +
                $"  lifetimeOverride={FormatFloat(update.LifetimeOverride)}, spawnAnchor={update.SpawnAnchor}, delaySeconds={FormatFloat(update.DelaySeconds)}" +
                (prefabRepoint != null ? $"\n  prefabPath={cue.PrefabPath} -> {prefabRepoint}" : string.Empty));

            try
            {
                CombatCardVfxCueTuningCsvWriter.UpdateFile(CardVfxCuePath, cue.CueId, update, createBackup: true);
                var reparsed = CombatCardVfxCsvConverter.ConvertFile(CardVfxCuePath).FirstOrDefault(item => string.Equals(item.CueId, cue.CueId, StringComparison.Ordinal));
                if (reparsed == null)
                {
                    throw new InvalidOperationException($"Saved CSV no longer contains cueId '{cue.CueId}'.");
                }

                ValidateSavedTuning(cue.CueId, update, reparsed);
                AssetDatabase.Refresh();
                RefreshCardVfxAssignmentRows();
                tunedPrefabPath = string.Empty;
                Debug.Log($"[Card VFX Tuning] Saved {cue.CueId} to CSV and verified parser round-trip.");

                if (applyCatalog)
                {
                    if (TryGetCueById(cue.CueId, out var refreshedCue))
                    {
                        ApplyTuningToCatalog(refreshedCue, CardVfxTuning.FromCue(refreshedCue), refreshedCue.PrefabPath);
                    }
                    else
                    {
                        Debug.LogWarning($"[Card VFX Tuning] CSV saved, but refreshed cue '{cue.CueId}' was not found for catalog apply.");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Card VFX Tuning] Save To CSV failed for {cue.CueId}: {ex.Message}");
            }
        }

        private static void ValidateSavedTuning(string cueId, CombatCardVfxCueTuningUpdate expected, CombatCardVfxCueDefinition actual)
        {
            const float tolerance = 0.0001f;
            if (Mathf.Abs(actual.ScaleMultiplier - expected.ScaleMultiplier) > tolerance ||
                actual.ScaleWithRadius != expected.ScaleWithRadius ||
                Mathf.Abs(actual.OffsetX - expected.OffsetX) > tolerance ||
                Mathf.Abs(actual.OffsetY - expected.OffsetY) > tolerance ||
                Mathf.Abs(actual.OffsetZ - expected.OffsetZ) > tolerance ||
                Mathf.Abs(actual.RotationX - expected.RotationX) > tolerance ||
                Mathf.Abs(actual.RotationY - expected.RotationY) > tolerance ||
                Mathf.Abs(actual.RotationZ - expected.RotationZ) > tolerance ||
                Mathf.Abs(actual.LifetimeOverride - expected.LifetimeOverride) > tolerance ||
                !string.Equals(actual.SpawnAnchor, expected.SpawnAnchor, StringComparison.Ordinal) ||
                Mathf.Abs(actual.DelaySeconds - expected.DelaySeconds) > tolerance ||
                (!string.IsNullOrEmpty(expected.PrefabPath) &&
                 !string.Equals(actual.PrefabPath, expected.PrefabPath, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"Parser round-trip mismatch after saving cueId '{cueId}'.");
            }
        }

        private void ApplyTuningToCatalog(CardVfxCueRow cue, CardVfxTuning tuning, string prefabPath = null)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<EffectVfxCatalog>(DefaultCatalogPath);
            if (catalog == null)
            {
                Debug.LogError($"[Card VFX Tuning] Missing catalog asset: {DefaultCatalogPath}");
                return;
            }

            var serialized = new SerializedObject(catalog);
            var entries = serialized.FindProperty("entries");
            if (entries == null)
            {
                Debug.LogError("[Card VFX Tuning] Catalog serialized entries property was not found.");
                return;
            }

            serialized.Update();
            for (var i = 0; i < entries.arraySize; i++)
            {
                var entry = entries.GetArrayElementAtIndex(i);
                var cueId = entry.FindPropertyRelative("cueId")?.stringValue ?? string.Empty;
                if (!string.Equals(cueId, cue.CueId, StringComparison.Ordinal))
                {
                    continue;
                }

                entry.FindPropertyRelative("scaleMultiplier").floatValue = tuning.ScaleMultiplier;
                entry.FindPropertyRelative("scaleWithRadius").boolValue = tuning.ScaleWithRadius;
                entry.FindPropertyRelative("positionOffset").vector3Value = tuning.Offset;
                entry.FindPropertyRelative("rotationEulerOffset").vector3Value = tuning.RotationEuler;
                entry.FindPropertyRelative("lifetimeOverride").floatValue = tuning.LifetimeOverride;
                entry.FindPropertyRelative("playbackDelaySeconds").floatValue = tuning.DelaySeconds;
                SetEnum(entry.FindPropertyRelative("spawnAnchor"), tuning.SpawnAnchor.ToString());
                SyncCatalogEntryPrefab(entry, prefabPath, "[Card VFX Tuning]");
                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                vfxCatalog = catalog;
                ApplyCatalog();
                Debug.Log(
                    $"[Card VFX Tuning] Applied {cue.CueId} to DefaultEffectVfxCatalog.asset (surgical entry update). " +
                    "Use Tools/Seoul Playup/Combat/Rebuild VFX Catalog From Combat CSV to rebuild from SOT when needed.");
                return;
            }

            Debug.LogError($"[Card VFX Tuning] Catalog does not contain cueId '{cue.CueId}'. Rebuild VFX Catalog From Combat CSV first.");
        }

        private bool TryGetCueById(string cueId, out CardVfxCueRow found)
        {
            for (var i = 0; i < cardVfxCueRows.Count; i++)
            {
                if (string.Equals(cardVfxCueRows[i].CueId, cueId, StringComparison.Ordinal))
                {
                    found = cardVfxCueRows[i];
                    return true;
                }
            }

            found = default;
            return false;
        }

        private static void SetEnum(SerializedProperty property, string enumName)
        {
            if (property == null)
            {
                return;
            }

            for (var i = 0; i < property.enumNames.Length; i++)
            {
                if (string.Equals(property.enumNames[i], enumName, StringComparison.Ordinal))
                {
                    property.enumValueIndex = i;
                    return;
                }
            }
        }

        private static string FormatVector(float x, float y, float z)
        {
            return $"({FormatFloat(x)}, {FormatFloat(y)}, {FormatFloat(z)})";
        }

        private static string BuildCsvTuningCells(CardVfxTuning tuning)
        {
            return string.Join(",", new[]
            {
                Quote(FormatFloat(tuning.ScaleMultiplier)),
                Quote(tuning.ScaleWithRadius ? "true" : "false"),
                Quote(FormatFloat(tuning.Offset.x)),
                Quote(FormatFloat(tuning.Offset.y)),
                Quote(FormatFloat(tuning.Offset.z)),
                Quote(FormatFloat(tuning.RotationEuler.x)),
                Quote(FormatFloat(tuning.RotationEuler.y)),
                Quote(FormatFloat(tuning.RotationEuler.z)),
                Quote(FormatFloat(tuning.LifetimeOverride)),
                Quote(FormatFloat(tuning.DelaySeconds))
            });
        }

        private static string BuildCsvTuningCells(MonsterVfxTuning tuning)
        {
            return string.Join(",", new[]
            {
                Quote(FormatFloat(tuning.ScaleMultiplier)),
                Quote(tuning.ScaleWithRadius ? "true" : "false"),
                Quote(FormatFloat(tuning.Offset.x)),
                Quote(FormatFloat(tuning.Offset.y)),
                Quote(FormatFloat(tuning.Offset.z)),
                Quote(FormatFloat(tuning.RotationEuler.x)),
                Quote(FormatFloat(tuning.RotationEuler.y)),
                Quote(FormatFloat(tuning.RotationEuler.z)),
                Quote(FormatFloat(tuning.LifetimeOverride))
            });
        }

        private static string Quote(string value)
        {
            return "\"" + value + "\"";
        }

        private static bool TryGetCueInList(List<CardVfxCueRow> cues, string cueId, out CardVfxCueRow found)
        {
            for (var i = 0; i < cues.Count; i++)
            {
                if (string.Equals(cues[i].CueId, cueId, StringComparison.Ordinal))
                {
                    found = cues[i];
                    return true;
                }
            }

            found = default;
            return false;
        }

        private void CopySelectedVfxBrowserPath()
        {
            var path = GetSelectedVfxBrowserPath();
            if (!string.IsNullOrWhiteSpace(path))
            {
                GUIUtility.systemCopyBuffer = path;
                Debug.Log($"[VFX Browser] Copied selected prefab path: {path}");
            }
        }

        private string GetSelectedVfxBrowserPath()
        {
            var filtered = GetFilteredVfxBrowserEntries();
            if (filtered.Count == 0)
            {
                return string.Empty;
            }

            var selected = filtered[Mathf.Clamp(vfxBrowserIndex, 0, filtered.Count - 1)];
            return selected.AssetPath;
        }

        private static string GetSuggestedCardVfxSearch(CardVfxAssignmentRow row)
        {
            if (row.CardId.StartsWith("M", StringComparison.Ordinal)) return "dash smoke trail speed";
            if (row.CardId.StartsWith("A", StringComparison.Ordinal)) return "slash hit impact";
            if (row.CardId.StartsWith("D", StringComparison.Ordinal)) return "shield aura barrier";
            if (row.CardId.StartsWith("S", StringComparison.Ordinal)) return "flash spark glow";
            if (row.CardId.StartsWith("F", StringComparison.Ordinal)) return "circle area flash fire heal";
            if (row.CardId.StartsWith("I", StringComparison.Ordinal)) return "scan glow flash";
            if (row.CardId.StartsWith("U", StringComparison.Ordinal)) return "magic poof glow";
            return "magic hit glow";
        }

        private static IReadOnlyList<Dictionary<string, string>> ReadCsvRows(string assetPath)
        {
            var result = new List<Dictionary<string, string>>();
            if (!File.Exists(assetPath))
            {
                return result;
            }

            var lines = File.ReadAllLines(assetPath);
            if (lines.Length <= 1)
            {
                return result;
            }

            var headers = SplitCsvLine(lines[0]);
            for (var i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                var values = SplitCsvLine(lines[i]);
                var row = new Dictionary<string, string>(StringComparer.Ordinal);
                for (var j = 0; j < headers.Count; j++)
                {
                    row[headers[j]] = j < values.Count ? values[j] : string.Empty;
                }

                result.Add(row);
            }

            return result;
        }

        private static List<string> SplitCsvLine(string line)
        {
            var values = new List<string>();
            var builder = new StringBuilder();
            var inQuotes = false;
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        builder.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    values.Add(builder.ToString());
                    builder.Length = 0;
                }
                else
                {
                    builder.Append(c);
                }
            }

            values.Add(builder.ToString());
            return values;
        }

        private static string GetCsv(Dictionary<string, string> row, string key)
        {
            return row.TryGetValue(key, out var value) ? value : string.Empty;
        }
#endif

#if UNITY_EDITOR
        private void PlayMonsterAnimationAndVfx(MonsterPatternVfxRow row, MonsterVfxCueRow cue, bool useCurrentSettings)
        {
            if (useCurrentSettings && !IsMonsterTuningRow(row, cue))
            {
                BeginMonsterTuning(row, cue);
            }

            var animationCommand = string.IsNullOrWhiteSpace(row.AnimationTrigger) ? "Attack" : row.AnimationTrigger;
            PlayPreviewAnimation(monsterVisual, animationCommand);
           var tuning = GetEffectiveMonsterTuning(row, cue);
           PlayAfterDelay(tuning.DelaySeconds, () =>
            {
                if (useCurrentSettings)
                {
                    PlayMonsterCurrentSettings(row, cue);
                }
                else
                {
                    PlayMonsterCsvVfx(row, cue);
                }
            });
            Debug.Log(
                $"[Monster VFX Timing] patternId={row.PatternId} cueId={cue.VfxCueId} " +
               $"animation={DisplayPreviewAnimation(animationCommand)} delay={FormatFloat(tuning.DelaySeconds)}s " +
                $"mode={(useCurrentSettings ? "current-settings" : "csv")}");
        }

        private void PlayCardAnimationAndVfx(CardVfxAssignmentRow row, List<CardVfxCueRow> cues)
        {
            if (cues == null || cues.Count == 0)
            {
                return;
            }

            var animationCommand = ResolveCardAnimationCommand(row);
            PlayPreviewAnimation(playerVisual, animationCommand);
            for (var i = 0; i < cues.Count; i++)
            {
                var cue = cues[i];
               var cueDelaySeconds = GetEffectiveTuning(cue).DelaySeconds;
               PlayAfterDelay(cueDelaySeconds, () => PlayCardVfxCue(cue));
            }

            Debug.Log(
                $"[Card VFX Timing] cardId={row.CardId} cueCount={cues.Count} " +
               $"animation={DisplayPreviewAnimation(animationCommand)} delay=per-cue mode=csv");
        }

        private void PlayCardAnimationAndVfx(CardVfxAssignmentRow row, CardVfxCueRow cue, bool useCurrentSettings)
        {
            if (useCurrentSettings && !IsTuningCue(cue))
            {
                BeginTuning(cue);
            }

            var animationCommand = ResolveCardAnimationCommand(row);
            PlayPreviewAnimation(playerVisual, animationCommand);
           var delaySeconds = GetEffectiveTuning(cue).DelaySeconds;
           PlayAfterDelay(delaySeconds, () => PlayCardVfxCue(cue));
            Debug.Log(
                $"[Card VFX Timing] cardId={row.CardId} cueId={cue.CueId} sourceRef={cue.SourceRef} " +
               $"animation={DisplayPreviewAnimation(animationCommand)} delay={FormatFloat(delaySeconds)}s " +
                $"mode={(useCurrentSettings ? "current-settings" : "csv")}");
        }

        private void PlayAfterDelay(float delaySeconds, Action action)
        {
            if (action == null)
            {
                return;
            }

            if (delaySeconds <= 0f || !Application.isPlaying || !isActiveAndEnabled)
            {
                action();
                return;
            }

            StartCoroutine(PlayAfterDelayRoutine(delaySeconds, action));
        }

        private IEnumerator PlayAfterDelayRoutine(float delaySeconds, Action action)
        {
            yield return new WaitForSeconds(Mathf.Max(0f, delaySeconds));
            action?.Invoke();
        }

        private static string ResolveCardAnimationCommand(CardVfxAssignmentRow row)
        {
            var behaviorId = row.BehaviorId ?? string.Empty;
            if (behaviorId.StartsWith("move.", StringComparison.OrdinalIgnoreCase))
            {
                return "Move";
            }

            if (behaviorId.StartsWith("defend.", StringComparison.OrdinalIgnoreCase) ||
                behaviorId.StartsWith("defense.", StringComparison.OrdinalIgnoreCase))
            {
                return "Shield";
            }

            if (behaviorId.StartsWith("field.", StringComparison.OrdinalIgnoreCase))
            {
                return "Field";
            }

            if (behaviorId.StartsWith("scout.", StringComparison.OrdinalIgnoreCase) ||
                behaviorId.StartsWith("objective.", StringComparison.OrdinalIgnoreCase) ||
                behaviorId.StartsWith("utility.", StringComparison.OrdinalIgnoreCase))
            {
                return "Buff";
            }

            return "Attack";
        }

        private static string DisplayPreviewAnimation(string animationCommand)
        {
            return string.IsNullOrWhiteSpace(animationCommand) ? "(none)" : animationCommand;
        }

        private static void PlayPreviewAnimation(CharacterActorVisual visual, string animationCommand)
        {
            if (visual == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(animationCommand))
            {
                return;
            }

            if (string.Equals(animationCommand, "Move", StringComparison.OrdinalIgnoreCase))
            {
                visual.PulseMove(1f, 0.35f);
                return;
            }

            if (string.Equals(animationCommand, "Shield", StringComparison.OrdinalIgnoreCase))
            {
                visual.TriggerShield();
                return;
            }

            if (string.Equals(animationCommand, "Buff", StringComparison.OrdinalIgnoreCase))
            {
                visual.TriggerBuff();
                return;
            }

            if (string.Equals(animationCommand, "Field", StringComparison.OrdinalIgnoreCase))
            {
                visual.TriggerField();
                return;
            }

            if (string.Equals(animationCommand, "Dance", StringComparison.OrdinalIgnoreCase))
            {
                visual.TriggerDance();
                return;
            }

            if (string.Equals(animationCommand, "Hit", StringComparison.OrdinalIgnoreCase))
            {
                visual.TriggerHit();
                return;
            }

            if (string.Equals(animationCommand, "Knockback", StringComparison.OrdinalIgnoreCase))
            {
                visual.TriggerKnockback();
                return;
            }

            if (string.Equals(animationCommand, "Dead", StringComparison.OrdinalIgnoreCase))
            {
                visual.TriggerDead();
                return;
            }

            if (string.Equals(animationCommand, "Attack", StringComparison.OrdinalIgnoreCase))
            {
                // The player's attack trigger parameter is "AttackTrigger" (not "Attack"), so route
                // through the parameterless TriggerAttack() which resolves the real parameter name with
                // fallbacks. Without this, "Attack" cards set a non-existent "Attack" trigger via the
                // string overload below and play no animation (unlike Shield/Buff/Field which already
                // call their parameterless trigger methods above).
                visual.TriggerAttack();
                return;
            }

            if (TrySetAnimatorTriggerOrBool(visual, animationCommand))
            {
                return;
            }

            visual.TriggerAttack(animationCommand);
        }

        private static bool TrySetAnimatorTriggerOrBool(CharacterActorVisual visual, string parameterName)
        {
            var animator = visual != null ? visual.Animator : null;
            if (animator == null || string.IsNullOrWhiteSpace(parameterName))
            {
                return false;
            }

            var parameters = animator.parameters;
            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                if (!string.Equals(parameter.name, parameterName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (parameter.type == AnimatorControllerParameterType.Trigger)
                {
                    animator.SetTrigger(parameterName);
                    return true;
                }

                if (parameter.type == AnimatorControllerParameterType.Bool)
                {
                    animator.SetBool(parameterName, true);
                    return true;
                }
            }

            return false;
        }
#endif

        private void PlayCardVfxCue(CardVfxCueRow cue)
        {
#if UNITY_EDITOR
            var prefabPath = GetEffectiveCardPrefabPath(cue);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab != null)
            {
                var tuning = GetEffectiveTuning(cue);
                var targetUnitId = ResolveCardVfxTargetUnitId(cue.TargetFilter);
                var resultEvent = CreateCardVfxResultEvent(cue, targetUnitId);
                var context = ResolveTunedSpawnContext(tuning.SpawnAnchor, targetUnitId, resultEvent);
                // Match EffectPresentationController: positionOffset is applied in facing space, while the
                // rotation offset only orients the spawned instance.
                var rotation = context.Facing * Quaternion.Euler(tuning.RotationEuler);
                var position = context.BasePosition + context.Facing * tuning.Offset;
                var scale = ResolveTunedScale(tuning, resultEvent);
                var lifetime = tuning.LifetimeOverride > 0f ? tuning.LifetimeOverride : directVfxLifetime;
                SpawnTunedCsvVfx(prefab, position, rotation, scale, lifetime, context.AnchorKind);
                RecordCsvDirectVfxDiagnostics(cue, tuning, resultEvent, prefab, position, scale, lifetime, context.AnchorKind);
                return;
            }

            Debug.LogWarning($"[Card VFX Assignment] prefabPath does not resolve: {cue.CueId} {prefabPath}");
#endif
            PlayCatalogCardVfxCue(cue);
        }

        private void PlayCatalogCardVfxCue(CardVfxCueRow cue)
        {
            if (!TryParseEffectKind(cue.EffectKind, out var kind))
            {
                kind = EffectKind.Damage;
            }

            var targetUnitId = ResolveCardVfxTargetUnitId(cue.TargetFilter);
            var resultEvent = CreateCardVfxResultEvent(cue, targetUnitId, kind);
            var target = string.Equals(targetUnitId, "player", StringComparison.OrdinalIgnoreCase)
                ? playerVisual
                : string.Equals(targetUnitId, "monster", StringComparison.OrdinalIgnoreCase)
                    ? monsterVisual
                    : null;

            if (target != null)
            {
                PlayActorVfx(playerVisual, target, resultEvent);
                AppendCatalogComparisonToSummary(cue, resultEvent);
                return;
            }

            if (effectPresentation == null)
            {
                return;
            }

            var center = fieldCenter != null ? fieldCenter.position : transform.position;
            RecordVfxDiagnostics(resultEvent, playerVisual, null, CharacterVfxAnchorKind.Root);
            effectPresentation.Play(resultEvent, center, Quaternion.identity);
            SpawnPreviewMarker(center, CharacterVfxAnchorKind.Root);
            AppendCatalogComparisonToSummary(cue, resultEvent);
        }

        private EffectResultEvent CreateCardVfxResultEvent(CardVfxCueRow cue, string targetUnitId, EffectKind? parsedKind = null)
        {
            var kind = parsedKind ?? (TryParseEffectKind(cue.EffectKind, out var parsed) ? parsed : EffectKind.Damage);
            return new EffectResultEvent(
                kind,
                targetUnitId: targetUnitId,
                appliedAmount: 1,
                radius: ResolveCardVfxPreviewRadius(cue, targetUnitId),
                sourceUnitId: "player",
                sourceActorKind: "player",
                targetActorKind: targetUnitId,
                sourceRef: cue.SourceRef);
        }

        // Field/area cues only show scaleWithRadius growth when the event carries a radius. The runtime
        // feeds a real radius from the placed field; the lab substitutes a fixed preview radius so the
        // designer can audition scaleWithRadius without a live board. Non-field cues stay at radius 0.
        private int ResolveCardVfxPreviewRadius(CardVfxCueRow cue, string targetUnitId)
        {
            var isField = string.Equals(cue.SpawnAnchor, "FieldCenter", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(targetUnitId, "field", StringComparison.OrdinalIgnoreCase);
            return isField ? Mathf.Max(0, fieldPreviewRadius) : 0;
        }

        // Returns the live in-memory tuning for the cue currently under the tuning panel; every other cue
        // resolves straight from its CSV values so Play CSV always reflects what is on disk for it.
        private CardVfxTuning GetEffectiveTuning(CardVfxCueRow cue)
        {
            if (IsTuningCue(cue))
            {
                return new CardVfxTuning(
                    tunedScale,
                    tunedScaleWithRadius,
                    tunedOffset,
                    tunedRotation,
                    tunedLifetime,
                    tunedSpawnAnchor,
                    cardPreviewVfxDelaySeconds);
            }

            return CardVfxTuning.FromCue(cue);
        }

        private bool IsTuningCue(CardVfxCueRow cue)
        {
            return !string.IsNullOrEmpty(tuningCueId) && string.Equals(tuningCueId, cue.CueId, StringComparison.Ordinal);
        }

        private string GetEffectiveCardPrefabPath(CardVfxCueRow cue)
        {
            return IsTuningCue(cue) && !string.IsNullOrWhiteSpace(tunedPrefabPath)
                ? tunedPrefabPath
                : cue.PrefabPath;
        }

        // Null when the effective prefab equals the CSV value, i.e. nothing to repoint on save.
        private string ResolveCardPrefabRepoint(CardVfxCueRow cue)
        {
            var effective = GetEffectiveCardPrefabPath(cue);
            return string.Equals(effective, cue.PrefabPath, StringComparison.Ordinal) ? null : effective;
        }

#if UNITY_EDITOR
        // ----- Grab mode: drag the live preview in the view to set Offset, mouse-wheel to set Scale -----
        // Editor-only: depends on editor-only tuning helpers (BeginTuning / TryGetCueById) and AssetDatabase.
        private bool grabModeActive;
        private bool grabDragging;
        private string grabCueId = string.Empty;
        private GameObject grabPreviewInstance;
        private GameObject grabPreviewPrefab;

        private bool IsGrabCue(CardVfxCueRow cue)
        {
            return grabModeActive && !string.IsNullOrEmpty(grabCueId) && string.Equals(grabCueId, cue.CueId, StringComparison.Ordinal);
        }

        private void ToggleGrabMode(CardVfxCueRow cue)
        {
            if (IsGrabCue(cue))
            {
                StopGrabMode();
                return;
            }

            StopGrabMode();
            if (!IsTuningCue(cue))
            {
                BeginTuning(cue);
            }

            grabModeActive = true;
            grabCueId = cue.CueId;
            EnsureGrabPreview(cue);
        }

        private void StopGrabMode()
        {
            grabModeActive = false;
            grabDragging = false;
            grabCueId = string.Empty;
            DestroyGrabPreviewInstance();
        }

        private void DestroyGrabPreviewInstance()
        {
            if (grabPreviewInstance != null)
            {
                DestroyObjectSafely(grabPreviewInstance);
                grabPreviewInstance = null;
            }

            grabPreviewPrefab = null;
        }

        private void EnsureGrabPreview(CardVfxCueRow cue)
        {
            DestroyGrabPreviewInstance();
            GameObject prefab = null;
#if UNITY_EDITOR
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GetEffectiveCardPrefabPath(cue));
#endif
            if (prefab == null)
            {
                StopGrabMode();
                return;
            }

            grabPreviewPrefab = prefab;
            grabPreviewInstance = Instantiate(prefab, transform);
            grabPreviewInstance.name = $"GRAB_PREVIEW ({cue.CueId})";
            PlayPrefabParticles(grabPreviewInstance);
            SyncGrabPreview(cue);
        }

        // Places the grab preview using the exact spawn math of PlayCardVfxCue so the drag result matches the game.
        private void SyncGrabPreview(CardVfxCueRow cue)
        {
            if (grabPreviewInstance == null || grabPreviewPrefab == null)
            {
                return;
            }

            var tuning = GetEffectiveTuning(cue);
            var targetUnitId = ResolveCardVfxTargetUnitId(cue.TargetFilter);
            var resultEvent = CreateCardVfxResultEvent(cue, targetUnitId);
            var context = ResolveTunedSpawnContext(tuning.SpawnAnchor, targetUnitId, resultEvent);
            var worldPos = context.BasePosition + context.Facing * tuning.Offset;
            var rotation = context.Facing * Quaternion.Euler(tuning.RotationEuler);
            var scale = ResolveTunedScale(tuning, resultEvent);
            grabPreviewInstance.transform.SetPositionAndRotation(worldPos, rotation);
            grabPreviewInstance.transform.localScale =
                Vector3.Scale(grabPreviewPrefab.transform.localScale, Vector3.one * Mathf.Max(0.0001f, scale));
        }

        // Left-drag over the view moves the preview (writes Offset); wheel over the view resizes it (writes Scale).
        // Runs before HandleCameraInput and consumes those events so the camera never fights the grab.
        private void HandleGrabInput()
        {
            if (!grabModeActive || labCamera == null)
            {
                return;
            }

            if (!TryGetCueById(grabCueId, out var cue))
            {
                return;
            }

            var e = Event.current;
            if (e == null)
            {
                return;
            }

            var overPanel = panelRect.Contains(e.mousePosition);

            if (e.type == EventType.ScrollWheel && !overPanel)
            {
                tunedScale = Mathf.Clamp(tunedScale * (1f - e.delta.y * 0.05f), 0.01f, 100f);
                SyncGrabPreview(cue);
                e.Use();
                return;
            }

            if (e.type == EventType.MouseDown && e.button == 0 && !overPanel)
            {
                grabDragging = true;
                e.Use();
                return;
            }

            if (e.type == EventType.MouseUp && e.button == 0 && grabDragging)
            {
                grabDragging = false;
                e.Use();
                return;
            }

            if (e.type == EventType.MouseDrag && e.button == 0 && grabDragging && !overPanel)
            {
                var tuning = GetEffectiveTuning(cue);
                var targetUnitId = ResolveCardVfxTargetUnitId(cue.TargetFilter);
                var resultEvent = CreateCardVfxResultEvent(cue, targetUnitId);
                var context = ResolveTunedSpawnContext(tuning.SpawnAnchor, targetUnitId, resultEvent);
                var currentWorld = context.BasePosition + context.Facing * tunedOffset;
                var dist = Mathf.Max(0.01f, Vector3.Distance(labCamera.transform.position, currentWorld));
                var worldPerPixel = 2f * dist * Mathf.Tan(labCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, Screen.height);
                var px = e.delta * panelUiScale;
                var worldDelta = (labCamera.transform.right * px.x - labCamera.transform.up * px.y) * worldPerPixel;
                tunedOffset += Quaternion.Inverse(context.Facing) * worldDelta;
                SyncGrabPreview(cue);
                e.Use();
            }
        }

        private void Update()
        {
            if (!grabModeActive || grabPreviewInstance == null)
            {
                return;
            }

            if (!TryGetCueById(grabCueId, out var cue))
            {
                StopGrabMode();
                return;
            }

            // Keep the preview parked on its anchor and alive so it stays draggable while auditioning.
            SyncGrabPreview(cue);
            var systems = grabPreviewInstance.GetComponentsInChildren<ParticleSystem>(true);
            var anyAlive = false;
            for (var i = 0; i < systems.Length; i++)
            {
                if (systems[i].IsAlive(true))
                {
                    anyAlive = true;
                    break;
                }
            }

            if (!anyAlive)
            {
                PlayPrefabParticles(grabPreviewInstance);
            }
        }
#endif

        // Mirrors EffectPresentationController spawn anchoring/facing so Play CSV lines up with Play Catalog
        // for identical values. Player is always the implicit VFX source in the lab.
        private TunedSpawnContext ResolveTunedSpawnContext(EffectVfxSpawnAnchor spawnAnchor, string targetUnitId, EffectResultEvent resultEvent)
        {
            var isField = string.Equals(targetUnitId, "field", StringComparison.OrdinalIgnoreCase);
            var targetVisual = string.Equals(targetUnitId, "player", StringComparison.OrdinalIgnoreCase)
                ? playerVisual
                : monsterVisual;

            CharacterVfxAnchorKind anchorKind;
            Vector3 basePosition;
            var sourceAnchor = CharacterVfxAnchorKind.AttackSource;

            switch (spawnAnchor)
            {
                case EffectVfxSpawnAnchor.SourceAttack:
                    anchorKind = CharacterVfxAnchorKind.AttackSource;
                    sourceAnchor = CharacterVfxAnchorKind.AttackSource;
                    basePosition = ResolveAnchorPosition(playerVisual, anchorKind);
                    break;
                case EffectVfxSpawnAnchor.SourceGround:
                    anchorKind = CharacterVfxAnchorKind.Ground;
                    sourceAnchor = CharacterVfxAnchorKind.Ground;
                    basePosition = ResolveAnchorPosition(playerVisual, anchorKind);
                    break;
                case EffectVfxSpawnAnchor.TargetHitCenter:
                    anchorKind = CharacterVfxAnchorKind.HitCenter;
                    basePosition = ResolveAnchorPosition(targetVisual, anchorKind);
                    break;
                case EffectVfxSpawnAnchor.TargetGround:
                    anchorKind = CharacterVfxAnchorKind.Ground;
                    basePosition = ResolveAnchorPosition(targetVisual, anchorKind);
                    break;
                case EffectVfxSpawnAnchor.FieldCenter:
                    return new TunedSpawnContext(
                        fieldCenter != null ? fieldCenter.position : transform.position,
                        Quaternion.identity,
                        CharacterVfxAnchorKind.Root);
                default: // Auto: defer to the shared anchor policy, like EffectPresentationController.
                    if (isField)
                    {
                        return new TunedSpawnContext(
                            fieldCenter != null ? fieldCenter.position : transform.position,
                            Quaternion.identity,
                            CharacterVfxAnchorKind.Root);
                    }

                    anchorKind = EffectVfxAnchorPolicy.ResolveTargetAnchor(resultEvent);
                    sourceAnchor = EffectVfxAnchorPolicy.ResolveSourceAnchor(resultEvent);
                    basePosition = ResolveAnchorPosition(targetVisual, anchorKind);
                    break;
            }

            // Match the runtime bridge: source-anchored VFX face the actual target so authored
            // offsets/rotations live in attack-direction space, not world space.
            var facingTarget = basePosition;
            if (spawnAnchor == EffectVfxSpawnAnchor.SourceAttack || spawnAnchor == EffectVfxSpawnAnchor.SourceGround)
            {
                facingTarget = ResolveAnchorPosition(targetVisual, CharacterVfxAnchorKind.HitCenter);
            }

            var facing = ResolveFacingFromSource(playerVisual, sourceAnchor, facingTarget);
            return new TunedSpawnContext(basePosition, facing, anchorKind);
        }

        private Quaternion ResolveFacingFromSource(CharacterActorVisual source, CharacterVfxAnchorKind sourceAnchor, Vector3 targetWorld)
        {
            if (source == null)
            {
                return Quaternion.identity;
            }

            return ResolveFacing(ResolveAnchorPosition(source, sourceAnchor), targetWorld);
        }

        // Delegates to EffectPresentationController.ResolveRadiusScale so Play CSV matches Play Catalog,
        // including the field/area footprint growth.
        private static float ResolveTunedScale(CardVfxTuning tuning, EffectResultEvent resultEvent)
        {
            return EffectPresentationController.ResolveRadiusScale(tuning.ScaleWithRadius, resultEvent) * tuning.ScaleMultiplier;
        }

        private static float ResolveTunedScale(MonsterVfxTuning tuning, EffectResultEvent resultEvent)
        {
            return EffectPresentationController.ResolveRadiusScale(tuning.ScaleWithRadius, resultEvent) * tuning.ScaleMultiplier;
        }

        private GameObject SpawnTunedCsvVfx(GameObject prefab, Vector3 position, Quaternion rotation, float scale, float lifetime, CharacterVfxAnchorKind anchorKind)
        {
            if (prefab == null)
            {
                return null;
            }

            var instance = Instantiate(prefab, position, rotation, transform);
            instance.name = $"CSV Tuned VFX ({prefab.name})";
            instance.transform.localScale = Vector3.Scale(instance.transform.localScale, Vector3.one * Mathf.Max(0.0001f, scale));
            directVfxInstances.Add(instance);
            PlayPrefabParticles(instance);
            SpawnPreviewMarker(position, anchorKind);

            if (Application.isPlaying && lifetime > 0f)
            {
                Destroy(instance, lifetime);
            }

            return instance;
        }

        private void RecordCsvDirectVfxDiagnostics(
            CardVfxCueRow cue,
            CardVfxTuning tuning,
            EffectResultEvent resultEvent,
            GameObject prefab,
            Vector3 position,
            float scale,
            float lifetime,
            CharacterVfxAnchorKind anchorKind)
        {
            var builder = new StringBuilder()
                .Append("Play CSV (direct prefab)")
                .Append(IsTuningCue(cue) ? "  [LIVE TUNING]" : string.Empty)
                .Append('\n')
                .Append("cueId=").Append(cue.CueId)
                .Append(" / card=").Append(cue.CardId)
                .Append(" / kind=").Append(cue.EffectKind)
                .Append('\n')
                .Append("CSV prefab=").Append(prefab != null ? prefab.name : "(missing)")
                .Append('\n')
                .Append("  ").Append(cue.PrefabPath)
                .Append('\n')
                .Append("spawnAnchor=").Append(tuning.SpawnAnchor).Append(" -> ").Append(anchorKind)
                .Append('\n')
                .Append("scale=").Append(FormatFloat(scale))
                .Append(" (x").Append(FormatFloat(tuning.ScaleMultiplier))
                .Append(", radius=").Append(tuning.ScaleWithRadius ? "on" : "off")
                .Append(", r=").Append(resultEvent.Radius).Append(')')
                .Append('\n')
                .Append("offset=").Append(FormatVector(tuning.Offset))
                .Append('\n')
                .Append("rotation=").Append(FormatVector(tuning.RotationEuler))
                .Append('\n')
                .Append("lifetime=").Append(FormatFloat(lifetime))
                .Append(tuning.LifetimeOverride > 0f ? " (override)" : " (lab default)")
                .Append('\n')
                .Append("worldPos=").Append(FormatVector(position));

            var catalog = vfxCatalog != null ? vfxCatalog : effectPresentation != null ? effectPresentation.VfxCatalog : null;
            if (catalog != null && catalog.TryResolve(resultEvent, out var entry))
            {
                builder.Append('\n')
                    .Append("Catalog resolves cueId=")
                    .Append(string.IsNullOrWhiteSpace(entry.CueId) ? "(none)" : entry.CueId)
                    .Append(" / prefab=").Append(DescribePrefabs(entry));
                AppendCatalogCsvComparison(builder, cue, tuning, entry);
            }
            else
            {
                builder.Append('\n')
                    .Append("Catalog resolves: (missing) ??Play Catalog would fall back to a placeholder particle.");
            }

            lastVfxSummary = builder.ToString();
        }

        private void AppendCatalogComparisonToSummary(CardVfxCueRow cue, EffectResultEvent resultEvent)
        {
            var catalog = vfxCatalog != null ? vfxCatalog : effectPresentation != null ? effectPresentation.VfxCatalog : null;
            var builder = new StringBuilder(lastVfxSummary)
                .Append('\n')
                .Append("--- Play Catalog vs CSV ---")
                .Append('\n')
                .Append("CSV prefab=").Append(Path.GetFileNameWithoutExtension(cue.PrefabPath));

            if (catalog != null && catalog.TryResolve(resultEvent, out var entry))
            {
                AppendCatalogCsvComparison(builder, cue, CardVfxTuning.FromCue(cue), entry);
            }
            else
            {
                builder.Append('\n')
                    .Append("WARNING: catalog has no entry for this cue; Play Catalog used a placeholder particle while Play CSV uses the CSV prefab.");
            }

            lastVfxSummary = builder.ToString();
        }

        // Surfaces drift between the authored CSV (source of truth) and the baked catalog entry so the
        // designer can tell whether a catalog rebuild is needed before trusting Play Catalog.
        private static void AppendCatalogCsvComparison(StringBuilder builder, CardVfxCueRow cue, CardVfxTuning tuning, EffectVfxCatalog.Entry entry)
        {
            var warnings = new List<string>();

            var csvPrefabName = Path.GetFileNameWithoutExtension(cue.PrefabPath);
            var catalogPrefabName = FirstPrefabName(entry);
            if (!string.IsNullOrEmpty(catalogPrefabName) &&
                !string.Equals(csvPrefabName, catalogPrefabName, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"prefab CSV={csvPrefabName} vs catalog={catalogPrefabName}");
            }

            if (entry.SpawnAnchor != tuning.SpawnAnchor)
            {
                warnings.Add($"spawnAnchor CSV={tuning.SpawnAnchor} vs catalog={entry.SpawnAnchor}");
            }

            if (!Mathf.Approximately(entry.ScaleMultiplier, tuning.ScaleMultiplier))
            {
                warnings.Add($"scaleMultiplier CSV={FormatFloat(tuning.ScaleMultiplier)} vs catalog={FormatFloat(entry.ScaleMultiplier)}");
            }

            if (entry.ScaleWithRadius != tuning.ScaleWithRadius)
            {
                warnings.Add($"scaleWithRadius CSV={tuning.ScaleWithRadius} vs catalog={entry.ScaleWithRadius}");
            }

            if (!ApproximatelyVector(entry.PositionOffset, tuning.Offset, 0.01f))
            {
                warnings.Add($"offset CSV={FormatVector(tuning.Offset)} vs catalog={FormatVector(entry.PositionOffset)}");
            }

            var csvEuler = Quaternion.Euler(tuning.RotationEuler).eulerAngles;
            var catalogEuler = entry.RotationOffset.eulerAngles;
            if (!ApproximatelyVector(csvEuler, catalogEuler, 0.5f))
            {
                warnings.Add($"rotation CSV={FormatVector(tuning.RotationEuler)} vs catalog={FormatVector(catalogEuler)}");
            }

            if (!Mathf.Approximately(entry.LifetimeOverride, tuning.LifetimeOverride))
            {
                warnings.Add($"lifetime CSV={FormatFloat(tuning.LifetimeOverride)} vs catalog={FormatFloat(entry.LifetimeOverride)}");
            }

            if (!Mathf.Approximately(entry.PlaybackDelaySeconds, tuning.DelaySeconds))
            {
                warnings.Add($"delaySeconds CSV={FormatFloat(tuning.DelaySeconds)} vs catalog={FormatFloat(entry.PlaybackDelaySeconds)}");
            }

            if (warnings.Count == 0)
            {
                builder.Append('\n').Append("MATCH: CSV values equal the catalog entry.");
                return;
            }

            builder.Append('\n').Append("WARNING: CSV vs Catalog differs:");
            for (var i = 0; i < warnings.Count; i++)
            {
                builder.Append("\n  - ").Append(warnings[i]);
            }
        }

        private static string FirstPrefabName(EffectVfxCatalog.Entry entry)
        {
            if (entry == null)
            {
                return string.Empty;
            }

            foreach (var prefab in entry.Prefabs)
            {
                if (prefab != null)
                {
                    return prefab.name;
                }
            }

            return string.Empty;
        }

        private static bool ApproximatelyVector(Vector3 a, Vector3 b, float tolerance)
        {
            return Mathf.Abs(a.x - b.x) <= tolerance &&
                   Mathf.Abs(a.y - b.y) <= tolerance &&
                   Mathf.Abs(a.z - b.z) <= tolerance;
        }

        private static string FormatVector(Vector3 value)
        {
            return $"({FormatFloat(value.x)}, {FormatFloat(value.y)}, {FormatFloat(value.z)})";
        }

        private static bool TryParseEffectKind(string value, out EffectKind kind)
        {
            return Enum.TryParse(value, ignoreCase: true, out kind);
        }

        private static float ParseFloat(string value, float fallback = 0f)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
                ? result
                : fallback;
        }

        private static bool ParseBool(string value)
        {
            return bool.TryParse(value, out var result) && result;
        }

        private static EffectVfxSpawnAnchor ParseSpawnAnchor(string value)
        {
            return Enum.TryParse<EffectVfxSpawnAnchor>(value, ignoreCase: true, out var result)
                ? result
                : EffectVfxSpawnAnchor.Auto;
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string DisplaySpawnAnchor(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? EffectVfxSpawnAnchor.Auto.ToString() : value;
        }

        private static string ResolveCardVfxTargetUnitId(string targetFilter)
        {
            if (string.Equals(targetFilter, "Player", StringComparison.OrdinalIgnoreCase))
            {
                return "player";
            }

            if (string.Equals(targetFilter, "Field", StringComparison.OrdinalIgnoreCase))
            {
                return "field";
            }

            return "monster";
        }

        private void DrawVfxAssetBrowser()
        {
            GUILayout.Space(8f);
            GUILayout.Label("Project VFX Browser");
#if UNITY_EDITOR
            if (vfxBrowserEntries.Count == 0)
            {
                RefreshVfxBrowserEntries();
            }

            GUILayout.Label($"Scanned prefabs: {vfxBrowserEntries.Count}");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search", GUILayout.Width(52f));
            vfxBrowserSearch = GUILayout.TextField(vfxBrowserSearch ?? string.Empty);
            GUILayout.EndHorizontal();

            var filtered = GetFilteredVfxBrowserEntries();
            if (filtered.Count == 0)
            {
                GUILayout.Label("No matching VFX prefabs.");
                if (GUILayout.Button("Rescan VFX Prefabs")) RefreshVfxBrowserEntries();
                return;
            }

            vfxBrowserIndex = Mathf.Clamp(vfxBrowserIndex, 0, filtered.Count - 1);
            var selected = filtered[vfxBrowserIndex];
            GUILayout.Label($"{vfxBrowserIndex + 1}/{filtered.Count}: {selected.Prefab.name}");
            GUILayout.Label(selected.AssetPath);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Rescan")) RefreshVfxBrowserEntries();
            if (GUILayout.Button("Prev")) StepVfxBrowserSelection(-1);
            if (GUILayout.Button("Next")) StepVfxBrowserSelection(1);
            GUILayout.EndHorizontal();

            GUILayout.Label("Spawn Anchor");
            GUILayout.BeginHorizontal();
            DrawVfxBrowserAnchorButton("Root", CharacterVfxAnchorKind.Root);
            DrawVfxBrowserAnchorButton("Hit", CharacterVfxAnchorKind.HitCenter);
            DrawVfxBrowserAnchorButton("Ground", CharacterVfxAnchorKind.Ground);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DrawVfxBrowserAnchorButton("Head", CharacterVfxAnchorKind.Head);
            DrawVfxBrowserAnchorButton("Attack", CharacterVfxAnchorKind.AttackSource);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Play Player")) PlayDirectVfx(selected.Prefab, playerVisual, vfxBrowserAnchor);
            if (GUILayout.Button("Play Monster")) PlayDirectVfx(selected.Prefab, monsterVisual, vfxBrowserAnchor);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Play Field")) PlayDirectVfxAt(selected.Prefab, fieldCenter != null ? fieldCenter.position : transform.position, CharacterVfxAnchorKind.Root);
            if (GUILayout.Button("Clear Direct")) ClearDirectVfx();
            GUILayout.EndHorizontal();

            GUILayout.Label("Nearby Matches");
            var first = Mathf.Max(0, vfxBrowserIndex - 2);
            var last = Mathf.Min(filtered.Count - 1, first + 4);
            first = Mathf.Max(0, last - 4);
            for (var i = first; i <= last; i++)
            {
                var entry = filtered[i];
                var label = i == vfxBrowserIndex ? $"> {entry.Prefab.name}" : entry.Prefab.name;
                if (GUILayout.Button(label))
                {
                    vfxBrowserIndex = i;
                }
            }
#else
            GUILayout.Label("VFX prefab browser requires the Unity Editor AssetDatabase.");
#endif
        }

#if UNITY_EDITOR
        private void RefreshVfxBrowserEntries()
        {
            vfxBrowserEntries.Clear();
            var folders = new List<string>();
            for (var i = 0; i < VfxBrowserFolders.Length; i++)
            {
                if (AssetDatabase.IsValidFolder(VfxBrowserFolders[i]))
                {
                    folders.Add(VfxBrowserFolders[i]);
                }
            }

            if (folders.Count == 0)
            {
                vfxBrowserIndex = 0;
                return;
            }

            var guids = AssetDatabase.FindAssets("t:Prefab", folders.ToArray());
            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                {
                    vfxBrowserEntries.Add(new VfxBrowserEntry(prefab, path));
                }
            }

            vfxBrowserEntries.Sort((a, b) => string.Compare(a.AssetPath, b.AssetPath, StringComparison.OrdinalIgnoreCase));
            vfxBrowserIndex = Mathf.Clamp(vfxBrowserIndex, 0, Mathf.Max(0, vfxBrowserEntries.Count - 1));
        }

        private List<VfxBrowserEntry> GetFilteredVfxBrowserEntries()
        {
            var filtered = new List<VfxBrowserEntry>();
            var search = vfxBrowserSearch ?? string.Empty;
            for (var i = 0; i < vfxBrowserEntries.Count; i++)
            {
                var entry = vfxBrowserEntries[i];
                if (string.IsNullOrWhiteSpace(search) ||
                    entry.Prefab.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    entry.AssetPath.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    filtered.Add(entry);
                }
            }

            return filtered;
        }

        private void StepVfxBrowserSelection(int delta)
        {
            var filtered = GetFilteredVfxBrowserEntries();
            if (filtered.Count == 0)
            {
                vfxBrowserIndex = 0;
                return;
            }

            vfxBrowserIndex = (vfxBrowserIndex + delta + filtered.Count) % filtered.Count;
        }
#endif

        private void DrawVfxBrowserAnchorButton(string label, CharacterVfxAnchorKind anchorKind)
        {
            var selected = vfxBrowserAnchor == anchorKind;
            if (GUILayout.Toggle(selected, label, GUI.skin.button) != selected)
            {
                vfxBrowserAnchor = anchorKind;
            }
        }

        private void PlayDirectVfx(GameObject prefab, CharacterActorVisual visual, CharacterVfxAnchorKind anchorKind)
        {
            if (visual == null)
            {
                return;
            }

            PlayDirectVfxAt(prefab, ResolveAnchorPosition(visual, anchorKind), anchorKind);
        }

        private void PlayDirectVfxAt(GameObject prefab, Vector3 position, CharacterVfxAnchorKind anchorKind)
        {
            if (prefab == null)
            {
                return;
            }

            var instance = Instantiate(prefab, position, Quaternion.Euler(directVfxRotationEulerOffset), transform);
            instance.name = $"Direct VFX Preview ({prefab.name})";
            directVfxInstances.Add(instance);
            PlayPrefabParticles(instance);
            SpawnPreviewMarker(position, anchorKind);

            if (Application.isPlaying && directVfxLifetime > 0f)
            {
                Destroy(instance, directVfxLifetime);
            }
        }

        private static void PlayPrefabParticles(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var particles = root.GetComponentsInChildren<ParticleSystem>(true);
            for (var i = 0; i < particles.Length; i++)
            {
                var particle = particles[i];
                particle.gameObject.SetActive(true);
                particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                particle.Play(true);
            }
        }

        private void ClearDirectVfx()
        {
            for (var i = 0; i < directVfxInstances.Count; i++)
            {
                DestroyObjectSafely(directVfxInstances[i]);
            }

            directVfxInstances.Clear();
        }

        private void PlayActorVfx(CharacterActorVisual source, CharacterActorVisual target, EffectResultEvent resultEvent)
        {
            if (effectPresentation == null || target == null)
            {
                return;
            }

            var entries = ResolveVfxEntries(resultEvent);
            if (entries.Length > 1)
            {
                var playedAny = false;
                for (var i = 0; i < entries.Length; i++)
                {
                    var entryForPlay = entries[i];
                    var anchorForPlay = ResolvePreviewAnchor(entryForPlay, resultEvent);
                    var worldForPlay = ResolvePreviewWorldPosition(entryForPlay, resultEvent, source, target, anchorForPlay);
                    var rotationForPlay = Quaternion.identity;
                    if (source != null)
                    {
                        var sourceWorldForPlay = ResolveAnchorPosition(source, EffectVfxAnchorPolicy.ResolveSourceAnchor(entryForPlay, resultEvent));
                        rotationForPlay = ResolveFacing(sourceWorldForPlay, worldForPlay);
                    }

                    effectPresentation.PlayResolvedEntry(
                        resultEvent,
                        entryForPlay,
                        worldForPlay,
                        rotationForPlay,
                        showFloatingText: !playedAny,
                        sourceFollowAnchor: ResolveLabSourceFollowAnchor(entryForPlay, resultEvent, source));
                    SpawnPreviewMarker(worldForPlay, anchorForPlay);
                    playedAny = true;
                }

                RecordVfxDiagnostics(resultEvent, source, target, ResolvePreviewAnchor(entries[0], resultEvent));
                return;
            }

            var entry = entries.Length == 1 ? entries[0] : ResolveVfxEntry(resultEvent);
            var targetAnchor = ResolvePreviewAnchor(entry, resultEvent);
            var targetWorld = ResolvePreviewWorldPosition(entry, resultEvent, source, target, targetAnchor);
            RecordVfxDiagnostics(resultEvent, source, target, targetAnchor);
            var rotation = Quaternion.identity;
            if (source != null)
            {
                var sourceWorld = ResolveAnchorPosition(source, EffectVfxAnchorPolicy.ResolveSourceAnchor(entry, resultEvent));
                rotation = ResolveFacing(sourceWorld, targetWorld);
            }

            effectPresentation.Play(resultEvent, targetWorld, rotation, ResolveLabSourceFollowAnchor(entry, resultEvent, source));
            SpawnPreviewMarker(targetWorld, targetAnchor);
        }

        // Lab twin of the runtime bridge's ResolveSourceFollowAnchor: followSourceAnchor cues track the
        // spawned lab actor's source anchor so the production follow behaviour previews here too.
        private static Transform ResolveLabSourceFollowAnchor(
            EffectVfxCatalog.Entry entry,
            EffectResultEvent resultEvent,
            CharacterActorVisual source)
        {
            if (entry == null || !entry.FollowSourceAnchor || source == null)
            {
                return null;
            }

            return source.TryGetVfxAnchor(EffectVfxAnchorPolicy.ResolveSourceAnchor(entry, resultEvent), out var anchor)
                ? anchor
                : null;
        }

        private EffectVfxCatalog.Entry ResolveVfxEntry(EffectResultEvent resultEvent)
        {
            var catalog = vfxCatalog != null ? vfxCatalog : effectPresentation != null ? effectPresentation.VfxCatalog : null;
            return catalog != null && catalog.TryResolve(resultEvent, out var entry) ? entry : null;
        }

        private EffectVfxCatalog.Entry[] ResolveVfxEntries(EffectResultEvent resultEvent)
        {
            var catalog = vfxCatalog != null ? vfxCatalog : effectPresentation != null ? effectPresentation.VfxCatalog : null;
            return catalog != null ? catalog.ResolveAll(resultEvent) : Array.Empty<EffectVfxCatalog.Entry>();
        }

        private CharacterVfxAnchorKind ResolvePreviewAnchor(EffectVfxCatalog.Entry entry, EffectResultEvent resultEvent)
        {
            if (entry != null)
            {
                switch (entry.SpawnAnchor)
                {
                    case EffectVfxSpawnAnchor.SourceAttack:
                    case EffectVfxSpawnAnchor.SourceGround:
                        return EffectVfxAnchorPolicy.ResolveSourceAnchor(entry, resultEvent);
                    case EffectVfxSpawnAnchor.TargetHitCenter:
                    case EffectVfxSpawnAnchor.TargetGround:
                        return EffectVfxAnchorPolicy.ResolveTargetAnchor(entry, resultEvent);
                    case EffectVfxSpawnAnchor.FieldCenter:
                        return CharacterVfxAnchorKind.Root;
                }
            }

            return EffectVfxAnchorPolicy.ResolveTargetAnchor(resultEvent);
        }

        private Vector3 ResolvePreviewWorldPosition(
            EffectVfxCatalog.Entry entry,
            EffectResultEvent resultEvent,
            CharacterActorVisual source,
            CharacterActorVisual target,
            CharacterVfxAnchorKind fallbackAnchor)
        {
            if (entry != null)
            {
                switch (entry.SpawnAnchor)
                {
                    case EffectVfxSpawnAnchor.SourceAttack:
                    case EffectVfxSpawnAnchor.SourceGround:
                        return ResolveAnchorPosition(source, EffectVfxAnchorPolicy.ResolveSourceAnchor(entry, resultEvent));
                    case EffectVfxSpawnAnchor.TargetHitCenter:
                    case EffectVfxSpawnAnchor.TargetGround:
                        return ResolveAnchorPosition(target, EffectVfxAnchorPolicy.ResolveTargetAnchor(entry, resultEvent));
                    case EffectVfxSpawnAnchor.FieldCenter:
                        return fieldCenter != null ? fieldCenter.position : transform.position;
                }
            }

            return ResolveAnchorPosition(target, fallbackAnchor);
        }

        private void PlaySelfVfx(CharacterActorVisual target, string targetUnitId, EffectKind kind, string sourceRef, int amount)
        {
            var resultEvent = new EffectResultEvent(
                kind,
                targetUnitId: targetUnitId,
                amount: amount,
                appliedAmount: amount,
                sourceUnitId: targetUnitId,
                sourceRef: sourceRef);
            PlayActorVfx(target, target, resultEvent);
        }

        private void PlayBuffVfx(CharacterActorVisual target, string targetUnitId, string sourceRef)
        {
            var resultEvent = new EffectResultEvent(
                EffectKind.StatusEffectApplied,
                targetUnitId: targetUnitId,
                appliedAmount: 1,
                sourceUnitId: targetUnitId,
                sourceRef: sourceRef,
                statusKind: StatusEffectKind.Agility);
            PlayActorVfx(target, target, resultEvent);
        }

        private void PlayFieldCenterVfx()
        {
            if (effectPresentation == null)
            {
                return;
            }

            var center = fieldCenter != null ? fieldCenter.position : transform.position;
            var resultEvent = new EffectResultEvent(
                EffectKind.FogReveal,
                targetUnitId: "field",
                amount: 1,
                appliedAmount: 1,
                radius: 1,
                sourceRef: "field.lab");
            RecordVfxDiagnostics(resultEvent, null, null, CharacterVfxAnchorKind.Root);
            effectPresentation.Play(resultEvent, center, Quaternion.identity);
            SpawnPreviewMarker(center, CharacterVfxAnchorKind.Root);
        }

        private void PlayObjectVfx(string sourceRef)
        {
            if (effectPresentation == null || string.IsNullOrWhiteSpace(sourceRef))
            {
                return;
            }

            var center = fieldCenter != null ? fieldCenter.position : transform.position;
            var resultEvent = new EffectResultEvent(
                EffectKind.StatusEffectApplied,
                targetUnitId: "field",
                appliedAmount: 1,
                sourceRef: sourceRef);
            RecordVfxDiagnostics(resultEvent, null, null, CharacterVfxAnchorKind.Root);
            effectPresentation.Play(resultEvent, center, Quaternion.identity);
            SpawnPreviewMarker(center, CharacterVfxAnchorKind.Root);
        }

        private void DrawLastVfxDiagnostics()
        {
            GUILayout.Space(6f);
            GUILayout.Label("Last VFX Diagnostics");
            GUILayout.Label(lastVfxSummary);
            if (hasLastVfxEvent && GUILayout.Button("Replay Last VFX"))
            {
                ReplayLastVfx();
            }
        }

        private void ReplayLastVfx()
        {
            if (!hasLastVfxEvent || effectPresentation == null)
            {
                return;
            }

            if (lastVfxTarget != null)
            {
                PlayActorVfx(lastVfxSource, lastVfxTarget, lastVfxEvent);
                return;
            }

            var center = fieldCenter != null ? fieldCenter.position : transform.position;
            RecordVfxDiagnostics(lastVfxEvent, null, null, CharacterVfxAnchorKind.Root);
            effectPresentation.Play(lastVfxEvent, center, Quaternion.identity);
            SpawnPreviewMarker(center, CharacterVfxAnchorKind.Root);
        }

        private void RecordVfxDiagnostics(
            EffectResultEvent resultEvent,
            CharacterActorVisual source,
            CharacterActorVisual target,
            CharacterVfxAnchorKind targetAnchor)
        {
            hasLastVfxEvent = true;
            lastVfxEvent = resultEvent;
            lastVfxSource = source;
            lastVfxTarget = target;
            lastVfxAnchorKind = targetAnchor;

            var builder = new StringBuilder();
            builder.Append("Command: ")
                .Append(resultEvent.Kind)
                .Append(", sourceRef=")
                .Append(string.IsNullOrWhiteSpace(resultEvent.SourceRef) ? "(kind fallback)" : resultEvent.SourceRef)
                .Append(", target=")
                .Append(string.IsNullOrWhiteSpace(resultEvent.TargetUnitId) ? "(none)" : resultEvent.TargetUnitId)
                .Append('\n')
                .Append("Anchor: ")
                .Append(lastVfxAnchorKind)
                .Append(", source=")
                .Append(DescribeVisualName(source))
                .Append(", target=")
                .Append(DescribeVisualName(target));

            var catalog = vfxCatalog != null ? vfxCatalog : effectPresentation != null ? effectPresentation.VfxCatalog : null;
            EffectVfxCatalog.Entry resolvedEntry = null;
            if (catalog != null && catalog.TryResolve(resultEvent, out resolvedEntry))
            {
                builder.Append('\n')
                    .Append("Resolved: ")
                    .Append(string.IsNullOrWhiteSpace(resolvedEntry.CueId) ? "(no cue id)" : resolvedEntry.CueId)
                    .Append(" / ")
                    .Append(string.IsNullOrWhiteSpace(resolvedEntry.DisplayName) ? "(no display name)" : resolvedEntry.DisplayName)
                    .Append(", prefabs=")
                    .Append(DescribePrefabs(resolvedEntry))
                    .Append(", spawnAnchor=")
                    .Append(resolvedEntry.SpawnAnchor);
            }
            else
            {
                builder.Append('\n')
                    .Append("Warning: missing catalog match; EffectPresentationController will use placeholder fallback.");
            }

            var floatingMode = resolvedEntry != null ? resolvedEntry.FloatingTextMode : EffectFloatingTextMode.Auto;
            var willShow = EffectPresentationController.ShouldShowFloatingText(resolvedEntry, resultEvent);
            builder.Append('\n')
                .Append("FloatingText: mode=")
                .Append(floatingMode)
                .Append(", shown=")
                .Append(willShow ? "yes" : "no");
            if (resolvedEntry != null && !string.IsNullOrWhiteSpace(resolvedEntry.FloatingTextOverride))
            {
                builder.Append(", override=\"").Append(resolvedEntry.FloatingTextOverride).Append('"');
            }

            lastVfxSummary = builder.ToString();
        }

        private static string DescribeVisualName(CharacterActorVisual visual)
        {
            return visual != null ? visual.gameObject.name : "(field)";
        }

        private static string DescribePrefabs(EffectVfxCatalog.Entry entry)
        {
            if (entry == null || entry.Prefabs.Length == 0)
            {
                return "(none)";
            }

            var builder = new StringBuilder();
            for (var i = 0; i < entry.Prefabs.Length; i++)
            {
                var prefab = entry.Prefabs[i];
                if (prefab == null)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(prefab.name);
            }

            return builder.Length > 0 ? builder.ToString() : "(none)";
        }

        private readonly struct VfxMatrixRow
        {
            public VfxMatrixRow(
                string action,
                bool canAnimate,
                EffectResultEvent resultEvent,
                CharacterVfxAnchorKind anchor,
                bool hasVfx,
                string prefabSummary)
            {
                Action = action ?? string.Empty;
                CanAnimate = canAnimate;
                ResultEvent = resultEvent;
                Anchor = anchor;
                HasVfx = hasVfx;
                PrefabSummary = prefabSummary ?? string.Empty;
            }

            public string Action { get; }
            public bool CanAnimate { get; }
            public EffectResultEvent ResultEvent { get; }
            public CharacterVfxAnchorKind Anchor { get; }
            public bool HasVfx { get; }
            public string PrefabSummary { get; }
        }

#if UNITY_EDITOR
        private readonly struct VfxBrowserEntry
        {
            public VfxBrowserEntry(GameObject prefab, string assetPath)
            {
                Prefab = prefab;
                AssetPath = assetPath ?? string.Empty;
            }

            public GameObject Prefab { get; }
            public string AssetPath { get; }
        }
#endif

        private readonly struct CardVfxAssignmentRow
        {
            public CardVfxAssignmentRow(
                string cardId,
                string name,
                string type,
                string target,
                string behaviorId,
                string status,
                string currentPrefabPaths,
                string desiredOwner)
            {
                CardId = cardId ?? string.Empty;
                Name = name ?? string.Empty;
                Type = type ?? string.Empty;
                Target = target ?? string.Empty;
                BehaviorId = behaviorId ?? string.Empty;
                Status = status ?? string.Empty;
                CurrentPrefabPaths = currentPrefabPaths ?? string.Empty;
                DesiredOwner = desiredOwner ?? string.Empty;
            }

            public string CardId { get; }
            public string Name { get; }
            public string Type { get; }
            public string Target { get; }
            public string BehaviorId { get; }
            public string Status { get; }
            public string CurrentPrefabPaths { get; }
            public string DesiredOwner { get; }
        }

        private readonly struct MonsterPatternVfxRow
        {
            public MonsterPatternVfxRow(
                string patternId,
                string displayName,
                string monsterId,
                string monsterName,
                string animationTrigger,
                string vfxCueId,
               int areaRadius,
               string bindingSpawnAnchor = "",
               int bindingOrder = 1,
               float delaySeconds = 0f)
            {
                PatternId = patternId ?? string.Empty;
                DisplayName = displayName ?? string.Empty;
                MonsterId = monsterId ?? string.Empty;
                MonsterName = monsterName ?? string.Empty;
                AnimationTrigger = animationTrigger ?? string.Empty;
                VfxCueId = vfxCueId ?? string.Empty;
                AreaRadius = Mathf.Max(0, areaRadius);
               BindingSpawnAnchor = bindingSpawnAnchor ?? string.Empty;
               BindingOrder = Mathf.Max(0, bindingOrder);
               DelaySeconds = Mathf.Max(0f, delaySeconds);
            }

            public string PatternId { get; }
            public string DisplayName { get; }
            public string MonsterId { get; }
            public string MonsterName { get; }
            public string AnimationTrigger { get; }
            public string VfxCueId { get; }
            public int AreaRadius { get; }
           public string BindingSpawnAnchor { get; }
           public int BindingOrder { get; }
           public float DelaySeconds { get; }
        }

        private readonly struct MonsterVfxCueRow
        {
            public MonsterVfxCueRow(
                string vfxCueId,
                string effectKind,
                string targetFilter,
                string sourceRef,
                string matchSourceRefPrefix,
                string spawnAnchor,
                string prefabPath,
                float scaleMultiplier,
                bool scaleWithRadius,
               Vector3 offset,
               Vector3 rotationEuler,
               float lifetimeOverride,
               float delaySeconds,
               string designerNote,
               bool followSourceAnchor = false)
            {
                VfxCueId = vfxCueId ?? string.Empty;
                EffectKind = effectKind ?? string.Empty;
                TargetFilter = targetFilter ?? string.Empty;
                SourceRef = sourceRef ?? string.Empty;
                MatchSourceRefPrefix = matchSourceRefPrefix ?? string.Empty;
                SpawnAnchor = string.IsNullOrWhiteSpace(spawnAnchor) ? EffectVfxSpawnAnchor.Auto.ToString() : spawnAnchor;
                PrefabPath = prefabPath ?? string.Empty;
                ScaleMultiplier = scaleMultiplier <= 0f ? 1f : scaleMultiplier;
                ScaleWithRadius = scaleWithRadius;
                Offset = offset;
               RotationEuler = rotationEuler;
               LifetimeOverride = Mathf.Max(0f, lifetimeOverride);
               DelaySeconds = Mathf.Max(0f, delaySeconds);
               DesignerNote = designerNote ?? string.Empty;
               FollowSourceAnchor = followSourceAnchor;
            }

            public string VfxCueId { get; }
            public string EffectKind { get; }
            public string TargetFilter { get; }
            public string SourceRef { get; }
            public string MatchSourceRefPrefix { get; }
            public string SpawnAnchor { get; }
            public string PrefabPath { get; }
            public float ScaleMultiplier { get; }
            public bool ScaleWithRadius { get; }
            public Vector3 Offset { get; }
           public Vector3 RotationEuler { get; }
           public float LifetimeOverride { get; }
           public float DelaySeconds { get; }
           public string DesignerNote { get; }
           public bool FollowSourceAnchor { get; }
        }

        private readonly struct CardVfxCueRow
        {
            public CardVfxCueRow(
                string cueId,
                string cardId,
                string effectKind,
                string targetFilter,
                string sourceRef,
                string spawnAnchor,
                string prefabPath,
                string floatingTextMode,
                string floatingTextOverride,
                float scaleMultiplier,
                bool scaleWithRadius,
               Vector3 offset,
               Vector3 rotationEuler,
               float lifetimeOverride,
               float delaySeconds)
            {
                CueId = cueId ?? string.Empty;
                CardId = cardId ?? string.Empty;
                EffectKind = effectKind ?? string.Empty;
                TargetFilter = targetFilter ?? string.Empty;
                SourceRef = sourceRef ?? string.Empty;
                SpawnAnchor = spawnAnchor ?? string.Empty;
                PrefabPath = prefabPath ?? string.Empty;
                FloatingTextMode = string.IsNullOrWhiteSpace(floatingTextMode) ? "Auto" : floatingTextMode;
                FloatingTextOverride = floatingTextOverride ?? string.Empty;
                ScaleMultiplier = scaleMultiplier <= 0f ? 1f : scaleMultiplier;
                ScaleWithRadius = scaleWithRadius;
                Offset = offset;
               RotationEuler = rotationEuler;
               LifetimeOverride = Mathf.Max(0f, lifetimeOverride);
               DelaySeconds = Mathf.Max(0f, delaySeconds);
            }

            public string CueId { get; }
            public string CardId { get; }
            public string EffectKind { get; }
            public string TargetFilter { get; }
            public string SourceRef { get; }
            public string SpawnAnchor { get; }
            public string PrefabPath { get; }
            public string FloatingTextMode { get; }
            public string FloatingTextOverride { get; }
            public float ScaleMultiplier { get; }
            public bool ScaleWithRadius { get; }
            public Vector3 Offset { get; }
           public Vector3 RotationEuler { get; }
           public float LifetimeOverride { get; }
           public float DelaySeconds { get; }
        }

        private readonly struct CardVfxTuning
        {
            public CardVfxTuning(
                float scaleMultiplier,
                bool scaleWithRadius,
               Vector3 offset,
               Vector3 rotationEuler,
               float lifetimeOverride,
               EffectVfxSpawnAnchor spawnAnchor,
               float delaySeconds = 0f)
            {
                ScaleMultiplier = scaleMultiplier <= 0f ? 1f : scaleMultiplier;
                ScaleWithRadius = scaleWithRadius;
                Offset = offset;
               RotationEuler = rotationEuler;
               LifetimeOverride = Mathf.Max(0f, lifetimeOverride);
               SpawnAnchor = spawnAnchor;
               DelaySeconds = Mathf.Max(0f, delaySeconds);
            }

            public float ScaleMultiplier { get; }
            public bool ScaleWithRadius { get; }
            public Vector3 Offset { get; }
            public Vector3 RotationEuler { get; }
           public float LifetimeOverride { get; }
           public EffectVfxSpawnAnchor SpawnAnchor { get; }
           public float DelaySeconds { get; }

            public static CardVfxTuning FromCue(CardVfxCueRow cue)
            {
                return new CardVfxTuning(
                    cue.ScaleMultiplier,
                    cue.ScaleWithRadius,
                    cue.Offset,
                   cue.RotationEuler,
                   cue.LifetimeOverride,
                   ParseSpawnAnchor(cue.SpawnAnchor),
                   cue.DelaySeconds);
            }
        }

        private readonly struct MonsterVfxTuning
        {
            public MonsterVfxTuning(
                float scaleMultiplier,
                bool scaleWithRadius,
               Vector3 offset,
               Vector3 rotationEuler,
               float lifetimeOverride,
               EffectVfxSpawnAnchor spawnAnchor,
               float delaySeconds = 0f)
            {
                ScaleMultiplier = scaleMultiplier <= 0f ? 1f : scaleMultiplier;
                ScaleWithRadius = scaleWithRadius;
                Offset = offset;
               RotationEuler = rotationEuler;
               LifetimeOverride = Mathf.Max(0f, lifetimeOverride);
               SpawnAnchor = spawnAnchor;
               DelaySeconds = Mathf.Max(0f, delaySeconds);
            }

            public float ScaleMultiplier { get; }
            public bool ScaleWithRadius { get; }
            public Vector3 Offset { get; }
            public Vector3 RotationEuler { get; }
           public float LifetimeOverride { get; }
           public EffectVfxSpawnAnchor SpawnAnchor { get; }
           public float DelaySeconds { get; }

            public static MonsterVfxTuning FromCue(MonsterVfxCueRow cue)
            {
                return FromCue(cue, default);
            }

            public static MonsterVfxTuning FromCue(MonsterVfxCueRow cue, MonsterPatternVfxRow row)
            {
                return new MonsterVfxTuning(
                    cue.ScaleMultiplier,
                    cue.ScaleWithRadius,
                    cue.Offset,
                   cue.RotationEuler,
                   cue.LifetimeOverride,
                   ParseSpawnAnchor(string.IsNullOrWhiteSpace(row.BindingSpawnAnchor) ? cue.SpawnAnchor : row.BindingSpawnAnchor),
                   row.DelaySeconds);
            }
        }

        private readonly struct TunedSpawnContext
        {
            public TunedSpawnContext(Vector3 basePosition, Quaternion facing, CharacterVfxAnchorKind anchorKind)
            {
                BasePosition = basePosition;
                Facing = facing;
                AnchorKind = anchorKind;
            }

            public Vector3 BasePosition { get; }
            public Quaternion Facing { get; }
            public CharacterVfxAnchorKind AnchorKind { get; }
        }

        private Vector3 ResolveAnchorPosition(CharacterActorVisual visual, CharacterVfxAnchorKind anchorKind)
        {
            return visual != null && visual.TryGetVfxAnchor(anchorKind, out var anchor) && anchor != null
                ? anchor.position
                : transform.position;
        }

        private Quaternion ResolveFacing(Vector3 sourceWorld, Vector3 targetWorld)
        {
            var direction = targetWorld - sourceWorld;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return Quaternion.identity;
            }

            return CombatFacingUtility.ResolveHexSideRotation(direction, snapFacingToHexSides, hexFacingYawOffset);
        }

        private void RespawnMonsterOnly()
        {
            if (monsterInstance != null)
            {
                DestroyObjectSafely(monsterInstance);
            }

            monsterInstance = SpawnCharacter(CurrentMonsterPrefab, monsterSlot, monsterSpawnPosition, "Lab Monster", out monsterVisual);
            FaceEachOther();
            ApplyAnchorVisibility();
        }

        private GameObject SpawnCharacter(GameObject prefab, Transform slot, Vector3 fallbackPosition, string instanceName, out CharacterActorVisual visual, Vector3? visualLocalScaleMultiplier = null)
        {
            visual = null;
            if (prefab == null)
            {
                return null;
            }

            var parent = slot != null ? slot : transform;
            var marker = new GameObject(instanceName);
            marker.transform.SetParent(parent, false);
            marker.transform.localPosition = slot != null ? Vector3.zero : fallbackPosition;
            marker.transform.localRotation = Quaternion.identity;
            marker.transform.localScale = Vector3.one;

            var visualRoot = Instantiate(prefab, marker.transform, false);
            // Match the game's spawn scaling: the prefab's authored localScale times the actor's visual-local-scale
            // multiplier (player = AtlasTileView.playerVisualLocalScale, monster = enemyVisualLocalScale). Without this
            // the lab spawned every actor at raw prefab scale, so the player was 1x here but 2.5x in PrototypeTest and
            // fixed-world-size VFX looked relatively smaller in game. Mirrors CombatActorMarkerPresenter's Vector3.Scale.
            var prefabScale = Vector3.Scale(visualRoot.transform.localScale, visualLocalScaleMultiplier ?? Vector3.one);
            visualRoot.name = $"{instanceName} Visual";

            visual = CharacterActorVisual.PrepareInstantiatedVisual(visualRoot, Vector3.zero, Vector3.zero, prefabScale);
            visual?.EnsureAnimationPlaybackReady();
            var debugView = marker.GetComponent<VfxAnchorDebugView>() ?? marker.AddComponent<VfxAnchorDebugView>();
            debugView.Visual = visual;
            debugView.ShowAnchors = showAnchors;
            return marker;
        }

        private void ClearCharacters()
        {
            ClearAllStatusLoops();
            if (playerInstance != null) DestroyObjectSafely(playerInstance);
            if (monsterInstance != null) DestroyObjectSafely(monsterInstance);
            playerInstance = null;
            monsterInstance = null;
            playerVisual = null;
            monsterVisual = null;
        }

        private void ApplyAnchorVisibility()
        {
            SetAnchorVisibility(playerInstance);
            SetAnchorVisibility(monsterInstance);
        }

        private void SetAnchorVisibility(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var debugView = root.GetComponent<VfxAnchorDebugView>();
            if (debugView != null)
            {
                debugView.ShowAnchors = showAnchors;
            }
        }

        private void SpawnPreviewMarker(Vector3 position, CharacterVfxAnchorKind anchorKind)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = $"VFX Spawn Preview ({anchorKind})";
            marker.transform.SetParent(transform, false);
            marker.transform.position = position;
            marker.transform.localScale = Vector3.one * 0.16f;
            var collider = marker.GetComponent<Collider>();
            if (collider != null)
            {
                DestroyObjectSafely(collider);
            }

            var renderer = marker.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = GetPreviewMarkerMaterial();
            }

            previewMarkers.Add(marker);
        }

        private Material GetPreviewMarkerMaterial()
        {
            if (previewMarkerMaterial != null)
            {
                return previewMarkerMaterial;
            }

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            previewMarkerMaterial = new Material(shader) { color = new Color(1f, 0.35f, 1f, 0.9f) };
            return previewMarkerMaterial;
        }

        private void ResolveSceneReferences()
        {
            if (effectPresentation == null)
            {
                effectPresentation = FindFirstObjectByType<EffectPresentationController>();
            }

            if (playerSlot == null)
            {
                var found = GameObject.Find("PlayerSlot");
                playerSlot = found != null ? found.transform : null;
            }

            if (monsterSlot == null)
            {
                var found = GameObject.Find("MonsterSlot");
                monsterSlot = found != null ? found.transform : null;
            }

            if (fieldCenter == null)
            {
                var found = GameObject.Find("FieldCenter");
                fieldCenter = found != null ? found.transform : null;
            }
        }

        private void EnsureRuntimeGround()
        {
            if (!createRuntimeGround || runtimeGroundRoot != null)
            {
                return;
            }

            var existing = GameObject.Find("Lab Runtime Ground");
            if (existing != null)
            {
                runtimeGroundRoot = existing.transform;
                return;
            }

            var root = new GameObject("Lab Runtime Ground");
            runtimeGroundRoot = root.transform;

            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = "Ground Plane";
            plane.transform.SetParent(runtimeGroundRoot, false);
            plane.transform.localScale = new Vector3(0.7f, 1f, 0.7f);
            var planeRenderer = plane.GetComponent<Renderer>();
            if (planeRenderer != null)
            {
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
                {
                    color = new Color(0.18f, 0.2f, 0.22f, 1f)
                };
                planeRenderer.sharedMaterial = material;
            }

            for (var i = -4; i <= 4; i++)
            {
                CreateGridLine($"Grid X {i}", new Vector3(i, 0.015f, -4f), new Vector3(i, 0.015f, 4f), runtimeGroundRoot);
                CreateGridLine($"Grid Z {i}", new Vector3(-4f, 0.015f, i), new Vector3(4f, 0.015f, i), runtimeGroundRoot);
            }
        }

        private static void CreateGridLine(string name, Vector3 start, Vector3 end, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
            line.widthMultiplier = 0.01f;
            line.useWorldSpace = false;
            line.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"))
            {
                color = new Color(0.45f, 0.5f, 0.55f, 0.65f)
            };
        }

        private void ResolveDefaultAssets()
        {
#if UNITY_EDITOR
            if (playerPrefab == null)
            {
                playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerAssetPath);
            }

            if (vfxCatalog == null)
            {
                vfxCatalog = AssetDatabase.LoadAssetAtPath<EffectVfxCatalog>(DefaultCatalogPath);
            }

            if (monsterPrefabs == null || monsterPrefabs.Length == 0)
            {
                var loaded = new List<GameObject>();
                for (var i = 0; i < MonsterAssetPaths.Length; i++)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MonsterAssetPaths[i]);
                    if (prefab != null)
                    {
                        loaded.Add(prefab);
                    }
                }

                monsterPrefabs = loaded.ToArray();
            }
#endif
            if (monsterPrefabs == null)
            {
                monsterPrefabs = Array.Empty<GameObject>();
            }

            if (monsterPrefabs.Length > 0)
            {
                monsterIndex = Mathf.Clamp(monsterIndex, 0, monsterPrefabs.Length - 1);
            }
            else
            {
                monsterIndex = 0;
            }
        }

        private void ApplyCatalog()
        {
            if (effectPresentation != null && vfxCatalog != null)
            {
                effectPresentation.SetVfxCatalog(vfxCatalog);
            }
        }

        private GameObject CurrentMonsterPrefab => monsterPrefabs != null && monsterPrefabs.Length > 0
            ? monsterPrefabs[Mathf.Clamp(monsterIndex, 0, monsterPrefabs.Length - 1)]
            : null;

        private string CurrentMonsterName => CurrentMonsterPrefab != null ? CurrentMonsterPrefab.name : "(none)";

        private static void Rotate(GameObject target, float degrees)
        {
            if (target != null)
            {
                target.transform.Rotate(Vector3.up, degrees, Space.World);
            }
        }

        private static void FaceTransform(Transform target, Vector3 lookAt)
        {
            var direction = lookAt - target.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            target.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        private static void DestroyObjectSafely(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}





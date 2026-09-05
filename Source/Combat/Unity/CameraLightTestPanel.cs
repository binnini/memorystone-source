using System;
using System.Collections.Generic;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Dev-only runtime panel for tuning orthographic camera and simple scene-lighting presets.
    /// It is intentionally scene-scoped and does not promote final camera UX or art direction.
    /// </summary>
    public sealed class CameraLightTestPanel : MonoBehaviour
    {
        [Serializable]
        private struct CameraPreset
        {
            public string Name;
            public Vector3 PlayerOffset;
            public Vector3 EulerAngles;
            public float OrthographicSize;
            public bool KeepCenteredOnPlayer;
        }

        [Serializable]
        private struct LightingPreset
        {
            public string Name;
            public Color AmbientColor;
            public Color CameraBackgroundColor;
            public Color DirectionalLightColor;
            public Vector3 DirectionalLightEulerAngles;
            public float DirectionalLightIntensity;
        }

        private enum MapLightTestKind
        {
            StreetLamp,
            BuildingWindow,
            Neon,
            Campfire,
            Torch
        }

        private enum MapLightCompareMode
        {
            All,
            EmissiveGlowOnly,
            PointLightsOnly,
            ProjectedGlowOnly
        }

        private enum VisualLightApproach
        {
            All,
            Unity3DLight,
            EmissiveGlow,
            ProjectedGlow
        }

        [Serializable]
        private struct MapLightTestObject
        {
            public string Name;
            public HexCoord RelativeCoord;
            public Color Color;
            public float Intensity;
            public int Radius;
            public float Height;
            public MapLightTestKind Kind;
            public bool AffectsFog;
        }

        private static readonly List<CameraLightTestPanel> ActivePanels = new List<CameraLightTestPanel>();

        private readonly struct ActiveMapLightSample
        {
            public ActiveMapLightSample(int instanceId, MapLightTestObject sample, HexCoord coord, VisualLightApproach approach)
            {
                InstanceId = instanceId;
                Sample = sample;
                Coord = coord;
                Approach = approach;
            }

            public int InstanceId { get; }
            public MapLightTestObject Sample { get; }
            public HexCoord Coord { get; }
            public VisualLightApproach Approach { get; }
        }

        private const float PanelWidth = 420f;
        private const float PanelHeight = 680f;
        private const float ButtonHeight = 26f;
        private const float SliderLabelWidth = 150f;
        private const float TileLightOverlayLift = 0.16f;
        private const float LightMaskPrototypeLift = 0.20f;
        private const float PointLightReceiverLift = 0.13f;
        private const int LightCompareRenderQueue = HexOverlayRenderOrder.CombatOverlayRenderQueue + 20;

        [SerializeField] private MapCombatController controller;
        [SerializeField] private Camera targetCamera;
        [SerializeField] private Light directionalLight;
        [SerializeField] private bool showPanel = true;
        [SerializeField] private bool showStandaloneWindow;
        [SerializeField] private bool applyInitialPresetsOnStart = true;
        // Gates ONLY the Start-time lighting preset. Disable in scenes whose lighting is owned by the
        // scene/art direction (e.g. MainGameplay night tone) so this debug panel's camera preset can
        // still apply on Start without stomping RenderSettings/the directional light.
        [SerializeField] private bool applyInitialLightingPresetOnStart = true;
        [SerializeField] private int initialCameraPresetIndex;
        [SerializeField] private int initialLightingPresetIndex;
        [SerializeField] private float zoomStep = 0.5f;
        [SerializeField] private float angleStep = 5f;
        [SerializeField] private float dragPanSpeed = 1f;
        [SerializeField] private bool showTorchFieldObjectLights = true;
        [SerializeField] private Color torchLightColor = new Color(1f, 0.58f, 0.24f, 1f);
        [SerializeField] private float torchLightHeight = 1.1f;
        [SerializeField] private float torchLightRangePerHex = 2.4f;
        [SerializeField] private float torchLightIntensity = 2.4f;
        [SerializeField] private bool showSamplePointLights = true;
        [SerializeField] private bool showTileOverlayLighting = true;
        [SerializeField] private bool showLightMaskPrototype = true;
        [SerializeField] private float globalSampleIntensity = 1f;
        [SerializeField] private float globalSampleRadiusScale = 1f;
        [SerializeField] private float samplePointLightRangePerHex = 2.4f;
        [SerializeField] private float maskDarkness = 0.35f;
        [SerializeField] private float maskIntensity = 1.15f;
        [SerializeField] private float maskRadiusScale = 1f;
        [SerializeField] private int selectedMapLightSampleIndex;
        [SerializeField] private MapLightCompareMode mapLightCompareMode = MapLightCompareMode.All;
        [SerializeField] private MapLightTestObject[] mapLightSamples = CreateDefaultMapLightSamples();
        [SerializeField] private CameraPreset[] cameraPresets = CreateDefaultCameraPresets();
        [SerializeField] private LightingPreset[] lightingPresets = CreateDefaultLightingPresets();

        private Vector3 currentPlayerOffset;
        private Vector3 currentEulerAngles;
        private float currentOrthographicSize;
        private bool currentKeepCentered;
        private int activeCameraPresetIndex = -1;
        private int activeLightingPresetIndex = -1;
        private bool dragCameraMode;
        private bool isDraggingCamera;
        private Vector2 lastDragMousePosition;
        private Rect panelRect = new Rect(16f, 16f, PanelWidth, PanelHeight);
        private Vector2 panelScrollPosition;
        private readonly Dictionary<string, Light> torchLightsByKey = new Dictionary<string, Light>();
        private readonly List<ActiveMapLightSample> activeMapLightSamples = new List<ActiveMapLightSample>();
        private readonly Dictionary<string, Light> samplePointLightsByKey = new Dictionary<string, Light>();
        private readonly Dictionary<string, GameObject> pointLightReceiverObjectsByKey = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, GameObject> lightFixtureObjectsByKey = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, GameObject> tileOverlayObjectsByKey = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, GameObject> lightMaskObjectsByKey = new Dictionary<string, GameObject>();
        private Material tileOverlayMaterial;
        private Material lightMaskMaterial;
        private Material pointLightReceiverMaterial;
        private Material fixturePoleMaterial;
        private Material fixtureLitFlameMaterial;
        private Material fixtureEmissiveFlameMaterial;
        private Mesh hexOverlayMesh;
        private Mesh diskOverlayMesh;
        private readonly HashSet<string> activeRendererKeys = new HashSet<string>();
        private MaterialPropertyBlock rendererColorBlock;
        private int nextMapLightSampleInstanceId;
        private AtlasTilePresentationView cachedTilePresentationView;

        private void OnEnable()
        {
            if (!ActivePanels.Contains(this))
            {
                ActivePanels.Add(this);
            }
        }

        private void Awake()
        {
            rendererColorBlock ??= new MaterialPropertyBlock();
            EnsureDefaultPresets();
            ValidateRequiredReferences();
            CaptureCameraSettings();
        }

        private void Start()
        {
            if (!applyInitialPresetsOnStart)
            {
                return;
            }

            ApplyCameraPreset(Mathf.Clamp(initialCameraPresetIndex, 0, cameraPresets.Length - 1));
            if (applyInitialLightingPresetOnStart)
            {
                ApplyLightingPreset(Mathf.Clamp(initialLightingPresetIndex, 0, lightingPresets.Length - 1));
            }
        }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame)
            {
                showPanel = !showPanel;
            }
#else
            if (Input.GetKeyDown(KeyCode.F9))
            {
                showPanel = !showPanel;
            }
#endif
            UpdateCameraDrag();
            SyncTorchFieldObjectLights();
            SyncMapLightCompareRenderers();
        }

        private void OnDisable()
        {
            ActivePanels.Remove(this);
            ClearMapLightSamples();
            ClearTorchFieldObjectLights();
        }

        private void OnDestroy()
        {
            ActivePanels.Remove(this);
            ClearMapLightSamples();
            ClearTorchFieldObjectLights();
            DestroyRuntimeCompareResources();
        }

        private void OnGUI()
        {
            if (!showStandaloneWindow)
            {
                return;
            }

            if (!showPanel)
            {
                var oldColor = GUI.color;
                GUI.color = Color.white;
                if (GUI.Button(new Rect(16f, 16f, 180f, 28f), "Show Camera/Light (F9)"))
                {
                    showPanel = true;
                }

                GUI.Label(new Rect(16f, 46f, 260f, 24f), "Camera/Light panel hidden");
                GUI.color = oldColor;
                return;
            }

            EnsurePanelFitsRightSide();
            panelRect = GUILayout.Window(GetInstanceID(), panelRect, DrawPanel, "Debug Control Window - Camera / Light / Trap");
        }

        private void EnsurePanelFitsRightSide()
        {
            var targetX = Mathf.Max(16f, Screen.width - PanelWidth - 16f);
            if (panelRect.x < 32f || panelRect.xMax > Screen.width + 1f)
            {
                panelRect.x = targetX;
            }

            panelRect.y = Mathf.Clamp(panelRect.y, 16f, Mathf.Max(16f, Screen.height - 64f));
            panelRect.width = PanelWidth;
            panelRect.height = PanelHeight;
        }

        private void DrawPanel(int windowId)
        {
            EnsureDefaultPresets();
            ValidateRequiredReferences();
            CaptureCameraSettings();

            panelScrollPosition = GUILayout.BeginScrollView(panelScrollPosition, false, true);

            GUILayout.Label("Unified dev-only control window. Camera/light tuning plus trap/status debug movement.");
            if (GUILayout.Button("Hide panel (F9)", GUILayout.Height(ButtonHeight)))
            {
                showPanel = false;
                GUI.DragWindow(new Rect(0f, 0f, PanelWidth, 24f));
                return;
            }

            GUILayout.Space(6f);

            GUILayout.Label("Camera presets");
            for (var i = 0; i < cameraPresets.Length; i++)
            {
                var label = activeCameraPresetIndex == i ? $"● {cameraPresets[i].Name}" : cameraPresets[i].Name;
                if (GUILayout.Button(label, GUILayout.Height(ButtonHeight)))
                {
                    ApplyCameraPreset(i);
                }
            }

            GUILayout.Space(8f);
            GUILayout.Label("Camera adjustments");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Zoom -", GUILayout.Height(ButtonHeight)))
            {
                ApplyCamera(currentPlayerOffset, currentEulerAngles, currentOrthographicSize + zoomStep, currentKeepCentered, -1);
            }

            if (GUILayout.Button("Zoom +", GUILayout.Height(ButtonHeight)))
            {
                ApplyCamera(currentPlayerOffset, currentEulerAngles, currentOrthographicSize - zoomStep, currentKeepCentered, -1);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Pitch -", GUILayout.Height(ButtonHeight)))
            {
                ApplyCamera(currentPlayerOffset, currentEulerAngles + new Vector3(-angleStep, 0f, 0f), currentOrthographicSize, currentKeepCentered, -1);
            }

            if (GUILayout.Button("Pitch +", GUILayout.Height(ButtonHeight)))
            {
                ApplyCamera(currentPlayerOffset, currentEulerAngles + new Vector3(angleStep, 0f, 0f), currentOrthographicSize, currentKeepCentered, -1);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Yaw -", GUILayout.Height(ButtonHeight)))
            {
                ApplyCamera(currentPlayerOffset, currentEulerAngles + new Vector3(0f, -angleStep, 0f), currentOrthographicSize, currentKeepCentered, -1);
            }

            if (GUILayout.Button("Yaw +", GUILayout.Height(ButtonHeight)))
            {
                ApplyCamera(currentPlayerOffset, currentEulerAngles + new Vector3(0f, angleStep, 0f), currentOrthographicSize, currentKeepCentered, -1);
            }
            GUILayout.EndHorizontal();

            var keepCentered = GUILayout.Toggle(currentKeepCentered, "Follow player / recenter on player");
            if (keepCentered != currentKeepCentered)
            {
                ApplyCamera(currentPlayerOffset, currentEulerAngles, currentOrthographicSize, keepCentered, -1);
            }

            var dragLabel = dragCameraMode ? "● Drag Camera: ON" : "Drag Camera: OFF";
            if (GUILayout.Button(dragLabel, GUILayout.Height(ButtonHeight)))
            {
                SetDragCameraMode(!dragCameraMode);
            }
            GUILayout.Label("Drag mode: hold left mouse and drag outside this panel to pan. Turning it off recenters on the player.");

            GUILayout.Space(8f);
            GUILayout.Label("Fog visibility");
            var fogLabel = controller != null && controller.IsFogDebugVisible
                ? "Fog ON (normal visibility)"
                : "Fog OFF (reveal all)";
            if (GUILayout.Button(fogLabel, GUILayout.Height(ButtonHeight)))
            {
                ToggleFogVisibility();
            }

            GUILayout.Space(8f);
            GUILayout.Label("Trap / status debug movement");
            var clickMoveDebugLabel = controller != null && controller.IsClickMoveDebugModeEnabled
                ? "● Trap Debug Move: ON (click a tile to warp)"
                : "Trap Debug Move: OFF (normal card movement)";
            if (GUILayout.Button(clickMoveDebugLabel, GUILayout.Height(ButtonHeight)))
            {
                ToggleClickMoveDebugMode();
            }

            var previousGuiEnabled = GUI.enabled;
            GUI.enabled = previousGuiEnabled && controller != null && controller.IsClickMoveDebugModeEnabled && controller.HasClickMoveDebugSelection;
            var selectedLabel = controller != null && controller.HasClickMoveDebugSelection
                ? $"Debug Move Selected {controller.ClickMoveDebugSelectedCoord}"
                : "Debug Move Selected Tile";
            if (GUILayout.Button(selectedLabel, GUILayout.Height(ButtonHeight)))
            {
                ConfirmClickMoveDebugSelection();
            }
            GUI.enabled = previousGuiEnabled;

            GUILayout.Space(8f);
            GUILayout.Label("Torch card test");
            if (GUILayout.Button("Play Campfire Torch at Player", GUILayout.Height(ButtonHeight)))
            {
                PlayTorchAtPlayer();
            }

            showTorchFieldObjectLights = GUILayout.Toggle(showTorchFieldObjectLights, "Show FieldObject torch lights");
            GUILayout.Label($"Active torch lights: {ActiveTorchLightCount}");
            DrawTorchLightControls();

            GUILayout.Space(8f);
            DrawMapLightCompareControls();

            GUILayout.Space(8f);
            GUILayout.Label("Lighting presets");
            for (var i = 0; i < lightingPresets.Length; i++)
            {
                var label = activeLightingPresetIndex == i ? $"● {lightingPresets[i].Name}" : lightingPresets[i].Name;
                if (GUILayout.Button(label, GUILayout.Height(ButtonHeight)))
                {
                    ApplyLightingPreset(i);
                }
            }
            DrawLightingFineTuneControls();

            GUILayout.Space(8f);
            GUILayout.Label("Current values");
            GUILayout.Label($"Camera preset: {FormatActiveName(cameraPresets, activeCameraPresetIndex)}");
            GUILayout.Label($"Light preset: {FormatActiveName(lightingPresets, activeLightingPresetIndex)}");
            GUILayout.Label($"Offset: {FormatVector(currentPlayerOffset)}");
            GUILayout.Label($"Euler: {FormatVector(currentEulerAngles)}");
            GUILayout.Label($"Ortho size: {currentOrthographicSize:0.00}");
            GUILayout.Label($"Follow: {currentKeepCentered}");
            GUILayout.Label($"Drag camera: {dragCameraMode}");
            if (controller != null)
            {
                GUILayout.Label($"Fog: {(controller.IsFogDebugVisible ? "ON" : "OFF")}");
            }
            GUILayout.Label($"Light samples: {ActiveMapLightSampleCount}");
            GUILayout.Label($"Sample point lights: {ActiveSamplePointLightCount}");
            GUILayout.Label($"Runtime light fixtures: {ActiveLightFixtureCount}");
            GUILayout.Label($"Emissive helpers: {ActiveEmissiveGlowCount}");
            GUILayout.Label($"Projected glows: {ActiveProjectedGlowCount}");
            if (targetCamera != null)
            {
                GUILayout.Label($"Camera position: {FormatVector(targetCamera.transform.position)}");
            }

            GUILayout.Space(4f);
            GUILayout.Label("F9: hide/show panel");
            GUILayout.EndScrollView();

            panelRect.height = PanelHeight;
            GUI.DragWindow(new Rect(0f, 0f, PanelWidth, 24f));
        }


        public void DrawCameraDebugPage()
        {
            EnsureDefaultPresets();
            CaptureCameraSettings();

            GUILayout.Label("Camera presets");
            for (var i = 0; i < cameraPresets.Length; i++)
            {
                var label = activeCameraPresetIndex == i ? $"● {cameraPresets[i].Name}" : cameraPresets[i].Name;
                if (GUILayout.Button(label, GUILayout.Height(ButtonHeight)))
                {
                    ApplyCameraPreset(i);
                }
            }

            GUILayout.Space(8f);
            GUILayout.Label("Camera adjustments");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Zoom -", GUILayout.Height(ButtonHeight)))
            {
                ApplyCamera(currentPlayerOffset, currentEulerAngles, currentOrthographicSize + zoomStep, currentKeepCentered, -1);
            }

            if (GUILayout.Button("Zoom +", GUILayout.Height(ButtonHeight)))
            {
                ApplyCamera(currentPlayerOffset, currentEulerAngles, currentOrthographicSize - zoomStep, currentKeepCentered, -1);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Pitch -", GUILayout.Height(ButtonHeight)))
            {
                ApplyCamera(currentPlayerOffset, currentEulerAngles + new Vector3(-angleStep, 0f, 0f), currentOrthographicSize, currentKeepCentered, -1);
            }

            if (GUILayout.Button("Pitch +", GUILayout.Height(ButtonHeight)))
            {
                ApplyCamera(currentPlayerOffset, currentEulerAngles + new Vector3(angleStep, 0f, 0f), currentOrthographicSize, currentKeepCentered, -1);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Yaw -", GUILayout.Height(ButtonHeight)))
            {
                ApplyCamera(currentPlayerOffset, currentEulerAngles + new Vector3(0f, -angleStep, 0f), currentOrthographicSize, currentKeepCentered, -1);
            }

            if (GUILayout.Button("Yaw +", GUILayout.Height(ButtonHeight)))
            {
                ApplyCamera(currentPlayerOffset, currentEulerAngles + new Vector3(0f, angleStep, 0f), currentOrthographicSize, currentKeepCentered, -1);
            }
            GUILayout.EndHorizontal();

            var keepCentered = GUILayout.Toggle(currentKeepCentered, "Follow player / recenter on player");
            if (keepCentered != currentKeepCentered)
            {
                ApplyCamera(currentPlayerOffset, currentEulerAngles, currentOrthographicSize, keepCentered, -1);
            }

            var dragLabel = dragCameraMode ? "● Drag Camera: ON" : "Drag Camera: OFF";
            if (GUILayout.Button(dragLabel, GUILayout.Height(ButtonHeight)))
            {
                SetDragCameraMode(!dragCameraMode);
            }

            GUILayout.Label("Drag mode pans only when starting outside the Debug Control Window.");
            DrawCurrentCameraLightValues(includeLighting: false);
        }

        public void DrawLightingDebugPage()
        {
            EnsureDefaultPresets();
            CaptureCameraSettings();

            GUILayout.Label("Lighting presets");
            for (var i = 0; i < lightingPresets.Length; i++)
            {
                var label = activeLightingPresetIndex == i ? $"● {lightingPresets[i].Name}" : lightingPresets[i].Name;
                if (GUILayout.Button(label, GUILayout.Height(ButtonHeight)))
                {
                    ApplyLightingPreset(i);
                }
            }

            DrawLightingFineTuneControls();
            DrawCurrentCameraLightValues(includeLighting: true);
        }

        public void DrawLightSamplesDebugPage()
        {
            EnsureDefaultPresets();
            CaptureCameraSettings();

            GUILayout.Label("Torch card test");
            if (GUILayout.Button("Play Campfire Torch at Player", GUILayout.Height(ButtonHeight)))
            {
                PlayTorchAtPlayer();
            }

            showTorchFieldObjectLights = GUILayout.Toggle(showTorchFieldObjectLights, "Show FieldObject torch lights");
            GUILayout.Label($"Active torch lights: {ActiveTorchLightCount}");
            DrawTorchLightControls();

            GUILayout.Space(8f);
            DrawMapLightCompareControls();
        }

        private void DrawCurrentCameraLightValues(bool includeLighting)
        {
            GUILayout.Space(8f);
            GUILayout.Label("Current values");
            GUILayout.Label($"Camera preset: {FormatActiveName(cameraPresets, activeCameraPresetIndex)}");
            GUILayout.Label($"Offset: {FormatVector(currentPlayerOffset)}");
            GUILayout.Label($"Euler: {FormatVector(currentEulerAngles)}");
            GUILayout.Label($"Ortho size: {currentOrthographicSize:0.00}");
            GUILayout.Label($"Follow: {currentKeepCentered}");
            GUILayout.Label($"Drag camera: {dragCameraMode}");
            if (includeLighting)
            {
                GUILayout.Label($"Light preset: {FormatActiveName(lightingPresets, activeLightingPresetIndex)}");
                GUILayout.Label($"Light samples: {ActiveMapLightSampleCount}");
                GUILayout.Label($"Sample point lights: {ActiveSamplePointLightCount}");
                GUILayout.Label($"Runtime light fixtures: {ActiveLightFixtureCount}");
                GUILayout.Label($"Emissive helpers: {ActiveEmissiveGlowCount}");
                GUILayout.Label($"Projected glows: {ActiveProjectedGlowCount}");
            }

            if (targetCamera != null)
            {
                GUILayout.Label($"Camera position: {FormatVector(targetCamera.transform.position)}");
            }
        }

        private void DrawTorchLightControls()
        {
            GUILayout.Label("Torch light tuning");
            torchLightIntensity = DrawFloatSlider("Intensity", torchLightIntensity, 0f, 6f);
            torchLightRangePerHex = DrawFloatSlider("Range / hex", torchLightRangePerHex, 0.5f, 5f);
            torchLightHeight = DrawFloatSlider("Height", torchLightHeight, 0f, 3f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Warm", GUILayout.Height(ButtonHeight)))
            {
                torchLightColor = new Color(1f, 0.58f, 0.24f, 1f);
            }

            if (GUILayout.Button("Soft", GUILayout.Height(ButtonHeight)))
            {
                torchLightColor = new Color(1f, 0.72f, 0.42f, 1f);
            }

            if (GUILayout.Button("Pale", GUILayout.Height(ButtonHeight)))
            {
                torchLightColor = new Color(1f, 0.86f, 0.62f, 1f);
            }
            GUILayout.EndHorizontal();

            torchLightColor = DrawColorSliders("Torch RGB", torchLightColor);
        }

        private void DrawMapLightCompareControls()
        {
            GUILayout.Label("Visual Light Compare");
            EnsureDefaultPresets();

            GUILayout.Label($"Sample: {GetSelectedMapLightSampleName()}");
            selectedMapLightSampleIndex = GUILayout.SelectionGrid(
                Mathf.Clamp(selectedMapLightSampleIndex, 0, mapLightSamples.Length - 1),
                GetMapLightSampleNames(),
                1);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Selected at Player", GUILayout.Height(ButtonHeight)))
            {
                SpawnSelectedLightSampleAtPlayer();
            }

            if (GUILayout.Button("Add Selected Near Player", GUILayout.Height(ButtonHeight)))
            {
                SpawnSelectedLightSampleNearPlayer();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Street Lamp", GUILayout.Height(ButtonHeight)))
            {
                SpawnFirstLightSampleOfKindNearPlayer(MapLightTestKind.StreetLamp);
            }

            if (GUILayout.Button("Add Window", GUILayout.Height(ButtonHeight)))
            {
                SpawnFirstLightSampleOfKindNearPlayer(MapLightTestKind.BuildingWindow);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Blue Neon", GUILayout.Height(ButtonHeight)))
            {
                SpawnFirstLightSampleOfKindNearPlayer(MapLightTestKind.Neon);
            }

            if (GUILayout.Button("Add Campfire", GUILayout.Height(ButtonHeight)))
            {
                SpawnFirstLightSampleOfKindNearPlayer(MapLightTestKind.Campfire);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Spawn 3-Way Visual Compare", GUILayout.Height(ButtonHeight)))
            {
                SpawnThreeWayVisualLightCompare();
            }

            if (GUILayout.Button("Clear Light Samples", GUILayout.Height(ButtonHeight)))
            {
                ClearMapLightSamples();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Spawn Mixed Light Samples", GUILayout.Height(ButtonHeight)))
            {
                SpawnMixedLightSamples();
            }

            if (GUILayout.Button("Add All at Player", GUILayout.Height(ButtonHeight)))
            {
                SpawnSelectedLightSampleAtPlayer();
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("3-way layout: left=3D Light, center=Emissive, right=Projected Glow.");

            GUILayout.Label($"Compare mode: {mapLightCompareMode}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(mapLightCompareMode == MapLightCompareMode.All ? "● All" : "All", GUILayout.Height(ButtonHeight)))
            {
                SetMapLightCompareMode(MapLightCompareMode.All);
            }

            if (GUILayout.Button(mapLightCompareMode == MapLightCompareMode.EmissiveGlowOnly ? "● Emissive" : "Emissive", GUILayout.Height(ButtonHeight)))
            {
                SetMapLightCompareMode(MapLightCompareMode.EmissiveGlowOnly);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(mapLightCompareMode == MapLightCompareMode.PointLightsOnly ? "● 3D Light" : "3D Light", GUILayout.Height(ButtonHeight)))
            {
                SetMapLightCompareMode(MapLightCompareMode.PointLightsOnly);
            }

            if (GUILayout.Button(mapLightCompareMode == MapLightCompareMode.ProjectedGlowOnly ? "● Projected" : "Projected", GUILayout.Height(ButtonHeight)))
            {
                SetMapLightCompareMode(MapLightCompareMode.ProjectedGlowOnly);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Exaggerate Compare", GUILayout.Height(ButtonHeight)))
            {
                ApplyExaggeratedLightCompareValues();
            }

            if (GUILayout.Button("Balanced Compare", GUILayout.Height(ButtonHeight)))
            {
                ApplyBalancedLightCompareValues();
            }
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Night Render Preview", GUILayout.Height(ButtonHeight)))
            {
                ApplyNightRenderPreview();
            }

            showTileOverlayLighting = DrawMapLightToggle(showTileOverlayLighting, "Emissive / Glow Helpers", SyncTileOverlayLighting, ClearTileOverlayLighting);
            showSamplePointLights = DrawMapLightToggle(showSamplePointLights, "Unity 3D Point Light", SyncSamplePointLights, ClearSamplePointLights);
            showLightMaskPrototype = DrawMapLightToggle(showLightMaskPrototype, "Projected Additive Glow", SyncLightMaskPrototype, ClearLightMaskPrototype);

            GUILayout.Label($"Active samples: {ActiveMapLightSampleCount}");
            GUILayout.Label("Emissive/Projected are visual-only glow helpers; gameplay visibility stays in its separate system.");
            GUILayout.Label("Samples create simple runtime torch fixtures so lights/glow can be judged on an object without changing production tiles.");
            globalSampleIntensity = DrawFloatSlider("Global intensity", globalSampleIntensity, 0f, 2f);
            globalSampleRadiusScale = DrawFloatSlider("Global radius", globalSampleRadiusScale, 0.25f, 2f);
            samplePointLightRangePerHex = DrawFloatSlider("Point range / hex", samplePointLightRangePerHex, 0.5f, 5f);
            maskDarkness = DrawFloatSlider("Projected falloff", maskDarkness, 0f, 1f);
            maskIntensity = DrawFloatSlider("Projected intensity", maskIntensity, 0f, 2f);
            maskRadiusScale = DrawFloatSlider("Projected radius", maskRadiusScale, 0.25f, 2.5f);
        }

        private bool DrawMapLightToggle(bool currentValue, string label, Action syncWhenEnabled, Action clearWhenDisabled)
        {
            var nextValue = GUILayout.Toggle(currentValue, label);
            if (nextValue == currentValue)
            {
                return currentValue;
            }

            if (nextValue)
            {
                syncWhenEnabled?.Invoke();
            }
            else
            {
                clearWhenDisabled?.Invoke();
            }

            return nextValue;
        }

        private void DrawLightingFineTuneControls()
        {
            GUILayout.Space(6f);
            GUILayout.Label("Night / scene light tuning");

            var ambient = DrawColorSliders("Ambient RGB", RenderSettings.ambientLight);
            if (ambient != RenderSettings.ambientLight)
            {
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = ambient;
                activeLightingPresetIndex = -1;
            }

            if (targetCamera != null)
            {
                var background = DrawColorSliders("Bg RGB", targetCamera.backgroundColor);
                if (background != targetCamera.backgroundColor)
                {
                    targetCamera.backgroundColor = background;
                    activeLightingPresetIndex = -1;
                }
            }

            if (directionalLight != null)
            {
                var intensity = DrawFloatSlider("Directional", directionalLight.intensity, 0f, 2f);
                if (!Mathf.Approximately(intensity, directionalLight.intensity))
                {
                    directionalLight.intensity = intensity;
                    activeLightingPresetIndex = -1;
                }
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Night +", GUILayout.Height(ButtonHeight)))
            {
                BrightenCurrentLighting(0.08f);
            }

            if (GUILayout.Button("Night -", GUILayout.Height(ButtonHeight)))
            {
                DarkenCurrentLighting(0.08f);
            }
            GUILayout.EndHorizontal();
        }

        private void ApplyCameraPreset(int index)
        {
            if (cameraPresets == null || cameraPresets.Length == 0)
            {
                return;
            }

            index = Mathf.Clamp(index, 0, cameraPresets.Length - 1);
            var preset = cameraPresets[index];
            ApplyCamera(preset.PlayerOffset, preset.EulerAngles, preset.OrthographicSize, preset.KeepCenteredOnPlayer, index);
        }

        private void ApplyCamera(Vector3 playerOffset, Vector3 eulerAngles, float orthographicSize, bool keepCentered, int presetIndex)
        {
            currentPlayerOffset = playerOffset;
            currentEulerAngles = ClampEuler(eulerAngles);
            currentKeepCentered = keepCentered;

            var minSize = controller != null ? controller.MinCameraOrthographicSize : 1f;
            var maxSize = controller != null ? controller.MaxCameraOrthographicSize : 24f;
            currentOrthographicSize = Mathf.Clamp(orthographicSize, minSize, Mathf.Max(minSize, maxSize));
            activeCameraPresetIndex = presetIndex;

            if (controller != null)
            {
                controller.ApplyGameplayCameraSettingsForDev(
                    currentPlayerOffset,
                    currentEulerAngles,
                    currentOrthographicSize,
                    currentKeepCentered);
                return;
            }

            if (targetCamera == null)
            {
                return;
            }

            targetCamera.transform.rotation = Quaternion.Euler(currentEulerAngles);
            if (targetCamera.orthographic)
            {
                targetCamera.nearClipPlane = -50f;
                targetCamera.farClipPlane = 200f;
                targetCamera.orthographicSize = currentOrthographicSize;
            }
        }

        private void SetDragCameraMode(bool enabled)
        {
            dragCameraMode = enabled;
            isDraggingCamera = false;
            ApplyCamera(currentPlayerOffset, currentEulerAngles, currentOrthographicSize, !enabled, -1);
        }

        private void ToggleFogVisibility()
        {
            if (controller == null)
            {
                return;
            }

            controller.SetFogDebugVisible(!controller.IsFogDebugVisible);
        }

        private void ToggleClickMoveDebugMode()
        {
            controller?.ToggleClickMoveDebugMode();
        }

        private void ConfirmClickMoveDebugSelection()
        {
            controller?.ConfirmClickMoveDebugSelection();
        }

        private void PlayTorchAtPlayer()
        {
            if (controller == null)
            {
                return;
            }

            controller.PlayTorchAtPlayerForDev();
            SyncTorchFieldObjectLights();
        }

        private void UpdateCameraDrag()
        {
            if (!dragCameraMode || targetCamera == null)
            {
                isDraggingCamera = false;
                return;
            }

            var mousePosition = GetMousePosition();
            if (GetMouseButtonDown())
            {
                if (IsPointerOverPanel(mousePosition) || CombatDebugControlPanel.IsPointerOverAnyGameViewPanel(mousePosition))
                {
                    isDraggingCamera = false;
                    return;
                }

                isDraggingCamera = true;
                lastDragMousePosition = mousePosition;
            }

            if (!GetMouseButtonHeld())
            {
                isDraggingCamera = false;
                return;
            }

            if (!isDraggingCamera)
            {
                return;
            }

            var delta = mousePosition - lastDragMousePosition;
            lastDragMousePosition = mousePosition;
            PanCamera(delta);
        }

        private void PanCamera(Vector2 screenDelta)
        {
            if (screenDelta == Vector2.zero || targetCamera == null)
            {
                return;
            }

            var viewportHeight = Mathf.Max(1, Screen.height);
            var unitsPerPixel = targetCamera.orthographic
                ? targetCamera.orthographicSize * 2f / viewportHeight
                : Mathf.Max(0.01f, Vector3.Distance(targetCamera.transform.position, Vector3.zero)) / viewportHeight;
            var movement = (-screenDelta.x * targetCamera.transform.right - screenDelta.y * targetCamera.transform.up)
                * unitsPerPixel
                * Mathf.Max(0.01f, dragPanSpeed);
            targetCamera.transform.position += movement;
        }


        public static bool IsPointerOverAnyVisiblePanel(Vector2 mousePosition)
        {
            for (var i = ActivePanels.Count - 1; i >= 0; i--)
            {
                var panel = ActivePanels[i];
                if (panel == null)
                {
                    ActivePanels.RemoveAt(i);
                    continue;
                }

                if (panel.IsPointerOverPanel(mousePosition))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsPointerOverPanel(Vector2 mousePosition)
        {
            if (!showStandaloneWindow || !showPanel)
            {
                return false;
            }

            return panelRect.Contains(new Vector2(mousePosition.x, Screen.height - mousePosition.y));
        }

        private static Vector2 GetMousePosition()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
            return Input.mousePosition;
#endif
        }

        private static bool GetMouseButtonDown()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(0);
#endif
        }

        private static bool GetMouseButtonHeld()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.isPressed;
#else
            return Input.GetMouseButton(0);
#endif
        }

        private void ApplyLightingPreset(int index)
        {
            if (lightingPresets == null || lightingPresets.Length == 0)
            {
                return;
            }

            index = Mathf.Clamp(index, 0, lightingPresets.Length - 1);
            var preset = lightingPresets[index];
            activeLightingPresetIndex = index;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = preset.AmbientColor;
            if (targetCamera != null)
            {
                targetCamera.backgroundColor = preset.CameraBackgroundColor;
            }

            if (directionalLight == null)
            {
                return;
            }

            directionalLight.type = LightType.Directional;
            directionalLight.color = preset.DirectionalLightColor;
            directionalLight.intensity = Mathf.Max(0f, preset.DirectionalLightIntensity);
            directionalLight.transform.rotation = Quaternion.Euler(preset.DirectionalLightEulerAngles);
        }

        private void BrightenCurrentLighting(float amount)
        {
            AdjustCurrentLighting(Mathf.Abs(amount));
        }

        private void DarkenCurrentLighting(float amount)
        {
            AdjustCurrentLighting(-Mathf.Abs(amount));
        }

        private void AdjustCurrentLighting(float amount)
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = AdjustColor(RenderSettings.ambientLight, amount);
            if (targetCamera != null)
            {
                targetCamera.backgroundColor = AdjustColor(targetCamera.backgroundColor, amount * 0.65f);
            }

            if (directionalLight != null)
            {
                directionalLight.intensity = Mathf.Clamp(directionalLight.intensity + amount * 2.5f, 0f, 2f);
            }

            activeLightingPresetIndex = -1;
        }

        public int ActiveMapLightSampleCount => activeMapLightSamples.Count;
        public int ActiveSamplePointLightCount => samplePointLightsByKey.Count;
        public int ActiveTileOverlayCount => tileOverlayObjectsByKey.Count;
        public int ActiveEmissiveGlowCount => tileOverlayObjectsByKey.Count;
        public int ActivePointLightReceiverCount => pointLightReceiverObjectsByKey.Count;
        public int ActiveLightFixtureCount => lightFixtureObjectsByKey.Count;
        public int ActiveLightMaskCount => lightMaskObjectsByKey.Count;
        public int ActiveProjectedGlowCount => lightMaskObjectsByKey.Count;
        public int RuntimeCompareResourceCount => CountRuntimeCompareResources();

        private void SpawnMixedLightSamples()
        {
            EnsureDefaultPresets();
            activeMapLightSamples.Clear();
            var anchor = controller?.State?.PlayerCoord ?? new HexCoord(0, 0);
            foreach (var sample in mapLightSamples)
            {
                var coord = ResolveLightSampleSpawnCoord(anchor + sample.RelativeCoord, anchor);
                if (controller != null && !controller.TryGetTileWorldPosition(coord, out _))
                {
                    continue;
                }

                SpawnMapLightSample(sample, coord, syncAfterSpawn: false);
            }

            SyncMapLightCompareRenderers();
        }

        private void SpawnSelectedLightSampleAtPlayer()
        {
            EnsureDefaultPresets();
            SpawnMapLightSample(GetSelectedMapLightSample(), controller?.State?.PlayerCoord ?? new HexCoord(0, 0));
        }

        private void SpawnSelectedLightSampleNearPlayer()
        {
            EnsureDefaultPresets();
            var sample = GetSelectedMapLightSample();
            var anchor = controller?.State?.PlayerCoord ?? new HexCoord(0, 0);
            SpawnMapLightSample(sample, ResolveLightSampleSpawnCoord(anchor + sample.RelativeCoord, anchor));
        }

        private void SpawnFirstLightSampleOfKindNearPlayer(MapLightTestKind kind)
        {
            EnsureDefaultPresets();
            for (var i = 0; i < mapLightSamples.Length; i++)
            {
                if (mapLightSamples[i].Kind != kind)
                {
                    continue;
                }

                selectedMapLightSampleIndex = i;
                SpawnSelectedLightSampleNearPlayer();
                return;
            }
        }

        private void SpawnThreeWayVisualLightCompare()
        {
            EnsureDefaultPresets();
            activeMapLightSamples.Clear();
            var sample = GetSelectedMapLightSample();
            var anchor = controller?.State?.PlayerCoord ?? new HexCoord(0, 0);

            SpawnMapLightSample(sample, ResolveLightSampleSpawnCoord(anchor + new HexCoord(-2, 0), anchor), VisualLightApproach.Unity3DLight, syncAfterSpawn: false);
            SpawnMapLightSample(sample, ResolveLightSampleSpawnCoord(anchor, anchor), VisualLightApproach.EmissiveGlow, syncAfterSpawn: false);
            SpawnMapLightSample(sample, ResolveLightSampleSpawnCoord(anchor + new HexCoord(2, 0), anchor), VisualLightApproach.ProjectedGlow, syncAfterSpawn: false);
            SetMapLightCompareMode(MapLightCompareMode.All);
        }

        private void SpawnMapLightSample(MapLightTestObject sample, HexCoord coord, bool syncAfterSpawn = true)
        {
            SpawnMapLightSample(sample, coord, VisualLightApproach.All, syncAfterSpawn);
        }

        private void SpawnMapLightSample(MapLightTestObject sample, HexCoord coord, VisualLightApproach approach, bool syncAfterSpawn = true)
        {
            if (controller != null && !controller.TryGetTileWorldPosition(coord, out _))
            {
                return;
            }

            activeMapLightSamples.Add(new ActiveMapLightSample(++nextMapLightSampleInstanceId, sample, coord, approach));
            if (syncAfterSpawn)
            {
                SyncMapLightCompareRenderers();
            }
        }

        private HexCoord ResolveLightSampleSpawnCoord(HexCoord preferredCoord, HexCoord fallbackCoord)
        {
            if (controller == null || controller.TryGetTileWorldPosition(preferredCoord, out _))
            {
                return preferredCoord;
            }

            if (controller.TryGetTileWorldPosition(fallbackCoord, out _))
            {
                return fallbackCoord;
            }

            foreach (var coord in EnumerateHexDisk(fallbackCoord, 3))
            {
                if (controller.TryGetTileWorldPosition(coord, out _))
                {
                    return coord;
                }
            }

            return preferredCoord;
        }


        private bool TryGetLightCompareSurfacePosition(HexCoord coord, out Vector3 position)
        {
            var tileView = GetTilePresentationView();
            if (tileView != null)
            {
                position = tileView.transform.TransformPoint(tileView.ProjectOverlaySurface(coord));
                return true;
            }

            if (controller != null && controller.TryGetTileWorldPosition(coord, out position))
            {
                return true;
            }

            position = default;
            return false;
        }

        private AtlasTilePresentationView GetTilePresentationView()
        {
            if (cachedTilePresentationView != null)
            {
                return cachedTilePresentationView;
            }

            cachedTilePresentationView = FindFirstObjectByType<AtlasTilePresentationView>();
            return cachedTilePresentationView;
        }

        private void SetSelectedMapLightSampleIndex(int index)
        {
            EnsureDefaultPresets();
            selectedMapLightSampleIndex = Mathf.Clamp(index, 0, mapLightSamples.Length - 1);
        }

        private MapLightTestObject GetSelectedMapLightSample()
        {
            EnsureDefaultPresets();
            selectedMapLightSampleIndex = Mathf.Clamp(selectedMapLightSampleIndex, 0, mapLightSamples.Length - 1);
            return mapLightSamples[selectedMapLightSampleIndex];
        }

        private string GetSelectedMapLightSampleName()
        {
            return GetSelectedMapLightSample().Name;
        }

        private string[] GetMapLightSampleNames()
        {
            EnsureDefaultPresets();
            var names = new string[mapLightSamples.Length];
            for (var i = 0; i < mapLightSamples.Length; i++)
            {
                names[i] = string.IsNullOrWhiteSpace(mapLightSamples[i].Name) ? $"Sample {i + 1}" : mapLightSamples[i].Name;
            }

            return names;
        }

        private void ClearMapLightSamples()
        {
            activeMapLightSamples.Clear();
            ClearSamplePointLights();
            ClearLightFixtureObjects();
            ClearTileOverlayLighting();
            ClearLightMaskPrototype();
        }


        private void SetMapLightCompareMode(MapLightCompareMode mode)
        {
            mapLightCompareMode = mode;
            showTileOverlayLighting = mode == MapLightCompareMode.All || mode == MapLightCompareMode.EmissiveGlowOnly;
            showSamplePointLights = mode == MapLightCompareMode.All || mode == MapLightCompareMode.PointLightsOnly;
            showLightMaskPrototype = mode == MapLightCompareMode.All || mode == MapLightCompareMode.ProjectedGlowOnly;
            SyncMapLightCompareRenderers();
        }

        private void ApplyExaggeratedLightCompareValues()
        {
            globalSampleIntensity = 1.65f;
            globalSampleRadiusScale = 1.25f;
            samplePointLightRangePerHex = 3.2f;
            maskDarkness = 0.12f;
            maskIntensity = 1.75f;
            maskRadiusScale = 1.3f;
            SyncMapLightCompareRenderers();
        }

        private void ApplyBalancedLightCompareValues()
        {
            globalSampleIntensity = 1f;
            globalSampleRadiusScale = 1f;
            samplePointLightRangePerHex = 2.4f;
            maskDarkness = 0.35f;
            maskIntensity = 1.15f;
            maskRadiusScale = 1f;
            SyncMapLightCompareRenderers();
        }

        private void ApplyNightRenderPreview()
        {
            ApplyLightingPreset(Mathf.Min(4, lightingPresets.Length - 1));
            ApplyExaggeratedLightCompareValues();
            if (activeMapLightSamples.Count == 0)
            {
                SpawnSelectedLightSampleAtPlayer();
            }

            SetMapLightCompareMode(MapLightCompareMode.PointLightsOnly);
        }

        private void SetSamplePointLightsVisible(bool visible)
        {
            showSamplePointLights = visible;
            if (visible)
            {
                SyncSamplePointLights();
            }
            else
            {
                ClearSamplePointLights();
            }
        }

        private void SetTileOverlayLightingVisible(bool visible)
        {
            showTileOverlayLighting = visible;
            if (visible)
            {
                SyncTileOverlayLighting();
            }
            else
            {
                ClearTileOverlayLighting();
            }
        }

        private void SetLightMaskPrototypeVisible(bool visible)
        {
            showLightMaskPrototype = visible;
            if (visible)
            {
                SyncLightMaskPrototype();
            }
            else
            {
                ClearLightMaskPrototype();
            }
        }

        private void SyncMapLightCompareRenderers()
        {
            SyncLightFixtureObjects();

            if (showSamplePointLights)
            {
                SyncSamplePointLights();
            }
            else
            {
                ClearSamplePointLights();
            }

            if (showTileOverlayLighting)
            {
                SyncTileOverlayLighting();
            }
            else
            {
                ClearTileOverlayLighting();
            }

            if (showLightMaskPrototype)
            {
                SyncLightMaskPrototype();
            }
            else
            {
                ClearLightMaskPrototype();
            }
        }

        private void SyncSamplePointLights()
        {
            if (activeMapLightSamples.Count == 0 || controller == null)
            {
                ClearSamplePointLights();
                return;
            }

            activeRendererKeys.Clear();
            foreach (var activeSample in activeMapLightSamples)
            {
                if (!ShouldRenderApproach(activeSample, VisualLightApproach.Unity3DLight))
                {
                    continue;
                }

                if (!TryGetLightCompareSurfacePosition(activeSample.Coord, out var position))
                {
                    continue;
                }

                var key = CreateSampleLightKey(activeSample);
                activeRendererKeys.Add(key);
                var light = GetOrCreateSamplePointLight(key);
                var sample = activeSample.Sample;
                light.transform.position = position + Vector3.up * Mathf.Max(0f, sample.Height);
                light.color = ClampColor(sample.Color);
                light.intensity = Mathf.Max(0f, sample.Intensity * globalSampleIntensity);
                light.range = Mathf.Max(0.1f, (sample.Radius + 1) * samplePointLightRangePerHex * Mathf.Max(0.1f, globalSampleRadiusScale));
            }

            RemoveInactiveObjects(samplePointLightsByKey, activeRendererKeys, DestroyTorchLight);
            ClearPointLightReceivers();
        }

        private Light GetOrCreateSamplePointLight(string key)
        {
            if (samplePointLightsByKey.TryGetValue(key, out var light) && light != null)
            {
                return light;
            }

            var root = new GameObject($"CameraLightTest Sample Point Light {key}");
            root.transform.SetParent(transform, false);
            light = root.AddComponent<Light>();
            light.type = LightType.Point;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.bounceIntensity = 0.2f;
            samplePointLightsByKey[key] = light;
            return light;
        }

        private void SyncTileOverlayLighting()
        {
            if (activeMapLightSamples.Count == 0 || controller == null)
            {
                ClearTileOverlayLighting();
                return;
            }

            activeRendererKeys.Clear();
            foreach (var activeSample in activeMapLightSamples)
            {
                if (!ShouldRenderApproach(activeSample, VisualLightApproach.EmissiveGlow))
                {
                    continue;
                }

                if (!TryGetLightCompareSurfacePosition(activeSample.Coord, out var position))
                {
                    continue;
                }

                var key = CreateSampleLightKey(activeSample);
                activeRendererKeys.Add(key);
                var sample = activeSample.Sample;
                var glow = GetOrCreateOverlayObject(tileOverlayObjectsByKey, key, "CameraLightTest Emissive Glow Helper", GetDiskOverlayMesh(), GetTileOverlayMaterial());
                var radius = Mathf.Max(0.35f, 0.34f + sample.Radius * 0.12f);
                glow.transform.position = position + Vector3.up * Mathf.Max(0.18f, sample.Height * 0.62f);
                glow.transform.rotation = Quaternion.identity;
                glow.transform.localScale = new Vector3(radius, radius, radius);
                SetRendererColor(glow, CreateOverlayColor(sample.Color, sample.Intensity * globalSampleIntensity, 1f));
            }

            RemoveInactiveObjects(tileOverlayObjectsByKey, activeRendererKeys, DestroyOverlayObject);
        }

        private void SyncLightFixtureObjects()
        {
            if (activeMapLightSamples.Count == 0 || controller == null)
            {
                ClearLightFixtureObjects();
                return;
            }

            activeRendererKeys.Clear();
            foreach (var activeSample in activeMapLightSamples)
            {
                if (!TryGetLightCompareSurfacePosition(activeSample.Coord, out var position))
                {
                    continue;
                }

                var key = CreateSampleLightKey(activeSample);
                activeRendererKeys.Add(key);
                var fixture = GetOrCreateLightFixtureObject(key);
                var sample = activeSample.Sample;
                fixture.transform.position = position;
                fixture.transform.rotation = Quaternion.identity;

                var pole = fixture.transform.Find("Pole")?.GetComponent<MeshRenderer>();
                if (pole != null)
                {
                    pole.sharedMaterial = GetFixturePoleMaterial();
                }

                var flame = fixture.transform.Find("Flame")?.GetComponent<MeshRenderer>();
                if (flame != null)
                {
                    var useEmission = ShouldRenderApproach(activeSample, VisualLightApproach.EmissiveGlow);
                    flame.sharedMaterial = useEmission ? GetFixtureEmissiveFlameMaterial() : GetFixtureLitFlameMaterial();
                    var color = CreateOverlayColor(sample.Color, Mathf.Max(0.25f, sample.Intensity * globalSampleIntensity), 1f);
                    SetRendererColor(flame, useEmission ? color : new Color(color.r, color.g, color.b, 1f));
                }
            }

            RemoveInactiveObjects(lightFixtureObjectsByKey, activeRendererKeys, DestroyOverlayObject);
        }

        private void SyncLightMaskPrototype()
        {
            if (activeMapLightSamples.Count == 0 || controller == null)
            {
                ClearLightMaskPrototype();
                return;
            }

            activeRendererKeys.Clear();
            foreach (var activeSample in activeMapLightSamples)
            {
                if (!ShouldRenderApproach(activeSample, VisualLightApproach.ProjectedGlow))
                {
                    continue;
                }

                if (!TryGetLightCompareSurfacePosition(activeSample.Coord, out var position))
                {
                    continue;
                }

                var key = CreateSampleLightKey(activeSample);
                activeRendererKeys.Add(key);
                var mask = GetOrCreateOverlayObject(lightMaskObjectsByKey, key, "CameraLightTest Projected Glow", GetDiskOverlayMesh(), GetLightMaskMaterial());
                var radius = Mathf.Max(0.2f, (activeSample.Sample.Radius + 0.5f) * maskRadiusScale * Mathf.Max(0.1f, globalSampleRadiusScale));
                mask.transform.position = position + Vector3.up * LightMaskPrototypeLift;
                mask.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
                mask.transform.localScale = new Vector3(radius * 2.1f, radius * 2.1f, 1f);
                var alphaScale = Mathf.Lerp(0.25f, 1f, Mathf.Clamp01(1f - maskDarkness));
                SetRendererColor(mask, CreateOverlayColor(activeSample.Sample.Color, activeSample.Sample.Intensity * globalSampleIntensity * maskIntensity * alphaScale, 0.9f));
            }

            RemoveInactiveObjects(lightMaskObjectsByKey, activeRendererKeys, DestroyOverlayObject);
        }

        private void ClearSamplePointLights()
        {
            foreach (var light in samplePointLightsByKey.Values)
            {
                DestroyTorchLight(light);
            }
            samplePointLightsByKey.Clear();

            ClearPointLightReceivers();
        }

        private void ClearPointLightReceivers()
        {
            foreach (var receiver in pointLightReceiverObjectsByKey.Values)
            {
                DestroyOverlayObject(receiver);
            }
            pointLightReceiverObjectsByKey.Clear();
        }

        private void ClearLightFixtureObjects()
        {
            foreach (var fixture in lightFixtureObjectsByKey.Values)
            {
                DestroyOverlayObject(fixture);
            }
            lightFixtureObjectsByKey.Clear();
        }

        private void ClearTileOverlayLighting()
        {
            foreach (var overlay in tileOverlayObjectsByKey.Values)
            {
                DestroyOverlayObject(overlay);
            }
            tileOverlayObjectsByKey.Clear();
        }

        private void ClearLightMaskPrototype()
        {
            foreach (var mask in lightMaskObjectsByKey.Values)
            {
                DestroyOverlayObject(mask);
            }
            lightMaskObjectsByKey.Clear();
        }

        private GameObject GetOrCreateOverlayObject(Dictionary<string, GameObject> objectsByKey, string key, string namePrefix, Mesh mesh, Material material)
        {
            if (objectsByKey.TryGetValue(key, out var overlay) && overlay != null)
            {
                return overlay;
            }

            overlay = new GameObject($"{namePrefix} {key}");
            overlay.transform.SetParent(transform, false);
            var meshFilter = overlay.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;
            var meshRenderer = overlay.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.sortingOrder = 50;
            objectsByKey[key] = overlay;
            return overlay;
        }

        private GameObject GetOrCreateLightFixtureObject(string key)
        {
            if (lightFixtureObjectsByKey.TryGetValue(key, out var fixture) && fixture != null)
            {
                return fixture;
            }

            fixture = new GameObject($"CameraLightTest Runtime Torch Fixture {key}");
            fixture.transform.SetParent(transform, false);

            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "Pole";
            pole.transform.SetParent(fixture.transform, false);
            pole.transform.localPosition = new Vector3(0f, 0.42f, 0f);
            pole.transform.localScale = new Vector3(0.08f, 0.42f, 0.08f);
            var poleCollider = pole.GetComponent<Collider>();
            if (poleCollider != null)
            {
                DestroyRuntimeAsset(poleCollider);
            }
            var poleRenderer = pole.GetComponent<MeshRenderer>();
            if (poleRenderer != null)
            {
                poleRenderer.sharedMaterial = GetFixturePoleMaterial();
                poleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                poleRenderer.receiveShadows = true;
            }

            var flame = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flame.name = "Flame";
            flame.transform.SetParent(fixture.transform, false);
            flame.transform.localPosition = new Vector3(0f, 0.92f, 0f);
            flame.transform.localScale = new Vector3(0.32f, 0.42f, 0.32f);
            var flameCollider = flame.GetComponent<Collider>();
            if (flameCollider != null)
            {
                DestroyRuntimeAsset(flameCollider);
            }
            var flameRenderer = flame.GetComponent<MeshRenderer>();
            if (flameRenderer != null)
            {
                flameRenderer.sharedMaterial = GetFixtureLitFlameMaterial();
                flameRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                flameRenderer.receiveShadows = true;
            }

            lightFixtureObjectsByKey[key] = fixture;
            return fixture;
        }

        private Material GetTileOverlayMaterial()
        {
            if (tileOverlayMaterial == null)
            {
                tileOverlayMaterial = CreateEmissiveGlowMaterial("CameraLightTest Emissive Glow Material");
            }

            return tileOverlayMaterial;
        }

        private Material GetLightMaskMaterial()
        {
            if (lightMaskMaterial == null)
            {
                lightMaskMaterial = CreateAdditiveOverlayMaterial("CameraLightTest Projected Additive Glow Material");
            }

            return lightMaskMaterial;
        }

        private Material GetPointLightReceiverMaterial()
        {
            if (pointLightReceiverMaterial == null)
            {
                pointLightReceiverMaterial = CreatePointLightReceiverMaterial();
            }

            return pointLightReceiverMaterial;
        }

        private Material GetFixturePoleMaterial()
        {
            if (fixturePoleMaterial == null)
            {
                fixturePoleMaterial = CreateLitFixtureMaterial("CameraLightTest Fixture Pole Material", new Color(0.18f, 0.13f, 0.08f, 1f));
            }

            return fixturePoleMaterial;
        }

        private Material GetFixtureLitFlameMaterial()
        {
            if (fixtureLitFlameMaterial == null)
            {
                fixtureLitFlameMaterial = CreateLitFixtureMaterial("CameraLightTest Fixture Lit Flame Material", new Color(1f, 0.58f, 0.24f, 1f));
            }

            return fixtureLitFlameMaterial;
        }

        private Material GetFixtureEmissiveFlameMaterial()
        {
            if (fixtureEmissiveFlameMaterial == null)
            {
                fixtureEmissiveFlameMaterial = CreateEmissiveGlowMaterial("CameraLightTest Fixture Emissive Flame Material");
            }

            return fixtureEmissiveFlameMaterial;
        }

        private Mesh GetHexOverlayMesh()
        {
            if (hexOverlayMesh == null)
            {
                hexOverlayMesh = CreateDiscMesh("CameraLightTest Hex Overlay Mesh", 6);
            }

            return hexOverlayMesh;
        }

        private Mesh GetDiskOverlayMesh()
        {
            if (diskOverlayMesh == null)
            {
                diskOverlayMesh = CreateDiscMesh("CameraLightTest Disk Overlay Mesh", 32);
            }

            return diskOverlayMesh;
        }

        private static IEnumerable<HexCoord> EnumerateHexDisk(HexCoord center, int radius)
        {
            for (var q = -radius; q <= radius; q++)
            {
                var rMin = Mathf.Max(-radius, -q - radius);
                var rMax = Mathf.Min(radius, -q + radius);
                for (var r = rMin; r <= rMax; r++)
                {
                    yield return center + new HexCoord(q, r);
                }
            }
        }

        private static float CalculateTileOverlayFalloff(int distance, int radius)
        {
            if (distance <= 0)
            {
                return 1f;
            }

            if (radius <= 0 || distance > radius)
            {
                return 0f;
            }

            if (radius == 1)
            {
                return 0.6f;
            }

            return Mathf.Clamp01(1f - (distance / (radius + 0.35f)));
        }

        private static Color CreateOverlayColor(Color color, float intensity, float maxAlpha)
        {
            var clamped = ClampColor(color);
            var alpha = Mathf.Clamp01(intensity * maxAlpha);
            return new Color(clamped.r, clamped.g, clamped.b, alpha);
        }

        private void SetRendererColor(GameObject target, Color color)
        {
            var renderer = target != null ? target.GetComponent<MeshRenderer>() : null;
            SetRendererColor(renderer, color);
        }

        private void SetRendererColor(Renderer renderer, Color color)
        {
            if (renderer == null)
            {
                return;
            }

            rendererColorBlock ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(rendererColorBlock);
            rendererColorBlock.SetColor("_Color", color);
            rendererColorBlock.SetColor("_BaseColor", color);
            rendererColorBlock.SetColor("_EmissionColor", color * 2.5f);
            renderer.SetPropertyBlock(rendererColorBlock);
            rendererColorBlock.Clear();
        }

        private static Material CreateLitFixtureMaterial(string name, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
            var material = new Material(shader) { name = name };
            material.color = color;
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.GeometryLast;
            material.SetColor("_Color", color);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.2f);
            material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            return material;
        }

        private static Material CreateEmissiveGlowMaterial(string name)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
            var material = new Material(shader) { name = name };
            var color = new Color(1f, 0.72f, 0.34f, 1f);
            material.color = color;
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.GeometryLast;
            material.SetColor("_Color", color);
            material.SetColor("_BaseColor", color);
            material.SetColor("_EmissionColor", color * 2.5f);
            material.EnableKeyword("_EMISSION");
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.35f);
            material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            return material;
        }

        private static Material CreateAdditiveOverlayMaterial(string name)
        {
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = name };
            material.color = Color.white;
            material.renderQueue = LightCompareRenderQueue;
            material.SetColor("_Color", Color.white);
            material.SetColor("_BaseColor", Color.white);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            material.SetInt("_ZWrite", 0);
            material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            return material;
        }

        private static Material CreatePointLightReceiverMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
            var material = new Material(shader) { name = "CameraLightTest Point Light Opaque Lit Preview Material" };
            var color = new Color(0.18f, 0.18f, 0.18f, 1f);
            material.color = color;
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.GeometryLast;
            material.SetColor("_Color", color);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.18f);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            material.SetInt("_ZWrite", 1);
            material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            return material;
        }

        private static Mesh CreateDiscMesh(string name, int segments)
        {
            segments = Mathf.Max(3, segments);
            var vertices = new Vector3[segments + 1];
            var triangles = new int[segments * 3];
            vertices[0] = Vector3.zero;
            for (var i = 0; i < segments; i++)
            {
                var radians = Mathf.PI * 2f * i / segments;
                vertices[i + 1] = new Vector3(Mathf.Cos(radians) * 0.5f, Mathf.Sin(radians) * 0.5f, 0f);
            }

            for (var i = 0; i < segments; i++)
            {
                var triangleIndex = i * 3;
                triangles[triangleIndex] = 0;
                triangles[triangleIndex + 1] = i + 1;
                triangles[triangleIndex + 2] = i == segments - 1 ? 1 : i + 2;
            }

            var mesh = new Mesh { name = name };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void RemoveInactiveObjects<T>(Dictionary<string, T> objectsByKey, HashSet<string> activeKeys, Action<T> destroy)
        {
            var staleKeys = new List<string>();
            foreach (var pair in objectsByKey)
            {
                if (!activeKeys.Contains(pair.Key))
                {
                    destroy(pair.Value);
                    staleKeys.Add(pair.Key);
                }
            }

            foreach (var key in staleKeys)
            {
                objectsByKey.Remove(key);
            }
        }

        private int CountRuntimeCompareResources()
        {
            var count = 0;
            if (tileOverlayMaterial != null)
            {
                count++;
            }

            if (lightMaskMaterial != null)
            {
                count++;
            }

            if (pointLightReceiverMaterial != null)
            {
                count++;
            }

            if (fixturePoleMaterial != null)
            {
                count++;
            }

            if (fixtureLitFlameMaterial != null)
            {
                count++;
            }

            if (fixtureEmissiveFlameMaterial != null)
            {
                count++;
            }

            if (hexOverlayMesh != null)
            {
                count++;
            }

            if (diskOverlayMesh != null)
            {
                count++;
            }

            return count;
        }

        private void DestroyRuntimeCompareResources()
        {
            DestroyRuntimeAsset(tileOverlayMaterial);
            DestroyRuntimeAsset(lightMaskMaterial);
            DestroyRuntimeAsset(pointLightReceiverMaterial);
            DestroyRuntimeAsset(fixturePoleMaterial);
            DestroyRuntimeAsset(fixtureLitFlameMaterial);
            DestroyRuntimeAsset(fixtureEmissiveFlameMaterial);
            DestroyRuntimeAsset(hexOverlayMesh);
            DestroyRuntimeAsset(diskOverlayMesh);
            tileOverlayMaterial = null;
            lightMaskMaterial = null;
            pointLightReceiverMaterial = null;
            fixturePoleMaterial = null;
            fixtureLitFlameMaterial = null;
            fixtureEmissiveFlameMaterial = null;
            hexOverlayMesh = null;
            diskOverlayMesh = null;
        }

        private static void DestroyRuntimeAsset(UnityEngine.Object asset)
        {
            if (asset == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(asset);
            }
            else
            {
                DestroyImmediate(asset);
            }
        }

        private static void DestroyOverlayObject(GameObject overlay)
        {
            if (overlay == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(overlay);
            }
            else
            {
                DestroyImmediate(overlay);
            }
        }

        private static string CreateSampleLightKey(ActiveMapLightSample activeSample)
        {
            return $"sample-{activeSample.InstanceId}-{activeSample.Approach}-{activeSample.Sample.Kind}-{activeSample.Coord.Q}-{activeSample.Coord.R}-{SanitizeKey(activeSample.Sample.Name)}";
        }

        private static string CreateTileOverlayKey(ActiveMapLightSample activeSample, HexCoord coord)
        {
            return $"tile-{activeSample.InstanceId}-{activeSample.Approach}-{activeSample.Sample.Kind}-{activeSample.Coord.Q}-{activeSample.Coord.R}-at-{coord.Q}-{coord.R}-{SanitizeKey(activeSample.Sample.Name)}";
        }

        private static bool ShouldRenderApproach(ActiveMapLightSample activeSample, VisualLightApproach rendererApproach)
        {
            return activeSample.Approach == VisualLightApproach.All || activeSample.Approach == rendererApproach;
        }

        private static string SanitizeKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unnamed";
            }

            return value.Replace(' ', '-').Replace('/', '-').Replace('\\', '-');
        }

        public int ActiveTorchLightCount => torchLightsByKey.Count;

        private void SyncTorchFieldObjectLights()
        {
            if (!showTorchFieldObjectLights || controller?.State == null)
            {
                ClearTorchFieldObjectLights();
                return;
            }

            var activeKeys = new HashSet<string>();
            foreach (var fieldObject in controller.State.FieldObjects.Objects)
            {
                if (fieldObject.Kind != FieldObjectKind.FogReveal || fieldObject.IsExpired)
                {
                    continue;
                }

                var key = CreateTorchLightKey(fieldObject);
                if (!controller.TryGetTileWorldPosition(fieldObject.Position, out var position))
                {
                    continue;
                }

                activeKeys.Add(key);
                var light = GetOrCreateTorchLight(key);
                light.transform.position = position + Vector3.up * Mathf.Max(0f, torchLightHeight);
                light.color = ClampColor(torchLightColor);
                light.range = Mathf.Max(0.1f, (fieldObject.Radius + 1) * torchLightRangePerHex);
                light.intensity = Mathf.Max(0f, torchLightIntensity);
            }

            RemoveInactiveTorchLights(activeKeys);
        }

        private Light GetOrCreateTorchLight(string key)
        {
            if (torchLightsByKey.TryGetValue(key, out var light) && light != null)
            {
                return light;
            }

            var root = new GameObject($"CameraLightTest Torch Light {key}");
            root.transform.SetParent(transform, false);
            light = root.AddComponent<Light>();
            light.type = LightType.Point;
            light.shadows = LightShadows.Soft;
            light.bounceIntensity = 0.35f;
            torchLightsByKey[key] = light;
            return light;
        }

        private void RemoveInactiveTorchLights(HashSet<string> activeKeys)
        {
            RemoveInactiveObjects(torchLightsByKey, activeKeys, DestroyTorchLight);
        }

        private void ClearTorchFieldObjectLights()
        {
            foreach (var light in torchLightsByKey.Values)
            {
                DestroyTorchLight(light);
            }
            torchLightsByKey.Clear();
        }

        private static void DestroyTorchLight(Light light)
        {
            if (light == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(light.gameObject);
            }
            else
            {
                DestroyImmediate(light.gameObject);
            }
        }

        private static string CreateTorchLightKey(FieldObject fieldObject)
        {
            return $"{fieldObject.Kind}-{fieldObject.Position.Q}-{fieldObject.Position.R}-r{fieldObject.Radius}";
        }

        private void ValidateRequiredReferences()
        {
            if (controller != null && targetCamera != null && directionalLight != null)
            {
                return;
            }

            Debug.LogError(
                "CameraLightTestPanel requires explicit scene references for controller, targetCamera, and directionalLight.",
                this);
            enabled = false;
        }

        private void EnsureDefaultPresets()
        {
            if (cameraPresets == null || cameraPresets.Length == 0)
            {
                cameraPresets = CreateDefaultCameraPresets();
            }

            if (lightingPresets == null || lightingPresets.Length == 0)
            {
                lightingPresets = CreateDefaultLightingPresets();
            }

            if (mapLightSamples == null || mapLightSamples.Length == 0)
            {
                mapLightSamples = CreateDefaultMapLightSamples();
            }
        }

        private void CaptureCameraSettings()
        {
            if (controller != null)
            {
                currentPlayerOffset = controller.CameraPlayerOffset;
                currentEulerAngles = controller.CameraEulerAngles;
                currentOrthographicSize = controller.CameraOrthographicSize;
                currentKeepCentered = controller.KeepCameraCenteredOnPlayer;
                return;
            }

            if (targetCamera == null)
            {
                return;
            }

            currentEulerAngles = targetCamera.transform.rotation.eulerAngles;
            currentOrthographicSize = targetCamera.orthographicSize;
        }

        private static Vector3 ClampEuler(Vector3 eulerAngles)
        {
            eulerAngles.x = Mathf.Clamp(eulerAngles.x, 15f, 75f);
            eulerAngles.y = Mathf.Repeat(eulerAngles.y + 180f, 360f) - 180f;
            eulerAngles.z = 0f;
            return eulerAngles;
        }

        private static MapLightTestObject[] CreateDefaultMapLightSamples()
        {
            return new[]
            {
                new MapLightTestObject { Name = "Warm Street Lamp", RelativeCoord = new HexCoord(1, 0), Color = new Color(1f, 0.72f, 0.34f, 1f), Intensity = 1.2f, Radius = 2, Height = 2.2f, Kind = MapLightTestKind.StreetLamp, AffectsFog = false },
                new MapLightTestObject { Name = "Small Building Window", RelativeCoord = new HexCoord(-1, 1), Color = new Color(1f, 0.86f, 0.48f, 1f), Intensity = 0.65f, Radius = 1, Height = 1.35f, Kind = MapLightTestKind.BuildingWindow, AffectsFog = false },
                new MapLightTestObject { Name = "Window Row", RelativeCoord = new HexCoord(-2, 2), Color = new Color(1f, 0.78f, 0.42f, 1f), Intensity = 0.8f, Radius = 2, Height = 1.55f, Kind = MapLightTestKind.BuildingWindow, AffectsFog = false },
                new MapLightTestObject { Name = "Blue Neon", RelativeCoord = new HexCoord(2, -1), Color = new Color(0.18f, 0.62f, 1f, 1f), Intensity = 1.35f, Radius = 2, Height = 1.7f, Kind = MapLightTestKind.Neon, AffectsFog = false },
                new MapLightTestObject { Name = "Campfire", RelativeCoord = new HexCoord(0, 2), Color = new Color(1f, 0.42f, 0.16f, 1f), Intensity = 1.45f, Radius = 2, Height = 0.85f, Kind = MapLightTestKind.Campfire, AffectsFog = false }
            };
        }

        private static CameraPreset[] CreateDefaultCameraPresets()
        {
            return new[]
            {
                CreateCenteredCameraPreset("Current / PlayerState", new Vector3(35f, 0f, 0f), 17.65f, 8.5f),
                CreateCenteredCameraPreset("High Topdown", new Vector3(60f, 0f, 0f), 16.5f, 11f),
                CreateCenteredCameraPreset("Low Oblique", new Vector3(28f, 0f, 0f), 15.0f, 7f),
                CreateCenteredCameraPreset("Wide Map", new Vector3(45f, 0f, 0f), 25.55f, 14f),
                CreateCenteredCameraPreset("Close Tactical", new Vector3(32f, 0f, 0f), 11.86f, 5.5f)
            };
        }

        private static CameraPreset CreateCenteredCameraPreset(string name, Vector3 eulerAngles, float cameraDistance, float orthographicSize)
        {
            return new CameraPreset
            {
                Name = name,
                PlayerOffset = -(Quaternion.Euler(eulerAngles) * Vector3.forward) * cameraDistance,
                EulerAngles = eulerAngles,
                OrthographicSize = orthographicSize,
                KeepCenteredOnPlayer = true
            };
        }

        private static LightingPreset[] CreateDefaultLightingPresets()
        {
            return new[]
            {
                new LightingPreset { Name = "Baseline Warm", AmbientColor = new Color(0.42f, 0.42f, 0.42f, 1f), CameraBackgroundColor = new Color(0.192f, 0.302f, 0.475f, 1f), DirectionalLightColor = new Color(1f, 0.957f, 0.839f, 1f), DirectionalLightEulerAngles = new Vector3(50f, -25f, 15f), DirectionalLightIntensity = 1f },
                new LightingPreset { Name = "Soft Bright", AmbientColor = new Color(0.56f, 0.56f, 0.54f, 1f), CameraBackgroundColor = new Color(0.36f, 0.47f, 0.62f, 1f), DirectionalLightColor = new Color(1f, 0.98f, 0.9f, 1f), DirectionalLightEulerAngles = new Vector3(58f, -20f, 10f), DirectionalLightIntensity = 0.85f },
                new LightingPreset { Name = "High Contrast", AmbientColor = new Color(0.22f, 0.23f, 0.24f, 1f), CameraBackgroundColor = new Color(0.12f, 0.16f, 0.22f, 1f), DirectionalLightColor = new Color(1f, 0.91f, 0.72f, 1f), DirectionalLightEulerAngles = new Vector3(38f, -38f, 20f), DirectionalLightIntensity = 1.25f },
                new LightingPreset { Name = "Cool Overcast", AmbientColor = new Color(0.44f, 0.48f, 0.54f, 1f), CameraBackgroundColor = new Color(0.24f, 0.31f, 0.39f, 1f), DirectionalLightColor = new Color(0.78f, 0.88f, 1f, 1f), DirectionalLightEulerAngles = new Vector3(70f, -10f, 0f), DirectionalLightIntensity = 0.55f },
                new LightingPreset { Name = "Night View", AmbientColor = new Color(0.12f, 0.135f, 0.18f, 1f), CameraBackgroundColor = new Color(0.055f, 0.07f, 0.115f, 1f), DirectionalLightColor = new Color(0.58f, 0.68f, 0.95f, 1f), DirectionalLightEulerAngles = new Vector3(28f, -35f, 5f), DirectionalLightIntensity = 0.45f }
            };
        }

        private static float DrawFloatSlider(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {value:0.00}", GUILayout.Width(SliderLabelWidth));
            value = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.EndHorizontal();
            return Mathf.Clamp(value, min, max);
        }

        private static Color DrawColorSliders(string label, Color color)
        {
            GUILayout.Label($"{label}: {color.r:0.00}, {color.g:0.00}, {color.b:0.00}");
            color.r = DrawFloatSlider("R", color.r, 0f, 1f);
            color.g = DrawFloatSlider("G", color.g, 0f, 1f);
            color.b = DrawFloatSlider("B", color.b, 0f, 1f);
            color.a = 1f;
            return color;
        }

        private static Color AdjustColor(Color color, float amount)
        {
            return ClampColor(new Color(color.r + amount, color.g + amount, color.b + amount, 1f));
        }

        private static Color ClampColor(Color color)
        {
            return new Color(
                Mathf.Clamp01(color.r),
                Mathf.Clamp01(color.g),
                Mathf.Clamp01(color.b),
                Mathf.Clamp01(color.a));
        }

        private static string FormatVector(Vector3 value)
        {
            return $"({value.x:0.00}, {value.y:0.00}, {value.z:0.00})";
        }

        private static string FormatActiveName(CameraPreset[] presets, int index)
        {
            return presets != null && index >= 0 && index < presets.Length ? presets[index].Name : "Custom";
        }

        private static string FormatActiveName(LightingPreset[] presets, int index)
        {
            return presets != null && index >= 0 && index < presets.Length ? presets[index].Name : "Custom";
        }
    }
}

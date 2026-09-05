using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.MapDesign.Editor
{
    public sealed class HexSparseMapEditorWindow : EditorWindow
    {
        private readonly HexSparseMapEditorSession session = new HexSparseMapEditorSession();
        private string status;
        private MessageType statusMessageType = MessageType.Info;

        // Live preview
        [SerializeField] private AtlasTilePresentationView livePreviewView;
        [SerializeField] private bool livePreviewEnabled = true;
        [SerializeField] private float objectPreviewLift = 0.02f;
        private const string LivePreviewObjectPrefix = "MapObjectPreview_";
        private const string TrapPreviewPrefabPath = "Assets/Prefabs/Object/trap.prefab";
        private readonly List<GameObject> livePreviewObjects = new List<GameObject>();

        // SceneView authoring
        private float authoringPlaneY = 0f;
        private float tileRadius = 1f;
        private int gridMaxRadius = 40;
        private bool sceneGridVisible = true;
        private bool sceneQuickWindowVisible = true;
        [SerializeField] private Rect sceneQuickWindowRect = new Rect(10, 34, 360, 430);
        [SerializeField] private Rect scenePaletteWindowRect = new Rect(410, 34, 340, 420);
        private Vector2 scenePaletteWindowScroll;
        private Vector2 sceneQuickWindowScroll;

        // Panel hosting: each panel is either a floating window inside the Scene View or its own dockable
        // EditorWindow the designer can park next to the Inspector. Persisted so the layout survives reloads.
        internal enum PanelHost { SceneView, DockableWindow }
        private const float MinSceneWindowWidth = 200f;
        private const float MinSceneWindowHeight = 120f;
        private const string PrefKeyPaintHost = "HexMapEditor_PaintPanelHost";
        private const string PrefKeyPaletteHost = "HexMapEditor_PalettePanelHost";
        private PanelHost paintPanelHost = PanelHost.SceneView;
        private PanelHost palettePanelHost = PanelHost.SceneView;
        private int resizingSceneWindowId = -1;

        // Tool selection
        // Select picks painted tiles; SelectObject picks an already-placed map object. They are separate tools
        // because a click on one coordinate is ambiguous otherwise — the tile and whatever stands on it.
        private enum EditorTool { Paint, Erase, Pick, Select, SelectObject, StampComposite, PlaceObject, EraseObject, PaintPatrolArea, ErasePatrolArea, PaintDisk, EraseDisk, PaintArea, EraseArea }
        private enum PalettePanelMode { TilePalette, Object }
        private EditorTool activeTool = EditorTool.Paint;
        private PalettePanelMode palettePanelMode = PalettePanelMode.TilePalette;

        // Brush height override
        private bool brushHeightOverrideEnabled;
        private int brushHeightOverride;

        // Brush stroke
        private HexCoord? hoverCoord;
        private bool brushStrokeActive;
        private bool brushStrokeChanged;
        private int brushStrokeUndoGroup = -1;
        private readonly HashSet<HexCoord> brushStrokeCoords = new HashSet<HexCoord>();
        private readonly HashSet<HexCoord> selectedCoords = new HashSet<HexCoord>();
        private int eraseBrushRadius;
        private bool selectionMoveMode;
        private bool selectionDragActive;
        private HexCoord selectionDragLastCoord;
        private int selectionDragUndoGroup = -1;
        private bool objectDragActive;
        private bool objectDragMoved;
        private HexCoord objectDragLastCoord;
        private int objectDragUndoGroup = -1;

        // Palette search
        private string tilePaletteSearch = string.Empty;
        private string objectPaletteSearch = string.Empty;

        // Composite presets
        [SerializeField] private List<CompositeTilePreset> compositePresets = new List<CompositeTilePreset>();
        [SerializeField] private string compositePresetName = "Composite Preset";
        [SerializeField] private int activeCompositePresetIndex = -1;
        [SerializeField] private int compositeStampRotationSteps;

        // Palette scroll
        private Vector2 rootScroll;
        private Vector2 paletteScroll;
        private Vector2 compositePresetScroll;
        private Vector2 objectListScroll;
        private Vector2 objectPaletteScroll;
        private bool footprintEditMode;

        // Object list edit buffer
        private string selectedObjectId;
        private string objectEditId;
        private HexMapObjectType objectEditType;
        private string objectEditRef;
        private string objectEditRole;
        private HexMapPurpose objectEditPurpose;
        private bool objectEditBlocksMovement;
        private bool objectEditBlocksVision;
        private bool objectEditInteractable;
        private string objectEditPatrolAreaId;
        private Vector3 objectEditVisualScaleMultiplier = Vector3.one;
        private float objectEditCameraSpeedMultiplier = 1f;
        private float objectEditCameraDwellSeconds;
        private bool objectEditCameraStartsNewSegment;

        // Manual coord inputs
        private int manualQ;
        private int manualR;

        // Foldout states (persisted via EditorPrefs)
        private const string PrefKeySceneView   = "HexMapEditor_FoldSceneView";
        private const string PrefKeyManualCoord = "HexMapEditor_FoldManualCoord";
        private const string PrefKeyLivePreview = "HexMapEditor_FoldLivePreview";
        private const string PrefKeyObjectList  = "HexMapEditor_FoldObjectList";
        private const string PrefKeySceneGrid   = "HexMapEditor_SceneGridVisible";
        private const string PrefKeyQuickWindow = "HexMapEditor_QuickWindowVisible";
        private const string DefaultTilePresetCatalogPath = "Assets/Data/Map/Authoring/SparseMapTilePresets.asset";
        private static readonly string[] BuiltInMonsterSpawnRoles =
        {
            string.Empty,
            "primary_pressure",
            "boss",
            "elite",
            "patrol",
            "ambush"
        };
        private static readonly string[] BuiltInMonsterSpawnRoleLabels =
        {
            "<None>",
            "Primary Pressure",
            "Boss",
            "Elite",
            "Patrol",
            "Ambush"
        };
        private static readonly string[] MemoryStoneRoles =
        {
            string.Empty,
            "main",
            "objective"
        };
        private static readonly string[] MemoryStoneRoleLabels =
        {
            "<None>",
            "Main MemoryStone",
            "Objective MemoryStone"
        };
        private static readonly string[] LandmarkRoles =
        {
            string.Empty,
            "landmark"
        };
        private static readonly string[] LandmarkRoleLabels =
        {
            "<None>",
            "Landmark (intro cinematic)"
        };
        private static readonly Color VictoryCameraPointFillColor = new Color(1f, 0.92f, 0.05f, 0.52f);
        private static readonly Color VictoryCameraPointLineColor = new Color(1f, 0.78f, 0f, 1f);
        private static readonly Color IntroCameraPointFillColor = new Color(0.05f, 0.85f, 0.78f, 0.5f);
        private static readonly Color IntroCameraPointLineColor = new Color(0f, 0.96f, 0.86f, 1f);
        private static readonly Color IntroCameraPointCutColor = new Color(1f, 0.62f, 0.1f, 1f);
        private static readonly Color VictoryEndCameraPointFillColor = new Color(1f, 0.18f, 0.72f, 0.52f);
        private static readonly Color VictoryEndCameraPointLineColor = new Color(1f, 0.3f, 0.85f, 1f);

        private bool foldSceneView;
        private bool foldManualCoord;
        private bool foldLivePreview;
        private bool foldObjectList;

        // Pre-allocated SceneView draw buffers (avoid per-frame GC)
        private readonly Vector3[] hexCorners = new Vector3[6];
        private readonly Vector3[] hexOutline = new Vector3[7];

        [MenuItem("Seoul Playup/Map/Sparse Map Editor")]
        public static void Open()
        {
            GetWindow<HexSparseMapEditorWindow>("Sparse Map Editor");
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
            foldSceneView   = EditorPrefs.GetBool(PrefKeySceneView,   true);
            foldManualCoord = EditorPrefs.GetBool(PrefKeyManualCoord, false);
            foldLivePreview = EditorPrefs.GetBool(PrefKeyLivePreview, true);
            foldObjectList  = EditorPrefs.GetBool(PrefKeyObjectList,  false);
            sceneGridVisible = EditorPrefs.GetBool(PrefKeySceneGrid, true);
            sceneQuickWindowVisible = EditorPrefs.GetBool(PrefKeyQuickWindow, true);
            paintPanelHost = (PanelHost)EditorPrefs.GetInt(PrefKeyPaintHost, (int)PanelHost.SceneView);
            palettePanelHost = (PanelHost)EditorPrefs.GetInt(PrefKeyPaletteHost, (int)PanelHost.SceneView);
            AssignDefaultReferences();
        }

        /// <summary>
        /// The single live editor instance, or null when the Sparse Map Editor is closed. The dockable
        /// panel windows are thin views onto it — they own no state of their own.
        /// </summary>
        internal static HexSparseMapEditorWindow FindOpenInstance()
        {
            var windows = Resources.FindObjectsOfTypeAll<HexSparseMapEditorWindow>();
            return windows != null && windows.Length > 0 ? windows[0] : null;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            ClearLivePreview();
        }

        private void OnUndoRedoPerformed()
        {
            if (session.Source == null) return;
            if (livePreviewEnabled)
                ApplyLivePreview();
            SceneView.RepaintAll();
            Repaint();
        }

        private void AssignDefaultReferences()
        {
            if (session.TilePresetCatalog == null)
            {
                session.TilePresetCatalog = AssetDatabase.LoadAssetAtPath<HexTilePresetCatalog>(DefaultTilePresetCatalogPath);
            }

            if (livePreviewView == null)
            {
                livePreviewView = FindDefaultLivePreviewView();
            }
        }

        private static AtlasTilePresentationView FindDefaultLivePreviewView()
        {
            return Resources.FindObjectsOfTypeAll<AtlasTilePresentationView>()
                .FirstOrDefault(view =>
                    view != null &&
                    !EditorUtility.IsPersistent(view) &&
                    view.gameObject.scene.IsValid());
        }

        private static GUIContent Content(string text, string tooltip)
        {
            return new GUIContent(text, tooltip);
        }

        private void OnGUI()
        {
            rootScroll = EditorGUILayout.BeginScrollView(rootScroll);

            EditorGUILayout.LabelField("Sparse Map Editor", EditorStyles.boldLabel);
            DrawAssetRefsSection();
            DrawLivePreviewSection();
            DrawScenePaintWindowSection();
            DrawPatrolAreaSection();
            DrawBossArenaSection();
            DrawObjectListSection();
            DrawSceneViewSettingsSection();
            DrawSourceSummarySection();
            DrawManualCoordSection();

            if (!string.IsNullOrWhiteSpace(status))
                EditorGUILayout.HelpBox(status, statusMessageType);

            EditorGUILayout.EndScrollView();
        }

        // ── 1. Asset References ──────────────────────────────────────────

        private void DrawAssetRefsSection()
        {
            EditorGUILayout.Space();
            session.Source = (HexSparseMapAuthoringSource)EditorGUILayout.ObjectField(
                "Sparse Source", session.Source, typeof(HexSparseMapAuthoringSource), false);
            session.TilePresetCatalog = (HexTilePresetCatalog)EditorGUILayout.ObjectField(
                Content("Tile Preset Catalog", "Defaults to Assets/Data/Map/Authoring/SparseMapTilePresets.asset."), session.TilePresetCatalog, typeof(HexTilePresetCatalog), false);
        }

        private void DrawScenePaintWindowSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Panels", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                var label = sceneQuickWindowVisible ? "Hide Scene View Panels" : "Show Scene View Panels";
                if (GUILayout.Button(Content(label, "Toggles the floating panels drawn inside the Scene View. Panels popped out into their own window are unaffected.")))
                {
                    SetSceneQuickWindowVisible(!sceneQuickWindowVisible);
                }
            }

            // Each panel is hosted either in the Scene View overlay or as its own dockable Editor window,
            // so a designer can park them alongside the Inspector/Hierarchy instead of over the viewport.
            var nextPaintHost = (PanelHost)EditorGUILayout.EnumPopup(
                Content("Map Paint Panel", "Where the tool/rotation/selection panel is drawn."),
                paintPanelHost);
            if (nextPaintHost != paintPanelHost)
            {
                SetPaintPanelHost(nextPaintHost);
            }

            var nextPaletteHost = (PanelHost)EditorGUILayout.EnumPopup(
                Content("Palette / Object Panel", "Where the tile palette and object brush panel is drawn."),
                palettePanelHost);
            if (nextPaletteHost != palettePanelHost)
            {
                SetPalettePanelHost(nextPaletteHost);
            }
        }

        // ── 2. Palette ───────────────────────────────────────────────────

        private void DrawPaletteSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Tile Palette", EditorStyles.boldLabel);

            if (session.TilePresetCatalog != null && session.TilePresetCatalog.Entries.Count > 0)
            {
                tilePaletteSearch = DrawPaletteSearchField(tilePaletteSearch, "Search tile presets by label, id, or atlas id");
                var allEntries = GetVisiblePaletteEntries().ToArray();
                if (allEntries.Length == 0)
                {
                    EditorGUILayout.HelpBox("No tile presets match palette id format (letter(s) + number prefix, e.g. b01, by01, or d01_base).", MessageType.Warning);
                    return;
                }

                var entries = allEntries
                    .Where(entry => MatchesSearch(tilePaletteSearch, entry.Label, entry.TilePresetId, entry.AtlasVisualId))
                    .ToArray();
                if (entries.Length == 0)
                {
                    EditorGUILayout.HelpBox($"No tile preset matches '{tilePaletteSearch}'.", MessageType.Info);
                    return;
                }

                paletteScroll = EditorGUILayout.BeginScrollView(paletteScroll, GUILayout.Height(210));
                foreach (var entry in entries)
                {
                    if (entry == null) continue;
                    bool isActive = entry.TilePresetId == session.ActiveTilePresetId;
                    bool isDefaultFill = entry.TilePresetId == session.DefaultFillTilePresetId;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        DrawTilePresetPreview(entry, GUILayout.Width(42), GUILayout.Height(34));

                        using (new EditorGUILayout.VerticalScope())
                        {
                            string heightSuffix = entry.HeightLevel != 0 ? $"  H{entry.HeightLevel}" : string.Empty;
                            string btnLabel = $"{entry.Label}{heightSuffix}";
                            if (GUILayout.Toggle(isActive, btnLabel, isActive ? EditorStyles.toolbarButton : GUI.skin.button))
                                session.ActiveTilePresetId = entry.TilePresetId;
                            EditorGUILayout.LabelField(entry.AtlasVisualId, EditorStyles.miniLabel);
                        }

                        var starTip = new GUIContent(isDefaultFill ? "*" : " ", "Set as Default Fill");
                        if (GUILayout.Button(starTip, GUILayout.Width(24)))
                            session.DefaultFillTilePresetId = entry.TilePresetId;
                    }
                }
                EditorGUILayout.EndScrollView();

                using (new EditorGUI.DisabledScope(true))
                {
                    string defaultLabel = string.IsNullOrEmpty(session.DefaultFillTilePresetId)
                        ? "(none)" : session.DefaultFillTilePresetId;
                    EditorGUILayout.TextField("Default Fill", defaultLabel);
                }
            }
            else
            {
                session.ActiveTilePresetId = EditorGUILayout.TextField(
                    "Active Tile Preset", session.ActiveTilePresetId ?? string.Empty);
                session.DefaultFillTilePresetId = EditorGUILayout.TextField(
                    "Default Fill Preset", session.DefaultFillTilePresetId ?? string.Empty);
            }
        }

        // Toolbar-style search box with a clear button, shared by the tile and object palettes.
        private static string DrawPaletteSearchField(string current, string tooltip)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var next = EditorGUILayout.TextField(Content("Search", tooltip), current ?? string.Empty);
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(next)))
                {
                    if (GUILayout.Button("×", GUILayout.Width(22)))
                    {
                        GUI.FocusControl(null);
                        return string.Empty;
                    }
                }

                return next;
            }
        }

        // Space-separated terms, all of which must appear in at least one of the candidate fields.
        private static bool MatchesSearch(string search, params string[] candidates)
        {
            if (string.IsNullOrWhiteSpace(search)) return true;

            foreach (var term in search.Split(' '))
            {
                if (string.IsNullOrWhiteSpace(term)) continue;
                var matched = false;
                foreach (var candidate in candidates)
                {
                    if (!string.IsNullOrEmpty(candidate) &&
                        candidate.IndexOf(term, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        matched = true;
                        break;
                    }
                }

                if (!matched) return false;
            }

            return true;
        }

        private IEnumerable<HexTilePresetCatalog.Entry> GetVisiblePaletteEntries()
        {
            if (session.TilePresetCatalog == null)
                yield break;

            foreach (var entry in session.TilePresetCatalog.Entries)
            {
                if (entry != null && IsPaletteTilePresetId(entry.TilePresetId))
                    yield return entry;
            }
        }

        public static bool IsPaletteTilePresetId(string tilePresetId)
        {
            if (string.IsNullOrWhiteSpace(tilePresetId)) return false;
            var value = tilePresetId.Trim();
            if (value.Length < 2 || !char.IsLetter(value[0])) return false;

            var index = 1;
            while (index < value.Length && char.IsLetter(value[index]))
            {
                index++;
            }

            if (index >= value.Length || !char.IsDigit(value[index]))
            {
                return false;
            }

            while (index < value.Length && char.IsDigit(value[index]))
            {
                index++;
            }

            return index == value.Length || value[index] == '_' || value[index] == '-';
        }

        private void DrawTilePresetPreview(HexTilePresetCatalog.Entry entry, params GUILayoutOption[] options)
        {
            var rect = GUILayoutUtility.GetRect(42f, 42f, 34f, 34f, options);
            GUI.Box(rect, GUIContent.none);

            Texture preview = null;
            if (session.TilePresetCatalog?.AtlasTileCatalog != null)
            {
                var atlasEntry = session.TilePresetCatalog.AtlasTileCatalog.Entries
                    .FirstOrDefault(candidate => candidate != null && string.Equals(candidate.AtlasVisualId, entry.AtlasVisualId, System.StringComparison.OrdinalIgnoreCase));
                if (atlasEntry?.TopPrefab != null)
                {
                    preview = AssetPreview.GetAssetPreview(atlasEntry.TopPrefab) ?? AssetPreview.GetMiniThumbnail(atlasEntry.TopPrefab);
                }
            }

            if (preview != null)
            {
                GUI.DrawTexture(rect, preview, ScaleMode.ScaleToFit);
            }
            else
            {
                GUI.Label(rect, entry.TilePresetId, EditorStyles.centeredGreyMiniLabel);
            }
        }

        // ── 3. Tool + Height Override ────────────────────────────────────

        private void DrawToolSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Tool", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Toggle(activeTool == EditorTool.Paint, "Paint", EditorStyles.miniButtonLeft))
                    activeTool = EditorTool.Paint;
                if (GUILayout.Toggle(activeTool == EditorTool.Erase, "Erase", EditorStyles.miniButtonMid))
                    activeTool = EditorTool.Erase;
                if (GUILayout.Toggle(activeTool == EditorTool.Pick,  "Pick",  EditorStyles.miniButtonMid))
                    activeTool = EditorTool.Pick;
                if (GUILayout.Toggle(activeTool == EditorTool.Select,  "Select",  EditorStyles.miniButtonRight))
                    activeTool = EditorTool.Select;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Toggle(
                        activeTool == EditorTool.SelectObject,
                        Content("Select Object", "Click a placed map object in the Scene View to select it, then drag it to move and use the Rotation controls to turn it."),
                        EditorStyles.miniButtonLeft))
                {
                    if (activeTool != EditorTool.SelectObject)
                    {
                        activeTool = EditorTool.SelectObject;
                        SyncBrushRotationFromSelectedObject();
                    }
                }

                if (GUILayout.Toggle(activeTool == EditorTool.StampComposite, "Stamp Preset", EditorStyles.miniButtonRight))
                    activeTool = EditorTool.StampComposite;
            }

            if (activeTool == EditorTool.SelectObject)
            {
                DrawSelectedObjectTransformControls();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Toggle(activeTool == EditorTool.PlaceObject, "Place Object", EditorStyles.miniButtonLeft))
                    activeTool = EditorTool.PlaceObject;
                if (GUILayout.Toggle(activeTool == EditorTool.EraseObject, "Erase Object", EditorStyles.miniButtonRight))
                    activeTool = EditorTool.EraseObject;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Toggle(activeTool == EditorTool.PaintPatrolArea, "Paint Patrol", EditorStyles.miniButtonLeft))
                    activeTool = EditorTool.PaintPatrolArea;
                if (GUILayout.Toggle(activeTool == EditorTool.ErasePatrolArea, "Erase Patrol", EditorStyles.miniButtonRight))
                    activeTool = EditorTool.ErasePatrolArea;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Toggle(activeTool == EditorTool.PaintDisk, "Paint Disk", EditorStyles.miniButtonLeft))
                    activeTool = EditorTool.PaintDisk;
                if (GUILayout.Toggle(activeTool == EditorTool.EraseDisk, "Erase Disk", EditorStyles.miniButtonRight))
                    activeTool = EditorTool.EraseDisk;
            }

            if (activeTool == EditorTool.PaintDisk || activeTool == EditorTool.EraseDisk)
            {
                session.ActiveDiskBrushRadius = Mathf.Max(0, EditorGUILayout.IntField(
                    new GUIContent("Disk Radius", "브러시가 한 번에 찍는 헥스 디스크 반경. 0이면 단일 셀."),
                    session.ActiveDiskBrushRadius));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Toggle(activeTool == EditorTool.PaintArea, "Paint Area", EditorStyles.miniButtonLeft))
                    activeTool = EditorTool.PaintArea;
                if (GUILayout.Toggle(activeTool == EditorTool.EraseArea, "Erase Area", EditorStyles.miniButtonRight))
                    activeTool = EditorTool.EraseArea;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                brushHeightOverrideEnabled = EditorGUILayout.Toggle(
                    brushHeightOverrideEnabled, GUILayout.Width(16));
                EditorGUILayout.LabelField("Height Override", GUILayout.Width(110));
                using (new EditorGUI.DisabledScope(!brushHeightOverrideEnabled))
                    brushHeightOverride = EditorGUILayout.IntSlider(
                        brushHeightOverride,
                        HexCellData.MinHeightLevel,
                        HexCellData.MaxHeightLevel);
            }

            eraseBrushRadius = Mathf.Max(0, EditorGUILayout.IntSlider("Erase Radius", eraseBrushRadius, 0, 4));

            DrawRotationControls();
        }

        private void DrawRotationControls()
        {
            var supportsFineAngle = RotationTargetSupportsFineAngle();
            EditorGUILayout.LabelField("Rotation", $"{session.ActiveYawDegrees:0.#}°");
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("−60", EditorStyles.miniButtonLeft))
                {
                    SetActiveYaw(session.ActiveRotationSteps * 60f - 60f);
                }

                for (var i = 0; i < 6; i++)
                {
                    if (GUILayout.Toggle(session.ActiveRotationSteps == i, $"{i * 60}°", EditorStyles.miniButtonMid))
                    {
                        if (session.ActiveRotationSteps != i || session.ActiveRotationFineDegrees != 0f)
                        {
                            SetActiveYaw(i * 60f);
                        }
                    }
                }

                if (GUILayout.Button("+60", EditorStyles.miniButtonRight))
                {
                    SetActiveYaw(session.ActiveRotationSteps * 60f + 60f);
                }
            }

            // Free-angle slider. Tiles only exist on the 60° lattice (their edge masks and side meshes are
            // authored per hex face), so for tile targets the slider snaps back to the nearest step.
            using (var check = new EditorGUI.ChangeCheckScope())
            {
                var yaw = EditorGUILayout.Slider(
                    Content("Yaw (0-360)", supportsFineAngle
                        ? "Free-angle yaw for the object brush or the selected object."
                        : "Tiles only rotate in 60° steps — this slider snaps to the nearest step for tile targets."),
                    session.ActiveYawDegrees,
                    0f,
                    360f);
                if (check.changed)
                {
                    SetActiveYaw(yaw);
                }
            }

            if (activeTool == EditorTool.Select && selectedCoords.Count > 0)
            {
                EditorGUILayout.LabelField("Select Tiles", "rotation applies to selected tiles (60° steps)");
            }
            else if (activeTool == EditorTool.SelectObject)
            {
                EditorGUILayout.LabelField("Select Object", TryGetSelectedObjectRef(out var picked)
                    ? $"rotation applies to '{picked.ObjectId}'"
                    : "click a placed object in the Scene View");
            }
            else if (activeTool == EditorTool.StampComposite)
            {
                EditorGUILayout.LabelField("Stamp Preset", "uses this rotation");
            }
            else if (activeTool == EditorTool.PlaceObject)
            {
                EditorGUILayout.LabelField("Place Object", "uses this rotation");
            }
        }

        /// <summary>
        /// Only map objects carry the free-angle nudge; tile cells and composite stamps are hex-lattice only.
        /// </summary>
        private bool RotationTargetSupportsFineAngle()
        {
            return activeTool == EditorTool.PlaceObject || activeTool == EditorTool.SelectObject;
        }

        // Single entry point for every rotation control: snaps when the target cannot hold a free angle,
        // stores the brush rotation, then pushes it at whatever the active tool is pointed at.
        private void SetActiveYaw(float yawDegrees)
        {
            var previousSteps = session.ActiveRotationSteps;
            var resolved = RotationTargetSupportsFineAngle()
                ? yawDegrees
                : Mathf.Round(yawDegrees / 60f) * 60f;
            session.ActiveYawDegrees = resolved;
            ApplyUnifiedRotationStep(WrapRotationSteps(session.ActiveRotationSteps - previousSteps));
            Repaint();
            SceneView.RepaintAll();
        }

        // Axial neighbour deltas, listed clockwise from +Q so the nudge buttons read around the hex.
        private static readonly HexCoord[] HexNudgeDirections =
        {
            new HexCoord(1, 0),
            new HexCoord(1, -1),
            new HexCoord(0, -1),
            new HexCoord(-1, 0),
            new HexCoord(-1, 1),
            new HexCoord(0, 1),
        };
        private static readonly string[] HexNudgeLabels = { "+Q", "+Q−R", "−R", "−Q", "−Q+R", "+R" };

        // Select Object mode's transform panel: which object is picked, where it sits, and how to nudge it.
        // Rotation deliberately stays on the shared Rotation controls so one widget drives every target.
        private void DrawSelectedObjectTransformControls()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Selected Object", EditorStyles.boldLabel);

            if (!TryGetSelectedObjectRef(out var selected))
            {
                EditorGUILayout.HelpBox("Click a placed map object in the Scene View to select it.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(selected.ObjectId, $"{selected.ObjectType}  {selected.Coord}");
            EditorGUILayout.LabelField("Yaw", $"{selected.YawDegrees:0.#}°  (step {selected.RotationSteps * 60}° + {selected.RotationFineDegrees:0.#}°)");

            using (new EditorGUILayout.HorizontalScope())
            {
                for (var i = 0; i < HexNudgeDirections.Length; i++)
                {
                    var style = i == 0
                        ? EditorStyles.miniButtonLeft
                        : i == HexNudgeDirections.Length - 1 ? EditorStyles.miniButtonRight : EditorStyles.miniButtonMid;
                    if (GUILayout.Button(HexNudgeLabels[i], style))
                    {
                        MoveSelectedObjectBy(HexNudgeDirections[i]);
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Focus", EditorStyles.miniButtonLeft))
                {
                    FocusSceneViewOnObject(selected);
                }

                if (GUILayout.Button("Deselect", EditorStyles.miniButtonRight))
                {
                    selectedObjectId = null;
                    SceneView.RepaintAll();
                }
            }

            EditorGUILayout.LabelField("Scene", "click to select · drag to move");
        }

        private void SyncBrushRotationFromSelectedObject()
        {
            if (!TryGetSelectedObjectRef(out var selected)) return;
            session.ActiveRotationSteps = selected.RotationSteps;
            session.ActiveRotationFineDegrees = selected.RotationFineDegrees;
        }

        private void MoveSelectedObjectBy(HexCoord delta)
        {
            if (!TryGetSelectedObjectRef(out var selected)) return;
            MoveSelectedObjectTo(new HexCoord(selected.Coord.Q + delta.Q, selected.Coord.R + delta.R), registerUndo: true);
        }

        private bool MoveSelectedObjectTo(HexCoord target, bool registerUndo)
        {
            if (string.IsNullOrWhiteSpace(selectedObjectId)) return false;
            if (registerUndo) RegisterSourceUndo("Move Map Object");
            var ok = session.MoveObject(selectedObjectId, target, out status);
            statusMessageType = ok ? MessageType.Info : MessageType.Warning;
            if (ok)
            {
                SetDirtyAndPreview();
                SceneView.RepaintAll();
            }

            return ok;
        }

        // Mouse-down in Select Object mode both picks and arms a move, so a designer can grab an object and
        // slide it in one gesture. Picking nothing simply clears the selection and arms no drag.
        private void BeginObjectDrag(int controlId, HexCoord coord)
        {
            SelectPlacedObjectAt(coord, out status);
            statusMessageType = MessageType.Info;
            Repaint();
            if (string.IsNullOrWhiteSpace(selectedObjectId)) return;

            // The undo group opens on the first tile the object actually crosses, not here — a plain click
            // that only selects must not leave a "Move Map Object" entry the designer can undo to nothing.
            objectDragUndoGroup = -1;
            GUIUtility.hotControl = controlId;
            objectDragActive = true;
            objectDragMoved = false;
            objectDragLastCoord = coord;
        }

        private void BeginObjectDragUndoGroupOnce()
        {
            if (objectDragMoved || session.Source == null) return;
            Undo.IncrementCurrentGroup();
            objectDragUndoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Move Map Object");
            RegisterSourceUndo("Move Map Object");
            objectDragMoved = true;
        }

        private void EndObjectDrag()
        {
            if (objectDragUndoGroup >= 0)
            {
                Undo.CollapseUndoOperations(objectDragUndoGroup);
                objectDragUndoGroup = -1;
            }

            objectDragActive = false;
            GUIUtility.hotControl = 0;
            if (objectDragMoved)
            {
                objectDragMoved = false;
                SetDirtyAndPreview();
            }
        }

        private void DrawObjectSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Map Object Brush", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            var nextObjectType = (HexMapObjectType)EditorGUILayout.EnumPopup(
                Content("Object Type", "Changes this brush UI to show only fields relevant to the selected object type."),
                session.ActiveObjectType);
            if (nextObjectType != session.ActiveObjectType)
            {
                session.ApplyObjectTypeDefaults(nextObjectType);
            }

            DrawActiveObjectTypeInventory();
  
            if (session.ActiveObjectType == HexMapObjectType.MonsterSpawn)
            {
                DrawMonsterSpawnBrush();
                EditorGUI.indentLevel--;
                return;
            }

            if (session.ActiveObjectType == HexMapObjectType.PlayerSpawn)
            {
                DrawPlayerSpawnBrush();
                EditorGUI.indentLevel--;
                return;
            }

            if (session.ActiveObjectType == HexMapObjectType.ObjectiveMarker)
            {
                DrawObjectiveMarkerBrush();
                EditorGUI.indentLevel--;
                return;
            }

            if (session.ActiveObjectType == HexMapObjectType.EventTrigger)
            {
                DrawEventTriggerBrush();
                EditorGUI.indentLevel--;
                return;
            }

            if (session.ActiveObjectType == HexMapObjectType.Trap)
            {
                DrawTrapObjectBrush();
                EditorGUI.indentLevel--;
                return;
            }

            if (HexSparseMapEditorSession.IsCameraPointType(session.ActiveObjectType))
            {
                DrawCameraPointBrush(session.ActiveObjectType);
                EditorGUI.indentLevel--;
                return;
            }

              DrawPrefabObjectBrush();
              EditorGUI.indentLevel--;
          }

          private void DrawActiveObjectTypeInventory()
          {
              var placedCount = session.CountPlacedObjectType(session.ActiveObjectType);
              EditorGUILayout.LabelField(
                  Content("Placed In Source", "Number of currently authored entries of the selected Object Type in the selected Sparse Source."),
                  new GUIContent(session.Source == null ? "(no source)" : placedCount.ToString()));

              using (new EditorGUI.DisabledScope(session.Source == null || placedCount == 0))
              {
                  if (GUILayout.Button(Content(
                      $"Delete All {session.ActiveObjectType} ({placedCount})",
                      "Deletes every authored entry of the selected Object Type from the current Sparse Source. Monster spawns also remove their unshared patrol areas.")))
                  {
                      if (EditorUtility.DisplayDialog(
                          "Delete Object Type",
                          $"Delete all {placedCount} {session.ActiveObjectType} object(s) from the current sparse source?",
                          "Delete",
                          "Cancel"))
                      {
                          Undo.RegisterCompleteObjectUndo(session.Source, $"Delete {session.ActiveObjectType} Objects");
                          var ok = session.DeleteObjectType(session.ActiveObjectType, out status);
                          statusMessageType = ok ? MessageType.Info : MessageType.Warning;
                          if (ok)
                          {
                              selectedObjectId = null;
                              objectEditId = null;
                              SetDirtyAndPreview();
                          }
                      }
                  }
              }
          }
  
          private void DrawMonsterSpawnBrush()
        {
            session.ActiveObjectId = string.Empty;
            DrawSpawnerPresetDropdown();
            if (!string.IsNullOrWhiteSpace(session.ActiveSpawnerPresetId))
            {
                EditorGUILayout.LabelField(
                    Content("Linked Preset", "Placed monster spawns store this preset id (authoring provenance). Changing the monster id, role, or purpose unlinks it."),
                    new GUIContent(session.ActiveSpawnerPresetId));
            }

            EditorGUI.BeginChangeCheck();
            session.ActiveObjectRef = DrawMonsterIdDropdown("Monster Id", session.ActiveObjectRef);
            DrawObjectDefinitionPalette();
            session.ActiveObjectPatrolAreaId = DrawSpawnerPatrolAreaIdControl(session.ActiveObjectPatrolAreaId);
            session.ActivePatrolAreaRadius = Mathf.Max(0, EditorGUILayout.IntField(
                Content("Patrol Radius", "Radius used to generate/update this spawner's patrol area when the spawner is placed."),
                session.ActivePatrolAreaRadius));

            var activeObjectRole = session.ActiveObjectRole;
            var activeObjectPurpose = session.ActiveObjectPurpose;
            DrawContextualObjectMetadataFields(session.ActiveObjectType, ref activeObjectRole, ref activeObjectPurpose);
            session.ActiveObjectRole = activeObjectRole;
            session.ActiveObjectPurpose = activeObjectPurpose;
            if (EditorGUI.EndChangeCheck())
            {
                session.ActiveSpawnerPresetId = string.Empty;
            }

            session.ActiveObjectBlocksMovement = false;
            session.ActiveObjectBlocksVision = false;
            session.ActiveObjectInteractable = false;
            session.ActiveObjectFootprintOffsets = new[] { new HexCoord(0, 0) };
            session.ActiveObjectVisualScaleMultiplier = Vector3.one;

            EditorGUILayout.LabelField(
                Content("Placement", "Scene placement writes the selected/custom patrol area id to the spawn and generates that area around the clicked tile. Blank uses patrol-{objectId}."),
                new GUIContent(string.IsNullOrWhiteSpace(session.ActiveObjectPatrolAreaId)
                    ? "Auto patrol area id"
                    : $"Patrol: {session.ActiveObjectPatrolAreaId}"));
            EditorGUILayout.LabelField("Placement Rotation", $"{session.ActiveYawDegrees:0.#}° (shared Rotation UI)");
        }

        private void DrawPlayerSpawnBrush()
        {
            session.ActiveObjectId = string.Empty;
            session.ActiveObjectRef = string.Empty;
            session.ActiveObjectPatrolAreaId = string.Empty;

            var activeObjectRole = session.ActiveObjectRole;
            var activeObjectPurpose = session.ActiveObjectPurpose;
            DrawContextualObjectMetadataFields(session.ActiveObjectType, ref activeObjectRole, ref activeObjectPurpose);
            session.ActiveObjectRole = activeObjectRole;
            session.ActiveObjectPurpose = activeObjectPurpose;

            session.ActiveObjectBlocksMovement = false;
            session.ActiveObjectBlocksVision = false;
            session.ActiveObjectInteractable = false;
            session.ActiveObjectFootprintOffsets = new[] { new HexCoord(0, 0) };
            session.ActiveObjectVisualScaleMultiplier = Vector3.one;

            EditorGUILayout.LabelField(Content("Object Ref", "PlayerSpawn does not need a prefab/object reference."), new GUIContent("Not required"));
            EditorGUILayout.LabelField("Placement Rotation", $"{session.ActiveYawDegrees:0.#}° (shared Rotation UI)");
        }

        private void DrawObjectiveMarkerBrush()
        {
            session.ActiveObjectId = string.Empty;
            session.ActiveObjectRef = EditorGUILayout.TextField(
                Content("Objective Id", "Identifier that game/objective binding can use to find this marker."),
                session.ActiveObjectRef ?? string.Empty);

            var activeObjectRole = session.ActiveObjectRole;
            var activeObjectPurpose = session.ActiveObjectPurpose;
            DrawContextualObjectMetadataFields(session.ActiveObjectType, ref activeObjectRole, ref activeObjectPurpose);
            session.ActiveObjectRole = activeObjectRole;
            session.ActiveObjectPurpose = activeObjectPurpose;

            session.ActiveObjectBlocksMovement = EditorGUILayout.Toggle(Content("Blocks Movement", "Whether this marker occupies movement."), session.ActiveObjectBlocksMovement);
            session.ActiveObjectBlocksVision = EditorGUILayout.Toggle(Content("Blocks Vision", "Whether this marker blocks line-of-sight."), session.ActiveObjectBlocksVision);
            session.ActiveObjectInteractable = EditorGUILayout.Toggle(Content("Interactable", "Whether gameplay can interact with this marker."), session.ActiveObjectInteractable);
            session.ActiveObjectFootprintOffsets = new[] { new HexCoord(0, 0) };
            session.ActiveObjectVisualScaleMultiplier = Vector3.one;
            EditorGUILayout.LabelField("Placement Rotation", $"{session.ActiveYawDegrees:0.#}° (shared Rotation UI)");
        }

        private void DrawEventTriggerBrush()
        {
            session.ActiveObjectId = string.Empty;
            session.ActiveObjectRef = EditorGUILayout.TextField(
                Content("Event Id", "Identifier that event binding can use to resolve this trigger."),
                session.ActiveObjectRef ?? string.Empty);
            session.ActiveObjectRole = string.Empty;
            session.ActiveObjectPatrolAreaId = string.Empty;
            session.ActiveObjectBlocksMovement = EditorGUILayout.Toggle(Content("Blocks Movement", "Whether this trigger occupies movement."), session.ActiveObjectBlocksMovement);
            session.ActiveObjectBlocksVision = EditorGUILayout.Toggle(Content("Blocks Vision", "Whether this trigger blocks line-of-sight."), session.ActiveObjectBlocksVision);
            session.ActiveObjectInteractable = EditorGUILayout.Toggle(Content("Interactable", "Whether gameplay can interact with this trigger."), session.ActiveObjectInteractable);
            session.ActiveObjectFootprintOffsets = new[] { new HexCoord(0, 0) };
            session.ActiveObjectVisualScaleMultiplier = Vector3.one;
            EditorGUILayout.LabelField("Placement Rotation", $"{session.ActiveYawDegrees:0.#}° (shared Rotation UI)");
        }

        private void DrawCameraPointBrush(HexMapObjectType objectType)
        {
            bool isIntro = objectType == HexMapObjectType.IntroCameraPoint;
            bool isSingleton = HexSparseMapEditorSession.IsSingletonCameraPointType(objectType);
            session.PrepareCameraPointBrushForNextOrder(objectType);

            EditorGUILayout.LabelField(
                Content("Object Ref", "Runtime marker id for camera path points. This brush uses a non-rendered marker ref."),
                new GUIContent(session.ActiveObjectRef));
            if (isSingleton)
            {
                EditorGUILayout.HelpBox(
                    "Where the victory cut-3 camera COMES TO REST around the memory stone. Place one cell on " +
                    "the side of the stone you want the closing shot taken from — the orbit winds back from " +
                    "there, so keep the whole arc behind this point clear of buildings. Only one is used; " +
                    "placing again replaces it. Without one, the shot falls back to the nearest backdrop " +
                    "landmark, which is arbitrary on a map that has none.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.LabelField(
                    Content("Next Order", $"The next placed {objectType} will receive this order in Role. Existing point roles/object ids are scanned for their first number."),
                    new GUIContent(session.ActiveObjectRole));
            }
            session.ActiveObjectPurpose = (HexMapPurpose)EditorGUILayout.EnumPopup(
                Content("Enabled Purpose", "Filters this camera path point by map purpose."),
                session.ActiveObjectPurpose);

            if (isIntro)
            {
                session.ActiveObjectCameraSpeedMultiplier = Mathf.Max(0.01f, EditorGUILayout.FloatField(
                    Content("Speed Multiplier", "Playback speed of the dolly segment that STARTS at this point (>0, default 1). Higher = faster."),
                    session.ActiveObjectCameraSpeedMultiplier <= 0f ? 1f : session.ActiveObjectCameraSpeedMultiplier));
                session.ActiveObjectCameraDwellSeconds = Mathf.Max(0f, EditorGUILayout.FloatField(
                    Content("Dwell Seconds", "Seconds the camera holds still when it ARRIVES at this point (>=0, default 0)."),
                    session.ActiveObjectCameraDwellSeconds));
                session.ActiveObjectCameraStartsNewSegment = EditorGUILayout.Toggle(
                    Content("Starts New Segment (cut)", "Hard-cut to this point and begin a NEW dolly segment here (no connecting move from the previous point). Toggle on before placing the first point of a new zone."),
                    session.ActiveObjectCameraStartsNewSegment);
            }

            session.ActiveObjectBlocksMovement = false;
            session.ActiveObjectBlocksVision = false;
            session.ActiveObjectInteractable = false;
            session.ActiveObjectFootprintOffsets = new[] { new HexCoord(0, 0) };
            session.ActiveObjectVisualScaleMultiplier = Vector3.one;
            var placedCount = session.CountCameraPoints(objectType);
            EditorGUILayout.LabelField(
                Content("Placement", isSingleton
                    ? "Click a painted cell to set the closing framing. The Scene View marks it magenta."
                    : isIntro
                        ? "Click painted cells to place points. The editor writes Role as 00, 01, 02... and the Scene View marks the authored dolly path teal."
                        : "Click painted cells to place points. The editor writes Role as 00, 01, 02... and the Scene View marks points yellow."),
                new GUIContent(isSingleton
                    ? $"Cut 3 resting framing ({placedCount} placed)"
                    : $"Cinematic path marker ({placedCount} placed)"));

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(session.Source == null || placedCount == 0))
                {
                    if (GUILayout.Button(Content("Remove All Camera Points", $"Deletes every {objectType} from the selected sparse source. Other map objects remain untouched.")))
                    {
                        if (EditorUtility.DisplayDialog(
                            $"Remove {objectType} Points",
                            $"Remove all {placedCount} {objectType} object(s) from this sparse source?",
                            "Remove",
                            "Cancel"))
                        {
                            Undo.RegisterCompleteObjectUndo(session.Source, $"Remove {objectType} Points");
                            var ok = session.RemoveAllCameraPoints(objectType, out status);
                            statusMessageType = ok ? MessageType.Info : MessageType.Warning;
                            if (ok)
                            {
                                selectedObjectId = null;
                                objectEditId = null;
                                SetDirtyAndPreview();
                            }
                        }
                    }
                }
            }
        }

        private void DrawPrefabObjectBrush()
        {
            session.ActiveObjectId = string.Empty;
            session.ActiveObjectRef = MapObjectPrefabCatalog.DrawPrefabIdPopup("Object Prefab", session.ActiveObjectRef);
            DrawObjectDefinitionPalette();
            var activeObjectRole = session.ActiveObjectRole;
            var activeObjectPurpose = session.ActiveObjectPurpose;
            DrawContextualObjectMetadataFields(session.ActiveObjectType, ref activeObjectRole, ref activeObjectPurpose);
            session.ActiveObjectRole = activeObjectRole;
            session.ActiveObjectPurpose = activeObjectPurpose;
            session.ActiveObjectBlocksMovement = EditorGUILayout.Toggle(Content("Blocks Movement", "Default movement collision for this object prefab."), session.ActiveObjectBlocksMovement);
            session.ActiveObjectBlocksVision = EditorGUILayout.Toggle(Content("Blocks Vision", "Default vision/line-of-sight blocker for this object prefab."), session.ActiveObjectBlocksVision);
            session.ActiveObjectInteractable = EditorGUILayout.Toggle(Content("Interactable", "Default interactable state for this object prefab."), session.ActiveObjectInteractable);
            session.ActiveObjectVisualScaleMultiplier = DrawUniformScaleControl(
                Content("Scale", "Uniform visual scale saved with object defaults."),
                session.ActiveObjectVisualScaleMultiplier);
            EditorGUILayout.LabelField(Content("Footprint", "Tiles occupied by this object, relative to its anchor tile."), new GUIContent(FormatFootprint(session.ActiveObjectFootprintOffsets, "0,0")));
            footprintEditMode = EditorGUILayout.ToggleLeft(
                Content("Footprint Edit Mode (Scene click)", "First Scene View click places/selects the anchor object. Subsequent nearby tile clicks toggle occupied footprint tiles; the anchor is kept."),
                footprintEditMode);

            if (GUILayout.Button(Content("Save Object Defaults", "Saves footprint, collision, interaction, and uniform scale as defaults for this selected object prefab.")))
            {
                var ok = SaveObjectDefaultsFromCurrentContext(out status);
                statusMessageType = ok ? MessageType.Info : MessageType.Error;
                if (ok)
                {
                    footprintEditMode = false;
                    activeTool = EditorTool.PlaceObject;
                    selectedObjectId = null;
                    objectEditId = null;
                }
            }

            EditorGUILayout.LabelField("Placement Rotation", $"{session.ActiveYawDegrees:0.#}° (shared Rotation UI)");
        }

        private void DrawTrapObjectBrush()
        {
            session.ActiveObjectId = string.Empty;
            session.ActiveObjectRef = string.Empty;
            session.ActiveObjectRole = string.Empty;
            session.ActiveObjectPatrolAreaId = string.Empty;
            session.ActiveObjectBlocksMovement = false;
            session.ActiveObjectBlocksVision = false;
            session.ActiveObjectInteractable = false;
            session.ActiveObjectFootprintOffsets = new[] { new HexCoord(0, 0) };
            session.ActiveObjectVisualScaleMultiplier = Vector3.one;
            session.ActiveTrapId = string.Empty;

            DrawTrapPresetDropdown();
            if (!string.IsNullOrWhiteSpace(session.ActiveTrapPresetId))
            {
                EditorGUILayout.LabelField(
                    Content("Linked Preset", "Placed traps store this preset id; the preset stays authoritative at map build time. Editing a field below unlinks it."),
                    new GUIContent(session.ActiveTrapPresetId));
            }

            EditorGUILayout.LabelField("Trap Effect Palette", EditorStyles.miniBoldLabel);
            DrawTrapEffectPalette();

            // Manual edits unlink the preset so the hand-tuned inline values are used as-is.
            EditorGUI.BeginChangeCheck();
            session.ActiveTrapRadius = Mathf.Max(0, EditorGUILayout.IntSlider(
                Content("Radius", "0 affects only the stepped tile; N affects tiles within axial distance N."),
                session.ActiveTrapRadius,
                0,
                6));
            session.ActiveTrapEffectKind = (HexTrapEffectKind)EditorGUILayout.EnumPopup("Effect", session.ActiveTrapEffectKind);
            session.ActiveTrapEffectAmount = Mathf.Max(0, EditorGUILayout.IntField(
                Content("Amount", "Damage amount, per-turn DoT amount, or status strength depending on effect."),
                session.ActiveTrapEffectAmount));
            using (new EditorGUI.DisabledScope(!HexSparseMapEditorSession.RequiresDuration(session.ActiveTrapEffectKind)))
            {
                session.ActiveTrapEffectDurationTurns = Mathf.Max(1, EditorGUILayout.IntField(
                    Content("Duration Turns", "Used by Burn, Poison, Stun, Slow, and VisionDown."),
                    session.ActiveTrapEffectDurationTurns));
            }

            session.ActiveTrapAffectsPlayer = EditorGUILayout.Toggle("Affects Player", session.ActiveTrapAffectsPlayer);
            session.ActiveTrapAffectsMonsters = EditorGUILayout.Toggle("Affects Monsters", session.ActiveTrapAffectsMonsters);
            session.ActiveTrapOneShot = EditorGUILayout.Toggle("One Shot", session.ActiveTrapOneShot);
            session.ActiveTrapTriggerOnEnter = EditorGUILayout.Toggle("Trigger On Enter", session.ActiveTrapTriggerOnEnter);
            if (EditorGUI.EndChangeCheck())
            {
                session.ActiveTrapPresetId = string.Empty;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(Content("Generated Id", "Trap id is generated automatically from the placed coordinate."), "(auto)");
            }

            if (session.Source != null)
            {
                EditorGUILayout.LabelField("Placed Traps", session.Source == null ? "0" : session.Source.TrapRefCount.ToString());
                foreach (var trapRef in session.Source.TrapRefs.Where(trapRef => trapRef != null).Take(8))
                {
                    var effect = trapRef.Effects.FirstOrDefault();
                    EditorGUILayout.LabelField($"{trapRef.Coord}", $"{effect?.Kind.ToString() ?? "NoEffect"} r:{trapRef.Radius}");
                }

                if (session.Source.TrapRefCount > 8)
                {
                    EditorGUILayout.LabelField($"??{session.Source.TrapRefCount - 8} more");
                }
            }

            EditorGUILayout.HelpBox("Use Place Object / Erase Object in Scene View. Trap ids are generated automatically.", MessageType.Info);
        }

        private void DrawTrapPresetDropdown()
        {
            var catalog = TrapPresetCatalog.LoadDefault();
            var presets = catalog == null
                ? System.Array.Empty<TrapPresetCatalog.Entry>()
                : catalog.Presets.Where(preset => preset != null && !string.IsNullOrWhiteSpace(preset.PresetId)).ToArray();
            if (presets.Length == 0)
            {
                return;
            }

            var options = new List<string> { "<Apply trap preset...>" };
            options.AddRange(presets.Select(preset => preset.DisplayName));
            var picked = EditorGUILayout.Popup(
                Content("Trap Preset", "Apply a saved trap preset to the fields below. Traps stay stored inline on the map."),
                0,
                options.ToArray());
            if (picked > 0)
            {
                ApplyTrapPreset(presets[picked - 1]);
            }
        }

        private void ApplyTrapPreset(TrapPresetCatalog.Entry preset)
        {
            session.ActiveTrapEffectKind = preset.EffectKind;
            session.ActiveTrapEffectAmount = preset.EffectAmount;
            session.ActiveTrapEffectDurationTurns = HexSparseMapEditorSession.RequiresDuration(preset.EffectKind)
                ? Mathf.Max(1, preset.DurationTurns)
                : 1;
            session.ActiveTrapRadius = preset.Radius;
            session.ActiveTrapAffectsPlayer = preset.AffectsPlayer;
            session.ActiveTrapAffectsMonsters = preset.AffectsMonsters;
            session.ActiveTrapOneShot = preset.OneShot;
            session.ActiveTrapTriggerOnEnter = preset.TriggerOnEnter;
            session.ActiveTrapPresetId = preset.PresetId;
            activeTool = EditorTool.PlaceObject;
            palettePanelMode = PalettePanelMode.Object;
            status = $"Applied trap preset '{preset.PresetId}'.";
            statusMessageType = MessageType.Info;
        }

        private void DrawSpawnerPresetDropdown()
        {
            var catalog = SpawnerPresetCatalog.LoadDefault();
            var presets = catalog == null
                ? System.Array.Empty<SpawnerPresetCatalog.Entry>()
                : catalog.Presets.Where(preset => preset != null && !string.IsNullOrWhiteSpace(preset.PresetId)).ToArray();
            if (presets.Length == 0)
            {
                return;
            }

            var options = new List<string> { "<Apply spawner preset...>" };
            options.AddRange(presets.Select(preset => preset.DisplayName));
            var picked = EditorGUILayout.Popup(
                Content("Spawner Preset", "Apply a saved monster spawn preset. Monster ref must exist in the combat monster catalog."),
                0,
                options.ToArray());
            if (picked > 0)
            {
                ApplySpawnerPreset(presets[picked - 1]);
            }
        }

        private void ApplySpawnerPreset(SpawnerPresetCatalog.Entry preset)
        {
            session.ActiveObjectRef = preset.MonsterRef;
            session.ActiveObjectRole = preset.Role;
            session.ActiveObjectPurpose = preset.EnabledForPurpose;
            session.ActivePatrolAreaRadius = preset.PatrolRadius;
            session.ActiveSpawnerPresetId = preset.PresetId;
            var known = session.IsKnownMonsterId(preset.MonsterRef);
            status = known
                ? $"Applied spawner preset '{preset.PresetId}'."
                : $"Applied spawner preset '{preset.PresetId}' (monster '{preset.MonsterRef}' missing from catalog).";
            statusMessageType = known ? MessageType.Info : MessageType.Warning;
        }

        private void DrawTrapEffectPalette()
        {
            var kinds = new[]
            {
                HexTrapEffectKind.Damage,
                HexTrapEffectKind.Burn,
                HexTrapEffectKind.Poison,
                HexTrapEffectKind.Stun,
                HexTrapEffectKind.Slow,
                HexTrapEffectKind.VisionDown
            };

            const int columns = 3;
            for (var i = 0; i < kinds.Length; i += columns)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (var c = 0; c < columns && i + c < kinds.Length; c++)
                    {
                        var kind = kinds[i + c];
                        var selected = session.ActiveTrapEffectKind == kind;
                        using (new EditorGUILayout.VerticalScope(selected ? EditorStyles.helpBox : GUI.skin.box, GUILayout.Width(92)))
                        {
                            var rect = GUILayoutUtility.GetRect(72f, 44f, GUILayout.Width(72), GUILayout.Height(44));
                            DrawTrapEffectPreview(rect, kind);
                            if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
                            {
                                SelectTrapEffect(kind);
                            }

                            if (GUILayout.Toggle(selected, TrapEffectLabel(kind), EditorStyles.miniButton, GUILayout.Width(72)) && !selected)
                            {
                                SelectTrapEffect(kind);
                            }
                        }
                    }
                }
            }
        }

        private void SelectTrapEffect(HexTrapEffectKind kind)
        {
            session.ActiveTrapEffectKind = kind;
            session.ActiveTrapEffectAmount = DefaultTrapEffectAmount(kind);
            session.ActiveTrapEffectDurationTurns = HexSparseMapEditorSession.RequiresDuration(kind) ? DefaultTrapDuration(kind) : 1;
            session.ActiveTrapPresetId = string.Empty;
            activeTool = EditorTool.PlaceObject;
            palettePanelMode = PalettePanelMode.Object;
            status = $"Selected trap effect '{TrapEffectLabel(kind)}'.";
            statusMessageType = MessageType.Info;
        }

        private static void DrawTrapEffectPreview(Rect rect, HexTrapEffectKind kind)
        {
            EditorGUI.DrawRect(rect, TrapEffectColor(kind));
            GUI.Label(rect, TrapEffectIcon(kind), EditorStyles.centeredGreyMiniLabel);
        }

        private static string TrapEffectLabel(HexTrapEffectKind kind)
        {
            switch (kind)
            {
                case HexTrapEffectKind.VisionDown:
                    return "Vision";
                default:
                    return kind.ToString();
            }
        }

        private static string TrapEffectIcon(HexTrapEffectKind kind)
        {
            switch (kind)
            {
                case HexTrapEffectKind.Damage:
                    return "DMG";
                case HexTrapEffectKind.Burn:
                    return "FIRE";
                case HexTrapEffectKind.Poison:
                    return "POIS";
                case HexTrapEffectKind.Stun:
                    return "STUN";
                case HexTrapEffectKind.Slow:
                    return "SLOW";
                case HexTrapEffectKind.VisionDown:
                    return "FOG";
                default:
                    return kind.ToString();
            }
        }

        private static Color TrapEffectColor(HexTrapEffectKind kind)
        {
            switch (kind)
            {
                case HexTrapEffectKind.Damage:
                    return new Color(0.85f, 0.08f, 0.06f, 1f);
                case HexTrapEffectKind.Burn:
                    return new Color(1f, 0.38f, 0.02f, 1f);
                case HexTrapEffectKind.Poison:
                    return new Color(0.18f, 0.72f, 0.18f, 1f);
                case HexTrapEffectKind.Stun:
                    return new Color(0.98f, 0.86f, 0.12f, 1f);
                case HexTrapEffectKind.Slow:
                    return new Color(0.18f, 0.55f, 1f, 1f);
                case HexTrapEffectKind.VisionDown:
                    return new Color(0.35f, 0.22f, 0.65f, 1f);
                default:
                    return Color.red;
            }
        }

        private static int DefaultTrapEffectAmount(HexTrapEffectKind kind)
        {
            switch (kind)
            {
                case HexTrapEffectKind.Stun:
                case HexTrapEffectKind.Slow:
                case HexTrapEffectKind.VisionDown:
                    return 1;
                default:
                    return 3;
            }
        }

        private static int DefaultTrapDuration(HexTrapEffectKind kind)
        {
            switch (kind)
            {
                case HexTrapEffectKind.Stun:
                    return 1;
                default:
                    return 2;
            }
        }

        private void DrawContextualObjectMetadataFields(HexMapObjectType objectType, ref string role, ref HexMapPurpose purpose)
        {
            if (objectType == HexMapObjectType.MonsterSpawn)
            {
                role = DrawRoleDropdown(
                    Content("Spawn Role", "Monster runtime spawn role. 'boss' marks this spawn as the boss for MemoryStone activation."),
                    role,
                    BuildMonsterSpawnRoleValues(),
                    BuiltInMonsterSpawnRoles,
                    BuiltInMonsterSpawnRoleLabels);
                purpose = (HexMapPurpose)EditorGUILayout.EnumPopup(Content("Enabled Purpose", "Filters/validates this spawn by SmokeMap/PlayableMap purpose."), purpose);
            }
            else if (objectType == HexMapObjectType.PlayerSpawn)
            {
                purpose = (HexMapPurpose)EditorGUILayout.EnumPopup(Content("Enabled Purpose", "Used to validate duplicate start locations per map purpose."), purpose);
            }
            else if (objectType == HexMapObjectType.ObjectiveMarker)
            {
                role = EditorGUILayout.TextField(Content("Display Name", "Display label used by objective binding/UI."), role ?? string.Empty);
            }
            else if (objectType == HexMapObjectType.MemoryStone)
            {
                role = DrawRoleDropdown(
                    Content("MemoryStone Role", "'main' or 'objective' makes this MemoryStone preferred when multiple MemoryStones exist."),
                    role,
                    MemoryStoneRoles,
                    MemoryStoneRoles,
                    MemoryStoneRoleLabels);
            }
            else if (objectType == HexMapObjectType.Building || objectType == HexMapObjectType.Landmark)
            {
                role = DrawRoleDropdown(
                    Content("Object Role", "Set 'landmark' so the stage-intro cinematic sweeps the camera over this object (Building or Landmark) during the opening shot."),
                    role,
                    LandmarkRoles,
                    LandmarkRoles,
                    LandmarkRoleLabels);
            }
            else if (HexSparseMapEditorSession.IsCameraPointType(objectType))
            {
                role = EditorGUILayout.TextField(
                    Content("Camera Point Order", "Order key for the camera dolly path. Use 00, 01, 02..."),
                    role ?? string.Empty);
                purpose = (HexMapPurpose)EditorGUILayout.EnumPopup(
                    Content("Enabled Purpose", "Filters this camera path point by map purpose."),
                    purpose);
            }
        }

        private string[] BuildMonsterSpawnRoleValues()
        {
            var roles = new List<string>(BuiltInMonsterSpawnRoles);
            var catalog = SpawnerPresetCatalog.LoadDefault();
            if (catalog != null)
            {
                foreach (var preset in catalog.Presets)
                {
                    if (preset == null || string.IsNullOrWhiteSpace(preset.Role))
                    {
                        continue;
                    }

                    if (!roles.Contains(preset.Role))
                    {
                        roles.Add(preset.Role);
                    }
                }
            }

            return roles.ToArray();
        }

        private static string DrawRoleDropdown(
            GUIContent label,
            string currentRole,
            IReadOnlyList<string> roleValues,
            IReadOnlyList<string> builtInValues,
            IReadOnlyList<string> builtInLabels)
        {
            currentRole = currentRole ?? string.Empty;
            var values = roleValues == null ? new List<string>() : roleValues.ToList();
            if (!values.Contains(string.Empty))
            {
                values.Insert(0, string.Empty);
            }

            var selected = values.FindIndex(value => string.Equals(value, currentRole, System.StringComparison.Ordinal));
            if (selected < 0)
            {
                values.Add(currentRole);
                selected = values.Count - 1;
            }

            var labels = values.Select(value =>
            {
                var builtInIndex = builtInValues == null
                    ? -1
                    : builtInValues.ToList().FindIndex(builtIn => string.Equals(builtIn, value, System.StringComparison.Ordinal));
                if (builtInIndex >= 0 && builtInLabels != null && builtInIndex < builtInLabels.Count)
                {
                    return builtInLabels[builtInIndex];
                }

                return string.IsNullOrWhiteSpace(value) ? "<None>" : $"{value} (custom)";
            }).ToArray();

            selected = EditorGUILayout.Popup(label, selected, labels);
            return values[Mathf.Clamp(selected, 0, values.Count - 1)];
        }

        private static Vector3 DrawUniformScaleControl(string label, Vector3 current)
        {
            return DrawUniformScaleControl(new GUIContent(label), current);
        }

        private static Vector3 DrawUniformScaleControl(GUIContent label, Vector3 current)
        {
            var scale = Mathf.Max(0.05f, UniformScale(current));
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(label);
                if (GUILayout.Button("-", EditorStyles.miniButtonLeft, GUILayout.Width(28)))
                {
                    scale = Mathf.Max(0.05f, scale - 0.1f);
                }
                scale = Mathf.Max(0.05f, EditorGUILayout.FloatField(scale, GUILayout.Width(58)));
                if (GUILayout.Button("+", EditorStyles.miniButtonRight, GUILayout.Width(28)))
                {
                    scale += 0.1f;
                }
                GUILayout.Label($"x{scale:0.##}", EditorStyles.miniLabel);
            }

            return Vector3.one * scale;
        }

        private static float UniformScale(Vector3 value)
        {
            var x = value.x > 0f ? value.x : 1f;
            var y = value.y > 0f ? value.y : x;
            var z = value.z > 0f ? value.z : x;
            return (x + y + z) / 3f;
        }

        private bool SaveObjectDefaultsFromCurrentContext(out string message)
        {
            if (TryGetSelectedObjectRef(out var selected) &&
                selected.ObjectType == session.ActiveObjectType &&
                string.Equals(selected.ObjectRef, session.ActiveObjectRef, System.StringComparison.OrdinalIgnoreCase))
            {
                SyncActiveObjectBrushFrom(selected);
            }

            var ok = MapObjectPrefabCatalog.SaveDefinitionDefaults(
                session.ActiveObjectRef,
                session.ActiveObjectType,
                session.ActiveObjectBlocksMovement,
                session.ActiveObjectBlocksVision,
                session.ActiveObjectInteractable,
                session.ActiveObjectFootprintOffsets,
                session.ActiveObjectVisualScaleMultiplier,
                out message);
            if (ok)
            {
                message += " Footprint Edit Mode disabled; Place Object now uses these defaults.";
            }

            return ok;
        }

        private void SyncActiveObjectBrushFrom(HexMapObjectRef objectRef)
        {
            if (objectRef == null)
            {
                return;
            }

            session.ActiveObjectType = objectRef.ObjectType;
            session.ActiveObjectRef = objectRef.ObjectRef;
            session.ActiveObjectRole = objectRef.Role;
            session.ActiveObjectPurpose = objectRef.EnabledForPurpose;
            session.ActiveObjectBlocksMovement = objectRef.BlocksMovement;
            session.ActiveObjectBlocksVision = objectRef.BlocksVision;
            session.ActiveObjectInteractable = objectRef.Interactable;
            session.ActiveObjectFootprintOffsets = HexSparseMapEditorSession.NormalizeFootprintOffsets(objectRef.FootprintOffsets);
            session.ActiveObjectVisualScaleMultiplier = objectRef.VisualScaleMultiplier;
        }

        private string DrawMonsterIdDropdown(string label, string currentMonsterId)
        {
            var monsterIds = session.MonsterCatalogMonsterIds.ToList();
            if (monsterIds.Count == 0)
            {
                EditorGUILayout.HelpBox("Monster catalog has no selectable monster ids.", MessageType.Warning);
                return EditorGUILayout.TextField(label, currentMonsterId ?? string.Empty);
            }

            var current = currentMonsterId ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(current) && !monsterIds.Contains(current))
            {
                monsterIds.Add(current);
            }

            var selectedIndex = Mathf.Max(0, monsterIds.IndexOf(string.IsNullOrWhiteSpace(current) ? monsterIds[0] : current));
            var labels = monsterIds
                .Select(id => session.IsKnownMonsterId(id) ? id : $"{id} (missing from catalog)")
                .ToArray();
            var nextIndex = EditorGUILayout.Popup(label, selectedIndex, labels);
            var selected = monsterIds[Mathf.Clamp(nextIndex, 0, monsterIds.Count - 1)];
            EditorGUILayout.LabelField("Monster Catalog", string.IsNullOrWhiteSpace(session.MonsterCatalogSourceId) ? "(none)" : session.MonsterCatalogSourceId);
            if (!session.IsKnownMonsterId(selected))
            {
                EditorGUILayout.HelpBox($"Monster id '{selected}' is not present in the current monster catalog.", MessageType.Warning);
            }

            return selected;
        }


        private void DrawObjectDefinitionPalette()
        {
            if (!MapObjectPrefabCatalog.IsPreviewableObjectType(session.ActiveObjectType))
            {
                return;
            }

            var allDefinitions = MapObjectPrefabCatalog.GetDefinitionsForObjectType(session.ActiveObjectType);
            var definitions = allDefinitions;

            if (definitions.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    session.ActiveObjectType == HexMapObjectType.MonsterSpawn
                        ? "No monster preview prefabs found in the monster catalog."
                        : $"No {session.ActiveObjectType} object prefabs found in the object catalog.",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(
                session.ActiveObjectType == HexMapObjectType.MonsterSpawn ? "Monster Palette" : "Object Palette",
                EditorStyles.miniBoldLabel);

            objectPaletteSearch = DrawPaletteSearchField(objectPaletteSearch, "Search this palette by object name, prefab name, or category");
            definitions = allDefinitions
                .Where(definition => MatchesSearch(
                    objectPaletteSearch,
                    definition.ObjectRef,
                    definition.Prefab != null ? definition.Prefab.name : null,
                    definition.Category))
                .ToList();
            if (definitions.Count == 0)
            {
                EditorGUILayout.HelpBox($"No object matches '{objectPaletteSearch}'.", MessageType.Info);
                return;
            }

            objectPaletteScroll = EditorGUILayout.BeginScrollView(objectPaletteScroll, GUILayout.Height(150));

            // Group by editor palette category (e.g. Building vs Prop) so Prop-flavoured entries show as
            // their own section even though they share the Building object type.
            var groups = definitions
                .GroupBy(definition => definition.Category)
                .OrderBy(group => group.Key, System.StringComparer.OrdinalIgnoreCase)
                .ToList();
            var showGroupHeaders = groups.Count > 1;
            foreach (var group in groups)
            {
                if (showGroupHeaders)
                {
                    EditorGUILayout.LabelField(group.Key, EditorStyles.miniBoldLabel);
                }

                var items = group.ToList();
                const int columns = 3;
                for (var i = 0; i < items.Count; i += columns)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        for (var c = 0; c < columns && i + c < items.Count; c++)
                        {
                            var definition = items[i + c];
                            var selected = string.Equals(session.ActiveObjectRef, definition.ObjectRef, System.StringComparison.OrdinalIgnoreCase);
                            using (new EditorGUILayout.VerticalScope(selected ? EditorStyles.helpBox : GUI.skin.box, GUILayout.Width(92)))
                            {
                                var placedCount = session.CountPlacedObjectDefinition(definition.ObjectType, definition.ObjectRef);
                                var preview = definition.Prefab != null ? AssetPreview.GetAssetPreview(definition.Prefab) ?? AssetPreview.GetMiniThumbnail(definition.Prefab) : null;
                                if (GUILayout.Button(preview, GUILayout.Width(72), GUILayout.Height(54)))
                                {
                                    ApplyObjectDefinition(definition, clearPlacedSelection: true);
                                }

                                var label = placedCount > 0 ? $"{definition.ObjectRef} ({placedCount})" : definition.ObjectRef;
                                var nextSelected = GUILayout.Toggle(selected, label, EditorStyles.miniButton, GUILayout.Width(72));
                                if (nextSelected && !selected)
                                {
                                    ApplyObjectDefinition(definition, clearPlacedSelection: true);
                                }
                            }
                        }
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void ApplyObjectDefinition(MapObjectPrefabCatalog.ObjectDefinition definition, bool clearPlacedSelection = false)
        {
            if (definition == null)
            {
                return;
            }

            if (clearPlacedSelection)
            {
                selectedObjectId = null;
                objectEditId = null;
            }

            session.ActiveObjectType = definition.ObjectType;
            session.ActiveObjectRef = definition.ObjectRef;
            session.ActiveSpawnerPresetId = string.Empty;
            session.ActiveObjectBlocksMovement = definition.BlocksMovement;
            session.ActiveObjectBlocksVision = definition.BlocksVision;
            session.ActiveObjectInteractable = definition.Interactable;
            session.ActiveObjectFootprintOffsets = definition.FootprintOffsets;
            session.ActiveObjectVisualScaleMultiplier = definition.VisualScaleMultiplier;
            activeTool = EditorTool.PlaceObject;
            footprintEditMode = false;
            palettePanelMode = PalettePanelMode.Object;
            status = $"Selected object prefab '{definition.ObjectRef}' for placement.";
            statusMessageType = MessageType.Info;
        }

        private static string FormatFootprint(IEnumerable<HexCoord> offsets, string fallback)
        {
            var normalized = HexSparseMapEditorSession.NormalizeFootprintOffsets(offsets);
            if (normalized == null || normalized.Count == 0)
            {
                return fallback;
            }

            return string.Join(";", normalized.Select(coord => $"{coord.Q},{coord.R}"));
        }

        private static bool TryParseFootprint(string text, out IReadOnlyList<HexCoord> offsets, out string message)
        {
            offsets = null;
            message = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                offsets = new[] { new HexCoord(0, 0) };
                return true;
            }

            var parsed = new List<HexCoord>();
            foreach (var token in text.Split(';'))
            {
                var trimmed = token.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    continue;
                }

                var parts = trimmed.Split(',');
                if (parts.Length != 2 || !int.TryParse(parts[0].Trim(), out var q) || !int.TryParse(parts[1].Trim(), out var r))
                {
                    message = "Footprint must use q,r pairs separated by semicolons, e.g. 0,0;1,0;0,1.";
                    return false;
                }

                parsed.Add(new HexCoord(q, r));
            }

            offsets = HexSparseMapEditorSession.NormalizeFootprintOffsets(parsed);
            return true;
        }

        /// <summary>
        /// 보스 아레나 저작. 결계는 이 영역에서 유도되므로 두 가지가 반드시 맞아야 한다:
        /// 아레나가 보스 스폰 좌표를 포함할 것, 그리고 그 스폰에 바인딩될 것. "Generate Around Selected
        /// Boss Spawn"이 둘을 동시에 보장하므로 기본 경로로 쓴다.
        /// </summary>
        private void DrawBossArenaSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                Content("Boss Arena", "Select a boss MonsterSpawn object and generate the arena around it, or paint cells with Paint Area / Erase Area."),
                EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            if (session.Source == null)
            {
                EditorGUILayout.HelpBox("Select a sparse source before editing boss arenas.", MessageType.Info);
                EditorGUI.indentLevel--;
                return;
            }

            EditorGUILayout.LabelField("Authored Areas", session.Source.AreaRefs.Count.ToString());
            session.ActiveAreaId = DrawAreaIdDropdown("Select Area", session.ActiveAreaId);
            session.ActiveAreaRadius = Mathf.Max(0, EditorGUILayout.IntField(
                new GUIContent("Arena Radius", "생성 반경. 걸을 수 없는 셀은 자동으로 빠진다."),
                session.ActiveAreaRadius));

            if (session.Source.TryGetAreaRef(session.ActiveAreaId, out var activeArea))
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField("Bound Boss Spawn", string.IsNullOrWhiteSpace(activeArea.BossSpawnRefId)
                        ? "(none — barrier can never close)"
                        : activeArea.BossSpawnRefId);
                    EditorGUILayout.TextField("Cells", activeArea.Cells.Count.ToString());
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(selectedObjectId)))
                {
                    if (GUILayout.Button("Generate Around Selected Boss Spawn"))
                    {
                        Undo.RegisterCompleteObjectUndo(session.Source, "Generate Boss Arena");
                        bool ok = session.GenerateBossArenaAroundObject(selectedObjectId, session.ActiveAreaRadius, out status);
                        statusMessageType = ok ? MessageType.Info : MessageType.Error;
                        if (ok) SetDirtyAndPreview();
                    }
                }

                if (GUILayout.Button("Delete Active Area"))
                {
                    Undo.RegisterCompleteObjectUndo(session.Source, "Delete Boss Arena");
                    bool ok = session.Source.RemoveAreaRef(session.ActiveAreaId);
                    status = ok ? $"Deleted area '{session.ActiveAreaId}'." : $"No area '{session.ActiveAreaId}' exists.";
                    statusMessageType = ok ? MessageType.Info : MessageType.Warning;
                    if (ok) SetDirtyAndPreview();
                }
            }

            EditorGUI.indentLevel--;
        }

        private string DrawAreaIdDropdown(string label, string currentAreaId)
        {
            var current = currentAreaId ?? string.Empty;
            if (session.Source == null || session.Source.AreaRefs.Count == 0)
            {
                return EditorGUILayout.TextField(label, current);
            }

            var ids = session.Source.AreaRefs
                .Where(area => area != null && !string.IsNullOrWhiteSpace(area.AreaId))
                .Select(area => area.AreaId)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
            if (!ids.Contains(current) && !string.IsNullOrWhiteSpace(current))
            {
                ids.Insert(0, current);
            }

            var index = Mathf.Max(0, ids.IndexOf(current));
            var next = EditorGUILayout.Popup(label, index, ids.ToArray());
            var selected = ids.Count > 0 ? ids[Mathf.Clamp(next, 0, ids.Count - 1)] : current;
            return EditorGUILayout.TextField("Area Id", selected);
        }

        private void DrawPatrolAreaSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                Content("Patrol Area", "Use Paint Patrol / Erase Patrol in SceneView, or select a MonsterSpawn object and generate an area from its radius."),
                EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            session.ActivePatrolAreaRadius = Mathf.Max(0, EditorGUILayout.IntField("Spawn Radius", session.ActivePatrolAreaRadius));

            if (session.Source != null)
            {
                EditorGUILayout.LabelField("Authored Areas", session.Source.PatrolAreaRefs.Count.ToString());
                session.ActivePatrolAreaId = DrawPatrolAreaIdDropdown("Select Area", session.ActivePatrolAreaId, allowBlank: false);

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(selectedObjectId)))
                    {
                        if (GUILayout.Button("Generate Around Selected Spawn"))
                        {
                            Undo.RegisterCompleteObjectUndo(session.Source, "Generate Patrol Area");
                            bool ok = session.GeneratePatrolAreaAroundObject(selectedObjectId, session.ActivePatrolAreaRadius, out status);
                            statusMessageType = ok ? MessageType.Info : MessageType.Error;
                            if (ok) SetDirtyAndPreview();
                        }
                    }

                    if (GUILayout.Button("Delete Active Area"))
                    {
                        Undo.RegisterCompleteObjectUndo(session.Source, "Delete Patrol Area");
                        bool ok = session.Source.RemovePatrolAreaRef(session.ActivePatrolAreaId);
                        status = ok ? $"Deleted patrol area '{session.ActivePatrolAreaId}'." : $"No patrol area '{session.ActivePatrolAreaId}' exists.";
                        statusMessageType = ok ? MessageType.Info : MessageType.Warning;
                        if (ok) SetDirtyAndPreview();
                    }
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Select a sparse source before editing patrol areas.", MessageType.Info);
            }

            EditorGUI.indentLevel--;
        }

        private string DrawPatrolAreaIdDropdown(string label, string currentPatrolAreaId, bool allowBlank)
        {
            if (session.Source == null || session.Source.PatrolAreaRefs.Count == 0)
            {
                var unavailableCurrent = currentPatrolAreaId ?? string.Empty;
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField(label, string.IsNullOrWhiteSpace(unavailableCurrent) ? "(auto on spawn placement)" : unavailableCurrent);
                }

                return unavailableCurrent;
            }

            var ids = session.Source.PatrolAreaRefs
                .Where(area => area != null && !string.IsNullOrWhiteSpace(area.PatrolAreaId))
                .Select(area => area.PatrolAreaId)
                .Distinct()
                .OrderBy(id => id)
                .ToList();
            if (allowBlank)
            {
                ids.Insert(0, string.Empty);
            }

            var current = currentPatrolAreaId ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(current) && !ids.Contains(current))
            {
                ids.Add(current);
            }

            if (ids.Count == 0)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField(label, string.IsNullOrWhiteSpace(current) ? "(auto on spawn placement)" : current);
                }

                return current;
            }

            var selectedIndex = Mathf.Max(0, ids.IndexOf(current));
            var labels = ids.Select(id => string.IsNullOrWhiteSpace(id) ? "(none)" : id).ToArray();
            var nextIndex = EditorGUILayout.Popup(label, selectedIndex, labels);
            return ids[Mathf.Clamp(nextIndex, 0, ids.Count - 1)];
        }

        private string DrawSpawnerPatrolAreaIdControl(string currentPatrolAreaId)
        {
            var current = currentPatrolAreaId ?? string.Empty;
            var ids = session.Source == null
                ? new List<string>()
                : session.Source.PatrolAreaRefs
                    .Where(area => area != null && !string.IsNullOrWhiteSpace(area.PatrolAreaId))
                    .Select(area => area.PatrolAreaId)
                    .Distinct()
                    .OrderBy(id => id)
                    .ToList();

            if (ids.Count > 0)
            {
                const string autoLabel = "(auto: patrol-{objectId})";
                const string customLabel = "(custom / new id)";
                var options = new List<string> { autoLabel };
                options.AddRange(ids);
                options.Add(customLabel);

                var selectedIndex = string.IsNullOrWhiteSpace(current)
                    ? 0
                    : ids.IndexOf(current) >= 0
                        ? ids.IndexOf(current) + 1
                        : options.Count - 1;
                var nextIndex = EditorGUILayout.Popup(
                    Content("Patrol Area", "Choose an existing patrol area id, or choose custom/new id and type one below. Blank uses patrol-{objectId}."),
                    selectedIndex,
                    options.ToArray());
                if (nextIndex == 0)
                {
                    return string.Empty;
                }

                if (nextIndex > 0 && nextIndex <= ids.Count)
                {
                    return ids[nextIndex - 1];
                }

                return EditorGUILayout.TextField(
                    Content("Custom Patrol Id", "New or custom patrol area id. Placing the spawner generates/updates this patrol area around the clicked tile."),
                    current);
            }

            return EditorGUILayout.TextField(
                Content("Patrol Area Id", "Blank uses patrol-{objectId}. Type an id here to generate/assign a named patrol area when placing this spawner."),
                current);
        }

        private void DrawObjectListSection()
        {
            if (session.Source == null) return;

            EditorGUILayout.Space();
            bool next = EditorGUILayout.Foldout(foldObjectList, $"Object List ({session.Source.ObjectRefCount})", true);
            if (next != foldObjectList)
            {
                foldObjectList = next;
                EditorPrefs.SetBool(PrefKeyObjectList, foldObjectList);
            }
            if (!foldObjectList) return;

            EditorGUI.indentLevel++;
            if (session.Source.ObjectRefCount == 0)
            {
                EditorGUILayout.HelpBox("No map objectRefs are placed yet. Use Place Object to add one on a painted cell.", MessageType.Info);
                ClearSelectedObjectIfMissing();
                EditorGUI.indentLevel--;
                return;
            }

            objectListScroll = EditorGUILayout.BeginScrollView(objectListScroll, GUILayout.Height(140));
            foreach (var objectRef in session.Source.ObjectRefs.Where(objectRef => objectRef != null))
            {
                var isSelected = objectRef.ObjectId == selectedObjectId;
                var label = $"{objectRef.Coord}  {objectRef.ObjectType}  rot:{objectRef.YawDegrees:0.#}°  id:{objectRef.ObjectId}  ref:{objectRef.ObjectRef}";
                if (GUILayout.Toggle(isSelected, label, isSelected ? EditorStyles.toolbarButton : GUI.skin.button))
                {
                    SelectObjectForEditing(objectRef);
                }
            }
            EditorGUILayout.EndScrollView();

            if (TryGetSelectedObjectRef(out var selected))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"Selected {selected.Coord}", EditorStyles.miniBoldLabel);
                objectEditId = selected.ObjectId;
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField("Object Id", objectEditId ?? string.Empty);
                }

                objectEditType = (HexMapObjectType)EditorGUILayout.EnumPopup("Object Type", objectEditType);
                if (objectEditType == HexMapObjectType.MonsterSpawn)
                {
                    objectEditRef = DrawMonsterIdDropdown("Monster Id", objectEditRef);
                    objectEditPatrolAreaId = DrawPatrolAreaIdDropdown("Patrol Area", objectEditPatrolAreaId, allowBlank: true);
                }
                else if (objectEditType == HexMapObjectType.PlayerSpawn)
                {
                    objectEditRef = string.Empty;
                    objectEditPatrolAreaId = string.Empty;
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.TextField("Object Ref", "Not required for PlayerSpawn");
                    }
                }
                else if (HexSparseMapEditorSession.IsCameraPointType(objectEditType))
                {
                    objectEditRef = string.IsNullOrWhiteSpace(objectEditRef)
                        ? HexSparseMapEditorSession.DefaultCameraPointObjectRef(objectEditType)
                        : objectEditRef;
                    objectEditPatrolAreaId = string.Empty;
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.TextField("Object Ref", objectEditRef);
                    }
                }
                else
                {
                    objectEditRef = MapObjectPrefabCatalog.DrawPrefabIdPopup("Object Prefab", objectEditRef);
                }
                DrawContextualObjectMetadataFields(objectEditType, ref objectEditRole, ref objectEditPurpose);
                if (objectEditType == HexMapObjectType.IntroCameraPoint)
                {
                    objectEditCameraSpeedMultiplier = Mathf.Max(0.01f, EditorGUILayout.FloatField(
                        Content("Speed Multiplier", "Playback speed of the dolly segment that STARTS at this point (>0, default 1)."),
                        objectEditCameraSpeedMultiplier <= 0f ? 1f : objectEditCameraSpeedMultiplier));
                    objectEditCameraDwellSeconds = Mathf.Max(0f, EditorGUILayout.FloatField(
                        Content("Dwell Seconds", "Seconds the camera holds still when it ARRIVES at this point (>=0, default 0)."),
                        objectEditCameraDwellSeconds));
                    objectEditCameraStartsNewSegment = EditorGUILayout.Toggle(
                        Content("Starts New Segment (cut)", "Hard-cut to this point and begin a NEW dolly segment here (no connecting move from the previous point). Splits the path into zones."),
                        objectEditCameraStartsNewSegment);
                }
                objectEditBlocksMovement = EditorGUILayout.Toggle("Blocks Movement", objectEditBlocksMovement);
                objectEditBlocksVision = EditorGUILayout.Toggle("Blocks Vision", objectEditBlocksVision);
                objectEditInteractable = EditorGUILayout.Toggle("Interactable", objectEditInteractable);
                objectEditVisualScaleMultiplier = DrawUniformScaleControl("Scale", objectEditVisualScaleMultiplier);
                EditorGUILayout.LabelField("Footprint", FormatFootprint(selected.FootprintOffsets, "0,0"));
                footprintEditMode = EditorGUILayout.ToggleLeft("Footprint Edit Mode (Scene click)", footprintEditMode);

                EditorGUILayout.LabelField("Object Rotation", $"{selected.YawDegrees:0.#}° (shared Rotation UI)");

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Apply"))
                    {
                        Undo.RegisterCompleteObjectUndo(session.Source, "Edit Map Object");
                        bool ok = session.UpdateObjectRefShape(
                            selectedObjectId,
                            objectEditId,
                            objectEditType,
                            objectEditRef,
                            objectEditRole,
                            objectEditPurpose,
                            objectEditBlocksMovement,
                            objectEditBlocksVision,
                            objectEditInteractable,
                            objectEditPatrolAreaId,
                            selected.FootprintOffsets,
                            objectEditVisualScaleMultiplier,
                            out status,
                            cameraSpeedMultiplier: objectEditType == HexMapObjectType.IntroCameraPoint ? objectEditCameraSpeedMultiplier : -1f,
                            cameraDwellSeconds: objectEditType == HexMapObjectType.IntroCameraPoint ? objectEditCameraDwellSeconds : -1f,
                            cameraStartsNewSegment: objectEditType == HexMapObjectType.IntroCameraPoint ? objectEditCameraStartsNewSegment : (bool?)null);
                        statusMessageType = ok ? MessageType.Info : MessageType.Error;
                        if (ok)
                        {
                            selectedObjectId = objectEditId?.Trim();
                            SetDirtyAndPreview();
                        }
                    }

                    if (GUILayout.Button("Focus"))
                    {
                        FocusSceneViewOnObject(selected);
                    }

                    if (GUILayout.Button("Save Defaults"))
                    {
                        SyncActiveObjectBrushFrom(selected);
                        var ok = SaveObjectDefaultsFromCurrentContext(out status);
                        statusMessageType = ok ? MessageType.Info : MessageType.Error;
                        if (ok)
                        {
                            footprintEditMode = false;
                            activeTool = EditorTool.PlaceObject;
                            selectedObjectId = null;
                            objectEditId = null;
                        }
                    }

                    if (GUILayout.Button("Delete"))
                    {
                        Undo.RegisterCompleteObjectUndo(session.Source, "Delete Map Object");
                        bool ok = session.DeleteObjectRef(selectedObjectId, out status);
                        statusMessageType = ok ? MessageType.Info : MessageType.Error;
                        if (ok)
                        {
                            selectedObjectId = null;
                            SetDirtyAndPreview();
                        }
                    }
                }
            }
            else
            {
                ClearSelectedObjectIfMissing();
            }
            EditorGUI.indentLevel--;
        }

        // ── 4. SceneView Settings (Foldout) ──────────────────────────────

        private void DrawSceneViewSettingsSection()
        {
            EditorGUILayout.Space();
            bool next = EditorGUILayout.Foldout(foldSceneView, "SceneView Settings", true);
            if (next != foldSceneView)
            {
                foldSceneView = next;
                EditorPrefs.SetBool(PrefKeySceneView, foldSceneView);
            }
            if (!foldSceneView) return;

            EditorGUI.indentLevel++;
            authoringPlaneY = EditorGUILayout.FloatField("Authoring Plane Y", authoringPlaneY);
            tileRadius      = Mathf.Max(0.01f, EditorGUILayout.FloatField("Tile Radius", tileRadius));
            gridMaxRadius   = Mathf.Max(1,     EditorGUILayout.IntField("Grid Max Radius", gridMaxRadius));
            sceneGridVisible = EditorGUILayout.Toggle("Show Scene Layout", sceneGridVisible);
            EditorPrefs.SetBool(PrefKeySceneGrid, sceneGridVisible);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("Paint Window");
                var label = sceneQuickWindowVisible ? "Hide" : "Show";
                if (GUILayout.Button(label, EditorStyles.miniButton))
                {
                    SetSceneQuickWindowVisible(!sceneQuickWindowVisible);
                }
            }

            EditorGUILayout.LabelField("Camera Presets", EditorStyles.miniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Top",     EditorStyles.miniButtonLeft))
                    ApplyCameraPreset(Quaternion.Euler(90, 0, 0),   orthographic: true);
                if (GUILayout.Button("Iso NE",  EditorStyles.miniButtonMid))
                    ApplyCameraPreset(Quaternion.Euler(45, -45, 0), orthographic: false);
                if (GUILayout.Button("Iso NW",  EditorStyles.miniButtonMid))
                    ApplyCameraPreset(Quaternion.Euler(45, 45, 0),  orthographic: false);
                if (GUILayout.Button("Side N",  EditorStyles.miniButtonMid))
                    ApplyCameraPreset(Quaternion.Euler(0, 180, 0),  orthographic: false);
                if (GUILayout.Button("Side E",  EditorStyles.miniButtonRight))
                    ApplyCameraPreset(Quaternion.Euler(0, -90, 0),  orthographic: false);
            }
            EditorGUI.indentLevel--;
        }

        // ── 5. Source Summary + action buttons ───────────────────────────

        private void DrawSourceSummarySection()
        {
            if (session.Source == null) return;

            var bounds = session.Source.GetPaintedBounds();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Source Summary", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Painted Cells", session.Source.CellCount.ToString());
            EditorGUILayout.LabelField("Map Objects", session.Source.ObjectRefCount.ToString());
            EditorGUILayout.LabelField("Traps", session.Source.TrapRefCount.ToString());
            EditorGUILayout.LabelField("Patrol Areas", session.Source.PatrolAreaRefs.Count.ToString());
            EditorGUILayout.LabelField("Bounds", bounds.ToString());

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Validate"))
                {
                    bool ok = session.Validate(out var messages);
                    status = ok ? "Sparse map validation passed." : string.Join("\n", messages);
                    statusMessageType = ok ? MessageType.Info : MessageType.Warning;
                }
                if (GUILayout.Button("Save Assets"))
                {
                    AssetDatabase.SaveAssets();
                    status = "Assets saved.";
                    statusMessageType = MessageType.Info;
                }
            }
        }

        // ── 6. Live Preview (Foldout) ────────────────────────────────────

        private void DrawLivePreviewSection()
        {
            EditorGUILayout.Space();
            bool next = EditorGUILayout.Foldout(foldLivePreview, "Live Preview", true);
            if (next != foldLivePreview)
            {
                foldLivePreview = next;
                EditorPrefs.SetBool(PrefKeyLivePreview, foldLivePreview);
            }
            if (!foldLivePreview) return;

            EditorGUI.indentLevel++;
            if (livePreviewView == null)
            {
                livePreviewView = FindDefaultLivePreviewView();
            }

            livePreviewView = (AtlasTilePresentationView)EditorGUILayout.ObjectField(
                Content("Preview View", "Defaults to the first AtlasTilePresentationView found in an open scene."),
                livePreviewView, typeof(AtlasTilePresentationView), true);
            livePreviewEnabled = EditorGUILayout.Toggle(Content("Auto Preview on Paint", "Automatically refreshes preview after paint/object edits."), livePreviewEnabled);
            objectPreviewLift = EditorGUILayout.FloatField(Content("Object Preview Lift", "Small vertical offset used when previewing object prefabs."), objectPreviewLift);
            using (new EditorGUI.DisabledScope(livePreviewView == null || session.Source == null))
            {
                if (GUILayout.Button(Content("Preview Now", "Render the current sparse source into the preview view.")))
                    ApplyLivePreview();
            }

            using (new EditorGUI.DisabledScope(livePreviewView == null))
            {
                if (GUILayout.Button(Content("Clear Preview", "Clear rendered tile/object preview children.")))
                    ClearLivePreview();
            }
            EditorGUI.indentLevel--;
        }

        // ── 8. Manual Coordinate Input (Foldout, closed by default) ──────

        private void DrawManualCoordSection()
        {
            EditorGUILayout.Space();
            bool next = EditorGUILayout.Foldout(foldManualCoord, "Manual Coordinate Input  (Advanced)", true);
            if (next != foldManualCoord)
            {
                foldManualCoord = next;
                EditorPrefs.SetBool(PrefKeyManualCoord, foldManualCoord);
            }
            if (!foldManualCoord) return;

            EditorGUI.indentLevel++;
            manualQ = EditorGUILayout.IntField("Q", manualQ);
            manualR = EditorGUILayout.IntField("R", manualR);
            var coord = new HexCoord(manualQ, manualR);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Paint"))
                {
                    if (session.Source != null)
                        Undo.RegisterCompleteObjectUndo(session.Source, "Paint Hex Tile");
                    bool ok = session.Paint(coord, out status,
                        brushHeightOverrideEnabled ? (int?)brushHeightOverride : null);
                    statusMessageType = ok ? MessageType.Info : MessageType.Error;
                    if (ok) SetDirtyAndPreview();
                }
                if (GUILayout.Button("Erase"))
                {
                    if (session.Source != null)
                        Undo.RegisterCompleteObjectUndo(session.Source, "Erase Hex Tile");
                    bool ok = session.Erase(coord, out status);
                    statusMessageType = ok ? MessageType.Info : MessageType.Info;
                    if (ok) SetDirtyAndPreview();
                }
                if (GUILayout.Button("Pick"))
                {
                    session.Pick(coord, out status);
                    statusMessageType = MessageType.Info;
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Paint Patrol"))
                {
                    if (session.Source != null)
                        Undo.RegisterCompleteObjectUndo(session.Source, "Paint Patrol Area");
                    bool ok = session.PaintPatrolArea(coord, out status);
                    statusMessageType = ok ? MessageType.Info : MessageType.Error;
                    if (ok) SetDirtyAndPreview();
                }
                if (GUILayout.Button("Erase Patrol"))
                {
                    if (session.Source != null)
                        Undo.RegisterCompleteObjectUndo(session.Source, "Erase Patrol Area");
                    bool ok = session.ErasePatrolArea(coord, out status);
                    statusMessageType = ok ? MessageType.Info : MessageType.Warning;
                    if (ok) SetDirtyAndPreview();
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate Patrol Radius"))
                {
                    if (session.Source != null)
                        Undo.RegisterCompleteObjectUndo(session.Source, "Generate Patrol Area");
                    bool ok = session.GeneratePatrolAreaAroundCoord(coord, session.ActivePatrolAreaRadius, session.ActivePatrolAreaId, null, out status);
                    statusMessageType = ok ? MessageType.Info : MessageType.Error;
                    if (ok) SetDirtyAndPreview();
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Place Object"))
                {
                    if (session.Source != null)
                        Undo.RegisterCompleteObjectUndo(session.Source, "Place Map Object");
                    bool ok = session.PlaceObject(coord, out status);
                    statusMessageType = ok ? MessageType.Info : MessageType.Error;
                    if (ok) SetDirtyAndPreview();
                }
                if (GUILayout.Button("Erase Object"))
                {
                    if (session.Source != null)
                        Undo.RegisterCompleteObjectUndo(session.Source, "Erase Map Object");
                    bool ok = session.EraseObject(coord, out status);
                    statusMessageType = MessageType.Info;
                    if (ok) SetDirtyAndPreview();
                }
            }
            EditorGUI.indentLevel--;
        }

        // ── SceneView ────────────────────────────────────────────────────

        // Resolves the authoring frame so the SceneView grid/picking overlay matches the
        // live-preview view's local transform and projection. Falls back to the window's
        // own identity-space tileRadius/authoringPlaneY when no preview view is active.
        private void GetAuthoringFrame(out Matrix4x4 frame, out Matrix4x4 worldToLocal, out float radius, out float planeY)
        {
            var v = (livePreviewEnabled && livePreviewView != null) ? livePreviewView : null;
            frame        = v != null ? v.transform.localToWorldMatrix : Matrix4x4.identity;
            worldToLocal = v != null ? v.transform.worldToLocalMatrix : Matrix4x4.identity;
            radius       = v != null ? v.AuthoringHexRadius            : tileRadius;
            planeY       = v != null ? v.AuthoringPlaneLocalY          : authoringPlaneY;
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (session.Source == null)
                return;

            GetAuthoringFrame(out var frameMatrix, out var worldToLocal, out var overlayRadius, out var overlayPlaneY);
            var layout = new HexAxialLayout(overlayRadius);
            var evt = Event.current;

            if (evt.type == EventType.MouseMove ||
                evt.type == EventType.MouseDrag  ||
                evt.type == EventType.MouseDown)
            {
                hoverCoord = GetHoverCoord(sceneView, layout, worldToLocal, overlayPlaneY);
            }

            if (sceneGridVisible)
            {
                var prevMatrix = Handles.matrix;
                Handles.matrix = frameMatrix;
                DrawSceneGrid(layout, overlayPlaneY, worldToLocal);
                Handles.matrix = prevMatrix;
            }
            DrawSceneViewOverlay(sceneView);
            HandleSceneMouseEvents(evt, sceneView, layout, worldToLocal, overlayPlaneY);

            if (evt.type == EventType.MouseMove)
            {
                Repaint();
                sceneView.Repaint();
            }
        }

        private void DrawSceneViewOverlay(SceneView sceneView)
        {
            Handles.BeginGUI();
            string presetLabel  = string.IsNullOrEmpty(session.ActiveTilePresetId)
                ? "(none)" : session.ActiveTilePresetId;
            string heightSuffix = brushHeightOverrideEnabled
                ? $"  H:{brushHeightOverride}"
                : string.Empty;
            string objectSuffix = footprintEditMode
                ? $"  Footprint:{session.ActiveObjectType}"
                : activeTool == EditorTool.PlaceObject
                    ? session.ActiveObjectType == HexMapObjectType.Trap
                        ? $"  Trap:{session.ActiveTrapEffectKind} R{session.ActiveTrapRadius}"
                        : HexSparseMapEditorSession.IsCameraPointType(session.ActiveObjectType)
                            ? $"  CameraPoint:{session.FormatNextCameraPointRole(session.ActiveObjectType)}"
                        : $"  Object:{session.ActiveObjectType}"
                : activeTool == EditorTool.PaintPatrolArea || activeTool == EditorTool.ErasePatrolArea
                    ? $"  Patrol:{session.ActivePatrolAreaId}"
                : activeTool == EditorTool.PaintArea || activeTool == EditorTool.EraseArea
                    ? $"  Area:{session.ActiveAreaId}"
                : activeTool == EditorTool.PaintDisk || activeTool == EditorTool.EraseDisk
                    ? $"  Disk:r{session.ActiveDiskBrushRadius}"
                    : string.Empty;
            GUI.Label(new Rect(10, 10, 320, 20),
                $"[{activeTool}]  {presetLabel}  Rot:{session.ActiveYawDegrees:0.#}°{heightSuffix}{objectSuffix}");

            if (sceneQuickWindowVisible)
            {
                if (paintPanelHost == PanelHost.SceneView)
                {
                    sceneQuickWindowRect = ClampSceneWindowRect(sceneQuickWindowRect, sceneView.position, MinSceneWindowWidth, MinSceneWindowHeight);
                    sceneQuickWindowRect = GUILayout.Window(
                        GetInstanceID(),
                        sceneQuickWindowRect,
                        DrawSceneQuickWindow,
                        "Map Paint",
                        GUILayout.Width(sceneQuickWindowRect.width),
                        GUILayout.Height(sceneQuickWindowRect.height));
                }

                if (palettePanelHost == PanelHost.SceneView)
                {
                    scenePaletteWindowRect = ClampSceneWindowRect(scenePaletteWindowRect, sceneView.position, MinSceneWindowWidth, MinSceneWindowHeight);
                    scenePaletteWindowRect = GUILayout.Window(
                        GetInstanceID() + 1,
                        scenePaletteWindowRect,
                        DrawScenePaletteWindow,
                        "Palette / Object",
                        GUILayout.Width(scenePaletteWindowRect.width),
                        GUILayout.Height(scenePaletteWindowRect.height));
                }
            }
            else if (GUI.Button(new Rect(10, 34, 150, 22), "Show Paint Window"))
            {
                SetSceneQuickWindowVisible(true);
            }

            if (hoverCoord.HasValue)
            {
                float labelY = sceneView.position.height - 40;
                GUI.Label(new Rect(10, labelY, 200, 20),
                    $"Hex Q:{hoverCoord.Value.Q} R:{hoverCoord.Value.R}");
            }
            Handles.EndGUI();
        }

        private void DrawSceneQuickWindow(int id)
        {
            sceneQuickWindowScroll = EditorGUILayout.BeginScrollView(sceneQuickWindowScroll);
            DrawPaintPanelContents();
            EditorGUILayout.EndScrollView();

            sceneQuickWindowRect = DrawSceneWindowResizeGrip(id, sceneQuickWindowRect);
            // Title-bar-only drag so the resize grip and the panel's own controls stay clickable.
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        private void DrawScenePaletteWindow(int id)
        {
            scenePaletteWindowScroll = EditorGUILayout.BeginScrollView(scenePaletteWindowScroll);
            DrawPalettePanelContents();
            EditorGUILayout.EndScrollView();

            scenePaletteWindowRect = DrawSceneWindowResizeGrip(id, scenePaletteWindowRect);
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        /// <summary>Map Paint panel body. Shared by the Scene View window and the dockable window.</summary>
        internal void DrawPaintPanelContents()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(sceneGridVisible ? "Hide Layout" : "Show Layout", EditorStyles.miniButtonLeft))
                {
                    sceneGridVisible = !sceneGridVisible;
                    EditorPrefs.SetBool(PrefKeySceneGrid, sceneGridVisible);
                    SceneView.RepaintAll();
                }

                if (GUILayout.Button(
                        Content(paintPanelHost == PanelHost.SceneView ? "Pop Out" : "Dock To Scene",
                            "Move this panel between the Scene View overlay and its own dockable Editor window."),
                        EditorStyles.miniButtonMid))
                {
                    SetPaintPanelHost(paintPanelHost == PanelHost.SceneView ? PanelHost.DockableWindow : PanelHost.SceneView);
                }

                using (new EditorGUI.DisabledScope(paintPanelHost != PanelHost.SceneView))
                {
                    if (GUILayout.Button("Close", EditorStyles.miniButtonRight))
                    {
                        SetSceneQuickWindowVisible(false);
                    }
                }
            }

            DrawToolSection();
            DrawCompositeSelectionSection();
        }

        /// <summary>Palette / Object panel body. Shared by the Scene View window and the dockable window.</summary>
        internal void DrawPalettePanelContents()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Toggle(palettePanelMode == PalettePanelMode.TilePalette, "Tile Palette", EditorStyles.miniButtonLeft))
                    palettePanelMode = PalettePanelMode.TilePalette;
                if (GUILayout.Toggle(palettePanelMode == PalettePanelMode.Object, "Object", EditorStyles.miniButtonMid))
                    palettePanelMode = PalettePanelMode.Object;
                if (GUILayout.Button(
                        Content(palettePanelHost == PanelHost.SceneView ? "Pop Out" : "Dock To Scene",
                            "Move this panel between the Scene View overlay and its own dockable Editor window."),
                        EditorStyles.miniButtonRight,
                        GUILayout.Width(96)))
                {
                    SetPalettePanelHost(palettePanelHost == PanelHost.SceneView ? PanelHost.DockableWindow : PanelHost.SceneView);
                }
            }

            if (palettePanelMode == PalettePanelMode.TilePalette)
            {
                DrawPaletteSection();
            }
            else
            {
                DrawObjectSection();
            }
        }

        internal void SetPaintPanelHost(PanelHost host)
        {
            paintPanelHost = host;
            EditorPrefs.SetInt(PrefKeyPaintHost, (int)host);
            if (host == PanelHost.DockableWindow)
            {
                HexSparseMapPaintPanelWindow.Open();
            }
            else
            {
                HexSparseMapPaintPanelWindow.CloseIfOpen();
                SetSceneQuickWindowVisible(true);
            }

            SceneView.RepaintAll();
            Repaint();
        }

        internal void SetPalettePanelHost(PanelHost host)
        {
            palettePanelHost = host;
            EditorPrefs.SetInt(PrefKeyPaletteHost, (int)host);
            if (host == PanelHost.DockableWindow)
            {
                HexSparseMapPalettePanelWindow.Open();
            }
            else
            {
                HexSparseMapPalettePanelWindow.CloseIfOpen();
                SetSceneQuickWindowVisible(true);
            }

            SceneView.RepaintAll();
            Repaint();
        }

        /// <summary>
        /// Bottom-right drag handle that resizes a Scene View floating window. GUILayout.Window has no
        /// built-in resize, and the palette in particular is unusable at a size the designer cannot change.
        /// Coordinates here are window-local because this runs inside the window's own GUI callback.
        /// </summary>
        private Rect DrawSceneWindowResizeGrip(int windowId, Rect rect)
        {
            const float gripSize = 14f;
            var grip = new Rect(rect.width - gripSize - 4f, rect.height - gripSize - 4f, gripSize, gripSize);
            GUI.Box(grip, Content(string.Empty, "Drag to resize this panel."));
            EditorGUIUtility.AddCursorRect(grip, MouseCursor.ResizeUpLeft);

            var evt = Event.current;
            switch (evt.type)
            {
                case EventType.MouseDown when evt.button == 0 && grip.Contains(evt.mousePosition):
                    resizingSceneWindowId = windowId;
                    evt.Use();
                    break;
                case EventType.MouseDrag when resizingSceneWindowId == windowId:
                    rect.width = Mathf.Max(MinSceneWindowWidth, rect.width + evt.delta.x);
                    rect.height = Mathf.Max(MinSceneWindowHeight, rect.height + evt.delta.y);
                    evt.Use();
                    SceneView.RepaintAll();
                    break;
                case EventType.MouseUp when resizingSceneWindowId == windowId:
                    resizingSceneWindowId = -1;
                    evt.Use();
                    break;
            }

            return rect;
        }

        private void DrawCompositeSelectionSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Selection / Composite Preset", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Selected Tiles", selectedCoords.Count.ToString());
            selectionMoveMode = EditorGUILayout.ToggleLeft(
                Content("Move selected", "Move: toggle this or hold Shift, then drag a selected tile. Rotate: use the shared Rotation UI above."),
                selectionMoveMode);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(selectedCoords.Count == 0))
                {
                    if (GUILayout.Button("Clear Selection", EditorStyles.miniButtonLeft))
                    {
                        selectedCoords.Clear();
                        SceneView.RepaintAll();
                    }

                    if (GUILayout.Button("Delete", EditorStyles.miniButtonRight))
                    {
                        RegisterSourceUndo("Delete Selected Tiles");
                        var ok = session.DeleteCells(selectedCoords, out status);
                        statusMessageType = ok ? MessageType.Info : MessageType.Warning;
                        if (ok)
                        {
                            selectedCoords.Clear();
                            SetDirtyAndPreview();
                        }
                    }
                }
            }

            using (new EditorGUI.DisabledScope(selectedCoords.Count == 0))
            {
                compositePresetName = EditorGUILayout.TextField("Preset Name", compositePresetName ?? string.Empty);
                if (GUILayout.Button(Content("Save Selection as Preset", "Saves selected painted tiles as a composite preset.")))
                {
                    SaveSelectionAsCompositePreset();
                }
            }

            compositeStampRotationSteps = session.ActiveRotationSteps;
            if (compositePresets != null && compositePresets.Count > 0)
            {
                compositePresetScroll = EditorGUILayout.BeginScrollView(compositePresetScroll, GUILayout.Height(80));
                for (var i = 0; i < compositePresets.Count; i++)
                {
                    var preset = compositePresets[i];
                    if (preset == null) continue;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var active = i == activeCompositePresetIndex;
                        if (GUILayout.Toggle(active, $"{preset.Name} ({preset.Cells.Count})", active ? EditorStyles.toolbarButton : GUI.skin.button))
                        {
                            activeCompositePresetIndex = i;
                        }

                        if (GUILayout.Button("X", GUILayout.Width(24)))
                        {
                            compositePresets.RemoveAt(i);
                            if (activeCompositePresetIndex >= compositePresets.Count) activeCompositePresetIndex = compositePresets.Count - 1;
                            break;
                        }
                    }
                }
                EditorGUILayout.EndScrollView();
            }
            else
            {
                EditorGUILayout.LabelField(Content("Composite Presets", "Select painted tiles, then save them as a composite preset."), new GUIContent("None"));
            }
        }

        private HexCoord? GetHoverCoord(SceneView sceneView, HexAxialLayout layout, Matrix4x4 worldToLocal, float planeY)
        {
            var mousePos = Event.current.mousePosition;
            mousePos.y = sceneView.camera.pixelHeight - mousePos.y;
            var ray = sceneView.camera.ScreenPointToRay(mousePos);
            var origin = worldToLocal.MultiplyPoint3x4(ray.origin);
            var direction = worldToLocal.MultiplyVector(ray.direction);
            if (Mathf.Abs(direction.y) < 1e-6f) return null;
            float t = (planeY - origin.y) / direction.y;
            if (t < 0f) return null;
            var hit = origin + t * direction;
            return layout.WorldToCoord(new Vector2(hit.x, hit.z));
        }

        private void DrawSceneGrid(HexAxialLayout layout, float planeY, Matrix4x4 worldToLocal)
        {
            var camera = SceneView.currentDrawingSceneView?.camera;
            if (camera == null) return;

            bool hasRange = HexSparseMapSceneViewHelper.TryGetViewportAxialRange(
                camera, planeY, layout, 1, gridMaxRadius, worldToLocal,
                out int minQ, out int maxQ, out int minR, out int maxR);

            if (!hasRange) return;

            var painted = new HashSet<HexCoord>();
            foreach (var cell in session.Source.Cells)
                if (cell != null) painted.Add(cell.Coord);
            var objectCoords = new HashSet<HexCoord>();
            var victoryCameraPointCoords = new HashSet<HexCoord>();
            var introCameraPointCoords = new HashSet<HexCoord>();
            var victoryEndCameraPointCoords = new HashSet<HexCoord>();
            foreach (var objectRef in session.Source.ObjectRefs)
            {
                if (objectRef != null)
                {
                    if (objectRef.IsVictoryCameraPoint)
                    {
                        foreach (var occupiedCoord in objectRef.OccupiedCoords)
                            victoryCameraPointCoords.Add(occupiedCoord);
                    }
                    else if (objectRef.IsIntroCameraPoint)
                    {
                        foreach (var occupiedCoord in objectRef.OccupiedCoords)
                            introCameraPointCoords.Add(occupiedCoord);
                    }
                    else if (objectRef.IsVictoryEndCameraPoint)
                    {
                        foreach (var occupiedCoord in objectRef.OccupiedCoords)
                            victoryEndCameraPointCoords.Add(occupiedCoord);
                    }

                    foreach (var occupiedCoord in objectRef.OccupiedCoords)
                        objectCoords.Add(occupiedCoord);
                }
            }
            // Select Object mode needs to show WHICH object is picked, not just that a tile holds one.
            var selectedObjectCoords = new HashSet<HexCoord>();
            if (TryGetSelectedObjectRef(out var highlightedObject))
            {
                foreach (var occupiedCoord in highlightedObject.OccupiedCoords)
                    selectedObjectCoords.Add(occupiedCoord);
            }

            var patrolCoords = new HashSet<HexCoord>();
            foreach (var area in session.Source.PatrolAreaRefs)
                if (area != null && area.PatrolAreaId == session.ActivePatrolAreaId)
                    foreach (var cell in area.Cells)
                        if (cell != null) patrolCoords.Add(cell.Coord);
            // 활성 아레나를 씬에 비춘다. 반경 6짜리 디스크를 인스펙터 좌표 목록으로 검수하는 것은
            // 불가능하므로, 실제로 어디가 결계 안인지는 여기서만 확인할 수 있다.
            var arenaCoords = new HashSet<HexCoord>();
            foreach (var area in session.Source.AreaRefs)
                if (area != null && area.AreaId == session.ActiveAreaId)
                    foreach (var cell in area.Cells)
                        if (cell != null) arenaCoords.Add(cell.Coord);
            var trapCoords = new HashSet<HexCoord>();
            foreach (var trapRef in session.Source.TrapRefs)
                if (trapRef != null)
                    trapCoords.Add(trapRef.Coord);

            for (int rr = minR; rr <= maxR; rr++)
            {
                for (int qq = minQ; qq <= maxQ; qq++)
                {
                    var coord = new HexCoord(qq, rr);
                    layout.GetCorners(coord, hexCorners, planeY);

                    bool isPainted = painted.Contains(coord);
                    bool hasVictoryCameraPoint = victoryCameraPointCoords.Contains(coord);
                    bool hasIntroCameraPoint = introCameraPointCoords.Contains(coord);
                    bool hasVictoryEndCameraPoint = victoryEndCameraPointCoords.Contains(coord);
                    bool hasObject = objectCoords.Contains(coord);
                    bool hasTrap = trapCoords.Contains(coord);
                    bool hasPatrol = patrolCoords.Contains(coord);
                    bool hasArena = arenaCoords.Contains(coord);
                    bool isSelected = selectedCoords.Contains(coord);
                    bool isHover   = hoverCoord.HasValue && hoverCoord.Value == coord;

                    if (isHover)
                    {
                        Handles.color = new Color(0.05f, 0.42f, 1f, 0.72f);
                        Handles.DrawAAConvexPolygon(hexCorners);
                        Handles.color = new Color(0.02f, 0.18f, 0.95f, 1f);
                        System.Array.Copy(hexCorners, hexOutline, 6);
                        hexOutline[6] = hexCorners[0];
                        Handles.DrawAAPolyLine(3f, hexOutline);
                    }
                    else if (selectedObjectCoords.Contains(coord))
                    {
                        Handles.color = new Color(0.35f, 1f, 0.35f, 0.5f);
                        Handles.DrawAAConvexPolygon(hexCorners);
                        Handles.color = new Color(0.15f, 1f, 0.2f, 1f);
                        System.Array.Copy(hexCorners, hexOutline, 6);
                        hexOutline[6] = hexCorners[0];
                        Handles.DrawAAPolyLine(4f, hexOutline);
                    }
                    else if (isSelected)
                    {
                        Handles.color = new Color(0.1f, 0.85f, 1f, 0.46f);
                        Handles.DrawAAConvexPolygon(hexCorners);
                        Handles.color = new Color(0f, 0.95f, 1f, 1f);
                        System.Array.Copy(hexCorners, hexOutline, 6);
                        hexOutline[6] = hexCorners[0];
                        Handles.DrawAAPolyLine(4f, hexOutline);
                    }
                    else if (hasVictoryCameraPoint)
                    {
                        Handles.color = VictoryCameraPointFillColor;
                        Handles.DrawAAConvexPolygon(hexCorners);
                        Handles.color = VictoryCameraPointLineColor;
                        System.Array.Copy(hexCorners, hexOutline, 6);
                        hexOutline[6] = hexCorners[0];
                        Handles.DrawAAPolyLine(3.5f, hexOutline);
                    }
                    else if (hasIntroCameraPoint)
                    {
                        Handles.color = IntroCameraPointFillColor;
                        Handles.DrawAAConvexPolygon(hexCorners);
                        Handles.color = IntroCameraPointLineColor;
                        System.Array.Copy(hexCorners, hexOutline, 6);
                        hexOutline[6] = hexCorners[0];
                        Handles.DrawAAPolyLine(3.5f, hexOutline);
                    }
                    else if (hasVictoryEndCameraPoint)
                    {
                        Handles.color = VictoryEndCameraPointFillColor;
                        Handles.DrawAAConvexPolygon(hexCorners);
                        Handles.color = VictoryEndCameraPointLineColor;
                        System.Array.Copy(hexCorners, hexOutline, 6);
                        hexOutline[6] = hexCorners[0];
                        Handles.DrawAAPolyLine(3.5f, hexOutline);
                    }
                    else if (hasObject)
                    {
                        Handles.color = new Color(1f, 0.55f, 0.05f, 0.5f);
                        Handles.DrawAAConvexPolygon(hexCorners);
                        Handles.color = new Color(1f, 0.35f, 0.02f, 0.95f);
                        System.Array.Copy(hexCorners, hexOutline, 6);
                        hexOutline[6] = hexCorners[0];
                        Handles.DrawAAPolyLine(3f, hexOutline);
                    }
                    else if (hasTrap)
                    {
                        Handles.color = new Color(1f, 0.05f, 0.08f, 0.48f);
                        Handles.DrawAAConvexPolygon(hexCorners);
                        Handles.color = new Color(1f, 0.02f, 0.04f, 0.95f);
                        System.Array.Copy(hexCorners, hexOutline, 6);
                        hexOutline[6] = hexCorners[0];
                        Handles.DrawAAPolyLine(3f, hexOutline);
                    }
                    else if (hasPatrol)
                    {
                        Handles.color = new Color(0.65f, 0.2f, 1f, 0.38f);
                        Handles.DrawAAConvexPolygon(hexCorners);
                        Handles.color = new Color(0.55f, 0.1f, 0.95f, 0.9f);
                        System.Array.Copy(hexCorners, hexOutline, 6);
                        hexOutline[6] = hexCorners[0];
                        Handles.DrawAAPolyLine(2.5f, hexOutline);
                    }
                    else if (hasArena)
                    {
                        // 런타임 결계 오버레이와 같은 보스 레드 계열로 칠해, 저작 화면에서 본 모양과
                        // 판에서 보이는 모양이 같은 색으로 읽히게 한다.
                        Handles.color = new Color(1f, 0.18f, 0.10f, 0.30f);
                        Handles.DrawAAConvexPolygon(hexCorners);
                        Handles.color = new Color(1f, 0.30f, 0.12f, 0.9f);
                        System.Array.Copy(hexCorners, hexOutline, 6);
                        hexOutline[6] = hexCorners[0];
                        Handles.DrawAAPolyLine(2.5f, hexOutline);
                    }
                    else if (isPainted)
                    {
                        Handles.color = new Color(0.3f, 0.7f, 0.3f, 0.45f);
                        Handles.DrawAAConvexPolygon(hexCorners);
                        Handles.color = new Color(0.2f, 0.6f, 0.2f, 0.8f);
                        System.Array.Copy(hexCorners, hexOutline, 6);
                        hexOutline[6] = hexCorners[0];
                        Handles.DrawAAPolyLine(2f, hexOutline);
                    }
                    else
                    {
                        Handles.color = new Color(0.5f, 0.5f, 0.5f, 0.12f);
                        System.Array.Copy(hexCorners, hexOutline, 6);
                        hexOutline[6] = hexCorners[0];
                        Handles.DrawAAPolyLine(1f, hexOutline);
                    }
                }
            }

            DrawIntroCameraPointPath(layout, planeY);
        }

        // Draws the authored intro dolly path: an ordered polyline through IntroCameraPoint
        // markers with direction arrows, playback-order labels, and dwell rings.
        private void DrawIntroCameraPointPath(HexAxialLayout layout, float planeY)
        {
            if (session.Source == null) return;

            var points = new List<(int order, HexCoord coord, float dwell, bool cut)>();
            foreach (var objectRef in session.Source.ObjectRefs)
            {
                if (objectRef == null || !objectRef.IsIntroCameraPoint) continue;
                points.Add((ResolveCameraPointSortKey(objectRef), objectRef.Coord, objectRef.CameraDwellSeconds, objectRef.CameraStartsNewSegment));
            }
            if (points.Count == 0) return;

            points.Sort((a, b) =>
            {
                int c = a.order.CompareTo(b.order);
                if (c != 0) return c;
                c = a.coord.Q.CompareTo(b.coord.Q);
                return c != 0 ? c : a.coord.R.CompareTo(b.coord.R);
            });

            var centers = new Vector3[points.Count];
            for (int i = 0; i < points.Count; i++)
                centers[i] = CoordCenter(layout, points[i].coord, planeY);

            // Connect consecutive points, but NOT across a zone boundary: a point flagged 'starts new
            // segment' hard-cuts, so no line is drawn from the previous point into it.
            Handles.color = IntroCameraPointLineColor;
            for (int i = 0; i < centers.Length - 1; i++)
            {
                if (points[i + 1].cut) continue;
                Handles.DrawAAPolyLine(3.5f, centers[i], centers[i + 1]);
                DrawIntroPathArrow(centers[i], centers[i + 1]);
            }

            var labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = IntroCameraPointLineColor }
            };
            for (int i = 0; i < points.Count; i++)
            {
                Handles.Label(centers[i] + new Vector3(0.12f, 0.12f, 0.12f), i.ToString("00"), labelStyle);
                if (points[i].dwell > 0f)
                {
                    Handles.color = IntroCameraPointLineColor;
                    Handles.DrawWireDisc(centers[i], Vector3.up, 0.35f + Mathf.Min(points[i].dwell, 3f) * 0.12f);
                }
                // A zone-start marker: an amber ring at every hard-cut point (and the first point, which is
                // always a segment start), so the authored zone boundaries read at a glance.
                if (points[i].cut || i == 0)
                {
                    Handles.color = IntroCameraPointCutColor;
                    Handles.DrawWireDisc(centers[i], Vector3.up, 0.55f);
                    Handles.DrawWireDisc(centers[i], Vector3.up, 0.62f);
                }
            }
        }

        private Vector3 CoordCenter(HexAxialLayout layout, HexCoord coord, float planeY)
        {
            layout.GetCorners(coord, hexCorners, planeY);
            var sum = Vector3.zero;
            for (int i = 0; i < 6; i++) sum += hexCorners[i];
            return sum / 6f;
        }

        private static void DrawIntroPathArrow(Vector3 from, Vector3 to)
        {
            var dir = to - from;
            var len = dir.magnitude;
            if (len < 0.0001f) return;
            dir /= len;
            var mid = (from + to) * 0.5f;
            var right = Vector3.Cross(Vector3.up, dir).normalized;
            const float s = 0.3f;
            var tip = mid + dir * s;
            var back = mid - dir * (s * 0.5f);
            Handles.DrawAAPolyLine(3f, tip, back + right * (s * 0.7f));
            Handles.DrawAAPolyLine(3f, tip, back - right * (s * 0.7f));
        }

        private static int ResolveCameraPointSortKey(HexMapObjectRef objectRef)
        {
            if (TryParseFirstIntegerLocal(objectRef.Role, out var value) ||
                TryParseFirstIntegerLocal(objectRef.ObjectRef, out value) ||
                TryParseFirstIntegerLocal(objectRef.ObjectId, out value))
            {
                return value;
            }
            return int.MaxValue;
        }

        private static bool TryParseFirstIntegerLocal(string value, out int result)
        {
            result = 0;
            if (string.IsNullOrWhiteSpace(value)) return false;
            int i = 0;
            while (i < value.Length && !char.IsDigit(value[i])) i++;
            if (i >= value.Length) return false;
            int start = i;
            while (i < value.Length && char.IsDigit(value[i])) i++;
            return int.TryParse(value.Substring(start, i - start), out result);
        }

        private static HexCoord ResolveClickedFootprintOffset(HexMapObjectRef selected, HexCoord clickedCoord)
        {
            var delta = new HexCoord(clickedCoord.Q - selected.Coord.Q, clickedCoord.R - selected.Coord.R);
            var inverse = delta.RotateSteps(6 - selected.RotationSteps);
            var inverseOccupied = selected.Coord + inverse.RotateSteps(selected.RotationSteps);
            if (inverseOccupied == clickedCoord)
            {
                return inverse;
            }

            return delta;
        }

        private void HandleFootprintEditClick(HexCoord coord)
        {
            if (session.Source == null)
            {
                status = "Select a sparse map authoring source before editing footprint.";
                statusMessageType = MessageType.Error;
                return;
            }

            RegisterSourceUndo("Edit Object Footprint");
            if (!TryGetSelectedObjectRef(out var selected) || selected.ObjectType != session.ActiveObjectType || !string.Equals(selected.ObjectRef, session.ActiveObjectRef, System.StringComparison.OrdinalIgnoreCase))
            {
                var previousFootprint = session.ActiveObjectFootprintOffsets;
                session.ActiveObjectFootprintOffsets = new[] { new HexCoord(0, 0) };
                var placed = session.PlaceObject(coord, out status);
                session.ActiveObjectFootprintOffsets = previousFootprint;
                if (!placed)
                {
                    statusMessageType = MessageType.Error;
                    return;
                }

                if (session.Source.TryGetObjectRefAt(coord, session.ActiveObjectType, out var placedRef))
                {
                    SelectObjectForEditing(placedRef);
                    SyncActiveObjectBrushFrom(placedRef);
                }

                statusMessageType = MessageType.Info;
                SetDirtyAndPreview();
                return;
            }

            var localOffset = ResolveClickedFootprintOffset(selected, coord);
            var nextFootprint = selected.FootprintOffsets.ToList();
            if (localOffset.Q == 0 && localOffset.R == 0)
            {
                status = "Anchor tile stays in the footprint.";
                statusMessageType = MessageType.Info;
                return;
            }

            if (nextFootprint.Contains(localOffset))
            {
                nextFootprint.Remove(localOffset);
            }
            else
            {
                nextFootprint.Add(localOffset);
            }

            var ok = session.UpdateObjectRefShape(
                selected.ObjectId,
                selected.ObjectId,
                selected.ObjectType,
                selected.ObjectRef,
                selected.Role,
                selected.EnabledForPurpose,
                selected.BlocksMovement,
                selected.BlocksVision,
                selected.Interactable,
                selected.PatrolAreaId,
                nextFootprint,
                selected.VisualScaleMultiplier,
                out status);
            statusMessageType = ok ? MessageType.Info : MessageType.Error;
            if (ok)
            {
                SetDirtyAndPreview();
                if (session.Source.TryGetObjectRef(selected.ObjectId, out var updated))
                {
                    SelectObjectForEditing(updated);
                    SyncActiveObjectBrushFrom(updated);
                }
            }
        }

        private static Rect ClampSceneWindowRect(Rect rect, Rect sceneRect, float minWidth, float minHeight)
        {
            rect.width = Mathf.Max(minWidth, rect.width);
            rect.height = Mathf.Max(minHeight, rect.height);
            rect.x = Mathf.Clamp(rect.x, 4f, Mathf.Max(4f, sceneRect.width - rect.width - 4f));
            rect.y = Mathf.Clamp(rect.y, 24f, Mathf.Max(24f, sceneRect.height - rect.height - 4f));
            return rect;
        }

        private void HandleSceneMouseEvents(Event evt, SceneView sceneView, HexAxialLayout layout, Matrix4x4 worldToLocal, float planeY)
        {
            int controlId = GUIUtility.GetControlID(FocusType.Passive);

            switch (evt.type)
            {
                case EventType.MouseDown when evt.button == 0 && !evt.alt:
                    if (hoverCoord.HasValue)
                    {
                        if (footprintEditMode)
                        {
                            HandleFootprintEditClick(hoverCoord.Value);
                            evt.Use();
                            break;
                        }

                        if (activeTool == EditorTool.SelectObject)
                        {
                            BeginObjectDrag(controlId, hoverCoord.Value);
                            evt.Use();
                            break;
                        }

                        if (ShouldStartSelectionMove(evt, hoverCoord.Value))
                        {
                            BeginSelectionDrag(controlId, hoverCoord.Value);
                            evt.Use();
                            break;
                        }

                        if (evt.shift && selectedCoords.Count > 0)
                        {
                            PasteSelectionAt(hoverCoord.Value);
                            evt.Use();
                            break;
                        }

                        BeginBrushUndoGroup();
                        GUIUtility.hotControl = controlId;
                        brushStrokeActive = true;
                        brushStrokeChanged = false;
                        brushStrokeCoords.Clear();
                        ProcessBrushAt(hoverCoord.Value);
                        evt.Use();
                    }
                    break;

                case EventType.MouseDrag when objectDragActive && evt.button == 0:
                    hoverCoord = GetHoverCoord(sceneView, layout, worldToLocal, planeY);
                    if (hoverCoord.HasValue && hoverCoord.Value != objectDragLastCoord)
                    {
                        BeginObjectDragUndoGroupOnce();
                        if (MoveSelectedObjectTo(hoverCoord.Value, registerUndo: false))
                        {
                            objectDragLastCoord = hoverCoord.Value;
                        }
                    }
                    SceneView.RepaintAll();
                    evt.Use();
                    break;

                case EventType.MouseDrag when selectionDragActive && evt.button == 0:
                    hoverCoord = GetHoverCoord(sceneView, layout, worldToLocal, planeY);
                    if (hoverCoord.HasValue && hoverCoord.Value != selectionDragLastCoord)
                    {
                        var delta = new HexCoord(hoverCoord.Value.Q - selectionDragLastCoord.Q, hoverCoord.Value.R - selectionDragLastCoord.R);
                        MoveSelection(delta, registerUndo: false);
                        selectionDragLastCoord = hoverCoord.Value;
                    }
                    SceneView.RepaintAll();
                    evt.Use();
                    break;

                case EventType.MouseDrag when brushStrokeActive && evt.button == 0:
                    hoverCoord = GetHoverCoord(sceneView, layout, worldToLocal, planeY);
                    if (hoverCoord.HasValue)
                        ProcessBrushAt(hoverCoord.Value);
                    SceneView.RepaintAll();
                    evt.Use();
                    break;

                case EventType.MouseUp when evt.button == 0:
                    if (objectDragActive)
                    {
                        EndObjectDrag();
                        evt.Use();
                    }
                    else if (selectionDragActive)
                    {
                        EndSelectionDrag();
                        evt.Use();
                    }
                    else if (brushStrokeActive)
                    {
                        EndBrushUndoGroup();
                        brushStrokeActive = false;
                        brushStrokeCoords.Clear();
                        GUIUtility.hotControl = 0;
                        evt.Use();
                    }
                    break;
            }
        }

        private void ProcessBrushAt(HexCoord coord)
        {
            if (brushStrokeCoords.Contains(coord)) return;
            brushStrokeCoords.Add(coord);

            string msg;
            bool changed = false;
            switch (activeTool)
            {
                case EditorTool.Paint:
                    changed = session.Paint(coord, out msg,
                        brushHeightOverrideEnabled ? (int?)brushHeightOverride : null);
                    status = msg;
                    statusMessageType = changed ? MessageType.Info : MessageType.Error;
                    break;
                case EditorTool.Erase:
                    changed = EraseAt(coord, out msg);
                    status = msg;
                    statusMessageType = MessageType.Info;
                    break;
                case EditorTool.Pick:
                    session.Pick(coord, out msg);
                    status = msg;
                    statusMessageType = MessageType.Info;
                    activeTool = EditorTool.Paint;
                    break;
                case EditorTool.Select:
                    changed = ToggleSelection(coord, out msg);
                    status = msg;
                    statusMessageType = MessageType.Info;
                    break;
                case EditorTool.StampComposite:
                    changed = StampActiveCompositePreset(coord, out msg);
                    status = msg;
                    statusMessageType = changed ? MessageType.Info : MessageType.Warning;
                    break;
                case EditorTool.PlaceObject:
                    changed = session.PlaceObject(coord, out msg);
                    status = msg;
                    statusMessageType = changed ? MessageType.Info : MessageType.Error;
                    break;
                case EditorTool.EraseObject:
                    changed = session.EraseObject(coord, out msg);
                    status = msg;
                    statusMessageType = MessageType.Info;
                    break;
                case EditorTool.PaintPatrolArea:
                    changed = session.PaintPatrolArea(coord, out msg);
                    status = msg;
                    statusMessageType = changed ? MessageType.Info : MessageType.Error;
                    break;
                case EditorTool.ErasePatrolArea:
                    changed = session.ErasePatrolArea(coord, out msg);
                    status = msg;
                    statusMessageType = changed ? MessageType.Info : MessageType.Warning;
                    break;
                case EditorTool.PaintDisk:
                    changed = session.PaintDisk(coord, out msg);
                    status = msg;
                    statusMessageType = changed ? MessageType.Info : MessageType.Error;
                    break;
                case EditorTool.EraseDisk:
                    changed = session.EraseDisk(coord, out msg);
                    status = msg;
                    statusMessageType = changed ? MessageType.Info : MessageType.Warning;
                    break;
                case EditorTool.PaintArea:
                    changed = session.PaintArea(coord, out msg);
                    status = msg;
                    statusMessageType = changed ? MessageType.Info : MessageType.Error;
                    break;
                case EditorTool.EraseArea:
                    changed = session.EraseArea(coord, out msg);
                    status = msg;
                    statusMessageType = changed ? MessageType.Info : MessageType.Warning;
                    break;
            }

            if (changed)
            {
                brushStrokeChanged = true;
                SetDirtyAndMaybePreview(!brushStrokeActive);
            }
            Repaint();
        }

        private bool ToggleSelection(HexCoord coord, out string message)
        {
            if (session.Source == null || !session.Source.TryGetCell(coord, out _))
            {
                message = $"No painted tile at {coord} to select.";
                return false;
            }

            if (!selectedCoords.Add(coord))
            {
                selectedCoords.Remove(coord);
                message = $"Deselected {coord}.";
            }
            else
            {
                message = $"Selected {coord}.";
            }

            SceneView.RepaintAll();
            return false;
        }

        /// <summary>
        /// Picks whichever placed object covers <paramref name="coord"/> — footprint included, so clicking any
        /// tile of a multi-hex building selects it — and adopts its rotation into the brush so the shared
        /// Rotation controls start from where the object already is instead of snapping it on first use.
        /// </summary>
        private bool SelectPlacedObjectAt(HexCoord coord, out string message)
        {
            if (session.Source == null)
            {
                message = "Select a sparse map authoring source before selecting objects.";
                return false;
            }

            var hit = session.Source.ObjectRefs
                .Where(objectRef => objectRef != null)
                .FirstOrDefault(objectRef => objectRef.Coord == coord || objectRef.OccupiedCoords.Contains(coord));
            if (hit == null)
            {
                selectedObjectId = null;
                message = $"No placed map object at {coord}.";
                SceneView.RepaintAll();
                return false;
            }

            SelectObjectForEditing(hit);
            message = $"Selected map object '{hit.ObjectId}' ({hit.ObjectType}) at {hit.Coord}.";
            SceneView.RepaintAll();
            return false;
        }

        private bool EraseAt(HexCoord center, out string message)
        {
            var removed = 0;
            foreach (var coord in CoordsInRadius(center, eraseBrushRadius))
            {
                if (session.Erase(coord, out _))
                {
                    selectedCoords.Remove(coord);
                    removed++;
                }
            }

            message = removed > 0
                ? $"Erased {removed} tile(s) at {center} radius {eraseBrushRadius}."
                : $"No sparse cell exists at {center} radius {eraseBrushRadius}.";
            return removed > 0;
        }

        private bool StampActiveCompositePreset(HexCoord coord, out string message)
        {
            var preset = GetActiveCompositePreset();
            if (preset == null || preset.Cells.Count == 0)
            {
                message = "Select or save a composite preset before stamping.";
                return false;
            }

            return session.StampCompositeCells(coord, preset.ToAuthoringCells(), compositeStampRotationSteps, out message);
        }

        private void BeginBrushUndoGroup()
        {
            brushStrokeUndoGroup = -1;
            if (session.Source == null || !IsUndoableSceneTool(activeTool))
            {
                return;
            }

            Undo.IncrementCurrentGroup();
            brushStrokeUndoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(GetUndoName(activeTool));
            RegisterSourceUndo(GetUndoName(activeTool));
        }

        private void EndBrushUndoGroup()
        {
            if (brushStrokeUndoGroup >= 0)
            {
                Undo.CollapseUndoOperations(brushStrokeUndoGroup);
                brushStrokeUndoGroup = -1;
            }

            if (brushStrokeChanged)
            {
                SetDirtyAndPreview();
                brushStrokeChanged = false;
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────

        // deltaSteps is how far the brush rotation just turned. Tile selections rotate by that delta (their
        // coords move with it); a placed object instead takes the brush's absolute yaw, which is what the
        // free-angle slider needs — a fine-angle-only change has deltaSteps 0 and must still reach the object.
        private void ApplyUnifiedRotationStep(int deltaSteps)
        {
            var steps = WrapRotationSteps(deltaSteps);

            if (activeTool == EditorTool.Select && selectedCoords.Count > 0)
            {
                if (steps != 0) RotateSelection(steps);
                return;
            }

            if ((activeTool == EditorTool.SelectObject || activeTool == EditorTool.PlaceObject) &&
                TryGetSelectedObjectRef(out var selected))
            {
                RegisterSourceUndo("Rotate Map Object");
                var ok = session.SetObjectRotation(
                    selected.ObjectId,
                    session.ActiveRotationSteps,
                    session.ActiveRotationFineDegrees,
                    out status);
                statusMessageType = ok ? MessageType.Info : MessageType.Warning;
                if (ok) SetDirtyAndPreview();
            }
        }

        private bool ShouldStartSelectionMove(Event evt, HexCoord coord)
        {
            return activeTool == EditorTool.Select &&
                   selectedCoords.Count > 0 &&
                   selectedCoords.Contains(coord) &&
                   (selectionMoveMode || evt.shift);
        }

        private void BeginSelectionDrag(int controlId, HexCoord coord)
        {
            selectionDragUndoGroup = -1;
            if (session.Source != null)
            {
                Undo.IncrementCurrentGroup();
                selectionDragUndoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Drag Move Selected Tiles");
                RegisterSourceUndo("Drag Move Selected Tiles");
            }

            GUIUtility.hotControl = controlId;
            selectionDragActive = true;
            selectionDragLastCoord = coord;
            status = "Drag selected tiles to move them.";
            statusMessageType = MessageType.Info;
        }

        private void EndSelectionDrag()
        {
            if (selectionDragUndoGroup >= 0)
            {
                Undo.CollapseUndoOperations(selectionDragUndoGroup);
                selectionDragUndoGroup = -1;
            }

            selectionDragActive = false;
            GUIUtility.hotControl = 0;
            SetDirtyAndPreview();
        }

        private void PasteSelectionAt(HexCoord anchorCoord)
        {
            var anchor = GetSelectionAnchor();
            var cells = session.CaptureCompositeCells(selectedCoords, anchor);
            if (cells.Count == 0)
            {
                status = "Select painted tiles before Shift+click paste.";
                statusMessageType = MessageType.Warning;
                return;
            }

            RegisterSourceUndo("Paste Selected Tiles");
            var ok = session.StampCompositeCells(anchorCoord, cells, 0, out status);
            statusMessageType = ok ? MessageType.Info : MessageType.Warning;
            if (ok) SetDirtyAndPreview();
        }

        private void MoveSelection(HexCoord delta, bool registerUndo = true)
        {
            if (delta.Q == 0 && delta.R == 0) return;
            if (registerUndo) RegisterSourceUndo("Move Selected Tiles");
            var oldCoords = selectedCoords.ToArray();
            var ok = session.MoveCells(oldCoords, delta, out status);
            statusMessageType = ok ? MessageType.Info : MessageType.Warning;
            if (!ok) return;
            selectedCoords.Clear();
            foreach (var coord in oldCoords)
            {
                selectedCoords.Add(new HexCoord(coord.Q + delta.Q, coord.R + delta.R));
            }
            SetDirtyAndPreview();
        }

        private void RotateSelection(int steps)
        {
            var anchor = GetSelectionAnchor();
            RegisterSourceUndo("Rotate Selected Tiles");
            var oldCoords = selectedCoords.ToArray();
            var ok = session.RotateCells(oldCoords, anchor, steps, out status);
            statusMessageType = ok ? MessageType.Info : MessageType.Warning;
            if (!ok) return;
            selectedCoords.Clear();
            foreach (var coord in oldCoords)
            {
                var offset = new HexCoord(coord.Q - anchor.Q, coord.R - anchor.R);
                var rotated = HexSparseMapEditorSession.RotateOffset(offset, steps);
                selectedCoords.Add(new HexCoord(anchor.Q + rotated.Q, anchor.R + rotated.R));
            }
            SetDirtyAndPreview();
        }

        private HexCoord GetSelectionAnchor()
        {
            return selectedCoords.Count == 0 ? default : selectedCoords.OrderBy(coord => coord).First();
        }

        private void SaveSelectionAsCompositePreset()
        {
            var anchor = GetSelectionAnchor();
            var cells = session.CaptureCompositeCells(selectedCoords, anchor);
            if (cells.Count == 0)
            {
                status = "Select at least one painted tile before saving a composite preset.";
                statusMessageType = MessageType.Warning;
                return;
            }

            if (compositePresets == null)
                compositePresets = new List<CompositeTilePreset>();
            var preset = CompositeTilePreset.FromAuthoringCells(
                string.IsNullOrWhiteSpace(compositePresetName) ? $"Composite {compositePresets.Count + 1}" : compositePresetName.Trim(),
                cells);
            compositePresets.Add(preset);
            activeCompositePresetIndex = compositePresets.Count - 1;
            activeTool = EditorTool.StampComposite;
            status = $"Saved composite preset '{preset.Name}' with {preset.Cells.Count} tile(s).";
            statusMessageType = MessageType.Info;
        }

        private CompositeTilePreset GetActiveCompositePreset()
        {
            if (compositePresets == null || compositePresets.Count == 0)
                return null;
            if (activeCompositePresetIndex < 0 || activeCompositePresetIndex >= compositePresets.Count)
                activeCompositePresetIndex = 0;
            return compositePresets[activeCompositePresetIndex];
        }

        private static IEnumerable<HexCoord> CoordsInRadius(HexCoord center, int radius)
        {
            radius = Mathf.Max(0, radius);
            for (var dq = -radius; dq <= radius; dq++)
            {
                var minDr = Mathf.Max(-radius, -dq - radius);
                var maxDr = Mathf.Min(radius, -dq + radius);
                for (var dr = minDr; dr <= maxDr; dr++)
                {
                    yield return new HexCoord(center.Q + dq, center.R + dr);
                }
            }
        }

        private void RegisterSourceUndo(string name)
        {
            if (session.Source == null)
            {
                return;
            }

            Undo.RegisterCompleteObjectUndo(session.Source, name);
        }

        private void SetSceneQuickWindowVisible(bool visible)
        {
            sceneQuickWindowVisible = visible;
            EditorPrefs.SetBool(PrefKeyQuickWindow, sceneQuickWindowVisible);
            SceneView.RepaintAll();
            Repaint();
        }

        private static int WrapRotationSteps(int value)
        {
            var wrapped = value % 6;
            return wrapped < 0 ? wrapped + 6 : wrapped;
        }

        private static bool IsUndoableSceneTool(EditorTool tool)
        {
            return tool == EditorTool.Paint ||
                   tool == EditorTool.Erase ||
                   tool == EditorTool.StampComposite ||
                   tool == EditorTool.PlaceObject ||
                   tool == EditorTool.EraseObject ||
                   tool == EditorTool.PaintPatrolArea ||
                   tool == EditorTool.ErasePatrolArea ||
                   tool == EditorTool.PaintDisk ||
                   tool == EditorTool.EraseDisk ||
                   tool == EditorTool.PaintArea ||
                   tool == EditorTool.EraseArea;
        }

        private static string GetUndoName(EditorTool tool)
        {
            switch (tool)
            {
                case EditorTool.Paint:
                    return "Paint Hex Tiles";
                case EditorTool.Erase:
                    return "Erase Hex Tiles";
                case EditorTool.StampComposite:
                    return "Stamp Composite Preset";
                case EditorTool.PlaceObject:
                    return "Place Map Objects";
                case EditorTool.EraseObject:
                    return "Erase Map Objects";
                case EditorTool.PaintPatrolArea:
                    return "Paint Patrol Area";
                case EditorTool.ErasePatrolArea:
                    return "Erase Patrol Area";
                case EditorTool.PaintDisk:
                    return "Paint Hex Disk";
                case EditorTool.EraseDisk:
                    return "Erase Hex Disk";
                case EditorTool.PaintArea:
                    return "Paint Area";
                case EditorTool.EraseArea:
                    return "Erase Area";
                default:
                    return "Map Editor Change";
            }
        }

        private void SelectObjectForEditing(HexMapObjectRef objectRef)
        {
            if (objectRef == null) return;
            selectedObjectId = objectRef.ObjectId;
            objectEditId = objectRef.ObjectId;
            objectEditType = objectRef.ObjectType;
            objectEditRef = objectRef.ObjectRef;
            objectEditRole = objectRef.Role;
            objectEditPurpose = objectRef.EnabledForPurpose;
            objectEditBlocksMovement = objectRef.BlocksMovement;
            objectEditBlocksVision = objectRef.BlocksVision;
            objectEditInteractable = objectRef.Interactable;
            objectEditPatrolAreaId = objectRef.PatrolAreaId;
            objectEditVisualScaleMultiplier = objectRef.VisualScaleMultiplier;
            objectEditCameraSpeedMultiplier = objectRef.CameraSpeedMultiplier;
            objectEditCameraDwellSeconds = objectRef.CameraDwellSeconds;
            objectEditCameraStartsNewSegment = objectRef.CameraStartsNewSegment;
            SyncActiveObjectBrushFrom(objectRef);
            session.ActiveRotationSteps = objectRef.RotationSteps;
            session.ActiveRotationFineDegrees = objectRef.RotationFineDegrees;
        }

        private bool TryGetSelectedObjectRef(out HexMapObjectRef objectRef)
        {
            objectRef = null;
            return session.Source != null && session.Source.TryGetObjectRef(selectedObjectId, out objectRef);
        }

        private void ClearSelectedObjectIfMissing()
        {
            if (session.Source == null || string.IsNullOrWhiteSpace(selectedObjectId) || session.Source.TryGetObjectRef(selectedObjectId, out _))
            {
                return;
            }

            selectedObjectId = null;
        }

        private void FocusSceneViewOnObject(HexMapObjectRef objectRef)
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null || objectRef == null) return;
            GetAuthoringFrame(out var frame, out _, out var radius, out var planeY);
            var layout = new HexAxialLayout(radius);
            var local = layout.CoordToWorld(objectRef.Coord);
            var world = frame.MultiplyPoint3x4(new Vector3(local.x, planeY, local.y));
            sv.LookAt(world, sv.rotation, Mathf.Max(2f, radius * 4f));
            sv.Repaint();
        }

        private void ApplyCameraPreset(Quaternion rotation, bool orthographic)
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null) return;
            sv.orthographic = orthographic;
            sv.LookAt(GetPaintedCenter(), rotation);
        }

        private Vector3 GetPaintedCenter()
        {
            GetAuthoringFrame(out var frame, out _, out var radius, out var planeY);
            if (session.Source != null && !session.Source.GetPaintedBounds().IsEmpty)
            {
                var bounds = session.Source.GetPaintedBounds();
                var layout = new HexAxialLayout(radius);
                var center = layout.CoordToWorld(
                    new HexCoord((bounds.MinQ + bounds.MaxQ) / 2, (bounds.MinR + bounds.MaxR) / 2));
                return frame.MultiplyPoint3x4(new Vector3(center.x, planeY, center.y));
            }
            return frame.MultiplyPoint3x4(new Vector3(0, planeY, 0));
        }

        private void SetDirtyAndPreview()
        {
            SetDirtyAndMaybePreview(true);
        }

        private void SetDirtyAndMaybePreview(bool previewNow)
        {
            if (session.Source != null)
                EditorUtility.SetDirty(session.Source);
            if (previewNow && livePreviewEnabled)
                ApplyLivePreview();
            SceneView.RepaintAll();
        }

        private void ApplyLivePreview()
        {
            if (livePreviewView == null || session.Source == null) return;
            ClearLivePreviewObjects();
            livePreviewView.Render(BuildLivePreviewMapData());
            RenderLivePreviewObjects();
        }

        private void ClearLivePreview()
        {
            ClearLivePreviewObjects();
            if (livePreviewView != null)
                livePreviewView.Render((HexMapData)null);
        }

        private void RenderLivePreviewObjects()
        {
            if (livePreviewView == null || session.Source == null) return;

            foreach (var objectRef in session.Source.ObjectRefs)
            {
                if (objectRef == null ||
                    !MapObjectPrefabCatalog.IsPreviewableObjectType(objectRef.ObjectType) ||
                    string.IsNullOrWhiteSpace(objectRef.ObjectRef))
                {
                    continue;
                }

                if (!MapObjectPrefabCatalog.TryLoadPrefab(objectRef.ObjectRef, out var prefab))
                {
                    continue;
                }

                var instance = PrefabUtility.InstantiatePrefab(prefab, livePreviewView.transform) as GameObject;
                if (instance == null)
                {
                    instance = Instantiate(prefab, livePreviewView.transform);
                }

                instance.name = $"{LivePreviewObjectPrefix}{objectRef.Coord.Q}_{objectRef.Coord.R}_{objectRef.ObjectId}_{prefab.name}";
                instance.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
                instance.transform.localPosition = livePreviewView.ProjectOverlaySurface(objectRef.Coord) + Vector3.up * Mathf.Max(0f, objectPreviewLift);
                instance.transform.localRotation = Quaternion.Euler(0f, objectRef.YawDegrees, 0f);
                instance.transform.localScale = Vector3.Scale(instance.transform.localScale, objectRef.VisualScaleMultiplier);
                livePreviewObjects.Add(instance);
            }

            foreach (var trapRef in session.Source.TrapRefs)
            {
                if (trapRef == null)
                {
                    continue;
                }

                var marker = CreateTrapPreviewObject(livePreviewView.transform, out var usesTrapPrefab);
                marker.name = $"{LivePreviewObjectPrefix}{trapRef.Coord.Q}_{trapRef.Coord.R}_{trapRef.TrapId}_TrapMarker";
                marker.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
                marker.transform.localPosition = livePreviewView.ProjectOverlaySurface(trapRef.Coord) + Vector3.up * (Mathf.Max(0f, objectPreviewLift) + 0.015f);
                if (!usesTrapPrefab)
                {
                    var scale = 0.28f + Mathf.Max(0, trapRef.Radius) * 0.16f;
                    marker.transform.localScale = new Vector3(scale, 0.035f, scale);
                    var renderer = marker.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        renderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
                        {
                            color = new Color(1f, 0.05f, 0.04f, 0.78f)
                        };
                        renderer.sharedMaterial.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
                    }
                }

                livePreviewObjects.Add(marker);
            }
        }

        private static GameObject CreateTrapPreviewObject(Transform parent, out bool usesTrapPrefab)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TrapPreviewPrefabPath);
            if (prefab == null)
            {
                usesTrapPrefab = false;
                var fallback = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                fallback.transform.SetParent(parent, false);
                return fallback;
            }

            usesTrapPrefab = true;
            var instance = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
            if (instance == null)
            {
                instance = Instantiate(prefab, parent);
            }

            foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }

            return instance;
        }

        private void ClearLivePreviewObjects()
        {
            for (var i = livePreviewObjects.Count - 1; i >= 0; i--)
            {
                if (livePreviewObjects[i] != null)
                {
                    DestroyImmediate(livePreviewObjects[i]);
                }
            }

            livePreviewObjects.Clear();

            if (livePreviewView == null)
            {
                return;
            }

            for (var i = livePreviewView.transform.childCount - 1; i >= 0; i--)
            {
                var child = livePreviewView.transform.GetChild(i);
                if (child != null && child.name.StartsWith(LivePreviewObjectPrefix, System.StringComparison.Ordinal))
                {
                    DestroyImmediate(child.gameObject);
                }
            }
        }

        private HexMapData BuildLivePreviewMapData()
        {
            if (session.Source == null || session.Source.CellCount == 0) return null;

            var cells = new List<SeoulPlayup.Map.Runtime.HexCellData>(session.Source.CellCount);
            foreach (var authored in session.Source.Cells)
            {
                if (authored == null) continue;
                cells.Add(new SeoulPlayup.Map.Runtime.HexCellData(
                    authored.Coord,
                    authored.TilePresetId,
                    authored.TerrainTypeId,
                    1, authored.BaseWalkable, false, null, null, 0,
                    authored.AtlasVisualId,
                    authored.HeightLevel,
                    authored.RotationSteps,
                    authored.EdgeConnectionMask));
            }
            var traps = session.Source.TrapRefs
                .Where(trapRef => trapRef != null)
                .Select(trapRef => trapRef.ToRuntimeTrapData())
                .ToArray();
            return cells.Count > 0 ? new SeoulPlayup.Map.Runtime.HexMapData(cells, trapRefs: traps) : null;
        }
  
        [System.Serializable]
        private sealed class CompositeTilePreset
        {
            [SerializeField] private string name;
            [SerializeField] private List<CompositeTileCell> cells = new List<CompositeTileCell>();

            public string Name => string.IsNullOrWhiteSpace(name) ? "Composite Preset" : name;
            public List<CompositeTileCell> Cells => cells ?? (cells = new List<CompositeTileCell>());

            public static CompositeTilePreset FromAuthoringCells(string name, IReadOnlyList<HexSparseMapAuthoringCell> sourceCells)
            {
                var preset = new CompositeTilePreset { name = name };
                foreach (var cell in sourceCells)
                {
                    if (cell != null)
                        preset.Cells.Add(CompositeTileCell.FromAuthoringCell(cell));
                }
                return preset;
            }

            public IReadOnlyList<HexSparseMapAuthoringCell> ToAuthoringCells()
            {
                return Cells.Select(cell => cell?.ToAuthoringCell()).Where(cell => cell != null).ToArray();
            }
        }

        [System.Serializable]
        private sealed class CompositeTileCell
        {
            [SerializeField] private int q;
            [SerializeField] private int r;
            [SerializeField] private string tilePresetId;
            [SerializeField] private string terrainTypeId;
            [SerializeField] private string atlasVisualId;
            [SerializeField] private string eventId;
            [SerializeField] private string landmarkId;
            [SerializeField] private int heightLevel;
            [SerializeField] private int rotationSteps;
            [SerializeField] private int edgeConnectionMask;
            [SerializeField] private bool baseWalkable = true;

            public static CompositeTileCell FromAuthoringCell(HexSparseMapAuthoringCell cell)
            {
                return new CompositeTileCell
                {
                    q = cell.Q,
                    r = cell.R,
                    tilePresetId = cell.TilePresetId,
                    terrainTypeId = cell.TerrainTypeId,
                    atlasVisualId = cell.AtlasVisualId,
                    eventId = cell.EventId,
                    landmarkId = cell.LandmarkId,
                    heightLevel = cell.HeightLevel,
                    rotationSteps = cell.RotationSteps,
                    edgeConnectionMask = cell.EdgeConnectionMask,
                    baseWalkable = cell.BaseWalkable
                };
            }

            public HexSparseMapAuthoringCell ToAuthoringCell()
            {
                return new HexSparseMapAuthoringCell(
                    new HexCoord(q, r),
                    tilePresetId,
                    terrainTypeId,
                    atlasVisualId,
                    eventId,
                    landmarkId,
                    heightLevel,
                    rotationSteps,
                    edgeConnectionMask,
                    baseWalkable);
            }
        }
    }
}

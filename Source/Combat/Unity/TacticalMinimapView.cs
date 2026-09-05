using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    public sealed class TacticalMinimapView : MonoBehaviour
    {
        public const string ToggleButtonName = "Tactical Minimap Toggle Button";
        public const string StatusTextName = "Tactical Minimap Status Text";
        public const string CellRootName = "Tactical Minimap Cell Root";
        public const string CellNamePrefix = "Tactical Minimap Cell ";

        private static readonly Color PanelExpandedColor = new Color(0.03f, 0.07f, 0.09f, 0.86f);
        private static readonly Color PanelCollapsedColor = new Color(0.03f, 0.07f, 0.09f, 0.64f);
        private static readonly Color UnknownCellColor = new Color(0.18f, 0.20f, 0.22f, 0.96f);
        private static readonly Color HintedCellColor = new Color(0.30f, 0.36f, 0.40f, 0.98f);
        private static readonly Color RevealedCellColor = new Color(0.45f, 0.54f, 0.55f, 0.98f);
        private static readonly Color ReachableCellColor = new Color(0.08f, 0.72f, 0.82f, 1f);
        private static readonly Color PlayerCellColor = new Color(0.22f, 0.82f, 1f, 1f);
        private static readonly Color EnemyCellColor = new Color(0.96f, 0.26f, 0.22f, 1f);
        private static readonly Color ObjectiveCellColor = new Color(1f, 0.8f, 0.18f, 1f);
        private static readonly Color HexStrokeColor = new Color(0.76f, 0.95f, 1f, 0.78f);
        private static readonly Color UnknownStrokeColor = new Color(0.40f, 0.52f, 0.56f, 0.58f);
        private static readonly Color PrimaryTextColor = new Color(0.88f, 0.95f, 1f, 1f);
        private static readonly Color MutedTextColor = new Color(0.62f, 0.75f, 0.82f, 1f);

        [SerializeField] private bool expanded = true;
        [SerializeField] private RectTransform cellTemplate;

        private RectTransform rect;
        private Image panelImage;
        private Button toggleButton;
        private TMP_Text titleText;
        private TMP_Text statusText;
        private RectTransform cellRoot;
        private static Sprite hexCellSprite;
        private readonly List<CellWidget> cells = new List<CellWidget>();

        public bool IsExpanded => expanded;
        public int RenderedCellCount { get; private set; }
        public int RevealedEnemyMarkerCount { get; private set; }
        public int RevealedObjectiveMarkerCount { get; private set; }
        public int HiddenCellCount { get; private set; }
        public int ReachableMarkerCount { get; private set; }
        public int VisibleHexCellCount { get; private set; }
        public string StatusText => statusText == null ? string.Empty : statusText.text;

        private void Awake()
        {
            EnsureHierarchy();
        }

        public void Refresh(MapCombatController controller)
        {
            EnsureHierarchy();
            LayoutRoot();
            Render(controller);
        }

        public void ToggleExpanded()
        {
            expanded = !expanded;
            LayoutRoot();
        }

        private void EnsureHierarchy()
        {
            rect = transform as RectTransform;

            panelImage = GetComponent<Image>();
            if (rect == null || panelImage == null)
            {
                return;
            }

            panelImage.raycastTarget = false;
            titleText = titleText == null ? FindText("MiniMap Title") : titleText;

            statusText = statusText == null ? FindText(StatusTextName) : statusText;

            var existingBody = transform.Find("MiniMap Body");
            if (existingBody != null)
            {
                existingBody.gameObject.SetActive(false);
            }

            cellRoot = cellRoot == null ? transform.Find(CellRootName) as RectTransform : cellRoot;
            cellTemplate = cellTemplate != null ? cellTemplate : FindCellTemplate();

            toggleButton = toggleButton == null ? GetComponentsInChildren<Button>(includeInactive: true).FirstOrDefault(button => button.name == ToggleButtonName) : toggleButton;
            if (toggleButton == null)
            {
                return;
            }

            toggleButton.onClick.RemoveAllListeners();
            toggleButton.onClick.AddListener(ToggleExpanded);
        }

        private TMP_Text FindText(string objectName)
        {
            return GetComponentsInChildren<TMP_Text>(includeInactive: true)
                .FirstOrDefault(text => text.name == objectName);
        }

        private void LayoutRoot()
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(18f, 22f);
            rect.sizeDelta = expanded ? new Vector2(278f, 174f) : new Vector2(172f, 34f);
            panelImage.color = expanded ? PanelExpandedColor : PanelCollapsedColor;

            if (titleText == null || statusText == null || cellRoot == null || toggleButton == null)
            {
                return;
            }

            titleText.text = expanded ? "TACTICAL MAP" : "TACTICAL MAP  (collapsed)";
            LayoutText(titleText, new Vector2(12f, -8f), expanded ? new Vector2(132f, 18f) : new Vector2(126f, 18f), expanded ? 14f : 11f, TextAlignmentOptions.TopLeft);
            LayoutText(statusText, new Vector2(12f, -148f), new Vector2(254f, 18f), 10f, TextAlignmentOptions.TopLeft);
            statusText.gameObject.SetActive(expanded);

            cellRoot.gameObject.SetActive(expanded);
            cellRoot.anchorMin = new Vector2(0f, 1f);
            cellRoot.anchorMax = new Vector2(0f, 1f);
            cellRoot.pivot = new Vector2(0f, 1f);
            cellRoot.anchoredPosition = new Vector2(14f, -30f);
            cellRoot.sizeDelta = new Vector2(250f, 112f);

            var toggleRect = toggleButton.GetComponent<RectTransform>();
            toggleRect.anchorMin = new Vector2(1f, 1f);
            toggleRect.anchorMax = new Vector2(1f, 1f);
            toggleRect.pivot = new Vector2(1f, 1f);
            toggleRect.anchoredPosition = new Vector2(-8f, -7f);
            toggleRect.sizeDelta = new Vector2(50f, 16f);
            var label = toggleButton.GetComponentInChildren<TMP_Text>();
            if (label != null)
            {
                label.text = expanded ? "접기" : "+";
                label.alignment = TextAlignmentOptions.Center;
            }
        }

        private void Render(MapCombatController controller)
        {
            if (statusText == null || cellRoot == null)
            {
                return;
            }

            RenderedCellCount = 0;
            RevealedEnemyMarkerCount = 0;
            RevealedObjectiveMarkerCount = 0;
            HiddenCellCount = 0;
            ReachableMarkerCount = 0;
            VisibleHexCellCount = 0;

            if (!expanded)
            {
                return;
            }

            var map = controller == null ? null : controller.LoadedMap;
            var state = controller == null ? null : controller.State;
            if (map == null || state == null || map.Count == 0)
            {
                EnsureCellCount(0);
                statusText.text = "waiting for tactical state";
                return;
            }

            var allCells = map.AllCells.ToArray();
            RenderedCellCount = allCells.Length;
            var objectiveLandmarkId = state.ObjectiveBinding.LandmarkId;
            var selectedMode = controller.SelectedTargetCardKind.HasValue ? controller.SelectedTargetCardKind.Value.ToString() : "None";
            var cellStates = allCells
                .Select(cell => CreateCellState(controller, state, cell, objectiveLandmarkId))
                .ToArray();

            HiddenCellCount = cellStates.Count(cell => cell.Visibility == HexCellVisibility.Unknown);
            RevealedEnemyMarkerCount = cellStates.Count(cell => cell.Enemy);
            RevealedObjectiveMarkerCount = cellStates.Count(cell => cell.Objective);
            ReachableMarkerCount = cellStates.Count(cell => cell.Reachable);

            var viewportCells = SelectViewportCells(cellStates, state.PlayerCoord);
            VisibleHexCellCount = viewportCells.Length;
            EnsureCellCount(viewportCells.Length);
            var hexLayout = CalculateHexLayout(viewportCells.Select(cell => cell.MapCell));

            for (var i = 0; i < cells.Count; i++)
            {
                var active = i < viewportCells.Length;
                cells[i].Root.SetActive(active);
                if (!active)
                {
                    continue;
                }

                var cellState = viewportCells[i];
                var rectTransform = cells[i].Rect;
                var center = hexLayout.GetPosition(cellState.MapCell.Coord);
                var cellColor = GetCellColor(cellState.Visibility, cellState.Reachable, cellState.Player, cellState.Enemy, cellState.Objective);
                rectTransform.sizeDelta = new Vector2(hexLayout.HexWidth, hexLayout.HexHeight);
                rectTransform.anchoredPosition = center;
                cells[i].Background.sprite = GetHexCellSprite();
                cells[i].Background.color = cellColor;
                cells[i].Background.preserveAspect = true;
                cells[i].Background.raycastTarget = false;
                cells[i].Label.text = GetCellLabel(cellState.Visibility, cellState.Reachable, cellState.Player, cellState.Enemy, cellState.Objective);
                cells[i].Label.fontSize = Mathf.Clamp(hexLayout.Radius * 0.88f, 6f, 14f);
                cells[i].Label.color = cellState.Player || cellState.Enemy || cellState.Objective ? Color.black : PrimaryTextColor;
            }

            statusText.text = $"Fog strict · hidden {HiddenCellCount} · hex {VisibleHexCellCount}/{RenderedCellCount} · move {ReachableMarkerCount} · target {selectedMode}";
        }


        private static CellState CreateCellState(MapCombatController controller, CombatState state, HexCellData mapCell, string objectiveLandmarkId)
        {
            var visibility = controller.GetVisibilitySafeCellInfo(mapCell.Coord).Visibility;
            var revealed = visibility == HexCellVisibility.Revealed;
            var player = revealed && mapCell.Coord == state.PlayerCoord;
            var enemy = revealed && state.Monsters.Any(monster => !monster.IsDead && monster.Coord == mapCell.Coord);
            var objective = revealed && !string.IsNullOrEmpty(objectiveLandmarkId) && mapCell.LandmarkId == objectiveLandmarkId;
            var reachable = visibility != HexCellVisibility.Unknown && controller.Reachable.ContainsKey(mapCell.Coord);
            return new CellState(mapCell, visibility, player, enemy, objective, reachable);
        }

        private static CellState[] SelectViewportCells(IEnumerable<CellState> cellsToFilter, HexCoord playerCoord)
        {
            const int tacticalRadius = 4;
            return cellsToFilter
                .Where(cell => cell.MapCell.Coord.DistanceTo(playerCoord) <= tacticalRadius
                    || cell.Visibility != HexCellVisibility.Unknown
                    || cell.Reachable
                    || cell.Player
                    || cell.Enemy
                    || cell.Objective)
                .OrderBy(cell => cell.MapCell.Coord.R)
                .ThenBy(cell => cell.MapCell.Coord.Q)
                .ToArray();
        }

        private static Color GetCellColor(HexCellVisibility visibility, bool reachable, bool player, bool enemy, bool objective)
        {
            if (player)
            {
                return PlayerCellColor;
            }

            if (enemy)
            {
                return EnemyCellColor;
            }

            if (objective)
            {
                return ObjectiveCellColor;
            }

            if (reachable)
            {
                return ReachableCellColor;
            }

            switch (visibility)
            {
                case HexCellVisibility.Revealed:
                    return RevealedCellColor;
                case HexCellVisibility.Hinted:
                    return HintedCellColor;
                default:
                    return UnknownCellColor;
            }
        }

        private static string GetCellLabel(HexCellVisibility visibility, bool reachable, bool player, bool enemy, bool objective)
        {
            if (player)
            {
                return "P";
            }

            if (enemy)
            {
                return "E";
            }

            if (objective)
            {
                return "O";
            }

            if (reachable)
            {
                return "+";
            }

            return visibility == HexCellVisibility.Hinted ? "?" : string.Empty;
        }


        private static HexLayout CalculateHexLayout(IEnumerable<HexCellData> allCells)
        {
            const float contentWidth = 250f;
            const float contentHeight = 112f;
            const float padding = 4f;
            var coordinates = allCells.Select(cell => cell.Coord).ToArray();
            var radius = 8f;
            var bounds = CalculateHexBounds(coordinates, radius);
            if (bounds.width > 0f && bounds.height > 0f)
            {
                var uniformFit = Mathf.Min((contentWidth - padding * 2f) / bounds.width, (contentHeight - padding * 2f) / bounds.height);
                radius = Mathf.Clamp(radius * uniformFit, 5.2f, 12.5f);
            }

            bounds = CalculateHexBounds(coordinates, radius);
            var offset = new Vector2(
                padding - bounds.xMin + Mathf.Max(0f, contentWidth - padding * 2f - bounds.width) * 0.5f,
                -padding - bounds.yMax - Mathf.Max(0f, contentHeight - padding * 2f - bounds.height) * 0.5f);
            return new HexLayout(radius, offset);
        }

        private static Rect CalculateHexBounds(IEnumerable<HexCoord> coordinates, float radius)
        {
            var first = true;
            var xMin = 0f;
            var xMax = 0f;
            var yMin = 0f;
            var yMax = 0f;
            var layout = new HexLayout(radius, Vector2.zero);
            foreach (var coord in coordinates)
            {
                var position = layout.GetPosition(coord);
                var left = position.x - layout.HexWidth * 0.5f;
                var right = position.x + layout.HexWidth * 0.5f;
                var bottom = position.y - layout.HexHeight * 0.5f;
                var top = position.y + layout.HexHeight * 0.5f;
                if (first)
                {
                    xMin = left;
                    xMax = right;
                    yMin = bottom;
                    yMax = top;
                    first = false;
                    continue;
                }

                xMin = Mathf.Min(xMin, left);
                xMax = Mathf.Max(xMax, right);
                yMin = Mathf.Min(yMin, bottom);
                yMax = Mathf.Max(yMax, top);
            }

            return first ? new Rect(0f, 0f, 0f, 0f) : Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private void EnsureCellCount(int count)
        {
            if (cells.Count == 0)
            {
                BindAuthoredCells();
            }

            while (cells.Count < count)
            {
                if (cellTemplate == null)
                {
                    break;
                }

                var index = cells.Count;
                var cellObject = Instantiate(cellTemplate.gameObject);
                cellObject.name = CellNamePrefix + index;
                cellObject.transform.SetParent(cellRoot, false);
                var cellRect = cellObject.GetComponent<RectTransform>();
                cellRect.anchorMin = new Vector2(0f, 1f);
                cellRect.anchorMax = new Vector2(0f, 1f);
                cellRect.pivot = new Vector2(0.5f, 0.5f);
                var background = cellObject.GetComponent<Image>();
                if (background == null)
                {
                    background = cellObject.AddComponent<Image>();
                }
                background.sprite = GetHexCellSprite();
                background.raycastTarget = false;
                var label = cellObject.GetComponentsInChildren<TMP_Text>(includeInactive: true)
                    .FirstOrDefault(text => text.name == "Marker");
                if (label == null)
                {
                    DestroySafely(cellObject);
                    break;
                }
                cells.Add(new CellWidget(cellObject, cellRect, background, label));
            }

            for (var i = 0; i < cells.Count; i++)
            {
                cells[i].Root.SetActive(i < count);
            }
        }


        private static Sprite GetHexCellSprite()
        {
            if (hexCellSprite != null)
            {
                return hexCellSprite;
            }

            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Runtime Tactical Minimap Hex Sprite",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var transparent = new Color32(0, 0, 0, 0);
            var fill = new Color32(255, 255, 255, 255);
            var center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            var radius = size * 0.46f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var point = new Vector2(x, y);
                    texture.SetPixel(x, y, IsInsideHex(point, center, radius) ? fill : transparent);
                }
            }

            texture.Apply(false, true);
            hexCellSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            return hexCellSprite;
        }

        private static bool IsInsideHex(Vector2 point, Vector2 center, float radius)
        {
            var x = Mathf.Abs(point.x - center.x);
            var y = Mathf.Abs(point.y - center.y);
            var apothem = radius * 0.8660254f;
            return x <= radius * 0.5f || x * 0.8660254f + y * 0.5f <= apothem;
        }

        private void BindAuthoredCells()
        {
            if (cellRoot == null)
            {
                return;
            }

            foreach (Transform child in cellRoot)
            {
                var rectTransform = child as RectTransform;
                var background = child.GetComponent<Image>();
                var label = child.GetComponentsInChildren<TMP_Text>(includeInactive: true)
                    .FirstOrDefault(text => text.name == "Marker");
                if (rectTransform == null || background == null || label == null)
                {
                    continue;
                }

                if (cellTemplate == null)
                {
                    cellTemplate = rectTransform;
                }

                cells.Add(new CellWidget(child.gameObject, rectTransform, background, label));
            }
        }

        private RectTransform FindCellTemplate()
        {
            return cellRoot == null
                ? null
                : cellRoot.GetComponentsInChildren<RectTransform>(includeInactive: true)
                    .FirstOrDefault(child => child != cellRoot && child.name.StartsWith(CellNamePrefix, System.StringComparison.Ordinal));
        }

        private static void DestroySafely(GameObject target)
        {
            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }

        private static void LayoutText(TMP_Text text, Vector2 anchoredPosition, Vector2 sizeDelta, float fontSize, TextAlignmentOptions alignment)
        {
            if (text == null)
            {
                return;
            }

            var textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0f, 1f);
            textRect.anchorMax = new Vector2(0f, 1f);
            textRect.pivot = new Vector2(0f, 1f);
            textRect.anchoredPosition = anchoredPosition;
            textRect.sizeDelta = sizeDelta;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Truncate;
        }



        private readonly struct CellState
        {
            public CellState(HexCellData mapCell, HexCellVisibility visibility, bool player, bool enemy, bool objective, bool reachable)
            {
                MapCell = mapCell;
                Visibility = visibility;
                Player = player;
                Enemy = enemy;
                Objective = objective;
                Reachable = reachable;
            }

            public HexCellData MapCell { get; }
            public HexCellVisibility Visibility { get; }
            public bool Player { get; }
            public bool Enemy { get; }
            public bool Objective { get; }
            public bool Reachable { get; }
        }

        public static Vector2 ProjectHexCoordForTests(HexCoord coord, float radius = 1f)
        {
            return HexLayout.ProjectAtlasAligned(coord, radius);
        }

        public static Vector2 GetFittedHexPositionForTests(IEnumerable<HexCoord> coordinates, HexCoord coord)
        {
            var cellsForLayout = (coordinates ?? System.Array.Empty<HexCoord>())
                .Select(testCoord => new HexCellData(testCoord, string.Empty, string.Empty, 1, true, false));
            return CalculateHexLayout(cellsForLayout).GetPosition(coord);
        }

        private readonly struct HexLayout
        {
            public HexLayout(float radius, Vector2 offset)
            {
                Radius = Mathf.Max(0.01f, radius);
                Offset = offset;
            }

            public float Radius { get; }
            private Vector2 Offset { get; }
            public float HexWidth => Radius * 2f;
            public float HexHeight => Radius * 1.7320508f;

            public Vector2 GetPosition(HexCoord coord)
            {
                return ProjectAtlasAligned(coord, Radius) + Offset;
            }

            public static Vector2 ProjectAtlasAligned(HexCoord coord, float radius)
            {
                return new HexAxialLayout(radius).CoordToWorld(coord);
            }
        }

        private sealed class HexCellGraphic : MaskableGraphic
        {
            public Color FillColor { get; set; } = RevealedCellColor;
            public Color StrokeColor { get; set; } = HexStrokeColor;
            public float StrokeWidth { get; set; } = 1f;

            protected override void OnPopulateMesh(VertexHelper vh)
            {
                vh.Clear();
                var rectTransform = transform as RectTransform;
                var rect = rectTransform == null ? Rect.zero : rectTransform.rect;
                var center = rect.center;
                var radius = Mathf.Min(rect.width * 0.5f, rect.height / 1.7320508f);
                if (radius <= 0f)
                {
                    return;
                }

                var innerRadius = Mathf.Max(0f, radius - Mathf.Max(0f, StrokeWidth));
                var outer = CreateHexPoints(center, radius);
                var inner = CreateHexPoints(center, innerRadius);

                AddHex(vh, outer, StrokeColor * color);
                AddHex(vh, inner, FillColor * color);
            }

            private static Vector2[] CreateHexPoints(Vector2 center, float radius)
            {
                var points = new Vector2[6];
                for (var i = 0; i < points.Length; i++)
                {
                    var angle = Mathf.Deg2Rad * (60f * i);
                    points[i] = center + new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
                }

                return points;
            }

            private static void AddHex(VertexHelper vh, IReadOnlyList<Vector2> points, Color32 color)
            {
                var start = vh.currentVertCount;
                var center = Vector2.zero;
                for (var i = 0; i < points.Count; i++)
                {
                    center += points[i];
                }

                center /= points.Count;
                vh.AddVert(center, color, new Vector2(0.5f, 0.5f));
                for (var i = 0; i < points.Count; i++)
                {
                    vh.AddVert(points[i], color, Vector2.zero);
                }

                for (var i = 0; i < points.Count; i++)
                {
                    vh.AddTriangle(start, start + 1 + i, start + 1 + ((i + 1) % points.Count));
                }
            }
        }

        private readonly struct CellWidget
        {
            public CellWidget(GameObject root, RectTransform rect, Image background, TMP_Text label)
            {
                Root = root;
                Rect = rect;
                Background = background;
                Label = label;
            }

            public GameObject Root { get; }
            public RectTransform Rect { get; }
            public Image Background { get; }
            public TMP_Text Label { get; }
        }
    }
}

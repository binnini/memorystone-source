using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.Editor.Combat
{
    public sealed class MonsterAttackShapeDesignerWindow : EditorWindow
    {
        private static readonly HexCoord[] AdjacentRing =
        {
            new HexCoord(1, 0),
            new HexCoord(1, -1),
            new HexCoord(0, -1),
            new HexCoord(-1, 0),
            new HexCoord(-1, 1),
            new HexCoord(0, 1),
        };

        private static readonly string[] KnownShapeIds =
        {
            AttackShapeLibrary.Single,
            AttackShapeLibrary.Line2,
            AttackShapeLibrary.Line3,
            AttackShapeLibrary.Line4,
            AttackShapeLibrary.ConeMid,
            AttackShapeLibrary.ConeWide,
            AttackShapeLibrary.CrossFar,
            AttackShapeLibrary.VSplit,
            AttackShapeLibrary.Pincer,
            AttackShapeLibrary.Tremor,
        };

        private const string GeneratedShapePath = "Assets/Scripts/Combat/Runtime/AttackShapeLibrary.Generated.cs";
        private const string BuiltInOverwriteWarning =
            "Built-in shape IDs are kept as source-controlled runtime aliases. Use a new Shape ID for saved designer shapes.";

        private static readonly HashSet<string> BuiltInShapeIds = new HashSet<string>
        {
            AttackShapeLibrary.Single,
            AttackShapeLibrary.Line2,
            AttackShapeLibrary.Line3,
            AttackShapeLibrary.Line4,
            AttackShapeLibrary.ConeNear,
            AttackShapeLibrary.ConeMid,
            AttackShapeLibrary.ConeWide,
            AttackShapeLibrary.TForward,
            AttackShapeLibrary.CrossNear,
            AttackShapeLibrary.CrossFar,
            AttackShapeLibrary.RingNear,
            AttackShapeLibrary.VSplit,
            AttackShapeLibrary.Pincer,
            AttackShapeLibrary.Tremor,
        };

        private readonly HashSet<HexCoord> editableOffsets = new HashSet<HexCoord>();

        private string shapeId = "new-shape";
        private int selectedKnownShapeIndex;
        private int gridRadius = 4;
        private bool showCoordinateLabels = true;
        private bool showRuntimeAdjacentRing = true;
        private Vector2 outputScroll;
        private int selectedSavedShapeIndex;
        private string[] savedShapeIds = new string[0];

        [MenuItem("Seoul Playup/Combat/Monster Attack Shape Designer")]
        private static void Open()
        {
            GetWindow<MonsterAttackShapeDesignerWindow>("Monster Shape Designer");
        }

        private void OnEnable()
        {
            RefreshSavedShapeIds();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Monster Attack Shape Designer", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Click hexes to toggle shape-specific offsets. Runtime monster shapes always add the adjacent 1-ring around M; blue cells show that locked runtime ring.",
                MessageType.Info);

            DrawToolbar();

            EditorGUILayout.Space(6f);
            var previewRect = GUILayoutUtility.GetRect(420f, 420f, GUILayout.ExpandWidth(true));
            DrawHexEditor(previewRect);
            HandleHexInput(previewRect);

            EditorGUILayout.Space(6f);
            DrawOutput();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                shapeId = EditorGUILayout.TextField("Shape ID", shapeId);
                gridRadius = EditorGUILayout.IntSlider("Grid Radius", gridRadius, 2, 6);
                showCoordinateLabels = EditorGUILayout.Toggle("Show Coord Labels", showCoordinateLabels);
                showRuntimeAdjacentRing = EditorGUILayout.Toggle("Show Runtime 1-Ring", showRuntimeAdjacentRing);

                using (new EditorGUILayout.HorizontalScope())
                {
                    selectedKnownShapeIndex = EditorGUILayout.Popup("Load Built-in", selectedKnownShapeIndex, KnownShapeIds);
                    if (GUILayout.Button("Load", GUILayout.Width(72f)))
                    {
                        LoadKnownShape(KnownShapeIds[selectedKnownShapeIndex]);
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    var savedDisplayIds = savedShapeIds.Length == 0 ? new[] { "(none)" } : savedShapeIds;
                    using (new EditorGUI.DisabledScope(savedShapeIds.Length == 0))
                    {
                        selectedSavedShapeIndex = EditorGUILayout.Popup("Load Saved", selectedSavedShapeIndex, savedDisplayIds);
                        if (GUILayout.Button("Load", GUILayout.Width(72f)))
                        {
                            LoadSavedShape(savedShapeIds[selectedSavedShapeIndex]);
                        }
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Clear Editable Offsets"))
                    {
                        editableOffsets.Clear();
                    }

                    if (GUILayout.Button("Copy C# Shape Entry"))
                    {
                        GUIUtility.systemCopyBuffer = BuildCSharpShapeEntry();
                    }

                    if (GUILayout.Button("Copy Offset List"))
                    {
                        GUIUtility.systemCopyBuffer = BuildOffsetList();
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Save Shape"))
                    {
                        SaveCurrentShape();
                    }

                    using (new EditorGUI.DisabledScope(!SavedShapes().ContainsKey(NormalizeShapeId(shapeId))))
                    {
                        if (GUILayout.Button("Delete Saved Shape"))
                        {
                            DeleteCurrentSavedShape();
                        }
                    }

                    if (GUILayout.Button("Refresh Saved List"))
                    {
                        RefreshSavedShapeIds();
                    }
                }

                EditorGUILayout.HelpBox(
                    "Duplicate aliases are hidden from the built-in picker: cone-near/cross-near/ring-near behave like single, and t-forward behaves like cone-mid. They remain in runtime for old CSV compatibility.",
                    MessageType.None);
            }
        }

        private void LoadKnownShape(string knownShapeId)
        {
            shapeId = knownShapeId;
            editableOffsets.Clear();
            if (!AttackShapeLibrary.TryGet(knownShapeId, out var shape) || shape.Offsets == null)
            {
                return;
            }

            foreach (var offset in shape.Offsets)
            {
                editableOffsets.Add(offset);
            }
        }

        private void LoadSavedShape(string savedShapeId)
        {
            var shapes = SavedShapes();
            if (!shapes.TryGetValue(savedShapeId, out var offsets))
            {
                return;
            }

            shapeId = savedShapeId;
            editableOffsets.Clear();
            foreach (var offset in offsets)
            {
                editableOffsets.Add(offset);
            }
        }

        private void SaveCurrentShape()
        {
            var id = NormalizeShapeId(shapeId);
            if (string.IsNullOrEmpty(id))
            {
                EditorUtility.DisplayDialog("Shape ID required", "Enter a non-empty Shape ID before saving.", "OK");
                return;
            }

            if (BuiltInShapeIds.Contains(id))
            {
                EditorUtility.DisplayDialog("Built-in shape", BuiltInOverwriteWarning, "OK");
                return;
            }

            var shapes = SavedShapes();
            shapes[id] = editableOffsets
                .OrderBy(offset => offset.DistanceTo(new HexCoord(0, 0)))
                .ThenBy(offset => offset.Q)
                .ThenBy(offset => offset.R)
                .ToArray();

            WriteGeneratedShapes(shapes);
            shapeId = id;
            RefreshSavedShapeIds();
            AssetDatabase.Refresh();
        }

        private void DeleteCurrentSavedShape()
        {
            var id = NormalizeShapeId(shapeId);
            var shapes = SavedShapes();
            if (!shapes.Remove(id))
            {
                return;
            }

            WriteGeneratedShapes(shapes);
            RefreshSavedShapeIds();
            AssetDatabase.Refresh();
        }

        private void DrawHexEditor(Rect area)
        {
            EditorGUI.DrawRect(area, new Color(0.12f, 0.12f, 0.12f));

            var center = area.center;
            var cellSize = Mathf.Min(area.width, area.height) / ((gridRadius * 2f + 2f) * 1.5f);
            var allAffected = GetRuntimeAffectedCells();

            for (var r = -gridRadius; r <= gridRadius; r++)
            {
                for (var q = -gridRadius; q <= gridRadius; q++)
                {
                    var coord = new HexCoord(q, r);
                    if (coord.DistanceTo(new HexCoord(0, 0)) > gridRadius)
                    {
                        continue;
                    }

                    var hexCenter = AxialToPixel(coord, center, cellSize);
                    var fill = GetCellColor(coord, allAffected);
                    DrawHex(hexCenter, cellSize * 0.86f, fill, new Color(0.04f, 0.04f, 0.04f));

                    if (showCoordinateLabels)
                    {
                        var labelRect = new Rect(hexCenter.x - 24f, hexCenter.y - 8f, 48f, 16f);
                        var style = coord == new HexCoord(0, 0) ? EditorStyles.boldLabel : EditorStyles.miniLabel;
                        var previous = GUI.color;
                        GUI.color = Color.white;
                        GUI.Label(labelRect, coord == new HexCoord(0, 0) ? "M" : $"{q},{r}", style);
                        GUI.color = previous;
                    }
                }
            }

            DrawLegend(area);
        }

        private Color GetCellColor(HexCoord coord, HashSet<HexCoord> allAffected)
        {
            if (coord == new HexCoord(0, 0))
            {
                return new Color(0.95f, 0.78f, 0.22f);
            }

            if (editableOffsets.Contains(coord))
            {
                return new Color(1f, 0.28f, 0.22f);
            }

            if (showRuntimeAdjacentRing && AdjacentRing.Contains(coord))
            {
                return new Color(0.2f, 0.46f, 0.95f);
            }

            if (allAffected.Contains(coord))
            {
                return new Color(0.85f, 0.24f, 0.2f);
            }

            return new Color(0.28f, 0.28f, 0.28f);
        }

        private void HandleHexInput(Rect area)
        {
            var current = Event.current;
            if (current.type != EventType.MouseDown || current.button != 0 || !area.Contains(current.mousePosition))
            {
                return;
            }

            if (TryGetCoordAtMouse(area, current.mousePosition, out var coord) && coord != new HexCoord(0, 0))
            {
                if (editableOffsets.Contains(coord))
                {
                    editableOffsets.Remove(coord);
                }
                else
                {
                    editableOffsets.Add(coord);
                }

                current.Use();
                Repaint();
            }
        }

        private bool TryGetCoordAtMouse(Rect area, Vector2 mousePosition, out HexCoord selected)
        {
            selected = default;
            var center = area.center;
            var cellSize = Mathf.Min(area.width, area.height) / ((gridRadius * 2f + 2f) * 1.5f);
            var bestDistance = float.MaxValue;
            var found = false;

            for (var r = -gridRadius; r <= gridRadius; r++)
            {
                for (var q = -gridRadius; q <= gridRadius; q++)
                {
                    var coord = new HexCoord(q, r);
                    if (coord.DistanceTo(new HexCoord(0, 0)) > gridRadius)
                    {
                        continue;
                    }

                    var hexCenter = AxialToPixel(coord, center, cellSize);
                    var distance = Vector2.Distance(mousePosition, hexCenter);
                    if (distance < bestDistance && distance <= cellSize * 0.82f)
                    {
                        bestDistance = distance;
                        selected = coord;
                        found = true;
                    }
                }
            }

            return found;
        }

        private void DrawOutput()
        {
            var sortedOffsets = editableOffsets.OrderBy(coord => coord.DistanceTo(new HexCoord(0, 0)))
                .ThenBy(coord => coord.Q)
                .ThenBy(coord => coord.R)
                .ToArray();
            var affectedCount = GetRuntimeAffectedCells().Count;

            EditorGUILayout.LabelField(
                $"Editable offsets: {sortedOffsets.Length} / Runtime affected cells: {affectedCount}",
                EditorStyles.boldLabel);

            outputScroll = EditorGUILayout.BeginScrollView(outputScroll, GUILayout.MinHeight(96f), GUILayout.MaxHeight(160f));
            EditorGUILayout.TextArea(BuildCSharpShapeEntry(), GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private HashSet<HexCoord> GetRuntimeAffectedCells()
        {
            var cells = new HashSet<HexCoord>(editableOffsets);
            foreach (var coord in AdjacentRing)
            {
                cells.Add(coord);
            }

            return cells;
        }

        private string BuildOffsetList()
        {
            return string.Join(", ", editableOffsets
                .OrderBy(coord => coord.DistanceTo(new HexCoord(0, 0)))
                .ThenBy(coord => coord.Q)
                .ThenBy(coord => coord.R)
                .Select(coord => $"({coord.Q},{coord.R})"));
        }

        private string BuildCSharpShapeEntry()
        {
            var safeShapeId = string.IsNullOrWhiteSpace(shapeId) ? "new-shape" : shapeId.Trim();
            var builder = new StringBuilder();
            builder.AppendLine($"shapes[\"{safeShapeId}\"] = new AttackShapeDefinition(\"{safeShapeId}\", new[]");
            builder.AppendLine("{");
            foreach (var coord in editableOffsets
                         .OrderBy(offset => offset.DistanceTo(new HexCoord(0, 0)))
                         .ThenBy(offset => offset.Q)
                         .ThenBy(offset => offset.R))
            {
                builder.AppendLine($"    new HexCoord({coord.Q}, {coord.R}),");
            }

            builder.Append("}),");
            return builder.ToString();
        }

        private void RefreshSavedShapeIds()
        {
            savedShapeIds = SavedShapes().Keys.OrderBy(id => id).ToArray();
            if (selectedSavedShapeIndex >= savedShapeIds.Length)
            {
                selectedSavedShapeIndex = 0;
            }
        }

        private static string NormalizeShapeId(string raw)
        {
            return (raw ?? string.Empty).Trim();
        }

        private static Dictionary<string, HexCoord[]> SavedShapes()
        {
            var result = new Dictionary<string, HexCoord[]>();
            if (!File.Exists(GeneratedShapePath))
            {
                return result;
            }

            var source = File.ReadAllText(GeneratedShapePath, Encoding.UTF8);
            var shapeMatches = Regex.Matches(
                source,
                "shapes\\[\"(?<id>[^\"]+)\"\\]\\s*=\\s*new AttackShapeDefinition\\(\"[^\"]+\",\\s*new\\[\\]\\s*\\{(?<body>.*?)\\}\\);",
                RegexOptions.Singleline);
            foreach (Match shapeMatch in shapeMatches)
            {
                var id = shapeMatch.Groups["id"].Value;
                var coords = new List<HexCoord>();
                var coordMatches = Regex.Matches(
                    shapeMatch.Groups["body"].Value,
                    "new HexCoord\\(\\s*(?<q>-?\\d+)\\s*,\\s*(?<r>-?\\d+)\\s*\\)");
                foreach (Match coordMatch in coordMatches)
                {
                    coords.Add(new HexCoord(
                        int.Parse(coordMatch.Groups["q"].Value),
                        int.Parse(coordMatch.Groups["r"].Value)));
                }

                result[id] = coords.ToArray();
            }

            return result;
        }

        private static void WriteGeneratedShapes(Dictionary<string, HexCoord[]> shapes)
        {
            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated>");
            builder.AppendLine("// Generated by MonsterAttackShapeDesignerWindow. Do not hand-edit.");
            builder.AppendLine("// Use Seoul Playup > Combat > Monster Attack Shape Designer.");
            builder.AppendLine("// </auto-generated>");
            builder.AppendLine("using System.Collections.Generic;");
            builder.AppendLine("using SeoulPlayup.Map.Runtime;");
            builder.AppendLine();
            builder.AppendLine("namespace SeoulPlayup.Combat.Runtime");
            builder.AppendLine("{");
            builder.AppendLine("    public static partial class AttackShapeLibrary");
            builder.AppendLine("    {");
            builder.AppendLine("        static partial void RegisterGeneratedShapes(Dictionary<string, AttackShapeDefinition> shapes)");
            builder.AppendLine("        {");

            foreach (var pair in shapes.OrderBy(pair => pair.Key))
            {
                builder.AppendLine($"            shapes[\"{pair.Key}\"] = new AttackShapeDefinition(\"{pair.Key}\", new[]");
                builder.AppendLine("            {");
                foreach (var coord in pair.Value
                             .OrderBy(offset => offset.DistanceTo(new HexCoord(0, 0)))
                             .ThenBy(offset => offset.Q)
                             .ThenBy(offset => offset.R))
                {
                    builder.AppendLine($"                new HexCoord({coord.Q}, {coord.R}),");
                }

                builder.AppendLine("            });");
            }

            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine("}");

            var directory = Path.GetDirectoryName(GeneratedShapePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(GeneratedShapePath, builder.ToString(), Encoding.UTF8);
        }

        private static Vector2 AxialToPixel(HexCoord coord, Vector2 center, float size)
        {
            const float sqrt3 = 1.7320508f;
            return new Vector2(
                center.x + size * 1.5f * coord.Q,
                center.y + size * (sqrt3 * 0.5f * coord.Q + sqrt3 * coord.R));
        }

        private static void DrawHex(Vector2 center, float radius, Color fill, Color outline)
        {
            var points = new Vector3[6];
            for (var i = 0; i < 6; i++)
            {
                var angle = Mathf.Deg2Rad * (60f * i);
                points[i] = new Vector3(
                    center.x + Mathf.Cos(angle) * radius,
                    center.y + Mathf.Sin(angle) * radius,
                    0f);
            }

            Handles.BeginGUI();
            Handles.color = fill;
            Handles.DrawAAConvexPolygon(points);
            Handles.color = outline;
            for (var i = 0; i < points.Length; i++)
            {
                Handles.DrawAAPolyLine(2f, points[i], points[(i + 1) % points.Length]);
            }
            Handles.EndGUI();
        }

        private static void DrawLegend(Rect area)
        {
            var rect = new Rect(area.x + 10f, area.yMax - 28f, area.width - 20f, 22f);
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.35f));

            DrawLegendItem(new Rect(rect.x + 8f, rect.y + 5f, 110f, 16f), new Color(0.95f, 0.78f, 0.22f), "M Monster");
            DrawLegendItem(new Rect(rect.x + 120f, rect.y + 5f, 130f, 16f), new Color(0.2f, 0.46f, 0.95f), "Runtime 1-ring");
            DrawLegendItem(new Rect(rect.x + 260f, rect.y + 5f, 130f, 16f), new Color(1f, 0.28f, 0.22f), "Editable offset");
        }

        private static void DrawLegendItem(Rect rect, Color color, string label)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + 2f, 12f, 12f), color);
            GUI.Label(new Rect(rect.x + 16f, rect.y - 1f, rect.width - 16f, rect.height), label, EditorStyles.whiteMiniLabel);
        }
    }
}

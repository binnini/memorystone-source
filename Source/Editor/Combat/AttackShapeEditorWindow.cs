using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.EditorTools.Combat
{
    /// <summary>
    /// 공격 형상 클릭 편집기(계획 §27 · 안 A). 저작 원본은 <c>attack_shapes.csv</c> 하나이고,
    /// 이 창은 그 CSV를 읽어 육각 격자 클릭으로 오프셋을 토글한 뒤 <b>같은 CSV로</b> 저장한다 —
    /// 도표(tools/attack-shape-chart)·랩 미리보기·실게임이 전부 이 한 원본을 읽는다.
    ///
    /// 저장 전에 반드시 <see cref="AttackShapeCatalogCsv.ConvertText"/>를 통과시킨다(가드 포함):
    /// 인접 링 opt-out + 거리 1 칸 같은 "회피 계약을 깨는 저작"은 여기서 죽는다. 또 패턴 CSV와
    /// cards.csv가 참조하는 shapeId를 지우는 저장은 차단한다 — 지워진 형상은 런타임 변환에서
    /// 터지기 때문이다. 육각 클릭 편집 선례: <c>HexSparseMapEditorWindow</c>.
    /// </summary>
    public sealed class AttackShapeEditorWindow : EditorWindow
    {
        private const int EditRadius = AttackShapeCatalogCsv.MaxOffsetDistance;
        private const float HexSize = 22f;

        private sealed class ShapeModel
        {
            public string Id;
            public AttackShapeAdjacency Adjacency = AttackShapeAdjacency.Full;
            public bool IncludeAdjacentRing => Adjacency == AttackShapeAdjacency.Full;
            public readonly List<HexCoord> Offsets = new List<HexCoord>();
            public string DesignerNote = string.Empty;
        }

        private readonly List<ShapeModel> shapes = new List<ShapeModel>();
        private int selectedIndex;
        private string newShapeId = string.Empty;
        private string statusMessage = string.Empty;
        private MessageType statusType = MessageType.Info;
        private bool dirty;
        private Vector2 listScroll;

        private static readonly HexCoord OriginCell = new HexCoord(0, 0);

        private static readonly HexCoord[] AdjacentOffsets =
        {
            new HexCoord(1, 0), new HexCoord(1, -1), new HexCoord(0, -1),
            new HexCoord(-1, 0), new HexCoord(-1, 1), new HexCoord(0, 1)
        };

        [MenuItem("Seoul Playup/Combat/Attack Shape Editor")]
        public static void Open()
        {
            var window = GetWindow<AttackShapeEditorWindow>("Attack Shapes");
            window.minSize = new Vector2(760f, 560f);
            window.LoadFromCsv();
        }

        private void OnEnable()
        {
            if (shapes.Count == 0)
            {
                LoadFromCsv();
            }
        }

        // ----------------------------------------------------------------- CSV I/O

        private void LoadFromCsv()
        {
            shapes.Clear();
            selectedIndex = 0;
            dirty = false;
            if (!File.Exists(CombatCsvPaths.AttackShapesCsv))
            {
                SetStatus($"CSV가 없다: {CombatCsvPaths.AttackShapesCsv}", MessageType.Error);
                return;
            }

            var text = File.ReadAllText(CombatCsvPaths.AttackShapesCsv, Encoding.UTF8);
            // 검증은 런타임 파서로(가드 공유), designerNote는 CsvTable로 직접 회수한다(파서는 버린다).
            var catalog = AttackShapeCatalogCsv.ConvertText(text);
            var notes = CsvTable.Parse(text, "attack_shapes.csv").Rows
                .ToDictionary(
                    row => row.TryGet("shapeId", out var id) ? id.Trim() : string.Empty,
                    row => row.TryGet("designerNote", out var note) ? note.Trim() : string.Empty,
                    StringComparer.Ordinal);

            foreach (var shape in catalog.Shapes)
            {
                var model = new ShapeModel
                {
                    Id = shape.Id,
                    Adjacency = shape.Adjacency,
                    DesignerNote = notes.TryGetValue(shape.Id, out var note) ? note : string.Empty,
                };
                model.Offsets.AddRange(shape.Offsets);
                shapes.Add(model);
            }

            SetStatus($"{shapes.Count}종 로드: {CombatCsvPaths.AttackShapesCsv}", MessageType.Info);
        }

        private static string AdjacencyToken(AttackShapeAdjacency adjacency)
        {
            switch (adjacency)
            {
                case AttackShapeAdjacency.None: return "none";
                case AttackShapeAdjacency.Open: return "open";
                case AttackShapeAdjacency.Body: return "body";
                case AttackShapeAdjacency.BodyShell: return "body-shell";
                default: return "full";
            }
        }

        private string SerializeCsv()
        {
            var builder = new StringBuilder();
            builder.Append("shapeId,adjacency,offsets,designerNote\n");
            foreach (var shape in shapes)
            {
                var offsets = string.Join(" ", shape.Offsets.Select(offset => $"{offset.Q}:{offset.R}"));
                var note = (shape.DesignerNote ?? string.Empty).Replace(',', '·').Replace('\n', ' ');
                builder.Append($"{shape.Id},{AdjacencyToken(shape.Adjacency)},{offsets},{note}\n");
            }

            return builder.ToString();
        }

        private void SaveToCsv()
        {
            var csv = SerializeCsv();
            AttackShapeCatalogDefinition catalog;
            try
            {
                catalog = AttackShapeCatalogCsv.ConvertText(csv);
            }
            catch (ArgumentException exception)
            {
                SetStatus($"저장 거부(가드): {exception.Message}", MessageType.Error);
                return;
            }

            var missing = CollectReferencedShapeIds()
                .Where(id => shapes.All(shape => !string.Equals(shape.Id, id, StringComparison.Ordinal)))
                .ToList();
            if (missing.Count > 0)
            {
                SetStatus(
                    $"저장 거부: 패턴/카드가 참조하는 형상이 없다 — {string.Join(", ", missing)}. 참조를 먼저 옮길 것.",
                    MessageType.Error);
                return;
            }

            File.WriteAllText(CombatCsvPaths.AttackShapesCsv, csv, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(CombatCsvPaths.AttackShapesCsv);
            AttackShapeLibrary.Initialize(catalog);
            dirty = false;
            SetStatus(
                "저장 완료. 도표는 `python3 tools/attack-shape-chart/generate.py`로 다시 뽑을 것.",
                MessageType.Info);
        }

        /// <summary>패턴 CSV(shapeId)와 cards.csv(shape)가 실제로 쓰는 형상 id 전부.</summary>
        private static HashSet<string> CollectReferencedShapeIds()
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            CollectColumn(CombatCsvPaths.MonsterDirectory + "/monster_attack_patterns.csv", "shapeId", used);
            CollectColumn(CombatCsvPaths.CardsCsv, "shape", used);
            return used;
        }

        private static void CollectColumn(string path, string column, HashSet<string> into)
        {
            if (!File.Exists(path))
            {
                return;
            }

            foreach (var row in CsvTable.Parse(File.ReadAllText(path, Encoding.UTF8), Path.GetFileName(path)).Rows)
            {
                if (row.TryGet(column, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    into.Add(value.Trim());
                }
            }
        }

        // ----------------------------------------------------------------- GUI

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawShapeList();
                DrawEditPane();
            }

            if (!string.IsNullOrEmpty(statusMessage))
            {
                EditorGUILayout.HelpBox(statusMessage, statusType);
            }
        }

        private void DrawShapeList()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(220f)))
            {
                EditorGUILayout.LabelField("형상", EditorStyles.boldLabel);
                listScroll = EditorGUILayout.BeginScrollView(listScroll);
                for (var index = 0; index < shapes.Count; index++)
                {
                    var shape = shapes[index];
                    var label = $"{shape.Id} · {shape.Offsets.Count + (shape.IncludeAdjacentRing ? 6 : 0)}칸"
                                + (shape.IncludeAdjacentRing ? string.Empty : $" · {AdjacencyToken(shape.Adjacency)}");
                    if (GUILayout.Toggle(index == selectedIndex, label, "Button"))
                    {
                        selectedIndex = index;
                    }
                }

                EditorGUILayout.EndScrollView();

                GUILayout.Space(6f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    newShapeId = EditorGUILayout.TextField(newShapeId);
                    if (GUILayout.Button("추가", GUILayout.Width(44f)))
                    {
                        AddShape();
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("다시 로드"))
                    {
                        LoadFromCsv();
                    }

                    using (new EditorGUI.DisabledScope(!dirty))
                    {
                        if (GUILayout.Button(dirty ? "저장*" : "저장"))
                        {
                            SaveToCsv();
                        }
                    }
                }
            }
        }

        private void AddShape()
        {
            var id = (newShapeId ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(id))
            {
                SetStatus("새 형상의 shapeId를 먼저 입력할 것(kebab-case).", MessageType.Warning);
                return;
            }

            if (shapes.Any(shape => string.Equals(shape.Id, id, StringComparison.Ordinal)))
            {
                SetStatus($"'{id}'는 이미 있다.", MessageType.Warning);
                return;
            }

            shapes.Add(new ShapeModel { Id = id });
            selectedIndex = shapes.Count - 1;
            newShapeId = string.Empty;
            dirty = true;
        }

        private void DrawEditPane()
        {
            if (shapes.Count == 0)
            {
                EditorGUILayout.HelpBox("로드된 형상이 없다.", MessageType.Warning);
                return;
            }

            selectedIndex = Mathf.Clamp(selectedIndex, 0, shapes.Count - 1);
            var shape = shapes[selectedIndex];

            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUILayout.LabelField($"{shape.Id}", EditorStyles.boldLabel);

                // full=인접 6칸 자동 · none=원거리 밴드(거리 1 저작 금지) · open=인접을 저작으로 일부만.
                var adjacency = (AttackShapeAdjacency)EditorGUILayout.EnumPopup(
                    new GUIContent("adjacency", "full=인접 6칸 자동 추가 · none=링 없음(모든 칸 거리 2 이상이어야 저장) · open=링 자동 없음, 거리 1 직접 저작 허용"),
                    shape.Adjacency);
                if (adjacency != shape.Adjacency)
                {
                    shape.Adjacency = adjacency;
                    dirty = true;
                }

                var note = EditorGUILayout.TextField("designerNote", shape.DesignerNote);
                if (!string.Equals(note, shape.DesignerNote, StringComparison.Ordinal))
                {
                    shape.DesignerNote = note;
                    dirty = true;
                }

                if (shape.Adjacency == AttackShapeAdjacency.None
                    && shape.Offsets.Any(offset => OriginCell.DistanceTo(offset) < 2))
                {
                    EditorGUILayout.HelpBox(
                        "adjacency=none 형상에 거리 1 칸이 있다 — '붙으면 안전' 계약 위반이라 저장이 거부된다."
                        + " 붙어도 맞아야 하는 형상이면 adjacency=open으로 바꾼다.",
                        MessageType.Error);
                }

                EditorGUILayout.LabelField(
                    $"칸 수 {shape.Offsets.Count + (shape.IncludeAdjacentRing ? 6 : 0)}"
                    + " · 클릭=토글 · 정동(East) 기준 저작, 런타임이 조준 방향으로 회전");

                DrawHexGrid(shape);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("오프셋 전부 지우기"))
                    {
                        shape.Offsets.Clear();
                        dirty = true;
                    }

                    if (GUILayout.Button("형상 삭제"))
                    {
                        shapes.RemoveAt(selectedIndex);
                        selectedIndex = Mathf.Max(0, selectedIndex - 1);
                        dirty = true;
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }

        private void DrawHexGrid(ShapeModel shape)
        {
            var span = Mathf.Sqrt(3f) * HexSize * (2 * EditRadius + 1) + 8f;
            var height = 1.5f * HexSize * (2 * EditRadius) + 2f * HexSize + 8f;
            var rect = GUILayoutUtility.GetRect(span, height, GUILayout.ExpandWidth(false));
            var center = rect.center;

            var evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 0 && rect.Contains(evt.mousePosition))
            {
                var cell = PixelToHex(evt.mousePosition - center);
                if (cell != OriginCell && OriginCell.DistanceTo(cell) <= EditRadius)
                {
                    if (!shape.Offsets.Remove(cell))
                    {
                        shape.Offsets.Add(cell);
                    }

                    dirty = true;
                    evt.Use();
                    Repaint();
                }
            }

            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            Handles.BeginGUI();
            EditorGUI.DrawRect(rect, new Color(0.09f, 0.1f, 0.16f, 1f));
            for (var q = -EditRadius; q <= EditRadius; q++)
            {
                for (var r = -EditRadius; r <= EditRadius; r++)
                {
                    var cell = new HexCoord(q, r);
                    if (OriginCell.DistanceTo(cell) > EditRadius)
                    {
                        continue;
                    }

                    var pixel = center + HexToPixel(cell);
                    var isOffset = shape.Offsets.Contains(cell);
                    var isRing = shape.IncludeAdjacentRing && AdjacentOffsets.Contains(cell);
                    Color fill;
                    if (cell == OriginCell)
                    {
                        fill = new Color(0.25f, 0.66f, 0.96f, 0.95f); // 몸통(원점)
                    }
                    else if (isOffset)
                    {
                        fill = new Color(0.91f, 0.31f, 0.25f, 0.92f); // 형상 고유 칸
                    }
                    else if (isRing)
                    {
                        fill = new Color(0.85f, 0.64f, 0.25f, 0.75f); // 인접 링(자동 — 클릭 불필요)
                    }
                    else
                    {
                        fill = new Color(0.2f, 0.21f, 0.3f, 0.9f);
                    }

                    Handles.color = fill;
                    Handles.DrawAAConvexPolygon(HexCorners(pixel, HexSize * 0.94f));
                }
            }

            Handles.EndGUI();
        }

        private static Vector2 HexToPixel(HexCoord cell) =>
            new Vector2(
                Mathf.Sqrt(3f) * HexSize * (cell.Q + cell.R * 0.5f),
                1.5f * HexSize * cell.R);

        private static HexCoord PixelToHex(Vector2 pixel)
        {
            var fq = (Mathf.Sqrt(3f) / 3f * pixel.x - pixel.y / 3f) / HexSize;
            var fr = 2f / 3f * pixel.y / HexSize;
            var fs = -fq - fr;
            var q = Mathf.RoundToInt(fq);
            var r = Mathf.RoundToInt(fr);
            var s = Mathf.RoundToInt(fs);
            var dq = Mathf.Abs(q - fq);
            var dr = Mathf.Abs(r - fr);
            var ds = Mathf.Abs(s - fs);
            if (dq > dr && dq > ds)
            {
                q = -r - s;
            }
            else if (dr > ds)
            {
                r = -q - s;
            }

            return new HexCoord(q, r);
        }

        private static Vector3[] HexCorners(Vector2 pixelCenter, float size)
        {
            var corners = new Vector3[6];
            for (var i = 0; i < 6; i++)
            {
                var angle = Mathf.Deg2Rad * (60f * i - 90f);
                corners[i] = new Vector3(
                    pixelCenter.x + size * Mathf.Cos(angle),
                    pixelCenter.y + size * Mathf.Sin(angle),
                    0f);
            }

            return corners;
        }

        private void SetStatus(string message, MessageType type)
        {
            statusMessage = message;
            statusType = type;
        }
    }
}

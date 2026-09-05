using System.Collections.Generic;
using SeoulPlayup.Codex;
using SeoulPlayup.Map.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Flow.Unity
{
    /// <summary>
    /// 범위 도해를 그리는 한 장의 <see cref="Graphic"/>. 격자 전체를 <b>메시 하나</b>로 뱉으므로
    /// 칸마다 <c>Image</c>를 세우지 않는다(반경 6이면 127칸 — 오브젝트로 지으면 목록을 넘길 때마다
    /// 그만큼 만들고 부순다).
    /// <para>
    /// 이 클래스는 <b>칸을 색으로 옮기기만</b> 한다. 어떤 칸인지는 <see cref="CodexRangeDiagram"/>이
    /// 이미 정했고, 그 칸은 출하 집행 함수가 준 것이다(<c>docs/codex-plan.md</c> §3-1).
    /// </para>
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CodexHexDiagramGraphic : MaskableGraphic
    {
        /// <summary>육각형을 칸 간격보다 조금 작게 그려 남는 틈이 곧 격자선이 된다.</summary>
        private const float CellFillRatio = 0.90f;

        // 빈 칸도 보여야 한다 — 격자가 안 보이면 칠해진 칸이 "몇 칸 떨어져 있는지"를 셀 수 없다.
        private static readonly Color EmptyCell = new Color(0.129f, 0.145f, 0.259f, 1f);
        private static readonly Color BodyCell = new Color(0.298f, 0.325f, 0.545f, 1f);
        private static readonly Color RangeCell = new Color(0.176f, 0.353f, 0.663f, 1f);
        private static readonly Color ShapeCell = new Color(0.882f, 0.639f, 0.243f, 1f);
        private static readonly Color OriginCell = new Color(0.949f, 0.949f, 0.976f, 1f);
        private static readonly Color ShapeCenterRing = new Color(0.996f, 0.827f, 0.518f, 1f);

        private CodexRangeDiagram diagram;
        private bool hasDiagram;

        /// <summary>걸음 수 띠의 가장 가까운 칸 색. 멀어질수록 <see cref="RangeCell"/> 쪽으로 어두워진다.</summary>
        private static readonly Color NearStepCell = new Color(0.478f, 0.729f, 0.996f, 1f);

        public static Color LegendRange => RangeCell;
        public static Color LegendShape => ShapeCell;
        public static Color LegendBody => BodyCell;
        public static Color LegendOrigin => OriginCell;

        /// <summary>
        /// <paramref name="step"/>걸음 칸의 색. 뷰의 범례가 도해와 같은 함수를 써야 띠와 설명이 갈라지지 않는다.
        /// </summary>
        public static Color StepColor(int step, int maxStep)
        {
            if (maxStep <= 1)
            {
                return NearStepCell;
            }

            var t = Mathf.Clamp01((step - 1) / (float)(maxStep - 1));
            return Color.Lerp(NearStepCell, RangeCell, t);
        }

        public void SetDiagram(CodexRangeDiagram value)
        {
            diagram = value;
            hasDiagram = true;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (!hasDiagram || diagram.IsEmpty)
            {
                return;
            }

            var rect = GetPixelAdjustedRect();
            var radius = diagram.GridRadius;

            // 폭 기준 칸 크기: 뾰족지붕 육각의 가로 간격은 sqrt(3)*size, 격자 폭은 (2R+1)칸이다.
            var byWidth = rect.width / (Mathf.Sqrt(3f) * (2 * radius + 1));
            // 세로 간격은 1.5*size, 위아래 끝은 반 칸씩 더 튀어나온다.
            var byHeight = rect.height / (1.5f * 2 * radius + 2f);
            var size = Mathf.Min(byWidth, byHeight);
            var center = rect.center;

            var range = ToSet(diagram.RangeCells);
            var shape = ToSet(diagram.ShapeCells);
            var body = ToSet(diagram.BodyCells);

            foreach (var coord in HexArea.CellsWithin(diagram.Origin, radius))
            {
                var color = EmptyCell;
                if (range.Contains(coord))
                {
                    color = diagram.RangeSteps != null && diagram.RangeSteps.TryGetValue(coord, out var step)
                        ? StepColor(step, diagram.MaxRangeStep)
                        : RangeCell;
                }

                if (body.Contains(coord))
                {
                    color = BodyCell;
                }

                // 형상이 마지막이다 — 사거리 안에서 실제로 맞는 칸이 다른 무엇에도 가리면 안 된다.
                if (shape.Contains(coord))
                {
                    color = ShapeCell;
                }

                AddHex(vh, center + ToLocal(coord, diagram.Origin, size), size * CellFillRatio, color);
            }

            // 원점과 겨냥 칸은 작은 표식을 하나 더 얹어 "여기가 나 / 여기를 겨눴다"를 못 박는다.
            AddHex(vh, center + ToLocal(diagram.Origin, diagram.Origin, size), size * 0.34f, OriginCell);
            if (diagram.ShapeCenter.HasValue && diagram.ShapeCenter.Value != diagram.Origin)
            {
                AddHex(
                    vh,
                    center + ToLocal(diagram.ShapeCenter.Value, diagram.Origin, size),
                    size * 0.30f,
                    ShapeCenterRing);
            }
        }

        private static HashSet<HexCoord> ToSet(IReadOnlyList<HexCoord> cells)
        {
            var set = new HashSet<HexCoord>();
            for (var i = 0; i < cells.Count; i++)
            {
                set.Add(cells[i]);
            }

            return set;
        }

        /// <summary>축 좌표 → 화면 좌표(뾰족지붕). r이 커지면 화면 아래로 간다.</summary>
        private static Vector2 ToLocal(HexCoord coord, HexCoord origin, float size)
        {
            var q = coord.Q - origin.Q;
            var r = coord.R - origin.R;
            return new Vector2(
                size * Mathf.Sqrt(3f) * (q + r * 0.5f),
                -size * 1.5f * r);
        }

        private static void AddHex(VertexHelper vh, Vector2 center, float size, Color color)
        {
            var start = vh.currentVertCount;
            var vertex = UIVertex.simpleVert;
            vertex.color = color;

            vertex.position = center;
            vh.AddVert(vertex);

            for (var i = 0; i < 6; i++)
            {
                // 뾰족지붕: 첫 꼭짓점이 바로 위(90°)에 온다.
                var angle = Mathf.Deg2Rad * (60f * i + 90f);
                vertex.position = new Vector2(
                    center.x + size * Mathf.Cos(angle),
                    center.y + size * Mathf.Sin(angle));
                vh.AddVert(vertex);
            }

            for (var i = 0; i < 6; i++)
            {
                vh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % 6);
            }
        }
    }
}

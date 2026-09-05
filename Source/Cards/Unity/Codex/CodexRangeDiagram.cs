using System;
using System.Collections.Generic;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Codex
{
    /// <summary>
    /// 범위 도해 하나가 그려야 할 칸 집합. 뷰는 이것만 보고, 이 칸들이 어디서 왔는지는 모른다.
    /// <para>
    /// 🔴 <b>도감은 범위를 계산하지 않는다</b>(<c>docs/codex-plan.md</c> §3-1). 이 구조체를 채우는
    /// 해석기들(<see cref="CodexCardRange"/> · <see cref="CodexMonsterPatternRange"/>)은 전부
    /// 출하 집행 함수를 불러서 칸을 얻는다 — 도감용 계산을 새로 짜면 그 순간부터 도감은 조용히
    /// 거짓말을 시작한다.
    /// </para>
    /// <para>
    /// 어휘가 둘(카드 <c>blast-N</c> 반경 · 몬스터 <c>attack_shapes.csv</c> 오프셋)이라 인터페이스는
    /// <see cref="IReadOnlyList{HexCoord}"/>로 잡는다. 나중에 카드가 오프셋 어휘로 통일돼도
    /// 뷰는 안 깨진다.
    /// </para>
    /// </summary>
    public readonly struct CodexRangeDiagram
    {
        private static readonly IReadOnlyList<HexCoord> NoCells = Array.Empty<HexCoord>();

        public CodexRangeDiagram(
            HexCoord origin,
            int gridRadius,
            IReadOnlyList<HexCoord> rangeCells = null,
            IReadOnlyList<HexCoord> shapeCells = null,
            IReadOnlyList<HexCoord> bodyCells = null,
            HexCoord? shapeCenter = null,
            string note = "",
            string rangeLabel = "",
            string shapeLabel = "",
            IReadOnlyDictionary<HexCoord, int> rangeSteps = null)
        {
            Origin = origin;
            GridRadius = Math.Max(1, gridRadius);
            RangeCells = rangeCells ?? NoCells;
            ShapeCells = shapeCells ?? NoCells;
            BodyCells = bodyCells ?? NoCells;
            ShapeCenter = shapeCenter;
            Note = note ?? string.Empty;
            RangeLabel = rangeLabel ?? string.Empty;
            ShapeLabel = shapeLabel ?? string.Empty;
            RangeSteps = rangeSteps;

            var deepest = 0;
            if (rangeSteps != null)
            {
                foreach (var pair in rangeSteps)
                {
                    if (pair.Value > deepest)
                    {
                        deepest = pair.Value;
                    }
                }
            }

            MaxRangeStep = deepest;
        }

        /// <summary>도해의 중심 — 카드는 플레이어 자리, 몬스터 패턴은 몬스터 자리.</summary>
        public HexCoord Origin { get; }

        /// <summary>그릴 격자의 반경. 칸 집합이 격자를 넘지 않도록 해석기가 정해 준다.</summary>
        public int GridRadius { get; }

        /// <summary>겨냥할 수 있는 칸(사거리 / 도달 가능 칸).</summary>
        public IReadOnlyList<HexCoord> RangeCells { get; }

        /// <summary>실제로 맞는 칸(착탄 형상 / 패턴 형상).</summary>
        public IReadOnlyList<HexCoord> ShapeCells { get; }

        /// <summary>멀티셀 보스의 몸통 칸. 카드 도해에서는 비어 있다.</summary>
        public IReadOnlyList<HexCoord> BodyCells { get; }

        /// <summary><see cref="ShapeCells"/>가 어느 칸을 겨눈 결과인가. 자기중심 범위면 <see cref="Origin"/>.</summary>
        public HexCoord? ShapeCenter { get; }

        /// <summary>그릴 칸이 없을 때 대신 적는 한 줄(예: "자신에게 적용됩니다").</summary>
        public string Note { get; }

        /// <summary>
        /// 사거리 칸마다 <b>몇 걸음</b>인가(이동 카드만 채운다). 열린 벌판에서는 도달 칸이 곧 원판이라
        /// 한 가지 색으로 칠하면 "사거리 4"와 그림이 같아진다 — 걸음 수로 띠를 나눠야 "몇 걸음"이 읽힌다.
        /// <para><c>null</c>이면 사거리 칸을 한 가지 색으로 칠한다.</para>
        /// </summary>
        public IReadOnlyDictionary<HexCoord, int> RangeSteps { get; }

        /// <summary>가장 먼 칸의 걸음 수. <see cref="RangeSteps"/>가 없으면 0.</summary>
        public int MaxRangeStep { get; }

        /// <summary>범례에 적을 이름. 비우면 뷰가 기본값("사거리")을 쓴다.</summary>
        public string RangeLabel { get; }

        public string ShapeLabel { get; }

        public bool HasCells => RangeCells.Count > 0 || ShapeCells.Count > 0 || BodyCells.Count > 0;

        /// <summary>그릴 것이 아무것도 없는 도해(자기 대상 카드·저주 등). 뷰는 <see cref="Note"/>만 적는다.</summary>
        public bool IsEmpty => !HasCells;

        public static CodexRangeDiagram Nothing(string note) =>
            new CodexRangeDiagram(default, 1, note: note);
    }
}

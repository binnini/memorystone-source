using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Codex
{
    /// <summary>
    /// 몬스터 공격 패턴 형상의 도해. 카드와 달리 여기는 부를 함수가 처음부터 하나로 모여 있다 —
    /// <see cref="AttackShapeLibrary.GetAffectedCells(string, HexCoord, HexDirection, int)"/>를
    /// 플래너·집행·예고 오버레이가 전부 공유하므로, 도감이 같은 함수를 부르면 <b>예고=명중 계약이
    /// 자동으로 따라온다</b>. 회전 규약과 몸 반경 보정은 그 함수 안에 있고 밖에서 다시 하지 않는다.
    /// <para>
    /// 🔴 <see cref="AttackShapeLibrary.Initialize"/>가 먼저 불려 있어야 한다. 안 부르면 예외가 아니라
    /// <b>빈 집합</b>이 돌아온다 — 로비에는 그걸 부르는 코드가 없으므로 도감 뷰가
    /// <c>CombatCatalogTextAssetSource.InitializeAttackShapeLibrary()</c>로 부트스트랩한다.
    /// </para>
    /// </summary>
    public static class CodexMonsterPatternRange
    {
        public const string NotInitializedNote =
            "형상 카탈로그가 실리지 않았습니다 (AttackShapeLibrary.Initialize 미호출).";

        /// <summary>몸 반경 조절 폭 — 출하 보스가 쓰는 범위다.</summary>
        public const int MaxFootprintRadius = 2;

        public static CodexRangeDiagram Resolve(string shapeId, HexDirection direction, int footprintRadius)
        {
            footprintRadius = Math.Clamp(footprintRadius, 0, MaxFootprintRadius);

            var shapeCells = AttackShapeLibrary
                .GetAffectedCells(shapeId, CodexRangeArena.Origin, direction, footprintRadius)
                .ToList();

            if (shapeCells.Count == 0)
            {
                return CodexRangeDiagram.Nothing(
                    AttackShapeLibrary.AllShapes.Count == 0 ? NotInitializedNote : "칸이 없는 형상입니다.");
            }

            var bodyCells = HexArea.CellsWithin(CodexRangeArena.Origin, footprintRadius).ToList();

            return new CodexRangeDiagram(
                CodexRangeArena.Origin,
                GridRadiusFor(shapeCells),
                shapeCells: shapeCells,
                bodyCells: bodyCells,
                shapeCenter: CodexRangeArena.Origin,
                shapeLabel: "맞는 칸",
                rangeLabel: "몸통");
        }

        /// <summary>도해가 형상을 자르지 않도록 가장 먼 칸에 한 칸을 더한 반경.</summary>
        private static int GridRadiusFor(IReadOnlyList<HexCoord> cells)
        {
            var reach = cells.Max(coord => CodexRangeArena.Origin.DistanceTo(coord));
            return Math.Max(2, reach + 1);
        }
    }
}

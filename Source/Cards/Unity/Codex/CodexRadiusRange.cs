using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Codex
{
    /// <summary>
    /// 반경 하나가 전부인 도해(함정 · 소모품 · 형상 없는 몬스터 패턴).
    /// <para>
    /// 🔴 반경을 원판으로 펴는 일은 <see cref="EffectAreaFootprint"/>가 한다 — 카드 착탄이 지나는
    /// 바로 그 함수다. 여기서 <c>HexArea.CellsWithin</c>을 직접 부르면 "반경 N"의 뜻이 두 벌이 되고,
    /// 언젠가 한쪽만 바뀐다(<c>docs/codex-plan.md</c> §3-1).
    /// </para>
    /// </summary>
    public static class CodexRadiusRange
    {
        public static CodexRangeDiagram Resolve(int radius, string shapeLabel, string note = "")
        {
            radius = Math.Max(0, radius);

            // 경계에 잘리지 않도록 반경보다 넉넉한 판 위에서 편다(P1에서 실측으로 밟은 지뢰).
            var map = CodexRangeArena.CreateMap(radius + 2);
            var cells = new List<HexCoord>();
            EffectAreaFootprint.Resolve(
                new EffectResultEvent(EffectKind.Damage, center: CodexRangeArena.Origin, radius: radius),
                map,
                cells);

            if (cells.Count == 0)
            {
                return CodexRangeDiagram.Nothing(string.IsNullOrWhiteSpace(note) ? "덮는 칸이 없습니다." : note);
            }

            var reach = cells.Max(coord => CodexRangeArena.Origin.DistanceTo(coord));

            return new CodexRangeDiagram(
                CodexRangeArena.Origin,
                Math.Max(2, reach),
                shapeCells: cells,
                shapeCenter: CodexRangeArena.Origin,
                shapeLabel: string.IsNullOrWhiteSpace(shapeLabel) ? "덮는 칸" : shapeLabel,
                note: note);
        }
    }
}

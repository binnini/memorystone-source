using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Codex
{
    /// <summary>
    /// 카드 한 장의 범위 도해를 만든다. <b>칸을 세는 일은 전부 출하 집행 함수가 한다</b> —
    /// 이 클래스가 하는 일은 어느 함수에 물어볼지 고르고, 답을 <see cref="CodexRangeDiagram"/>에
    /// 담는 것뿐이다(<c>docs/codex-plan.md</c> §3-1).
    /// <list type="bullet">
    ///   <item>이동 — <see cref="CombatState.GetReachablePlayerMoves(string)"/> (경로탐색 그대로)</item>
    ///   <item>공격 — <see cref="CombatState.ValidateAttackTarget(HexCoord, string)"/> 전 칸 스윕</item>
    ///   <item>정찰 — <see cref="CombatState.ValidateScoutTarget(HexCoord, string)"/> 전 칸 스윕</item>
    ///   <item>기물 — <see cref="CombatState.ValidateFieldObjectTarget"/> 전 칸 스윕</item>
    ///   <item>착탄 형상 — <see cref="EffectAreaFootprint.Resolve"/> (연출·집행이 공유하는 그 함수)</item>
    /// </list>
    /// </summary>
    public static class CodexCardRange
    {
        /// <summary>자기 대상·저주처럼 겨냥할 칸이 없는 카드에 적는 문구.</summary>
        public const string SelfTargetNote = "자신에게 적용됩니다 — 겨냥할 칸이 없습니다.";

        public const string UnplayableNote = "사용할 수 없는 카드입니다 — 손에 있는 것 자체가 대가입니다.";

        public const string NoRangeNote = "범위가 없는 카드입니다.";

        public static CodexRangeDiagram Resolve(CodexRangeArena arena, CardCatalogEntry entry)
        {
            if (arena == null || entry == null)
            {
                return CodexRangeDiagram.Nothing(NoRangeNote);
            }

            // 🔴 저주는 CardEffectType.Status다. CombatCardKind로 옮기면 default 분기가 Move로 뭉개므로
            // 여기서 먼저 가른다(계획 §9-2와 같은 함정).
            if (entry.ActionType == CardEffectType.Status)
            {
                return CodexRangeDiagram.Nothing(UnplayableNote);
            }

            if (entry.ActionType == CardEffectType.Move)
            {
                return ResolveMove(arena, entry);
            }

            // 자기중심 범위 공격(휩쓸기 등)은 겨냥하는 칸이 없다 — 폭발은 언제나 플레이어 중심이다.
            if (entry.TargetMode == CardTargetMode.SelfArea)
            {
                return SelfCentredArea(arena, entry);
            }

            if (entry.TargetMode == CardTargetMode.Self || entry.TargetMode == CardTargetMode.None)
            {
                return CodexRangeDiagram.Nothing(SelfTargetNote);
            }

            var rangeCells = SweepTargets(arena, entry);
            if (rangeCells.Count == 0)
            {
                return CodexRangeDiagram.Nothing(NoRangeNote);
            }

            var shapeCenter = PickShapeCenter(rangeCells);
            var shapeCells = ResolveFootprint(arena, entry, shapeCenter);
            return new CodexRangeDiagram(
                CodexRangeArena.Origin,
                GridRadiusFor(rangeCells, shapeCells),
                rangeCells: rangeCells,
                shapeCells: shapeCells,
                shapeCenter: shapeCenter,
                rangeLabel: "겨냥 가능",
                shapeLabel: entry.AreaRadius > 0 ? $"착탄 (반경 {entry.AreaRadius})" : "착탄");
        }

        private static CodexRangeDiagram ResolveMove(CodexRangeArena arena, CardCatalogEntry entry)
        {
            // 몬스터가 선 칸은 걸어 들어갈 수 없고, 이동 카드는 이동 페이즈에서만 나간다 —
            // 도달 칸은 반드시 빈 벌판의 이동 페이즈에 물어야 한다.
            var reachable = arena.MovementState.GetReachablePlayerMoves(entry.Id);
            var cells = reachable.Keys
                .Where(coord => coord != CodexRangeArena.Origin)
                .OrderBy(coord => coord)
                .ToList();

            if (cells.Count == 0)
            {
                return CodexRangeDiagram.Nothing(SelfTargetNote);
            }

            // 걸음 수는 경로탐색이 낸 이동 비용 그대로다 — 도감이 거리를 다시 재지 않는다.
            var steps = new Dictionary<HexCoord, int>(cells.Count);
            foreach (var coord in cells)
            {
                steps[coord] = reachable[coord];
            }

            return new CodexRangeDiagram(
                CodexRangeArena.Origin,
                GridRadiusFor(cells, Array.Empty<HexCoord>()),
                rangeCells: cells,
                rangeSteps: steps,
                rangeLabel: "도달 가능",
                note: entry.TargetMode == CardTargetMode.RandomReachable
                    ? "도착 칸은 이 안에서 무작위로 정해집니다."
                    : string.Empty);
        }

        private static CodexRangeDiagram SelfCentredArea(CodexRangeArena arena, CardCatalogEntry entry)
        {
            var footprint = ResolveFootprint(arena, entry, CodexRangeArena.Origin);
            return new CodexRangeDiagram(
                CodexRangeArena.Origin,
                GridRadiusFor(Array.Empty<HexCoord>(), footprint),
                shapeCells: footprint,
                shapeCenter: CodexRangeArena.Origin,
                shapeLabel: $"착탄 (반경 {entry.AreaRadius})",
                note: "언제나 자신을 중심으로 터집니다.");
        }

        /// <summary>
        /// 아레나의 모든 칸에 대해 <b>출하 검증기</b>에게 "여기를 겨눌 수 있나"를 묻는다.
        /// 이 스윕이 곧 사거리다 — 도감이 거리를 직접 재지 않는다는 것이 이 트랙의 계약이다.
        /// </summary>
        private static List<HexCoord> SweepTargets(CodexRangeArena arena, CardCatalogEntry entry)
        {
            // 공격은 대상 칸에 적이 있어야 하고, 정찰·기물 설치는 반대로 빈 칸이라야 한다.
            // 상태를 잘못 고르면 "사거리 0"이 조용히 나온다.
            var state = entry.ActionType == CardEffectType.Attack ? arena.EnemyState : arena.OpenState;
            var cells = new List<HexCoord>();

            foreach (var coord in arena.Cells)
            {
                bool valid;
                switch (entry.ActionType)
                {
                    case CardEffectType.Attack:
                        valid = state.ValidateAttackTarget(coord, entry.Id).IsValid;
                        break;
                    case CardEffectType.Scout:
                        valid = state.ValidateScoutTarget(coord, entry.Id).IsValid;
                        break;
                    case CardEffectType.FieldObject:
                        valid = state.ValidateFieldObjectTarget(coord, entry.Id).IsValid;
                        break;
                    default:
                        valid = false;
                        break;
                }

                if (valid)
                {
                    cells.Add(coord);
                }
            }

            return cells;
        }

        /// <summary>
        /// 착탄 발자국. 연출·집행이 공유하는 <see cref="EffectAreaFootprint"/>를 그대로 부른다 —
        /// 반경 0이면 겨눈 칸 하나, 아니면 맵에 실재하는 원판이다.
        /// </summary>
        private static IReadOnlyList<HexCoord> ResolveFootprint(
            CodexRangeArena arena,
            CardCatalogEntry entry,
            HexCoord center)
        {
            var buffer = new List<HexCoord>();
            EffectAreaFootprint.Resolve(
                new EffectResultEvent(EffectKind.Damage, center: center, radius: entry.AreaRadius),
                arena.Map,
                buffer);
            return buffer;
        }

        /// <summary>
        /// 그릴 격자의 반경. 아레나 반경(넉넉하게 크다)이 아니라 <b>실제로 쓰인 칸</b>에서 되짚는다 —
        /// 아레나 크기를 그대로 쓰면 사거리 1짜리 카드도 반경 8 격자에 좁쌀만 하게 찍힌다.
        /// </summary>
        private static int GridRadiusFor(IReadOnlyList<HexCoord> rangeCells, IReadOnlyList<HexCoord> shapeCells)
        {
            var reach = 0;
            foreach (var coord in rangeCells)
            {
                reach = Math.Max(reach, CodexRangeArena.Origin.DistanceTo(coord));
            }

            foreach (var coord in shapeCells)
            {
                reach = Math.Max(reach, CodexRangeArena.Origin.DistanceTo(coord));
            }

            return Math.Max(2, reach);
        }

        /// <summary>
        /// 착탄 형상을 어느 칸에 얹어 보여 줄지. 가장 먼 칸 중 하나를 고르면 "사거리 끝에서 이만큼
        /// 터진다"가 한눈에 읽힌다. 정렬이 있으므로 같은 카드는 언제나 같은 그림이 된다.
        /// </summary>
        private static HexCoord PickShapeCenter(IReadOnlyList<HexCoord> rangeCells)
        {
            return rangeCells
                .OrderByDescending(coord => CodexRangeArena.Origin.DistanceTo(coord))
                .ThenByDescending(coord => coord.Q)
                .ThenBy(coord => coord.R)
                .First();
        }
    }
}

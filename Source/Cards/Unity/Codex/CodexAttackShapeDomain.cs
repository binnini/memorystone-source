using System.Collections.Generic;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Codex
{
    /// <summary>
    /// 저작된 공격 형상 전부를 그대로 늘어놓는 <b>개발 전용</b> 도메인. 방향 6갈래와 몸 반경 0~2를
    /// 돌려 보며 형상 저작을 확인하는 자리다.
    /// <para>
    /// 몬스터 도메인 자체는 P2지만 <b>패턴 도해는 P1에서 만든다</b>(인계문 §1-3) — 렌더러가 카드의
    /// 원판 어휘만이 아니라 오프셋 어휘도 받는다는 것을 눈으로 확인해야 하기 때문이다. P2에서 몬스터
    /// 도메인이 붙으면 그쪽이 패턴별로 이 도해를 쓰고, 이 도메인은 저작 확인용으로 남는다.
    /// </para>
    /// <para>
    /// 🔴 <see cref="AttackShapeLibrary.Initialize"/>가 먼저 불려 있어야 한다 — 안 불리면 예외가 아니라
    /// 항목 0개가 된다. 로비는 <c>CombatCatalogTextAssetSource.InitializeAttackShapeLibrary()</c>로 싣는다.
    /// </para>
    /// </summary>
    public sealed class CodexAttackShapeDomain : ICodexDomain
    {
        /// <summary>전투가 신호를 보낼 때 쓰는 것과 <b>같은 문자열</b>이어야 한다 —
        /// 그래서 값을 여기 적지 않고 아래층 상수를 가리킨다(P3 §5-5 지뢰).</summary>
        public const string DomainId = Combat.Runtime.CodexDomainIds.AttackShape;

        private readonly List<CodexEntry> entries = new List<CodexEntry>();

        public CodexAttackShapeDomain(Color accent)
        {
            Accent = accent;
            Build();
        }

        public string Id => DomainId;

        public string Label => "형상";

        public Color Accent { get; }

        /// <summary>저작 참조표다 — 잠글 것이 없다.</summary>
        public bool AlwaysUnlocked => true;

        public IReadOnlyList<CodexEntry> Entries => entries;

        private void Build()
        {
            foreach (var shape in AttackShapeLibrary.AllShapes)
            {
                var east = CodexMonsterPatternRange.Resolve(shape.Id, HexDirection.East, 0);
                entries.Add(new CodexEntry(
                    id: shape.Id,
                    displayName: shape.Id,
                    thumbnail: CodexThumbnail.Resolve(null, shape.Id, Accent),
                    subtitle: $"attack_shapes.csv · 오프셋 {shape.Offsets.Length}",
                    description: DescribeAdjacency(shape.Adjacency),
                    filterChip: AdjacencyChip(shape.Adjacency),
                    detailRows: new[]
                    {
                        new CodexDetailRow("정동 기준 칸 수", east.ShapeCells.Count.ToString()),
                        new CodexDetailRow("저작 오프셋", shape.Offsets.Length.ToString()),
                        new CodexDetailRow("인접 링", AdjacencyChip(shape.Adjacency)),
                    },
                    debugRows: BuildDebugRows(shape),
                    rangeSource: new CodexAttackShapeRangeSource(shape.Id)));
            }
        }

        /// <summary>
        /// 디버그 뷰의 원값 표. 이 도메인은 개발 전용이라 "저작을 그대로 보여 주는" 일이 곧 존재 이유고,
        /// 상세 절이 이미 <b>푼 결과</b>(정동 기준 칸 수)를 적으므로 여기서는 <b>CSV에 적힌 것</b>을 적는다 —
        /// 저작 오프셋이 도해와 어긋날 때 어느 쪽이 틀렸는지는 두 값을 나란히 놓아야 갈린다.
        /// </summary>
        private static IReadOnlyList<CodexDetailRow> BuildDebugRows(AttackShapeDefinition shape)
        {
            return new[]
            {
                new CodexDetailRow("patternId", shape.Id),
                new CodexDetailRow("adjacency", AdjacencyToken(shape.Adjacency)),
                new CodexDetailRow("offsetCount", shape.Offsets.Length.ToString()),
                new CodexDetailRow("offsets", FormatOffsets(shape.Offsets)),
            };
        }

        /// <summary>도감 본문의 인접 어휘 설명. 세 값이 서로 다른 회피 계약을 뜻하므로 문안도 셋이다.</summary>
        private static string DescribeAdjacency(AttackShapeAdjacency adjacency)
        {
            switch (adjacency)
            {
                case AttackShapeAdjacency.None:
                    return "인접 링을 쓰지 않는 원거리 밴드 형상입니다 — 붙으면 안전합니다.";
                case AttackShapeAdjacency.Open:
                    return "인접 칸을 저작으로 일부만 채운 형상입니다 — 붙어도 안전하지 않습니다.";
                case AttackShapeAdjacency.Body:
                    return "몸 둘레 전체를 때리는 형상입니다 — 몸이 클수록 넓어집니다.";
                case AttackShapeAdjacency.BodyShell:
                    return "몸에서 두 칸 떨어진 껍질을 때리는 형상입니다 — 붙으면 안전합니다.";
                default:
                    return "인접 6칸(공용 링)에 저작 오프셋을 더한 형상입니다.";
            }
        }

        private static string AdjacencyChip(AttackShapeAdjacency adjacency)
        {
            switch (adjacency)
            {
                case AttackShapeAdjacency.None: return "링 없음";
                case AttackShapeAdjacency.Open: return "링 부분";
                case AttackShapeAdjacency.Body: return "몸 둘레";
                case AttackShapeAdjacency.BodyShell: return "몸 껍질";
                default: return "링 포함";
            }
        }

        /// <summary>디버그 표에 적는 <b>CSV 원값</b>. 여기서만 소문자 토큰을 쓴다.</summary>
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

        /// <summary>
        /// 저작 오프셋을 <c>(q,r)</c>로 늘어놓는다. 인접 링은 <b>여기 없다</b> —
        /// <c>adjacency=full</c>이면 라이브러리가 실행 시점에 더하는 것이라
        /// CSV 행에는 적혀 있지 않기 때문이다(<c>open</c>은 인접 칸도 이 목록에 그대로 있다).
        /// </summary>
        private static string FormatOffsets(HexCoord[] offsets)
        {
            if (offsets == null || offsets.Length == 0)
            {
                return "(없음)";
            }

            var builder = new System.Text.StringBuilder();
            for (var i = 0; i < offsets.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(offsets[i].ToString());
            }

            return builder.ToString();
        }
    }
}

using System;
using System.Collections.Generic;
using SeoulPlayup.CardCore;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Codex
{
    /// <summary>
    /// 한 항목의 범위 도해를 내는 자리. 뷰는 이 인터페이스만 알고, 아레나도 형상 카탈로그도 모른다.
    /// <para>
    /// 계산은 <see cref="Resolve"/>를 부를 때 일어난다 — 목록을 여는 것만으로 카드 59장의 범위를
    /// 다 풀면 로비가 그만큼 늦어지고, 대부분은 보지도 않을 도해다.
    /// </para>
    /// <para>
    /// 축이 셋이다: <b>갈래</b>(몬스터의 공격 패턴처럼 한 항목이 여러 도해를 갖는 경우) ·
    /// <b>방향</b> · <b>몸 반경</b>. 쓰지 않는 축은 뷰가 조작 줄을 아예 그리지 않는다.
    /// </para>
    /// </summary>
    public interface ICodexRangeSource
    {
        /// <summary>
        /// 갈래 이름들. 비어 있으면 갈래가 하나뿐이라 선택 줄이 뜨지 않는다
        /// (몬스터만 패턴별로 채운다).
        /// </summary>
        IReadOnlyList<string> Variants { get; }

        /// <summary>방향 6갈래를 돌려 볼 수 있는가. 카드는 방향이 없다(언제나 플레이어 기준 원판).</summary>
        bool SupportsDirection { get; }

        /// <summary>몸 반경 조절 폭. 0이면 조절 칸이 뜨지 않는다.</summary>
        int MaxBodyRadius { get; }

        /// <summary>도해를 푼다. 지원하지 않는 축은 인자를 무시한다.</summary>
        CodexRangeDiagram Resolve(int variantIndex, HexDirection direction, int bodyRadius);
    }

    /// <summary>축이 하나도 없는 도해의 공통 바탕 — 갈래·방향·몸 반경을 전부 쓰지 않는다.</summary>
    public abstract class CodexSingleRangeSource : ICodexRangeSource
    {
        private static readonly IReadOnlyList<string> NoVariants = Array.Empty<string>();

        private CodexRangeDiagram cached;
        private bool hasCached;

        public IReadOnlyList<string> Variants => NoVariants;

        public bool SupportsDirection => false;

        public int MaxBodyRadius => 0;

        public CodexRangeDiagram Resolve(int variantIndex, HexDirection direction, int bodyRadius)
        {
            if (!hasCached)
            {
                cached = Build();
                hasCached = true;
            }

            return cached;
        }

        protected abstract CodexRangeDiagram Build();
    }

    /// <summary>
    /// 카드 한 장의 범위. 아레나는 <b>도메인 전체가 하나를 나눠 쓴다</b>(카드마다 만들면 59벌이 된다).
    /// </summary>
    public sealed class CodexCardRangeSource : CodexSingleRangeSource
    {
        private readonly Func<CodexRangeArena> arenaProvider;
        private readonly CardCatalogEntry entry;

        public CodexCardRangeSource(Func<CodexRangeArena> arenaProvider, CardCatalogEntry entry)
        {
            this.arenaProvider = arenaProvider ?? throw new ArgumentNullException(nameof(arenaProvider));
            this.entry = entry ?? throw new ArgumentNullException(nameof(entry));
        }

        protected override CodexRangeDiagram Build() => CodexCardRange.Resolve(arenaProvider(), entry);
    }

    /// <summary>
    /// 함정·소모품처럼 <b>반경 하나</b>가 전부인 항목. 카드 착탄과 같은
    /// <see cref="Combat.Runtime.EffectAreaFootprint"/>를 지나므로 "반경 N"의 뜻이 갈라지지 않는다.
    /// </summary>
    public sealed class CodexRadiusRangeSource : CodexSingleRangeSource
    {
        private readonly int radius;
        private readonly string shapeLabel;
        private readonly string note;

        public CodexRadiusRangeSource(int radius, string shapeLabel, string note = "")
        {
            this.radius = radius;
            this.shapeLabel = shapeLabel;
            this.note = note;
        }

        protected override CodexRangeDiagram Build() => CodexRadiusRange.Resolve(radius, shapeLabel, note);
    }

    /// <summary>몬스터 공격 형상 하나. 방향 6 · 몸 반경 0~2를 돌려 볼 수 있다.</summary>
    public sealed class CodexAttackShapeRangeSource : ICodexRangeSource
    {
        private static readonly IReadOnlyList<string> NoVariants = Array.Empty<string>();

        private readonly string shapeId;

        public CodexAttackShapeRangeSource(string shapeId)
        {
            this.shapeId = shapeId ?? string.Empty;
        }

        public IReadOnlyList<string> Variants => NoVariants;

        public bool SupportsDirection => true;

        public int MaxBodyRadius => CodexMonsterPatternRange.MaxFootprintRadius;

        public CodexRangeDiagram Resolve(int variantIndex, HexDirection direction, int bodyRadius) =>
            CodexMonsterPatternRange.Resolve(shapeId, direction, bodyRadius);
    }

    /// <summary>
    /// 몬스터 하나의 공격 패턴들. 갈래가 패턴이고, 각 갈래 안에서 방향·몸 반경을 돌린다.
    /// <para>
    /// 🔴 형상이 저작되지 않은 패턴(<c>shapeId</c> 빈 값)은 인접 링만 치는 기본 공격이 아니라
    /// <b>사거리 원판</b>으로 해소된다 — 그래서 형상 도해 대신 반경 도해를 낸다. 둘을 섞으면
    /// 도감이 "이 패턴은 앞쪽만 친다"고 거짓말한다.
    /// </para>
    /// </summary>
    public sealed class CodexMonsterPatternRangeSource : ICodexRangeSource
    {
        private readonly IReadOnlyList<Combat.Runtime.MonsterAttackPattern> patterns;
        private readonly List<string> variantLabels = new List<string>();

        public CodexMonsterPatternRangeSource(IReadOnlyList<Combat.Runtime.MonsterAttackPattern> patterns)
        {
            this.patterns = patterns ?? Array.Empty<Combat.Runtime.MonsterAttackPattern>();
            foreach (var pattern in this.patterns)
            {
                variantLabels.Add(string.IsNullOrWhiteSpace(pattern.DisplayName) ? pattern.Id : pattern.DisplayName);
            }
        }

        public IReadOnlyList<string> Variants => variantLabels;

        public bool SupportsDirection => true;

        public int MaxBodyRadius => CodexMonsterPatternRange.MaxFootprintRadius;

        public CodexRangeDiagram Resolve(int variantIndex, HexDirection direction, int bodyRadius)
        {
            if (patterns.Count == 0)
            {
                return CodexRangeDiagram.Nothing("저작된 공격 패턴이 없습니다.");
            }

            var pattern = patterns[Math.Clamp(variantIndex, 0, patterns.Count - 1)];

            if (string.IsNullOrWhiteSpace(pattern.ShapeId))
            {
                return CodexRadiusRange.Resolve(
                    Math.Max(pattern.Range, pattern.AreaRadius),
                    pattern.AreaRadius > 0 ? $"착탄 (반경 {pattern.AreaRadius})" : "사거리",
                    "형상이 저작되지 않아 사거리 원판으로 칩니다.");
            }

            return CodexMonsterPatternRange.Resolve(pattern.ShapeId, direction, bodyRadius);
        }
    }
}

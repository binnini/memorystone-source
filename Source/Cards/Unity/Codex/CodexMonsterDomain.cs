using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Combat.Runtime;
using UnityEngine;

namespace SeoulPlayup.Codex
{
    /// <summary>
    /// 몬스터 · 보스 도메인. <c>monster_catalog.csv</c> + <c>monster_attack_patterns.csv</c> +
    /// <c>monster_pattern_bindings.csv</c>가 합쳐진 <see cref="MonsterCatalogDefinition"/>을 읽는다.
    /// <para>
    /// 범위 도해는 <b>패턴마다</b> 하나씩이다(<see cref="CodexMonsterPatternRangeSource"/>) —
    /// 몬스터 한 마리가 여러 형상을 치므로 하나만 보여 주면 나머지를 감추는 셈이 된다.
    /// </para>
    /// <para>
    /// 🔴 형상 도해가 값을 얻으려면 <c>AttackShapeLibrary.Initialize</c>가 먼저 불려 있어야 한다.
    /// 다행히 <see cref="MonsterCatalogDefinition"/>을 만드는 경로(<c>CreateMonsterCatalog</c>)가
    /// 그것을 먼저 부르는 것이 계약이라, 이 도메인이 살아 있으면 형상도 실려 있다.
    /// </para>
    /// </summary>
    public sealed class CodexMonsterDomain : ICodexDomain
    {
        /// <summary>전투가 신호를 보낼 때 쓰는 것과 <b>같은 문자열</b>이어야 한다 —
        /// 그래서 값을 여기 적지 않고 아래층 상수를 가리킨다(P3 §5-5 지뢰).</summary>
        public const string DomainId = Combat.Runtime.CodexDomainIds.Monster;

        private readonly List<CodexEntry> entries = new List<CodexEntry>();

        /// <param name="thumbnails">
        /// P5에서 구운 전용 썸네일(1층). <see langword="null"/>이면 예전처럼 이름 전체 폴백으로 뜬다 —
        /// 시험 픽스처가 카탈로그 없이 도메인을 세울 수 있어야 하므로 필수 인자로 만들지 않는다.
        /// </param>
        public CodexMonsterDomain(
            MonsterCatalogDefinition catalog,
            Color accent,
            CodexThumbnailCatalog thumbnails = null)
        {
            Accent = accent;
            Build(catalog, thumbnails);
        }

        public string Id => DomainId;

        public string Label => "몬스터";

        public Color Accent { get; }

        /// <summary>시야에 들어와 본 몬스터만 열린다 — P3에서 해금 집합이 붙는다.</summary>
        public bool AlwaysUnlocked => false;

        public IReadOnlyList<CodexEntry> Entries => entries;

        private void Build(MonsterCatalogDefinition catalog, CodexThumbnailCatalog thumbnails)
        {
            if (catalog == null)
            {
                return;
            }

            foreach (var entry in catalog.Entries)
            {
                var patterns = entry.AttackPatterns ?? System.Array.Empty<MonsterAttackPattern>();

                // 🔑 프리팹이 아니라 <b>몬스터 id</b>로 찾는다. M901·M905가 프리팹 하나를 공유하지만
                // (인계문 §2.3) 지금은 게임에서도 같은 모델로 보이므로 도감이 같게 보이는 것이 사실이고,
                // 고깔 전용 모델이 반입되면 CSV 경로만 갈라져 다시 굽는 것으로 저절로 나뉜다.
                var dedicated = thumbnails != null ? thumbnails.Resolve(entry.Id) : null;

                entries.Add(new CodexEntry(
                    id: entry.Id,
                    displayName: entry.DisplayName,
                    // 구운 그림은 칸 비율에 맞춰 여백째 렌더된다 — 잘라내면 실루엣이 뭉개진다.
                    thumbnail: CodexThumbnail.Resolve(
                        dedicated, entry.DisplayName, Accent, preserveAspect: dedicated != null),
                    subtitle: $"{entry.Id} · {entry.Archetype}",
                    description: Describe(entry, patterns),
                    filterChip: entry.Archetype,
                    metaChips: BuildMetaChips(entry, patterns),
                    detailRows: BuildDetailRows(entry, patterns),
                    debugRows: BuildDebugRows(entry),
                    rangeSource: patterns.Length > 0 ? new CodexMonsterPatternRangeSource(patterns) : null));
            }
        }

        /// <summary>
        /// 몬스터에는 저작된 설명문이 없다(<c>designerNote</c>는 저작 메모지 플레이어 문장이 아니다).
        /// 값이 말하는 것만 한 문장으로 적는다.
        /// </summary>
        private static string Describe(MonsterCatalogEntry entry, IReadOnlyList<MonsterAttackPattern> patterns)
        {
            var mobility = entry.MovePerTurn <= 0 ? "제자리에서" : $"턴마다 {entry.MovePerTurn}칸씩 움직이며";
            var reach = patterns.Count == 0 ? 0 : patterns.Max(pattern => pattern.Range);
            return $"{mobility} {entry.DetectionRange}칸 안을 살피고, 사거리 {reach} 안을 공격합니다.";
        }

        private static IReadOnlyList<string> BuildMetaChips(
            MonsterCatalogEntry entry,
            IReadOnlyList<MonsterAttackPattern> patterns)
        {
            var chips = new List<string>(3) { $"체력 {entry.Hp}" };

            if (patterns.Count > 1)
            {
                chips.Add($"패턴 {patterns.Count}");
            }

            // 어휘 정본은 계획·툴팁과 같은 「맷집」·「약오름」·「견고」다(2026-08-10 사용자 확정).
            // 도감만 「강인」·「동요」로 갈라져 있었다 — 같은 것을 두 이름으로 부르면 플레이어가
            // 배지·툴팁에서 본 말과 도감을 잇지 못한다.
            if (entry.HasToughness)
            {
                chips.Add("맷집");
            }

            if (entry.HasSturdyBlock)
            {
                chips.Add("견고");
            }

            return chips;
        }

        private static IReadOnlyList<CodexDetailRow> BuildDetailRows(
            MonsterCatalogEntry entry,
            IReadOnlyList<MonsterAttackPattern> patterns)
        {
            var rows = new List<CodexDetailRow>(8)
            {
                new CodexDetailRow("체력", entry.Hp.ToString()),
                new CodexDetailRow("이동", entry.MovePerTurn <= 0 ? "없음" : $"{entry.MovePerTurn}칸"),
                new CodexDetailRow("감지", $"{entry.DetectionRange}칸"),
            };

            if (entry.AttackSpeed != 1)
            {
                rows.Add(new CodexDetailRow("공격 속도", entry.AttackSpeed.ToString()));
            }

            if (entry.HasAgitation)
            {
                rows.Add(new CodexDetailRow("약오름 최대", entry.AgitationMaxStacks.ToString()));
            }

            if (entry.HasToughness)
            {
                rows.Add(new CodexDetailRow("맷집 재충전", $"{entry.ToughnessReloadTurns}턴"));
            }

            if (entry.HasSturdyBlock)
            {
                rows.Add(new CodexDetailRow("견고", "방어막이 턴이 지나도 사라지지 않음"));
            }

            foreach (var pattern in patterns)
            {
                var name = string.IsNullOrWhiteSpace(pattern.DisplayName) ? pattern.Id : pattern.DisplayName;
                rows.Add(new CodexDetailRow(name, DescribePattern(pattern)));
            }

            return rows;
        }

        /// <summary>패턴 한 줄 — 피해·사거리·형상까지. 도해가 형상을 그리므로 여기는 수치를 맡는다.</summary>
        private static string DescribePattern(MonsterAttackPattern pattern)
        {
            var parts = new List<string>(4);

            if (pattern.Damage > 0)
            {
                parts.Add(pattern.HitCount > 1
                    ? $"피해 {pattern.Damage}×{pattern.HitCount}"
                    : $"피해 {pattern.Damage}");
            }

            parts.Add($"사거리 {pattern.Range}");

            if (pattern.AreaRadius > 0)
            {
                parts.Add($"반경 {pattern.AreaRadius}");
            }

            if (pattern.ShieldGain > 0)
            {
                parts.Add($"방어막 {pattern.ShieldGain}");
            }

            if (!string.IsNullOrWhiteSpace(pattern.ShapeId))
            {
                parts.Add(pattern.ShapeId);
            }

            return string.Join(" · ", parts);
        }

        private static IReadOnlyList<CodexDetailRow> BuildDebugRows(MonsterCatalogEntry entry)
        {
            return new[]
            {
                new CodexDetailRow("archetype", entry.Archetype),
                new CodexDetailRow("behaviorProfileRef", entry.BehaviorProfileRef),
                new CodexDetailRow("status", entry.Status),
                new CodexDetailRow("visualPrefabPath", entry.VisualPrefabPath),
                new CodexDetailRow("agitationMaxStacks", entry.AgitationMaxStacks.ToString()),
                new CodexDetailRow("toughnessReloadTurns", entry.ToughnessReloadTurns.ToString()),
                new CodexDetailRow("sturdyBlock", entry.HasSturdyBlock.ToString()),
                new CodexDetailRow("onDeathEffectRef", entry.OnDeathEffectRef),
                new CodexDetailRow("onDeathEffectParam", entry.OnDeathEffectParam),
            };
        }
    }
}

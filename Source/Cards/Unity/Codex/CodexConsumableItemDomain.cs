using System.Collections.Generic;
using SeoulPlayup.Combat.Runtime;
using UnityEngine;

namespace SeoulPlayup.Codex
{
    /// <summary>
    /// 소모성 아이템 도메인. <c>consumable_items.csv</c>(<see cref="ConsumableItemCatalogDefinition"/>)를
    /// 그대로 읽는다.
    /// <para>
    /// 범위 도해는 <b>반경이 있는 것만</b> 낸다 — 반경 0인 아이템에 도해를 그리면 "한 칸을 겨눈다"로
    /// 읽히는데, 실제로는 겨냥 자체가 없는 자기 대상 아이템이다.
    /// </para>
    /// </summary>
    public sealed class CodexConsumableItemDomain : ICodexDomain
    {
        /// <summary>전투가 신호를 보낼 때 쓰는 것과 <b>같은 문자열</b>이어야 한다 —
        /// 그래서 값을 여기 적지 않고 아래층 상수를 가리킨다(P3 §5-5 지뢰).</summary>
        public const string DomainId = Combat.Runtime.CodexDomainIds.Consumable;

        private readonly List<CodexEntry> entries = new List<CodexEntry>();

        /// <param name="icons">
        /// 소모품 아이콘 해소기(<c>iconId</c> → 스프라이트). 유물 도메인과 <b>같은 카탈로그</b>다.
        /// <see langword="null"/>이면 종전대로 2층(이름 전체) 폴백으로 뜬다.
        /// </param>
        public CodexConsumableItemDomain(
            ConsumableItemCatalogDefinition catalog,
            Color accent,
            Cards.Unity.RuntimeUiAssetCatalog icons = null)
        {
            Accent = accent;
            Build(catalog, icons);
        }

        public string Id => DomainId;

        public string Label => "소모품";

        public Color Accent { get; }

        /// <summary>주워 본 아이템만 열린다 — P3에서 해금 집합이 붙는다.</summary>
        public bool AlwaysUnlocked => false;

        public IReadOnlyList<CodexEntry> Entries => entries;

        private void Build(ConsumableItemCatalogDefinition catalog, Cards.Unity.RuntimeUiAssetCatalog icons)
        {
            if (catalog == null)
            {
                return;
            }

            foreach (var definition in catalog.Entries)
            {
                if (definition == null)
                {
                    continue;
                }

                var dedicated = icons != null ? icons.ResolveItemIcon(definition.IconId) : null;

                entries.Add(new CodexEntry(
                    id: definition.Id,
                    displayName: definition.DisplayName,
                    thumbnail: CodexThumbnail.Resolve(dedicated, definition.DisplayName, Accent, preserveAspect: true),
                    subtitle: $"{definition.Id} · {definition.Category}",
                    description: definition.Description,
                    // 칩은 저작된 분류(생존·버프·즉발·유틸…)를 그대로 쓴다 — 도감이 분류를 새로 지으면
                    // 저작과 화면이 갈라진다.
                    filterChip: definition.Category,
                    metaChips: BuildMetaChips(definition),
                    detailRows: BuildDetailRows(definition),
                    debugRows: BuildDebugRows(definition),
                    rangeSource: definition.Radius > 0
                        ? new CodexRadiusRangeSource(
                            definition.Radius,
                            $"덮는 칸 (반경 {definition.Radius})",
                            definition.Targeting == ConsumableItemTargeting.None
                                ? "언제나 자신을 중심으로 터집니다."
                                : "겨눈 칸을 중심으로 터집니다.")
                        : null));
            }
        }

        private static IReadOnlyList<string> BuildMetaChips(ConsumableItemDefinition definition)
        {
            var chips = new List<string>(2);

            if (definition.Targeting != ConsumableItemTargeting.None)
            {
                chips.Add(TargetingLabel(definition.Targeting));
            }

            if (definition.Radius > 0)
            {
                chips.Add($"반경 {definition.Radius}");
            }

            return chips;
        }

        private static IReadOnlyList<CodexDetailRow> BuildDetailRows(ConsumableItemDefinition definition)
        {
            var rows = new List<CodexDetailRow>(5)
            {
                new CodexDetailRow("분류", definition.Category),
                new CodexDetailRow("대상", TargetingLabel(definition.Targeting)),
            };

            if (definition.Amount > 0)
            {
                rows.Add(new CodexDetailRow("수치", definition.Amount.ToString()));
            }

            if (definition.DurationTurns > 0)
            {
                rows.Add(new CodexDetailRow("지속", $"{definition.DurationTurns}턴"));
            }

            if (definition.Radius > 0)
            {
                rows.Add(new CodexDetailRow("반경", definition.Radius.ToString()));
            }

            return rows;
        }

        private static IReadOnlyList<CodexDetailRow> BuildDebugRows(ConsumableItemDefinition definition)
        {
            return new[]
            {
                new CodexDetailRow("effectRef", definition.EffectRef),
                new CodexDetailRow("targeting", definition.Targeting.ToString()),
                new CodexDetailRow("category", definition.Category),
                new CodexDetailRow("amount", definition.Amount.ToString()),
                new CodexDetailRow("durationTurns", definition.DurationTurns.ToString()),
                new CodexDetailRow("radius", definition.Radius.ToString()),
            };
        }

        private static string TargetingLabel(ConsumableItemTargeting targeting)
        {
            switch (targeting)
            {
                case ConsumableItemTargeting.None: return "자신";
                case ConsumableItemTargeting.Tile: return "칸";
                case ConsumableItemTargeting.Enemy: return "몬스터";
                default: return targeting.ToString();
            }
        }
    }
}

using System.Collections.Generic;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using UnityEngine;

namespace SeoulPlayup.Codex
{
    /// <summary>
    /// 상태이상·버프 도메인. <c>status_effects.csv</c>(<see cref="StatusEffectCatalogDefinition"/>)를
    /// 읽고 아이콘은 <see cref="StatusEffectIconCatalog"/>에서 가져온다 — 둘 다 전투가 이미 쓰는
    /// 것이라 도감 전용 저작이 없다.
    /// <para>
    /// P0가 이 도메인부터 붙이는 이유(<c>docs/codex-plan.md</c> §4.2): 아이콘이 이미 있고(16/19라
    /// 폴백도 같은 화면에서 검증된다), 범위 도해가 필요 없고, Q6에 따라 해금을 타지 않아
    /// 다른 페이즈에 의존하지 않는 유일한 도메인이다.
    /// </para>
    /// </summary>
    public sealed class CodexStatusEffectDomain : ICodexDomain
    {
        /// <summary>전투가 신호를 보낼 때 쓰는 것과 <b>같은 문자열</b>이어야 한다 —
        /// 그래서 값을 여기 적지 않고 아래층 상수를 가리킨다(P3 §5-5 지뢰).</summary>
        public const string DomainId = Combat.Runtime.CodexDomainIds.StatusEffect;

        private readonly List<CodexEntry> entries = new List<CodexEntry>();

        public CodexStatusEffectDomain(
            StatusEffectCatalogDefinition catalog,
            StatusEffectIconCatalog icons,
            Color accent,
            Sprite defaultThumbnail = null)
        {
            Accent = accent;
            Build(catalog, icons, defaultThumbnail);
        }

        public string Id => DomainId;

        public string Label => "상태이상 · 버프";

        public Color Accent { get; }

        /// <summary>Q6 확정 — 규칙 참조표는 잠그지 않는다.</summary>
        public bool AlwaysUnlocked => true;

        public IReadOnlyList<CodexEntry> Entries => entries;

        private void Build(StatusEffectCatalogDefinition catalog, StatusEffectIconCatalog icons, Sprite defaultThumbnail)
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

                var icon = icons != null ? icons.GetSprite(definition.Kind) : null;
                var thumbnail = CodexThumbnail.Resolve(
                    icon,
                    definition.DisplayNameKo,
                    Accent,
                    defaultThumbnail,
                    // 아이콘은 잘리면 뜻이 사라진다 — 여백을 남겨 통째로 보여준다.
                    preserveAspect: true);

                entries.Add(new CodexEntry(
                    id: definition.Kind.ToString(),
                    displayName: definition.DisplayNameKo,
                    thumbnail: thumbnail,
                    subtitle: $"{definition.Kind} · {PolarityLabel(definition.Kind)}",
                    description: ResolveDescription(definition),
                    filterChip: PolarityLabel(definition.Kind),
                    metaChips: BuildMetaChips(definition),
                    detailRows: BuildDetailRows(definition),
                    debugRows: BuildDebugRows(definition)));
            }
        }

        /// <summary>
        /// 설명의 정본은 <b>game_keywords.csv 효과문 한 벌</b>이다(WS-I Q-I1, DEC-2026-08-19-08) —
        /// HUD 툴팁이 읽는 그 문장을 도감도 그대로 보여준다(#24 「도감에서 HUD 설명 그대로 열람」).
        /// {값}은 정적 표면이므로 값 컬럼으로 해소한다. 키워드 행이 없거나 카탈로그가 아직 없으면
        /// status_effects.csv의 옛 문안으로 폴백한다(그 컬럼은 이제 휴면 — 정본이 아니다).
        /// </summary>
        private static string ResolveDescription(StatusEffectDefinition definition)
        {
            var catalog = CardKeywordCatalogProvider.Active;
            if (catalog != null
                && catalog.TryGet(StatusEffectInfo.DisplayName(definition.Kind), out var keyword)
                && !string.IsNullOrWhiteSpace(keyword.Effect))
            {
                return KeywordEffectText.Resolve(keyword);
            }

            return definition.DescriptionKo;
        }

        private static string PolarityLabel(StatusEffectKind kind)
        {
            return StatusEffectInfo.GetPolarity(kind) == StatusEffectPolarity.Buff ? "버프" : "상태이상";
        }

        private static IReadOnlyList<string> BuildMetaChips(StatusEffectDefinition definition)
        {
            var chips = new List<string>(2);

            // Amount를 읽지 않는 종류(속박·기절)에 "값 0"을 띄우면 0이 의미 있는 수치처럼 보인다.
            if (definition.ValueMode != StatusEffectValueMode.None && definition.DefaultAmount > 0)
            {
                chips.Add($"값 {definition.DefaultAmount}");
            }

            if (definition.DefaultDurationTurns > 0)
            {
                chips.Add($"{definition.DefaultDurationTurns}턴");
            }

            return chips;
        }

        private static IReadOnlyList<CodexDetailRow> BuildDetailRows(StatusEffectDefinition definition)
        {
            var rows = new List<CodexDetailRow>(3);

            if (definition.ValueMode != StatusEffectValueMode.None && definition.DefaultAmount > 0)
            {
                rows.Add(new CodexDetailRow("기본 값", definition.DefaultAmount.ToString()));
            }

            rows.Add(new CodexDetailRow(
                "기본 지속",
                definition.DefaultDurationTurns > 0 ? $"{definition.DefaultDurationTurns}턴" : "지속 없음"));
            rows.Add(new CodexDetailRow("중첩 방식", StackPolicyLabel(definition.StackPolicy)));
            return rows;
        }

        /// <summary>
        /// 적용 시점·만료 규칙·값 축은 저작자와 우리를 위한 정보다 — 플레이어에게는 뜻이 없으므로
        /// 디버그 뷰로 내린다.
        /// </summary>
        private static IReadOnlyList<CodexDetailRow> BuildDebugRows(StatusEffectDefinition definition)
        {
            return new[]
            {
                new CodexDetailRow("effectKind", definition.Kind.ToString()),
                new CodexDetailRow("valueMode", definition.ValueMode.ToString()),
                new CodexDetailRow("timing", definition.Timing.ToString()),
                new CodexDetailRow("stackPolicy", definition.StackPolicy.ToString()),
                new CodexDetailRow("expirePolicy", definition.ExpirePolicy.ToString()),
                new CodexDetailRow("maxStacks", definition.MaxStacks.ToString()),
            };
        }

        private static string StackPolicyLabel(StatusEffectStackPolicy policy)
        {
            switch (policy)
            {
                case StatusEffectStackPolicy.Add: return "값이 더해집니다";
                case StatusEffectStackPolicy.RefreshDuration: return "지속이 새로 고쳐집니다";
                default: return policy.ToString();
            }
        }
    }
}

using System.Collections.Generic;
using SeoulPlayup.Combat.Runtime;
using UnityEngine;

namespace SeoulPlayup.Codex
{
    /// <summary>
    /// 유물 도메인. <c>relics.csv</c>(<see cref="RelicCatalogDefinition"/>)를 그대로 읽는다 —
    /// 전투가 쓰는 카탈로그와 같은 것이라 도감 전용 저작이 없다.
    /// <para>
    /// 🔴 저주는 여기 없다. Q3 확정대로 <b>카드의 한 종류</b>이고 <see cref="CodexCardDomain"/>의
    /// 필터 칩으로 산다 — 유물 도메인에 끌어오면 같은 것이 두 곳에 뜬다.
    /// </para>
    /// </summary>
    public sealed class CodexRelicDomain : ICodexDomain
    {
        /// <summary>전투가 신호를 보낼 때 쓰는 것과 <b>같은 문자열</b>이어야 한다 —
        /// 그래서 값을 여기 적지 않고 아래층 상수를 가리킨다(P3 §5-5 지뢰).</summary>
        public const string DomainId = Combat.Runtime.CodexDomainIds.Relic;

        private readonly List<CodexEntry> entries = new List<CodexEntry>();

        /// <param name="icons">
        /// 유물 아이콘 해소기(<c>iconId</c> → 스프라이트). 세 소비처가 공유하는 그 카탈로그다 —
        /// <see langword="null"/>이면 종전대로 2층(이름 전체) 폴백으로 뜬다.
        /// </param>
        public CodexRelicDomain(
            RelicCatalogDefinition catalog,
            Color accent,
            Cards.Unity.RuntimeUiAssetCatalog icons = null)
        {
            Accent = accent;
            Build(catalog, icons);
        }

        public string Id => DomainId;

        public string Label => "유물";

        public Color Accent { get; }

        /// <summary>얻어 본 유물만 열린다 — P3에서 해금 집합이 붙는다.</summary>
        public bool AlwaysUnlocked => false;

        public IReadOnlyList<CodexEntry> Entries => entries;

        private void Build(RelicCatalogDefinition catalog, Cards.Unity.RuntimeUiAssetCatalog icons)
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

                // 아이콘은 카드 프레임처럼 잘리면 뜻이 사라지는 그림이라 여백을 남겨 통째로 보인다.
                var dedicated = icons != null ? icons.ResolveItemIcon(definition.IconId) : null;

                entries.Add(new CodexEntry(
                    id: definition.Id,
                    displayName: definition.DisplayName,
                    thumbnail: CodexThumbnail.Resolve(dedicated, definition.DisplayName, Accent, preserveAspect: true),
                    subtitle: $"{definition.Id} · {TriggerLabel(definition)}",
                    description: definition.Description,
                    filterChip: TriggerLabel(definition),
                    metaChips: BuildMetaChips(definition),
                    detailRows: BuildDetailRows(definition),
                    debugRows: BuildDebugRows(definition)));
            }
        }

        /// <summary>
        /// 상시로 붙어 있는 유물과 무언가 일어날 때만 깨어나는 유물은 <b>읽는 법이 다르다</b> —
        /// 그래서 이것이 필터 축이다. 수치 크기나 효과 종류로 가르면 28종이 잘게 부서지기만 한다.
        /// </summary>
        private static string TriggerLabel(PlayerPermanentItemDefinition definition)
        {
            return definition.TriggerKind == RelicTriggerKind.None ? "상시" : "발동";
        }

        private static IReadOnlyList<string> BuildMetaChips(PlayerPermanentItemDefinition definition)
        {
            var chips = new List<string>(2);

            // 🔴 트리거 유물은 EffectKind가 <b>의도적으로</b> None이라(RC-9) 스칼라 문장을 지으면
            //    「None +1」이 찍힌다 — 어휘 정본이 그 판단을 대신한다.
            var headline = PlayerPermanentItemText.Headline(
                definition.EffectKind, definition.EffectAmount, definition.TriggerKind, definition.TriggerParam);
            if (headline != PlayerPermanentItemText.Unknown)
            {
                chips.Add(headline);
            }

            if (definition.ExtraEffects != null && definition.ExtraEffects.Count > 0)
            {
                chips.Add($"효과 +{definition.ExtraEffects.Count}");
            }

            return chips;
        }

        private static IReadOnlyList<CodexDetailRow> BuildDetailRows(PlayerPermanentItemDefinition definition)
        {
            var rows = new List<CodexDetailRow>(5)
            {
                new CodexDetailRow(
                    "효과",
                    PlayerPermanentItemText.Headline(
                        definition.EffectKind, definition.EffectAmount, definition.TriggerKind, definition.TriggerParam)),
            };

            if (definition.DurationTurns > 0)
            {
                rows.Add(new CodexDetailRow("지속", $"{definition.DurationTurns}턴"));
            }

            if (definition.TriggerKind != RelicTriggerKind.None)
            {
                // 발동 조건은 이미 「효과」 줄이 문장으로 말한다 — 여기서 enum 이름과 원시 수치를
                // 또 흘리면 플레이어가 읽을 수 없는 글자가 둘 늘어날 뿐이다(디버그 행에 남아 있다).
                rows.Add(new CodexDetailRow("발동", TriggerLabel(definition)));
            }

            if (definition.ExtraEffects != null)
            {
                foreach (var extra in definition.ExtraEffects)
                {
                    rows.Add(new CodexDetailRow("추가 효과", PlayerPermanentItemText.Effect(extra.Kind, extra.Amount)));
                }
            }

            return rows;
        }

        private static IReadOnlyList<CodexDetailRow> BuildDebugRows(PlayerPermanentItemDefinition definition)
        {
            return new[]
            {
                new CodexDetailRow("kind", definition.Kind.ToString()),
                new CodexDetailRow("effectKind", definition.EffectKind.ToString()),
                new CodexDetailRow("effectAmount", definition.EffectAmount.ToString()),
                new CodexDetailRow("durationTurns", definition.DurationTurns.ToString()),
                new CodexDetailRow("triggerRef", definition.TriggerKind.ToString()),
                new CodexDetailRow("triggerParam", definition.TriggerParam.ToString()),
            };
        }

        /// <summary>
        /// 효과 종류의 한글 이름. ⚠️여기서 <c>default</c>로 새면 새 효과가 생겼을 때 <b>영어 enum 이름이
        /// 그대로 화면에 뜬다</b> — 그건 감추는 것보다 낫다(모르는 값을 아는 척하지 않는다).
        /// </summary>
    }
}

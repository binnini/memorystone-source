using System.Collections.Generic;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEngine;

namespace SeoulPlayup.Codex
{
    /// <summary>
    /// 함정 도메인. <see cref="TrapPresetCatalog"/>(저작 프리셋)를 읽는다.
    /// <para>
    /// 🔴 <b>카탈로그는 직렬화 참조로 받아야 한다.</b> <see cref="TrapPresetCatalog.LoadDefault"/>는
    /// <c>Resources.Load</c> 뒤에 <c>AssetDatabase</c> 폴백이 붙어 있는데, 이 에셋은
    /// <c>Resources/</c> 아래에 있지 않다 — 즉 <b>빌드에서는 언제나 <c>null</c></b>이고 에디터에서만
    /// 멀쩡해 보인다(<c>docs/codex-plan.md</c> §2.2 지뢰의 교과서적 사례).
    /// </para>
    /// <para>
    /// 썸네일은 Q10·Q12의 "표식 판 + 효과 아이콘" 조합 중 <b>효과 아이콘 쪽만</b> 붙였다 —
    /// 함정이 거는 상태이상이 있으면 그 아이콘을 쓴다. 표식 판은 아트 발주(P7) 대기다.
    /// </para>
    /// </summary>
    public sealed class CodexTrapDomain : ICodexDomain
    {
        /// <summary>전투가 신호를 보낼 때 쓰는 것과 <b>같은 문자열</b>이어야 한다 —
        /// 그래서 값을 여기 적지 않고 아래층 상수를 가리킨다(P3 §5-5 지뢰).</summary>
        public const string DomainId = Combat.Runtime.CodexDomainIds.Trap;

        private readonly List<CodexEntry> entries = new List<CodexEntry>();

        public CodexTrapDomain(TrapPresetCatalog catalog, StatusEffectIconCatalog icons, Color accent)
        {
            Accent = accent;
            Build(catalog, icons);
        }

        public string Id => DomainId;

        public string Label => "함정";

        public Color Accent { get; }

        /// <summary>밟아 본 함정만 열린다 — P3에서 해금 집합이 붙는다.</summary>
        public bool AlwaysUnlocked => false;

        public IReadOnlyList<CodexEntry> Entries => entries;

        private void Build(TrapPresetCatalog catalog, StatusEffectIconCatalog icons)
        {
            if (catalog == null)
            {
                return;
            }

            foreach (var preset in catalog.Presets)
            {
                if (preset == null)
                {
                    continue;
                }

                // ⚠️ Burn은 D-1로 폐기된 유령 값이다(대응 상태이상이 없어 밟으면 집행이 던진다).
                // 도감 뷰에서는 감추고 디버그 뷰에서 경고와 함께 남긴다 — 계획 §9-6.
                var isDeprecated = preset.EffectKind == HexTrapEffectKind.Burn;

                var icon = ResolveIcon(preset, icons);
                entries.Add(new CodexEntry(
                    id: preset.PresetId,
                    displayName: preset.DisplayName,
                    thumbnail: CodexThumbnail.Resolve(icon, preset.DisplayName, Accent, preserveAspect: icon != null),
                    subtitle: $"{preset.PresetId} · {EffectLabel(preset.EffectKind)}",
                    description: Describe(preset),
                    filterChip: isDeprecated ? string.Empty : EffectLabel(preset.EffectKind),
                    metaChips: BuildMetaChips(preset),
                    detailRows: BuildDetailRows(preset),
                    debugRows: BuildDebugRows(preset),
                    authoringWarning: isDeprecated
                        ? "폐기된 효과(Burn) — 대응 상태이상이 없어 밟으면 집행이 예외를 던진다."
                        : string.Empty,
                    // 반경 0이면 밟은 칸 하나다 — 그것도 그려야 "한 칸짜리"라는 것이 읽힌다.
                    rangeSource: new CodexRadiusRangeSource(
                        preset.Radius,
                        preset.Radius > 0 ? $"덮는 칸 (반경 {preset.Radius})" : "밟는 칸",
                        preset.PeriodTurns > 0
                            ? $"{preset.PeriodTurns}턴마다 스스로 터집니다 — 밟아도 터지지 않습니다."
                            : string.Empty)));
            }
        }

        /// <summary>
        /// 함정이 거는 상태이상의 아이콘. 🔴판정을 여기서 다시 짜지 않고
        /// <see cref="CombatState.TryGetTrapStatusEffectKind"/>에 묻는다 — 밖에서 표를 하나 더 만들면
        /// 아이콘과 실제로 걸리는 상태이상이 언젠가 갈라진다.
        /// </summary>
        private static Sprite ResolveIcon(TrapPresetCatalog.Entry preset, StatusEffectIconCatalog icons)
        {
            if (icons == null || !CombatState.TryGetTrapStatusEffectKind(preset.EffectKind, out var statusKind))
            {
                return null;
            }

            return icons.GetSprite(statusKind);
        }

        /// <summary>
        /// 함정에는 저작된 설명문이 없다 — 값에서 한 문장을 만든다. 지어낸 이야기를 붙이지 않고
        /// <b>저작값이 말하는 것만</b> 적는다.
        /// </summary>
        private static string Describe(TrapPresetCatalog.Entry preset)
        {
            var target = preset.AffectsPlayer && preset.AffectsMonsters
                ? "밟은 누구에게나"
                : preset.AffectsMonsters ? "몬스터에게" : "플레이어에게";

            switch (preset.EffectKind)
            {
                case HexTrapEffectKind.Damage:
                    return $"{target} 피해 {preset.EffectAmount}를 줍니다.";
                case HexTrapEffectKind.Teleport:
                    return $"{target} 반경 {preset.EffectAmount} 안의 칸으로 순간이동시킵니다.";
                case HexTrapEffectKind.SpawnMonsters:
                    return $"몬스터를 {preset.EffectAmount}마리 부릅니다.";
                case HexTrapEffectKind.InjectStatusCard:
                    return $"상태 카드를 {preset.EffectAmount}장 덱에 넣습니다.";
                default:
                    if (!CombatState.TryGetTrapStatusEffectKind(preset.EffectKind, out _))
                    {
                        return $"{target} {EffectLabel(preset.EffectKind)} 효과를 줍니다.";
                    }

                    // 조사는 카드 설명이 쓰는 그 해소기를 그대로 쓴다 — "중독을 / 기절을 / 둔화를"이
                    // 앞 글자 받침에 따라 갈리므로 "을(를)"로 도망치면 도감만 어색해진다.
                    // ⚠️ Agree는 <b>조사만</b> 돌려준다(값을 붙여 주지 않는다).
                    var effect = EffectLabel(preset.EffectKind);
                    return $"{target} {effect}{KoreanParticle.Agree(effect, "을")} {preset.DurationTurns}턴 겁니다.";
            }
        }

        private static IReadOnlyList<string> BuildMetaChips(TrapPresetCatalog.Entry preset)
        {
            var chips = new List<string>(2);

            if (!preset.OneShot)
            {
                chips.Add("반복");
            }

            if (preset.PeriodTurns > 0)
            {
                chips.Add($"{preset.PeriodTurns}턴 주기");
            }

            return chips;
        }

        private static IReadOnlyList<CodexDetailRow> BuildDetailRows(TrapPresetCatalog.Entry preset)
        {
            var rows = new List<CodexDetailRow>(6)
            {
                new CodexDetailRow("효과", EffectLabel(preset.EffectKind)),
            };

            if (preset.EffectAmount > 0)
            {
                rows.Add(new CodexDetailRow("수치", preset.EffectAmount.ToString()));
            }

            if (CombatState.TryGetTrapStatusEffectKind(preset.EffectKind, out _))
            {
                rows.Add(new CodexDetailRow("지속", $"{preset.DurationTurns}턴"));
            }

            rows.Add(new CodexDetailRow("반경", preset.Radius.ToString()));
            rows.Add(new CodexDetailRow("대상", preset.AffectsPlayer && preset.AffectsMonsters
                ? "플레이어 · 몬스터"
                : preset.AffectsMonsters ? "몬스터" : "플레이어"));
            // I-30/I-34(WS-I): 「턴이 끝나면」 분기는 거짓이었다 — 밟기도 주기도 아닌 조합은 영원히
            // 발동하지 않는다(밟기 해소는 TriggerOnEnter만, 주기 해소는 IsPeriodic만 통과). 주기의
            // 실제 발화 시점도 막연한 「스스로」가 아니라 <b>몬스터 행동 직전</b>이다.
            rows.Add(new CodexDetailRow("발동", preset.PeriodTurns > 0
                ? $"{preset.PeriodTurns}턴마다 몬스터 행동 직전에"
                : preset.TriggerOnEnter ? "밟으면" : "발동하지 않음(저작 오류)"));
            rows.Add(new CodexDetailRow("횟수", preset.OneShot ? "한 번" : "여러 번"));

            return rows;
        }

        private static IReadOnlyList<CodexDetailRow> BuildDebugRows(TrapPresetCatalog.Entry preset)
        {
            return new[]
            {
                new CodexDetailRow("presetId", preset.PresetId),
                new CodexDetailRow("effectKind", preset.EffectKind.ToString()),
                new CodexDetailRow("effectAmount", preset.EffectAmount.ToString()),
                new CodexDetailRow("durationTurns", preset.DurationTurns.ToString()),
                new CodexDetailRow("radius", preset.Radius.ToString()),
                new CodexDetailRow("oneShot", preset.OneShot ? "TRUE" : "FALSE"),
                new CodexDetailRow("triggerOnEnter", preset.TriggerOnEnter ? "TRUE" : "FALSE"),
                new CodexDetailRow("periodTurns", preset.PeriodTurns.ToString()),
                new CodexDetailRow("monsterDefinitionId", preset.MonsterDefinitionId),
                new CodexDetailRow("statusCardId", preset.StatusCardId),
            };
        }

        private static string EffectLabel(HexTrapEffectKind kind)
        {
            switch (kind)
            {
                case HexTrapEffectKind.Damage: return "피해";
                case HexTrapEffectKind.Poison: return "중독";
                case HexTrapEffectKind.Stun: return "기절";
                case HexTrapEffectKind.Slow: return "둔화";
                case HexTrapEffectKind.VisionDown: return "실명";
                case HexTrapEffectKind.Weaken: return "쇠약";
                case HexTrapEffectKind.Disarm: return "무장 해제";
                case HexTrapEffectKind.Teleport: return "순간이동";
                case HexTrapEffectKind.SpawnMonsters: return "몬스터 소환";
                case HexTrapEffectKind.InjectStatusCard: return "상태 카드";
                default: return kind.ToString();
            }
        }
    }
}

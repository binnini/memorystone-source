using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// 연마 비교 화면의 「전 → 후」 요약(D-6, 2026-09-02 #9). 순수 함수 — 뷰는 색 두 개만 넘기고 문자열을 받는다.
    ///
    /// 효과 연마 2차(DEC-2026-09-06-08)까지는 수치 축(코스트·수치·사거리·범위·지속)만 비교해 M05(민첩 2→3)·F05(타수 2→3)·
    /// 효과 연마 8장은 「문안이 달라집니다」로만 뭉뚱그려졌다. 이제 세 층으로 말한다:
    /// ① 스냅샷 수치 축(+ 회복) · ② 정의 축(타수·버프/디버프·상태이상, 정의 쌍이 있을 때) · ③ 문안 구절 diff —
    /// 렌더링 문안의 공통 앞뒤를 걷어내고 달라진 구절만 「전 → 후」로 보인다(효과 연마의 유일한 표면).
    /// </summary>
    public static class RefineDiffSummary
    {
        public const string Separator = "   ·   ";

        public static string Build(
            CombatCardSnapshot before,
            CombatCardSnapshot after,
            CardDefinition beforeDefinition,
            CardDefinition afterDefinition,
            string improvedHex,
            string mutedHex)
        {
            var lines = new List<string>();
            void Append(string label, int oldValue, int newValue)
            {
                if (oldValue != newValue)
                {
                    lines.Add($"{label} <color=#{mutedHex}>{oldValue}</color> → <color=#{improvedHex}>{newValue}</color>");
                }
            }

            // ① 스냅샷 수치 축
            Append("코스트", before.Cost, after.Cost);
            Append("수치", before.Value, after.Value);
            // 회복은 heal 축이 접혀 Value와 같은 카드(S02·F02)가 있다 — 같은 변화를 두 줄로 찍지 않는다.
            var healFoldedIntoValue = before.HealValue == before.Value && after.HealValue == after.Value;
            if (!healFoldedIntoValue)
            {
                Append("회복", before.HealValue, after.HealValue);
            }
            Append("사거리", before.Range, after.Range);
            Append("범위", before.AreaRadius, after.AreaRadius);
            Append("지속", before.DurationTurns, after.DurationTurns);

            // ② 정의 축 — 스냅샷이 싣지 않는 것들
            if (beforeDefinition != null && afterDefinition != null)
            {
                Append("타수", Math.Max(1, beforeDefinition.HitCount), Math.Max(1, afterDefinition.HitCount));
                AppendEffectList(lines, beforeDefinition.BuffDebuff, afterDefinition.BuffDebuff, improvedHex, mutedHex);
                AppendEffectList(lines, beforeDefinition.StateEffect, afterDefinition.StateEffect, improvedHex, mutedHex);
            }

            // ③ 문안 구절 diff
            var phrase = BuildPhraseDiff(before.Description, after.Description, improvedHex, mutedHex);
            if (phrase != null)
            {
                lines.Add(phrase);
            }

            return lines.Count == 0 ? "달라지는 것이 없습니다." : string.Join(Separator, lines);
        }

        /// <summary>「Agility:2」 → 「Agility:3」 같은 효과 목록의 종류별 수치 변화. 종류 이름은 상태이상 표시 이름(민첩·반사 …)으로 푼다.</summary>
        private static void AppendEffectList(List<string> lines, string beforeList, string afterList, string improvedHex, string mutedHex)
        {
            var beforeEffects = CardBehaviorMetadata.ParseEffectList(beforeList ?? string.Empty).ToDictionary(e => e.Kind, e => e.Amount, StringComparer.OrdinalIgnoreCase);
            var afterEffects = CardBehaviorMetadata.ParseEffectList(afterList ?? string.Empty).ToDictionary(e => e.Kind, e => e.Amount, StringComparer.OrdinalIgnoreCase);
            foreach (var kind in beforeEffects.Keys.Union(afterEffects.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(k => k, StringComparer.Ordinal))
            {
                beforeEffects.TryGetValue(kind, out var oldValue);
                afterEffects.TryGetValue(kind, out var newValue);
                if (oldValue != newValue)
                {
                    lines.Add($"{DisplayName(kind)} <color=#{mutedHex}>{oldValue}</color> → <color=#{improvedHex}>{newValue}</color>");
                }
            }
        }

        private static string DisplayName(string kind)
        {
            return Enum.TryParse<StatusEffectKind>(kind, true, out var parsed) ? StatusEffectInfo.DisplayName(parsed) : kind;
        }

        /// <summary>
        /// 두 문안을 공백 단위 낱말로 보고 공통 앞·뒤 낱말을 걷어낸 가운데 구절만 「전 → 후」로. 낱말 단위라
        /// 「3명」→「2명」처럼 달라진 낱말만 남고, 문장이 통째로 빠지면 「삭제」, 덧붙으면 「+」로 말한다. 같은 문안이면 null.
        /// </summary>
        internal static string BuildPhraseDiff(string before, string after, string improvedHex, string mutedHex)
        {
            before ??= string.Empty;
            after ??= string.Empty;
            if (string.Equals(before, after, StringComparison.Ordinal))
            {
                return null;
            }

            var oldWords = before.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var newWords = after.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var prefix = 0;
            while (prefix < oldWords.Length && prefix < newWords.Length && oldWords[prefix] == newWords[prefix])
            {
                prefix++;
            }

            var suffix = 0;
            while (suffix < oldWords.Length - prefix && suffix < newWords.Length - prefix
                   && oldWords[oldWords.Length - 1 - suffix] == newWords[newWords.Length - 1 - suffix])
            {
                suffix++;
            }

            var oldPhrase = string.Join(" ", oldWords, prefix, oldWords.Length - prefix - suffix);
            var newPhrase = string.Join(" ", newWords, prefix, newWords.Length - prefix - suffix);
            if (oldPhrase.Length == 0)
            {
                return $"문안 + <color=#{improvedHex}>{newPhrase}</color>";
            }

            if (newPhrase.Length == 0)
            {
                return $"문안 <color=#{mutedHex}><s>{oldPhrase}</s></color> 삭제";
            }

            return $"문안 <color=#{mutedHex}>{oldPhrase}</color> → <color=#{improvedHex}>{newPhrase}</color>";
        }
    }
}

using System;
using System.Collections.Generic;

namespace SeoulPlayup.Codex
{
    /// <summary>
    /// 목록 격자에 무엇을 어떤 순서로 내놓을지 정하는 순수 함수. 뷰에서 떼어 둔 이유는 여기가
    /// 해금·검색·필터가 만나는 곳이고, 셋이 만나는 지점의 규칙은 <b>테스트로 못 박아야</b> 하기
    /// 때문이다.
    /// </summary>
    public static class CodexListQuery
    {
        /// <summary>
        /// 필터 칩과 검색어를 적용한 목록. 잠긴 항목은 <b>목록에는 남지만</b>(Q7 — 자리를 남긴다)
        /// <b>검색에는 걸리지 않는다</b>(이름을 모르는 것이 해금의 전부다).
        /// </summary>
        /// <param name="entries">도메인이 내놓은 전체 항목.</param>
        /// <param name="filterChip">선택된 칩. 비어 있으면 칩 필터를 적용하지 않는다.</param>
        /// <param name="search">검색어. 비어 있으면 검색을 적용하지 않는다.</param>
        /// <param name="isUnlocked">
        /// 항목이 열려 있는지 묻는다. <see langword="null"/>이면 전부 열린 것으로 본다(디버그 뷰).
        /// </param>
        public static List<CodexEntry> Apply(
            IReadOnlyList<CodexEntry> entries,
            string filterChip,
            string search,
            Func<CodexEntry, bool> isUnlocked = null)
        {
            var result = new List<CodexEntry>(entries?.Count ?? 0);
            if (entries == null)
            {
                return result;
            }

            var hasChip = !string.IsNullOrWhiteSpace(filterChip);
            var needle = string.IsNullOrWhiteSpace(search) ? null : search.Trim().ToLowerInvariant();

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null)
                {
                    continue;
                }

                if (hasChip && !string.Equals(entry.FilterChip, filterChip, StringComparison.Ordinal))
                {
                    continue;
                }

                if (needle != null)
                {
                    // 잠긴 항목이 검색으로 새어 나오면 해금이 무의미해진다.
                    if (isUnlocked != null && !isUnlocked(entry))
                    {
                        continue;
                    }

                    if (entry.SearchHaystack.IndexOf(needle, StringComparison.Ordinal) < 0)
                    {
                        continue;
                    }
                }

                result.Add(entry);
            }

            return result;
        }

        /// <summary>
        /// 이 도메인이 내놓는 필터 칩 목록. 저작 순서를 그대로 따른다 — 알파벳 정렬하면
        /// "이로움 · 해로움"처럼 뜻이 있는 순서가 흐트러진다.
        /// </summary>
        public static List<string> CollectChips(IReadOnlyList<CodexEntry> entries)
        {
            var chips = new List<string>();
            if (entries == null)
            {
                return chips;
            }

            for (var i = 0; i < entries.Count; i++)
            {
                var chip = entries[i]?.FilterChip;
                if (string.IsNullOrWhiteSpace(chip) || chips.Contains(chip))
                {
                    continue;
                }

                chips.Add(chip);
            }

            return chips;
        }

        /// <summary>
        /// 레일에 찍는 수집률. 디버그 뷰는 전량 공개이므로 호출자가 <paramref name="isUnlocked"/>에
        /// <see langword="null"/>을 넘겨 <c>(총, 총)</c>을 받는다.
        /// </summary>
        public static (int Unlocked, int Total) Tally(
            IReadOnlyList<CodexEntry> entries,
            Func<CodexEntry, bool> isUnlocked = null)
        {
            if (entries == null)
            {
                return (0, 0);
            }

            if (isUnlocked == null)
            {
                return (entries.Count, entries.Count);
            }

            var unlocked = 0;
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null && isUnlocked(entries[i]))
                {
                    unlocked++;
                }
            }

            return (unlocked, entries.Count);
        }
    }
}

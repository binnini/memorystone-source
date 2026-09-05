using System;
using System.Collections.Generic;
using System.Linq;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// How a keyword's numeric magnitude (값) is sourced for display.
    /// <see cref="None"/>: the keyword has no number (e.g. 속박/기절/소멸).
    /// <see cref="Fixed"/>: the number is authored in the CSV 값 column and is constant
    /// (중독 3, 둔화 1, 파열 2, 반사 50%, 강화 100%).
    /// <see cref="Dynamic"/>: the number is determined at runtime per instance (민첩 이동 보너스, 방어막 흡수량,
    /// 밀치기 넉백 거리) and the CSV 값 is left blank — UI must resolve the live value from the effect/card.
    /// </summary>
    public enum KeywordValueKind
    {
        None,
        Fixed,
        Dynamic,
    }

    /// <summary>
    /// One designer-authored game keyword (분류/키워드/값/값종류/효과) from game_keywords.csv. Pure runtime
    /// model (no UnityEngine) so it can power the description decorator, the hover tooltip, the status-effect
    /// HUD tooltip, and the monster info panel from a single source.
    /// </summary>
    public sealed class KeywordDefinition
    {
        public KeywordDefinition(string category, string keyword, string effect)
            : this(category, keyword, effect, KeywordValueKind.None, null)
        {
        }

        public KeywordDefinition(string category, string keyword, string effect, KeywordValueKind valueKind, string value)
        {
            Category = category ?? string.Empty;
            Keyword = keyword ?? string.Empty;
            Effect = effect ?? string.Empty;
            ValueKind = valueKind;
            Value = value ?? string.Empty;
        }

        /// <summary>분류 — e.g. 상태이상 / 버프 / 카드 효과.</summary>
        public string Category { get; }

        /// <summary>키워드 원형 — the canonical form as authored, e.g. "밀치기(넉백)".</summary>
        public string Keyword { get; }

        /// <summary>효과 — Korean explanation shown in the hover tooltip (authored verbatim, numbers inline).</summary>
        public string Effect { get; }

        /// <summary>How <see cref="Value"/> should be interpreted by display code.</summary>
        public KeywordValueKind ValueKind { get; }

        /// <summary>값 — the fixed magnitude as authored (e.g. "3", "50"); empty for None/Dynamic keywords.</summary>
        public string Value { get; }

        /// <summary>True when this keyword carries a constant authored number that UI can show directly.</summary>
        public bool HasFixedValue => ValueKind == KeywordValueKind.Fixed && !string.IsNullOrWhiteSpace(Value);

        /// <summary>Parses <see cref="Value"/> as an integer; returns <paramref name="fallback"/> when not a fixed number.</summary>
        public int FixedValueOrDefault(int fallback = 0)
            => HasFixedValue && int.TryParse(Value.Trim(), out var parsed) ? parsed : fallback;
    }

    /// <summary>A concrete text term that, when found in card text, maps back to a <see cref="KeywordDefinition"/>.</summary>
    public readonly struct KeywordMatchTerm
    {
        public KeywordMatchTerm(string term, KeywordDefinition definition)
        {
            Term = term;
            Definition = definition;
        }

        public string Term { get; }
        public KeywordDefinition Definition { get; }
    }

    /// <summary>
    /// Keyword → {분류, 효과} catalog with substring match terms. Parenthesised keywords like
    /// "밀치기(넉백)" expand into two aliases ("밀치기" and "넉백") that both resolve to the same
    /// canonical entry, because the literal "밀치기(넉백)" never appears in card/pattern text.
    /// </summary>
    public sealed class KeywordCatalogDefinition
    {
        private readonly Dictionary<string, KeywordDefinition> byKeyword;
        private readonly IReadOnlyList<KeywordMatchTerm> matchTermsLongestFirst;

        public KeywordCatalogDefinition(IEnumerable<KeywordDefinition> entries)
        {
            Entries = (entries ?? Array.Empty<KeywordDefinition>())
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.Keyword))
                .ToList();

            byKeyword = new Dictionary<string, KeywordDefinition>(StringComparer.Ordinal);
            foreach (var entry in Entries)
            {
                byKeyword[entry.Keyword] = entry;
            }

            matchTermsLongestFirst = BuildMatchTerms(Entries);
        }

        public IReadOnlyList<KeywordDefinition> Entries { get; }

        /// <summary>Match terms sorted longest-first so substring matching prefers the longer keyword.</summary>
        public IReadOnlyList<KeywordMatchTerm> MatchTermsLongestFirst => matchTermsLongestFirst;

        /// <summary>Looks up a definition by its canonical keyword (원형), e.g. "밀치기(넉백)".</summary>
        public bool TryGet(string keyword, out KeywordDefinition definition)
        {
            if (keyword == null)
            {
                definition = null;
                return false;
            }

            return byKeyword.TryGetValue(keyword, out definition);
        }

        private static IReadOnlyList<KeywordMatchTerm> BuildMatchTerms(IReadOnlyList<KeywordDefinition> entries)
        {
            var terms = new List<KeywordMatchTerm>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var entry in entries)
            {
                foreach (var alias in DeriveAliases(entry.Keyword))
                {
                    if (!string.IsNullOrWhiteSpace(alias) && seen.Add(alias))
                    {
                        terms.Add(new KeywordMatchTerm(alias, entry));
                    }
                }
            }

            // Longest-first so e.g. a longer keyword wins over a shorter one starting at the same index.
            return terms
                .OrderByDescending(term => term.Term.Length)
                .ThenBy(term => term.Term, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Derives the literal match terms for a keyword. "밀치기(넉백)" → {"밀치기", "넉백"}; everything
        /// else → {itself}. The parenthetical is treated as a synonym, not a separate keyword.
        /// </summary>
        public static IEnumerable<string> DeriveAliases(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                yield break;
            }

            var open = keyword.IndexOf('(');
            var close = keyword.IndexOf(')');
            if (open >= 0 && close > open)
            {
                var baseTerm = keyword.Substring(0, open).Trim();
                var inner = keyword.Substring(open + 1, close - open - 1).Trim();
                if (!string.IsNullOrWhiteSpace(baseTerm))
                {
                    yield return baseTerm;
                }

                if (!string.IsNullOrWhiteSpace(inner))
                {
                    yield return inner;
                }

                yield break;
            }

            yield return keyword.Trim();
        }
    }

    /// <summary>
    /// Process-wide handle for the active keyword catalog. The pure description decorator reads it through
    /// here so it can stay UnityEngine-free; the Unity layer assigns it at startup from a baked catalog.
    /// When null (e.g. EditMode tests, or before bootstrap) decoration is a no-op, so plain text is preserved.
    /// </summary>
    public static class CardKeywordCatalogProvider
    {
        public static KeywordCatalogDefinition Active { get; set; }
    }
}

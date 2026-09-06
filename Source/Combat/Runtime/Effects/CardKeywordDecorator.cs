using System.Collections.Generic;
using System.Text;
using SeoulPlayup.Combat.Runtime.Cards;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// Wraps game keywords found in card description text with TMP rich-text emphasis and a hover link:
    /// <c>&lt;link="kw:속박"&gt;&lt;i&gt;&lt;b&gt;속박&lt;/b&gt;&lt;/i&gt;&lt;/link&gt;</c>. Pure string work — no
    /// UnityEngine — so it can run inside the runtime <c>Describe</c> choke point. The link payload is the
    /// canonical keyword (원형) so the hover tooltip can resolve it even for parenthesised aliases.
    ///
    /// Matching is longest-first substring (no word boundaries, per design), tag-aware so existing markup is
    /// copied verbatim, and idempotent: re-running on already-decorated text leaves it unchanged because the
    /// scan never matches inside an existing keyword <c>&lt;link&gt;</c> region.
    ///
    /// P5(2026-09-06, 키워드 명시화): 카드 문안은 <see cref="DecorateForCard"/>로 장식한다 — 카드 클래스가
    /// <see cref="CardBehavior.Keywords"/>로 선언한 키워드만 걸리고, 선언이 없는 카드(카탈로그 밖 픽스처 포함)는 문안이 그대로다.
    /// 카탈로그 전체를 부분 문자열로 훑는 <see cref="Decorate(string, KeywordCatalogDefinition)"/>는 카드에 묶이지 않은 문안 전용이다.
    /// </summary>
    public static class CardKeywordDecorator
    {
        public const string LinkIdPrefix = "kw:";

        /// <summary>
        /// 카드 문안 장식: 카드 클래스가 선언한 <see cref="CardBehavior.Keywords"/>만 강조·링크한다(활성 카탈로그 기준).
        /// 등록되지 않은 카드 id는 선언이 없으므로 문안이 그대로 돌아간다.
        /// </summary>
        public static string DecorateForCard(string text, string cardId)
        {
            return CardBehaviorRegistry.TryGet(cardId, out var behavior)
                ? Decorate(text, CardKeywordCatalogProvider.Active, behavior.Keywords)
                : text;
        }

        /// <summary>선언된 키워드(원형)만 걸리는 장식. <paramref name="keywords"/>가 비면 문안이 그대로다.</summary>
        public static string Decorate(string text, KeywordCatalogDefinition catalog, IReadOnlyCollection<string> keywords)
        {
            if (string.IsNullOrEmpty(text) || catalog == null || keywords == null || keywords.Count == 0)
            {
                return text;
            }

            var declared = new HashSet<string>(keywords, System.StringComparer.Ordinal);
            var terms = new List<KeywordMatchTerm>();
            foreach (var term in catalog.MatchTermsLongestFirst)
            {
                if (declared.Contains(term.Definition.Keyword))
                {
                    terms.Add(term);
                }
            }

            return Decorate(text, terms);
        }

        /// <summary>
        /// Decorates <paramref name="text"/> against <paramref name="catalog"/>. Returns the input unchanged
        /// when the catalog is null/empty or the text has no keywords.
        /// </summary>
        public static string Decorate(string text, KeywordCatalogDefinition catalog)
        {
            if (string.IsNullOrEmpty(text) || catalog == null)
            {
                return text;
            }

            return Decorate(text, catalog.MatchTermsLongestFirst);
        }

        private static string Decorate(string text, IReadOnlyList<KeywordMatchTerm> terms)
        {
            if (string.IsNullOrEmpty(text) || terms == null || terms.Count == 0)
            {
                return text;
            }

            var builder = new StringBuilder(text.Length + 32);
            var insideKeywordLink = false;
            var i = 0;
            while (i < text.Length)
            {
                var c = text[i];

                // Copy any rich-text tag verbatim and track whether we are inside a keyword <link> region so
                // we never re-decorate already-wrapped keywords (idempotency).
                if (c == '<')
                {
                    var close = text.IndexOf('>', i);
                    if (close < 0)
                    {
                        builder.Append(text, i, text.Length - i);
                        break;
                    }

                    var tagLength = close - i + 1;
                    var tag = text.Substring(i, tagLength);
                    if (IsKeywordLinkOpen(tag))
                    {
                        insideKeywordLink = true;
                    }
                    else if (IsLinkClose(tag))
                    {
                        insideKeywordLink = false;
                    }

                    builder.Append(tag);
                    i += tagLength;
                    continue;
                }

                if (!insideKeywordLink && TryMatchTerm(text, i, terms, out var matched, out var canonical))
                {
                    builder.Append("<link=\"").Append(LinkIdPrefix).Append(canonical).Append("\"><i><b>")
                        .Append(matched)
                        .Append("</b></i></link>");
                    i += matched.Length;
                    continue;
                }

                builder.Append(c);
                i++;
            }

            return builder.ToString();
        }

        private static bool TryMatchTerm(
            string text,
            int index,
            System.Collections.Generic.IReadOnlyList<KeywordMatchTerm> termsLongestFirst,
            out string matchedTerm,
            out string canonicalKeyword)
        {
            for (var t = 0; t < termsLongestFirst.Count; t++)
            {
                var term = termsLongestFirst[t].Term;
                if (term.Length > 0 &&
                    index + term.Length <= text.Length &&
                    string.CompareOrdinal(text, index, term, 0, term.Length) == 0)
                {
                    matchedTerm = term;
                    canonicalKeyword = termsLongestFirst[t].Definition.Keyword;
                    return true;
                }
            }

            matchedTerm = null;
            canonicalKeyword = null;
            return false;
        }

        private static bool IsKeywordLinkOpen(string tag)
        {
            // Matches <link="kw:...> produced by this decorator (and only ours, via the kw: prefix).
            return tag.StartsWith("<link", System.StringComparison.OrdinalIgnoreCase) &&
                   tag.IndexOf(LinkIdPrefix, System.StringComparison.Ordinal) >= 0;
        }

        private static bool IsLinkClose(string tag)
        {
            return tag.StartsWith("</link", System.StringComparison.OrdinalIgnoreCase);
        }
    }
}

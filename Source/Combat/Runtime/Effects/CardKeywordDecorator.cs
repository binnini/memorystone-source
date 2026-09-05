using System.Text;

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
    /// </summary>
    public static class CardKeywordDecorator
    {
        public const string LinkIdPrefix = "kw:";

        /// <summary>Decorates using the process-wide active catalog. No-op when none is set (plain text in/out).</summary>
        public static string Decorate(string text) => Decorate(text, CardKeywordCatalogProvider.Active);

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

            var terms = catalog.MatchTermsLongestFirst;
            if (terms.Count == 0)
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

using System;
using System.Text.RegularExpressions;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// Korean particle agreement for text assembled by token substitution.
    /// <para>
    /// Card copy is authored as templates like "{Shape}를 탐색합니다." and "피해 {Damage}를 줍니다.",
    /// so the author has to commit to a particle before knowing what the token expands to. That is
    /// wrong whenever the substituted value flips the 받침: "{Shape}" resolves to "범위 1", giving
    /// "범위 1를", and "피해 {Damage}" with damage 1 gives "피해 1를". The particle has to be chosen
    /// from the resolved value, which is what <see cref="Agree"/> and <see cref="ResolveTokens"/> do.
    /// </para>
    /// </summary>
    public static class KoreanParticle
    {
        // '으로'/'로' must come first so the two-character form wins over the one-character one.
        // 한글 토큰({값}·{지속} — game_keywords.csv 효과문, WS-I)도 카드 템플릿의 영문 토큰과 같은
        // 문법으로 치환된다. 미지 토큰은 그대로 남으므로 본문 속 우연한 {한글} 중괄호도 안전하다.
        private static readonly Regex TokenPattern = new Regex(
            @"\{(?<token>[A-Za-z가-힣]+)\}(?<particle>으로|을|를|이|가|은|는|와|과|로)?",
            RegexOptions.CultureInvariant);

        private const char HangulBase = '가';
        private const char HangulLast = '힣';
        private const int JongseongCount = 28;
        private const int RieulJongseong = 8;

        /// <summary>
        /// Substitutes {Token} occurrences using <paramref name="lookup"/> and fixes any particle
        /// that immediately follows. Unknown tokens are left untouched so a typo stays visible
        /// instead of silently deleting text.
        /// </summary>
        public static string ResolveTokens(string template, Func<string, string> lookup)
        {
            if (string.IsNullOrEmpty(template) || lookup == null)
            {
                return template;
            }

            return TokenPattern.Replace(template, match =>
            {
                var value = lookup(match.Groups["token"].Value);
                if (value == null)
                {
                    return match.Value;
                }

                var particle = match.Groups["particle"];
                if (!particle.Success)
                {
                    return value;
                }

                // Only treat the trailing syllable as a particle when it actually ends the word.
                // Otherwise "{Damage}이상" would have its "이" rewritten into a subject marker.
                var after = match.Index + match.Length;
                if (after < template.Length && !IsWordBoundary(template[after]))
                {
                    return value + particle.Value;
                }

                return value + Agree(value, particle.Value);
            });
        }

        /// <summary>
        /// Returns the form of <paramref name="particle"/> that agrees with <paramref name="value"/>.
        /// Unrecognised particles are returned unchanged.
        /// </summary>
        public static string Agree(string value, string particle)
        {
            var hasFinal = EndsWithFinalConsonant(value);
            switch (particle)
            {
                case "을":
                case "를":
                    return hasFinal ? "을" : "를";
                case "이":
                case "가":
                    return hasFinal ? "이" : "가";
                case "은":
                case "는":
                    return hasFinal ? "은" : "는";
                case "와":
                case "과":
                    return hasFinal ? "과" : "와";
                case "으로":
                case "로":
                    // 'ㄹ' behaves like an open syllable for this one.
                    return !hasFinal || EndsWithRieul(value) ? "로" : "으로";
                default:
                    return particle;
            }
        }

        /// <summary>True when the last meaningful character closes with a 받침.</summary>
        public static bool EndsWithFinalConsonant(string value)
        {
            var c = LastMeaningfulChar(value);
            if (c == '\0')
            {
                return false;
            }

            if (c >= '0' && c <= '9')
            {
                // Agreement follows how the digit is read aloud, and for multi-digit numbers the
                // final syllable is decided by the final digit (10 -> 십, 100 -> 백 both close too).
                switch (c)
                {
                    case '0': // 영
                    case '1': // 일
                    case '3': // 삼
                    case '6': // 육
                    case '7': // 칠
                    case '8': // 팔
                        return true;
                    default: // 2 이, 4 사, 5 오, 9 구
                        return false;
                }
            }

            if (c >= HangulBase && c <= HangulLast)
            {
                return (c - HangulBase) % JongseongCount != 0;
            }

            return false;
        }

        /// <summary>True when the last meaningful character closes with 'ㄹ' specifically.</summary>
        public static bool EndsWithRieul(string value)
        {
            var c = LastMeaningfulChar(value);
            if (c == '\0')
            {
                return false;
            }

            if (c == '1' || c == '7' || c == '8')
            {
                return true; // 일, 칠, 팔
            }

            if (c >= HangulBase && c <= HangulLast)
            {
                return (c - HangulBase) % JongseongCount == RieulJongseong;
            }

            return false;
        }

        private static char LastMeaningfulChar(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return '\0';
            }

            for (var i = value.Length - 1; i >= 0; i--)
            {
                var c = value[i];

                // TMP 리치 텍스트 태그는 소리 나는 글자가 아니다. 실효 수치 강조가 값을
                // "<color=#6FE07A>6</color>"으로 감싸므로, 태그를 건너뛰지 않으면 조사가 '>'를 보고
                // 받침 없음으로 판정해 "6를"을 찍는다(6은 '육'이라 "6을"이 맞다).
                if (c == '>')
                {
                    var open = value.LastIndexOf('<', i);
                    if (open >= 0)
                    {
                        i = open; // 루프의 i-- 와 합쳐져 여는 꺾쇠 앞으로 넘어간다
                        continue;
                    }

                    return c; // 짝이 없는 '>'는 그냥 글자로 본다
                }

                if (!char.IsWhiteSpace(c) && c != ')' && c != ']' && c != '"' && c != '\'')
                {
                    return c;
                }
            }

            return '\0';
        }

        private static bool IsWordBoundary(char c)
        {
            if (char.IsWhiteSpace(c))
            {
                return true;
            }

            switch (c)
            {
                case '.':
                case ',':
                case '!':
                case '?':
                case ';':
                case ':':
                case ')':
                case ']':
                case '}':
                case '"':
                case '\'':
                case '<':
                    return true;
                default:
                    return false;
            }
        }
    }
}

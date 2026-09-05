using System;
using System.Collections.Generic;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 저주 카드 <b>풀</b> 저작(요괴 5종 트랙 §3-3)의 파서와 추첨. 단일 저작
    /// (<c>injectStatusCardId</c>)이 "항상 이 카드"라면 풀은 "이 중 하나"다 — 도깨비 장난(A035)이
    /// 첫 사용자이고, 「심술」 뒤끝(<c>aftermath.curse</c>)이 같은 추첨을 공유한다.
    ///
    /// <para>🔴 추첨은 <b>순수 함수</b>다. 이 프로젝트의 전투 RNG(<c>pushRng</c>)는 무시드이고
    /// 세이브는 full-snapshot이라(DEC-2026-07-18-02), 주변 RNG를 쓰면 같은 상황에서 무엇이 뽑혔는지
    /// 설명할 수도 재현할 수도 없다. 대신 그 순간 세이브가 이미 복원하는 값들(몬스터 id · 패턴 id ·
    /// 전체 턴 번호)을 FNV-1a로 섞어 시드를 만든다 — 같은 상황이면 같은 카드가 나오고, 턴이 흐르면
    /// 갈린다. 해시는 <c>string.GetHashCode</c>가 아니라 FNV-1a여야 한다(런타임 간 안정성).</para>
    /// </summary>
    internal static class MonsterCurseCardPool
    {
        /// <summary>풀 저작의 구분자. CSV 셀 하나 안이라 쉼표를 쓸 수 없다(컬럼 밀림 차단 규약).</summary>
        public const char Separator = ';';

        /// <summary>
        /// <c>X05;X07;X11</c> 형태를 카드 id 배열로 판다. 빈 값은 "풀 없음"이라 성공하고 빈 배열을 준다 —
        /// 저작하지 않은 패턴이 대다수이기 때문이다.
        /// </summary>
        public static bool TryParse(string raw, out string[] pool, out string error)
        {
            pool = Array.Empty<string>();
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return true;
            }

            var parsed = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var token in raw.Split(Separator))
            {
                var id = token.Trim();
                if (id.Length == 0)
                {
                    error = $"curse pool '{raw}' has an empty entry — '{Separator}'로 끝나거나 비어 있는 칸이 있다.";
                    return false;
                }

                if (!seen.Add(id))
                {
                    error = $"curse pool '{raw}' has duplicate entry '{id}' — 중복은 가중치가 아니라 저작 실수로 취급한다.";
                    return false;
                }

                parsed.Add(id);
            }

            pool = parsed.ToArray();
            return true;
        }

        /// <summary>풀에서 한 장. 빈 풀이면 빈 문자열(호출부가 "저작 없음"으로 다룬다).</summary>
        public static string Pick(IReadOnlyList<string> pool, int seed)
        {
            if (pool == null || pool.Count == 0)
            {
                return string.Empty;
            }

            return pool[(int)((uint)seed % (uint)pool.Count)];
        }

        /// <summary>
        /// 풀에서 서로 다른 <paramref name="count"/>장. 풀보다 많이 요구하면 풀 전체를 준다 —
        /// 같은 카드를 두 번 넣어 수를 채우지 않는다(저작 의도는 "종류가 섞인다"이기 때문).
        /// </summary>
        public static IReadOnlyList<string> PickDistinct(IReadOnlyList<string> pool, int count, int seed)
        {
            if (pool == null || pool.Count == 0 || count <= 0)
            {
                return Array.Empty<string>();
            }

            var shuffled = new List<string>(pool);
            // 시드에서 뽑은 LCG 한 줄로 Fisher-Yates. System.Random을 쓰지 않는 이유는 구현이
            // 런타임 버전에 묶여 있어 "같은 시드면 같은 결과"가 플랫폼 간 계약이 되지 못하기 때문이다.
            var state = (uint)seed | 1u;
            for (var i = shuffled.Count - 1; i > 0; i--)
            {
                state = unchecked(state * 1664525u + 1013904223u);
                var j = (int)(state % (uint)(i + 1));
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }

            return shuffled.GetRange(0, Math.Min(count, shuffled.Count));
        }

        /// <summary>
        /// 추첨 시드. 세이브가 복원하는 값만 섞는다 — 전투 중 저장·복원 뒤에도 같은 카드가 나온다.
        /// (<c>CombatState.MixSpawnHpSeed</c>와 같은 FNV-1a 규약.)
        /// </summary>
        public static int MixSeed(string text, int salt) => CombatDeterministicSeed.Mix(text, salt);
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>파싱·검증이 끝난 홀림(오라 봉인) 저작 한 벌.</summary>
    internal readonly struct MonsterAuraSealSpec
    {
        public MonsterAuraSealSpec(int radius, int cards)
        {
            Radius = radius;
            Cards = cards;
        }

        /// <summary>오라 반경 — 이 거리 안에 그슨새가 있는 동안 봉인이 유지된다.</summary>
        public int Radius { get; }

        /// <summary>봉인 장수(Seal Amount). 봉인 대상 선정은 기존 Seal 규약(결정적 순위)이 맡는다.</summary>
        public int Cards { get; }
    }

    /// <summary>
    /// 홀림(그슨새 · 2026-09-04 리워크 §2-C)의 저작 표면 파서 — <c>hiddenTraitRef</c>/<c>hiddenTraitParam</c>
    /// 「특성 1슬롯」에 은신(<see cref="MonsterHiddenTrait"/>)과 나란히 실린다(ref가 갈래를 정한다).
    ///
    /// <para>규칙: 그슨새가 플레이어와 <b>거리 radius 이내에 있는 동안</b> 무작위 카드 cards장이
    /// 봉인(Seal)으로 잠기고, 벗어나면 풀린다 — 오라형. 어느 카드가 잠기는가는 기존 Seal의
    /// 결정적 순위(<c>StableSealHash</c>)가 정하므로 무시드 RNG가 없고, 여러 마리·사자탈 A026과
    /// 겹쳐도 Amount 합산으로 서로 다른 카드가 잠긴다(이중 봉인 없음).</para>
    /// </summary>
    internal static class MonsterAuraSeal
    {
        public const string AuraSealRef = "aura.seal";

        public const int DefaultRadius = 2;
        public const int DefaultCards = 1;

        public static bool IsAuraSeal(string traitRef)
        {
            return string.Equals((traitRef ?? string.Empty).Trim(), AuraSealRef, StringComparison.Ordinal);
        }

        public static bool TryParse(string traitRef, string param, out MonsterAuraSealSpec spec, out string error)
        {
            spec = default;
            error = string.Empty;
            if (!IsAuraSeal(traitRef))
            {
                error = $"hiddenTraitRef '{traitRef}' is not '{AuraSealRef}'.";
                return false;
            }

            if (!TryParseParams(param, out var values, out error))
            {
                return false;
            }

            if (!TryGetIntOrDefault(values, "radius", DefaultRadius, out var radius, out error)) return false;
            if (!TryGetIntOrDefault(values, "cards", DefaultCards, out var cards, out error)) return false;
            if (radius <= 0 || cards <= 0)
            {
                error = "hiddenTraitParam 'radius'/'cards' must be positive — 0이면 아무것도 봉인하지 않는 죽은 저작이다.";
                return false;
            }

            spec = new MonsterAuraSealSpec(radius, cards);
            return true;
        }

        private static bool TryParseParams(string param, out Dictionary<string, string> values, out string error)
        {
            values = new Dictionary<string, string>(StringComparer.Ordinal);
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(param))
            {
                return true;
            }

            foreach (var token in param.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                var split = token.Split('=');
                if (split.Length != 2 || string.IsNullOrWhiteSpace(split[0]))
                {
                    error = $"hiddenTraitParam token '{token}' is not key=value.";
                    return false;
                }

                values[split[0].Trim()] = split[1].Trim();
            }

            return true;
        }

        private static bool TryGetIntOrDefault(
            Dictionary<string, string> values, string key, int fallback, out int result, out string error)
        {
            error = string.Empty;
            result = fallback;
            if (!values.TryGetValue(key, out var text) || string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
            {
                error = $"hiddenTraitParam '{key}' must be an integer.";
                return false;
            }

            return true;
        }
    }
}

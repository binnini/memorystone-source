using System;
using System.Collections.Generic;
using System.Globalization;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>파싱·검증이 끝난 은신 특성 저작 한 벌.</summary>
    internal readonly struct MonsterHiddenTraitSpec
    {
        public MonsterHiddenTraitSpec(int revealTurns, int growthPerTurn, int growthMax)
        {
            RevealTurns = revealTurns;
            GrowthPerTurn = growthPerTurn;
            GrowthMax = growthMax;
        }

        /// <summary>드러난 뒤 다시 숨을 때까지의 턴 수(2026-09-01 사용자 확정 = 4 — 종전 2).</summary>
        public int RevealTurns { get; }

        /// <summary>「어둠 먹기」 — 드러나지 않은 턴마다 오르는 공격 피해. 0이면 성장 없음.</summary>
        public int GrowthPerTurn { get; }

        /// <summary>성장 상한(피해 보너스의 최대값).</summary>
        public int GrowthMax { get; }

        public bool HasGrowth => GrowthPerTurn > 0 && GrowthMax > 0;
    }

    /// <summary>
    /// 은신 특성(요괴 트랙 §4-1)의 저작 표면 파서. monster_catalog.csv의
    /// <c>hiddenTraitRef</c>/<c>hiddenTraitParam</c>을 읽으며, 형식은 뒤끝
    /// (<see cref="MonsterDeathAftermath"/>)과 <b>같은 규약</b>이다 — 등록된 ref + <c>key=value;…</c> blob을
    /// 임포트 검증과 런타임이 같은 TryParse로 지난다. 새 문법을 만들지 않는 것이 요점이다.
    ///
    /// <para>🔴 은신이 더하는 것은 <b>「시야 안인데도 안 보임」</b> 하나뿐이다. 시야 밖 몬스터는 지금도
    /// 안 보이므로(<c>GetVisibility(coord) == Revealed</c> 판정), 두 조건을 같은 술어로 합치면 노출 규칙이
    /// 안개 규칙을 덮어쓴다.</para>
    /// </summary>
    internal static class MonsterHiddenTrait
    {
        public const string StealthRef = "hidden.stealth";

        /// <summary>노출 지속 기본값(2026-09-01 사용자 확정 — 공격 해소·정찰 명중 모두 4턴, 종전 2턴).</summary>
        public const int DefaultRevealTurns = 4;

        public static bool TryParse(string traitRef, string param, out MonsterHiddenTraitSpec spec, out string error)
        {
            spec = default;
            error = string.Empty;
            var normalizedRef = (traitRef ?? string.Empty).Trim();
            if (normalizedRef.Length == 0)
            {
                if (!string.IsNullOrWhiteSpace(param))
                {
                    error = "hiddenTraitParam is authored without a hiddenTraitRef.";
                    return false;
                }

                return true;
            }

            if (normalizedRef != StealthRef)
            {
                error = $"hiddenTraitRef '{normalizedRef}' is not registered (known: {StealthRef}).";
                return false;
            }

            if (!TryParseParams(param, out var values, out error))
            {
                return false;
            }

            if (!TryGetIntOrDefault(values, "revealTurns", DefaultRevealTurns, out var revealTurns, out error)) return false;
            if (revealTurns <= 0)
            {
                error = "hiddenTraitParam 'revealTurns' must be positive — 0이면 드러나지 않는 은신이라 잡을 수 없다.";
                return false;
            }

            if (!TryGetIntOrDefault(values, "growthPerTurn", 0, out var growthPerTurn, out error)) return false;
            if (!TryGetIntOrDefault(values, "growthMax", 0, out var growthMax, out error)) return false;
            if (growthPerTurn < 0 || growthMax < 0)
            {
                error = "hiddenTraitParam growth values cannot be negative.";
                return false;
            }

            if ((growthPerTurn > 0) != (growthMax > 0))
            {
                error = "hiddenTraitParam 'growthPerTurn'과 'growthMax'는 함께 저작한다 — 상한 없는 성장은 금지다.";
                return false;
            }

            // scatterPattern(구 A039 그림자로 흩어지기)은 2026-09-04 리워크로 삭제됐다 — 남은 저작이
            // 있어도 미지의 key=value로 조용히 무시된다(옵셔널 TryGetValue 규약 그대로).
            spec = new MonsterHiddenTraitSpec(revealTurns, growthPerTurn, growthMax);
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

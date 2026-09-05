using System;
using System.Collections.Generic;
using System.Globalization;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>파싱·검증이 끝난 소환 저작 한 벌(요괴 트랙 §3-3 · §4-4 · 구미호 A047).</summary>
    internal readonly struct MonsterSummonSpecData
    {
        public MonsterSummonSpecData(string monsterDefinitionId, int count, int maxAlive)
        {
            MonsterDefinitionId = monsterDefinitionId ?? string.Empty;
            Count = count;
            MaxAlive = maxAlive;
        }

        public string MonsterDefinitionId { get; }

        /// <summary>한 번에 부르는 마리 수. 자리는 형상(<c>summon-2</c>)의 칸이 정한다.</summary>
        public int Count { get; }

        /// <summary>
        /// 이 소환자가 동시에 살려 둘 수 있는 최대 마리 수. 없으면 무한 소환이 되어
        /// "부르는 쪽을 먼저 처리한다"는 판단 자체가 사라진다(§4-4).
        /// </summary>
        public int MaxAlive { get; }

        public bool HasSummon => Count > 0 && !string.IsNullOrEmpty(MonsterDefinitionId);
    }

    /// <summary>
    /// 소환 저작 표면 파서. monster_attack_patterns.csv의 <c>summonSpec</c> 컬럼이며 형식은
    /// <c>summon:&lt;monsterId&gt;;&lt;마리 수&gt;[;&lt;동시 상한&gt;]</c>이다(상한 생략 시 마리 수와 같다).
    /// 지대(<see cref="MonsterStatusZone"/>)·뒤끝과 같은 규약이라 임포트와 집행이 같은 파서를 지난다.
    /// </summary>
    internal static class MonsterSummonSpec
    {
        private const string SummonPrefix = "summon:";

        public static bool TryParse(string raw, out MonsterSummonSpecData spec, out string error)
        {
            spec = default;
            error = string.Empty;
            var text = (raw ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                return true;
            }

            if (!text.StartsWith(SummonPrefix, StringComparison.Ordinal))
            {
                error = $"summonSpec '{text}' must start with '{SummonPrefix}' (예: summon:M007;2).";
                return false;
            }

            var parts = text.Substring(SummonPrefix.Length).Split(';');
            if (parts.Length < 2 || parts.Length > 3 || string.IsNullOrWhiteSpace(parts[0]))
            {
                error = $"summonSpec '{text}' must be 'summon:<monsterId>;<마리 수>[;<동시 상한>]'.";
                return false;
            }

            if (!TryParsePositive(parts[1], "count", out var count, out error))
            {
                return false;
            }

            var maxAlive = count;
            if (parts.Length == 3 && !TryParsePositive(parts[2], "maxAlive", out maxAlive, out error))
            {
                return false;
            }

            if (maxAlive < count)
            {
                error = $"summonSpec '{text}': 동시 상한({maxAlive})이 한 번에 부르는 수({count})보다 작다"
                        + " — 부르자마자 상한을 넘는 저작이다.";
                return false;
            }

            spec = new MonsterSummonSpecData(parts[0].Trim(), count, maxAlive);
            return true;
        }

        private static bool TryParsePositive(string text, string key, out int value, out string error)
        {
            error = string.Empty;
            if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value <= 0)
            {
                error = $"summonSpec '{key}' must be a positive integer.";
                return false;
            }

            return true;
        }
    }
}

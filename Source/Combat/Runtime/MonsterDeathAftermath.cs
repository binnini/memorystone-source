using System;
using System.Globalization;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>뒤끝(T7-2)이 죽은 자리에 남기는 효과의 종류.</summary>
    internal enum MonsterDeathAftermathKind
    {
        /// <summary>독기 장판 — 죽은 칸에 N턴 지속 FieldDamage 장판을 남긴다.</summary>
        Field,

        /// <summary>즉시 폭발 — 죽는 순간 반경 내 플레이어에게 1회 피해(철조각 흡수 폭발 계약).</summary>
        Blast,

        /// <summary>막타 디버프 — 처치한 플레이어에게 상태이상을 남긴다.</summary>
        Debuff,

        /// <summary>심술 — 쓰러지면서 저주 카드 N장을 덱에 섞는다(요괴 트랙 §4-5 · 두두리).</summary>
        Curse,

        /// <summary>
        /// 반환 — 쓰러지면서 플레이어의 특정 상태이상 하나를 <b>걷어 간다</b>(야광귀: 묶어 둔 발을 풀어 준다).
        /// 뒤끝의 유일한 이로운 갈래이고, 그래서 「쫓아가 잡을 이유」가 규칙으로 성립한다.
        /// </summary>
        Restore,
    }

    /// <summary>파싱·검증이 끝난 뒤끝 저작 한 벌. 컨버터와 집행이 같은 파서를 지나므로 갈라질 수 없다.</summary>
    internal readonly struct MonsterDeathAftermathSpec
    {
        public MonsterDeathAftermathSpec(
            MonsterDeathAftermathKind kind,
            int damage,
            int radius,
            int durationTurns,
            StatusEffectKind statusKind,
            int statusTurns,
            string[] cursePool = null,
            int curseCount = 0,
            bool restoresMoney = false)
        {
            Kind = kind;
            Damage = damage;
            Radius = radius;
            DurationTurns = durationTurns;
            StatusKind = statusKind;
            StatusTurns = statusTurns;
            CursePool = cursePool ?? System.Array.Empty<string>();
            CurseCount = curseCount;
            RestoresMoney = restoresMoney;
        }

        public MonsterDeathAftermathKind Kind { get; }
        public int Damage { get; }

        /// <summary>
        /// 갈래마다 뜻이 다르다: Field는 장판 반경, Blast는 피해 반경, <b>Curse는 부여 사거리</b>다
        /// (2026-09-01 #3). Curse에서만 <b>음수 = 제한 없음</b>이고 나머지는 0 이상으로 정규화된다.
        /// </summary>
        public int Radius { get; }
        public int DurationTurns { get; }
        public StatusEffectKind StatusKind { get; }
        public int StatusTurns { get; }

        /// <summary>심술이 뽑는 저주 카드 풀. 값 안의 목록이라 <c>|</c>로 나눈다(boss mechanicParams
        /// <c>volleyByPhase=5|6|7</c> 선례 — <c>;</c>는 blob의 key 구분자라 못 쓴다).</summary>
        public string[] CursePool { get; }

        /// <summary>심술이 남기는 장수. 풀보다 크면 풀 전체(같은 카드를 겹쳐 채우지 않는다).</summary>
        public int CurseCount { get; }

        /// <summary>
        /// 반환 갈래가 <b>엽전</b>을 돌려주는가(소매치기 · 2026-09-04 §2-D — <c>kind=Money</c>).
        /// 얼마를 돌려주는가는 저작이 아니라 개체가 실제로 훔친 액수(<c>MonsterRuntime.StolenMoney</c>)다.
        /// </summary>
        public bool RestoresMoney { get; }
    }

    /// <summary>
    /// 뒤끝(T7-2)의 저작 표면 파서. monster_catalog.csv의 <c>onDeathEffectRef</c>/<c>onDeathEffectParam</c>
    /// (param은 boss_profiles.csv mechanicParams와 같은 <c>key=value;key=value</c> blob)을 해석한다.
    /// 미등록 ref·깨진 param은 <b>임포트 시점에 거부</b>된다(RelicTriggerRegistry 선례) — 런타임 집행은
    /// 같은 TryParse를 다시 지나므로 검증과 해석이 한 몸이다.
    /// </summary>
    internal static class MonsterDeathAftermath
    {
        public const string FieldRef = "aftermath.field";
        public const string BlastRef = "aftermath.blast";
        public const string DebuffRef = "aftermath.debuff";
        public const string CurseRef = "aftermath.curse";

        /// <summary>반환(야광귀) — <c>kind=</c> 하나만 받는다. 상태이상이면 걷어 가고
        /// <c>kind=Money</c>(2026-09-04 소매치기)면 훔친 엽전 전액을 돌려준다.</summary>
        public const string RestoreRef = "aftermath.restore";

        /// <summary>반환 갈래의 엽전 토큰 — StatusEffectKind가 아니라 재화 축이다.</summary>
        public const string MoneyRestoreKind = "Money";

        /// <summary>풀 목록 구분자. blob이 <c>;</c>를 key 구분에 이미 쓰므로 값 안에서는 <c>|</c>다.</summary>
        private const char PoolSeparator = '|';

        public static bool TryParse(string effectRef, string param, out MonsterDeathAftermathSpec spec, out string error)
        {
            spec = default;
            error = string.Empty;
            var normalizedRef = (effectRef ?? string.Empty).Trim();
            if (normalizedRef.Length == 0)
            {
                error = "onDeathEffectRef is empty.";
                return false;
            }

            if (!TryParseParams(param, out var values, out error))
            {
                return false;
            }

            switch (normalizedRef)
            {
                case FieldRef:
                {
                    if (!TryGetPositiveInt(values, "damage", out var damage, out error)) return false;
                    if (!TryGetIntOrDefault(values, "radius", 1, out var radius, out error)) return false;
                    if (!TryGetPositiveInt(values, "turns", out var turns, out error)) return false;
                    spec = new MonsterDeathAftermathSpec(
                        MonsterDeathAftermathKind.Field, damage, Math.Max(0, radius), turns, default, 0);
                    return true;
                }

                case BlastRef:
                {
                    if (!TryGetPositiveInt(values, "damage", out var damage, out error)) return false;
                    if (!TryGetIntOrDefault(values, "radius", 1, out var radius, out error)) return false;
                    spec = new MonsterDeathAftermathSpec(
                        MonsterDeathAftermathKind.Blast, damage, Math.Max(0, radius), 0, default, 0);
                    return true;
                }

                case DebuffRef:
                {
                    if (!values.TryGetValue("kind", out var kindText) || string.IsNullOrWhiteSpace(kindText))
                    {
                        error = $"onDeathEffectParam for '{DebuffRef}' requires 'kind='.";
                        return false;
                    }

                    // ignoreCase:false — 저작이 enum 원형(Poison 등)과 정확히 일치해야 한다(ParseEnum 규약).
                    if (!Enum.TryParse<StatusEffectKind>(kindText.Trim(), ignoreCase: false, out var statusKind))
                    {
                        error = $"onDeathEffectParam kind '{kindText}' is not a StatusEffectKind.";
                        return false;
                    }

                    if (!TryGetIntOrDefault(values, "turns", 2, out var statusTurns, out error)) return false;
                    if (statusTurns <= 0)
                    {
                        error = "onDeathEffectParam 'turns' must be positive.";
                        return false;
                    }

                    // 반경은 <b>부여 사거리</b>다(2026-09-04) — 저주와 같은 규약이고 음수 = 제한 없음이라
                    // 종전 저작(반경 없음)은 그대로 「죽으면 무조건」으로 남는다. 반경을 적는 순간
                    // 「붙어서 잡으면 대가, 떨어져서 잡으면 면한다」가 되고 호버 오버레이가 그 칸을 그린다.
                    if (!TryGetIntOrDefault(values, "radius", -1, out var debuffRadius, out error)) return false;
                    spec = new MonsterDeathAftermathSpec(
                        MonsterDeathAftermathKind.Debuff, 0, debuffRadius, 0, statusKind, statusTurns);
                    return true;
                }

                case RestoreRef:
                {
                    if (!values.TryGetValue("kind", out var restoreKindText) || string.IsNullOrWhiteSpace(restoreKindText))
                    {
                        error = $"onDeathEffectParam for '{RestoreRef}' requires 'kind='.";
                        return false;
                    }

                    // 소매치기(2026-09-04 §2-D): kind=Money는 상태이상이 아니라 <b>훔친 엽전 전액</b> 반환이다.
                    // 액수는 저작이 아니라 개체 누적(StolenMoney)이므로 param에 실을 것이 kind 하나뿐이다.
                    if (string.Equals(restoreKindText.Trim(), MoneyRestoreKind, StringComparison.Ordinal))
                    {
                        spec = new MonsterDeathAftermathSpec(
                            MonsterDeathAftermathKind.Restore, 0, 0, 0, default, 0, restoresMoney: true);
                        return true;
                    }

                    if (!Enum.TryParse<StatusEffectKind>(restoreKindText.Trim(), ignoreCase: false, out var restoreKind))
                    {
                        error = $"onDeathEffectParam kind '{restoreKindText}' is not a StatusEffectKind or '{MoneyRestoreKind}'.";
                        return false;
                    }

                    spec = new MonsterDeathAftermathSpec(
                        MonsterDeathAftermathKind.Restore, 0, 0, 0, restoreKind, 0);
                    return true;
                }

                case CurseRef:
                {
                    if (!values.TryGetValue("pool", out var poolText) || string.IsNullOrWhiteSpace(poolText))
                    {
                        error = $"onDeathEffectParam for '{CurseRef}' requires 'pool=' (예: pool=X02|X09|X12).";
                        return false;
                    }

                    var pool = new System.Collections.Generic.List<string>();
                    var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                    foreach (var token in poolText.Split(PoolSeparator))
                    {
                        var cardId = token.Trim();
                        if (cardId.Length == 0)
                        {
                            error = $"onDeathEffectParam pool '{poolText}' has an empty entry.";
                            return false;
                        }

                        if (!seen.Add(cardId))
                        {
                            error = $"onDeathEffectParam pool '{poolText}' has duplicate entry '{cardId}'.";
                            return false;
                        }

                        pool.Add(cardId);
                    }

                    // 사거리(2026-09-01 #3): 「인접 1칸 안에서만 부여」(사용자 확정). 저주는 <b>영구</b>
                    // 오염이라 무조건 부여는 사기였다 — 붙어서 잡으면 대가를 치르고, 떨어져서 잡으면
                    // 면한다. 음수 = 제한 없음(종전 동작) 이므로 기존 저작은 그대로 산다.
                    if (!TryGetIntOrDefault(values, "radius", -1, out var curseRadius, out error)) return false;

                    if (!TryGetIntOrDefault(values, "count", 1, out var curseCount, out error)) return false;
                    if (curseCount <= 0)
                    {
                        error = "onDeathEffectParam 'count' must be positive.";
                        return false;
                    }

                    if (curseCount > pool.Count)
                    {
                        error = $"onDeathEffectParam count {curseCount} exceeds pool size {pool.Count}"
                                + " — 같은 저주를 겹쳐 채우지 않으므로 풀을 늘리거나 수를 줄인다.";
                        return false;
                    }

                    spec = new MonsterDeathAftermathSpec(
                        MonsterDeathAftermathKind.Curse, 0, curseRadius, 0, default, 0, pool.ToArray(), curseCount);
                    return true;
                }

                default:
                    error = $"onDeathEffectRef '{normalizedRef}' is not registered "
                            + $"(known: {FieldRef}, {BlastRef}, {DebuffRef}, {CurseRef}, {RestoreRef}).";
                    return false;
            }
        }

        private static bool TryParseParams(
            string param,
            out System.Collections.Generic.Dictionary<string, string> values,
            out string error)
        {
            values = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
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
                    error = $"onDeathEffectParam token '{token}' is not key=value.";
                    return false;
                }

                values[split[0].Trim()] = split[1].Trim();
            }

            return true;
        }

        private static bool TryGetPositiveInt(
            System.Collections.Generic.Dictionary<string, string> values,
            string key,
            out int result,
            out string error)
        {
            if (!TryGetIntOrDefault(values, key, 0, out result, out error))
            {
                return false;
            }

            if (result <= 0)
            {
                error = $"onDeathEffectParam '{key}' must be positive.";
                return false;
            }

            return true;
        }

        private static bool TryGetIntOrDefault(
            System.Collections.Generic.Dictionary<string, string> values,
            string key,
            int fallback,
            out int result,
            out string error)
        {
            error = string.Empty;
            result = fallback;
            if (!values.TryGetValue(key, out var text) || string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
            {
                error = $"onDeathEffectParam '{key}' must be an integer.";
                return false;
            }

            return true;
        }
    }
}

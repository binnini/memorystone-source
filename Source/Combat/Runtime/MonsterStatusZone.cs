using System;
using System.Collections.Generic;
using System.Globalization;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>파싱·검증이 끝난 상태이상 지대 저작 한 벌.</summary>
    internal readonly struct MonsterStatusZoneSpec
    {
        public MonsterStatusZoneSpec(StatusEffectKind statusKind, int durationTurns, HexCoord[] offsets)
        {
            StatusKind = statusKind;
            DurationTurns = durationTurns;
            Offsets = offsets ?? Array.Empty<HexCoord>();
        }

        public StatusEffectKind StatusKind { get; }

        /// <summary>지대가 판에 남는 턴 수(상태이상 지속이 아니다 — 그쪽은 밟을 때마다 갱신된다).</summary>
        public int DurationTurns { get; }

        /// <summary>정동(East) 기준 축좌표. 형상 오프셋의 <b>부분집합</b>이어야 한다(임포트가 검증).</summary>
        /// <remarks>🔴 <c>default(MonsterStatusZoneSpec)</c>은 생성자를 지나지 않아 이 배열이 null이다
        /// ("지대 없음"이 바로 그 값이다) — 그래서 읽는 쪽이 전부 null을 견뎌야 한다.</remarks>
        public HexCoord[] Offsets { get; }

        public bool HasZone => Offsets != null && Offsets.Length > 0;
    }

    /// <summary>
    /// 상태이상 지대(요괴 트랙 §3-3 · §4-3)의 저작 표면 파서. monster_attack_patterns.csv의
    /// <c>zoneEffect</c>(<c>zone:Kind;턴</c>)와 <c>zoneOffsets</c>(형상과 같은 <c>q:r</c> 공백 구분)를 읽는다.
    ///
    /// <para>🔴 지대를 <b>형상이 아니라 패턴</b>에 둔 것이 계약이다 — 형상은 공유 자산이라
    /// 같은 <c>slam-heavy</c>를 쓰는 다음 요괴가 원치 않는 장판을 물려받으면 안 된다.</para>
    ///
    /// <para>🔴 <c>zoneOffsets</c>는 그 패턴 형상의 <b>부분집합</b>이어야 한다. 형상 밖에 지대를 깔면
    /// "예고=명중"이 깨진다 — 위험 칸 예고는 형상에서 나오는데 장판은 다른 칸에 생기기 때문이다.</para>
    /// </summary>
    internal static class MonsterStatusZone
    {
        /// <summary>지대가 거는 상태이상의 갱신 지속(섬광 장판 F03의 <c>FieldImmobilizeRefreshTurns</c>와 같은 값·이유).</summary>
        public const int RefreshTurns = 2;

        /// <summary>효과 신호의 출처 ref. VFX·툴팁이 "몬스터가 깐 지대"를 이 키로 가른다.</summary>
        public const string SourceRef = "monster.zone";

        private const string ZonePrefix = "zone:";

        /// <summary>
        /// <c>zone:Rupture;3</c> + <c>2:-2 2:-1 ...</c>를 판다. 둘 다 비면 "지대 없음"이라 성공한다.
        /// 한쪽만 저작하면 거부한다 — 효과 없는 칸이나 칸 없는 효과는 저작 실수다.
        /// </summary>
        public static bool TryParse(
            string zoneEffect,
            string zoneOffsets,
            out MonsterStatusZoneSpec spec,
            out string error)
        {
            spec = default;
            error = string.Empty;
            var effect = (zoneEffect ?? string.Empty).Trim();
            var offsetsRaw = (zoneOffsets ?? string.Empty).Trim();
            if (effect.Length == 0 && offsetsRaw.Length == 0)
            {
                return true;
            }

            if (effect.Length == 0 || offsetsRaw.Length == 0)
            {
                error = "zoneEffect와 zoneOffsets는 함께 저작한다 — 칸 없는 효과나 효과 없는 칸은 아무 일도 하지 않는다.";
                return false;
            }

            if (!effect.StartsWith(ZonePrefix, StringComparison.Ordinal))
            {
                error = $"zoneEffect '{effect}' must start with '{ZonePrefix}' (예: zone:Rupture;3).";
                return false;
            }

            var body = effect.Substring(ZonePrefix.Length).Split(';');
            if (body.Length != 2 || string.IsNullOrWhiteSpace(body[0]))
            {
                error = $"zoneEffect '{effect}' must be 'zone:<StatusEffectKind>;<턴>'.";
                return false;
            }

            // ignoreCase:false — 저작이 enum 원형과 정확히 일치해야 한다(뒤끝 파서와 같은 규약).
            if (!Enum.TryParse<StatusEffectKind>(body[0].Trim(), ignoreCase: false, out var statusKind))
            {
                error = $"zoneEffect kind '{body[0]}' is not a StatusEffectKind.";
                return false;
            }

            if (!int.TryParse(body[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var turns)
                || turns <= 0)
            {
                error = $"zoneEffect '{effect}' turns must be a positive integer.";
                return false;
            }

            if (turns > MaxDurationTurns)
            {
                error = $"zoneEffect '{effect}' turns {turns} exceeds the cap {MaxDurationTurns}"
                        + " — 지대는 영구가 아니다(요괴 §4-3).";
                return false;
            }

            if (!TryParseOffsets(offsetsRaw, out var offsets, out error))
            {
                return false;
            }

            spec = new MonsterStatusZoneSpec(statusKind, turns, offsets);
            return true;
        }

        /// <summary>지대 지속 상한. "3턴·영구 금지"(§4-3)를 저작 실수로 넘길 수 없게 파서가 잡는다.</summary>
        public const int MaxDurationTurns = 3;

        private static bool TryParseOffsets(string raw, out HexCoord[] offsets, out string error)
        {
            offsets = Array.Empty<HexCoord>();
            error = string.Empty;
            var parsed = new List<HexCoord>();
            var seen = new HashSet<HexCoord>();
            foreach (var token in raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = token.Split(':');
                if (parts.Length != 2
                    || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var q)
                    || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r))
                {
                    error = $"zoneOffsets '{token}' is not 'q:r' (형상 저작과 같은 규약 · 공백 구분).";
                    return false;
                }

                var offset = new HexCoord(q, r);
                if (!seen.Add(offset))
                {
                    error = $"zoneOffsets has duplicate offset {q}:{r}.";
                    return false;
                }

                parsed.Add(offset);
            }

            if (parsed.Count == 0)
            {
                error = "zoneOffsets is empty after parsing.";
                return false;
            }

            offsets = parsed.ToArray();
            return true;
        }
    }
}

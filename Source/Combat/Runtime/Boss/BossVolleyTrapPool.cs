using System;
using System.Collections.Generic;
using System.Globalization;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 살포 동반 함정 풀의 저작 표면 파서(2026-09-05 결정 2). <c>volleyTrapPoolByPhase</c> 한 페이즈 항목
    /// (<c>Slow:2+Curse:2+Stun:1+Turret:1</c>)을 종류·개수 목록으로 편다. 임포트 검증(<c>ValidateParams</c>)과
    /// 결의(<c>Resolve</c>)가 <b>같은 파서</b>를 지나므로 "파싱은 통과했는데 런타임이 다르게 읽는" 갭이 없다
    /// (지대·소환 저작 파서와 같은 규약).
    ///
    /// <para>종류 어휘가 넷뿐인 이유: 맵 함정의 <c>HexTrapEffectKind</c>를 그대로 노출하면 Burn(폐기)·Teleport·
    /// VisionDown 같은 보스전에 뜻이 없는 값이 저작 표면에 새고, 저주(InjectStatusCard)·터렛(SpawnMonsters)은
    /// 카드 풀·몬스터 id를 따로 물어야 해서 「종류」 한 단어로는 부족하다. 그래서 보스 저작 어휘와 맵 enum을
    /// 이 파서가 한 번 번역한다.</para>
    /// </summary>
    public static class BossVolleyTrapPool
    {
        public readonly struct Entry
        {
            public Entry(string kind, int count)
            {
                Kind = kind;
                Count = count;
            }

            /// <summary><see cref="IronScrapMechanicParams.TrapKindSlow"/> 등 네 어휘 중 하나.</summary>
            public string Kind { get; }
            public int Count { get; }
        }

        private static readonly string[] KnownKinds =
        {
            IronScrapMechanicParams.TrapKindSlow,
            IronScrapMechanicParams.TrapKindStun,
            IronScrapMechanicParams.TrapKindCurse,
            IronScrapMechanicParams.TrapKindTurret
        };

        /// <summary>페이즈별 항목(<c>|</c> 구분)을 전부 편다. 빈 값은 빈 목록(함정 없음).</summary>
        public static IReadOnlyList<IReadOnlyList<Entry>> ParseByPhase(string value, string keyForError)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Array.Empty<IReadOnlyList<Entry>>();
            }

            var phases = value.Split(new[] { '|' }, StringSplitOptions.None);
            var result = new IReadOnlyList<Entry>[phases.Length];
            for (var i = 0; i < phases.Length; i++)
            {
                result[i] = ParsePhase(phases[i], keyForError, i + 1);
            }

            return result;
        }

        /// <summary>한 페이즈 항목(<c>Slow:2+Curse:2</c>)을 편다. 빈 항목은 빈 목록(그 페이즈는 함정 없음).</summary>
        public static IReadOnlyList<Entry> ParsePhase(string value, string keyForError, int phaseForError)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Array.Empty<Entry>();
            }

            var parts = value.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            var entries = new List<Entry>(parts.Length);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in parts)
            {
                var pair = raw.Split(':');
                if (pair.Length != 2)
                {
                    throw new ArgumentException(
                        $"mechanicParams '{keyForError}' phase {phaseForError} entry '{raw.Trim()}' must be '종류:개수' (e.g. Slow:2).");
                }

                var kind = pair[0].Trim();
                if (Array.IndexOf(KnownKinds, kind) < 0)
                {
                    throw new ArgumentException(
                        $"mechanicParams '{keyForError}' phase {phaseForError} kind '{kind}' is not one of {string.Join("/", KnownKinds)}.");
                }

                if (!seen.Add(kind))
                {
                    throw new ArgumentException(
                        $"mechanicParams '{keyForError}' phase {phaseForError} lists kind '{kind}' twice — 한 종류는 한 항목에 개수를 합쳐 적는다.");
                }

                if (!int.TryParse(pair[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count < 0)
                {
                    throw new ArgumentException(
                        $"mechanicParams '{keyForError}' phase {phaseForError} kind '{kind}' count '{pair[1].Trim()}' must be a non-negative integer.");
                }

                if (count > 0)
                {
                    entries.Add(new Entry(kind, count));
                }
            }

            return entries;
        }

        /// <summary><c>X07+X05+X02</c> 같은 <c>+</c> 구분 문자열 목록. 빈 값은 빈 목록.</summary>
        public static IReadOnlyList<string> ParseIdList(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Array.Empty<string>();
            }

            var parts = value.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new List<string>(parts.Length);
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (trimmed.Length > 0)
                {
                    result.Add(trimmed);
                }
            }

            return result;
        }

        /// <summary><c>M902|M903|M904</c> 같은 <c>|</c> 구분 페이즈별 문자열 목록. 빈 값은 빈 목록.</summary>
        public static IReadOnlyList<string> ParsePhaseStringList(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Array.Empty<string>();
            }

            var parts = value.Split(new[] { '|' }, StringSplitOptions.None);
            var result = new string[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                result[i] = parts[i].Trim();
            }

            return result;
        }
    }
}

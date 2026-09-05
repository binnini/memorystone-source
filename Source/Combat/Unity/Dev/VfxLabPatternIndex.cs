using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Unity.Dev
{
    /// <summary>
    /// VFX 랩과 스틸 캡처가 공유하는 <b>패턴 조회표</b>. 몬스터 공격 패턴(형상·반경·피해·대상)과
    /// 그 패턴에 붙은 VFX 바인딩을 한 자리에서 준다.
    ///
    /// <para>🔑 CSV를 손으로 파싱하지 않는다 — 출하 변환기
    /// <see cref="MonsterCatalogCsvConverter.ConvertDirectories"/>를 그대로 부른다. 랩이 CSV를 따로
    /// 읽으면 컬럼이 하나 늘 때마다 조용히 뒤처진다(실제로 랩의 옛 리더는 <c>areaSpawnMode</c> 컬럼을
    /// 몰라서 모드 C를 영영 못 봤다).</para>
    /// </summary>
    public static class VfxLabPatternIndex
    {
        public readonly struct Entry
        {
            public Entry(MonsterAttackPattern pattern, string monsterId, string monsterName, IReadOnlyList<string> cueIds)
            {
                Pattern = pattern;
                MonsterId = monsterId ?? string.Empty;
                MonsterName = monsterName ?? string.Empty;
                CueIds = cueIds ?? Array.Empty<string>();
            }

            public MonsterAttackPattern Pattern { get; }
            public string MonsterId { get; }
            public string MonsterName { get; }

            /// <summary>이 패턴에 저작된 바인딩의 cueId들. 둘 이상이면 하이브리드다(A018).</summary>
            public IReadOnlyList<string> CueIds { get; }

            public string PatternId => Pattern.Id;
            public bool IsSelfTargeted => string.Equals(Pattern.Targeting, "self", StringComparison.OrdinalIgnoreCase);

            /// <summary>자기부여 패턴은 피해가 없어 상태 부여 이벤트로 뜬다 — 큐 해소 키가 달라진다.</summary>
            public EffectKind EffectKind => Pattern.Damage > 0 ? EffectKind.Damage : EffectKind.StatusEffectApplied;

            public StatusEffectKind? StatusKind =>
                Pattern.StatusEffects != null && Pattern.StatusEffects.Count > 0
                    ? Pattern.StatusEffects[0]
                    : (StatusEffectKind?)null;

            /// <summary>이 패턴이 덮는 칸. 프로덕션 겨냥 함수를 그대로 쓴다.</summary>
            public IReadOnlyList<HexCoord> ResolveCells(HexCoord origin, HexDirection direction) =>
                VfxLabStage.ResolveShapeCells(Pattern.ShapeId, origin, direction, footprintRadius: 0);

            /// <summary>
            /// 런타임이 이 패턴에 대해 올리는 것과 같은 모양의 이벤트. 큐 해소(<c>ResolveAll</c>)가
            /// 실게임과 같은 답을 내려면 kind·targetUnitId·sourceRef가 전부 맞아야 한다.
            /// </summary>
            public EffectResultEvent BuildEvent(HexCoord origin, IReadOnlyList<HexCoord> cells)
            {
                return new EffectResultEvent(
                    EffectKind,
                    targetUnitId: IsSelfTargeted ? "monster-lab" : "player",
                    appliedAmount: 1,
                    radius: Pattern.AreaRadius,
                    center: origin,
                    sourceRef: "monster.pattern." + Pattern.Id,
                    sourceUnitId: "monster-lab",
                    sourceActorKind: "monster",
                    targetActorKind: IsSelfTargeted ? "monster" : "player",
                    sourcePatternId: Pattern.Id,
                    statusKind: StatusKind,
                    areaCoords: cells);
            }
        }

        /// <summary>
        /// 바인딩 CSV가 커버하는 패턴 전부를 patternId 순으로. 바인딩이 없는 패턴은 빠진다 —
        /// 랩은 "무엇이 저작됐나"를 보는 곳이고, "무엇이 폴백인가"는 카탈로그 해소가 답한다.
        /// </summary>
        public static IReadOnlyList<Entry> Build()
        {
            var bundle = MonsterCatalogCsvConverter.ConvertDirectories(
                CombatCsvPaths.MonsterDirectory,
                CombatCsvPaths.PresentationDirectory);

            var cueIdsByPattern = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var binding in bundle.PatternVfxBindings)
            {
                if (!cueIdsByPattern.TryGetValue(binding.PatternId, out var list))
                {
                    list = new List<string>();
                    cueIdsByPattern[binding.PatternId] = list;
                }

                list.Add(binding.VfxCueId);
            }

            var results = new List<Entry>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var monster in bundle.MonsterCatalog.Entries)
            {
                if (monster.AttackPatterns == null)
                {
                    continue;
                }

                foreach (var pattern in monster.AttackPatterns)
                {
                    if (string.IsNullOrWhiteSpace(pattern.Id) || !seen.Add(monster.Id + "|" + pattern.Id))
                    {
                        continue;
                    }

                    cueIdsByPattern.TryGetValue(pattern.Id, out var cueIds);
                    results.Add(new Entry(pattern, monster.Id, monster.DisplayName, cueIds));
                }
            }

            return results
                .OrderBy(entry => entry.MonsterId, StringComparer.Ordinal)
                .ThenBy(entry => entry.PatternId, StringComparer.Ordinal)
                .ToList();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace SeoulPlayup.Combat.Runtime
{
    public sealed class MonsterCatalogDefinition
    {
        private readonly List<MonsterCatalogEntry> entries;
        private readonly Dictionary<string, MonsterPatternPresentationDefinition> patternPresentations;

        public MonsterCatalogDefinition(
            string sourceId,
            string displayName,
            IEnumerable<MonsterCatalogEntry> entries,
            IEnumerable<MonsterPatternPresentationDefinition> patternPresentations = null)
        {
            SourceId = string.IsNullOrWhiteSpace(sourceId) ? throw new ArgumentException("Monster catalog source id is required.", nameof(sourceId)) : sourceId;
            DisplayName = displayName ?? string.Empty;
            this.entries = entries == null ? new List<MonsterCatalogEntry>() : entries.ToList();
            this.patternPresentations = new Dictionary<string, MonsterPatternPresentationDefinition>(StringComparer.Ordinal);
            foreach (var presentation in patternPresentations ?? Array.Empty<MonsterPatternPresentationDefinition>())
            {
                if (presentation != null && !string.IsNullOrWhiteSpace(presentation.PatternId))
                {
                    this.patternPresentations[presentation.PatternId] = presentation;
                }
            }
        }

        public string SourceId { get; }
        public string DisplayName { get; }
        public IReadOnlyList<MonsterCatalogEntry> Entries => entries;

        public bool TryGetEntry(string id, out MonsterCatalogEntry entry)
        {
            entry = entries.FirstOrDefault(candidate => candidate.Id == id);
            return !string.IsNullOrEmpty(entry.Id);
        }

        /// <summary>
        /// 패턴의 연출 저작(monster_attack_patterns.csv의 vfx/sound/animation 컬럼). 2026-09-05 이전에는
        /// 변환 번들에만 실려 런타임에 닿지 못했다(소비자 0) — 이제 카탈로그가 함께 들고 다니므로
        /// 표현층이 <c>EffectResultEvent.SourcePatternId</c>로 패턴 타격음(soundImpactCueId)을 찾는다.
        /// 손으로 만든 픽스처(patternPresentations 생략)는 아무 패턴도 못 찾는다 — 그때 소리는 종전 그대로다.
        /// </summary>
        public bool TryGetPatternPresentation(string patternId, out MonsterPatternPresentationDefinition presentation)
        {
            presentation = null;
            return !string.IsNullOrWhiteSpace(patternId) && patternPresentations.TryGetValue(patternId, out presentation);
        }

        /// <summary>패턴 id로 규칙 정의를 찾는다(어느 몬스터에 묶였든). 없으면 false.</summary>
        public bool TryGetAttackPattern(string patternId, out MonsterAttackPattern pattern)
        {
            pattern = default;
            if (string.IsNullOrWhiteSpace(patternId))
            {
                return false;
            }

            foreach (var entry in entries)
            {
                var patterns = entry.AttackPatterns;
                if (patterns == null)
                {
                    continue;
                }

                foreach (var candidate in patterns)
                {
                    if (string.Equals(candidate.Id, patternId, StringComparison.Ordinal))
                    {
                        pattern = candidate;
                        return true;
                    }
                }
            }

            return false;
        }

        public MonsterCatalogBindingEvidence CreateBindingEvidence(IEnumerable<string> activeDefinitionIds = null, IEnumerable<string> spawnRefIds = null)
        {
            var reason = string.Empty;
            if (entries.Count == 0)
            {
                reason = "Monster catalog has no monster definitions.";
            }
            else
            {
                var duplicate = entries.GroupBy(entry => entry.Id).FirstOrDefault(group => group.Count() > 1);
                if (duplicate != null)
                {
                    reason = $"Monster catalog has duplicate monster id '{duplicate.Key}'.";
                }
            }

            return new MonsterCatalogBindingEvidence(
                SourceId,
                entries.Select(entry => entry.Id),
                activeDefinitionIds ?? Array.Empty<string>(),
                spawnRefIds ?? Array.Empty<string>(),
                string.IsNullOrEmpty(reason),
                reason);
        }
    }
}

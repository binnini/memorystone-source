using System.Collections.Generic;
using System.Linq;

namespace SeoulPlayup.Combat.Runtime
{
    // Pure presentation-record judgments extracted from MapCombatController (P4 Stage 4).
    // Decides which monster action records the timeline replays and which undispatched buffered
    // effects the defensive leftover flush must suppress. No Unity dependencies; the host's
    // *ForTests wrappers keep delegating here so the existing test contract is unchanged.
    public static class MonsterActionPresentationFilters
    {
        public static string ResolvePresentableAttackingMonsterId(IEnumerable<MonsterActionResolutionRecord> records)
        {
            return GetPresentableMonsterAttackRecords(records)
                .Select(record => record.MonsterId)
                .FirstOrDefault();
        }

        public static IReadOnlyList<MonsterActionResolutionRecord> GetPresentableMonsterAttackRecords(IEnumerable<MonsterActionResolutionRecord> records)
        {
            return records?
                .Where(record => record.AttackedPlayer && record.ShouldPresent)
                .OrderBy(GetAttackPresentationOrder)
                .ThenByDescending(record => record.AffectedPlayer)
                .ThenBy(record => record.MonsterId)
                .ToList()
                ?? new List<MonsterActionResolutionRecord>();
        }

        public static IReadOnlyList<MonsterActionResolutionRecord> GetPresentableMonsterActionRecords(IEnumerable<MonsterActionResolutionRecord> records)
        {
            return records?
                .Where(record => record.ShouldPresent && (record.Moved || record.AttackedPlayer))
                .OrderBy(GetActionPresentationOrder)
                .ThenBy(record => record.MonsterId)
                .ToList()
                ?? new List<MonsterActionResolutionRecord>();
        }

        public static IReadOnlyList<MonsterActionResolutionRecord> GetPresentableMonsterMoveRecords(IEnumerable<MonsterActionResolutionRecord> records)
        {
            return records?
                .Where(record => record.Moved && record.ShouldPresent)
                .OrderBy(record => record.MonsterId)
                .ToList()
                ?? new List<MonsterActionResolutionRecord>();
        }

        private static int GetActionPresentationOrder(MonsterActionResolutionRecord record)
        {
            if (record.ActionOrder >= 0)
            {
                return record.ActionOrder;
            }

            return record.AttackOrder >= 0 ? record.AttackOrder : int.MaxValue;
        }

        private static int GetAttackPresentationOrder(MonsterActionResolutionRecord record)
        {
            if (record.AttackOrder >= 0)
            {
                return record.AttackOrder;
            }

            return record.ActionOrder >= 0 ? record.ActionOrder : int.MaxValue;
        }

        public static bool ShouldSuppressUndispatchedMonsterEffect(EffectResultEvent effect, IReadOnlyList<MonsterActionResolutionRecord> monsterActionRecords)
        {
            if (monsterActionRecords == null || monsterActionRecords.Count == 0)
            {
                return false;
            }

            for (var i = 0; i < monsterActionRecords.Count; i++)
            {
                var record = monsterActionRecords[i];
                var matchesPresentationGroup = !string.IsNullOrEmpty(effect.PresentationGroupId)
                    && string.Equals(record.PresentationGroupId, effect.PresentationGroupId, System.StringComparison.Ordinal);
                var matchesMonsterTarget = IsMonsterTargetEffect(effect)
                    && string.Equals(record.MonsterId, effect.TargetUnitId, System.StringComparison.Ordinal);
                if (!matchesPresentationGroup && !matchesMonsterTarget)
                {
                    continue;
                }

                // Hidden (Unknown/Hinted) monster attacks are intentionally not replayed by the timeline. Do
                // not let the defensive leftover flush leak their VFX/SFX/floating text after the phase.
                return !record.IsMonsterVisible;
            }

            return false;
        }

        public static bool IsMonsterTargetEffect(EffectResultEvent effect)
        {
            return string.Equals(effect.TargetActorKind, "monster", System.StringComparison.OrdinalIgnoreCase)
                || (!string.Equals(effect.TargetUnitId, "player", System.StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(effect.TargetUnitId));
        }
    }
}

using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Unity;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Shared VFX anchor routing for combat effect presentation. Keep this policy in one place so
    /// production bridges and development labs preview effects from the same actor anchors.
    /// </summary>
    public static class EffectVfxAnchorPolicy
    {
        public static CharacterVfxAnchorKind ResolveTargetAnchor(EffectVfxCatalog.Entry entry, EffectResultEvent resultEvent)
        {
            if (entry != null)
            {
                switch (entry.SpawnAnchor)
                {
                    case EffectVfxSpawnAnchor.TargetHitCenter:
                        return CharacterVfxAnchorKind.HitCenter;
                    case EffectVfxSpawnAnchor.TargetGround:
                        return CharacterVfxAnchorKind.Ground;
                    case EffectVfxSpawnAnchor.SourceAttack:
                    case EffectVfxSpawnAnchor.SourceGround:
                    case EffectVfxSpawnAnchor.FieldCenter:
                    case EffectVfxSpawnAnchor.Auto:
                    default:
                        break;
                }
            }

            return ResolveTargetAnchor(resultEvent);
        }

        public static CharacterVfxAnchorKind ResolveSourceAnchor(EffectVfxCatalog.Entry entry, EffectResultEvent resultEvent)
        {
            if (entry != null)
            {
                switch (entry.SpawnAnchor)
                {
                    case EffectVfxSpawnAnchor.SourceGround:
                        return CharacterVfxAnchorKind.Ground;
                    case EffectVfxSpawnAnchor.SourceAttack:
                        return CharacterVfxAnchorKind.AttackSource;
                    case EffectVfxSpawnAnchor.TargetGround:
                    case EffectVfxSpawnAnchor.TargetHitCenter:
                    case EffectVfxSpawnAnchor.FieldCenter:
                    case EffectVfxSpawnAnchor.Auto:
                    default:
                        break;
                }
            }

            return ResolveSourceAnchor(resultEvent);
        }

        public static CharacterVfxAnchorKind ResolveTargetAnchor(EffectResultEvent resultEvent)
        {
            if (resultEvent.Radius > 0 || IsFieldLike(resultEvent) || IsTrapLike(resultEvent))
            {
                return CharacterVfxAnchorKind.Root;
            }

            if (IsMoveLike(resultEvent)
                || resultEvent.Kind == EffectKind.Push
                || resultEvent.Kind == EffectKind.Knockback)
            {
                return CharacterVfxAnchorKind.Ground;
            }

            if (resultEvent.Kind == EffectKind.FogReveal)
            {
                return CharacterVfxAnchorKind.Root;
            }

            return CharacterVfxAnchorKind.HitCenter;
        }

        public static CharacterVfxAnchorKind ResolveSourceAnchor(EffectResultEvent resultEvent)
        {
            if (IsFieldLike(resultEvent) || IsTrapLike(resultEvent))
            {
                return CharacterVfxAnchorKind.Root;
            }

            return CharacterVfxAnchorKind.AttackSource;
        }

        public static bool IsAreaLike(EffectResultEvent resultEvent)
        {
            return resultEvent.Radius > 0 || IsFieldLike(resultEvent) || IsTrapLike(resultEvent);
        }

        public static bool IsFieldLike(EffectResultEvent resultEvent)
        {
            return StartsWith(resultEvent.SourceRef, "field.") ||
                   StartsWith(resultEvent.SourceRef, "area.") ||
                   IsTarget(resultEvent, "field");
        }

        public static bool IsTrapLike(EffectResultEvent resultEvent)
        {
            return StartsWith(resultEvent.SourceRef, "trap.");
        }

        public static bool IsMoveLike(EffectResultEvent resultEvent)
        {
            return StartsWith(resultEvent.SourceRef, "move.") ||
                   EndsWith(resultEvent.SourceRef, ".move") ||
                   StartsWith(resultEvent.SourceRef, "player.move") ||
                   StartsWith(resultEvent.SourceRef, "monster.move");
        }

        private static bool IsTarget(EffectResultEvent resultEvent, string targetUnitId)
        {
            return string.Equals(resultEvent.TargetUnitId, targetUnitId, System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool StartsWith(string value, string prefix)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.StartsWith(prefix, System.StringComparison.Ordinal);
        }

        private static bool EndsWith(string value, string suffix)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.EndsWith(suffix, System.StringComparison.Ordinal);
        }
    }
}

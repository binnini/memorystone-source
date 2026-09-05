using SeoulPlayup.Combat.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>One-shot tile flash request: overlay style plus its fade envelope timing.</summary>
    public readonly struct CombatTileFlashSpec
    {
        public CombatTileFlashSpec(CombatOverlayStyle style, float durationSeconds, float attackSeconds = 0.08f)
        {
            Style = style;
            DurationSeconds = Mathf.Max(0.05f, durationSeconds);
            AttackSeconds = Mathf.Max(0.01f, attackSeconds);
        }

        public CombatOverlayStyle Style { get; }

        /// <summary>Total flash lifetime (rise + fade), in seconds.</summary>
        public float DurationSeconds { get; }

        /// <summary>Rise time from invisible to full intensity, in seconds.</summary>
        public float AttackSeconds { get; }
    }

    /// <summary>
    /// Presentation policy mapping an area-like effect event to its one-shot tile flash (or none).
    /// This replaces the legacy opaque quad highlights:
    ///  - player AoE hits: red fill + edge glow flash
    ///  - monster attack impacts (blocked hits included): red diagonal stripes over the attacker's
    ///    committed pattern footprint — same danger-hatch grammar as MonsterAttackIntent, so
    ///    "예고 → 적중" reads as one visual language; whiffs flash the same footprint in white
    ///  - scout / fog reveals: subtle gold rim (the fog actually lifting is the real feedback)
    ///  - field placement: kind-colored boundary + marching-ants punch on the footprint
    ///  - field per-turn ticks: very faint kind-colored fill swell (turn-start noise kept low)
    ///  - traps: status-colored hard flash with a fast shader pulse
    ///  - field fog-reveal ticks (campfire): no flash — it repeats every turn and would be noise
    /// Colors mirror EffectPresentationController's per-kind palette so floating text, placeholder
    /// particles and tile flashes speak one color language.
    /// </summary>
    public static class CombatTileFlashStyles
    {
        private static readonly Color DamageColor = new Color(1f, 0.2f, 0.12f, 1f);
        private static readonly Color HealColor = new Color(0.25f, 1f, 0.55f, 1f);
        private static readonly Color FogRevealColor = new Color(1f, 0.92f, 0.45f, 1f);
        private static readonly Color BlockColor = new Color(0.2f, 0.65f, 1f, 1f);
        private static readonly Color GenericStatusColor = new Color(0.75f, 0.85f, 1f, 1f);

        /// <summary>
        /// True when this event's flash must cover the attacker's committed pattern footprint
        /// (carried on the event as <see cref="EffectResultEvent.AreaCoords"/>) instead of a disk
        /// around the event center. Applies to monster attack impacts — including fully-blocked
        /// hits (the swing landed either way), whiffs, and single-target patterns (radius 0) — so
        /// the intent hatch and the impact flash always share one shape.
        /// </summary>
        public static bool UsesAttackerFootprint(EffectResultEvent resultEvent)
        {
            return IsMonsterPatternSource(resultEvent.SourceRef)
                && (resultEvent.Kind == EffectKind.Damage
                    || resultEvent.Kind == EffectKind.DamageBlocked
                    || resultEvent.Kind == EffectKind.AttackMissed);
        }

        public static bool TryResolve(EffectResultEvent resultEvent, out CombatTileFlashSpec spec)
        {
            spec = default;

            // Monster attack impacts flash regardless of area radius (footprint comes from the
            // attacker's pattern), so they are resolved before the area-like gate.
            if (UsesAttackerFootprint(resultEvent))
            {
                spec = resultEvent.Kind == EffectKind.AttackMissed
                    ? BuildMonsterWhiffSpec()
                    : BuildMonsterAreaHitSpec();
                return true;
            }

            if (!EffectVfxAnchorPolicy.IsAreaLike(resultEvent))
            {
                return false;
            }

            if (EffectVfxAnchorPolicy.IsTrapLike(resultEvent))
            {
                spec = BuildTrapSpec(ResolveEventColor(resultEvent));
                return true;
            }

            var sourceRef = resultEvent.SourceRef ?? string.Empty;
            switch (sourceRef)
            {
                case CardEffectRefs.FieldPlacement:
                    spec = BuildFieldPlacementSpec(ResolveEventColor(resultEvent));
                    return true;
                case CardEffectRefs.FieldDamage:
                case CardEffectRefs.FieldLifesteal:
                    spec = BuildFieldTickSpec(DamageColor);
                    return true;
                case CardEffectRefs.FieldHeal:
                    spec = BuildFieldTickSpec(HealColor);
                    return true;
                case CardEffectRefs.FieldImmobilizeFlashbang:
                    spec = BuildFieldTickSpec(StatusColor(StatusEffectKind.Immobilize));
                    return true;
                case CardEffectRefs.FieldFogReveal:
                case CardEffectRefs.FieldFogRevealCampfire:
                    // Campfire vision refreshes every turn; flashing each tick would be noise.
                    return false;
            }

            switch (resultEvent.Kind)
            {
                case EffectKind.FogReveal:
                    spec = BuildScoutRevealSpec();
                    return true;
                case EffectKind.Damage:
                    spec = BuildPlayerAreaHitSpec();
                    return true;
                case EffectKind.Heal:
                    spec = BuildSoftAreaSpec(HealColor);
                    return true;
                case EffectKind.StatusEffectApplied:
                    spec = BuildSoftAreaSpec(ResolveEventColor(resultEvent));
                    return true;
                default:
                    // Push/knockback/expiry and other non-impact kinds never flash the footprint.
                    return false;
            }
        }

        private static bool IsMonsterPatternSource(string sourceRef)
        {
            return !string.IsNullOrEmpty(sourceRef) &&
                   sourceRef.StartsWith("monster.pattern", System.StringComparison.Ordinal);
        }

        // Player AoE card impact: solid red wash lifted by a warm glow, gone in half a second.
        private static CombatTileFlashSpec BuildPlayerAreaHitSpec()
        {
            var style = new CombatOverlayStyle(
                WithAlpha(DamageColor, 0.30f),
                Color.clear,
                0.10f,
                useFill: true,
                useBoundary: false,
                edgeGlowColor: new Color(1.20f, 0.40f, 0.25f, 0.90f),
                edgeGlowWidth: 0.55f,
                edgeFeather: 0.16f);
            return new CombatTileFlashSpec(style, 0.5f);
        }

        // Monster AoE impact: the MonsterAttackIntent danger hatch, flashed hard once at the hit.
        // radiusScale 1.0 merges the struck tiles into one silhouette exactly like the intent layer.
        private static CombatTileFlashSpec BuildMonsterAreaHitSpec()
        {
            var style = new CombatOverlayStyle(
                new Color(1f, 0.12f, 0.10f, 0.60f),
                Color.clear,
                0.14f,
                useFill: true,
                useBoundary: false,
                edgeGlowColor: new Color(1.20f, 0.25f, 0.20f, 0.70f),
                edgeGlowWidth: 0.40f,
                edgeFeather: 0.10f,
                fillPatternMode: 1,
                patternScale: 4.0f,
                patternAngle: 45f,
                patternOpacity: 0.75f,
                fillRadiusScale: 1.0f);
            return new CombatTileFlashSpec(style, 0.5f);
        }

        // Monster whiff: the committed footprint glows white — "the swing happened here, but hit
        // nothing" — clearly distinct from the red impact hatch while sharing its shape.
        private static CombatTileFlashSpec BuildMonsterWhiffSpec()
        {
            var style = new CombatOverlayStyle(
                new Color(1f, 1f, 1f, 0.20f),
                Color.clear,
                0.10f,
                useFill: true,
                useBoundary: false,
                edgeGlowColor: new Color(1.20f, 1.20f, 1.20f, 0.80f),
                edgeGlowWidth: 0.50f,
                edgeFeather: 0.16f);
            return new CombatTileFlashSpec(style, 0.45f);
        }

        // Scout reveal: gold rim with a barely-there fill — the fog lifting carries the moment.
        private static CombatTileFlashSpec BuildScoutRevealSpec()
        {
            var style = new CombatOverlayStyle(
                WithAlpha(FogRevealColor, 0.12f),
                Color.clear,
                0.08f,
                useFill: true,
                useBoundary: false,
                edgeGlowColor: new Color(1.30f, 1.05f, 0.40f, 1.0f),
                edgeGlowWidth: 0.60f,
                edgeFeather: 0.20f);
            return new CombatTileFlashSpec(style, 0.6f, attackSeconds: 0.12f);
        }

        // Field placement: boundary + marching ants stamp the footprint once, in the field's color.
        // The lasting range readout stays with the FieldObjectRange hover overlay.
        private static CombatTileFlashSpec BuildFieldPlacementSpec(Color color)
        {
            var style = new CombatOverlayStyle(
                WithAlpha(color, 0.26f),
                WithAlpha(Brighten(color, 1.15f), 0.95f),
                0.13f,
                useFill: true,
                useBoundary: true,
                edgeGlowColor: WithAlpha(Brighten(color, 1.10f), 0.60f),
                edgeGlowWidth: 0.45f,
                edgeFeather: 0.14f,
                antsSpeed: 0.8f,
                antsDashLength: 0.5f);
            return new CombatTileFlashSpec(style, 0.7f);
        }

        // Field per-turn tick: the placement flash's color language at whisper volume — a faint
        // fill swell that says "the field worked this turn" without shouting every turn start.
        private static CombatTileFlashSpec BuildFieldTickSpec(Color color)
        {
            var style = new CombatOverlayStyle(
                WithAlpha(color, 0.11f),
                Color.clear,
                0.08f,
                useFill: true,
                useBoundary: false,
                edgeGlowColor: WithAlpha(Brighten(color, 1.05f), 0.20f),
                edgeGlowWidth: 0.35f,
                edgeFeather: 0.22f);
            return new CombatTileFlashSpec(style, 0.5f, attackSeconds: 0.15f);
        }

        // Trap trigger: the hardest hit of the set — status-colored, strong glow, and a fast shader
        // pulse riding the fade so it reads as a "쾅" double-blink.
        private static CombatTileFlashSpec BuildTrapSpec(Color color)
        {
            var style = new CombatOverlayStyle(
                WithAlpha(color, 0.34f),
                Color.clear,
                0.10f,
                useFill: true,
                useBoundary: false,
                edgeGlowColor: WithAlpha(Brighten(color, 1.25f), 1.0f),
                edgeGlowWidth: 0.60f,
                edgeFeather: 0.14f,
                pulseSpeed: 14f,
                pulseAlphaMin: 0.40f,
                pulseAlphaMax: 1.0f);
            return new CombatTileFlashSpec(style, 0.8f, attackSeconds: 0.06f);
        }

        // Fallback for other area-like impacts (e.g. an area status card): quiet kind-colored wash.
        private static CombatTileFlashSpec BuildSoftAreaSpec(Color color)
        {
            var style = new CombatOverlayStyle(
                WithAlpha(color, 0.16f),
                Color.clear,
                0.08f,
                useFill: true,
                useBoundary: false,
                edgeGlowColor: WithAlpha(Brighten(color, 1.10f), 0.60f),
                edgeGlowWidth: 0.50f,
                edgeFeather: 0.18f);
            return new CombatTileFlashSpec(style, 0.55f);
        }

        private static Color ResolveEventColor(EffectResultEvent resultEvent)
        {
            if (resultEvent.StatusKind.HasValue)
            {
                return StatusColor(resultEvent.StatusKind.Value);
            }

            switch (resultEvent.Kind)
            {
                case EffectKind.Damage:
                case EffectKind.ReflectDamage:
                    return DamageColor;
                case EffectKind.Heal:
                    return HealColor;
                case EffectKind.FogReveal:
                    return FogRevealColor;
                case EffectKind.Block:
                case EffectKind.DamageBlocked:
                    return BlockColor;
                case EffectKind.StatusEffectApplied:
                    return GenericStatusColor;
                default:
                    return Color.white;
            }
        }

        private static Color StatusColor(StatusEffectKind kind) => StatusColor(kind, GenericStatusColor);

        /// <summary>
        /// Per-status tint shared by the tile flash and the effect-particle presenter, which used to hold
        /// byte-identical copies of this switch. Only the unmatched-kind fallback differed, so it stays a
        /// caller argument. The status-icon overlay deliberately keeps its own palette (its 반사/민첩 hues
        /// differ) and is not folded in here.
        /// </summary>
        internal static Color StatusColor(StatusEffectKind kind, Color fallback)
        {
            switch (kind)
            {
                case StatusEffectKind.Immobilize: return new Color(0.55f, 0.75f, 1f, 1f);
                case StatusEffectKind.Poison: return new Color(0.22f, 0.95f, 0.22f, 1f);
                case StatusEffectKind.Stun: return new Color(1f, 0.86f, 0.08f, 1f);
                case StatusEffectKind.Slow: return new Color(0.18f, 0.58f, 1f, 1f);
                case StatusEffectKind.Rupture: return new Color(0.9f, 0.1f, 0.35f, 1f);
                case StatusEffectKind.Reflect: return new Color(0.95f, 0.35f, 1f, 1f);
                case StatusEffectKind.Agility: return new Color(0.6f, 1f, 0.75f, 1f);
                default: return fallback;
            }
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private static Color Brighten(Color color, float factor)
        {
            return new Color(color.r * factor, color.g * factor, color.b * factor, color.a);
        }
    }
}

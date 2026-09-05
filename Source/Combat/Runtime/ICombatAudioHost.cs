using System;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// Seam interface the audio presentation layer depends on instead of the concrete
    /// <c>MapCombatController</c>. Exposes only the read-only combat signals the audio
    /// presenter observes, so the audio layer can live in its own assembly without a
    /// reverse dependency on the Unity combat host (breaks the Audio↔God-Object cycle).
    /// Pure by design (no UnityEngine types) so it can reside in Combat.Runtime.
    /// </summary>
    public interface ICombatAudioHost
    {
        /// <summary>Raised by the host when a UI/combat sound cue should play.</summary>
        event Action<string, string> AudioCueRequested;

        /// <summary>
        /// Raised after a presentation timeline finishes for each monster that died during it
        /// (unit id, monster definition id, context). Carries the unit id — rather than going through
        /// <see cref="AudioCueRequested"/> — so the audio presenter can drop the request when the
        /// effect stream already sounded that monster's death at impact. This is the *fallback* death
        /// path: it only sounds deaths the lethal-effect path could not cover (e.g. a monster that dies
        /// from a snapshot diff without a lethal Damage effect).
        /// </summary>
        event Action<string, string, string> MonsterDeathAudioRequested;

        /// <summary>Current resolved combat state, or null before a combat is bound.</summary>
        CombatState State { get; }

        /// <summary>Whether the victory-phase audio should be suppressed this frame.</summary>
        bool ShouldSuppressVictoryPhaseAudio { get; }

        /// <summary>Resolves the presentation delay (seconds) for the given resolved effect.</summary>
        float ResolveEffectVfxDelaySeconds(EffectResultEvent effect);
    }
}

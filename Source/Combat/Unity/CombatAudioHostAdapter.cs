using System;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Unity
{
    // P4 Stage 1: the ICombatAudioHost implementation extracted from MapCombatController.
    // Plain service owned by the controller (mirrors the CombatCameraController pattern): the
    // controller keeps its serialized fields and seam surface and forwards here, so scene
    // references and CombatAudioPresenter's MonoBehaviour-based binding stay intact. Controller
    // state arrives through delegates so this class never references the concrete host type.
    public sealed class CombatAudioHostAdapter : ICombatAudioHost
    {
        private readonly Func<CombatState> stateProvider;
        private readonly Func<bool> suppressVictoryPhaseAudioProvider;
        private readonly Func<EffectPresentationController> effectPresentationProvider;

        public CombatAudioHostAdapter(
            Func<CombatState> stateProvider,
            Func<bool> suppressVictoryPhaseAudioProvider,
            Func<EffectPresentationController> effectPresentationProvider)
        {
            this.stateProvider = stateProvider;
            this.suppressVictoryPhaseAudioProvider = suppressVictoryPhaseAudioProvider;
            this.effectPresentationProvider = effectPresentationProvider;
        }

        public event Action<string, string> AudioCueRequested;

        public event Action<string, string, string> MonsterDeathAudioRequested;

        public CombatState State => stateProvider();

        public bool ShouldSuppressVictoryPhaseAudio => suppressVictoryPhaseAudioProvider();

        // Per-cue playback delay authored on the resolved VFX catalog entry (0 when none). Used to keep
        // reactions and camera shake in lockstep with the cue's own delayed VFX/floating number.
        public float ResolveEffectVfxDelaySeconds(EffectResultEvent effect)
        {
            var presentation = effectPresentationProvider();
            var catalog = presentation != null ? presentation.VfxCatalog : null;
            if (catalog != null && catalog.TryResolve(effect, out var entry) && entry != null)
            {
                return entry.PlaybackDelaySeconds;
            }

            return 0f;
        }

        /// <summary>
        /// How many seconds of screen time the cue for this effect occupies: its authored start delay plus
        /// its resolved play length (docs/presentation-duration-data-plan.md P4). Returns 0 when no cue
        /// matches or its length is unknown, which callers must read as "no information" rather than
        /// "instant" — the length data is deliberately allowed to be incomplete.
        ///
        /// Delay is included because a cue that waits 0.7s before starting occupies the timeline for that
        /// long too; leaving it out is the exact mistake ResolveTraceTailSeconds makes in reverse (it counts
        /// only the delay).
        /// </summary>
        public float ResolveEffectPresentationSeconds(EffectResultEvent effect)
        {
            var presentation = effectPresentationProvider();
            var catalog = presentation != null ? presentation.VfxCatalog : null;
            if (catalog == null || !catalog.TryResolve(effect, out var entry) || entry == null)
            {
                return 0f;
            }

            return entry.Duration.TryResolveTotalOccupancySeconds(
                entry.PlaybackDelaySeconds,
                entry.PlaybackSpeed,
                out var seconds)
                ? seconds
                : 0f;
        }

        public void RequestAudioCue(string cueId, string context)
        {
            AudioCueRequested?.Invoke(cueId, context ?? string.Empty);
        }

        // Fallback death audio: raised per died monster once its timeline has finished. The presenter
        // drops it when the lethal effect already sounded that monster's death at impact, so a death
        // is heard exactly once no matter which path covers it.
        public void RequestMonsterDeathAudioCues(string unitId, string monsterDefinitionId, string context)
        {
            MonsterDeathAudioRequested?.Invoke(unitId ?? string.Empty, monsterDefinitionId ?? string.Empty, context ?? string.Empty);
        }

        public void RequestInvalidInputAudioCue(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                RequestAudioCue(AudioCueIds.UiCardInvalid, "invalid");
                return;
            }

            var cueId = reason.IndexOf("Ki", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        reason.IndexOf("Not enough", StringComparison.OrdinalIgnoreCase) >= 0
                ? AudioCueIds.UiKiInsufficient
                : AudioCueIds.UiCardInvalid;
            RequestAudioCue(cueId, $"invalid:{reason}");
        }

        public void RequestCardHoverAudio(string cardId)
        {
            RequestAudioCue(AudioCueIds.UiCardHover, $"hover:{cardId}");
        }

        // Plays the per-card-kind "cast" SFX the instant a card is committed (separate from the effect SFX,
        // which fires when the effect resolves). Scout has no cast cue by design.
        public void RaiseCardCastCue(CombatCardKind kind, string cardSelectionKey = "")
        {
            var cueId = kind == CombatCardKind.FieldObject
                ? ResolveFieldObjectCastCue(cardSelectionKey)
                : ResolveCardCastCue(kind);
            if (!string.IsNullOrEmpty(cueId))
            {
                RequestAudioCue(cueId, $"card-cast:{kind}");
            }
        }

        private static string ResolveCardCastCue(CombatCardKind kind)
        {
            switch (kind)
            {
                case CombatCardKind.Attack: return "card.attack.cast";
                case CombatCardKind.Defend: return "card.defend.cast";
                case CombatCardKind.Buff: return "card.buff.cast";
                case CombatCardKind.Move: return "card.move.cast";
                case CombatCardKind.Utility: return "card.utility.cast";
                default: return string.Empty; // Scout (and anything else) has no cast cue.
            }
        }

        private static string ResolveFieldObjectCastCue(string cardSelectionKey)
        {
            return AudioCueIds.CardFieldCast;
        }
    }
}

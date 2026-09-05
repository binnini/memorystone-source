using System;
using System.Collections.Generic;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// Pure presentation-only helper that diffs successive <see cref="PlayerStateSnapshot"/> values
    /// and emits the effect events that should be played for the observed deltas. It owns no Unity
    /// state and never mutates combat rules; it only decides which <see cref="EffectResultEvent"/>s a
    /// presentation layer should render. The first snapshot after a reset establishes a baseline and
    /// produces no events so initial binding never flashes effects.
    /// </summary>
    public sealed class PlayerStateEffectDeltaResolver
    {
        private const string PlayerTargetId = "player";

        private bool hasBaseline;
        private PlayerStateSnapshot previous;

        public bool HasBaseline => hasBaseline;

        /// <summary>Forget the baseline so the next <see cref="Resolve"/> call re-captures without emitting.</summary>
        public void Reset()
        {
            hasBaseline = false;
            previous = default;
        }

        /// <summary>Record a baseline without emitting any events (safe initial bind).</summary>
        public void CaptureBaseline(PlayerStateSnapshot snapshot)
        {
            previous = snapshot;
            hasBaseline = true;
        }

        /// <summary>
        /// Compare <paramref name="current"/> against the stored baseline and return the effect events
        /// for any observed deltas. Always advances the baseline to <paramref name="current"/>.
        /// </summary>
        public IReadOnlyList<EffectResultEvent> Resolve(PlayerStateSnapshot current)
        {
            if (!hasBaseline)
            {
                CaptureBaseline(current);
                return Array.Empty<EffectResultEvent>();
            }

            var events = new List<EffectResultEvent>();

            if (current.Hp < previous.Hp)
            {
                var delta = previous.Hp - current.Hp;
                events.Add(new EffectResultEvent(
                    EffectKind.Damage,
                    targetUnitId: PlayerTargetId,
                    amount: delta,
                    appliedAmount: delta,
                    previousValue: previous.Hp,
                    currentValue: current.Hp,
                    center: current.Position,
                    sourceRef: "player-state.hp-loss"));
            }
            else if (current.Hp > previous.Hp)
            {
                var delta = current.Hp - previous.Hp;
                events.Add(new EffectResultEvent(
                    EffectKind.Heal,
                    targetUnitId: PlayerTargetId,
                    amount: delta,
                    appliedAmount: delta,
                    previousValue: previous.Hp,
                    currentValue: current.Hp,
                    center: current.Position,
                    sourceRef: "player-state.hp-gain"));
            }

            if (current.Block > previous.Block)
            {
                var delta = current.Block - previous.Block;
                events.Add(new EffectResultEvent(
                    EffectKind.Block,
                    targetUnitId: PlayerTargetId,
                    amount: delta,
                    appliedAmount: delta,
                    previousValue: previous.Block,
                    currentValue: current.Block,
                    center: current.Position,
                    sourceRef: "player-state.block-gain"));
            }

            if (current.Visibility.RevealedCount > previous.Visibility.RevealedCount)
            {
                var delta = current.Visibility.RevealedCount - previous.Visibility.RevealedCount;
                events.Add(new EffectResultEvent(
                    EffectKind.FogReveal,
                    targetUnitId: PlayerTargetId,
                    amount: delta,
                    appliedAmount: delta,
                    previousValue: previous.Visibility.RevealedCount,
                    currentValue: current.Visibility.RevealedCount,
                    center: current.Position,
                    radius: 1,
                    sourceRef: "player-state.fog-reveal"));
            }

            previous = current;
            return events;
        }
    }
}

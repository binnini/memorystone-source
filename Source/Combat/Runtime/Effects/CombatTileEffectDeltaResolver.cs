using System;
using System.Collections.Generic;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>Immutable per-monster combat snapshot the tile resolver diffs against.</summary>
    public readonly struct MonsterEffectSnapshot
    {
        public MonsterEffectSnapshot(string id, HexCoord coord, int hp, bool isDead)
        {
            Id = id ?? string.Empty;
            Coord = coord;
            Hp = hp;
            IsDead = isDead;
        }

        public string Id { get; }
        public HexCoord Coord { get; }
        public int Hp { get; }
        public bool IsDead { get; }
    }

    /// <summary>
    /// Pure presentation-only helper that diffs successive monster snapshots and emits the effect
    /// events for tile/target-targeted actions (player attacks, AoE hits on monsters, heals on
    /// monsters). Every emitted event carries the affected monster's hex as <see cref="EffectResultEvent.Center"/>
    /// so the presentation layer can place the VFX on the correct tile rather than on the player.
    /// It owns no Unity state and never mutates combat rules. The first snapshot after a reset
    /// establishes a baseline and produces no events.
    /// </summary>
    public sealed class CombatTileEffectDeltaResolver
    {
        private readonly Dictionary<string, MonsterEffectSnapshot> previous = new Dictionary<string, MonsterEffectSnapshot>();
        private bool hasBaseline;

        public bool HasBaseline => hasBaseline;

        /// <summary>Forget the baseline so the next <see cref="Resolve"/> call re-captures without emitting.</summary>
        public void Reset()
        {
            previous.Clear();
            hasBaseline = false;
        }

        /// <summary>Record a baseline without emitting any events (safe initial bind).</summary>
        public void CaptureBaseline(IEnumerable<MonsterEffectSnapshot> snapshots)
        {
            StorePrevious(snapshots);
            hasBaseline = true;
        }

        /// <summary>
        /// Compare <paramref name="current"/> monster snapshots against the stored baseline and return
        /// the effect events for any observed per-monster HP deltas, each centered on the monster's hex.
        /// Always advances the baseline to <paramref name="current"/>.
        /// </summary>
        public IReadOnlyList<EffectResultEvent> Resolve(IEnumerable<MonsterEffectSnapshot> current)
        {
            if (!hasBaseline)
            {
                CaptureBaseline(current);
                return Array.Empty<EffectResultEvent>();
            }

            var events = new List<EffectResultEvent>();
            var seen = new List<MonsterEffectSnapshot>();
            foreach (var monster in current)
            {
                seen.Add(monster);
                if (string.IsNullOrEmpty(monster.Id) || !previous.TryGetValue(monster.Id, out var before))
                {
                    continue;
                }

                if (monster.Hp < before.Hp)
                {
                    var delta = before.Hp - monster.Hp;
                    events.Add(new EffectResultEvent(
                        EffectKind.Damage,
                        targetUnitId: monster.Id,
                        amount: delta,
                        appliedAmount: delta,
                        previousValue: before.Hp,
                        currentValue: monster.Hp,
                        center: monster.Coord,
                        sourceRef: "combat-tile.monster-hp-loss"));
                }
                else if (monster.Hp > before.Hp)
                {
                    var delta = monster.Hp - before.Hp;
                    events.Add(new EffectResultEvent(
                        EffectKind.Heal,
                        targetUnitId: monster.Id,
                        amount: delta,
                        appliedAmount: delta,
                        previousValue: before.Hp,
                        currentValue: monster.Hp,
                        center: monster.Coord,
                        sourceRef: "combat-tile.monster-hp-gain"));
                }
            }

            StorePrevious(seen);
            return events;
        }

        private void StorePrevious(IEnumerable<MonsterEffectSnapshot> snapshots)
        {
            previous.Clear();
            foreach (var monster in snapshots)
            {
                if (!string.IsNullOrEmpty(monster.Id))
                {
                    previous[monster.Id] = monster;
                }
            }
        }
    }
}

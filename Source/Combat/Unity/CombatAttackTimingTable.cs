using System;
using System.Collections.Generic;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Per-attack presentation-timing overrides, keyed by <c>cardId</c> (player attacks) or
    /// <c>patternId</c> (monster attacks). Each field is optional: an unset field inherits the global
    /// <see cref="CombatTimingProfile"/> value. An empty table — or any row whose fields are all unset —
    /// produces timing byte-identical to the global-only behavior, so this is a purely additive,
    /// non-breaking override layer (release-safe; only authored/edited from the dev timing lab).
    ///
    /// Authoring source of truth is <c>Assets/Data/Combat/Presentation/Source/combat_attack_timing.csv</c>,
    /// imported into the asset by <c>CombatAttackTimingTableCsvImporter</c> (editor only). The lab can also
    /// edit entries live and write them back out to the CSV.
    /// </summary>
    [CreateAssetMenu(menuName = "Seoul Playup/Combat/Attack Timing Table", fileName = "CombatAttackTimingTable")]
    public sealed class CombatAttackTimingTable : ScriptableObject
    {
        /// <summary>
        /// A float that may or may not be authored. Serializes cleanly (Unity has no nullable&lt;float&gt;):
        /// a CSV blank cell leaves <see cref="hasValue"/> false so the global profile value is used.
        /// </summary>
        [Serializable]
        public struct OptionalFloat
        {
            [SerializeField] private bool hasValue;
            [SerializeField] private float value;

            public bool HasValue => hasValue;
            public float Value => value;

            public static OptionalFloat Unset => default;
            public static OptionalFloat Of(float v) => new OptionalFloat { hasValue = true, value = v };

            /// <summary>Return the authored value if present, otherwise <paramref name="fallback"/>.</summary>
            public float Or(float fallback) => hasValue ? value : fallback;

            public void Set(float v)
            {
                hasValue = true;
                value = v;
            }

            public void Clear()
            {
                hasValue = false;
                value = 0f;
            }
        }

        [Serializable]
        public sealed class Entry
        {
            [Tooltip("cardId (player attack) or patternId (monster attack). The lookup key.")]
            public string id;

            [Tooltip("Optional designer note (e.g. 'player basic', 'M001 slam'). Not used at runtime.")]
            public string note;

            [Tooltip("Wind-up delay (trigger -> impact). Unset = inherit global AttackWindupDelay.")]
            public OptionalFloat windupDelay;
            [Tooltip("Impact hold (time on the impact frame). Unset = inherit global AttackImpactDelay.")]
            public OptionalFloat impactDelay;
            [Tooltip("Post-death hold. Unset = inherit global DeathDelay.")]
            public OptionalFloat deathDelay;

            [Tooltip("Player-attack hit-stop seconds. Unset = inherit global. Still gated by global EnableHitStop.")]
            public OptionalFloat playerHitStopSeconds;
            [Tooltip("Monster-attack hit-stop seconds. Unset = inherit global. Still gated by global EnableHitStop.")]
            public OptionalFloat monsterHitStopSeconds;
            [Tooltip("Lethal hit-stop seconds. Unset = inherit global. Still gated by global EnableHitStop.")]
            public OptionalFloat lethalHitStopSeconds;

            // No SFX channel by convention: impact SFX timing is authored on the VFX cue and the sound
            // follows it, so a per-attack sound offset here would be a second place to tune the same beat.
            [Tooltip("Signed VFX+damage-number impact offset. Unset = inherit global VisualImpactOffset.")]
            public OptionalFloat visualImpactOffset;
            [Tooltip("Signed camera-shake impact offset. Unset = inherit global ShakeImpactOffset.")]
            public OptionalFloat shakeImpactOffset;
            [Tooltip("Signed hit-stop impact offset. Unset = inherit global HitStopImpactOffset.")]
            public OptionalFloat hitStopImpactOffset;

            public bool HasAnyOverride =>
                windupDelay.HasValue || impactDelay.HasValue || deathDelay.HasValue
                || playerHitStopSeconds.HasValue || monsterHitStopSeconds.HasValue || lethalHitStopSeconds.HasValue
                || visualImpactOffset.HasValue
                || shakeImpactOffset.HasValue || hitStopImpactOffset.HasValue;
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        public IReadOnlyList<Entry> Entries => entries;

        /// <summary>Find the override row for an id (cardId or patternId). Null/blank ids never match.</summary>
        public bool TryGet(string id, out Entry entry)
        {
            entry = null;
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null && string.Equals(entries[i].id, id, StringComparison.Ordinal))
                {
                    entry = entries[i];
                    return true;
                }
            }

            return false;
        }

        /// <summary>Get the existing row for an id, or create and append a fresh (all-unset) one.</summary>
        public Entry GetOrCreate(string id)
        {
            if (TryGet(id, out var existing))
            {
                return existing;
            }

            var entry = new Entry { id = id };
            entries.Add(entry);
            return entry;
        }
    }
}

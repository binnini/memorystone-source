using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Combat.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    public enum SoundBus
    {
        Ui,
        Sfx,
        Ambience,
        Music,
        Debug
    }

    public enum MissingClipBehavior
    {
        Silent,
        LogWarning,
        EditorOnlyWarning
    }

    public enum SoundPlaybackStatus
    {
        Playable,
        MissingCue,
        MissingClip,
        Cooldown,
        InvalidCueId
    }

    /// <summary>
    /// Unity presentation-layer catalog for stable sound cue IDs. Missing cues and null clips are
    /// tolerated so audio content can be added or removed without blocking gameplay resolution.
    /// </summary>
    [CreateAssetMenu(menuName = "Seoul Playup/Combat/Sound Catalog", fileName = "SoundCatalog")]
    public sealed class SoundCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            [SerializeField] private string cueId = string.Empty;
            [SerializeField] private string displayName = string.Empty;
            [SerializeField] private string category = string.Empty;
            [SerializeField] private string[] tags = Array.Empty<string>();
            [SerializeField] private string designerNote = string.Empty;
            [SerializeField] private bool deprecated;
            [SerializeField] private AudioClip clip;
            [SerializeField] private SoundBus bus = SoundBus.Sfx;
            [Range(0f, 2f)] [SerializeField] private float volume = 1f;
            [Range(0.1f, 3f)] [SerializeField] private float pitchMin = 1f;
            [Range(0.1f, 3f)] [SerializeField] private float pitchMax = 1f;
            [Min(0f)] [SerializeField] private float cooldownSeconds;
            [SerializeField] private MissingClipBehavior missingClipBehavior = MissingClipBehavior.Silent;

            [Header("Play Length (docs/presentation-duration-data-plan.md)")]
            [Tooltip("BAKED, DO NOT HAND-EDIT. Clip length in seconds at the SLOWEST pitch this cue can pick " +
                "(clip.length / pitchMin), written by 'Bake Presentation Durations'. When pitchMin != pitchMax " +
                "playback picks a random pitch, so this is an upper bound rather than an exact duration. " +
                "0 = not measured.")]
            [Min(0f)] [SerializeField] private float measuredLengthSeconds;
            [Tooltip("Authored duration in seconds; 0 = unset, use the measured bound.")]
            [Min(0f)] [SerializeField] private float authoredLengthSeconds;

            public Entry() { }

            public Entry(
                string cueId,
                AudioClip clip = null,
                SoundBus bus = SoundBus.Sfx,
                float volume = 1f,
                float pitchMin = 1f,
                float pitchMax = 1f,
                float cooldownSeconds = 0f,
                MissingClipBehavior missingClipBehavior = MissingClipBehavior.Silent,
                string displayName = "",
                string category = "",
                string[] tags = null,
                string designerNote = "",
                bool deprecated = false,
                // Appended last so existing positional/named call sites keep compiling unchanged.
                float measuredLengthSeconds = 0f,
                float authoredLengthSeconds = 0f)
            {
                this.measuredLengthSeconds = Mathf.Max(0f, measuredLengthSeconds);
                this.authoredLengthSeconds = Mathf.Max(0f, authoredLengthSeconds);
                this.cueId = cueId ?? string.Empty;
                this.displayName = displayName ?? string.Empty;
                this.category = category ?? string.Empty;
                this.tags = tags ?? Array.Empty<string>();
                this.designerNote = designerNote ?? string.Empty;
                this.deprecated = deprecated;
                this.clip = clip;
                this.bus = bus;
                this.volume = Mathf.Clamp(volume, 0f, 2f);
                this.pitchMin = Mathf.Clamp(pitchMin, 0.1f, 3f);
                this.pitchMax = Mathf.Clamp(pitchMax, 0.1f, 3f);
                this.cooldownSeconds = Mathf.Max(0f, cooldownSeconds);
                this.missingClipBehavior = missingClipBehavior;
            }

            public string CueId => cueId ?? string.Empty;
            public string DisplayName => displayName ?? string.Empty;
            public string Category => category ?? string.Empty;
            public string[] Tags => tags ?? Array.Empty<string>();
            public string DesignerNote => designerNote ?? string.Empty;
            public bool Deprecated => deprecated;
            public AudioClip Clip => clip;
            public SoundBus Bus => bus;
            public float Volume => Mathf.Clamp(volume, 0f, 2f);
            public float PitchMin => Mathf.Clamp(Mathf.Min(pitchMin, pitchMax), 0.1f, 3f);
            public float PitchMax => Mathf.Clamp(Mathf.Max(pitchMin, pitchMax), 0.1f, 3f);
            public float CooldownSeconds => Mathf.Max(0f, cooldownSeconds);
            public MissingClipBehavior MissingClipBehavior => missingClipBehavior;

            /// <summary>Baked clip length at the slowest pitch; 0 when unmeasured. Tool output.</summary>
            public float MeasuredLengthSeconds => Mathf.Max(0f, measuredLengthSeconds);

            /// <summary>Authored duration; 0 = unset. The only hand-edited half of the pair.</summary>
            public float AuthoredLengthSeconds => Mathf.Max(0f, authoredLengthSeconds);

            /// <summary>
            /// True when this cue's pitch is fixed, so <see cref="MeasuredLengthSeconds"/> is exact rather
            /// than an upper bound. 14 of the 75 shipping cues randomize pitch and are therefore inexact.
            /// </summary>
            public bool HasDeterministicLength => PitchMin >= PitchMax;

            /// <summary>
            /// How long this cue plays, on the <b>unscaled</b> clock: <see cref="AudioSource"/> ignores
            /// <c>Time.timeScale</c>, so this must not be added to a scaled VFX/animation length without
            /// accounting for hit-stop (plan §2.5). Music and Ambience are reported as unmeasurable because
            /// they loop — a clip length is not their play length.
            ///
            /// Nothing in the shipping presentation reads this yet (plan D6).
            /// </summary>
            public PresentationDuration Duration => PresentationDuration.Create(
                MeasuredLengthSeconds,
                AuthoredLengthSeconds,
                measurable: bus != SoundBus.Music && bus != SoundBus.Ambience,
                clock: PresentationClock.Unscaled);
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        private readonly Dictionary<string, float> lastPlayableTimes = new Dictionary<string, float>();
        private Dictionary<string, Entry> lookup;

        public IReadOnlyList<Entry> Entries => entries;

        public void SetEntries(params Entry[] newEntries)
        {
            entries = newEntries == null ? new List<Entry>() : newEntries.ToList();
            RebuildLookup();
        }

        public static SoundCatalog CreateForTests(params Entry[] testEntries)
        {
            var catalog = CreateInstance<SoundCatalog>();
            if (testEntries != null)
            {
                catalog.entries.AddRange(testEntries);
            }

            catalog.RebuildLookup();
            return catalog;
        }


        public static SoundCatalog CreateP0PlaceholderCatalogForRuntime()
        {
            return CreateForTests(
                new Entry(AudioCueIds.UiCardHover, bus: SoundBus.Ui, cooldownSeconds: 0.08f),
                new Entry(AudioCueIds.UiCardSelect, bus: SoundBus.Ui, cooldownSeconds: 0.05f),
                new Entry(AudioCueIds.UiCardInvalid, bus: SoundBus.Ui, cooldownSeconds: 0.15f),
                new Entry(AudioCueIds.UiKiInsufficient, bus: SoundBus.Ui, cooldownSeconds: 0.20f),
                new Entry(AudioCueIds.UiPhaseChange, bus: SoundBus.Ui, cooldownSeconds: 0.10f),
                new Entry(AudioCueIds.RewardCardHover, bus: SoundBus.Ui, cooldownSeconds: 0.08f),
                new Entry(AudioCueIds.CardMoveResolve, bus: SoundBus.Sfx, cooldownSeconds: 0.10f),
                new Entry(AudioCueIds.CardAttackResolve, bus: SoundBus.Sfx, cooldownSeconds: 0.05f),
                new Entry(AudioCueIds.CardDefendResolve, bus: SoundBus.Sfx, cooldownSeconds: 0.05f),
                new Entry(AudioCueIds.CardScoutResolve, bus: SoundBus.Sfx, cooldownSeconds: 0.10f),
                new Entry(AudioCueIds.FogTileRevealed, bus: SoundBus.Sfx, cooldownSeconds: 0.20f),
                new Entry(AudioCueIds.CombatPlayerHit, bus: SoundBus.Sfx, cooldownSeconds: 0.05f),
                new Entry(AudioCueIds.CombatPlayerDeath, bus: SoundBus.Sfx, cooldownSeconds: 0.50f),
                new Entry(AudioCueIds.CombatMonsterHit, bus: SoundBus.Sfx, cooldownSeconds: 0.05f),
                new Entry(AudioCueIds.PlayerVoiceAttack, bus: SoundBus.Sfx, volume: 1.6f, cooldownSeconds: 0.10f),
                new Entry(AudioCueIds.PlayerVoiceHit, bus: SoundBus.Sfx, volume: 1.6f, cooldownSeconds: 0.05f),
                new Entry(AudioCueIds.PlayerVoiceDotHit, bus: SoundBus.Sfx, volume: 1.6f, cooldownSeconds: 0.05f),
                new Entry(AudioCueIds.PlayerVoiceDeath, bus: SoundBus.Sfx, volume: 1.6f, cooldownSeconds: 0.10f),
                new Entry(AudioCueIds.ObjectiveMemoryGyeolRevealed, bus: SoundBus.Sfx, cooldownSeconds: 0.25f),
                new Entry(AudioCueIds.ObjectiveInvestigateSuccess, bus: SoundBus.Sfx, cooldownSeconds: 0.25f),
                new Entry(AudioCueIds.GameDefeat, bus: SoundBus.Music, cooldownSeconds: 0.50f),
                new Entry(AudioCueIds.GameVictory, bus: SoundBus.Sfx, cooldownSeconds: 0.50f),
                new Entry(AudioCueIds.MusicVictory, bus: SoundBus.Music, cooldownSeconds: 0.50f),
                new Entry("music.lobby", bus: SoundBus.Music, cooldownSeconds: 0.50f),
                new Entry("music.gameplay", bus: SoundBus.Music, cooldownSeconds: 0.50f),
                new Entry(AudioCueIds.UiCardDraw, bus: SoundBus.Ui, cooldownSeconds: 0.05f),
                new Entry(AudioCueIds.UiCardDiscard, bus: SoundBus.Ui, cooldownSeconds: 0.05f),
                new Entry(AudioCueIds.MovementTerrainStreet, bus: SoundBus.Sfx, cooldownSeconds: 0.10f),
                new Entry(AudioCueIds.MovementTerrainPark, bus: SoundBus.Sfx, cooldownSeconds: 0.10f),
                new Entry(AudioCueIds.MonsterIntentWarning, bus: SoundBus.Sfx, cooldownSeconds: 0.25f),
                new Entry(AudioCueIds.MonsterMoveResolve, bus: SoundBus.Sfx, cooldownSeconds: 0.10f),
                new Entry(AudioCueIds.EnemyTurnBegin, bus: SoundBus.Sfx, cooldownSeconds: 0.10f),
                new Entry(AudioCueIds.AmbienceSeoulBase, bus: SoundBus.Ambience, cooldownSeconds: 1.00f),
                new Entry(AudioCueIds.AmbienceYokaiPressure, bus: SoundBus.Ambience, cooldownSeconds: 1.00f),
                new Entry(AudioCueIds.CombatEnemyDeath, bus: SoundBus.Sfx, cooldownSeconds: 0.10f),
                new Entry(AudioCueIds.EffectHeal, bus: SoundBus.Sfx, cooldownSeconds: 0.10f),
                new Entry(AudioCueIds.EffectPoison, bus: SoundBus.Sfx, cooldownSeconds: 0.12f),
                new Entry(AudioCueIds.EffectStun, bus: SoundBus.Sfx, cooldownSeconds: 0.12f),
                new Entry(AudioCueIds.EffectSlow, bus: SoundBus.Sfx, cooldownSeconds: 0.12f),
                new Entry(AudioCueIds.EffectRupture, bus: SoundBus.Sfx, cooldownSeconds: 0.12f),
                new Entry(AudioCueIds.EffectImmobilize, bus: SoundBus.Sfx, cooldownSeconds: 0.12f),
                new Entry(AudioCueIds.EffectPush, bus: SoundBus.Sfx, cooldownSeconds: 0.10f),
                new Entry(AudioCueIds.EffectTrapTrigger, bus: SoundBus.Sfx, cooldownSeconds: 0.15f),
                new Entry(AudioCueIds.EffectKnockbackImpact, bus: SoundBus.Sfx, cooldownSeconds: 0.05f),
                new Entry(AudioCueIds.FieldDamageEffect, bus: SoundBus.Sfx, cooldownSeconds: 0.10f),
                new Entry(AudioCueIds.FieldFlashbangEffect, bus: SoundBus.Sfx, cooldownSeconds: 0.10f),
                new Entry(AudioCueIds.CardBuffResolve, bus: SoundBus.Sfx, cooldownSeconds: 0.10f),
                new Entry(AudioCueIds.CardMoveCast, bus: SoundBus.Sfx, cooldownSeconds: 0.10f),
                new Entry(AudioCueIds.CardAttackCast, bus: SoundBus.Sfx, cooldownSeconds: 0.10f),
                new Entry(AudioCueIds.CardDefendCast, bus: SoundBus.Sfx, cooldownSeconds: 0.10f),
                new Entry(AudioCueIds.CardBuffCast, bus: SoundBus.Sfx, cooldownSeconds: 0.10f),
                new Entry(AudioCueIds.CardUtilityCast, bus: SoundBus.Sfx, cooldownSeconds: 0.10f),
                new Entry(AudioCueIds.CardFieldCast, bus: SoundBus.Sfx, cooldownSeconds: 0.10f),
                new Entry(AudioCueIds.MonsterAttack("M001"), bus: SoundBus.Sfx, cooldownSeconds: 0.10f),
                new Entry(AudioCueIds.MonsterHit("M001"), bus: SoundBus.Sfx, cooldownSeconds: 0.05f),
                new Entry(AudioCueIds.MonsterDeath("M001"), bus: SoundBus.Sfx, cooldownSeconds: 0.10f),
                new Entry(AudioCueIds.MonsterAttack("M002"), bus: SoundBus.Sfx, cooldownSeconds: 0.10f),
                new Entry(AudioCueIds.MonsterHit("M002"), bus: SoundBus.Sfx, cooldownSeconds: 0.05f),
                new Entry(AudioCueIds.MonsterDeath("M002"), bus: SoundBus.Sfx, cooldownSeconds: 0.10f),
                new Entry(AudioCueIds.MonsterAttack("M003"), bus: SoundBus.Sfx, cooldownSeconds: 0.10f),
                new Entry(AudioCueIds.MonsterHit("M003"), bus: SoundBus.Sfx, cooldownSeconds: 0.05f),
                new Entry(AudioCueIds.MonsterDeath("M003"), bus: SoundBus.Sfx, cooldownSeconds: 0.10f),
                new Entry(AudioCueIds.MonsterAttack("M004"), bus: SoundBus.Sfx, cooldownSeconds: 0.10f),
                new Entry(AudioCueIds.MonsterHit("M004"), bus: SoundBus.Sfx, cooldownSeconds: 0.05f),
                new Entry(AudioCueIds.MonsterDeath("M004"), bus: SoundBus.Sfx, cooldownSeconds: 0.10f),
                new Entry(AudioCueIds.MonsterAttack("M005"), bus: SoundBus.Sfx, cooldownSeconds: 0.10f),
                new Entry(AudioCueIds.MonsterHit("M005"), bus: SoundBus.Sfx, cooldownSeconds: 0.05f),
                new Entry(AudioCueIds.MonsterDeath("M005"), bus: SoundBus.Sfx, cooldownSeconds: 0.10f),
                new Entry(AudioCueIds.MonsterAttack("M006"), bus: SoundBus.Sfx, cooldownSeconds: 0.10f),
                new Entry(AudioCueIds.MonsterHit("M006"), bus: SoundBus.Sfx, cooldownSeconds: 0.05f),
                new Entry(AudioCueIds.MonsterDeath("M006"), bus: SoundBus.Sfx, cooldownSeconds: 0.10f),
                new Entry(AudioCueIds.RewardPopupAppear, bus: SoundBus.Ui, cooldownSeconds: 0.20f),
                new Entry(AudioCueIds.RewardCardAcquire, bus: SoundBus.Ui, cooldownSeconds: 0.10f),
                new Entry(AudioCueIds.RewardChestOpen, bus: SoundBus.Ui, cooldownSeconds: 0.20f),
                new Entry(AudioCueIds.UiButtonClick, bus: SoundBus.Ui, cooldownSeconds: 0.05f));
        }

        public bool TryGetEntry(string cueId, out Entry entry)
        {
            entry = null;
            if (string.IsNullOrWhiteSpace(cueId))
            {
                return false;
            }

            EnsureLookup();
            return lookup.TryGetValue(cueId, out entry) && entry != null;
        }

        public SoundPlaybackStatus TryBeginPlayback(string cueId, float nowSeconds, out Entry entry)
        {
            entry = null;
            if (string.IsNullOrWhiteSpace(cueId))
            {
                return SoundPlaybackStatus.InvalidCueId;
            }

            if (!TryGetEntry(cueId, out entry))
            {
                return SoundPlaybackStatus.MissingCue;
            }

            if (entry.Clip == null)
            {
                return SoundPlaybackStatus.MissingClip;
            }

            var now = Mathf.Max(0f, nowSeconds);
            if (entry.CooldownSeconds > 0f && lastPlayableTimes.TryGetValue(entry.CueId, out var last) && now - last < entry.CooldownSeconds)
            {
                return SoundPlaybackStatus.Cooldown;
            }

            lastPlayableTimes[entry.CueId] = now;
            return SoundPlaybackStatus.Playable;
        }

        public void ResetCooldowns()
        {
            lastPlayableTimes.Clear();
        }

        private void OnEnable()
        {
            RebuildLookup();
        }

        private void OnValidate()
        {
            RebuildLookup();
        }

        private void EnsureLookup()
        {
            if (lookup == null)
            {
                RebuildLookup();
            }
        }

        private void RebuildLookup()
        {
            lookup = new Dictionary<string, Entry>(StringComparer.Ordinal);
            if (entries == null)
            {
                entries = new List<Entry>();
                return;
            }

            foreach (var entry in entries)
            {
                if (entry == null || entry.Deprecated || string.IsNullOrWhiteSpace(entry.CueId))
                {
                    continue;
                }

                if (!lookup.ContainsKey(entry.CueId))
                {
                    lookup.Add(entry.CueId, entry);
                }
            }
        }
    }
}

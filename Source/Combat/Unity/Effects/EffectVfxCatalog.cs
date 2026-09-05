using System;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    public enum EffectVfxTargetFilter
    {
        Any,
        Player,
        Monster,
        Field
    }

    public enum EffectVfxSpawnAnchor
    {
        Auto,
        SourceAttack,
        SourceGround,
        TargetHitCenter,
        TargetGround,
        FieldCenter
    }

    /// <summary>
    /// How an area effect's cue is placed over its footprint. None keeps the single center/actor
    /// spawn every existing entry ships with; PerTile spawns one instance per affected tile
    /// (ring/donut/artillery/spiral/tremor patterns), optionally staggered per tile.
    /// </summary>
    public enum EffectVfxAreaSpawnMode
    {
        None,
        PerTile
    }

    [CreateAssetMenu(menuName = "Seoul Playup/Combat/Effect VFX Catalog", fileName = "EffectVfxCatalog")]
    public sealed class EffectVfxCatalog : ScriptableObject
    {
        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        public Entry[] Entries => entries ?? Array.Empty<Entry>();

        public void SetEntries(params Entry[] newEntries)
        {
            entries = newEntries ?? Array.Empty<Entry>();
        }

        public bool TryResolve(EffectResultEvent resultEvent, out Entry entry)
        {
            var safeEntries = Entries;
            for (var i = 0; i < safeEntries.Length; i++)
            {
                var candidate = safeEntries[i];
                if (candidate != null && !candidate.Deprecated && candidate.MatchesExactSourceCardId(resultEvent))
                {
                    entry = candidate;
                    return true;
                }
            }

            for (var i = 0; i < safeEntries.Length; i++)
            {
                var candidate = safeEntries[i];
                if (candidate != null && !candidate.Deprecated && candidate.MatchesExactSourceRef(resultEvent))
                {
                    entry = candidate;
                    return true;
                }
            }

            for (var i = 0; i < safeEntries.Length; i++)
            {
                var candidate = safeEntries[i];
                if (candidate != null && !candidate.Deprecated && candidate.MatchesSourceRefPrefix(resultEvent))
                {
                    entry = candidate;
                    return true;
                }
            }

            for (var i = 0; i < safeEntries.Length; i++)
            {
                var candidate = safeEntries[i];
                if (candidate != null && !candidate.Deprecated && candidate.MatchesStatusKindApply(resultEvent))
                {
                    entry = candidate;
                    return true;
                }
            }

            for (var i = 0; i < safeEntries.Length; i++)
            {
                var candidate = safeEntries[i];
                if (candidate != null && !candidate.Deprecated && candidate.MatchesKind(resultEvent))
                {
                    entry = candidate;
                    return true;
                }
            }

            entry = null;
            return false;
        }

        public Entry[] ResolveAll(EffectResultEvent resultEvent)
        {
            var safeEntries = Entries;

            var cardScoped = new System.Collections.Generic.List<Entry>();
            for (var i = 0; i < safeEntries.Length; i++)
            {
                var candidate = safeEntries[i];
                if (candidate != null && !candidate.Deprecated && candidate.MatchesExactSourceCardId(resultEvent))
                {
                    cardScoped.Add(candidate);
                }
            }

            if (cardScoped.Count > 0)
            {
                return cardScoped.ToArray();
            }

            var exact = new System.Collections.Generic.List<Entry>();
            for (var i = 0; i < safeEntries.Length; i++)
            {
                var candidate = safeEntries[i];
                if (candidate != null && !candidate.Deprecated && candidate.MatchesExactSourceRef(resultEvent))
                {
                    exact.Add(candidate);
                }
            }

            if (exact.Count > 0)
            {
                return exact.ToArray();
            }

            var prefix = new System.Collections.Generic.List<Entry>();
            for (var i = 0; i < safeEntries.Length; i++)
            {
                var candidate = safeEntries[i];
                if (candidate != null && !candidate.Deprecated && candidate.MatchesSourceRefPrefix(resultEvent))
                {
                    prefix.Add(candidate);
                }
            }

            if (prefix.Count > 0)
            {
                return prefix.ToArray();
            }

            var statusApply = new System.Collections.Generic.List<Entry>();
            for (var i = 0; i < safeEntries.Length; i++)
            {
                var candidate = safeEntries[i];
                if (candidate != null && !candidate.Deprecated && candidate.MatchesStatusKindApply(resultEvent))
                {
                    statusApply.Add(candidate);
                }
            }

            if (statusApply.Count > 0)
            {
                return statusApply.ToArray();
            }

            for (var i = 0; i < safeEntries.Length; i++)
            {
                var candidate = safeEntries[i];
                if (candidate != null && !candidate.Deprecated && candidate.MatchesKind(resultEvent))
                {
                    return new[] { candidate };
                }
            }

            return System.Array.Empty<Entry>();
        }

        /// <summary>
        /// Resolves the persistent looping VFX cue authored for a given status effect on a given actor
        /// kind. Loop cues are kept out of the one-shot resolution paths (<see cref="TryResolve"/> /
        /// <see cref="ResolveAll"/>) so applying a status still plays its instant burst separately.
        /// </summary>
        public bool TryResolveStatusLoop(StatusEffectKind statusKind, EffectVfxTargetFilter target, out Entry entry)
        {
            var safeEntries = Entries;
            for (var i = 0; i < safeEntries.Length; i++)
            {
                var candidate = safeEntries[i];
                if (candidate != null && !candidate.Deprecated && candidate.MatchesStatusLoop(statusKind, target))
                {
                    entry = candidate;
                    return true;
                }
            }

            entry = null;
            return false;
        }

#if UNITY_EDITOR
        public void SetEntriesForTests(params Entry[] testEntries)
        {
            SetEntries(testEntries);
        }
#endif

        [Serializable]
        public sealed class Entry
        {
            [SerializeField] private string cueId = string.Empty;
            [SerializeField] private string displayName = string.Empty;
            [SerializeField] private string category = string.Empty;
            [SerializeField] private string[] tags = Array.Empty<string>();
            [SerializeField] private string designerNote = string.Empty;
            [SerializeField] private bool deprecated;
            [SerializeField] private EffectKind kind;
            [SerializeField] private EffectVfxTargetFilter targetFilter;
            [SerializeField] private EffectVfxSpawnAnchor spawnAnchor;
            [Tooltip("None (default) keeps the single center spawn. PerTile spawns one instance per affected " +
                "footprint tile (EffectResultEvent.AreaCoords, disk fallback) when played through PlayArea. " +
                "Author scaleWithRadius=false on PerTile cues — each instance covers one tile, not the whole disk.")]
            [SerializeField] private EffectVfxAreaSpawnMode areaSpawnMode;
            [Tooltip("Per-tile stagger for PerTile mode: tile i spawns after playbackDelaySeconds + i × this. " +
                "0 = all tiles at once. Tile order follows the authored footprint order (shape offsets / center-out disk).")]
            [SerializeField] private float perTileDelaySeconds;
            [Tooltip("When true the spawned one-shot tracks the source actor's spawn anchor transform every " +
                "frame (position + rotation delta since spawn), so e.g. a breath cone sweeps with the " +
                "monster's head animation. Requires the caller to supply the anchor transform; without one " +
                "the cue falls back to the normal fixed spawn. PerTile instances never follow.")]
            [SerializeField] private bool followSourceAnchor;
            [SerializeField] private string sourceRef = string.Empty;
            [Tooltip("When set, this cue is selected by the event's SourceCardId — the top resolution tier. " +
                "Lets one card override a look authored on a shared behaviour ref (e.g. field.damage, which " +
                "several field cards tick under) without touching the shared entry. If sourceRef is also set " +
                "it acts as an extra constraint, so the override applies only to that effect of that card.")]
            [SerializeField] private string sourceCardId = string.Empty;
            [SerializeField] private bool matchSourceRefPrefix;
            [Tooltip("When true this one-shot cue is selected by matching the event's StatusKind (status-apply tier), sitting between the sourceRef tiers and the plain kind tier.")]
            [SerializeField] private bool matchStatusKind;
            [SerializeField] private GameObject[] prefabs = Array.Empty<GameObject>();
            [SerializeField] private float scaleMultiplier = 1f;
            [SerializeField] private bool scaleWithRadius = true;
            [Tooltip("Per-axis scale multipliers applied on top of scaleMultiplier and the (uniform) " +
                "scaleWithRadius growth, in the cue's facing-rotated local axes. 1 (or 0/unset) per axis " +
                "keeps that axis uniform. Applied on a dedicated unrotated child pivot so the rotated " +
                "spawn root never combines rotation with non-uniform scale (no shear).")]
            [SerializeField] private Vector3 axisScaleMultiplier = Vector3.one;
            [SerializeField] private Vector3 positionOffset;
            [SerializeField] private Vector3 rotationEulerOffset;
            [SerializeField] private float lifetimeOverride;
            [SerializeField] private float playbackDelaySeconds;
            [Tooltip("Particle simulation speed multiplier applied to every ParticleSystem in the spawned prefab. 1 (or 0/unset) keeps the prefab's authored speed.")]
            [SerializeField] private float playbackSpeed = 1f;
            [SerializeField] private EffectFloatingTextMode floatingTextMode = EffectFloatingTextMode.Auto;
            [SerializeField] private string floatingTextOverride = string.Empty;

            [Header("Persistent Status Loop VFX")]
            [Tooltip("When true this cue is a persistent, looping status VFX that stays attached until the status is removed (not a one-shot burst).")]
            [SerializeField] private bool loop;
            [Tooltip("When true the looping VFX is parented under the affected actor so it follows the character's movement.")]
            [SerializeField] private bool attachToActor = true;
            [Tooltip("Which status effect this looping cue represents. Only used when loop is true.")]
            [SerializeField] private StatusEffectKind statusKind;
            [Tooltip("Which actor anchor the looping VFX attaches to (feet, chest, head, ...). Only used when loop is true.")]
            [SerializeField] private CharacterVfxAnchorKind loopAnchor = CharacterVfxAnchorKind.Ground;

            [Header("Play Length (docs/presentation-duration-data-plan.md)")]
            [Tooltip("BAKED, DO NOT HAND-EDIT. The prefab's natural play length in seconds at playbackSpeed 1, " +
                "written by 'Bake Presentation Durations'. 0 = not measured (or not measurable: a loop cue, a " +
                "prefab with no ParticleSystem, or one containing a looping system). This is a fact about the " +
                "asset; the drift audit re-measures and compares against it, so editing it by hand defeats the audit.")]
            [SerializeField] private float measuredLengthSeconds;
            [Tooltip("Authored on-screen duration in seconds; 0 = unset, use the measured length. THIS is the " +
                "field to tune when a consumer should treat the event as longer or shorter than the asset. " +
                "Deliberately separate from lifetimeOverride, which destroys the spawned object — trimming a " +
                "look there must not silently retime the camera.")]
            [SerializeField] private float authoredLengthSeconds;

            public Entry(
                EffectKind kind,
                GameObject[] prefabs,
                EffectVfxTargetFilter targetFilter = EffectVfxTargetFilter.Any,
                string sourceRef = "",
                bool matchSourceRefPrefix = false,
                float scaleMultiplier = 1f,
                bool scaleWithRadius = true,
                Vector3 positionOffset = default,
                Vector3 rotationEulerOffset = default,
                float lifetimeOverride = 0f,
                string cueId = "",
                string displayName = "",
                string category = "",
                string[] tags = null,
                string designerNote = "",
                bool deprecated = false,
                EffectVfxSpawnAnchor spawnAnchor = EffectVfxSpawnAnchor.Auto,
                EffectFloatingTextMode floatingTextMode = EffectFloatingTextMode.Auto,
                string floatingTextOverride = "",
                float playbackDelaySeconds = 0f,
                bool matchStatusKind = false,
                bool loop = false,
                bool attachToActor = true,
                StatusEffectKind statusKind = StatusEffectKind.Immobilize,
                CharacterVfxAnchorKind loopAnchor = CharacterVfxAnchorKind.Ground,
                // Appended last so existing positional/named call sites keep compiling unchanged.
                string sourceCardId = "",
                float playbackSpeed = 1f,
                Vector3 axisScaleMultiplier = default,
                float measuredLengthSeconds = 0f,
                float authoredLengthSeconds = 0f,
                EffectVfxAreaSpawnMode areaSpawnMode = EffectVfxAreaSpawnMode.None,
                float perTileDelaySeconds = 0f,
                bool followSourceAnchor = false)
            {
                this.areaSpawnMode = areaSpawnMode;
                this.perTileDelaySeconds = Mathf.Max(0f, perTileDelaySeconds);
                this.followSourceAnchor = followSourceAnchor;
                this.measuredLengthSeconds = Mathf.Max(0f, measuredLengthSeconds);
                this.authoredLengthSeconds = Mathf.Max(0f, authoredLengthSeconds);
                this.sourceCardId = sourceCardId ?? string.Empty;
                this.cueId = cueId ?? string.Empty;
                this.displayName = displayName ?? string.Empty;
                this.category = category ?? string.Empty;
                this.tags = tags ?? Array.Empty<string>();
                this.designerNote = designerNote ?? string.Empty;
                this.deprecated = deprecated;
                this.kind = kind;
                this.targetFilter = targetFilter;
                this.spawnAnchor = spawnAnchor;
                this.prefabs = prefabs ?? Array.Empty<GameObject>();
                this.sourceRef = sourceRef ?? string.Empty;
                this.matchSourceRefPrefix = matchSourceRefPrefix;
                this.matchStatusKind = matchStatusKind;
                this.scaleMultiplier = scaleMultiplier;
                this.scaleWithRadius = scaleWithRadius;
                this.positionOffset = positionOffset;
                this.rotationEulerOffset = rotationEulerOffset;
                this.lifetimeOverride = lifetimeOverride;
                this.playbackDelaySeconds = Mathf.Max(0f, playbackDelaySeconds);
                this.playbackSpeed = playbackSpeed;
                this.axisScaleMultiplier = axisScaleMultiplier;
                this.floatingTextMode = floatingTextMode;
                this.floatingTextOverride = floatingTextOverride ?? string.Empty;
                this.loop = loop;
                this.attachToActor = attachToActor;
                this.statusKind = statusKind;
                this.loopAnchor = loopAnchor;
            }

            public string CueId => cueId ?? string.Empty;
            public string DisplayName => displayName ?? string.Empty;
            public string Category => category ?? string.Empty;
            public string[] Tags => tags ?? Array.Empty<string>();
            public string DesignerNote => designerNote ?? string.Empty;
            public bool Deprecated => deprecated;
            public EffectKind Kind => kind;
            public EffectVfxTargetFilter TargetFilter => targetFilter;
            public EffectVfxSpawnAnchor SpawnAnchor => spawnAnchor;
            public EffectVfxAreaSpawnMode AreaSpawnMode => areaSpawnMode;
            public float PerTileDelaySeconds => Mathf.Max(0f, perTileDelaySeconds);
            public bool FollowSourceAnchor => followSourceAnchor;
            public string SourceRef => sourceRef ?? string.Empty;
            public string SourceCardId => sourceCardId ?? string.Empty;
            public bool MatchSourceRefPrefix => matchSourceRefPrefix;
            public bool MatchStatusKind => matchStatusKind;
            public GameObject[] Prefabs => prefabs ?? Array.Empty<GameObject>();
            public float ScaleMultiplier => scaleMultiplier <= 0f ? 1f : scaleMultiplier;
            public bool ScaleWithRadius => scaleWithRadius;
            public Vector3 PositionOffset => positionOffset;
            public Quaternion RotationOffset => Quaternion.Euler(rotationEulerOffset);
            public float LifetimeOverride => Mathf.Max(0f, lifetimeOverride);
            public float PlaybackDelaySeconds => Mathf.Max(0f, playbackDelaySeconds);
            public float PlaybackSpeed => playbackSpeed <= 0f ? 1f : playbackSpeed;

            /// <summary>
            /// Per-axis scale with each unset (&lt;= 0) component normalized to 1, so entries serialized
            /// before this field existed (deserialized as zero) behave as uniform.
            /// </summary>
            public Vector3 AxisScaleMultiplier => new Vector3(
                axisScaleMultiplier.x <= 0f ? 1f : axisScaleMultiplier.x,
                axisScaleMultiplier.y <= 0f ? 1f : axisScaleMultiplier.y,
                axisScaleMultiplier.z <= 0f ? 1f : axisScaleMultiplier.z);
            public EffectFloatingTextMode FloatingTextMode => floatingTextMode;
            public string FloatingTextOverride => floatingTextOverride ?? string.Empty;
            public bool Loop => loop;
            public bool AttachToActor => attachToActor;
            public StatusEffectKind StatusKind => statusKind;
            public CharacterVfxAnchorKind LoopAnchor => loopAnchor;
            public bool HasSourceRefRule => !string.IsNullOrWhiteSpace(SourceRef);
            public bool HasSourceCardIdRule => !string.IsNullOrWhiteSpace(SourceCardId);

            /// <summary>Baked natural length at speed 1; 0 when unmeasured. Tool output — see <see cref="Duration"/>.</summary>
            public float MeasuredLengthSeconds => Mathf.Max(0f, measuredLengthSeconds);

            /// <summary>Authored on-screen duration; 0 = unset. The only hand-edited half of the pair.</summary>
            public float AuthoredLengthSeconds => Mathf.Max(0f, authoredLengthSeconds);

            /// <summary>
            /// How long this cue plays, as a queryable contract. Loop cues are reported as unmeasurable —
            /// they live until the status is removed, so no measured number could be true for them and only
            /// <see cref="AuthoredLengthSeconds"/> can answer.
            ///
            /// Nothing in the shipping presentation reads this yet: the track keeps the data informational
            /// until a consumer opts in, so adding it cannot retime anything (plan D6).
            /// </summary>
            public PresentationDuration Duration => PresentationDuration.Create(
                MeasuredLengthSeconds,
                AuthoredLengthSeconds,
                measurable: !loop,
                clock: PresentationClock.Scaled,
                // Loop cues pass 0: PlayLoop keeps the instance alive until the status is removed and never
                // consults lifetimeOverride, so for them the field is not a cut-off at all. One-shots go
                // through Play, where ResolvePrefabLifetime does honor it.
                destroyAfterSeconds: loop ? 0f : LifetimeOverride);

            /// <summary>
            /// Top tier: the event names this card as its source. Effects that fire detached from the card
            /// that caused them (a field object ticking each turn, a post-action status) carry a shared
            /// behaviour ref in <see cref="EffectResultEvent.SourceRef"/> but still name the card in
            /// <see cref="EffectResultEvent.SourceCardId"/>, so this tier is what lets one card override a
            /// look authored on the shared ref. When this entry also has a sourceRef rule that rule must
            /// hold too, which keeps an override scoped to one effect of the card instead of all of them.
            /// </summary>
            public bool MatchesExactSourceCardId(EffectResultEvent resultEvent)
            {
                if (loop ||
                    !HasSourceCardIdRule ||
                    !HasPrefab ||
                    kind != resultEvent.Kind ||
                    !MatchesStatusKindGate(resultEvent) ||
                    string.IsNullOrWhiteSpace(resultEvent.SourceCardId) ||
                    !MatchesTarget(resultEvent.TargetUnitId))
                {
                    return false;
                }

                if (!string.Equals(resultEvent.SourceCardId, SourceCardId, StringComparison.Ordinal))
                {
                    return false;
                }

                if (!HasSourceRefRule)
                {
                    return true;
                }

                return matchSourceRefPrefix
                    ? !string.IsNullOrWhiteSpace(resultEvent.SourceRef)
                        && resultEvent.SourceRef.StartsWith(SourceRef, StringComparison.Ordinal)
                    : string.Equals(resultEvent.SourceRef, SourceRef, StringComparison.Ordinal);
            }

            public bool MatchesStatusLoop(StatusEffectKind requestedStatusKind, EffectVfxTargetFilter requestedTarget)
            {
                if (!loop || !HasPrefab || statusKind != requestedStatusKind)
                {
                    return false;
                }

                return targetFilter == EffectVfxTargetFilter.Any
                    || requestedTarget == EffectVfxTargetFilter.Any
                    || targetFilter == requestedTarget;
            }

            // A card-scoped entry is reachable only from the top tier: without this exclusion an entry keyed on
            // sourceCardId alone would also read as a generic kind-tier cue and fire for every other card.
            public bool MatchesKind(EffectResultEvent resultEvent)
            {
                return !loop && !matchStatusKind && !HasSourceRefRule && !HasSourceCardIdRule && kind == resultEvent.Kind && HasPrefab && MatchesTarget(resultEvent.TargetUnitId);
            }

            // Status-apply tier: select a one-shot cue purely by the event's StatusKind (no sourceRef rule).
            // Sits between the sourceRef tiers and the plain kind tier in TryResolve / ResolveAll.
            public bool MatchesStatusKindApply(EffectResultEvent resultEvent)
            {
                return !loop
                    && matchStatusKind
                    && !HasSourceRefRule
                    && !HasSourceCardIdRule
                    && kind == EffectKind.StatusEffectApplied
                    && resultEvent.Kind == EffectKind.StatusEffectApplied
                    && resultEvent.StatusKind.HasValue
                    && resultEvent.StatusKind.Value == statusKind
                    && HasPrefab
                    && MatchesTarget(resultEvent.TargetUnitId);
            }

            public bool MatchesExactSourceRef(EffectResultEvent resultEvent)
            {
                if (loop ||
                    HasSourceCardIdRule ||
                    matchSourceRefPrefix ||
                    !MatchesEventKind(resultEvent) ||
                    !MatchesStatusKindGate(resultEvent) ||
                    !CanMatchSourceRef(resultEvent.SourceRef) ||
                    !MatchesTarget(resultEvent.TargetUnitId))
                {
                    return false;
                }

                return string.Equals(resultEvent.SourceRef, SourceRef, StringComparison.Ordinal);
            }

            public bool MatchesSourceRefPrefix(EffectResultEvent resultEvent)
            {
                if (loop ||
                    HasSourceCardIdRule ||
                    !matchSourceRefPrefix ||
                    !MatchesEventKind(resultEvent) ||
                    !MatchesStatusKindGate(resultEvent) ||
                    !CanMatchSourceRef(resultEvent.SourceRef) ||
                    !MatchesTarget(resultEvent.TargetUnitId))
                {
                    return false;
                }

                return resultEvent.SourceRef.StartsWith(SourceRef, StringComparison.Ordinal);
            }

            /// <summary>
            /// sourceRef 티어의 종류 판정. <b>피해가 막혔어도 공격은 일어났다</b>(2026-09-05 실플레이 피드백:
            /// "플레이어가 방어했을 때에도 몬스터 공격 VFX가 재생되도록 하기").
            ///
            /// <para>🔴 방어로 전부 흡수되면 규칙층은 <c>Damage</c> 대신 <c>DamageBlocked</c>를 올린다.
            /// 그런데 몬스터 패턴 큐는 <b>전부</b> <c>Damage</c>로 저작돼 있어(56종 전부) 어떤 큐도 안 걸리고
            /// 화면이 조용해졌다 — 감사의 <c>uncoveredEffectKinds</c>에도 <c>DamageBlocked</c>가 잡혀 있었다.</para>
            ///
            /// <para>🔑 큐를 50여 개 새로 저작하는 대신 <b>같은 공격의 두 결과</b>로 본다: 공격 연출은
            /// 「때렸다」의 그림이지 「피해가 들어갔다」의 그림이 아니다. 종류 티어(<see cref="MatchesKind"/>)는
            /// 그대로 엄격하게 둔다 — 범용 Damage 큐까지 방어에 튀어나오면 그건 다른 이야기가 된다.</para>
            /// </summary>
            private bool MatchesEventKind(EffectResultEvent resultEvent)
            {
                return kind == resultEvent.Kind
                    || (kind == EffectKind.Damage && resultEvent.Kind == EffectKind.DamageBlocked);
            }

            // A sourceRef-tier entry that also opts into matchStatusKind only matches events whose StatusKind
            // equals this entry's statusKind (and only for status-apply events). Non-matchStatusKind entries
            // pass through unconditionally.
            private bool MatchesStatusKindGate(EffectResultEvent resultEvent)
            {
                if (!matchStatusKind)
                {
                    return true;
                }

                return resultEvent.Kind == EffectKind.StatusEffectApplied
                    && resultEvent.StatusKind.HasValue
                    && resultEvent.StatusKind.Value == statusKind;
            }

            public bool MatchesSourceRef(string candidateSourceRef)
            {
                var resultEvent = new EffectResultEvent(kind, sourceRef: candidateSourceRef);
                return MatchesExactSourceRef(resultEvent) || MatchesSourceRefPrefix(resultEvent);
            }

            private bool CanMatchSourceRef(string candidateSourceRef)
            {
                return HasSourceRefRule && HasPrefab && !string.IsNullOrWhiteSpace(candidateSourceRef);
            }

            private bool MatchesTarget(string targetUnitId)
            {
                switch (targetFilter)
                {
                    case EffectVfxTargetFilter.Player:
                        return IsPlayerTarget(targetUnitId);
                    case EffectVfxTargetFilter.Monster:
                        return !string.IsNullOrWhiteSpace(targetUnitId) &&
                               !IsPlayerTarget(targetUnitId) &&
                               !IsFieldTarget(targetUnitId);
                    case EffectVfxTargetFilter.Field:
                        return IsFieldTarget(targetUnitId);
                    default:
                        return true;
                }
            }

            private static bool IsPlayerTarget(string targetUnitId)
            {
                return string.Equals(targetUnitId, "player", StringComparison.OrdinalIgnoreCase);
            }

            private static bool IsFieldTarget(string targetUnitId)
            {
                return string.Equals(targetUnitId, "field", StringComparison.OrdinalIgnoreCase);
            }

            private bool HasPrefab
            {
                get
                {
                    foreach (var prefab in Prefabs)
                    {
                        if (prefab != null)
                        {
                            return true;
                        }
                    }

                    return false;
                }
            }
        }
    }
}

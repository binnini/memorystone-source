// NOTE(공개 발췌): 이 파일은 서드파티 에셋 「Cartoon FX Remaster (JMO Assets)」의 프리팹·타입을 경로/이름으로만 참조한다. 해당 에셋은 이 리포에 포함되지 않는다.
using System.Collections;
using System.Collections.Generic;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Runtime.Presentation;
using SeoulPlayup.Map.Runtime;
using TMPro;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SeoulPlayup.Combat.Unity
{
    public sealed class EffectPresentationController : MonoBehaviour
    {
        private const string DefaultEffectVfxCatalogResourcePath = "Combat/DefaultEffectVfxCatalog";
        private const string OverlayTextShaderName = "TextMeshPro/Distance Field Overlay";
        private static readonly int ZTestModeProperty = Shader.PropertyToID("_ZTestMode");

        [SerializeField] private Transform effectRoot;
        [SerializeField] private EffectVfxCatalog vfxCatalog;
        [SerializeField] private float particleLifetime = 1.2f;
        [SerializeField] private float floatingTextHeight = 1.4f;
        [SerializeField] private float floatingTextFontSize = 7f;
        [SerializeField] private TMP_FontAsset floatingTextFont;
        [Header("Floating Number Motion (MapleStory-style)")]
        [SerializeField] private float floatingTextLifetime = 1.1f;
        [SerializeField] private float floatingTextRiseHeight = 0.9f;
        [SerializeField] private float hexSpacing = 1.15f;

        private readonly Dictionary<EffectKind, Material> particleMaterials = new Dictionary<EffectKind, Material>();
        private readonly List<GameObject> spawnedEffects = new List<GameObject>();
        private readonly List<GameObject> loopingEffects = new List<GameObject>();
        private readonly List<Coroutine> pendingEffectRoutines = new List<Coroutine>();

        public IReadOnlyList<GameObject> SpawnedEffects => spawnedEffects;
        public IReadOnlyList<GameObject> LoopingEffects => loopingEffects;
        public EffectVfxCatalog VfxCatalog => ResolveVfxCatalog();

        /// <summary>
        /// Fired at the exact frame an event-driven floating number/text spawns (after any authored
        /// or event-carried delay). This is the presentation moment of the underlying effect, so
        /// listeners that must move in lockstep with the number — e.g. MonsterHealthBarView draining
        /// a health bar per hit — key off this instead of the earlier simulation-time event.
        /// </summary>
        public static event System.Action<EffectResultEvent> FloatingTextPresented;

        /// <summary>
        /// Dev-only (trailer filming): suppresses the visual floating-text objects while leaving
        /// <see cref="FloatingTextPresented"/> firing, so listeners that sync off the impact stamp (monster
        /// health bars) stay correct. Floating text is a world-space object, which is exactly why the
        /// canvas-alpha UI hide cannot catch it. Set/cleared by TrailerShotRunner's filming state only.
        /// </summary>
        public static bool DebugSuppressFloatingText { get; set; }

        public void SetVfxCatalog(EffectVfxCatalog catalog)
        {
            vfxCatalog = catalog;
        }

#if UNITY_EDITOR
        public void SetVfxCatalogForTests(EffectVfxCatalog catalog)
        {
            SetVfxCatalog(catalog);
        }
#endif

        private void OnDestroy()
        {
            StopPendingEffectRoutines();
            StopAllLoops(immediate: true);
            foreach (var material in particleMaterials.Values)
            {
                DestroyObjectSafely(material);
            }

            particleMaterials.Clear();
        }

        public void Play(EffectResultEvent resultEvent)
        {
            var position = resultEvent.Center.HasValue
                ? HexToWorld(resultEvent.Center.Value)
                : transform.position;
            Play(resultEvent, position);
        }

        public void Play(EffectResultEvent resultEvent, Vector3 worldPosition)
        {
            Play(resultEvent, worldPosition, Quaternion.identity);
        }

        /// <param name="sourceFollowAnchor">Optional source-actor anchor transform. Entries authored with
        /// followSourceAnchor track its position/rotation delta every frame after spawning; null (the
        /// default) keeps the fixed one-shot placement for every entry.</param>
        public void Play(EffectResultEvent resultEvent, Vector3 worldPosition, Quaternion facingRotation, Transform sourceFollowAnchor = null)
        {
            EnsureRoot();

            var catalog = ResolveVfxCatalog();
            var entries = catalog != null ? catalog.ResolveAll(resultEvent) : System.Array.Empty<EffectVfxCatalog.Entry>();
            var entry = entries.Length > 0 ? entries[0] : null;

            if (!SpawnCatalogPrefabs(entries, resultEvent, worldPosition, facingRotation, sourceFollowAnchor)
                && ShouldSpawnPlaceholderParticle(resultEvent))
            {
                SpawnParticle(resultEvent.Kind, worldPosition, resultEvent.Radius);
            }

            // The floating-text policy is owned by the resolved catalog entry, so it applies
            // identically whether we spawned authored prefabs or fell back to the placeholder particle.
            if (ShouldShowFloatingText(entry, resultEvent))
            {
                SpawnFloatingText(entry, resultEvent, worldPosition + Vector3.up * floatingTextHeight, entry != null ? entry.PlaybackDelaySeconds : 0f);
            }
        }

        public void PlayFloatingText(string text, Vector3 worldPosition, Color color, float delaySeconds = 0f)
        {
            EnsureRoot();
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var spawnPosition = worldPosition + Vector3.up * floatingTextHeight;
            if (delaySeconds > 0f && isActiveAndEnabled)
            {
                pendingEffectRoutines.Add(StartCoroutine(SpawnFloatingTextAfterDelay(text, color, spawnPosition, delaySeconds)));
                return;
            }

            SpawnFloatingTextNow(text, color, spawnPosition);
        }

        /// <summary>
        /// One-shot variant of <see cref="Play"/> whose spawned cues track a live anchor world position
        /// (re-evaluated every frame via <paramref name="positionProvider"/>) instead of being pinned to a
        /// fixed point. Used for the player move dust, which must stay under the marker's feet while it
        /// animates from tile to tile. Offsets/rotation/scale are applied in <paramref name="facingRotation"/>
        /// space exactly like <see cref="Play"/>, and each instance auto-destroys on the same lifetime timer
        /// as a fixed one-shot. Reuses the shear-safe follower pivot from the looping path.
        /// </summary>
        public void PlayFollowing(EffectResultEvent resultEvent, System.Func<Vector3> positionProvider, Quaternion facingRotation)
        {
            if (positionProvider == null)
            {
                return;
            }

            EnsureRoot();

            var catalog = ResolveVfxCatalog();
            var entries = catalog != null ? catalog.ResolveAll(resultEvent) : System.Array.Empty<EffectVfxCatalog.Entry>();
            var entry = entries.Length > 0 ? entries[0] : null;

            if (!SpawnFollowingCatalogPrefabs(entries, resultEvent, positionProvider, facingRotation))
            {
                SpawnParticle(resultEvent.Kind, positionProvider(), resultEvent.Radius);
            }

            if (ShouldShowFloatingText(entry, resultEvent))
            {
                SpawnFloatingText(entry, resultEvent, positionProvider() + Vector3.up * floatingTextHeight, entry != null ? entry.PlaybackDelaySeconds : 0f);
            }
        }

        /// <summary>
        /// Live handle to a persistent following cue started by <see cref="PlayFollowingLoop"/>. The caller
        /// owns pacing: update the facing while the tracked motion continues, then <see cref="StopFollowingLoop"/>
        /// to wind the emitter down. <see cref="IsAlive"/> goes false once stopped (or swept by
        /// <see cref="ClearSpawnedEffects"/>).
        /// </summary>
        public sealed class FollowingLoopHandle
        {
            internal GameObject Root;
            internal Quaternion RotationOffset;
            internal Vector3 PositionOffset;
            internal FollowingLoopRotator Rotator;
            internal StatusLoopFollower Follower;
            public bool IsAlive => Root != null;
        }

        /// <summary>
        /// Persistent variant of <see cref="PlayFollowing"/>: spawns the resolved cue once as a looping
        /// emitter that tracks the live anchor, and keeps it running until <see cref="StopFollowingLoop"/>.
        /// Fade-in is the particle system's natural build-up (no prewarm on purpose). Returns null when no
        /// catalog cue with a prefab resolves — the loop path spawns no placeholder, because a permanent
        /// placeholder emitter has no natural end. Floating text is skipped: a continuous emitter has no
        /// single landing moment to stamp a number on.
        /// </summary>
        public FollowingLoopHandle PlayFollowingLoop(EffectResultEvent resultEvent, System.Func<Vector3> positionProvider, Quaternion facingRotation)
        {
            if (positionProvider == null)
            {
                return null;
            }

            EnsureRoot();

            var catalog = ResolveVfxCatalog();
            var entries = catalog != null ? catalog.ResolveAll(resultEvent) : System.Array.Empty<EffectVfxCatalog.Entry>();
            EffectVfxCatalog.Entry entry = null;
            GameObject prefab = null;
            foreach (var candidate in entries)
            {
                if (candidate == null)
                {
                    continue;
                }

                foreach (var candidatePrefab in candidate.Prefabs)
                {
                    if (candidatePrefab != null)
                    {
                        entry = candidate;
                        prefab = candidatePrefab;
                        break;
                    }
                }

                if (prefab != null)
                {
                    break;
                }
            }

            if (entry == null || prefab == null)
            {
                return null;
            }

            CombatPresentationTrace.Record(
                CombatTraceChannel.Vfx,
                string.IsNullOrEmpty(entry.CueId) ? prefab.name : entry.CueId,
                $"{resultEvent.Kind} → {resultEvent.TargetUnitId} following-loop");

            var pivot = new GameObject($"VFX {resultEvent.Kind} Loop ({prefab.name})");
            pivot.transform.SetParent(effectRoot, worldPositionStays: true);
            pivot.transform.rotation = facingRotation * entry.RotationOffset;
            pivot.transform.localScale = Vector3.one * ResolveVfxScale(resultEvent, entry);
            var axisScale = entry.AxisScaleMultiplier;
            var prefabHost = axisScale == Vector3.one ? pivot.transform : CreateAxisScalePivot(pivot.transform, axisScale);
            Instantiate(prefab, prefabHost);
            var follower = pivot.AddComponent<StatusLoopFollower>();
            follower.Configure(positionProvider, facingRotation * entry.PositionOffset);
            var rotator = pivot.AddComponent<FollowingLoopRotator>();
            rotator.Snap(facingRotation * entry.RotationOffset);
            // A following loop exists to trail a moving anchor, so its particles must simulate in world
            // space: in the prefab's authored Local space every live particle rides along with the emitter,
            // which visually overrides the authored emission direction whenever the anchor moves faster
            // than the particles (the move dust looked like it sprayed forward for exactly that reason).
            // World space leaves each puff where it was emitted and the anchor walks away from it.
            foreach (var particle in pivot.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = particle.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
            }

            DisableRenderersWithMissingMaterials(pivot);
            PlayPrefabParticles(pivot);
            ApplyPlaybackSpeed(pivot, entry.PlaybackSpeed);
            spawnedEffects.Add(pivot);
            return new FollowingLoopHandle
            {
                Root = pivot,
                RotationOffset = entry.RotationOffset,
                PositionOffset = entry.PositionOffset,
                Rotator = rotator,
                Follower = follower
            };
        }

        /// <summary>
        /// Retargets a live following loop's facing; the pivot slews there smoothly instead of snapping.
        /// The authored position offset is facing-relative (a -Z offset keeps the emitter behind the actor),
        /// so it is re-aimed along with the rotation.
        /// </summary>
        public void SetFollowingLoopFacing(FollowingLoopHandle handle, Quaternion facingRotation)
        {
            if (handle == null || handle.Root == null)
            {
                return;
            }

            if (handle.Rotator != null)
            {
                handle.Rotator.SetTarget(facingRotation * handle.RotationOffset);
            }
            else
            {
                handle.Root.transform.rotation = facingRotation * handle.RotationOffset;
            }

            if (handle.Follower != null)
            {
                handle.Follower.SetWorldOffset(facingRotation * handle.PositionOffset);
            }
        }

        /// <summary>
        /// Halts a following loop's emission without ending it. Paired with
        /// <see cref="ResumeFollowingLoopEmission"/> so a caller that only *might* be done (a move step
        /// ended, the next may follow within a frame) can cut new particles instantly — otherwise the
        /// emitter keeps puffing next to an actor that has already stopped — and still resume seamlessly.
        /// </summary>
        public void PauseFollowingLoopEmission(FollowingLoopHandle handle)
        {
            if (handle == null || handle.Root == null)
            {
                return;
            }

            foreach (var particle in handle.Root.GetComponentsInChildren<ParticleSystem>(true))
            {
                particle.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        /// <summary>Resumes emission paused by <see cref="PauseFollowingLoopEmission"/>; live particles are kept, not cleared.</summary>
        public void ResumeFollowingLoopEmission(FollowingLoopHandle handle)
        {
            if (handle == null || handle.Root == null)
            {
                return;
            }

            foreach (var particle in handle.Root.GetComponentsInChildren<ParticleSystem>(true))
            {
                particle.Play(true);
            }
        }

        /// <summary>
        /// Winds a following loop down: stops emission so live particles dissipate over their remaining
        /// lifetime (the quick fade-out), then destroys the root after that tail. The handle goes dead
        /// immediately — a caller that needs dust again starts a fresh loop while the old tail fades.
        /// </summary>
        public void StopFollowingLoop(FollowingLoopHandle handle)
        {
            if (handle == null || handle.Root == null)
            {
                return;
            }

            var root = handle.Root;
            handle.Root = null;
            handle.Rotator = null;
            var tailSeconds = 0.5f;
            foreach (var particle in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                particle.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                var main = particle.main;
                var speed = Mathf.Max(0.01f, main.simulationSpeed);
                tailSeconds = Mathf.Max(tailSeconds, main.startLifetime.constantMax / speed);
            }

            ScheduleDestroyObject(root, tailSeconds + 0.25f);
        }

        public void PlayResolvedEntry(
            EffectResultEvent resultEvent,
            EffectVfxCatalog.Entry entry,
            Vector3 worldPosition,
            Quaternion facingRotation,
            bool showFloatingText = true,
            Transform sourceFollowAnchor = null)
        {
            EnsureRoot();

            if (!SpawnCatalogPrefabs(entry, resultEvent, worldPosition, facingRotation, sourceFollowAnchor)
                && ShouldSpawnPlaceholderParticle(resultEvent))
            {
                SpawnParticle(resultEvent.Kind, worldPosition, resultEvent.Radius);
            }

            if (showFloatingText && ShouldShowFloatingText(entry, resultEvent))
            {
                SpawnFloatingText(entry, resultEvent, worldPosition + Vector3.up * floatingTextHeight, entry != null ? entry.PlaybackDelaySeconds : 0f);
            }
        }

        /// <summary>
        /// Resolves whether a floating text number should be spawned for this effect, honoring the
        /// per-card/per-VFX policy authored on the catalog entry. Entries with no catalog match (placeholder
        /// particles) fall through to <see cref="EffectFloatingTextMode.Auto"/>.
        /// </summary>
        public static bool ShouldShowFloatingText(EffectVfxCatalog.Entry entry, EffectResultEvent resultEvent)
        {
            if (resultEvent.Kind == EffectKind.Block && resultEvent.AppliedAmount <= 0)
            {
                // 보호구역 안에서 도발(D02)·부적 방패(D05) grant no block — they nullify incoming damage. Show
                // their dedicated "피해 면역" feedback instead of staying silent; every other zero-block hides.
                return CombatEffectSourceClassifier.IsDamageImmunitySource(resultEvent.SourceRef);
            }

            // Field-zone placement VFX (폭탄 투하/섬광 등) are raised on the field itself ("field" target) with no
            // amount, purely to show the effect appearing over its footprint — they carry no number. Real
            // field-zone numbers (e.g. field damage) and the reveal cue's "밝혀짐!" still show.
            if (resultEvent.AppliedAmount <= 0
                && string.Equals(resultEvent.TargetUnitId, "field", System.StringComparison.Ordinal)
                && resultEvent.Kind != EffectKind.FogReveal
                && !HasAuthoredFieldPlacementText(entry))
            {
                return false;
            }

            // Heal (체력 회복) feedback is always meaningful and must never silently disappear: a mis-authored
            // catalog entry must not be able to hide the "+N" number.
            if (resultEvent.Kind == EffectKind.Heal && resultEvent.AppliedAmount > 0)
            {
                return true;
            }

            var mode = entry != null ? entry.FloatingTextMode : EffectFloatingTextMode.Auto;
            switch (mode)
            {
                case EffectFloatingTextMode.Hide:
                    return false;
                case EffectFloatingTextMode.Show:
                    return true;
                default:
                    return ResolveAutoFloatingText(resultEvent);
            }
        }

        private static bool HasAuthoredFieldPlacementText(EffectVfxCatalog.Entry entry)
        {
            return entry != null
                && entry.FloatingTextMode == EffectFloatingTextMode.Show
                && !string.IsNullOrWhiteSpace(entry.FloatingTextOverride);
        }

        // Safe default: keep showing damage/heal/status numbers (preserves prior behavior) but suppress
        // movement/push text, which would otherwise spam every tile step. Movement cards still set Hide
        // explicitly; this only covers entries that leave the policy on Auto.
        private static bool ResolveAutoFloatingText(EffectResultEvent resultEvent)
        {
            if (resultEvent.Kind == EffectKind.Push || EffectVfxAnchorPolicy.IsMoveLike(resultEvent))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Play an area effect over its footprint. Entries authored with
        /// <see cref="EffectVfxAreaSpawnMode.PerTile"/> spawn one instance per
        /// <paramref name="tileWorldPositions"/> tile (optionally staggered by the entry's
        /// perTileDelaySeconds, in tile order); every other entry keeps the single center burst at
        /// <paramref name="centerWorld"/> — with no PerTile entry this is exactly <see cref="Play"/>.
        /// The tile flash itself lives in <see cref="CombatTileFlashPresenter"/> (driven by the
        /// presentation bridge), not here.
        /// </summary>
        public void PlayArea(EffectResultEvent resultEvent, Vector3 centerWorld, IReadOnlyList<Vector3> tileWorldPositions)
        {
            PlayArea(resultEvent, centerWorld, tileWorldPositions, Quaternion.identity);
        }

        public void PlayArea(EffectResultEvent resultEvent, Vector3 centerWorld, IReadOnlyList<Vector3> tileWorldPositions, Quaternion facingRotation, Transform sourceFollowAnchor = null)
        {
            EnsureRoot();

            var catalog = ResolveVfxCatalog();
            var entries = catalog != null ? catalog.ResolveAll(resultEvent) : System.Array.Empty<EffectVfxCatalog.Entry>();
            if (!HasPerTileAreaEntry(entries) || tileWorldPositions == null || tileWorldPositions.Count == 0)
            {
                Play(resultEvent, centerWorld, facingRotation, sourceFollowAnchor);
                return;
            }

            var entry = entries[0];
            foreach (var candidate in entries)
            {
                if (candidate == null)
                {
                    continue;
                }

                if (candidate.AreaSpawnMode == EffectVfxAreaSpawnMode.PerTile)
                {
                    // PerTile instances are tile-pinned by definition and never follow the source anchor.
                    SpawnPerTileCatalogPrefabs(candidate, resultEvent, tileWorldPositions, facingRotation);
                }
                else
                {
                    SpawnCatalogPrefabs(candidate, resultEvent, centerWorld, facingRotation, sourceFollowAnchor);
                }
            }

            if (ShouldShowFloatingText(entry, resultEvent))
            {
                SpawnFloatingText(entry, resultEvent, centerWorld + Vector3.up * floatingTextHeight, entry != null ? entry.PlaybackDelaySeconds : 0f);
            }
        }

        /// <summary>True when any resolved entry opts into per-tile area spawning; the bridge uses this to route area events through <see cref="PlayArea"/>.</summary>
        public static bool HasPerTileAreaEntry(IReadOnlyList<EffectVfxCatalog.Entry> entries)
        {
            if (entries == null)
            {
                return false;
            }

            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null && entries[i].AreaSpawnMode == EffectVfxAreaSpawnMode.PerTile)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Spawn delay for the tile at <paramref name="tileIndex"/>: the cue's authored delay plus the per-tile stagger.</summary>
        public static float ResolvePerTileSpawnDelay(EffectVfxCatalog.Entry entry, int tileIndex)
        {
            if (entry == null)
            {
                return 0f;
            }

            return entry.PlaybackDelaySeconds + Mathf.Max(0, tileIndex) * entry.PerTileDelaySeconds;
        }

        private void SpawnPerTileCatalogPrefabs(
            EffectVfxCatalog.Entry entry,
            EffectResultEvent resultEvent,
            IReadOnlyList<Vector3> tileWorldPositions,
            Quaternion facingRotation)
        {
            foreach (var prefab in entry.Prefabs)
            {
                if (prefab == null)
                {
                    continue;
                }

                for (var i = 0; i < tileWorldPositions.Count; i++)
                {
                    var delay = ResolvePerTileSpawnDelay(entry, i);
                    if (delay > 0f && isActiveAndEnabled)
                    {
                        pendingEffectRoutines.Add(StartCoroutine(SpawnCatalogPrefabAfterDelay(prefab, entry, resultEvent, tileWorldPositions[i], facingRotation, delay)));
                    }
                    else
                    {
                        SpawnCatalogPrefab(prefab, entry, resultEvent, tileWorldPositions[i], facingRotation);
                    }
                }
            }
        }

        private bool SpawnCatalogPrefabs(EffectVfxCatalog.Entry entry, EffectResultEvent resultEvent, Vector3 worldPosition, Quaternion facingRotation, Transform sourceFollowAnchor = null)
        {
            return SpawnCatalogPrefabs(entry == null ? System.Array.Empty<EffectVfxCatalog.Entry>() : new[] { entry }, resultEvent, worldPosition, facingRotation, sourceFollowAnchor);
        }

        private bool SpawnCatalogPrefabs(IReadOnlyList<EffectVfxCatalog.Entry> entries, EffectResultEvent resultEvent, Vector3 worldPosition, Quaternion facingRotation, Transform sourceFollowAnchor = null)
        {
            if (entries == null || entries.Count == 0)
            {
                return false;
            }

            var spawned = false;
            foreach (var entry in entries)
            {
                if (entry == null)
                {
                    continue;
                }

                foreach (var prefab in entry.Prefabs)
                {
                    if (prefab == null)
                    {
                        continue;
                    }

                    if (entry.PlaybackDelaySeconds > 0f && isActiveAndEnabled)
                    {
                        pendingEffectRoutines.Add(StartCoroutine(SpawnCatalogPrefabAfterDelay(prefab, entry, resultEvent, worldPosition, facingRotation, entry.PlaybackDelaySeconds, sourceFollowAnchor)));
                    }
                    else
                    {
                        SpawnCatalogPrefab(prefab, entry, resultEvent, worldPosition, facingRotation, sourceFollowAnchor);
                    }

                    spawned = true;
                }
            }

            return spawned;
        }

        private IEnumerator SpawnCatalogPrefabAfterDelay(
            GameObject prefab,
            EffectVfxCatalog.Entry entry,
            EffectResultEvent resultEvent,
            Vector3 worldPosition,
            Quaternion facingRotation,
            float delaySeconds,
            Transform sourceFollowAnchor = null)
        {
            yield return new WaitForSeconds(Mathf.Max(0f, delaySeconds));
            pendingEffectRoutines.RemoveAll(routine => routine == null);
            if (this == null || prefab == null || entry == null)
            {
                yield break;
            }

            EnsureRoot();
            SpawnCatalogPrefab(prefab, entry, resultEvent, worldPosition, facingRotation, sourceFollowAnchor);
        }

        private void SpawnCatalogPrefab(
            GameObject prefab,
            EffectVfxCatalog.Entry entry,
            EffectResultEvent resultEvent,
            Vector3 worldPosition,
            Quaternion facingRotation,
            Transform sourceFollowAnchor = null)
        {
            if (prefab == null || entry == null)
            {
                return;
            }

            // Stamped here, not where the spawn was requested: this is after the cue's authored delay, so the
            // trace shows when the VFX actually appeared on screen.
            CombatPresentationTrace.Record(
                CombatTraceChannel.Vfx,
                string.IsNullOrEmpty(entry.CueId) ? prefab.name : entry.CueId,
                $"{resultEvent.Kind} → {resultEvent.TargetUnitId} delay={entry.PlaybackDelaySeconds:0.###}");

            var pose = ResolveSpawnPose(entry, resultEvent, worldPosition, facingRotation);

            GameObject go;
            if (pose.AxisScale == Vector3.one)
            {
                go = Instantiate(prefab, pose.Position, pose.Rotation, effectRoot);
                go.name = $"VFX {resultEvent.Kind} ({prefab.name})";
                go.transform.localScale = Vector3.Scale(go.transform.localScale, Vector3.one * pose.UniformScale);
            }
            else
            {
                // Shear-safe non-uniform scale: the rotated root keeps rotation + uniform scale only, and
                // the axis scale lives on an unrotated child pivot; the prefab is re-rooted beneath at the
                // same world pose the direct spawn above would have given it.
                go = new GameObject($"VFX {resultEvent.Kind} ({prefab.name})");
                go.transform.SetParent(effectRoot, false);
                go.transform.SetPositionAndRotation(pose.Position, pose.Rotation);
                go.transform.localScale = Vector3.one * pose.UniformScale;
                var instance = Instantiate(prefab, CreateAxisScalePivot(go.transform, pose.AxisScale));
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
            }
            DisableRenderersWithMissingMaterials(go);
            PlayPrefabParticles(go);
            ApplyPlaybackSpeed(go, entry.PlaybackSpeed);
            if (entry.FollowSourceAnchor && sourceFollowAnchor != null)
            {
                go.AddComponent<AnchorDeltaFollower>().Configure(sourceFollowAnchor);
            }

            spawnedEffects.Add(go);
            ScheduleDestroyObject(go, ResolvePrefabLifetime(go, entry));
        }

        private bool SpawnFollowingCatalogPrefabs(
            IReadOnlyList<EffectVfxCatalog.Entry> entries,
            EffectResultEvent resultEvent,
            System.Func<Vector3> positionProvider,
            Quaternion facingRotation)
        {
            if (entries == null || entries.Count == 0)
            {
                return false;
            }

            var spawned = false;
            foreach (var entry in entries)
            {
                if (entry == null)
                {
                    continue;
                }

                foreach (var prefab in entry.Prefabs)
                {
                    if (prefab == null)
                    {
                        continue;
                    }

                    if (entry.PlaybackDelaySeconds > 0f && isActiveAndEnabled)
                    {
                        pendingEffectRoutines.Add(StartCoroutine(SpawnFollowingCatalogPrefabAfterDelay(prefab, entry, resultEvent, positionProvider, facingRotation)));
                    }
                    else
                    {
                        SpawnFollowingCatalogPrefab(prefab, entry, resultEvent, positionProvider, facingRotation);
                    }

                    spawned = true;
                }
            }

            return spawned;
        }

        private IEnumerator SpawnFollowingCatalogPrefabAfterDelay(
            GameObject prefab,
            EffectVfxCatalog.Entry entry,
            EffectResultEvent resultEvent,
            System.Func<Vector3> positionProvider,
            Quaternion facingRotation)
        {
            yield return new WaitForSeconds(entry != null ? entry.PlaybackDelaySeconds : 0f);
            pendingEffectRoutines.RemoveAll(routine => routine == null);
            if (this == null || prefab == null || entry == null)
            {
                yield break;
            }

            EnsureRoot();
            SpawnFollowingCatalogPrefab(prefab, entry, resultEvent, positionProvider, facingRotation);
        }

        private void SpawnFollowingCatalogPrefab(
            GameObject prefab,
            EffectVfxCatalog.Entry entry,
            EffectResultEvent resultEvent,
            System.Func<Vector3> positionProvider,
            Quaternion facingRotation)
        {
            if (prefab == null || entry == null || positionProvider == null)
            {
                return;
            }

            // The follower pivot owns ONLY the world rotation + uniform scale and tracks the live anchor each
            // frame; the prefab is instantiated underneath with its authored local transform untouched. This is
            // the same shear-safe arrangement used by PlayLoop, so non-uniform prefab scales never twist. An
            // authored axis scale is inserted as an unrotated child pivot between the two for the same reason.
            // The facing-space position offset is re-applied as a constant world offset (facing is fixed per move).
            // Instrumented like SpawnCatalogPrefab: without this the follow path was invisible to the
            // presentation trace, so "no VFX line" could not be told apart from "no VFX spawned".
            CombatPresentationTrace.Record(
                CombatTraceChannel.Vfx,
                string.IsNullOrEmpty(entry.CueId) ? prefab.name : entry.CueId,
                $"{resultEvent.Kind} \u2192 {resultEvent.TargetUnitId} following delay={entry.PlaybackDelaySeconds:0.###}");

            var pivot = new GameObject($"VFX {resultEvent.Kind} ({prefab.name})");
            pivot.transform.SetParent(effectRoot, worldPositionStays: true);
            pivot.transform.rotation = facingRotation * entry.RotationOffset;
            pivot.transform.localScale = Vector3.one * ResolveVfxScale(resultEvent, entry);
            var axisScale = entry.AxisScaleMultiplier;
            var prefabHost = axisScale == Vector3.one ? pivot.transform : CreateAxisScalePivot(pivot.transform, axisScale);
            Instantiate(prefab, prefabHost);
            var follower = pivot.AddComponent<StatusLoopFollower>();
            follower.Configure(positionProvider, facingRotation * entry.PositionOffset);
            DisableRenderersWithMissingMaterials(pivot);
            PlayPrefabParticles(pivot);
            ApplyPlaybackSpeed(pivot, entry.PlaybackSpeed);
            spawnedEffects.Add(pivot);
            ScheduleDestroyObject(pivot, ResolvePrefabLifetime(pivot, entry));
        }

        private EffectVfxCatalog ResolveVfxCatalog()
        {
            if (vfxCatalog != null)
            {
                return vfxCatalog;
            }

            vfxCatalog = Resources.Load<EffectVfxCatalog>(DefaultEffectVfxCatalogResourcePath);
            return vfxCatalog;
        }

        private static float ResolveVfxScale(EffectResultEvent resultEvent, EffectVfxCatalog.Entry entry)
        {
            return ResolveRadiusScale(entry.ScaleWithRadius, resultEvent) * entry.ScaleMultiplier;
        }

        /// <summary>
        /// 큐 하나가 놓일 최종 월드 포즈(위치·회전·균일 스케일·축 스케일).
        ///
        /// <para>🔑 <b>배치 계산의 단일 출처다.</b> 런타임 스폰(<c>SpawnCatalogPrefab</c>)과 VFX 랩·
        /// 스틸 캡처가 전부 이 함수를 통과하므로, 랩에서 맞고 실게임에서 다른 상황이 <b>구조적으로</b>
        /// 생길 수 없다. 이 함수가 생기기 전 랩은 위치·회전·스케일을 따로 계산했고, 그게 바로
        /// 카메라 트랙에서 한 번 밟았던 "랩↔실게임 갭"의 재발 조건이었다.</para>
        ///
        /// <para>축 스케일이 <see cref="Vector3.one"/>이 아니면 회전과 비균일 스케일이 한 트랜스폼에
        /// 겹쳐 전단(shear)이 나므로, 호출부는 축 스케일을 <b>회전 없는 자식 피벗</b>에 걸어야 한다.</para>
        /// </summary>
        public static VfxSpawnPose ResolveSpawnPose(
            EffectVfxCatalog.Entry entry,
            EffectResultEvent resultEvent,
            Vector3 worldPosition,
            Quaternion facingRotation)
        {
            if (entry == null)
            {
                return new VfxSpawnPose(worldPosition, facingRotation, 1f, Vector3.one);
            }

            return new VfxSpawnPose(
                worldPosition + facingRotation * entry.PositionOffset,
                facingRotation * entry.RotationOffset,
                ResolveVfxScale(resultEvent, entry),
                entry.AxisScaleMultiplier);
        }

        /// <summary>
        /// Shared scaleWithRadius growth used by both the runtime presenter and the VFX lab so Play CSV
        /// matches Play Catalog. Field/area placements (sourceRef "field."/"area." or a "field" target)
        /// scale to fill their footprint: a radius-R hex disk spans (2R+1) tiles, so the effect diameter
        /// tracks the placed range. Other radius cues keep the gentler linear-in-radius growth.
        /// </summary>
        public static float ResolveRadiusScale(bool scaleWithRadius, EffectResultEvent resultEvent)
        {
            if (!scaleWithRadius)
            {
                return 1f;
            }

            var radius = resultEvent.Radius <= 0 ? 0 : resultEvent.Radius;
            if (EffectVfxAnchorPolicy.IsFieldLike(resultEvent))
            {
                return Mathf.Max(1f, 2f * radius + 1f);
            }

            return Mathf.Max(1f, radius <= 0 ? 1f : radius);
        }

        private static void PlayPrefabParticles(GameObject root)
        {
            if (UsesCartoonFxLifecycle(root))
            {
                ResetCartoonFxState(root);
                return;
            }

            PlayChildParticles(root);
        }

        private static bool UsesCartoonFxLifecycle(GameObject root)
        {
            var components = root.GetComponentsInChildren<Component>(true);
            foreach (var component in components)
            {
                if (component == null)
                {
                    continue;
                }

                var type = component.GetType();
                if (type.FullName == "CartoonFX.CFXR_Effect")
                {
                    return true;
                }
            }

            return false;
        }

        private static void ResetCartoonFxState(GameObject root)
        {
            var components = root.GetComponentsInChildren<Component>(true);
            foreach (var component in components)
            {
                if (component == null)
                {
                    continue;
                }

                var type = component.GetType();
                if (type.FullName != "CartoonFX.CFXR_Effect")
                {
                    continue;
                }

                var resetState = type.GetMethod("ResetState", System.Type.EmptyTypes);
                resetState?.Invoke(component, null);
            }
        }

        private static void PlayChildParticles(GameObject root)
        {
            var particles = root.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var particle in particles)
            {
                particle.gameObject.SetActive(true);
                particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                particle.Play(true);
            }
        }

        /// <summary>
        /// Child pivot carrying the cue's non-uniform axis scale. It sits unrotated between the rotated
        /// spawn root and the prefab, so the scale applies in the cue's facing-rotated axes without the
        /// root ever combining rotation and non-uniform scale in one transform (which would shear).
        /// </summary>
        private static Transform CreateAxisScalePivot(Transform parent, Vector3 axisScale)
        {
            var scalePivot = new GameObject("Axis Scale").transform;
            scalePivot.SetParent(parent, false);
            scalePivot.localScale = axisScale;
            return scalePivot;
        }

        /// <summary>
        /// Multiplies (not overwrites) every child ParticleSystem's simulation speed, so a prefab authored
        /// faster or slower than 1 keeps its internal ratios. CFXR effects manage their own lifetime but
        /// never touch simulationSpeed, so the multiplier holds for them too.
        /// </summary>
        private static void ApplyPlaybackSpeed(GameObject root, float speed)
        {
            if (Mathf.Approximately(speed, 1f) || speed <= 0f)
            {
                return;
            }

            var particles = root.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var particle in particles)
            {
                var main = particle.main;
                main.simulationSpeed *= speed;
            }
        }

        private float ResolvePrefabLifetime(GameObject root, EffectVfxCatalog.Entry entry)
        {
            if (entry.LifetimeOverride > 0f)
            {
                return entry.LifetimeOverride;
            }

            var lifetime = particleLifetime + 0.25f;
            // A cue playing at N× speed finishes in 1/N of the authored duration, so the natural lifetime
            // shrinks with it (an authored lifetimeOverride above still wins untouched).
            var speed = entry.PlaybackSpeed;
            var particles = root.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var particle in particles)
            {
                var main = particle.main;
                var duration = (main.duration + main.startLifetime.constantMax) / speed;
                if (duration > lifetime)
                {
                    lifetime = duration + 0.25f;
                }
            }

            return lifetime;
        }

        public void ClearSpawnedEffects()
        {
            StopPendingEffectRoutines();
            foreach (var effect in spawnedEffects)
            {
                if (effect != null)
                {
                    DestroyObjectSafely(effect);
                }
            }

            spawnedEffects.Clear();
        }

        /// <summary>
        /// Spawns a persistent, looping status VFX that stays alive until <see cref="StopLoop"/> (or
        /// <see cref="StopAllLoops"/>) is called — unlike <see cref="Play"/>, it is never auto-destroyed on
        /// a timer. When the cue is authored with <see cref="EffectVfxCatalog.Entry.AttachToActor"/> and an
        /// <paramref name="attachTarget"/> is supplied, the instance is parented under that transform so it
        /// follows the actor; the entry's position/rotation offsets are then applied in the actor's local
        /// space. Otherwise it is placed in world space under the effect root like a one-shot.
        /// </summary>
        public GameObject PlayLoop(
            EffectVfxCatalog.Entry entry,
            Transform attachTarget,
            Vector3 worldPosition,
            Quaternion facingRotation,
            int radius = 0)
        {
            if (entry == null)
            {
                return null;
            }

            GameObject prefab = null;
            foreach (var candidate in entry.Prefabs)
            {
                if (candidate != null)
                {
                    prefab = candidate;
                    break;
                }
            }

            if (prefab == null)
            {
                return null;
            }

            EnsureRoot();
            var scale = ResolveLoopScale(entry, radius);

            // Attached loops hang off a dedicated pivot that owns ONLY a uniform scale + world rotation and
            // follows the actor's anchor position. The prefab is instantiated under the pivot with its authored
            // local transform untouched. This matters because a uniform scale applied above a rotated child
            // never shears it — whereas writing a non-uniform/over-rotated transform directly onto the prefab
            // root (or inheriting a bone's transform) twists shaped effects like the buff/debuff arrows.
            GameObject go;
            if (entry.AttachToActor && attachTarget != null)
            {
                var pivot = new GameObject($"Status Loop VFX {entry.StatusKind} ({prefab.name})");
                pivot.transform.SetParent(null);
                pivot.transform.rotation = entry.RotationOffset;
                pivot.transform.localScale = ResolveLoopScaleVector(entry, radius);
                Instantiate(prefab, pivot.transform);
                var follower = pivot.AddComponent<StatusLoopFollower>();
                follower.Configure(attachTarget, entry.PositionOffset);
                go = pivot;
            }
            else
            {
                go = Instantiate(prefab, worldPosition + facingRotation * entry.PositionOffset, facingRotation * entry.RotationOffset, effectRoot);
                go.transform.localScale = Vector3.Scale(go.transform.localScale, Vector3.one * scale);
            }

            go.name = $"Status Loop VFX {entry.StatusKind} ({prefab.name})";
            DisableRenderersWithMissingMaterials(go);
            ForceLoopParticles(go);
            loopingEffects.Add(go);
            return go;
        }

        /// <summary>
        /// Runtime variant of <see cref="PlayLoop"/> for actual combat: the loop follows a live anchor world
        /// position supplied by <paramref name="positionProvider"/> (evaluated every frame), since combat
        /// markers are pooled/animated and do not expose a stable Transform. Uses the same shear-safe pivot
        /// (uniform-on-XZ scale + world rotation) as the lab path.
        /// </summary>
        /// <param name="bodyScale">
        /// 몸집 비례 계수(2026-08-20 WS-2, <see cref="StatusLoopBodyScale"/>). 저작 스케일과 저작 오프셋에
        /// <b>둘 다</b> 곱한다 — 크기만 키우고 오프셋을 상수로 두면 큰 몬스터에서 링이 상대적으로 얕게
        /// 묻혀 저작 의도(발치에 반쯤 잠긴 링)가 깨진다. 균일 계수라 저작된 (s : 1 : s) 종횡비는
        /// 그대로 보존되므로 shear는 생기지 않는다. 기준 체구(플레이어)는 1이라 현행과 동일하다.
        /// </param>
        public GameObject PlayLoopFollowing(
            EffectVfxCatalog.Entry entry,
            System.Func<Vector3> positionProvider,
            int radius = 0,
            float bodyScale = 1f)
        {
            if (entry == null || positionProvider == null)
            {
                return null;
            }

            GameObject prefab = null;
            foreach (var candidate in entry.Prefabs)
            {
                if (candidate != null)
                {
                    prefab = candidate;
                    break;
                }
            }

            if (prefab == null)
            {
                return null;
            }

            EnsureRoot();

            var safeBodyScale = bodyScale > 0f ? bodyScale : 1f;
            var pivot = new GameObject($"Status Loop VFX {entry.StatusKind} ({prefab.name})");
            pivot.transform.SetParent(null);
            pivot.transform.rotation = entry.RotationOffset;
            pivot.transform.localScale = ResolveLoopScaleVector(entry, radius) * safeBodyScale;
            Instantiate(prefab, pivot.transform);
            var follower = pivot.AddComponent<StatusLoopFollower>();
            follower.Configure(positionProvider, entry.PositionOffset * safeBodyScale);

            DisableRenderersWithMissingMaterials(pivot);
            ForceLoopParticles(pivot);
            loopingEffects.Add(pivot);
            return pivot;
        }

        public void StopLoop(GameObject handle)
        {
            StopLoop(handle, immediate: false);
        }

        public void StopLoop(GameObject handle, bool immediate)
        {
            if (handle == null)
            {
                loopingEffects.RemoveAll(effect => effect == null);
                return;
            }

            loopingEffects.Remove(handle);

            if (immediate || !Application.isPlaying)
            {
                DestroyObjectSafely(handle);
                return;
            }

            // Stop emitting and let already-spawned particles fade out before destroying, so the loop
            // doesn't pop off abruptly when the status is cleared.
            var particles = handle.GetComponentsInChildren<ParticleSystem>(true);
            var tail = 0.25f;
            foreach (var particle in particles)
            {
                particle.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                var main = particle.main;
                tail = Mathf.Max(tail, main.startLifetime.constantMax);
            }

            Destroy(handle, tail);
        }

        public void StopAllLoops()
        {
            StopAllLoops(immediate: false);
        }

        public void StopAllLoops(bool immediate)
        {
            for (var i = loopingEffects.Count - 1; i >= 0; i--)
            {
                StopLoop(loopingEffects[i], immediate);
            }

            loopingEffects.Clear();
        }

        /// <summary>
        /// Live-updates a looping VFX handle's follow target and world transform without respawning it, so
        /// the dev lab can tune offset/rotation/scale independently. World-space values keep scale and
        /// rotation decoupled (changing one never affects the other).
        /// </summary>
        public void UpdateLoopTransform(GameObject handle, Transform attachTarget, Vector3 worldOffset, Quaternion worldRotation, Vector3 localScale)
        {
            if (handle == null)
            {
                return;
            }

            var follower = handle.GetComponent<StatusLoopFollower>();
            if (follower != null)
            {
                follower.Configure(attachTarget, worldOffset);
            }
            else if (attachTarget != null)
            {
                handle.transform.position = attachTarget.position + worldOffset;
            }

            handle.transform.rotation = worldRotation;
            handle.transform.localScale = localScale;
        }

        private static float ResolveLoopScale(EffectVfxCatalog.Entry entry, int radius)
        {
            var radiusScale = entry.ScaleWithRadius ? Mathf.Max(1f, radius <= 0 ? 1f : radius) : 1f;
            return radiusScale * entry.ScaleMultiplier;
        }

        /// <summary>
        /// Pivot scale for a looping status VFX. Flat "arrow" auras (buff/debuff and the rest) shear when
        /// stretched vertically, so they only scale on X/Z and keep Y fixed at 1. Stun (overhead stars) is
        /// volumetric and scales uniformly on all axes.
        /// </summary>
        public static Vector3 ResolveLoopScaleVector(StatusEffectKind statusKind, float scale)
        {
            return statusKind == StatusEffectKind.Stun
                ? Vector3.one * scale
                : new Vector3(scale, 1f, scale);
        }

        private static Vector3 ResolveLoopScaleVector(EffectVfxCatalog.Entry entry, int radius)
        {
            return ResolveLoopScaleVector(entry.StatusKind, ResolveLoopScale(entry, radius));
        }

        private static void ForceLoopParticles(GameObject root)
        {
            var particles = root.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var particle in particles)
            {
                particle.gameObject.SetActive(true);
                var main = particle.main;
                main.loop = true;
                main.playOnAwake = false;
                particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                particle.Play(true);
            }
        }

        private void StopPendingEffectRoutines()
        {
            for (var i = 0; i < pendingEffectRoutines.Count; i++)
            {
                var routine = pendingEffectRoutines[i];
                if (routine != null)
                {
                    StopCoroutine(routine);
                }
            }

            pendingEffectRoutines.Clear();
        }

        private void SpawnParticle(EffectKind kind, Vector3 worldPosition, int radius)
        {
            var go = new GameObject($"VFX Placeholder {kind}");
            go.transform.SetParent(effectRoot, false);
            go.transform.position = worldPosition;
            go.transform.localScale = Vector3.one * Mathf.Max(1f, radius <= 0 ? 1f : radius);
            var particle = go.AddComponent<ParticleSystem>();
            ConfigureParticle(particle, kind);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = GetParticleMaterial(kind);
            spawnedEffects.Add(go);
            ScheduleDestroyObject(go, particleLifetime + 0.25f);
        }

        private void ConfigureParticle(ParticleSystem particle, EffectKind kind)
        {
            particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particle.main;
            main.playOnAwake = false;
            main.duration = particleLifetime;
            main.loop = false;
            main.startLifetime = Mathf.Max(0.35f, particleLifetime * 0.65f);
            main.startSpeed = kind == EffectKind.Block ? 0.35f : 1.4f;
            main.startSize = kind == EffectKind.FogReveal ? 0.22f : kind == EffectKind.ReflectDamage ? 0.2f : 0.16f;
            main.startColor = ToColor(kind);
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = particle.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, ToBurstCount(kind)) });

            var shape = particle.shape;
            shape.enabled = true;
            shape.shapeType = kind == EffectKind.Block ? ParticleSystemShapeType.Circle : ParticleSystemShapeType.Sphere;
            shape.radius = kind == EffectKind.FogReveal ? 1.1f : 0.35f;

            particle.Play();
        }

        private void SpawnFloatingText(EffectVfxCatalog.Entry entry, EffectResultEvent resultEvent, Vector3 worldPosition, float delaySeconds = 0f)
        {
            // Honor an event-carried delay (e.g. monster intent-cancel text floats after the status VFX).
            delaySeconds = Mathf.Max(delaySeconds, resultEvent.DelaySeconds);
            if (delaySeconds > 0f && isActiveAndEnabled)
            {
                pendingEffectRoutines.Add(StartCoroutine(SpawnFloatingTextAfterDelay(entry, resultEvent, worldPosition, delaySeconds)));
                return;
            }

            SpawnFloatingTextNow(entry, resultEvent, worldPosition);
        }

        private IEnumerator SpawnFloatingTextAfterDelay(EffectVfxCatalog.Entry entry, EffectResultEvent resultEvent, Vector3 worldPosition, float delaySeconds)
        {
            yield return new WaitForSeconds(Mathf.Max(0f, delaySeconds));
            pendingEffectRoutines.RemoveAll(routine => routine == null);
            if (this == null)
            {
                yield break;
            }

            EnsureRoot();
            SpawnFloatingTextNow(entry, resultEvent, worldPosition);
        }

        private IEnumerator SpawnFloatingTextAfterDelay(string text, Color color, Vector3 worldPosition, float delaySeconds)
        {
            yield return new WaitForSeconds(Mathf.Max(0f, delaySeconds));
            pendingEffectRoutines.RemoveAll(routine => routine == null);
            if (this == null)
            {
                yield break;
            }

            EnsureRoot();
            SpawnFloatingTextNow(text, color, worldPosition);
        }

        private void SpawnFloatingTextNow(EffectVfxCatalog.Entry entry, EffectResultEvent resultEvent, Vector3 worldPosition)
        {
            FloatingTextPresented?.Invoke(resultEvent);
            SpawnFloatingTextNow(
                FormatText(entry, resultEvent),
                ResolveFloatingTextColor(resultEvent),
                worldPosition,
                $"Floating Effect Text {resultEvent.Kind}");
        }

        private void SpawnFloatingTextNow(string textValue, Color color, Vector3 worldPosition, string objectName = "Floating Effect Text")
        {
            // Every floating-text path funnels through here; the entry-variant above has already raised
            // FloatingTextPresented, so filming suppression drops only the visual.
            if (DebugSuppressFloatingText)
            {
                return;
            }

            CombatPresentationTrace.Record(CombatTraceChannel.Text, textValue ?? string.Empty);

            var go = new GameObject(objectName);
            go.transform.SetParent(effectRoot, false);
            go.transform.position = worldPosition;
            var text = go.AddComponent<TextMeshPro>();
            text.text = textValue ?? string.Empty;
            text.color = color;
            ApplyFloatingTextFont(text);
            text.fontSize = Mathf.Max(3f, floatingTextFontSize);
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            ApplyAlwaysOnTop(text);
            var billboard = go.AddComponent<FloatingEffectTextBillboard>();
            billboard.Initialize(Mathf.Max(0.1f, floatingTextLifetime), floatingTextRiseHeight);
            billboard.AlignNow();
            spawnedEffects.Add(go);
            ScheduleDestroyObject(go, Mathf.Max(particleLifetime, floatingTextLifetime) + 0.5f);
        }


        // Keeps floating numbers from being hidden behind player/monster meshes. World-space TMP text
        // depth-tests against scene geometry by default, so we render it through TMP's dedicated overlay
        // shader (ZTest Always) and push it to the overlay queue. Operates on the per-instance
        // fontMaterial, so the shared font asset and other text are untouched, and the instanced
        // material is freed with this text object.
        private Shader cachedOverlayTextShader;

        private void ApplyAlwaysOnTop(TMP_Text text)
        {
            if (text == null)
            {
                return;
            }

            var material = text.fontMaterial;
            if (material == null)
            {
                return;
            }

            if (cachedOverlayTextShader == null)
            {
                cachedOverlayTextShader = Shader.Find(OverlayTextShaderName);
            }

            if (cachedOverlayTextShader != null)
            {
                material.shader = cachedOverlayTextShader;
            }
            else if (material.HasProperty(ZTestModeProperty))
            {
                material.SetFloat(ZTestModeProperty, (float)UnityEngine.Rendering.CompareFunction.Always);
            }

            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Overlay;
        }

        private void ApplyFloatingTextFont(TMP_Text text)
        {
            if (text == null)
            {
                return;
            }

            var font = GetFloatingTextFont();
            if (font != null)
            {
                EnsureFontSupportsText(font, text.text);
                text.font = font;
            }
        }

        private TMP_FontAsset GetFloatingTextFont()
        {
            if (floatingTextFont != null)
            {
                return floatingTextFont;
            }

            floatingTextFont = KoreanFontProvider.Load();
            return floatingTextFont;
        }

        private static void EnsureFontSupportsText(TMP_FontAsset font, string value)
        {
            if (font == null || string.IsNullOrEmpty(value))
            {
                return;
            }

            try
            {
                font.HasCharacters(value);
            }
            catch (UnityException)
            {
                // Keep the assigned font instead of breaking effect presentation.
                // Required Korean glyphs are pre-baked into the project TMP font asset.
            }
        }

        private static void DisableRenderersWithMissingMaterials(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer is ParticleSystemRenderer)
                {
                    continue;
                }

                var materials = renderer.sharedMaterials;
                if (materials == null || materials.Length == 0)
                {
                    renderer.enabled = false;
                    continue;
                }

                foreach (var material in materials)
                {
                    if (material == null)
                    {
                        renderer.enabled = false;
                        break;
                    }
                }
            }
        }

        private void EnsureRoot()
        {
            if (effectRoot != null)
            {
                return;
            }

            var root = new GameObject("Effect Presentation Runtime Root");
            root.transform.SetParent(transform, false);
            effectRoot = root.transform;
        }

        private Vector3 HexToWorld(HexCoord coord)
        {
            var x = hexSpacing * (Mathf.Sqrt(3f) * coord.Q + Mathf.Sqrt(3f) / 2f * coord.R);
            var z = hexSpacing * (1.5f * coord.R);
            return new Vector3(x, 0.08f, z);
        }

        // Damage numbers are tinted by who got hit (MapleStory convention): the player taking
        // damage reads as an urgent red, while damage dealt to enemies reads as bright amber/white.
        // Non-damage effects keep their per-kind color.
        private static Color ResolveFloatingTextColor(EffectResultEvent resultEvent)
        {
            if (resultEvent.Kind != EffectKind.Damage && resultEvent.Kind != EffectKind.ReflectDamage)
            {
                return resultEvent.Kind == EffectKind.StatusEffectApplied && resultEvent.StatusKind.HasValue
                ? ToColor(resultEvent.StatusKind.Value)
                : ToColor(resultEvent.Kind);
            }

            return IsPlayerTarget(resultEvent)
                ? new Color(1f, 0.27f, 0.22f, 1f)
                : new Color(1f, 0.95f, 0.55f, 1f);
        }

        private static bool IsPlayerTarget(EffectResultEvent resultEvent)
        {
            return string.Equals(resultEvent.TargetUnitId, "player", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(resultEvent.TargetActorKind, "player", System.StringComparison.OrdinalIgnoreCase);
        }

        private static Color ToColor(EffectKind kind)
        {
            switch (kind)
            {
                case EffectKind.Damage:
                    return new Color(1f, 0.2f, 0.12f, 1f);
                case EffectKind.Block:
                    return new Color(0.2f, 0.65f, 1f, 1f);
                case EffectKind.DamageBlocked:
                    return new Color(0.2f, 0.65f, 1f, 1f);
                case EffectKind.Heal:
                    return new Color(0.25f, 1f, 0.55f, 1f);
                case EffectKind.FogReveal:
                    return new Color(1f, 0.92f, 0.45f, 1f);
                case EffectKind.Push:
                    return new Color(0.95f, 0.95f, 1f, 1f);
                case EffectKind.Knockback:
                    return new Color(1f, 0.55f, 0.1f, 1f);
                case EffectKind.ReflectDamage:
                    return new Color(0.95f, 0.35f, 1f, 1f);
                case EffectKind.StatusEffectApplied:
                    return new Color(0.75f, 0.85f, 1f, 1f);
                case EffectKind.StatusNegated:
                    // 수호의 무효화 — 유물 칩과 같은 금색 계열로 "지켜졌다"를 읽게 한다.
                    return new Color(0.95f, 0.8f, 0.35f, 1f);
                case EffectKind.MonsterTraitTriggered:
                    // 몬스터 특성 — 적 쪽 신호라 붉은 기가 도는 자주색. 무효(금색)와 갈리고,
                    // 피해(빨강)·상태(연푸름)와도 갈린다. 🔑 색이 완전히 고유할 필요는 없다:
                    // 색상환이 포화라 식별자는 색이 아니라 <b>형상</b>(문안)으로 넘어간 지 오래다
                    // (2026-08-10 사용자 확정) — 「은신!」과 「-6」을 헷갈릴 사람은 없다.
                    return new Color(0.85f, 0.5f, 0.85f, 1f);
                default:
                    return Color.white;
            }
        }

        private static Color ToColor(StatusEffectKind kind) => CombatTileFlashStyles.StatusColor(kind, Color.white);

        private static short ToBurstCount(EffectKind kind)
        {
            return kind == EffectKind.FogReveal ? (short)48 : kind == EffectKind.ReflectDamage ? (short)32 : (short)24;
        }

        private Material GetParticleMaterial(EffectKind kind)
        {
            if (particleMaterials.TryGetValue(kind, out var material) && material != null)
            {
                return material;
            }

            material = CreateColoredMaterial(ToColor(kind));
            particleMaterials[kind] = material;
            return material;
        }

        private static Material CreateColoredMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Sprites/Default")
                ?? Shader.Find("Unlit/Color");
            var material = new Material(shader);
            SetMaterialColor(material, color);
            return material;
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            material.color = color;
        }

        // Korean floating-text mapping. Designers can override the whole string per cue via the catalog
        // entry's floatingTextOverride (e.g. field cards: "피해 필드 설치", "3턴 후 사라짐"). Override text
        // supports {amount} / {turns} tokens so authored copy can still surface runtime numbers.
        private static string FormatText(EffectVfxCatalog.Entry entry, EffectResultEvent resultEvent)
        {
            if (entry != null && !string.IsNullOrWhiteSpace(entry.FloatingTextOverride))
            {
                return ApplyTextTokens(entry.FloatingTextOverride, resultEvent);
            }

            if (resultEvent.Kind == EffectKind.StatusEffectApplied &&
                string.Equals(resultEvent.SourceRef, "object.treasure_chest.reward", System.StringComparison.Ordinal))
            {
                return "카드 획득!";
            }

            // 몬스터 특성 알림(2026-09-01). 문안 표는 규칙층과 <b>공유</b>한다 — 여기에 문자열을 따로
            // 들면 규칙이 던지는 ref와 화면이 말하는 말이 갈린다(도감 문안 사고와 같은 자리).
            // 🔑 은신 노출은 kind가 FogReveal이라 아래 switch의 "밝혀짐!"으로 갈 뻔한다: 이 표를 먼저
            //    물어야 「들킴!」이 이긴다. 횃불·정찰의 밝혀짐은 ref가 달라 그대로 남는다.
            // 🔑 StatusKind를 함께 넘긴다 — 지대 생성 알림만 그 축을 읽어 "속박 지대 생성!"을 만든다
            //    (나머지 항목은 무시한다). 종류를 표현층이 따로 해석하지 않는 것이 요점이다.
            if (MonsterTraitAnnouncement.TryGetText(
                    resultEvent.SourceRef, resultEvent.AppliedAmount, resultEvent.StatusKind, out var traitText))
            {
                return traitText;
            }

            if (resultEvent.Kind == EffectKind.StatusEffectApplied && resultEvent.StatusKind.HasValue)
            {
                return FormatStatusText(resultEvent);
            }

            if (resultEvent.Kind == EffectKind.StatusEffectExpired && resultEvent.StatusKind.HasValue)
            {
                var name = KoreanStatusName(resultEvent.StatusKind.Value);
                // Debuffs are "해제"(cleared); buffs are "종료"(ended).
                return StatusEffectInfo.IsBuff(resultEvent.StatusKind.Value) ? name + " 종료" : name + " 해제";
            }

            if (resultEvent.Kind == EffectKind.StatusNegated)
            {
                // 수호(T2 페이즈 C)의 무효화 — 무엇을 삼켰는지 함께 보여준다(사용자 확정 문안).
                return resultEvent.StatusKind.HasValue
                    ? KoreanStatusName(resultEvent.StatusKind.Value) + " 무효!"
                    : "무효!";
            }

            switch (resultEvent.Kind)
            {
                case EffectKind.Damage:
                    if (resultEvent.StatusKind == StatusEffectKind.Poison)
                    {
                        // The trap is announced exactly once — at the moment it's stepped on — via the
                        // StatusEffectApplied event ("함정 발동! 중독 N"). Per-turn poison ticks are a residual
                        // status effect, not a fresh trigger, so they must NEVER re-announce "함정 발동!" even
                        // though trap-applied poison still carries its "trap." source ref. Always a plain tick.
                        return $"중독 -{resultEvent.AppliedAmount}";
                    }
                    if (IsKnockbackImpact(resultEvent))
                    {
                        return $"충돌! -{resultEvent.AppliedAmount}";
                    }
                    return IsTrapEvent(resultEvent)
                        ? $"함정 발동! -{resultEvent.AppliedAmount}"
                        : $"-{resultEvent.AppliedAmount}";
                case EffectKind.Block:
                    return CombatEffectSourceClassifier.IsDamageImmunitySource(resultEvent.SourceRef)
                        ? "피해 면역"
                        : $"방어 +{resultEvent.AppliedAmount}";
                case EffectKind.DamageBlocked:
                    return "방어!";
                case EffectKind.Heal:
                    return $"+{resultEvent.AppliedAmount}";
                case EffectKind.FogReveal:
                    return "밝혀짐!";
                case EffectKind.ReflectDamage:
                    return $"반사 -{resultEvent.AppliedAmount}";
                // 🔑 부호가 방향이다(§16.1) — 음수 = 끌어당김(2026-09-01 #5). 종전에는 둘 다 「밀려남」이라
                //    끌려가 놓고 밀려났다고 읽혔다. 툴팁의 명사 어휘(밀치기/끌어당김)와 짝을 이루는
                //    <b>당한 쪽의 말</b>이므로 「밀려남/끌려감」으로 간다.
                case EffectKind.Knockback:
                case EffectKind.Push:
                    return resultEvent.AppliedAmount < 0 ? "끌려감" : "밀려남";
                case EffectKind.AttackCancelled:
                {
                    var moveCancelled = (resultEvent.Amount & 1) != 0;
                    var attackCancelled = (resultEvent.Amount & 2) != 0;
                    if (moveCancelled && attackCancelled)
                    {
                        return "이동, 공격 취소!";
                    }
                    return attackCancelled ? "공격 취소!" : "이동 취소!";
                }
                case EffectKind.AttackMissed:
                    return "빗맞음";
                case EffectKind.TrapDisarmed:
                    return "해체!";
                case EffectKind.MonsterTraitTriggered:
                    // 표에 없는 ref로 이 kind가 나온 것 = 새 특성을 만들고 문안을 안 적었다는 뜻이다.
                    // 조용히 삼키지 말고 ref를 그대로 띄워 저작 구멍이 화면에 드러나게 한다.
                    return resultEvent.SourceRef;
                default:
                    return resultEvent.Kind.ToString();
            }
        }

        private static string FormatStatusText(EffectResultEvent resultEvent)
        {
            // A12 전염병's contagion re-applies the carried debuff through the ordinary status-apply channel;
            // the source ref is the only thing distinguishing it from a fresh application. Prefix the existing
            // text rather than minting a new EffectKind, so the cue rides the same floating-text channel as
            // every other status (and 함정 발동! prefixes the same way one level down).
            if (IsPlagueContagionEvent(resultEvent))
            {
                return "전염! " + FormatStatusTextCore(resultEvent);
            }

            // 뒤끝(T7-2, 2026-08-19 #14): 죽은 몬스터가 남긴 디버프는 "누가 걸었는지"가 화면에 없다 —
            // 시체는 이미 쓰러졌고 부여 텍스트만 뜨면 영문 모를 실명이 된다. 전염과 같은 접두 문법.
            if (string.Equals(resultEvent.SourceRef, MonsterDeathAftermath.DebuffRef, System.StringComparison.Ordinal))
            {
                return "뒤끝! " + FormatStatusTextCore(resultEvent);
            }

            return FormatStatusTextCore(resultEvent);
        }

        private static bool IsPlagueContagionEvent(EffectResultEvent resultEvent)
        {
            return string.Equals(resultEvent.SourceRef, CardEffectRefs.AttackPlague, System.StringComparison.Ordinal);
        }

        /// <summary>
        /// 부여 문안의 정본은 <c>status_effects.csv</c>의 <c>floatingTextTemplate</c>(1단계 구조 리팩토링) —
        /// 여기 있던 switch(속박 {n}턴·중독 {n}·민첩 +{n}·반사 부여…)는 <see cref="StatusEffectInfo"/> 폴백 템플릿으로 옮겼다.
        /// 함정 접두·수치 토큰 문법은 <see cref="StatusEffectInfo.FormatFloatingText"/> 하나가 안다.
        /// </summary>
        private static string FormatStatusTextCore(EffectResultEvent resultEvent)
        {
            return StatusEffectInfo.FloatingText(resultEvent.StatusKind.Value, resultEvent.AppliedAmount, IsTrapEvent(resultEvent));
        }

        internal static string FormatStatusTextForTests(EffectResultEvent resultEvent) => FormatStatusText(resultEvent);

        private static string KoreanStatusName(StatusEffectKind kind) => StatusEffectInfo.DisplayName(kind);

        // 🔴 <b>다단 히트는 히트마다 자기 연출을 낸다</b>(2026-09-02 #3 · 사용자 확정으로 종전 결정을
        //    뒤집었다). 종전에는 "후속 히트는 선두 히트가 띄운 VFX를 재사용한다"며 HitIndex > 0인
        //    이벤트의 프리팹·폴백 파티클을 통째로 막았고, 그 결과 <b>숫자만 세 번 뜨고 그림은 한 번</b>
        //    나는 화면이 됐다 — 플로팅 텍스트가 이 게이트 밖에 있었기 때문이다.
        //
        // 🔑 걷어내는 것만으로는 부족했다: 규칙층이 히트를 <b>같은 프레임에</b> 내고 있어서 세 발이
        //    한 점에 겹쳤다. 시간으로 벌리는 일은 규칙층의 박자(CombatState.MultiHitBeatDelaySeconds)가
        //    맡는다 — <b>반복을 보이게 하는 것은 횟수가 아니라 간격</b>이다.

        // Status-expiry events are loop-VFX teardown signals, not a fresh effect, so they never get a
        // placeholder particle when no authored prefab matches. 무효(StatusNegated)도 마찬가지 —
        // "아무 일도 일어나지 않았다"가 내용이므로 플로팅 텍스트만 띄운다.
        private static bool ShouldSpawnPlaceholderParticle(EffectResultEvent resultEvent)
        {
            return resultEvent.Kind != EffectKind.StatusEffectExpired
                && resultEvent.Kind != EffectKind.AttackMissed
                && resultEvent.Kind != EffectKind.StatusNegated
                && resultEvent.Kind != EffectKind.TrapDisarmed
                && resultEvent.Kind != EffectKind.MonsterTraitTriggered;
        }

        private static string ApplyTextTokens(string template, EffectResultEvent resultEvent)
        {
            return template
                .Replace("{amount}", resultEvent.AppliedAmount.ToString())
                .Replace("{turns}", resultEvent.AppliedAmount.ToString());
        }

        private static bool IsTrapEvent(EffectResultEvent resultEvent)
        {
            return !string.IsNullOrWhiteSpace(resultEvent.SourceRef) &&
                   resultEvent.SourceRef.StartsWith("trap.", System.StringComparison.Ordinal);
        }

        private static bool IsKnockbackImpact(EffectResultEvent resultEvent)
        {
            return string.Equals(resultEvent.SourceRef, "knockback.impact", System.StringComparison.Ordinal);
        }


        // Slews the pivot toward a target rotation instead of snapping, so a following loop whose facing
        // changes mid-flight (move dust turning at a tile boundary) sweeps around smoothly. Runs in
        // LateUpdate like StatusLoopFollower; the follower owns position, this owns rotation.
        internal sealed class FollowingLoopRotator : MonoBehaviour
        {
            private const float DegreesPerSecond = 540f;

            private Quaternion target = Quaternion.identity;

            public void Snap(Quaternion rotation)
            {
                target = rotation;
                transform.rotation = rotation;
            }

            public void SetTarget(Quaternion rotation)
            {
                target = rotation;
            }

            private void LateUpdate()
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target, DegreesPerSecond * Time.deltaTime);
            }
        }

        // Re-applies the source anchor's pose delta (since spawn) to a one-shot VFX every frame, so a cue
        // authored followSourceAnchor sweeps with the anchor's animation (e.g. a breath cone tracking the
        // monster's head). The authored spawn pose stays the baseline: at spawn the VFX looks exactly like
        // the fixed one-shot, then anchor translation/rotation deltas carry it. Scale is never touched, so
        // a scaled/skewed bone driving the anchor cannot deform the effect.
        // Public (unlike the loop followers) because the VFX lab's direct-spawn tuning path lives in a
        // different assembly and attaches the same follower so its preview matches production.
        public sealed class AnchorDeltaFollower : MonoBehaviour
        {
            private Transform anchor;
            private Vector3 baselineAnchorPosition;
            private Quaternion baselineAnchorRotationInverse = Quaternion.identity;
            private Vector3 basePosition;
            private Quaternion baseRotation = Quaternion.identity;

            public void Configure(Transform sourceAnchor)
            {
                anchor = sourceAnchor;
                if (anchor == null)
                {
                    return;
                }

                baselineAnchorPosition = anchor.position;
                baselineAnchorRotationInverse = Quaternion.Inverse(anchor.rotation);
                basePosition = transform.position;
                baseRotation = transform.rotation;
            }

            private void LateUpdate()
            {
                if (anchor == null)
                {
                    return;
                }

                var deltaRotation = anchor.rotation * baselineAnchorRotationInverse;
                transform.SetPositionAndRotation(
                    anchor.position + deltaRotation * (basePosition - baselineAnchorPosition),
                    deltaRotation * baseRotation);
            }
        }

        // Keeps a looping status VFX glued to an actor anchor's world position while leaving its rotation
        // and scale untouched, so the effect follows movement without inheriting the bone's rotated/skewed
        // transform (which would otherwise couple "scale" with visible rotation).
        internal sealed class StatusLoopFollower : MonoBehaviour
        {
            private Transform target;
            private System.Func<Vector3> positionProvider;
            private Vector3 worldOffset;

            public void Configure(Transform followTarget, Vector3 offset)
            {
                target = followTarget;
                positionProvider = null;
                worldOffset = offset;
                SyncNow();
            }

            // Runtime markers are pooled/animated, so combat resolves anchors to a live world position each
            // frame rather than handing out a stable Transform. The provider lets the loop track that.
            public void Configure(System.Func<Vector3> provider, Vector3 offset)
            {
                positionProvider = provider;
                target = null;
                worldOffset = offset;
                SyncNow();
            }

            /// <summary>Re-aims the offset without touching the follow source — used when a facing-relative offset must track a facing change.</summary>
            public void SetWorldOffset(Vector3 offset)
            {
                worldOffset = offset;
            }

            private void LateUpdate()
            {
                SyncNow();
            }

            private void SyncNow()
            {
                if (positionProvider != null)
                {
                    transform.position = positionProvider() + worldOffset;
                }
                else if (target != null)
                {
                    transform.position = target.position + worldOffset;
                }
            }
        }

        private sealed class FloatingEffectTextBillboard : MonoBehaviour
        {
            private const float PopInDuration = 0.16f;
            private const float PopScale = 1.32f;
            private const float FadeStartFraction = 0.55f;

            private TMP_Text text;
            private Vector3 startPosition;
            private Color baseColor = Color.white;
            private bool captured;
            private float age;
            private float lifetime = 1.1f;
            private float riseHeight = 0.9f;

            public void Initialize(float textLifetime, float textRiseHeight)
            {
                lifetime = Mathf.Max(0.1f, textLifetime);
                riseHeight = textRiseHeight;
                EnsureCaptured();
            }

            private void Awake()
            {
                EnsureCaptured();
            }

            private void EnsureCaptured()
            {
                if (captured)
                {
                    return;
                }

                text = GetComponent<TMP_Text>();
                startPosition = transform.position;
                if (text != null)
                {
                    baseColor = text.color;
                }

                captured = true;
            }

            private void LateUpdate()
            {
                EnsureCaptured();
                age += Time.deltaTime;
                var t = Mathf.Clamp01(age / lifetime);

                // Ease-out rise: fast launch that settles near the top, like a damage popup arc.
                var rise = 1f - (1f - t) * (1f - t);
                transform.position = startPosition + Vector3.up * (riseHeight * rise);

                // Scale punch on spawn, then settle to normal size.
                var scale = age < PopInDuration ? Mathf.Lerp(PopScale, 1f, age / PopInDuration) : 1f;
                transform.localScale = Vector3.one * scale;

                // Hold full alpha, then fade out over the tail of the lifetime.
                if (text != null)
                {
                    var alpha = t <= FadeStartFraction ? 1f : Mathf.InverseLerp(1f, FadeStartFraction, t);
                    var color = baseColor;
                    color.a = alpha;
                    text.color = color;
                }

                AlignNow();
            }

            public void AlignNow()
            {
                var targetCamera = ResolveCamera();
                if (targetCamera == null)
                {
                    return;
                }

                var forwardAwayFromCamera = transform.position - targetCamera.transform.position;
                if (forwardAwayFromCamera.sqrMagnitude <= 0.0001f)
                {
                    transform.rotation = targetCamera.transform.rotation;
                    return;
                }

                transform.rotation = Quaternion.LookRotation(forwardAwayFromCamera.normalized, targetCamera.transform.up);
            }

            private static Camera ResolveCamera()
            {
                if (Camera.main != null)
                {
                    return Camera.main;
                }

                if (Camera.current != null)
                {
                    return Camera.current;
                }

#if UNITY_EDITOR
                if (!Application.isPlaying && SceneView.lastActiveSceneView != null)
                {
                    return SceneView.lastActiveSceneView.camera;
                }
#endif

                return Camera.allCamerasCount > 0 ? Camera.allCameras[0] : null;
            }
        }

        private static void ScheduleDestroyObject(Object target, float delay)
        {
            if (Application.isPlaying)
            {
                Destroy(target, delay);
            }
        }

        private static void DestroyObjectSafely(Object target)
        {
            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}



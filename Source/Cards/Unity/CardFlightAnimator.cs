using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Spawns short-lived "ghost" cards that fly between the deck piles and the hand to sell the
    /// draw/discard feel.
    ///
    /// Phase 1 scope: a single reveal-style draw flight — a face-down ghost (just the card back) flies
    /// from the draw pile to a hand slot, then the caller reveals the real card at the landing moment
    /// (option 1, "reveal flip"). The ghost therefore only needs a back sprite; the front is always the
    /// real <c>CardFront</c> hand slot.
    ///
    /// Lives on the CardLane root (the bottom HUD prefab, which already parents both piles and the hand)
    /// and lazily creates its own top-most overlay container so flights render above the fanned cards
    /// without participating in their layout. Mirrors the lazy-child pattern used by
    /// <see cref="GameplayCardLaneView"/>'s home input proxy.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CardFlightAnimator : MonoBehaviour
    {
        public const string OverlayName = "CardFlightOverlay";

        [Header("Ghost Card (back only)")]
        [SerializeField] private Sprite cardBackSprite;
        [Tooltip("Optional authored ghost prefab (e.g. CardBack.prefab). When null a runtime Image is built from 'cardBackSprite'.")]
        [SerializeField] private GameObject cardBackPrefab;
        [SerializeField] private Vector2 ghostSize = new Vector2(200f, 320f);

        [Header("Draw Flight (snappy)")]
        [Tooltip("Flight duration from the draw pile to the hand slot. Uses unscaled time so it stays crisp during hit-stop / pauses. Phase 4 will move these onto CombatTimingProfile.")]
        [SerializeField] private float drawFlightSeconds = 0.45f;
        [Tooltip("Ghost scale at take-off; grows steadily to full size by the time it lands. A small value makes the card clearly emerge tiny from the pile.")]
        [SerializeField] private float drawStartScale = 0.3f;
        [Tooltip("Upward bow of the flight path, in overlay units. 0 = straight line.")]
        [SerializeField] private float drawArcHeight = 90f;
        [Tooltip("Scale overshoot played at the landing point to give a little pop.")]
        [SerializeField] private float landingPopScale = 1.08f;
        [SerializeField] private float landingPopSeconds = 0.08f;
        [Tooltip("Delay between consecutive cards in a staggered draw batch. Phase 4 will move this onto CombatTimingProfile.")]
        [SerializeField] private float drawStaggerSeconds = 0.12f;

        [Header("Discard Flight (front, sucked into the pile)")]
        [Tooltip("Flight duration from a hand slot to the discard pile. Slightly quicker than the draw.")]
        [SerializeField] private float discardFlightSeconds = 0.16f;
        [Tooltip("Delay between consecutive cards in a staggered discard batch.")]
        [SerializeField] private float discardStaggerSeconds = 0.045f;
        [Tooltip("Scale the front ghost shrinks to as it is sucked into the pile.")]
        [SerializeField] private float discardEndScale = 0.35f;
        [Tooltip("Upward bow of the discard flight path, in overlay units. 0 = straight line.")]
        [SerializeField] private float discardArcHeight = 60f;

        [Header("Exile Flight (front, dissolved away into the removed pile)")]
        [Tooltip("Flight duration from a hand slot to the exile/removed pile.")]
        [SerializeField] private float exileFlightSeconds = 0.22f;
        [Tooltip("Delay between consecutive cards in a staggered exile batch.")]
        [SerializeField] private float exileStaggerSeconds = 0.05f;
        [Tooltip("Scale the front ghost grows to as it dissolves (slightly larger for a 'burned away' feel).")]
        [SerializeField] private float exileEndScale = 1.06f;
        [Tooltip("Upward bow of the exile flight path, in overlay units. 0 = straight line.")]
        [SerializeField] private float exileArcHeight = 110f;
        [Tooltip("Tint of the dissolve wash overlaid on the exiled ghost.")]
        [SerializeField] private Color exileWashColor = new Color(0.55f, 0.25f, 0.85f, 1f);
        [Tooltip("Peak opacity of the dissolve wash (0-1).")]
        [SerializeField] private float exileWashStrength = 0.78f;

        [Header("Reshuffle (discard pile sweeps back into the draw pile)")]
        [Tooltip("Duration of the reshuffle sweep cut (bottom-right discard dock -> bottom-left draw dock).")]
        [SerializeField] private float reshuffleSeconds = 0.25f;
        [Tooltip("How many card backs sweep across during the reshuffle cut.")]
        [SerializeField] private int reshuffleGhostCount = 3;

        private RectTransform overlay;
        private Canvas canvas;
        private readonly List<RectTransform> ghostPool = new List<RectTransform>();
        private int activeFlights;
        private CombatTimingProfile timingProfile;

        public RectTransform Overlay => overlay;

        /// <summary>Per-card delay used when callers stagger a batch of draw flights.</summary>
        public float DrawStaggerSeconds => EffectiveDrawStaggerSeconds;

        /// <summary>Per-card delay used when callers stagger a batch of discard flights.</summary>
        public float DiscardStaggerSeconds => EffectiveDiscardStaggerSeconds;

        /// <summary>Per-card delay used when callers stagger a batch of exile flights.</summary>
        public float ExileStaggerSeconds => Mathf.Max(0f, exileStaggerSeconds);

        /// <summary>Duration of the reshuffle sweep — callers offset draw flights by this.</summary>
        public float ReshuffleSeconds => EffectiveReshuffleSeconds;

        /// <summary>True while at least one ghost is in the air (or waiting out its stagger delay).</summary>
        public bool IsFlying => activeFlights > 0;

        // Presentation beats fired on the same clock as the flights, so a future sound layer can subscribe
        // and stay in sync without re-deriving the timing. OnReshuffle is reserved for Phase 5.
        public event Action OnDrawFlightStart;
        public event Action OnDrawLand;
        public event Action OnDiscardLand;
        public event Action OnExileLand;
        public event Action OnReshuffle;

        /// <summary>
        /// Centralize draw/discard feel on a <see cref="CombatTimingProfile"/> asset. When null (or a value is
        /// left at its default) the animator keeps using its own inspector fields — purely additive override.
        /// </summary>
        public void ConfigureTimingProfile(CombatTimingProfile profile)
        {
            timingProfile = profile;
        }

        private float EffectiveDrawFlightSeconds => timingProfile != null ? timingProfile.DrawFlightSeconds : drawFlightSeconds;
        private float EffectiveDrawStartScale => timingProfile != null ? timingProfile.DrawStartScale : drawStartScale;
        private float EffectiveDrawArcHeight => timingProfile != null ? timingProfile.DrawArcHeight : drawArcHeight;
        private float EffectiveDrawStaggerSeconds => timingProfile != null ? timingProfile.DrawStaggerSeconds : drawStaggerSeconds;
        private float EffectiveLandingPopScale => timingProfile != null ? timingProfile.DrawLandingPopScale : landingPopScale;
        private float EffectiveLandingPopSeconds => timingProfile != null ? timingProfile.DrawLandingPopSeconds : landingPopSeconds;
        private float EffectiveDiscardFlightSeconds => timingProfile != null ? timingProfile.DiscardFlightSeconds : discardFlightSeconds;
        private float EffectiveDiscardStaggerSeconds => timingProfile != null ? timingProfile.DiscardStaggerSeconds : discardStaggerSeconds;
        private float EffectiveDiscardEndScale => timingProfile != null ? timingProfile.DiscardEndScale : discardEndScale;
        private float EffectiveDiscardArcHeight => timingProfile != null ? timingProfile.DiscardArcHeight : discardArcHeight;
        private float EffectiveReshuffleSeconds => timingProfile != null ? timingProfile.ReshuffleSeconds : reshuffleSeconds;

        /// <summary>Fire the reshuffle beat without a visual (e.g. when docks are missing).</summary>
        public void RaiseReshuffle() => OnReshuffle?.Invoke();

        // Deactivating this GameObject permanently stops every in-flight coroutine, so the DiscardFlight /
        // ExileFlight finally blocks that would Destroy the captured front-face clones never run, leaving a
        // frozen "used card" stranded at the hand-slot position (the captured pose). Clean those up here so
        // an interrupted flight can never leave an afterimage. See docs/card-draw-discard-animation-design.md.
        private void OnDisable()
        {
            CleanupGhosts();
        }

        /// <summary>
        /// Destroy orphaned front-face discard/exile clones left in the overlay by an interrupted flight, and
        /// return pooled card-back ghosts to rest. Safe to call only when no flight is active — otherwise it
        /// would nuke a legitimately in-flight clone. Callers gate on <see cref="IsFlying"/>.
        /// </summary>
        public void PurgeOrphanedGhosts()
        {
            if (activeFlights > 0)
            {
                return;
            }

            CleanupGhosts();
        }

        // Front-face clones (CaptureDiscardGhost, named "CardDiscardGhost") are one-shot Instantiated copies
        // that are never pooled, so any overlay child not in the pool is a leaked clone and is destroyed.
        // Pooled card-back ghosts are reset to inactive instead. Resets the in-flight counter since stopped
        // coroutines will not decrement it themselves.
        private void CleanupGhosts()
        {
            if (overlay != null)
            {
                for (var i = overlay.childCount - 1; i >= 0; i--)
                {
                    var child = overlay.GetChild(i) as RectTransform;
                    if (child == null)
                    {
                        continue;
                    }

                    if (ghostPool.Contains(child))
                    {
                        ReleaseGhost(child);
                    }
                    else
                    {
                        Destroy(child.gameObject);
                    }
                }
            }

            activeFlights = 0;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Debug hotkey: press F in play mode (with combat running so the hand has cards) to fire a
        // single draw flight from the draw pile to the first active hand slot. Editor / dev builds only.
        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.fKey.wasPressedThisFrame)
            {
                DebugFlyDrawPileToFirstSlot();
            }
        }
#endif

        /// <summary>Fly a face-down ghost from <paramref name="source"/> to <paramref name="target"/>.</summary>
        /// <param name="onArrive">Invoked the frame the ghost reaches the slot — reveal the real card here.</param>
        /// <param name="startDelaySeconds">Optional stagger delay before take-off. The flight's start/end
        /// coordinates are resolved when the ghost actually launches, so a delayed flight still uses the
        /// slot's final layout even if a layout pass happened during the wait.</param>
        public void PlayDrawFlight(RectTransform source, RectTransform target, Action onArrive = null, float startDelaySeconds = 0f)
        {
            if (source == null || target == null)
            {
                onArrive?.Invoke();
                return;
            }

            EnsureOverlay();
            StartCoroutine(DrawFlightWithDelay(source, target, onArrive, Mathf.Max(0f, startDelaySeconds)));
        }

        private IEnumerator DrawFlightWithDelay(RectTransform source, RectTransform target, Action onArrive, float delay)
        {
            activeFlights++;
            try
            {
                var waited = 0f;
                while (waited < delay)
                {
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }

                if (!TryWorldToOverlayLocal(source, out var fromLocal) || !TryWorldToOverlayLocal(target, out var toLocal))
                {
                    onArrive?.Invoke();
                    yield break;
                }

                var ghost = GetGhost();
                ghost.anchoredPosition = fromLocal;
                ghost.localScale = Vector3.one * EffectiveDrawStartScale;
                ghost.gameObject.SetActive(true);
                ghost.SetAsLastSibling();
                OnDrawFlightStart?.Invoke();
                yield return DrawFlightRoutine(ghost, fromLocal, toLocal, onArrive);
            }
            finally
            {
                activeFlights--;
            }
        }

        private IEnumerator DrawFlightRoutine(RectTransform ghost, Vector2 from, Vector2 to, Action onArrive)
        {
            var duration = Mathf.Max(0.01f, EffectiveDrawFlightSeconds);
            var startScale = EffectiveDrawStartScale;
            var arcHeight = EffectiveDrawArcHeight;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                var eased = EaseOutCubic(t);
                var pos = Vector2.LerpUnclamped(from, to, eased);
                // Parabolic arc that peaks mid-flight and returns to 0 at both ends.
                pos.y += arcHeight * 4f * eased * (1f - eased);
                ghost.anchoredPosition = pos;
                // Scale grows on raw (linear) progress so the card keeps visibly enlarging across the
                // whole flight and only reaches full size at the slot, rather than snapping big early.
                ghost.localScale = Vector3.one * Mathf.LerpUnclamped(startScale, 1f, t);
                yield return null;
            }

            ghost.anchoredPosition = to;
            ghost.localScale = Vector3.one;
            onArrive?.Invoke();
            OnDrawLand?.Invoke();

            yield return LandingPop(ghost);
            ReleaseGhost(ghost);
        }

        /// <summary>
        /// Clone a hand slot's current visuals into the overlay so the real card appears to fly to the
        /// discard pile (front-facing, no flip). Must be called BEFORE the hand re-lays-out, while the
        /// slot still shows the outgoing card. The returned ghost is detached from the lane, so its
        /// position is frozen even after the slot is removed/reused. Fly it later with
        /// <see cref="FlyDiscardGhost"/>.
        /// </summary>
        public RectTransform CaptureDiscardGhost(RectTransform sourceSlot)
        {
            if (sourceSlot == null)
            {
                return null;
            }

            EnsureOverlay();
            if (!TryWorldToOverlayLocal(sourceSlot, out var local))
            {
                return null;
            }

            var clone = Instantiate(sourceSlot.gameObject, overlay, false);
            clone.name = "CardDiscardGhost";
            StripToVisual(clone);

            var rect = clone.transform as RectTransform ?? clone.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = sourceSlot.rect.size;
            rect.anchoredPosition = local;
            rect.localScale = Vector3.one;
            rect.SetAsLastSibling();

            // Unity '??' ignores fake-null, so add the CanvasGroup via an explicit Unity-aware null check
            // (otherwise a clone without one would throw MissingComponentException and leak the ghost).
            var group = clone.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = clone.AddComponent<CanvasGroup>();
            }
            group.alpha = 1f;
            group.blocksRaycasts = false;
            group.interactable = false;

            clone.SetActive(true);
            return rect;
        }

        /// <summary>Fly a captured front ghost into the pile, shrinking and fading, then destroy it.</summary>
        public void FlyDiscardGhost(RectTransform ghost, RectTransform targetDock, float startDelaySeconds = 0f)
        {
            if (ghost == null)
            {
                return;
            }

            if (targetDock == null)
            {
                Destroy(ghost.gameObject);
                return;
            }

            EnsureOverlay();
            StartCoroutine(DiscardFlightRoutine(ghost, targetDock, Mathf.Max(0f, startDelaySeconds)));
        }

        private IEnumerator DiscardFlightRoutine(RectTransform ghost, RectTransform targetDock, float delay)
        {
            activeFlights++;
            try
            {
                var waited = 0f;
                while (waited < delay)
                {
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }

                if (ghost == null)
                {
                    yield break;
                }

                var from = ghost.anchoredPosition;
                if (!TryWorldToOverlayLocal(targetDock, out var to))
                {
                    yield break;
                }

                var group = ghost.GetComponent<CanvasGroup>();
                var duration = Mathf.Max(0.01f, EffectiveDiscardFlightSeconds);
                var endScale = EffectiveDiscardEndScale;
                var arcHeight = EffectiveDiscardArcHeight;
                var elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    var t = Mathf.Clamp01(elapsed / duration);
                    var eased = EaseInCubic(t);
                    var pos = Vector2.LerpUnclamped(from, to, eased);
                    pos.y += arcHeight * 4f * eased * (1f - eased);
                    ghost.anchoredPosition = pos;
                    ghost.localScale = Vector3.one * Mathf.LerpUnclamped(1f, endScale, eased);
                    if (group != null)
                    {
                        group.alpha = 1f - t;
                    }

                    yield return null;
                }

                OnDiscardLand?.Invoke();
            }
            finally
            {
                activeFlights--;
                if (ghost != null)
                {
                    Destroy(ghost.gameObject);
                }
            }
        }

        /// <summary>
        /// Fly an exiled (removed-from-deck) card front to the removed pile, overlaying a dissolve wash so it
        /// reads as "burned away" rather than a normal discard. Mirrors <see cref="FlyDiscardGhost"/>.
        /// </summary>
        public void FlyExileGhost(RectTransform ghost, RectTransform targetDock, float startDelaySeconds = 0f)
        {
            if (ghost == null)
            {
                return;
            }

            if (targetDock == null)
            {
                Destroy(ghost.gameObject);
                return;
            }

            EnsureOverlay();
            StartCoroutine(ExileFlightRoutine(ghost, targetDock, Mathf.Max(0f, startDelaySeconds)));
        }

        private IEnumerator ExileFlightRoutine(RectTransform ghost, RectTransform targetDock, float delay)
        {
            activeFlights++;
            try
            {
                var waited = 0f;
                while (waited < delay)
                {
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }

                if (ghost == null)
                {
                    yield break;
                }

                var from = ghost.anchoredPosition;
                if (!TryWorldToOverlayLocal(targetDock, out var to))
                {
                    yield break;
                }

                var group = ghost.GetComponent<CanvasGroup>();
                var wash = EnsureExileWash(ghost);
                var duration = Mathf.Max(0.01f, exileFlightSeconds);
                var endScale = exileEndScale;
                var arcHeight = Mathf.Abs(exileArcHeight);
                var peakWash = Mathf.Clamp01(exileWashStrength);
                var elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    var t = Mathf.Clamp01(elapsed / duration);
                    var eased = EaseOutCubic(t);
                    var pos = Vector2.LerpUnclamped(from, to, eased);
                    pos.y += arcHeight * 4f * eased * (1f - eased);
                    ghost.anchoredPosition = pos;
                    ghost.localScale = Vector3.one * Mathf.LerpUnclamped(1f, endScale, eased);
                    if (wash != null)
                    {
                        var color = exileWashColor;
                        color.a = peakWash * Mathf.Clamp01(t / 0.45f);
                        wash.color = color;
                    }

                    if (group != null)
                    {
                        group.alpha = t < 0.5f ? 1f : Mathf.InverseLerp(1f, 0.5f, t);
                    }

                    yield return null;
                }

                OnExileLand?.Invoke();
            }
            finally
            {
                activeFlights--;
                if (ghost != null)
                {
                    Destroy(ghost.gameObject);
                }
            }
        }

        // Lazily creates a full-rect Image child ("ExileWash") on the ghost used as the dissolve tint overlay.
        private static Image EnsureExileWash(RectTransform ghost)
        {
            if (ghost == null)
            {
                return null;
            }

            if (ghost.Find("ExileWash") is RectTransform existing)
            {
                return existing.GetComponent<Image>();
            }

            var washObject = new GameObject("ExileWash", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = washObject.GetComponent<RectTransform>();
            rect.SetParent(ghost, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.SetAsLastSibling();

            var image = washObject.GetComponent<Image>();
            image.raycastTarget = false;
            image.color = new Color(0f, 0f, 0f, 0f);
            return image;
        }

        /// <summary>
        /// Play the reshuffle cut: a few card backs sweep from the discard dock (bottom-right) into the draw
        /// dock (bottom-left), selling the discard pile being shuffled back. Draw flights should be delayed
        /// until this finishes. Fires <see cref="OnReshuffle"/> at the start of the visible sweep.
        /// </summary>
        public void PlayReshuffle(RectTransform fromDock, RectTransform toDock, float startDelaySeconds = 0f)
        {
            if (fromDock == null || toDock == null)
            {
                RaiseReshuffle();
                return;
            }

            EnsureOverlay();
            StartCoroutine(ReshuffleRoutine(fromDock, toDock, Mathf.Max(0f, startDelaySeconds)));
        }

        private IEnumerator ReshuffleRoutine(RectTransform fromDock, RectTransform toDock, float delay)
        {
            activeFlights++;
            var ghosts = new List<RectTransform>();
            try
            {
                var waited = 0f;
                while (waited < delay)
                {
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }

                if (!TryWorldToOverlayLocal(fromDock, out var from) || !TryWorldToOverlayLocal(toDock, out var to))
                {
                    OnReshuffle?.Invoke();
                    yield break;
                }

                OnReshuffle?.Invoke();

                var count = Mathf.Max(1, reshuffleGhostCount);
                for (var i = 0; i < count; i++)
                {
                    var ghost = GetGhost();
                    ghost.anchoredPosition = from;
                    ghost.localScale = Vector3.one * Mathf.Lerp(0.8f, 1f, count <= 1 ? 1f : i / (float)(count - 1));
                    ghost.gameObject.SetActive(true);
                    ghost.SetAsLastSibling();
                    ghosts.Add(ghost);
                }

                var duration = Mathf.Max(0.01f, EffectiveReshuffleSeconds);
                var perGhostLead = count > 1 ? Mathf.Min(0.05f, duration * 0.3f / count) : 0f;
                var arc = -Mathf.Abs(EffectiveDiscardArcHeight); // bow downward for the cross-screen sweep
                var total = duration + perGhostLead * (count - 1);
                var elapsed = 0f;
                while (elapsed < total)
                {
                    elapsed += Time.unscaledDeltaTime;
                    for (var i = 0; i < ghosts.Count; i++)
                    {
                        var ghost = ghosts[i];
                        if (ghost == null)
                        {
                            continue;
                        }

                        var local = Mathf.Clamp01((elapsed - perGhostLead * i) / duration);
                        var eased = EaseOutCubic(local);
                        var pos = Vector2.LerpUnclamped(from, to, eased);
                        pos.y += arc * 4f * eased * (1f - eased);
                        ghost.anchoredPosition = pos;

                        // Unity '??' does not honor fake-null of a missing UnityEngine.Object, so an absent
                        // CanvasGroup would slip through and throw MissingComponentException on set_alpha. Use an
                        // explicit Unity-aware null check so the component is actually added.
                        var cg = ghost.GetComponent<CanvasGroup>();
                        if (cg == null)
                        {
                            cg = ghost.gameObject.AddComponent<CanvasGroup>();
                        }
                        cg.alpha = local < 0.85f ? 1f : Mathf.InverseLerp(1f, 0.85f, local); // fade out near arrival
                    }

                    yield return null;
                }
            }
            finally
            {
                activeFlights--;
                for (var i = 0; i < ghosts.Count; i++)
                {
                    ReleaseGhost(ghosts[i]);
                }
            }
        }

        // Destroy interaction/logic so the clone is a pure visual; never blocks input, never hovers.
        private static void StripToVisual(GameObject root)
        {
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour is HandCardInteraction || behaviour is CardHoverGlow || behaviour is CardUnavailableFeedback)
                {
                    Destroy(behaviour);
                }
            }

            foreach (var selectable in root.GetComponentsInChildren<Selectable>(true))
            {
                Destroy(selectable);
            }

            foreach (var shadow in root.GetComponentsInChildren<Shadow>(true))
            {
                Destroy(shadow); // Outline derives from Shadow — drop hover/selection outlines.
            }

            foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
            {
                graphic.raycastTarget = false;
            }
        }

        private static float EaseInCubic(float t)
        {
            return t * t * t;
        }

        private IEnumerator LandingPop(RectTransform ghost)
        {
            var duration = Mathf.Max(0f, EffectiveLandingPopSeconds);
            var popScale = EffectiveLandingPopScale;
            if (duration <= 0f || popScale <= 1f)
            {
                yield break;
            }

            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                // Up then back down: sin(pi*t) peaks at t=0.5.
                var pop = 1f + (popScale - 1f) * Mathf.Sin(Mathf.PI * t);
                ghost.localScale = Vector3.one * pop;
                yield return null;
            }

            ghost.localScale = Vector3.one;
        }

        private static float EaseOutCubic(float t)
        {
            var inv = 1f - t;
            return 1f - inv * inv * inv;
        }

        private void EnsureOverlay()
        {
            canvas = canvas != null ? canvas : GetComponentInParent<Canvas>();
            if (overlay != null)
            {
                overlay.SetAsLastSibling();
                return;
            }

            var existing = transform.Find(OverlayName);
            var go = existing != null ? existing.gameObject : new GameObject(OverlayName, typeof(RectTransform));
            overlay = go.GetComponent<RectTransform>();
            if (overlay.parent != transform)
            {
                overlay.SetParent(transform, false);
            }

            overlay.anchorMin = Vector2.zero;
            overlay.anchorMax = Vector2.one;
            overlay.pivot = new Vector2(0.5f, 0.5f);
            overlay.offsetMin = Vector2.zero;
            overlay.offsetMax = Vector2.zero;
            overlay.localScale = Vector3.one;
            overlay.SetAsLastSibling();

            // The overlay is a passthrough container; it must never block card input.
            var blocker = go.GetComponent<Graphic>();
            if (blocker != null)
            {
                blocker.raycastTarget = false;
            }
        }

        private RectTransform GetGhost()
        {
            for (var i = 0; i < ghostPool.Count; i++)
            {
                var pooled = ghostPool[i];
                if (pooled != null && !pooled.gameObject.activeSelf)
                {
                    return pooled;
                }
            }

            var ghost = CreateGhost();
            ghostPool.Add(ghost);
            return ghost;
        }

        private RectTransform CreateGhost()
        {
            RectTransform rect;
            if (cardBackPrefab != null)
            {
                var instance = Instantiate(cardBackPrefab, overlay, false);
                rect = instance.transform as RectTransform ?? instance.AddComponent<RectTransform>();
                // Respect the authored prefab's size/pivot; only force center anchoring so the
                // anchoredPosition tween is well-defined, and make sure it never blocks card input.
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                foreach (var graphic in instance.GetComponentsInChildren<Graphic>(true))
                {
                    graphic.raycastTarget = false;
                }
            }
            else
            {
                var go = new GameObject("CardFlightGhost", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                rect = go.GetComponent<RectTransform>();
                rect.SetParent(overlay, false);
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = ghostSize;
                var image = go.GetComponent<Image>();
                image.sprite = cardBackSprite;
                image.raycastTarget = false;
                image.preserveAspect = true;
            }

            rect.gameObject.SetActive(false);
            return rect;
        }

        private void ReleaseGhost(RectTransform ghost)
        {
            if (ghost == null)
            {
                return;
            }

            // Reshuffle reuses these pooled back ghosts and fades them via a CanvasGroup; reset alpha so a
            // later draw flight reusing the same ghost is fully opaque.
            var cg = ghost.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.alpha = 1f;
            }

            ghost.localScale = Vector3.one;
            ghost.gameObject.SetActive(false);
        }

        private bool TryWorldToOverlayLocal(RectTransform target, out Vector2 local)
        {
            local = Vector2.zero;
            if (overlay == null || target == null)
            {
                return false;
            }

            var cam = GetEventCamera();
            // Use the rect's geometric centre (not its pivot) so the start/end points sit dead-centre on
            // the dock and the slot regardless of pivot — the draw pile dock pivots at its bottom edge.
            var worldCenter = target.TransformPoint(target.rect.center);
            var screen = RectTransformUtility.WorldToScreenPoint(cam, worldCenter);
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(overlay, screen, cam, out local);
        }

        private Camera GetEventCamera()
        {
            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return null;
            }

            return canvas.worldCamera;
        }

        // --- Phase 1 PoC trigger: fly one ghost from the draw pile to the first active hand slot. ---
        public bool DebugFlyDrawPileToFirstSlot()
        {
            var source = FindDeepChild(transform, "DrawPileDock") ?? FindDeepChild(transform, "DrawPile_Icon");
            var target = FindFirstActiveHandSlot();
            if (source == null || target == null)
            {
                Debug.LogWarning($"[CardFlight] PoC missing refs: source={(source != null)}, target={(target != null)}");
                return false;
            }

            PlayDrawFlight(source, target, () => Debug.Log("[CardFlight] arrived — reveal point."));
            return true;
        }

        private RectTransform FindFirstActiveHandSlot()
        {
            var lane = GetComponentInChildren<GameplayCardLaneView>(true);
            if (lane == null)
            {
                return null;
            }

            foreach (var slot in lane.MoveCardSlots)
            {
                if (slot != null && slot.gameObject.activeInHierarchy)
                {
                    return slot.transform as RectTransform;
                }
            }

            foreach (var slot in lane.ActionCardSlots)
            {
                if (slot != null && slot.gameObject.activeInHierarchy)
                {
                    return slot.transform as RectTransform;
                }
            }

            return null;
        }

        private static RectTransform FindDeepChild(Transform root, string childName)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == childName)
            {
                return root as RectTransform;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindDeepChild(root.GetChild(i), childName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}

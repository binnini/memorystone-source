using System;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// Which clock a duration is measured against. Nothing in this project sets
    /// <c>ParticleSystem.main.useUnscaledTime</c> or <c>Animator.updateMode</c>, so particles and animation
    /// run on the scaled clock and stretch under hit-stop / the debug slow-motion slider, while
    /// <c>AudioSource</c> ignores <c>Time.timeScale</c> entirely. A consumer that adds a VFX length to an SFX
    /// length without knowing this gets a wrong answer the moment hit-stop fires, so the clock travels with
    /// the number instead of being assumed. See docs/presentation-duration-data-plan.md §2.5.
    /// </summary>
    public enum PresentationClock
    {
        /// <summary>Affected by <c>Time.timeScale</c> (particles, animation).</summary>
        Scaled,

        /// <summary>Wall-clock; unaffected by <c>Time.timeScale</c> (audio).</summary>
        Unscaled
    }

    /// <summary>
    /// How long one presentation asset plays. Pure by design: this assembly has
    /// <c>noEngineReferences</c>, so the value can be asserted in EditMode without an Animator, a
    /// ParticleSystem, or a running player — which is the entire point of the track. The runtime-only
    /// derivation this replaces shipped disabled *and* broken for exactly that reason
    /// (docs/presentation-duration-data-plan.md §2.2).
    ///
    /// Two numbers are kept apart on purpose (§3):
    /// <list type="bullet">
    /// <item><see cref="MeasuredSeconds"/> is a fact about the asset — its natural length at speed 1.
    /// It is written by the bake tool and must never be hand-edited.</item>
    /// <item><see cref="AuthoredSeconds"/> is a designer's intent — "keep it on screen this long". It is
    /// the only field authored by hand, and it wins when present.</item>
    /// </list>
    /// Merging them would mean a designer trimming a look also silently retimes every consumer, and would
    /// leave the drift audit with nothing to compare against.
    /// </summary>
    public readonly struct PresentationDuration
    {
        private const float MinimumSpeed = 0.0001f;

        private PresentationDuration(
            float measuredSeconds,
            bool hasAuthored,
            float authoredSeconds,
            bool measurable,
            PresentationClock clock,
            float destroyAfterSeconds)
        {
            MeasuredSeconds = measuredSeconds;
            HasAuthored = hasAuthored;
            AuthoredSeconds = authoredSeconds;
            Measurable = measurable;
            Clock = clock;
            DestroyAfterSeconds = destroyAfterSeconds;
        }

        /// <summary>
        /// The asset's natural length at speed 1, or 0 when nothing has measured it yet. Never divided by a
        /// playback speed — that happens in <see cref="TryResolveSeconds"/>, so the stored number stays a
        /// property of the asset rather than of one call site.
        /// </summary>
        public float MeasuredSeconds { get; }

        /// <summary>True when a designer authored an explicit on-screen duration.</summary>
        public bool HasAuthored { get; }

        /// <summary>
        /// The authored on-screen duration in final seconds. Unlike <see cref="MeasuredSeconds"/> this is
        /// NOT divided by the playback speed on resolve — it already states the intended wall result. This
        /// mirrors how <c>EffectVfxCatalog.Entry.LifetimeOverride</c> has always been treated.
        /// </summary>
        public float AuthoredSeconds { get; }

        /// <summary>
        /// False when the asset has no natural length at all — a looping status VFX or an idle/move
        /// animation clip. For those, measuring is meaningless and <see cref="AuthoredSeconds"/> is the only
        /// possible answer. Distinct from "measurable but not yet measured".
        /// </summary>
        public bool Measurable { get; }

        /// <summary>Which clock <see cref="MeasuredSeconds"/> / <see cref="AuthoredSeconds"/> refer to.</summary>
        public PresentationClock Clock { get; }

        /// <summary>
        /// The cue's own hard cut-off — for VFX, <c>EffectVfxCatalog.Entry.LifetimeOverride</c>, after which
        /// the spawned object is destroyed. 0 = none.
        ///
        /// Included in resolution because measurement proved it is the real end of the effect far more often
        /// than the natural length is (plan §6.6): 19 of the 28 cues that have both values are cut SHORT by
        /// it, by up to 4.5s, and 37 cues have no measurable length at all (their third-party prefabs contain
        /// looping ParticleSystems) so this timer is the only thing that ends them. Reading it here does not
        /// modify or reinterpret the field itself — D7 stands.
        /// </summary>
        public float DestroyAfterSeconds { get; }

        /// <summary>
        /// True when a duration can actually be produced. Checked instead of comparing a resolved value
        /// against 0: "nobody knows how long this is" and "this takes no time" must not collapse into the
        /// same number, or a consumer reads an unmeasured cue as instantaneous.
        /// </summary>
        public bool IsKnown
            => HasAuthored || (Measurable && MeasuredSeconds > 0f) || DestroyAfterSeconds > 0f;

        /// <summary>A measurable asset that has not been measured yet — the state every cue starts in.</summary>
        public static PresentationDuration Unmeasured(PresentationClock clock)
            => new PresentationDuration(0f, false, 0f, true, clock, 0f);

        /// <summary>An asset with no natural length (looping VFX, looping animation clip).</summary>
        public static PresentationDuration Unmeasurable(PresentationClock clock)
            => new PresentationDuration(0f, false, 0f, false, clock, 0f);

        /// <summary>
        /// Builds a duration from raw authored/baked numbers, using the CSV convention that 0 means "unset"
        /// for both columns. Negative and non-finite inputs are treated as unset rather than trusted, so a
        /// malformed cell cannot turn into a negative wait downstream.
        /// </summary>
        public static PresentationDuration Create(
            float measuredSeconds,
            float authoredSeconds,
            bool measurable,
            PresentationClock clock,
            float destroyAfterSeconds = 0f)
        {
            var measured = IsUsable(measuredSeconds) ? measuredSeconds : 0f;
            var hasAuthored = IsUsable(authoredSeconds);
            return new PresentationDuration(
                measurable ? measured : 0f,
                hasAuthored,
                hasAuthored ? authoredSeconds : 0f,
                measurable,
                clock,
                IsUsable(destroyAfterSeconds) ? destroyAfterSeconds : 0f);
        }

        /// <summary>Copy with the measured length replaced (the struct is immutable). Used by the bake tool.</summary>
        public PresentationDuration WithMeasured(float measuredSeconds)
            => Create(measuredSeconds, HasAuthored ? AuthoredSeconds : 0f, Measurable, Clock, DestroyAfterSeconds);

        /// <summary>Copy with the authored length replaced (the struct is immutable).</summary>
        public PresentationDuration WithAuthored(float authoredSeconds)
            => Create(MeasuredSeconds, authoredSeconds, Measurable, Clock, DestroyAfterSeconds);

        /// <summary>
        /// Resolves how long the cue is actually on screen, returning false when that is unknown (see
        /// <see cref="IsKnown"/>). Precedence, highest first:
        /// <list type="number">
        /// <item><see cref="AuthoredSeconds"/> — a designer's explicit intent is final and is returned
        /// untouched, neither divided by speed nor capped. It is the escape hatch from everything below.</item>
        /// <item><see cref="MeasuredSeconds"/> / speed, then clamped by <see cref="DestroyAfterSeconds"/> —
        /// a cue at N× finishes in 1/N of its natural length, but it cannot outlive its destroy timer.</item>
        /// <item><see cref="DestroyAfterSeconds"/> alone — the only answer for the 37 cues whose prefabs
        /// contain looping ParticleSystems and so have no natural end (plan §6.6).</item>
        /// </list>
        /// A non-positive or non-finite speed is read as 1, matching how the VFX catalog already normalizes
        /// its playbackSpeed field.
        /// </summary>
        public bool TryResolveSeconds(float speedMultiplier, out float seconds)
        {
            if (HasAuthored)
            {
                seconds = AuthoredSeconds;
                return true;
            }

            if (Measurable && MeasuredSeconds > 0f)
            {
                var natural = MeasuredSeconds / NormalizeSpeed(speedMultiplier);
                // The destroy timer is a cut-off, never an extension: a cue whose timer outlasts its
                // particles has already stopped being visible, so taking the max would report dead air.
                seconds = DestroyAfterSeconds > 0f ? Math.Min(natural, DestroyAfterSeconds) : natural;
                return true;
            }

            if (DestroyAfterSeconds > 0f)
            {
                seconds = DestroyAfterSeconds;
                return true;
            }

            seconds = 0f;
            return false;
        }

        /// <summary>
        /// Resolves how long this cue occupies the timeline from the moment it is dispatched: the authored
        /// start delay plus the play length. Provided as its own method because the delay and the length are
        /// easy to confuse — <c>ResolveTraceTailSeconds</c> currently estimates a cue's tail from its
        /// *delay* alone, which is that mistake already in the codebase (plan §2.3 ①).
        ///
        /// The delay is added on the same clock as the length; a caller mixing a scaled delay with an
        /// unscaled length is a bug this type cannot catch, which is why <see cref="Clock"/> is public.
        /// </summary>
        public bool TryResolveTotalOccupancySeconds(float delaySeconds, float speedMultiplier, out float seconds)
        {
            if (!TryResolveSeconds(speedMultiplier, out var length))
            {
                seconds = 0f;
                return false;
            }

            seconds = (IsUsable(delaySeconds) ? delaySeconds : 0f) + length;
            return true;
        }

        private static float NormalizeSpeed(float speedMultiplier)
        {
            if (float.IsNaN(speedMultiplier) || float.IsInfinity(speedMultiplier) || speedMultiplier <= 0f)
            {
                return 1f;
            }

            return Math.Max(MinimumSpeed, speedMultiplier);
        }

        private static bool IsUsable(float seconds)
            => seconds > 0f && !float.IsNaN(seconds) && !float.IsInfinity(seconds);
    }
}

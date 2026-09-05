// NOTE(공개 발췌): 이 파일은 서드파티 에셋 「Cartoon FX Remaster (JMO Assets)」의 프리팹·타입을 경로/이름으로만 참조한다. 해당 에셋은 이 리포에 포함되지 않는다.
using System.Collections.Generic;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// One ParticleSystem's timing, lifted out of Unity so the arithmetic can be tested. Mirrors
    /// <c>ParticleSystem.main</c>: <see cref="DurationSeconds"/> is <c>main.duration</c> and
    /// <see cref="StartLifetimeMaxSeconds"/> is <c>main.startLifetime.constantMax</c> — the max is used
    /// because start lifetime may be a random range and the last particle spawned is what ends the effect.
    /// </summary>
    public readonly struct ParticleTiming
    {
        public ParticleTiming(float durationSeconds, float startLifetimeMaxSeconds, bool looping)
        {
            DurationSeconds = durationSeconds;
            StartLifetimeMaxSeconds = startLifetimeMaxSeconds;
            Looping = looping;
        }

        public float DurationSeconds { get; }
        public float StartLifetimeMaxSeconds { get; }

        /// <summary><c>main.loop</c>. A looping system never ends on its own.</summary>
        public bool Looping { get; }
    }

    /// <summary>Outcome of measuring one VFX prefab.</summary>
    public readonly struct ParticleMeasurement
    {
        public ParticleMeasurement(float naturalSeconds, int particleCount, int loopingCount)
        {
            NaturalSeconds = naturalSeconds;
            ParticleCount = particleCount;
            LoopingCount = loopingCount;
        }

        /// <summary>Longest emit+lifetime across the prefab's systems, at playback speed 1. 0 when none.</summary>
        public float NaturalSeconds { get; }

        public int ParticleCount { get; }

        /// <summary>How many of the systems loop.</summary>
        public int LoopingCount { get; }

        /// <summary>
        /// True when the prefab has no ParticleSystem at all (a mesh/animation-driven effect, or a
        /// third-party prefab like CFXR that manages its own lifetime). The natural length is unknown rather
        /// than zero, so the cue needs an authored duration — writing 0 would read as "instantaneous".
        /// </summary>
        public bool HasNoParticles => ParticleCount == 0;

        /// <summary>
        /// True when at least one system loops, so the prefab never finishes on its own and
        /// <see cref="NaturalSeconds"/> is only the period of its longest cycle, not an end time. Such a cue
        /// is not measurable in the sense this track means and must be authored (plan §4.5).
        /// </summary>
        public bool HasLoopingParticles => LoopingCount > 0;

        /// <summary>Whether <see cref="NaturalSeconds"/> may be stored as a measured length.</summary>
        public bool IsMeasurable => !HasNoParticles && !HasLoopingParticles && NaturalSeconds > 0f;
    }

    /// <summary>
    /// Pure measurement arithmetic for the bake step (docs/presentation-duration-data-plan.md P2). Kept out
    /// of the editor tool so the rules — what counts as unmeasurable, how pitch stretches a clip — are
    /// asserted in EditMode rather than trusted.
    ///
    /// ⚠️ This is deliberately NOT the same computation as
    /// <c>EffectPresentationController.ResolvePrefabLifetime</c>. That one produces a *destroy timer*: it
    /// starts from a serialized floor and adds 0.25s of padding so a cue is never cut off. This one produces
    /// the asset's natural length. Folding them together would change shipping destroy timings.
    /// </summary>
    public static class PresentationDurationMeasurement
    {
        private const float MinimumPitch = 0.01f;

        /// <summary>
        /// Longest emit+lifetime across a prefab's particle systems, at speed 1. The playback-speed division
        /// is intentionally NOT applied here — the stored number is a property of the asset, and speed is
        /// applied on resolve (<see cref="PresentationDuration.TryResolveSeconds"/>).
        /// </summary>
        public static ParticleMeasurement MeasureParticles(IReadOnlyList<ParticleTiming> particles)
        {
            if (particles == null || particles.Count == 0)
            {
                return new ParticleMeasurement(0f, 0, 0);
            }

            var longest = 0f;
            var looping = 0;
            for (var i = 0; i < particles.Count; i++)
            {
                var particle = particles[i];
                if (particle.Looping)
                {
                    looping++;
                }

                var total = Sanitize(particle.DurationSeconds) + Sanitize(particle.StartLifetimeMaxSeconds);
                if (total > longest)
                {
                    longest = total;
                }
            }

            return new ParticleMeasurement(longest, particles.Count, looping);
        }

        /// <summary>
        /// Longest possible playback of an audio clip: playback sets
        /// <c>AudioSource.pitch = Random.Range(pitchMin, pitchMax)</c> and pitch divides duration, so the
        /// SLOWEST pitch (<paramref name="pitchMin"/>) gives the upper bound. A cue with a pitch range has no
        /// single length, which is why this is documented as a bound rather than a measurement (plan §4.3).
        /// </summary>
        public static float ResolveAudioUpperBoundSeconds(float clipLengthSeconds, float pitchMin)
        {
            var length = Sanitize(clipLengthSeconds);
            if (length <= 0f)
            {
                return 0f;
            }

            var pitch = Sanitize(pitchMin);
            return pitch < MinimumPitch ? length : length / pitch;
        }

        private static float Sanitize(float value)
            => value > 0f && !float.IsNaN(value) && !float.IsInfinity(value) ? value : 0f;
    }
}

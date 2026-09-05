// NOTE(공개 발췌): 이 파일은 서드파티 에셋 「Cartoon FX Remaster (JMO Assets)」의 프리팹·타입을 경로/이름으로만 참조한다. 해당 에셋은 이 리포에 포함되지 않는다.
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// The measurement rules used by the P2 bake tool. Pure so the "what cannot be measured" policy is
    /// asserted rather than trusted to an editor script nobody runs in CI.
    /// </summary>
    public sealed class PresentationDurationMeasurementTests
    {
        [Test]
        public void NaturalLengthIsTheLongestEmitPlusLifetime()
        {
            var result = PresentationDurationMeasurement.MeasureParticles(new[]
            {
                new ParticleTiming(1.0f, 0.5f, looping: false),
                new ParticleTiming(0.2f, 2.0f, looping: false),
                new ParticleTiming(0.4f, 0.4f, looping: false)
            });

            // The last particle spawned at the end of the emit window is what ends the effect, so it is
            // duration + startLifetime, maxed across systems: 0.2 + 2.0 wins over 1.0 + 0.5.
            Assert.That(result.NaturalSeconds, Is.EqualTo(2.2f).Within(0.0001f));
            Assert.That(result.ParticleCount, Is.EqualTo(3));
            Assert.That(result.IsMeasurable, Is.True);
        }

        [Test]
        public void OrderDoesNotAffectTheResult()
        {
            var ascending = PresentationDurationMeasurement.MeasureParticles(new[]
            {
                new ParticleTiming(1.0f, 0f, false),
                new ParticleTiming(1.1f, 0f, false)
            });
            var descending = PresentationDurationMeasurement.MeasureParticles(new[]
            {
                new ParticleTiming(1.1f, 0f, false),
                new ParticleTiming(1.0f, 0f, false)
            });

            // Guards against the order-dependent accumulation that EffectPresentationController's destroy
            // timer has (it compares each raw duration against a running padded max, so a longer system can
            // be skipped). A measurement must be a property of the asset, not of component order.
            Assert.That(ascending.NaturalSeconds, Is.EqualTo(descending.NaturalSeconds).Within(0.0001f));
            Assert.That(ascending.NaturalSeconds, Is.EqualTo(1.1f).Within(0.0001f));
        }

        [Test]
        public void PrefabWithNoParticleSystemIsNotMeasurable()
        {
            var result = PresentationDurationMeasurement.MeasureParticles(new ParticleTiming[0]);

            Assert.That(result.HasNoParticles, Is.True);
            Assert.That(result.IsMeasurable, Is.False, "A mesh/CFXR-driven effect must be authored, not assumed 0.");
            Assert.That(result.NaturalSeconds, Is.EqualTo(0f));
        }

        [Test]
        public void AnyLoopingSystemMakesThePrefabUnmeasurable()
        {
            var result = PresentationDurationMeasurement.MeasureParticles(new[]
            {
                new ParticleTiming(1f, 0.5f, looping: false),
                new ParticleTiming(2f, 0.5f, looping: true)
            });

            // A looping system never stops emitting, so the prefab has no end of its own — main.duration is
            // its cycle period, not a finish time. Storing that as a length would be a lie.
            Assert.That(result.HasLoopingParticles, Is.True);
            Assert.That(result.LoopingCount, Is.EqualTo(1));
            Assert.That(result.IsMeasurable, Is.False);
        }

        [Test]
        public void MalformedParticleTimingsAreTreatedAsZeroContribution()
        {
            var result = PresentationDurationMeasurement.MeasureParticles(new[]
            {
                new ParticleTiming(float.NaN, float.PositiveInfinity, false),
                new ParticleTiming(-5f, 0.5f, false)
            });

            Assert.That(result.NaturalSeconds, Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void NullParticleListIsNotMeasurable()
        {
            var result = PresentationDurationMeasurement.MeasureParticles(null);

            Assert.That(result.HasNoParticles, Is.True);
            Assert.That(result.IsMeasurable, Is.False);
        }

        [Test]
        public void AudioUpperBoundUsesTheSlowestPitch()
        {
            // pitch divides duration, so the slowest pitch is the longest play. 1.0s at 0.8x lasts 1.25s.
            Assert.That(
                PresentationDurationMeasurement.ResolveAudioUpperBoundSeconds(1f, 0.8f),
                Is.EqualTo(1.25f).Within(0.0001f));

            Assert.That(
                PresentationDurationMeasurement.ResolveAudioUpperBoundSeconds(1f, 2f),
                Is.EqualTo(0.5f).Within(0.0001f));

            Assert.That(
                PresentationDurationMeasurement.ResolveAudioUpperBoundSeconds(1.5f, 1f),
                Is.EqualTo(1.5f).Within(0.0001f));
        }

        [Test]
        public void AudioUpperBoundRejectsUnusableInputInsteadOfDividingByZero()
        {
            Assert.That(PresentationDurationMeasurement.ResolveAudioUpperBoundSeconds(0f, 1f), Is.EqualTo(0f));
            Assert.That(PresentationDurationMeasurement.ResolveAudioUpperBoundSeconds(-1f, 1f), Is.EqualTo(0f));
            Assert.That(PresentationDurationMeasurement.ResolveAudioUpperBoundSeconds(float.NaN, 1f), Is.EqualTo(0f));

            // A zero/garbage pitch must fall back to the raw clip length, not produce Infinity.
            foreach (var pitch in new[] { 0f, -1f, float.NaN })
            {
                Assert.That(
                    PresentationDurationMeasurement.ResolveAudioUpperBoundSeconds(2f, pitch),
                    Is.EqualTo(2f).Within(0.0001f),
                    $"pitch {pitch} must fall back to the clip length.");
            }
        }
    }
}

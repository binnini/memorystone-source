using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// The duration contract from docs/presentation-duration-data-plan.md P1. These run without an Animator,
    /// ParticleSystem, or player — the property that the runtime-only derivation this replaces never had.
    /// </summary>
    public sealed class PresentationDurationTests
    {
        [Test]
        public void AuthoredLengthWinsOverMeasuredAndIsNotDividedBySpeed()
        {
            var duration = PresentationDuration.Create(
                measuredSeconds: 4f,
                authoredSeconds: 1.2f,
                measurable: true,
                clock: PresentationClock.Scaled);

            Assert.That(duration.HasAuthored, Is.True);
            Assert.That(duration.TryResolveSeconds(2f, out var seconds), Is.True);
            // Authored states the intended wall result, so a 2x playback speed must NOT halve it. This
            // mirrors how EffectVfxCatalog.Entry.LifetimeOverride has always been treated.
            Assert.That(seconds, Is.EqualTo(1.2f).Within(0.0001f));
        }

        [Test]
        public void MeasuredLengthIsDividedByThePlaybackSpeed()
        {
            var duration = PresentationDuration.Create(3f, 0f, true, PresentationClock.Scaled);

            Assert.That(duration.TryResolveSeconds(2f, out var faster), Is.True);
            Assert.That(faster, Is.EqualTo(1.5f).Within(0.0001f), "A cue at 2x finishes in half the time.");

            Assert.That(duration.TryResolveSeconds(0.5f, out var slower), Is.True);
            Assert.That(slower, Is.EqualTo(6f).Within(0.0001f));
        }

        [Test]
        public void NonPositiveOrNonFiniteSpeedIsReadAsOne()
        {
            var duration = PresentationDuration.Create(3f, 0f, true, PresentationClock.Scaled);

            // Matches how the VFX catalog already normalizes playbackSpeed (<= 0 means "unset", not "freeze").
            foreach (var speed in new[] { 0f, -2f, float.NaN, float.PositiveInfinity })
            {
                Assert.That(duration.TryResolveSeconds(speed, out var seconds), Is.True);
                Assert.That(seconds, Is.EqualTo(3f).Within(0.0001f), $"speed {speed} must read as 1.");
            }
        }

        [Test]
        public void UnmeasuredIsUnknownRatherThanZeroSeconds()
        {
            var duration = PresentationDuration.Unmeasured(PresentationClock.Scaled);

            Assert.That(duration.Measurable, Is.True, "It could be measured; it just has not been.");
            Assert.That(duration.IsKnown, Is.False);
            // The whole point of returning false: a consumer must not read "nobody measured this" as
            // "this finishes instantly" (plan D5).
            Assert.That(duration.TryResolveSeconds(1f, out var seconds), Is.False);
            Assert.That(seconds, Is.EqualTo(0f));
        }

        [Test]
        public void LoopCueHasNoNaturalLengthAndIgnoresAMeasuredValue()
        {
            var loop = PresentationDuration.Unmeasurable(PresentationClock.Scaled);

            Assert.That(loop.Measurable, Is.False);
            Assert.That(loop.IsKnown, Is.False);
            Assert.That(loop.TryResolveSeconds(1f, out _), Is.False);

            // A bake tool must not be able to give a looping cue a natural length by writing the column:
            // a loop plays until the status is removed, so any measured number would be fiction.
            var withMeasured = PresentationDuration.Create(5f, 0f, measurable: false, clock: PresentationClock.Scaled);
            Assert.That(withMeasured.MeasuredSeconds, Is.EqualTo(0f));
            Assert.That(withMeasured.TryResolveSeconds(1f, out _), Is.False);
        }

        [Test]
        public void LoopCueBecomesKnownOnlyByAuthoring()
        {
            var authoredLoop = PresentationDuration.Create(0f, 0.8f, measurable: false, clock: PresentationClock.Scaled);

            Assert.That(authoredLoop.IsKnown, Is.True);
            Assert.That(authoredLoop.TryResolveSeconds(3f, out var seconds), Is.True);
            Assert.That(seconds, Is.EqualTo(0.8f).Within(0.0001f));
        }

        [Test]
        public void MalformedColumnValuesAreTreatedAsUnsetRatherThanTrusted()
        {
            foreach (var bad in new[] { -1f, float.NaN, float.NegativeInfinity })
            {
                var duration = PresentationDuration.Create(bad, bad, true, PresentationClock.Scaled);
                Assert.That(duration.HasAuthored, Is.False, $"authored {bad} must not count as authored.");
                Assert.That(duration.MeasuredSeconds, Is.EqualTo(0f), $"measured {bad} must not be stored.");
                Assert.That(duration.TryResolveSeconds(1f, out _), Is.False);
            }
        }

        [Test]
        public void TotalOccupancyAddsTheStartDelayToThePlayLength()
        {
            var duration = PresentationDuration.Create(2f, 0f, true, PresentationClock.Scaled);

            Assert.That(duration.TryResolveTotalOccupancySeconds(0.7f, 2f, out var total), Is.True);
            // The delay is not scaled by playback speed (it is a scheduler wait, not asset playback), while
            // the length is: 0.7 + 2/2. Conflating the two is the mistake ResolveTraceTailSeconds makes today.
            Assert.That(total, Is.EqualTo(1.7f).Within(0.0001f));
        }

        [Test]
        public void TotalOccupancyIsUnknownWhenTheLengthIsUnknown()
        {
            var unknown = PresentationDuration.Unmeasured(PresentationClock.Scaled);

            // A cue with a delay but no known length must not report the delay alone as its occupancy —
            // that would silently reintroduce "delay == length".
            Assert.That(unknown.TryResolveTotalOccupancySeconds(0.7f, 1f, out var total), Is.False);
            Assert.That(total, Is.EqualTo(0f));
        }

        [Test]
        public void DestroyTimerCutsAMeasuredLengthShort()
        {
            // The shipping shape this exists for: 19 of the 28 cues that have both values are cut short by
            // their destroy timer, by up to 4.5s (plan §6.6). Reporting the natural 4s would overstate what
            // the player sees by 2.5s.
            var duration = PresentationDuration.Create(
                measuredSeconds: 4f,
                authoredSeconds: 0f,
                measurable: true,
                clock: PresentationClock.Scaled,
                destroyAfterSeconds: 1.5f);

            Assert.That(duration.TryResolveSeconds(1f, out var seconds), Is.True);
            Assert.That(seconds, Is.EqualTo(1.5f).Within(0.0001f));
        }

        [Test]
        public void DestroyTimerNeverExtendsBeyondTheNaturalLength()
        {
            // A timer that outlasts the particles means dead air, not a longer effect — so this clamps, it
            // does not take the max.
            var duration = PresentationDuration.Create(1f, 0f, true, PresentationClock.Scaled, destroyAfterSeconds: 9f);

            Assert.That(duration.TryResolveSeconds(1f, out var seconds), Is.True);
            Assert.That(seconds, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void DestroyTimerIsClampedAfterTheSpeedDivisionNotBefore()
        {
            // measured 4s at 2x = 2s natural, which is still above a 1.5s timer.
            var duration = PresentationDuration.Create(4f, 0f, true, PresentationClock.Scaled, destroyAfterSeconds: 1.5f);
            Assert.That(duration.TryResolveSeconds(2f, out var capped), Is.True);
            Assert.That(capped, Is.EqualTo(1.5f).Within(0.0001f));

            // measured 4s at 4x = 1s natural, now below the timer, so the natural length wins.
            Assert.That(duration.TryResolveSeconds(4f, out var natural), Is.True);
            Assert.That(natural, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void DestroyTimerAloneAnswersWhenNothingIsMeasurable()
        {
            // 37 shipping cues are exactly this: their third-party prefabs contain looping ParticleSystems,
            // so nothing can be measured and the destroy timer is the only thing that ends them.
            var duration = PresentationDuration.Create(0f, 0f, true, PresentationClock.Scaled, destroyAfterSeconds: 2.5f);

            Assert.That(duration.IsKnown, Is.True);
            Assert.That(duration.TryResolveSeconds(1f, out var seconds), Is.True);
            Assert.That(seconds, Is.EqualTo(2.5f).Within(0.0001f));
        }

        [Test]
        public void AuthoredLengthOverridesTheDestroyTimerToo()
        {
            var duration = PresentationDuration.Create(4f, 3f, true, PresentationClock.Scaled, destroyAfterSeconds: 1f);

            Assert.That(duration.TryResolveSeconds(1f, out var seconds), Is.True);
            // Authored is the escape hatch: it must not be clamped, or a designer could never ask a consumer
            // to hold longer than the destroy timer.
            Assert.That(seconds, Is.EqualTo(3f).Within(0.0001f));
        }

        [Test]
        public void LoopCueIgnoresADestroyTimerItWouldNeverHonor()
        {
            // A looping status VFX is kept alive by PlayLoop until the status ends and never consults
            // lifetimeOverride, so passing one must not invent a length. Callers pass 0 for loop cues; this
            // asserts the guard rather than the caller's discipline.
            var loop = PresentationDuration.Create(0f, 0f, measurable: false, clock: PresentationClock.Scaled);

            Assert.That(loop.IsKnown, Is.False);
            Assert.That(loop.TryResolveSeconds(1f, out _), Is.False);
        }

        [Test]
        public void WithMeasuredAndWithAuthoredPreserveTheOtherHalfAndTheClock()
        {
            var authored = PresentationDuration
                .Unmeasured(PresentationClock.Unscaled)
                .WithAuthored(1.5f);

            var baked = authored.WithMeasured(4f);

            Assert.That(baked.Clock, Is.EqualTo(PresentationClock.Unscaled), "The clock must survive a bake.");
            Assert.That(baked.MeasuredSeconds, Is.EqualTo(4f).Within(0.0001f));
            // Baking must not clobber designer intent — that is the whole reason the two are separate fields.
            Assert.That(baked.HasAuthored, Is.True);
            Assert.That(baked.AuthoredSeconds, Is.EqualTo(1.5f).Within(0.0001f));
        }
    }
}

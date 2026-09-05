using NUnit.Framework;
using SeoulPlayup.Combat.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// The event's length is charged AFTER the framing, not before it
    /// (docs/monster-action-camera-focus-plan.md §10.6).
    ///
    /// Previously the measured length inflated the pan, which is a *leading* wait: the camera arrived, sat on
    /// an empty frame for the length of the event, and then the event played — and the next spotlight was free
    /// to take the camera the moment the cues had been fired, mid-explosion. The pan is now a constant arrival
    /// lead and the length is the dwell the next spotlight has to wait out.
    /// </summary>
    public sealed class FocusPanLengthScalingTests
    {
        private CombatTimingProfile profile;

        [SetUp]
        public void SetUp() => profile = ScriptableObject.CreateInstance<CombatTimingProfile>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(profile);

        [Test]
        public void PanIsConstantAndIgnoresHowLongTheCoveredEventIs()
        {
            profile.TunableFocusPanSeconds = 0.5f;

            // Nothing about the covered event may lengthen the lead — that time would be spent staring at the
            // framing before anything happens in it.
            Assert.That(profile.FocusPanSeconds, Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void MeasuredSecondsBecomeTheDwell()
        {
            profile.TunableFocusDwellMaxSeconds = 6f;

            Assert.That(profile.ResolveFocusDwellSeconds(4f), Is.EqualTo(4f).Within(0.0001f));
        }

        [Test]
        public void NoDurationDataMeansNoDwellRatherThanAGuess()
        {
            profile.TunableFocusDwellMaxSeconds = 6f;

            // A reaction-only cluster (or a pre-bake project) reports 0 seconds. The floating-text signal is
            // what carries such a framing at replay time; inventing a length here would be a second estimator
            // silently disagreeing with the first.
            Assert.That(profile.ResolveFocusDwellSeconds(0f), Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void CeilingStillCapsALongCluster()
        {
            profile.TunableFocusDwellMaxSeconds = 3f;

            Assert.That(profile.ResolveFocusDwellSeconds(60f), Is.EqualTo(3f).Within(0.0001f));
        }

        [Test]
        public void NonFiniteMeasurementsAreTreatedAsUnknown()
        {
            profile.TunableFocusDwellMaxSeconds = 6f;

            Assert.That(profile.ResolveFocusDwellSeconds(float.NaN), Is.EqualTo(0f).Within(0.0001f));
            Assert.That(profile.ResolveFocusDwellSeconds(float.PositiveInfinity), Is.EqualTo(0f).Within(0.0001f));
        }

        /// <summary>
        /// §10.11. The field-damage cue is authored with a 1.1s playback delay, so a hold measured from
        /// dispatch expired while the explosion was still pending and the camera left before anything was on
        /// screen. The lead-in is therefore a floor that the ceiling may not cut into.
        /// </summary>
        [Test]
        public void LeadInHoldsTheFramingUntilTheCueIsActuallyVisible()
        {
            profile.TunableFocusDwellMaxSeconds = 3f;
            profile.TunableFocusDwellTextReadSeconds = 0.5f;

            // No measured length at all, but the cue does not show anything for 1.1s: the hold must still
            // outlast that, plus the time it takes to read the number that appears then.
            Assert.That(
                profile.ResolveFocusDwellSeconds(0f, 1.1f),
                Is.EqualTo(1.6f).Within(0.0001f));
        }

        [Test]
        public void CeilingClampsTheContentButNeverTheLeadIn()
        {
            profile.TunableFocusDwellMaxSeconds = 1f;
            profile.TunableFocusDwellTextReadSeconds = 0.5f;

            // The ceiling exists to stop a long event parking the camera. Applying it to the lead-in instead
            // would hand the camera back before the event began — the exact failure it is meant to prevent.
            Assert.That(
                profile.ResolveFocusDwellSeconds(60f, 1.1f),
                Is.EqualTo(1.6f).Within(0.0001f));
        }

        [Test]
        public void TheLongerOfContentAndLeadInWins()
        {
            profile.TunableFocusDwellMaxSeconds = 6f;
            profile.TunableFocusDwellTextReadSeconds = 0.5f;

            // A cue that shows instantly but plays for 4s is held for its content, not shortened to the
            // lead-in path; the two are alternatives, not addends.
            Assert.That(profile.ResolveFocusDwellSeconds(4f, 0f), Is.EqualTo(4f).Within(0.0001f));
            Assert.That(profile.ResolveFocusDwellSeconds(4f, 0.2f), Is.EqualTo(4f).Within(0.0001f));
        }

        [Test]
        public void NonFiniteLeadInIsTreatedAsNoLeadIn()
        {
            profile.TunableFocusDwellMaxSeconds = 6f;

            Assert.That(profile.ResolveFocusDwellSeconds(2f, float.NaN), Is.EqualTo(2f).Within(0.0001f));
            Assert.That(
                profile.ResolveFocusDwellSeconds(2f, float.PositiveInfinity), Is.EqualTo(2f).Within(0.0001f));
        }

        [Test]
        public void OmittingTheLeadInKeepsTheOldBehaviour()
        {
            profile.TunableFocusDwellMaxSeconds = 3f;

            // The parameter is optional so callers that have no cue data (and the existing tests above) keep
            // resolving to the content-only answer.
            Assert.That(profile.ResolveFocusDwellSeconds(2f), Is.EqualTo(2f).Within(0.0001f));
        }

        [Test]
        public void GlobalSpeedMultiplierStillScalesBothSides()
        {
            profile.TunableFocusPanSeconds = 0.5f;
            profile.TunableFocusDwellMaxSeconds = 10f;
            profile.TunableFocusDwellTextReadSeconds = 0.5f;
            profile.TunableGlobalSpeedMultiplier = 2f;

            // These are authored waits, so unlike a measured asset length they DO obey the speed knob.
            Assert.That(profile.FocusPanSeconds, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(profile.ResolveFocusDwellSeconds(4f), Is.EqualTo(2f).Within(0.0001f));
            Assert.That(profile.FocusDwellTextReadSeconds, Is.EqualTo(0.25f).Within(0.0001f));
        }
    }
}

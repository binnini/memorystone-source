using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime.Timeline;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// P4: the spotlight pan is sized by the measured length of the covered cues instead of by counting
    /// triggers (docs/presentation-duration-data-plan.md §6.4). The provider is faked, so the arithmetic is
    /// asserted without a VFX catalog.
    /// </summary>
    public sealed class ActorSpotlightDurationTests
    {
        [Test]
        public void CoveredSecondsSumsTheCuesOfTheCoveredEffectBeats()
        {
            var timeline = new CombatTimeline();
            timeline.Append(CombatTimelineEventKind.ActorSpotlight, to: new HexCoord(3, 0));
            timeline.Append(CombatTimelineEventKind.Effect, effectIndex: 0);
            timeline.Append(CombatTimelineEventKind.ActorHit, actorId: "m1");
            timeline.Append(CombatTimelineEventKind.Effect, effectIndex: 1);

            timeline.AnnotateActorSpotlightSeconds(index => index == 0 ? 2.5f : 1.5f);

            var spotlight = timeline.Events.Single(e => e.Kind == CombatTimelineEventKind.ActorSpotlight);
            Assert.That(spotlight.CoveredSeconds, Is.EqualTo(4f).Within(0.0001f));
        }

        [Test]
        public void EachSpotlightOnlyCountsUpToTheNextSpotlight()
        {
            var timeline = new CombatTimeline();
            timeline.Append(CombatTimelineEventKind.ActorSpotlight, to: new HexCoord(3, 0));
            timeline.Append(CombatTimelineEventKind.Effect, effectIndex: 0);
            timeline.Append(CombatTimelineEventKind.ActorSpotlight, to: new HexCoord(9, 0));
            timeline.Append(CombatTimelineEventKind.Effect, effectIndex: 1);
            timeline.Append(CombatTimelineEventKind.Effect, effectIndex: 2);

            timeline.AnnotateActorSpotlightSeconds(index => index == 0 ? 2f : 0.5f);

            var spotlights = timeline.Events
                .Where(e => e.Kind == CombatTimelineEventKind.ActorSpotlight)
                .ToList();
            Assert.That(spotlights[0].CoveredSeconds, Is.EqualTo(2f).Within(0.0001f));
            // The second spotlight owns the two effects after it, not the first one's.
            Assert.That(spotlights[1].CoveredSeconds, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void NonEffectBeatsContributeNothingRatherThanAGuess()
        {
            var timeline = new CombatTimeline();
            timeline.Append(CombatTimelineEventKind.ActorSpotlight, to: new HexCoord(3, 0));
            timeline.Append(CombatTimelineEventKind.ActorHit, actorId: "m1");
            timeline.Append(CombatTimelineEventKind.ActorDeath, actorId: "m1");
            timeline.Append(CombatTimelineEventKind.EnemyMoveStep, actorId: "m1");

            var probed = new List<int>();
            timeline.AnnotateActorSpotlightSeconds(index =>
            {
                probed.Add(index);
                return 99f;
            });

            var spotlight = timeline.Events.Single(e => e.Kind == CombatTimelineEventKind.ActorSpotlight);
            // Reaction/movement animation lengths are outside this track's consumer scope, so they must not
            // be invented — the pan then falls back to the trigger-count estimator.
            Assert.That(spotlight.CoveredSeconds, Is.EqualTo(0f));
            Assert.That(probed, Is.Empty, "Only Effect beats may be asked for a duration.");
        }

        [Test]
        public void UnknownAndMalformedDurationsAreSkippedInsteadOfPoisoningTheSum()
        {
            var timeline = new CombatTimeline();
            timeline.Append(CombatTimelineEventKind.ActorSpotlight, to: new HexCoord(3, 0));
            timeline.Append(CombatTimelineEventKind.Effect, effectIndex: 0);
            timeline.Append(CombatTimelineEventKind.Effect, effectIndex: 1);
            timeline.Append(CombatTimelineEventKind.Effect, effectIndex: 2);
            timeline.Append(CombatTimelineEventKind.Effect, effectIndex: 3);

            timeline.AnnotateActorSpotlightSeconds(index => index switch
            {
                0 => 1.25f,
                1 => 0f,                        // no duration data for this cue
                2 => float.NaN,
                _ => float.PositiveInfinity
            });

            var spotlight = timeline.Events.Single(e => e.Kind == CombatTimelineEventKind.ActorSpotlight);
            Assert.That(spotlight.CoveredSeconds, Is.EqualTo(1.25f).Within(0.0001f));
        }

        /// <summary>
        /// §10.11: the lead-in is the dead time before a cue shows anything, and every covered cue waits it
        /// out from the same dispatch. Summing would park the camera on an empty patch for the total, so the
        /// pass takes the MAX — this is the one place the two annotation passes deliberately disagree.
        /// </summary>
        [Test]
        public void CoveredLeadInTakesTheLongestDelayRatherThanSummingThem()
        {
            var timeline = new CombatTimeline();
            timeline.Append(CombatTimelineEventKind.ActorSpotlight, to: new HexCoord(8, 0));
            timeline.Append(CombatTimelineEventKind.Effect, effectIndex: 0);
            timeline.Append(CombatTimelineEventKind.Effect, effectIndex: 1);
            timeline.Append(CombatTimelineEventKind.Effect, effectIndex: 2);

            timeline.AnnotateActorSpotlightLeadIn(index => index switch
            {
                0 => 1.1f,
                1 => 0.4f,
                _ => 1.1f,
            });

            var spotlight = timeline.Events.Single(e => e.Kind == CombatTimelineEventKind.ActorSpotlight);
            Assert.That(
                spotlight.CoveredLeadInSeconds,
                Is.EqualTo(1.1f).Within(0.0001f),
                "three cues that each wait 1.1s concurrently become visible at 1.1s, not 2.6s");
        }

        [Test]
        public void CoveredLeadInIsScopedToTheSpotlightThatOwnsTheBeats()
        {
            var timeline = new CombatTimeline();
            timeline.Append(CombatTimelineEventKind.ActorSpotlight, to: new HexCoord(8, 0));
            timeline.Append(CombatTimelineEventKind.Effect, effectIndex: 0);
            timeline.Append(CombatTimelineEventKind.ActorSpotlight, to: new HexCoord(-8, 0));
            timeline.Append(CombatTimelineEventKind.Effect, effectIndex: 1);

            timeline.AnnotateActorSpotlightLeadIn(index => index == 0 ? 1.1f : 0f);

            var spotlights = timeline.Events
                .Where(e => e.Kind == CombatTimelineEventKind.ActorSpotlight)
                .ToList();
            Assert.That(spotlights[0].CoveredLeadInSeconds, Is.EqualTo(1.1f).Within(0.0001f));
            // A framing whose own cue is instant must not inherit its neighbour's lead-in and stall.
            Assert.That(spotlights[1].CoveredLeadInSeconds, Is.EqualTo(0f));
        }

        [Test]
        public void LeadInAnnotationSurvivesTheOtherTwoPasses()
        {
            var timeline = new CombatTimeline();
            timeline.Append(CombatTimelineEventKind.ActorSpotlight, to: new HexCoord(8, 0));
            timeline.Append(CombatTimelineEventKind.Effect, effectIndex: 0);

            timeline.AnnotateActorSpotlightLeadIn(_ => 1.1f);
            timeline.AnnotateActorSpotlightCoverage();
            timeline.AnnotateActorSpotlightSeconds(_ => 3f);

            var spotlight = timeline.Events.Single(e => e.Kind == CombatTimelineEventKind.ActorSpotlight);
            Assert.That(spotlight.CoveredLeadInSeconds, Is.EqualTo(1.1f).Within(0.0001f));
            Assert.That(spotlight.CoveredSeconds, Is.EqualTo(3f).Within(0.0001f));
            Assert.That(spotlight.CoveredBeats, Is.EqualTo(1));
        }

        [Test]
        public void NullProviderLeavesTheTimelineUntouched()
        {
            var timeline = new CombatTimeline();
            timeline.Append(CombatTimelineEventKind.ActorSpotlight, to: new HexCoord(3, 0));
            timeline.Append(CombatTimelineEventKind.Effect, effectIndex: 0);

            timeline.AnnotateActorSpotlightSeconds(null);

            // No duration source (bake not run, or action focus off) must be a no-op, not a zero-fill crash.
            Assert.That(
                timeline.Events.Single(e => e.Kind == CombatTimelineEventKind.ActorSpotlight).CoveredSeconds,
                Is.EqualTo(0f));
        }

        [Test]
        public void CoverageAnnotationsDoNotOverwriteEachOther()
        {
            var timeline = new CombatTimeline();
            timeline.Append(CombatTimelineEventKind.ActorSpotlight, to: new HexCoord(3, 0));
            timeline.Append(CombatTimelineEventKind.Effect, effectIndex: 0);
            timeline.Append(CombatTimelineEventKind.ActorHit, actorId: "m1");

            timeline.AnnotateActorSpotlightCoverage();
            timeline.AnnotateActorSpotlightSeconds(_ => 3f);

            var spotlight = timeline.Events.Single(e => e.Kind == CombatTimelineEventKind.ActorSpotlight);
            // Both estimators must survive both passes in either order: the seconds pass is the preferred
            // input and the beat count is what the profile falls back to.
            Assert.That(spotlight.CoveredBeats, Is.EqualTo(2));
            Assert.That(spotlight.CoveredSeconds, Is.EqualTo(3f).Within(0.0001f));

            timeline.AnnotateActorSpotlightCoverage();
            var again = timeline.Events.Single(e => e.Kind == CombatTimelineEventKind.ActorSpotlight);
            Assert.That(again.CoveredSeconds, Is.EqualTo(3f).Within(0.0001f), "Re-running coverage must not clear seconds.");
            Assert.That(again.CoveredBeats, Is.EqualTo(2));
        }
    }
}

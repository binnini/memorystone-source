using NUnit.Framework;
using SeoulPlayup.Combat.Runtime.Presentation;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// Pins the P0 measurement accumulator (docs/monster-action-camera-focus-plan.md §5 P0).
    ///
    /// The point of this pass is to replace §6's provisional <c>coalesceRadiusHexes 5</c> and
    /// <c>maxFocusPerPhase 2</c> with measured values, so the clustering maths that produces those two
    /// numbers has to be right — a silently wrong cluster count would set the tunables wrong and the error
    /// would only surface as a camera that cuts too often during real play. The behaviours fixed here are:
    /// on-screen events never enter the budget, clustering is per phase instance (never across turns), and
    /// linkage is transitive so a chain of events stays one camera cut.
    /// </summary>
    public sealed class CombatActionFocusMetricsTests
    {
        // The accumulator is ambient static (same trade as CombatPresentationTrace), so each test both starts
        // and ends from a clean slate — otherwise one test's samples would show up in the next one's counts.
        [SetUp]
        public void ArmCleanSlate() => CombatActionFocusMetrics.Reset();

        [TearDown]
        public void LeaveCleanSlate() => CombatActionFocusMetrics.Reset();

        private static CombatActionFocusSample Sample(
            int turn,
            string phase,
            int q,
            int r,
            bool framed = false,
            string label = "enemy-move")
            => new CombatActionFocusSample(
                turn,
                phase,
                label,
                "monster-1",
                new HexCoord(q, r),
                DistanceFromPlayer(q, r),
                framed,
                framed);

        /// <summary>Fixtures put the player at the origin, so a sample's distance follows from its coord.</summary>
        private static int DistanceFromPlayer(int q, int r) => new HexCoord(0, 0).DistanceTo(new HexCoord(q, r));

        [Test]
        public void RecordIsIgnoredUntilEnabled()
        {
            CombatActionFocusMetrics.Record(Sample(1, "monster-attack", 9, 0));
            Assert.That(CombatActionFocusMetrics.SampleCount, Is.Zero, "recording must be off by default");

            CombatActionFocusMetrics.Enable();
            CombatActionFocusMetrics.Record(Sample(1, "monster-attack", 9, 0));
            Assert.That(CombatActionFocusMetrics.SampleCount, Is.EqualTo(1));

            CombatActionFocusMetrics.Disable();
            CombatActionFocusMetrics.Record(Sample(1, "monster-attack", 9, 0));
            Assert.That(
                CombatActionFocusMetrics.SampleCount,
                Is.EqualTo(1),
                "Disable keeps what was accumulated but stops recording");
        }

        [Test]
        public void FramedSamplesAreExcludedFromTheFocusBudget()
        {
            CombatActionFocusMetrics.Enable();
            CombatActionFocusMetrics.Record(Sample(1, "monster-attack", 1, 0, framed: true));
            CombatActionFocusMetrics.Record(Sample(1, "monster-attack", 2, 0, framed: true));

            var stats = CombatActionFocusMetrics.ComputeClusterStats(5, useMargin: true);

            // This is the plan's zero-regression promise expressed as data: an already-framed event costs no
            // camera move, so it must not create a phase or a cluster.
            Assert.That(stats.PhaseCount, Is.Zero);
            Assert.That(stats.ClusterCount, Is.Zero);
            Assert.That(stats.MaxClustersInOnePhase, Is.Zero);
        }

        [Test]
        public void EventsWithinTheRadiusCoalesceIntoOneCut()
        {
            CombatActionFocusMetrics.Enable();
            CombatActionFocusMetrics.Record(Sample(1, "monster-movement", 8, 0));
            CombatActionFocusMetrics.Record(Sample(1, "monster-movement", 10, 0));

            Assert.That(
                CombatActionFocusMetrics.ComputeClusterStats(2, useMargin: true).ClusterCount,
                Is.EqualTo(1),
                "two hexes apart is inside radius 2");
            Assert.That(
                CombatActionFocusMetrics.ComputeClusterStats(1, useMargin: true).ClusterCount,
                Is.EqualTo(2),
                "and outside radius 1");
        }

        [Test]
        public void LinkageIsTransitiveAcrossAChainOfEvents()
        {
            CombatActionFocusMetrics.Enable();
            // 0 → 2 → 4 along one axis: the ends are 4 apart, further than the radius, but the chain holds.
            CombatActionFocusMetrics.Record(Sample(1, "monster-movement", 0, 0));
            CombatActionFocusMetrics.Record(Sample(1, "monster-movement", 2, 0));
            CombatActionFocusMetrics.Record(Sample(1, "monster-movement", 4, 0));

            var stats = CombatActionFocusMetrics.ComputeClusterStats(2, useMargin: true);

            // Single linkage on purpose: the framing point is the cluster's bounding-box centre, so a chain is
            // still coverable by one shot. Pairwise-only linkage would over-count cuts and inflate the budget.
            Assert.That(stats.ClusterCount, Is.EqualTo(1));
            Assert.That(stats.MaxClusterSize, Is.EqualTo(3));
        }

        [Test]
        public void ClustersNeverSpanTurnsOrPhases()
        {
            CombatActionFocusMetrics.Enable();
            // Same coordinate every time, so only the phase key can separate them.
            CombatActionFocusMetrics.Record(Sample(1, "monster-movement", 9, 0));
            CombatActionFocusMetrics.Record(Sample(1, "monster-attack", 9, 0));
            CombatActionFocusMetrics.Record(Sample(2, "monster-movement", 9, 0));

            var stats = CombatActionFocusMetrics.ComputeClusterStats(5, useMargin: true);

            // maxFocusPerPhase is a per-phase budget, so a coordinate revisited next turn must be its own cut.
            Assert.That(stats.PhaseCount, Is.EqualTo(3));
            Assert.That(stats.ClusterCount, Is.EqualTo(3));
            Assert.That(stats.MaxClustersInOnePhase, Is.EqualTo(1), "one cut per phase, not three");
        }

        [Test]
        public void MaxClustersInOnePhaseIsTheBudgetTheWorstPhaseNeeds()
        {
            CombatActionFocusMetrics.Enable();
            // Turn 1: one tight pair → 1 cut. Turn 2: three far-apart events → 3 cuts.
            CombatActionFocusMetrics.Record(Sample(1, "monster-movement", 8, 0));
            CombatActionFocusMetrics.Record(Sample(1, "monster-movement", 9, 0));
            CombatActionFocusMetrics.Record(Sample(2, "monster-movement", 0, 0));
            CombatActionFocusMetrics.Record(Sample(2, "monster-movement", 20, 0));
            CombatActionFocusMetrics.Record(Sample(2, "monster-movement", 0, 20));

            var stats = CombatActionFocusMetrics.ComputeClusterStats(3, useMargin: true);

            Assert.That(stats.ClusterCount, Is.EqualTo(4));
            Assert.That(stats.MaxClustersInOnePhase, Is.EqualTo(3), "the value maxFocusPerPhase must cover");
            Assert.That(stats.MeanClustersPerPhase, Is.EqualTo(2d).Within(0.001d));
        }

        [Test]
        public void MarginVerdictSelectsWhichSamplesCountAsOffScreen()
        {
            CombatActionFocusMetrics.Enable();
            // An event just inside the frame edge: framed at margin 0, off-screen once the margin insets it.
            CombatActionFocusMetrics.Record(new CombatActionFocusSample(
                1, "monster-attack", "enemy-attack", "monster-1", new HexCoord(5, 0), 5,
                framed: true, framedWithMargin: false));

            Assert.That(
                CombatActionFocusMetrics.ComputeClusterStats(5, useMargin: false).ClusterCount,
                Is.Zero,
                "at margin 0 the event reads as already visible");
            Assert.That(
                CombatActionFocusMetrics.ComputeClusterStats(5, useMargin: true).ClusterCount,
                Is.EqualTo(1),
                "with the margin applied it needs a cut — which is why both verdicts are recorded per sample");
        }

        [Test]
        public void SummaryReportsEveryCandidateRadiusAndSurvivesAnEmptyRecording()
        {
            Assert.That(
                CombatActionFocusMetrics.FormatSummary(),
                Does.Contain("표본 없음"),
                "an empty summary must say so rather than print a table of zeroes");

            CombatActionFocusMetrics.Enable();
            CombatActionFocusMetrics.Record(Sample(1, "monster-movement", 9, 0));
            CombatActionFocusMetrics.Record(Sample(1, "monster-attack", 2, 0, framed: true));

            var summary = CombatActionFocusMetrics.FormatSummary();

            Assert.That(summary, Does.Contain("samples=2"));
            Assert.That(summary, Does.Contain("r9"), "the distance histogram carries the off-screen event");
            foreach (var radius in CombatActionFocusMetrics.CandidateCoalesceRadii)
            {
                // The whole table is printed so the two tunables are chosen by reading a sweep rather than by
                // re-running the playtest once per candidate value.
                Assert.That(
                    CombatActionFocusMetrics.ComputeClusterStats(radius, useMargin: true).PhaseCount,
                    Is.EqualTo(1),
                    $"radius {radius} sees the single off-screen phase");
            }
        }

        [Test]
        public void ClearKeepsTheArmedStateButResetDoesNot()
        {
            CombatActionFocusMetrics.Enable();
            CombatActionFocusMetrics.Record(Sample(1, "monster-movement", 9, 0));

            CombatActionFocusMetrics.Clear();
            Assert.That(CombatActionFocusMetrics.SampleCount, Is.Zero);
            Assert.That(CombatActionFocusMetrics.IsEnabled, Is.True, "Clear starts a fresh scenario mid-session");

            CombatActionFocusMetrics.Reset();
            Assert.That(CombatActionFocusMetrics.IsEnabled, Is.False);
        }
    }
}

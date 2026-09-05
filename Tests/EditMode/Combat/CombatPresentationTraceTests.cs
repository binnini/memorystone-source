using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime.Presentation;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// The presentation trace recorder. It is ambient static state, so these tests pin the two properties
    /// that make that safe: nothing is recorded unless a recording was explicitly begun, and a recording
    /// never leaks into the next one.
    /// </summary>
    public sealed class CombatPresentationTraceTests
    {
        private float now;

        [SetUp]
        public void SetUp()
        {
            now = 0f;
            CombatPresentationTrace.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            CombatPresentationTrace.Reset();
        }

        [Test]
        public void RecordingIsInertUntilBegun()
        {
            CombatPresentationTrace.Record(CombatTraceChannel.Sfx, "card.attack.resolve");

            Assert.That(CombatPresentationTrace.IsRecording, Is.False);
            Assert.That(CombatPresentationTrace.End(), Is.Null);
        }

        [Test]
        public void EntriesAreStampedRelativeToTheStartOfTheRecording()
        {
            now = 100f;
            CombatPresentationTrace.Begin("player-attack A01", () => now);

            CombatPresentationTrace.Record(CombatTraceChannel.Dispatch, "#0 Damage");
            now = 100.5f;
            CombatPresentationTrace.Record(CombatTraceChannel.Vfx, "CVA01");
            now = 100.7f;
            CombatPresentationTrace.Record(CombatTraceChannel.Sfx, "card.attack.resolve");

            var log = CombatPresentationTrace.End();

            Assert.That(log.Label, Is.EqualTo("player-attack A01"));
            // Tolerance, not equality: the stamp is a subtraction of two floats, so a clock reading of 100.7
            // lands a few ULPs off 0.7. Timestamps are read at millisecond resolution, well above that.
            Assert.That(
                log.Entries.Select(entry => entry.TimeSeconds),
                Is.EqualTo(new[] { 0f, 0.5f, 0.7f }).Within(0.0001f));
            Assert.That(log.DurationSeconds, Is.EqualTo(0.7f).Within(0.0001f));
        }

        [Test]
        public void ChannelDriftWithinOneEffectIsReadableFromTheLog()
        {
            // The whole point of the tool: VFX at +0.5 and SFX at +0.7 off the same dispatch is the shape of
            // an authored-delay mismatch, and it has to be recoverable from the log as a number.
            CombatPresentationTrace.Begin("drift", () => now);
            CombatPresentationTrace.Record(CombatTraceChannel.Dispatch, "#0 Damage");
            now = 0.5f;
            CombatPresentationTrace.Record(CombatTraceChannel.Vfx, "CVA01");
            now = 0.7f;
            CombatPresentationTrace.Record(CombatTraceChannel.Sfx, "combat.monster.hit");
            var log = CombatPresentationTrace.End();

            var dispatch = log.Entries.First(entry => entry.Channel == CombatTraceChannel.Dispatch);
            var vfx = log.Entries.First(entry => entry.Channel == CombatTraceChannel.Vfx);
            var sfx = log.Entries.First(entry => entry.Channel == CombatTraceChannel.Sfx);

            Assert.That(vfx.TimeSeconds - dispatch.TimeSeconds, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(sfx.TimeSeconds - vfx.TimeSeconds, Is.EqualTo(0.2f).Within(0.0001f));
        }

        [Test]
        public void EndClosesTheRecordingSoLaterCallsAreInert()
        {
            CombatPresentationTrace.Begin("first", () => now);
            CombatPresentationTrace.Record(CombatTraceChannel.Beat, "PlayerAttackStart");
            CombatPresentationTrace.End();

            CombatPresentationTrace.Record(CombatTraceChannel.Sfx, "leaked");

            CombatPresentationTrace.Begin("second", () => now);
            var log = CombatPresentationTrace.End();

            Assert.That(log.Label, Is.EqualTo("second"));
            Assert.That(log.Entries, Is.Empty, "A finished recording leaked into the next one.");
        }

        [Test]
        public void BeginWhileRecordingRestartsInsteadOfNesting()
        {
            CombatPresentationTrace.Begin("first", () => now);
            CombatPresentationTrace.Record(CombatTraceChannel.Beat, "stale");
            CombatPresentationTrace.Begin("second", () => now);
            CombatPresentationTrace.Record(CombatTraceChannel.Beat, "fresh");

            var log = CombatPresentationTrace.End();

            Assert.That(log.Label, Is.EqualTo("second"));
            Assert.That(log.Entries.Select(entry => entry.Label), Is.EqualTo(new[] { "fresh" }));
        }

        [Test]
        public void EntriesAreCappedAndTheLogSaysSo()
        {
            CombatPresentationTrace.Begin("capped", () => now);
            for (var i = 0; i < CombatPresentationTrace.MaxEntries + 25; i++)
            {
                CombatPresentationTrace.Record(CombatTraceChannel.Beat, $"beat{i}");
            }

            var log = CombatPresentationTrace.End();

            Assert.That(log.Entries.Count, Is.EqualTo(CombatPresentationTrace.MaxEntries));
            Assert.That(log.Truncated, Is.True);
            Assert.That(log.Format(), Does.Contain("누락"));
        }

        [Test]
        public void FormatOrdersEntriesAndIndentsChannelsUnderTheirBeat()
        {
            CombatPresentationTrace.Begin("player-attack A01", () => now);
            CombatPresentationTrace.Record(CombatTraceChannel.Dispatch, "#0 Damage", "src=A01");
            now = 0.5f;
            CombatPresentationTrace.Record(CombatTraceChannel.Vfx, "CVA01", "delay=0.5");
            var log = CombatPresentationTrace.End();

            var lines = log.Format().Split('\n');

            Assert.That(lines[0], Does.Contain("player-attack A01"));
            Assert.That(lines[1], Does.Contain("EFFECT").And.Contain("#0 Damage").And.Contain("src=A01"));
            Assert.That(lines[2], Does.Contain("    VFX").And.Contain("CVA01"));
            Assert.That(lines[2].IndexOf("VFX"), Is.GreaterThan(lines[1].IndexOf("EFFECT")));
        }

        [Test]
        public void ResetDropsAnInProgressRecording()
        {
            CombatPresentationTrace.Begin("aborted", () => now);
            CombatPresentationTrace.Record(CombatTraceChannel.Beat, "PlayerAttackStart");
            CombatPresentationTrace.Reset();

            Assert.That(CombatPresentationTrace.IsRecording, Is.False);
            Assert.That(CombatPresentationTrace.End(), Is.Null);
        }
    }
}

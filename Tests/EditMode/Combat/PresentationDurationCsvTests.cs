using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// Covers the measured/authored length columns added to the pure CSV converters (P1). The critical
    /// property asserted here is that the columns are OPTIONAL: the shipping CSVs do not have them yet, so a
    /// converter that required them would break every catalog on the first commit of this track.
    /// </summary>
    public sealed class PresentationDurationCsvTests
    {
        private const string CardVfxHeader =
            "cueId,cardId,effectRef,effectKind,targetFilter,prefabPath,lifetimeOverride,playbackSpeed";

        [Test]
        public void CardVfxCuesConvertWithoutTheLengthColumnsAsUnmeasured()
        {
            var csv = CardVfxHeader + "\nCV01,M01,move.basic,Push,Player,Assets/x.prefab,2.5,1\n";

            var cue = CombatCardVfxCsvConverter.Convert(csv).Single();

            Assert.That(cue.MeasuredLengthSeconds, Is.EqualTo(0f));
            Assert.That(cue.AuthoredLengthSeconds, Is.EqualTo(0f));
            // lifetimeOverride must be left exactly as authored and must NOT be written into either half of
            // the length pair — it stays its own field (plan D7).
            Assert.That(cue.LifetimeOverride, Is.EqualTo(2.5f).Within(0.0001f));

            // It IS read at resolve time though: with nothing measured, the destroy timer is the only thing
            // that ends this cue, so the duration is known and equals it (plan §11 option A).
            var duration = cue.CreateDuration(measurable: true);
            Assert.That(duration.IsKnown, Is.True);
            Assert.That(duration.TryResolveSeconds(1f, out var seconds), Is.True);
            Assert.That(seconds, Is.EqualTo(2.5f).Within(0.0001f));
        }

        [Test]
        public void CardVfxCuesParseBothLengthColumnsWhenPresent()
        {
            var csv = CardVfxHeader + ",measuredLengthSeconds,authoredLengthSeconds\n"
                + "CV01,M01,move.basic,Push,Player,Assets/x.prefab,2.5,2,3,1.2\n";

            var cue = CombatCardVfxCsvConverter.Convert(csv).Single();

            Assert.That(cue.MeasuredLengthSeconds, Is.EqualTo(3f).Within(0.0001f));
            Assert.That(cue.AuthoredLengthSeconds, Is.EqualTo(1.2f).Within(0.0001f));

            var duration = cue.CreateDuration(measurable: true);
            Assert.That(duration.Clock, Is.EqualTo(PresentationClock.Scaled), "Particles run on the scaled clock.");
            Assert.That(duration.TryResolveSeconds(cue.PlaybackSpeed, out var seconds), Is.True);
            Assert.That(seconds, Is.EqualTo(1.2f).Within(0.0001f), "Authored wins over measured.");
        }

        [Test]
        public void CardVfxMeasuredLengthResolvesAgainstTheCuesOwnPlaybackSpeed()
        {
            var csv = CardVfxHeader + ",measuredLengthSeconds\n"
                + "CV01,M01,move.basic,Push,Player,Assets/x.prefab,0,2,3\n";

            var cue = CombatCardVfxCsvConverter.Convert(csv).Single();

            Assert.That(cue.PlaybackSpeed, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(cue.CreateDuration(measurable: true).TryResolveSeconds(cue.PlaybackSpeed, out var seconds), Is.True);
            // 52 of the 110 shipping cues have playbackSpeed != 1, so this division is the common case,
            // not an edge case (plan §2.4).
            Assert.That(seconds, Is.EqualTo(1.5f).Within(0.0001f));
        }

        [Test]
        public void CardVfxNegativeLengthsAreClampedRatherThanStored()
        {
            var csv = CardVfxHeader + ",measuredLengthSeconds,authoredLengthSeconds\n"
                + "CV01,M01,move.basic,Push,Player,Assets/x.prefab,0,1,-3,-1\n";

            var cue = CombatCardVfxCsvConverter.Convert(csv).Single();

            Assert.That(cue.MeasuredLengthSeconds, Is.EqualTo(0f));
            Assert.That(cue.AuthoredLengthSeconds, Is.EqualTo(0f));
        }

        [Test]
        public void LoopCueCannotBeGivenANaturalLengthByTheMeasuredColumn()
        {
            var csv = CardVfxHeader + ",measuredLengthSeconds\n"
                + "CV01,M01,move.basic,Push,Player,Assets/x.prefab,0,1,5\n";

            var cue = CombatCardVfxCsvConverter.Convert(csv).Single();

            // Same row, different measurability: a looping cue plays until its status is removed, so the
            // measured number is fiction and must not resolve (plan §4.5).
            Assert.That(cue.CreateDuration(measurable: true).TryResolveSeconds(1f, out _), Is.True);
            Assert.That(cue.CreateDuration(measurable: false).TryResolveSeconds(1f, out _), Is.False);
        }

        [Test]
        public void SoundCueDurationsUseTheUnscaledClock()
        {
            var duration = new CombatSoundCueDefinition(
                    "S001",
                    "Assets/clip.wav",
                    "Sfx",
                    volume: 1f,
                    pitchMin: 1f,
                    pitchMax: 1f,
                    cooldownSeconds: 0f,
                    missingClipBehavior: "Silent",
                    designerNote: string.Empty,
                    measuredLengthSeconds: 0.8f,
                    authoredLengthSeconds: 0f)
                .CreateDuration(measurable: true);

            // AudioSource ignores Time.timeScale, so mixing this with a scaled VFX length under hit-stop is
            // wrong — the clock has to travel with the number (plan §2.5).
            Assert.That(duration.Clock, Is.EqualTo(PresentationClock.Unscaled));
            Assert.That(duration.TryResolveSeconds(1f, out var seconds), Is.True);
            Assert.That(seconds, Is.EqualTo(0.8f).Within(0.0001f));
        }

        [Test]
        public void SoundCueReportsWhetherItsLengthIsExactOrAnUpperBound()
        {
            var fixedPitch = MakeSoundCue(pitchMin: 1f, pitchMax: 1f);
            var randomPitch = MakeSoundCue(pitchMin: 0.9f, pitchMax: 1.1f);

            Assert.That(fixedPitch.HasDeterministicLength, Is.True);
            // 14 of the 75 shipping sound cues randomize pitch, so their length varies per play and the
            // stored number is only an upper bound (plan §4.3).
            Assert.That(randomPitch.HasDeterministicLength, Is.False);
        }

        private static CombatSoundCueDefinition MakeSoundCue(float pitchMin, float pitchMax)
            => new CombatSoundCueDefinition(
                "S001",
                "Assets/clip.wav",
                "Sfx",
                volume: 1f,
                pitchMin: pitchMin,
                pitchMax: pitchMax,
                cooldownSeconds: 0f,
                missingClipBehavior: "Silent");
    }
}

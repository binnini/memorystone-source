using NUnit.Framework;
using SeoulPlayup.Combat.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class SoundCatalogTests
    {
        [Test]
        public void KnownCueIdFindsEntry()
        {
            var catalog = SoundCatalog.CreateForTests(new SoundCatalog.Entry("ui.card.select", bus: SoundBus.Ui));
            try
            {
                Assert.That(catalog.TryGetEntry("ui.card.select", out var entry), Is.True);
                Assert.That(entry.Bus, Is.EqualTo(SoundBus.Ui));
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void MissingCueDoesNotThrow()
        {
            var catalog = SoundCatalog.CreateForTests(new SoundCatalog.Entry("ui.card.select"));
            try
            {
                Assert.DoesNotThrow(() => catalog.TryBeginPlayback("missing.cue", 0f, out _));
                Assert.That(catalog.TryBeginPlayback("missing.cue", 0f, out _), Is.EqualTo(SoundPlaybackStatus.MissingCue));
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void NullClipDoesNotThrow()
        {
            var catalog = SoundCatalog.CreateForTests(new SoundCatalog.Entry("ui.card.select", clip: null));
            try
            {
                Assert.DoesNotThrow(() => catalog.TryBeginPlayback("ui.card.select", 0f, out _));
                Assert.That(catalog.TryBeginPlayback("ui.card.select", 0f, out _), Is.EqualTo(SoundPlaybackStatus.MissingClip));
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void DeprecatedCueDoesNotResolve()
        {
            var catalog = SoundCatalog.CreateForTests(new SoundCatalog.Entry("ui.card.select", deprecated: true));
            try
            {
                Assert.That(catalog.TryGetEntry("ui.card.select", out _), Is.False);
                Assert.That(catalog.TryBeginPlayback("ui.card.select", 0f, out _), Is.EqualTo(SoundPlaybackStatus.MissingCue));
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void CooldownPreventsDuplicatePlayableCue()
        {
            var clip = AudioClip.Create("test-select", 64, 1, 44100, false);
            var catalog = SoundCatalog.CreateForTests(new SoundCatalog.Entry("ui.card.select", clip, SoundBus.Ui, cooldownSeconds: 0.5f));
            try
            {
                Assert.That(catalog.TryBeginPlayback("ui.card.select", 1f, out _), Is.EqualTo(SoundPlaybackStatus.Playable));
                Assert.That(catalog.TryBeginPlayback("ui.card.select", 1.25f, out _), Is.EqualTo(SoundPlaybackStatus.Cooldown));
                Assert.That(catalog.TryBeginPlayback("ui.card.select", 1.5f, out _), Is.EqualTo(SoundPlaybackStatus.Playable));
            }
            finally
            {
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(clip);
            }
        }
    }
}


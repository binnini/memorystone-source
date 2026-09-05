using System.Reflection;
using NUnit.Framework;
using SeoulPlayup.Combat.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// Covers the persistence/state logic of <see cref="DisplaySettingsService"/>. The actual
    /// Screen.SetResolution call inside Apply() is a no-op in the editor, so these tests exercise the
    /// PlayerPrefs mirror, the lazy load, and the change event — the visible fullscreen switch itself is
    /// verified in a standalone build.
    /// </summary>
    public sealed class DisplaySettingsServiceTests
    {
        private const string FullscreenKey = "seoulplayup.display.fullscreen";

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey(FullscreenKey);
            PlayerPrefs.Save();
            ResetLoadedFlag();
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(FullscreenKey);
            PlayerPrefs.Save();
            ResetLoadedFlag();
        }

        // Forces the next property/EnsureLoaded access to re-read PlayerPrefs, simulating a fresh session.
        private static void ResetLoadedFlag()
        {
            typeof(DisplaySettingsService)
                .GetField("loaded", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, false);
        }

        [Test]
        public void DefaultsToFullscreenWhenUnset()
        {
            Assert.IsTrue(DisplaySettingsService.Fullscreen);
        }

        [Test]
        public void SetFullscreenMirrorsToPlayerPrefs()
        {
            DisplaySettingsService.SetFullscreen(false);
            Assert.AreEqual(0, PlayerPrefs.GetInt(FullscreenKey, 1));
            Assert.IsFalse(DisplaySettingsService.Fullscreen);

            DisplaySettingsService.SetFullscreen(true);
            Assert.AreEqual(1, PlayerPrefs.GetInt(FullscreenKey, 0));
            Assert.IsTrue(DisplaySettingsService.Fullscreen);
        }

        [Test]
        public void EnsureLoadedReadsPersistedValue()
        {
            PlayerPrefs.SetInt(FullscreenKey, 0);
            PlayerPrefs.Save();
            ResetLoadedFlag();

            Assert.IsFalse(DisplaySettingsService.Fullscreen);

            PlayerPrefs.SetInt(FullscreenKey, 1);
            PlayerPrefs.Save();
            ResetLoadedFlag();

            Assert.IsTrue(DisplaySettingsService.Fullscreen);
        }

        [Test]
        public void SetFullscreenRaisesSettingsChangedOnlyOnChange()
        {
            // Start from a known state (default true after SetUp).
            var raised = 0;
            void Handler() => raised++;
            DisplaySettingsService.SettingsChanged += Handler;
            try
            {
                DisplaySettingsService.SetFullscreen(true); // no change -> no event
                Assert.AreEqual(0, raised);

                DisplaySettingsService.SetFullscreen(false); // change -> one event
                Assert.AreEqual(1, raised);
            }
            finally
            {
                DisplaySettingsService.SettingsChanged -= Handler;
            }
        }
    }
}

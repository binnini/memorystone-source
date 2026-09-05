using System;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Persistent store + applier for the fullscreen/windowed display preference. Mirrors
    /// <see cref="SoundSettingsService"/> (static store + PlayerPrefs mirror + SettingsChanged event +
    /// lazy EnsureLoaded).
    ///
    /// Lives in SeoulPlayup.Combat (Unity layer), matching its namespace. It used to sit in the Audio
    /// assembly so the sound settings panel could reference it directly, but no Audio-assembly type uses
    /// it any more; the real consumers are CombatPauseMenuController (this assembly) and SeoulPlayup.Flow
    /// (LobbyController + SceneFlowController, which calls <see cref="Apply"/> once at boot) — both of
    /// which reference SeoulPlayup.Combat. Screen changes are ignored in the editor, so the visible
    /// effect is verified in a standalone build.
    /// </summary>
    public static class DisplaySettingsService
    {
        private const string FullscreenKey = "seoulplayup.display.fullscreen";

        // Fraction of the native resolution used for the windowed size. Kept fixed (resizable window is
        // allowed via PlayerSettings, but we still open at a sane default size).
        private const float WindowedScale = 0.8f;

        private static bool loaded;
        private static bool fullscreen = true;

        public static event Action SettingsChanged;

        public static bool Fullscreen
        {
            get
            {
                EnsureLoaded();
                return fullscreen;
            }
            set => SetFullscreen(value);
        }

        /// <summary>Localized label for a toggle button that reflects the current mode.</summary>
        public static string ModeLabel
        {
            get
            {
                EnsureLoaded();
                return fullscreen ? "화면 모드: 전체화면" : "화면 모드: 창 모드";
            }
        }

        public static void SetFullscreen(bool value)
        {
            EnsureLoaded();
            if (fullscreen == value)
                return;

            fullscreen = value;
            PlayerPrefs.SetInt(FullscreenKey, fullscreen ? 1 : 0);
            PlayerPrefs.Save();
            Apply();
            SettingsChanged?.Invoke();
        }

        public static void EnsureLoaded()
        {
            if (loaded)
                return;

            loaded = true;
            fullscreen = PlayerPrefs.GetInt(FullscreenKey, 1) != 0;
        }

        /// <summary>
        /// Pushes the current preference to the Screen. Fullscreen -> borderless fullscreen window at the
        /// display's native resolution; windowed -> <see cref="WindowedScale"/> of the native resolution,
        /// windowed. Exclusive fullscreen is intentionally avoided (alt-tab issues). Call once at boot and
        /// whenever the preference changes. Ignored by the editor; effective only in a player build.
        /// </summary>
        public static void Apply()
        {
            EnsureLoaded();

            var nativeWidth = Display.main.systemWidth;
            var nativeHeight = Display.main.systemHeight;

            if (fullscreen)
            {
                Screen.SetResolution(nativeWidth, nativeHeight, FullScreenMode.FullScreenWindow);
            }
            else
            {
                var width = Mathf.RoundToInt(nativeWidth * WindowedScale);
                var height = Mathf.RoundToInt(nativeHeight * WindowedScale);
                Screen.SetResolution(width, height, FullScreenMode.Windowed);
            }
        }
    }
}

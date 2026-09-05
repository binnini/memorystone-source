using System;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    public enum SoundSettingsChannel
    {
        Bgm,
        Sfx,
        Voiceover
    }

    /// <summary>
    /// Small persistent store for player sound-channel volumes. Values live in memory for scene
    /// transitions and are mirrored to PlayerPrefs so returning sessions keep the same sliders.
    /// </summary>
    public static class SoundSettingsService
    {
        private const string BgmKey = "seoulplayup.sound.bgm";
        private const string SfxKey = "seoulplayup.sound.sfx";
        private const string VoiceoverKey = "seoulplayup.sound.voiceover";

        private static bool loaded;
        private static float bgmVolume = 1f;
        private static float sfxVolume = 1f;
        private static float voiceoverVolume = 1f;

        public static event Action SettingsChanged;

        public static float BgmVolume
        {
            get
            {
                EnsureLoaded();
                return bgmVolume;
            }
            set => SetVolume(SoundSettingsChannel.Bgm, value);
        }

        public static float SfxVolume
        {
            get
            {
                EnsureLoaded();
                return sfxVolume;
            }
            set => SetVolume(SoundSettingsChannel.Sfx, value);
        }

        public static float VoiceoverVolume
        {
            get
            {
                EnsureLoaded();
                return voiceoverVolume;
            }
            set => SetVolume(SoundSettingsChannel.Voiceover, value);
        }

        public static float GetVolume(SoundSettingsChannel channel)
        {
            EnsureLoaded();
            switch (channel)
            {
                case SoundSettingsChannel.Bgm:
                    return bgmVolume;
                case SoundSettingsChannel.Sfx:
                    return sfxVolume;
                case SoundSettingsChannel.Voiceover:
                    return voiceoverVolume;
                default:
                    return 1f;
            }
        }

        public static void SetVolume(SoundSettingsChannel channel, float value)
        {
            EnsureLoaded();
            value = Mathf.Clamp01(value);

            switch (channel)
            {
                case SoundSettingsChannel.Bgm:
                    if (Mathf.Approximately(bgmVolume, value))
                        return;
                    bgmVolume = value;
                    PlayerPrefs.SetFloat(BgmKey, bgmVolume);
                    break;
                case SoundSettingsChannel.Sfx:
                    if (Mathf.Approximately(sfxVolume, value))
                        return;
                    sfxVolume = value;
                    PlayerPrefs.SetFloat(SfxKey, sfxVolume);
                    break;
                case SoundSettingsChannel.Voiceover:
                    if (Mathf.Approximately(voiceoverVolume, value))
                        return;
                    voiceoverVolume = value;
                    PlayerPrefs.SetFloat(VoiceoverKey, voiceoverVolume);
                    break;
            }

            PlayerPrefs.Save();
            SettingsChanged?.Invoke();
        }

        public static void EnsureLoaded()
        {
            if (loaded)
                return;

            loaded = true;
            bgmVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(BgmKey, 1f));
            sfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxKey, 1f));
            voiceoverVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(VoiceoverKey, 1f));
        }
    }
}

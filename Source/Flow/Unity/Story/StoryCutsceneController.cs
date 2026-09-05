using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Flow.Unity.Story;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SeoulPlayup.Flow.Unity
{
    /// <summary>
    /// Drives a visual-novel style cutscene: an illustration background, a bottom text bar with an
    /// optional speaker name, click/Enter to advance and ESC to skip the whole cutscene. The UI is
    /// authored as a prefab and only instantiated here; this controller never builds UI at runtime.
    /// The <see cref="StoryScriptAsset"/> to play comes from <see cref="GameSession"/> (set by the
    /// flow), with a serialized default so the scene can be previewed standalone in the editor.
    /// </summary>
    public sealed class StoryCutsceneController : MonoBehaviour
    {
        [Header("Bound UI (assigned on the prefab)")]
        [SerializeField] private Image illustrationImage;
        [SerializeField] private GameObject speakerPanel;
        [SerializeField] private TMP_Text speakerLabel;
        [SerializeField] private TMP_Text bodyLabel;
        [SerializeField] private GameObject continueIndicator;
        [SerializeField] private GameObject loadingPanel;
        [SerializeField] private AudioSource bgmSource;
        [SerializeField] private AudioSource sfxSource;

        [Header("Standalone preview fallback")]
        [SerializeField] private StoryScriptAsset defaultStory;

        [Header("Tuning")]
        [SerializeField] private float charactersPerSecond = 45f;
        [SerializeField] private bool advanceEndsScene = true;

        private StoryLine[] lines = System.Array.Empty<StoryLine>();
        private int currentIndex = -1;
        private bool isTyping;
        private bool finished;
        private float revealedCharacters;
        private int totalCharacters;

        private void Start()
        {
            var session = GameSession.GetOrCreate();
            var story = session.PendingCutsceneStory != null ? session.PendingCutsceneStory : defaultStory;

            lines = story != null ? story.Lines : System.Array.Empty<StoryLine>();

            if (illustrationImage != null)
            {
                var startSprite = story != null ? story.DefaultIllustration : null;
                ApplyIllustration(startSprite);
            }

            PlayBgm(story != null ? story.BackgroundMusic : null);

            if (continueIndicator != null)
            {
                continueIndicator.SetActive(false);
            }

            if (lines.Length == 0)
            {
                Debug.LogWarning("StoryCutsceneController has no story lines; finishing immediately.", this);
                Finish();
                return;
            }

            ShowLine(0);
        }

        private void Update()
        {
            if (finished)
            {
                return;
            }

            // ESC skips the entire cutscene immediately.
            if (SkipPressedThisFrame())
            {
                Finish();
                return;
            }

            if (isTyping)
            {
                AdvanceTypewriter();
            }

            if (!AdvancePressedThisFrame())
            {
                return;
            }

            if (isTyping)
            {
                CompleteTypewriter();
                return;
            }

            if (currentIndex + 1 < lines.Length)
            {
                ShowLine(currentIndex + 1);
            }
            else
            {
                Finish();
            }
        }

        private void ShowLine(int index)
        {
            currentIndex = index;
            var line = lines[index];
            var speaker = line != null ? line.speaker : string.Empty;
            var text = line != null ? line.text : string.Empty;

            if (line != null && line.illustration != null)
            {
                ApplyIllustration(line.illustration);
            }

            if (line != null && line.sound != null)
            {
                PlayLineSound(line.sound);
            }

            var hasSpeaker = !string.IsNullOrEmpty(speaker);
            if (speakerLabel != null)
            {
                speakerLabel.text = speaker;
            }

            if (speakerPanel != null)
            {
                speakerPanel.SetActive(hasSpeaker);
            }
            else if (speakerLabel != null)
            {
                speakerLabel.gameObject.SetActive(hasSpeaker);
            }

            if (continueIndicator != null)
            {
                continueIndicator.SetActive(false);
            }

            if (bodyLabel != null)
            {
                bodyLabel.text = text ?? string.Empty;
                bodyLabel.ForceMeshUpdate();
                totalCharacters = bodyLabel.textInfo.characterCount;
                bodyLabel.maxVisibleCharacters = 0;
            }
            else
            {
                totalCharacters = 0;
            }

            revealedCharacters = 0f;
            isTyping = totalCharacters > 0 && charactersPerSecond > 0f;
            if (!isTyping)
            {
                CompleteTypewriter();
            }
        }

        private void ApplyIllustration(Sprite sprite)
        {
            if (illustrationImage == null)
            {
                return;
            }

            if (sprite != null)
            {
                illustrationImage.sprite = sprite;
                illustrationImage.color = Color.white;
                illustrationImage.enabled = true;
            }
            // When no sprite is provided, keep whatever is currently shown (VN convention). If
            // nothing has ever been shown the prefab's placeholder background remains visible.
        }

        private void AdvanceTypewriter()
        {
            revealedCharacters += charactersPerSecond * Time.unscaledDeltaTime;
            var visible = Mathf.FloorToInt(revealedCharacters);
            if (visible >= totalCharacters)
            {
                CompleteTypewriter();
                return;
            }

            if (bodyLabel != null)
            {
                bodyLabel.maxVisibleCharacters = visible;
            }
        }

        private void CompleteTypewriter()
        {
            isTyping = false;
            if (bodyLabel != null)
            {
                bodyLabel.maxVisibleCharacters = Mathf.Max(totalCharacters, 0);
            }

            if (continueIndicator != null)
            {
                continueIndicator.SetActive(true);
            }
        }

        private void Finish()
        {
            if (finished)
            {
                return;
            }

            finished = true;

            if (continueIndicator != null)
            {
                continueIndicator.SetActive(false);
            }

            if (!advanceEndsScene)
            {
                return;
            }

            // Show the loading panel so the player perceives the transition before the next scene
            // is activated. The async load (driven by SceneFlowController) keeps this scene visible
            // until MainGameplay is ready, so this panel bridges the gap with no blank flash.
            if (loadingPanel != null)
            {
                loadingPanel.transform.SetAsLastSibling();
                loadingPanel.SetActive(true);
            }

            SceneFlowController.GetOrCreate().ContinueAfterCutscene();
        }

        private void PlayBgm(AudioClip clip)
        {
            if (bgmSource == null || clip == null)
            {
                return;
            }

            SoundSettingsService.EnsureLoaded();
            bgmSource.clip = clip;
            bgmSource.loop = true;
            bgmSource.playOnAwake = false;
            bgmSource.spatialBlend = 0f;
            bgmSource.volume = SoundSettingsService.BgmVolume;
            bgmSource.Play();
        }

        private void PlayLineSound(AudioClip clip)
        {
            var source = sfxSource != null ? sfxSource : bgmSource;
            if (source == null || clip == null)
            {
                return;
            }

            SoundSettingsService.EnsureLoaded();
            source.PlayOneShot(clip, SoundSettingsService.SfxVolume);
        }

        private static bool AdvancePressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            var keyPressed = keyboard != null &&
                (keyboard.enterKey.wasPressedThisFrame ||
                 keyboard.numpadEnterKey.wasPressedThisFrame ||
                 keyboard.spaceKey.wasPressedThisFrame);
            var clickPressed = mouse != null && mouse.leftButton.wasPressedThisFrame;
            return keyPressed || clickPressed;
#else
            return Input.GetMouseButtonDown(0) ||
                   Input.GetKeyDown(KeyCode.Return) ||
                   Input.GetKeyDown(KeyCode.KeypadEnter) ||
                   Input.GetKeyDown(KeyCode.Space);
#endif
        }

        private static bool SkipPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }
    }
}

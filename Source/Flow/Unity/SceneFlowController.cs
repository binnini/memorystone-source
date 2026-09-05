using System.Collections;
using SeoulPlayup.Combat.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SeoulPlayup.Flow.Unity
{
    public sealed class SceneFlowController : MonoBehaviour
    {
        public const string DefaultBootSceneName = "Boot";
        public const string DefaultLobbySceneName = "Lobby";
        public const string DefaultMainGameplaySceneName = "MainGameplay";
        public const string DefaultStoryCutsceneSceneName = "StoryCutscene";

        public static SceneFlowController Instance { get; private set; }

        [SerializeField] private string lobbySceneName = DefaultLobbySceneName;
        [SerializeField] private string mainGameplaySceneName = DefaultMainGameplaySceneName;
        [SerializeField] private string storyCutsceneSceneName = DefaultStoryCutsceneSceneName;
        [SerializeField] private bool loadLobbyOnStart = true;

        public static SceneFlowController GetOrCreate()
        {
            if (Instance != null)
                return Instance;

            var existing = FindFirstObjectByType<SceneFlowController>();
            if (existing != null)
                return existing;

            var root = GameObject.Find("AppRoot") ?? new GameObject("AppRoot");
            return root.AddComponent<SceneFlowController>();
        }

        public void LoadLobby()
        {
            SceneManager.LoadScene(lobbySceneName);
        }

        public void StartStage(StageDefinition stage)
        {
            var session = GameSession.GetOrCreate();
            session.SetCurrentStage(stage);
            SceneManager.LoadScene(mainGameplaySceneName);
        }

        /// <summary>
        /// Starts a stage, playing its intro story cutscene first when one is assigned. When no
        /// intro is set this is equivalent to <see cref="StartStage"/>.
        /// </summary>
        public void StartStageWithIntro(StageDefinition stage)
        {
            var session = GameSession.GetOrCreate();
            session.SetCurrentStage(stage);

            if (stage != null && stage.IntroStory != null)
            {
                session.SetPendingCutscene(stage.IntroStory, StoryCutsceneAfter.EnterCurrentStageGameplay);
                SceneManager.LoadScene(storyCutsceneSceneName);
                return;
            }

            SceneManager.LoadScene(mainGameplaySceneName);
        }

        /// <summary>
        /// Invoked from the victory UI "다음으로" button. Plays the cleared stage's outro cutscene
        /// when one is assigned, otherwise advances straight to the next stage (or lobby).
        /// </summary>
        public void PlayStageOutroOrAdvance()
        {
            var session = GameSession.GetOrCreate();
            var current = session.CurrentStage;

            if (current != null && current.OutroStory != null)
            {
                session.SetPendingCutscene(current.OutroStory, StoryCutsceneAfter.AdvanceToNextStageWithIntro);
                SceneManager.LoadScene(storyCutsceneSceneName);
                return;
            }

            LoadNextStage();
        }

        /// <summary>
        /// Called by <see cref="StoryCutsceneController"/> once the player finishes the cutscene.
        /// </summary>
        /// <summary>
        /// Debug helper: plays the given story in the cutscene scene, then returns to the current
        /// stage's gameplay. Used by the combat debug panel's Intro/Outro preview buttons.
        /// </summary>
        public void PreviewCutscene(Story.StoryScriptAsset story)
        {
            if (story == null)
            {
                Debug.LogWarning("PreviewCutscene called with no story asset.", this);
                return;
            }

            var session = GameSession.GetOrCreate();
            session.SetPendingCutscene(story, StoryCutsceneAfter.EnterCurrentStageGameplay);
            SceneManager.LoadScene(storyCutsceneSceneName);
        }

        public void ContinueAfterCutscene()
        {
            var session = GameSession.GetOrCreate();
            var after = session.PendingCutsceneAfter;
            session.ClearPendingCutscene();

            switch (after)
            {
                case StoryCutsceneAfter.EnterCurrentStageGameplay:
                    // Async load so the cutscene scene's loading panel stays visible during the
                    // transition (a synchronous LoadScene would freeze the frame instead).
                    StartCoroutine(LoadSceneWithMinimumAsync(mainGameplaySceneName));
                    break;
                case StoryCutsceneAfter.AdvanceToNextStageWithIntro:
                    var next = session.CurrentStage != null ? session.CurrentStage.NextStage : null;
                    if (next != null)
                        StartStageWithIntro(next);
                    else
                        ReturnToLobby();
                    break;
                default:
                    ReturnToLobby();
                    break;
            }
        }

        private IEnumerator LoadSceneWithMinimumAsync(string sceneName, float minimumLoadingSeconds = 0.6f)
        {
            var elapsed = 0f;
            var operation = SceneManager.LoadSceneAsync(sceneName);
            if (operation == null)
            {
                SceneManager.LoadScene(sceneName);
                yield break;
            }

            operation.allowSceneActivation = false;
            while (operation.progress < 0.9f || elapsed < minimumLoadingSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            operation.allowSceneActivation = true;
        }

        public IEnumerator StartStageAsync(StageDefinition stage, System.Action<float> onProgress = null, float minimumLoadingSeconds = 0.5f)
        {
            var session = GameSession.GetOrCreate();
            session.SetCurrentStage(stage);

            var elapsed = 0f;
            var operation = SceneManager.LoadSceneAsync(mainGameplaySceneName);
            if (operation == null)
            {
                SceneManager.LoadScene(mainGameplaySceneName);
                yield break;
            }

            operation.allowSceneActivation = false;
            while (operation.progress < 0.9f || elapsed < minimumLoadingSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                onProgress?.Invoke(Mathf.Clamp01(operation.progress / 0.9f));
                yield return null;
            }

            onProgress?.Invoke(1f);
            operation.allowSceneActivation = true;
        }

        public void RestartCurrentStage()
        {
            var session = GameSession.GetOrCreate();
            StartStage(session.CurrentStage);
        }

        public void ReturnToLobby()
        {
            LoadLobby();
        }

        public void LoadNextStage()
        {
            var session = GameSession.GetOrCreate();
            var current = session.CurrentStage;
            if (current != null && current.NextStage != null)
            {
                StartStage(current.NextStage);
                return;
            }

            ReturnToLobby();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Apply the persisted display preference once at boot. SceneFlowController is the single
            // object that survives every load and initializes before any scene UI, so it is the uniform
            // place to push the fullscreen/windowed choice to the Screen. (No-op in the editor.)
            DisplaySettingsService.Apply();

            // Reclaim assets at every scene boundary. The in-game stage advance (and restart) path
            // swaps one heavy MainGameplay scene for another via a synchronous single-mode load;
            // without an explicit unload, assets orphaned by the outgoing stage stay resident and
            // accumulate. The editor masks this with extra headroom, but a player build has far less
            // and can crash. SceneFlowController is the one object that survives every load, so it is
            // the single, uniform place to drive cleanup for lobby/gameplay/cutscene transitions.
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Only single-mode loads replace the previous scene (and thus orphan its assets); additive
            // loads should not trigger a full unload.
            if (mode == LoadSceneMode.Single)
                StartCoroutine(UnloadOrphanedAssetsAfterLoad());
        }

        private IEnumerator UnloadOrphanedAssetsAfterLoad()
        {
            // Let the freshly loaded scene finish its first frame of initialization so anything it
            // still needs is referenced before we sweep. UnloadUnusedAssets only frees assets with no
            // live references, so this never reclaims something the new scene is about to use.
            yield return null;
            yield return Resources.UnloadUnusedAssets();
            System.GC.Collect();
        }

        private void Start()
        {
            if (loadLobbyOnStart && SceneManager.GetActiveScene().name == DefaultBootSceneName)
                LoadLobby();
        }
    }
}

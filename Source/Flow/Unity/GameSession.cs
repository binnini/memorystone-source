using SeoulPlayup.Combat.Runtime;
using UnityEngine;

namespace SeoulPlayup.Flow.Unity
{
    /// <summary>
    /// What the story cutscene should do once the player finishes advancing through it.
    /// </summary>
    public enum StoryCutsceneAfter
    {
        EnterCurrentStageGameplay,
        AdvanceToNextStageWithIntro,
        ReturnToLobby
    }

    public sealed class GameSession : MonoBehaviour
    {
        public static GameSession Instance { get; private set; }

        [SerializeField] private StageCatalog stageCatalog;
        [SerializeField] private StageDefinition currentStage;

        public StageCatalog StageCatalog => stageCatalog;
        public StageDefinition CurrentStage => currentStage != null ? currentStage : stageCatalog != null ? stageCatalog.DefaultStage : null;

        // Hand-off payload for the StoryCutscene scene. Set by the flow before loading the
        // cutscene scene; consumed by StoryCutsceneController on the other side.
        public Story.StoryScriptAsset PendingCutsceneStory { get; private set; }
        public StoryCutsceneAfter PendingCutsceneAfter { get; private set; }

        public void SetPendingCutscene(Story.StoryScriptAsset story, StoryCutsceneAfter after)
        {
            PendingCutsceneStory = story;
            PendingCutsceneAfter = after;
        }

        public void ClearPendingCutscene()
        {
            PendingCutsceneStory = null;
            PendingCutsceneAfter = StoryCutsceneAfter.ReturnToLobby;
        }

        // Hand-off payload for the lobby "이어하기" path. Set by LobbyController before loading the
        // gameplay scene; MainGameplayController applies it when the loaded stage matches its
        // StageId. Kept for the session so restarting the stage re-enters with the same loadout;
        // cleared on new game or defeat.
        public PlayerRunSaveEnvelope PendingRunContinue { get; private set; }

        public void SetPendingRunContinue(PlayerRunSaveEnvelope envelope)
        {
            PendingRunContinue = envelope;
        }

        public void ClearPendingRunContinue()
        {
            PendingRunContinue = null;
        }

        public static GameSession GetOrCreate()
        {
            if (Instance != null)
                return Instance;

            var existing = FindFirstObjectByType<GameSession>();
            if (existing != null)
                return existing;

            var root = GameObject.Find("AppRoot") ?? new GameObject("AppRoot");
            return root.AddComponent<GameSession>();
        }

        public void SetStageCatalog(StageCatalog catalog)
        {
            stageCatalog = catalog;
            if (currentStage == null && stageCatalog != null)
                currentStage = stageCatalog.DefaultStage;
        }

        public void SetCurrentStage(StageDefinition stage)
        {
            currentStage = stage != null ? stage : stageCatalog != null ? stageCatalog.DefaultStage : null;
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

            if (currentStage == null && stageCatalog != null)
                currentStage = stageCatalog.DefaultStage;
        }
    }
}

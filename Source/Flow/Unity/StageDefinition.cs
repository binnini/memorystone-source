using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Combat.Unity.Tutorial;
using SeoulPlayup.Map.Unity;
using UnityEngine;

namespace SeoulPlayup.Flow.Unity
{
    [CreateAssetMenu(fileName = "StageDefinition", menuName = "Seoul Playup/Flow/Stage Definition")]
    public sealed class StageDefinition : ScriptableObject
    {
        [SerializeField] private string stageId = "stage_001_prototype";
        [SerializeField] private string displayName = "Prototype Stage";
        [TextArea]
        [SerializeField] private string description = "Initial gameplay stage based on PrototypeTest.";
        [SerializeField] private HexSparseMapAuthoringSource mapSource;
        [Tooltip("Optional environment look (lighting rig, ambient/fog, post profile, visibility). Empty = keep the scene-authored look. Applied once at stage entry, before the map renders.")]
        [SerializeField] private EnvironmentLookPreset lookPreset;
        [Tooltip("Optional daytime look shown DURING the stage-intro trailer. The intro borrows this bright day look, then crossfades back to 'lookPreset' (night) over the finale ripple. Empty = no day treatment (trailer plays over the normal night look). Ignored unless playIntroCinematic is on.")]
        [SerializeField] private EnvironmentLookPreset introDayLookPreset;
        [Tooltip("Optional 'purified' look the memory-stone victory sweep crossfades TO (night -> clear) as the fog lifts. Empty = reuse 'introDayLookPreset'; empty both = no lighting change during the victory sequence.")]
        [SerializeField] private EnvironmentLookPreset victoryClearLookPreset;
        [SerializeField] private StageDefinition nextStage;
        [SerializeField] private PlayerStartingDeckAsset startingDeckOverride;
        [SerializeField] private bool keepStartingDeckOrderInPlayMode;
        [Tooltip("When enabled, this stage opens with a cinematic camera sweep over the map (overview -> landmarks -> memory stone) before the first turn starts.")]
        [SerializeField] private bool playIntroCinematic;
        [Tooltip("Play the memory-stone victory cinematic (camera sweep + ripple) when the stage is cleared. Off for the tutorial, which ends on a plain clear.")]
        [SerializeField] private bool playVictoryCinematic = true;
        [Tooltip("When enabled, monster placements tagged with a randomizationGroup on the map source are " +
                 "shuffled per stage entry (placement-randomization-plan). Default off — per-stage opt-in; " +
                 "untagged spawns and stages with this off keep the authored layout exactly.")]
        [SerializeField] private bool randomizePlacements;
        [SerializeField] private TutorialScriptAsset tutorialScript;
        [Header("Story cutscene (visual-novel)")]
        [Tooltip("Played before MainGameplay when the stage starts. Leave empty to skip the intro cutscene.")]
        [SerializeField] private SeoulPlayup.Flow.Unity.Story.StoryScriptAsset introStory;
        [Tooltip("Played when the stage is cleared and the player presses '다음으로' on the victory UI. Leave empty to skip the outro cutscene.")]
        [SerializeField] private SeoulPlayup.Flow.Unity.Story.StoryScriptAsset outroStory;
        [TextArea]
        [SerializeField] private string implementationNote;

        public string StageId => stageId;
        public string DisplayName => displayName;
        public string Description => description;
        public HexSparseMapAuthoringSource MapSource => mapSource;
        public EnvironmentLookPreset LookPreset => lookPreset;
        public EnvironmentLookPreset IntroDayLookPreset => introDayLookPreset;

        /// <summary>
        /// Look the victory sweep resolves to as the map is purified. Falls back to the intro's day preset so a
        /// stage that already authored a day look gets the night→day clear transition for free.
        /// </summary>
        public EnvironmentLookPreset VictoryClearLookPreset =>
            victoryClearLookPreset != null ? victoryClearLookPreset : introDayLookPreset;
        public StageDefinition NextStage => nextStage;
        public PlayerStartingDeckAsset StartingDeckOverride => startingDeckOverride;
        public bool KeepStartingDeckOrderInPlayMode => keepStartingDeckOrderInPlayMode;
        public bool PlayIntroCinematic => playIntroCinematic;
        public bool PlayVictoryCinematic => playVictoryCinematic;
        /// <summary>배치 랜덤화 옵트인(placement-randomization-plan §2-3c). 기본 off.</summary>
        public bool RandomizePlacements => randomizePlacements;
        public TutorialScriptAsset TutorialScript => tutorialScript;
        /// <summary>
        /// 스테이지 진입 전에 재생할 스토리. <see cref="Story.StoryCutsceneFeature.Enabled"/>가 꺼져 있으면
        /// 저작 여부와 무관하게 null이다 — 호출부는 전부 "스토리 없음" 갈래로 떨어진다.
        /// 저작 원본은 <c>introStory</c> 필드에 그대로 남아 있다.
        /// </summary>
        public SeoulPlayup.Flow.Unity.Story.StoryScriptAsset IntroStory =>
            Story.StoryCutsceneFeature.Enabled ? introStory : null;

        /// <summary>
        /// 스테이지 클리어 후 재생할 스토리. <see cref="IntroStory"/>와 같은 스위치를 공유한다.
        /// </summary>
        public SeoulPlayup.Flow.Unity.Story.StoryScriptAsset OutroStory =>
            Story.StoryCutsceneFeature.Enabled ? outroStory : null;
        public string ImplementationNote => implementationNote;

        public void Configure(
            string id,
            string stageDisplayName,
            string stageDescription,
            HexSparseMapAuthoringSource source,
            StageDefinition followingStage = null,
            string note = null,
            PlayerStartingDeckAsset deckOverride = null,
            bool keepDeckOrderInPlayMode = false,
            TutorialScriptAsset stageTutorialScript = null,
            bool playStageIntroCinematic = false,
            SeoulPlayup.Flow.Unity.Story.StoryScriptAsset stageIntroStory = null,
            SeoulPlayup.Flow.Unity.Story.StoryScriptAsset stageOutroStory = null)
        {
            stageId = id;
            displayName = stageDisplayName;
            description = stageDescription;
            mapSource = source;
            nextStage = followingStage;
            startingDeckOverride = deckOverride;
            keepStartingDeckOrderInPlayMode = keepDeckOrderInPlayMode;
            tutorialScript = stageTutorialScript;
            playIntroCinematic = playStageIntroCinematic;
            introStory = stageIntroStory;
            outroStory = stageOutroStory;
            implementationNote = note;
        }
    }
}


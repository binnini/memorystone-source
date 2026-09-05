using System.Collections;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Unity;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Flow.Unity
{
    [DefaultExecutionOrder(-1000)]
    public sealed class MainGameplayController : MonoBehaviour
    {
        [SerializeField] private HexSparseMapSourceLoader mapSourceLoader;
        [SerializeField] private MapCombatController combatController;
        [SerializeField] private bool applyStageMapSource = true;
        [SerializeField] private float minimumReadyScreenSeconds = 0.8f;
        [SerializeField] private float gameStartDwellSeconds = 1.2f;

        public StageDefinition CurrentStage { get; private set; }

        private CanvasGroup loadingOverlay;
        private TMP_Text loadingLabel;
        private PlayerRunSaveStore runSaveStore;
        private CombatSuspendStore combatSuspendStore;
        private CombatState saveSubscribedState;

        private void Awake()
        {
            DestroyStaleLoadingOverlays();

            if (mapSourceLoader == null)
                mapSourceLoader = FindFirstObjectByType<HexSparseMapSourceLoader>();

            if (combatController == null)
                combatController = FindFirstObjectByType<MapCombatController>();

            if (combatController != null)
            {
                combatController.SetAutomaticInitialization(false);
                combatController.SetDrawOpeningHandsOnInitialize(false);
            }

            // P5: host the ESC pause menu for the gameplay scene. Runtime-created so the scene
            // needs no authored object; the menu gates input through the combat controller and
            // routes lobby/quit through the flow layer.
            CombatPauseMenuController.GetOrCreate(combatController);
        }

        private IEnumerator Start()
        {
            ShowLoadingOverlay("스테이지 로딩 중...");

            var session = GameSession.GetOrCreate();
            CurrentStage = session.CurrentStage;
            var resumedFromSuspend = false;
            if (combatController != null)
            {
                combatController.SetStartingDeckOverride(CurrentStage != null ? CurrentStage.StartingDeckOverride : null);
                combatController.SetKeepStartingDeckOrderInPlayMode(CurrentStage != null && CurrentStage.KeepStartingDeckOrderInPlayMode);
                combatController.SetPlayStageIntroCinematic(CurrentStage != null && CurrentStage.PlayIntroCinematic);
                combatController.SetPlayMemoryStoneVictorySequence(CurrentStage == null || CurrentStage.PlayVictoryCinematic);
                combatController.SetStageIntroLookPresets(
                    CurrentStage != null ? CurrentStage.IntroDayLookPreset : null,
                    CurrentStage != null ? CurrentStage.LookPreset : null);
                combatController.SetMemoryStoneVictoryClearLookPreset(
                    CurrentStage != null ? CurrentStage.VictoryClearLookPreset : null);
                combatController.SetTutorialScript(CurrentStage != null ? CurrentStage.TutorialScript : null);

                var pendingContinue = session.PendingRunContinue;
                if (pendingContinue != null && CurrentStage != null && pendingContinue.StageId == CurrentStage.StageId)
                {
                    combatController.SetPlayerRunRestore(pendingContinue.Player);
                    Debug.Log($"MainGameplay resuming saved run (stage '{pendingContinue.StageId}', turn {pendingContinue.OverallTurn}).", this);
                }

                // ② suspend resume takes priority: if a full-snapshot slot exists for this stage, restore
                // the exact mid-combat state. The slot is deleted after a successful load (anti-scum).
                // Gated on a pending continue so a NEW game never resumes a stale snapshot — the lobby
                // sets PendingRunContinue only on "이어하기" and clears it (and deletes the slot) on new game.
                CombatSuspendEnvelope resumedSuspendEnvelope = null;
                if (pendingContinue != null && CurrentStage != null && !string.IsNullOrWhiteSpace(CurrentStage.StageId))
                {
                    combatSuspendStore ??= new CombatSuspendStore();
                    if (combatSuspendStore.TryLoad(out var suspendEnvelope, out _) &&
                        suspendEnvelope.StageId == CurrentStage.StageId)
                    {
                        combatController.SetCombatSuspendRestore(suspendEnvelope);
                        resumedSuspendEnvelope = suspendEnvelope;
                        resumedFromSuspend = true;
                        Debug.Log($"MainGameplay resuming suspended combat (stage '{suspendEnvelope.StageId}', turn {suspendEnvelope.OverallTurn}).", this);
                    }
                }

                // 배치 랜덤화 시드(placement-randomization-plan §2-2, Q1: 스테이지 진입마다 발급).
                // 재개(②>①)는 세이브의 시드로 같은 배치를 재생성하고, 시드 없는 구세이브는
                // 「랜덤화 미적용(저작 원본)」으로 해석한다. 새 진입만 새 시드를 굴린다.
                if (CurrentStage != null)
                {
                    var randomizePlacements = CurrentStage.RandomizePlacements;
                    bool hasPlacementSeed;
                    int placementSeed;
                    if (resumedSuspendEnvelope != null)
                    {
                        hasPlacementSeed = resumedSuspendEnvelope.HasPlacementSeed;
                        placementSeed = resumedSuspendEnvelope.PlacementSeed;
                    }
                    else if (pendingContinue != null)
                    {
                        hasPlacementSeed = pendingContinue.HasPlacementSeed;
                        placementSeed = pendingContinue.PlacementSeed;
                    }
                    else
                    {
                        // 🔑 런 시드 결정 지점(seed-determinism-handoff P1). 배치 랜덤화가 꺼진
                        // 스테이지에서도 발급한다 — 전투 판정·덱 셔플·보상이 이 값에서 갈라지므로
                        // 시드가 없으면 그 축들이 전부 무시드로 돌아간다. 사용자가 지정한 시드가
                        // 있으면(디버그 패널 「이 시드로 재시작」) 그것을, 없으면 무작위.
                        hasPlacementSeed = true;
                        placementSeed = RunSeedRequest.TryConsume(out var requestedSeed)
                            ? requestedSeed
                            : System.Guid.NewGuid().GetHashCode();
                    }

                    combatController.SetPlacementRandomization(randomizePlacements, hasPlacementSeed, placementSeed, CurrentStage.StageId);
                    if (hasPlacementSeed)
                    {
                        Debug.Log($"MainGameplay run seed={placementSeed} (stage '{CurrentStage.StageId}', placement randomization {(randomizePlacements ? "on" : "off")}).", this);
                        RunSeedHudLabel.Show(placementSeed);
                    }
                }
            }

            if (CurrentStage == null)
            {
                Debug.LogWarning("MainGameplayController started without a selected stage; keeping scene-authored PrototypeTest defaults.", this);
                if (combatController != null)
                    yield return combatController.BeginGameplayStartSequence(gameStartDwellSeconds);
                HideLoadingOverlay();
                yield break;
            }

            Debug.Log($"MainGameplay loaded stage '{CurrentStage.StageId}' ({CurrentStage.DisplayName}).", this);

            // Apply the stage's environment look BEFORE the map renders: the visibility mask/materials are
            // built during Render, so the look values (incl. visibility coefficients) must be set first.
            // A stage with no preset is a no-op — the scene-authored night look is kept unchanged.
            if (CurrentStage.LookPreset != null)
            {
                var lookNote = SeoulPlayup.Map.Unity.LookPresetApplier.Apply(CurrentStage.LookPreset);
                Debug.Log($"MainGameplay applied look preset '{CurrentStage.LookPreset.name}': {lookNote}", this);
            }

            // 뷰 일원화(placement-randomization-plan §5, Q7-A 단일 소스): 전투 컨트롤러가 있으면 뷰
            // 렌더의 유일한 소스는 컨트롤러의 LoadedMap(랜덤화 적용본)이다 — InitializeIntegration이
            // RenderSelected3DView(LoadedMap)로 그린다. 여기서 저작 원본을 선행 렌더하면 상자 셔플
            // 이후 전투 상태와 화면이 갈라진다(뷰가 소스에서 자기 맵을 따로 빌드하기 때문).
            // 컨트롤러가 없는 씬 구성에서만 로더가 직접 그린다.
            if (applyStageMapSource && CurrentStage.MapSource != null && mapSourceLoader != null)
                mapSourceLoader.ApplySource(CurrentStage.MapSource, renderImmediately: combatController == null);

            if (applyStageMapSource && CurrentStage.MapSource != null && combatController != null)
                combatController.ApplySparseSource(CurrentStage.MapSource);

            yield return null;
            if (loadingLabel != null)
                loadingLabel.text = "게임 준비 중...";

            if (combatController != null && !combatController.IsInitialized)
            {
                Debug.Log("MainGameplay initializing combat after stage loading.", this);
                combatController.InitializeIntegration();
            }

            // Anti-scum (RS-12): delete the slot only when the restore was actually applied during init
            // (CombatSuspendRestoreApplied), not merely because a save loaded — a skipped init must never
            // discard an unrestored save. Strict order: load → restore-applied → delete.
            if (resumedFromSuspend && combatController != null && combatController.CombatSuspendRestoreApplied)
            {
                combatSuspendStore?.Delete();
            }

            HookRunAutoSave();

            if (minimumReadyScreenSeconds > 0f)
                yield return new WaitForSecondsRealtime(minimumReadyScreenSeconds);

            HideLoadingOverlay();

            if (combatController != null)
            {
                Debug.Log("MainGameplay starting gated game-start sequence.", this);
                yield return combatController.BeginGameplayStartSequence(gameStartDwellSeconds);
                combatController.BeginTutorial();
                Debug.Log("MainGameplay game-start sequence completed.", this);
            }
        }

        public void ReturnToLobby()
        {
            SaveCombatSuspend();
            DestroyLoadingOverlay();
            SceneFlowController.GetOrCreate().ReturnToLobby();
        }

        /// <summary>
        /// ② suspend trigger (RS-10): captures the full mid-combat snapshot into the suspend slot when the
        /// player leaves an in-progress combat. No-op for a terminal (Victory/Defeat) combat or when no
        /// stage/state is active. OverallTurnEnded fires at a consistent turn boundary, so the live state at
        /// a lobby exit is already settled between actions.
        /// </summary>
        public void SaveCombatSuspend()
        {
            var state = combatController != null ? combatController.State : null;
            if (state == null || state.IsTerminal ||
                CurrentStage == null || string.IsNullOrWhiteSpace(CurrentStage.StageId))
            {
                return;
            }

            // 🔴 IsTerminal만으로는 부족하다: 몬스터 연출이 재생되는 동안 규칙 상태는 공격 전으로
            // 되감겨 있어 죽은 판에서도 플레이어가 살아 있는 것으로 보인다(따라서 위 가드를 통과한다).
            // 확정된 결과가 사망이면 이 전투는 이어서 할 것이 아니라 패배이므로 중단 저장하지 않는다.
            if (state.HasDeferredMonsterActionPlayerDeath)
            {
                return;
            }

            combatSuspendStore ??= new CombatSuspendStore();
            var hasPlacementSeed = combatController.TryGetRunSeed(out var placementSeed);
            var envelope = CombatSuspendEnvelope.Create(
                CurrentStage.StageId, state.OverallTurnNumber, state.CreateSuspendSnapshot(),
                hasPlacementSeed, placementSeed, combatController.RewardRandomCursor);
            if (!combatSuspendStore.TrySave(envelope, out var reason))
            {
                Debug.LogWarning($"Combat suspend save failed: {reason}", this);
            }
        }

        public void RestartStage()
        {
            SceneFlowController.GetOrCreate().RestartCurrentStage();
        }

        public void ContinueToNextStageOrLobby()
        {
            SceneFlowController.GetOrCreate().LoadNextStage();
        }

        // Invoked (via SendMessage) by the victory overlay "다음으로" button. Plays the cleared
        // stage's outro story cutscene when assigned, otherwise advances to the next stage.
        public void PlayStageOutroOrAdvance()
        {
            DestroyLoadingOverlay();
            SceneFlowController.GetOrCreate().PlayStageOutroOrAdvance();
        }

        // Debug preview hooks (invoked via SendMessage from the combat debug panel). Play the
        // current stage's intro/outro cutscene immediately, returning to gameplay afterward.
        public void DebugPlayIntroCutscene()
        {
            var stage = CurrentStage != null ? CurrentStage : GameSession.GetOrCreate().CurrentStage;
            if (stage == null || stage.IntroStory == null)
            {
                Debug.LogWarning("DebugPlayIntroCutscene: current stage has no intro story assigned.", this);
                return;
            }

            DestroyLoadingOverlay();
            SceneFlowController.GetOrCreate().PreviewCutscene(stage.IntroStory);
        }

        public void DebugPlayOutroCutscene()
        {
            var stage = CurrentStage != null ? CurrentStage : GameSession.GetOrCreate().CurrentStage;
            if (stage == null || stage.OutroStory == null)
            {
                Debug.LogWarning("DebugPlayOutroCutscene: current stage has no outro story assigned.", this);
                return;
            }

            DestroyLoadingOverlay();
            SceneFlowController.GetOrCreate().PreviewCutscene(stage.OutroStory);
        }

        private void OnDisable()
        {
            HideLoadingOverlay();
        }

        // Hard-exit ② suspend triggers: mobile backgrounding is reliably observed via OnApplicationPause,
        // desktop/editor shutdown via OnApplicationQuit. SaveCombatSuspend already guards terminal combats
        // and missing stage/state, so both are safe to fire unconditionally (a redundant call is a no-op).
        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                SaveCombatSuspend();
            }
        }

        private void OnApplicationQuit()
        {
            SaveCombatSuspend();
        }

        private void OnDestroy()
        {
            UnhookRunAutoSave();
            DestroyLoadingOverlay();
        }

        private void HookRunAutoSave()
        {
            if (combatController == null || combatController.State == null ||
                CurrentStage == null || string.IsNullOrWhiteSpace(CurrentStage.StageId))
            {
                return;
            }

            UnhookRunAutoSave();
            runSaveStore ??= new PlayerRunSaveStore();
            saveSubscribedState = combatController.State;
            saveSubscribedState.OverallTurnEnded += HandleOverallTurnEndedForSave;
            saveSubscribedState.PhaseChanged += HandlePhaseChangedForSave;
        }

        private void UnhookRunAutoSave()
        {
            if (saveSubscribedState == null)
            {
                return;
            }

            saveSubscribedState.OverallTurnEnded -= HandleOverallTurnEndedForSave;
            saveSubscribedState.PhaseChanged -= HandlePhaseChangedForSave;
            saveSubscribedState = null;
        }

        // Auto-save checkpoint (G5 decision): OverallTurnEnded fires once monster resolution has
        // settled the player's turn, so the persisted loadout is never mid-action.
        private void HandleOverallTurnEndedForSave(int overallTurn)
        {
            var state = saveSubscribedState;
            if (state == null || state.Phase == CombatPhase.Victory || state.Phase == CombatPhase.Defeat)
            {
                return;
            }

            var placementSeed = 0;
            var hasPlacementSeed = combatController != null && combatController.TryGetRunSeed(out placementSeed);
            var envelope = PlayerRunSaveEnvelope.Create(
                CurrentStage.StageId, overallTurn, PlayerRunSaveData.FromCombatState(state),
                hasPlacementSeed, placementSeed);
            if (!runSaveStore.TrySave(envelope, out var reason))
            {
                Debug.LogWarning($"Run auto-save failed: {reason}", this);
            }
        }

        // A defeated run is over — the single continue slot must not offer it again.
        private void HandlePhaseChangedForSave(CombatPhase previous, CombatPhase current)
        {
            // RS-13: a terminal combat (win or lose) invalidates any suspend slot for this stage.
            if (current == CombatPhase.Victory || current == CombatPhase.Defeat)
            {
                combatSuspendStore ??= new CombatSuspendStore();
                combatSuspendStore.Delete();
            }

            if (current != CombatPhase.Defeat)
            {
                return;
            }

            runSaveStore?.Delete();
            GameSession.GetOrCreate().ClearPendingRunContinue();
        }

        private void ShowLoadingOverlay(string message)
        {
            if (loadingOverlay == null)
                BuildLoadingOverlay();

            if (loadingLabel != null)
                loadingLabel.text = message;

            loadingOverlay.alpha = 1f;
            loadingOverlay.blocksRaycasts = true;
            loadingOverlay.interactable = true;
            loadingOverlay.gameObject.SetActive(true);
        }

        private void HideLoadingOverlay()
        {
            if (loadingOverlay == null)
                return;

            loadingOverlay.alpha = 0f;
            loadingOverlay.blocksRaycasts = false;
            loadingOverlay.interactable = false;
            loadingOverlay.gameObject.SetActive(false);
        }

        private void DestroyLoadingOverlay()
        {
            if (loadingOverlay == null)
                return;

            var overlayObject = loadingOverlay.gameObject;
            loadingOverlay = null;
            loadingLabel = null;

            if (Application.isPlaying)
                Destroy(overlayObject);
            else
                DestroyImmediate(overlayObject);
        }

        private static void DestroyStaleLoadingOverlays()
        {
            var overlays = GameObject.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < overlays.Length; i++)
            {
                var overlay = overlays[i];
                if (overlay == null ||
                    (overlay.name != "GameplayLoadingCanvas" && overlay.name != "LobbyLoadingOverlay"))
                {
                    continue;
                }

                if (Application.isPlaying)
                    Destroy(overlay.gameObject);
                else
                    DestroyImmediate(overlay.gameObject);
            }
        }

        private void BuildLoadingOverlay()
        {
            var canvasObject = new GameObject("GameplayLoadingCanvas");
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            // Match the gameplay HUD reference resolution (GameplaySceneContract 2200x1238) so this
            // canvas does not drift in scale from the shipping HUD.
            scaler.referenceResolution = new Vector2(2200f, 1238f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            loadingOverlay = canvasObject.AddComponent<CanvasGroup>();

            var backgroundObject = new GameObject("LoadingBackground", typeof(RectTransform));
            backgroundObject.transform.SetParent(canvasObject.transform, false);
            var backgroundRect = backgroundObject.GetComponent<RectTransform>();
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            var background = backgroundObject.AddComponent<Image>();
            background.color = new Color(0.01f, 0.012f, 0.018f, 0.995f);

            var textObject = new GameObject("LoadingText", typeof(RectTransform));
            textObject.transform.SetParent(canvasObject.transform, false);
            var textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.5f, 0.5f);
            textRect.anchorMax = new Vector2(0.5f, 0.5f);
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.anchoredPosition = Vector2.zero;
            textRect.sizeDelta = new Vector2(700f, 100f);
            loadingLabel = textObject.AddComponent<TextMeshProUGUI>();
            loadingLabel.alignment = TextAlignmentOptions.Center;
            loadingLabel.fontSize = 44f;
            loadingLabel.fontStyle = FontStyles.Bold;
            loadingLabel.color = Color.white;
            loadingLabel.raycastTarget = false;
        }
    }
}

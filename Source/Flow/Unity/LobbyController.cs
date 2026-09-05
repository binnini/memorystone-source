using System.Collections;
using System.Collections.Generic;
using SeoulPlayup.Codex;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Unity;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace SeoulPlayup.Flow.Unity
{
    public sealed class LobbyController : MonoBehaviour
    {
        [SerializeField] private StageCatalog stageCatalog;
        [SerializeField] private StageDefinition defaultStage;
        [SerializeField] private StageDefinition tutorialStage;
        [SerializeField] private StageDefinition stageOneStage;
        [SerializeField] private Button startButton;
        [SerializeField] private Button continueButton;
        [SerializeField] private Button settingButton;
        [SerializeField] private Button codexButton;
        [SerializeField] private Button introButton;
        [SerializeField] private Button exitButton;
        [Tooltip("「게임 소개」 패널이 읽는 저작 에셋(문안·이미지). 🔴반드시 여기에 직렬화로 물려야 한다 — " +
            "에셋이 Resources 밖에 있어 되찾을 폴백이 없다. 비면 소개 버튼을 감춘다(경고 1회).")]
        [SerializeField] private Intro.LobbyIntroPageAsset introPages;
        [Tooltip("도감이 읽는 저작 카탈로그 묶음(status_effects.csv 등). 전투가 쓰는 것과 같은 에셋이라 " +
            "도감 전용 저작이 없다. 비워 두면 도감 버튼이 조용히 꺼진다 — 계획 정본 docs/codex-plan.md §4.")]
        [SerializeField] private CombatCatalogTextAssetSource codexCatalogSource;
        [Tooltip("상태이상 아이콘. 전투 HUD가 쓰는 카탈로그를 그대로 받는다. 비어 있는 kind는 " +
            "도감이 이름 전체 폴백으로 그린다(계획 §4.3).")]
        [SerializeField] private StatusEffectIconCatalog codexStatusEffectIcons;
        [Tooltip("도감 카드 도메인이 읽는 베이크된 카드 카탈로그(cards.csv 산출물).")]
        [SerializeField] private CardCatalogAsset codexCardCatalog;
        [Tooltip("도감이 세우는 카드 프리팹. 🔴반드시 여기에 직렬화로 물려야 한다 — 코드가 " +
            "AssetDatabase로 찾는 경로는 에디터에서만 살아 있어 빌드에서 카드가 빈다(계획 §2.2).")]
        [SerializeField] private GameObject codexMoveCardFrontPrefab;
        [SerializeField] private GameObject codexActionCardFrontPrefab;
        [Tooltip("저주(상태) 카드 프레임. 역시 직렬화 참조여야 한다 — 비면 저주가 행동 카드 얼굴로 뜬다.")]
        [SerializeField] private Sprite codexStatusCardFrame;
        [Tooltip("도감 함정 도메인이 읽는 프리셋 카탈로그. 🔴반드시 직렬화 참조 — TrapPresetCatalog." +
            "LoadDefault()는 Resources 밖 에셋이라 빌드에서 언제나 null이고 에디터에서만 멀쩡해 보인다.")]
        [SerializeField] private TrapPresetCatalog codexTrapPresets;
        [Tooltip("도감 오브젝트 도메인이 읽는 맵 오브젝트 카탈로그 묶음(P6). 🔴함정 카탈로그와 같은 이유로 " +
            "반드시 직렬화 참조 — LoadDefault()의 Resources 폴백은 빌드에서 null이다. 비면 이름·설명은 " +
            "CSV로 뜨지만 길 막음·차지하는 칸 같은 수치 줄이 빠진다.")]
        [SerializeField] private MapObjectCatalogSet codexMapObjects;
        [Tooltip("P5에서 구운 도감 전용 썸네일 카탈로그(codex_thumb_{id}). 비워 두면 Resources에서 " +
            "찾아보고, 그것도 없으면 이름 전체 폴백으로 뜬다 — 화면이 비지는 않는다.")]
        [SerializeField] private CodexThumbnailCatalog codexThumbnails;
        [Tooltip("잠긴 칸 실루엣 재질(SeoulPlayup/UI/Silhouette). 🔴직렬화 참조여야 한다 — 셰이더를 " +
            "Shader.Find로 찾으면 빌드에서 스트리핑되어 실루엣이 흰 판이 된다. 비우면 예전 곱셈 " +
            "틴트로 떨어져 어두운 몬스터가 판에 묻힌다(계획 §13.8 Q39).")]
        [SerializeField] private Material codexSilhouetteMaterial;
        [Tooltip("Sound catalog the lobby resolves its music.lobby / ui.button.click clips from, so lobby " +
            "audio is authored in the same asset as combat audio. The explicit clip fields below stay as " +
            "per-scene overrides and win when assigned.")]
        [SerializeField] private SoundCatalog soundCatalog;
        [SerializeField] private AudioClip lobbyBgm;
        [SerializeField] private AudioClip buttonClickSfx;
        [SerializeField] private bool buildFallbackUi = true;

        private GameObject stageSelectPanel;
        private GameObject settingsPanel;
        private Button tutorialStageButton;
        private Button stageOneStageButton;
        private Button closeStageSelectButton;
        private SoundSettingsPanelView settingsPanelView;
        private GameObject soundSettingsContent;
        private GameObject displaySettingsContent;
        private Button soundTabButton;
        private Button displayTabButton;
        private Button fullscreenToggleButton;
        private Button displayCloseButton;
        private TMP_Text fullscreenToggleLabel;
        private CanvasGroup loadingOverlay;
        private GameObject continueConfirmOverlay;
        private TMP_Text loadingLabel;
        private AudioSource bgmSource;
        private AudioSource uiSfxSource;
        private float bgmBaseVolume = 1f;
        private PlayerRunSaveStore runSaveStore;
        private CombatSuspendStore combatSuspendStore;
        private CodexOverlayView codexOverlay;
        private Intro.LobbyIntroOverlayView introOverlay;

        private void Awake()
        {
            DestroyStaleLoadingOverlays();
            EnsureEventSystem();

            if (buildFallbackUi && startButton == null)
                BuildFallbackUi();

            AutoBindPrototypeLobbyButtons();
            AutoBindAuthoredPanels();
            WireButtons();
            PlayLobbyBgm();
        }

        private void OnEnable()
        {
            SoundSettingsService.EnsureLoaded();
            SoundSettingsService.SettingsChanged += ApplySoundSettings;
            ApplySoundSettings();
        }

        private void OnDisable()
        {
            SoundSettingsService.SettingsChanged -= ApplySoundSettings;
        }

        private void OnDestroy()
        {
            DestroyLoadingOverlay();
        }

        public void ShowStageSelection()
        {
            EnsureStageSelectPanel();
            if (stageSelectPanel != null)
                stageSelectPanel.SetActive(true);
        }

        public void StartTutorialStage()
        {
            StartStage(tutorialStage != null ? tutorialStage : defaultStage);
        }

        public void StartStageOne()
        {
            StartStage(stageOneStage != null ? stageOneStage : defaultStage);
        }

        public void StartDefaultStage()
        {
            StartStage(defaultStage != null ? defaultStage : stageCatalog != null ? stageCatalog.DefaultStage : null);
        }

        public void QuitGame()
        {
#if UNITY_EDITOR
            Debug.Log("Quit requested from LobbyController. Editor play mode is not stopped by this MVP flow.", this);
#else
            Application.Quit();
#endif
        }

        public void ShowSettingsPlaceholder()
        {
            ShowSettingsPanel();
        }

        /// <summary>
        /// Resumes the saved run: loads the single-slot save, resolves its stage from the catalog,
        /// and enters gameplay with the persisted loadout (intro cutscene is skipped — the run
        /// already saw it when the stage was first entered).
        /// </summary>
        public void ContinueRun()
        {
            var store = ResolveRunSaveStore();
            if (!store.HasSave)
            {
                Debug.LogWarning("이어하기를 시작할 수 없습니다: 저장된 기록이 없습니다.", this);
                RefreshContinueButton();
                return;
            }

            // 2026-09-05 실플레이 #10: 이어하기는 중단 저장 슬롯을 소비한다(복원 직후 삭제 — MainGameplayController
            // RS-12). 다음 턴 종료 전에 나가면 그 진행은 사라지므로, 누르자마자 들어가지 않고 한 번 묻는다.
            ShowContinueConfirm();
        }

        private void ConfirmContinueRun()
        {
            HideContinueConfirm();
            StartContinueRun();
        }

        private void StartContinueRun()
        {
            var store = ResolveRunSaveStore();
            if (!store.TryLoad(out var envelope, out var reason))
            {
                Debug.LogWarning($"이어하기를 시작할 수 없습니다: {reason}", this);
                RefreshContinueButton();
                return;
            }

            var session = GameSession.GetOrCreate();
            if (stageCatalog != null)
                session.SetStageCatalog(stageCatalog);

            var catalog = stageCatalog != null ? stageCatalog : session.StageCatalog;
            var stage = catalog != null ? catalog.FindById(envelope.StageId) : null;
            if (stage == null)
            {
                Debug.LogWarning($"이어하기를 시작할 수 없습니다: 저장된 스테이지 '{envelope.StageId}'가 카탈로그에 없습니다.", this);
                return;
            }

            session.SetPendingRunContinue(envelope);
            session.SetCurrentStage(stage);
            StartCoroutine(StartStageWithLoading(stage));
        }

        private PlayerRunSaveStore ResolveRunSaveStore()
        {
            return runSaveStore ??= new PlayerRunSaveStore();
        }

        private CombatSuspendStore ResolveCombatSuspendStore()
        {
            return combatSuspendStore ??= new CombatSuspendStore();
        }

        private void RefreshContinueButton()
        {
            if (continueButton != null)
                continueButton.interactable = ResolveRunSaveStore().HasSave;
        }

        private void StartStage(StageDefinition stage)
        {
            var session = GameSession.GetOrCreate();
            // Every stage-select entry is a NEW game: wipe both save layers so nothing resumes.
            // ① the stage-boundary run save (PlayerRunSaveStore) and ② the mid-combat full-snapshot
            // suspend slot (CombatSuspendStore) — the latter is loaded unconditionally by
            // MainGameplayController, so leaving it behind would silently resume a prior run.
            // Resuming always goes through ContinueRun instead.
            session.ClearPendingRunContinue();
            ResolveRunSaveStore().Delete();
            ResolveCombatSuspendStore().Delete();
            RefreshContinueButton();
            if (stageCatalog != null)
                session.SetStageCatalog(stageCatalog);

            stage ??= stageCatalog != null ? stageCatalog.DefaultStage : session.CurrentStage;
            if (stage == null)
            {
                Debug.LogError("LobbyController cannot start gameplay because no stage is assigned.", this);
                return;
            }

            // Stages with an intro story cutscene route through the cutscene scene first; the
            // cutscene then loads MainGameplay. Stages without an intro keep the async loading flow.
            if (stage.IntroStory != null)
            {
                SceneFlowController.GetOrCreate().StartStageWithIntro(stage);
                return;
            }

            StartCoroutine(StartStageWithLoading(stage));
        }

        private IEnumerator StartStageWithLoading(StageDefinition stage)
        {
            ShowLoadingOverlay("스테이지 로딩 중... 0%");
            Canvas.ForceUpdateCanvases();
            yield return null;

            var flow = SceneFlowController.GetOrCreate();
            yield return flow.StartStageAsync(stage, progress =>
            {
                if (loadingLabel != null)
                    loadingLabel.text = $"스테이지 로딩 중... {Mathf.RoundToInt(progress * 100f)}%";
            }, minimumLoadingSeconds: 0.6f);
        }

        private void WireButtons()
        {
            if (startButton != null)
            {
                startButton.onClick.RemoveListener(StartDefaultStage);
                startButton.onClick.RemoveListener(ShowStageSelection);
                WireButtonClickSfx(startButton);
                startButton.onClick.AddListener(ShowStageSelection);
            }

            if (continueButton != null)
            {
                continueButton.onClick.RemoveListener(ContinueRun);
                WireButtonClickSfx(continueButton);
                continueButton.onClick.AddListener(ContinueRun);
            }

            RefreshContinueButton();

            if (exitButton != null)
            {
                exitButton.onClick.RemoveListener(QuitGame);
                WireButtonClickSfx(exitButton);
                exitButton.onClick.AddListener(QuitGame);
            }

            if (settingButton != null)
            {
                settingButton.onClick.RemoveListener(ShowSettingsPlaceholder);
                WireButtonClickSfx(settingButton);
                settingButton.onClick.AddListener(ShowSettingsPlaceholder);
            }

            WireCodexButton();
            WireIntroButton();
            WireSettingsTabs();
            EnsureStageSelectPanel();
        }

        private void WireSettingsTabs()
        {
            if (soundTabButton != null)
            {
                soundTabButton.onClick.RemoveListener(ShowSoundSettingsTab);
                WireButtonClickSfx(soundTabButton);
                soundTabButton.onClick.AddListener(ShowSoundSettingsTab);
            }

            if (displayTabButton != null)
            {
                displayTabButton.onClick.RemoveListener(ShowDisplaySettingsTab);
                WireButtonClickSfx(displayTabButton);
                displayTabButton.onClick.AddListener(ShowDisplaySettingsTab);
            }

            if (fullscreenToggleButton != null)
            {
                fullscreenToggleButton.onClick.RemoveListener(ToggleFullscreen);
                WireButtonClickSfx(fullscreenToggleButton);
                fullscreenToggleButton.onClick.AddListener(ToggleFullscreen);
            }

            if (displayCloseButton != null)
            {
                displayCloseButton.onClick.RemoveListener(HideSettingsPanel);
                WireButtonClickSfx(displayCloseButton);
                displayCloseButton.onClick.AddListener(HideSettingsPanel);
            }
        }

        private void ShowSoundSettingsTab()
        {
            if (soundSettingsContent != null)
                soundSettingsContent.SetActive(true);
            if (displaySettingsContent != null)
                displaySettingsContent.SetActive(false);
        }

        private void ShowDisplaySettingsTab()
        {
            if (soundSettingsContent != null)
                soundSettingsContent.SetActive(false);
            if (displaySettingsContent != null)
                displaySettingsContent.SetActive(true);
            UpdateFullscreenToggleLabel();
        }

        private void ToggleFullscreen()
        {
            DisplaySettingsService.SetFullscreen(!DisplaySettingsService.Fullscreen);
            UpdateFullscreenToggleLabel();
        }

        private void UpdateFullscreenToggleLabel()
        {
            if (fullscreenToggleLabel != null)
                fullscreenToggleLabel.text = DisplaySettingsService.ModeLabel;
        }

        private void HideSettingsPanel()
        {
            if (settingsPanel != null)
                settingsPanel.SetActive(false);
        }

        private void AutoBindPrototypeLobbyButtons()
        {
            startButton ??= FindButtonByName("StartButton");
            continueButton ??= FindButtonByName("ContinueButton");
            exitButton ??= FindButtonByName("ExitButton");
            settingButton ??= FindButtonByName("SettingButton");
            codexButton ??= FindButtonByName("CodexButton");
            introButton ??= FindButtonByName("IntroButton");
        }

        // ── 도감 (docs/codex-plan.md P0) ────────────────────────────────────

        private void WireCodexButton()
        {
            if (codexButton == null)
            {
                return;
            }

            // 읽을 카탈로그가 없으면 버튼을 아예 감춘다. 눌러도 빈 화면이 뜨는 것보다 없는 편이 낫다.
            if (codexCatalogSource == null || !codexCatalogSource.HasStatusEffectCatalog)
            {
                Debug.LogWarning(
                    "[Lobby] 도감 카탈로그(codexCatalogSource)가 배선되지 않아 도감 버튼을 숨긴다. " +
                    "CombatCatalogTextAssetSource를 LobbyController에 할당할 것.",
                    this);
                codexButton.gameObject.SetActive(false);
                return;
            }

            codexButton.onClick.RemoveListener(ShowCodex);
            WireButtonClickSfx(codexButton);
            codexButton.onClick.AddListener(ShowCodex);
        }

        private void ShowCodex()
        {
            if (codexOverlay == null)
            {
                codexOverlay = gameObject.AddComponent<CodexOverlayView>();
                codexOverlay.ConfigureCardVisuals(
                    codexMoveCardFrontPrefab, codexActionCardFrontPrefab, codexStatusCardFrame,
                    codexSilhouetteMaterial);
                // 🔴진행도는 로비가 만드는 것이 아니라 <b>이미 있는 것</b>을 받는다 — 전투가 같은
                // 집합에 신호를 넣고 있고, 그 사이에 놓인 씬 전환이 참조를 들고 다닐 방법을 주지 않는다.
                codexOverlay.Configure(BuildCodexDomains(), CodexProgressStore.Shared.Progress);
            }

            // 로비로 돌아오는 길에 못 썼더라도 도감을 여는 이 순간에 한 번 더 챙긴다 —
            // 도감을 보는 사람은 방금 연 것이 남기를 기대한다.
            CodexProgressStore.Shared.SaveIfDirty();
            codexOverlay.Open(ResolveLobbySceneCanvas().transform);
        }

        // ── 게임 소개 (2026-09-01 확정) ─────────────────────────────────────

        /// <summary>
        /// 「게임 소개」 버튼. 도감과 같은 계약이다 — 읽을 저작이 없으면 <b>버튼을 감춘다.</b>
        /// 눌렀는데 빈 판이 뜨는 것보다 버튼이 없는 편이 낫다.
        /// </summary>
        private void WireIntroButton()
        {
            if (introButton == null)
            {
                return;
            }

            // 2026-09-05 사용자 확정: 소개 버튼을 로비에서 뺀다. 저작 없음과 같은 「버튼 없음」 갈래로
            // 떨어뜨리되, 이쪽은 의도된 것이라 경고를 남기지 않는다(스위치 정본: LobbyIntroFeature).
            if (!Intro.LobbyIntroFeature.Enabled)
            {
                introButton.gameObject.SetActive(false);
                return;
            }

            // 🔴폴백이 없다. 저작 에셋은 Resources 밖(Assets/Data/Intro/)에 있으므로
            // Resources.Load로 되찾을 길이 없고, AssetDatabase는 에디터에서만 살아 있어
            // 「에디터에선 멀쩡하고 빌드에서 빈다」를 만든다. 직렬화 참조가 유일한 정본이다.
            if (introPages == null || introPages.Pages.Length == 0)
            {
                Debug.LogWarning(
                    "[Lobby] 소개 페이지 저작(introPages)이 배선되지 않아 「게임 소개」 버튼을 숨긴다. " +
                    "LobbyIntroPageAsset을 LobbyController에 할당할 것.",
                    this);
                introButton.gameObject.SetActive(false);
                return;
            }

            introButton.onClick.RemoveListener(ShowIntro);
            WireButtonClickSfx(introButton);
            introButton.onClick.AddListener(ShowIntro);
        }

        /// <summary>
        /// 소개 패널을 연다. 도감과 같은 문법(<see cref="ShowCodex"/>)이라 손이 하나다.
        /// <para>
        /// 🔑 <b>진행도를 저장하지 않는다.</b> 첫 방문 자동 표시를 하지 않기로 확정돼(2026-09-01)
        /// 「봤음」을 기억할 이유가 없다 — 언제나 1페이지 첫 비트에서 시작한다.
        /// </para>
        /// </summary>
        private void ShowIntro()
        {
            if (introOverlay == null)
            {
                introOverlay = gameObject.AddComponent<Intro.LobbyIntroOverlayView>();
                introOverlay.Configure(introPages);
            }

            introOverlay.Open(ResolveLobbySceneCanvas().transform);
        }

        /// <summary>
        /// P0.5까지 붙는 도메인은 둘(카드·상태이상). 나머지 5도메인은 P2에서 이 목록에 더한다 —
        /// 셸은 <see cref="ICodexDomain"/>만 알므로 여기 한 줄씩 추가하는 것이 전부다.
        /// </summary>
        /// <summary>
        /// 썸네일 카탈로그는 <b>직렬화 참조가 정본</b>이고 <c>Resources</c>는 보조다 —
        /// 씬 배선을 잊었을 때 조용히 폴백으로 뜨는 대신 한 번 경고를 남기고 되찾는다.
        /// (카탈로그 에셋 자체는 <c>Resources/</c> 안에 있지만 스프라이트는 밖에 있다 — 다이어트 규약.)
        /// </summary>
        private CodexThumbnailCatalog ResolveCodexThumbnails()
        {
            if (codexThumbnails != null)
            {
                return codexThumbnails;
            }

            codexThumbnails = CodexThumbnailCatalog.LoadDefault();
            if (codexThumbnails == null)
            {
                Debug.LogWarning(
                    "[Lobby] 도감 썸네일 카탈로그를 찾지 못했다 — 몬스터가 이름 전체 폴백으로 뜬다. " +
                    "Tools/Codex/Bake Monster Thumbnails로 구운 뒤 LobbyController에 배선할 것.",
                    this);
            }

            return codexThumbnails;
        }

        /// <summary>
        /// 유물·소모품 아이콘 카탈로그. 도감·사이드바 칩·잡화점 타일이 <b>같은 에셋</b>을 본다 —
        /// 여기서 따로 로드하지 않고 그 공용 통로(<c>LoadDefault</c>)를 그대로 부른다.
        /// 없으면 <see langword="null"/>이고 도감은 이름 전체 폴백으로 뜬다.
        /// </summary>
        private static SeoulPlayup.Cards.Unity.RuntimeUiAssetCatalog ResolveItemIconCatalog()
        {
            return SeoulPlayup.Cards.Unity.RuntimeUiAssetCatalog.LoadDefault();
        }

        private IReadOnlyList<ICodexDomain> BuildCodexDomains()
        {
            var domains = new List<ICodexDomain>(2);

            if (codexCardCatalog != null)
            {
                domains.Add(new CodexCardDomain(
                    // CombatConfig.Default로 충분하다 — 도감은 전투 상태가 없는 로비에서 열리고,
                    // 카드 얼굴에 뜨는 값은 저작 그대로다(전투 중 보정은 손패가 하는 일이다).
                    codexCardCatalog.ToCardCatalogDefinition(CombatConfig.Default),
                    // 행동 카드 계열의 호박색 — 카드 프레임 인코딩(행동 47°)과 같은 자리를 가리킨다.
                    new Color(0.941f, 0.725f, 0.231f, 1f)));
            }

            domains.Add(new CodexStatusEffectDomain(
                codexCatalogSource.CreateStatusEffectCatalog(),
                codexStatusEffectIcons,
                // 상태이상 계열의 보라 — 카드 프레임 인코딩(상태 287°)과 같은 자리를 가리킨다.
                new Color(0.725f, 0.545f, 0.961f, 1f)));

            // ── P2 도메인들 ────────────────────────────────────────────────
            // 저작 소스가 빠진 도메인은 조용히 빈 목록으로 뜨는 대신 아예 레일에 오르지 않는다 —
            // 빈 격자는 "아직 안 만든 것"과 "배선이 빠진 것"을 구분해 주지 못한다.
            if (codexCatalogSource.HasRelicCatalog)
            {
                domains.Add(new CodexRelicDomain(
                    codexCatalogSource.CreateRelicCatalog(),
                    new Color(0.898f, 0.769f, 0.404f, 1f),
                    ResolveItemIconCatalog()));
            }

            if (codexTrapPresets != null)
            {
                domains.Add(new CodexTrapDomain(
                    codexTrapPresets,
                    codexStatusEffectIcons,
                    new Color(0.902f, 0.451f, 0.396f, 1f)));
            }

            if (codexCatalogSource.HasConsumableItemCatalog)
            {
                domains.Add(new CodexConsumableItemDomain(
                    codexCatalogSource.CreateConsumableItemCatalog(),
                    new Color(0.451f, 0.812f, 0.686f, 1f),
                    ResolveItemIconCatalog()));
            }

            // ── P6 오브젝트 도메인 ────────────────────────────────────────
            // 🔴 저작(map_objects.csv)이 없으면 레일에 올리지 않는다 — 이름을 지어내지 않는다는 것이
            //    이 도메인을 P6까지 미룬 이유 자체다(계획 §10.4).
            if (codexCatalogSource.HasMapObjectCodexCatalog)
            {
                var objectCatalog = codexCatalogSource.CreateMapObjectCodexCatalog();

                // 전투의 해금 훑기도 같은 표를 봐야 한다 — 빌드에는 Assets/ 경로가 없어서
                // CSV 직독 폴백이 죽는다(계획 §13.1 Resources 다이어트 규약과 같은 결).
                CodexObjectCatalogSource.Register(objectCatalog);

                domains.Add(new CodexObjectDomain(
                    objectCatalog,
                    // 서울 야경의 청록 — 상호작용 기물의 표식 색과 같은 자리를 가리킨다.
                    new Color(0.400f, 0.729f, 0.906f, 1f),
                    codexMapObjects,
                    ResolveCodexThumbnails()));
            }

            if (codexCatalogSource.HasMonsterCatalog)
            {
                // CreateMonsterCatalog가 AttackShapeLibrary.Initialize를 먼저 부르는 것이 계약이라,
                // 이 도메인이 서면 몬스터 패턴 도해도 형상 데이터를 갖는다.
                domains.Add(new CodexMonsterDomain(
                    codexCatalogSource.CreateMonsterCatalog(),
                    new Color(0.847f, 0.494f, 0.796f, 1f),
                    ResolveCodexThumbnails()));
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // 공격 형상 19종(P1 범위 뷰어 검증용) — 저작 참조표라 출하 빌드에는 코드째 들어가지 않는다
            // (docs/codex-plan.md §3-3: 개발 전용 면은 런타임 플래그가 아니라 컴파일 게이트 안에 둔다).
            //
            // 🔴 Initialize를 먼저 부르지 않으면 GetAffectedCells가 예외가 아니라 빈 집합을 돌려주고,
            //    도메인이 항목 0개로 조용히 뜬다. 전투는 CreateMonsterCatalog가 이걸 대신 불러 준다.
            if (codexCatalogSource != null)
            {
                codexCatalogSource.InitializeAttackShapeLibrary();
                domains.Add(new CodexAttackShapeDomain(new Color(0.435f, 0.780f, 0.643f, 1f)));
            }
#endif

            return domains;
        }

        private void AutoBindAuthoredPanels()
        {
            stageSelectPanel ??= FindChildByName("StageDock")?.gameObject;
            settingsPanel ??= FindChildByName("SettingDock")?.gameObject;

            tutorialStageButton ??= FindButtonByName("TutorialStageButton");
            stageOneStageButton ??= FindButtonByName("StageOneButton");
            closeStageSelectButton ??= FindButtonByName("CloseStageSelectButton");
            settingsPanelView ??= settingsPanel != null ? settingsPanel.GetComponentInChildren<SoundSettingsPanelView>(true) : null;

            settingsPanelView?.AutoBindFromHierarchy();

            // Settings dock is a two-tab panel: sound sliders and a display (fullscreen/windowed) tab.
            soundSettingsContent ??= FindChildByName("Sound Settings Content")?.gameObject;
            displaySettingsContent ??= FindChildByName("Display Settings Content")?.gameObject;
            soundTabButton ??= FindButtonByName("SoundTabButton");
            displayTabButton ??= FindButtonByName("DisplayTabButton");
            fullscreenToggleButton ??= FindButtonByName("FullscreenToggleButton");
            displayCloseButton ??= FindButtonByName("CloseDisplaySettingsButton");
            if (fullscreenToggleButton != null)
                fullscreenToggleLabel = fullscreenToggleButton.GetComponentInChildren<TMP_Text>(true);

            if (stageSelectPanel != null)
                stageSelectPanel.SetActive(false);
            if (settingsPanel != null)
                settingsPanel.SetActive(false);
        }

        private Transform FindChildByName(string objectName)
        {
            var transforms = GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                if (transforms[i].name == objectName)
                    return transforms[i];
            }

            return null;
        }

        private Button FindButtonByName(string objectName)
        {
            var buttons = GetComponentsInChildren<Button>(true);
            for (var i = 0; i < buttons.Length; i++)
            {
                if (buttons[i].name == objectName)
                    return buttons[i];
            }

            return null;
        }

        private void WireButtonClickSfx(Button button)
        {
            if (button == null)
                return;

            button.onClick.RemoveListener(PlayButtonClickSfx);
            button.onClick.AddListener(PlayButtonClickSfx);
        }

        // Resolves a lobby sound from the shared catalog, so lobby audio is authored where combat audio is
        // instead of only through this component's serialized clip fields. Returns the catalog's authored
        // volume alongside the clip; an explicit clip override plays at unit volume as it always did.
        private bool TryResolveLobbySound(AudioClip overrideClip, string cueId, out AudioClip clip, out float volume)
        {
            if (overrideClip != null)
            {
                clip = overrideClip;
                volume = 1f;
                return true;
            }

            if (soundCatalog != null && soundCatalog.TryGetEntry(cueId, out var entry) && entry != null && entry.Clip != null)
            {
                clip = entry.Clip;
                volume = entry.Volume;
                return true;
            }

            clip = null;
            volume = 1f;
            return false;
        }

        private void PlayButtonClickSfx()
        {
            if (!TryResolveLobbySound(buttonClickSfx, AudioCueIds.UiButtonClick, out var clip, out var clipVolume))
                return;

            if (uiSfxSource == null)
            {
                uiSfxSource = gameObject.AddComponent<AudioSource>();
                uiSfxSource.playOnAwake = false;
                uiSfxSource.loop = false;
                uiSfxSource.spatialBlend = 0f;
            }

            uiSfxSource.volume = 1f;
            uiSfxSource.PlayOneShot(clip, clipVolume * SoundSettingsService.SfxVolume);
        }

        private void PlayLobbyBgm()
        {
            if (!TryResolveLobbySound(lobbyBgm, AudioCueIds.MusicLobby, out var clip, out var clipVolume))
                return;

            bgmSource = GetComponent<AudioSource>();
            if (bgmSource == null)
                bgmSource = gameObject.AddComponent<AudioSource>();

            bgmSource.clip = clip;
            bgmSource.loop = true;
            bgmSource.playOnAwake = false;
            bgmSource.spatialBlend = 0f;
            bgmBaseVolume = clipVolume;
            ApplySoundSettings();

            if (!bgmSource.isPlaying)
                bgmSource.Play();
        }

        private void ApplySoundSettings()
        {
            if (bgmSource != null)
                bgmSource.volume = bgmBaseVolume * SoundSettingsService.BgmVolume;

            if (uiSfxSource != null)
                uiSfxSource.volume = 1f;
        }

        private void ShowSettingsPanel()
        {
            EnsureSettingsPanel();
            if (settingsPanel != null)
            {
                settingsPanel.SetActive(true);
                ShowSoundSettingsTab(); // always open on the sound tab
            }
        }

        private void EnsureSettingsPanel()
        {
            AutoBindAuthoredPanels();
            if (settingsPanel == null)
                Debug.LogWarning("Lobby SettingDock was not found. Assign ScreenLobby prefab SettingDock for settings UI.", this);
        }

        private void EnsureStageSelectPanel()
        {
            AutoBindAuthoredPanels();
            if (stageSelectPanel == null)
            {
                Debug.LogWarning("Lobby StageDock was not found. Assign ScreenLobby prefab StageDock for stage selection UI.", this);
                return;
            }

            if (tutorialStageButton != null)
            {
                WireButtonClickSfx(tutorialStageButton);
                tutorialStageButton.onClick.RemoveListener(StartTutorialStage);
                tutorialStageButton.onClick.AddListener(StartTutorialStage);
            }

            if (stageOneStageButton != null)
            {
                WireButtonClickSfx(stageOneStageButton);
                stageOneStageButton.onClick.RemoveListener(StartStageOne);
                stageOneStageButton.onClick.AddListener(StartStageOne);
            }

            if (closeStageSelectButton != null)
            {
                WireButtonClickSfx(closeStageSelectButton);
                closeStageSelectButton.onClick.RemoveListener(HideStageSelection);
                closeStageSelectButton.onClick.AddListener(HideStageSelection);
            }
        }

        private void HideStageSelection()
        {
            if (stageSelectPanel != null)
                stageSelectPanel.SetActive(false);
        }

        private void ShowLoadingOverlay(string message)
        {
            if (loadingOverlay == null)
                BuildLoadingOverlay();

            if (loadingLabel != null)
                loadingLabel.text = message;

            loadingOverlay.alpha = 1f;
            loadingOverlay.interactable = true;
            loadingOverlay.blocksRaycasts = true;
            loadingOverlay.gameObject.SetActive(true);
        }

        private void HideLoadingOverlay()
        {
            if (loadingOverlay == null)
                return;

            loadingOverlay.alpha = 0f;
            loadingOverlay.interactable = false;
            loadingOverlay.blocksRaycasts = false;
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

        private void ShowContinueConfirm()
        {
            if (continueConfirmOverlay == null)
                BuildContinueConfirmOverlay();

            continueConfirmOverlay.transform.SetAsLastSibling();
            continueConfirmOverlay.SetActive(true);
        }

        private void HideContinueConfirm()
        {
            if (continueConfirmOverlay != null)
                continueConfirmOverlay.SetActive(false);
        }

        // Built in code like the loading overlay so the ScreenLobby prefab needs no edits. Full-screen dim
        // swallows clicks behind the dialog; the panel/buttons reuse the P4 procedural skin.
        private void BuildContinueConfirmOverlay()
        {
            var canvas = ResolveLobbySceneCanvas();
            var center = new Vector2(0.5f, 0.5f);

            var overlay = CreateRect("LobbyContinueConfirmOverlay", canvas.transform, center, new Vector2(2200f, 1238f));
            var dim = overlay.gameObject.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.62f);
            dim.raycastTarget = true;

            var panel = CreateRect("Panel", overlay, center, new Vector2(780f, 380f));
            var panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.color = new Color(0.07f, 0.07f, 0.09f, 0.97f);
            panelImage.raycastTarget = true;
            panel.gameObject.AddComponent<UiProceduralPanel>();

            var title = CreateText("Title", panel, "이어하기", 36f, new Vector2(0f, 128f), new Vector2(700f, 56f));
            title.alignment = TextAlignmentOptions.Center;
            title.fontStyle = FontStyles.Bold;
            title.raycastTarget = false;

            var message = CreateText(
                "Message",
                panel,
                "이어하기를 시작하면 저장된 기록이 초기화됩니다.\n다음 턴을 마칠 때 다시 자동 저장됩니다.\n계속하시겠습니까?",
                26f,
                new Vector2(0f, 22f),
                new Vector2(700f, 150f));
            message.alignment = TextAlignmentOptions.Center;
            message.textWrappingMode = TextWrappingModes.Normal;
            message.raycastTarget = false;

            var confirm = CreateButton("ConfirmButton", panel, "이어하기", new Vector2(-130f, -120f));
            ((RectTransform)confirm.transform).sizeDelta = new Vector2(220f, 60f);
            UiButtonSkin.Apply(confirm);
            WireButtonClickSfx(confirm);
            confirm.onClick.AddListener(ConfirmContinueRun);

            var cancel = CreateButton("CancelButton", panel, "취소", new Vector2(130f, -120f));
            ((RectTransform)cancel.transform).sizeDelta = new Vector2(220f, 60f);
            UiButtonSkin.Apply(cancel);
            WireButtonClickSfx(cancel);
            cancel.onClick.AddListener(HideContinueConfirm);

            continueConfirmOverlay = overlay.gameObject;
            continueConfirmOverlay.SetActive(false);
        }

        private void BuildLoadingOverlay()
        {
            var canvas = ResolveLobbySceneCanvas();

            // Sized to the lobby canvas reference resolution (2200x1238) so the loading dim fully covers
            // the screen; this is a fixed-size centered rect, so it must track the reference resolution.
            var overlay = CreateRect("LobbyLoadingOverlay", canvas.transform, new Vector2(0.5f, 0.5f), new Vector2(2200f, 1238f));
            loadingOverlay = overlay.gameObject.AddComponent<CanvasGroup>();
            loadingOverlay.alpha = 0f;
            loadingOverlay.interactable = false;
            loadingOverlay.blocksRaycasts = false;

            var background = overlay.gameObject.AddComponent<Image>();
            background.color = new Color(0.01f, 0.012f, 0.018f, 0.995f);

            loadingLabel = CreateText("LobbyLoadingText", overlay, "스테이지 로딩 중...", 42f, Vector2.zero, new Vector2(720f, 100f));
            loadingLabel.alignment = TextAlignmentOptions.Center;
            loadingLabel.raycastTarget = false;
        }

        private Canvas ResolveLobbySceneCanvas()
        {
            var canvas = GetComponentInChildren<Canvas>(true);
            if (canvas != null)
                return canvas;

            var scene = gameObject.scene;
            var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < canvases.Length; i++)
            {
                canvas = canvases[i];
                if (canvas != null && canvas.gameObject.scene == scene)
                    return canvas;
            }

            var canvasObject = new GameObject("LobbyCanvas");
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.AddComponent<CanvasScaler>();
            canvasObject.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        private static void DestroyStaleLoadingOverlays()
        {
            var transforms = GameObject.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < transforms.Length; i++)
            {
                var target = transforms[i];
                if (target == null ||
                    (target.name != "LobbyLoadingOverlay" && target.name != "GameplayLoadingCanvas"))
                {
                    continue;
                }

                if (Application.isPlaying)
                    Destroy(target.gameObject);
                else
                    DestroyImmediate(target.gameObject);
            }
        }

        private void BuildFallbackUi()
        {
            var canvasObject = new GameObject("LobbyCanvas");
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.AddComponent<CanvasScaler>();
            canvasObject.AddComponent<GraphicRaycaster>();

            var panel = CreateRect("MainMenuPanel", canvasObject.transform, new Vector2(0.5f, 0.5f), new Vector2(720f, 420f));
            var title = CreateText("TitleText", panel, "Seoul Playup", 56f, new Vector2(0f, 110f), new Vector2(620f, 90f));
            title.alignment = TextAlignmentOptions.Center;

            startButton = CreateButton("StartButton", panel, "새 게임", new Vector2(0f, 0f));
            exitButton = CreateButton("ExitButton", panel, "종료", new Vector2(0f, -86f));
        }

        private static void EnsureEventSystem()
        {
            var current = FindFirstObjectByType<EventSystem>();
            var eventSystem = current != null ? current.gameObject : new GameObject("EventSystem");

            if (current == null)
                eventSystem.AddComponent<EventSystem>();

#if ENABLE_INPUT_SYSTEM
            var inputModule = eventSystem.GetComponent<InputSystemUIInputModule>();
            if (inputModule == null)
                inputModule = eventSystem.AddComponent<InputSystemUIInputModule>();

            if (inputModule.actionsAsset == null)
                inputModule.AssignDefaultActions();

            var standaloneModule = eventSystem.GetComponent<StandaloneInputModule>();
            if (standaloneModule != null)
                standaloneModule.enabled = false;
#else
            if (eventSystem.GetComponent<StandaloneInputModule>() == null)
                eventSystem.AddComponent<StandaloneInputModule>();
#endif
        }

        private static RectTransform CreateRect(string name, Transform parent, Vector2 pivot, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = pivot;
            rect.anchorMax = pivot;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        private static TextMeshProUGUI CreateText(string name, Transform parent, string value, float fontSize, Vector2 position, Vector2 size)
        {
            var rect = CreateRect(name, parent, new Vector2(0.5f, 0.5f), size);
            rect.anchoredPosition = position;
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = fontSize;
            text.color = Color.white;
            return text;
        }

        private static Button CreateButton(string name, Transform parent, string label, Vector2 position)
        {
            var rect = CreateRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(360f, 62f));
            rect.anchoredPosition = position;
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.12f, 0.18f, 0.28f, 1f);
            var button = rect.gameObject.AddComponent<Button>();
            var text = CreateText("Label", rect, label, 26f, Vector2.zero, new Vector2(330f, 46f));
            text.alignment = TextAlignmentOptions.Center;
            return button;
        }
    }
}

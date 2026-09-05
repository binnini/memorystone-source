using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Runtime.Timeline;
using SeoulPlayup.Combat.Unity.Tutorial;
using SeoulPlayup.Combat.Unity.Presentation;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using static SeoulPlayup.Combat.Unity.CombatCameraController;
using Cinemachine;
using TMPro;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// 호스트 전용 이음새(2단계 구조 리팩토링 2-A). 여기 있는 것은 전부 <b>이 MonoBehaviour의 직렬화·사설 필드를
    /// 직접 쓰는</b> 테스트 구성·인스펙터 토글(안개 리빌·오버레이 레이어·클릭 이동)이다 — 디버그 조작이 아니라
    /// 호스트 상태 배선이라 <see cref="CombatDebugFacade"/>로 내보내지 않는다(내보내면 세터 20개짜리 인터페이스가 된다).
    /// 옛 DebugTools.cs에서 본문 무변경으로 옮겼다.
    /// </summary>
    public sealed partial class MapCombatController
    {
        public void ConfigureForTests(
            AtlasTilePresentationView atlasView,
            HexMapInputController input,
            Camera camera)
        {
            sparseSource = null;
            atlasTilePresentationView = atlasView;
            EnsureOverlayPresenter();
            inputController = input;
            prototype3DCamera = camera;
            autoCreateDebugControlPanel = false;
            useExplicitTestReferences = true;
        }

        public void ConfigureMapForTests(HexMapData map)
        {
            configuredMap = map;
        }

        public void ConfigurePlayerVisionRangeForTests(int range)
        {
            testPlayerVisionRangeOverride = range < 0 ? 0 : range;
        }

        public void ConfigureSparseSourceForTests(HexSparseMapAuthoringSource source)
        {
            sparseSource = source;
        }

        public void ConfigureExpectedBoardPurposeForTests(HexMapPurpose purpose)
        {
            expectedBoardPurpose = purpose;
        }

        public void ConfigureDebugRevealAllMapCellsForTests(bool enabled)
        {
            SetDebugRevealAllMapCells(enabled, refresh: false);
        }

        public void ToggleFogDebugVisibility()
        {
            SetDebugRevealAllMapCells(!revealAllMapCellsInDebugMode, refresh: true);
        }

        public void SetFogDebugVisible(bool visible)
        {
            SetDebugRevealAllMapCells(!visible, refresh: true);
        }

        private void SetDebugRevealAllMapCells(bool enabled, bool refresh)
        {
            if (revealAllMapCellsInDebugMode == enabled)
            {
                return;
            }

            revealAllMapCellsInDebugMode = enabled;
            if (refresh && State != null)
            {
                RefreshView();
            }
        }

        public void ConfigureOverlayDebugControlsForTests(bool enabled)
        {
            showOverlayDebugControls = enabled;
        }

        public void ConfigureOverlayRendererBackendForTests(CombatOverlayRendererBackend backend)
        {
            overlayRendererBackend = backend;
            RecreateOverlayPresenter();
        }

        public CombatOverlayRendererBackend ActiveOverlayRendererBackendForTests => activeOverlayRendererBackend;

        public int GetCombatOverlayActiveCount(HexOverlayLayer layer)
        {
            return EnsureOverlayPresenter().GetActiveCount(layer);
        }

        public void ConfigureOverlayDebugLayerForTests(CombatOverlayDebugLayer layer, bool visible)
        {
            SetOverlayDebugLayerVisible(layer, visible, refresh: false);
        }

        public bool IsOverlayDebugLayerVisible(CombatOverlayDebugLayer layer)
        {
            switch (layer)
            {
                case CombatOverlayDebugLayer.PlayerMovement:
                    return showPlayerMovementOverlay;
                case CombatOverlayDebugLayer.PlayerAction:
                    return showPlayerActionOverlay;
                case CombatOverlayDebugLayer.MonsterMove:
                    return showMonsterMoveOverlay;
                case CombatOverlayDebugLayer.MonsterAttack:
                    return showMonsterAttackOverlay;
                case CombatOverlayDebugLayer.MonsterChase:
                    return showMonsterChaseOverlay;
                case CombatOverlayDebugLayer.MonsterIntentArrows:
                    return showMonsterIntentArrows;
                default:
                    return true;
            }
        }

        public void ToggleOverlayDebugLayer(CombatOverlayDebugLayer layer)
        {
            SetOverlayDebugLayerVisible(layer, !IsOverlayDebugLayerVisible(layer), refresh: true);
        }

        public void SetOverlayDebugLayerVisible(CombatOverlayDebugLayer layer, bool visible)
        {
            SetOverlayDebugLayerVisible(layer, visible, refresh: true);
        }

        public void ToggleClickMoveDebugMode()
        {
            SetClickMoveDebugMode(!clickMoveDebugModeEnabled);
        }

        public void SetClickMoveDebugMode(bool enabled)
        {
            clickMoveDebugModeEnabled = enabled;
            if (!enabled)
            {
                clickMoveDebugSelectedCoord = null;
                LastInputMessage = "Click move debug OFF. Normal map clicks are restored.";
            }
            else
            {
                LastInputMessage = "Trap debug move ON. Click a map tile to move there without using movement cards.";
            }

            RefreshView();
        }

        public void SelectClickMoveDebugTileForTests(HexCoord coord)
        {
            SelectClickMoveDebugTile(coord);
        }

        public bool ConfirmClickMoveDebugSelection()
        {
            if (!clickMoveDebugModeEnabled)
            {
                LastInputMessage = "Click move debug is OFF.";
                RefreshHudOnly();
                return false;
            }

            if (!clickMoveDebugSelectedCoord.HasValue)
            {
                LastInputMessage = "Click move debug: click a map tile before confirming movement.";
                RefreshHudOnly();
                return false;
            }

            var selected = clickMoveDebugSelectedCoord.Value;
            clickMoveDebugSelectedCoord = null;
            var moved = TryDebugMoveTo(selected);
            if (!moved)
            {
                clickMoveDebugSelectedCoord = selected;
                RefreshView();
            }

            return moved;
        }

        private void SetOverlayDebugLayerVisible(CombatOverlayDebugLayer layer, bool visible, bool refresh)
        {
            switch (layer)
            {
                case CombatOverlayDebugLayer.PlayerMovement:
                    showPlayerMovementOverlay = visible;
                    break;
                case CombatOverlayDebugLayer.PlayerAction:
                    showPlayerActionOverlay = visible;
                    break;
                case CombatOverlayDebugLayer.MonsterMove:
                    showMonsterMoveOverlay = visible;
                    break;
                case CombatOverlayDebugLayer.MonsterAttack:
                    showMonsterAttackOverlay = visible;
                    break;
                case CombatOverlayDebugLayer.MonsterChase:
                    showMonsterChaseOverlay = visible;
                    break;
                case CombatOverlayDebugLayer.MonsterIntentArrows:
                    showMonsterIntentArrows = visible;
                    break;
            }

            if (refresh && State != null)
            {
                RefreshView();
            }
        }

        public void ConfigurePresentationForTests(bool immediateSequences)
        {
            resolvePresentationSequencesImmediately = immediateSequences;
        }

        /// <summary>
        /// 뽑기·카드 보상 추첨의 난수원을 갈아끼운다. 상자가 카드팩/돈/유물 중 무엇을 내놓을지가
        /// 무작위가 되면서, 이걸 고정하지 않으면 상자 관련 테스트가 확률적으로 깨진다.
        /// null을 넘기면 프로덕션 난수로 되돌아간다 — 런 시드가 있으면 시드 구현, 없으면
        /// <c>UnityEngine.Random</c>(<see cref="ApplyRewardRandom"/>).
        /// </summary>
        public void ConfigureRewardRandomForTests(IRewardRandom random)
        {
            rewardRandomForTests = random;
            ApplyRewardRandom();
        }

        // 테스트가 꽂은 난수원. InitializeIntegration보다 먼저 꽂히는 것이 관례라(EventObjectTests)
        // 초기화가 이 값을 덮어쓰지 않도록 따로 들고 있다가 매번 같이 해소한다.
        private IRewardRandom rewardRandomForTests;

        /// <summary>
        /// 지금 보상 흐름이 쓰는 난수원(seed-determinism-handoff P3). 테스트 주입 > 런 시드 구현
        /// (스트림 7) > null(= <c>CombatRewardFlow</c>의 <c>UnityEngine.Random</c> 폴백, 무시드 진입만).
        /// 시드는 <see cref="SetPlacementRandomization"/>로 들어오므로 초기화 때마다 다시 해소한다.
        /// </summary>
        private void ApplyRewardRandom()
        {
            // 재개(②)면 봉투의 보상 커서까지 되감는다(P5). 이 호출은 InitializeIntegration이 복원 페이로드를
            // 소비하기 전에 오므로 여기서 읽을 수 있다. 새 진입·구세이브는 0 = 첫 칸.
            var rewardCursor = combatSuspendRestore?.RewardCursor ?? 0;
            ActiveRewardRandom = rewardRandomForTests
                ?? (hasPlacementSeed ? new SeededRewardRandom(RunSeedStreams.Derive(placementSeed, RunSeedStreams.Rewards), rewardCursor) : null);
            RewardFlow.ConfigureRewardRandom(ActiveRewardRandom);
        }

        /// <summary>보상 흐름에 실제로 꽂힌 난수원(진단·테스트용). null = Unity 전역 난수 폴백.</summary>
        internal IRewardRandom ActiveRewardRandom { get; private set; }

        /// <summary>
        /// 보상 난수(스트림 7)가 지금까지 소비한 칸(P5). 세이브 봉투 작성자(플로우 계층)가 <c>RewardCursor</c>로 싣는다.
        /// 시드 구현이 아니면(테스트 주입·Unity 폴백) 0.
        /// </summary>
        public int RewardRandomCursor => (ActiveRewardRandom as SeededRewardRandom)?.Consumed ?? 0;

        /// <summary>
        /// 뽑기 분포를 갈아 끼운다(테스트·랩 전용). <c>null</c>이면 출하 표로 돌아간다.
        ///
        /// <para>🔑 이 이음새가 필요해진 이유: 2026-09-01 출하 분포가 「부적 하나」(100/0/0/0)로 굳으면서,
        /// <b>돈·유물 즉시 지급 경로</b>를 재던 테스트가 출하 표만으로는 그 경로에 도달할 수 없게 됐다.
        /// 배선(경로가 살아 있는가)과 밸런스(무엇이 얼마나 나오는가)는 다른 질문이고, 배선 테스트가
        /// 밸런스 결정에 매여 사라지면 지급 경로가 조용히 끊겨도 아무도 모른다.</para>
        /// </summary>
        public void ConfigureGachaRewardWeightsForTests(GachaRewardWeights weights)
        {
            RewardFlow.ConfigureGachaRewardWeights(weights);
        }

        public void ConfigurePresentationTimingsForTests(float playerMoveSecondsOverride, float enemyMoveSecondsOverride = -1f, float attackImpactDelayOverride = -1f, float attackWindupDelayOverride = 0f, float deathDelayOverride = 0f)
        {
            playerMoveSeconds = Mathf.Max(0f, playerMoveSecondsOverride);
            enemyMoveSeconds = Mathf.Max(0f, enemyMoveSecondsOverride < 0f ? playerMoveSecondsOverride : enemyMoveSecondsOverride);
            attackImpactDelay = Mathf.Max(0f, attackImpactDelayOverride < 0f ? attackImpactDelay : attackImpactDelayOverride);
            attackWindupDelay = Mathf.Max(0f, attackWindupDelayOverride);
            deathDelay = Mathf.Max(0f, deathDelayOverride);
        }

        public void RefreshForTests()
        {
            RefreshView();
        }

        private void SelectClickMoveDebugTile(HexCoord coord)
        {
            clickMoveDebugSelectedCoord = coord;
            LastTargetInfoText = GetVisibilitySafeTooltipText(coord);
            LastInputMessage = $"Trap debug selected {coord}. Press Move Selected to move there without using movement cards.";
            RefreshView();
        }
    }
}

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
    /// 런타임 인스펙터 동기화(2단계 구조 리팩토링 2-A): 플레이 중 인스펙터에서 바꾼 직렬화 필드를 매 프레임
    /// 스냅샷과 비교해 뷰/재시작에 반영한다. <c>LateUpdate</c>의 핫패스이고 필드 41개+미러 프로퍼티 16개를
    /// 읽으므로 호스트에 남긴다. 옛 DebugTools.cs에서 본문 무변경으로 옮겼다.
    /// </summary>
    public sealed partial class MapCombatController
    {
        private void ApplyRuntimeInspectorSettingsIfChanged()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            var current = CaptureRuntimeInspectorSettings();
            if (!hasAppliedRuntimeInspectorSettings)
            {
                RememberAppliedRuntimeInspectorSettings(current);
                return;
            }

            if (HaveRestartSettingsChanged(current))
            {
                InitializeIntegration();
                return;
            }

            var visualSettingsChanged = false;
            if (HaveEnemyMarkerSettingsChanged(current))
            {
                ApplyEnemyMarkerSettings();
                UpdateEnemyMarker();
                visualSettingsChanged = true;
            }

            if (HaveDebugVisibilitySettingsChanged(current))
            {
                RefreshView();
                visualSettingsChanged = true;
            }

            if (HaveOverlayDebugSettingsChanged(current))
            {
                RefreshView();
                visualSettingsChanged = true;
            }

            if (HaveOverlayRendererSettingsChanged(current))
            {
                RecreateOverlayPresenter();
                InvalidateBatchedOverlayGeometry();
                RefreshView();
                visualSettingsChanged = true;
            }

            if (HaveGameplayCameraSettingsChanged(current))
            {
                visualSettingsChanged = true;
            }

            if (visualSettingsChanged)
            {
                RememberAppliedRuntimeInspectorSettings(current);
            }
        }

        private RuntimeInspectorSettingsSnapshot CaptureRuntimeInspectorSettings()
        {
            return new RuntimeInspectorSettingsSnapshot(
                sparseSource,
                atlasTilePresentationView,
                inputController,
                prototype3DCamera,
                playerStart,
                enemyStart,
                playerCombatProfileId,
                playerHp,
                enemyHp,
                playerMovePoints,
                playerVisionRange,
                playerAttackDamage,
                playerBlock,
                actionHandSize,
                enemyChaseRange,
                enemyAttackDamage,
                enemyMarkerColor,
                enemyMarkerRadius,
                enemyMarkerHeightOffset,
                revealAllMapCellsInDebugMode,
                showOverlayDebugControls,
                showPlayerMovementOverlay,
                showPlayerActionOverlay,
                showMonsterMoveOverlay,
                showMonsterAttackOverlay,
                showMonsterChaseOverlay,
                overlayRendererBackend,
                keepCameraCenteredOnPlayer,
                cameraPlayerOffset,
                autoCalculateCameraPlayerOffset,
                cameraFollowDistance,
                cameraFramingOffset,
                cameraEulerAngles,
                cameraOrthographicSize,
                minCameraOrthographicSize,
                maxCameraOrthographicSize,
                cameraFollowSmoothing);
        }

        private bool HaveRestartSettingsChanged(RuntimeInspectorSettingsSnapshot current)
        {
            return appliedRuntimeInspectorSettings.SparseSource != current.SparseSource
                || appliedRuntimeInspectorSettings.AtlasTilePresentationView != current.AtlasTilePresentationView
                || appliedRuntimeInspectorSettings.InputController != current.InputController
                || appliedRuntimeInspectorSettings.Prototype3DCamera != current.Prototype3DCamera
                || !appliedRuntimeInspectorSettings.PlayerStart.Equals(current.PlayerStart)
                || !appliedRuntimeInspectorSettings.EnemyStart.Equals(current.EnemyStart)
                || appliedRuntimeInspectorSettings.PlayerCombatProfileId != current.PlayerCombatProfileId
                || appliedRuntimeInspectorSettings.PlayerHp != current.PlayerHp
                || appliedRuntimeInspectorSettings.EnemyHp != current.EnemyHp
                || appliedRuntimeInspectorSettings.PlayerMovePoints != current.PlayerMovePoints
                || appliedRuntimeInspectorSettings.PlayerVisionRange != current.PlayerVisionRange
                || appliedRuntimeInspectorSettings.PlayerAttackDamage != current.PlayerAttackDamage
                || appliedRuntimeInspectorSettings.PlayerBlock != current.PlayerBlock
                || appliedRuntimeInspectorSettings.ActionHandSize != current.ActionHandSize
                || appliedRuntimeInspectorSettings.EnemyChaseRange != current.EnemyChaseRange
                || appliedRuntimeInspectorSettings.EnemyAttackDamage != current.EnemyAttackDamage;
        }

        private bool HaveEnemyMarkerSettingsChanged(RuntimeInspectorSettingsSnapshot current)
        {
            return appliedRuntimeInspectorSettings.EnemyMarkerColor != current.EnemyMarkerColor
                || !Mathf.Approximately(appliedRuntimeInspectorSettings.EnemyMarkerRadius, current.EnemyMarkerRadius)
                || !Mathf.Approximately(appliedRuntimeInspectorSettings.EnemyMarkerHeightOffset, current.EnemyMarkerHeightOffset);
        }

        private bool HaveDebugVisibilitySettingsChanged(RuntimeInspectorSettingsSnapshot current)
        {
            return appliedRuntimeInspectorSettings.RevealAllMapCellsInDebugMode != current.RevealAllMapCellsInDebugMode;
        }

        private bool HaveOverlayDebugSettingsChanged(RuntimeInspectorSettingsSnapshot current)
        {
            return appliedRuntimeInspectorSettings.ShowOverlayDebugControls != current.ShowOverlayDebugControls
                || appliedRuntimeInspectorSettings.ShowPlayerMovementOverlay != current.ShowPlayerMovementOverlay
                || appliedRuntimeInspectorSettings.ShowPlayerActionOverlay != current.ShowPlayerActionOverlay
                || appliedRuntimeInspectorSettings.ShowMonsterMoveOverlay != current.ShowMonsterMoveOverlay
                || appliedRuntimeInspectorSettings.ShowMonsterAttackOverlay != current.ShowMonsterAttackOverlay
                || appliedRuntimeInspectorSettings.ShowMonsterChaseOverlay != current.ShowMonsterChaseOverlay;
        }

        private bool HaveOverlayRendererSettingsChanged(RuntimeInspectorSettingsSnapshot current)
        {
            return appliedRuntimeInspectorSettings.OverlayRendererBackend != current.OverlayRendererBackend;
        }

        private bool HaveGameplayCameraSettingsChanged(RuntimeInspectorSettingsSnapshot current)
        {
            return appliedRuntimeInspectorSettings.KeepCameraCenteredOnPlayer != current.KeepCameraCenteredOnPlayer
                || appliedRuntimeInspectorSettings.CameraPlayerOffset != current.CameraPlayerOffset
                || appliedRuntimeInspectorSettings.AutoCalculateCameraPlayerOffset != current.AutoCalculateCameraPlayerOffset
                || !Mathf.Approximately(appliedRuntimeInspectorSettings.CameraFollowDistance, current.CameraFollowDistance)
                || appliedRuntimeInspectorSettings.CameraFramingOffset != current.CameraFramingOffset
                || appliedRuntimeInspectorSettings.CameraEulerAngles != current.CameraEulerAngles
                || !Mathf.Approximately(appliedRuntimeInspectorSettings.CameraOrthographicSize, current.CameraOrthographicSize)
                || !Mathf.Approximately(appliedRuntimeInspectorSettings.MinCameraOrthographicSize, current.MinCameraOrthographicSize)
                || !Mathf.Approximately(appliedRuntimeInspectorSettings.MaxCameraOrthographicSize, current.MaxCameraOrthographicSize)
                || !Mathf.Approximately(appliedRuntimeInspectorSettings.CameraFollowSmoothing, current.CameraFollowSmoothing);
        }

        private void RememberAppliedRuntimeInspectorSettings()
        {
            RememberAppliedRuntimeInspectorSettings(CaptureRuntimeInspectorSettings());
        }

        private void RememberAppliedRuntimeInspectorSettings(RuntimeInspectorSettingsSnapshot snapshot)
        {
            hasAppliedRuntimeInspectorSettings = true;
            appliedRuntimeInspectorSettings = snapshot;
        }
    }
}

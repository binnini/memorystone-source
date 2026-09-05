using System.Collections;
using System.Collections.Generic;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// <see cref="CombatDebugFacade"/>가 호스트(<see cref="MapCombatController"/>)에서 필요로 하는 조각(2단계 구조 리팩토링 2-B).
    /// 일부러 좁게 잡았다 — 파사드는 상태를 읽고, 출하 경로(뷰 갱신·타임라인·보상 팝업·카메라 바인더)를 부르고,
    /// 연출 전용 플래그 몇 개(숨김 몬스터·강제 리빌·주간 룩)를 <b>호스트 메서드로</b> 놓을 수 있을 뿐이다.
    /// 직렬화 필드를 쓰는 테스트 이음새는 여기 없다(<c>MapCombatController.TestSeams.cs</c>가 호스트에서 직접 쓴다).
    ///
    /// <para>선례 = <c>IMapObjectVisualHost</c> 등 뷰 절단 5건: 직렬화 필드는 옮기지 않고 인터페이스 멤버 하나로 노출,
    /// public API는 호스트에 위임 보존, 협력자는 지연 생성.</para>
    /// </summary>
    internal interface ICombatDebugHost
    {
        // ── 상태 ──
        CombatState State { get; }
        HexMapData LoadedMap { get; }
        string LastInputMessage { get; set; }
        bool IsSequencePlaying { get; }
        Transform HostTransform { get; }
        Camera GameplayCamera { get; }
        /// <summary>null일 수 있다(테스트 픽스처).</summary>
        AtlasTilePresentationView TileView { get; }
        /// <summary>null일 수 있다.</summary>
        CombatActorMarkerPresenter ActorMarkers { get; }
        CombatCameraController CameraController { get; }
        /// <summary>null일 수 있다(바인더 미배치).</summary>
        CinemachineCombatCameraBinder ResolveCameraBinder();
        Vector3 EnemyVisualLocalScale { get; }
        float StageIntroLookAtHeightOffset { get; }

        // ── 뷰·HUD 갱신 ──
        void RefreshView();
        void RefreshHudOnly();
        void CommitPresentationFromState(bool immediateCamera);
        void ClearSelection();
        void UpdateEnemyMarker();
        void RefreshMapVisibilityAndHighlights();
        void RecenterGameplayCameraOnPlayer(bool immediate);
        Coroutine StartCoroutine(IEnumerator routine);

        // ── 보스 비트 ──
        bool IsBossPhaseTransitionViewActive { get; }
        bool IsBossEncounterViewActive { get; }
        IEnumerator RunBossPhaseTransition(string bossUnitId);
        IEnumerator RunBossArenaEncounterCinematic();
        void ReconcileStatusLoopVfx();

        // ── 연출 시퀀스(타임라인은 전부 호스트 것) ──
        MapCombatController.CombatPresentationSnapshot CapturePresentationSnapshot();
        bool ShouldPlayPresentationSequence();
        bool EffectiveAlignImpactToAnimation { get; }
        /// <summary>빈 값은 기본 공격 id로 정규화된다(<see cref="MapCombatController.DebugReplayPlayerAttackTimingId"/>).</summary>
        string ReplayPlayerAttackTimingId { get; set; }
        void StartPresentationSequence(string resolvingText, IEnumerator sequence);
        IEnumerator RunAttackTimeline(MapCombatController.CombatPresentationSnapshot before, MapCombatController.CombatPresentationSnapshot after, HexCoord target, string targetMonsterId, string finalMessage, string attackId);
        IEnumerator RunMoveTimeline(MapCombatController.CombatPresentationSnapshot before, MapCombatController.CombatPresentationSnapshot after, string finalMessage);
        IEnumerator RunBurstTimeline(MapCombatController.CombatPresentationSnapshot before, MapCombatController.CombatPresentationSnapshot after, string finalMessage);
        IEnumerator RunMonsterAttackReplayTimeline(MapCombatController.CombatPresentationSnapshot before, MapCombatController.CombatPresentationSnapshot after, string attackerId, HexCoord attackerCoord, string animationTrigger, bool playerDied, string finalMessage, string attackId, float impactDelayBonus);

        // ── 몬스터 몸집(순수 연출) ──
        Dictionary<string, float> MonsterVisualScaleOverrides { get; }
        float ResolveMonsterVisualScale(string monsterUnitId);
        float ResolveDebugMonsterVisualScaleOverride(string monsterUnitId);

        // ── 스테이지 인트로 부품(트레일러 이음새) ──
        void FaceStageIntroMonsterAtCamera(string monsterId, Vector3 monsterWorld, Vector3 cameraPosition);
        void PlayStageIntroMonsterSpawnVfx(HexCoord coord, Vector3 tileWorld);
        void SetStageIntroHiddenMonsters(HashSet<string> monsterIds);
        void SetCinematicForcedRevealCells(HashSet<HexCoord> cells);
        bool ResolveTileInteractionAtPlayerCoord();
        bool IsStageIntroActive { get; }
        LookPresetCrossfader StageIntroLookCrossfader { get; set; }
        EnvironmentLookPreset StageIntroDayLookPreset { get; }
        EnvironmentLookPreset StageIntroNightLookPreset { get; }
        void EngageStageIntroBuildingNightDim();
        void SnapStageIntroBuildingNightLook();

        // ── 보상 팝업(출하 경로) ──
        void ShowTreasureChestReward(HexMapObjectData chest);
        bool IsRewardPopupOpen();
        bool IsLootPopupOpen();
        IReadOnlyList<string> BuildGachaItemPool();
        /// <summary>처치 보상과 같은 경로로 전리품 목록을 연다(대기 보상 필드 초기화 포함).</summary>
        void OpenLootPopupForDrop(KillDropOutcome drop);
    }
}

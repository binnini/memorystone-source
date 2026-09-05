using System.Collections;
using System.Collections.Generic;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity.Presentation;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// 디버그 공개 API의 파사드(2단계 구조 리팩토링 2-B). 본문은 전부 <see cref="CombatDebugFacade"/>에 있고 여기는
    /// 한 줄 위임 62개 + <see cref="ICombatDebugHost"/> 명시적 구현뿐이다. 시그니처는 옛 DebugTools/DebugPlaytest와
    /// 동일하다(호출부 ~230곳 무변경). 협력자는 지연 생성(<c>??=</c>) — EditMode 픽스처가 Unity 생명주기 없이
    /// <c>AddComponent</c>로 굴리고 필드 이니셜라이저는 <c>this</c>를 못 넘긴다(뷰 절단 선례).
    /// </summary>
    public sealed partial class MapCombatController : ICombatDebugHost
    {
        private CombatDebugFacade debugFacade;

        private CombatDebugFacade DebugFacade => debugFacade ??= new CombatDebugFacade(this);

        // ── 위임(옛 DebugTools.cs · DebugPlaytest.cs 공개 표면, 시그니처 불변) ──
        public bool IsBossPhaseTransitionBeatActive => DebugFacade.IsBossPhaseTransitionBeatActive;
        public bool DebugPlayBossPhaseTransitionBeat(out string report) => DebugFacade.DebugPlayBossPhaseTransitionBeat(out report);
        public bool DebugAdvanceBossPhaseWithBeat(out string report) => DebugFacade.DebugAdvanceBossPhaseWithBeat(out report);
        public bool IsBossArenaEncounterBeatActive => DebugFacade.IsBossArenaEncounterBeatActive;
        public bool DebugPlayBossArenaEncounterBeat(out string report) => DebugFacade.DebugPlayBossArenaEncounterBeat(out report);
        public bool DebugArrangeBossLeap(out string report) => DebugFacade.DebugArrangeBossLeap(out report);
        public void DebugRestoreCombatants() => DebugFacade.DebugRestoreCombatants();
        public void DebugReplayPlayerAttack(bool lethal) => DebugFacade.DebugReplayPlayerAttack(lethal);
        public void DebugReplayPlayerAttack(string attackCardId, bool lethal) => DebugFacade.DebugReplayPlayerAttack(attackCardId, lethal);
        public void DebugReplayCard(string cardId) => DebugFacade.DebugReplayCard(cardId);
        public IReadOnlyList<KeyValuePair<string, string>> GetCatalogCardChoices(CardEffectType effectType) => DebugFacade.GetCatalogCardChoices(effectType);
        public void DebugReplayMonsterAttack(bool lethal) => DebugFacade.DebugReplayMonsterAttack(lethal);
        public float DebugPlaybackTimeScale => DebugFacade.DebugPlaybackTimeScale;
        public void DebugSetPlaybackTimeScale(float scale) => DebugFacade.DebugSetPlaybackTimeScale(scale);
        public bool DebugSandboxSpawnMonster(string definitionId, int maxHp, int preferredDistance) => DebugFacade.DebugSandboxSpawnMonster(definitionId, maxHp, preferredDistance);
        public float DebugGetMonsterVisualScaleOverride(string monsterUnitId) => DebugFacade.DebugGetMonsterVisualScaleOverride(monsterUnitId);
        public void DebugSetMonsterVisualScaleOverride(string monsterUnitId, float multiplier) => DebugFacade.DebugSetMonsterVisualScaleOverride(monsterUnitId, multiplier);
        public bool DebugTryGetMonsterPrefabRootScale(string monsterUnitId, out float rootScale) => DebugFacade.DebugTryGetMonsterPrefabRootScale(monsterUnitId, out rootScale);
        public bool DebugWriteMonsterVisualScaleToPrefab(string definitionId, float rootScale, out string message) => DebugFacade.DebugWriteMonsterVisualScaleToPrefab(definitionId, rootScale, out message);
        public int DebugSandboxRemoveSpawnedMonsters() => DebugFacade.DebugSandboxRemoveSpawnedMonsters();
        public bool DebugSandboxApplyStatusToPlayer(StatusEffectKind kind, int turns, int amount) => DebugFacade.DebugSandboxApplyStatusToPlayer(kind, turns, amount);
        public int DebugSandboxApplyStatusToMonsters(StatusEffectKind kind, int turns, int amount) => DebugFacade.DebugSandboxApplyStatusToMonsters(kind, turns, amount);
        public int DebugSandboxFillExilePile(int count) => DebugFacade.DebugSandboxFillExilePile(count);
        public int DebugSandboxDamagePlayer(int amount) => DebugFacade.DebugSandboxDamagePlayer(amount);
        public void DebugSetActorMarkerUiVisible(bool visible) => DebugFacade.DebugSetActorMarkerUiVisible(visible);
        public bool DebugSandboxSpawnMonsterAtCoord(string definitionId, HexCoord coord, int maxHp, out string spawnedId) => DebugFacade.DebugSandboxSpawnMonsterAtCoord(definitionId, coord, maxHp, out spawnedId);
        public void DebugFaceMonsterAt(string monsterId, Vector3 monsterWorld, Vector3 lookFromWorld) => DebugFacade.DebugFaceMonsterAt(monsterId, monsterWorld, lookFromWorld);
        public void DebugTriggerMonsterAttack(string monsterId) => DebugFacade.DebugTriggerMonsterAttack(monsterId);
        public void DebugTriggerMonsterAttack(string monsterId, string animationTrigger) => DebugFacade.DebugTriggerMonsterAttack(monsterId, animationTrigger);
        public void DebugSetPresentationHiddenMonsters(IEnumerable<string> monsterIds) => DebugFacade.DebugSetPresentationHiddenMonsters(monsterIds);
        public void DebugSetForcedRevealCells(IEnumerable<HexCoord> coords) => DebugFacade.DebugSetForcedRevealCells(coords);
        public void DebugPlayMonsterSpawnVfx(HexCoord coord, Vector3 tileWorld) => DebugFacade.DebugPlayMonsterSpawnVfx(coord, tileWorld);
        public bool DebugTeleportPlayerTo(HexCoord coord) => DebugFacade.DebugTeleportPlayerTo(coord);
        public bool DebugTriggerTileInteractionAtPlayerCoord() => DebugFacade.DebugTriggerTileInteractionAtPlayerCoord();
        public void DebugFacePlayerTowards(HexCoord target) => DebugFacade.DebugFacePlayerTowards(target);
        public void DebugResetPlayerVisual() => DebugFacade.DebugResetPlayerVisual();
        public void DebugSetPlayerDeathFreezeDurationScale(float scale) => DebugFacade.DebugSetPlayerDeathFreezeDurationScale(scale);
        public void DebugSetMonsterAttackReplayImpactDelayBonus(float seconds) => DebugFacade.DebugSetMonsterAttackReplayImpactDelayBonus(seconds);
        public void DebugBeginCinematicCamera(Vector3 startCameraPosition, Vector3 startLookAt, float fieldOfView) => DebugFacade.DebugBeginCinematicCamera(startCameraPosition, startLookAt, fieldOfView);
        public void DebugApplyCinematicCameraPose(Vector3 cameraPosition, Vector3 lookAtPoint) => DebugFacade.DebugApplyCinematicCameraPose(cameraPosition, lookAtPoint);
        public void DebugEndCinematicCamera() => DebugFacade.DebugEndCinematicCamera();
        public void DebugRecenterGameplayCameraOnPlayer() => DebugFacade.DebugRecenterGameplayCameraOnPlayer();
        public void DebugSetCinemachineDebugTextSuppressed(bool suppressed) => DebugFacade.DebugSetCinemachineDebugTextSuppressed(suppressed);
        public void DebugSetIngameFilmingCameraPose(float orbitYawDegrees, float zoomDistanceOverride, float zoomOffset) => DebugFacade.DebugSetIngameFilmingCameraPose(orbitYawDegrees, zoomDistanceOverride, zoomOffset);
        public bool DebugSandboxInstallFieldObject(HexCoord coord, int radius, int remainingTurns, FieldObjectKind kind, int value, string visualRef = "F01") => DebugFacade.DebugSandboxInstallFieldObject(coord, radius, remainingTurns, kind, value, visualRef);
        public Camera DebugGameplayCamera => DebugFacade.DebugGameplayCamera;
        public CombatCinemachineCameraProfile DebugCameraProfile => DebugFacade.DebugCameraProfile;
        public float DebugCameraZoomDistance => DebugFacade.DebugCameraZoomDistance;
        public float DebugCameraMinZoomDistance => DebugFacade.DebugCameraMinZoomDistance;
        public float DebugCameraMaxZoomDistance => DebugFacade.DebugCameraMaxZoomDistance;
        public void DebugSetCameraZoomDistance(float distance) => DebugFacade.DebugSetCameraZoomDistance(distance);
        public void DebugClearIngameFilmingCameraPose() => DebugFacade.DebugClearIngameFilmingCameraPose();
        public bool IsCinematicCameraBlending => DebugFacade.IsCinematicCameraBlending;
        public float StageIntroLookAtHeightOffset => DebugFacade.StageIntroLookAtHeightOffset;
        public void DebugSetTrailerDayLook(bool useDayLook) => DebugFacade.DebugSetTrailerDayLook(useDayLook);
        public bool DebugTeleportToTreasureChest(out string message) => DebugFacade.DebugTeleportToTreasureChest(out message);
        public bool DebugOpenTreasureChest(out string message) => DebugFacade.DebugOpenTreasureChest(out message);
        public bool DebugOpenLootPopup(out string message) => DebugFacade.DebugOpenLootPopup(out message);
        public int DebugGrantMoney(int amount) => DebugFacade.DebugGrantMoney(amount);
        public bool DebugGrantPermanentItem(string definitionId, out string message) => DebugFacade.DebugGrantPermanentItem(definitionId, out message);
        public bool DebugGrantConsumable(string itemId, out string message) => DebugFacade.DebugGrantConsumable(itemId, out message);
        public int DebugSandboxSpawnSquad(IReadOnlyList<string> definitionIds, int startDistance, out string message) => DebugFacade.DebugSandboxSpawnSquad(definitionIds, startDistance, out message);

        // ── ICombatDebugHost (명시적 구현 — 공개 API 무증가) ──
        // State · LoadedMap · StartCoroutine은 이미 public이라 암시적으로 만족한다. LastInputMessage는 public getter/private setter라
        // 세터 쪽만 명시적으로 잇는다(공개 세터를 만들지 않는다).
        string ICombatDebugHost.LastInputMessage
        {
            get => LastInputMessage;
            set => LastInputMessage = value;
        }
        bool ICombatDebugHost.IsSequencePlaying => isSequencePlaying;
        Transform ICombatDebugHost.HostTransform => transform;
        Camera ICombatDebugHost.GameplayCamera => prototype3DCamera;
        AtlasTilePresentationView ICombatDebugHost.TileView => atlasTilePresentationView;
        CombatActorMarkerPresenter ICombatDebugHost.ActorMarkers => actorMarkerPresenter;
        CombatCameraController ICombatDebugHost.CameraController => CameraController;
        CinemachineCombatCameraBinder ICombatDebugHost.ResolveCameraBinder() => ResolveCinemachineCombatCameraBinder();
        Vector3 ICombatDebugHost.EnemyVisualLocalScale => enemyVisualLocalScale;
        float ICombatDebugHost.StageIntroLookAtHeightOffset => stageIntroLookAtHeightOffset;

        void ICombatDebugHost.RefreshView() => RefreshView();
        void ICombatDebugHost.RefreshHudOnly() => RefreshHudOnly();
        void ICombatDebugHost.CommitPresentationFromState(bool immediateCamera) => CommitPresentationFromState(immediateCamera);
        void ICombatDebugHost.ClearSelection() => ClearSelection();
        void ICombatDebugHost.UpdateEnemyMarker() => UpdateEnemyMarker();
        void ICombatDebugHost.RefreshMapVisibilityAndHighlights() => RefreshMapVisibilityAndHighlights();
        void ICombatDebugHost.RecenterGameplayCameraOnPlayer(bool immediate) => RecenterGameplayCameraOnPlayer(immediate);

        bool ICombatDebugHost.IsBossPhaseTransitionViewActive => bossPhaseTransitionViewActive;
        bool ICombatDebugHost.IsBossEncounterViewActive => bossEncounterViewActive;
        IEnumerator ICombatDebugHost.RunBossPhaseTransition(string bossUnitId) => ((ICombatPresentationSink)this).BossPhaseTransition(bossUnitId, null);
        IEnumerator ICombatDebugHost.RunBossArenaEncounterCinematic() => PlayBossArenaEncounterCinematic(CapturePresentationSnapshot());
        void ICombatDebugHost.ReconcileStatusLoopVfx() => StatusLoopVfx.Reconcile();

        CombatPresentationSnapshot ICombatDebugHost.CapturePresentationSnapshot() => CapturePresentationSnapshot();
        bool ICombatDebugHost.ShouldPlayPresentationSequence() => ShouldPlayPresentationSequence();
        bool ICombatDebugHost.EffectiveAlignImpactToAnimation => EffectiveAlignImpactToAnimation;
        string ICombatDebugHost.ReplayPlayerAttackTimingId
        {
            get => DebugReplayPlayerAttackTimingId;
            set => DebugReplayPlayerAttackTimingId = value;
        }
        void ICombatDebugHost.StartPresentationSequence(string resolvingText, IEnumerator sequence) => StartPresentationSequence(resolvingText, sequence);
        IEnumerator ICombatDebugHost.RunAttackTimeline(CombatPresentationSnapshot before, CombatPresentationSnapshot after, HexCoord target, string targetMonsterId, string finalMessage, string attackId)
            => RunAttackTimeline(before, after, target, targetMonsterId, finalMessage, attackId);
        IEnumerator ICombatDebugHost.RunMoveTimeline(CombatPresentationSnapshot before, CombatPresentationSnapshot after, string finalMessage) => RunMoveTimeline(before, after, finalMessage);
        IEnumerator ICombatDebugHost.RunBurstTimeline(CombatPresentationSnapshot before, CombatPresentationSnapshot after, string finalMessage) => RunBurstTimeline(before, after, finalMessage);
        IEnumerator ICombatDebugHost.RunMonsterAttackReplayTimeline(CombatPresentationSnapshot before, CombatPresentationSnapshot after, string attackerId, HexCoord attackerCoord, string animationTrigger, bool playerDied, string finalMessage, string attackId, float impactDelayBonus)
            => RunMonsterAttackReplayTimeline(before, after, attackerId, attackerCoord, animationTrigger, playerDied, finalMessage, attackId, impactDelayBonus);

        Dictionary<string, float> ICombatDebugHost.MonsterVisualScaleOverrides => debugMonsterVisualScaleOverrides;
        float ICombatDebugHost.ResolveMonsterVisualScale(string monsterUnitId) => ResolveMonsterVisualScale(monsterUnitId);
        float ICombatDebugHost.ResolveDebugMonsterVisualScaleOverride(string monsterUnitId) => ResolveDebugMonsterVisualScaleOverride(monsterUnitId);

        void ICombatDebugHost.FaceStageIntroMonsterAtCamera(string monsterId, Vector3 monsterWorld, Vector3 cameraPosition) => Cinematics.FaceStageIntroMonsterAtCamera(monsterId, monsterWorld, cameraPosition);
        void ICombatDebugHost.PlayStageIntroMonsterSpawnVfx(HexCoord coord, Vector3 tileWorld) => Cinematics.PlayStageIntroMonsterSpawnVfx(coord, tileWorld);
        void ICombatDebugHost.SetStageIntroHiddenMonsters(HashSet<string> monsterIds) => stageIntroHiddenMonsterIds = monsterIds;
        void ICombatDebugHost.SetCinematicForcedRevealCells(HashSet<HexCoord> cells) => cinematicForcedRevealCells = cells;
        bool ICombatDebugHost.ResolveTileInteractionAtPlayerCoord() => ResolveTileInteractionAtPlayerCoord();
        bool ICombatDebugHost.IsStageIntroActive => Cinematics.IsStageIntroActive;
        LookPresetCrossfader ICombatDebugHost.StageIntroLookCrossfader
        {
            get => Cinematics.StageIntroLookCrossfader;
            set => Cinematics.StageIntroLookCrossfader = value;
        }
        EnvironmentLookPreset ICombatDebugHost.StageIntroDayLookPreset => Cinematics.StageIntroDayLookPreset;
        EnvironmentLookPreset ICombatDebugHost.StageIntroNightLookPreset => Cinematics.StageIntroNightLookPreset;
        void ICombatDebugHost.EngageStageIntroBuildingNightDim() => Cinematics.EngageStageIntroBuildingNightDim();
        void ICombatDebugHost.SnapStageIntroBuildingNightLook() => Cinematics.SnapStageIntroBuildingNightLook();

        void ICombatDebugHost.ShowTreasureChestReward(HexMapObjectData chest) => ShowTreasureChestReward(chest);
        bool ICombatDebugHost.IsRewardPopupOpen() => IsRewardPopupOpen();
        bool ICombatDebugHost.IsLootPopupOpen() => IsLootPopupOpen();
        IReadOnlyList<string> ICombatDebugHost.BuildGachaItemPool() => BuildGachaItemPool();
        void ICombatDebugHost.OpenLootPopupForDrop(KillDropOutcome drop) => OpenLootPopupForDrop(drop);
    }
}

using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 2단계 구조 리팩토링 2-B의 이음새 검증. 파사드는 <see cref="ICombatDebugHost"/>만 보므로 <b>씬도 컨트롤러도 없이</b>
    /// 스텁 호스트로 굴릴 수 있다 — 그것이 추출의 실익이고, 옛 partial 시절엔 이 표면(공개 62개)에 테스트가 0건이었다.
    /// 출하값은 핀하지 않는다. 상태는 <c>CombatStateFixture</c>에서만 만든다.
    /// </summary>
    public sealed class CombatDebugFacadeTests
    {
        private sealed class StubHost : ICombatDebugHost
        {
            public CombatState State { get; set; }
            public HexMapData LoadedMap => State?.Map;
            public string LastInputMessage { get; set; } = string.Empty;
            public bool IsSequencePlaying { get; set; }
            public Transform HostTransform => null;
            public Camera GameplayCamera => null;
            public AtlasTilePresentationView TileView => null;
            public CombatActorMarkerPresenter ActorMarkers => null;
            public CombatCameraController CameraController => null;
            public CinemachineCombatCameraBinder ResolveCameraBinder() => null;
            public Vector3 EnemyVisualLocalScale => Vector3.one;
            public float StageIntroLookAtHeightOffset => 0f;

            public int RefreshViewCalls;
            public int RefreshHudOnlyCalls;
            public int UpdateEnemyMarkerCalls;
            public HashSet<string> HiddenMonsters = new HashSet<string> { "sentinel" };
            public bool HiddenMonstersAssigned;

            public void RefreshView() => RefreshViewCalls++;
            public void RefreshHudOnly() => RefreshHudOnlyCalls++;
            public void CommitPresentationFromState(bool immediateCamera) { }
            public void ClearSelection() { }
            public void UpdateEnemyMarker() => UpdateEnemyMarkerCalls++;
            public void RefreshMapVisibilityAndHighlights() { }
            public void RecenterGameplayCameraOnPlayer(bool immediate) { }
            public Coroutine StartCoroutine(IEnumerator routine) => null;

            public bool IsBossPhaseTransitionViewActive => false;
            public bool IsBossEncounterViewActive => false;
            public IEnumerator RunBossPhaseTransition(string bossUnitId) { yield break; }
            public IEnumerator RunBossArenaEncounterCinematic() { yield break; }
            public void ReconcileStatusLoopVfx() { }

            public MapCombatController.CombatPresentationSnapshot CapturePresentationSnapshot() => default;
            public bool ShouldPlayPresentationSequence() => false;
            public bool EffectiveAlignImpactToAnimation => false;
            public string ReplayPlayerAttackTimingId { get; set; } = "A01";
            public void StartPresentationSequence(string resolvingText, IEnumerator sequence) { }
            public IEnumerator RunAttackTimeline(MapCombatController.CombatPresentationSnapshot before, MapCombatController.CombatPresentationSnapshot after, HexCoord target, string targetMonsterId, string finalMessage, string attackId) { yield break; }
            public IEnumerator RunMoveTimeline(MapCombatController.CombatPresentationSnapshot before, MapCombatController.CombatPresentationSnapshot after, string finalMessage) { yield break; }
            public IEnumerator RunBurstTimeline(MapCombatController.CombatPresentationSnapshot before, MapCombatController.CombatPresentationSnapshot after, string finalMessage) { yield break; }
            public IEnumerator RunMonsterAttackReplayTimeline(MapCombatController.CombatPresentationSnapshot before, MapCombatController.CombatPresentationSnapshot after, string attackerId, HexCoord attackerCoord, string animationTrigger, bool playerDied, string finalMessage, string attackId, float impactDelayBonus) { yield break; }

            public Dictionary<string, float> MonsterVisualScaleOverrides { get; } = new Dictionary<string, float>();
            public float ResolveMonsterVisualScale(string monsterUnitId) => 1f;
            public float ResolveDebugMonsterVisualScaleOverride(string monsterUnitId) => MonsterVisualScaleOverrides.TryGetValue(monsterUnitId, out var v) ? v : 1f;

            public void FaceStageIntroMonsterAtCamera(string monsterId, Vector3 monsterWorld, Vector3 cameraPosition) { }
            public void PlayStageIntroMonsterSpawnVfx(HexCoord coord, Vector3 tileWorld) { }
            public void SetStageIntroHiddenMonsters(HashSet<string> monsterIds) { HiddenMonsters = monsterIds; HiddenMonstersAssigned = true; }
            public void SetCinematicForcedRevealCells(HashSet<HexCoord> cells) { }
            public bool ResolveTileInteractionAtPlayerCoord() => false;
            public bool IsStageIntroActive => false;
            public LookPresetCrossfader StageIntroLookCrossfader { get; set; }
            public EnvironmentLookPreset StageIntroDayLookPreset => null;
            public EnvironmentLookPreset StageIntroNightLookPreset => null;
            public void EngageStageIntroBuildingNightDim() { }
            public void SnapStageIntroBuildingNightLook() { }

            public void ShowTreasureChestReward(HexMapObjectData chest) { }
            public bool IsRewardPopupOpen() => false;
            public bool IsLootPopupOpen() => false;
            public IReadOnlyList<string> BuildGachaItemPool() => new List<string>();
            public void OpenLootPopupForDrop(KillDropOutcome drop) { }
        }

        private static (StubHost host, CombatDebugFacade facade) Arrange()
        {
            var host = new StubHost { State = CombatStateFixture.Arena(3).WithEnemyEastAt(2).Build() };
            return (host, new CombatDebugFacade(host));
        }

        [Test]
        public void GrantMoneyGoesThroughTheWalletAndRefreshesTheHud()
        {
            var (host, facade) = Arrange();
            var before = host.State.PlayerInventory.Wallet.Balance;

            var balance = facade.DebugGrantMoney(7);

            Assert.That(balance, Is.EqualTo(before + 7));
            Assert.That(host.State.PlayerInventory.Wallet.Balance, Is.EqualTo(before + 7), "지급은 출하 지갑을 그대로 지난다.");
            Assert.That(host.LastInputMessage, Does.Contain("엽전"));
            Assert.That(host.RefreshHudOnlyCalls, Is.EqualTo(1), "지급 뒤 HUD 갱신을 호스트에 요청해야 화면이 따라온다.");
        }

        [Test]
        public void SandboxMutationsRefuseWhileASequenceIsPlaying()
        {
            var (host, facade) = Arrange();
            host.IsSequencePlaying = true;
            var hp = host.State.Player.Hp;

            Assert.That(facade.DebugSandboxDamagePlayer(3), Is.EqualTo(0));
            Assert.That(host.State.Player.Hp, Is.EqualTo(hp), "연출 재생 중엔 규칙을 만지면 안 된다(옛 switch 가드와 같다).");
            Assert.That(host.RefreshViewCalls, Is.EqualTo(0));

            host.IsSequencePlaying = false;
            Assert.That(facade.DebugSandboxDamagePlayer(3), Is.GreaterThan(0));
            Assert.That(host.RefreshViewCalls, Is.EqualTo(1), "규칙을 바꿨으면 뷰 갱신을 호스트에 요청한다.");
        }

        [Test]
        public void DeathFreezeScaleIsClampedAndReadableByTheHost()
        {
            var (_, facade) = Arrange();
            Assert.That(facade.PlayerDeathFreezeDurationScale, Is.EqualTo(1f), "기본은 저작값 그대로(1).");

            facade.DebugSetPlayerDeathFreezeDurationScale(5f);
            Assert.That(facade.PlayerDeathFreezeDurationScale, Is.EqualTo(2f), "상한 2로 클램프.");
            facade.DebugSetPlayerDeathFreezeDurationScale(-1f);
            Assert.That(facade.PlayerDeathFreezeDurationScale, Is.EqualTo(0f), "하한 0으로 클램프.");
        }

        [Test]
        public void HiddenMonstersArePassedToTheHostAndNullClears()
        {
            var (host, facade) = Arrange();

            facade.DebugSetPresentationHiddenMonsters(new[] { "m1", "m2" });
            Assert.That(host.HiddenMonsters, Is.EquivalentTo(new[] { "m1", "m2" }));
            Assert.That(host.UpdateEnemyMarkerCalls, Is.EqualTo(1), "숨김 집합을 바꿨으면 마커를 다시 그려야 한다.");

            facade.DebugSetPresentationHiddenMonsters(null);
            Assert.That(host.HiddenMonstersAssigned, Is.True);
            Assert.That(host.HiddenMonsters, Is.Null, "null은 「숨김 해제」다(인트로 teardown과 같은 계약).");
        }

        [Test]
        public void MonsterVisualScaleOverrideIsRemovedAtOneAndStoredOtherwise()
        {
            var (host, facade) = Arrange();

            facade.DebugSetMonsterVisualScaleOverride("m1", 1.5f);
            Assert.That(facade.DebugGetMonsterVisualScaleOverride("m1"), Is.EqualTo(1.5f));

            facade.DebugSetMonsterVisualScaleOverride("m1", 1f);
            Assert.That(host.MonsterVisualScaleOverrides.ContainsKey("m1"), Is.False, "1은 「배율 없음」이라 항목을 지운다.");
        }
    }
}

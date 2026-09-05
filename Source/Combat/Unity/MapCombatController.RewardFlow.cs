using System.Collections.Generic;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity.Tutorial;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// 보상·상점·서비스 흐름의 파사드(4B-B). 본문은 전부 <see cref="CombatRewardFlow"/>에 있고 여기는 시그니처 불변 위임과
    /// <see cref="ICombatRewardFlowHost"/> 명시적 구현뿐이다. 협력자는 지연 생성(<c>??=</c>) — EditMode 픽스처가
    /// <c>AddComponent</c>로 굴리고 필드 이니셜라이저는 <c>this</c>를 못 넘긴다(2단계 선례).
    /// </summary>
    public sealed partial class MapCombatController : ICombatRewardFlowHost
    {
        private CombatRewardFlow rewardFlow;

        private CombatRewardFlow RewardFlow => rewardFlow ??= new CombatRewardFlow(this);

        // ── 공개 표면(옛 Rewards/Services 파셜, 시그니처 불변) ──
        public void DebugShowCardReward(bool elite) => RewardFlow.DebugShowCardReward(elite);
        public static string FormatCamperHealDetail(int hp, int maxHp, int healAmount) => CombatRewardFlow.FormatCamperHealDetail(hp, maxHp, healAmount);
        public bool DebugTeleportToServiceObject(string kind, out string message) => RewardFlow.DebugTeleportToServiceObject(kind, out message);
        public bool DebugOpenServiceModal(string kind, out string message) => RewardFlow.DebugOpenServiceModal(kind, out message);
        public int DebugResetServiceConsumption() => RewardFlow.DebugResetServiceConsumption();

        // ── 다른 파셜이 부르는 사설 이음새(같은 이름 위임) ──
        private void RefreshCardRewardPopup() => RewardFlow.RefreshCardRewardPopup();
        private bool TryTriggerTreasureChestAtPlayerCoord() => RewardFlow.TryTriggerTreasureChestAtPlayerCoord();
        private bool TryTriggerCursedGachaAtPlayerCoord() => RewardFlow.TryTriggerCursedGachaAtPlayerCoord();
        private bool TryTriggerShopAtPlayerCoord() => RewardFlow.TryTriggerShopAtPlayerCoord();
        private bool TryTriggerCamperVanAtPlayerCoord() => RewardFlow.TryTriggerCamperVanAtPlayerCoord();
        private bool TryTriggerWorkshopAtPlayerCoord() => RewardFlow.TryTriggerWorkshopAtPlayerCoord();
        private bool IsRewardPopupOpen() => RewardFlow.IsRewardPopupOpen();
        private void ShowTreasureChestReward(HexMapObjectData objectData) => RewardFlow.ShowTreasureChestReward(objectData);
        private bool IsLootPopupOpen() => RewardFlow.IsLootPopupOpen();
        private static IReadOnlyList<string> BuildGachaItemPool() => CombatRewardFlow.BuildGachaItemPool();
        private void OpenLootPopupForDrop(KillDropOutcome drop) => RewardFlow.OpenLootPopupForDrop(drop);
        private void ResetRewardStateForNewCombat() => RewardFlow.ResetRewardStateForNewCombat();
        // 리플렉션 문자열 "DescribeRewardCatalogEntry"(CardCatalogCsvImporterTests · NonPublic|Static)가 이 이름을 호스트에서
        // 잡는다 — 지우면 컴파일은 되고 그 테스트만 런타임에 깨진다.
        private static string DescribeRewardCatalogEntry(CardCatalogEntry entry, string catalogSourceId) => CombatRewardFlow.DescribeRewardCatalogEntry(entry, catalogSourceId);

        // ── ICombatRewardFlowHost (명시적 구현 — 공개 API 무증가) ──
        // State · LoadedMap · TryGetTileWorldPosition · CanSelectRewardForTutorial · ShowTutorialBlockedFeedback ·
        // NotifyTutorialRewardSelected는 이미 public이라 암시적으로 만족한다.
        string ICombatRewardFlowHost.LastInputMessage
        {
            get => LastInputMessage;
            set => LastInputMessage = value;
        }
        bool ICombatRewardFlowHost.IsSequencePlaying => isSequencePlaying;
        Object ICombatRewardFlowHost.LogContext => this;
        CardRewardPopupView ICombatRewardFlowHost.CardRewardPopupView => cardRewardPopupView;
        ICardRewardPopupView ICombatRewardFlowHost.CardRewardPresentation => CardRewardPresentation;
        void ICombatRewardFlowHost.EnsureCardRewardPopupView() => EnsureCardRewardPopupView();
        AtlasTilePresentationView ICombatRewardFlowHost.TileView => atlasTilePresentationView;
        CombatCatalogTextAssetSource ICombatRewardFlowHost.CatalogSource => catalogTextAssetSource;
        void ICombatRewardFlowHost.RefreshHudOnly() => RefreshHudOnly();
        void ICombatRewardFlowHost.RequestAudioCue(string cueId, string context) => RequestAudioCue(cueId, context);
        void ICombatRewardFlowHost.CommitPresentationFromState(bool immediateCamera) => CommitPresentationFromState(immediateCamera);
        EffectPresentationController ICombatRewardFlowHost.ResolveMovementEffectPresentation() => ResolveMovementEffectPresentation();
        TutorialDirector ICombatRewardFlowHost.ResolveTutorialDirector(bool createIfMissing) => ResolveTutorialDirector(createIfMissing);
    }
}

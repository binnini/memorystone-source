using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity.Tutorial;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// 보상·상점·서비스 오브젝트 흐름의 협력자(4단계 구조 리팩토링 4B-B · 2026-09-04). 옛
    /// <c>MapCombatController.Rewards.cs</c>·<c>.Shop.cs</c>·<c>.Services.cs</c>의 본문을 <b>무변경</b>으로 옮겼고, 호스트에는
    /// <see cref="ICombatRewardFlowHost"/>를 통해서만 닿는다 — 사설 필드 직접 접근 0. 세 묶음을 한 협력자로 둔 이유: 모달
    /// 상호배제 게이트(<c>IsRewardPopupOpen</c>이 상점·서비스 열림까지 본다)·공유 보상 코어(카드 풀·난수·뽑기 풀)·
    /// 같은 소비 장부(<c>State.TryConsumeShopObject</c>)를 셋이 함께 쓴다.
    ///
    /// <para>소유 상태: 보상 대기 상태(pendingReward*)·전리품 목록·상점 재고·서비스 대기·런타임 생성 팝업 뷰 3종·CSV 캐시 4종.
    /// 코루틴 0 · 직렬화 필드 0. 직렬화 팝업(<c>cardRewardPopupView</c>)과 그 생성(<c>EnsureCardRewardPopupView</c>)은
    /// 호스트 것이다(직렬화 필드는 옮기지 않는다).</para>
    ///
    /// <para>공개 API(<c>DebugShowCardReward</c>·서비스 디버그 3종·<c>FormatCamperHealDetail</c>)는 그대로
    /// <see cref="MapCombatController"/>에 위임으로 남는다. 상점(잡화점)·서비스 문서는 각 절 머리의 옛 파일 주석을 보라.</para>
    /// </summary>
    internal sealed class CombatRewardFlow
    {
        private readonly ICombatRewardFlowHost host;

        public CombatRewardFlow(ICombatRewardFlowHost host)
        {
            this.host = host ?? throw new System.ArgumentNullException(nameof(host));
        }

        // ── 4B-A(2026-09-04): 본체에서 옮겨온 보상 전용 상수·상태(직렬화 아님 · 본문 무변경). 보상 파셜 밖에서 닿는 곳은
        // 디버그 파사드(OpenLootPopupForDrop)와 승리 결과창(acquiredRewardCardCount)뿐이다.
        private const string TreasureChestOpenVfxCue = "object.treasure_chest.open";
        private const string TreasureChestRewardVfxCue = "object.treasure_chest.reward";
        private const string TreasureChestClaimVfxCue = "object.treasure_chest.claim";

        private readonly HashSet<string> rewardedMonsterIds = new HashSet<string>();
        private string pendingRewardMonsterId;
        private CombatEventObjectDefinition pendingRewardEventObject;
        private HexMapObjectData pendingRewardMapObjectData;
        private bool hasPendingRewardMapObjectData;
        private IReadOnlyList<CardRewardOffer> pendingRewardOffers;
        private bool pendingRewardRerollUsed;
        // True when the active reward (incl. its reroll) comes from an elite monster kill, which draws
        // Epic/Legendary only. Treasure chests and regular kills leave this false.
        private bool pendingRewardIsElite;
        private int acquiredRewardCardCount;

        /// <summary>승리 결과창의 「추가한 카드 : N장」(호스트 HudTooltips가 읽는다).</summary>
        internal int AcquiredRewardCardCount => acquiredRewardCardCount;

        // 전투 (재)시작 시 보상 상태 초기화 — 옛 InitializeIntegration 안의 리셋 블록 그대로.
        internal void ResetRewardStateForNewCombat()
        {
            rewardedMonsterIds.Clear();
            pendingRewardMonsterId = null;
            pendingRewardEventObject = null;
            pendingRewardMapObjectData = default;
            hasPendingRewardMapObjectData = false;
            pendingRewardOffers = null;
            pendingRewardIsElite = false;
            acquiredRewardCardCount = 0;
        }

        /// <summary>같은 몬스터로 두 번 받지 않게 래치를 건다 — 씬 안의 HashSet과 저장을 건너는 상태 플래그에 함께.</summary>
        private void MarkPendingMonsterRewarded()
        {
            if (pendingRewardMonsterId == null)
            {
                return;
            }

            rewardedMonsterIds.Add(pendingRewardMonsterId);
            host.State?.MarkMonsterRewardClaimed(pendingRewardMonsterId);
        }

        // 디버그 파사드의 전리품 목록 강제(ICombatDebugHost.OpenLootPopupForDrop) — 옛 명시적 구현 본문 그대로.
        internal void OpenLootPopupForDrop(KillDropOutcome drop)
        {
            pendingRewardMonsterId = null;
            pendingRewardRerollUsed = false;
            pendingRewardIsElite = false;
            pendingKillDrop = drop;
            pendingLootRows = BuildLootRows(pendingKillDrop);
            ShowLootPopup();
        }

        internal void RefreshCardRewardPopup()
        {
            if (host.CardRewardPopupView == null)
            {
                return;
            }

            if (host.CardRewardPresentation.gameObject.activeSelf || IsLootPopupOpen())
            {
                return;
            }

            if (host.State == null || host.IsSequencePlaying || host.State.Player.IsDead || pendingRewardEventObject != null)
            {
                return;
            }

            // 보스 기물(철조각 등)은 파괴해도 카드 보상을 주지 않는다. 기믹이 매 턴 새로 뿌리는 기물이
            // 보상 추첨에 들어가면 카드를 무한히 뽑을 수 있다.
            // 🔑 래치는 두 벌이다: 이 HashSet(같은 씬 안)과 상태의 RewardClaimed(중단 저장을 건너서).
            //    HashSet만 있을 때는 로비로 나갔다 이어하면 시체 전부가 「방금 죽은 적」이 돼 보상을
            //    처치 수만큼 다시 뿌렸다(2026-09-05 실플레이 #7).
            var newlyDead = host.State.Monsters.FirstOrDefault(m =>
                m.IsDead && !MonsterSpawnRoles.IsProp(m.SpawnRole) && !m.RewardClaimed && !rewardedMonsterIds.Contains(m.Id));
            if (string.IsNullOrEmpty(newlyDead.Id))
            {
                return;
            }

            pendingRewardMonsterId = newlyDead.Id;
            pendingRewardRerollUsed = false;
            pendingRewardIsElite = MonsterSpawnRoles.IsElite(newlyDead.SpawnRole);

            // 🔑 처치 보상의 입구는 이제 <b>전리품 목록</b>이다(DEC-2026-08-31-03). 부적 3택은 그
            //    목록의 「부적 추가」 줄을 눌러야 열린다 — 여기서 미리 만들지 않는다.
            pendingKillDrop = RollKillDrop(newlyDead.SpawnRole);
            pendingLootRows = BuildLootRows(pendingKillDrop, newlyDead.RestoredMoney);
            // 여는 소리는 ShowLootPopup이 낸다(reward.loot.appear) — 3택에서 목록으로 돌아올 때도 같은 창이 다시 열린다.
            ShowLootPopup();
        }

        private void OnCardRewardSelected(string cardId)
        {
            if (!host.CanSelectRewardForTutorial(cardId, out var tutorialFailure))
            {
                host.ShowTutorialBlockedFeedback(tutorialFailure);
                return;
            }

            var pendingOffer = pendingRewardEventObject?.RewardOffers.FirstOrDefault(offer =>
                string.Equals(offer.RewardId, cardId, System.StringComparison.Ordinal));
            host.RequestAudioCue(
                pendingOffer.HasValue ? ResolveRewardOfferCueId(pendingOffer.Value.Kind) : AudioCueIds.RewardCardAcquire,
                $"reward:acquire:{cardId}");
            if (pendingRewardEventObject != null)
            {
                ClaimPendingRewardEventObject(cardId);
                return;
            }

            var reason = string.Empty;
            if (host.State == null || !host.State.TryAddRewardCardToCurrentHand(cardId, out reason))
            {
                host.LastInputMessage = string.IsNullOrWhiteSpace(reason) ? "Card reward could not be added to the current hand." : reason;
                // 부적을 손패에 못 넣어도 남은 전리품은 이 처치의 것이다 — 목록으로 돌려보낸다.
                ResolveCardRowAndReturnToLoot();
                return;
            }

            acquiredRewardCardCount++;
            host.LastInputMessage = "Card reward added to the current hand.";
            host.NotifyTutorialRewardSelected(cardId);
            ResolveCardRowAndReturnToLoot();
        }

        private void OnCardRewardSkipped()
        {
            var director = host.ResolveTutorialDirector();
            if (director != null && director.IsActive && director.CurrentStep?.AdvanceMode == TutorialAdvanceMode.RewardSelected)
            {
                host.ShowTutorialBlockedFeedback("\uD29C\uD1A0\uB9AC\uC5BC \uC9C4\uD589 \uC911\uC5D0\uB294 \uBCF4\uC0C1 \uCE74\uB4DC\uB97C \uC120\uD0DD\uD574\uC57C \uD569\uB2C8\uB2E4.");
                return;
            }

            host.RequestAudioCue(AudioCueIds.RewardSkip, "reward:skip:card");
            if (pendingRewardEventObject != null && host.State != null)
            {
                if (host.State.TrySkipRewardEventObject(pendingRewardEventObject, out _))
                {
                    host.TileView?.ConsumeMapObjectVisual(pendingRewardEventObject.ObjectId);
                }
            }

            var wasEventObject = pendingRewardEventObject != null;
            pendingRewardEventObject = null;
            pendingRewardMapObjectData = default;
            hasPendingRewardMapObjectData = false;

            if (!wasEventObject && cardRewardOpenedFromLoot)
            {
                // 🔑 3택의 넘기기는 <b>부적만</b> 포기한다 — 목록에 남은 전리품은 그대로 있고,
                //    전부 버리는 것은 전리품 목록 쪽 「넘기기」다(그쪽은 무엇을 버리는지 보인다).
                host.LastInputMessage = "Card reward skipped.";
                ResolveCardRowAndReturnToLoot();
                return;
            }

            MarkPendingMonsterRewarded();
            pendingRewardMonsterId = null;
            pendingRewardOffers = null;
            pendingRewardRerollUsed = false;
            host.CardRewardPopupView?.Hide();
            host.LastInputMessage = "Card reward skipped.";
            host.RefreshHudOnly();
        }

        private void OnCardRewardRerollRequested()
        {
            if (pendingRewardRerollUsed || host.CardRewardPopupView == null)
            {
                return;
            }

            pendingRewardRerollUsed = true;
            host.RequestAudioCue(AudioCueIds.RewardReroll, "reward:reroll");
            pendingRewardOffers = GenerateCardRewardOffers();
            if (pendingRewardEventObject != null && hasPendingRewardMapObjectData)
            {
                pendingRewardEventObject = CombatEventObjectDefinition.CardRewardMachine(
                    pendingRewardMapObjectData.ObjectId,
                    pendingRewardOffers.Select(offer => RewardEventObjectOffer.Card(
                        offer.CardId,
                        offer.DisplayName,
                        offer.Description,
                        offer.EffectType)));
            }

            host.CardRewardPresentation.ReplaceOffers(pendingRewardOffers, rerollAvailable: false);
            host.LastInputMessage = "\uBCF4\uC0C1 \uCE74\uB4DC\uB97C \uB2E4\uC2DC \uBF51\uC558\uC2B5\uB2C8\uB2E4.";
            host.RefreshHudOnly();
        }

        /// <summary>
        /// Debug-only: opens the card reward popup on demand (no monster kill / treasure chest needed)
        /// so reward presentation \u2014 rarity glow, badge, weighting \u2014 can be inspected in play mode.
        /// Selecting a card adds it to the current hand; skip/reroll behave normally.
        /// </summary>
        public void DebugShowCardReward(bool elite)
        {
            if (!Application.isPlaying || host.State?.CardCatalog == null)
            {
                host.LastInputMessage = "Debug card reward needs play mode and a loaded catalog.";
                return;
            }

            host.EnsureCardRewardPopupView();
            if (host.CardRewardPopupView == null)
            {
                host.LastInputMessage = "Debug card reward: popup view unavailable.";
                return;
            }

            if (host.CardRewardPresentation.gameObject.activeSelf)
            {
                host.CardRewardPresentation.Hide();
            }

            pendingRewardEventObject = null;
            pendingRewardMonsterId = null;
            pendingRewardMapObjectData = default;
            hasPendingRewardMapObjectData = false;
            pendingRewardRerollUsed = false;
            pendingRewardIsElite = elite;
            pendingRewardOffers = GenerateCardRewardOffers();
            host.CardRewardPresentation.Show(pendingRewardOffers, OnCardRewardSelected, OnCardRewardSkipped, OnCardRewardHovered, OnCardRewardRerollRequested, !pendingRewardRerollUsed);
            host.RequestAudioCue(AudioCueIds.RewardPopupAppear, elite ? "reward:debug:elite" : "reward:debug:normal");
            host.LastInputMessage = elite ? "Debug: elite card reward shown (\uC601\uC6C5/\uC804\uC124)." : "Debug: card reward shown (\uD76C\uADC0/\uC601\uC6C5/\uC804\uC124).";
            host.RefreshHudOnly();
        }

        internal bool TryTriggerTreasureChestAtPlayerCoord()
        {
            if (host.State == null || host.LoadedMap == null)
            {
                return false;
            }

            var objectData = host.LoadedMap.GetObjectsAt(host.State.PlayerCoord)
                .FirstOrDefault(candidate =>
                    candidate.Interactable &&
                    string.Equals(candidate.ObjectType, "TreasureChest", System.StringComparison.Ordinal) &&
                    !host.State.ClaimedEventObjectIds.Contains(candidate.ObjectId));

            if (!objectData.IsConfigured)
            {
                return false;
            }

            ShowTreasureChestReward(objectData);
            return true;
        }

        /// <summary>
        /// 저주받은 인형뽑기(T2-C). 보물상자와 달리 팝업 없이 즉시 확정 지급 — 추첨·지급·소비 전부
        /// <see cref="CombatState.TryOpenCursedGachaMachine"/>(순수 C#, EditMode 테스트 가능)에 있고
        /// 여기는 오브젝트 탐지와 메시지 표출만 한다.
        /// </summary>
        internal bool TryTriggerCursedGachaAtPlayerCoord()
        {
            // 은퇴한 오브젝트(2026-09-01 사용자 확정)는 밟혀도 열리지 않는다.
            // 🔴 시각 스폰(MapObjectVisualRegistry)과 <b>한 쌍</b>이다 — 한쪽만 끄면
            //    「안 보이는데 밟히는 칸」이 된다.
            if (!SeoulPlayup.Map.Runtime.EventObjectAvailability.CursedGachaMachineEnabled)
            {
                return false;
            }

            if (host.State == null || host.LoadedMap == null)
            {
                return false;
            }

            var objectData = host.LoadedMap.GetObjectsAt(host.State.PlayerCoord)
                .FirstOrDefault(candidate =>
                    candidate.Interactable &&
                    string.Equals(candidate.ObjectType, "CursedGachaMachine", System.StringComparison.Ordinal) &&
                    !host.State.ClaimedEventObjectIds.Contains(candidate.ObjectId));

            if (!objectData.IsConfigured)
            {
                return false;
            }

            if (!host.State.TryOpenCursedGachaMachine(objectData.ObjectId, RewardRandom, out var message))
            {
                return false;
            }

            host.LastInputMessage = message;
            host.RefreshHudOnly();
            return true;
        }

        private void OnCardRewardHovered()
        {
            host.RequestAudioCue(AudioCueIds.RewardCardHover, "reward:hover");
        }

        internal void ShowTreasureChestReward(HexMapObjectData objectData)
        {
            if (host.State == null)
            {
                return;
            }

            if (host.CardRewardPopupView != null && host.CardRewardPresentation.gameObject.activeSelf)
            {
                return;
            }

            if (host.State.ClaimedEventObjectIds.Contains(objectData.ObjectId))
            {
                host.LastInputMessage = "\uC774\uBBF8 \uBF51\uC740 \uAE30\uACC4\uC785\uB2C8\uB2E4.";
                host.RefreshHudOnly();
                return;
            }

            // \uC0C1\uC790\uB294 \uACE7 \uC778\uD615\uBF51\uAE30\uB2E4(D-3). \uBB34\uC5C7\uC774 \uB098\uC62C\uC9C0\uB294 \uC21C\uC218 \uCD94\uCCA8\uAE30\uAC00 \uBA3C\uC800 \uC815\uD558\uACE0, \uCE74\uB4DC\uD329\uC77C \uB54C\uB9CC
            // \uC544\uB798\uC758 \uCE74\uB4DC 3\uD0DD \uACBD\uB85C\uB85C \uC774\uC5B4\uC9C4\uB2E4. \uB3C8\u00B7\uC720\uBB3C\uC740 \uC120\uD0DD \uC5C6\uC774 \uC989\uC2DC \uC9C0\uAE09\uB41C\uB2E4.
            var outcome = GachaRewardRoller.Roll(
                GachaRewardWeights,
                GachaRewardRoller.BuildRelicPool(PlayerPermanentItemCatalog.Definitions, host.State.PlayerInventory?.RelicsAndCurses),
                RewardRandom,
                BuildGachaItemPool());

            if (outcome.Kind != GachaRewardKind.CardPack)
            {
                GrantNonCardGachaOutcome(objectData, outcome);
                return;
            }

            host.EnsureCardRewardPopupView();
            if (host.CardRewardPopupView == null)
            {
                host.LastInputMessage = "\uBCF4\uC0C1 \uC120\uD0DD UI\uB97C \uC5F4 \uC218 \uC5C6\uC2B5\uB2C8\uB2E4. \uB2E4\uC2DC \uC2DC\uB3C4\uD574 \uC8FC\uC138\uC694.";
                host.RefreshHudOnly();
                return;
            }

            pendingRewardMonsterId = null;
            pendingRewardMapObjectData = objectData;
            hasPendingRewardMapObjectData = true;
            pendingRewardRerollUsed = false;
            pendingRewardIsElite = false;
            pendingRewardOffers = GenerateCardRewardOffers();
            pendingRewardEventObject = CombatEventObjectDefinition.CardRewardMachine(
                objectData.ObjectId,
                pendingRewardOffers.Select(offer => RewardEventObjectOffer.Card(
                    offer.CardId,
                    offer.DisplayName,
                    offer.Description,
                    offer.EffectType)));

            PlayTreasureChestVfx(objectData, TreasureChestOpenVfxCue);
            host.CardRewardPresentation.Show(pendingRewardOffers, OnCardRewardSelected, OnCardRewardSkipped, OnCardRewardHovered, OnCardRewardRerollRequested, !pendingRewardRerollUsed);
            PlayTreasureChestVfx(objectData, TreasureChestRewardVfxCue);
            host.RequestAudioCue(AudioCueIds.RewardChestOpen, $"reward:chest:{objectData.ObjectId}");
            host.LastInputMessage = "\uC0C1\uC790\uB97C \uC5F4\uC5C8\uC2B5\uB2C8\uB2E4. \uBCF4\uC0C1 \uCE74\uB4DC\uB97C \uC120\uD0DD\uD558\uC138\uC694.";
            host.RefreshHudOnly();
        }

        private void PlayTreasureChestVfx(HexMapObjectData objectData, string sourceRef)
        {
            if (!objectData.IsConfigured || string.IsNullOrWhiteSpace(sourceRef))
            {
                return;
            }

            var presentation = host.ResolveMovementEffectPresentation();
            if (presentation == null || !host.TryGetTileWorldPosition(objectData.Coord, out var worldPosition))
            {
                return;
            }

            // Only the reward cue carries floating text ("카드 획득!", mapped in EffectPresentationController).
            // The open/claim cues are firework VFX only: passing amount 0 on a "field" target suppresses their
            // floating text, otherwise they leak the literal enum name ("StatusEffectApplied") over the chest.
            var showRewardText = string.Equals(sourceRef, TreasureChestRewardVfxCue, System.StringComparison.Ordinal);
            var resultEvent = new EffectResultEvent(
                EffectKind.StatusEffectApplied,
                targetUnitId: "field",
                appliedAmount: showRewardText ? 1 : 0,
                center: objectData.Coord,
                sourceRef: sourceRef);
            presentation.Play(resultEvent, worldPosition, Quaternion.identity);
        }

        private void ClaimPendingRewardEventObject(string cardId)
        {
            var eventObject = pendingRewardEventObject;
            var selectedOffer = eventObject?.RewardOffers.FirstOrDefault(offer =>
                string.Equals(offer.RewardId, cardId, System.StringComparison.Ordinal));
            var reason = string.Empty;

            if (eventObject == null ||
                !selectedOffer.HasValue ||
                string.IsNullOrWhiteSpace(selectedOffer.Value.RewardId) ||
                !host.State.TryClaimRewardEventObject(eventObject, selectedOffer.Value, out reason))
            {
                host.LastInputMessage = string.IsNullOrWhiteSpace(reason) ? "\uBCF4\uC0C1\uC744 \uD68D\uB4DD\uD560 \uC218 \uC5C6\uC2B5\uB2C8\uB2E4. \uB2E4\uC2DC \uC2DC\uB3C4\uD574 \uC8FC\uC138\uC694." : reason;
            }
            else
            {
                if (hasPendingRewardMapObjectData)
                {
                    PlayTreasureChestVfx(pendingRewardMapObjectData, TreasureChestClaimVfxCue);
                }

                host.TileView?.ConsumeMapObjectVisual(eventObject.ObjectId);
                acquiredRewardCardCount++;
                host.LastInputMessage = "\uBCF4\uC0C1 \uCE74\uB4DC\uB97C \uD68D\uB4DD\uD588\uC2B5\uB2C8\uB2E4.";
                host.NotifyTutorialRewardSelected(cardId);
            }

            pendingRewardEventObject = null;
            pendingRewardMapObjectData = default;
            hasPendingRewardMapObjectData = false;
            pendingRewardOffers = null;
            pendingRewardRerollUsed = false;
            host.CardRewardPopupView?.Hide();
            host.RefreshHudOnly();
        }

        // Reward rarity weights are authored in Assets/Data/Combat/Reward/Source/combat_rewards.csv
        // (DEC-2026-07-28-04). Loaded once per combat and cached; falls back to the code default when the
        // TextAsset is unassigned, mirroring the player-profile pipeline.
        private CardRewardRarityWeights cardRewardWeights;

        private CardRewardRarityWeights CardRewardWeights
        {
            get
            {
                if (cardRewardWeights == null)
                {
                    cardRewardWeights = host.CatalogSource != null && host.CatalogSource.HasCardRewardWeights
                        ? host.CatalogSource.CreateCardRewardWeights()
                        : CardRewardRarityWeights.Default;
                }

                return cardRewardWeights;
            }
        }

        // 인형뽑기 결과 분포는 Assets/Data/Combat/Reward/Source/gacha_rewards.csv가 정본이다.
        // 카드 등급 분포와 같은 관례로 전투당 한 번 로드해 캐시하고, TextAsset 미할당 시 코드 폴백.
        private GachaRewardWeights gachaRewardWeights;

        /// <summary>테스트 이음새(호스트 <c>ConfigureGachaRewardWeightsForTests</c>가 위임). null = 출하 표.</summary>
        internal void ConfigureGachaRewardWeights(GachaRewardWeights weights) => gachaRewardWeights = weights;

        private GachaRewardWeights GachaRewardWeights
        {
            get
            {
                if (gachaRewardWeights == null)
                {
                    gachaRewardWeights = host.CatalogSource != null && host.CatalogSource.HasGachaRewardWeights
                        ? host.CatalogSource.CreateGachaRewardWeights()
                        : Runtime.GachaRewardWeights.Default;
                }

                return gachaRewardWeights;
            }
        }

        /// <summary>
        /// 카드팩이 아닌 뽑기 결과(돈·유물)를 지급하고 상자를 소비한다. 선택지가 없으므로 팝업을
        /// 띄우지 않고 즉시 확정한다 — 지급 실패는 추첨기 계약상 일어나지 않아야 하지만, 일어나면
        /// 상자를 소비하지 않고 메시지로 드러낸다(조용히 삼키면 상자만 사라진다).
        /// </summary>
        /// <summary>뽑기 Item(T4-3) 추첨 풀 — 소모품 카탈로그 전종(12종 균등, 중복 보유 허용).</summary>
        internal static System.Collections.Generic.IReadOnlyList<string> BuildGachaItemPool()
        {
            var pool = new System.Collections.Generic.List<string>();
            foreach (var item in ConsumableItemCatalog.Definitions)
            {
                // 은퇴한 소모품은 뽑히지 않는다 — 지급 관문이 어차피 막으므로, 풀에 남겨 두면
                // 「뽑았는데 아무 일도 없다」가 된다(CR-10이 없애려 한 바로 그 체감).
                if (ConsumableItemAvailability.IsRetired(item.Id))
                {
                    continue;
                }

                pool.Add(item.Id);
            }

            return pool;
        }

        private void GrantNonCardGachaOutcome(HexMapObjectData objectData, GachaOutcome outcome)
        {
            // 소모품(T4-3): 가방이 가득이면 돈 +15 폴백 — "뽑았는데 아무 일도 없음"이 최악(CR-10의 정신).
            var isItem = outcome.Kind == GachaRewardKind.Item;
            if (isItem && host.State.PlayerInventory?.Bag != null
                && host.State.PlayerInventory.Bag.UsedSlotCount >= host.State.GetEffectiveBagSlotLimit())
            {
                outcome = new GachaOutcome(GachaRewardKind.Money, GachaFullBagFallbackMoney);
                isItem = false;
            }

            var isRelic = outcome.Kind == GachaRewardKind.Relic;
            var offer = isRelic
                ? RewardEventObjectOffer.PermanentItem(outcome.RelicId)
                : isItem
                    ? RewardEventObjectOffer.BagItem(outcome.ItemId)
                    : RewardEventObjectOffer.Money(outcome.Amount);
            var machine = isRelic
                ? CombatEventObjectDefinition.RelicRewardMachine(objectData.ObjectId, new[] { offer })
                : isItem
                    ? CombatEventObjectDefinition.ItemRewardMachine(objectData.ObjectId, new[] { offer })
                    : CombatEventObjectDefinition.MoneyRewardMachine(objectData.ObjectId, new[] { offer });

            if (!host.State.TryClaimRewardEventObject(machine, offer, out var reason))
            {
                host.LastInputMessage = string.IsNullOrWhiteSpace(reason) ? "보상을 지급하지 못했습니다." : reason;
                host.RefreshHudOnly();
                return;
            }

            host.TileView?.ConsumeMapObjectVisual(objectData.ObjectId);
            PlayTreasureChestVfx(objectData, TreasureChestOpenVfxCue);
            PlayTreasureChestVfx(objectData, TreasureChestRewardVfxCue);
            host.RequestAudioCue(AudioCueIds.RewardChestOpen, $"reward:chest:{objectData.ObjectId}");

            if (isRelic)
            {
                var name = PlayerPermanentItemCatalog.TryGet(outcome.RelicId, out var definition)
                    ? definition.DisplayName
                    : outcome.RelicId;
                host.LastInputMessage = $"유물 획득: {name}";
            }
            else if (isItem)
            {
                var name = ConsumableItemCatalog.TryGet(outcome.ItemId, out var itemDefinition)
                    ? itemDefinition.DisplayName
                    : outcome.ItemId;
                host.LastInputMessage = $"아이템 획득: {name} (가방 {host.State.PlayerInventory.Bag.UsedSlotCount}/{host.State.GetEffectiveBagSlotLimit()})";
            }
            else
            {
                host.LastInputMessage = $"재화 {outcome.Amount} 획득 (보유 {host.State.PlayerInventory.Wallet.Balance})";
            }

            host.RefreshHudOnly();
        }

        // ── P4: 몬스터 처치 추가 보상 (DEC-2026-08-31-02 — D-2 개정) ───────────────────

        /// <summary>이 처치에 얹힌 것. 카드 3택이 해소되는 순간 함께 지급되고 비워진다.</summary>
        private KillDropOutcome pendingKillDrop;

        private KillDropRates killDropRates;

        /// <summary>
        /// 확률표는 <c>kill_drop_rates.csv</c>가 정본이다. 카드 등급 분포·뽑기 가중치와 같은 관례로
        /// 전투당 한 번 로드해 캐시하고, TextAsset 미할당 시 코드 폴백으로 떨어진다.
        /// </summary>
        private KillDropRates KillDropRates
        {
            get
            {
                if (killDropRates == null)
                {
                    killDropRates = host.CatalogSource != null && host.CatalogSource.HasKillDropRates
                        ? host.CatalogSource.CreateKillDropRates()
                        : Runtime.KillDropRates.Default;
                }

                return killDropRates;
            }
        }

        /// <summary>
        /// 급별 확률로 추가 보상을 굴린다. <b>새 규칙을 만들지 않는다</b> — 유물 후보는 뽑기와
        /// 같은 <see cref="GachaRewardRoller.BuildRelicPool"/>(D-9 · 보유분 제외)이 걸러 주고,
        /// 소모품 풀은 잡화점·뽑기가 쓰는 것과 같은 12종 균등 풀이다(N1).
        /// </summary>
        private KillDropOutcome RollKillDrop(string spawnRole)
        {
            if (host.State == null)
            {
                return default;
            }

            return KillDropRoller.Roll(
                Runtime.KillDropRates.TierFromSpawnRole(spawnRole),
                KillDropRates,
                BuildGachaItemPool(),
                GachaRewardRoller.BuildRelicPool(PlayerPermanentItemCatalog.Definitions, host.State.PlayerInventory?.RelicsAndCurses),
                RewardRandom);
        }

        /// <summary>이 처치의 전리품 목록. 「부적 추가」는 언제나 마지막 줄로 선다.</summary>
        private List<LootRewardRow> pendingLootRows = new List<LootRewardRow>();

        /// <summary>부적 3택이 전리품 목록에서 열렸는가 — 상자(이벤트 오브젝트) 경로와 갈라야 한다.</summary>
        private bool cardRewardOpenedFromLoot;

        private LootRewardPopupView lootRewardPopupView;
        // Loot list panel while it is up (the reward step opens this first; the card 3-pick comes after).
        public RectTransform LootPanelRect => lootRewardPopupView != null ? lootRewardPopupView.PanelRect : null;

        internal bool IsLootPopupOpen() => lootRewardPopupView != null && lootRewardPopupView.IsOpen;

        /// <summary>
        /// 떨어진 것 + 언제나 서는 「부적 추가」로 목록을 짓는다. 아이콘은 사이드바 칩·잡화점
        /// 타일과 <b>같은 통로</b>(<c>RuntimeUiAssetCatalog.LoadItemIcon</c>)에서 온다.
        /// </summary>
        private List<LootRewardRow> BuildLootRows(KillDropOutcome drop, int restoredMoney = 0)
        {
            var rows = new List<LootRewardRow>(4);

            if (drop.HasMoney)
            {
                rows.Add(new LootRewardRow(
                    LootRewardRowKind.Money, "money", string.Empty, CoinRewardSprite, drop.MoneyAmount));
            }

            // 🔴 되돌려받은 엽전은 <b>처치 보상과 별도 줄</b>이다(2026-09-05 사용자 요구). 종전에는
            // 규칙층이 조용히 지갑에 넣고 끝나 「돌려받았다」는 사실이 화면에 아무 흔적도 남기지 않았다.
            // 지갑에는 이미 들어간 값이라 이 줄은 <b>알림</b>이다 — 눌러도 돈이 두 번 들어오지 않는다.
            if (restoredMoney > 0)
            {
                rows.Add(new LootRewardRow(
                    LootRewardRowKind.MoneyReturned, "money-returned", "(반환)", CoinRewardSprite, restoredMoney));
            }

            if (drop.HasItem && ConsumableItemCatalog.TryGet(drop.ItemId, out var item))
            {
                rows.Add(new LootRewardRow(
                    LootRewardRowKind.Item, item.Id, item.DisplayName,
                    SeoulPlayup.Cards.Unity.RuntimeUiAssetCatalog.LoadItemIcon(item.IconId)));
            }

            if (drop.HasRelic && PlayerPermanentItemCatalog.TryGet(drop.RelicId, out var relic))
            {
                rows.Add(new LootRewardRow(
                    LootRewardRowKind.Relic, relic.Id, relic.DisplayName,
                    SeoulPlayup.Cards.Unity.RuntimeUiAssetCatalog.LoadItemIcon(relic.IconId)));
            }

            // 🔑 부적은 떨어지는 것이 아니라 처치의 기본 보상이라 <b>언제나</b> 선다.
            rows.Add(new LootRewardRow(LootRewardRowKind.Card, "card", "부적 추가"));
            return rows;
        }

        private Sprite CoinRewardSprite
        {
            get
            {
                var catalog = SeoulPlayup.Cards.Unity.RuntimeUiAssetCatalog.LoadDefault();
                return catalog != null ? catalog.CoinLightSprite : null;
            }
        }

        private void ShowLootPopup()
        {
            var layers = FindGameplayLayersRoot();
            lootRewardPopupView = lootRewardPopupView != null
                ? lootRewardPopupView
                : LootRewardPopupView.FindOrCreate(layers);

            if (lootRewardPopupView == null)
            {
                // 전리품 UI를 못 세우면 보상이 통째로 사라지는 대신 종전 경로(부적 3택)로 물러난다.
                Debug.LogWarning("[Reward] 전리품 목록 뷰를 세우지 못했다 — 부적 3택으로 폴백한다.", host.LogContext);
                OnLootCardRowChosen();
                return;
            }

            lootRewardPopupView.Show(
                pendingLootRows, TryClaimLootRow, OnLootCardRowChosen, OnLootSkipped, ResolveLootFlyTarget);
            host.RequestAudioCue(AudioCueIds.RewardLootAppear, $"loot:appear:{pendingLootRows.Count}");
        }

        /// <summary>
        /// 줄 하나를 실제로 지급한다. <b>지급 규칙은 기존 것을 재사용한다</b>: 가방 만원이면
        /// 돈 +15(CR-10) · 유물은 지급 관문 <c>TryGrantPermanentItem</c>(RC-5)을 지난다.
        /// </summary>
        private bool TryClaimLootRow(LootRewardRow row)
        {
            if (host.State == null)
            {
                return false;
            }

            switch (row.Kind)
            {
                case LootRewardRowKind.Money:
                    host.State.PlayerInventory?.Wallet?.Add(row.Amount);
                    host.LastInputMessage = $"재화 {row.Amount} 획득 (보유 {host.State.PlayerInventory?.Wallet?.Balance ?? 0})";
                    break;

                case LootRewardRowKind.MoneyReturned:
                    // 지갑에는 처치 시점에 이미 들어갔다 — 여기서 또 더하면 두 배가 된다.
                    host.LastInputMessage = $"빼앗겼던 엽전 {row.Amount} 반환 (보유 {host.State.PlayerInventory?.Wallet?.Balance ?? 0})";
                    break;

                case LootRewardRowKind.Item:
                    // 🔴 2026-09-05 사용자 확정: 가방이 꽉 찼으면 <b>거부한다</b>. 종전에는 돈으로 바꿔
                    // 주면서 줄까지 목록에서 없앴다 — 플레이어가 원한 물건은 사라지고 대신 돈이 들어와
                    // "왜 안 들어왔지"가 됐다. 이제 줄은 목록에 남아, 가방을 비우고 다시 누를 수 있다.
                    if (!host.State.TryAddBagItem(row.Id))
                    {
                        host.LastInputMessage = "가방이 꽉 찼습니다!";
                        host.RefreshHudOnly();
                        return false;
                    }

                    host.LastInputMessage = $"아이템 획득: {row.Label}";
                    break;

                case LootRewardRowKind.Relic:
                    if (!host.State.TryGrantPermanentItem(row.Id, out var reason))
                    {
                        host.LastInputMessage = string.IsNullOrWhiteSpace(reason) ? "유물을 받을 수 없습니다." : reason;
                        return false;
                    }

                    host.LastInputMessage = $"유물 획득: {row.Label}";
                    break;

                default:
                    return false;
            }

            host.RequestAudioCue(ResolveLootClaimCueId(row.Kind), $"loot:{row.Kind}:{row.Id}");
            pendingLootRows.RemoveAll(r => r.Kind == row.Kind && r.Id == row.Id);
            host.RefreshHudOnly();
            return true;
        }

        /// <summary>
        /// 전리품 줄 지급음(2026-09 발주 B). 엽전(반환 포함)은 money.gain, 유물은 reward.relic.acquire,
        /// 소모품·부적은 종전 카드 획득음 그대로 — 그 둘은 전용 클립을 받지 않았다.
        /// </summary>
        internal static string ResolveLootClaimCueId(LootRewardRowKind kind)
        {
            switch (kind)
            {
                case LootRewardRowKind.Money:
                case LootRewardRowKind.MoneyReturned:
                    return AudioCueIds.MoneyGain;
                case LootRewardRowKind.Relic:
                    return AudioCueIds.RewardRelicAcquire;
                default:
                    return AudioCueIds.RewardCardAcquire;
            }
        }

        /// <summary>
        /// 보상 기물(보상뽑기·유물/엽전/소모품 뽑기 결과)에서 고른 줄의 획득음. 뽑기 결과가 유물이면
        /// 유물 획득음, 엽전이면 엽전음, 그 밖(부적·소모품)은 카드 획득음이다.
        /// </summary>
        internal static string ResolveRewardOfferCueId(RewardEventObjectOfferKind kind)
        {
            switch (kind)
            {
                case RewardEventObjectOfferKind.Money:
                    return AudioCueIds.MoneyGain;
                case RewardEventObjectOfferKind.PermanentItem:
                    return AudioCueIds.RewardRelicAcquire;
                default:
                    return AudioCueIds.RewardCardAcquire;
            }
        }

        /// <summary>전리품이 빨려들어갈 사이드바 칸. 배선이 없으면 연출만 생략된다.</summary>
        private RectTransform ResolveLootFlyTarget(LootRewardRowKind kind)
        {
            var panelKey = kind == LootRewardRowKind.Money ? "currency"
                : kind == LootRewardRowKind.Item ? "bag"
                : kind == LootRewardRowKind.Relic ? "relic_curse"
                : null;
            if (panelKey == null)
            {
                return null;
            }

            var sidebar = FindGameplayLayersRoot();
            if (sidebar == null)
            {
                return null;
            }

            var wanted = "Sidebar Button " + panelKey;
            return sidebar.GetComponentsInChildren<RectTransform>(includeInactive: true)
                .FirstOrDefault(rect => rect.name == wanted);
        }

        /// <summary>
        /// 「부적 추가」 — 3택을 연다. 목록은 잠시 물러났다가 선택이 끝나면 돌아온다.
        ///
        /// <para>🔑 여는 소리는 <b>보상뽑기 상자와 같은 큐</b>다(2026-09-01 사용자 확정):
        /// 두 화면이 같은 「부적 3택」으로 이어지므로 소리도 하나여야 같은 물건으로 읽힌다.
        /// 줄을 <b>지급</b>할 때 나는 <c>RewardCardAcquire</c>와는 다른 자리다 — 이건 「열린다」는 소리다.</para>
        /// </summary>
        private void OnLootCardRowChosen()
        {
            // Tutorial: the loot-intro step waits on this ("전리품 화면 설명 → 부적 추가 → 3택").
            host.NotifyTutorialCombatEvent(TutorialRewardCardRowEventId);
            host.RequestAudioCue(AudioCueIds.RewardChestOpen, "loot:card");
            pendingRewardOffers = GenerateCardRewardOffers();
            pendingRewardRerollUsed = false;
            cardRewardOpenedFromLoot = true;
            lootRewardPopupView?.Hide();
            host.EnsureCardRewardPopupView();
            host.CardRewardPresentation.Show(
                pendingRewardOffers, OnCardRewardSelected, OnCardRewardSkipped,
                OnCardRewardHovered, OnCardRewardRerollRequested, !pendingRewardRerollUsed);
        }

        /// <summary>부적 줄을 지우고 목록으로 돌아간다. 남은 줄이 없으면 이 처치는 끝난다.</summary>
        private void ResolveCardRowAndReturnToLoot()
        {
            cardRewardOpenedFromLoot = false;
            pendingRewardOffers = null;
            pendingRewardRerollUsed = false;
            pendingLootRows.RemoveAll(r => r.Kind == LootRewardRowKind.Card);
            host.CardRewardPopupView?.Hide();

            if (pendingLootRows.Count > 0)
            {
                // 🔴 튜토리얼 안에서는 목록으로 <b>돌아가지 않는다</b>(2026-09-06 실플레이). 부적 3택에서
                //    고르는 순간 대본은 다음 스텝(손패의 새 부적 → 턴 종료)으로 넘어가고, 스포트라이트는
                //    그 초점 밖의 클릭을 전부 막는다. 여기서 목록을 다시 세우면 엽전 줄은 눌리지 않고
                //    목록의 뒷배경은 턴 종료 버튼을 덮어 진행이 영영 막혔다(돈을 먼저 누른 순서만 살았다).
                //    남은 줄은 지급 규칙 그대로 <b>자동 지급</b>하고 이 처치를 닫는다. 대본이 아직 3택을
                //    기다리는 중(부적을 손패에 못 넣은 경우)이면 종전대로 목록으로 돌아간다.
                if (IsTutorialPastRewardSelection())
                {
                    ClaimRemainingLootRows();
                    FinishKillReward();
                    return;
                }

                ShowLootPopup();
                host.RefreshHudOnly();
                return;
            }

            FinishKillReward();
        }

        /// <summary>튜토리얼이 켜져 있고, 현재 스텝이 더는 부적 3택을 기다리지 않는가.</summary>
        private bool IsTutorialPastRewardSelection()
        {
            var director = host.ResolveTutorialDirector();
            return director != null && director.IsActive
                && director.CurrentStep?.AdvanceMode != TutorialAdvanceMode.RewardSelected;
        }

        /// <summary>
        /// 목록에 남은 돈·소모품·유물 줄을 클릭한 것과 같은 경로(<see cref="TryClaimLootRow"/>)로 지급한다.
        /// 지급이 거부된 줄(가방 만원 등)은 남지만, 호출부가 곧 <see cref="FinishKillReward"/>로 닫는다 —
        /// 튜토리얼에서 목록을 다시 세워 진행을 막는 것보다 낫다.
        /// </summary>
        private void ClaimRemainingLootRows()
        {
            foreach (var row in pendingLootRows.ToArray())
            {
                if (row.Kind != LootRewardRowKind.Card && !TryClaimLootRow(row))
                {
                    Debug.LogWarning($"[Reward] 튜토리얼 자동 지급 거부: {row.Kind} {row.Id} — 이 줄은 버려진다.", host.LogContext);
                }
            }
        }

        /// <summary>
        /// 🔴 목록의 「넘기기」는 <b>남은 전리품을 버린다</b>(사용자 확정 2026-08-31). 무엇이 남았는지
        /// 눈에 보이는 상태에서 누르는 것이라 함정이 아니고, 그래야 「전부 그만두기」로 일관된다.
        /// </summary>
        public const string TutorialRewardCardRowEventId = "reward.cardrow";

        private void OnLootSkipped()
        {
            // Mirrors OnCardRewardSkipped: while a tutorial step is waiting on the loot/reward flow, skipping
            // would strand it, so refuse with the usual feedback.
            var director = host.ResolveTutorialDirector();
            if (director != null && director.IsActive
                && (director.CurrentStep?.AdvanceMode == TutorialAdvanceMode.RewardSelected
                    || director.CurrentStep?.AdvanceMode == TutorialAdvanceMode.CombatEvent))
            {
                host.ShowTutorialBlockedFeedback("\uD29C\uD1A0\uB9AC\uC5BC \uC9C4\uD589 \uC911\uC5D0\uB294 \uC804\uB9AC\uD488\uC744 \uB118\uAE38 \uC218 \uC5C6\uC2B5\uB2C8\uB2E4.");
                return;
            }

            host.LastInputMessage = pendingLootRows.Any(r => r.Kind != LootRewardRowKind.Card)
                ? "전리품을 넘겼습니다."
                : "Card reward skipped.";
            host.RequestAudioCue(AudioCueIds.RewardSkip, "reward:skip:loot");
            FinishKillReward();
        }

        /// <summary>이 처치의 보상 처리를 닫는다 — 같은 몬스터로 두 번 받지 않게 래치를 건다.</summary>
        private void FinishKillReward()
        {
            MarkPendingMonsterRewarded();

            pendingRewardMonsterId = null;
            pendingRewardOffers = null;
            pendingRewardRerollUsed = false;
            cardRewardOpenedFromLoot = false;
            pendingKillDrop = default;
            pendingLootRows.Clear();
            lootRewardPopupView?.Hide();
            host.CardRewardPopupView?.Hide();
            host.RefreshHudOnly();
        }

        /// <summary>가방 만원 시 뽑기 Item 결과의 돈 폴백 금액(T4-3 확정 — 교체 선택 UI는 후속).</summary>
        private const int GachaFullBagFallbackMoney = 15;

        // 테스트가 갈아끼우는 난수원. 프로덕션에서는 null이라 UnityRewardRandom이 쓰인다.
        private IRewardRandom rewardRandomOverride;

        /// <summary>테스트 이음새(호스트 <c>ConfigureRewardRandomForTests</c>가 위임). null = 프로덕션 난수.</summary>
        internal void ConfigureRewardRandom(IRewardRandom random) => rewardRandomOverride = random;

        private IRewardRandom RewardRandom => rewardRandomOverride ?? UnityRewardRandom.Instance;

        /// <summary>Reward draws use Unity's global random; the roll itself is pure and lives in Combat.Runtime.</summary>
        private sealed class UnityRewardRandom : IRewardRandom
        {
            public static readonly UnityRewardRandom Instance = new UnityRewardRandom();

            public int Next(int maxExclusive) => Random.Range(0, maxExclusive);
        }

        private IReadOnlyList<CardRewardOffer> GenerateCardRewardOffers()
        {
            return GenerateCardRewardOffers(pendingRewardIsElite);
        }

        private IReadOnlyList<CardRewardOffer> GenerateCardRewardOffers(bool eliteOnly)
        {
            // CR-1~CR-5 live in CardRewardRoller (pure, testable). This method only maps the presentation
            // pool to (id, rarity) candidates and maps the chosen ids back to offers.
            var pool = BuildRewardCardPoolFromCurrentCatalog();
            if (pool.Count == 0)
            {
                return System.Array.Empty<CardRewardOffer>();
            }

            var candidates = new CardRewardCandidate[pool.Count];
            for (var i = 0; i < pool.Count; i++)
            {
                candidates[i] = new CardRewardCandidate(pool[i].CardId, pool[i].Rarity);
            }

            var chosenIds = CardRewardRoller.SelectRewardCardIds(
                candidates,
                CardRewardWeights,
                eliteOnly,
                RewardRandom);

            var offersById = new Dictionary<string, CardRewardOffer>(System.StringComparer.Ordinal);
            foreach (var offer in pool)
            {
                if (!offersById.ContainsKey(offer.CardId))
                {
                    offersById[offer.CardId] = offer;
                }
            }

            var result = new List<CardRewardOffer>(chosenIds.Count);
            foreach (var cardId in chosenIds)
            {
                if (offersById.TryGetValue(cardId, out var offer))
                {
                    result.Add(offer);
                }
            }

            return result;
        }

        private IReadOnlyList<CardRewardOffer> BuildRewardCardPoolFromCurrentCatalog()
        {
            if (host.State?.CardCatalog == null)
            {
                return System.Array.Empty<CardRewardOffer>();
            }

            var pool = host.State.CardCatalog
                .GetVisibleCatalogEntries()
                .Where(entry => entry.IncludeInGameplayDecks)
                .Select(entry => new CardRewardOffer(
                    entry.Id,
                    entry.DisplayName,
                    FormatRewardCardTypeLabel(entry.ActionType),
                    DescribeRewardCatalogEntry(entry, host.State.CardCatalog.SourceId),
                    entry.Cost,
                    entry.ActionType,
                    entry.Range,
                    entry.TargetMode,
                    entry.PlayMode,
                    entry.AreaRadius,
                    entry.ChoiceOptions,
                    entry.ChoiceOptionTexts,
                    entry.Rarity))
                .ToArray();

            return pool;
        }

        private static string FormatRewardCardTypeLabel(CardEffectType effectType)
        {
            switch (effectType)
            {
                case CardEffectType.Move:
                    return "\uC774\uB3D9";
                case CardEffectType.Attack:
                    return "\uACF5\uACA9";
                case CardEffectType.Defend:
                    return "\uBC29\uC5B4";
                case CardEffectType.Scout:
                case CardEffectType.Investigate:
                    return "\uC815\uCC30";
                case CardEffectType.FieldObject:
                    return "\uD544\uB4DC";
                case CardEffectType.Buff:
                    return "\uBC84\uD504";
                default:
                    return "\uCE74\uB4DC";
            }
        }

        internal static string DescribeRewardCatalogEntry(CardCatalogEntry entry, string catalogSourceId)
        {
            if (entry == null)
            {
                return string.Empty;
            }

            return CombatState.Describe(entry.ToCardDefinition(catalogSourceId));
        }

        internal bool IsRewardPopupOpen()
        {
            // 상점도 같은 게이트를 쓴다: 열려 있는 동안 보드 입력·단축키가 막혀야 하는 모달이라는
            // 점에서 보상 팝업과 동류다.
            return pendingRewardEventObject != null ||
                   pendingRewardMonsterId != null ||
                   (host.CardRewardPopupView != null && host.CardRewardPresentation.gameObject.activeInHierarchy) ||
                   IsShopPopupOpen() ||
                   IsServicePopupOpen();
        }

        // ── 상점(잡화점) — 옛 MapCombatController.Shop.cs ──────────────────────────────
        // 상점(잡화점 — 표시명 2026-09-04 개명(야시장→잡화점), 내부 식별자 Shop/popup_shop 유지) 트리거·거래 배선.
        // 상점은 1회 방문 소비다: 밟으면 재고를 추첨해 열고, 떠나는 순간(구매 여부와 무관하게) 오브젝트를 소비한다 —
        // 상자 CR-6의 "건너뛰기도 소비"와 같은 원칙이다. 재고는 팝업이 열려 있는 동안만 살아 있으므로 세이브에 싣지 않는다;
        // 열어 둔 채 중단→재개하면 소비 전이라 다시 밟아 새 재고로 열 수 있다(허용된 미세 이득).
        private ShopPopupView shopPopupView;
        private HexMapObjectData pendingShopObjectData;
        private bool hasPendingShopObjectData;
        private ShopInventory pendingShopInventory;

        /// <summary>
        /// 뷰가 들고 있는 <b>바로 그</b> 진열 모델들. N3(단골 도장 즉시 재계산)가 값을 다시 써 넣을
        /// 대상이라 컨트롤러도 참조를 쥐고 있어야 한다 — 새로 만들어 넘기면 판매 완료 표시가 날아간다.
        /// </summary>
        private IReadOnlyList<ShopOfferSlotModel> pendingShopSlots = System.Array.Empty<ShopOfferSlotModel>();

        /// <summary>이 방문의 진열가를 만든 할인율. 구매 뒤 이 값이 바뀌면 재계산이 필요하다는 뜻이다.</summary>
        private int pendingShopDiscountPercent;

        /// <summary>재계산이 카드 등급을 다시 물어볼 때 쓰는 후보 목록(추첨은 다시 하지 않는다).</summary>
        private CardRewardCandidate[] pendingShopCardCandidates = System.Array.Empty<CardRewardCandidate>();

        // 상점 가격표는 Assets/Data/Combat/Reward/Source/shop_prices.csv가 정본이다.
        // 등급 분포와 같은 관례로 전투당 한 번 로드해 캐시하고, TextAsset 미할당 시 코드 폴백.
        private ShopPrices shopPrices;

        private ShopPrices ShopPrices
        {
            get
            {
                if (shopPrices == null)
                {
                    shopPrices = host.CatalogSource != null && host.CatalogSource.HasShopPrices
                        ? host.CatalogSource.CreateShopPrices()
                        : Runtime.ShopPrices.Default;
                }

                return shopPrices;
            }
        }

        private bool IsShopPopupOpen()
        {
            return hasPendingShopObjectData || (shopPopupView != null && shopPopupView.IsOpen);
        }

        internal bool TryTriggerShopAtPlayerCoord()
        {
            if (host.State == null || host.LoadedMap == null)
            {
                return false;
            }

            var objectData = host.LoadedMap.GetObjectsAt(host.State.PlayerCoord)
                .FirstOrDefault(candidate =>
                    candidate.Interactable &&
                    candidate.IsShop &&
                    !host.State.ClaimedEventObjectIds.Contains(candidate.ObjectId));

            if (!objectData.IsConfigured)
            {
                return false;
            }

            OpenShop(objectData);
            return true;
        }

        private void OpenShop(HexMapObjectData objectData)
        {
            if (host.State == null || IsShopPopupOpen())
            {
                return;
            }

            if (host.CardRewardPopupView != null && host.CardRewardPresentation.gameObject.activeSelf)
            {
                return;
            }

            var layers = FindGameplayLayersRoot();
            shopPopupView = shopPopupView != null ? shopPopupView : ShopPopupView.FindOrCreate(layers);
            if (shopPopupView == null)
            {
                host.LastInputMessage = "상점 UI를 열 수 없습니다. 다시 시도해 주세요.";
                host.RefreshHudOnly();
                return;
            }

            // 재고는 진입 시 한 번 추첨해 고정된다(D-12). 유물은 보유분을 뺀 풀에서만 뽑고, 카드는
            // 가격표에 있는 등급만 후보가 되므로 진열된 것은 항상 구매 가능하다.
            var pool = BuildRewardCardPoolFromCurrentCatalog();
            var candidates = new CardRewardCandidate[pool.Count];
            for (var i = 0; i < pool.Count; i++)
            {
                candidates[i] = new CardRewardCandidate(pool[i].CardId, pool[i].Rarity);
            }

            pendingShopDiscountPercent = host.State.GetRelicEffectTotal(PlayerPermanentItemEffectKind.ShopDiscountPercent);
            pendingShopCardCandidates = candidates;
            pendingShopInventory = ShopInventoryRoller.Roll(
                candidates,
                CardRewardWeights,
                // 단골 도장(T2 페이즈 B): 추첨 전에 가격표를 깎아 진열가와 결제가가 갈라질 수 없게 한다.
                ShopPrices.WithDiscountPercent(pendingShopDiscountPercent),
                GachaRewardRoller.BuildRelicPool(PlayerPermanentItemCatalog.Definitions, host.State.PlayerInventory?.RelicsAndCurses),
                RewardRandom,
                BuildGachaItemPool());
            pendingShopObjectData = objectData;
            hasPendingShopObjectData = true;
            pendingShopSlots = BuildShopSlotModels(pendingShopInventory, pool);

            shopPopupView.Show(
                pendingShopSlots,
                () => host.State?.PlayerInventory?.Wallet?.Balance ?? 0,
                OnShopPurchaseRequested,
                BuildShopRemovalCandidates,
                OnShopRemovalPicked,
                CloseShopAndConsume,
                // ⑩ T2: 진열 카드의 실물 프레임용 스냅샷 통로(카탈로그 entry → 덱 목록 빌더).
                cardId => host.State != null && host.State.TryCreateCatalogCardSnapshot(cardId, out var snapshot)
                    ? snapshot
                    : (CombatCardSnapshot?)null);
            host.RequestAudioCue(AudioCueIds.RewardPopupAppear, $"shop:{objectData.ObjectId}");
            host.LastInputMessage = "잡화점에 들어왔습니다.";
            host.RefreshHudOnly();
        }

        private IReadOnlyList<ShopOfferSlotModel> BuildShopSlotModels(
            ShopInventory inventory,
            IReadOnlyList<CardRewardOffer> pool)
        {
            var offersById = new Dictionary<string, CardRewardOffer>(System.StringComparer.Ordinal);
            foreach (var offer in pool)
            {
                if (!offersById.ContainsKey(offer.CardId))
                {
                    offersById[offer.CardId] = offer;
                }
            }

            var slots = new List<ShopOfferSlotModel>();
            foreach (var stock in inventory.Cards)
            {
                var hasOffer = offersById.TryGetValue(stock.ItemId, out var offer);
                slots.Add(new ShopOfferSlotModel(
                    ShopItemKind.Card,
                    stock.ItemId,
                    hasOffer ? offer.DisplayName : stock.ItemId,
                    hasOffer ? $"{offer.TypeLabel} · {FormatShopRarityLabel(offer.Rarity)}" : string.Empty,
                    stock.Price));
            }

            foreach (var stock in inventory.Relics)
            {
                var title = stock.ItemId;
                var detail = string.Empty;
                var iconId = string.Empty;
                if (PlayerPermanentItemCatalog.TryGet(stock.ItemId, out var definition))
                {
                    title = definition.DisplayName;
                    detail = definition.Description;
                    // 🔴 등급(definition.Rarity)은 여기서 절대 문안에 섞지 않는다(DEC-2026-08-31-01 Q1).
                    //    등급이 하는 일은 기준가를 고르는 것뿐이고 화면에는 값으로만 드러난다.
                    iconId = definition.IconId;
                }

                slots.Add(new ShopOfferSlotModel(ShopItemKind.Relic, stock.ItemId, title, detail, stock.Price, iconId));
            }

            foreach (var stock in inventory.Items)
            {
                var title = stock.ItemId;
                var detail = string.Empty;
                var iconId = string.Empty;
                if (ConsumableItemCatalog.TryGet(stock.ItemId, out var itemDefinition))
                {
                    title = itemDefinition.DisplayName;
                    detail = itemDefinition.Description;
                    iconId = itemDefinition.IconId;
                }

                slots.Add(new ShopOfferSlotModel(ShopItemKind.Item, stock.ItemId, title, detail, stock.Price, iconId));
            }

            if (inventory.CardRemovalOffered)
            {
                slots.Add(new ShopOfferSlotModel(
                    ShopItemKind.CardRemoval,
                    "card-removal",
                    "카드 제거",
                    "덱에서 카드 한 장을 영구히 제거합니다 (1회)",
                    inventory.CardRemovalPrice));
            }

            return slots;
        }

        /// <summary>
        /// 할인 유물을 방금 샀는지 보고, 그렇다면 <b>남은 재고의 값을 그 자리에서 다시 매긴다</b>
        /// (DEC-2026-08-31-01 N3). 뽑기는 다시 돌리지 않는다 — 같은 물건이 같은 자리에 남는다.
        /// <para>
        /// 🔴 진열가와 결제가는 <b>같은 표</b>에서 나와야 한다. 그래서 재고를 먼저 재계산하고
        /// 그 결과를 진열 모델에 옮겨 적는다 — 화면 숫자만 깎으면 결제는 옛 값으로 돌아
        /// "보이는 값과 빠지는 값이 다른" 고장이 된다.
        /// </para>
        /// </summary>
        /// <returns>값이 실제로 바뀌었으면 <see langword="true"/>.</returns>
        private bool TryRepriceShopStockForNewDiscount()
        {
            if (host.State == null || pendingShopInventory == null)
            {
                return false;
            }

            var discount = host.State.GetRelicEffectTotal(PlayerPermanentItemEffectKind.ShopDiscountPercent);
            if (discount == pendingShopDiscountPercent)
            {
                return false;
            }

            pendingShopDiscountPercent = discount;
            pendingShopInventory = ShopInventoryRoller.Reprice(
                pendingShopInventory,
                ShopPrices.WithDiscountPercent(discount),
                pendingShopCardCandidates);

            var changed = false;
            foreach (var stock in pendingShopInventory.Cards)
            {
                changed |= ApplyRepricedStock(ShopItemKind.Card, stock);
            }

            foreach (var stock in pendingShopInventory.Relics)
            {
                changed |= ApplyRepricedStock(ShopItemKind.Relic, stock);
            }

            foreach (var stock in pendingShopInventory.Items)
            {
                changed |= ApplyRepricedStock(ShopItemKind.Item, stock);
            }

            if (pendingShopInventory.CardRemovalOffered)
            {
                changed |= ApplyRepricedStock(
                    ShopItemKind.CardRemoval,
                    new ShopStockItem("card-removal", pendingShopInventory.CardRemovalPrice));
            }

            return changed;
        }

        private bool ApplyRepricedStock(ShopItemKind kind, ShopStockItem stock)
        {
            var changed = false;
            foreach (var slot in pendingShopSlots)
            {
                if (slot == null || slot.Kind != kind || !string.Equals(slot.ItemId, stock.ItemId, System.StringComparison.Ordinal))
                {
                    continue;
                }

                if (slot.Price != stock.Price)
                {
                    slot.SetPrice(stock.Price);
                    changed = true;
                }
            }

            return changed;
        }

        private static string FormatShopRarityLabel(CardRarity rarity)
        {
            switch (rarity)
            {
                case CardRarity.Rare:
                    return "희귀";
                case CardRarity.Epic:
                    return "에픽";
                case CardRarity.Legendary:
                    return "전설";
                default:
                    return rarity.ToString();
            }
        }

        private bool OnShopPurchaseRequested(ShopOfferSlotModel slot)
        {
            if (host.State == null || slot == null || slot.Kind == ShopItemKind.CardRemoval)
            {
                return false;
            }

            var ok = slot.Kind == ShopItemKind.Card
                ? host.State.TryPurchaseShopCard(slot.ItemId, slot.Price, out var reason)
                : slot.Kind == ShopItemKind.Item
                    ? host.State.TryPurchaseShopItem(slot.ItemId, slot.Price, out reason)
                    : host.State.TryPurchaseShopRelic(slot.ItemId, slot.Price, out reason);

            if (!ok)
            {
                host.RequestAudioCue(AudioCueIds.ShopDenied, $"shop:denied:{slot.ItemId}");
                shopPopupView?.SetMessage(string.IsNullOrWhiteSpace(reason) ? "구매할 수 없습니다." : reason);
                return false;
            }

            host.RequestAudioCue(AudioCueIds.ShopBuy, $"shop:buy:{slot.ItemId}");

            // N3: 방금 산 것이 단골 도장이면 남은 재고가 <b>이 자리에서</b> 싸진다. 문안이
            // 「상점 전 품목 20% 할인」이므로, 같은 방문에서 효과가 없으면 그 문장이 거짓이 된다.
            var discountApplied = TryRepriceShopStockForNewDiscount();

            shopPopupView?.SetMessage(discountApplied
                ? $"{slot.Title} 구매 완료 — 남은 물건 값이 내렸습니다"
                : $"{slot.Title} 구매 완료");
            host.LastInputMessage = slot.Kind == ShopItemKind.Card
                ? $"카드 구매: {slot.Title} (현재 손패로 들어갑니다)"
                : slot.Kind == ShopItemKind.Item
                    ? $"아이템 구매: {slot.Title} (가방으로 들어갑니다)"
                    : $"유물 구매: {slot.Title}";
            host.RefreshHudOnly();
            return true;
        }

        private IReadOnlyList<ShopRemovalCandidate> BuildShopRemovalCandidates()
        {
            if (host.State == null)
            {
                return System.Array.Empty<ShopRemovalCandidate>();
            }

            // 제거 대상은 덱 전체(손패·뽑을 더미·버림 더미)다. GetDeckListCards가 정확히 그 합집합을
            // 돌려준다(소멸 더미 제외). 키는 SelectionKey(instance id 우선)라 같은 카드 여러 장도
            // 정확히 한 장만 지운다.
            return host.State.GetDeckListCards()
                .Select(snapshot => new ShopRemovalCandidate(
                    snapshot.SelectionKey,
                    $"{snapshot.Name} <size=70%>({FormatShopPileLabel(snapshot.Pile)})</size>",
                    // 실물 프레임으로 그리도록 스냅샷을 함께 넘긴다(#8) — 덱 목록 뷰와 같은 값이다.
                    snapshot))
                .ToArray();
        }

        private static string FormatShopPileLabel(string pile)
        {
            switch (pile)
            {
                case "Movement hand":
                    return "이동 · 손패";
                case "Movement draw":
                    return "이동 · 뽑을 더미";
                case "Movement discard":
                    return "이동 · 버림 더미";
                case "Action hand":
                    return "행동 · 손패";
                case "Action draw":
                    return "행동 · 뽑을 더미";
                case "Action discard":
                    return "행동 · 버림 더미";
                default:
                    return pile;
            }
        }

        private bool OnShopRemovalPicked(ShopRemovalCandidate candidate)
        {
            if (host.State == null || pendingShopInventory == null || !pendingShopInventory.CardRemovalOffered)
            {
                return false;
            }

            if (!host.State.TryPurchaseShopCardRemoval(candidate.Key, pendingShopInventory.CardRemovalPrice, out var reason))
            {
                host.RequestAudioCue(AudioCueIds.ShopDenied, "shop:denied:remove");
                shopPopupView?.SetMessage(string.IsNullOrWhiteSpace(reason) ? "카드를 제거할 수 없습니다." : reason);
                return false;
            }

            host.RequestAudioCue(AudioCueIds.ShopCardRemove, "shop:remove");
            shopPopupView?.SetMessage("카드를 제거했습니다.");
            host.LastInputMessage = "카드 제거 완료.";
            host.RefreshHudOnly();
            return true;
        }

        /// <summary>
        /// 상점을 떠난다 = 오브젝트 소비(1회 방문 소비). 구매를 하나도 안 했어도 소비된다 —
        /// 안 하면 같은 상점을 다시 밟아 재고를 무한 재추첨할 수 있다(상자의 무한 재뽑기와 같은 구멍).
        /// </summary>
        private void CloseShopAndConsume()
        {
            if (hasPendingShopObjectData && host.State != null &&
                host.State.TryConsumeShopObject(pendingShopObjectData.ObjectId, out _))
            {
                host.TileView?.ConsumeMapObjectVisual(pendingShopObjectData.ObjectId);
            }

            hasPendingShopObjectData = false;
            pendingShopObjectData = default;
            pendingShopInventory = null;
            pendingShopSlots = System.Array.Empty<ShopOfferSlotModel>();
            pendingShopCardCandidates = System.Array.Empty<CardRewardCandidate>();
            pendingShopDiscountPercent = 0;
            shopPopupView?.Hide();
            host.LastInputMessage = "잡화점을 떠났습니다.";
            host.RefreshHudOnly();
        }

        private RectTransform FindGameplayLayersRoot()
        {
            var root = GameObject.Find(GameplaySceneContract.GameplayLayerRootName);
            return root != null ? root.transform as RectTransform : null;
        }

        // ── 서비스 오브젝트(캠핑카·공작소) — 옛 MapCombatController.Services.cs ────────
        // 캠핑카·공작소(서비스 오브젝트) 트리거·배선(camper-workshop-plan.md P2·P3). 상점 배선을 템플릿으로 따른다:
        // 밟으면 열리고, 떠나는 순간(이용 여부와 무관하게) 소비된다(D-5, 상점 CR-6과 같은 원칙). 계약 —
        // 캠핑카 = (체력 30% 회복 ∥ 카드 연마) 택1 · 공작소 = (카드 제거 ∥ 카드 연마) 택1.
        // 서비스 하나를 성공적으로 쓰면 그 자리에서 닫고 소비한다(택1). 공작소 제거는 상점의 유료 제거와 달리 무료다 — 택1 자체가 비용이다.
        private ServiceObjectPopupView serviceObjectPopupView;
        private HexMapObjectData pendingServiceObjectData;
        private bool hasPendingServiceObjectData;

        private bool IsServicePopupOpen()
        {
            return hasPendingServiceObjectData || (serviceObjectPopupView != null && serviceObjectPopupView.IsOpen);
        }

        internal bool TryTriggerCamperVanAtPlayerCoord()
        {
            return TryTriggerServiceObjectAtPlayerCoord(candidate => candidate.IsCamperVan);
        }

        internal bool TryTriggerWorkshopAtPlayerCoord()
        {
            // 공작소는 은퇴했다(ServiceObjectAvailability) — 시각 스폰 게이트와 한 쌍이다.
            return ServiceObjectAvailability.WorkshopEnabled
                && TryTriggerServiceObjectAtPlayerCoord(candidate => candidate.IsWorkshop);
        }

        private bool TryTriggerServiceObjectAtPlayerCoord(System.Func<HexMapObjectData, bool> match)
        {
            if (host.State == null || host.LoadedMap == null)
            {
                return false;
            }

            var objectData = host.LoadedMap.GetObjectsAt(host.State.PlayerCoord)
                .FirstOrDefault(candidate =>
                    candidate.Interactable &&
                    match(candidate) &&
                    !host.State.ClaimedEventObjectIds.Contains(candidate.ObjectId));

            if (!objectData.IsConfigured)
            {
                return false;
            }

            OpenServiceObject(objectData);
            return true;
        }

        private void OpenServiceObject(HexMapObjectData objectData)
        {
            if (host.State == null || IsServicePopupOpen() || IsShopPopupOpen())
            {
                return;
            }

            if (host.CardRewardPopupView != null && host.CardRewardPresentation.gameObject.activeSelf)
            {
                return;
            }

            var layers = FindGameplayLayersRoot();
            serviceObjectPopupView = serviceObjectPopupView != null
                ? serviceObjectPopupView
                : ServiceObjectPopupView.FindOrCreate(layers);
            if (serviceObjectPopupView == null)
            {
                host.LastInputMessage = "서비스 UI를 열 수 없습니다. 다시 시도해 주세요.";
                host.RefreshHudOnly();
                return;
            }

            pendingServiceObjectData = objectData;
            hasPendingServiceObjectData = true;

            var camperVan = objectData.IsCamperVan;
            var refinable = host.State.HasRefinableCard();
            var refineOption = new ServiceObjectOptionModel(
                "refine",
                "카드 연마",
                "카드 한 장을 골라 한 단계 연마합니다 (카드당 1회)",
                ServiceOptionFlow.CardPickCompare,
                enabled: refinable,
                disabledDetail: "연마할 수 있는 카드가 없습니다");
            var firstOption = camperVan
                ? new ServiceObjectOptionModel(
                    "heal",
                    "체력 회복",
                    // 🔑 2026-09-01 #11: 「+29」만으로는 <b>쓸지 말지</b>를 정할 수 없다 — 지금 체력이
                    //    얼마인지가 그 판단의 절반이고, 캠핑카 화면은 사이드바를 덮는다.
                    //    만피면 회복량 대신 그 사실을 말한다("+0"은 정보가 아니라 잡음이다).
                    BuildCamperHealDetail(),
                    ServiceOptionFlow.Instant)
                : new ServiceObjectOptionModel(
                    "remove",
                    "카드 제거",
                    "덱에서 카드 한 장을 영구히 제거합니다 (무료)",
                    ServiceOptionFlow.CardPick);

            serviceObjectPopupView.Show(
                camperVan ? ServiceObjectScreen.CamperVan : ServiceObjectScreen.Workshop,
                camperVan ? ObjectInfoTooltipContent.CamperVanTitle : ObjectInfoTooltipContent.WorkshopTitle,
                "서비스 하나를 이용하면 방문이 끝납니다. 떠나면 사라집니다.",
                new[] { firstOption, refineOption },
                OnServiceInstantRequested,
                BuildServiceCardCandidates,
                BuildServiceComparePair,
                OnServiceCardConfirmed,
                CloseServiceObjectAndConsume);
            host.RequestAudioCue(AudioCueIds.RewardPopupAppear, $"service:{objectData.ObjectId}");
            host.LastInputMessage = camperVan ? "캠핑카에 들렀습니다." : "공작소에 들어왔습니다.";
            host.RefreshHudOnly();
        }

        /// <summary>
        /// 캠핑카 회복 선택지의 설명 한 줄. 현재 체력을 함께 말한다(2026-09-01 #11) — 회복량만으로는
        /// 「지금 쓸 것인가」를 정할 수 없고, 이 화면이 사이드바를 덮어 체력을 볼 길이 없다.
        /// </summary>
        private string BuildCamperHealDetail()
        {
            var player = host.State.Player;
            return FormatCamperHealDetail(
                player != null ? player.Hp : 0,
                player != null ? player.MaxHp : 0,
                host.State.GetCamperHealAmount());
        }

        /// <summary>
        /// 순수 문안 — 씬 없이 잴 수 있게 값만 받는다.
        ///
        /// <para>🔑 <b>두 줄</b>이다(2026-09-01 사용자 지정): 첫 줄이 「지금 내 체력」, 둘째 줄이
        /// 「누르면 무슨 일이 생기나」. 한 줄로 이어 붙이면 폭에 밀려 아무 데서나 접히고
        /// 「(+24)」만 다음 줄에 홀로 남는다 — 줄바꿈을 <b>뜻</b>으로 정해 두면 그 사고가 안 난다.
        /// 줄 나눔은 표현층이 <c>\n</c>으로 읽어 갈라 세운다.</para>
        /// </summary>
        public static string FormatCamperHealDetail(int hp, int maxHp, int healAmount)
        {
            var effect = hp >= maxHp
                ? "이미 가득 찼습니다"
                : $"최대 체력의 30%를 회복합니다 (+{healAmount})";
            return $"체력 {hp}/{maxHp}\n{effect}";
        }

        private bool OnServiceInstantRequested(ServiceObjectOptionModel option)
        {
            if (host.State == null || option == null || option.Id != "heal")
            {
                return false;
            }

            if (!host.State.TryUseCamperHeal(out var healed, out var reason))
            {
                serviceObjectPopupView?.SetMessage(string.IsNullOrWhiteSpace(reason) ? "회복할 수 없습니다." : reason);
                return false;
            }

            host.RequestAudioCue(AudioCueIds.ServiceRest, "service:heal");
            host.LastInputMessage = healed > 0 ? $"체력을 {healed} 회복했습니다." : "이미 체력이 가득합니다.";
            CloseServiceObjectAndConsume();
            return true;
        }

        private System.Collections.Generic.IReadOnlyList<ServiceCardCandidate> BuildServiceCardCandidates(
            ServiceObjectOptionModel option)
        {
            if (host.State == null || option == null)
            {
                return System.Array.Empty<ServiceCardCandidate>();
            }

            // 제거 후보 = 덱 전체(상점 제거와 같은 합집합), 연마 후보 = CanRefine 통과 카드만.
            return host.State.GetDeckListCards()
                .Where(snapshot => option.Id != "refine" || host.State.CanRefineCard(snapshot.SelectionKey))
                // 🔑 스냅샷을 함께 넘긴다 — 고르는 화면이 이름이 아니라 <b>실물 카드</b>를 그린다.
                .Select(snapshot => new ServiceCardCandidate(
                    snapshot.SelectionKey,
                    $"{snapshot.Name} <size=70%>({FormatShopPileLabel(snapshot.Pile)})</size>",
                    snapshot))
                .ToArray();
        }

        private ServiceCardComparePair BuildServiceComparePair(ServiceObjectOptionModel option, ServiceCardCandidate candidate)
        {
            if (host.State == null ||
                !host.State.TryPreviewRefinedCardSnapshots(candidate.Key, out var before, out var after, out _))
            {
                return default;
            }

            return new ServiceCardComparePair(before, after);
        }

        private bool OnServiceCardConfirmed(ServiceObjectOptionModel option, ServiceCardCandidate candidate)
        {
            if (host.State == null || option == null)
            {
                return false;
            }

            if (option.Id == "remove")
            {
                if (!host.State.TryPermanentRemoveCardFromDeck(candidate.Key, out var removeReason))
                {
                    serviceObjectPopupView?.SetMessage(
                        string.IsNullOrWhiteSpace(removeReason) ? "카드를 제거할 수 없습니다." : removeReason);
                    return false;
                }

                host.RequestAudioCue(AudioCueIds.ShopCardRemove, "service:remove");
                host.LastInputMessage = "카드를 제거했습니다.";
                CloseServiceObjectAndConsume();
                return true;
            }

            if (option.Id == "refine")
            {
                if (!host.State.TryRefineCard(candidate.Key, out var refineReason))
                {
                    serviceObjectPopupView?.SetMessage(
                        string.IsNullOrWhiteSpace(refineReason) ? "카드를 연마할 수 없습니다." : refineReason);
                    return false;
                }

                host.RequestAudioCue(AudioCueIds.ServiceRefine, "service:refine");
                host.LastInputMessage = "카드를 연마했습니다.";
                CloseServiceObjectAndConsume();
                return true;
            }

            return false;
        }

        // ── #20 서비스 디버그 표면(실플레이 피드백 2026-08-19) ──────────────────────────
        // 서비스 3종은 맵 곳곳에 하나씩이라 실플레이 검증이 매번 답사를 요구했다 — 디버그 패널이
        // 이 세 API로 ①순간이동 ②모달 즉시 열기 ③소비 원장 리셋을 제공한다(개발 전용 패널 소비).

        /// <summary>지정 타입("shop"/"camper"/"workshop")의 첫 오브젝트 좌표로 순간이동.</summary>
        public bool DebugTeleportToServiceObject(string kind, out string message)
        {
            message = string.Empty;
            if (!TryFindServiceObject(kind, out var objectData))
            {
                message = $"'{kind}' 오브젝트가 맵에 없습니다.";
                return false;
            }

            if (host.State == null || !host.State.TryDebugMovePlayer(objectData.Coord))
            {
                message = host.State != null ? host.State.LastFailureReason : "State가 없습니다.";
                return false;
            }

            host.CommitPresentationFromState(immediateCamera: false);
            host.RefreshHudOnly();
            message = $"{objectData.ObjectId}로 이동했습니다.";
            return true;
        }

        /// <summary>지정 서비스 모달을 좌표·소비 여부와 무관하게 즉시 연다(UI 검증 전용).</summary>
        public bool DebugOpenServiceModal(string kind, out string message)
        {
            message = string.Empty;
            if (!TryFindServiceObject(kind, out var objectData))
            {
                message = $"'{kind}' 오브젝트가 맵에 없습니다.";
                return false;
            }

            // 🔴 열기는 <b>거절될 수 있다</b> — 다른 모달(잡화점·서비스·보상 3택)이 이미 떠 있으면
            //    Open*는 조용히 되돌아온다. 실제로 열렸는지 확인하지 않으면 이 도구가 「열었습니다」라고
            //    거짓 보고하고, 판정하는 사람은 화면이 안 바뀌는 이유를 찾느라 시간을 버린다(실측).
            if (objectData.IsShop)
            {
                OpenShop(objectData);
                if (!IsShopPopupOpen())
                {
                    message = "다른 창이 열려 있어 잡화점을 열지 못했습니다. 먼저 닫아 주세요.";
                    return false;
                }
            }
            else
            {
                OpenServiceObject(objectData);
                if (!IsServicePopupOpen())
                {
                    message = "다른 창이 열려 있어 서비스를 열지 못했습니다. 먼저 닫아 주세요.";
                    return false;
                }
            }

            message = $"{objectData.ObjectId} 모달을 열었습니다.";
            return true;
        }

        /// <summary>서비스 3종+잡화점<b>과 보상뽑기 상자</b>의 소비 원장을 되돌리고 숨긴 시각도 복원한다.</summary>
        public int DebugResetServiceConsumption()
        {
            if (host.State == null || host.LoadedMap == null)
            {
                return 0;
            }

            var restored = 0;
            foreach (var objectRef in host.LoadedMap.ObjectRefs.Where(candidate =>
                         candidate.IsShop || candidate.IsCamperVan
                         || (candidate.IsWorkshop && ServiceObjectAvailability.WorkshopEnabled)
                         || candidate.IsTreasureChest))
            {
                if (!host.State.DebugUnclaimEventObject(objectRef.ObjectId))
                {
                    continue;
                }

                host.TileView?.RestoreMapObjectVisual(objectRef.ObjectId);
                restored++;
            }

            host.RefreshHudOnly();
            return restored;
        }

        private bool TryFindServiceObject(string kind, out HexMapObjectData objectData)
        {
            objectData = default;
            if (host.LoadedMap == null)
            {
                return false;
            }

            System.Func<HexMapObjectData, bool> match = kind switch
            {
                "shop" => candidate => candidate.IsShop,
                "camper" => candidate => candidate.IsCamperVan,
                "workshop" => candidate => candidate.IsWorkshop && ServiceObjectAvailability.WorkshopEnabled,
                _ => _ => false,
            };
            objectData = host.LoadedMap.ObjectRefs.FirstOrDefault(candidate => candidate.Interactable && match(candidate));
            return objectData.IsConfigured;
        }

        /// <summary>
        /// 서비스 오브젝트를 떠난다 = 소비(1회 방문, D-5). 서비스를 하나도 안 썼어도 소비된다 —
        /// 상점 CR-6과 같은 원칙이고, 소비 대장도 상점과 같은 ClaimedEventObjectIds를 쓰므로
        /// 세이브 중단→재개에서도 유지된다.
        /// </summary>
        private void CloseServiceObjectAndConsume()
        {
            if (hasPendingServiceObjectData && host.State != null &&
                host.State.TryConsumeShopObject(pendingServiceObjectData.ObjectId, out _))
            {
                host.TileView?.ConsumeMapObjectVisual(pendingServiceObjectData.ObjectId);
            }

            hasPendingServiceObjectData = false;
            pendingServiceObjectData = default;
            serviceObjectPopupView?.Hide();
            host.RefreshHudOnly();
        }
    }
}

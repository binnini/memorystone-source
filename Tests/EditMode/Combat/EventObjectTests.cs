using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Runtime.Cards;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class EventObjectTests
    {
        [Test]
        public void EventObjectDefinitionsSeparateRewardDebuffAndKnockbackCategories()
        {
            var reward = CombatEventObjectDefinition.CardRewardMachine(
                "card-gacha",
                new[] { RewardEventObjectOffer.Card(CardIds.Flashbang) });
            var debuff = CombatEventObjectDefinition.Debuff("poison-machine", DebuffEventObjectKind.Poison, 2, 3);
            var knockback = CombatEventObjectDefinition.BoxingGloveMachine("boxing-glove", 2, 1);

            Assert.That(reward.Category, Is.EqualTo(CombatEventObjectCategory.Reward));
            Assert.That(reward.RewardKind, Is.EqualTo(RewardEventObjectKind.CardDrawMachine));
            Assert.That(debuff.Category, Is.EqualTo(CombatEventObjectCategory.Debuff));
            Assert.That(debuff.DebuffKind, Is.EqualTo(DebuffEventObjectKind.Poison));
            Assert.That(knockback.Category, Is.EqualTo(CombatEventObjectCategory.Knockback));
            Assert.That(knockback.KnockbackKind, Is.EqualTo(KnockbackEventObjectKind.BoxingGloveMachine));
        }

        [Test]
        public void CardRewardEventObjectAddsSelectedCardToDeckAndMarksEventClaimed()
        {
            var state = CreateApprovedCatalogState();
            var offer = RewardEventObjectOffer.Card(
                CardIds.Flashbang,
                "섬광",
                "주변을 밝히고 범위 내 적의 이동을 막습니다.",
                CardEffectType.FieldObject);
            var reward = CombatEventObjectDefinition.CardRewardMachine("card-gacha", new[] { offer });
            var before = CountCards(state, CardIds.Flashbang);
            var beforeHand = CountHandCards(state, CardIds.Flashbang);

            Assert.That(state.TryClaimRewardEventObject(reward, offer, out var reason), Is.True, reason);

            Assert.That(CountCards(state, CardIds.Flashbang), Is.EqualTo(before + 1));
            Assert.That(CountHandCards(state, CardIds.Flashbang), Is.EqualTo(beforeHand + 1));
            Assert.That(state.ClaimedEventObjectIds, Does.Contain("card-gacha"));
        }

        [Test]
        public void RelicRewardEventObjectAddsSelectedPermanentItemToInventory()
        {
            var state = CreateApprovedCatalogState();
            var offer = RewardEventObjectOffer.PermanentItem(
                PlayerPermanentItemCatalog.TigerBadgeRelicId,
                "호신 삼단봉",
                "공격 카드 피해 +1.");
            var reward = CombatEventObjectDefinition.RelicRewardMachine("relic-gacha", new[] { offer });

            Assert.That(state.TryClaimRewardEventObject(reward, offer, out var reason), Is.True, reason);

            Assert.That(state.PlayerInventory.RelicsAndCurses.RelicCount, Is.EqualTo(1));
            Assert.That(state.PlayerInventory.RelicsAndCurses.SumEffect(PlayerPermanentItemEffectKind.AttackDamageBonus), Is.EqualTo(1));
            Assert.That(state.ClaimedEventObjectIds, Does.Contain("relic-gacha"));
        }

        [Test]
        public void RewardEventObjectCannotBeClaimedTwice()
        {
            var state = CreateApprovedCatalogState();
            var offer = RewardEventObjectOffer.PermanentItem(PlayerPermanentItemCatalog.HanriverShoesRelicId, "한강 러닝화");
            var reward = CombatEventObjectDefinition.RelicRewardMachine("relic-gacha", new[] { offer });

            Assert.That(state.TryClaimRewardEventObject(reward, offer, out var firstReason), Is.True, firstReason);

            Assert.That(state.TryClaimRewardEventObject(reward, offer, out var secondReason), Is.False);
            Assert.That(secondReason, Does.Contain("already claimed"));
            Assert.That(state.PlayerInventory.RelicsAndCurses.RelicCount, Is.EqualTo(1));
        }

        [Test]
        public void CardRewardEventOffersCanBeAdaptedForExistingRewardPopupUi()
        {
            var offer = RewardEventObjectOffer.Card(
                CardIds.Flashbang,
                "섬광",
                "주변을 밝히고 범위 내 적의 이동을 막습니다.",
                CardEffectType.FieldObject);
            var reward = CombatEventObjectDefinition.CardRewardMachine("card-gacha", new[] { offer });

            var uiOffers = RewardEventObjectUiAdapter.ToCardRewardOffers(reward);

            Assert.That(uiOffers, Has.Count.EqualTo(1));
            Assert.That(uiOffers[0].CardId, Is.EqualTo(CardIds.Flashbang));
            Assert.That(uiOffers[0].DisplayName, Is.EqualTo("섬광"));
            Assert.That(uiOffers[0].EffectType, Is.EqualTo(CardEffectType.FieldObject));
        }


        [Test]
        public void MovingOntoTreasureChestTileShowsRewardPopupWithoutClickingObject()
        {
            var host = new GameObject("Treasure chest move trigger test");
            try
            {
                CreateAuthoredRewardPopup();

                var controller = host.AddComponent<MapCombatController>();
                controller.UseDemoCardCatalogForTests();
                controller.ConfigurePresentationForTests(immediateSequences: true);
                // 상자는 이제 인형뽑기라 카드팩/돈/유물 중 하나가 나온다. 카드 3택 UI를 검사하는
                // 이 테스트는 카드팩 구간(누적 [0,50))으로 결과를 고정해야 확률적으로 깨지지 않는다.
                controller.ConfigureRewardRandomForTests(new FixedRewardRandom(0));
                controller.ConfigureMapForTests(CreateTreasureChestMap());
                controller.InitializeIntegration();

                Assert.That(controller.BeginMoveSelection(), Is.True);
                Assert.That(controller.TryMoveTo(new HexCoord(1, 0)), Is.True);

                var rewardPopup = Object.FindFirstObjectByType<CardRewardPopupView>(FindObjectsInactive.Include);
                Assert.That(rewardPopup, Is.Not.Null);
                Assert.That(rewardPopup.gameObject.activeSelf, Is.True);
                Assert.That(controller.LastInputMessage, Is.Not.Empty);
                Assert.That(controller.State.ClaimedEventObjectIds, Does.Not.Contain("treasure-chest-test"),
                    "Moving onto the tile should open the reward UI; claiming still happens from the reward UI selection.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                foreach (var popup in Object.FindObjectsByType<CardRewardPopupView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    Object.DestroyImmediate(popup.gameObject);
                }
            }
        }

        /// <summary>추첨 롤을 고정하는 난수원. 모든 Next 호출에 같은 값을 돌려준다.</summary>
        private sealed class FixedRewardRandom : IRewardRandom
        {
            private readonly int value;

            public FixedRewardRandom(int value) => this.value = value;

            public int Next(int maxExclusive) => value % Mathf.Max(1, maxExclusive);
        }

        // 상자를 밟았을 때 카드팩이 아닌 결과가 나오면 선택 UI 없이 즉시 지급되고 상자가 소비된다.
        // 이 두 테스트가 없으면 "유물 획득 경로가 프로덕션에 있다"는 사실이 다시 조용히 끊길 수 있다.
        //
        // 🔑 분포는 <b>테스트가 들고 온다</b>(2026-09-01): 출하 표가 「부적 하나」(100/0/0/0)로 굳어
        // 출하 값으로는 이 경로에 도달할 수 없다. 여기서 재는 것은 밸런스가 아니라 <b>배선</b>이다 —
        // 돈·유물 결과가 나왔을 때 선택 UI 없이 즉시 지급되고 상자가 소비되는가.
        private static readonly GachaRewardWeights NonCardOutcomeWeights = new GachaRewardWeights(
            new[]
            {
                new GachaRewardWeight(GachaRewardKind.CardPack, 50),
                new GachaRewardWeight(GachaRewardKind.Money, 35, 15, 40),
                new GachaRewardWeight(GachaRewardKind.Relic, 10),
                new GachaRewardWeight(GachaRewardKind.Item, 5)
            });
        [Test]
        public void MovingOntoTreasureChestCanPayMoneyWithoutOpeningAnyRewardUi()
        {
            var host = new GameObject("Treasure chest money outcome test");
            try
            {
                CreateAuthoredRewardPopup();

                var controller = host.AddComponent<MapCombatController>();
                controller.UseDemoCardCatalogForTests();
                controller.ConfigurePresentationForTests(immediateSequences: true);
                // 누적 구간 [50,90)이 돈이다. 55는 금액 오프셋으로도 쓰여 범위 안의 액수가 된다.
                controller.ConfigureRewardRandomForTests(new FixedRewardRandom(55));
                controller.ConfigureGachaRewardWeightsForTests(NonCardOutcomeWeights);
                controller.ConfigureMapForTests(CreateTreasureChestMap());
                controller.InitializeIntegration();

                Assert.That(controller.State.PlayerInventory.Wallet.Balance, Is.Zero);
                Assert.That(controller.BeginMoveSelection(), Is.True);
                Assert.That(controller.TryMoveTo(new HexCoord(1, 0)), Is.True);

                Assert.That(controller.State.PlayerInventory.Wallet.Balance, Is.GreaterThan(0), "돈 결과는 즉시 잔액에 들어가야 한다.");
                Assert.That(controller.State.ClaimedEventObjectIds, Does.Contain("treasure-chest-test"),
                    "선택지가 없는 결과는 그 자리에서 상자를 소비한다 — 안 그러면 같은 상자를 다시 밟아 무한히 뽑을 수 있다.");

                var rewardPopup = Object.FindFirstObjectByType<CardRewardPopupView>(FindObjectsInactive.Include);
                Assert.That(rewardPopup.gameObject.activeSelf, Is.False, "돈은 카드 3택 UI를 열지 않는다.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                foreach (var popup in Object.FindObjectsByType<CardRewardPopupView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    Object.DestroyImmediate(popup.gameObject);
                }
            }
        }

        [Test]
        public void MovingOntoTreasureChestCanGrantARelicThroughTheProductionPath()
        {
            var host = new GameObject("Treasure chest relic outcome test");
            try
            {
                CreateAuthoredRewardPopup();

                var controller = host.AddComponent<MapCombatController>();
                controller.UseDemoCardCatalogForTests();
                controller.ConfigurePresentationForTests(immediateSequences: true);
                // 누적 구간 [85,95)가 유물이다(T4-3 재배분 50/35/10/5).
                controller.ConfigureRewardRandomForTests(new FixedRewardRandom(85));
                controller.ConfigureGachaRewardWeightsForTests(NonCardOutcomeWeights);
                controller.ConfigureMapForTests(CreateTreasureChestMap());
                controller.InitializeIntegration();

                Assert.That(controller.State.PlayerInventory.RelicsAndCurses.Items, Is.Empty);
                Assert.That(controller.BeginMoveSelection(), Is.True);
                Assert.That(controller.TryMoveTo(new HexCoord(1, 0)), Is.True);

                var owned = controller.State.PlayerInventory.RelicsAndCurses.Items;
                Assert.That(owned.Count, Is.EqualTo(1), "유물 결과는 선택 없이 즉시 인벤토리에 들어간다.");
                Assert.That(owned[0].Kind, Is.EqualTo(PlayerPermanentItemKind.Relic), "뽑기에서 저주는 나오지 않는다(D-8).");
                Assert.That(controller.State.ClaimedEventObjectIds, Does.Contain("treasure-chest-test"));
            }
            finally
            {
                Object.DestroyImmediate(host);
                foreach (var popup in Object.FindObjectsByType<CardRewardPopupView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    Object.DestroyImmediate(popup.gameObject);
                }
            }
        }

        [Test]
        public void CardRewardOverlayRootPrefabBindsRewardCardsAndSkipButton()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/UI/Prototype/Card Reward Overlay Root.prefab");
            Assert.That(prefab, Is.Not.Null);

            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            try
            {
                Assert.That(instance, Is.Not.Null);
                var view = instance.GetComponent<CardRewardPopupView>();
                Assert.That(view, Is.Not.Null);

                var authoredSlots = instance.GetComponentsInChildren<ChoiceCardOptionSlot>(includeInactive: true);
                Assert.That(authoredSlots, Has.Length.GreaterThanOrEqualTo(2));
                Assert.That(authoredSlots.All(slot => slot.GetComponent<CardRewardGlowOverlay>() != null), Is.True);
                var cardList = instance.GetComponentsInChildren<RectTransform>(includeInactive: true)
                    .FirstOrDefault(rect => rect.name == "Card List");
                Assert.That(cardList, Is.Not.Null);
                var glowLayer = cardList.Find("Reward Glow Layer");
                Assert.That(glowLayer, Is.Not.Null);
                Assert.That(glowLayer.GetSiblingIndex(), Is.EqualTo(0));

                var selectedCardId = string.Empty;
                var skipped = false;
                view.Show(new[]
                {
                    new CardRewardOffer(CardIds.Sweep, "Attack", "Attack", "Deal damage.", 1, CardEffectType.Attack),
                    new CardRewardOffer(CardIds.Move2Hex, "Move", "Move", "Move farther.", 0, CardEffectType.Move)
                }, cardId => selectedCardId = cardId, () => skipped = true);

                Assert.That(instance.activeSelf, Is.True);
                var rewardButtons = instance.GetComponentsInChildren<Button>(includeInactive: true)
                    .Where(button => button.GetComponent<ChoiceCardOptionSlot>() != null)
                    .ToArray();
                Assert.That(rewardButtons, Has.Length.GreaterThanOrEqualTo(2));

                var firstGlowRoot = glowLayer.Find("Reward Card Option 0 RewardGlowRoot");
                Assert.That(firstGlowRoot, Is.Not.Null);
                Assert.That(firstGlowRoot.GetSiblingIndex(), Is.EqualTo(0));

                var rays = firstGlowRoot.Find("card_glow_reward2_Rays")?.GetComponent<Image>();
                var soft = firstGlowRoot.Find("card_glow_reward_Soft")?.GetComponent<Image>();
                Assert.That(rays, Is.Not.Null);
                Assert.That(soft, Is.Not.Null);
                Assert.That(rays.transform.GetSiblingIndex(), Is.LessThan(soft.transform.GetSiblingIndex()));
                Assert.That(rays.raycastTarget, Is.False);
                Assert.That(soft.raycastTarget, Is.False);
                Assert.That(rays.preserveAspect, Is.True);
                Assert.That(soft.preserveAspect, Is.True);
                Assert.That(rays.material != null && rays.material.name.Contains("UI_AdditiveGlow"), Is.True);
                Assert.That(soft.material != null && soft.material.name.Contains("UI_AdditiveGlow"), Is.True);

                rewardButtons[0].onClick.Invoke();
                Assert.That(selectedCardId, Is.Not.Empty);

                var skipButton = instance.GetComponentsInChildren<Button>(includeInactive: true)
                    .FirstOrDefault(button => button.name == "SkipButton");
                Assert.That(skipButton, Is.Not.Null);

                skipButton.onClick.Invoke();
                Assert.That(skipped, Is.True);
            }
            finally
            {
                if (instance != null)
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void RewardCardHoverFeedbackRaisesCueOncePerPointerEnter()
        {
            var view = CreateAuthoredRewardPopup();
            try
            {
                var hoverCount = 0;
                view.Show(new[]
                {
                    new CardRewardOffer(CardIds.Sweep, "Attack", "Attack", "Deal damage.", 1, CardEffectType.Attack)
                }, _ => { }, () => { }, () => hoverCount++);

                var overlay = view.GetComponentsInChildren<CardRewardGlowOverlay>(includeInactive: true).FirstOrDefault();
                Assert.That(overlay, Is.Not.Null);

                overlay.OnPointerEnter(new PointerEventData(EventSystem.current));
                overlay.OnPointerEnter(new PointerEventData(EventSystem.current));
                Assert.That(hoverCount, Is.EqualTo(1));

                overlay.OnPointerExit(new PointerEventData(EventSystem.current));
                overlay.OnPointerEnter(new PointerEventData(EventSystem.current));
                Assert.That(hoverCount, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(view.transform.root.gameObject);
            }
        }


        [Test]
        public void CardRewardGeneratedFallbackTextUsesKoreanWithoutMojibake()
        {
            var view = CreateAuthoredRewardPopup();
            try
            {
                view.Show(new[]
                {
                    new CardRewardOffer(CardIds.Sweep, "Attack", "\uACF5\uACA9", "\uD53C\uD574\uB97C \uC785\uD799\uB2C8\uB2E4.", 1, CardEffectType.Attack)
                }, _ => { }, () => { });

                var allText = string.Join("\n", view.GetComponentsInChildren<TMP_Text>(includeInactive: true).Select(text => text.text));
                Assert.That(allText, Does.Contain("\uBCF4\uC0C1\uC744 \uC120\uD0DD\uD558\uC138\uC694."));
                Assert.That(allText, Does.Contain("\uCE74\uB4DC \uD55C \uC7A5\uC744 \uC120\uD0DD\uD574 \uD604\uC7AC \uC190\uD328\uC5D0 \uCD94\uAC00\uD569\uB2C8\uB2E4."));
                Assert.That(allText, Does.Contain("\uB118\uAE30\uAE30"));
            }
            finally
            {
                Object.DestroyImmediate(view.transform.root.gameObject);
            }
        }


        [Test]
        public void TreasureChestRewardSelectionAddsSelectedCardToCurrentHandAndHidesPopup()
        {
            var host = new GameObject("Treasure chest reward select test");
            try
            {
                CreateAuthoredRewardPopup();

                var controller = host.AddComponent<MapCombatController>();
                controller.UseDemoCardCatalogForTests();
                // The reward pool only drops Rare+ cards, so the runtime fallback catalog
                // (Basic-only) yields zero offers and the card buttons stay unbound —
                // assign the CSV catalog like real scenes do.
                AssignCsvCardCatalog(controller);
                controller.ConfigurePresentationForTests(immediateSequences: true);
                controller.ConfigureMapForTests(CreateTreasureChestMap());
                // 상자는 인형뽑기라 결과가 무작위다(카드팩 50%). 고정하지 않으면 돈/유물이 나와
                // 팝업이 뜨지 않고 이 테스트가 확률적으로 깨진다 — cs:790에서 이웃 테스트를 고정한
                // 것과 같은 이유. 0은 카드팩 구간 [0,50)이다.
                controller.ConfigureRewardRandomForTests(new FixedRewardRandom(0));
                controller.InitializeIntegration();

                Assert.That(controller.BeginMoveSelection(), Is.True);
                Assert.That(controller.TryMoveTo(new HexCoord(1, 0)), Is.True);

                var rewardPopup = Object.FindFirstObjectByType<CardRewardPopupView>(FindObjectsInactive.Include);
                Assert.That(rewardPopup, Is.Not.Null);
                // The reward card is injected into its kind's deck hand; GetHandCards() only exposes
                // the current phase's hand, so count across both deck hands instead.
                var beforeHandCount = controller.State.MovementDeck.HandCount + controller.State.ActionDeck.HandCount;

                var cardButton = rewardPopup.GetComponentsInChildren<Button>(includeInactive: true)
                    .FirstOrDefault(button => button.name.StartsWith("Reward Card ") && button.name.EndsWith(" Click"));
                Assert.That(cardButton, Is.Not.Null);

                cardButton.onClick.Invoke();

                Assert.That(
                    controller.State.MovementDeck.HandCount + controller.State.ActionDeck.HandCount,
                    Is.EqualTo(beforeHandCount + 1));
                Assert.That(controller.State.ClaimedEventObjectIds, Does.Contain("treasure-chest-test"));
                Assert.That(rewardPopup.gameObject.activeSelf, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(host);
                foreach (var popup in Object.FindObjectsByType<CardRewardPopupView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    Object.DestroyImmediate(popup.gameObject);
                }
            }
        }

        [Test]
        public void TreasureChestRewardSkipMarksRewardClaimedWithoutAddingCurrentHandCard()
        {
            var host = new GameObject("Treasure chest reward skip test");
            try
            {
                CreateAuthoredRewardPopup();

                var controller = host.AddComponent<MapCombatController>();
                controller.UseDemoCardCatalogForTests();
                controller.ConfigurePresentationForTests(immediateSequences: true);
                controller.ConfigureMapForTests(CreateTreasureChestMap());
                controller.InitializeIntegration();

                Assert.That(controller.BeginMoveSelection(), Is.True);
                Assert.That(controller.TryMoveTo(new HexCoord(1, 0)), Is.True);

                var rewardPopup = Object.FindFirstObjectByType<CardRewardPopupView>(FindObjectsInactive.Include);
                Assert.That(rewardPopup, Is.Not.Null);
                var beforeHandCount = controller.State.GetHandCards().Count;

                var skipButton = rewardPopup.GetComponentsInChildren<Button>(includeInactive: true)
                    .FirstOrDefault(button => button.name == "Reward Skip Button");
                Assert.That(skipButton, Is.Not.Null);

                skipButton.onClick.Invoke();

                Assert.That(controller.State.GetHandCards().Count, Is.EqualTo(beforeHandCount));
                Assert.That(controller.State.ClaimedEventObjectIds, Does.Contain("treasure-chest-test"));
                Assert.That(rewardPopup.gameObject.activeSelf, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(host);
                foreach (var popup in Object.FindObjectsByType<CardRewardPopupView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    Object.DestroyImmediate(popup.gameObject);
                }
            }
        }

        private static void AssignCsvCardCatalog(MapCombatController controller)
        {
            var catalogAsset = AssetDatabase.LoadAssetAtPath<CardCatalogAsset>(
                TestAssetPaths.CardCatalogAsset);
            Assert.That(catalogAsset, Is.Not.Null, "CSV CardCatalog.asset must exist for reward offers.");
            typeof(MapCombatController)
                .GetField("cardCatalogAsset", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(controller, catalogAsset);
        }

        private static CardRewardPopupView CreateAuthoredRewardPopup()
        {
            var root = new GameObject(
                "Card Reward Popup Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster),
                typeof(CardRewardPopupView));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 110;

            var overlay = CreateRect("Reward Overlay", root.transform);
            overlay.gameObject.AddComponent<Image>();

            var panel = CreateRect("Reward Panel", root.transform);
            panel.gameObject.AddComponent<Image>();
            CreateText("Reward Title", panel);
            CreateText("Reward Subtitle", panel);

            for (var i = 0; i < 3; i++)
            {
                var card = CreateRect($"Reward Card {i}", panel);
                card.gameObject.AddComponent<Image>();

                var costPanel = CreateRect($"Reward Card {i} Cost Panel", card);
                costPanel.gameObject.AddComponent<Image>();
                CreateText($"Reward Card {i} Cost", costPanel);
                CreateText($"Reward Card {i} Type", card);
                CreateText($"Reward Card {i} Name", card);
                CreateText($"Reward Card {i} Desc", card);

                var clickTarget = CreateRect($"Reward Card {i} Click", card);
                clickTarget.gameObject.AddComponent<Image>();
                clickTarget.gameObject.AddComponent<Button>();
            }

            var skipButton = CreateRect("Reward Skip Button", panel);
            skipButton.gameObject.AddComponent<Image>();
            skipButton.gameObject.AddComponent<Button>();
            CreateText("Reward Skip Label", skipButton);

            var view = root.GetComponent<CardRewardPopupView>();
            view.AutoBindFromHierarchy();
            root.SetActive(false);
            return view;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static TMP_Text CreateText(string name, Transform parent)
        {
            var text = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI))
                .GetComponent<TMP_Text>();
            text.transform.SetParent(parent, false);
            text.text = string.Empty;
            return text;
        }

        private static int CountCards(CombatState state, string cardId)
        {
            return state.MovementDeck.DrawPile
                .Concat(state.MovementDeck.Hand)
                .Concat(state.MovementDeck.DiscardPile)
                .Concat(state.ActionDeck.DrawPile)
                .Concat(state.ActionDeck.Hand)
                .Concat(state.ActionDeck.DiscardPile)
                .Count(card => card.Id == cardId);
        }



        private static int CountHandCards(CombatState state, string cardId)
        {
            return state.MovementDeck.Hand
                .Concat(state.ActionDeck.Hand)
                .Count(card => card.Id == cardId);
        }
        private static HexMapData CreateTreasureChestMap()
        {
            var cells = new[]
            {
                new HexCellData(new HexCoord(0, 0), "plain", "plain", 1, true, false),
                new HexCellData(new HexCoord(1, 0), "plain", "plain", 1, true, false),
                new HexCellData(new HexCoord(2, 0), "plain", "plain", 1, true, false)
            };
            var objects = new[]
            {
                new HexMapObjectData(
                    "treasure-chest-test",
                    "TreasureChest",
                    "treasureChest_tmp",
                    new HexCoord(1, 0),
                    interactable: true)
            };

            return new HexMapData(cells, objectRefs: objects);
        }
        private static CombatState CreateApprovedCatalogState()
        {
            return new CombatState(
                CombatState.CreateDemoMap(2),
                new SeoulPlayup.Map.Runtime.HexCoord(-1, 0),
                new SeoulPlayup.Map.Runtime.HexCoord(2, 0),
                CombatConfig.Default,
                cardCatalog: DemoCardCatalog.Create(CombatConfig.Default));
        }
    }
}



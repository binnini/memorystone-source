#if UNITY_EDITOR
using System;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class GameplayCardLaneViewTests
    {
        private const string CardFrontPrefabPath = TestAssetPaths.CardFrontPrefab;
        private const string MoveCardFrontPrefabPath = TestAssetPaths.MoveCardFrontPrefab;
        private const string ActionCardFrontPrefabPath = TestAssetPaths.ActionCardFrontPrefab;
        private const string DefaultCardFramePath = TestAssetPaths.ActionCardFrame;
        private const string MoveCardFramePath = TestAssetPaths.MoveCardFrame;
        private const string ActionCardFramePath = TestAssetPaths.ActionCardFrame;
        private const string DefaultCardIllustrationPath = "Assets/Art/UI/Cards/Illust/default_illust.png";
        private const string AttackA01IllustrationPath = "Assets/Art/UI/Cards/Illust/card_illust_A01.png";

        [TestCase(0, 0)]
        [TestCase(1, 0)]
        [TestCase(1, 2)]
        [TestCase(2, 3)]
        [TestCase(4, 6)]
        public void RefreshHandCardsPoolsSlotsToMatchCurrentHandAndSeparatesMoveFromAction(int moveCount, int actionCount)
        {
            var harness = CreateHarness();
            try
            {
                var hand = CreateHand(moveCount, actionCount);

                harness.View.RefreshHandCards(hand, null, null, _ => { });
                harness.View.RefreshHandCards(hand, null, null, _ => { });

                var activeMoveSlots = harness.MoveRoot.GetComponentsInChildren<HandCardInteraction>(includeInactive: false);
                var activeActionSlots = harness.ActionRoot.GetComponentsInChildren<HandCardInteraction>(includeInactive: false);
                Assert.That(activeMoveSlots, Has.Length.EqualTo(moveCount));
                Assert.That(activeActionSlots, Has.Length.EqualTo(actionCount));
                Assert.That(CountDirectSlots(harness.MoveRoot), Is.EqualTo(moveCount));
                Assert.That(CountDirectSlots(harness.ActionRoot), Is.EqualTo(actionCount));
                Assert.That(activeMoveSlots.All(slot => slot.transform.parent == harness.MoveRoot), Is.True);
                Assert.That(activeActionSlots.All(slot => slot.transform.parent == harness.ActionRoot), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(harness.Root);
            }
        }

        [Test]
        public void RefreshHandCardsReusesExtraSlotsAndDisablesSurplusWithoutGrowingPool()
        {
            var harness = CreateHarness();
            try
            {
                harness.View.RefreshHandCards(CreateHand(moveCount: 3, actionCount: 7), null, null, _ => { });
                var movePool = CountDirectSlots(harness.MoveRoot);
                var actionPool = CountDirectSlots(harness.ActionRoot);

                harness.View.RefreshHandCards(CreateHand(moveCount: 1, actionCount: 2), null, null, _ => { });
                harness.View.RefreshHandCards(CreateHand(moveCount: 1, actionCount: 2), null, null, _ => { });

                Assert.That(CountDirectSlots(harness.MoveRoot), Is.EqualTo(movePool));
                Assert.That(CountDirectSlots(harness.ActionRoot), Is.EqualTo(actionPool));
                Assert.That(harness.MoveRoot.GetComponentsInChildren<HandCardInteraction>(includeInactive: false), Has.Length.EqualTo(1));
                Assert.That(harness.ActionRoot.GetComponentsInChildren<HandCardInteraction>(includeInactive: false), Has.Length.EqualTo(2));
                Assert.That(harness.MoveRoot.GetComponentsInChildren<HandCardInteraction>(includeInactive: true).Count(slot => !slot.gameObject.activeSelf), Is.EqualTo(movePool - 1));
                Assert.That(harness.ActionRoot.GetComponentsInChildren<HandCardInteraction>(includeInactive: true).Count(slot => !slot.gameObject.activeSelf), Is.EqualTo(actionPool - 2));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(harness.Root);
            }
        }

        [Test]
        public void RefreshHandCardsAdoptsAuthoredCardFrontNamedSlotsAndDisablesSurplus()
        {
            var harness = CreateHarness();
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CardFrontPrefabPath);
                var staleMove = UnityEngine.Object.Instantiate(prefab, harness.MoveRoot, false);
                staleMove.name = "CardFront";
                var staleAction = UnityEngine.Object.Instantiate(prefab, harness.ActionRoot, false);
                staleAction.name = "CardFront";

                harness.View.RefreshHandCards(CreateHand(moveCount: 1, actionCount: 0), null, null, _ => { });

                Assert.That(staleMove.GetComponent<HandCardInteraction>(), Is.Not.Null);
                Assert.That(CountDirectSlots(harness.MoveRoot), Is.EqualTo(1),
                    "An authored CardFront child should be adopted as a pool slot instead of left as an unmanaged raycast blocker.");
                Assert.That(CountDirectSlots(harness.ActionRoot), Is.EqualTo(1),
                    "Surplus authored CardFront children should stay in the pool.");
                Assert.That(harness.MoveRoot.GetComponentsInChildren<HandCardInteraction>(includeInactive: false), Has.Length.EqualTo(1));
                Assert.That(harness.ActionRoot.GetComponentsInChildren<HandCardInteraction>(includeInactive: false), Is.Empty,
                    "Surplus Action CardFront children should be inactive so they cannot intercept CardLane hover raycasts.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(harness.Root);
            }
        }

        [Test]
        public void RefreshHandCardsKeepsStableSlotOrderWhileCardIsHovered()
        {
            var harness = CreateHarness();
            try
            {
                var hand = new[]
                {
                    new CombatCardSnapshot("move-a", CombatCardKind.Move, "Move A", "Move", 1, true, false, "Ready", instanceId: "move-instance-a"),
                    new CombatCardSnapshot("move-b", CombatCardKind.Move, "Move B", "Move", 1, true, false, "Ready", instanceId: "move-instance-b")
                };
                harness.View.RefreshHandCards(hand, null, null, _ => { });

                var firstSlot = harness.MoveRoot.GetComponentsInChildren<HandCardInteraction>(includeInactive: false)
                    .OrderBy(slot => slot.transform.GetSiblingIndex())
                    .First();
                firstSlot.SetHoverFromLane(true);
                Assert.That(firstSlot.IsHovering, Is.True);
                Assert.That(firstSlot.CurrentCardSelectionKey, Is.EqualTo("move-instance-a"));

                harness.View.RefreshHandCards(hand, null, null, _ => { });

                var activeSlots = harness.MoveRoot.GetComponentsInChildren<HandCardInteraction>(includeInactive: false);
                Assert.That(activeSlots, Has.Length.EqualTo(2));
                Assert.That(activeSlots.Count(slot => slot.CurrentCardSelectionKey == "move-instance-a"), Is.EqualTo(1),
                    "Refreshing while a hovered card is rendered as last sibling must not bind that same card into its old slot.");
                Assert.That(activeSlots.Count(slot => slot.CurrentCardSelectionKey == "move-instance-b"), Is.EqualTo(1));
                Assert.That(firstSlot.IsHovering, Is.True);
                Assert.That(firstSlot.CurrentCardSelectionKey, Is.EqualTo("move-instance-a"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(harness.Root);
            }
        }

        [Test]
        public void RefreshHandCardsDoesNotPreserveHoveredSlotWhenThatCardLeftHand()
        {
            var harness = CreateHarness();
            try
            {
                var firstHand = new[]
                {
                    new CombatCardSnapshot("move-a", CombatCardKind.Move, "Move A", "Move", 1, true, false, "Ready", instanceId: "move-instance-a"),
                    new CombatCardSnapshot("move-b", CombatCardKind.Move, "Move B", "Move", 1, true, false, "Ready", instanceId: "move-instance-b")
                };
                harness.View.RefreshHandCards(firstHand, null, null, _ => { });

                var firstSlot = harness.MoveRoot.GetComponentsInChildren<HandCardInteraction>(includeInactive: false)
                    .OrderBy(slot => slot.transform.GetSiblingIndex())
                    .First();
                firstSlot.SetHoverFromLane(true);
                Assert.That(firstSlot.IsHovering, Is.True);
                Assert.That(firstSlot.CurrentCardSelectionKey, Is.EqualTo("move-instance-a"));

                harness.View.RefreshHandCards(new[] { firstHand[1] }, null, null, _ => { });

                var activeSlots = harness.MoveRoot.GetComponentsInChildren<HandCardInteraction>(includeInactive: false);
                Assert.That(activeSlots, Has.Length.EqualTo(1));
                Assert.That(activeSlots[0].CurrentCardSelectionKey, Is.EqualTo("move-instance-b"),
                    "A hovered slot whose card left hand must be rebound instead of kept as a ghost at its old position.");
                Assert.That(activeSlots[0].IsHovering, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(harness.Root);
            }
        }

        [Test]
        public void HoverResolverTransfersByHomeSliceInsteadOfLiftedVisualOverlap()
        {
            var harness = CreateHarness();
            try
            {
                harness.View.RefreshHandCards(CreateHand(moveCount: 2, actionCount: 0), null, null, _ => { });
                var slots = harness.View.MoveCardSlots.Take(2).ToArray();
                Assert.That(slots, Has.Length.EqualTo(2));

                SetSlotHome(slots[0], new Vector2(0f, 0f), siblingIndex: 0);
                SetSlotHome(slots[1], new Vector2(80f, 0f), siblingIndex: 1);

                harness.View.ApplyResolvedHover(ToScreenPoint(harness.MoveRoot, slots[0].HomeAnchoredPosition));
                Assert.That(slots[0].IsHovering, Is.True);
                Assert.That(slots[1].IsHovering, Is.False);

                harness.View.ApplyResolvedHover(ToScreenPoint(harness.MoveRoot, slots[1].HomeAnchoredPosition));

                Assert.That(slots[0].IsHovering, Is.False,
                    "The enlarged first card must not trap hover after the pointer moves into the neighbor's resting slice.");
                Assert.That(slots[1].IsHovering, Is.True);
                Assert.That(slots.Count(slot => slot.IsHovering), Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(harness.Root);
            }
        }

        [Test]
        public void HoverResolverUsesStableSlotOrderAfterHoveredCardMovesToFront()
        {
            var harness = CreateHarness();
            try
            {
                harness.View.RefreshHandCards(CreateHand(moveCount: 2, actionCount: 0), null, null, _ => { });
                var slots = harness.View.MoveCardSlots.Take(2).ToArray();
                SetSlotHome(slots[0], new Vector2(0f, 0f), siblingIndex: 0);
                SetSlotHome(slots[1], new Vector2(80f, 0f), siblingIndex: 1);

                harness.View.ApplyResolvedHover(ToScreenPoint(harness.MoveRoot, slots[0].HomeAnchoredPosition));
                Assert.That(slots[0].IsHovering, Is.True);
                slots[0].transform.SetAsLastSibling();
                Assert.That(slots[0].transform.GetSiblingIndex(), Is.EqualTo(harness.MoveRoot.childCount - 1));

                harness.View.RefreshHandCards(CreateHand(moveCount: 2, actionCount: 0), null, null, _ => { });
                harness.View.ApplyResolvedHover(ToScreenPoint(harness.MoveRoot, slots[1].HomeAnchoredPosition));

                Assert.That(slots[0].CurrentCardSelectionKey, Is.EqualTo("move-instance-0"));
                Assert.That(slots[1].CurrentCardSelectionKey, Is.EqualTo("move-instance-1"));
                Assert.That(slots[0].IsHovering, Is.False);
                Assert.That(slots[1].IsHovering, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(harness.Root);
            }
        }

        [Test]
        public void HoverResolverRestoresActionRootInFrontOfMoveRootWhenNoCardIsHovered()
        {
            var harness = CreateHarness();
            try
            {
                harness.View.RefreshHandCards(CreateHand(moveCount: 1, actionCount: 1), null, null, _ => { });
                var moveSlot = harness.View.MoveCardSlots.Single();
                var actionSlot = harness.View.ActionCardSlots.Single();
                SetSlotHome(moveSlot, new Vector2(-40f, 0f), siblingIndex: 0);
                SetSlotHome(actionSlot, new Vector2(40f, 0f), siblingIndex: 0);

                harness.View.ApplyResolvedHover(ToScreenPoint(harness.MoveRoot, moveSlot.HomeAnchoredPosition));
                Assert.That(moveSlot.IsHovering, Is.True);
                Assert.That(harness.MoveRoot.GetSiblingIndex(), Is.GreaterThan(harness.ActionRoot.GetSiblingIndex()),
                    "A hovered Move card should temporarily bring its root in front of the Action root.");

                harness.View.ApplyResolvedHover(new Vector2(-10000f, -10000f));

                Assert.That(moveSlot.IsHovering, Is.False);
                Assert.That(actionSlot.IsHovering, Is.False);
                Assert.That(harness.MoveRoot.GetSiblingIndex(), Is.LessThan(harness.ActionRoot.GetSiblingIndex()),
                    "After hover clears, right-side Action cards must render in front of left-side Move cards at the boundary.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(harness.Root);
            }
        }

        [Test]
        public void HomeInputProxyClicksHoveredCardAtOriginalHomePosition()
        {
            var harness = CreateHarness();
            try
            {
                EnsureEventSystem();
                harness.View.RefreshHandCards(CreateHand(moveCount: 1, actionCount: 0), null, null, _ => { });
                var slot = harness.View.MoveCardSlots.Single();
                SetSlotHome(slot, Vector2.zero, siblingIndex: 0);

                var clicked = false;
                slot.Initialize((_, _, _) => { }, (_, _) => clicked = true);
                slot.Configure(
                    new CombatCardSnapshot("move-click", CombatCardKind.Move, "Move Click", "Move", 1, true, false, "Ready"),
                    playable: true,
                    selected: false);
                slot.SetLaneHoverControlled(true);

                var homeScreenPosition = ToScreenPoint(harness.MoveRoot, slot.HomeAnchoredPosition);
                harness.View.ApplyResolvedHover(homeScreenPosition);
                Assert.That(slot.IsHovering, Is.True);
                Assert.That(((RectTransform)slot.transform).anchoredPosition.y, Is.GreaterThan(slot.HomeAnchoredPosition.y),
                    "The regression setup needs the visual card lifted away from its original home hit area.");

                var proxy = harness.Root.transform.Find("CardLaneHomeInputProxy");
                Assert.That(proxy, Is.Not.Null);

                var eventData = new PointerEventData(EventSystem.current)
                {
                    position = homeScreenPosition,
                    pressPosition = homeScreenPosition,
                    button = PointerEventData.InputButton.Left
                };

                ExecuteEvents.Execute(proxy.gameObject, eventData, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(proxy.gameObject, eventData, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute(proxy.gameObject, eventData, ExecuteEvents.pointerClickHandler);

                Assert.That(clicked, Is.True,
                    "Clicking the card's original home position should still activate the hovered/lifted card.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(harness.Root);
                DestroyEventSystem();
            }
        }

        [Test]
        public void HomeInputProxyStartsDragFromHoveredCardOriginalHomePosition()
        {
            var harness = CreateHarness();
            try
            {
                EnsureEventSystem();
                harness.View.RefreshHandCards(CreateHand(moveCount: 1, actionCount: 0), null, null, _ => { });
                var slot = harness.View.MoveCardSlots.Single();
                SetSlotHome(slot, Vector2.zero, siblingIndex: 0);
                slot.Configure(
                    new CombatCardSnapshot("move-drag", CombatCardKind.Move, "Move Drag", "Move", 1, true, false, "Ready"),
                    playable: true,
                    selected: false);
                slot.SetLaneHoverControlled(true);

                var homeScreenPosition = ToScreenPoint(harness.MoveRoot, slot.HomeAnchoredPosition);
                harness.View.ApplyResolvedHover(homeScreenPosition);

                var proxy = harness.Root.transform.Find("CardLaneHomeInputProxy");
                Assert.That(proxy, Is.Not.Null);

                var eventData = new PointerEventData(EventSystem.current)
                {
                    position = homeScreenPosition,
                    pressPosition = homeScreenPosition,
                    button = PointerEventData.InputButton.Left
                };

                ExecuteEvents.Execute(proxy.gameObject, eventData, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(proxy.gameObject, eventData, ExecuteEvents.beginDragHandler);

                Assert.That(slot.IsDragging, Is.True,
                    "Dragging from the card's original home position should grab the hovered/lifted card.");

                ExecuteEvents.Execute(proxy.gameObject, eventData, ExecuteEvents.endDragHandler);
                Assert.That(slot.IsDragging, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(harness.Root);
                DestroyEventSystem();
            }
        }

        [Test]
        public void RevealStateStaysOpenWhileCardPointerStateIsActive()
        {
            var harness = CreateHarness();
            try
            {
                harness.View.RefreshHandCards(CreateHand(moveCount: 1, actionCount: 0), null, null, _ => { });
                var slot = harness.View.MoveCardSlots.Single();

                Assert.That(harness.View.HasPointerActiveCardForTests(), Is.False);

                slot.SetHoverFromLane(true);

                Assert.That(harness.View.HasPointerActiveCardForTests(), Is.True,
                    "CardLane reveal should not hide while a card is hovered or being dragged above the bottom reveal band.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(harness.Root);
            }
        }

        [Test]
        public void CardFrontPrefabKeepsDefaultFrameAndTextDoesNotBlockRaycasts()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CardFrontPrefabPath);
            var defaultFrame = AssetDatabase.LoadAssetAtPath<Sprite>(DefaultCardFramePath);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(defaultFrame, Is.Not.Null);

            Assert.That(prefab.GetComponent<Image>(), Is.Null,
                "CardFront root should not own a frame Image; Card_Frame_Overlay is the single authored frame image.");
            var frameOverlay = FindImage(prefab.transform, "Card_Frame_Overlay");
            Assert.That(frameOverlay, Is.Not.Null);
            Assert.That(frameOverlay.raycastTarget, Is.True);
            Assert.That(frameOverlay.sprite, Is.EqualTo(defaultFrame));
            Assert.That(prefab.GetComponent<HandCardInteraction>(), Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<TMPro.TMP_Text>(includeInactive: true).Any(text => text.raycastTarget), Is.False);

            var visualChildren = prefab.GetComponentsInChildren<Image>(includeInactive: true)
                .Where(image => image.gameObject != prefab)
                .ToArray();
            Assert.That(visualChildren.Any(image => image.name == "Card_Illust"), Is.True);
            Assert.That(visualChildren.Any(image => image.name == "Card_Frame_Overlay"), Is.True);
            Assert.That(visualChildren.Where(image => image.name != "Card_Frame_Overlay").Any(image => image.raycastTarget), Is.False,
                "Only Card_Frame_Overlay should receive pointer input; other visual children must not steal clicks/drags from HandCardInteraction.");
            Assert.That(prefab.GetComponentsInChildren<HandCardInteraction>(includeInactive: true), Has.Length.EqualTo(1),
                "Nested visual children must not own card interaction state because uninitialized child handlers swallow input.");
        }

        [Test]
        public void RefreshHandCardsChangesTextContentAndIllustrationWithoutOverridingPrefabTextOrImageColors()
        {
            var harness = CreateHarness();
            try
            {
                // The hand below holds a Move card, so slots instantiate the move-authored CardFront variant —
                // authored colors must come from that prefab (it adds children like Cost_Icon over the base).
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MoveCardFrontPrefabPath);
                var defaultIllustration = AssetDatabase.LoadAssetAtPath<Sprite>(DefaultCardIllustrationPath);
                Assert.That(prefab, Is.Not.Null);
                Assert.That(defaultIllustration, Is.Not.Null);

                var prefabTextColors = prefab.GetComponentsInChildren<TMPro.TMP_Text>(includeInactive: true)
                    .ToDictionary(text => text.name, text => text.color);
                var prefabChildImageColors = prefab.GetComponentsInChildren<Image>(includeInactive: true)
                    .Where(image => image.gameObject != prefab)
                    .ToDictionary(image => image.name, image => image.color);

                var hand = new[]
                {
                    new CombatCardSnapshot("move-a", CombatCardKind.Move, "Move A", "Runtime move description", 1, false, false, "Waiting", cost: 2, instanceId: "move-instance-a")
                };

                harness.View.RefreshHandCards(hand, null, null, _ => { });

                var slot = harness.MoveRoot.GetComponentsInChildren<HandCardInteraction>(includeInactive: false)
                    .Single();

                Assert.That(slot.GetComponent<Image>(), Is.Null,
                    "Card root should not regain a duplicate Image when CardLane refreshes prefab slots.");
                foreach (var image in slot.GetComponentsInChildren<Image>(includeInactive: true)
                    .Where(image => image.gameObject != slot.gameObject))
                {
                    // The runtime-added dim overlay is not part of the authored prefab; the overlay approach
                    // (cs:647) never touches the authored Image colors, so every prefab image stays authored.
                    if (image.name == DimOverlayName)
                    {
                        continue;
                    }

                    Assert.That(prefabChildImageColors, Contains.Key(image.name));
                    AssertColorApproximately(image.color, prefabChildImageColors[image.name],
                        $"{image.name} color should stay authored by the prefab.");
                }

                foreach (var text in slot.GetComponentsInChildren<TMPro.TMP_Text>(includeInactive: true))
                {
                    if (text.name == "UnavailableReason_TMP")
                    {
                        continue;
                    }

                    Assert.That(prefabTextColors, Contains.Key(text.name));
                    if (text.name == "DescriptionText_TMP")
                    {
                        continue;
                    }

                    Assert.That(text.color, Is.EqualTo(prefabTextColors[text.name]),
                        $"{text.name} color should stay authored by the prefab.");
                }

                Assert.That(FindText(slot.transform, "CardNameText_TMP").text, Is.EqualTo("Move A"));
                Assert.That(FindText(slot.transform, "TypeText_TMP").text, Is.EqualTo("이동"));
                Assert.That(FindText(slot.transform, "DescriptionText_TMP").text, Is.EqualTo("Runtime move description"));
                // 판이 세 갈래 모두 다크로 통일되면서(cs:936) 이동 카드의 검은 글자는 크림 판
                // 시절의 잔재가 됐다 — 다크 판 위에서는 읽히지 않는다.
                Assert.That(FindText(slot.transform, "DescriptionText_TMP").color, Is.EqualTo(Color.white),
                    "다크 판으로 통일했으므로 이동 카드 설명 글자는 흰색이어야 한다.");

                // 이동·행동이 같은 출처(CardFront.prefab의 기본값)를 상속하는지 저작 수준에서 잠근다.
                // 한쪽만 오버라이드로 갈라진 것이 애초에 이 결함의 원인이었다.
                var actionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ActionCardFrontPrefabPath);
                Assert.That(actionPrefab, Is.Not.Null);
                var moveDescription = prefab.GetComponentsInChildren<TMPro.TMP_Text>(includeInactive: true)
                    .Single(text => text.name == "DescriptionText_TMP");
                var actionDescription = actionPrefab.GetComponentsInChildren<TMPro.TMP_Text>(includeInactive: true)
                    .Single(text => text.name == "DescriptionText_TMP");
                Assert.That(moveDescription.color, Is.EqualTo(actionDescription.color),
                    "이동·행동 카드의 설명 글자 색은 같은 기본값에서 와야 한다 — 한쪽만 덮으면 판 통일이 깨진다.");
                Assert.That(FindText(slot.transform, "CostText_TMP").text, Is.EqualTo("2"),
                    "CostText_TMP should reflect the runtime card cost while keeping prefab-authored layout/color.");
                Assert.That(FindImage(slot.transform, "Card_Illust").sprite, Is.EqualTo(defaultIllustration),
                    "Card_Illust should reflect the runtime card kind while keeping prefab-authored layout/color.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(harness.Root);
            }
        }

        [Test]
        public void RefreshHandCardsAppliesCrashSafeColorTintToUnavailableCards()
        {
            var harness = CreateHarness();
            try
            {
                var hand = new[]
                {
                    new CombatCardSnapshot("move-a", CombatCardKind.Move, "Move A", "Runtime move description", 1, false, false, CombatCardStatusText.NotEnoughKi, cost: 2, instanceId: "move-instance-a")
                };

                harness.View.RefreshHandCards(hand, null, null, _ => { });

                var slot = harness.MoveRoot.GetComponentsInChildren<HandCardInteraction>(includeInactive: false)
                    .Single();
                var feedback = slot.GetComponent<CardUnavailableFeedback>();
                Assert.That(feedback, Is.Not.Null);
                Assert.That(feedback.IsUnavailable, Is.True);

                // Crash-safe dimming (cs:647): grayscale shader and sprite-copy overlays crashed on the target
                // GPU (see CardUnavailableFeedback's history comment), so unavailable cards are darkened by a
                // black translucent overlay. Authored card visuals must stay untouched.
                var illust = FindImage(slot.transform, "Card_Illust");
                var frame = FindImage(slot.transform, "Card_Frame_Overlay");
                foreach (var image in new[] { illust, frame })
                {
                    Assert.That(image, Is.Not.Null);
                    AssertColorApproximately(image.color, prefabColor(image.name),
                        $"{image.name} must keep its authored color; dimming happens via the overlay, not colour tints.");
                }

                var dimOverlay = FindImage(slot.transform, DimOverlayName);
                Assert.That(dimOverlay.gameObject.activeSelf, Is.True,
                    "Dim overlay must be active while the card is unavailable.");
                Assert.That(dimOverlay.raycastTarget, Is.False, "Dim overlay must not block CardLane clicks.");
                // ⚠️ Image.color를 본다. 한때 canvasRenderer.GetColor()를 봤는데, 그건 구현이 런타임에
                //    canvasRenderer.SetColor로 칠하던 시절의 채널이다. 그 방식은 빌드에서 렌더되지 않아
                //    폐기됐고(CardUnavailableFeedback 주석 참조) 지금은 프리팹에 저작된 색을 쓴다.
                //    Image.color는 캔버스 리빌드를 거쳐야 CanvasRenderer로 내려가는데 EditMode 테스트에는
                //    그 리빌드가 없어서, 옛 단언은 항상 기본값 흰색을 집어 실패한다.
                AssertColorApproximately(dimOverlay.color, DimOverlayColor,
                    "Dim overlay must carry the shared dim colour (prefab-authored, or set once on the runtime fallback).");

                Assert.That(slot.GetComponentsInChildren<Image>(includeInactive: true)
                        .Any(image => image.name == "GrayscaleOverlay"),
                    Is.False,
                    "Unavailable dimming must not spawn wash overlay objects (sprite-copy overlays crashed the target GPU).");
                Assert.That(illust.material.shader.name, Is.Not.EqualTo("SeoulPlayup/UI/Grayscale"),
                    "Card images must not use the crash-prone grayscale shader material.");

                var reasonText = FindText(slot.transform, "UnavailableReason_TMP");
                Assert.That(reasonText.raycastTarget, Is.False, "Unavailable reason text must not block CardLane clicks.");

                // Hiding the card (empty hand) must deactivate the overlay and leave authored colors intact.
                harness.View.RefreshHandCards(new CombatCardSnapshot[0], null, null, _ => { });
                Assert.That(dimOverlay.gameObject.activeSelf, Is.False,
                    "Dim overlay must deactivate once the card is no longer shown as unavailable.");
                AssertColorApproximately(illust.color, prefabColor("Card_Illust"),
                    "Card_Illust color should stay authored once the card is no longer shown as unavailable.");
                AssertColorApproximately(frame.color, prefabColor("Card_Frame_Overlay"),
                    "Card_Frame_Overlay color should stay authored once the card is no longer shown as unavailable.");

                Color prefabColor(string imageName)
                {
                    // Move cards instantiate the move-authored CardFront variant, so authored colors come from it.
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MoveCardFrontPrefabPath);
                    return prefab.GetComponentsInChildren<Image>(includeInactive: true)
                        .Single(image => image.name == imageName)
                        .color;
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(harness.Root);
            }
        }

        // Mirror CardUnavailableFeedback's private DimOverlayName/DimOverlayColor — the code constants are
        // private on purpose (prefab-authored and runtime-added feedback must dim identically), so the
        // contract values live here.
        private const string DimOverlayName = "UnavailableDim_Overlay";
        // 알파는 0.85다. 한때 0.75였고 구현이 0.85로 옮겨간 뒤에도 여기가 안 따라와서, 채널만 고쳤을 때
        // 알파에서 다시 깨졌다. 값을 바꿀 때는 CardUnavailableFeedback.DimOverlayColor와
        // CardFront.prefab의 UnavailableDim_Overlay 직렬화 색까지 **세 곳**을 같이 맞출 것.
        private static readonly Color DimOverlayColor = new Color(0f, 0f, 0f, 0.85f);

        private static void AssertColorApproximately(Color actual, Color expected, string message)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.005f), message);
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.005f), message);
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.005f), message);
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.005f), message);
        }

        [Test]
        public void UnavailableCardClickShowsReasonWithoutInvokingUseHandler()
        {
            EnsureEventSystem();
            var harness = CreateHarness();
            try
            {
                var hand = new[]
                {
                    new CombatCardSnapshot("move-a", CombatCardKind.Move, "Move A", "Runtime move description", 1, false, false, CombatCardStatusText.NotEnoughKi, cost: 2, instanceId: "move-instance-a")
                };
                var used = false;

                harness.View.RefreshHandCards(hand, null, null, _ => used = true);

                var slot = harness.MoveRoot.GetComponentsInChildren<HandCardInteraction>(includeInactive: false)
                    .Single();
                slot.OnPointerClick(new PointerEventData(EventSystem.current));

                Assert.That(used, Is.False);
                var feedback = slot.GetComponent<CardUnavailableFeedback>();
                Assert.That(feedback.CurrentReason, Is.EqualTo("전투가 초기화되지 않았습니다"));
                var reasonText = FindText(slot.transform, "UnavailableReason_TMP");
                Assert.That(reasonText.gameObject.activeSelf, Is.True);
                Assert.That(reasonText.text, Is.EqualTo("전투가 초기화되지 않았습니다"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(harness.Root);
                DestroyEventSystem();
            }
        }

        [Test]
        public void GetUnavailableReasonMapsStatusEffectStatusesToKorean()
        {
            // Movement phase: a move card grayed out by 속박/기절 must surface the matching Korean reason,
            // travelling the same Status -> reason path as 기력 부족 / 사거리 부족.
            var movementState = CombatState.CreateDefaultDemo();
            Assert.That(movementState.Phase, Is.EqualTo(CombatPhase.PlayerMovement));

            Assert.That(
                InvokeGetUnavailableReason(MakeUnusableMoveSnapshot(CombatCardStatusText.Immobilized), movementState),
                Is.EqualTo("속박 상태입니다"));
            Assert.That(
                InvokeGetUnavailableReason(MakeUnusableMoveSnapshot(CombatCardStatusText.Stunned), movementState),
                Is.EqualTo("기절 상태입니다"));
            Assert.That(
                InvokeGetUnavailableReason(MakeUnusableMoveSnapshot(CombatCardStatusText.MomentumLocked), movementState),
                Is.EqualTo("추진력 효과로 사용 불가"));

            // Action phase: a stunned action card surfaces the 기절 reason as well.
            var actionState = CombatState.CreateDefaultDemo();
            Assert.That(actionState.EndAction(), Is.True);
            Assert.That(
                InvokeGetUnavailableReason(MakeUnusableActionSnapshot(CombatCardStatusText.Stunned), actionState),
                Is.EqualTo("기절 상태입니다"));
        }

        [Test]
        public void GetUnavailableReasonMapsStatusCardSealAndDisarmToDedicatedSentences()
        {
            // 상태 카드(X01~X03) · 봉인(C-16) · 무장 해제(C-5)는 한때 맨 아래 폴백으로 새어 라벨 문자열이
            // 그대로 사유로 떴다. 전용 문장을 확인한다.
            var movementState = CombatState.CreateDefaultDemo();
            Assert.That(movementState.Phase, Is.EqualTo(CombatPhase.PlayerMovement));

            // ⚠️상태 카드·봉인은 '행동 카드'라서 이동 페이즈에는 "행동 페이즈에 사용할 수 있습니다"가
            //   먼저 잡힐 자리에 있다. 그 카드들은 페이즈가 바뀌어도 못 쓰므로 그 문장이 뜨면 안 된다.
            Assert.That(
                InvokeGetUnavailableReason(MakeUnusableActionSnapshot(CombatCardStatusText.StatusCard), movementState),
                Is.EqualTo("낼 수 없는 카드입니다"));
            Assert.That(
                InvokeGetUnavailableReason(MakeUnusableActionSnapshot(CombatCardStatusText.Sealed), movementState),
                Is.EqualTo("봉인되어 사용할 수 없습니다"));
            // 봉인은 이동 카드도 잠근다(O-11).
            Assert.That(
                InvokeGetUnavailableReason(MakeUnusableMoveSnapshot(CombatCardStatusText.Sealed), movementState),
                Is.EqualTo("봉인되어 사용할 수 없습니다"));

            var actionState = CombatState.CreateDefaultDemo();
            Assert.That(actionState.EndAction(), Is.True);
            Assert.That(
                InvokeGetUnavailableReason(MakeUnusableActionSnapshot(CombatCardStatusText.Disarmed), actionState),
                Is.EqualTo("무장 해제 상태입니다"));
        }

        [Test]
        public void SealedCardShowsPrefabAuthoredRedCrossAndClearsWhenTheSealMoves()
        {
            // 빨간 X는 CardFront.prefab에 저작돼 있고 런타임은 SetActive로만 토글한다(dim과 같은 규약 —
            // 런타임 색 조작은 "빌드에서만" 실패한 이력이 있다. CardUnavailableFeedback 주석 참조).
            var harness = CreateHarness();
            try
            {
                var sealedHand = new[] { MakeUnusableActionSnapshot(CombatCardStatusText.Sealed) };
                harness.View.RefreshHandCards(sealedHand, null, null, _ => { });

                var slot = harness.ActionRoot.GetComponentsInChildren<HandCardInteraction>(includeInactive: false)
                    .Single();
                var feedback = slot.GetComponent<CardUnavailableFeedback>();
                Assert.That(feedback, Is.Not.Null);
                Assert.That(feedback.IsSealed, Is.True);

                var mark = slot.transform.Find(SealedMarkName);
                Assert.That(mark, Is.Not.Null,
                    $"{SealedMarkName} must be authored in CardFront.prefab — the runtime only toggles it.");
                Assert.That(mark.gameObject.activeSelf, Is.True, "봉인된 카드에는 빨간 X가 켜져야 한다.");

                var bars = mark.GetComponentsInChildren<Image>(includeInactive: true);
                Assert.That(bars, Has.Length.EqualTo(2), "X는 ±45° 막대 두 개로 저작돼 있다.");
                foreach (var bar in bars)
                {
                    Assert.That(bar.raycastTarget, Is.False, "봉인 표시가 카드 클릭을 막으면 안 된다.");
                    Assert.That(bar.color.r, Is.GreaterThan(0.5f), "봉인 표시는 빨간색이어야 한다.");
                    Assert.That(bar.color.g, Is.LessThan(0.35f));
                    Assert.That(bar.color.b, Is.LessThan(0.35f));
                }

                // dim 오버레이보다 뒤에 그려져야 검은 막에 묻히지 않는다.
                var dim = slot.transform.Find(DimOverlayName);
                Assert.That(dim, Is.Not.Null);
                Assert.That(mark.GetSiblingIndex(), Is.GreaterThan(dim.GetSiblingIndex()),
                    "봉인 X는 dim 오버레이 위에 그려져야 한다.");

                // ⚠️봉인은 매 턴 다른 카드로 옮겨 간다 — 갱신 한 번으로 꺼져야 한다.
                harness.View.RefreshHandCards(
                    new[] { MakeUnusableActionSnapshot(CombatCardStatusText.NotEnoughKi) }, null, null, _ => { });
                Assert.That(feedback.IsSealed, Is.False);
                Assert.That(mark.gameObject.activeSelf, Is.False,
                    "봉인이 다른 카드로 옮겨 가면 이 카드의 X는 꺼져야 한다(O-11 재선정).");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(harness.Root);
            }
        }

        [Test]
        public void StatusCardAuraStaysOffUntilTheStatusCardFrameArrives()
        {
            // 2026-07-31 사용자 확정: 불길한 아우라 OFF. 갤러리 판정에서 기각됐다 — 호버·선택·튜토리얼과
            // 같은 모양의 네온 외곽선이라 "불길함"이 아니라 "선택됨"으로 읽혔다. 상태 카드의 정체는
            // 전용 프레임이 말한다(백로그 §1-A 안 다). 이 테스트는 그 결정이 **조용히 되돌아오는 것**을 막는다.
            // 되살릴 때는 GameplayCardLaneView.ShowStatusCardAura를 true로 바꾸고 이 테스트를 뒤집을 것.
            var harness = CreateHarness();
            try
            {
                harness.View.RefreshHandCards(
                    new[] { MakeUnusableActionSnapshot(CombatCardStatusText.StatusCard) }, null, null, _ => { });
                var slot = harness.ActionRoot.GetComponentsInChildren<HandCardInteraction>(includeInactive: false)
                    .Single();
                var glow = slot.GetComponent<CardHoverGlow>();
                Assert.That(glow, Is.Not.Null);
                Assert.That(ReadOminousAura(glow), Is.False,
                    "상태 카드 아우라는 전용 프레임이 올 때까지 꺼져 있어야 한다.");

                harness.View.RefreshHandCards(
                    new[] { MakeUnusableActionSnapshot(CombatCardStatusText.Sealed) }, null, null, _ => { });
                Assert.That(ReadOminousAura(glow), Is.False,
                    "봉인은 애초에 아우라가 아니라 빨간 X로 표현한다.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(harness.Root);
            }
        }

        [Test]
        public void StatusCardFrameSwapsOnBindAndRestoresWhenANormalCardReturns()
        {
            // R-1(2026-08-01): 상태 카드 표현은 전용 프레임. 행동 슬롯은 actionCardSlotPrefab 하나에서
            // 풀링돼 상태 카드와 일반 카드가 같은 슬롯을 번갈아 쓰므로 변형 프리팹 분기가 아니라
            // 바인딩 시 Card_Frame_Overlay 스프라이트 스왑으로 붙는다. 같은 슬롯에 일반 카드가
            // 다시 앉으면 원래 프레임으로 돌아와야 한다(안 돌아오면 스왑이 아니라 오염이다).
            var harness = CreateHarness();
            try
            {
                harness.View.RefreshHandCards(
                    new[] { MakeUnusableActionSnapshot(CombatCardStatusText.NotEnoughKi) }, null, null, _ => { });
                var slot = harness.ActionRoot.GetComponentsInChildren<HandCardInteraction>(includeInactive: false)
                    .Single();
                var frame = slot.GetComponentsInChildren<Image>(true)
                    .Single(image => image.name == "Card_Frame_Overlay");
                var defaultSprite = frame.sprite;
                Assert.That(defaultSprite, Is.Not.Null, "행동 카드 슬롯은 기본 프레임을 가지고 있어야 한다.");

                var statusFrame = Sprite.Create(
                    Texture2D.blackTexture,
                    new Rect(0f, 0f, Texture2D.blackTexture.width, Texture2D.blackTexture.height),
                    new Vector2(0.5f, 0.5f));
                typeof(GameplayCardLaneView)
                    .GetField("statusCardFrameSprite",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .SetValue(harness.View, statusFrame);

                // 상태 여부는 저작이 정하는 스냅샷 축(IsStatusCard)이다. 예전에는 Status 문자열이
                // "사용 불가"인지로 대신 판정했는데 그 라벨은 손패에서만 찍혀, 덱 목록에서는 상태
                // 카드가 이동 카드로 보였다. 그래서 여기서도 라벨이 아니라 축을 세운다.
                harness.View.RefreshHandCards(
                    new[] { MakeUnusableActionSnapshot(CombatCardStatusText.StatusCard, isStatusCard: true) },
                    null, null, _ => { });
                Assert.That(frame.sprite, Is.SameAs(statusFrame),
                    "상태 카드가 앉은 슬롯의 프레임은 상태 카드 프레임으로 바뀌어야 한다.");

                harness.View.RefreshHandCards(
                    new[] { MakeUnusableActionSnapshot(CombatCardStatusText.NotEnoughKi) }, null, null, _ => { });
                Assert.That(frame.sprite, Is.SameAs(defaultSprite),
                    "일반 카드가 다시 앉으면 프레임은 원래 스프라이트로 돌아와야 한다.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(harness.Root);
            }
        }

        [Test]
        public void OminousAuraPlumbingSurvivesTheOffSwitch()
        {
            // 아우라 배관은 지우지 않고 상수 하나로 껐다(GameplayCardLaneView.ShowStatusCardAura).
            // 배관이 살아 있는지 여기서 직접 확인한다 — 안 그러면 되살리는 날 "켰는데 안 나온다"를
            // 처음부터 다시 디버깅하게 된다. ⚠️`isDisabled`보다 앞에 둬야 보인다는 것이 그때의 함정이었다.
            var go = new GameObject("glow-probe", typeof(RectTransform), typeof(CardHoverGlow));
            try
            {
                var glow = go.GetComponent<CardHoverGlow>();
                Assert.That(ReadOminousAura(glow), Is.False, "기본값은 꺼짐이어야 한다.");

                glow.SetOminousAura(true);
                Assert.That(ReadOminousAura(glow), Is.True,
                    "SetOminousAura(true)가 상태를 켜야 한다 — 배관이 살아 있어야 상수 한 줄로 되살릴 수 있다.");

                glow.SetOminousAura(false);
                Assert.That(ReadOminousAura(glow), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static bool ReadOminousAura(CardHoverGlow glow)
        {
            var field = typeof(CardHoverGlow).GetField(
                "isOminousAura",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.That(field, Is.Not.Null, "CardHoverGlow.isOminousAura seam should exist.");
            return (bool)field.GetValue(glow);
        }

        // CardUnavailableFeedback.SealedMarkName과 CardFront.prefab의 오브젝트 이름을 함께 잠근다.
        private const string SealedMarkName = "SealedMark_Overlay";

        private static CombatCardSnapshot MakeUnusableMoveSnapshot(string status)
        {
            return new CombatCardSnapshot("blocked-move", CombatCardKind.Move, "Blocked Move", "desc", 1, false, false, status, cost: 1, instanceId: "blocked-move-instance");
        }

        private static CombatCardSnapshot MakeUnusableActionSnapshot(string status)
        {
            return MakeUnusableActionSnapshot(status, isStatusCard: false);
        }

        private static CombatCardSnapshot MakeUnusableActionSnapshot(string status, bool isStatusCard)
        {
            return new CombatCardSnapshot("blocked-action", CombatCardKind.Attack, "Blocked Action", "desc", 1, false, false, status, cost: 1, instanceId: "blocked-action-instance", isStatusCard: isStatusCard);
        }

        private static string InvokeGetUnavailableReason(CombatCardSnapshot snapshot, CombatState state)
        {
            var method = typeof(GameplayCardLaneView).GetMethod(
                "GetUnavailableReason",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.That(method, Is.Not.Null, "GetUnavailableReason seam should exist.");
            return (string)method.Invoke(null, new object[] { snapshot, state });
        }

        [Test]
        public void RefreshHandCardsUsesMoveAndActionCardFrames()
        {
            var harness = CreateHarness();
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CardFrontPrefabPath);
                var moveFrame = AssetDatabase.LoadAssetAtPath<Sprite>(MoveCardFramePath);
                var actionFrame = AssetDatabase.LoadAssetAtPath<Sprite>(ActionCardFramePath);
                Assert.That(prefab, Is.Not.Null);
                Assert.That(moveFrame, Is.Not.Null);
                Assert.That(actionFrame, Is.Not.Null);
                var prefabDescriptionColor = prefab.GetComponentsInChildren<TMPro.TMP_Text>(includeInactive: true)
                    .Single(text => text.name == "DescriptionText_TMP")
                    .color;

                harness.View.RefreshHandCards(CreateHand(moveCount: 1, actionCount: 1), null, null, _ => { });

                var moveSlot = harness.MoveRoot.GetComponentsInChildren<HandCardInteraction>(includeInactive: false)
                    .Single();
                var actionSlot = harness.ActionRoot.GetComponentsInChildren<HandCardInteraction>(includeInactive: false)
                    .Single();

                Assert.That(FindImage(moveSlot.transform, "Card_Frame_Overlay").sprite, Is.SameAs(moveFrame),
                    "Move cards should use the move gameplay card frame on Card_Frame_Overlay.");
                Assert.That(FindImage(actionSlot.transform, "Card_Frame_Overlay").sprite, Is.SameAs(actionFrame),
                    "Action cards should use the action gameplay card frame on Card_Frame_Overlay.");
                Assert.That(FindText(actionSlot.transform, "DescriptionText_TMP").color, Is.EqualTo(prefabDescriptionColor),
                    "Action cards should keep the prefab-authored description color.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(harness.Root);
            }
        }

        [Test]
        public void RefreshHandCardsUsesCsvIllustrationIdBeforeKindFallback()
        {
            var harness = CreateHarness();
            try
            {
                var a01Illustration = AssetDatabase.LoadAssetAtPath<Sprite>(AttackA01IllustrationPath);
                Assert.That(a01Illustration, Is.Not.Null);

                var so = new SerializedObject(harness.View);
                var cardIllustrations = so.FindProperty("cardIllustrations");
                cardIllustrations.arraySize = 1;
                var binding = cardIllustrations.GetArrayElementAtIndex(0);
                binding.FindPropertyRelative("illustrationId").stringValue = "card_illust_A01";
                binding.FindPropertyRelative("sprite").objectReferenceValue = a01Illustration;
                so.ApplyModifiedPropertiesWithoutUndo();

                var hand = new[]
                {
                    new CombatCardSnapshot(
                        "A01",
                        CombatCardKind.Attack,
                        "Attack A01",
                        "Runtime attack description",
                        1,
                        true,
                        false,
                        "Ready",
                        illustrationId: "card_illust_A01")
                };

                harness.View.RefreshHandCards(hand, null, null, _ => { });

                var slot = harness.ActionRoot.GetComponentsInChildren<HandCardInteraction>(includeInactive: false)
                    .Single();
                Assert.That(FindImage(slot.transform, "Card_Illust").sprite, Is.EqualTo(a01Illustration),
                    "Card_Illust should resolve the CSV-provided illustrationId before falling back to card kind/default art.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(harness.Root);
            }
        }

        private static LaneHarness CreateHarness()
        {
            var root = new GameObject("CardLane Test Root", typeof(RectTransform), typeof(Canvas), typeof(GameplayCardLaneView));
            var moveRoot = CreateChild(root.transform, "MoveCards");
            var actionRoot = CreateChild(root.transform, "ActionCards");
            CreateChild(root.transform, "MoveSectionLabel");
            CreateChild(root.transform, "ActionSectionLabel");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CardFrontPrefabPath);
            var movePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MoveCardFrontPrefabPath);
            var actionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ActionCardFrontPrefabPath);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(movePrefab, Is.Not.Null);
            Assert.That(actionPrefab, Is.Not.Null);
            var view = root.GetComponent<GameplayCardLaneView>();
            view.Configure(
                prefab,
                new Vector2(200f, 320f),
                108f,
                32f,
                10f,
                28f,
                42f,
                0.55f,
                496f,
                false,
                132f,
                328f,
                14f,
                AssetDatabase.LoadAssetAtPath<Sprite>(DefaultCardIllustrationPath),
                moveCardPrefab: movePrefab,
                actionCardPrefab: actionPrefab);
            return new LaneHarness(root, view, moveRoot, actionRoot);
        }

        private static RectTransform CreateChild(Transform parent, string name)
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            return (RectTransform)child.transform;
        }

        private static void SetSlotHome(HandCardInteraction slot, Vector2 anchoredPosition, int siblingIndex)
        {
            var rect = (RectTransform)slot.transform;
            rect.sizeDelta = new Vector2(200f, 320f);
            rect.anchoredPosition = anchoredPosition;
            slot.SetHomePoseFromLayout(anchoredPosition, siblingIndex);
        }

        private static Vector2 ToScreenPoint(RectTransform root, Vector2 localPoint)
        {
            return RectTransformUtility.WorldToScreenPoint(null, root.TransformPoint(localPoint));
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            new GameObject("GameplayCardLaneViewTests EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        private static void DestroyEventSystem()
        {
            var eventSystem = EventSystem.current;
            if (eventSystem != null && eventSystem.name == "GameplayCardLaneViewTests EventSystem")
            {
                UnityEngine.Object.DestroyImmediate(eventSystem.gameObject);
            }
        }

        private static TMPro.TMP_Text FindText(Transform root, string name)
        {
            return root.GetComponentsInChildren<TMPro.TMP_Text>(includeInactive: true)
                .Single(text => text.name == name);
        }

        private static Image FindImage(Transform root, string name)
        {
            return root.GetComponentsInChildren<Image>(includeInactive: true)
                .Single(image => image.name == name);
        }

        private static CombatCardSnapshot[] CreateHand(int moveCount, int actionCount)
        {
            return Enumerable.Range(0, moveCount)
                .Select(i => new CombatCardSnapshot($"move-{i}", CombatCardKind.Move, $"Move {i}", "Move", 1, true, false, "Ready", instanceId: $"move-instance-{i}"))
                .Concat(Enumerable.Range(0, actionCount)
                    .Select(i => new CombatCardSnapshot($"action-{i}", CombatCardKind.Defend, $"Action {i}", "Action", 1, true, false, "Ready", instanceId: $"action-instance-{i}")))
                .ToArray();
        }

        private static int CountDirectSlots(RectTransform root)
        {
            return root.Cast<Transform>().Count(child => child.GetComponent<HandCardInteraction>() != null);
        }

        private readonly struct LaneHarness
        {
            public LaneHarness(GameObject root, GameplayCardLaneView view, RectTransform moveRoot, RectTransform actionRoot)
            {
                Root = root;
                View = view;
                MoveRoot = moveRoot;
                ActionRoot = actionRoot;
            }

            public GameObject Root { get; }
            public GameplayCardLaneView View { get; }
            public RectTransform MoveRoot { get; }
            public RectTransform ActionRoot { get; }
        }
    }
}
#endif



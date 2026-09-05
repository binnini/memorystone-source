#if UNITY_EDITOR
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEditor;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class DeckPileListOverlayViewTests
    {
        /// <summary>
        /// 저주 주입 연출(2026-09-01 #9)은 <b>배선이 없으면 조용히 접혀야 한다</b>. 하단 HUD는 런타임에
        /// 서므로 비행 기구·뽑을 더미 자리가 아직 없는 프레임이 실제로 존재하고, 그때 예외가 나면
        /// 규칙이 이미 끝낸 전투가 연출 때문에 멈춘다(전리품 비행과 같은 계약).
        /// </summary>
        [Test]
        public void CurseInjectionPresenterStaysSilentUntilItIsWired()
        {
            var host = new GameObject("curse-injection");
            try
            {
                var presenter = host.AddComponent<CurseCardInjectionPresenter>();
                Assert.That(presenter.IsReady, Is.False, "배선 전에는 준비되지 않은 것으로 답해야 한다.");

                var state = CombatState.CreateDefaultDemo();
                Assert.That(state.TryCreateCatalogCardSnapshot(
                    state.ActionDeck.Hand.First().Id, out var snapshot), Is.True,
                    "전제: 카드 스냅샷을 만들 수 있다.");

                Assert.DoesNotThrow(() => presenter.Play(snapshot),
                    "배선 없이 불려도 던지지 않는다 — 연출만 접힌다.");

                presenter.Configure(null, null);
                Assert.That(presenter.IsReady, Is.False, "null 배선도 준비되지 않은 것이다.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void CombatStateExposesDeckDrawAndDiscardListSnapshots()
        {
            var state = CreateState();
            var handBeforeDiscard = state.MovementDeck.Hand.FirstOrDefault();
            Assert.That(handBeforeDiscard, Is.Not.Null);
            Assert.That(state.MovementDeck.DiscardFromHand(handBeforeDiscard), Is.True);
            var handBeforeExile = state.ActionDeck.Hand.FirstOrDefault();
            Assert.That(handBeforeExile, Is.Not.Null);
            Assert.That(state.ActionDeck.PermanentRemoveFromHand(handBeforeExile), Is.True);

            var deckCards = state.GetDeckListCards();
            var drawCards = state.GetDrawPileCards();
            var discardCards = state.GetDiscardPileCards();
            var exileCards = state.GetExilePileCards();

            Assert.That(deckCards.Count, Is.EqualTo(
                state.MovementDeck.HandCount + state.MovementDeck.DrawCount + state.MovementDeck.DiscardCount +
                state.ActionDeck.HandCount + state.ActionDeck.DrawCount + state.ActionDeck.DiscardCount));
            Assert.That(drawCards.Count, Is.EqualTo(state.MovementDeck.DrawCount + state.ActionDeck.DrawCount));
            Assert.That(discardCards.Count, Is.EqualTo(state.MovementDeck.DiscardCount + state.ActionDeck.DiscardCount));
            Assert.That(exileCards.Count, Is.EqualTo(state.MovementDeck.RemovedCount + state.ActionDeck.RemovedCount));
            Assert.That(drawCards, Has.Some.Matches<CombatCardSnapshot>(card => card.Pile.Contains("draw") && !card.IsUsable));
            Assert.That(discardCards, Has.Some.Matches<CombatCardSnapshot>(card => card.Pile.Contains("discard") && card.IsDiscarded));
            Assert.That(exileCards, Has.Some.Matches<CombatCardSnapshot>(card => card.Pile.Contains("removed") && card.IsDiscarded));
            Assert.That(deckCards, Has.None.Matches<CombatCardSnapshot>(card => card.Pile.Contains("removed")),
                "The full deck list must not show exiled cards.");
            Assert.That(deckCards, Has.None.Matches<CombatCardSnapshot>(card => card.InstanceId == handBeforeExile.InstanceId),
                "The exiled card instance must be removed from the full deck list.");
            Assert.That(exileCards, Has.Some.Matches<CombatCardSnapshot>(card => card.InstanceId == handBeforeExile.InstanceId),
                "Exiled cards should remain visible through the dedicated exile pile list.");
        }

        [Test]
        public void DeckListSnapshotsPreserveOwnedCardInstanceMetadata()
        {
            var state = CreateUpgradedInstanceState();

            var cards = state.GetDeckListCards();

            Assert.That(cards, Has.Some.Matches<CombatCardSnapshot>(card =>
                card.Id == ApprovedCardCatalogFactory.MoveBasicId &&
                card.InstanceId == "owned-move-upgraded" &&
                card.UpgradeLevel == 2 &&
                card.IsTemporary));
            Assert.That(cards.Select(card => card.InstanceId).Distinct().Count(), Is.EqualTo(cards.Count),
                "Deck list should expose owned card instances, not collapse duplicate card ids.");
        }

        [Test]
        public void OverlayIsCreatedUnderGameplayLayersAndNotNacreRoot()
        {
            var gameplayLayers = CreateRect("Gameplay UI Layers");
            CreateAuthoredOverlayRoot(gameplayLayers);
            var legacyRoot = CreateRect("Legacy HUD Root");
            legacyRoot.SetParent(gameplayLayers, false);
            try
            {
                var overlay = DeckPileListOverlayView.FindOrCreate(gameplayLayers);

                Assert.That(overlay, Is.Not.Null);
                Assert.That(overlay.transform.parent, Is.EqualTo(gameplayLayers));
                Assert.That(overlay.transform.IsChildOf(legacyRoot), Is.False);
                Assert.That(overlay.name, Is.EqualTo(DeckPileListOverlayView.RootName));
            }
            finally
            {
                Object.DestroyImmediate(gameplayLayers.gameObject);
            }
        }

        [Test]
        public void OverlayOpensDeckDrawDiscardAndExileLists()
        {
            var gameplayLayers = CreateRect("Gameplay UI Layers");
            CreateAuthoredOverlayRoot(gameplayLayers);
            try
            {
                var state = CreateState();
                var overlay = DeckPileListOverlayView.FindOrCreate(gameplayLayers);
                overlay.Refresh(state, state.CreatePlayerStateSnapshot(), null);

                overlay.OpenDeckList();
                Assert.That(overlay.ActiveKind, Is.EqualTo(DeckPileListOverlayView.PileViewKind.Deck));
                Assert.That(overlay.gameObject.activeSelf, Is.True);
                AssertOverlayCardCount(overlay.gameObject, state.GetDeckListCards().Count);
                Assert.That(FindRect(overlay.gameObject, "Deck Pile List Overlay Sort Bar").gameObject.activeSelf, Is.True);
                AssertOverlayContains(overlay.gameObject, "\uB371 \uBAA9\uB85D");
                AssertOverlayContains(overlay.gameObject, "\uD68D\uB4DD\uC21C");
                AssertOverlayContains(overlay.gameObject, "\uCE74\uB4DC \uC720\uD615");

                overlay.OpenDrawPile();
                Assert.That(overlay.ActiveKind, Is.EqualTo(DeckPileListOverlayView.PileViewKind.DrawPile));
                Assert.That(overlay.gameObject.activeSelf, Is.True);
                AssertOverlayCardCount(overlay.gameObject, state.GetDrawPileCards().Count);
                Assert.That(FindRect(overlay.gameObject, "Deck Pile List Overlay Sort Bar").gameObject.activeSelf, Is.False);
                AssertOverlayContains(overlay.gameObject, "\uB4DC\uB85C\uC6B0 \uB354\uBBF8");

                overlay.OpenDiscardPile();
                Assert.That(overlay.ActiveKind, Is.EqualTo(DeckPileListOverlayView.PileViewKind.DiscardPile));
                Assert.That(overlay.gameObject.activeSelf, Is.True);
                AssertOverlayCardCount(overlay.gameObject, state.GetDiscardPileCards().Count);
                AssertOverlayContains(overlay.gameObject, "\uBC84\uB9B0 \uCE74\uB4DC \uB354\uBBF8");

                var exileCandidate = state.ActionDeck.Hand.FirstOrDefault();
                Assert.That(exileCandidate, Is.Not.Null);
                Assert.That(state.ActionDeck.PermanentRemoveFromHand(exileCandidate), Is.True);
                overlay.Refresh(state, state.CreatePlayerStateSnapshot(), null);
                overlay.OpenExilePile();
                Assert.That(overlay.ActiveKind, Is.EqualTo(DeckPileListOverlayView.PileViewKind.ExilePile));
                Assert.That(overlay.gameObject.activeSelf, Is.True);
                AssertOverlayCardCount(overlay.gameObject, state.GetExilePileCards().Count);
                AssertOverlayContains(overlay.gameObject, "\uC18C\uBA78 \uB354\uBBF8");
            }
            finally
            {
                Object.DestroyImmediate(gameplayLayers.gameObject);
            }
        }

        [Test]
        public void EmptyPileShowsMessageInsteadOfBlankScroll()
        {
            var gameplayLayers = CreateRect("Gameplay UI Layers");
            CreateAuthoredOverlayRoot(gameplayLayers);
            try
            {
                var state = CreateState();
                while (state.MovementDeck.DrawCount > 0)
                {
                    state.MovementDeck.Draw(1);
                }

                while (state.ActionDeck.DrawCount > 0)
                {
                    state.ActionDeck.Draw(1);
                }

                var overlay = DeckPileListOverlayView.FindOrCreate(gameplayLayers);
                overlay.Refresh(state, state.CreatePlayerStateSnapshot(), null);
                overlay.OpenDrawPile();

                var body = overlay.GetComponentsInChildren<TMP_Text>(true)
                    .FirstOrDefault(text => text.name == "Deck Pile List Overlay Empty Body");
                var scroll = FindRect(overlay.gameObject, "Deck Pile List Card Scroll");

                Assert.That(body, Is.Not.Null);
                Assert.That(body.gameObject.activeSelf, Is.True);
                Assert.That(body.text, Is.Not.Empty);
                Assert.That(scroll.gameObject.activeSelf, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(gameplayLayers.gameObject);
            }
        }

        [Test]
        public void OverlayCardsAreReadOnlyAndUseDeckCardView()
        {
            var gameplayLayers = CreateRect("Gameplay UI Layers");
            CreateAuthoredOverlayRoot(gameplayLayers);
            try
            {
                var state = CreateState();
                var overlay = DeckPileListOverlayView.FindOrCreate(gameplayLayers);
                overlay.Refresh(state, state.CreatePlayerStateSnapshot(), null);
                overlay.OpenDeckList();

                foreach (var card in GetActiveOverlayCards(overlay.gameObject))
                {
                    Assert.That(card.GetComponent<Button>(), Is.Null);
                    Assert.That(card.GetComponent<HandCardInteraction>(), Is.Null);
                    var group = card.GetComponent<CanvasGroup>();
                    Assert.That(group, Is.Not.Null);
                    Assert.That(group.interactable, Is.False);
                    Assert.That(group.blocksRaycasts, Is.False);
                    Assert.That(card.GetComponent<DeckPileOverlayCardView>(), Is.Not.Null);
                }
            }
            finally
            {
                Object.DestroyImmediate(gameplayLayers.gameObject);
            }
        }

        [Test]
        public void OverlayDefaultsDeckTmpToKoreanCapableFont()
        {
            var gameplayLayers = CreateRect("Gameplay UI Layers");
            CreateAuthoredOverlayRoot(gameplayLayers);
            try
            {
                var state = CreateState();
                var overlay = DeckPileListOverlayView.FindOrCreate(gameplayLayers);
                overlay.Refresh(state, state.CreatePlayerStateSnapshot(), null);
                overlay.OpenDeckList();

                var deckTexts = overlay.GetComponentsInChildren<TMP_Text>(true);
                Assert.That(deckTexts, Is.Not.Empty);
                Assert.That(deckTexts.All(text => FontRendersKorean(text.font)),
                    Is.True,
                    "Deck UI TMPs should render Korean via DNFForgedBlade — either as the display font or through its fallback table — so card names/descriptions do not fall back to a Latin-only font.");
            }
            finally
            {
                Object.DestroyImmediate(gameplayLayers.gameObject);
            }
        }

        [Test]
        public void OverlayRootExcludesSidebarWhenSidebarIsPresent()
        {
            var gameplayLayers = CreateRect("Gameplay UI Layers");
            CreateAuthoredOverlayRoot(gameplayLayers);
            var sidebar = CreateRect("Sidebar");
            // The overlay now resolves the sidebar by SidebarRootMarker component (rename-safe) rather than by
            // the "Sidebar" name, so the fixture tags the sidebar root the same way the shipped prefab does.
            sidebar.gameObject.AddComponent<SidebarRootMarker>();
            sidebar.SetParent(gameplayLayers, false);
            sidebar.sizeDelta = new Vector2(144f, 800f);
            try
            {
                var state = CreateState();
                var overlay = DeckPileListOverlayView.FindOrCreate(gameplayLayers);
                overlay.Refresh(state, state.CreatePlayerStateSnapshot(), null);
                overlay.OpenDeckList();

                var overlayRect = overlay.GetComponent<RectTransform>();
                Assert.That(overlayRect.offsetMin.x, Is.EqualTo(144f).Within(0.01f),
                    "Deck overlay should start after Sidebar width plus its configured gap.");
            }
            finally
            {
                Object.DestroyImmediate(gameplayLayers.gameObject);
            }
        }

        [Test]
        public void OverlayCardsUseCardFrontPrefabPresentation()
        {
            var gameplayLayers = CreateRect("Gameplay UI Layers");
            CreateAuthoredOverlayRoot(gameplayLayers);
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TestAssetPaths.CardFrontPrefab);
                Assert.That(prefab, Is.Not.Null);
                var state = CreateState();
                var overlay = DeckPileListOverlayView.FindOrCreate(gameplayLayers);
                overlay.Refresh(state, state.CreatePlayerStateSnapshot(), null);
                overlay.OpenDeckList();

                var firstCard = GetActiveOverlayCards(overlay.gameObject).FirstOrDefault();
                Assert.That(firstCard, Is.Not.Null);
                Assert.That(firstCard.GetComponent<HandCardInteraction>(), Is.Null,
                    "Deck overlay cards should use CardFront visuals without CardLane interaction behavior.");
                Assert.That(firstCard.GetComponentsInChildren<TMP_Text>(true)
                    .Any(text => text.name == "StatusText_TMP"), Is.True,
                    "Deck overlay cards should be instantiated from the CardFront prefab structure used by CardLane.");

                var content = FindRect(overlay.gameObject, "Content");
                var grid = content.GetComponent<GridLayoutGroup>();
                Assert.That(grid, Is.Not.Null);
                Assert.That(grid.cellSize, Is.EqualTo(new Vector2(240f, 384f)),
                    "Deck overlay grid cells should match CardFront_Move/CardFront_Action prefab root size so authored illustration/text proportions are preserved.");
            }
            finally
            {
                Object.DestroyImmediate(gameplayLayers.gameObject);
            }
        }

        [Test]
        public void DeckOverlayCardBindUpdatesTypeAndRangePresentation()
        {
            var root = CreateOverlayCardRoot();
            try
            {
                var view = root.AddComponent<DeckPileOverlayCardView>();

                view.Bind(new CombatCardSnapshot(
                    "A01",
                    CombatCardKind.Attack,
                    "휘둘러치기",
                    "피해",
                    3,
                    isUsable: false,
                    isDiscarded: false,
                    status: string.Empty,
                    range: 0));

                Assert.That(FindText(root, "TypeText_TMP").text, Is.EqualTo("공격"));
                Assert.That(FindText(root, "RangeText_TMP").text, Is.EqualTo("0"));
                Assert.That(FindText(root, "RangeText_TMP").gameObject.activeSelf, Is.True);
                Assert.That(FindImage(root, "Range_Icon").gameObject.activeSelf, Is.True);

                view.Bind(new CombatCardSnapshot(
                    "U01",
                    CombatCardKind.Utility,
                    "다시 뽑기",
                    "유틸",
                    0,
                    isUsable: false,
                    isDiscarded: false,
                    status: string.Empty,
                    range: 0,
                    playMode: CardPlayMode.Self,
                    targetMode: CardTargetMode.Self));

                Assert.That(FindText(root, "TypeText_TMP").text, Is.EqualTo("유틸리티"));
                Assert.That(FindText(root, "RangeText_TMP").gameObject.activeSelf, Is.False);
                Assert.That(FindImage(root, "Range_Icon").gameObject.activeSelf, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void DeckOverlayCardAppliesStatusFrameAndLabelInsteadOfMovePresentation()
        {
            // 회귀 방지: 상태 카드는 저작 EffectType이 Status인데도 CombatCardKind가 Move로
            // 뭉개진다(ToKind의 default 분기). 그래서 덱 목록은 이동 프리팹으로 상태 카드를 그렸고,
            // 프레임도 라벨도 이동 카드와 똑같이 나왔다(2026-08-02 실기 캡처로 확인).
            // 이제 판정은 스냅샷의 IsStatusCard 한 축에서만 온다 — Kind는 여전히 Move다.
            var root = CreateOverlayCardRoot();
            try
            {
                var view = root.AddComponent<DeckPileOverlayCardView>();
                var frame = FindImage(root, "Card_Frame_Overlay");
                var moveFrame = Sprite.Create(
                    Texture2D.whiteTexture,
                    new Rect(0f, 0f, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
                    new Vector2(0.5f, 0.5f));
                var statusFrame = Sprite.Create(
                    Texture2D.blackTexture,
                    new Rect(0f, 0f, Texture2D.blackTexture.width, Texture2D.blackTexture.height),
                    new Vector2(0.5f, 0.5f));

                frame.sprite = moveFrame;
                view.Bind(
                    new CombatCardSnapshot(
                        "X01",
                        CombatCardKind.Move,
                        "미세먼지",
                        "사용할 수 없습니다.",
                        0,
                        isUsable: false,
                        isDiscarded: false,
                        status: "Action draw",
                        range: 0,
                        isStatusCard: true),
                    statusFrame);

                Assert.That(frame.sprite, Is.SameAs(statusFrame),
                    "덱 목록의 상태 카드는 이동 프레임이 아니라 상태 프레임을 써야 한다.");
                Assert.That(FindText(root, "TypeText_TMP").text, Is.EqualTo("저주"),
                    "Kind가 Move로 뭉개져도 라벨은 '이동'이 아니라 '저주'여야 한다.");
                Assert.That(FindText(root, "CostText_TMP").gameObject.activeSelf, Is.False,
                    "낼 수 없는 카드의 코스트 0은 '공짜'로 읽히므로 목록에서도 감춰야 한다.");

                // 일반 카드는 프리팹이 들고 온 프레임을 그대로 둔다 — 목록 카드는 매번 새로 만들어지므로
                // 되돌릴 캐시가 없고, 건드리면 그게 곧 오염이다.
                frame.sprite = moveFrame;
                view.Bind(
                    new CombatCardSnapshot(
                        "M01",
                        CombatCardKind.Move,
                        "1칸 이동",
                        "최대 1칸 이동합니다.",
                        1,
                        isUsable: false,
                        isDiscarded: false,
                        status: "Movement draw",
                        range: 0),
                    statusFrame);

                Assert.That(frame.sprite, Is.SameAs(moveFrame),
                    "일반 카드의 프레임은 프리팹이 저작한 그대로여야 한다.");
                Assert.That(FindText(root, "TypeText_TMP").text, Is.EqualTo("이동"));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ViewportMaskKeepsOpaqueStencilGraphicSoCardsCanRender()
        {
            var gameplayLayers = CreateRect("Gameplay UI Layers");
            var overlay = CreateAuthoredOverlayRoot(gameplayLayers);
            try
            {
                var viewport = FindRect(overlay.gameObject, "Viewport");
                viewport.GetComponent<Image>().color = Color.clear;

                var state = CreateState();
                overlay.Refresh(state, state.CreatePlayerStateSnapshot(), null);
                overlay.OpenDeckList();

                var viewportImage = FindRect(overlay.gameObject, "Viewport").GetComponent<Image>();
                var mask = viewportImage.GetComponent<Mask>();
                Assert.That(mask, Is.Not.Null);
                Assert.That(mask.showMaskGraphic, Is.False);
                Assert.That(viewportImage.color.a, Is.GreaterThan(0f),
                    "UGUI Mask needs a non-transparent stencil graphic even when showMaskGraphic hides it.");
                AssertOverlayCardCount(overlay.gameObject, state.GetDeckListCards().Count);
            }
            finally
            {
                Object.DestroyImmediate(gameplayLayers.gameObject);
            }
        }

        [Test]
        public void OverlayCardGridShowsUpgradeTemporaryAndInstanceOwnedData()
        {
            var gameplayLayers = CreateRect("Gameplay UI Layers");
            CreateAuthoredOverlayRoot(gameplayLayers);
            try
            {
                var state = CreateUpgradedInstanceState();
                var overlay = DeckPileListOverlayView.FindOrCreate(gameplayLayers);
                overlay.Refresh(state, state.CreatePlayerStateSnapshot(), null);
                overlay.OpenDeckList();

                Assert.That(GetActiveOverlayCards(overlay.gameObject)
                    .All(card => card.GetComponent<DeckPileOverlayCardView>() != null), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(gameplayLayers.gameObject);
            }
        }

        [Test]
        public void PanelClickDoesNotDismissButBackdropAndCloseDo()
        {
            var gameplayLayers = CreateRect("Gameplay UI Layers");
            CreateAuthoredOverlayRoot(gameplayLayers);
            var eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            try
            {
                var state = CreateState();
                var overlay = DeckPileListOverlayView.FindOrCreate(gameplayLayers);
                overlay.Refresh(state, state.CreatePlayerStateSnapshot(), null);
                overlay.OpenDeckList();

                var panel = FindRect(overlay.gameObject, "Deck Pile List Overlay Panel");
                var backdropTarget = FindRect(overlay.gameObject, "Deck Pile List Overlay Backdrop Click Target");
                Assert.That(panel, Is.Not.Null);
                Assert.That(backdropTarget, Is.Not.Null);

                ExecuteEvents.ExecuteHierarchy(
                    panel.gameObject,
                    new PointerEventData(EventSystem.current),
                    ExecuteEvents.pointerClickHandler);
                Assert.That(overlay.IsOpen, Is.True);

                backdropTarget.GetComponent<Button>().onClick.Invoke();
                Assert.That(overlay.IsOpen, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(eventSystemObject);
                Object.DestroyImmediate(gameplayLayers.gameObject);
            }
        }

        [Test]
        public void SidebarDeckButtonHasNoNacreDependencyAndCanRouteExternally()
        {
            var sidebarRoot = CreateRect("Sidebar");
            var buttonObject = new GameObject("Sidebar Button deck", typeof(RectTransform), typeof(Image), typeof(Button), typeof(SidebarPanelButton));
            buttonObject.transform.SetParent(sidebarRoot, false);
            try
            {
                var binding = buttonObject.GetComponent<SidebarPanelButton>();
                binding.Bind("deck", null);
                var clicked = false;
                binding.BindExternalClickHandler(key =>
                {
                    clicked = key == "deck";
                    return clicked;
                });

                buttonObject.GetComponent<Button>().onClick.Invoke();

                Assert.That(clicked, Is.True);
                Assert.That(typeof(SidebarPanelButton).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Any(field => field.FieldType.Name == "NacreGameplayHudView"), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(sidebarRoot.gameObject);
            }
        }

        private static CombatState CreateState()
        {
            var state = new CombatState(
                CombatState.CreateDemoMap(2),
                new HexCoord(0, 0),
                new HexCoord(1, 0),
                CombatConfig.Default);
            var action = state.ActionDeck.Hand.FirstOrDefault();
            if (action != null)
            {
                state.ActionDeck.DiscardFromHand(action);
            }

            return state;
        }

        private static CombatState CreateUpgradedInstanceState()
        {
            var config = CombatConfig.Default;
            var catalog = ApprovedCardCatalogFactory.CreateApprovedCatalog(config);
            var playerDeck = new PlayerDeckData(
                new[]
                {
                    new PlayerCardInstanceData("owned-move-base", ApprovedCardCatalogFactory.MoveBasicId),
                    new PlayerCardInstanceData("owned-move-upgraded", ApprovedCardCatalogFactory.MoveBasicId, upgradeLevel: 2, isTemporary: true)
                },
                new[]
                {
                    new PlayerCardInstanceData("owned-attack", ApprovedCardCatalogFactory.AttackSweepId)
                });

            return new CombatState(
                CombatState.CreateDemoMap(2),
                new HexCoord(0, 0),
                new HexCoord(1, 0),
                config,
                cardCatalog: catalog,
                playerDeck: playerDeck);
        }


        private static DeckPileListOverlayView CreateAuthoredOverlayRoot(RectTransform gameplayLayers)
        {
            var existing = gameplayLayers.GetComponentsInChildren<DeckPileListOverlayView>(true).FirstOrDefault();
            if (existing != null)
            {
                return existing;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/Prototype/Deck Pile List Overlay Root.prefab");
            Assert.That(prefab, Is.Not.Null, "Deck pile overlay tests require the authored overlay prefab.");
            var overlayRoot = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            overlayRoot.transform.SetParent(gameplayLayers, false);
            overlayRoot.SetActive(false);
            return overlayRoot.GetComponent<DeckPileListOverlayView>();
        }
        private static RectTransform CreateRect(string name)
        {
            return new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        }

        private static GameObject CreateOverlayCardRoot()
        {
            var root = new GameObject("Deck Overlay Card Test Root", typeof(RectTransform));
            CreateText(root.transform, "CardNameText_TMP");
            CreateText(root.transform, "DescriptionText_TMP");
            CreateText(root.transform, "CostText_TMP");
            CreateText(root.transform, "TypeText_TMP");
            CreateText(root.transform, "RangeText_TMP");
            new GameObject("Range_Icon", typeof(RectTransform), typeof(Image)).transform.SetParent(root.transform, false);
            new GameObject("Cost_Icon", typeof(RectTransform), typeof(Image)).transform.SetParent(root.transform, false);
            new GameObject("Card_Frame_Overlay", typeof(RectTransform), typeof(Image)).transform.SetParent(root.transform, false);
            new GameObject("Card_Illust", typeof(RectTransform), typeof(Image)).transform.SetParent(root.transform, false);
            return root;
        }

        private static void CreateText(Transform parent, string name)
        {
            new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).transform.SetParent(parent, false);
        }

        private static bool FontRendersKorean(TMP_FontAsset font)
        {
            if (font == null)
            {
                return false;
            }

            if (font.name.Contains("DNFForgedBlade"))
            {
                return true;
            }

            var fallbacks = font.fallbackFontAssetTable;
            return fallbacks != null
                && fallbacks.Any(fallback => fallback != null && fallback.name.Contains("DNFForgedBlade"));
        }

        private static TMP_Text FindText(GameObject root, string objectName)
        {
            return root.GetComponentsInChildren<TMP_Text>(true)
                .FirstOrDefault(text => text.name == objectName);
        }

        private static Image FindImage(GameObject root, string objectName)
        {
            return root.GetComponentsInChildren<Image>(true)
                .FirstOrDefault(image => image.name == objectName);
        }

        private static void AssertOverlayContains(GameObject root, string expected)
        {
            var allText = string.Join("\n", root.GetComponentsInChildren<TMP_Text>(true).Select(text => text.text));
            Assert.That(allText, Does.Contain(expected));
        }

        private static void AssertOverlayCardCount(GameObject root, int expected)
        {
            Assert.That(GetActiveOverlayCards(root).Length, Is.EqualTo(expected));
        }

        private static RectTransform[] GetActiveOverlayCards(GameObject root)
        {
            return root.GetComponentsInChildren<RectTransform>(true)
                .Where(rect => rect.name.StartsWith("DeckPileOverlayCard_") && rect.gameObject.activeSelf)
                .ToArray();
        }

        private static RectTransform FindRect(GameObject root, string objectName)
        {
            return root.GetComponentsInChildren<RectTransform>(true)
                .FirstOrDefault(rect => rect.name == objectName);
        }
    }
}
#endif


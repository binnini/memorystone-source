#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Unity;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class PrototypeTestSceneTests
    {
        private const string ScenePath = TestAssetPaths.PrototypeTestScene;
        private const string GwangjinSourcePath = "Assets/Data/Map/Authoring/GwangjinGuSource.asset";
        private const string CardCatalogPath = TestAssetPaths.CardCatalogAsset;
        private const string CombatCatalogTextAssetSourcePath = "Assets/Data/Combat/Catalogs/CombatCatalogTextAssetSource.asset";
        private const string StartingDeckPath = "Assets/Data/Combat/Cards/Catalogs/StartingDeck_Prototype.asset";
        private const string SoundCatalogPath = TestAssetPaths.SoundCatalogAsset;
        private const string UnifiedUiRootName = "PrototypeTest Unified UI Root";
        private const string GameplayLayerRootName = "Gameplay UI Layers";
        private const string LegacyNacreCardFrameGuid = "a583ed6406c24077bb947098f4e04175";
        private const string DefaultCardFramePath = TestAssetPaths.ActionCardFrame;
        private const string MoveCardFramePath = TestAssetPaths.MoveCardFrame;
        private const string ActionCardFramePath = TestAssetPaths.ActionCardFrame;
        private const string CardFrontPrefabPath = TestAssetPaths.CardFrontPrefab;
        private const string MoveCardFrontPrefabPath = TestAssetPaths.MoveCardFrontPrefab;
        private const string ActionCardFrontPrefabPath = TestAssetPaths.ActionCardFrontPrefab;

        // PrototypeTest.unity is expensive to open (~14s each). Opening it once per fixture and
        // sharing it across every scene test cuts the file's cost from ~155s (11 repeated additive
        // opens) to a single open plus near-free per-test asserts. Sharing is safe because every
        // mutating test starts by rebuilding runtime state through MapCombatController.
        // InitializeIntegration() (fresh CombatState + guarded Ensure* singletons) and/or
        // GameplayHudBridge.RefreshForTests() (idempotent slot rebuilds), so test order never
        // leaves polluting runtime state behind. Read-only tests only inspect authored serialized
        // fields / scene structure, which InitializeIntegration does not touch. Each test re-fetches
        // roots so newly spawned objects stay visible.
        private Scene _previousScene;
        private Scene _sharedScene;

        [OneTimeSetUp]
        public void OpenSharedScene()
        {
            _previousScene = EditorSceneManager.GetActiveScene();
            _sharedScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        }

        [OneTimeTearDown]
        public void CloseSharedScene()
        {
            if (_sharedScene.IsValid())
            {
                // removeScene: true discards runtime mutations without saving, matching the prior
                // per-test CloseScene(scene, true) so a follow-up tests-run never aborts on a dirty scene.
                EditorSceneManager.CloseScene(_sharedScene, true);
            }

            if (_previousScene.IsValid())
            {
                EditorSceneManager.SetActiveScene(_previousScene);
            }
        }

        [SetUp]
        public void ResetSharedSceneRuntimeState()
        {
            if (!_sharedScene.IsValid())
            {
                return;
            }

            var roots = _sharedScene.GetRootGameObjects();

            // The shared scene persists the transient runtime UI tests spawn: EnsureCardSlotCount only
            // grows the CardLane slot pool (it never destroys excess slots), and overlays/panels stay
            // in whatever open state a prior test left them. A freshly opened scene has none of that, so
            // reset it here to give every test the same baseline regardless of run order.
            var cardLane = FindSceneComponent<GameplayCardLaneView>(roots);
            if (cardLane != null)
            {
                DestroyChildren(cardLane.MoveCardsRoot);
                DestroyChildren(cardLane.ActionCardsRoot);
            }

            foreach (var overlay in roots.SelectMany(root => root.GetComponentsInChildren<DeckPileListOverlayView>(true)))
            {
                overlay.gameObject.SetActive(false);
            }

            var choiceOverlay = FindSceneRectOrDefault(roots, "Choice Overlay Root");
            if (choiceOverlay != null)
            {
                choiceOverlay.gameObject.SetActive(false);
            }

            foreach (var sidebarController in roots.SelectMany(root => root.GetComponentsInChildren<SidebarCalloutPanelController>(true)))
            {
                sidebarController.HideAllPanels();
            }
        }

        private static void DestroyChildren(RectTransform root)
        {
            if (root == null)
            {
                return;
            }

            for (var i = root.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(root.GetChild(i).gameObject);
            }
        }

        [Category("ShippingData")]
        [Test]
        public void PrototypeTestSceneUsesCsvPlayerCombatProfile()
        {
            var roots = _sharedScene.GetRootGameObjects();
            var controller = FindSceneComponent<MapCombatController>(roots);

            Assert.That(controller, Is.Not.Null);

            var controllerSo = new SerializedObject(controller);
            Assert.That(controllerSo.FindProperty("playerCombatProfileId").stringValue,
                Is.EqualTo(PlayerCombatProfileCatalog.PrototypeProfileId),
                "PrototypeTest should use the CSV player profile that preserves its intentionally reduced vision range.");
            var textAssetSource = AssetDatabase.LoadAssetAtPath<CombatCatalogTextAssetSource>(CombatCatalogTextAssetSourcePath);
            Assert.That(controllerSo.FindProperty("catalogTextAssetSource").objectReferenceValue,
                Is.SameAs(textAssetSource),
                "PrototypeTest should use TextAsset-backed combat CSVs so player builds do not depend on loose Assets/Data files.");

            var playerProfile = PlayerCombatProfileCsvConverter.ConvertFile(CombatCsvPaths.PlayerCombatProfilesCsv)
                .GetProfileOrDefault(PlayerCombatProfileCatalog.PrototypeProfileId);
            Assert.That(playerProfile.MaxHp, Is.EqualTo(80),
                "PrototypeTest player health is sourced from the CSV player profile.");
            Assert.That(playerProfile.VisionRange, Is.EqualTo(3),
                "PrototypeTest's reduced vision range should be data-authored in P001_PROTOTYPE.");
            Assert.That(controllerSo.FindProperty("playerHp").intValue, Is.EqualTo(80),
                "Legacy fallback player health should stay aligned with the CSV profile.");
            Assert.That(controllerSo.FindProperty("enemyHp").intValue, Is.EqualTo(30),
                "PrototypeTest should keep the current ThreeEyeDog baseline health balance.");
            Assert.That(controllerSo.FindProperty("enemyAttackDamage").intValue, Is.EqualTo(5),
                "PrototypeTest should keep the current ThreeEyeDog baseline attack damage balance.");
        }

        [Category("ShippingData")]
        [Test]
        public void PrototypeTestSceneCombinesGwangjinApprovedCardsPlayerStateCameraAndAudio()
        {
            var roots = _sharedScene.GetRootGameObjects();
            var controller = FindSceneComponent<MapCombatController>(roots);
            var setup = FindSceneComponent<PlayerStateTestSceneSetup>(roots);
            var cameraPanel = FindSceneComponent<CameraLightTestPanel>(roots);
            var effectBridge = FindSceneComponent<PlayerStateEffectPresentationBridge>(roots);
            var unifiedUiRoot = roots.SingleOrDefault(root => root.name == UnifiedUiRootName);

            Assert.That(controller, Is.Not.Null);
            Assert.That(setup, Is.Not.Null, "Prototype scene must carry the PlayerState camera/lighting setup.");
            Assert.That(cameraPanel, Is.Not.Null, "CameraLightTest panel remains available for runtime tuning.");
            Assert.That(effectBridge, Is.Not.Null, "PlayerState effect bridge must remain wired.");
            Assert.That(unifiedUiRoot, Is.Not.Null, "PrototypeTest UI should be grouped under a single editable root.");

            var controllerSo = new SerializedObject(controller);
            Assert.That(controllerSo.FindProperty("sparseSource").objectReferenceValue,
                Is.SameAs(AssetDatabase.LoadAssetAtPath<HexSparseMapAuthoringSource>(GwangjinSourcePath)));
            var textAssetSource = AssetDatabase.LoadAssetAtPath<CombatCatalogTextAssetSource>(CombatCatalogTextAssetSourcePath);
            var cardCatalogAsset = AssetDatabase.LoadAssetAtPath<CardCatalogAsset>(CardCatalogPath);
            var startingDeckAsset = AssetDatabase.LoadAssetAtPath<PlayerStartingDeckAsset>(StartingDeckPath);
            Assert.That(textAssetSource, Is.Not.Null, "PrototypeTest should have one build-included TextAsset combat catalog source.");
            Assert.That(cardCatalogAsset, Is.Not.Null, "PrototypeTest should use the CSV-imported card catalog asset.");
            Assert.That(startingDeckAsset, Is.Not.Null, "PrototypeTest should use the authored CSV-id starting deck asset.");
            Assert.That(controllerSo.FindProperty("catalogTextAssetSource").objectReferenceValue,
                Is.SameAs(textAssetSource),
                "PrototypeTest should use TextAsset-backed combat CSVs so player builds do not depend on loose Assets/Data files.");
            Assert.That(textAssetSource.HasPlayerCombatProfiles, Is.True);
            Assert.That(textAssetSource.HasMonsterCatalog, Is.True);
            Assert.That(textAssetSource.CreatePlayerCombatProfileCatalog()
                    .GetProfileOrDefault(PlayerCombatProfileCatalog.PrototypeProfileId)
                    .VisionRange,
                Is.EqualTo(3));
            Assert.That(textAssetSource.CreateMonsterCatalog().TryGetEntry(CombatCatalogFactory.ThreeEyeDogMonsterId, out _),
                Is.True,
                "TextAsset monster catalog should include the PrototypeTest baseline monster used in player builds.");
            Assert.That(startingDeckAsset.CharacterId, Is.EqualTo("seorin"),
                "PrototypeTest should start from the playable main character Seorin's source-authored deck.");
            Assert.That(controllerSo.FindProperty("cardCatalogAsset").objectReferenceValue,
                Is.EqualTo(cardCatalogAsset));
            Assert.That(controllerSo.FindProperty("playerCombatProfileId").stringValue,
                Is.EqualTo(PlayerCombatProfileCatalog.PrototypeProfileId),
                "PrototypeTest should use the CSV player profile that preserves its intentionally reduced vision range.");
            Assert.That(controllerSo.FindProperty("startingDeckAsset").objectReferenceValue,
                Is.EqualTo(startingDeckAsset));
            var playerProfile = PlayerCombatProfileCsvConverter.ConvertFile(CombatCsvPaths.PlayerCombatProfilesCsv)
                .GetProfileOrDefault(PlayerCombatProfileCatalog.PrototypeProfileId);
            Assert.That(controllerSo.FindProperty("playerHp").intValue, Is.EqualTo(80),
                "Legacy fallback player health should stay aligned with the CSV profile.");
            Assert.That(playerProfile.MaxHp, Is.EqualTo(80),
                "PrototypeTest player health is sourced from the CSV player profile.");
            Assert.That(playerProfile.VisionRange, Is.EqualTo(3),
                "PrototypeTest's reduced vision range should be data-authored in P001_PROTOTYPE.");
            Assert.That(controllerSo.FindProperty("enemyHp").intValue, Is.EqualTo(30),
                "PrototypeTest should use the current ThreeEyeDog baseline health balance.");
            Assert.That(controllerSo.FindProperty("enemyAttackDamage").intValue, Is.EqualTo(5),
                "PrototypeTest should use the current baseline monster attack damage balance.");
            Assert.That(startingDeckAsset.MovementCards.Count, Is.GreaterThanOrEqualTo(3),
                "PrototypeTest movement deck should be inspectable and large enough to fill the authored hand size.");
            Assert.That(startingDeckAsset.ActionCards.Count, Is.GreaterThanOrEqualTo(5),
                "PrototypeTest action deck should be inspectable and large enough to fill the authored hand size.");
            Assert.That(controllerSo.FindProperty("soundCatalog").objectReferenceValue,
                Is.SameAs(AssetDatabase.LoadAssetAtPath<SoundCatalog>(SoundCatalogPath)));
            Assert.That(controllerSo.FindProperty("autoCreateAudioPresenter").boolValue, Is.True);

            var panelSo = new SerializedObject(cameraPanel);
            Assert.That(panelSo.FindProperty("applyInitialPresetsOnStart").boolValue, Is.False,
                "PlayerState setup owns initial camera/lighting; CameraLightTest remains manual.");

            var setupSo = new SerializedObject(setup);
            Assert.That(setupSo.FindProperty("applyOnStart").boolValue, Is.True);
            Assert.That(setupSo.FindProperty("useCustomInitialCameraFrame").boolValue, Is.True,
                "PrototypeTest setup should still own startup camera angle/zoom while MapCombatController owns automatic center offset calculation.");
            var customEuler = setupSo.FindProperty("customInitialCameraEulerAngles").vector3Value;
            Assert.That(customEuler.x, Is.EqualTo(45f).Within(0.01f));
            Assert.That(customEuler.y, Is.EqualTo(-30f).Within(0.01f));
            Assert.That(customEuler.z, Is.EqualTo(0f).Within(0.01f));
            var customOffset = setupSo.FindProperty("customInitialCameraPlayerOffset").vector3Value;
            Assert.That(customOffset.x, Is.EqualTo(0.0533f).Within(0.01f));
            Assert.That(customOffset.y, Is.EqualTo(9.8846f).Within(0.01f));
            Assert.That(customOffset.z, Is.EqualTo(-11.8429f).Within(0.01f));
            Assert.That(setupSo.FindProperty("customInitialCameraOrthographicSize").floatValue, Is.EqualTo(7f).Within(0.01f));

            Assert.That(controllerSo.FindProperty("autoCalculateCameraPlayerOffset").boolValue, Is.True,
                "PrototypeTest should center the player from camera angle and follow distance instead of hand-tuned xyz offsets.");
            Assert.That(controllerSo.FindProperty("cameraFollowDistance").floatValue, Is.EqualTo(12f).Within(0.01f));
            Assert.That(controllerSo.FindProperty("cameraFramingOffset").vector2Value, Is.EqualTo(Vector2.zero));

            controller.InitializeIntegration();
            setup.ApplyLowObliqueDefaults();

            Assert.That(controller.LoadedMap, Is.Not.Null);
            Assert.That(controller.LoadedMap.Count, Is.GreaterThan(0), "Prototype scene must load the configured GwangjinGuSource map.");
            Assert.That(controller.CameraEulerAngles.x, Is.EqualTo(45f).Within(0.01f));
            Assert.That(controller.CameraEulerAngles.y, Is.EqualTo(-30f).Within(0.01f));
            Assert.That(controller.AutoCalculateCameraPlayerOffset, Is.True);
            Assert.That(controller.CameraFollowDistance, Is.EqualTo(12f).Within(0.01f));
            Assert.That(controller.CameraFramingOffset, Is.EqualTo(Vector2.zero));
            Assert.That(controller.CameraPlayerOffset.x, Is.EqualTo(0.0533f).Within(0.01f),
                "Serialized manual offset stays available as the compatibility fallback.");
            Assert.That(controller.CameraPlayerOffset.y, Is.EqualTo(9.8846f).Within(0.01f));
            Assert.That(controller.CameraPlayerOffset.z, Is.EqualTo(-5f).Within(0.01f));
            Assert.That(controller.CameraOrthographicSize, Is.EqualTo(7f).Within(0.01f));
            Assert.That(controller.State.CardCatalog.SourceId, Is.EqualTo(cardCatalogAsset.SourceId));
            Assert.That(controller.State.CardCatalog.GetVisibleCatalogEntries().All(entry => entry.Status == SeoulPlayup.CardCore.CardCatalogStatus.Approved), Is.True);
            Assert.That(controller.State.CardCatalog.Entries.Where(entry => entry.Status == SeoulPlayup.CardCore.CardCatalogStatus.Draft).All(entry => !entry.VisibleInCatalog && !entry.IncludeInGameplayDecks), Is.True);
            Assert.That(controller.GetComponentInChildren<CombatAudioPresenter>(includeInactive: true), Is.Not.Null,
                "Controller initialization should create the bound CombatAudioPresenter.");
        }

        [Test]
        public void PrototypeTestSidebarUsesAnchoredResponsiveLayoutAndSidebarSprites()
        {
            var roots = _sharedScene.GetRootGameObjects();
            var gameplayLayers = FindSceneRect(roots, GameplayLayerRootName);
            var sidebar = FindSceneRect(roots, "Sidebar");
            Assert.That(gameplayLayers, Is.Not.Null);
            Assert.That(sidebar, Is.Not.Null);
            Assert.That(sidebar.IsChildOf(gameplayLayers), Is.True);
            Assert.That(sidebar.parent.name, Is.EqualTo("SidebarSystem"));
            Assert.That(sidebar.anchorMin, Is.EqualTo(new Vector2(0f, 0f)));
            Assert.That(sidebar.anchorMax, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(sidebar.pivot, Is.EqualTo(new Vector2(0f, 0.5f)));
            var layoutSettings = sidebar.GetComponent<SidebarLayoutSettings>();
            Assert.That(layoutSettings, Is.Not.Null,
                "Sidebar layout values should be adjustable directly from the Inspector.");
            Assert.That(sidebar.sizeDelta.x, Is.EqualTo(layoutSettings.SidebarWidth).Within(0.01f));
            Assert.That(sidebar.sizeDelta.y, Is.EqualTo(0f).Within(0.01f));
            var calloutSettings = sidebar.GetComponent<SidebarCalloutPanelSettings>();
            var controller = sidebar.GetComponent<SidebarCalloutPanelController>();
            var panelLayer = FindSceneRect(roots, "Sidebar Callout Panel Layer");
            Assert.That(calloutSettings, Is.Not.Null, "Sidebar callout panel visuals should be adjustable directly from the Inspector.");
            Assert.That(controller, Is.Not.Null, "Sidebar buttons should be wired through a callout panel controller.");
            Assert.That(panelLayer, Is.Not.Null);
            Assert.That(controller.PanelLayer, Is.SameAs(panelLayer));
            Assert.That(controller.Panels.Count(), Is.EqualTo(4));
            Assert.That(calloutSettings.PanelOverrides.Count(), Is.EqualTo(5),
                "Each sidebar callout panel should expose its own Inspector override entry.");

            foreach (var key in new[] { "currency", "bag", "deck", "relic_curse", "settings" })
            {
                Assert.That(calloutSettings.PanelOverrides.Any(entry => entry.Key == key), Is.True,
                    $"Callout panel {key} should have a dedicated Inspector override entry.");
                var button = FindSceneButton(roots, $"Sidebar Button {key}");
                Assert.That(button, Is.Not.Null, $"Sidebar should expose {key} as a button.");
                if (key == "currency")
                {
                    Assert.That(button.interactable, Is.False, "Currency should remain visible but not open a callout panel.");
                    Assert.That(FindSceneRect(roots, $"Sidebar Callout Panel {key}"), Is.Null);
                    continue;
                }

                var binding = button.GetComponent<SidebarPanelButton>();
                Assert.That(binding, Is.Not.Null, $"Sidebar button {key} should have a panel click binding.");
                Assert.That(binding.PanelKey, Is.EqualTo(key));
                Assert.That(binding.Controller, Is.SameAs(controller));

                var panel = FindSceneRect(roots, $"Sidebar Callout Panel {key}");
                Assert.That(panel, Is.Not.Null, $"Sidebar should have a generated callout panel for {key}.");
                Assert.That(panel.IsChildOf(panelLayer), Is.True);
                Assert.That(panel.sizeDelta, Is.EqualTo(calloutSettings.GetPanelSize(key)));
                Assert.That(panel.GetComponentsInChildren<TMP_Text>(true).Any(text => text.text == key), Is.False,
                    "Callout panels should not render their internal key/name as a panel title.");
                Assert.That(button.targetGraphic, Is.TypeOf<Image>());
                var iconImage = (Image)button.targetGraphic;
                Assert.That(iconImage.sprite, Is.SameAs(AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Art/UI/Sidebar/ui_sidebar_icon_{key}_default.png")));
                Assert.That(button.spriteState.highlightedSprite,
                    Is.SameAs(AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Art/UI/Sidebar/ui_sidebar_icon_{key}_hover.png")));
            }

            // 리뉴얼(2026-08-31): 가방·유물 콜아웃은 「머리글 / 격자 / 상세 줄」 한 골격을 공유한다.
            // 가방은 4열 8칸(멜빵 유물이 늘려도 두 줄 안에 선다), 유물은 5열 20칸.
            var bagPanel = FindSceneRect(roots, "Sidebar Callout Panel bag");
            Assert.That(bagPanel.GetComponentsInChildren<GridLayoutGroup>(true)
                .Any(grid => grid.constraintCount == 4), Is.True,
                "Bag panel should expose a four-column slot grid.");
            Assert.That(CountSlots(bagPanel, "Bag Slot "), Is.EqualTo(8));
            // 이름·설명은 패널 안이 아니라 커서 옆 툴팁이 말한다(사용자 확정 2026-08-31) — 판 안에
            // 상세 줄을 남겨 두면 판이 세로로 길어지고 시선이 아이콘과 판 바닥을 왕복해야 한다.
            Assert.That(bagPanel.GetComponentsInChildren<TMPro.TMP_Text>(true).Any(text => text.name == "Bag Detail"), Is.False);
            Assert.That(bagPanel.GetComponentsInChildren<RectTransform>(true).Any(rect => rect.name == "Bag Slot Icon"), Is.True,
                "가방 칸은 그림 한 장이다 — 그림 타깃이 저작돼 있어야 한다.");

            var relicPanel = FindSceneRect(roots, "Sidebar Callout Panel relic_curse");
            Assert.That(relicPanel.GetComponentsInChildren<GridLayoutGroup>(true)
                .Any(grid => grid.constraintCount == 5), Is.True,
                "Relic panel should expose a five-column chip grid.");
            Assert.That(CountSlots(relicPanel, "Relic Curse Slot "), Is.EqualTo(20));
            Assert.That(relicPanel.GetComponentsInChildren<TMPro.TMP_Text>(true).Any(text => text.name == "Relic Detail"), Is.False);
            Assert.That(FindSceneRect(roots, "Sidebar Callout Panel deck").GetComponentInChildren<ScrollRect>(true), Is.Not.Null);
            Assert.That(FindSceneRect(roots, "Sidebar Callout Panel status_effect"), Is.Null,
                "Status-effect UI moved to the CardLane StatusEffectDock; the sidebar panel should be gone.");

            controller.HideAllPanels();
            Assert.That(FindSceneRect(roots, "Sidebar Callout Panel bag").gameObject.activeSelf, Is.False);
            controller.ShowPanel("bag");
            Assert.That(FindSceneRect(roots, "Sidebar Callout Panel bag").gameObject.activeSelf, Is.True);
            Assert.That(FindSceneRect(roots, "Sidebar Callout Panel deck").gameObject.activeSelf, Is.False);
            controller.ShowPanel("deck");
            Assert.That(FindSceneRect(roots, "Sidebar Callout Panel bag").gameObject.activeSelf, Is.False);
            Assert.That(FindSceneRect(roots, "Sidebar Callout Panel deck").gameObject.activeSelf, Is.True);
        }

        [Test]
        public void PrototypeTestDeckPileOverlayIsExternalAndRoutedFromSidebarAndBottomPiles()
        {
            var roots = _sharedScene.GetRootGameObjects();
            var gameplayLayers = FindSceneRect(roots, GameplayLayerRootName);
            var controller = FindSceneComponent<MapCombatController>(roots);
            var bridge = FindSceneComponent<GameplayHudBridge>(roots);
            Assert.That(gameplayLayers, Is.Not.Null);
            Assert.That(FindSceneRectOrDefault(roots, "Nacre HUD Root"), Is.Null, "Nacre HUD Root should be removed from PrototypeTest.");
            Assert.That(controller, Is.Not.Null);
            Assert.That(bridge, Is.Not.Null);

            controller.ConfigurePresentationForTests(immediateSequences: true);
            controller.InitializeIntegration();
            bridge.RefreshForTests();

            var overlayRoot = FindSceneRect(roots, DeckPileListOverlayView.RootName);
            Assert.That(overlayRoot, Is.Not.Null);
            Assert.That(overlayRoot.parent, Is.SameAs(gameplayLayers));
            Assert.That(FindSceneRectOrDefault(roots, "Nacre Menu Popup Card Scroll"), Is.Null);
            Assert.That(typeof(SidebarPanelButton)
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Any(field => field.FieldType.Name == "NacreGameplayHudView"), Is.False);

            var overlay = overlayRoot.GetComponent<DeckPileListOverlayView>();
            var deckButton = FindSceneButton(roots, "Sidebar Button deck");
            Assert.That(deckButton, Is.Not.Null);
            deckButton.onClick.Invoke();
            Assert.That(overlay.ActiveKind, Is.EqualTo(DeckPileListOverlayView.PileViewKind.Deck));
            Assert.That(overlayRoot.GetComponentsInChildren<RectTransform>(true)
                .Count(rect => rect.name.StartsWith("DeckPileOverlayCard_") && rect.gameObject.activeSelf),
                Is.EqualTo(controller.State.GetDeckListCards().Count));

            var drawPileButton = FindSceneButton(roots, "DrawPileDock");
            Assert.That(drawPileButton, Is.Not.Null);
            drawPileButton.onClick.Invoke();
            Assert.That(overlay.ActiveKind, Is.EqualTo(DeckPileListOverlayView.PileViewKind.DrawPile));

            var discardPileButton = FindSceneButton(roots, "DiscardPileDock");
            Assert.That(discardPileButton, Is.Not.Null);
            discardPileButton.onClick.Invoke();
            Assert.That(overlay.ActiveKind, Is.EqualTo(DeckPileListOverlayView.PileViewKind.DiscardPile));

            var exilePileButton = FindSceneButton(roots, "ExilePileDock");
            Assert.That(exilePileButton, Is.Not.Null);
            exilePileButton.onClick.Invoke();
            Assert.That(overlay.ActiveKind, Is.EqualTo(DeckPileListOverlayView.PileViewKind.ExilePile));
        }

        [Test]
        public void PrototypeTestClickMoveDebugCanHighlightClickedTileThenConfirmMove()
        {
            var roots = _sharedScene.GetRootGameObjects();
            var controller = FindSceneComponent<MapCombatController>(roots);
            var setup = FindSceneComponent<PlayerStateTestSceneSetup>(roots);
            var cameraPanel = FindSceneComponent<CameraLightTestPanel>(roots);
            var atlas = roots.SelectMany(root => root.GetComponentsInChildren<AtlasTilePresentationView>(true)).SingleOrDefault();
            Assert.That(controller, Is.Not.Null);
            Assert.That(setup, Is.Not.Null);
            Assert.That(cameraPanel, Is.Not.Null);
            Assert.That(atlas, Is.Not.Null);

            controller.ConfigurePresentationForTests(immediateSequences: true);
            controller.InitializeIntegration();
            setup.ApplyLowObliqueDefaults();
            Assert.That(controller.BeginMoveSelection(), Is.True);

            var destination = controller.Reachable.Keys
                .Where(coord => coord != controller.State.PlayerCoord)
                .OrderBy(coord => coord.Q)
                .ThenBy(coord => coord.R)
                .First();
            var before = controller.State.PlayerCoord;

            Assert.That(controller.IsClickMoveDebugModeEnabled, Is.False);
            InvokePanel(cameraPanel, "ToggleClickMoveDebugMode");
            Assert.That(controller.IsClickMoveDebugModeEnabled, Is.True);

            controller.SelectClickMoveDebugTileForTests(destination);

            Assert.That(controller.State.PlayerCoord, Is.EqualTo(before), "Debug tile click should highlight first, not move immediately.");
            Assert.That(controller.HasClickMoveDebugSelection, Is.True);
            Assert.That(controller.ClickMoveDebugSelectedCoord, Is.EqualTo(destination));
            Assert.That(controller.GetCombatOverlayActiveCount(HexOverlayLayer.PlayerActionRange), Is.EqualTo(1));
            Assert.That(controller.LastInputMessage, Does.Contain(destination.ToString()));

            InvokePanel(cameraPanel, "ConfirmClickMoveDebugSelection");

            Assert.That(controller.State.PlayerCoord, Is.EqualTo(destination));
            Assert.That(controller.HasClickMoveDebugSelection, Is.False);
            Assert.That(controller.GetCombatOverlayActiveCount(HexOverlayLayer.PlayerActionRange), Is.Zero);
        }

        [Test]
        public void PrototypeTestBottomHudEndButtonOwnsCurrentPhase()
        {
            var roots = _sharedScene.GetRootGameObjects();
            var controller = FindSceneComponent<MapCombatController>(roots);
            var bridge = FindSceneComponent<GameplayHudBridge>(roots);
            Assert.That(controller, Is.Not.Null);
            Assert.That(bridge, Is.Not.Null);

            controller.ConfigurePresentationForTests(immediateSequences: true);
            controller.InitializeIntegration();
            bridge.RefreshForTests();

            var legacyTopHud = FindSceneRectOrDefault(roots, "TopHUD");
            var sidebar = FindSceneRect(roots, "Sidebar");
            Assert.That(legacyTopHud, Is.Null,
                "Legacy TopHUD should be removed once Sidebar/CardLane own gameplay HUD entrypoints.");
            Assert.That(sidebar, Is.Not.Null);
            Assert.That(sidebar.gameObject.activeInHierarchy, Is.True,
                "Sidebar should remain the visible entrypoint for map/bag/settings/deck information.");

            var bottomControlDock = FindSceneRect(roots, "BottomCombatControlDock");
            var bottomEndButton = FindSceneButton(roots, "BottomCombatEndActionButton");
            Assert.That(bottomControlDock, Is.Not.Null);
            Assert.That(bottomEndButton, Is.Not.Null);
            Assert.That(bottomControlDock.IsChildOf(FindSceneRect(roots, "CardLane")), Is.True,
                "Combat controls should be absorbed into CardLane/Bottom HUD, not a new gameplay root.");
            Assert.That(bottomControlDock.GetComponent<Image>()?.sprite, Is.Null,
                "Bottom combat control must use a default Unity Image instead of legacy HUD image sources.");
            Assert.That(ColorUtility.ToHtmlStringRGBA(bottomControlDock.GetComponent<Image>().color), Is.EqualTo("303468EE"));

            var legacyControlPanel = FindSceneRectOrDefault(roots, "ControlPanel");
            var endMoveButton = FindSceneButton(roots, "Button_EndMove");
            var endTurnButton = FindSceneButton(roots, "Button_EndTurn");
            Assert.That(legacyControlPanel, Is.Null,
                "Legacy ControlPanel should be removed once Bottom HUD owns end-action routing.");
            Assert.That(endMoveButton, Is.Null);
            Assert.That(endTurnButton, Is.Null);
            Assert.That(controller.State.Phase, Is.EqualTo(CombatPhase.PlayerMovement));
            Assert.That(bottomEndButton.interactable, Is.True);
            Assert.That(FindButtonLabel(bottomEndButton), Is.EqualTo("\uC774\uB3D9 \uC885\uB8CC"));

            bottomEndButton.onClick.Invoke();
            bridge.RefreshForTests();

            Assert.That(controller.State.Phase, Is.EqualTo(CombatPhase.PlayerAction));
            Assert.That(bottomEndButton.interactable, Is.True);
            Assert.That(FindButtonLabel(bottomEndButton), Is.EqualTo("\uD134 \uC885\uB8CC"));

            bottomEndButton.onClick.Invoke();
            bridge.RefreshForTests();

            Assert.That(controller.State.Phase, Is.EqualTo(CombatPhase.PlayerMovement));
        }

        [Test]
        public void PrototypeTestChoiceOverlayIsExternalAndInputContractIsPreserved()
        {
            var externalRoot = new GameObject("External Choice Panel Test Root", typeof(RectTransform));
            var externalChoicePanel = new GameObject("PlayerState Choice Card Panel", typeof(RectTransform));
            externalChoicePanel.transform.SetParent(externalRoot.transform, false);
            try
            {
                var roots = _sharedScene.GetRootGameObjects();
                var controller = FindSceneComponent<MapCombatController>(roots);
                var bridge = FindSceneComponent<GameplayHudBridge>(roots);
                var contract = FindSceneComponent<GameplaySceneContract>(roots);
                Assert.That(controller, Is.Not.Null);
                Assert.That(bridge, Is.Not.Null);
                Assert.That(contract, Is.Not.Null);
                Assert.That(FindSceneRectOrDefault(roots, "Nacre HUD Root"), Is.Null, "PrototypeTest should no longer carry Nacre HUD Root.");
                AssertSceneOwnsInputSystemEventSystem(roots);

                controller.ConfigurePresentationForTests(immediateSequences: true);
                controller.InitializeIntegration();
                if (controller.State.Phase == CombatPhase.PlayerMovement)
                {
                    Assert.That(controller.State.EndAction(), Is.True, "Choice cards are action cards and require the action phase.");
                    controller.State.ResolveMonsterMovement(); // -> PlayerAction (DEC-2026-07-03-02)
                }

                Assert.That(controller.State.DebugInjectCardIntoHand("A03"), Is.True);
                Assert.That(controller.BeginChoiceCardSelection("A03"), Is.True);
                bridge.RefreshForTests();

                var gameplayLayers = FindSceneRect(roots, GameplayLayerRootName);
                var choiceOverlay = FindSceneRectOrDefault(roots, "Choice Overlay Root");
                AssertLegacyPrototypeHudRemoved(roots);
                Assert.That(FindSceneRectOrDefault(roots, "Nacre HUD Root"), Is.Null, "Nacre HUD Root should be removed from PrototypeTest.");
                Assert.That(gameplayLayers, Is.Not.Null);
                Assert.That(choiceOverlay, Is.Not.Null, "Choice panel should render on an external gameplay overlay root.");
                Assert.That(choiceOverlay.parent, Is.SameAs(gameplayLayers));
                var choicePanel = choiceOverlay.GetComponentsInChildren<RectTransform>(true)
                    .FirstOrDefault(rect => rect.name == "PlayerState Choice Card Panel");
                Assert.That(choicePanel, Is.Not.Null);
                Assert.That(choicePanel.IsChildOf(choiceOverlay), Is.True);
                Assert.That(choicePanel.gameObject.activeInHierarchy, Is.True);
                Assert.That(choiceOverlay.GetComponentsInChildren<RectTransform>(true)
                    .Count(rect => rect.name == "PlayerState Choice Card Panel"), Is.EqualTo(1));
                Assert.That(externalChoicePanel.transform.parent, Is.EqualTo(externalRoot.transform),
                    "PrototypeTest bridge must not steal same-named choice panels from other loaded scenes/editor context.");

                var optionButtons = choicePanel.GetComponentsInChildren<Button>(includeInactive: true)
                    .Where(button => button.gameObject.activeInHierarchy)
                    .ToArray();
                Assert.That(optionButtons, Has.Length.GreaterThanOrEqualTo(2));
                // Option buttons are mini card fronts (cost/range/name/type/description texts), so
                // aggregate every TMP text per button: the heal option says "\u2026\uD68C\uBCF5\u2026" and the attack
                // option's description says "\u2026\uD53C\uD574\u2026" (both share the \uACF5\uACA9 type label).
                var optionTexts = optionButtons
                    .Select(button => string.Join("\n", button.GetComponentsInChildren<TMP_Text>(includeInactive: true).Select(text => text.text)))
                    .ToArray();
                Assert.That(optionTexts.Any(text => text.Contains("\uD68C\uBCF5")), Is.True);
                Assert.That(optionTexts.Any(text => text.Contains("\uD53C\uD574")), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(externalRoot);
            }
        }

        [Test]
        public void BridgeDoesNotCreateNacreHudFallbackAndUsesNeutralChoiceLookup()
        {
            // Type-level check instead of grepping the bridge source: as long as the Nacre HUD type
            // does not exist in the gameplay assembly, no code path can recreate or depend on it —
            // the compiler enforces what the old string asserts could only approximate.
            var gameplayAssembly = typeof(GameplayHudBridge).Assembly;
            Assert.That(gameplayAssembly.GetTypes().Any(type => type.Name == "NacreGameplayHudView"), Is.False,
                "The legacy Nacre HUD type must stay deleted; the bridge must not regain a fallback to it.");

            var choiceRootName = typeof(GameplayHudBridge)
                .GetField("ChoiceOverlayRootName", BindingFlags.NonPublic | BindingFlags.Static)
                ?.GetRawConstantValue() as string;
            Assert.That(choiceRootName, Is.EqualTo("Choice Overlay Root"),
                "Bridge must look up the neutral Choice Overlay Root name, not a legacy Nacre alias.");
        }

        [Test]
        public void PrototypeTestGameplayCardLaneAttackCardOutOfRangeIsVisibleButNotPlayable()
        {
            var roots = _sharedScene.GetRootGameObjects();
            var controller = FindSceneComponent<MapCombatController>(roots);
            var bridge = FindSceneComponent<GameplayHudBridge>(roots);
            Assert.That(controller, Is.Not.Null);
            Assert.That(bridge, Is.Not.Null);

            controller.ConfigurePresentationForTests(immediateSequences: true);
            controller.InitializeIntegration();
            if (controller.State.Phase == CombatPhase.PlayerMovement)
            {
                Assert.That(controller.State.EndAction(), Is.True, "Attack cards require the action phase.");
                controller.State.ResolveMonsterMovement(); // -> PlayerAction (DEC-2026-07-03-02)
            }

            MovePlayerOutsideAttackRange(controller);
            var attackCard = controller.State.GetCombatCards()
                .Where(card => card.Kind == CombatCardKind.Attack && !card.IsDiscarded)
                .FirstOrDefault(card => CombatCardStatusText.IsAttackOutOfRange(card.Status));
            Assert.That(attackCard.Kind, Is.EqualTo(CombatCardKind.Attack), "Test setup should produce an out-of-range attack card.");

            bridge.RefreshForTests();
            var cardSlot = FindGameplayCardSlot(roots, attackCard.Name);
            Assert.That(cardSlot, Is.Not.Null, "Gameplay CardLane should render the out-of-range attack card.");
            // Current contract: CardLane interaction gates on card.IsUsable, and an attack card
            // without a living monster in reach reports "사거리 부족" (not usable) — the card stays
            // visible in the lane but is not clickable, so no selection/preview starts from it.
            Assert.That(cardSlot.IsPlayable, Is.False, "Out-of-range attack cards render but are not playable.");

            var cardLane = FindSceneComponent<GameplayCardLaneView>(roots);
            Assert.That(cardLane, Is.Not.Null);
            cardLane.ApplyResolvedHover(RectTransformUtility.WorldToScreenPoint(null, ((RectTransform)cardSlot.transform).position));
            cardSlot.OnPointerClick(new PointerEventData(EventSystem.current));

            Assert.That(controller.IsAttackSelectionActive, Is.False);
            Assert.That(string.IsNullOrEmpty(controller.SelectedCardId), Is.True,
                "Clicking an unplayable out-of-range attack card must not begin a selection.");
        }


        [Test]
        public void PrototypeTestGameplayUiUsesCardFrontAndNoRuntimeLegacyNacrePrefabOrArtReferences()
        {
            var sceneText = File.ReadAllText(ScenePath);
            var cardFrontPrefabText = File.ReadAllText(CardFrontPrefabPath);
            var moveCardFrontPrefabText = File.ReadAllText(MoveCardFrontPrefabPath);
            var actionCardFrontPrefabText = File.ReadAllText(ActionCardFrontPrefabPath);
            var gameplayApplierSource = File.ReadAllText("Assets/Editor/UI/GameplayUiPrototypeTestApplier.cs");

            Assert.That(sceneText, Does.Not.Contain("Assets/Prefabs/UI/P0_Nacre"));
            Assert.That(sceneText, Does.Not.Contain("Assets/Prefabs/UI/_Legacy/Nacre"),
                "PrototypeTest runtime scene must not reference archived legacy Nacre prefabs.");
            Assert.That(sceneText, Does.Not.Contain(LegacyNacreCardFrameGuid),
                "PrototypeTest scene cards must not reference the removed legacy Nacre card frame sprite.");
            Assert.That(cardFrontPrefabText, Does.Not.Contain(LegacyNacreCardFrameGuid),
                "CardFront prefab must use the default card frame instead of the removed legacy Nacre card frame sprite.");
            Assert.That(cardFrontPrefabText, Does.Not.Contain("m_SourcePrefab"),
                "CardFront prefab should be a standalone gameplay card prefab, not a variant over deleted legacy UI sources.");
            var defaultFrame = AssetDatabase.LoadAssetAtPath<Sprite>(DefaultCardFramePath);
            Assert.That(defaultFrame, Is.Not.Null, "Default card frame sprite should be available for CardFront.");
            Assert.That(cardFrontPrefabText, Does.Contain(AssetDatabase.AssetPathToGUID(DefaultCardFramePath)),
                "CardFront prefab(개발 씬 전용)은 출하 행동 프레임을 그대로 써야 한다 — 랩에서만 딴 룩이 보이면 안 된다.");
            Assert.That(moveCardFrontPrefabText, Does.Contain(AssetDatabase.AssetPathToGUID(MoveCardFramePath)),
                "Move CardFront prefab should author card_frame_move without runtime sprite swapping.");
            Assert.That(actionCardFrontPrefabText, Does.Contain(AssetDatabase.AssetPathToGUID(ActionCardFramePath)),
                "Action CardFront prefab should author card_frame_action without runtime sprite swapping.");
            Assert.That(gameplayApplierSource, Does.Contain(CardFrontPrefabPath),
                "Gameplay UI authoring should use CardFront as the canonical CardLane slot prefab.");
            Assert.That(gameplayApplierSource, Does.Contain(MoveCardFrontPrefabPath),
                "Gameplay UI authoring should bind the move-card slot prefab explicitly.");
            Assert.That(gameplayApplierSource, Does.Contain(ActionCardFrontPrefabPath),
                "Gameplay UI authoring should bind the action-card slot prefab explicitly.");
            Assert.That(gameplayApplierSource, Does.Not.Contain("CardSlot_Nacre"));
            // Member-level checks instead of grepping the view source: the contract is that the
            // CardLane type owns no frame-sprite fields or style-swapping methods, which reflection
            // states directly and formatting changes cannot break.
            AssertCardLaneOwnsNoFrameSwappingMembers();
        }

        private static void AssertCardLaneOwnsNoFrameSwappingMembers()
        {
            const BindingFlags anyMember = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            var cardLaneType = typeof(GameplayCardLaneView);
            foreach (var forbiddenField in new[] { "moveCardFrameSprite", "actionCardFrameSprite" })
            {
                Assert.That(cardLaneType.GetField(forbiddenField, anyMember), Is.Null,
                    $"CardLane must not own a {forbiddenField} field; card frames are prefab-authored.");
            }

            foreach (var forbiddenMethod in new[] { "SetCardFrame", "SetDescriptionTextColor" })
            {
                Assert.That(cardLaneType.GetMethods(anyMember).Any(method => method.Name == forbiddenMethod), Is.False,
                    $"CardLane must not expose {forbiddenMethod}; runtime should preserve prefab-authored card art/text styling.");
            }
        }

        [Test]
        public void PrototypeTestGameplayCardLaneDoesNotDuplicateSlotsAcrossRefreshes()
        {
            var roots = _sharedScene.GetRootGameObjects();
            var controller = FindSceneComponent<MapCombatController>(roots);
            var bridge = FindSceneComponent<GameplayHudBridge>(roots);
            Assert.That(controller, Is.Not.Null);
            Assert.That(bridge, Is.Not.Null);

            controller.ConfigurePresentationForTests(immediateSequences: true);
            controller.InitializeIntegration();
            bridge.RefreshForTests();
            bridge.RefreshForTests();
            bridge.RefreshForTests();

            var cardLane = FindSceneComponent<GameplayCardLaneView>(roots);
            Assert.That(cardLane, Is.Not.Null);
            Assert.That(CountDirectCardSlots(cardLane.MoveCardsRoot, "MoveCard_"), Is.EqualTo(2));
            Assert.That(CountDirectCardSlots(cardLane.ActionCardsRoot, "ActionCard_"), Is.EqualTo(5));
        }

        [Test]
        public void PrototypeTestGameplayCardLaneCardsReceiveClicksWhenDeckOverlayClosed()
        {
            var roots = _sharedScene.GetRootGameObjects();
            var controller = FindSceneComponent<MapCombatController>(roots);
            var bridge = FindSceneComponent<GameplayHudBridge>(roots);
            Assert.That(controller, Is.Not.Null);
            Assert.That(bridge, Is.Not.Null);

            controller.ConfigurePresentationForTests(immediateSequences: true);
            controller.InitializeIntegration();
            if (controller.State.Phase == CombatPhase.PlayerMovement)
            {
                Assert.That(controller.State.EndAction(), Is.True, "Attack cards require the action phase.");
                controller.State.ResolveMonsterMovement(); // -> PlayerAction (DEC-2026-07-03-02)
            }

            bridge.RefreshForTests();
            var overlay = FindSceneComponent<DeckPileListOverlayView>(roots);
            Assert.That(overlay, Is.Not.Null);
            Assert.That(overlay.gameObject.activeSelf, Is.False,
                "Closed deck overlay must not leave its backdrop active over CardLane clicks.");

            var cardSlot = roots
                .SelectMany(root => root.GetComponentsInChildren<HandCardInteraction>(true))
                .FirstOrDefault(slot => slot.gameObject.activeInHierarchy && slot.IsPlayable);
            Assert.That(cardSlot, Is.Not.Null, "Test setup should expose at least one playable CardLane card.");

            var cardFrame = cardSlot.GetComponentsInChildren<Image>(true)
                .SingleOrDefault(image => image.name == "Card_Frame_Overlay");
            Assert.That(cardSlot.GetComponent<Image>(), Is.Null,
                "The active CardLane slot root should not regain a duplicate frame Image after slot cache rebuilds.");
            Assert.That(cardFrame?.raycastTarget, Is.True,
                "Card_Frame_Overlay must remain the CardLane raycast target after slot cache rebuilds.");
            Assert.That(cardFrame?.sprite,
                Is.SameAs(AssetDatabase.LoadAssetAtPath<Sprite>(ActionCardFramePath)),
                "Playable action CardLane slots should use the action gameplay card frame sprite on Card_Frame_Overlay.");
            Assert.That(cardSlot.transform.parent.name, Does.Contain("Cards"),
                "The click target should be a direct CardLane slot, not a stale nested duplicate or overlay child.");

            var visibleSlots = roots
                .SelectMany(root => root.GetComponentsInChildren<HandCardInteraction>(true))
                .Where(slot => slot.gameObject.activeInHierarchy)
                .ToArray();
            Assert.That(visibleSlots.Select(slot => Mathf.RoundToInt(((RectTransform)slot.transform).anchoredPosition.x)).Distinct().Count(),
                Is.GreaterThan(1), "Visible CardFront slots should be spread into a fan instead of stacked.");
            Assert.That(visibleSlots.Any(slot => Mathf.Abs(((RectTransform)slot.transform).localEulerAngles.z) > 0.1f),
                Is.True, "Visible CardFront slots should receive fan rotation.");

            var clickPosition = RectTransformUtility.WorldToScreenPoint(null, ((RectTransform)cardSlot.transform).position);
            var cardLane = FindSceneComponent<GameplayCardLaneView>(roots);
            Assert.That(cardLane, Is.Not.Null);
            cardLane.ApplyResolvedHover(clickPosition);
            Assert.That(cardSlot.IsHovering, Is.True, "Setup: the slot under the pointer should be hover-resolved.");
            var eventData = new PointerEventData(EventSystem.current) { position = clickPosition };
            ExecuteEvents.ExecuteHierarchy(cardSlot.gameObject, eventData, ExecuteEvents.pointerClickHandler);

            // §28 W1: 클릭은 더 이상 카드를 사용하지 않는다(사용은 드래그 아웃 단일 경로). 이 테스트의
            // 목적은 "닫힌 덱 오버레이가 CardLane 클릭을 가로막지 않는가"이므로, 클릭이 카드에 닿았다는
            // 증거는 컴포넌트 계약("클릭은 핸들러 호출 전에 호버를 걷어낸다")으로 확인한다.
            Assert.That(cardSlot.IsHovering, Is.False,
                "The click must reach the CardLane slot (a stale overlay backdrop would swallow it).");
            Assert.That(string.IsNullOrEmpty(controller.SelectedCardId), Is.True,
                "Click alone must not play/select a card — drag-out is the only use path (§28 W1).");
        }


        [Test]
        public void PrototypeTestExternalGameplayUiRefreshPreservesAuthoredMajorLayout()
        {
            var roots = _sharedScene.GetRootGameObjects();
            var controller = FindSceneComponent<MapCombatController>(roots);
            var bridge = FindSceneComponent<GameplayHudBridge>(roots);
            Assert.That(controller, Is.Not.Null);
            Assert.That(bridge, Is.Not.Null);

            controller.ConfigurePresentationForTests(immediateSequences: true);
            controller.InitializeIntegration();

            foreach (var rectName in new[] { "CardLane" })
            {
                var rect = FindSceneRect(roots, rectName);
                Assert.That(rect, Is.Not.Null, $"PrototypeTest external gameplay UI should contain {rectName}.");
                if (rectName == "CardLane")
                {
                    var gameplayLayers = FindSceneRect(roots, GameplayLayerRootName);
                    Assert.That(FindSceneRectOrDefault(roots, "Nacre HUD Root"), Is.Null,
                        "PrototypeTest should not keep the legacy Nacre HUD Root after external gameplay UI owns the HUD responsibilities.");
                    Assert.That(rect.parent, Is.SameAs(gameplayLayers),
                        "CardLane should live directly under Gameplay UI Layers so cards are managed by external gameplay UI.");
                    Assert.That(rect.GetComponent<GameplayCardLaneView>(), Is.Not.Null,
                        "CardLane should have its own UI script for card layout/refresh/reveal behavior.");
                    Assert.That(rect.GetComponent<BottomCardHudView>(), Is.Not.Null,
                        "CardLane should carry the migrated bottom-card HUD coordinator for piles, vitals, energy, and lane refresh.");
                    foreach (var dockName in new[] { "DrawPileDock", "HealthDock", "EnergyDock", "DiscardPileDock" })
                    {
                        Assert.That(rect.GetComponentsInChildren<RectTransform>(true).Any(child => child.name == dockName), Is.True,
                            $"Bottom card HUD should expose {dockName} for scene-view tuning.");
                    }
                    var cardLaneSo = new SerializedObject(rect.GetComponent<GameplayCardLaneView>());
                    Assert.That(cardLaneSo.FindProperty("autoLayoutEnabled").boolValue, Is.True,
                        "PrototypeTest CardLane should fan out CardFront cards at runtime.");
                    Assert.That(cardLaneSo.FindProperty("cardSlotPrefab").objectReferenceValue,
                        Is.SameAs(AssetDatabase.LoadAssetAtPath<GameObject>(CardFrontPrefabPath)));
                    Assert.That(cardLaneSo.FindProperty("moveCardSlotPrefab").objectReferenceValue,
                        Is.SameAs(AssetDatabase.LoadAssetAtPath<GameObject>(MoveCardFrontPrefabPath)),
                        "Move cards should instantiate the move-authored CardFront prefab instead of changing frame/text style in code.");
                    Assert.That(cardLaneSo.FindProperty("actionCardSlotPrefab").objectReferenceValue,
                        Is.SameAs(AssetDatabase.LoadAssetAtPath<GameObject>(ActionCardFrontPrefabPath)),
                        "Action cards should instantiate the action-authored CardFront prefab instead of changing frame style in code.");
                    AssertCardLaneOwnsNoFrameSwappingMembers();
                    foreach (var slot in rect.GetComponentsInChildren<RectTransform>(true)
                        .Where(child => child.name.StartsWith("Card_")
                            || child.name.StartsWith("MoveCard_")
                            || child.name.StartsWith("ActionCard_")))
                    {
                        Assert.That(slot.GetComponentInChildren<HandCardInteraction>(includeInactive: true), Is.Not.Null,
                            $"Scene CardLane slot {slot.name} should be owned by gameplay card interaction, not legacy Nacre HUD popup code.");
                    }
                }
                var authored = RectTransformSnapshot.Capture(rect);

                bridge.RefreshForTests();

                Assert.That(RectTransformSnapshot.Capture(rect), Is.EqualTo(authored),
                    $"Refresh must not overwrite authored RectTransform values for {rectName}.");
            }
        }

        private static void AssertLegacyPrototypeHudRemoved(GameObject[] roots)
        {
            foreach (var name in new[]
            {
                "Legacy Top HUD Backup",
                "Legacy Backup - Top HUD Text",
                GameplaySceneContract.PlayerUiRootName,
                GameplaySceneContract.DevUiRootName,
                GameplaySceneContract.HudLayerName,
                GameplaySceneContract.CardHandLayerName,
                GameplaySceneContract.SystemUiLayerName,
                GameplaySceneContract.CardRailRootName,
                "Nacre Backdrop Root",
                "Nacre Backdrop Canvas",
                "BG_Seoul"
            })
            {
                Assert.That(FindSceneObjectOrDefault(roots, name), Is.Null, $"Legacy Prototype HUD object should be removed: {name}");
            }
        }

        private static GameObject FindSceneObjectOrDefault(GameObject[] roots, string objectName)
        {
            return roots
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(transform => transform.name == objectName)
                ?.gameObject;
        }

        private readonly struct RectTransformSnapshot
        {
            private readonly Vector2 anchorMin;
            private readonly Vector2 anchorMax;
            private readonly Vector2 pivot;
            private readonly Vector2 anchoredPosition;
            private readonly Vector2 sizeDelta;
            private readonly Vector3 localScale;

            private RectTransformSnapshot(RectTransform rect)
            {
                anchorMin = rect.anchorMin;
                anchorMax = rect.anchorMax;
                pivot = rect.pivot;
                anchoredPosition = rect.anchoredPosition;
                sizeDelta = rect.sizeDelta;
                localScale = rect.localScale;
            }

            public static RectTransformSnapshot Capture(RectTransform rect) => new RectTransformSnapshot(rect);

            public override bool Equals(object obj)
            {
                return obj is RectTransformSnapshot other
                    && Approximately(anchorMin, other.anchorMin)
                    && Approximately(anchorMax, other.anchorMax)
                    && Approximately(pivot, other.pivot)
                    && Approximately(anchoredPosition, other.anchoredPosition)
                    && Approximately(sizeDelta, other.sizeDelta)
                    && Approximately(localScale, other.localScale);
            }

            public override int GetHashCode() => 0;

            private static bool Approximately(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 0.0001f;

            private static bool Approximately(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 0.0001f;
        }

        private static void InvokePanel(CameraLightTestPanel panel, string methodName)
        {
            var method = typeof(CameraLightTestPanel).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(panel, null);
        }

        private static T FindSceneComponent<T>(GameObject[] roots) where T : Component
        {
            return roots.SelectMany(root => root.GetComponentsInChildren<T>(true)).SingleOrDefault();
        }

        /// <summary>「이름 + 숫자」인 칸만 센다 — 칸 안의 자식(Bag Slot Icon 등)이 섞여 들어오지 않게.</summary>
        private static int CountSlots(RectTransform panel, string prefix)
        {
            return panel.GetComponentsInChildren<RectTransform>(true)
                .Count(rect => rect.name.StartsWith(prefix)
                    && rect.name.Length > prefix.Length
                    && char.IsDigit(rect.name[prefix.Length]));
        }

        private static RectTransform FindSceneRect(GameObject[] roots, string objectName)
        {
            return roots
                .SelectMany(root => root.GetComponentsInChildren<RectTransform>(true))
                .SingleOrDefault(rect => rect.name == objectName);
        }

        private static RectTransform FindSceneRectOrDefault(GameObject[] roots, string objectName)
        {
            return roots
                .SelectMany(root => root.GetComponentsInChildren<RectTransform>(true))
                .FirstOrDefault(rect => rect.name == objectName);
        }

        private static void MovePlayerOutsideAttackRange(MapCombatController controller)
        {
            var maxAttackRange = controller.State.GetCombatCards()
                .Where(card => card.Kind == CombatCardKind.Attack && !card.IsDiscarded)
                .Select(card => card.Range)
                .DefaultIfEmpty(1)
                .Max();
            var monsterCoords = controller.State.Monsters
                .Where(monster => !monster.IsDead)
                .Select(monster => monster.Coord)
                .ToArray();

            foreach (var coord in controller.LoadedMap.AllCells
                         .Select(cell => cell.Coord)
                         .Where(coord => monsterCoords.All(monsterCoord => coord.DistanceTo(monsterCoord) > maxAttackRange))
                         .OrderByDescending(coord => monsterCoords.Length == 0 ? 0 : monsterCoords.Min(monsterCoord => coord.DistanceTo(monsterCoord))))
            {
                if (controller.State.TryDebugMovePlayer(coord))
                {
                    return;
                }
            }

            Assert.Fail("Could not find a walkable PrototypeTest cell outside all attack ranges.");
        }


        private static int CountSceneRects(GameObject[] roots, string objectName)
        {
            return roots
                .SelectMany(root => root.GetComponentsInChildren<RectTransform>(true))
                .Count(rect => rect.name == objectName);
        }

        private static int CountSceneTexts(GameObject[] roots, string objectName)
        {
            return roots
                .SelectMany(root => root.GetComponentsInChildren<TMP_Text>(true))
                .Count(text => text.name == objectName);
        }

        private static HandCardInteraction FindGameplayCardSlot(GameObject[] roots, string cardName)
        {
            return roots
                .SelectMany(root => root.GetComponentsInChildren<HandCardInteraction>(true))
                .FirstOrDefault(slot => slot.GetComponentsInChildren<TMP_Text>(true)
                    .Any(text => text.text == cardName));
        }

        private static void AssertSceneOwnsInputSystemEventSystem(GameObject[] roots)
        {
            var eventSystems = roots.SelectMany(root => root.GetComponentsInChildren<EventSystem>(true)).ToArray();
            Assert.That(eventSystems, Has.Length.EqualTo(1), "PrototypeTest should keep one gameplay EventSystem.");
            Assert.That(eventSystems[0].GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>(), Is.Not.Null);
            Assert.That(eventSystems[0].GetComponent<StandaloneInputModule>(), Is.Null, "Legacy style-test StandaloneInputModule must not be imported into PrototypeTest.");
        }

        private static int CountDirectCardSlots(RectTransform root, string prefix)
        {
            if (root == null)
            {
                return 0;
            }

            return root.Cast<Transform>()
                .Count(child => child.name.StartsWith(prefix, System.StringComparison.Ordinal));
        }

        private static Button FindSceneButton(GameObject[] roots, string name)
        {
            return roots
                .SelectMany(root => root.GetComponentsInChildren<Button>(true))
                .SingleOrDefault(button => button.name == name);
        }

        // 2026-07-26: 종료 버튼이 아이콘 전용이 되면서 라벨이 버튼 밖(도크 자식)으로 나갔다.
        // 이 테스트가 지키려는 계약은 "버튼이 현재 페이즈 문구를 소유한다"는 **동작**이지
        // "라벨이 버튼의 자식"이라는 배치가 아니므로, 조회 범위를 도크까지 넓힌다.
        // 버튼 안을 먼저 보므로 라벨이 버튼 자식인 다른 버튼들의 동작은 그대로다.
        private static string FindButtonLabel(Button button)
        {
            var inside = button.GetComponentsInChildren<TMP_Text>(includeInactive: true)
                .FirstOrDefault()?.text;
            if (inside != null)
            {
                return inside;
            }

            // ⚠️ 비활성은 제외한다 — 도크의 첫 자식이 비활성 페이즈 텍스트(BottomCombatPhase_TMP)라
            //    includeInactive로 훑으면 그게 먼저 잡혀 "이동 페이즈"를 돌려준다.
            var dock = button.transform.parent;
            return dock == null
                ? null
                : dock.GetComponentsInChildren<TMP_Text>(includeInactive: false)
                    .Where(t => t.transform != dock)
                    .Select(t => t.text)
                    .FirstOrDefault(t => !string.IsNullOrEmpty(t));
        }
    }
}
#endif

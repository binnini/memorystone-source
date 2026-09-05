using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Unity;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class GameplaySceneContractTests
    {

        [Test]
        public void FactoryBuildsDocumentedScreenInventoryAndGameplayLayers()
        {
            var contract = GameplaySceneFactory.Build();
            try
            {
                Assert.That(contract.Canvas, Is.Not.Null);
                Assert.That(contract.HasRequiredScreen(GameplaySceneContract.ScreenTitleName), Is.True);
                Assert.That(contract.HasRequiredScreen(GameplaySceneContract.ScreenBriefingName), Is.True);
                Assert.That(contract.HasRequiredScreen(GameplaySceneContract.ScreenGameplayName), Is.True);
                Assert.That(contract.HasRequiredScreen(GameplaySceneContract.ScreenPauseName), Is.True);
                Assert.That(contract.HasRequiredScreen(GameplaySceneContract.ScreenGameOverName), Is.True);
                Assert.That(contract.HasRequiredScreen(GameplaySceneContract.ScreenClearName), Is.True);
                Assert.That(contract.ScreenRoots.Single(root => root.name == GameplaySceneContract.ScreenGameplayName).gameObject.activeSelf, Is.True);

                Assert.That(contract.HasRequiredGameplayLayer(GameplaySceneContract.WorldMapLayerName), Is.True);
                Assert.That(contract.HasRequiredGameplayLayer(GameplaySceneContract.LegendLayerName), Is.True);
                Assert.That(contract.HasRequiredGameplayLayer(GameplaySceneContract.HudLayerName), Is.True);
                Assert.That(contract.HasRequiredGameplayLayer(GameplaySceneContract.CardHandLayerName), Is.True);
                Assert.That(contract.HasRequiredGameplayLayer(GameplaySceneContract.SystemUiLayerName), Is.True);
                Assert.That(contract.HasRequiredGameplayLayer(GameplaySceneContract.DevOnlyLayerName), Is.True);
                Assert.That(contract.HasRequiredGameplayRoot(GameplaySceneContract.PlayerUiRootName), Is.True);
                Assert.That(contract.HasRequiredGameplayRoot(GameplaySceneContract.DevUiRootName), Is.True);
                Assert.That(contract.CardRailRoot, Is.Not.Null);
                Assert.That(contract.HasRequiredGameplayLayer(GameplaySceneContract.TacticalOverlayLayerName), Is.False);
                Assert.That(contract.HasRequiredGameplayLayer(GameplaySceneContract.EntityMarkerLayerName), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(contract.gameObject);
            }
        }

        [Test]
        public void FactoryUsesNacreGameplayContractForRemovedMinimapAndDevOnlyRestart()
        {
            var contract = GameplaySceneFactory.Build();
            try
            {
                Assert.That(contract.HasRequiredGameplayLayer("MiniMap Panel"), Is.False);
                Assert.That(FindChild(contract.transform, "MiniMap Panel"), Is.Null);

                var bridge = contract.gameObject.AddComponent<GameplayHudBridge>();
                bridge.RefreshForTests();

                var devLayer = contract.DevUiRoot.GetComponentsInChildren<RectTransform>(includeInactive: true)
                    .Single(rect => rect.name == GameplaySceneContract.DevOnlyLayerName);
                var restart = FindChild(contract.transform, "PlayerState Dev Restart Button")?.GetComponent<Button>();
                Assert.That(restart, Is.Not.Null, "Restart is retained as a dev-only control.");
                Assert.That(restart.transform.parent, Is.EqualTo(devLayer));
                Assert.That(FindChild(contract.transform, "PlayerState Playtest End Action Button"), Is.Null);
                Assert.That(FindChild(contract.transform, "PlayerState Playtest Restart Button"), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(contract.gameObject);
            }
        }

        [Test]
        public void FactoryUsesCurrentPrototypeCopyAndKeepsLegacyObjectiveOutOfPlayerFacingCopy()
        {
            var contract = GameplaySceneFactory.Build();
            try
            {
                AssertCurrentPrototypeCopy(contract);
            }
            finally
            {
                Object.DestroyImmediate(contract.gameObject);
            }
        }

        [Test]
        public void FactoryEncodesGameplayRemodelLayoutRules()
        {
            var contract = GameplaySceneFactory.Build();
            try
            {
                AssertGameplayRemodelLayout(contract);
            }
            finally
            {
                Object.DestroyImmediate(contract.gameObject);
            }
        }

        [Test]
        public void FactoryKeepsDecorativeHudPanelsFromBlockingMapClicks()
        {
            var contract = GameplaySceneFactory.Build();
            try
            {
                AssertOwnImageRaycastTarget(contract, GameplaySceneContract.WorldMapLayerName, false);
                AssertOwnImageRaycastTarget(contract, GameplaySceneContract.PlayerUiRootName, false);
                AssertOwnImageRaycastTarget(contract, GameplaySceneContract.DevUiRootName, false);
                AssertOwnImageRaycastTarget(contract, GameplaySceneContract.LegendLayerName, false);
                AssertOwnImageRaycastTarget(contract, GameplaySceneContract.HudLayerName, false);
                AssertOwnImageRaycastTarget(contract, GameplaySceneContract.TopLaneHudName, false);
                AssertOwnImageRaycastTarget(contract, GameplaySceneContract.PlayerStatusClusterName, false);
                AssertOwnImageRaycastTarget(contract, GameplaySceneContract.BagQuickSlotClusterName, false);
                AssertOwnImageRaycastTarget(contract, GameplaySceneContract.MapInfoClusterName, false);
                AssertOwnImageRaycastTarget(contract, GameplaySceneContract.SystemButtonClusterName, false);
                AssertOwnImageRaycastTarget(contract, GameplaySceneContract.RelicCurseRailName, false);
                AssertOwnImageRaycastTarget(contract, GameplaySceneContract.SystemUiLayerName, false);
                AssertOwnImageRaycastTarget(contract, GameplaySceneContract.DevOnlyLayerName, false);
                AssertOwnImageRaycastTarget(contract, GameplaySceneContract.DebugEvidenceName, false);
                AssertOwnImageRaycastTarget(contract, GameplaySceneContract.CardRailRootName, false);

                Assert.That(contract.MoveCardDrawer.GetComponent<Image>().raycastTarget, Is.True);
                Assert.That(contract.ActionCardDrawer.GetComponent<Image>().raycastTarget, Is.True);
                Assert.That(contract.GetComponentsInChildren<TMP_Text>(includeInactive: true).Any(text => text.raycastTarget), Is.False);
                Assert.That(contract.MoveCardDrawer.GetComponentsInChildren<HandCardInteraction>(includeInactive: true), Is.Not.Empty);
                Assert.That(contract.ActionCardDrawer.GetComponentsInChildren<HandCardInteraction>(includeInactive: true), Is.Not.Empty);
                AssertCardTitlesUseVisibleHeader(contract.MoveCardDrawer);
                AssertCardTitlesUseVisibleHeader(contract.ActionCardDrawer);
            }
            finally
            {
                Object.DestroyImmediate(contract.gameObject);
            }
        }

        [Test]
        public void FactorySeparatesPlayerAndDevRootsAndSupportsDrawerRailComparison()
        {
            var contract = GameplaySceneFactory.Build();
            try
            {
                Assert.That(contract.PlayerUiRoot, Is.Not.Null);
                Assert.That(contract.DevUiRoot, Is.Not.Null);
                Assert.That(contract.CardRailRoot, Is.Not.Null);
                Assert.That(IsDescendantOf(contract.MoveCardDrawer.transform, contract.PlayerUiRoot), Is.True);
                Assert.That(IsDescendantOf(contract.ActionCardDrawer.transform, contract.PlayerUiRoot), Is.True);
                Assert.That(IsDescendantOf(contract.BottomResourceText.transform, contract.PlayerUiRoot), Is.True);
                Assert.That(IsDescendantOf(contract.DevLogText.transform, contract.DevUiRoot), Is.True);
                Assert.That(IsDescendantOf(contract.DebugEvidenceText.transform, contract.DevUiRoot), Is.True);

                var bridge = contract.gameObject.AddComponent<GameplayHudBridge>();
                bridge.SetDevUiVisibleForTests(false);
                Assert.That(contract.DevUiRoot.gameObject.activeSelf, Is.False);
                Assert.That(contract.PlayerUiRoot.gameObject.activeSelf, Is.True);
                Assert.That(contract.MoveCardDrawer.gameObject.activeSelf, Is.True);

                bridge.SetDevUiVisibleForTests(true);
                Assert.That(contract.DevUiRoot.gameObject.activeSelf, Is.True);
                bridge.RefreshForTests();

                var layoutToggle = FindChild(contract.DevUiRoot, "PlayerState Dev Toggle Card Layout Button")?.GetComponent<Button>();
                Assert.That(layoutToggle, Is.Not.Null);
                Assert.That(layoutToggle.transform.parent, Is.EqualTo(contract.DevUiRoot.GetComponentsInChildren<RectTransform>(includeInactive: true).Single(rect => rect.name == GameplaySceneContract.DevOnlyLayerName)));
                Assert.That(layoutToggle.GetComponentInChildren<TMP_Text>(includeInactive: true).text, Is.EqualTo("Use Rail"));
            }
            finally
            {
                Object.DestroyImmediate(contract.gameObject);
            }
        }


        private static void AssertCurrentPrototypeCopy(GameplaySceneContract contract)
        {
            Assert.That(contract.BriefingText.text, Is.Not.Empty);
            Assert.That(contract.BriefingText.text, Does.Contain("MVP").Or.Contain("Seoul").Or.Contain("서울"));
            Assert.That(contract.DebugEvidenceText.text, Does.Contain("region_key=dong_seoul"));

            AssertNoLegacyCurrentObjectiveCopy(contract.BriefingText.text);
            AssertUsesKoreanFont(contract);
        }

        private static void AssertGameplayRemodelLayout(GameplaySceneContract contract, bool allowLegacySceneCards = false)
        {
            Assert.That(contract.TopHudText.text, Does.Contain("HP"));
            Assert.That(contract.TopHudText.text, Does.Contain("Coin"));
            Assert.That(FindChild(contract.transform, GameplaySceneContract.TopLaneHudName), Is.Not.Null);
            Assert.That(FindChild(contract.transform, GameplaySceneContract.RelicCurseRailName), Is.Not.Null);
            Assert.That(FindChild(contract.transform, "Objective Menu Panel"), Is.Null);

            Assert.That(contract.BottomResourceText.text, Does.Contain("기력").Or.Contain("Move D/H/X"));
            Assert.That(contract.DebugEvidenceText.text, Does.Contain("map_overlay_owned_by=map_scene_system"));
            Assert.That(contract.DevLogText.text, Does.Contain("DevOnly"));
            Assert.That(contract.DevLogText.text, Does.Contain("removable"));

            AssertCardDrawer(contract.MoveCardDrawer, "MoveCardDrawer");
            AssertCardDrawer(contract.ActionCardDrawer, "ActionCardDrawer");
            AssertCombatMovementTestCardUi(contract.MoveCardDrawer, "MoveCardDrawer", new[] { "Move" });
            AssertCombatMovementTestCardUi(contract.ActionCardDrawer, "ActionCardDrawer", new[] { "A" });
            var eventSystem = Object.FindFirstObjectByType<EventSystem>();
            Assert.That(eventSystem, Is.Not.Null);
            Assert.That(eventSystem.GetComponent<InputSystemUIInputModule>(), Is.Not.Null);
        }

        private static void AssertOwnImageRaycastTarget(GameplaySceneContract contract, string objectName, bool expected)
        {
            var target = contract.GetComponentsInChildren<Transform>(includeInactive: true)
                .Single(transform => transform.name == objectName);
            Assert.That(target.GetComponent<Image>().raycastTarget, Is.EqualTo(expected), objectName);
        }

        private static bool IsDescendantOf(Transform child, Transform expectedAncestor)
        {
            return child != null && expectedAncestor != null && child.IsChildOf(expectedAncestor);
        }

        private static void AssertCardDrawer(GameplayCardDrawer drawer, string expectedKind)
        {
            Assert.That(drawer, Is.Not.Null);
            Assert.That(drawer.DrawerKind, Is.Not.Empty);
            Assert.That(drawer.HoverRevealsDrawer, Is.True);
            Assert.That(drawer.CollapsedAnchoredPosition.y, Is.LessThan(drawer.ExpandedAnchoredPosition.y));
            Assert.That(drawer.CollapsedAnchoredPosition.y + ((RectTransform)drawer.transform).rect.height * 0.5f, Is.GreaterThan(30f));
            drawer.PreviewHover(false);
            Assert.That(((RectTransform)drawer.transform).anchoredPosition, Is.EqualTo(drawer.CollapsedAnchoredPosition));
            drawer.PreviewHover(true);
            Assert.That(((RectTransform)drawer.transform).anchoredPosition, Is.EqualTo(drawer.ExpandedAnchoredPosition));
        }

        private static void AssertCombatMovementTestCardUi(GameplayCardDrawer drawer, string drawerName, params string[][] acceptedCardSets)
        {
            var root = (RectTransform)drawer.transform;
            var labels = root.GetComponentsInChildren<TMP_Text>(includeInactive: true)
                .Select(text => text.text)
                .ToArray();
            Assert.That(labels, Is.Not.Empty);

            var cardImages = root.GetComponentsInChildren<Image>(includeInactive: true)
                .Where(image => image.gameObject.name.StartsWith(drawerName + " Test Card ", System.StringComparison.Ordinal))
                .Where(image => image.GetComponent<HandCardInteraction>() != null)
                .ToArray();
            Assert.That(cardImages, Is.Not.Empty);
            Assert.That(cardImages.Count(image => image.sprite != null), Is.EqualTo(cardImages.Length));
            Assert.That(cardImages.Count(image => image.raycastTarget), Is.EqualTo(cardImages.Length));

            var interactions = root.GetComponentsInChildren<HandCardInteraction>(includeInactive: true);
            Assert.That(interactions, Has.Length.EqualTo(cardImages.Length));
            var binders = root.GetComponentsInChildren<GameplayCardInteractionBinder>(includeInactive: true);
            Assert.That(binders, Has.Length.EqualTo(cardImages.Length));
            Assert.That(binders.Count(binder => binder.CanDrag), Is.GreaterThanOrEqualTo(cardImages.Length - 1));
        }
        private static void AssertUsesKoreanFont(GameplaySceneContract contract)
        {
            Assert.That(contract.BriefingText.font, Is.Not.Null);
            Assert.That(contract.DebugEvidenceText.font, Is.Not.Null);
            Assert.That(contract.TopHudText.font, Is.Not.Null);
            Assert.That(contract.BottomResourceText.font, Is.Not.Null);
            Assert.That(contract.DevLogText.font, Is.Not.Null);
            // BriefingText is a full-screen header body, so it carries the Medium title weight;
            // the HUD/debug labels stay on the Light body face.
            Assert.That(contract.BriefingText.font.name, Does.Contain("DNFForgedBlade-Medium SDF"));
            Assert.That(contract.DebugEvidenceText.font.name, Does.Contain("DNFForgedBlade-Light SDF"));
            Assert.That(contract.TopHudText.font.name, Does.Contain("DNFForgedBlade-Light SDF"));
            Assert.That(contract.BottomResourceText.font.name, Does.Contain("DNFForgedBlade-Light SDF"));
            Assert.That(contract.DevLogText.font.name, Does.Contain("DNFForgedBlade-Light SDF"));
        }

        private static void AssertNoLegacyCurrentObjectiveCopy(string text)
        {
            Assert.That(text, Does.Not.Contain("landmark-63"));
            Assert.That(text, Does.Not.Contain("63 Building"));
            Assert.That(text, Does.Not.Contain("Han River"));
            Assert.That(text, Does.Not.Contain("Yeouido"));
            Assert.That(text, Does.Not.Contain("Current Objective"));
        }
#if UNITY_EDITOR

        private static void AssertCardTitlesUseVisibleHeader(GameplayCardDrawer drawer)
        {
            var title = drawer.GetComponentsInChildren<TMP_Text>(includeInactive: true)
                .FirstOrDefault(text => text.name.EndsWith(" Title"));
            Assert.That(title, Is.Not.Null, $"{drawer.name} should create visible card title text.");

            var titleRect = title.GetComponent<RectTransform>();
            Assert.That(titleRect.anchoredPosition.y, Is.GreaterThan(-40f), "Card title should stay in the visible top header, not near the bottom clipped text stack.");
            Assert.That(titleRect.sizeDelta.y, Is.GreaterThanOrEqualTo(32f));
            Assert.That(title.overflowMode, Is.EqualTo(TextOverflowModes.Overflow));

            var backdrop = title.transform.parent
                .GetComponentsInChildren<Image>(includeInactive: true)
                .FirstOrDefault(image => image.name.EndsWith(" Title Backdrop"));
            Assert.That(backdrop, Is.Not.Null, "Card title should have a contrast backdrop so it remains readable over card art.");
            Assert.That(backdrop.raycastTarget, Is.False);
        }

        private static Transform FindChild(Transform root, string name)
        {
            return root.GetComponentsInChildren<Transform>(includeInactive: true)
                .SingleOrDefault(transform => transform.name == name);
        }

#endif
    }
}


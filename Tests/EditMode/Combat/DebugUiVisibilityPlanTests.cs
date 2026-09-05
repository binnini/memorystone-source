using NUnit.Framework;
using SeoulPlayup.Combat.Unity;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class DebugUiVisibilityPlanTests
    {
        [Test]
        public void FullLevelKeepsEveryGroupVisible()
        {
            foreach (DebugUiGroup group in System.Enum.GetValues(typeof(DebugUiGroup)))
            {
                Assert.That(
                    DebugUiVisibilityPlan.IsVisible(group, DebugUiVisibilityLevel.Full, hideModalsInHandOnly: false),
                    Is.True,
                    $"{group} should stay visible at Full");
            }
        }

        [Test]
        public void NoneLevelHidesEveryGroup()
        {
            foreach (DebugUiGroup group in System.Enum.GetValues(typeof(DebugUiGroup)))
            {
                Assert.That(
                    DebugUiVisibilityPlan.IsVisible(group, DebugUiVisibilityLevel.None, hideModalsInHandOnly: false),
                    Is.False,
                    $"{group} should be hidden at None");
            }
        }

        [Test]
        public void HandOnlyKeepsCardRowsAndHidesDockAndChrome()
        {
            Assert.That(DebugUiVisibilityPlan.IsVisible(DebugUiGroup.Hand, DebugUiVisibilityLevel.HandOnly, false), Is.True);
            Assert.That(DebugUiVisibilityPlan.IsVisible(DebugUiGroup.HandDock, DebugUiVisibilityLevel.HandOnly, false), Is.False);
            Assert.That(DebugUiVisibilityPlan.IsVisible(DebugUiGroup.Chrome, DebugUiVisibilityLevel.HandOnly, false), Is.False);
        }

        [Test]
        public void HandOnlyKeepsModalsUnlessExplicitlyHidden()
        {
            Assert.That(DebugUiVisibilityPlan.IsVisible(DebugUiGroup.Modal, DebugUiVisibilityLevel.HandOnly, false), Is.True);
            Assert.That(DebugUiVisibilityPlan.IsVisible(DebugUiGroup.Modal, DebugUiVisibilityLevel.HandOnly, true), Is.False);
        }

        [Test]
        public void CardLaneChildrenSplitIntoCardRowsAndDock()
        {
            Assert.That(DebugUiVisibilityPlan.ClassifyCardLaneChild("MoveCards"), Is.EqualTo(DebugUiGroup.Hand));
            Assert.That(DebugUiVisibilityPlan.ClassifyCardLaneChild("ActionCards"), Is.EqualTo(DebugUiGroup.Hand));
            Assert.That(DebugUiVisibilityPlan.ClassifyCardLaneChild("CardFlightOverlay"), Is.EqualTo(DebugUiGroup.Hand));

            // The authored CardLane prefab bundles the whole bottom dock next to the card rows.
            foreach (var dockName in new[]
                     {
                         "HealthDock",
                         "EnergyDock",
                         "DrawPileDock",
                         "DiscardPileDock",
                         "ExilePileDock",
                         "StatusEffectDock",
                         "TurnPhaseDock",
                         "BottomCombatControlDock"
                     })
            {
                Assert.That(
                    DebugUiVisibilityPlan.ClassifyCardLaneChild(dockName),
                    Is.EqualTo(DebugUiGroup.HandDock),
                    dockName);
            }
        }

        [Test]
        public void BlockingOverlaysClassifyAsModalAndTheRestAsChrome()
        {
            foreach (var modalName in new[]
                     {
                         "Card Selection Overlay Root",
                         "Choice Overlay Root",
                         "Game Victory Overlay Root",
                         "Game Over Overlay Root",
                         "CutSceneWithText",
                         "Card Reward Overlay Root",
                         "Card Reward Popup Canvas"
                     })
            {
                Assert.That(DebugUiVisibilityPlan.ClassifyRoot(modalName), Is.EqualTo(DebugUiGroup.Modal), modalName);
            }

            foreach (var chromeName in new[]
                     {
                         "SidebarSystem",
                         "Deck Pile List Overlay Root",
                         "03 Runtime Debug UI",
                         "Monster Tooltip Hud Root"
                     })
            {
                Assert.That(DebugUiVisibilityPlan.ClassifyRoot(chromeName), Is.EqualTo(DebugUiGroup.Chrome), chromeName);
            }
        }

        [Test]
        public void UnknownRootsFallBackToChromeSoNewUiIsHiddenNotLeaked()
        {
            Assert.That(DebugUiVisibilityPlan.ClassifyRoot("Some Future Hud"), Is.EqualTo(DebugUiGroup.Chrome));
            Assert.That(DebugUiVisibilityPlan.ClassifyRoot(null), Is.EqualTo(DebugUiGroup.Chrome));
        }

        [Test]
        public void NextCyclesFullHandOnlyNoneAndBack()
        {
            Assert.That(DebugUiVisibilityPlan.Next(DebugUiVisibilityLevel.Full), Is.EqualTo(DebugUiVisibilityLevel.HandOnly));
            Assert.That(DebugUiVisibilityPlan.Next(DebugUiVisibilityLevel.HandOnly), Is.EqualTo(DebugUiVisibilityLevel.None));
            Assert.That(DebugUiVisibilityPlan.Next(DebugUiVisibilityLevel.None), Is.EqualTo(DebugUiVisibilityLevel.Full));
        }

        [Test]
        public void CardLaneIsRecognisedByName()
        {
            Assert.That(DebugUiVisibilityPlan.IsCardLane("CardLane"), Is.True);
            Assert.That(DebugUiVisibilityPlan.IsCardLane("CardLane Extra"), Is.False);
            Assert.That(DebugUiVisibilityPlan.IsCardLane(null), Is.False);
        }

        [Test]
        public void ExternalRootsCoverTheRewardCanvasOutsideTheGameplayLayerRoot()
        {
            Assert.That(DebugUiVisibilityPlan.ExternalRootNames, Contains.Item("Card Reward Popup Canvas"));
            Assert.That(DebugUiVisibilityPlan.ExternalRootNames, Contains.Item("03 Runtime Debug UI"));
        }

        [Test]
        public void DescribeIsEmptyOnlyAtFull()
        {
            Assert.That(DebugUiVisibilityPlan.Describe(DebugUiVisibilityLevel.Full), Is.Empty);
            Assert.That(DebugUiVisibilityPlan.Describe(DebugUiVisibilityLevel.HandOnly), Is.Not.Empty);
            Assert.That(DebugUiVisibilityPlan.Describe(DebugUiVisibilityLevel.None), Is.Not.Empty);
        }
    }
}

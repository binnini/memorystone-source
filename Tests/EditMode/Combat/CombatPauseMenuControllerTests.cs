using NUnit.Framework;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class CombatPauseMenuControllerTests
    {
        [Test]
        public void PauseGateBlocksEndActionWithPauseMessage()
        {
            var root = new GameObject("Pause Gate Test");
            try
            {
                var controller = root.AddComponent<MapCombatController>();
                controller.SetPauseMenuInputBlocked(true);

                Assert.That(controller.IsPauseMenuInputBlocked, Is.True);
                Assert.That(controller.EndAction(), Is.False);
                Assert.That(controller.LastInputMessage, Does.Contain("일시정지"));

                controller.SetPauseMenuInputBlocked(false);
                Assert.That(controller.IsPauseMenuInputBlocked, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void OpenBlocksCombatInputAndCloseReleasesIt()
        {
            var sparseSource = CreateSparseSource("Pause Menu Open Source");
            var root = new GameObject("Pause Menu Open Test");
            CombatPauseMenuController menu = null;
            try
            {
                var controller = root.AddComponent<MapCombatController>();
                controller.ConfigureSparseSourceForTests(sparseSource);
                controller.ConfigureExpectedBoardPurposeForTests(HexMapPurpose.PlayableMap);
                controller.InitializeIntegration();
                Assert.That(controller.IsInitialized, Is.True);

                menu = CombatPauseMenuController.GetOrCreate(controller);
                menu.Open();

                Assert.That(menu.IsOpen, Is.True);
                Assert.That(controller.IsPauseMenuInputBlocked, Is.True);
                Assert.That(controller.EndAction(), Is.False);
                Assert.That(controller.LastInputMessage, Does.Contain("일시정지"));

                menu.Close();

                Assert.That(menu.IsOpen, Is.False);
                Assert.That(controller.IsPauseMenuInputBlocked, Is.False);
            }
            finally
            {
                if (menu != null)
                {
                    Object.DestroyImmediate(menu.gameObject);
                }

                Object.DestroyImmediate(root);
                Object.DestroyImmediate(sparseSource);
            }
        }

        [Test]
        public void SoundSettingsPageClonesScenePanelAndHidesItsLobbyFlow()
        {
            var sparseSource = CreateSparseSource("Pause Menu Sound Source");
            var root = new GameObject("Pause Menu Sound Test");
            var scenePanelHost = new GameObject("Scene Sound Settings", typeof(RectTransform));
            CombatPauseMenuController menu = null;
            try
            {
                var sceneView = scenePanelHost.AddComponent<SoundSettingsPanelView>();
                CreateChildButton(scenePanelHost.transform, "ReturnToLobbyButton");
                CreateChild(scenePanelHost.transform, "ReturnToLobbyConfirmDialog");

                var controller = root.AddComponent<MapCombatController>();
                controller.ConfigureSparseSourceForTests(sparseSource);
                controller.ConfigureExpectedBoardPurposeForTests(HexMapPurpose.PlayableMap);
                controller.InitializeIntegration();

                menu = CombatPauseMenuController.GetOrCreate(controller);
                menu.Open();

                var soundButton = FindChildByName(menu.transform, "SoundSettingsButton");
                Assert.That(soundButton, Is.Not.Null);
                soundButton.GetComponent<Button>().onClick.Invoke();

                Assert.That(FindChildByName(menu.transform, "Main Page").gameObject.activeSelf, Is.False);
                Assert.That(FindChildByName(menu.transform, "Sound Page").gameObject.activeSelf, Is.True);

                var clone = menu.GetComponentInChildren<SoundSettingsPanelView>(true);
                Assert.That(clone, Is.Not.Null);
                Assert.That(clone, Is.Not.SameAs(sceneView));
                Assert.That(FindChildByName(clone.transform, "ReturnToLobbyButton").gameObject.activeSelf, Is.False);
                Assert.That(FindChildByName(clone.transform, "ReturnToLobbyConfirmDialog").gameObject.activeSelf, Is.False);

                // Menu is still pausing gameplay while the sound page is open.
                Assert.That(controller.IsPauseMenuInputBlocked, Is.True);
            }
            finally
            {
                if (menu != null)
                {
                    Object.DestroyImmediate(menu.gameObject);
                }

                Object.DestroyImmediate(scenePanelHost);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(sparseSource);
            }
        }

        private static HexSparseMapAuthoringSource CreateSparseSource(string name)
        {
            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            source.name = name;
            source.ConfigureForTests(
                new[]
                {
                    new HexSparseMapAuthoringCell(new HexCoord(0, 0), "start", "seoul-start-neighborhood", "mvp-start", eventId: "mvp-start"),
                    new HexSparseMapAuthoringCell(new HexCoord(1, 0), "street", "seoul-street", "mvp-street"),
                    new HexSparseMapAuthoringCell(new HexCoord(2, 0), "finish", "yeouido-landmark", "mvp-landmark-63", eventId: "mvp-goal", landmarkId: "finish-landmark")
                },
                HexMapPurpose.PlayableMap,
                objectRefs: new[]
                {
                    new HexMapObjectRef("player-spawn", HexMapObjectType.PlayerSpawn, string.Empty, 0, 0),
                    new HexMapObjectRef("reach-finish", HexMapObjectType.ObjectiveMarker, "finish-landmark", 2, 0, "Finish", HexMapPurpose.PlayableMap, interactable: true)
                });
            return source;
        }

        private static void CreateChildButton(Transform parent, string name)
        {
            var child = CreateChild(parent, name);
            child.gameObject.AddComponent<Image>();
            child.gameObject.AddComponent<Button>();
        }

        private static Transform CreateChild(Transform parent, string name)
        {
            var child = new GameObject(name, typeof(RectTransform)).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static Transform FindChildByName(Transform root, string name)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == name)
                {
                    return all[i];
                }
            }

            return null;
        }
    }
}

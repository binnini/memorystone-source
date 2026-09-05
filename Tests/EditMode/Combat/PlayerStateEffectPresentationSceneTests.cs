#if UNITY_EDITOR
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using TMPro;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class PlayerStateEffectPresentationSceneTests
    {
        [Test]
        public void TrapFloatingTextUsesKoreanFontAndReadableSize()
        {
            var previousMainCameras = GameObject.FindGameObjectsWithTag("MainCamera");
            var cameraObject = new GameObject("Effect Presentation Test Camera");
            var root = new GameObject("Effect Presentation Test Root");
            try
            {
                foreach (var previousMainCamera in previousMainCameras)
                {
                    if (previousMainCamera != null)
                    {
                        previousMainCamera.tag = "Untagged";
                    }
                }

                var camera = cameraObject.AddComponent<Camera>();
                cameraObject.tag = "MainCamera";
                camera.transform.position = new Vector3(0f, 5f, -6f);
                camera.transform.rotation = Quaternion.LookRotation(Vector3.zero - camera.transform.position, Vector3.up);

                var presentation = root.AddComponent<EffectPresentationController>();
                var trapEvent = new EffectResultEvent(
                    EffectKind.StatusEffectApplied,
                    appliedAmount: 2,
                    sourceRef: "trap.poison",
                    statusKind: StatusEffectKind.Poison);

                presentation.Play(trapEvent, Vector3.zero);

                var floatingText = presentation.SpawnedEffects
                    .Select(effect => effect == null ? null : effect.GetComponent<TextMeshPro>())
                    .SingleOrDefault(text => text != null);

                Assert.That(floatingText, Is.Not.Null, "Trap presentation must spawn a floating TextMeshPro label.");
                Assert.That(floatingText.text, Is.EqualTo("함정 발동! 중독 2"));
                Assert.That(floatingText.fontSize, Is.GreaterThanOrEqualTo(7f), "Trap floating text must be readable in Scene/Game views.");
                Assert.That(floatingText.font, Is.Not.Null);
                Assert.That(floatingText.font.name, Does.Contain("DNFForgedBlade"), "Trap Korean text must use a Korean-capable TMP font instead of the default Latin font.");

                var expectedForward = (floatingText.transform.position - camera.transform.position).normalized;
                Assert.That(Vector3.Dot(floatingText.transform.forward, expectedForward), Is.GreaterThan(0.99f),
                    "Trap floating text must billboard toward the PrototypeTest camera instead of showing its mirrored back face.");
            }
            finally
            {
                foreach (var previousMainCamera in previousMainCameras)
                {
                    if (previousMainCamera != null)
                    {
                        previousMainCamera.tag = "MainCamera";
                    }
                }

                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void TreasureChestRewardFloatingTextUsesKoreanCardRewardCopy()
        {
            var root = new GameObject("Treasure Chest Reward Presentation Test Root");
            try
            {
                var presentation = root.AddComponent<EffectPresentationController>();
                var rewardEvent = new EffectResultEvent(
                    EffectKind.StatusEffectApplied,
                    targetUnitId: "field",
                    appliedAmount: 1,
                    sourceRef: "object.treasure_chest.reward");

                presentation.Play(rewardEvent, Vector3.zero);

                var floatingText = presentation.SpawnedEffects
                    .Select(effect => effect == null ? null : effect.GetComponent<TextMeshPro>())
                    .SingleOrDefault(text => text != null);

                Assert.That(floatingText, Is.Not.Null, "Treasure reward presentation must spawn a floating TextMeshPro label.");
                Assert.That(floatingText.text, Is.EqualTo("카드 획득!"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static GameObject AssertRootExists(GameObject[] roots, string name)
        {
            var root = roots.SingleOrDefault(gameObject => gameObject.name == name);
            Assert.That(root, Is.Not.Null, name);
            return root;
        }
    }
}
#endif




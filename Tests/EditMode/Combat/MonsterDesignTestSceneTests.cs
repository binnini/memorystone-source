#if UNITY_EDITOR
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Unity;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class MonsterDesignTestSceneTests
    {
        [Test]
        public void MonsterDesignTestControllerCanLoadMeleePatternsAndResolveDamage()
        {
            var root = new GameObject("Monster Design Test Controller Unit");
            try
            {
                var controller = root.AddComponent<MonsterDesignTestController>();
                controller.LoadMeleeRecommendations();
                controller.MoveDummyToMonsterRangeAndReset();
                var hpBefore = controller.State.Player.Hp;

                controller.ResolveMonsterTurn();

                Assert.That(controller.AttackPatterns, Has.Count.EqualTo(20));
                Assert.That(controller.State.Player.Hp, Is.LessThan(hpBefore));
                Assert.That(controller.State.LastMonsterActionRecords.Any(record => record.AttackedPlayer), Is.True);
                Assert.That(controller.StatusText, Does.Contain("Monster attacked dummy"));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void AssertUsesUrpMaterialAsset(string objectName)
        {
            var gameObject = GameObject.Find(objectName);
            Assert.That(gameObject, Is.Not.Null, objectName);

            var renderer = gameObject.GetComponentInChildren<Renderer>();
            Assert.That(renderer, Is.Not.Null, objectName);
            Assert.That(renderer.sharedMaterial, Is.Not.Null, objectName);

            var assetPath = AssetDatabase.GetAssetPath(renderer.sharedMaterial);
            Assert.That(assetPath, Does.StartWith("Assets/Art/Combat/MonsterDesignTest/Materials/"), objectName);
            Assert.That(renderer.sharedMaterial.shader.name, Does.Contain("Universal Render Pipeline"), objectName);
        }
    }
}
#endif


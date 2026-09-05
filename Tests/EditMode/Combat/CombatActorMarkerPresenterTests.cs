using NUnit.Framework;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Unity;
using System.Reflection;
using System.Linq;
using TMPro;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class CombatActorMarkerPresenterTests
    {
        [Test]
        public void EnsureEnemyMarkerCreatesPrimitiveMarkerUnderSelectedView()
        {
            var parent = new GameObject("Actor Marker Parent");
            var presenter = new CombatActorMarkerPresenter();
            try
            {
                var marker = presenter.EnsureEnemyMarker(
                    parent.transform,
                    null,
                    null,
                    Color.red,
                    0.5f,
                    Vector3.zero,
                    Vector3.zero,
                    Vector3.one);

                Assert.That(marker, Is.Not.Null);
                Assert.That(marker.name, Is.EqualTo("M2 Integration Enemy Marker"));
                Assert.That(marker.transform.parent, Is.EqualTo(parent.transform));
                Assert.That(marker.transform.localScale, Is.EqualTo(Vector3.one * 0.5f));
                Assert.That(marker.GetComponent<Collider>().enabled, Is.False);
                Assert.That(marker.GetComponent<Renderer>().enabled, Is.False);
                Assert.That(presenter.Marker, Is.EqualTo(marker));
                Assert.That(presenter.ActorVisual, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(presenter.Marker);
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void ApplyEnemyMarkerSettingsUpdatesPrimitiveScaleAndMaterialColor()
        {
            var presenter = new CombatActorMarkerPresenter();
            try
            {
                var marker = presenter.EnsureEnemyMarker(null, null, null, Color.red, 0.5f, Vector3.zero, Vector3.zero, Vector3.one);

                presenter.ApplyEnemyMarkerSettings(null, Color.blue, 0.25f);

                Assert.That(marker.transform.localScale, Is.EqualTo(Vector3.one * 0.25f));
                var renderer = marker.GetComponent<Renderer>();
                Assert.That(renderer.sharedMaterial.color, Is.EqualTo(Color.blue));
            }
            finally
            {
                Object.DestroyImmediate(presenter.Marker);
            }
        }

        [Test]
        public void ShowMonsterMarkersCreatesMarkerPerLivingMonsterAndHidesStaleMarkers()
        {
            var parent = new GameObject("Multi Actor Marker Parent");
            var presenter = new CombatActorMarkerPresenter();
            try
            {
                presenter.ShowMonsterMarkers(
                    new[]
                    {
                        new CombatActorMarkerPresenter.MonsterMarkerState("monster-a", new Vector3(1f, 0f, 0f)),
                        new CombatActorMarkerPresenter.MonsterMarkerState("monster-b", new Vector3(2f, 0f, 0f))
                    },
                    parent.transform,
                    null,
                    null,
                    Color.red,
                    0.5f,
                    Vector3.zero,
                    Vector3.zero,
                    Vector3.one);

                Assert.That(presenter.MarkerCount, Is.EqualTo(2));
                Assert.That(parent.transform.Cast<Transform>().Count(child => child.gameObject.activeSelf), Is.EqualTo(2));

                presenter.ShowMonsterMarkers(
                    new[] { new CombatActorMarkerPresenter.MonsterMarkerState("monster-b", new Vector3(3f, 0f, 0f)) },
                    parent.transform,
                    null,
                    null,
                    Color.blue,
                    0.25f,
                    Vector3.zero,
                    Vector3.zero,
                    Vector3.one);

                Assert.That(presenter.MarkerCount, Is.EqualTo(2), "Presenter keeps pooled markers for reuse.");
                Assert.That(parent.transform.Cast<Transform>().Count(child => child.gameObject.activeSelf), Is.EqualTo(1));
                Assert.That(parent.transform.Cast<Transform>().Single(child => child.gameObject.activeSelf).localPosition, Is.EqualTo(new Vector3(3f, 0f, 0f)));
            }
            finally
            {
                presenter.DestroyAllMarkers();
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void ShowMonsterMarkersUpdatesNameLabelAndHealthBar()
        {
            var parent = new GameObject("Monster Health Marker Parent");
            var presenter = new CombatActorMarkerPresenter();
            try
            {
                presenter.ShowMonsterMarkers(
                    new[]
                    {
                        new CombatActorMarkerPresenter.MonsterMarkerState(
                            "monster-a",
                            new Vector3(1f, 0f, 0f),
                            "테스트 근접 몬스터\nAttack",
                            Color.red,
                            hp: 3,
                            maxHp: 10)
                    },
                    parent.transform,
                    null,
                    null,
                    Color.red,
                    0.5f,
                    Vector3.zero,
                    Vector3.zero,
                    Vector3.one);

                var marker = presenter.Marker;
                var label = marker.GetComponentInChildren<TMP_Text>(includeInactive: true);
                var nameplate = marker.transform.Find("Monster Nameplate");
                var fill = marker.transform.Find("Monster Nameplate/Monster Health Bar/Monster Health Bar Fill") as RectTransform;

                Assert.That(label, Is.Not.Null);
                Assert.That(label.text, Is.EqualTo("테스트 근접 몬스터\nAttack"));
                Assert.That(nameplate, Is.Not.Null);
                Assert.That(nameplate.gameObject.activeSelf, Is.True);
                Assert.That(nameplate.GetComponent<CombatWorldSpaceBillboard>(), Is.Not.Null);
                Assert.That(fill, Is.Not.Null);
                Assert.That(fill.sizeDelta.x, Is.EqualTo(150f * 0.3f).Within(0.0001f));
            }
            finally
            {
                presenter.DestroyAllMarkers();
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void ShowMonsterMarkersDisplaysBossLabelForBossMonster()
        {
            var parent = new GameObject("Boss Marker Parent");
            var presenter = new CombatActorMarkerPresenter();
            try
            {
                presenter.ShowMonsterMarkers(
                    new[]
                    {
                        new CombatActorMarkerPresenter.MonsterMarkerState(
                            "boss-a",
                            Vector3.zero,
                            "Boss Monster",
                            Color.red,
                            hp: 10,
                            maxHp: 10,
                            isBoss: true,
                            visualPrefab: null,
                            visualLocalScaleMultiplier: Vector3.one)
                    },
                    parent.transform,
                    null,
                    null,
                    Color.red,
                    0.5f,
                    Vector3.zero,
                    Vector3.zero,
                    Vector3.one);

                var bossLabel = presenter.Marker
                    .GetComponentsInChildren<TMP_Text>(includeInactive: true)
                    .SingleOrDefault(label => label.name == "Monster Boss Text");

                Assert.That(bossLabel, Is.Not.Null);
                Assert.That(bossLabel.gameObject.activeSelf, Is.True);
                Assert.That(bossLabel.text, Is.EqualTo("보스"));
                Assert.That(bossLabel.color.r, Is.EqualTo(1f).Within(0.001f));
                Assert.That(bossLabel.rectTransform.anchoredPosition.y, Is.GreaterThan(0f), "Boss label should sit above the monster name, away from the health bar.");
            }
            finally
            {
                presenter.DestroyAllMarkers();
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void ShowMonsterMarkersDisplaysEliteLabelForEliteMonster()
        {
            var parent = new GameObject("Elite Marker Parent");
            var presenter = new CombatActorMarkerPresenter();
            try
            {
                presenter.ShowMonsterMarkers(
                    new[]
                    {
                        new CombatActorMarkerPresenter.MonsterMarkerState(
                            "elite-a",
                            Vector3.zero,
                            "Elite Monster",
                            Color.red,
                            hp: 10,
                            maxHp: 10,
                            false,
                            true,
                            null,
                            Vector3.one)
                    },
                    parent.transform,
                    null,
                    null,
                    Color.red,
                    0.5f,
                    Vector3.zero,
                    Vector3.zero,
                    Vector3.one);

                var eliteLabel = presenter.Marker
                    .GetComponentsInChildren<TMP_Text>(includeInactive: true)
                    .SingleOrDefault(label => label.name == "Monster Boss Text");

                Assert.That(eliteLabel, Is.Not.Null);
                Assert.That(eliteLabel.gameObject.activeSelf, Is.True);
                Assert.That(eliteLabel.text, Is.EqualTo("엘리트"));
                Assert.That(eliteLabel.color.b, Is.GreaterThan(eliteLabel.color.r), "Elite label should use a purple accent.");
            }
            finally
            {
                presenter.DestroyAllMarkers();
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void SetMarkerVisualScaleReanchorsTheNameplateAboveTheGrownBody()
        {
            // 2026-09-05 후속 #4: 보스 페이즈 성장 트윈은 모델 배율만 바꿨고 명판은 생성 시 높이에 남아 몸통에 파묻혔다.
            var parent = new GameObject("Nameplate Anchor Parent");
            var prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            prefab.name = "Nameplate Anchor Body";
            var presenter = new CombatActorMarkerPresenter();
            try
            {
                presenter.ShowMonsterMarkers(
                    new[] { new CombatActorMarkerPresenter.MonsterMarkerState("boss", Vector3.zero, "보스", Color.red, hp: 10, maxHp: 10) },
                    parent.transform,
                    null,
                    prefab,
                    Color.red,
                    0.5f,
                    Vector3.zero,
                    Vector3.zero,
                    Vector3.one);
                var nameplate = presenter.Marker.transform.Find("Monster Nameplate");
                Assert.That(nameplate, Is.Not.Null);
                var before = nameplate.localPosition.y;

                presenter.SetMarkerVisualScale("boss", Vector3.one * 3f);

                Assert.That(nameplate.localPosition.y, Is.GreaterThan(before), "몸이 3배로 자라면 명판도 그 위로 올라간다.");
                presenter.SetMarkerVisualScale("boss", Vector3.one);
                Assert.That(nameplate.localPosition.y, Is.EqualTo(before).Within(0.001f), "되돌리면 원래 높이(멱등).");
            }
            finally
            {
                presenter.DestroyAllMarkers();
                Object.DestroyImmediate(prefab);
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void MonsterNameplateBillboardStaysUprightWhenCameraYawOrbits()
        {
            var existingCameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var existingCameraEnabledStates = existingCameras.Select(camera => camera.enabled).ToArray();
            var cameraObject = new GameObject("Billboard Test Main Camera");
            var parent = new GameObject("Billboard Marker Parent");
            var presenter = new CombatActorMarkerPresenter();
            try
            {
                foreach (var existingCamera in existingCameras)
                {
                    existingCamera.enabled = false;
                }

                var camera = cameraObject.AddComponent<Camera>();
                cameraObject.tag = "MainCamera";

                presenter.ShowMonsterMarkers(
                    new[]
                    {
                        new CombatActorMarkerPresenter.MonsterMarkerState(
                            "monster-a",
                            Vector3.zero,
                            "테스트 몬스터",
                            Color.red,
                            hp: 5,
                            maxHp: 10)
                    },
                    parent.transform,
                    null,
                    null,
                    Color.red,
                    0.5f,
                    Vector3.zero,
                    Vector3.zero,
                    Vector3.one);

                var nameplate = presenter.Marker.transform.Find("Monster Nameplate");
                var billboard = nameplate.GetComponent<CombatWorldSpaceBillboard>();
                Assert.That(billboard, Is.Not.Null);

                AssertBillboardUprightFrom(camera, nameplate, billboard, new Vector3(0f, 3f, -5f), Quaternion.Euler(28f, 0f, 0f));
                AssertBillboardUprightFrom(camera, nameplate, billboard, new Vector3(5f, 3f, 0f), Quaternion.Euler(28f, 270f, 0f));
                AssertBillboardUprightFrom(camera, nameplate, billboard, new Vector3(0f, 3f, 5f), Quaternion.Euler(28f, 180f, 0f));
            }
            finally
            {
                presenter.DestroyAllMarkers();
                Object.DestroyImmediate(parent);
                Object.DestroyImmediate(cameraObject);
                for (var i = 0; i < existingCameras.Length; i++)
                {
                    if (existingCameras[i] != null)
                    {
                        existingCameras[i].enabled = existingCameraEnabledStates[i];
                    }
                }
            }
        }

        [Test]
        public void SetActivePositionAndFacingUpdateMarkerTransform()
        {
            var presenter = new CombatActorMarkerPresenter();
            try
            {
                var marker = presenter.EnsureEnemyMarker(null, null, null, Color.red, 0.5f, Vector3.zero, Vector3.zero, Vector3.one);

                presenter.SetActive(false);
                presenter.SetLocalPosition(new Vector3(1f, 2f, 3f));
                presenter.FaceToward(Vector3.zero, Vector3.forward);

                Assert.That(marker.activeSelf, Is.False);
                Assert.That(marker.transform.localPosition, Is.EqualTo(new Vector3(1f, 2f, 3f)));
                Assert.That(Quaternion.Angle(marker.transform.localRotation, Quaternion.LookRotation(Vector3.forward, Vector3.up)), Is.LessThan(0.01f));
            }
            finally
            {
                Object.DestroyImmediate(presenter.Marker);
            }
        }

        [Test]
        public void EnsureEnemyMarkerWithPrefabCreatesVisualChildAndActorVisual()
        {
            var parent = new GameObject("Actor Marker Prefab Parent");
            var prefab = new GameObject("Enemy Visual Prefab");
            prefab.AddComponent<CharacterActorVisual>();
            prefab.transform.localScale = Vector3.one * 3f;
            var presenter = new CombatActorMarkerPresenter();
            try
            {
                var marker = presenter.EnsureEnemyMarker(
                    null,
                    parent.transform,
                    prefab,
                    Color.red,
                    0.5f,
                    new Vector3(0f, 0.1f, 0f),
                    new Vector3(0f, 45f, 0f),
                    Vector3.one * 2f);

                Assert.That(marker.transform.parent, Is.EqualTo(parent.transform));
                Assert.That(marker.transform.localScale, Is.EqualTo(Vector3.one));
                Assert.That(marker.transform.childCount, Is.EqualTo(3)); // visual root + badge + nameplate canvas
                Assert.That(presenter.ActorVisual, Is.Not.Null);
                Assert.That(presenter.ActorVisual.transform.localPosition, Is.EqualTo(new Vector3(0f, 0.1f, 0f)));
                Assert.That(presenter.ActorVisual.transform.localScale, Is.EqualTo(Vector3.one * 6f));
            }
            finally
            {
                Object.DestroyImmediate(presenter.Marker);
                Object.DestroyImmediate(prefab);
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void TriggerKnockbackForwardsToActorVisual()
        {
            var parent = new GameObject("Knockback Marker Parent");
            var prefab = new GameObject("Knockback Visual Prefab");
            prefab.AddComponent<CharacterActorVisual>();
            var presenter = new CombatActorMarkerPresenter();
            try
            {
                presenter.ShowMonsterMarkers(
                    new[] { new CombatActorMarkerPresenter.MonsterMarkerState("monster-a", Vector3.zero) },
                    parent.transform,
                    null,
                    prefab,
                    Color.red,
                    0.5f,
                    Vector3.zero,
                    Vector3.zero,
                    Vector3.one);

                presenter.TriggerKnockback("monster-a");

                var visual = presenter.Marker.GetComponentInChildren<CharacterActorVisual>(true);
                Assert.That(visual, Is.Not.Null);
                Assert.That(visual.LastTriggerName, Is.EqualTo("KnockbackTrigger"));
            }
            finally
            {
                presenter.DestroyAllMarkers();
                Object.DestroyImmediate(prefab);
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void ShowMonsterMarkersUsesPerMonsterVisualPrefabAndPreservesPrefabScale()
        {
            var parent = new GameObject("Per Monster Visual Parent");
            var fallbackPrefab = new GameObject("Fallback Enemy Visual Prefab");
            var bulgasalPrefab = new GameObject("Bulgasal Visual Prefab");
            var tigerPrefab = new GameObject("Tiger Visual Prefab");
            fallbackPrefab.AddComponent<CharacterActorVisual>();
            bulgasalPrefab.AddComponent<CharacterActorVisual>();
            tigerPrefab.AddComponent<CharacterActorVisual>();
            bulgasalPrefab.transform.localScale = Vector3.one * 5f;
            tigerPrefab.transform.localScale = Vector3.one * 3f;
            var presenter = new CombatActorMarkerPresenter();
            try
            {
                presenter.ShowMonsterMarkers(
                    new[]
                    {
                        new CombatActorMarkerPresenter.MonsterMarkerState("spawn-M002", Vector3.zero, "Bulgasal", Color.red, 10, 10, bulgasalPrefab, Vector3.one),
                        new CombatActorMarkerPresenter.MonsterMarkerState("spawn-M006", Vector3.right, "Tiger", Color.blue, 10, 10, tigerPrefab, Vector3.one)
                    },
                    parent.transform,
                    null,
                    fallbackPrefab,
                    Color.red,
                    0.5f,
                    Vector3.zero,
                    Vector3.zero,
                    Vector3.one);

                var visualNames = parent.transform
                    .Cast<Transform>()
                    .Select(marker => marker.GetComponentInChildren<CharacterActorVisual>(true))
                    .Where(visual => visual != null)
                    .Select(visual => visual.transform)
                    .ToArray();

                Assert.That(visualNames.Select(visual => visual.localScale.x), Is.EquivalentTo(new[] { 5f, 3f }));
                Assert.That(visualNames.Select(visual => visual.gameObject.name), Has.None.EqualTo("Fallback Enemy Visual Prefab"));
            }
            finally
            {
                presenter.DestroyAllMarkers();
                Object.DestroyImmediate(fallbackPrefab);
                Object.DestroyImmediate(bulgasalPrefab);
                Object.DestroyImmediate(tigerPrefab);
                Object.DestroyImmediate(parent);
            }
        }

        private static void AssertBillboardUprightFrom(
            Camera camera,
            Transform nameplate,
            Component billboard,
            Vector3 cameraPosition,
            Quaternion cameraRotation)
        {
            camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
            InvokeLateUpdate(billboard);

            var expectedForward = nameplate.position - camera.transform.position;
            expectedForward.y = 0f;
            expectedForward.Normalize();

            Assert.That(Vector3.Dot(nameplate.forward, expectedForward), Is.GreaterThan(0.99f));
            Assert.That(Vector3.Dot(nameplate.up, Vector3.up), Is.GreaterThan(0.99f));
        }

        private static void InvokeLateUpdate(Component component)
        {
            component.GetType()
                .GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(component, null);
        }
    }
}


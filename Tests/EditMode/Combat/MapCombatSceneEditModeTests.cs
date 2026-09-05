using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class MapCombatIntegrationSceneEditModeTests
    {
        [Test]
        public void ControllerLoadsSmokeSparseSourceWithExplicitRuntimePurposeEvidence()
        {
            var sparseSource = CreateSparseSourceForPurpose("Smoke Sparse Runtime Source", HexMapPurpose.SmokeMap);
            var root = new GameObject("Smoke Sparse Runtime Selection Test");
            try
            {
                var controller = root.AddComponent<MapCombatController>();
                controller.ConfigureSparseSourceForTests(sparseSource);
                controller.ConfigureExpectedBoardPurposeForTests(HexMapPurpose.SmokeMap);

                controller.InitializeIntegration();

                Assert.That(controller.LoadedMap, Is.Not.Null);
                Assert.That(controller.SelectedBoardName, Is.EqualTo("Smoke Sparse Runtime Source"));
                Assert.That(controller.SelectedBoardPurpose, Is.EqualTo(HexMapPurpose.SmokeMap));
                Assert.That(controller.BoardSelectionEvidenceText, Does.Contain("SmokeMap"));
                Assert.That(controller.State.ObjectiveBinding.ObjectiveId, Is.EqualTo("reach-finish"));
                Assert.That(controller.State.ObjectiveBindingEvidenceText, Does.Contain("targets landmark finish-landmark"));
                Assert.That(controller.State.ActiveMonsterSpawnRefIds, Is.EquivalentTo(new[] { "enemy-spawn" }));
                Assert.That(controller.State.MonsterCatalogEvidenceText, Does.Contain("SpawnRefs=[enemy-spawn]"));
                Assert.That(controller.StatusText, Does.Contain("SmokeMap"));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(sparseSource);
            }
        }

        [Test]
        public void ControllerUsesMonsterCatalogVisualPrefabAndAuthoredPrefabScale()
        {
            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            var root = new GameObject("Monster Catalog Visual Runtime Test");
            var atlasRoot = new GameObject("Monster Catalog Visual Atlas");
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var topPrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                source.ConfigureForTests(
                    new[]
                    {
                        new HexSparseMapAuthoringCell(new HexCoord(0, 0), "start", "street", "atlas-test"),
                        new HexSparseMapAuthoringCell(new HexCoord(1, 0), "monster", "street", "atlas-test")
                    },
                    HexMapPurpose.PlayableMap,
                    objectRefs: new[]
                    {
                        new HexMapObjectRef("player-spawn", HexMapObjectType.PlayerSpawn, string.Empty, 0, 0),
                        new HexMapObjectRef("bulgasal-spawn", HexMapObjectType.MonsterSpawn, "M002", 1, 0, "primary_pressure", HexMapPurpose.PlayableMap)
                    });

                var atlas = atlasRoot.AddComponent<AtlasTilePresentationView>();
                catalog.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("atlas-test", topPrefab) });
                atlas.ConfigureForTests(catalog);

                var controller = root.AddComponent<MapCombatController>();
                controller.ConfigureForTests(atlas, null, null);
                controller.ConfigureSparseSourceForTests(source);
                controller.ConfigureExpectedBoardPurposeForTests(HexMapPurpose.PlayableMap);
                controller.ConfigureDebugRevealAllMapCellsForTests(true);

                controller.InitializeIntegration();

                Assert.That(controller.State.Monsters.Single().DefinitionId, Is.EqualTo("M002"));
                Assert.That(controller.EnemyActorVisual, Is.Not.Null);
                Assert.That(controller.EnemyActorVisual.name, Is.EqualTo("M2 Integration Enemy Visual"));
                Assert.That(controller.EnemyActorVisual.transform.localScale, Is.EqualTo(Vector3.one * 5f));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(atlasRoot);
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(topPrefab);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void ControllerRejectsSparsePurposeMismatchBeforeRuntimeSelectionSucceeds()
        {
            var sparseSource = CreateSparseSourceForPurpose("Smoke Sparse Runtime Source", HexMapPurpose.SmokeMap);
            var root = new GameObject("Sparse Purpose Mismatch Test");
            try
            {
                var controller = root.AddComponent<MapCombatController>();
                controller.ConfigureSparseSourceForTests(sparseSource);
                controller.ConfigureExpectedBoardPurposeForTests(HexMapPurpose.PlayableMap);

                LogAssert.Expect(LogType.Error, "M2 map combat integration selected board 'Smoke Sparse Runtime Source' has purpose SmokeMap, expected PlayableMap.");
                controller.InitializeIntegration();

                Assert.That(controller.LoadedMap, Is.Null);
                Assert.That(controller.State, Is.Null);
                Assert.That(controller.SelectedBoardName, Is.EqualTo("Smoke Sparse Runtime Source"));
                Assert.That(controller.SelectedBoardPurpose, Is.EqualTo(HexMapPurpose.SmokeMap));
                Assert.That(controller.BoardSelectionEvidenceText, Does.Contain("SmokeMap"));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(sparseSource);
            }
        }

        [Test]
        public void MonsterIntentFacingIsSuppressedDuringPresentationSequences()
        {
            var root = new GameObject("Monster Facing Presentation Guard Test");
            try
            {
                var controller = root.AddComponent<MapCombatController>();
                var state = new CombatState(
                    CombatState.CreateDemoMap(2),
                    new HexCoord(0, 0),
                    new HexCoord(1, 0),
                    CombatConfig.Default);

                SetControllerState(controller, state);
                Assert.That(controller.ShouldUpdateMonsterIntentFacing, Is.True);

                SetPrivateField(controller, "isSequencePlaying", true);

                Assert.That(controller.ShouldUpdateMonsterIntentFacing, Is.False,
                    "While a before/after presentation sequence is replaying, LateUpdate must not overwrite monster movement-facing with the next locked intent.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SacrificeAttackLocksTargetBeforeOpeningHandCardSelection()
        {
            var root = new GameObject("Sacrifice Attack Target First Test");
            try
            {
                var controller = root.AddComponent<MapCombatController>();
                var target = new HexCoord(1, 0);
                var state = CreateSacrificeAttackState(target);
                SetControllerState(controller, state);
                Assert.That(state.EndAction(), Is.True); // PlayerMovement -> MonsterMovement
                state.ResolveMonsterMovement(); // -> PlayerAction (DEC-2026-07-03-02)

                Assert.That(controller.BeginAttackSelection("sacrifice-instance"), Is.True);
                Assert.That(controller.IsAttackSelectionActive, Is.True);
                Assert.That(controller.HandCardSelectionPanel.IsActive, Is.False,
                    "A targeted hand-card-selection card should ask for the target before opening the card picker.");

                Assert.That(controller.TryUseSelectedTargetCard(target), Is.True);
                Assert.That(controller.IsAttackSelectionActive, Is.False);
                Assert.That(controller.HandCardSelectionPanel.IsActive, Is.True);
                Assert.That(controller.HandCardSelectionPanel.PromptText, Is.EqualTo("소멸시킬 카드를 선택하세요."),
                    "Exile-cost cards should explain the Card Selection Overlay action in Korean.");
                Assert.That(state.Monsters.Single().Hp, Is.EqualTo(20),
                    "Choosing the target should not resolve damage until the hand-card cost selection is confirmed.");

                Assert.That(controller.ToggleHandCardSelection("strike-instance"), Is.True);
                Assert.That(controller.ConfirmHandCardSelection(), Is.True);

                Assert.That(state.Monsters.Single().Hp, Is.EqualTo(15));
                Assert.That(state.GetCombatCards().Single(card => card.InstanceId == "sacrifice-instance").IsDiscarded, Is.True);
                Assert.That(state.ActionDeck.RemovedPile.Single(card => card.InstanceId == "strike-instance").Id, Is.EqualTo("strike-test"));
                Assert.That(state.ActionDeck.DiscardPile.All(card => card.InstanceId != "strike-instance"), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TestConfiguredAtlasPresentationSupportsMoveSelectionAndScreenClick()
        {
            var root = new GameObject("Atlas M2 Integration Test");
            var atlasRoot = new GameObject("Atlas Presentation");
            var cameraObject = new GameObject("Atlas Test Camera");
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            prefab.name = "AtlasM2IntegrationTopPrefab";
            try
            {
                var atlas = atlasRoot.AddComponent<AtlasTilePresentationView>();
                catalog.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("atlas-m2", prefab) });
                atlas.ConfigureForTests(catalog, heightStep: 0.5f);
                var input = root.AddComponent<HexMapInputController>();
                var controller = root.AddComponent<MapCombatController>();
                var camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 5f;
                camera.pixelRect = new Rect(0f, 0f, 800f, 600f);
                camera.transform.position = new Vector3(0f, 10f, 0f);
                camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

                var destination = new HexCoord(1, 0);
                // The default demo-monster auto-spawn contract is gone, so the fixture spawns the
                // enemy explicitly — the EnemyMarker parenting assert below needs a living monster.
                var map = new HexMapData(new[]
                {
                    new HexCellData(new HexCoord(0, 0), "tile-start", "street", 1, true, false, atlasVisualId: "atlas-m2", heightLevel: 0),
                    new HexCellData(destination, "tile-destination", "street", 1, true, false, atlasVisualId: "atlas-m2", heightLevel: 1),
                    new HexCellData(new HexCoord(2, 0), "tile-enemy", "street", 1, true, false, atlasVisualId: "atlas-m2", heightLevel: 1)
                },
                monsterSpawnRefs: new[] { new HexMonsterSpawnRef("spawn-enemy", "M001", new HexCoord(2, 0)) });

                controller.ConfigureForTests(atlas, input, camera);
                controller.ConfigureMapForTests(map);
                controller.ConfigureDebugRevealAllMapCellsForTests(true);
                controller.InitializeIntegration();
                Assert.That(controller.ActiveOverlayRendererBackendForTests, Is.EqualTo(CombatOverlayRendererBackend.BatchedMesh));

                Assert.That(controller.State, Is.Not.Null);
                Assert.That(atlas.TopVisualCount, Is.EqualTo(map.Count));
                Assert.That(controller.EnemyMarker.transform.parent, Is.EqualTo(atlas.transform));
                input.ConfigurePointerPollingForTests(false);
                Assert.That(input.TryPointerHex(out _), Is.False, "Mouse-less EditMode should keep pointer polling inert while direct screen-click tests use the same Atlas route.");

                Assert.That(controller.BeginMoveSelection(), Is.True);
                Assert.That(controller.Reachable.ContainsKey(destination), Is.True);
                Assert.That(controller.GetCombatOverlayActiveCount(HexOverlayLayer.Reachable), Is.EqualTo(controller.Reachable.Count));

                var world = atlas.transform.TransformPoint(atlas.ProjectTop(destination));
                var screen = camera.WorldToScreenPoint(world);

                Assert.That(controller.TryMoveFromScreenPosition(screen), Is.True);
                Assert.That(controller.State.PlayerCoord, Is.EqualTo(destination));
                AssertVector3(atlas.ProjectOverlaySurface(destination) + Vector3.up * 0.32f, atlas.PlayerMarkerLocalPosition);
                Assert.That(controller.GetCombatOverlayActiveCount(HexOverlayLayer.Reachable), Is.EqualTo(0));
                Assert.That(atlas.VisibilityRefreshCount, Is.GreaterThanOrEqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(atlasRoot);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(prefab);
            }
        }

        private static void AssertMonsterMarkerLabelsAndBadges(AtlasTilePresentationView atlas, int expectedCount)
        {
            var labels = atlas.GetComponentsInChildren<Transform>(includeInactive: true)
                .Where(transform => transform.name == "Monster Marker Label" && transform.gameObject.activeInHierarchy)
                .Select(ReadComponentText)
                .ToArray();
            Assert.That(labels, Has.Length.EqualTo(expectedCount));
            for (var i = 1; i <= expectedCount; i++)
            {
                Assert.That(labels.Any(label => label.StartsWith($"M{i}\n")), Is.True, $"Monster marker label M{i} should be visible.");
            }

            var badgeColorKeys = atlas.GetComponentsInChildren<Transform>(includeInactive: true)
                .Where(transform => transform.name == "Monster Marker Badge" && transform.gameObject.activeInHierarchy)
                .Select(transform => transform.GetComponent<Renderer>())
                .Where(renderer => renderer != null && renderer.sharedMaterial != null)
                .Select(renderer => ColorKey(renderer.sharedMaterial.color))
                .Distinct()
                .ToArray();
            Assert.That(badgeColorKeys, Has.Length.EqualTo(expectedCount), "Each living monster marker should receive a distinct accent badge color.");
        }

        private static string ReadComponentText(Transform transform)
        {
            var component = transform.GetComponent("TextMeshPro");
            var textProperty = component?.GetType().GetProperty("text");
            return textProperty?.GetValue(component) as string ?? string.Empty;
        }

        private static CombatState CreateSacrificeAttackState(HexCoord enemyCoord)
        {
            var config = new CombatConfig(20, 20, 2, 1, 4, 4, 0, 1, 3, actionBudget: 4, movementHandSize: 1, actionHandSize: 3);
            var move = new CardDefinition(
                "move-test",
                "Move Test",
                CardCategory.Movement,
                CardEffectType.Move,
                1,
                1,
                1,
                effectRef: CardEffectRefs.MoveBasic,
                targeting: "reachable_hex",
                instanceId: "move-instance");
            var sacrifice = new CardDefinition(
                ApprovedCardCatalogFactory.AttackSacrificeId,
                "Sacrifice Attack",
                CardCategory.Action,
                CardEffectType.Attack,
                1,
                1,
                5,
                effectRef: CardEffectRefs.AttackDamage,
                targeting: "living_monster_and_hand_card",
                status: CardCatalogStatus.Approved,
                targetMode: CardTargetMode.Enemy,
                instanceId: "sacrifice-instance",
                additionalCost: CardBehaviorMetadata.AdditionalCostExileSelectedHandCards);
            var strike = new CardDefinition(
                "strike-test",
                "Strike Test",
                CardCategory.Action,
                CardEffectType.Attack,
                1,
                1,
                2,
                effectRef: CardEffectRefs.AttackDamage,
                targeting: "living_monster_in_range",
                status: CardCatalogStatus.Approved,
                instanceId: "strike-instance");
            var defend = new CardDefinition(
                "defend-test",
                "Defend Test",
                CardCategory.Action,
                CardEffectType.Defend,
                1,
                0,
                2,
                effectRef: CardEffectRefs.DefendBlock,
                targeting: "self",
                playMode: CardPlayMode.Self,
                status: CardCatalogStatus.Approved,
                instanceId: "defend-instance");
            var catalog = new CardCatalogDefinition(
                "test.sacrifice-target-first",
                "Sacrifice target-first test catalog",
                new[]
                {
                    new CardCatalogEntry(move.Id, move.DisplayName, move.Category, move.EffectType, move.Cost, move.Range, move.Amount, move.EffectRef, move.Targeting, status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(sacrifice.Id, sacrifice.DisplayName, sacrifice.Category, sacrifice.EffectType, sacrifice.Cost, sacrifice.Range, sacrifice.Amount, sacrifice.EffectRef, sacrifice.Targeting, status: CardCatalogStatus.Approved, targetMode: CardTargetMode.Enemy, additionalCost: sacrifice.AdditionalCost),
                    new CardCatalogEntry(strike.Id, strike.DisplayName, strike.Category, strike.EffectType, strike.Cost, strike.Range, strike.Amount, strike.EffectRef, strike.Targeting, status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(defend.Id, defend.DisplayName, defend.Category, defend.EffectType, defend.Cost, defend.Range, defend.Amount, defend.EffectRef, defend.Targeting, playMode: CardPlayMode.Self, status: CardCatalogStatus.Approved)
                });
            return new CombatState(
                CombatState.CreateDemoMap(2),
                new HexCoord(0, 0),
                enemyCoord,
                config,
                cardCatalog: catalog,
                movementDeck: new CardDeckState(null, new[] { move }, null, null),
                actionDeck: new CardDeckState(null, new[] { sacrifice, strike, defend }, null, null),
                drawOpeningHands: false);
        }

        private static HexSparseMapAuthoringSource CreateSparseSourceForPurpose(string name, HexMapPurpose purpose)
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
                purpose,
                objectRefs: new[]
                {
                    new HexMapObjectRef("player-spawn", HexMapObjectType.PlayerSpawn, string.Empty, 0, 0),
                    new HexMapObjectRef("reach-finish", HexMapObjectType.ObjectiveMarker, "finish-landmark", 2, 0, "Finish", purpose, interactable: true),
                    new HexMapObjectRef("enemy-spawn", HexMapObjectType.MonsterSpawn, "M001", 1, 0, "primary_pressure", purpose)
                });
            return source;
        }

        private static void AssertVector3(Vector3 expected, Vector3 actual)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.001f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.001f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.001f));
        }

        private static void SetControllerState(MapCombatController controller, CombatState state)
        {
            typeof(MapCombatController)
                .GetProperty(nameof(MapCombatController.State), BindingFlags.Instance | BindingFlags.Public)
                .GetSetMethod(nonPublic: true)
                .Invoke(controller, new object[] { state });
        }

        private static void SetPrivateField<T>(MapCombatController controller, string fieldName, T value)
        {
            typeof(MapCombatController)
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(controller, value);
        }

        private static string ColorKey(Color color)
        {
            return $"{Mathf.RoundToInt(color.r * 255f)}:{Mathf.RoundToInt(color.g * 255f)}:{Mathf.RoundToInt(color.b * 255f)}:{Mathf.RoundToInt(color.a * 255f)}";
        }
    }
}


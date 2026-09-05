#if UNITY_EDITOR
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// MainGameplay.unity is the authoring source of truth (AGENTS.md -> "Unity Rules");
    /// PrototypeTest.unity is a legacy dev sandbox. These checks read scene/prefab YAML
    /// as text (no scene load) to catch unintended drift without the multi-minute cost of
    /// opening the 300MB+ MainGameplay scene in a test run.
    /// </summary>
    public sealed class MainGameplaySceneAuthorityTests
    {
        private const string MainGameplayScenePath = "Assets/Scenes/Game/MainGameplay.unity";
        private const string PrototypeTestScenePath = TestAssetPaths.PrototypeTestScene;
        private const string CardLanePrefabPath = "Assets/Prefabs/UI/Prototype/CardLane.prefab";
        private const string MoveCardFrontPrefabPath = TestAssetPaths.MoveCardFrontPrefab;
        private const string ActionCardFrontPrefabPath = TestAssetPaths.ActionCardFrontPrefab;

        private const string MonsterCatalogCsvPath = "Assets/Data/Combat/Monsters/Source/monster_catalog.csv";

        private const string MapCombatControllerScriptGuid = "d9c59f35b97d4c798e1309eada56c1c6";

        private static readonly string[] SharedDataAssetFields =
        {
            "cardCatalogAsset",
            "terrainPalette",
            "catalogTextAssetSource",
            "startingDeckAsset",
            "soundCatalog",
            "cameraProfile",
        };

        [Test]
        public void MainGameplayAndPrototypeTestShareCoreCatalogBindings()
        {
            var mainBlock = ExtractYamlDocumentContaining(MainGameplayScenePath, "guid: " + MapCombatControllerScriptGuid);
            var protoBlock = ExtractYamlDocumentContaining(PrototypeTestScenePath, "guid: " + MapCombatControllerScriptGuid);

            Assert.That(mainBlock, Is.Not.Null,
                "MainGameplay must contain a MapCombatController — it is the authoring source of truth for the shipped combat scene.");
            Assert.That(protoBlock, Is.Not.Null,
                "PrototypeTest must contain a MapCombatController.");

            foreach (var field in SharedDataAssetFields)
            {
                var mainGuid = ExtractReferenceGuid(mainBlock, field);
                var protoGuid = ExtractReferenceGuid(protoBlock, field);
                Assert.That(mainGuid, Is.EqualTo(protoGuid),
                    $"MapCombatController.{field} diverged between MainGameplay (authoring source) and " +
                    "PrototypeTest (legacy sandbox). If this divergence is intentional, update this test's " +
                    "baseline; otherwise PrototypeTest has drifted from the shipped data configuration.");
            }
        }

        [Test]
        public void CardLanePrefabDefaultsKeepMoveAndActionSlotPrefabsNonNull()
        {
            var text = File.ReadAllText(CardLanePrefabPath);

            // MainGameplay does not override GameplayCardLaneView.cardSlotPrefab on its CardLane
            // instance (PrototypeTest does, historically). That is currently harmless only because
            // ResolveMoveCardSlotPrefab/ResolveActionCardSlotPrefab fall back to the generic
            // cardSlotPrefab exclusively when the specific slot is null. Guard the prefab's own
            // defaults so that fallback path is never silently exercised on the shipped scene.
            //
            // Assert the exact authored CardFront prefabs (not merely non-null): the bucket-C scene
            // repair rebound these to the CardFront_Move/CardFront_Action family, and a wrong-but-
            // non-null rebind is exactly the drift a non-null-only guard would miss.
            var moveGuid = AssetDatabase.AssetPathToGUID(MoveCardFrontPrefabPath);
            var actionGuid = AssetDatabase.AssetPathToGUID(ActionCardFrontPrefabPath);
            Assert.That(moveGuid, Is.Not.Empty, "CardFront_Move.prefab should exist for the CardLane default binding.");
            Assert.That(actionGuid, Is.Not.Empty, "CardFront_Action.prefab should exist for the CardLane default binding.");
            Assert.That(ExtractFieldValueLine(text, "moveCardSlotPrefab"), Does.Contain("guid: " + moveGuid),
                "CardLane.prefab's default moveCardSlotPrefab must bind CardFront_Move; MainGameplay relies on " +
                "this default instead of a scene-level override.");
            Assert.That(ExtractFieldValueLine(text, "actionCardSlotPrefab"), Does.Contain("guid: " + actionGuid),
                "CardLane.prefab's default actionCardSlotPrefab must bind CardFront_Action; MainGameplay relies on " +
                "this default instead of a scene-level override.");
        }

        [Test]
        public void MainGameplayReliesOnCardLanePrefabSlotDefaultsWithoutOverride()
        {
            // The guard above only protects CardLane.prefab's defaults. MainGameplay must additionally
            // NOT override moveCardSlotPrefab/actionCardSlotPrefab on its own CardLane instance — a
            // scene-level override (especially to fileID: 0) would silently regress the shipped hand
            // cards to the null generic cardSlotPrefab fallback, exactly the parity gap this suite
            // guards. If an intentional override is added later, update this test to assert its value.
            Assert.That(SceneContainsLine(MainGameplayScenePath, "propertyPath: moveCardSlotPrefab"), Is.False,
                "MainGameplay must not override moveCardSlotPrefab on its CardLane instance; it should use the CardLane.prefab default.");
            Assert.That(SceneContainsLine(MainGameplayScenePath, "propertyPath: actionCardSlotPrefab"), Is.False,
                "MainGameplay must not override actionCardSlotPrefab on its CardLane instance; it should use the CardLane.prefab default.");
        }

        /// <summary>
        /// 출하 배선 감사: 카탈로그가 시각 프리팹을 저작한 몬스터는 MainGameplay 씬의
        /// <c>monsterVisualPrefabBindings</c>에도 반드시 들어 있어야 한다.
        ///
        /// 이 감사가 없어서 실제로 사고가 났다 — 신규 요괴 6종(M008~M014)은 카탈로그·프리팹·
        /// 애니메이터가 전부 정상인데 씬 바인딩만 옛 목록(M001~M006·M901~M905)에 멈춰 있었고,
        /// 에디터는 <c>ResolveMonsterVisualPrefab</c>의 <c>#if UNITY_EDITOR</c> 카탈로그 폴백이
        /// 덮어 줘서 아무 증상도 내지 않았다. 폴백이 없는 <b>빌드에서만</b> 여섯 마리가 모델도
        /// 애니메이션도 없는 마커로 나온다 — 에디터에서는 절대 드러나지 않는 부류의 결함이라,
        /// 「에디터에서 잘 보인다」는 증거가 되지 못한다.
        /// </summary>
        [Test]
        [Category("ShippingData")]
        public void MainGameplayBindsEveryAuthoredMonsterVisualPrefab()
        {
            var block = ExtractYamlDocumentContaining(MainGameplayScenePath, "guid: " + MapCombatControllerScriptGuid);
            Assert.That(block, Is.Not.Null, "MainGameplay must contain a MapCombatController.");

            var authored = ReadAuthoredMonsterVisualPaths();
            Assert.That(authored, Is.Not.Empty,
                "monster_catalog.csv에서 visualPrefabPath를 하나도 못 읽었다 — 감사가 빈 채로 통과하고 있다.");

            var failures = new System.Collections.Generic.List<string>();
            foreach (var (monsterId, prefabPath) in authored)
            {
                var expectedGuid = AssetDatabase.AssetPathToGUID(prefabPath);
                if (string.IsNullOrEmpty(expectedGuid))
                {
                    failures.Add($"{monsterId}: 저작된 프리팹이 없다 ({prefabPath}).");
                    continue;
                }

                var bound = Regex.Match(
                    block,
                    $@"^\s*-\s*definitionId:\s*{Regex.Escape(monsterId)}\s*$\r?\n\s*prefab:\s*\{{fileID:\s*-?\d+(?:,\s*guid:\s*([0-9a-f]+))?",
                    RegexOptions.Multiline);
                if (!bound.Success)
                {
                    failures.Add($"{monsterId}: monsterVisualPrefabBindings에 항목이 없다 — 빌드에서 모델 없이 나온다.");
                    continue;
                }

                var boundGuid = bound.Groups[1].Success ? bound.Groups[1].Value : null;
                if (!string.Equals(boundGuid, expectedGuid, System.StringComparison.Ordinal))
                {
                    failures.Add($"{monsterId}: 바인딩이 다른 프리팹을 가리킨다 (scene={boundGuid ?? "null"} / catalog={expectedGuid} {prefabPath}).");
                }
            }

            Assert.That(failures, Is.Empty,
                "MapCombatController의 씬 바인딩이 monster_catalog.csv와 갈라졌다. " +
                "MapCombatController 인스펙터의 컨텍스트 메뉴 'Populate Monster Visual Prefabs'로 다시 채우고 씬을 저장할 것:\n  " +
                string.Join("\n  ", failures));
        }

        /// <summary>monsterId → visualPrefabPath. 따옴표로 감싼 designerNote가 있으므로 인용 인식 분해가 필요하다.</summary>
        private static System.Collections.Generic.List<(string MonsterId, string PrefabPath)> ReadAuthoredMonsterVisualPaths()
        {
            var rows = new System.Collections.Generic.List<(string, string)>();
            var lines = File.ReadAllLines(MonsterCatalogCsvPath);
            if (lines.Length == 0)
            {
                return rows;
            }

            var header = SplitCsvLine(lines[0]);
            var idIndex = header.FindIndex(name => string.Equals(name.Trim().TrimStart('\uFEFF'), "monsterId", System.StringComparison.Ordinal));
            var pathIndex = header.FindIndex(name => string.Equals(name.Trim(), "visualPrefabPath", System.StringComparison.Ordinal));
            Assert.That(idIndex, Is.GreaterThanOrEqualTo(0), $"monsterId 컬럼이 없다: {MonsterCatalogCsvPath}");
            Assert.That(pathIndex, Is.GreaterThanOrEqualTo(0), $"visualPrefabPath 컬럼이 없다: {MonsterCatalogCsvPath}");

            for (var i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                var fields = SplitCsvLine(lines[i]);
                if (fields.Count <= pathIndex)
                {
                    continue;
                }

                var path = fields[pathIndex].Trim();
                if (path.Length > 0)
                {
                    rows.Add((fields[idIndex].Trim(), path));
                }
            }

            return rows;
        }

        private static System.Collections.Generic.List<string> SplitCsvLine(string line)
        {
            var fields = new System.Collections.Generic.List<string>();
            var current = new System.Text.StringBuilder();
            var inQuotes = false;
            foreach (var c in line)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (c == ',' && !inQuotes)
                {
                    fields.Add(current.ToString());
                    current.Clear();
                    continue;
                }

                current.Append(c);
            }

            fields.Add(current.ToString());
            return fields;
        }

        private static bool SceneContainsLine(string assetPath, string needle)
        {
            using var reader = new StreamReader(assetPath);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Contains(needle))
                {
                    return true;
                }
            }
            return false;
        }

        private static string ExtractYamlDocumentContaining(string assetPath, string needle)
        {
            using var reader = new StreamReader(assetPath);
            var current = new System.Text.StringBuilder();
            string line;
            string match = null;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.StartsWith("--- !u!"))
                {
                    if (match == null && current.Length > 0 && current.ToString().Contains(needle))
                    {
                        match = current.ToString();
                    }
                    current.Clear();
                }
                current.AppendLine(line);
            }
            if (match == null && current.ToString().Contains(needle))
            {
                match = current.ToString();
            }
            return match;
        }

        private static string ExtractReferenceGuid(string yamlBlock, string fieldName)
        {
            var m = Regex.Match(yamlBlock, $@"^\s*{Regex.Escape(fieldName)}:\s*\{{fileID:\s*-?\d+(?:,\s*guid:\s*([0-9a-f]+))?", RegexOptions.Multiline);
            if (!m.Success)
            {
                return null;
            }
            return m.Groups[1].Success ? m.Groups[1].Value : null;
        }

        private static string ExtractFieldValueLine(string text, string fieldName)
        {
            var m = Regex.Match(text, $@"^\s*{Regex.Escape(fieldName)}:.*$", RegexOptions.Multiline);
            Assert.That(m.Success, Is.True, $"Field '{fieldName}' not found in {CardLanePrefabPath}.");
            return m.Value;
        }
    }
}
#endif

#nullable enable
using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using AIGD;
using com.IvanMurzak.McpPlugin;
using com.IvanMurzak.ReflectorNet.Utils;
using UnityEditor;

namespace SeoulPlayup.Combat.Unity.Editor.AiTools
{
    [AiToolType]
    public partial class Tool_TurnFlowWiring
    {
        [AiTool
        (
            "turn-flow-wiring-audit",
            Title = "Turn Flow Wiring / Audit",
            ReadOnlyHint = true,
            IdempotentHint = true
        )]
        [Description("authored 씬의 MainGameplayController 배선을 씬 YAML 스크래핑으로 확인한다: 컨트롤러 존재, " +
            "combatController·mapSourceLoader 참조 바인딩, MapCombatController 존재. 추가로 reflection으로 auto-init/" +
            "auto-save 배선 코드 사실 존재(MapCombatController.initializeOnStart 필드, MainGameplayController의 " +
            "HookRunAutoSave·HandleOverallTurnEndedForSave, CombatState.OverallTurnEnded 이벤트)를 확인한다(멤버 존재만; " +
            "런타임 도달성은 EditMode 스위트가 커버). 씬 로드 없는 읽기 전용 감사.")]
        public TurnFlowWiringResult Audit
        (
            [Description("검사할 씬 경로. 기본 MainGameplay(정본).")]
            string sceneName = "Assets/Scenes/Game/MainGameplay.unity"
        )
        {
            return MainThread.Instance.Run(() =>
            {
                var result = new TurnFlowWiringResult { scenePath = sceneName };

                if (!File.Exists(sceneName))
                {
                    throw new InvalidOperationException($"Scene not found: '{sceneName}'.");
                }

                var controllerGuid = ResolveScriptGuid("MainGameplayController");
                if (controllerGuid == null)
                {
                    throw new InvalidOperationException("Could not resolve a MonoScript guid for MainGameplayController.");
                }

                var block = ExtractYamlDocumentContaining(sceneName, "guid: " + controllerGuid);
                result.hasMainGameplayController = block != null;
                if (block == null)
                {
                    result.notes.Add("Scene has no MainGameplayController component.");
                    return result;
                }

                var combatFileId = ExtractFileId(block, "combatController");
                result.combatControllerBound = combatFileId.HasValue && combatFileId.Value != 0;
                if (!result.combatControllerBound)
                {
                    result.notes.Add("combatController is unbound; the controller relies on FindFirstObjectByType at runtime.");
                }

                var loaderFileId = ExtractFileId(block, "mapSourceLoader");
                result.mapSourceLoaderBound = loaderFileId.HasValue && loaderFileId.Value != 0;

                var applyFlag = ExtractIntValue(block, "applyStageMapSource");
                result.applyStageMapSource = applyFlag switch
                {
                    1 => "true",
                    0 => "false",
                    _ => "unknown"
                };

                var mapControllerGuid = ResolveScriptGuid("MapCombatController");
                if (mapControllerGuid == null)
                {
                    result.notes.Add("Could not resolve MapCombatController script guid; presence check skipped.");
                }
                else
                {
                    result.mapCombatControllerPresent = SceneContainsLine(sceneName, "guid: " + mapControllerGuid);
                    if (!result.mapCombatControllerPresent)
                    {
                        result.notes.Add("No MapCombatController component found in the scene.");
                    }
                }

                CheckWiringCodeFacts(result);

                return result;
            });
        }

        // Reflection pass: confirm the auto-init / auto-save wiring members still exist. This guards against
        // silent rename/removal of the code that the scene YAML check cannot see. It verifies member
        // PRESENCE only — whether the save actually fires at runtime is covered by the EditMode suite.
        private static void CheckWiringCodeFacts(TurnFlowWiringResult result)
        {
            const BindingFlags anyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            var mapCombatController = FindType("SeoulPlayup.Combat.Unity.MapCombatController");
            if (mapCombatController == null)
            {
                result.notes.Add("Reflection: type SeoulPlayup.Combat.Unity.MapCombatController not found.");
            }
            else
            {
                result.autoInitFieldPresent = mapCombatController.GetField("initializeOnStart", anyInstance) != null;
                if (!result.autoInitFieldPresent)
                {
                    result.notes.Add("Reflection: MapCombatController.initializeOnStart auto-init field is missing.");
                }
            }

            var mainGameplayController = FindType("SeoulPlayup.Flow.Unity.MainGameplayController");
            if (mainGameplayController == null)
            {
                result.notes.Add("Reflection: type SeoulPlayup.Flow.Unity.MainGameplayController not found.");
            }
            else
            {
                var hasHook = mainGameplayController.GetMethod("HookRunAutoSave", anyInstance) != null;
                var hasHandler = mainGameplayController.GetMethod("HandleOverallTurnEndedForSave", anyInstance) != null;
                result.autoSaveHookPresent = hasHook && hasHandler;
                if (!hasHook)
                {
                    result.notes.Add("Reflection: MainGameplayController.HookRunAutoSave is missing.");
                }

                if (!hasHandler)
                {
                    result.notes.Add("Reflection: MainGameplayController.HandleOverallTurnEndedForSave is missing.");
                }
            }

            var combatState = FindType("SeoulPlayup.Combat.Runtime.CombatState");
            if (combatState == null)
            {
                result.notes.Add("Reflection: type SeoulPlayup.Combat.Runtime.CombatState not found.");
            }
            else
            {
                result.overallTurnEndedEventPresent = combatState.GetEvent("OverallTurnEnded", anyInstance) != null;
                if (!result.overallTurnEndedEventPresent)
                {
                    result.notes.Add("Reflection: CombatState.OverallTurnEnded event is missing (auto-save hook target).");
                }
            }

            result.codeFactsOk = result.autoInitFieldPresent
                && result.autoSaveHookPresent
                && result.overallTurnEndedEventPresent;
        }

        private static Type? FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName, throwOnError: false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static string? ResolveScriptGuid(string typeName)
        {
            foreach (var guid in AssetDatabase.FindAssets($"t:MonoScript {typeName}"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script != null && script.GetClass()?.Name == typeName)
                {
                    return guid;
                }
            }

            return null;
        }

        private static bool SceneContainsLine(string assetPath, string needle)
        {
            using var reader = new StreamReader(assetPath);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Contains(needle))
                {
                    return true;
                }
            }

            return false;
        }

        private static string? ExtractYamlDocumentContaining(string assetPath, string needle)
        {
            using var reader = new StreamReader(assetPath);
            var current = new StringBuilder();
            string? match = null;
            string? line;
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

        private static long? ExtractFileId(string yamlBlock, string fieldName)
        {
            var match = Regex.Match(
                yamlBlock,
                $@"^\s*{Regex.Escape(fieldName)}:\s*\{{fileID:\s*(-?\d+)",
                RegexOptions.Multiline);
            return match.Success && long.TryParse(match.Groups[1].Value, out var value) ? value : (long?)null;
        }

        private static int? ExtractIntValue(string yamlBlock, string fieldName)
        {
            var match = Regex.Match(
                yamlBlock,
                $@"^\s*{Regex.Escape(fieldName)}:\s*(-?\d+)\s*$",
                RegexOptions.Multiline);
            return match.Success && int.TryParse(match.Groups[1].Value, out var value) ? value : (int?)null;
        }
    }
}

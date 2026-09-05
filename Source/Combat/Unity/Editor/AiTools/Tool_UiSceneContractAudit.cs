#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using AIGD;
using com.IvanMurzak.McpPlugin;
using com.IvanMurzak.ReflectorNet.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity.Editor.AiTools
{
    [AiToolType]
    public partial class Tool_UiSceneContractAudit
    {
        [AiTool
        (
            "ui-scene-contract-audit",
            Title = "UI Scene Contract / Audit",
            ReadOnlyHint = true,
            IdempotentHint = true
        )]
        [Description("GameplaySceneFactory.Build()로 UI/UX 프로토타입 씬 계약을 메모리에 구성해 문서화된 " +
            "필수 화면·게임플레이 레이어·루트 인벤토리와 raycast 계약(장식 패널 non-raycast·카드 드로어 clickable·" +
            "TMP_Text 무 raycast)·폰트 계약(한글 SDF)·legacy 카피 부재·단일 EventSystem(+InputSystemUIInputModule)을 " +
            "검증한 뒤 즉시 파괴한다(씬 저장/오염 없음). 읽기 전용 감사.")]
        public UiSceneContractAuditResult Audit()
        {
            return MainThread.Instance.Run(() =>
            {
                var result = new UiSceneContractAuditResult();

                GameplaySceneContract? contract = null;
                try
                {
                    // GameplaySceneFactory is internal (visible only to the test assembly), so build
                    // through reflection to avoid touching game source with an InternalsVisibleTo grant.
                    // The returned contract type is public, so everything after this is a direct call.
                    var factoryType = typeof(GameplaySceneContract).Assembly
                        .GetType("SeoulPlayup.Combat.Unity.GameplaySceneFactory");
                    var buildMethod = factoryType?.GetMethod("Build", BindingFlags.Public | BindingFlags.Static);
                    if (buildMethod == null)
                    {
                        throw new InvalidOperationException("GameplaySceneFactory.Build could not be located.");
                    }

                    contract = (GameplaySceneContract)buildMethod.Invoke(null, new object?[] { null });

                    result.canvasPresent = contract.Canvas != null;
                    result.cardRailRootPresent = contract.CardRailRoot != null;

                    CheckScreen(contract, GameplaySceneContract.ScreenTitleName, result.missingScreens);
                    CheckScreen(contract, GameplaySceneContract.ScreenBriefingName, result.missingScreens);
                    CheckScreen(contract, GameplaySceneContract.ScreenGameplayName, result.missingScreens);
                    CheckScreen(contract, GameplaySceneContract.ScreenPauseName, result.missingScreens);
                    CheckScreen(contract, GameplaySceneContract.ScreenGameOverName, result.missingScreens);
                    CheckScreen(contract, GameplaySceneContract.ScreenClearName, result.missingScreens);

                    CheckLayer(contract, GameplaySceneContract.WorldMapLayerName, result.missingLayers);
                    CheckLayer(contract, GameplaySceneContract.LegendLayerName, result.missingLayers);
                    CheckLayer(contract, GameplaySceneContract.HudLayerName, result.missingLayers);
                    CheckLayer(contract, GameplaySceneContract.CardHandLayerName, result.missingLayers);
                    CheckLayer(contract, GameplaySceneContract.SystemUiLayerName, result.missingLayers);
                    CheckLayer(contract, GameplaySceneContract.DevOnlyLayerName, result.missingLayers);

                    CheckRoot(contract, GameplaySceneContract.PlayerUiRootName, result.missingRoots);
                    CheckRoot(contract, GameplaySceneContract.DevUiRootName, result.missingRoots);

                    CheckFonts(contract, result.fontViolations);
                    CheckLegacyCopy(contract, result.legacyCopyViolations);
                    CheckRaycasts(contract, result.raycastViolations);
                    CheckEventSystem(contract, result);

                    result.allContractMet = result.canvasPresent
                        && result.cardRailRootPresent
                        && result.missingScreens.Count == 0
                        && result.missingLayers.Count == 0
                        && result.missingRoots.Count == 0
                        && result.fontViolations.Count == 0
                        && result.legacyCopyViolations.Count == 0
                        && result.raycastViolations.Count == 0
                        && result.eventSystemOk;
                }
                finally
                {
                    if (contract != null)
                    {
                        // Build() parents the EventSystem and Canvas under the root, so destroying the
                        // root removes the entire temporary tree — no active-scene pollution persists.
                        UnityEngine.Object.DestroyImmediate(contract.gameObject);
                    }
                }

                return result;
            });
        }

        private static void CheckScreen(GameplaySceneContract contract, string name, List<string> missing)
        {
            if (!contract.HasRequiredScreen(name))
            {
                missing.Add(name);
            }
        }

        private static void CheckLayer(GameplaySceneContract contract, string name, List<string> missing)
        {
            if (!contract.HasRequiredGameplayLayer(name))
            {
                missing.Add(name);
            }
        }

        private static void CheckRoot(GameplaySceneContract contract, string name, List<string> missing)
        {
            if (!contract.HasRequiredGameplayRoot(name))
            {
                missing.Add(name);
            }
        }

        private const string KoreanFontMarker = "DNFForgedBlade";

        // Retired objective copy that must never resurface in player-facing text.
        private static readonly string[] LegacyObjectiveStrings =
        {
            "landmark-63", "63 Building", "Han River", "Yeouido", "Current Objective"
        };

        // Decorative panels/roots whose own Image must NOT be a raycast target, so map clicks pass through.
        private static readonly string[] DecorativeRaycastOffNames =
        {
            GameplaySceneContract.WorldMapLayerName,
            GameplaySceneContract.PlayerUiRootName,
            GameplaySceneContract.DevUiRootName,
            GameplaySceneContract.LegendLayerName,
            GameplaySceneContract.HudLayerName,
            GameplaySceneContract.TopLaneHudName,
            GameplaySceneContract.PlayerStatusClusterName,
            GameplaySceneContract.BagQuickSlotClusterName,
            GameplaySceneContract.MapInfoClusterName,
            GameplaySceneContract.SystemButtonClusterName,
            GameplaySceneContract.RelicCurseRailName,
            GameplaySceneContract.SystemUiLayerName,
            GameplaySceneContract.DevOnlyLayerName,
            GameplaySceneContract.DebugEvidenceName,
            GameplaySceneContract.CardRailRootName,
        };

        private static void CheckFonts(GameplaySceneContract contract, List<string> violations)
        {
            CheckFont("BriefingText", contract.BriefingText, violations);
            CheckFont("DebugEvidenceText", contract.DebugEvidenceText, violations);
            CheckFont("TopHudText", contract.TopHudText, violations);
            CheckFont("BottomResourceText", contract.BottomResourceText, violations);
            CheckFont("DevLogText", contract.DevLogText, violations);
        }

        private static void CheckFont(string role, TMP_Text text, List<string> violations)
        {
            if (text == null)
            {
                violations.Add($"{role}: text component missing");
                return;
            }

            if (text.font == null)
            {
                violations.Add($"{role}: font is null");
                return;
            }

            if (!text.font.name.Contains(KoreanFontMarker))
            {
                violations.Add($"{role}: font '{text.font.name}' is not '{KoreanFontMarker}'");
            }
        }

        private static void CheckLegacyCopy(GameplaySceneContract contract, List<string> violations)
        {
            var briefing = contract.BriefingText != null ? contract.BriefingText.text ?? string.Empty : string.Empty;
            foreach (var legacy in LegacyObjectiveStrings)
            {
                if (briefing.Contains(legacy))
                {
                    violations.Add($"briefing copy contains legacy string '{legacy}'");
                }
            }
        }

        private static void CheckRaycasts(GameplaySceneContract contract, List<string> violations)
        {
            var transforms = contract.GetComponentsInChildren<Transform>(true);
            foreach (var name in DecorativeRaycastOffNames)
            {
                var target = transforms.FirstOrDefault(transform => transform.name == name);
                if (target == null)
                {
                    violations.Add($"{name}: decorative object not found");
                    continue;
                }

                var image = target.GetComponent<Image>();
                if (image != null && image.raycastTarget)
                {
                    violations.Add($"{name}: Image.raycastTarget is true (should be false so map clicks pass through)");
                }
            }

            CheckDrawerRaycast("MoveCardDrawer", contract.MoveCardDrawer, violations);
            CheckDrawerRaycast("ActionCardDrawer", contract.ActionCardDrawer, violations);

            foreach (var text in contract.GetComponentsInChildren<TMP_Text>(true).Where(text => text.raycastTarget))
            {
                violations.Add($"{text.name}: TMP_Text.raycastTarget is true (text should never block raycasts)");
            }
        }

        private static void CheckDrawerRaycast(string role, GameplayCardDrawer drawer, List<string> violations)
        {
            if (drawer == null)
            {
                violations.Add($"{role}: drawer missing");
                return;
            }

            var image = drawer.GetComponent<Image>();
            if (image == null || !image.raycastTarget)
            {
                violations.Add($"{role}: drawer Image.raycastTarget is not true (drawer must stay clickable)");
            }
        }

        private static void CheckEventSystem(GameplaySceneContract contract, UiSceneContractAuditResult result)
        {
            // The factory parents the EventSystem under the contract root, so search within the built tree
            // (not the whole active scene) to audit exactly what Build() produced.
            var eventSystems = contract.GetComponentsInChildren<EventSystem>(true);
            var inputModules = contract.GetComponentsInChildren<InputSystemUIInputModule>(true);

            if (eventSystems.Length != 1)
            {
                result.eventSystemOk = false;
                result.eventSystemNote = $"expected exactly 1 EventSystem, found {eventSystems.Length}";
                return;
            }

            if (!eventSystems[0].gameObject.activeInHierarchy)
            {
                result.eventSystemOk = false;
                result.eventSystemNote = "EventSystem is not active in hierarchy";
                return;
            }

            if (inputModules.Length != 1)
            {
                result.eventSystemOk = false;
                result.eventSystemNote = $"expected exactly 1 InputSystemUIInputModule, found {inputModules.Length}";
                return;
            }

            if (inputModules[0].gameObject != eventSystems[0].gameObject)
            {
                result.eventSystemOk = false;
                result.eventSystemNote = "InputSystemUIInputModule is not on the EventSystem GameObject";
                return;
            }

            result.eventSystemOk = true;
            result.eventSystemNote = "single active EventSystem with InputSystemUIInputModule on the same GameObject";
        }
    }
}

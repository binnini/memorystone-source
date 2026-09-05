#nullable enable
using System.Collections.Generic;

namespace AIGD
{
    // Structured result for the `turn-flow-wiring-audit` MCP tool. Checks the authored scene wiring
    // of MainGameplayController by scraping scene YAML (no scene load), plus a reflection pass that
    // confirms the auto-init / auto-save wiring CODE FACTS still exist (member presence, not runtime
    // reachability — the EditMode suite covers whether the save actually fires on turn end).
    public sealed class TurnFlowWiringResult
    {
        public string scenePath = string.Empty;
        public bool hasMainGameplayController;
        // combatController serialized reference points at a scene object (fileID != 0). When false the
        // controller falls back to FindFirstObjectByType<MapCombatController>() at runtime.
        public bool combatControllerBound;
        public bool mapSourceLoaderBound;
        // Serialized applyStageMapSource flag ("true" / "false" / "unknown").
        public string applyStageMapSource = "unknown";
        // A MapCombatController component exists somewhere in the scene.
        public bool mapCombatControllerPresent;

        // --- Reflection code-fact checks (member presence only) ---
        // MapCombatController has the auto-init serialized field (`initializeOnStart`).
        public bool autoInitFieldPresent;
        // MainGameplayController has both save-hook methods (HookRunAutoSave + HandleOverallTurnEndedForSave).
        public bool autoSaveHookPresent;
        // CombatState exposes the OverallTurnEnded event the auto-save hook subscribes to.
        public bool overallTurnEndedEventPresent;
        // True when all three wiring code facts are present.
        public bool codeFactsOk;

        public List<string> notes = new();
    }
}

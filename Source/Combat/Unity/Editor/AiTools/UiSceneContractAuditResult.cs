#nullable enable
using System.Collections.Generic;

namespace AIGD
{
    // Structured result for the `ui-scene-contract-audit` MCP tool. Builds the UI/UX prototype scene
    // contract in memory and checks the documented screen/layer/root inventory plus raycast, font, and
    // legacy-copy contracts and the single-EventSystem rule, then tears it down.
    public sealed class UiSceneContractAuditResult
    {
        public bool canvasPresent;
        public bool cardRailRootPresent;
        public List<string> missingScreens = new();
        public List<string> missingLayers = new();
        public List<string> missingRoots = new();

        // --- Raycast contract: decorative panels must not block map clicks; card drawers must stay clickable ---
        // Decorative objects whose own Image.raycastTarget is true (should be false), drawers whose Image is
        // not raycastable (should be true), or TMP_Text that is raycastable (should never be). Also lists
        // decorative objects the audit expected but could not find.
        public List<string> raycastViolations = new();

        // --- Font contract: player-facing text must use the Korean SDF font ---
        // TMP_Text roles whose font is null or not a documented DNFForgedBlade face.
        public List<string> fontViolations = new();

        // --- Legacy-copy contract: retired objective strings must not resurface in player-facing copy ---
        // Legacy strings found in the briefing copy (should be none).
        public List<string> legacyCopyViolations = new();

        // --- Single EventSystem rule ---
        // True when the built contract owns exactly one active EventSystem with exactly one
        // InputSystemUIInputModule on the same GameObject.
        public bool eventSystemOk;
        public string eventSystemNote = string.Empty;

        public bool allContractMet;
    }
}

#nullable enable
using System.Collections.Generic;

namespace AIGD
{
    // Structured result for the `scene-binding-audit` MCP tool. Compares a controller's serialized
    // asset references between two scenes by scraping scene YAML (no scene load), to surface drift.
    public sealed class SceneBindingAuditResult
    {
        public string controllerTypeName = string.Empty;
        public string controllerScriptGuid = string.Empty;
        public string sceneA = string.Empty;
        public string sceneB = string.Empty;
        public bool sceneAHasController;
        public bool sceneBHasController;
        // "field: guid (assetPath)" for fields whose reference matches in both scenes.
        public List<string> matched = new();
        // "field: A=... vs B=..." for fields that differ between the scenes.
        public List<string> diverged = new();
        // Non-fatal notes (e.g. a scene lacking the controller block).
        public List<string> notes = new();
    }
}

namespace SeoulPlayup.Combat.Unity
{
    // Overlay-renderer backend selection extracted from MapCombatController (refactoring stage
    // 4-4). Scope is intentionally narrow: only the pure Auto/BatchedMesh/Atlas decision and its
    // status text move here. Presenter construction/lifecycle (EnsureOverlayPresenter/
    // CreateOverlayPresenter) stays on the host — it is tied to the host's view/renderer field
    // lifecycle (atlasTilePresentationView, batchedOverlayRenderer), mirroring why Cinemachine
    // component lifecycle stayed on the host in 4-1. The hover/selection-driven overlay show/clear
    // coordination (OnHexHovered et al.) also stays on the host since it is combat-selection
    // orchestration, not overlay backend selection.
    public static class CombatOverlayCoordinator
    {
        // Unified 2026-07-02 (승인된 권고안 A): tactical overlays always render through the
        // BatchedMesh backend. The Atlas/Auto enum values survive only to avoid scene
        // serialization churn; they no longer select a renderer. The Atlas per-tile tactical
        // path was removed (fog/visibility still goes through AtlasCombatOverlayRenderer).
        public static CombatOverlayRendererBackend ResolveOverlayRendererBackend(
            CombatOverlayRendererBackend configuredBackend)
        {
            return CombatOverlayRendererBackend.BatchedMesh;
        }

        public static string FormatOverlayRendererStatus(
            CombatOverlayRendererBackend configuredBackend, CombatOverlayRendererBackend activeBackend)
        {
            if (configuredBackend == CombatOverlayRendererBackend.Auto)
            {
                return $"Renderer: {activeBackend} (Auto)";
            }

            return $"Renderer: {activeBackend}";
        }
    }
}

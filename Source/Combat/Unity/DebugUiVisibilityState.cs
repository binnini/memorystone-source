namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Shared flag the debug UI ladder raises while gameplay UI is hidden.
    /// IMGUI dev overlays (the combat debug control window, the scene perf HUD) draw through
    /// OnGUI rather than a Canvas, so a CanvasGroup cannot reach them — they poll this instead.
    /// Owned by DebugUiVisibilityController; lives here because the Dev assembly references
    /// SeoulPlayup.Combat and not the other way round.
    /// </summary>
    public static class DebugUiVisibilityState
    {
        public static bool ImmediateModeDebugUiHidden { get; set; }
    }
}

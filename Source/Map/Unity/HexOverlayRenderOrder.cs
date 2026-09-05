using UnityEngine.Rendering;

namespace SeoulPlayup.Map.Unity
{
    /// <summary>
    /// Shared Unity-facing render ordering contract for map surface overlays.
    /// Keeps visibility fog below tactical overlays without coupling their presenters.
    /// </summary>
    public static class HexOverlayRenderOrder
    {
        public const float TileSurfaceLift = 0f;
        public const float VisibilityFogLift = 0.015f;
        public const float CombatFillLift = 0.035f;
        public const float CombatBoundaryLift = 0.045f;
        public const float ActorMarkerMinimumLift = 0.30f;

        public const int VisibilityFogRenderQueue = (int)RenderQueue.Transparent;
        public const int CombatOverlayRenderQueue = VisibilityFogRenderQueue + 10;
    }
}

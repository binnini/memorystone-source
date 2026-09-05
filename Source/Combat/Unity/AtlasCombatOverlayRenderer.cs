using System;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Visibility (fog) bridge from combat presentation to the Atlas tile view. Tactical
    /// overlays render exclusively through BatchedMeshCombatOverlayRenderer since the
    /// 2026-07-02 backend unification; only the fog path still goes through the Atlas view.
    /// </summary>
    public sealed class AtlasCombatOverlayRenderer : ICombatVisibilityRenderer
    {
        private readonly AtlasTilePresentationView view;

        public AtlasCombatOverlayRenderer(AtlasTilePresentationView view)
        {
            this.view = view;
        }

        public AtlasTilePresentationView View => view;

        public void ApplyVisibility(Func<HexCoord, HexVisibilitySafeCellInfo> visibilityProvider, long? stateEpoch = null)
        {
            view?.ApplyVisibility(visibilityProvider, stateEpoch);
        }
    }
}

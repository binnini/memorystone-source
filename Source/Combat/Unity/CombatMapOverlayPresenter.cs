using System;
using System.Collections.Generic;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Owns the Unity map overlay commands needed by combat presentation.
    /// Combat rule/query code still decides which cells belong to each overlay.
    /// </summary>
    public sealed class CombatMapOverlayPresenter
    {
        private readonly ICombatOverlayRenderer overlayRenderer;
        private ICombatVisibilityRenderer visibilityRenderer;
        private ICombatOverlayIconRenderer iconRenderer;

        public CombatMapOverlayPresenter(ICombatOverlayRenderer overlayRenderer = null)
        {
            this.overlayRenderer = overlayRenderer;
            visibilityRenderer = overlayRenderer as ICombatVisibilityRenderer;
        }

        public ICombatOverlayRenderer OverlayRenderer => overlayRenderer;

        public ICombatOverlayIconRenderer IconRenderer => iconRenderer;

        public void SetIconRenderer(ICombatOverlayIconRenderer renderer)
        {
            iconRenderer = renderer;
        }

        public void SetVisibilityRenderer(ICombatVisibilityRenderer renderer)
        {
            visibilityRenderer = renderer;
        }

        public void ApplyVisibility(Func<HexCoord, HexVisibilitySafeCellInfo> visibilityProvider, long? stateEpoch = null)
        {
            visibilityRenderer?.ApplyVisibility(visibilityProvider, stateEpoch);
        }

        public int GetActiveCount(HexOverlayLayer layer)
        {
            return overlayRenderer != null ? overlayRenderer.GetActiveCount(layer) : 0;
        }

        public void ApplyPresentation(CombatOverlayPresentation presentation)
        {
            if (presentation == null)
            {
                ClearTacticalLayers();
                iconRenderer?.Clear();
                return;
            }

            var includedLayers = new HashSet<HexOverlayLayer>();
            foreach (var layerState in presentation.Layers)
            {
                includedLayers.Add(layerState.Layer);
            }

            foreach (var layer in CombatOverlayPresentation.TacticalLayers)
            {
                if (!includedLayers.Contains(layer))
                {
                    ClearLayer(layer);
                }
            }

            foreach (var layerState in presentation.Layers)
            {
                overlayRenderer?.ShowLayer(layerState.Layer, layerState.Coords, layerState.Style);
            }

            // Distribute the icon annotation channel (empty input clears existing icons).
            iconRenderer?.ApplyAnnotations(presentation.Annotations);
        }

        public void ShowPlayerHoverMove(IEnumerable<HexCoord> coords) =>
            ShowLayer(HexOverlayLayer.PlayerHoverMove, coords);

        public void ClearPlayerHoverMove() =>
            ClearLayer(HexOverlayLayer.PlayerHoverMove);

        public void ShowPlayerHoverAction(IEnumerable<HexCoord> coords) =>
            ShowLayer(HexOverlayLayer.PlayerHoverAction, coords);

        public void ClearPlayerHoverAction() =>
            ClearLayer(HexOverlayLayer.PlayerHoverAction);

        public void ShowPlayerActionEffectArea(IEnumerable<HexCoord> coords) =>
            ShowLayer(HexOverlayLayer.PlayerActionEffectArea, coords);

        public void ClearPlayerActionEffectArea() =>
            ClearLayer(HexOverlayLayer.PlayerActionEffectArea);

        // Field object range is a hover-driven, non-tactical layer (not in CombatOverlayPresentation.
        // TacticalLayers), so the per-refresh tactical rebuild leaves it untouched — same lifecycle as
        // the PlayerHover* layers. The hovering path owns its show/clear.
        public void ShowFieldObjectRange(IEnumerable<HexCoord> coords) =>
            ShowLayer(HexOverlayLayer.FieldObjectRange, coords);

        public void ClearFieldObjectRange() =>
            ClearLayer(HexOverlayLayer.FieldObjectRange);

        public void ShowTutorialTileHighlight(IEnumerable<HexCoord> coords) =>
            ShowLayer(HexOverlayLayer.TutorialTarget, coords);

        public void ClearTutorialTileHighlight() =>
            ClearLayer(HexOverlayLayer.TutorialTarget);

        public void ShowDebugSelection(IEnumerable<HexCoord> coords)
        {
            ShowLayer(HexOverlayLayer.PlayerActionRange, coords);
        }

        private void ClearTacticalLayers()
        {
            ClearLayer(HexOverlayLayer.Reachable);
            ClearLayer(HexOverlayLayer.AttackRange);
            ClearLayer(HexOverlayLayer.Path);
            ClearLayer(HexOverlayLayer.PlayerActionRange);
            ClearLayer(HexOverlayLayer.PlayerActionEffectArea);
            ClearLayer(HexOverlayLayer.MonsterMoveIntent);
            ClearLayer(HexOverlayLayer.MonsterAttackIntent);
            ClearLayer(HexOverlayLayer.MonsterChaseRange);
            ClearLayer(HexOverlayLayer.BossArenaBoundary);
        }

        private void ShowLayer(HexOverlayLayer layer, IEnumerable<HexCoord> coords)
        {
            overlayRenderer?.ShowLayer(layer, coords, CombatOverlayTheme.ResolveDefaultStyle(layer));
        }

        private void ClearLayer(HexOverlayLayer layer)
        {
            overlayRenderer?.ClearLayer(layer);
        }
    }
}

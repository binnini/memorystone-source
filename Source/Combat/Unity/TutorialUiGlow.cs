using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Draws the tutorial "press this button" cue: a single additive orange aura that reparents onto whichever
    /// HUD button the current tutorial step wants highlighted (<see cref="Tutorial.TutorialHighlightTargetType.UiElement"/>).
    ///
    /// Mirrors the per-frame polling the card lane uses for card highlights
    /// (<c>GameplayCardLaneView.UpdateTutorialCardCue</c>): step transitions, tutorial end (IsActive=false) and
    /// runtime-created overlay buttons (the shared pile-overlay close button) are all handled for free because
    /// the target is re-resolved every frame from <see cref="MapCombatController.TutorialHighlightUiTargetId"/>.
    ///
    /// The look matches <c>CardHoverGlow.SetTutorialHighlight</c>: orange (1, 0.5, 0.06), alpha 0 → 0.85 lerped
    /// at fadeSpeed 10, and a sine pulse (base 1.15 ± 0.035 at speed 3). The glow is a child (last sibling) of
    /// the target so it inherits the button's canvas/overrideSorting without disturbing any layout group, and it
    /// never blocks input (<c>raycastTarget = false</c>).
    ///
    /// Created and configured entirely at runtime by <see cref="GameplayHudBridge"/> — no scene or prefab
    /// authoring. Sprite and material load build-safe from <c>Resources/UI/Glow</c> (never AssetDatabase).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TutorialUiGlow : MonoBehaviour
    {
        private const string GlowMaterialResourcePath = "UI/Glow/UI_ProceduralGlow";

        private static readonly Color GlowColor = new Color(1f, 0.5f, 0.06f, 1f);
        private const float TargetAlpha = 0.85f;
        private const float FadeSpeed = 10f;
        // Halo geometry, pixels. Pad >= Thickness so the outward halo never clips the padded quad.
        private const float Pad = 24f;
        private const float Thickness = 18f;
        private const float CornerRadius = 12f;
        private const float Softness = 3f;
        private const float IntensityBase = 1f;
        private const float IntensityPulse = 0.15f;
        private const float PulseSpeed = 3f;

        private MapCombatController controller;
        private BottomCardHudView bottomCardHudView;
        private GameplaySceneContract contract;
        private DeckPileListOverlayView deckPileListOverlayView;

        private RectTransform glowRect;
        private CanvasGroup glowGroup;
        private Image glowImage;
        private Material glowMaterial;
        private bool glowBuilt;
        private RectTransform currentTarget;

        // Stable targets are cached lazily (the sidebar, its deck button and the turn-phase dock never move once
        // the scene is up). The pile-overlay close button and bottom HUD docks are resolved through their views
        // each frame instead, since those can be rebuilt at runtime.
        private RectTransform cachedSidebarRoot;
        private RectTransform cachedDeckButton;
        private RectTransform cachedTurnPhaseDock;
        private RectTransform cachedStatusPanel;

        // Injected by GameplayHudBridge every Refresh so late-resolved references (controller, HUD views,
        // overlay) are always current.
        public void Configure(
            MapCombatController mapController,
            BottomCardHudView bottomHud,
            GameplaySceneContract sceneContract,
            DeckPileListOverlayView deckOverlay)
        {
            controller = mapController;
            bottomCardHudView = bottomHud;
            contract = sceneContract;
            deckPileListOverlayView = deckOverlay;
        }

        private void LateUpdate()
        {
            var targetId = controller != null ? controller.TutorialHighlightUiTargetId : null;
            var target = string.IsNullOrEmpty(targetId) ? null : ResolveTarget(targetId.Trim());

            if (target == null)
            {
                FadeOutAndPark();
                return;
            }

            EnsureGlow();
            if (target != currentTarget)
            {
                AttachTo(target);
                currentTarget = target;
            }

            UpdateGlowMotion(show: true);
        }

        private void FadeOutAndPark()
        {
            if (!glowBuilt)
            {
                return;
            }

            UpdateGlowMotion(show: false);
            if (glowGroup.alpha <= 0.01f && glowRect.gameObject.activeSelf)
            {
                glowGroup.alpha = 0f;
                glowRect.gameObject.SetActive(false);
                currentTarget = null;
            }
        }

        private void UpdateGlowMotion(bool show)
        {
            if (!glowBuilt)
            {
                return;
            }

            var targetAlpha = show ? TargetAlpha : 0f;
            glowGroup.alpha = Mathf.Lerp(glowGroup.alpha, targetAlpha, Time.deltaTime * FadeSpeed);

            if (glowMaterial != null)
            {
                // Track the button's live size (layout may settle a frame after attach) and pulse via the
                // shader's intensity instead of scaling geometry, so the halo shape never distorts.
                if (show && currentTarget != null)
                {
                    ApplyRectSize(currentTarget);
                }

                var pulse = show ? Mathf.Sin(Time.time * PulseSpeed) * IntensityPulse : 0f;
                glowMaterial.SetFloat("_Intensity", IntensityBase + pulse);
            }
        }

        private void AttachTo(RectTransform target)
        {
            glowRect.gameObject.SetActive(true);
            glowRect.SetParent(target, worldPositionStays: false);
            glowRect.SetAsLastSibling();

            // Fill the target button, then let localScale grow the aura symmetrically beyond its edges.
            glowRect.anchorMin = Vector2.zero;
            glowRect.anchorMax = Vector2.one;
            glowRect.pivot = new Vector2(0.5f, 0.5f);
            // Extend the quad Pad px beyond the button on every side so the outward halo has room to render;
            // the shader computes the inner (button) rect back from _Pad, so the halo hugs the true button edge.
            glowRect.offsetMin = new Vector2(-Pad, -Pad);
            glowRect.offsetMax = new Vector2(Pad, Pad);
            glowRect.localScale = Vector3.one;
            ApplyRectSize(target);
        }

        private void ApplyRectSize(RectTransform target)
        {
            if (glowMaterial == null || target == null)
            {
                return;
            }

            var size = target.rect.size;
            glowMaterial.SetVector("_RectSize", new Vector4(size.x + Pad * 2f, size.y + Pad * 2f, 0f, 0f));
        }

        private void EnsureGlow()
        {
            if (glowBuilt)
            {
                return;
            }

            var go = new GameObject("Tutorial UI Button Glow", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            glowRect = go.GetComponent<RectTransform>();
            glowRect.SetParent(transform, worldPositionStays: false);

            // Some target buttons (e.g. the sidebar deck button) have a LayoutGroup that would otherwise
            // size/position this child and collapse the aura to zero. Ignore layout so the glow always keeps
            // its stretch-to-button rect regardless of the parent's layout.
            var layoutElement = go.AddComponent<LayoutElement>();
            layoutElement.ignoreLayout = true;

            glowGroup = go.GetComponent<CanvasGroup>();
            glowGroup.alpha = 0f;
            glowGroup.interactable = false;
            glowGroup.blocksRaycasts = false;

            glowImage = go.GetComponent<Image>();
            // Procedural glow: no sprite. A null-sprite Image draws a full-rect quad with uv 0..1, which the
            // shader needs to evaluate its SDF (a real sprite could carry a tight/atlas mesh with non-0..1 uv).
            glowImage.sprite = null;
            var baseMaterial = Resources.Load<Material>(GlowMaterialResourcePath);
            glowMaterial = baseMaterial != null ? new Material(baseMaterial) : null;
            if (glowMaterial != null)
            {
                glowMaterial.SetColor("_Color", GlowColor);
                glowMaterial.SetFloat("_Radius", CornerRadius);
                glowMaterial.SetFloat("_Pad", Pad);
                glowMaterial.SetFloat("_Thickness", Thickness);
                glowMaterial.SetFloat("_Softness", Softness);
                glowMaterial.SetFloat("_Intensity", IntensityBase);
                glowImage.material = glowMaterial;
            }
            glowImage.color = Color.white; // colour comes from the material; keep the vertex tint neutral
            glowImage.raycastTarget = false;
            glowImage.maskable = false;
            glowImage.type = Image.Type.Simple;

            go.SetActive(false);
            glowBuilt = true;
        }

        // The same id → RectTransform table, for the host's tutorial focus resolver (spotlight hole / panel
        // placement). One table so the glow and the spotlight can never disagree about where a button is.
        public RectTransform ResolveTargetRect(string targetId)
        {
            return string.IsNullOrWhiteSpace(targetId) ? null : ResolveTarget(targetId.Trim());
        }

        // Maps a tutorial UiElement targetId to the HUD RectTransform to glow. Unknown/out-of-scope ids
        // resolve to null, which is a safe no-op.
        private RectTransform ResolveTarget(string targetId)
        {
            switch (targetId)
            {
                case "end_turn_button":
                case "end_move_phase_button":
                    return RectOf(bottomCardHudView != null ? bottomCardHudView.EndActionButton : null);
                case "drawpile_button":
                    return bottomCardHudView != null ? bottomCardHudView.DrawPileDock : null;
                case "discardpile_button":
                    return bottomCardHudView != null ? bottomCardHudView.DiscardPileDock : null;
                case "exilepile_button":
                    return bottomCardHudView != null ? bottomCardHudView.ExilePileDock : null;
                case "hand_cards":
                    return bottomCardHudView != null && bottomCardHudView.CardLaneView != null ? bottomCardHudView.CardLaneView.RectTransform : null;
                case "hp_bar":
                    return bottomCardHudView != null ? bottomCardHudView.HealthDock : null;
                case "energy_bar":
                    return bottomCardHudView != null ? bottomCardHudView.EnergyDock : null;
                case "player_status_panel":
                    return ResolveStatusPanel();
                case "deck_button":
                    return ResolveDeckButton();
                case "left_ui_bar":
                    return ResolveSidebarRoot();
                case "turn_phase_dock":
                    return ResolveTurnPhaseDock();
                case "deck_close_button":
                case "discard_close_button":
                case "drawpile_close_button":
                case "discardpile_close_button":
                case "exilepile_close_button":
                    return RectOf(deckPileListOverlayView != null ? deckPileListOverlayView.CloseButton : null);
                default:
                    return null;
            }
        }

        private RectTransform ResolveDeckButton()
        {
            if (cachedDeckButton != null)
            {
                return cachedDeckButton;
            }

            if (contract == null)
            {
                return null;
            }

            var button = contract.GetComponentsInChildren<SidebarPanelButton>(includeInactive: true)
                .FirstOrDefault(candidate => candidate.PanelKey == "deck");
            cachedDeckButton = button != null ? button.transform as RectTransform : null;
            return cachedDeckButton;
        }

        private RectTransform ResolveSidebarRoot()
        {
            if (cachedSidebarRoot != null)
            {
                return cachedSidebarRoot;
            }

            if (contract == null)
            {
                return null;
            }

            var marker = contract.GetComponentInChildren<SidebarRootMarker>(includeInactive: true);
            cachedSidebarRoot = marker != null ? marker.transform as RectTransform : null;
            return cachedSidebarRoot;
        }

        private RectTransform ResolveTurnPhaseDock()
        {
            if (cachedTurnPhaseDock != null)
            {
                return cachedTurnPhaseDock;
            }

            if (contract == null)
            {
                return null;
            }

            var dock = contract.GetComponentInChildren<TurnPhaseDockView>(includeInactive: true);
            cachedTurnPhaseDock = dock != null ? dock.transform as RectTransform : null;
            return cachedTurnPhaseDock;
        }

        private RectTransform ResolveStatusPanel()
        {
            if (cachedStatusPanel != null)
            {
                return cachedStatusPanel;
            }

            if (contract == null)
            {
                return null;
            }

            var dock = contract.GetComponentInChildren<CardLaneStatusEffectDockView>(includeInactive: true);
            cachedStatusPanel = dock != null ? dock.StatusPanel : null;
            return cachedStatusPanel;
        }

        private static RectTransform RectOf(Selectable selectable)
        {
            return selectable != null ? selectable.transform as RectTransform : null;
        }
    }
}

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Drives the additive aura image shown behind a UI card during hover/selection states.
    /// The glow image should use an additive UI material so black pixels contribute no visible color.
    /// </summary>
    public sealed class CardHoverGlow : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [Header("References")]
        [SerializeField] private CanvasGroup glowGroup;
        [SerializeField] private RectTransform glowRect;
        [SerializeField] private RectTransform cardRoot;


        [Header("Input")]
        [SerializeField] private bool acceptPointerEvents = true;

        [Header("Alpha")]
        [SerializeField] private float hoverGlowAlpha = 0.55f;
        [SerializeField] private float selectedGlowAlpha = 0.85f;
        [SerializeField] private float playableGlowAlpha = 0.25f;
        [SerializeField] private float fadeSpeed = 10f;

        [Header("Scale")]
        [SerializeField] private float hoverCardScale = 1.04f;
        [SerializeField] private float selectedCardScale = 1.05f;
        [SerializeField] private float glowBaseScale = 1.18f;
        [SerializeField] private float glowPulseAmount = 0.035f;
        [SerializeField] private float pulseSpeed = 3f;

        [Header("Lift")]
        [SerializeField] private float hoverYOffset = 24f;
        [SerializeField] private float selectedYOffset = 28f;

        private bool isHovered;
        private bool isSelected;
        private bool isDisabled;
        private bool isTutorialHighlight;
        private bool isOminousAura;
        private bool isPlayableHint;
        private HandCardSelectionVisualState selectionVisualState;
        private float targetGlowAlpha;
        private Vector3 defaultCardScale = Vector3.one;
        private Vector2 defaultCardPosition;
        private bool hasCardRootDefault;
        private Graphic glowGraphic;
        private Color defaultGlowColor = Color.white;
        private bool hasDefaultGlowColor;

        // Procedural glow (UI/ProceduralGlow): a uniform-thickness rounded-rect halo driven per-state, replacing
        // the stretched bitmap so the glow never distorts and thickness is a clean parameter. The tutorial
        // highlight uses a noticeably thicker halo. Pulse animates the shader intensity, not the transform, so
        // the halo shape and position stay put. If the material is missing, the original bitmap path is kept.
        private static Material sharedGlowMaterialSource;
        private Material glowMaterial;
        private bool proceduralReady;
        private const float GlowPad = 30f;            // must be >= GlowThicknessTutorial (halo room in the quad)
        private const float GlowCornerRadius = 22f;
        private const float GlowSoftness = 3f;
        private const float GlowThicknessNormal = 14f;
        private const float GlowThicknessTutorial = 26f;
        private const float GlowIntensityBase = 1f;
        private const float GlowIntensityPulse = 0.15f;

        // 불길한 아우라 — 상태 카드 전용. 자주-보라(핏빛 보라)로, 노랑/주황 선택 상태와 주황 튜토리얼 큐,
        // 그리고 남색 기본 글로우 어디와도 겹치지 않는 색이다. 상태이상 팔레트 3벌은 현행 유지 결정이므로
        // (project_status_effect_palette_decision) 여기서 팔레트를 통합하지 않는다.
        private static readonly Color OminousAuraColor = new Color(0.66f, 0.11f, 0.45f, 1f);
        private const float OminousAuraAlpha = 0.8f;

        private void Awake()
        {
            CacheDefaults();
            EnsureProceduralGlow();
            HideGlowImmediately();
        }

        private void OnEnable()
        {
            CacheDefaults();
            EnsureProceduralGlow();
            ApplyImmediateState();
        }

        private void Update()
        {
            UpdateTargetAlpha();
            UpdateGlowAlpha();
            UpdateGlowPulse();
            UpdateCardMotion();
        }

        public void SetHovered(bool hovered)
        {
            isHovered = hovered && !isDisabled;
        }

        public void SetSelected(bool selected)
        {
            isSelected = selected && !isDisabled;
        }

        public void SetDisabled(bool disabled)
        {
            isDisabled = disabled;

            if (isDisabled)
            {
                isHovered = false;
                isSelected = false;
                isPlayableHint = false;
            }
        }

        public void SetPlayableHint(bool playableHint)
        {
            isPlayableHint = playableHint && !isDisabled;
        }

        public void SetHandCardSelectionVisualState(HandCardSelectionVisualState visualState)
        {
            selectionVisualState = visualState;
            ApplyGlowColor();
        }

        public void ApplyImmediateState()
        {
            UpdateTargetAlpha();

            if (glowGroup != null)
            {
                glowGroup.alpha = targetGlowAlpha;
            }

            if (glowRect != null)
            {
                glowRect.localScale = glowMaterial != null ? Vector3.one : Vector3.one * glowBaseScale;
            }

            ApplyGlowColor();
            ApplyCardMotion(immediate: true);
        }

        public void HideGlowImmediately()
        {
            targetGlowAlpha = 0f;
            if (glowGroup != null)
            {
                glowGroup.alpha = 0f;
            }

            if (glowRect != null)
            {
                glowRect.localScale = glowMaterial != null ? Vector3.one : Vector3.one * glowBaseScale;
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (acceptPointerEvents)
            {
                SetHovered(true);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (acceptPointerEvents)
            {
                SetHovered(false);
            }
        }

        // Forces a bright orange glow that ignores the disabled/playable state, used by the tutorial to
        // point at a specific card the player must hover or select.
        public void SetTutorialHighlight(bool on)
        {
            if (isTutorialHighlight == on)
            {
                return;
            }

            isTutorialHighlight = on;
            ApplyGlowColor();
        }

        // 상태 카드(미세먼지·깨진 유리·정전)에 씌우는 "불길한 아우라". 튜토리얼 하이라이트와 **같은 경로**를
        // 일부러 재사용한다: 색은 절차 글로우 머터리얼의 _Color로 가고(빌드에서 검증된 출하 경로), 알파는
        // CanvasGroup이 담당한다. 카드 위에 런타임으로 Image를 만들거나 Image.color를 칠하는 방식은
        // CardUnavailableFeedback 주석의 이력대로 "빌드에서만" 실패한 전력이 있어 쓰지 않는다.
        public void SetOminousAura(bool on)
        {
            if (isOminousAura == on)
            {
                return;
            }

            isOminousAura = on;
            ApplyGlowColor();
        }

        private void CacheDefaults()
        {
            if (glowGraphic == null && glowRect != null)
            {
                glowGraphic = glowRect.GetComponent<Graphic>();
                if (glowGraphic != null && !hasDefaultGlowColor)
                {
                    defaultGlowColor = glowGraphic.color;
                    hasDefaultGlowColor = true;
                }
            }

            if (cardRoot == null || hasCardRootDefault)
            {
                return;
            }

            defaultCardScale = cardRoot.localScale;
            defaultCardPosition = cardRoot.anchoredPosition;
            hasCardRootDefault = true;
        }

        private void ApplyGlowColor()
        {
            if (glowGraphic == null && glowRect != null)
            {
                glowGraphic = glowRect.GetComponent<Graphic>();
            }

            if (glowGraphic == null)
            {
                return;
            }

            if (!hasDefaultGlowColor)
            {
                defaultGlowColor = glowGraphic.color;
                hasDefaultGlowColor = true;
            }

            // A live hand-card selection (e.g. the A10 exile-cost picker) owns the colour so its yellow
            // "candidate" / orange "picked" states read correctly. Without this precedence the tutorial's
            // orange card cue would paint an unpicked candidate orange, making it look already selected.
            switch (selectionVisualState)
            {
                case HandCardSelectionVisualState.Picked:
                    SetGlowColor(new Color(1f, 0.48f, 0.08f, defaultGlowColor.a));
                    return;
                case HandCardSelectionVisualState.Candidate:
                    SetGlowColor(new Color(1f, 0.82f, 0.22f, defaultGlowColor.a));
                    return;
            }

            if (isTutorialHighlight)
            {
                SetGlowColor(new Color(1f, 0.5f, 0.06f, defaultGlowColor.a));
                return;
            }

            // 튜토리얼 뒤에 둔다: 둘 다 걸렸다면 "지금 이 카드를 눌러라"는 지시가 우선이다.
            if (isOminousAura)
            {
                SetGlowColor(OminousAuraColor);
                return;
            }

            SetGlowColor(defaultGlowColor);
        }

        // Routes the glow colour to the procedural material (fade stays with the CanvasGroup, so force alpha 1
        // on the material colour) when active, else falls back to tinting the bitmap graphic directly.
        private void SetGlowColor(Color color)
        {
            if (glowMaterial != null)
            {
                glowMaterial.SetColor("_Color", new Color(color.r, color.g, color.b, 1f));
            }
            else if (glowGraphic != null)
            {
                glowGraphic.color = color;
            }
        }

        // Builds the procedural glow once: swaps the glow image to the SDF material (no sprite → full-rect quad),
        // frames the quad to the card plus GlowPad on every side, and neutralises the old scale-based sizing.
        private void EnsureProceduralGlow()
        {
            if (proceduralReady || glowRect == null)
            {
                return;
            }

            if (glowGraphic == null)
            {
                glowGraphic = glowRect.GetComponent<Graphic>();
            }
            if (glowGraphic == null)
            {
                return;
            }

            if (sharedGlowMaterialSource == null)
            {
                sharedGlowMaterialSource = Resources.Load<Material>("UI/Glow/UI_ProceduralGlow");
            }
            if (sharedGlowMaterialSource == null)
            {
                return; // material missing → keep the authored bitmap glow as a safe fallback
            }

            glowMaterial = new Material(sharedGlowMaterialSource);
            glowMaterial.SetFloat("_Radius", GlowCornerRadius);
            glowMaterial.SetFloat("_Pad", GlowPad);
            glowMaterial.SetFloat("_Softness", GlowSoftness);
            glowMaterial.SetFloat("_Thickness", GlowThicknessNormal);
            glowMaterial.SetFloat("_Intensity", GlowIntensityBase);

            var image = glowGraphic as Image;
            if (image != null)
            {
                image.sprite = null;           // procedural full-rect quad (uv 0..1)
                image.type = Image.Type.Simple;
                image.preserveAspect = false;
            }
            glowGraphic.material = glowMaterial;
            glowGraphic.color = Color.white;    // colour now comes from the material; keep the vertex tint neutral
            glowGraphic.raycastTarget = false;

            // Frame the card: stretch the glow quad to its parent (the card) plus GlowPad on every side so the
            // outline hugs the card edge with room for the outward halo. localScale stays 1 (pulse uses shader).
            glowRect.anchorMin = Vector2.zero;
            glowRect.anchorMax = Vector2.one;
            glowRect.pivot = new Vector2(0.5f, 0.5f);
            glowRect.offsetMin = new Vector2(-GlowPad, -GlowPad);
            glowRect.offsetMax = new Vector2(GlowPad, GlowPad);
            glowRect.localScale = Vector3.one;

            proceduralReady = true;
            ApplyGlowColor();
            UpdateRectSize();
        }

        // Feeds the shader the current quad pixel size (= card + 2*GlowPad); _Pad recovers the card rect from it.
        private void UpdateRectSize()
        {
            if (glowMaterial == null || glowRect == null)
            {
                return;
            }

            var size = glowRect.rect.size;
            glowMaterial.SetVector("_RectSize", new Vector4(size.x, size.y, 0f, 0f));
        }

        private void UpdateTargetAlpha()
        {
            if (selectionVisualState == HandCardSelectionVisualState.Picked)
            {
                targetGlowAlpha = selectedGlowAlpha;
            }
            else if (selectionVisualState == HandCardSelectionVisualState.Candidate)
            {
                targetGlowAlpha = Mathf.Max(playableGlowAlpha, 0.42f);
            }
            else if (isTutorialHighlight)
            {
                // Always visible, even on a not-yet-playable card, so the tutorial cue is never hidden.
                targetGlowAlpha = 0.85f;
            }
            else if (isOminousAura)
            {
                // isDisabled 앞에 둔다. 상태 카드는 **정의상 항상 사용 불가**라 disabled로 들어오는데,
                // 그 분기가 먼저 잡히면 아우라가 한 번도 보이지 않는다(튜토리얼 큐와 같은 이유).
                targetGlowAlpha = OminousAuraAlpha;
            }
            else if (isDisabled)
            {
                targetGlowAlpha = 0f;
            }
            else if (isSelected)
            {
                targetGlowAlpha = selectedGlowAlpha;
            }
            else if (isHovered)
            {
                targetGlowAlpha = hoverGlowAlpha;
            }
            else if (isPlayableHint)
            {
                targetGlowAlpha = playableGlowAlpha;
            }
            else
            {
                targetGlowAlpha = 0f;
            }
        }

        private void UpdateGlowAlpha()
        {
            if (glowGroup == null)
            {
                return;
            }

            glowGroup.alpha = Mathf.Lerp(glowGroup.alpha, targetGlowAlpha, Time.deltaTime * fadeSpeed);
        }

        private void UpdateGlowPulse()
        {
            if (glowRect == null)
            {
                return;
            }

            // A live selection (candidate/picked) takes the normal, non-tutorial look so its colour and
            // thickness match the selection semantics rather than the thicker tutorial cue.
            var selectionActive = selectionVisualState == HandCardSelectionVisualState.Candidate
                || selectionVisualState == HandCardSelectionVisualState.Picked;
            var shouldPulse = (isTutorialHighlight && !selectionActive)
                || (isOminousAura && !selectionActive)
                || (!isDisabled && (isHovered || isSelected || selectionVisualState == HandCardSelectionVisualState.Picked));

            if (glowMaterial == null)
            {
                // Fallback: original bitmap scale-pulse.
                var pulseScale = shouldPulse ? Mathf.Sin(Time.time * pulseSpeed) * glowPulseAmount : 0f;
                glowRect.localScale = Vector3.Lerp(
                    glowRect.localScale,
                    Vector3.one * (glowBaseScale + pulseScale),
                    Time.deltaTime * fadeSpeed);
                return;
            }

            // Procedural: thickness by state (tutorial highlight is thicker), pulse via shader intensity so the
            // halo never distorts or drifts. Track the card's live size in case its layout changes.
            UpdateRectSize();
            glowMaterial.SetFloat("_Thickness", isTutorialHighlight && !selectionActive ? GlowThicknessTutorial : GlowThicknessNormal);
            var pulse = shouldPulse ? Mathf.Sin(Time.time * pulseSpeed) * GlowIntensityPulse : 0f;
            glowMaterial.SetFloat("_Intensity", GlowIntensityBase + pulse);
        }

        private void UpdateCardMotion()
        {
            ApplyCardMotion(immediate: false);
        }

        private void ApplyCardMotion(bool immediate)
        {
            if (cardRoot == null || !hasCardRootDefault)
            {
                return;
            }

            var scaleMultiplier = 1f;
            var yOffset = 0f;

            if (!isDisabled)
            {
                if (isSelected)
                {
                    scaleMultiplier = selectedCardScale;
                    yOffset = selectedYOffset;
                }
                else if (isHovered)
                {
                    scaleMultiplier = hoverCardScale;
                    yOffset = hoverYOffset;
                }
            }

            var targetScale = defaultCardScale * scaleMultiplier;
            var targetPosition = defaultCardPosition + Vector2.up * yOffset;

            if (immediate)
            {
                cardRoot.localScale = targetScale;
                cardRoot.anchoredPosition = targetPosition;
                return;
            }

            cardRoot.localScale = Vector3.Lerp(cardRoot.localScale, targetScale, Time.deltaTime * fadeSpeed);
            cardRoot.anchoredPosition = Vector2.Lerp(cardRoot.anchoredPosition, targetPosition, Time.deltaTime * fadeSpeed);
        }
    }
}



using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Adds a soft procedural halo behind a skinned button that fades in on pointer hover (P6 T1). Reuses the
    /// halo-only <c>UI/ProceduralGlow</c> shader — the same one <see cref="CardHoverGlow"/> and the tutorial
    /// glow drive — so buttons gain the lobby's "lit on hover" feedback without any bitmap. Attached by
    /// <see cref="UiButtonSkin"/>; self-builds its glow child, so callers only AddComponent it.
    ///
    /// Non-breaking, same as the panel skin: if the glow material is missing the component simply does nothing.
    /// Fades on <see cref="Time.unscaledDeltaTime"/> so the glow still animates in the timescale-0 pause menu.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class UiButtonHoverGlow : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private const string GlowMaterialResourcePath = "UI/Glow/UI_ProceduralGlow";
        private const string GlowChildName = "ButtonHoverGlow";
        private const float Pad = 16f;            // halo room around the button rect, in px
        private const float CornerRadius = 12f;   // matches the button plate corner
        private const float Softness = 3f;
        private const float Thickness = 12f;

        // Warm gold halo, a touch brighter than the plate border (#C9A227) so the hover reads as "lit".
        private static readonly Color GlowColor = new Color(0.90f, 0.72f, 0.28f, 1f);

        [SerializeField] private float hoverAlpha = 0.6f;
        [SerializeField] private float fadeSpeed = 12f;

        private Selectable selectable;
        private CanvasGroup glowGroup;
        private RectTransform glowRect;
        private Material glowMaterial;
        private bool isHovered;
        private bool built;
        private static bool warnedMissingMaterial;

        private void Awake()
        {
            Build();
        }

        private void OnEnable()
        {
            Build();
            isHovered = false;
            if (glowGroup != null)
            {
                glowGroup.alpha = 0f;
            }
        }

        private void OnDisable()
        {
            isHovered = false;
            if (glowGroup != null)
            {
                glowGroup.alpha = 0f;
            }
        }

        private void OnDestroy()
        {
            if (glowMaterial != null)
            {
                Destroy(glowMaterial);
                glowMaterial = null;
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            isHovered = true;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            isHovered = false;
        }

        private void Update()
        {
            if (glowGroup == null)
            {
                return;
            }

            var interactable = selectable == null || selectable.IsInteractable();
            var target = (isHovered && interactable) ? hoverAlpha : 0f;
            glowGroup.alpha = Mathf.Lerp(glowGroup.alpha, target, Time.unscaledDeltaTime * fadeSpeed);
            UpdateRectSize();
        }

        // Builds (or, on a cloned button, reuses) the single glow child and gives this component its own material
        // instance so parallel buttons never share _RectSize. Safe to call repeatedly.
        private void Build()
        {
            if (built)
            {
                return;
            }

            selectable = GetComponent<Selectable>();

            var source = Resources.Load<Material>(GlowMaterialResourcePath);
            if (source == null)
            {
                if (!warnedMissingMaterial)
                {
                    warnedMissingMaterial = true;
                    Debug.LogWarning(
                        $"UiButtonHoverGlow material missing at Resources/{GlowMaterialResourcePath}; " +
                        "buttons keep their skin without a hover glow.", this);
                }
                built = true; // don't retry every frame
                return;
            }

            // Reuse a glow child carried over by a button clone, else create one; guarantees exactly one child.
            var existing = transform.Find(GlowChildName);
            GameObject go;
            if (existing != null)
            {
                go = existing.gameObject;
                glowRect = (RectTransform)existing;
            }
            else
            {
                go = new GameObject(GlowChildName, typeof(RectTransform));
                glowRect = (RectTransform)go.transform;
                glowRect.SetParent(transform, false);
            }

            // ignoreLayout so a LayoutGroup on the button (button rows) can't reposition/resize the halo.
            var layoutElement = go.GetComponent<LayoutElement>();
            if (layoutElement == null)
            {
                layoutElement = go.AddComponent<LayoutElement>();
            }
            layoutElement.ignoreLayout = true;

            // Frame the button plus Pad on every side; the halo hugs the button edge and fades outward.
            glowRect.anchorMin = Vector2.zero;
            glowRect.anchorMax = Vector2.one;
            glowRect.pivot = new Vector2(0.5f, 0.5f);
            glowRect.offsetMin = new Vector2(-Pad, -Pad);
            glowRect.offsetMax = new Vector2(Pad, Pad);
            glowRect.localScale = Vector3.one;
            glowRect.SetAsFirstSibling(); // render behind the label (halo is outside the plate anyway)

            var image = go.GetComponent<Image>();
            if (image == null)
            {
                image = go.AddComponent<Image>();
            }
            image.sprite = null;            // procedural full-rect quad (uv 0..1)
            image.type = Image.Type.Simple;
            image.raycastTarget = false;

            glowMaterial = new Material(source);
            glowMaterial.SetColor("_Color", GlowColor);
            glowMaterial.SetFloat("_Radius", CornerRadius);
            glowMaterial.SetFloat("_Pad", Pad);
            glowMaterial.SetFloat("_Softness", Softness);
            glowMaterial.SetFloat("_Thickness", Thickness);
            glowMaterial.SetFloat("_Intensity", 1f);
            image.material = glowMaterial;
            image.color = Color.white;       // colour comes from the material; keep the vertex tint neutral

            glowGroup = go.GetComponent<CanvasGroup>();
            if (glowGroup == null)
            {
                glowGroup = go.AddComponent<CanvasGroup>();
            }
            glowGroup.alpha = 0f;
            glowGroup.blocksRaycasts = false;
            glowGroup.interactable = false;

            built = true;
            UpdateRectSize();
        }

        private void UpdateRectSize()
        {
            if (glowMaterial == null || glowRect == null)
            {
                return;
            }

            var size = glowRect.rect.size;
            glowMaterial.SetVector("_RectSize", new Vector4(size.x, size.y, 0f, 0f));
        }
    }
}

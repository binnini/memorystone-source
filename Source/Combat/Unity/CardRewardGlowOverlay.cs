using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Animates the two authored additive reward glow layers behind a reward card.
    /// The images intentionally keep their black pixels and rely on an additive UI material.
    /// </summary>
    public sealed class CardRewardGlowOverlay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        private const string GlowRootName = "RewardGlowRoot";
        private const string RaysName = "card_glow_reward2_Rays";
        private const string SoftName = "card_glow_reward_Soft";

        [SerializeField] private RectTransform glowRoot;
        [SerializeField] private Image raysImage;
        [SerializeField] private Image softImage;
        [SerializeField] private Sprite softGlowSprite;
        [SerializeField] private Sprite raysGlowSprite;
        [SerializeField] private Material additiveMaterial;
        [SerializeField] private Color glowTint = Color.white;

        [Header("Alpha")]
        [SerializeField] private float normalSoftAlpha = 0.42f;
        [SerializeField] private float normalRaysAlpha = 0.22f;
        [SerializeField] private float hoverSoftAlpha = 0.64f;
        [SerializeField] private float hoverRaysAlpha = 0.36f;
        [SerializeField] private float selectedSoftAlpha = 0.82f;
        [SerializeField] private float selectedRaysAlpha = 0.48f;
        [SerializeField] private float fadeSpeed = 8f;

        [Header("Motion")]
        [SerializeField] private bool animateAlpha = true;
        [SerializeField] private bool animateTransform = true;
        [SerializeField] private float softPulseAmount = 0.03f;
        [SerializeField] private float raysPulseAmount = 0.015f;
        [SerializeField] private float pulseSpeed = 1.7f;
        [SerializeField] private float raysRotationDegreesPerSecond = 3f;
        [SerializeField] private float hoverCardScaleMultiplier = 1.06f;
        [SerializeField] private float hoverCardScaleSpeed = 14f;

        private RectTransform softRect;
        private RectTransform raysRect;
        private RectTransform cardRect;
        private bool isHovered;
        private bool isSelected;
        private float targetSoftAlpha;
        private float targetRaysAlpha;
        private bool capturedPrefabAlpha;
        private bool capturedPrefabTransform;
        private Vector3 softBaseScale = Vector3.one;
        private Vector3 raysBaseScale = Vector3.one;
        private Vector3 cardBaseScale = Vector3.one;
        private Quaternion softBaseRotation = Quaternion.identity;
        private Quaternion raysBaseRotation = Quaternion.identity;
        private float animationTime;
        private Action onHoverEntered;

        public void Configure(Sprite softGlow, Sprite raysGlow, Material glowMaterial, Action hoverEntered = null, Color? tint = null)
        {
            // The caller-provided sprite wins over any authored field so the rarity-tintable white glow
            // always replaces a prefab's serialized gold sprite (gold corrupts the blue/purple tint).
            softGlowSprite = softGlow != null ? softGlow : softGlowSprite;
            raysGlowSprite = raysGlow != null ? raysGlow : raysGlowSprite;
            additiveMaterial = additiveMaterial != null ? additiveMaterial : glowMaterial;
            onHoverEntered = hoverEntered;
            if (tint.HasValue)
            {
                glowTint = tint.Value;
            }

            CacheAuthoredHierarchy();
            ApplySpritesAndMaterial();
        }

        public void SetSelected(bool selected)
        {
            isSelected = selected;
        }

        private void Awake()
        {
            CacheAuthoredHierarchy();
            ApplySpritesAndMaterial();
        }

        private void OnEnable()
        {
            isHovered = false;
            isSelected = false;
            CacheAuthoredHierarchy();
            ApplySpritesAndMaterial();
            if (cardRect != null)
            {
                cardRect.localScale = cardBaseScale;
            }
        }

        private void Update()
        {
            CacheAuthoredHierarchy();
            UpdateTargets();
            UpdateAlpha();
            UpdateTransformAnimation();
            UpdateCardHoverScale();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!isHovered)
            {
                onHoverEntered?.Invoke();
            }

            isHovered = true;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            isHovered = false;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            isSelected = true;
        }

        private void CacheAuthoredHierarchy()
        {
            if (cardRect == null)
            {
                cardRect = transform as RectTransform;
            }

            if (glowRoot == null)
            {
                glowRoot = transform.Find(GlowRootName) as RectTransform;
            }

            if (glowRoot == null)
            {
                return;
            }

            if (raysImage == null)
            {
                raysImage = glowRoot.Find(RaysName)?.GetComponent<Image>();
            }

            if (softImage == null)
            {
                softImage = glowRoot.Find(SoftName)?.GetComponent<Image>();
            }

            raysRect = raysImage != null ? raysImage.rectTransform : null;
            softRect = softImage != null ? softImage.rectTransform : null;

            ConfigureImage(raysImage);
            ConfigureImage(softImage);
            CapturePrefabAlpha();
            CapturePrefabTransform();
        }

        private void ApplySpritesAndMaterial()
        {
            ApplyImage(raysImage, raysGlowSprite);
            ApplyImage(softImage, softGlowSprite);
            ApplyTint();
        }

        // Tints the additive glow layers by rarity while leaving the alpha to the
        // hover/selected animation (LerpImageAlpha only touches the alpha channel).
        private void ApplyTint()
        {
            ApplyImageTint(softImage);
            ApplyImageTint(raysImage);
        }

        private void ApplyImageTint(Image image)
        {
            if (image == null)
            {
                return;
            }

            var color = image.color;
            image.color = new Color(glowTint.r, glowTint.g, glowTint.b, color.a);
        }

        private void UpdateTargets()
        {
            if (isSelected)
            {
                targetSoftAlpha = selectedSoftAlpha;
                targetRaysAlpha = selectedRaysAlpha;
            }
            else if (isHovered)
            {
                targetSoftAlpha = hoverSoftAlpha;
                targetRaysAlpha = hoverRaysAlpha;
            }
            else
            {
                targetSoftAlpha = normalSoftAlpha;
                targetRaysAlpha = normalRaysAlpha;
            }
        }

        private void UpdateAlpha()
        {
            if (!animateAlpha)
            {
                return;
            }

            LerpImageAlpha(softImage, targetSoftAlpha);
            LerpImageAlpha(raysImage, targetRaysAlpha);
        }

        private void UpdateTransformAnimation()
        {
            if (!animateTransform)
            {
                return;
            }

            animationTime += Time.deltaTime;
            var pulse = Mathf.Sin(animationTime * pulseSpeed);

            if (softRect != null)
            {
                softRect.localScale = softBaseScale * (1f + pulse * softPulseAmount);
                softRect.localRotation = softBaseRotation;
            }

            if (raysRect != null)
            {
                raysRect.localScale = raysBaseScale * (1f + pulse * raysPulseAmount);
                raysRect.localRotation = raysBaseRotation * Quaternion.Euler(0f, 0f, animationTime * raysRotationDegreesPerSecond);
            }
        }

        private void UpdateCardHoverScale()
        {
            if (cardRect == null)
            {
                return;
            }

            var multiplier = isHovered || isSelected ? Mathf.Max(1f, hoverCardScaleMultiplier) : 1f;
            cardRect.localScale = Vector3.Lerp(
                cardRect.localScale,
                cardBaseScale * multiplier,
                Time.deltaTime * Mathf.Max(0f, hoverCardScaleSpeed));
        }

        private void LerpImageAlpha(Image image, float targetAlpha)
        {
            if (image == null)
            {
                return;
            }

            var color = image.color;
            color.a = Mathf.Lerp(color.a, targetAlpha, Time.deltaTime * fadeSpeed);
            image.color = color;
        }

        private void CapturePrefabAlpha()
        {
            if (capturedPrefabAlpha)
            {
                return;
            }

            if (softImage != null)
            {
                normalSoftAlpha = softImage.color.a;
            }

            if (raysImage != null)
            {
                normalRaysAlpha = raysImage.color.a;
            }

            capturedPrefabAlpha = true;
        }

        private void CapturePrefabTransform()
        {
            if (capturedPrefabTransform)
            {
                return;
            }

            if (softRect != null)
            {
                softBaseScale = softRect.localScale;
                softBaseRotation = softRect.localRotation;
            }

            if (raysRect != null)
            {
                raysBaseScale = raysRect.localScale;
                raysBaseRotation = raysRect.localRotation;
            }

            if (cardRect != null)
            {
                cardBaseScale = cardRect.localScale;
            }

            capturedPrefabTransform = true;
        }

        private void ApplyImage(Image image, Sprite sprite)
        {
            if (image == null)
            {
                return;
            }

            // Force-assign so the rarity-tintable white glow overrides the prefab's authored gold sprite
            // (a gold texture would corrupt the vertex-color tint, turning blue into green, purple into red).
            if (sprite != null)
            {
                image.sprite = sprite;
            }

            if (image.material == null)
            {
                image.material = additiveMaterial;
            }
        }

        private static void ConfigureImage(Image image)
        {
            if (image == null)
            {
                return;
            }

            image.raycastTarget = false;
            image.preserveAspect = true;
            image.type = Image.Type.Simple;
        }

    }
}

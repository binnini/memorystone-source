using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity.Tutorial
{
    /// <summary>
    /// Full-screen tutorial dim with one rounded-rect hole over the current step's focus target, plus a glow
    /// ring hugging the hole. Everything outside the hole is darkened and — because this Image is a raycast
    /// target — swallows pointer input, so the player can only act on the thing the step points at. Inside the
    /// hole <see cref="ICanvasRaycastFilter"/> reports "not hit" so clicks pass through to the button / card /
    /// world tile underneath.
    ///
    /// The hole is animated (exponential lerp) between steps so the eye is led from one target to the next.
    /// A step with no focus target dims the whole screen (no hole, no ring). Built entirely at runtime by
    /// <see cref="TutorialHudView"/>; the material loads build-safe from <c>Resources/UI/Glow</c> and is
    /// instanced per view so property writes never touch the shared asset.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public sealed class TutorialSpotlightView : MonoBehaviour, ICanvasRaycastFilter
    {
        private const string MaterialResourcePath = "UI/Glow/UI_TutorialSpotlight";
        private const float HoleLerpSpeed = 12f;
        private const float FadeSpeed = 6f;

        private static readonly int RectSizeId = Shader.PropertyToID("_RectSize");
        private static readonly int HoleCenterId = Shader.PropertyToID("_HoleCenter");
        private static readonly int HoleSizeId = Shader.PropertyToID("_HoleSize");
        private static readonly int HoleShapeId = Shader.PropertyToID("_HoleShape");

        private Image image;
        private Material material;
        private RectTransform rect;
        private CanvasGroup group;

        private bool hasTargetHole;
        private Rect targetHole;
        private bool hasCurrentHole;
        private Rect currentHole;
        private float targetAlpha;
        private float dimStrength = 1f;
        private string currentTargetKey;
        private bool holeIsEllipse;

        public static TutorialSpotlightView Build(Transform parent)
        {
            var go = new GameObject("TutorialSpotlight", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            go.transform.SetParent(parent, worldPositionStays: false);
            var view = go.AddComponent<TutorialSpotlightView>();
            view.Initialize();
            return view;
        }

        private void Initialize()
        {
            rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            group = GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = true;

            image = GetComponent<Image>();
            image.sprite = null;
            image.color = Color.white;
            image.raycastTarget = true;
            image.maskable = false;
            image.type = Image.Type.Simple;

            var baseMaterial = Resources.Load<Material>(MaterialResourcePath);
            material = baseMaterial != null ? new Material(baseMaterial) : null;
            if (material != null)
            {
                image.material = material;
            }
            else
            {
                // Material missing: fall back to a plain translucent quad with no hole so the tutorial still
                // reads as "dimmed", rather than silently rendering nothing.
                image.color = new Color(0.02f, 0.027f, 0.06f, 0.62f);
            }

            gameObject.SetActive(false);
        }

        /// <summary>Dim the screen with a hole over <paramref name="holeCanvasRect"/> (this view's local space).</summary>
        public void Show(Rect? holeCanvasRect)
        {
            Show(holeCanvasRect, TutorialFocusShape.Rect, 1f, null);
        }

        /// <summary>
        /// Dim with an optional hole. <paramref name="strength"/> scales the dim alpha (a lighter dim keeps a
        /// tile's own overlay readable). While <paramref name="targetKey"/> stays the same the hole snaps to the
        /// target every frame (a moving card must not be chased); it eases only when the target changes.
        /// </summary>
        public void Show(Rect? holeCanvasRect, TutorialFocusShape shape, float strength, string targetKey, bool blocksInput = true)
        {
            targetAlpha = Mathf.Clamp01(strength);
            dimStrength = targetAlpha;
            holeIsEllipse = shape == TutorialFocusShape.Ellipse;
            if (group != null)
            {
                group.blocksRaycasts = blocksInput;
            }
            if (image != null)
            {
                image.raycastTarget = blocksInput;
            }
            hasTargetHole = holeCanvasRect.HasValue && shape != TutorialFocusShape.NoHole;
            var sameTarget = string.Equals(currentTargetKey, targetKey, System.StringComparison.Ordinal);
            currentTargetKey = targetKey;
            if (hasTargetHole)
            {
                targetHole = holeCanvasRect.Value;
                if (!hasCurrentHole || (sameTarget && targetKey != null))
                {
                    currentHole = targetHole;
                    hasCurrentHole = true;
                }
            }

            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
                if (hasTargetHole)
                {
                    currentHole = targetHole;
                    hasCurrentHole = true;
                }
                Apply();
            }
        }

        public void Hide()
        {
            targetAlpha = 0f;
            // Stop swallowing input the moment the dim starts fading, not when the fade finishes.
            if (group != null)
            {
                group.blocksRaycasts = false;
            }
        }

        public void HideImmediate()
        {
            targetAlpha = 0f;
            if (group != null)
            {
                group.alpha = 0f;
                group.blocksRaycasts = false;
            }

            hasCurrentHole = false;
            gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (group == null)
            {
                return;
            }

            var dt = Time.unscaledDeltaTime;
            group.alpha = Mathf.MoveTowards(group.alpha, targetAlpha, dt * FadeSpeed);
            if (targetAlpha <= 0f && group.alpha <= 0.001f)
            {
                hasCurrentHole = false;
                gameObject.SetActive(false);
                return;
            }

            if (targetAlpha <= 0f)
            {
                // Fading out: keep the last hole (and its passthrough) instead of collapsing to a full dim.
                Apply();
                return;
            }

            if (hasTargetHole)
            {
                var t = 1f - Mathf.Exp(-HoleLerpSpeed * dt);
                currentHole = new Rect(
                    Vector2.Lerp(currentHole.position, targetHole.position, t),
                    Vector2.Lerp(currentHole.size, targetHole.size, t));
                hasCurrentHole = true;
            }
            else
            {
                hasCurrentHole = false;
            }

            Apply();
        }

        private void Apply()
        {
            if (material == null || rect == null)
            {
                return;
            }

            var size = rect.rect.size;
            material.SetVector(RectSizeId, new Vector4(size.x, size.y, 0f, 0f));
            if (hasCurrentHole)
            {
                material.SetVector(HoleCenterId, new Vector4(currentHole.center.x, currentHole.center.y, 0f, 0f));
                material.SetVector(HoleSizeId, new Vector4(currentHole.width, currentHole.height, 0f, 0f));
                material.SetFloat(HoleShapeId, holeIsEllipse ? 1f : 0f);
            }
            else
            {
                material.SetVector(HoleSizeId, Vector4.zero);
            }
        }

        // Pointer inside the (animated) hole is not ours — let the target underneath take it.
        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            if (!hasCurrentHole || rect == null)
            {
                return true;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screenPoint, eventCamera, out var local))
            {
                return true;
            }

            if (holeIsEllipse)
            {
                var c = currentHole.center;
                var rx = Mathf.Max(1f, currentHole.width * 0.5f);
                var ry = Mathf.Max(1f, currentHole.height * 0.5f);
                var dx = (local.x - c.x) / rx;
                var dy = (local.y - c.y) / ry;
                return dx * dx + dy * dy > 1f;
            }

            return !currentHole.Contains(local);
        }

        private void OnDestroy()
        {
            if (material != null)
            {
                Destroy(material);
                material = null;
            }
        }
    }
}

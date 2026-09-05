using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity.Tutorial
{
    /// <summary>
    /// "Press this key" cue: a procedural keycap (rounded fill + border, same skin material the HUD popups use)
    /// with the key's label, and a yellow arrow bobbing above it pointing down at the cap. When the key is
    /// actually pressed the cap sinks and flashes so the player sees the press was accepted before the step
    /// moves on. No new art: the keycap is drawn by <c>UI/ProceduralPanel</c> and the arrow reuses
    /// <c>Resources/UI/Icons/ui_arrow</c> rotated to point down (the tutorial font has no arrow glyphs).
    /// Built at runtime as a child of the tutorial panel by <see cref="TutorialHudView"/>.
    /// </summary>
    public sealed class TutorialKeyCueView : MonoBehaviour
    {
        private const string PanelMaterialResourcePath = "UI/Panel/UI_ProceduralPanel";
        private const string ArrowSpriteResourcePath = "UI/Icons/ui_arrow";

        private static readonly int RectSizeId = Shader.PropertyToID("_RectSize");
        private static readonly int FillColorId = Shader.PropertyToID("_FillColor");
        private static readonly int BorderColorId = Shader.PropertyToID("_BorderColor");
        private static readonly int RadiusId = Shader.PropertyToID("_Radius");
        private static readonly int BorderThicknessId = Shader.PropertyToID("_BorderThickness");

        private static readonly Color CapFill = new Color(0.082f, 0.106f, 0.169f, 1f);
        private static readonly Color CapBorder = new Color(0.788f, 0.604f, 0.18f, 1f);
        private static readonly Color CapBorderFlash = new Color(1f, 0.93f, 0.7f, 1f);

        private const float CapSize = 84f;
        private const float WideCapWidth = 200f;
        private const float ArrowSize = 44f;
        private const float ArrowGap = 10f;
        private const float BobAmplitude = 10f;
        private const float BobHz = 1.6f;

        private RectTransform root;
        private sealed class Cap
        {
            public RectTransform Root;
            public RectTransform CapRect;
            public Material Material;
            public TMP_Text Label;
            public RectTransform ArrowRect;
        }

        private const float CapGap = 14f;
        private readonly System.Collections.Generic.List<Cap> caps = new System.Collections.Generic.List<Cap>();
        private System.Action<TMP_Text> fontApplierRef;
        private float arrowBaseY;
        private Coroutine pressRoutine;

        public RectTransform RectTransform => root;
        public bool IsShowing => root != null && root.gameObject.activeSelf;

        public static TutorialKeyCueView Build(Transform parent, System.Action<TMP_Text> fontApplier)
        {
            var go = new GameObject("TutorialKeyCue", typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            var view = go.AddComponent<TutorialKeyCueView>();
            view.Initialize(fontApplier);
            return view;
        }

        private void Initialize(System.Action<TMP_Text> fontApplier)
        {
            fontApplierRef = fontApplier;
            root = (RectTransform)transform;
            root.pivot = new Vector2(0f, 0.5f);
            root.sizeDelta = new Vector2(CapSize, CapSize + ArrowGap + ArrowSize + BobAmplitude);
            arrowBaseY = CapSize + ArrowGap + ArrowSize * 0.5f + BobAmplitude;
            root.gameObject.SetActive(false);
        }

        private Cap CreateCap()
        {
            var cap = new Cap();
            var capRoot = new GameObject("KeyCapSlot", typeof(RectTransform));
            capRoot.transform.SetParent(root, false);
            cap.Root = (RectTransform)capRoot.transform;
            cap.Root.anchorMin = cap.Root.anchorMax = new Vector2(0f, 0f);
            cap.Root.pivot = new Vector2(0f, 0f);

            var capGo = new GameObject("KeyCap", typeof(RectTransform), typeof(Image));
            capGo.transform.SetParent(cap.Root, false);
            cap.CapRect = (RectTransform)capGo.transform;
            cap.CapRect.anchorMin = new Vector2(0.5f, 0f);
            cap.CapRect.anchorMax = new Vector2(0.5f, 0f);
            cap.CapRect.pivot = new Vector2(0.5f, 0f);
            cap.CapRect.anchoredPosition = Vector2.zero;
            var capImage = capGo.GetComponent<Image>();
            capImage.sprite = null;
            capImage.raycastTarget = false;
            capImage.maskable = false;
            var baseMaterial = Resources.Load<Material>(PanelMaterialResourcePath);
            cap.Material = baseMaterial != null ? new Material(baseMaterial) : null;
            if (cap.Material != null)
            {
                cap.Material.SetColor(FillColorId, CapFill);
                cap.Material.SetColor(BorderColorId, CapBorder);
                cap.Material.SetFloat(RadiusId, 14f);
                cap.Material.SetFloat(BorderThicknessId, 3f);
                capImage.material = cap.Material;
                capImage.color = Color.white;
            }
            else
            {
                capImage.color = CapFill;
            }

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(cap.CapRect, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            cap.Label = labelGo.AddComponent<TextMeshProUGUI>();
            cap.Label.fontSize = 40f;
            cap.Label.fontStyle = FontStyles.Bold;
            cap.Label.alignment = TextAlignmentOptions.Center;
            cap.Label.color = new Color(0.953f, 0.914f, 0.784f, 1f);
            cap.Label.raycastTarget = false;
            cap.Label.textWrappingMode = TextWrappingModes.NoWrap;
            fontApplierRef?.Invoke(cap.Label);

            var arrowGo = new GameObject("Arrow", typeof(RectTransform), typeof(Image));
            arrowGo.transform.SetParent(cap.Root, false);
            cap.ArrowRect = (RectTransform)arrowGo.transform;
            cap.ArrowRect.anchorMin = new Vector2(0.5f, 0f);
            cap.ArrowRect.anchorMax = new Vector2(0.5f, 0f);
            cap.ArrowRect.pivot = new Vector2(0.5f, 0.5f);
            cap.ArrowRect.sizeDelta = new Vector2(ArrowSize, ArrowSize);
            cap.ArrowRect.localRotation = Quaternion.Euler(0f, 0f, -90f); // sprite points right → point down
            cap.ArrowRect.anchoredPosition = new Vector2(0f, arrowBaseY);
            var arrowImage = arrowGo.GetComponent<Image>();
            arrowImage.sprite = Resources.Load<Sprite>(ArrowSpriteResourcePath);
            arrowImage.preserveAspect = true;
            arrowImage.raycastTarget = false;
            arrowImage.maskable = false;
            arrowImage.enabled = arrowImage.sprite != null;

            caps.Add(cap);
            return cap;
        }

        public void Show(TutorialKeyId key)
        {
            Show(new[] { key });
        }

        /// <summary>Shows one keycap per key, left to right (e.g. Q and E for "rotate either way").</summary>
        public void Show(System.Collections.Generic.IReadOnlyList<TutorialKeyId> keys)
        {
            var shown = new System.Collections.Generic.List<TutorialKeyId>();
            if (keys != null)
            {
                foreach (var key in keys)
                {
                    if (key != TutorialKeyId.None && !shown.Contains(key))
                    {
                        shown.Add(key);
                    }
                }
            }

            if (shown.Count == 0)
            {
                Hide();
                return;
            }

            if (pressRoutine != null)
            {
                StopCoroutine(pressRoutine);
                pressRoutine = null;
            }

            while (caps.Count < shown.Count)
            {
                CreateCap();
            }

            var x = 0f;
            for (var i = 0; i < caps.Count; i++)
            {
                var cap = caps[i];
                if (i >= shown.Count)
                {
                    cap.Root.gameObject.SetActive(false);
                    continue;
                }

                var key = shown[i];
                var wide = key == TutorialKeyId.Space;
                var width = wide ? WideCapWidth : CapSize;
                cap.Root.gameObject.SetActive(true);
                cap.Root.anchoredPosition = new Vector2(x, 0f);
                cap.Root.sizeDelta = new Vector2(width, CapSize + ArrowGap + ArrowSize + BobAmplitude);
                cap.CapRect.sizeDelta = new Vector2(width, CapSize);
                cap.CapRect.localScale = Vector3.one;
                cap.Label.text = LabelFor(key);
                cap.Label.fontSize = wide ? 26f : 40f;
                if (cap.Material != null)
                {
                    cap.Material.SetVector(RectSizeId, new Vector4(width, CapSize, 0f, 0f));
                    cap.Material.SetColor(BorderColorId, CapBorder);
                }

                x += width + CapGap;
            }

            root.sizeDelta = new Vector2(Mathf.Max(0f, x - CapGap), CapSize + ArrowGap + ArrowSize + BobAmplitude);
            root.gameObject.SetActive(true);
        }

        public void Hide()
        {
            // A press animation in flight finishes first (the step usually advances the same frame the key
            // lands, and hiding instantly would swallow the feedback); PressRoutine hides at its end.
            if (pressRoutine != null)
            {
                return;
            }

            if (root != null)
            {
                root.gameObject.SetActive(false);
            }
        }

        /// <summary>Sink-and-flash on an accepted press (all visible caps). Runs even if the step advances meanwhile.</summary>
        public void PlayPressed()
        {
            if (!IsShowing)
            {
                return;
            }

            if (pressRoutine != null)
            {
                StopCoroutine(pressRoutine);
            }

            pressRoutine = StartCoroutine(PressRoutine());
        }

        private IEnumerator PressRoutine()
        {
            const float sink = 0.07f;
            const float rise = 0.14f;
            var t = 0f;
            while (t < sink)
            {
                t += Time.unscaledDeltaTime;
                var k = Mathf.Clamp01(t / sink);
                ApplyPress(Mathf.Lerp(1f, 0.9f, k), Color.Lerp(CapBorder, CapBorderFlash, k));
                yield return null;
            }

            t = 0f;
            while (t < rise)
            {
                t += Time.unscaledDeltaTime;
                var k = Mathf.Clamp01(t / rise);
                ApplyPress(Mathf.Lerp(0.9f, 1f, k), Color.Lerp(CapBorderFlash, CapBorder, k));
                yield return null;
            }

            ApplyPress(1f, CapBorder);
            pressRoutine = null;
            Hide();
        }

        private void ApplyPress(float scale, Color border)
        {
            foreach (var cap in caps)
            {
                if (!cap.Root.gameObject.activeSelf)
                {
                    continue;
                }

                cap.CapRect.localScale = Vector3.one * scale;
                cap.Material?.SetColor(BorderColorId, border);
            }
        }

        private void Update()
        {
            if (!IsShowing)
            {
                return;
            }

            var bob = (1f - Mathf.Cos(Time.unscaledTime * BobHz * Mathf.PI * 2f)) * 0.5f * BobAmplitude;
            foreach (var cap in caps)
            {
                if (cap.ArrowRect != null)
                {
                    cap.ArrowRect.anchoredPosition = new Vector2(0f, arrowBaseY - bob);
                }
            }
        }

        private static string LabelFor(TutorialKeyId key)
        {
            switch (key)
            {
                case TutorialKeyId.Space: return "SPACE";
                case TutorialKeyId.None: return string.Empty;
                default: return key.ToString().ToUpperInvariant();
            }
        }

        // Deactivation (ours or an ancestor's) kills the coroutine without running its tail, which would
        // leave pressRoutine dangling and Hide() permanently deferred.
        private void OnDisable()
        {
            pressRoutine = null;
            ApplyPress(1f, CapBorder);
        }

        private void OnDestroy()
        {
            foreach (var cap in caps)
            {
                if (cap.Material != null)
                {
                    Destroy(cap.Material);
                }
            }
            caps.Clear();
        }
    }
}

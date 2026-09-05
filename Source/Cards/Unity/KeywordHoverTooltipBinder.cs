using SeoulPlayup.Combat.Runtime;
using TMPro;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Attaches to a card-description <see cref="TMP_Text"/> and shows a keyword tooltip when the mouse
    /// hovers over a <c>&lt;link="kw:..."&gt;</c> span produced by <see cref="CardKeywordDecorator"/>.
    ///
    /// It is strictly display-only: it reads the mouse position and tests link geometry via
    /// <see cref="TMP_TextUtilities.FindIntersectingLink"/>, never consuming pointer input — so it is safe
    /// on draggable hand cards. A single shared <see cref="ObjectInfoTooltipHudPresenter"/> (above card UI)
    /// is reused across every bound text.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeywordHoverTooltipBinder : MonoBehaviour
    {
        private const int KeywordTooltipSortingOrder = 4560; // above the tutorial spotlight dim (4500)

        private static readonly Color TitleColor = new Color(1f, 0.92f, 0.66f, 1f);
        private static readonly Color CategoryColor = new Color(0.66f, 0.72f, 0.82f, 1f);
        private static readonly Color EffectColor = new Color(0.92f, 0.92f, 0.92f, 1f);

        private static ObjectInfoTooltipHudPresenter sharedPresenter;
        private static KeywordHoverTooltipBinder currentOwner;

        private TMP_Text label;
        private int lastLinkIndex = -1;

        /// <summary>Ensures a binder is present on the given label's GameObject (idempotent).</summary>
        public static void Ensure(TMP_Text label)
        {
            if (label == null)
            {
                return;
            }

            label.richText = true;
            var binder = label.GetComponent<KeywordHoverTooltipBinder>();
            if (binder == null)
            {
                binder = label.gameObject.AddComponent<KeywordHoverTooltipBinder>();
            }

            binder.label = label;
        }

        /// <summary>Finds a named TMP child under <paramref name="root"/> and binds it.</summary>
        public static void EnsureOnChild(Transform root, string childName)
        {
            if (root == null || string.IsNullOrEmpty(childName))
            {
                return;
            }

            foreach (var text in root.GetComponentsInChildren<TMP_Text>(includeInactive: true))
            {
                if (text != null && text.name == childName)
                {
                    Ensure(text);
                    return;
                }
            }
        }

        private void Awake()
        {
            if (label == null)
            {
                label = GetComponent<TMP_Text>();
            }
        }

        private void OnDisable()
        {
            ClearIfOwner();
        }

        private void OnDestroy()
        {
            ClearIfOwner();
        }

        private void Update()
        {
            var catalog = CardKeywordCatalogProvider.Active;
            if (label == null || catalog == null)
            {
                return;
            }

            if (!TryGetMousePosition(out var mousePos))
            {
                return;
            }

            var camera = ResolveCamera();
            var linkIndex = TMP_TextUtilities.FindIntersectingLink(label, mousePos, camera);
            if (linkIndex < 0)
            {
                ClearIfOwner();
                lastLinkIndex = -1;
                return;
            }

            if (linkIndex == lastLinkIndex && currentOwner == this)
            {
                return; // already showing this link
            }

            lastLinkIndex = linkIndex;
            var linkId = label.textInfo.linkInfo[linkIndex].GetLinkID();
            if (string.IsNullOrEmpty(linkId) || !linkId.StartsWith(CardKeywordDecorator.LinkIdPrefix, System.StringComparison.Ordinal))
            {
                ClearIfOwner();
                return;
            }

            var keyword = linkId.Substring(CardKeywordDecorator.LinkIdPrefix.Length);
            if (!catalog.TryGet(keyword, out var definition))
            {
                ClearIfOwner();
                return;
            }

            ShowTooltip(definition);
        }

        private void ShowTooltip(KeywordDefinition definition)
        {
            var presenter = EnsurePresenter();
            var lines = new System.Collections.Generic.List<ObjectInfoTooltipHudPresenter.Line>(2);
            if (!string.IsNullOrWhiteSpace(definition.Category))
            {
                lines.Add(new ObjectInfoTooltipHudPresenter.Line(definition.Category, CategoryColor));
            }

            if (!string.IsNullOrWhiteSpace(definition.Effect))
            {
                // 정적 표면 — {값}은 CSV 값 컬럼으로 해소한다(살아 있는 인스턴스가 없다).
                lines.Add(new ObjectInfoTooltipHudPresenter.Line(KeywordEffectText.Resolve(definition), EffectColor));
            }

            presenter.Show(definition.Keyword, TitleColor, lines);
            currentOwner = this;
        }

        private void ClearIfOwner()
        {
            if (currentOwner == this)
            {
                sharedPresenter?.Hide();
                currentOwner = null;
            }
        }

        private Camera ResolveCamera()
        {
            var canvas = label.canvas;
            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return null;
            }

            return canvas.worldCamera;
        }

        private static ObjectInfoTooltipHudPresenter EnsurePresenter()
        {
            if (sharedPresenter != null)
            {
                return sharedPresenter;
            }

            var host = new GameObject("KeywordTooltipHud");
            Object.DontDestroyOnLoad(host);
            sharedPresenter = host.AddComponent<ObjectInfoTooltipHudPresenter>();
            sharedPresenter.SetSortingOrder(KeywordTooltipSortingOrder);
            return sharedPresenter;
        }

        private static bool TryGetMousePosition(out Vector2 position)
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse == null)
            {
                position = default;
                return false;
            }

            position = mouse.position.ReadValue();
            return true;
#else
            position = Input.mousePosition;
            return true;
#endif
        }
    }
}

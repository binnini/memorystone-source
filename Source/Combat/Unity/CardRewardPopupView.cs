using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SeoulPlayup.Combat.Unity
{
    public readonly struct CardRewardOffer
    {
        public CardRewardOffer(
            string cardId,
            string displayName,
            string typeLabel,
            string description,
            int cost,
            CardEffectType effectType,
            int range = 0,
            CardTargetMode targetMode = CardTargetMode.None,
            CardPlayMode playMode = CardPlayMode.ManualTarget,
            int areaRadius = 0,
            string choiceOptions = "",
            string choiceOptionTexts = "",
            CardRarity rarity = CardRarity.Basic)
        {
            CardId = cardId ?? string.Empty;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? (cardId ?? string.Empty) : displayName;
            TypeLabel = typeLabel ?? string.Empty;
            Description = description ?? string.Empty;
            Cost = Math.Max(0, cost);
            EffectType = effectType;
            Range = Math.Max(0, range);
            TargetMode = targetMode;
            PlayMode = playMode;
            AreaRadius = Math.Max(0, areaRadius);
            ChoiceOptions = choiceOptions ?? string.Empty;
            ChoiceOptionTexts = choiceOptionTexts ?? string.Empty;
            Rarity = rarity;
        }

        public string CardId { get; }
        public string DisplayName { get; }
        public string TypeLabel { get; }
        public string Description { get; }
        public int Cost { get; }
        public CardEffectType EffectType { get; }
        public int Range { get; }
        public CardTargetMode TargetMode { get; }
        public CardPlayMode PlayMode { get; }
        public int AreaRadius { get; }
        public string ChoiceOptions { get; }
        public string ChoiceOptionTexts { get; }
        public CardRarity Rarity { get; }
    }

    public sealed class CardRewardPopupView : MonoBehaviour, ICardRewardPopupView
    {
        private const string AttackCardSpritePath = "UI/Cards/Exorcism/card_front_attack";
        private const string DefendCardSpritePath = "UI/Cards/Exorcism/card_front_defend";
        private const string ScoutFieldCardSpritePath = "UI/Cards/Exorcism/card_front_scout_investigate";
        private const string MoveCardSpritePath = "UI/Cards/Exorcism/card_front_move";
        // Grayscale (luminance) glow copies so the additive glow can be tinted to the rarity color. The
        // authored gold sprites kill the blue channel, which corrupts blue/purple tints into green/red.
        // Builds use the prefab's serialized sprite reference; this editor path is the editor-only fallback.
        private const string RewardSoftGlowSpritePath = "Assets/Art/UI/Cards/card_glow_reward_white.png";
        private const string RewardRaysGlowSpritePath = "Assets/Art/UI/Cards/card_glow_reward2_white.png";
        private const string RewardGlowMaterialPath = "Assets/Materials/UI_AdditiveGlow.mat";

        private static readonly Color CardBaseColor = new Color(0.14f, 0.15f, 0.18f, 0.98f);

        private static TMP_FontAsset koreanFontAsset;
        private static Sprite attackCardSprite;
        private static Sprite defendCardSprite;
        private static Sprite scoutFieldCardSprite;
        private static Sprite moveCardSprite;
        private static Sprite rewardSoftGlowSpriteFallback;
        private static Sprite rewardRaysGlowSpriteFallback;
        private static Material rewardGlowMaterialFallback;

        [Header("Authored Reward Popup")]
        [SerializeField] private RectTransform overlay;
        [SerializeField] private RectTransform panel;
        // The visible reward panel, or null while the popup is closed — the tutorial spotlight targets it.
        public RectTransform PanelRect => panel != null && panel.gameObject.activeInHierarchy ? panel : null;

        // Screen-space bounds of the offered cards (the panel rect itself is only a small anchor; the card
        // slots are laid out around it). Null while closed. Used by the tutorial spotlight.
        public bool TryGetOfferCardsScreenBounds(out Rect bounds)
        {
            bounds = default;
            if (!gameObject.activeInHierarchy)
            {
                return false;
            }

            var corners = new Vector3[4];
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            var any = false;
            foreach (var slot in GetComponentsInChildren<HandCardInteraction>(includeInactive: false))
            {
                var rect = slot.transform as RectTransform;
                if (rect == null)
                {
                    continue;
                }

                var canvas = rect.GetComponentInParent<Canvas>();
                var camera = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
                rect.GetWorldCorners(corners);
                for (var i = 0; i < 4; i++)
                {
                    var p = RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                }
                any = true;
            }

            if (!any)
            {
                return false;
            }

            bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            return true;
        }
        [Tooltip("Optional popup-skin theme routing; when empty the skin keeps its built-in defaults.")]
        [SerializeField] private UiThemeAsset theme;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text subtitleText;
        [SerializeField] private Button skipButton;
        [SerializeField] private TMP_Text skipLabelText;
        [SerializeField] private Button rerollButton;
        [SerializeField] private TMP_Text rerollLabelText;
        [SerializeField] private Button hideButton;
        [SerializeField] private TMP_Text hideLabelText;
        [SerializeField] private RewardCardSlotBinding[] cardSlots = Array.Empty<RewardCardSlotBinding>();
        [SerializeField] private Sprite rewardSoftGlowSprite;
        [SerializeField] private Sprite rewardRaysGlowSprite;
        [SerializeField] private Material rewardGlowMaterial;

        private IReadOnlyList<CardRewardOffer> currentOffers;
        private Action<string> onCardSelected;
        private Action onSkipped;
        private Action onCardHovered;
        private Action onReroll;
        private bool canReroll;

        // "숨기기" temporarily hides the reward chrome (and stops it blocking raycasts) so the player can
        // inspect the Sidebar deck list / CardLane piles, then auto-restores when a pile overlay closes.
        private CanvasGroup rootCanvasGroup;
        private Button restoreButton;
        private TMP_Text restoreLabelText;
        private bool isTemporarilyHidden;
        private bool subscribedToPileOverlay;
        private DeckPileListOverlayView subscribedPileOverlay;

        public void AutoBindFromHierarchy()
        {
            var rects = GetComponentsInChildren<RectTransform>(includeInactive: true);
            overlay = overlay != null ? overlay : rects.FirstOrDefault(rect => rect.name == "Reward Overlay" || rect.name == "OverlayPanel");
            panel = panel != null ? panel : rects.FirstOrDefault(rect => rect.name == "Reward Panel" || rect.name == "Card List");
            titleText = titleText != null ? titleText : FindText("Reward Title") ?? FindText("TitleText");
            subtitleText = subtitleText != null ? subtitleText : FindText("Reward Subtitle") ?? FindText("DescriptionText");
            skipButton = skipButton != null ? skipButton : GetComponentsInChildren<Button>(includeInactive: true)
                .FirstOrDefault(button => button.name == "Reward Skip Button" || button.name == "SkipButton");
            skipLabelText = skipLabelText != null ? skipLabelText : FindText("Reward Skip Label") ?? FindTextUnder(skipButton, "Text (TMP)");
            rerollButton = rerollButton != null ? rerollButton : GetComponentsInChildren<Button>(includeInactive: true)
                .FirstOrDefault(button => button.name == "Reward Reroll Button" || button.name == "RerollButton");
            rerollLabelText = rerollLabelText != null ? rerollLabelText : FindText("Reward Reroll Label") ?? FindTextUnder(rerollButton, "Text (TMP)");
            hideButton = hideButton != null ? hideButton : GetComponentsInChildren<Button>(includeInactive: true)
                .FirstOrDefault(button => button.name == "Reward Hide Button" || button.name == "HideButton");
            hideLabelText = hideLabelText != null ? hideLabelText : FindText("Reward Hide Label") ?? FindTextUnder(hideButton, "Text (TMP)");

            cardSlots = rects
                .Where(IsRewardCardSlotRoot)
                .OrderBy(rect => rect.name, StringComparer.Ordinal)
                .Select(RewardCardSlotBinding.From)
                .Where(slot => slot.Root != null)
                .ToArray();

            ApplyKoreanFontToAllText();
            ConfigureSkipButton();
            ConfigureRerollButton();
            ConfigureHideButton();
        }

        public void Show(IReadOnlyList<CardRewardOffer> offers, Action<string> onSelected, Action onSkip, Action onHover = null)
        {
            Show(offers, onSelected, onSkip, onHover, null, false);
        }

        public void Show(
            IReadOnlyList<CardRewardOffer> offers,
            Action<string> onSelected,
            Action onSkip,
            Action onHover,
            Action onRerollRequested,
            bool rerollAvailable)
        {
            currentOffers = offers ?? Array.Empty<CardRewardOffer>();
            onCardSelected = onSelected;
            onSkipped = onSkip;
            onCardHovered = onHover;
            onReroll = onRerollRequested;
            canReroll = rerollAvailable && onRerollRequested != null;
            EnsureBound();
            RestoreFromHidden();
            PopulateCards();
            RefreshRerollButton();
            gameObject.SetActive(true);
        }

        public void ReplaceOffers(IReadOnlyList<CardRewardOffer> offers, bool rerollAvailable)
        {
            currentOffers = offers ?? Array.Empty<CardRewardOffer>();
            canReroll = rerollAvailable && onReroll != null;
            EnsureBound();
            PopulateCards();
            RefreshRerollButton();
        }

        public void Hide()
        {
            // Full hide (reward resolved): drop any temporary-hide state so the next Show() starts clean.
            RestoreFromHidden();
            gameObject.SetActive(false);
        }

        private void EnsureBound()
        {
            if (cardSlots == null || cardSlots.Length == 0 || cardSlots.Any(slot => slot.Root == null))
            {
                AutoBindFromHierarchy();
            }
            else
            {
                ConfigureSkipButton();
                ConfigureRerollButton();
                ConfigureHideButton();
            }

            ApplyPanelSkin();
        }

        // P4 popup skin: runtime-attached (no scene/prefab edits, same pattern as the runtime buttons).
        // The authored flat panel colour stays as the fallback look when the skin material is missing.
        private void ApplyPanelSkin()
        {
            var panelImage = panel != null ? panel.GetComponent<Image>() : null;
            if (panelImage == null)
            {
                return;
            }

            var skin = panel.GetComponent<UiProceduralPanel>();
            if (skin == null)
            {
                skin = panel.gameObject.AddComponent<UiProceduralPanel>();
            }

            skin.Configure(theme);
        }

        private void ConfigureSkipButton()
        {
            if (skipButton == null)
            {
                return;
            }

            skipButton.onClick.RemoveAllListeners();
            skipButton.onClick.AddListener(() => onSkipped?.Invoke());
            // P4 unified button skin (runtime attach; authored blue plate stays as the fallback look).
            UiButtonSkin.Apply(skipButton, theme);
            if (skipLabelText != null && string.IsNullOrWhiteSpace(skipLabelText.text))
            {
                skipLabelText.text = "\uB118\uAE30\uAE30";
            }

            AlignActionButtonsSymmetrically();
        }

        private void ConfigureRerollButton()
        {
            if (rerollButton == null)
            {
                CreateRuntimeRerollButton();
            }

            if (rerollButton == null)
            {
                return;
            }

            rerollButton.onClick.RemoveAllListeners();
            rerollButton.onClick.AddListener(() =>
            {
                if (!canReroll)
                {
                    return;
                }

                onReroll?.Invoke();
            });

            if (rerollLabelText != null && string.IsNullOrWhiteSpace(rerollLabelText.text))
            {
                rerollLabelText.text = "\uB2E4\uC2DC \uBF51\uAE30";
            }

            AlignActionButtonsSymmetrically();
            RefreshRerollButton();
        }

        private void CreateRuntimeRerollButton()
        {
            if (skipButton == null)
            {
                return;
            }

            var skipRect = skipButton.GetComponent<RectTransform>();
            var parent = skipRect != null ? skipRect.parent : skipButton.transform.parent;
            if (parent == null)
            {
                return;
            }

            var go = new GameObject("Reward Reroll Button", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            if (skipRect != null)
            {
                rect.anchorMin = skipRect.anchorMin;
                rect.anchorMax = skipRect.anchorMax;
                rect.pivot = skipRect.pivot;
                rect.sizeDelta = skipRect.sizeDelta;
                rect.anchoredPosition = skipRect.anchoredPosition;
            }

            var image = go.GetComponent<Image>();
            var skipImage = skipButton.GetComponent<Image>();
            if (skipImage != null)
            {
                image.sprite = skipImage.sprite;
                image.type = skipImage.type;
                image.color = skipImage.color;
            }
            else
            {
                image.color = new Color(0.16f, 0.18f, 0.22f, 0.95f);
            }

            rerollButton = go.GetComponent<Button>();
            var labelGo = new GameObject("Reward Reroll Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(go.transform, false);
            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            rerollLabelText = labelGo.GetComponent<TextMeshProUGUI>();
            rerollLabelText.alignment = TextAlignmentOptions.Center;
            rerollLabelText.fontSize = skipLabelText != null ? skipLabelText.fontSize : 18f;
            rerollLabelText.color = skipLabelText != null ? skipLabelText.color : Color.white;
            rerollLabelText.raycastTarget = false;
            KoreanFontProvider.Apply(rerollLabelText);
            UiButtonSkin.Apply(rerollButton, theme);
            AlignActionButtonsSymmetrically();
        }

        private void ConfigureHideButton()
        {
            if (hideButton == null)
            {
                CreateRuntimeHideButton();
            }

            if (hideButton == null)
            {
                return;
            }

            hideButton.onClick.RemoveAllListeners();
            hideButton.onClick.AddListener(HideTemporarily);

            if (hideLabelText != null && string.IsNullOrWhiteSpace(hideLabelText.text))
            {
                hideLabelText.text = "숨기기"; // 숨기기
            }

            AlignActionButtonsSymmetrically();
        }

        private void CreateRuntimeHideButton()
        {
            var go = CloneSkipButton("Reward Hide Button", "Reward Hide Label", out var label);
            if (go == null)
            {
                return;
            }

            hideButton = go.GetComponent<Button>();
            hideLabelText = label;
        }

        // Clones the authored SkipButton's RectTransform + Image styling into a new sibling button so the
        // runtime-created 다시뽑기/숨기기 buttons match 넘기기 without prefab edits.
        private GameObject CloneSkipButton(string buttonName, string labelName, out TMP_Text label)
        {
            label = null;
            if (skipButton == null)
            {
                return null;
            }

            var skipRect = skipButton.GetComponent<RectTransform>();
            var parent = skipRect != null ? skipRect.parent : skipButton.transform.parent;
            if (parent == null)
            {
                return null;
            }

            var go = new GameObject(buttonName, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            if (skipRect != null)
            {
                rect.anchorMin = skipRect.anchorMin;
                rect.anchorMax = skipRect.anchorMax;
                rect.pivot = skipRect.pivot;
                rect.sizeDelta = skipRect.sizeDelta;
                rect.anchoredPosition = skipRect.anchoredPosition;
            }

            var image = go.GetComponent<Image>();
            var skipImage = skipButton.GetComponent<Image>();
            if (skipImage != null)
            {
                image.sprite = skipImage.sprite;
                image.type = skipImage.type;
                image.color = skipImage.color;
            }
            else
            {
                image.color = new Color(0.16f, 0.18f, 0.22f, 0.95f);
            }

            var labelGo = new GameObject(labelName, typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(go.transform, false);
            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            label = labelGo.GetComponent<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = skipLabelText != null ? skipLabelText.fontSize : 18f;
            label.color = skipLabelText != null ? skipLabelText.color : Color.white;
            label.raycastTarget = false;
            KoreanFontProvider.Apply(label);
            UiButtonSkin.Apply(go.GetComponent<Button>(), theme);
            return go;
        }

        // --- 숨기기 / 보상 보기 (temporary hide) -------------------------------------------------

        private void HideTemporarily()
        {
            isTemporarilyHidden = true;
            EnsureRootCanvasGroup();
            if (rootCanvasGroup != null)
            {
                rootCanvasGroup.alpha = 0f;
                rootCanvasGroup.interactable = false;
                rootCanvasGroup.blocksRaycasts = false; // let Sidebar / CardLane receive clicks
            }

            EnsureRestoreButton();
            if (restoreButton != null)
            {
                restoreButton.gameObject.SetActive(true);
                restoreButton.transform.SetAsLastSibling();
            }

            SubscribeToPileOverlayClose();
        }

        private void RestoreFromHidden()
        {
            isTemporarilyHidden = false;
            if (rootCanvasGroup != null)
            {
                rootCanvasGroup.alpha = 1f;
                rootCanvasGroup.interactable = true;
                rootCanvasGroup.blocksRaycasts = true;
            }

            if (restoreButton != null)
            {
                restoreButton.gameObject.SetActive(false);
            }
        }

        private void EnsureRootCanvasGroup()
        {
            if (rootCanvasGroup == null)
            {
                rootCanvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            }
        }

        // Floating "보상 보기" button shown while the reward is temporarily hidden. It carries its own
        // CanvasGroup with ignoreParentGroups so it stays visible/clickable even though the root
        // CanvasGroup is alpha 0 / non-blocking.
        private void EnsureRestoreButton()
        {
            if (restoreButton != null)
            {
                return;
            }

            var go = new GameObject("Reward Restore Button", typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(Button));
            go.transform.SetParent(transform, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(200f, 64f);
            rect.anchoredPosition = new Vector2(0f, 36f);

            var group = go.GetComponent<CanvasGroup>();
            group.ignoreParentGroups = true;
            group.alpha = 1f;
            group.interactable = true;
            group.blocksRaycasts = true;

            var image = go.GetComponent<Image>();
            var skipImage = skipButton != null ? skipButton.GetComponent<Image>() : null;
            if (skipImage != null)
            {
                image.sprite = skipImage.sprite;
                image.type = skipImage.type;
                image.color = skipImage.color;
            }
            else
            {
                image.color = new Color(0.16f, 0.18f, 0.22f, 0.95f);
            }

            var labelGo = new GameObject("Reward Restore Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(go.transform, false);
            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            restoreLabelText = labelGo.GetComponent<TextMeshProUGUI>();
            restoreLabelText.alignment = TextAlignmentOptions.Center;
            restoreLabelText.fontSize = skipLabelText != null ? skipLabelText.fontSize : 18f;
            restoreLabelText.color = skipLabelText != null ? skipLabelText.color : Color.white;
            restoreLabelText.raycastTarget = false;
            restoreLabelText.text = "보상 보기"; // 보상 보기
            KoreanFontProvider.Apply(restoreLabelText);

            restoreButton = go.GetComponent<Button>();
            restoreButton.onClick.AddListener(RestoreFromHidden);
            UiButtonSkin.Apply(restoreButton, theme);
            go.SetActive(false);
        }

        // Auto-restore the reward when the player closes the deck/pile overlay they opened to inspect cards.
        private void SubscribeToPileOverlayClose()
        {
            if (subscribedToPileOverlay)
            {
                return;
            }

            var overlay = FindFirstObjectByType<DeckPileListOverlayView>(FindObjectsInactive.Include);
            if (overlay == null)
            {
                return; // restore button still works as the manual fallback
            }

            overlay.Closed += OnPileOverlayClosed;
            subscribedPileOverlay = overlay;
            subscribedToPileOverlay = true;
        }

        private void OnDestroy()
        {
            if (subscribedPileOverlay != null)
            {
                subscribedPileOverlay.Closed -= OnPileOverlayClosed;
                subscribedPileOverlay = null;
            }
        }

        private void OnPileOverlayClosed()
        {
            if (isTemporarilyHidden)
            {
                RestoreFromHidden();
            }
        }

        // Lays the three action buttons out symmetrically about 넘기기(Skip): 다시뽑기(left) | 넘기기(center) |
        // 숨기기(right). The 다시뽑기 slot is always reserved (even when reroll is unavailable and hidden) so
        // 넘기기 stays perfectly centered, keeping the row mirror-symmetric.
        private void AlignActionButtonsSymmetrically()
        {
            if (skipButton == null)
            {
                return;
            }

            var skipRect = skipButton.GetComponent<RectTransform>();
            if (skipRect == null)
            {
                return;
            }

            var rerollRect = rerollButton != null ? rerollButton.GetComponent<RectTransform>() : null;
            var hideRect = hideButton != null ? hideButton.GetComponent<RectTransform>() : null;
            var parent = skipRect.parent as RectTransform;

            var gap = 16f;
            var buttonWidth = WidthOf(skipRect);
            buttonWidth = Mathf.Max(buttonWidth, WidthOf(rerollRect));
            buttonWidth = Mathf.Max(buttonWidth, WidthOf(hideRect));
            var step = buttonWidth + gap;
            var centerX = ResolveMiddleCardCenterX(parent);
            var y = skipRect.anchoredPosition.y;

            CenterAnchor(skipRect);
            skipRect.anchoredPosition = new Vector2(centerX, y);

            if (rerollRect != null && rerollRect.parent == skipRect.parent)
            {
                CenterAnchor(rerollRect);
                rerollRect.anchoredPosition = new Vector2(centerX - step, y);
            }

            if (hideRect != null && hideRect.parent == skipRect.parent)
            {
                CenterAnchor(hideRect);
                hideRect.anchoredPosition = new Vector2(centerX + step, y);
            }
        }

        private static float WidthOf(RectTransform rect)
        {
            if (rect == null)
            {
                return 0f;
            }

            return rect.rect.width > 0f ? rect.rect.width : Mathf.Max(0f, rect.sizeDelta.x);
        }

        private static void CenterAnchor(RectTransform rect)
        {
            rect.anchorMin = new Vector2(0.5f, rect.anchorMin.y);
            rect.anchorMax = new Vector2(0.5f, rect.anchorMax.y);
        }

        private float ResolveMiddleCardCenterX(RectTransform buttonParent)
        {
            if (buttonParent == null || cardSlots == null || cardSlots.Length == 0)
            {
                return 0f;
            }

            var visibleSlots = cardSlots
                .Where(slot => slot.Root != null && slot.Root.activeSelf)
                .ToArray();
            var middleSlot = visibleSlots.Length > 0
                ? visibleSlots[visibleSlots.Length / 2]
                : cardSlots[cardSlots.Length / 2];
            if (middleSlot.Root == null)
            {
                return 0f;
            }

            var middleRect = middleSlot.Root.transform as RectTransform;
            if (middleRect == null)
            {
                return 0f;
            }

            Canvas.ForceUpdateCanvases();
            if (panel != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
            }

            var corners = new Vector3[4];
            middleRect.GetWorldCorners(corners);
            var worldCenter = (corners[0] + corners[2]) * 0.5f;
            var localCenter = buttonParent.InverseTransformPoint(worldCenter);
            return localCenter.x;
        }

        private void RefreshRerollButton()
        {
            if (rerollButton == null)
            {
                return;
            }

            rerollButton.gameObject.SetActive(canReroll);
            rerollButton.interactable = canReroll;
        }

        private void PopulateCards()
        {
            if (titleText != null && string.IsNullOrWhiteSpace(titleText.text))
            {
                titleText.text = "\uBCF4\uC0C1\uC744 \uC120\uD0DD\uD558\uC138\uC694.";
            }

            if (subtitleText != null && string.IsNullOrWhiteSpace(subtitleText.text))
            {
                subtitleText.text = "\uCE74\uB4DC \uD55C \uC7A5\uC744 \uC120\uD0DD\uD574 \uD604\uC7AC \uC190\uD328\uC5D0 \uCD94\uAC00\uD569\uB2C8\uB2E4.";
            }

            for (var i = 0; i < cardSlots.Length; i++)
            {
                var hasOffer = currentOffers != null && i < currentOffers.Count;
                cardSlots[i].SetActive(hasOffer);
                if (!hasOffer)
                {
                    continue;
                }

                var slotIndex = i;
                var offer = currentOffers[i];
                cardSlots[i].Set(
                    offer,
                    GetCardSprite(offer.EffectType),
                    CardBaseColor,
                    ResolveRewardSoftGlowSprite(rewardSoftGlowSprite),
                    ResolveRewardRaysGlowSprite(rewardRaysGlowSprite),
                    ResolveRewardGlowMaterial(rewardGlowMaterial),
                    () => OnCardClicked(slotIndex),
                    () => onCardHovered?.Invoke());
            }

            AlignActionButtonsSymmetrically();
        }

        private void OnCardClicked(int slotIndex)
        {
            if (currentOffers == null || slotIndex < 0 || slotIndex >= currentOffers.Count)
            {
                return;
            }

            onCardSelected?.Invoke(currentOffers[slotIndex].CardId);
        }

        private TMP_Text FindText(string objectName)
        {
            return GetComponentsInChildren<TMP_Text>(includeInactive: true)
                .FirstOrDefault(text => text.name == objectName);
        }

        private TMP_Text FindTextUnder(Component root, string objectName)
        {
            return root == null
                ? null
                : root.GetComponentsInChildren<TMP_Text>(includeInactive: true)
                    .FirstOrDefault(text => text.name == objectName);
        }

        private bool IsRewardCardSlotRoot(RectTransform rect)
        {
            if (rect == null || rect == transform)
            {
                return false;
            }

            if (rect.name.StartsWith("Reward Card ", StringComparison.Ordinal) && rect.parent == panel)
            {
                return true;
            }

            return rect.GetComponent<ChoiceCardOptionSlot>() != null
                && rect.GetComponent<Button>() != null
                && (panel == null || rect.IsChildOf(panel));
        }

        private void ApplyKoreanFontToAllText()
        {
            var font = LoadKoreanFont();
            if (font == null)
            {
                return;
            }

            foreach (var text in GetComponentsInChildren<TMP_Text>(includeInactive: true))
            {
                // This sweep would otherwise flatten the authored title weight back to the body face.
                if (IsTitleLabel(text))
                {
                    KoreanFontProvider.ApplyTitle(text);
                }
                else
                {
                    KoreanFontProvider.Apply(text);
                }
            }
        }

        private static bool IsTitleLabel(TMP_Text text)
        {
            if (text == null)
            {
                return false;
            }

            var name = text.name;
            return name == "Reward Title" || name == "TitleText" || name == "DescriptionText";
        }

        private static Sprite GetCardSprite(CardEffectType effectType)
        {
            switch (effectType)
            {
                case CardEffectType.Attack:
                    return attackCardSprite ?? (attackCardSprite = Resources.Load<Sprite>(AttackCardSpritePath));
                case CardEffectType.Defend:
                    return defendCardSprite ?? (defendCardSprite = Resources.Load<Sprite>(DefendCardSpritePath));
                case CardEffectType.Scout:
                case CardEffectType.FieldObject:
                    return scoutFieldCardSprite ?? (scoutFieldCardSprite = Resources.Load<Sprite>(ScoutFieldCardSpritePath));
                case CardEffectType.Move:
                    return moveCardSprite ?? (moveCardSprite = Resources.Load<Sprite>(MoveCardSpritePath));
                default:
                    return null;
            }
        }

        private static TMP_FontAsset LoadKoreanFont()
        {
            if (koreanFontAsset != null)
            {
                return koreanFontAsset;
            }

            koreanFontAsset = KoreanFontProvider.Load();
            return koreanFontAsset;
        }

        private static Sprite ResolveRewardSoftGlowSprite(Sprite authoredSprite)
        {
            return authoredSprite != null ? authoredSprite : LoadEditorSprite(RewardSoftGlowSpritePath, ref rewardSoftGlowSpriteFallback);
        }

        private static Sprite ResolveRewardRaysGlowSprite(Sprite authoredSprite)
        {
            return authoredSprite != null ? authoredSprite : LoadEditorSprite(RewardRaysGlowSpritePath, ref rewardRaysGlowSpriteFallback);
        }

        private static Material ResolveRewardGlowMaterial(Material authoredMaterial)
        {
            if (authoredMaterial != null)
            {
                return authoredMaterial;
            }

#if UNITY_EDITOR
            return rewardGlowMaterialFallback != null
                ? rewardGlowMaterialFallback
                : (rewardGlowMaterialFallback = AssetDatabase.LoadAssetAtPath<Material>(RewardGlowMaterialPath));
#else
            return rewardGlowMaterialFallback;
#endif
        }

        private static Sprite LoadEditorSprite(string path, ref Sprite cache)
        {
            if (cache != null)
            {
                return cache;
            }

#if UNITY_EDITOR
            cache = AssetDatabase.LoadAssetAtPath<Sprite>(path);
#endif
            return cache;
        }

        [Serializable]
        private sealed class RewardCardSlotBinding
        {
            [SerializeField] private GameObject root;
            [SerializeField] private Image background;
            [SerializeField] private TMP_Text costLabel;
            [SerializeField] private TMP_Text typeLabel;
            [SerializeField] private TMP_Text nameLabel;
            [SerializeField] private TMP_Text descLabel;
            [SerializeField] private Button clickButton;
            [SerializeField] private ChoiceCardOptionSlot optionSlot;
            [SerializeField] private CardRewardGlowOverlay rewardGlowOverlay;

            public GameObject Root => root;

            public static RewardCardSlotBinding From(RectTransform slotRoot)
            {
                var binding = new RewardCardSlotBinding
                {
                    root = slotRoot != null ? slotRoot.gameObject : null,
                    background = slotRoot != null ? slotRoot.GetComponent<Image>() : null,
                    optionSlot = slotRoot != null ? slotRoot.GetComponent<ChoiceCardOptionSlot>() : null
                };

                var texts = slotRoot != null ? slotRoot.GetComponentsInChildren<TMP_Text>(includeInactive: true) : Array.Empty<TMP_Text>();
                binding.costLabel = texts.FirstOrDefault(text => text.name.EndsWith(" Cost", StringComparison.Ordinal));
                binding.typeLabel = texts.FirstOrDefault(text => text.name.EndsWith(" Type", StringComparison.Ordinal));
                binding.nameLabel = texts.FirstOrDefault(text => text.name.EndsWith(" Name", StringComparison.Ordinal));
                binding.descLabel = texts.FirstOrDefault(text => text.name.EndsWith(" Desc", StringComparison.Ordinal));
                binding.clickButton = slotRoot != null
                    ? slotRoot.GetComponentsInChildren<Button>(includeInactive: true).FirstOrDefault()
                    : null;
                return binding;
            }

            public void SetActive(bool active)
            {
                if (root != null)
                {
                    root.SetActive(active);
                }
            }

            public void Set(
                CardRewardOffer offer,
                Sprite sprite,
                Color fallbackColor,
                Sprite softGlowSprite,
                Sprite raysGlowSprite,
                Material glowMaterial,
                Action onClick,
                Action onHover)
            {
                ConfigureRewardGlow(softGlowSprite, raysGlowSprite, glowMaterial, onHover, offer.Rarity);
                ConfigureRarityBadge(offer.Rarity);

                if (optionSlot != null)
                {
                    optionSlot.Bind(
                        CreateRewardSnapshot(offer),
                        CreateRewardOption(offer),
                        onClick,
                        enableCardHoverGlow: false,
                        enablePointerLayerPromotion: false);
                }

                SetText(costLabel, offer.Cost.ToString());
                SetText(typeLabel, offer.TypeLabel);
                SetText(nameLabel, offer.DisplayName);
                SetText(descLabel, CardKeywordDecorator.Decorate(offer.Description));
                KeywordHoverTooltipBinder.Ensure(descLabel);

                if (background != null)
                {
                    background.sprite = sprite;
                    background.type = Image.Type.Simple;
                    background.preserveAspect = false;
                    background.color = sprite != null ? Color.white : fallbackColor;
                }

                if (clickButton != null)
                {
                    clickButton.onClick.RemoveAllListeners();
                    clickButton.onClick.AddListener(() =>
                    {
                        if (rewardGlowOverlay != null)
                        {
                            rewardGlowOverlay.SetSelected(true);
                        }

                        onClick?.Invoke();
                    });
                }
            }

            private void ConfigureRewardGlow(Sprite softGlowSprite, Sprite raysGlowSprite, Material glowMaterial, Action onHover, CardRarity rarity)
            {
                if (root == null)
                {
                    return;
                }

                rewardGlowOverlay = rewardGlowOverlay != null
                    ? rewardGlowOverlay
                    : root.GetComponent<CardRewardGlowOverlay>();
                if (rewardGlowOverlay == null)
                {
                    rewardGlowOverlay = root.AddComponent<CardRewardGlowOverlay>();
                }

                if (rewardGlowOverlay != null)
                {
                    var tint = CardRarityRewardPresentation.TryGet(rarity, out var rarityColor, out _)
                        ? (Color?)rarityColor
                        : null;
                    rewardGlowOverlay.Configure(softGlowSprite, raysGlowSprite, glowMaterial, onHover, tint);
                }

                foreach (var interaction in root.GetComponentsInChildren<HandCardInteraction>(includeInactive: true))
                {
                    interaction.ConfigureHoverMotion(liftY: 0f, hoverScaleMultiplier: 1f, dragScaleMultiplier: 1f);
                    interaction.ConfigureCardHoverGlowEnabled(false);
                    interaction.ConfigurePointerLayerPromotion(false);
                }
            }

            // Runtime-created rarity chip pinned to the card's top edge. Looked up by name each time so it
            // is reused (never duplicated) even if the slot binding is rebuilt by AutoBindFromHierarchy.
            private const string RarityBadgeName = "Reward Rarity Badge";

            private void ConfigureRarityBadge(CardRarity rarity)
            {
                if (root == null)
                {
                    return;
                }

                var hasStyle = CardRarityRewardPresentation.TryGet(rarity, out var color, out var label);
                var badgeRect = root.transform.Find(RarityBadgeName) as RectTransform;
                if (!hasStyle)
                {
                    if (badgeRect != null)
                    {
                        badgeRect.gameObject.SetActive(false);
                    }

                    return;
                }

                if (badgeRect == null)
                {
                    badgeRect = CreateRarityBadge();
                }

                badgeRect.gameObject.SetActive(true);
                badgeRect.SetAsLastSibling();

                var background = badgeRect.GetComponent<Image>();
                if (background != null)
                {
                    background.color = new Color(color.r, color.g, color.b, 0.94f);
                }

                var labelText = badgeRect.GetComponentInChildren<TMP_Text>(includeInactive: true);
                if (labelText != null)
                {
                    KoreanFontProvider.Apply(labelText);
                    labelText.text = label;
                }
            }

            private RectTransform CreateRarityBadge()
            {
                var go = new GameObject(RarityBadgeName, typeof(RectTransform), typeof(Image));
                var rect = go.GetComponent<RectTransform>();
                rect.SetParent(root.transform, false);
                // Pin just ABOVE the card's top edge (pivot at the chip's bottom, positive Y) so the badge
                // floats over the header gap instead of covering the card art.
                rect.anchorMin = new Vector2(0.5f, 1f);
                rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.sizeDelta = new Vector2(96f, 30f);
                rect.anchoredPosition = new Vector2(0f, 8f);

                var background = go.GetComponent<Image>();
                background.raycastTarget = false;

                var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                var labelRect = labelGo.GetComponent<RectTransform>();
                labelRect.SetParent(rect, false);
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;

                var labelText = labelGo.GetComponent<TextMeshProUGUI>();
                labelText.alignment = TextAlignmentOptions.Center;
                labelText.fontSize = 16f;
                labelText.fontStyle = FontStyles.Bold;
                labelText.color = Color.white;
                labelText.raycastTarget = false;
                KoreanFontProvider.Apply(labelText);

                return rect;
            }

            private static void SetText(TMP_Text text, string value)
            {
                if (text != null)
                {
                    KoreanFontProvider.Apply(text);
                    text.text = value ?? string.Empty;
                }
            }

            private static ChoiceCardOptionModel CreateRewardOption(CardRewardOffer offer)
            {
                return new ChoiceCardOptionModel(offer.CardId, offer.DisplayName, offer.Description, offer.TargetMode);
            }

            private static CombatCardSnapshot CreateRewardSnapshot(CardRewardOffer offer)
            {
                return new CombatCardSnapshot(
                    offer.CardId,
                    ToCombatCardKind(offer.EffectType),
                    offer.DisplayName,
                    offer.Description,
                    0,
                    isUsable: true,
                    isDiscarded: false,
                    string.Empty,
                    offer.Cost,
                    offer.Range,
                    pile: "Reward",
                    playMode: offer.PlayMode,
                    areaRadius: offer.AreaRadius,
                    choiceOptions: offer.ChoiceOptions,
                    choiceOptionTexts: offer.ChoiceOptionTexts,
                    targetMode: offer.TargetMode,
                    isStatusCard: offer.EffectType == CardEffectType.Status);
            }

            private static CombatCardKind ToCombatCardKind(CardEffectType effectType)
            {
                switch (effectType)
                {
                    case CardEffectType.Move: return CombatCardKind.Move;
                    case CardEffectType.Attack: return CombatCardKind.Attack;
                    case CardEffectType.Defend: return CombatCardKind.Defend;
                    case CardEffectType.Scout: return CombatCardKind.Scout;
                    case CardEffectType.FieldObject: return CombatCardKind.FieldObject;
                    case CardEffectType.Buff: return CombatCardKind.Buff;
                    case CardEffectType.Utility: return CombatCardKind.Utility;
                    default: return CombatCardKind.Utility;
                }
            }
        }
    }
}


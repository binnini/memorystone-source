using System;
using SeoulPlayup.Cards.Unity;
using SeoulPlayup.Combat.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// 가방 소모품 사용 확인창(2026-09-02 #16 · 사용자 확정: "확인/취소 2버튼").
    ///
    /// <para>🔴 <b>무대상 아이템은 클릭 한 번이 곧 소모였다.</b> 대상 지정형(T4-2)은 「다음 맵 클릭이
    /// 대상」이라 그 사이에 마음을 바꿀 창이 있는데, 무대상 아이템에는 그 창이 없어 잘못 누르면
    /// 되돌릴 수 없었다. 그래서 <b>무대상 아이템에만</b> 이 확인창을 세운다 — 대상 지정형까지 물으면
    /// 확인이 두 번(확인 → 대상 클릭)이 되어, 이미 있는 취소 경로 위에 절차만 하나 얹는 셈이 된다.</para>
    ///
    /// <para>🔑 버튼 규격은 <see cref="CardConfirmPanelSpec"/>에서 가져온다 — 카드 제거·연마 확인과
    /// 같은 크기·같은 리듬이어야 「확인하는 화면」이 게임 안에서 한 가지 모양으로 읽힌다(그 파일이
    /// 존재하는 이유이며, 한쪽만 손대면 다시 갈린다).</para>
    ///
    /// <para>시청각 피드백은 여기서 만들지 않는다 — 사용이 확정되면 규칙층이 이미 효과 이벤트를
    /// 올리고(<c>CombatState.BagItems</c>의 Heal·Block·상태이상·피해) 기존 VFX/SFX 파이프라인이 그것을
    /// 그린다. 확인창이 자기 소리를 따로 내면 같은 사건이 두 번 들린다.</para>
    /// </summary>
    public sealed class BagItemUseConfirmView : MonoBehaviour
    {
        public const string RootName = "Bag Item Confirm Overlay Root";

        /// <summary>사이드바·전리품 목록 위, 호버 설명창(1200) 아래. 확인 중에는 설명창이 떠 있어도 읽힌다.</summary>
        private const int SortingOrder = 1150;

        private const float BackdropAlpha = 0.62f;
        private static readonly Vector2 PanelSize = new Vector2(520f, 0f);
        private const float IconSize = 88f;

        private static readonly Color BackdropColor = new Color(0f, 0f, 0f, BackdropAlpha);
        private static readonly Color PanelColor = new Color(0.11f, 0.12f, 0.19f, 0.97f);
        private static readonly Color RimColor = new Color(0.898f, 0.769f, 0.404f, 0.55f);
        private static readonly Color TextColor = new Color(0.94f, 0.95f, 0.99f, 1f);
        private static readonly Color MutedColor = new Color(0.68f, 0.71f, 0.85f, 1f);
        private static readonly Color ConfirmColor = new Color(0.25f, 0.44f, 0.32f, 1f);
        private static readonly Color CancelColor = new Color(0.22f, 0.23f, 0.31f, 1f);

        private RectTransform panel;
        private Image iconImage;
        private TMP_Text nameText;
        private TMP_Text descriptionText;
        private Func<bool> onConfirm;

        public bool IsOpen => gameObject.activeSelf;

        public static BagItemUseConfirmView FindOrCreate(RectTransform gameplayLayers)
        {
            if (gameplayLayers == null)
            {
                return null;
            }

            foreach (var candidate in gameplayLayers.GetComponentsInChildren<BagItemUseConfirmView>(includeInactive: true))
            {
                if (candidate.name == RootName)
                {
                    return candidate;
                }
            }

            var created = new GameObject(RootName, typeof(RectTransform));
            var root = (RectTransform)created.transform;
            root.SetParent(gameplayLayers, worldPositionStays: false);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
            return created.AddComponent<BagItemUseConfirmView>();
        }

        /// <param name="confirm">「사용」을 눌렀을 때. 실제 사용을 집행하고 성공 여부를 돌려준다.</param>
        public void Show(ConsumableItemDefinition item, string fallbackName, Sprite icon, Func<bool> confirm)
        {
            onConfirm = confirm;

            // 🔴 <b>켜고 나서 짓는다</b> — UiProceduralPanel.Configure는 비활성 오브젝트에서 아무것도 하지
            //    않고 복구를 OnEnable에 기대는데 [ExecuteAlways]가 없어 에디트 모드에서는 그 복구가 없다
            //    (전리품 목록에서 밟은 것과 같은 함정).
            gameObject.SetActive(true);
            EnsureLayout();

            var displayName = item != null && !string.IsNullOrWhiteSpace(item.DisplayName)
                ? item.DisplayName
                : fallbackName;
            nameText.text = string.IsNullOrWhiteSpace(displayName) ? "알 수 없는 물건" : displayName;
            descriptionText.text = item != null && !string.IsNullOrWhiteSpace(item.Description)
                ? item.Description
                : "효과 정보 없음";

            iconImage.sprite = icon;
            iconImage.enabled = icon != null;
            iconImage.transform.parent.gameObject.SetActive(icon != null);

            transform.SetAsLastSibling();
        }

        public void Hide()
        {
            onConfirm = null;
            gameObject.SetActive(false);
        }

        private void OnConfirmClicked()
        {
            var confirm = onConfirm;
            Hide();
            confirm?.Invoke();
        }

        private void EnsureLayout()
        {
            if (panel != null)
            {
                return;
            }

            var rootRect = (RectTransform)transform;
            // 🔴🔴 가짜 null 때문에 `GetComponent ?? AddComponent`는 쓰지 않는다(명시적 null 검사).
            var canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
            }

            canvas.overrideSorting = true;
            canvas.sortingOrder = SortingOrder;
            if (gameObject.GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            // 뒷배경: 확인 중에 뒤를 눌러 다른 일이 일어나면 안 되므로 클릭을 먹는다.
            var backdropObject = new GameObject("Backdrop", typeof(RectTransform), typeof(Image), typeof(Button));
            var backdrop = (RectTransform)backdropObject.transform;
            backdrop.SetParent(rootRect, worldPositionStays: false);
            backdrop.anchorMin = Vector2.zero;
            backdrop.anchorMax = Vector2.one;
            backdrop.offsetMin = Vector2.zero;
            backdrop.offsetMax = Vector2.zero;
            backdropObject.GetComponent<Image>().color = BackdropColor;
            // 바깥을 누르면 취소 — 「되돌릴 수 있다」가 이 화면의 존재 이유라 가장 싼 취소도 열어 둔다.
            backdropObject.GetComponent<Button>().onClick.AddListener(Hide);

            panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter)).GetComponent<RectTransform>();
            panel.SetParent(rootRect, worldPositionStays: false);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = PanelSize;
            UiProceduralPanel.Attach(panel.GetComponent<Image>(), PanelColor, RimColor, 2f, 14f);

            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = CardConfirmPanelSpec.Padding;
            layout.spacing = CardConfirmPanelSpec.RowSpacing;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            // 🔴 세로는 내용이 정한다 — VerticalLayoutGroup 없이 Fitter만 붙이면 판이 0으로 접힌다(전례).
            var fitter = panel.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var iconHolder = new GameObject("Icon Holder", typeof(RectTransform), typeof(LayoutElement));
            var iconHolderRect = (RectTransform)iconHolder.transform;
            iconHolderRect.SetParent(panel, worldPositionStays: false);
            var iconElement = iconHolder.GetComponent<LayoutElement>();
            iconElement.preferredHeight = IconSize;
            var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            var iconRect = (RectTransform)iconObject.transform;
            iconRect.SetParent(iconHolderRect, worldPositionStays: false);
            iconRect.anchorMin = new Vector2(0.5f, 0.5f);
            iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(IconSize, IconSize);
            iconImage = iconObject.GetComponent<Image>();
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;

            nameText = CreateText("Name", CardConfirmPanelSpec.TitleFontSize, TextColor, FontStyles.Bold, 44f);
            descriptionText = CreateText("Description", CardConfirmPanelSpec.CaptionFontSize, MutedColor, FontStyles.Normal, 68f);
            descriptionText.textWrappingMode = TextWrappingModes.Normal;

            var buttonRow = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            var buttonRect = (RectTransform)buttonRow.transform;
            buttonRect.SetParent(panel, worldPositionStays: false);
            buttonRow.GetComponent<LayoutElement>().preferredHeight = CardConfirmPanelSpec.ButtonHeight;
            var buttonLayout = buttonRow.GetComponent<HorizontalLayoutGroup>();
            buttonLayout.spacing = CardConfirmPanelSpec.ButtonSpacing;
            buttonLayout.childAlignment = TextAnchor.MiddleCenter;
            buttonLayout.childControlWidth = false;
            buttonLayout.childControlHeight = false;
            buttonLayout.childForceExpandWidth = false;
            buttonLayout.childForceExpandHeight = false;

            // 취소가 왼쪽, 사용이 오른쪽 — 카드 제거·연마 확인과 같은 차례다.
            CreateButton(buttonRect, "취소", CancelColor, Hide);
            CreateButton(buttonRect, "사용", ConfirmColor, OnConfirmClicked);
        }

        private TMP_Text CreateText(string name, float fontSize, Color color, FontStyles style, float preferredHeight)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            var rect = (RectTransform)textObject.transform;
            rect.SetParent(panel, worldPositionStays: false);
            textObject.GetComponent<LayoutElement>().preferredHeight = preferredHeight;
            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.color = color;
            text.fontStyle = style;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            TooltipFontProvider.Apply(text);
            return text;
        }

        private void CreateButton(RectTransform parent, string label, Color color, Action onClick)
        {
            var buttonObject = new GameObject($"{label} Button", typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(LayoutElement));
            var rect = (RectTransform)buttonObject.transform;
            rect.SetParent(parent, worldPositionStays: false);
            rect.sizeDelta = new Vector2(CardConfirmPanelSpec.ButtonWidth, CardConfirmPanelSpec.ButtonHeight);
            var element = buttonObject.GetComponent<LayoutElement>();
            element.preferredWidth = CardConfirmPanelSpec.ButtonWidth;
            element.preferredHeight = CardConfirmPanelSpec.ButtonHeight;

            var image = buttonObject.GetComponent<Image>();
            UiProceduralPanel.Attach(image, color, RimColor, 1f, 8f);

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            var labelRect = (RectTransform)labelObject.transform;
            labelRect.SetParent(rect, worldPositionStays: false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var text = labelObject.GetComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = CardConfirmPanelSpec.ButtonFontSize;
            text.color = TextColor;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            TooltipFontProvider.Apply(text);

            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick?.Invoke());
        }
    }
}

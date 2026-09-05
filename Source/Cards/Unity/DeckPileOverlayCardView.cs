using System.Collections.Generic;
using System.Globalization;
using SeoulPlayup.Combat.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    [DisallowMultipleComponent]
    public sealed class DeckPileOverlayCardView : MonoBehaviour
    {
        private static readonly Color ModifiedCostColor = new Color(0.36f, 0.62f, 1f, 1f);
        private readonly Dictionary<TMP_Text, Color> originalCostColors = new Dictionary<TMP_Text, Color>();

        public void Bind(CombatCardSnapshot card)
        {
            Bind(card, null);
        }

        /// <param name="statusFrameSprite">
        /// 상태 카드 프레임. 목록 카드는 매번 새로 만들어지므로 원본 캐시가 필요 없다.
        /// </param>
        public void Bind(CombatCardSnapshot card, Sprite statusFrameSprite)
        {
            CardFrontPresentationFormatting.ApplyCardFrame(transform, card, statusFrameSprite);
            SetText("CardNameText_TMP", card.Name);
            SetText("DescriptionText_TMP", CardKeywordDecorator.Decorate(card.Description));
            SetText("CostText_TMP", card.Cost.ToString(CultureInfo.InvariantCulture));
            ApplyCostColor(card);
            SetText("TypeText_TMP", CardFrontPresentationFormatting.KindLabel(card));
            // 손패와 같은 규칙을 태운다 — 한쪽만 숨기면 "코스트는 있는데 사거리는 없는" 반쪽이 된다.
            CardFrontPresentationFormatting.RefreshCardCost(transform, card);
            CardFrontPresentationFormatting.RefreshCardRange(transform, card);
            KeywordHoverTooltipBinder.EnsureOnChild(transform, "DescriptionText_TMP");
            SetText("StatusText_TMP", string.Empty);
            SetIllustration(card.IllustrationId, card.Id);
        }

        private void ApplyCostColor(CombatCardSnapshot card)
        {
            var costText = FindText("CostText_TMP");
            if (costText == null)
            {
                return;
            }

            if (!originalCostColors.ContainsKey(costText))
            {
                originalCostColors[costText] = costText.color;
            }

            costText.color = card.IsCostModified ? ModifiedCostColor : originalCostColors[costText];
        }

        private void SetText(string childName, string value)
        {
            var text = FindText(childName);
            if (text != null)
            {
                text.text = value ?? string.Empty;
            }
        }

        private TMP_Text FindText(string childName)
        {
            foreach (var t in GetComponentsInChildren<TMP_Text>(includeInactive: true))
            {
                if (t.name == childName)
                {
                    return t;
                }
            }

            return null;
        }

        private void SetIllustration(string illustrationId, string cardId)
        {
            Image illustImage = null;
            foreach (var img in GetComponentsInChildren<Image>(includeInactive: true))
            {
                if (img.name == "Card_Illust")
                {
                    illustImage = img;
                    break;
                }
            }

            if (illustImage == null) return;

            var id = !string.IsNullOrWhiteSpace(illustrationId) ? illustrationId : $"card_illust_{cardId}";
            var sprite = CardIllustrationCatalog.ResolveDefault(id);
            if (sprite != null)
            {
                illustImage.sprite = sprite;
            }
        }
    }
}

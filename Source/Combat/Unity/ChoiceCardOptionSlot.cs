using System;
using System.Collections.Generic;
using System.Globalization;
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
    public sealed class ChoiceCardOptionSlot : MonoBehaviour
    {
        [SerializeField] private RectTransform cardRoot;

        [SerializeField] private GameObject actionCardFrontInstance;
        [SerializeField] private GameObject moveCardFrontInstance;

        // 상태 카드 프레임. 이 슬롯은 두 CardFront 인스턴스를 **재사용**하므로(EnsureCardFront가
        // 새로 만들지 않는다) 손패 레인과 같이 **원본 프레임 캐시가 반드시 필요하다** — 캐시 없이
        // 갈아 끼우면 다음에 앉는 일반 카드까지 상태 프레임을 쓰게 된다(스왑이 아니라 오염).
        [SerializeField] private Sprite statusCardFrameSprite;
        private readonly Dictionary<Image, Sprite> defaultFrameSpriteBySlotFrame = new Dictionary<Image, Sprite>();

        public void Bind(
            CombatCardSnapshot sourceCard,
            ChoiceCardOptionModel option,
            Action onClicked,
            bool enableCardHoverGlow = true,
            bool enablePointerLayerPromotion = true)
        {
            if (string.IsNullOrWhiteSpace(sourceCard.Id))
            {
                HideCardFronts();
                return;
            }

            gameObject.SetActive(true);
            var cardFront = EnsureCardFront(sourceCard.Kind);
            if (cardFront == null)
            {
                return;
            }

            CardFrontPresentationFormatting.ApplyCardFrame(
                cardFront.transform, sourceCard, ResolveStatusCardFrameSprite(), defaultFrameSpriteBySlotFrame);
            SetCardFrontText(cardFront, "CardNameText_TMP", option.DisplayName);
            SetCardFrontText(cardFront, "DescriptionText_TMP", CardKeywordDecorator.Decorate(option.CardText));
            KeywordHoverTooltipBinder.EnsureOnChild(cardFront.transform, "DescriptionText_TMP");
            SetCardFrontText(cardFront, "CostText_TMP", sourceCard.KiCost.ToString(CultureInfo.InvariantCulture));
            SetCardFrontText(cardFront, "TypeText_TMP", CardFrontPresentationFormatting.KindLabel(sourceCard));
            CardFrontPresentationFormatting.RefreshCardRange(cardFront.transform, CreateOptionSnapshot(sourceCard, option));
            SetCardFrontText(cardFront, "StatusText_TMP", string.Empty);
            SetCardIllustration(cardFront, sourceCard);

            var interaction = cardFront.GetComponentInChildren<HandCardInteraction>(includeInactive: true);
            if (interaction != null)
            {
                interaction.Initialize(null, (_, __) => onClicked?.Invoke());
                interaction.ConfigureCardHoverGlowEnabled(enableCardHoverGlow);
                interaction.ConfigurePointerLayerPromotion(enablePointerLayerPromotion);
                interaction.Configure(CreateOptionSnapshot(sourceCard, option), playable: true, selected: false);
            }
        }

        private Sprite ResolveStatusCardFrameSprite()
        {
            if (statusCardFrameSprite != null) return statusCardFrameSprite;
            // 빌드 안전 폴백(2026-08-19 #16 계열): Resources의 RuntimeUiAssetCatalog가 정본.
            var catalog = SeoulPlayup.Cards.Unity.RuntimeUiAssetCatalog.LoadDefault();
            if (catalog != null && catalog.StatusCardFrameSprite != null) return statusCardFrameSprite = catalog.StatusCardFrameSprite;
#if UNITY_EDITOR
            return statusCardFrameSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/Cards/card_frame_status.png");
#else
            return null;
#endif
        }

        private GameObject EnsureCardFront(CombatCardKind kind)
        {
            CacheAuthoredCardFronts();
            var requested = kind == CombatCardKind.Move ? moveCardFrontInstance : actionCardFrontInstance;
            var other = kind == CombatCardKind.Move ? actionCardFrontInstance : moveCardFrontInstance;

            if (other != null)
            {
                other.SetActive(false);
            }

            if (requested != null)
            {
                requested.SetActive(true);
                return requested;
            }

            Debug.LogWarning($"Choice option slot '{name}' cannot render because its authored CardFront instance is missing. Add CardFront_Action and CardFront_Move under this slot.", this);
            return null;
        }

        private void CacheAuthoredCardFronts()
        {
            if (actionCardFrontInstance != null && moveCardFrontInstance != null)
            {
                return;
            }

            var roots = (cardRoot != null ? cardRoot : transform).GetComponentsInChildren<Transform>(includeInactive: true);
            if (actionCardFrontInstance == null)
            {
                actionCardFrontInstance = roots
                    .Select(candidate => candidate.gameObject)
                    .FirstOrDefault(candidate => candidate != gameObject && candidate.name == "CardFront_Action");
            }

            if (moveCardFrontInstance == null)
            {
                moveCardFrontInstance = roots
                    .Select(candidate => candidate.gameObject)
                    .FirstOrDefault(candidate => candidate != gameObject && candidate.name == "CardFront_Move");
            }
        }

        private void HideCardFronts()
        {
            CacheAuthoredCardFronts();
            if (actionCardFrontInstance != null)
            {
                actionCardFrontInstance.SetActive(false);
            }

            if (moveCardFrontInstance != null)
            {
                moveCardFrontInstance.SetActive(false);
            }
        }

        private static CombatCardSnapshot CreateOptionSnapshot(CombatCardSnapshot sourceCard, ChoiceCardOptionModel option)
        {
            var targetMode = option.TargetMode == CardTargetMode.None ? sourceCard.TargetMode : option.TargetMode;
            var range = UsesRangeIcon(targetMode) ? sourceCard.Range : 0;
            return new CombatCardSnapshot(
                sourceCard.Id,
                sourceCard.Kind,
                option.DisplayName,
                option.CardText,
                sourceCard.Value,
                isUsable: true,
                isDiscarded: false,
                string.Empty,
                sourceCard.KiCost,
                range,
                sourceCard.Pile,
                sourceCard.CatalogSourceId,
                sourceCard.EffectRef,
                sourceCard.PhaseAvailability,
                sourceCard.PlayMode,
                sourceCard.FieldObjectKind,
                sourceCard.DurationTurns,
                sourceCard.AreaRadius,
                sourceCard.InstanceId,
                sourceCard.UpgradeLevel,
                sourceCard.IsTemporary,
                sourceCard.ChoiceOptions,
                sourceCard.ChoiceOptionTexts,
                sourceCard.IllustrationId,
                baseCost: sourceCard.BaseCost,
                targetMode: targetMode,
                isStatusCard: sourceCard.IsStatusCard);
        }

        private static bool UsesRangeIcon(CardTargetMode targetMode)
        {
            return targetMode == CardTargetMode.Enemy
                || targetMode == CardTargetMode.Tile
                || targetMode == CardTargetMode.SelfOrEnemy
                || targetMode == CardTargetMode.RandomReachable
                || targetMode == CardTargetMode.OptionThenTarget
                || targetMode == CardTargetMode.SelfArea;
        }

        private static void ConfigureCardFrontRect(RectTransform rect)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        private static bool SetCardFrontText(GameObject cardFront, string objectName, string value)
        {
            var text = cardFront.GetComponentsInChildren<TMP_Text>(includeInactive: true)
                .FirstOrDefault(candidate => candidate != null && candidate.name == objectName);
            if (text == null)
            {
                return false;
            }

            KoreanFontProvider.Apply(text);
            text.text = value ?? string.Empty;
            return true;
        }

        private static void SetCardIllustration(GameObject cardFront, CombatCardSnapshot sourceCard)
        {
            var image = cardFront.GetComponentsInChildren<Image>(includeInactive: true)
                .FirstOrDefault(candidate => candidate != null && candidate.name == "Card_Illust");
            if (image == null)
            {
                return;
            }

            var sprite = ResolveCardIllustration(sourceCard);
            if (sprite == null)
            {
                return;
            }

            image.sprite = sprite;
            image.color = Color.white;
        }

        private static Sprite ResolveCardIllustration(CombatCardSnapshot sourceCard)
        {
            var illustrationId = !string.IsNullOrWhiteSpace(sourceCard.IllustrationId)
                ? sourceCard.IllustrationId
                : string.IsNullOrWhiteSpace(sourceCard.Id)
                    ? string.Empty
                    : $"card_illust_{sourceCard.Id}";

            if (!string.IsNullOrWhiteSpace(illustrationId))
            {
                return CardIllustrationCatalog.ResolveDefault(illustrationId);
            }

            return null;
        }

    }
}


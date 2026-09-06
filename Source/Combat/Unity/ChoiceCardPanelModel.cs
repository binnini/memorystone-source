using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Unity
{
    public readonly struct ChoiceCardOptionModel
    {
        public ChoiceCardOptionModel(string optionId, string displayName, string cardText, CardTargetMode targetMode)
        {
            OptionId = optionId ?? string.Empty;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? OptionId : displayName;
            CardText = cardText ?? string.Empty;
            TargetMode = targetMode;
        }

        public string OptionId { get; }
        public string DisplayName { get; }
        public string CardText { get; }
        public CardTargetMode TargetMode { get; }
    }

    public sealed class ChoiceCardPanelModel
    {
        public ChoiceCardPanelModel(string sourceCardId, IEnumerable<ChoiceCardOptionModel> options)
            : this(default, sourceCardId, sourceCardId, options)
        {
        }

        public ChoiceCardPanelModel(string sourceCardId, string sourceCardKey, IEnumerable<ChoiceCardOptionModel> options)
            : this(default, sourceCardId, sourceCardKey, options)
        {
        }

        public ChoiceCardPanelModel(CombatCardSnapshot sourceCard, string sourceCardId, string sourceCardKey, IEnumerable<ChoiceCardOptionModel> options)
        {
            SourceCard = sourceCard;
            SourceCardId = sourceCardId ?? string.Empty;
            SourceCardKey = string.IsNullOrWhiteSpace(sourceCardKey) ? SourceCardId : sourceCardKey;
            Options = (options ?? Enumerable.Empty<ChoiceCardOptionModel>()).ToArray();
        }

        public CombatCardSnapshot SourceCard { get; }
        public string SourceCardId { get; }
        public string SourceCardKey { get; }
        public IReadOnlyList<ChoiceCardOptionModel> Options { get; }

        public static ChoiceCardPanelModel ForCard(CombatCardSnapshot card)
        {
            var metadataOptions = ParseChoiceOptions(card).ToArray();
            if (metadataOptions.Length > 0)
            {
                return new ChoiceCardPanelModel(card, card.Id, card.SelectionKey, metadataOptions);
            }

            if (card.Id == SeoulPlayup.Combat.Runtime.Cards.CardIds.HolyLight)
            {
                return new ChoiceCardPanelModel(card, card.Id, card.SelectionKey, new[]
                {
                    new ChoiceCardOptionModel("heal", "회복", CardKeywordDecorator.DecorateForCard($"기력 {card.KiCost}: 자신을 {card.HealValue} 회복합니다.", card.Id), CardTargetMode.Self),
                    new ChoiceCardOptionModel("attack", "공격", CardKeywordDecorator.DecorateForCard($"기력 {card.KiCost}: 적에게 {card.Value} 피해를 줍니다.", card.Id), CardTargetMode.Enemy)
                });
            }

            return new ChoiceCardPanelModel(card, card.Id, card.SelectionKey, Array.Empty<ChoiceCardOptionModel>());
        }

        private static IEnumerable<ChoiceCardOptionModel> ParseChoiceOptions(CombatCardSnapshot card)
        {
            // 선택지 규칙은 카드 클래스(CardBehavior.Choices)가, 문안은 데이터(choiceOptionTexts)가 든다.
            var options = SeoulPlayup.Combat.Runtime.Cards.CardBehaviorRegistry.TryGet(card.Id, out var behavior)
                ? behavior.Choices
                : System.Array.Empty<CardBehaviorMetadata.ChoiceOption>();
            if (options.Count == 0)
            {
                yield break;
            }

            var textByOption = CardBehaviorMetadata.ParseChoiceOptionTexts(card.ChoiceOptionTexts)
                .ToDictionary(text => text.OptionId, StringComparer.OrdinalIgnoreCase);
            foreach (var option in options)
            {
                var optionId = option.OptionId;
                if (string.IsNullOrWhiteSpace(optionId))
                {
                    continue;
                }

                var targetMode = ResolveTargetMode(option.Target);
                textByOption.TryGetValue(optionId, out var optionText);
                yield return new ChoiceCardOptionModel(
                    optionId,
                    string.IsNullOrWhiteSpace(optionText.DisplayName) ? ResolveDisplayName(optionId) : optionText.DisplayName,
                    string.IsNullOrWhiteSpace(optionText.CardText) ? ResolveCardText(card, optionId, targetMode) : ResolveTemplateText(card, optionText.CardText),
                    targetMode);
            }
        }

        private static string ResolveTemplateText(CombatCardSnapshot card, string template)
        {
            var cost = card.KiCost.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var range = card.Range.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var value = card.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var healValue = card.HealValue.ToString(System.Globalization.CultureInfo.InvariantCulture);

            // Same resolver as the card catalog so a trailing 조사 agrees with the substituted number.
            var resolved = KoreanParticle.ResolveTokens(template ?? string.Empty, token =>
            {
                switch (token)
                {
                    case "Cost":
                    case "KiCost":
                        return cost;
                    case "Range":
                        return range;
                    case "Damage":
                    case "Amount":
                        return value;
                    case "Heal":
                        // I-08(WS-I): 회복 토큰은 heal 축으로 — damage와 heal을 동시에 저작한 카드(A03)에서 갈린다.
                        return healValue;
                    default:
                        return null;
                }
            });
            return CardKeywordDecorator.DecorateForCard(resolved, card.Id);
        }

        private static CardTargetMode ResolveTargetMode(string targetToken)
        {
            switch ((targetToken ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "self":
                    return CardTargetMode.Self;
                case "tile":
                    return CardTargetMode.Tile;
                case "enemy":
                default:
                    return CardTargetMode.Enemy;
            }
        }

        private static string ResolveDisplayName(string optionId)
        {
            switch ((optionId ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "heal":
                    return "회복";
                case "attack":
                    return "공격";
                default:
                    return optionId;
            }
        }

        private static string ResolveCardText(CombatCardSnapshot card, string optionId, CardTargetMode targetMode)
        {
            switch ((optionId ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "heal":
                    return CardKeywordDecorator.DecorateForCard($"기력 {card.KiCost}: 자신을 {card.HealValue} 회복합니다.", card.Id);
                case "attack":
                    return CardKeywordDecorator.DecorateForCard($"기력 {card.KiCost}: 적에게 {card.Value} 피해를 줍니다.", card.Id);
                default:
                    return CardKeywordDecorator.DecorateForCard(targetMode == CardTargetMode.Self
                        ? $"기력 {card.KiCost}: 자신에게 효과를 적용합니다."
                        : $"기력 {card.KiCost}: 대상을 선택해 효과를 적용합니다.", card.Id);
            }
        }
    }
}

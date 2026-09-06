using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SeoulPlayup.CardCore
{
    public static class CardBehaviorMetadata
    {
        public const string AdditionalCostDiscardSelectedHandCards = "DiscardSelectedHandCards";
        public const string AdditionalCostExileSelectedHandCards = "ExileSelectedHandCards";
        public const string PostActionApplyMark = "ApplyMark";
        public const string PostActionApplyImmobilize = "ApplyImmobilize";
        public const string PostActionInjectCopy = "InjectCopy";
        // A12 전염병: spread the struck target's debuffs to its neighbours. Payload is the hex radius.
        public const string PostActionSpreadStatus = "SpreadStatus";
        public const string ChoiceEffectHealPlayer = "heal.player";
        public const string ChoiceEffectAttackDamage = "attack.damage";
        // U02 부적 끌어오기: the choice vocabulary grew past "heal or hit" for the first time.
        public const string ChoiceEffectDrawActionCards = "draw.action_cards";
        public const string ChoiceEffectRecoverExiledCard = "recover.exiled_card";
        public const string ChoiceTargetSelf = "self";

        public readonly struct ChoiceOption
        {
            public ChoiceOption(string optionId, string effectRef, string target)
            {
                OptionId = optionId ?? string.Empty;
                EffectRef = effectRef ?? string.Empty;
                Target = target ?? string.Empty;
            }

            public string OptionId { get; }
            public string EffectRef { get; }
            public string Target { get; }
        }

        public readonly struct ChoiceOptionText
        {
            public ChoiceOptionText(string optionId, string displayName, string cardText)
            {
                OptionId = optionId ?? string.Empty;
                DisplayName = displayName ?? string.Empty;
                CardText = cardText ?? string.Empty;
            }

            public string OptionId { get; }
            public string DisplayName { get; }
            public string CardText { get; }
        }

        /// <summary>One `kind:amount` item from the `stateEffect` / `buff_debuff` columns.</summary>
        public readonly struct EffectAmount
        {
            public EffectAmount(string kind, int amount)
            {
                Kind = kind ?? string.Empty;
                Amount = amount;
            }

            public string Kind { get; }
            public int Amount { get; }
        }

        public readonly struct PostAction
        {
            public PostAction(string actionId, string payload)
            {
                ActionId = actionId ?? string.Empty;
                Payload = payload ?? string.Empty;
            }

            public string ActionId { get; }
            public string Payload { get; }
        }

        public static IReadOnlyList<ChoiceOptionText> ParseChoiceOptionTexts(string rawList)
        {
            var texts = new List<ChoiceOptionText>();
            foreach (var item in SplitTokens(rawList))
            {
                var parts = item.Split(new[] { '|' }, 3);
                if (parts.Length != 3)
                {
                    continue;
                }

                var optionId = parts[0].Trim();
                if (string.IsNullOrEmpty(optionId))
                {
                    continue;
                }

                texts.Add(new ChoiceOptionText(optionId, parts[1].Trim(), parts[2].Trim()));
            }

            return texts;
        }

        public static bool ValidateChoiceOptionTexts(string rawList)
        {
            var optionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in SplitTokens(rawList))
            {
                var parts = item.Split(new[] { '|' }, 3);
                if (parts.Length != 3
                    || string.IsNullOrWhiteSpace(parts[0])
                    || string.IsNullOrWhiteSpace(parts[1])
                    || string.IsNullOrWhiteSpace(parts[2])
                    || !optionIds.Add(parts[0].Trim()))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Parses a `stateEffect` / `buff_debuff` list — `kind:amount` items separated by `;`
        /// (e.g. `Agility:2`, `Reflect:50`). The per-status duration comes from the card's `duration`
        /// column, not from a `@n` suffix: that suffix appears in old schema notes but was never
        /// implemented, and accepting it silently would recreate the very drift these columns had.
        /// </summary>
        public static IReadOnlyList<EffectAmount> ParseEffectList(string rawList)
        {
            var effects = new List<EffectAmount>();
            foreach (var item in SplitTokens(rawList))
            {
                var separator = item.IndexOf(':');
                if (separator <= 0)
                {
                    continue;
                }

                var kind = item.Substring(0, separator).Trim();
                if (kind.Length > 0
                    && int.TryParse(
                        item.Substring(separator + 1).Trim(),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var amount))
                {
                    effects.Add(new EffectAmount(kind, amount));
                }
            }

            return effects;
        }

        /// <summary>Reads one authored effect magnitude, returning <paramref name="fallback"/> when absent.</summary>
        public static int GetEffectAmount(string rawList, string kind, int fallback)
        {
            foreach (var effect in ParseEffectList(rawList))
            {
                if (string.Equals(effect.Kind, kind, StringComparison.OrdinalIgnoreCase))
                {
                    return effect.Amount;
                }
            }

            return fallback;
        }

        /// <summary>Format gate for the effect-list columns: unique kinds, integer amounts, no `@` suffix.</summary>
        public static bool ValidateEffectListFormat(string rawList)
        {
            var kinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in SplitTokens(rawList))
            {
                var separator = item.IndexOf(':');
                if (separator <= 0)
                {
                    return false;
                }

                var kind = item.Substring(0, separator).Trim();
                var amount = item.Substring(separator + 1).Trim();
                if (kind.Length == 0
                    || !int.TryParse(amount, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                    || !kinds.Add(kind))
                {
                    return false;
                }
            }

            return true;
        }

        private static IEnumerable<string> SplitTokens(string rawList)
        {
            return string.IsNullOrWhiteSpace(rawList)
                ? Enumerable.Empty<string>()
                : rawList.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(item => item.Trim())
                    .Where(item => item.Length > 0);
        }
    }
}

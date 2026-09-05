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

        public static bool HasToken(string rawList, string token)
        {
            if (string.IsNullOrWhiteSpace(rawList) || string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            return SplitTokens(rawList).Any(item =>
            {
                var tokenEnd = item.IndexOf(':');
                var head = tokenEnd < 0 ? item : item.Substring(0, tokenEnd);
                return string.Equals(head.Trim(), token, StringComparison.OrdinalIgnoreCase);
            });
        }

        public static IReadOnlyList<ChoiceOption> ParseChoiceOptions(string rawList)
        {
            var options = new List<ChoiceOption>();
            foreach (var item in SplitTokens(rawList))
            {
                var parts = item.Split(':');
                if (parts.Length != 3)
                {
                    continue;
                }

                var optionId = parts[0].Trim();
                var effectRef = parts[1].Trim();
                var target = parts[2].Trim();
                if (string.IsNullOrEmpty(optionId) || string.IsNullOrEmpty(effectRef) || string.IsNullOrEmpty(target))
                {
                    continue;
                }

                options.Add(new ChoiceOption(optionId, effectRef, target));
            }

            return options;
        }

        /// <summary>
        /// True when a 갈림길(choice) card offers at least one self-targeted branch (e.g. 성스러운 빛 = 회복 or 공격).
        /// Such a card is always playable (the self branch needs no enemy in range) and the hand hides its range
        /// icon — the per-branch range is shown only in the choice overlay.
        /// </summary>
        public static bool HasSelfTargetedChoiceOption(string rawList)
        {
            foreach (var option in ParseChoiceOptions(rawList))
            {
                if (string.Equals(option.Target, ChoiceTargetSelf, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool TryGetChoiceOption(string rawList, string optionId, out ChoiceOption option)
        {
            option = default;
            if (string.IsNullOrWhiteSpace(optionId))
            {
                return false;
            }

            foreach (var candidate in ParseChoiceOptions(rawList))
            {
                if (string.Equals(candidate.OptionId, optionId, StringComparison.OrdinalIgnoreCase))
                {
                    option = candidate;
                    return true;
                }
            }

            return false;
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

        public static IReadOnlyList<PostAction> ParsePostActions(string rawList)
        {
            var actions = new List<PostAction>();
            foreach (var item in SplitTokens(rawList))
            {
                var payloadStart = item.IndexOf(':');
                if (payloadStart < 0)
                {
                    actions.Add(new PostAction(item.Trim(), string.Empty));
                    continue;
                }

                var actionId = item.Substring(0, payloadStart).Trim();
                var payload = item.Substring(payloadStart + 1).Trim();
                if (actionId.Length > 0)
                {
                    actions.Add(new PostAction(actionId, payload));
                }
            }

            return actions;
        }

        public static bool ValidateAdditionalCost(string rawList)
        {
            return SplitTokens(rawList).All(item =>
                string.Equals(item, AdditionalCostDiscardSelectedHandCards, StringComparison.Ordinal)
                || string.Equals(item, AdditionalCostExileSelectedHandCards, StringComparison.Ordinal));
        }

        public static bool ValidatePostActions(string rawList)
        {
            return SplitTokens(rawList).All(item =>
            {
                if (string.Equals(item, PostActionApplyMark, StringComparison.Ordinal))
                {
                    return true;
                }

                // ApplyImmobilize:<turns> — payload must be a positive turn count.
                if (item.StartsWith(PostActionApplyImmobilize + ":", StringComparison.Ordinal))
                {
                    return int.TryParse(item.Substring(PostActionApplyImmobilize.Length + 1).Trim(), out var turns)
                        && turns > 0;
                }

                // SpreadStatus:<radius> — payload must be a positive hex radius.
                if (item.StartsWith(PostActionSpreadStatus + ":", StringComparison.Ordinal))
                {
                    return int.TryParse(item.Substring(PostActionSpreadStatus.Length + 1).Trim(), out var radius)
                        && radius > 0;
                }

                return item.StartsWith(PostActionInjectCopy + ":", StringComparison.Ordinal)
                    && item.Length > PostActionInjectCopy.Length + 1;
            });
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

        /// <summary>
        /// behaviorParams is a generic <c>키:정수</c> scalar bag shared by every behaviorId, so a behavior that
        /// needs one one-off number (임계값, 드로우 장수 …) costs a token instead of a whole CSV column.
        /// Format only: keys must be unique and values must parse as integers. Which keys a given behaviorId
        /// actually accepts is allow-listed by the catalog importer — an unrecognized key is a typo, and a typo
        /// that imports silently is the failure mode this column exists to avoid.
        /// </summary>
        public static bool ValidateBehaviorParams(string rawList)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in SplitTokens(rawList))
            {
                var separator = item.IndexOf(':');
                if (separator <= 0)
                {
                    return false;
                }

                var key = item.Substring(0, separator).Trim();
                var value = item.Substring(separator + 1).Trim();
                if (key.Length == 0
                    || !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                    || !keys.Add(key))
                {
                    return false;
                }
            }

            return true;
        }

        public static IReadOnlyList<string> ParseBehaviorParamKeys(string rawList)
        {
            var keys = new List<string>();
            foreach (var item in SplitTokens(rawList))
            {
                var separator = item.IndexOf(':');
                if (separator > 0)
                {
                    keys.Add(item.Substring(0, separator).Trim());
                }
            }

            return keys;
        }

        /// <summary>Reads a behaviorParams scalar, returning <paramref name="fallback"/> when absent.</summary>
        public static int GetBehaviorParam(string rawList, string key, int fallback)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return fallback;
            }

            foreach (var item in SplitTokens(rawList))
            {
                var separator = item.IndexOf(':');
                if (separator <= 0 || !string.Equals(item.Substring(0, separator).Trim(), key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (int.TryParse(
                        item.Substring(separator + 1).Trim(),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var value))
                {
                    return value;
                }
            }

            return fallback;
        }

        public static bool ValidateChoiceOptions(string rawList)
        {
            var tokens = SplitTokens(rawList).ToArray();
            if (tokens.Length == 0)
            {
                return true;
            }

            var optionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in tokens)
            {
                var parts = item.Split(':');
                if (parts.Length != 3)
                {
                    return false;
                }

                var optionId = parts[0].Trim();
                var effectRef = parts[1].Trim();
                var target = parts[2].Trim();
                if (string.IsNullOrEmpty(optionId)
                    || string.IsNullOrEmpty(effectRef)
                    || !IsKnownChoiceEffect(effectRef)
                    || !IsKnownChoiceTarget(target)
                    || !optionIds.Add(optionId))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsKnownChoiceEffect(string effectRef)
        {
            return string.Equals(effectRef, ChoiceEffectHealPlayer, StringComparison.Ordinal)
                || string.Equals(effectRef, ChoiceEffectAttackDamage, StringComparison.Ordinal)
                || string.Equals(effectRef, ChoiceEffectDrawActionCards, StringComparison.Ordinal)
                || string.Equals(effectRef, ChoiceEffectRecoverExiledCard, StringComparison.Ordinal);
        }

        private static bool IsKnownChoiceTarget(string target)
        {
            return string.Equals(target, "self", StringComparison.OrdinalIgnoreCase)
                || string.Equals(target, "enemy", StringComparison.OrdinalIgnoreCase)
                || string.Equals(target, "tile", StringComparison.OrdinalIgnoreCase);
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

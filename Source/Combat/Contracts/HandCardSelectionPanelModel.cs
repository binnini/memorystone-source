using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Unity
{
    public readonly struct HandCardSelectionPanelModel
    {
        public HandCardSelectionPanelModel(
            bool isActive,
            string sourceCardKey,
            string promptText,
            int minSelectCount,
            int maxSelectCount,
            IReadOnlyCollection<string> selectedCardKeys)
        {
            IsActive = isActive;
            SourceCardKey = sourceCardKey ?? string.Empty;
            PromptText = promptText ?? string.Empty;
            MinSelectCount = Math.Max(0, minSelectCount);
            MaxSelectCount = Math.Max(MinSelectCount, maxSelectCount);
            SelectedCardKeys = selectedCardKeys == null
                ? Array.Empty<string>()
                : selectedCardKeys.ToArray();
        }

        public bool IsActive { get; }
        public string SourceCardKey { get; }
        public string PromptText { get; }
        public int MinSelectCount { get; }
        public int MaxSelectCount { get; }
        public IReadOnlyList<string> SelectedCardKeys { get; }
        public int SelectedCount => SelectedCardKeys?.Count ?? 0;
        public bool CanConfirm => IsActive && SelectedCount >= MinSelectCount;

        public bool IsSelected(CombatCardSnapshot card)
        {
            if (SelectedCardKeys == null)
            {
                return false;
            }

            return SelectedCardKeys.Any(key => Matches(card, key));
        }

        public static bool Matches(CombatCardSnapshot card, string cardKey)
        {
            return !string.IsNullOrEmpty(cardKey)
                && (string.Equals(card.SelectionKey, cardKey, StringComparison.Ordinal)
                    || string.Equals(card.InstanceId, cardKey, StringComparison.Ordinal)
                    || string.Equals(card.Id, cardKey, StringComparison.Ordinal)
                    || string.Equals(card.CatalogSourceId, cardKey, StringComparison.Ordinal));
        }

        public static HandCardSelectionPanelModel Empty { get; } =
            new HandCardSelectionPanelModel(false, string.Empty, string.Empty, 0, 0, Array.Empty<string>());
    }
}

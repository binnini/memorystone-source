using System;
using System.Collections.Generic;
using System.Linq;

namespace SeoulPlayup.CardCore
{
    public readonly struct CardCatalogBindingEvidence
    {
        public CardCatalogBindingEvidence(
            string sourceId,
            string displayName,
            bool isValid,
            string failureReason,
            IEnumerable<string> moveDeckCardIds,
            IEnumerable<string> actionDeckCardIds)
        {
            SourceId = sourceId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            IsValid = isValid;
            FailureReason = failureReason ?? string.Empty;
            MoveDeckCardIds = (moveDeckCardIds ?? Array.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).ToArray();
            ActionDeckCardIds = (actionDeckCardIds ?? Array.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).ToArray();
        }

        public string SourceId { get; }
        public string DisplayName { get; }
        public bool IsValid { get; }
        public string FailureReason { get; }
        public IReadOnlyList<string> MoveDeckCardIds { get; }
        public IReadOnlyList<string> ActionDeckCardIds { get; }

        public string ToEvidenceText()
        {
            var status = IsValid ? "valid" : $"invalid: {FailureReason}";
            return $"Card catalog '{SourceId}' ({DisplayName}) is {status}. MoveDeck=[{string.Join(",", MoveDeckCardIds)}]; ActionDeck=[{string.Join(",", ActionDeckCardIds)}].";
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Unity
{
    public sealed class CardPresentationCatalog
    {
        private readonly Dictionary<string, CardPresentationRef> entries;

        public CardPresentationCatalog(IEnumerable<CardPresentationRef> entries)
        {
            this.entries = (entries ?? Enumerable.Empty<CardPresentationRef>())
                .Where(entry => !string.IsNullOrWhiteSpace(entry.PresentationId))
                .GroupBy(entry => entry.PresentationId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        }

        public CardPresentationRef Resolve(CardDefinition card)
        {
            if (card == null)
            {
                return CardPresentationRef.Placeholder();
            }

            if (!string.IsNullOrWhiteSpace(card.PresentationRef.PresentationId) &&
                entries.TryGetValue(card.PresentationRef.PresentationId, out var catalogRef))
            {
                return EnsureFallbacks(catalogRef);
            }

            return EnsureFallbacks(card.PresentationRef);
        }

        private static CardPresentationRef EnsureFallbacks(CardPresentationRef value)
        {
            var frameId = string.IsNullOrWhiteSpace(value.IllustrationId)
                ? CardPresentationRef.PlaceholderFrameId
                : value.FrameId;

            return new CardPresentationRef(
                value.PresentationId,
                frameId,
                value.IconId,
                value.IllustrationId,
                value.VfxId,
                value.SfxId);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace SeoulPlayup.CardCore
{
    public sealed class CardCatalogDefinition
    {
        private readonly List<CardCatalogEntry> entries;

        public CardCatalogDefinition(string sourceId, string displayName, IEnumerable<CardCatalogEntry> entries)
        {
            SourceId = string.IsNullOrWhiteSpace(sourceId) ? throw new ArgumentException("Card catalog source id is required.", nameof(sourceId)) : sourceId;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? SourceId : displayName;
            this.entries = entries == null
                ? new List<CardCatalogEntry>()
                : entries.Where(entry => entry != null).ToList();
        }

        public string SourceId { get; }
        public string DisplayName { get; }
        public IReadOnlyList<CardCatalogEntry> Entries => entries;

        public IReadOnlyList<CardDefinition> CreateDeck(CardCategory deckType)
        {
            return entries
                .Where(entry => entry.DeckType == deckType && entry.IncludeInGameplayDecks)
                .Select(entry => entry.ToCardDefinition(SourceId))
                .ToList();
        }

        public IReadOnlyList<CardCatalogEntry> GetVisibleCatalogEntries()
        {
            return entries
                .Where(entry => entry.VisibleInCatalog)
                .ToList();
        }

        public CardCatalogBindingEvidence CreateBindingEvidence()
        {
            var validation = Validate(out var reason);
            return new CardCatalogBindingEvidence(
                SourceId,
                DisplayName,
                validation,
                reason,
                entries.Where(entry => entry.DeckType == CardCategory.Movement && entry.IncludeInGameplayDecks).Select(entry => entry.Id).ToArray(),
                entries.Where(entry => entry.DeckType == CardCategory.Action && entry.IncludeInGameplayDecks).Select(entry => entry.Id).ToArray());
        }

        public bool Validate(out string reason)
        {
            if (entries.Count == 0)
            {
                reason = "Card catalog has no card definitions.";
                return false;
            }

            var duplicate = entries
                .GroupBy(entry => entry.Id)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicate != null)
            {
                reason = $"Card catalog has duplicate card id '{duplicate.Key}'.";
                return false;
            }

            if (!entries.Any(entry => entry.DeckType == CardCategory.Movement && entry.IncludeInGameplayDecks))
            {
                reason = "Card catalog must define at least one MoveDeck card.";
                return false;
            }

            if (!entries.Any(entry => entry.DeckType == CardCategory.Action && entry.IncludeInGameplayDecks))
            {
                reason = "Card catalog must define at least one ActionDeck card.";
                return false;
            }

            var unsupported = entries.FirstOrDefault(entry => !IsSupported(entry));
            if (unsupported != null)
            {
                reason = $"Card catalog entry '{unsupported.Id}' has unsupported deck/action binding.";
                return false;
            }

            var invalidFieldObject = entries.FirstOrDefault(entry => !IsValidFieldObject(entry, out _));
            if (invalidFieldObject != null)
            {
                IsValidFieldObject(invalidFieldObject, out reason);
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static bool IsSupported(CardCatalogEntry entry)
        {
            if (entry.DeckType == CardCategory.Movement)
            {
                return entry.ActionType == CardEffectType.Move;
            }

            return entry.ActionType == CardEffectType.Attack
                || entry.ActionType == CardEffectType.Defend
                || entry.ActionType == CardEffectType.Scout
                || entry.ActionType == CardEffectType.Investigate
                || entry.ActionType == CardEffectType.FieldObject
                || entry.ActionType == CardEffectType.Buff
                || entry.ActionType == CardEffectType.Utility
                // 상태 카드(C-17)는 행동 덱에 섞이지만 결코 사용되지 않는다. 여기 없으면 카탈로그 검증이
                // 통째로 실패해 상태 카드를 저작하는 순간 게임이 안 뜬다.
                || entry.ActionType == CardEffectType.Status;
        }

        private static bool IsValidFieldObject(CardCatalogEntry entry, out string reason)
        {
            reason = string.Empty;
            if (entry.ActionType != CardEffectType.FieldObject)
            {
                return true;
            }

            if (entry.FieldObjectKind == CardFieldObjectKind.None)
            {
                reason = $"Card catalog FieldObject entry '{entry.Id}' must define a field object kind.";
                return false;
            }

            if (entry.DurationTurns <= 0)
            {
                reason = $"Card catalog FieldObject entry '{entry.Id}' must define a positive duration.";
                return false;
            }

            if (entry.PlayMode == CardPlayMode.Self && entry.Range != 0)
            {
                reason = $"Card catalog self-play FieldObject entry '{entry.Id}' must use range 0.";
                return false;
            }

            return true;
        }
    }
}

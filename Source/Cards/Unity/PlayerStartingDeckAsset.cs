using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.CardCore;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    [CreateAssetMenu(fileName = "PlayerStartingDeck", menuName = "Seoul Playup/Cards/Player Starting Deck")]
    public sealed class PlayerStartingDeckAsset : ScriptableObject
    {
        [SerializeField] private string characterId;
        [SerializeField] private string characterDisplayName;
        [SerializeField] private string sourceCsvPath;
        [SerializeField] private List<PlayerStartingCardConfig> movementCards = new List<PlayerStartingCardConfig>();
        [SerializeField] private List<PlayerStartingCardConfig> actionCards = new List<PlayerStartingCardConfig>();

        public string CharacterId => characterId;
        public string CharacterDisplayName => characterDisplayName;
        public string SourceCsvPath => sourceCsvPath;
        public IReadOnlyList<PlayerStartingCardConfig> MovementCards => movementCards;
        public IReadOnlyList<PlayerStartingCardConfig> ActionCards => actionCards;

        public void SetSource(string id, string displayName, string csvPath)
        {
            characterId = id?.Trim() ?? string.Empty;
            characterDisplayName = displayName?.Trim() ?? string.Empty;
            sourceCsvPath = csvPath?.Trim() ?? string.Empty;
        }

        public void SetCards(IEnumerable<PlayerStartingCardConfig> movement, IEnumerable<PlayerStartingCardConfig> action)
        {
            movementCards = movement == null
                ? new List<PlayerStartingCardConfig>()
                : movement.Where(card => card != null).Select(card => card.Clone()).ToList();
            actionCards = action == null
                ? new List<PlayerStartingCardConfig>()
                : action.Where(card => card != null).Select(card => card.Clone()).ToList();
        }

        public PlayerDeckData ToPlayerDeckData(CardCatalogDefinition catalog, string instancePrefix = "start")
        {
            var movement = CreateInstances(catalog, CardCategory.Movement, movementCards, instancePrefix);
            var action = CreateInstances(catalog, CardCategory.Action, actionCards, instancePrefix);
            return new PlayerDeckData(movement, action);
        }

        public bool Validate(CardCatalogDefinition catalog, out string reason)
        {
            if (catalog == null)
            {
                reason = "Card catalog is required to validate a starting deck.";
                return false;
            }

            if (!ValidateCards(catalog, CardCategory.Movement, movementCards, out reason)
                || !ValidateCards(catalog, CardCategory.Action, actionCards, out reason))
            {
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static bool ValidateCards(
            CardCatalogDefinition catalog,
            CardCategory category,
            IEnumerable<PlayerStartingCardConfig> cards,
            out string reason)
        {
            foreach (var card in cards ?? Enumerable.Empty<PlayerStartingCardConfig>())
            {
                var cardId = card?.CardId?.Trim();
                if (string.IsNullOrEmpty(cardId))
                {
                    continue;
                }

                var exists = catalog.Entries.Any(entry =>
                    entry.IncludeInGameplayDecks &&
                    entry.DeckType == category &&
                    string.Equals(entry.Id, cardId, StringComparison.Ordinal));
                if (!exists)
                {
                    reason = $"Starting deck card '{cardId}' is not a gameplay {category} card in catalog '{catalog.SourceId}'.";
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }

        private static List<PlayerCardInstanceData> CreateInstances(
            CardCatalogDefinition catalog,
            CardCategory category,
            IEnumerable<PlayerStartingCardConfig> cards,
            string instancePrefix)
        {
            var result = new List<PlayerCardInstanceData>();
            var index = 0;
            foreach (var config in cards ?? Enumerable.Empty<PlayerStartingCardConfig>())
            {
                var cardId = config?.CardId?.Trim();
                if (string.IsNullOrEmpty(cardId))
                {
                    continue;
                }

                var instanceId = string.IsNullOrWhiteSpace(config.InstanceId)
                    ? $"{instancePrefix}.{category}.{cardId}.{index:D3}"
                    : config.InstanceId.Trim();
                var instance = new PlayerCardInstanceData(instanceId, cardId, config.UpgradeLevel, config.IsTemporary);
                if (PlayerDeckData.ResolveCard(catalog, category, instance) != null)
                {
                    result.Add(instance);
                }

                index++;
            }

            return result;
        }
    }

    [Serializable]
    public sealed class PlayerStartingCardConfig
    {
        [SerializeField] private string instanceId;
        [SerializeField] private string cardId;
        [SerializeField] private int upgradeLevel;
        [SerializeField] private bool isTemporary;

        public PlayerStartingCardConfig()
        {
        }

        public PlayerStartingCardConfig(string cardId, string instanceId = null, int upgradeLevel = 0, bool isTemporary = false)
        {
            this.instanceId = instanceId?.Trim() ?? string.Empty;
            this.cardId = cardId?.Trim() ?? string.Empty;
            this.upgradeLevel = upgradeLevel;
            this.isTemporary = isTemporary;
        }

        public string InstanceId => instanceId;
        public string CardId => cardId;
        public int UpgradeLevel => upgradeLevel;
        public bool IsTemporary => isTemporary;

        public PlayerStartingCardConfig Clone()
        {
            return new PlayerStartingCardConfig(cardId, instanceId, upgradeLevel, isTemporary);
        }
    }
}

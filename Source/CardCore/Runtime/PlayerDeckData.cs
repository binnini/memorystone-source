using System;
using System.Collections.Generic;
using System.Linq;

namespace SeoulPlayup.CardCore
{
    [Serializable]
    public sealed class PlayerCardInstanceData
    {
        public PlayerCardInstanceData(
            string instanceId,
            string cardId,
            int upgradeLevel = 0,
            bool isTemporary = false)
        {
            // 빈 id는 카드 id로 대신한다 — Guid를 뽑으면 「같은 시드 → 같은 판」이 깨진다
            // (seed-determinism P4). 고유 id가 필요한 발급부(덱 추가·런타임 생성)는 자기 카운터를 쓴다.
            InstanceId = string.IsNullOrWhiteSpace(instanceId)
                ? (cardId ?? string.Empty)
                : instanceId;
            CardId = cardId ?? string.Empty;
            UpgradeLevel = Math.Max(0, upgradeLevel);
            IsTemporary = isTemporary;
        }

        public string InstanceId { get; }
        public string CardId { get; }
        public int UpgradeLevel { get; }
        public bool IsTemporary { get; }
    }

    [Serializable]
    public sealed class PlayerDeckData
    {
        private readonly List<PlayerCardInstanceData> movementCards;
        private readonly List<PlayerCardInstanceData> actionCards;

        public PlayerDeckData(
            IEnumerable<PlayerCardInstanceData> movementCards = null,
            IEnumerable<PlayerCardInstanceData> actionCards = null)
        {
            this.movementCards = (movementCards ?? Enumerable.Empty<PlayerCardInstanceData>())
                .Where(card => card != null)
                .ToList();
            this.actionCards = (actionCards ?? Enumerable.Empty<PlayerCardInstanceData>())
                .Where(card => card != null)
                .ToList();
        }

        public IReadOnlyList<PlayerCardInstanceData> MovementCards => movementCards;
        public IReadOnlyList<PlayerCardInstanceData> ActionCards => actionCards;

        public static PlayerDeckData FromCatalog(CardCatalogDefinition catalog)
        {
            if (catalog == null)
            {
                return new PlayerDeckData();
            }

            return new PlayerDeckData(
                CreateDefaultInstances(catalog, CardCategory.Movement),
                CreateDefaultInstances(catalog, CardCategory.Action));
        }

        public IReadOnlyList<CardDefinition> CreateDeck(CardCatalogDefinition catalog, CardCategory category)
        {
            if (catalog == null)
            {
                return Array.Empty<CardDefinition>();
            }

            var instances = category == CardCategory.Movement ? movementCards : actionCards;
            return instances
                .Select(instance => ResolveInstance(catalog, category, instance))
                .Where(card => card != null)
                .ToList();
        }

        public static CardDefinition ResolveCard(
            CardCatalogDefinition catalog,
            CardCategory expectedCategory,
            PlayerCardInstanceData instance,
            bool requireGameplayDeckEntry = true)
        {
            return ResolveInstance(catalog, expectedCategory, instance, requireGameplayDeckEntry);
        }

        public PlayerDeckData AddCard(CardCatalogDefinition catalog, string cardId, string instanceId = null, int upgradeLevel = 0, bool isTemporary = false)
        {
            if (catalog == null || string.IsNullOrWhiteSpace(cardId))
            {
                return this;
            }

            var entry = catalog.Entries.FirstOrDefault(candidate =>
                candidate.IncludeInGameplayDecks &&
                string.Equals(candidate.Id, cardId, StringComparison.Ordinal));
            if (entry == null)
            {
                return this;
            }

            // id를 안 주면 덱 안에서 고유한 결정적 id를 만든다(같은 카드를 여러 장 넣어도 안 겹친다).
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                instanceId = CreateAddedInstanceId(catalog.SourceId, cardId);
            }

            var card = new PlayerCardInstanceData(instanceId, cardId, upgradeLevel, isTemporary);
            var nextMovement = movementCards.ToList();
            var nextAction = actionCards.ToList();
            if (entry.DeckType == CardCategory.Movement)
            {
                nextMovement.Add(card);
            }
            else
            {
                nextAction.Add(card);
            }

            return new PlayerDeckData(nextMovement, nextAction);
        }

        /// <summary>
        /// Returns a deck with the instance of <paramref name="instanceId"/> removed, or this same
        /// deck when no instance matches. Instance id (not card id) is the key on purpose: the deck
        /// may hold duplicates of one card, and a removal purchase targets exactly one copy.
        /// </summary>
        public PlayerDeckData RemoveCard(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                return this;
            }

            var nextMovement = movementCards
                .Where(card => !string.Equals(card.InstanceId, instanceId, StringComparison.Ordinal))
                .ToList();
            var nextAction = actionCards
                .Where(card => !string.Equals(card.InstanceId, instanceId, StringComparison.Ordinal))
                .ToList();
            if (nextMovement.Count == movementCards.Count && nextAction.Count == actionCards.Count)
            {
                return this;
            }

            return new PlayerDeckData(nextMovement, nextAction);
        }

        /// <summary>
        /// Returns a deck with the instance of <paramref name="instanceId"/> replaced at the same
        /// position with <paramref name="upgradeLevel"/>, or this same deck when no instance matches.
        /// 연마(카드 강화)의 영구 덱 반영 — instance id가 키인 이유는 <see cref="RemoveCard"/>와 같다.
        /// </summary>
        public PlayerDeckData UpgradeCard(string instanceId, int upgradeLevel)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                return this;
            }

            var replaced = false;
            List<PlayerCardInstanceData> Replace(List<PlayerCardInstanceData> cards) => cards
                .Select(card =>
                {
                    if (!string.Equals(card.InstanceId, instanceId, StringComparison.Ordinal))
                    {
                        return card;
                    }

                    replaced = true;
                    return new PlayerCardInstanceData(card.InstanceId, card.CardId, upgradeLevel, card.IsTemporary);
                })
                .ToList();

            var nextMovement = Replace(movementCards);
            var nextAction = Replace(actionCards);
            return replaced ? new PlayerDeckData(nextMovement, nextAction) : this;
        }

        private string CreateAddedInstanceId(string sourceId, string cardId)
        {
            var existing = new HashSet<string>(
                movementCards.Concat(actionCards).Select(card => card.InstanceId),
                StringComparer.Ordinal);
            for (var index = 0; ; index++)
            {
                var candidate = $"{sourceId}.{cardId}.add{index:D3}";
                if (!existing.Contains(candidate))
                {
                    return candidate;
                }
            }
        }

        private static IEnumerable<PlayerCardInstanceData> CreateDefaultInstances(CardCatalogDefinition catalog, CardCategory category)
        {
            var index = 0;
            foreach (var entry in catalog.Entries.Where(entry => entry.DeckType == category && entry.IncludeInGameplayDecks))
            {
                yield return new PlayerCardInstanceData(
                    $"{catalog.SourceId}.{entry.Id}.{index++:D3}",
                    entry.Id);
            }
        }

        private static CardDefinition ResolveInstance(
            CardCatalogDefinition catalog,
            CardCategory expectedCategory,
            PlayerCardInstanceData instance,
            bool requireGameplayDeckEntry = true)
        {
            if (instance == null || string.IsNullOrWhiteSpace(instance.CardId))
            {
                return null;
            }

            var entry = catalog.Entries.FirstOrDefault(candidate =>
                (!requireGameplayDeckEntry || candidate.IncludeInGameplayDecks) &&
                candidate.DeckType == expectedCategory &&
                string.Equals(candidate.Id, instance.CardId, StringComparison.Ordinal));
            return entry?.ToCardDefinition(
                catalog.SourceId,
                instance.InstanceId,
                instance.UpgradeLevel,
                instance.IsTemporary);
        }
    }
}

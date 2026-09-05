using System;
using System.Collections.Generic;
using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>Logical pile a card instance currently belongs to.</summary>
    public enum CardZone
    {
        Draw,
        Hand,
        Discard,
        Removed
    }

    /// <summary>
    /// A single card instance moving between zones across two snapshots. <see cref="From"/> is null
    /// when the card was not tracked in the previous snapshot (opening deal, or a card injected
    /// straight into the hand mid-combat).
    /// </summary>
    public readonly struct CardZoneTransition
    {
        public CardZoneTransition(string instanceId, CardZone? from, CardZone to)
        {
            InstanceId = instanceId ?? string.Empty;
            From = from;
            To = to;
        }

        public string InstanceId { get; }
        public CardZone? From { get; }
        public CardZone To { get; }

        /// <summary>Draw pile → hand: a normal turn-start or effect-driven draw.</summary>
        public bool IsDraw => From == CardZone.Draw && To == CardZone.Hand;

        /// <summary>First time a card is seen, landing directly in hand (opening deal, or a card injected into hand).</summary>
        public bool IsInitialDeal => From == null && To == CardZone.Hand;

        /// <summary>Hand → discard: turn-end cleanup, card play, or cost.</summary>
        public bool IsDiscard => From == CardZone.Hand && To == CardZone.Discard;

        /// <summary>Hand → draw: an effect returning the hand to the deck.</summary>
        public bool IsExile => From == CardZone.Hand && To == CardZone.Removed;

        public bool IsReturnToDraw => From == CardZone.Hand && To == CardZone.Draw;

        /// <summary>Should be presented as a card flying from the draw pile into the hand.</summary>
        public bool PlaysAsDrawFlight => IsDraw || IsInitialDeal;
    }

    /// <summary>Result of a single <see cref="CardZoneDiff.Compute"/> pass.</summary>
    public sealed class CardZoneDiffResult
    {
        public static readonly CardZoneDiffResult Empty =
            new CardZoneDiffResult(Array.Empty<CardZoneTransition>(), false);

        public CardZoneDiffResult(IReadOnlyList<CardZoneTransition> transitions, bool reshuffled)
        {
            Transitions = transitions ?? Array.Empty<CardZoneTransition>();
            Reshuffled = reshuffled;
        }

        public IReadOnlyList<CardZoneTransition> Transitions { get; }

        /// <summary>True when at least one card moved discard → draw, i.e. the discard pile was reshuffled back.</summary>
        public bool Reshuffled { get; }

        public bool HasAny => Transitions.Count > 0;
    }

    /// <summary>
    /// Pure diff between two card-zone snapshots keyed by stable instance id. Lets draw/discard flight
    /// presentation be driven without the runtime ever signalling "why" a card moved — turn-start draws,
    /// effect-driven draws/discards, costs, and reshuffles all reduce to the same zone transitions.
    /// No UnityEngine dependencies so it can be unit tested in EditMode.
    ///
    /// Only visually meaningful transitions are emitted:
    /// - any zone change for a previously tracked card (<see cref="CardZoneTransition.From"/> is set), and
    /// - a brand-new card that appears directly in the hand (opening deal / inject-to-hand).
    /// New cards that first appear in the draw/discard/removed piles are treated as silent registration
    /// (no flight), so the first snapshot does not animate the entire starting deck.
    /// </summary>
    public static class CardZoneDiff
    {
        public static CardZoneDiffResult Compute(
            IReadOnlyDictionary<string, CardZone> previous,
            IReadOnlyDictionary<string, CardZone> current)
        {
            if (current == null || current.Count == 0)
            {
                return CardZoneDiffResult.Empty;
            }

            var transitions = new List<CardZoneTransition>();
            var reshuffled = false;

            foreach (var entry in current)
            {
                var instanceId = entry.Key;
                var to = entry.Value;

                CardZone? from = null;
                if (previous != null && previous.TryGetValue(instanceId, out var prevZone))
                {
                    if (prevZone == to)
                    {
                        continue; // unchanged
                    }

                    from = prevZone;
                }
                else if (to != CardZone.Hand)
                {
                    // Brand-new card registered straight into a pile: silent, no flight.
                    continue;
                }

                transitions.Add(new CardZoneTransition(instanceId, from, to));
                if (from == CardZone.Discard && to == CardZone.Draw)
                {
                    reshuffled = true;
                }
            }

            return new CardZoneDiffResult(transitions, reshuffled);
        }
    }

    /// <summary>
    /// Builds a flat {instanceId -> <see cref="CardZone"/>} map across both the movement and action decks
    /// so the presentation layer can diff card movement between HUD refreshes. Pure; no UnityEngine deps.
    /// The two decks are merged because the draw/discard pile UI shows a single combined count.
    /// </summary>
    public static class CombatCardZoneSnapshot
    {
        public static Dictionary<string, CardZone> Build(CombatState state)
        {
            var map = new Dictionary<string, CardZone>(StringComparer.Ordinal);
            if (state == null)
            {
                return map;
            }

            AddDeck(map, state.MovementDeck);
            AddDeck(map, state.ActionDeck);
            return map;
        }

        private static void AddDeck(IDictionary<string, CardZone> map, DeckState<CardDefinition> deck)
        {
            if (deck == null)
            {
                return;
            }

            AddZone(map, deck.DrawPile, CardZone.Draw);
            AddZone(map, deck.Hand, CardZone.Hand);
            AddZone(map, deck.DiscardPile, CardZone.Discard);
            AddZone(map, deck.RemovedPile, CardZone.Removed);
        }

        private static void AddZone(IDictionary<string, CardZone> map, IReadOnlyList<CardDefinition> cards, CardZone zone)
        {
            if (cards == null)
            {
                return;
            }

            for (var i = 0; i < cards.Count; i++)
            {
                var key = cards[i]?.InstanceId;
                if (!string.IsNullOrEmpty(key))
                {
                    map[key] = zone;
                }
            }
        }
    }
}

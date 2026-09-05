using System;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    public readonly struct PlayerStateSnapshot
    {
        public PlayerStateSnapshot(
            int hp,
            int maxHp,
            int block,
            bool isDead,
            HexCoord position,
            CombatPhase phase,
            int currentKi,
            int maxKi,
            PlayerDeckRuntimeSummary moveDeck,
            PlayerDeckRuntimeSummary actionDeck,
            bool objectiveCompleted,
            string objectiveStatusText,
            CombatCardKind? lastDiscardedCard,
            string lastFailureReason,
            string lastInvestigateResult,
            PlayerVisibilitySummary visibility)
        {
            Hp = hp;
            MaxHp = maxHp;
            Block = block;
            IsDead = isDead;
            Position = position;
            Phase = phase;
            CurrentKi = currentKi;
            MaxKi = maxKi;
            MoveDeck = moveDeck;
            ActionDeck = actionDeck;
            ObjectiveCompleted = objectiveCompleted;
            ObjectiveStatusText = objectiveStatusText ?? string.Empty;
            LastDiscardedCard = lastDiscardedCard;
            LastFailureReason = lastFailureReason ?? string.Empty;
            LastInvestigateResult = lastInvestigateResult ?? string.Empty;
            Visibility = visibility;
        }

        public int Hp { get; }
        public int MaxHp { get; }
        public int Block { get; }
        public bool IsDead { get; }
        public HexCoord Position { get; }
        public CombatPhase Phase { get; }
        public int CurrentKi { get; }
        public int MaxKi { get; }
        public PlayerDeckRuntimeSummary MoveDeck { get; }
        public PlayerDeckRuntimeSummary ActionDeck { get; }
        public bool ObjectiveCompleted { get; }
        public string ObjectiveStatusText { get; }
        public CombatCardKind? LastDiscardedCard { get; }
        public string LastFailureReason { get; }
        public string LastInvestigateResult { get; }
        public PlayerVisibilitySummary Visibility { get; }
    }

    public readonly struct PlayerDeckRuntimeSummary
    {
        public PlayerDeckRuntimeSummary(int drawCount, int handCount, int discardCount)
        {
            DrawCount = Math.Max(0, drawCount);
            HandCount = Math.Max(0, handCount);
            DiscardCount = Math.Max(0, discardCount);
        }

        public int DrawCount { get; }
        public int HandCount { get; }
        public int DiscardCount { get; }
    }

    public readonly struct PlayerVisibilitySummary
    {
        public PlayerVisibilitySummary(int unknownCount, int hintedCount, int revealedCount)
        {
            UnknownCount = Math.Max(0, unknownCount);
            HintedCount = Math.Max(0, hintedCount);
            RevealedCount = Math.Max(0, revealedCount);
        }

        public int UnknownCount { get; }
        public int HintedCount { get; }
        public int RevealedCount { get; }
        public int TotalCount => UnknownCount + HintedCount + RevealedCount;
    }
}

using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Unity
{
    public enum CombatSelectionMode
    {
        None,
        Move,
        Attack,
        Scout,
        Investigate,
        FieldObject
    }

    public readonly struct CombatSelectionState
    {
        private readonly string cardId;
        private readonly string cardInstanceId;
        private readonly string cardName;

        private CombatSelectionState(CombatSelectionMode mode, string cardId, string cardInstanceId, string cardName)
        {
            Mode = mode;
            this.cardId = cardId ?? string.Empty;
            this.cardInstanceId = cardInstanceId ?? string.Empty;
            this.cardName = cardName ?? string.Empty;
        }

        public CombatSelectionMode Mode { get; }
        public string CardId => cardId ?? string.Empty;
        public string CardInstanceId => cardInstanceId ?? string.Empty;
        public string CardKey => string.IsNullOrEmpty(CardInstanceId) ? CardId : CardInstanceId;
        public string CardName => cardName ?? string.Empty;
        public bool IsMove => Mode == CombatSelectionMode.Move;
        public bool HasTargetCard => TargetCardKind.HasValue;

        public CombatCardKind? TargetCardKind
        {
            get
            {
                switch (Mode)
                {
                    case CombatSelectionMode.Attack:
                        return CombatCardKind.Attack;
                    case CombatSelectionMode.Scout:
                        return CombatCardKind.Scout;
                    case CombatSelectionMode.Investigate:
                        return CombatCardKind.Investigate;
                    case CombatSelectionMode.FieldObject:
                        return CombatCardKind.FieldObject;
                    default:
                        return null;
                }
            }
        }

        public string TargetCardId => HasTargetCard ? CardId : string.Empty;
        public string TargetCardKey => HasTargetCard ? CardKey : string.Empty;

        public static CombatSelectionState None => default;

        public static CombatSelectionState Move(string cardId, string cardName)
        {
            return Move(cardId, string.Empty, cardName);
        }

        public static CombatSelectionState Move(string cardId, string cardInstanceId, string cardName)
        {
            return new CombatSelectionState(CombatSelectionMode.Move, cardId, cardInstanceId, string.IsNullOrEmpty(cardName) ? "Move" : cardName);
        }

        public static CombatSelectionState Target(CombatCardKind kind, string cardId, string cardName)
        {
            return Target(kind, cardId, string.Empty, cardName);
        }

        public static CombatSelectionState Target(CombatCardKind kind, string cardId, string cardInstanceId, string cardName)
        {
            return new CombatSelectionState(ToMode(kind), cardId, cardInstanceId, string.IsNullOrEmpty(cardName) ? kind.ToString() : cardName);
        }

        private static CombatSelectionMode ToMode(CombatCardKind kind)
        {
            switch (kind)
            {
                case CombatCardKind.Attack:
                    return CombatSelectionMode.Attack;
                case CombatCardKind.Scout:
                    return CombatSelectionMode.Scout;
                case CombatCardKind.Investigate:
                    return CombatSelectionMode.Investigate;
                case CombatCardKind.FieldObject:
                    return CombatSelectionMode.FieldObject;
                default:
                    return CombatSelectionMode.None;
            }
        }
    }
}

using System;

namespace SeoulPlayup.Combat.Runtime
{
    public readonly struct EffectDefinition : IEquatable<EffectDefinition>
    {
        private readonly string id;
        private readonly string sourceRef;

        public EffectDefinition(
            string id,
            EffectType type,
            EffectKind kind,
            int amount = 0,
            int radius = 0,
            int durationTurns = 0,
            string sourceRef = "")
        {
            this.id = id ?? string.Empty;
            Type = type;
            Kind = kind;
            StatusKind = null;
            Amount = Math.Max(0, amount);
            Radius = Math.Max(0, radius);
            DurationTurns = Math.Max(0, durationTurns);
            this.sourceRef = sourceRef ?? string.Empty;
        }

        public EffectDefinition(
            string id,
            EffectType type,
            StatusEffectKind statusKind,
            int amount = 0,
            int radius = 0,
            int durationTurns = 0,
            string sourceRef = "")
        {
            this.id = id ?? string.Empty;
            Type = type;
            Kind = EffectKind.StatusEffectApplied;
            StatusKind = statusKind;
            Amount = Math.Max(0, amount);
            Radius = Math.Max(0, radius);
            DurationTurns = Math.Max(0, durationTurns);
            this.sourceRef = sourceRef ?? string.Empty;
        }

        public string Id => id ?? string.Empty;
        public EffectType Type { get; }
        public EffectKind Kind { get; }
        public StatusEffectKind? StatusKind { get; }
        public int Amount { get; }
        public int Radius { get; }
        public int DurationTurns { get; }
        public string SourceRef => sourceRef ?? string.Empty;

        public bool Equals(EffectDefinition other)
        {
            return Id == other.Id
                && Type == other.Type
                && Kind == other.Kind
                && StatusKind == other.StatusKind
                && Amount == other.Amount
                && Radius == other.Radius
                && DurationTurns == other.DurationTurns
                && SourceRef == other.SourceRef;
        }

        public override bool Equals(object obj)
        {
            return obj is EffectDefinition other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Id.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)Type;
                hashCode = (hashCode * 397) ^ (int)Kind;
                hashCode = (hashCode * 397) ^ (StatusKind.HasValue ? (int)StatusKind.Value : -1);
                hashCode = (hashCode * 397) ^ Amount;
                hashCode = (hashCode * 397) ^ Radius;
                hashCode = (hashCode * 397) ^ DurationTurns;
                hashCode = (hashCode * 397) ^ SourceRef.GetHashCode();
                return hashCode;
            }
        }

        public static bool operator ==(EffectDefinition left, EffectDefinition right) => left.Equals(right);
        public static bool operator !=(EffectDefinition left, EffectDefinition right) => !left.Equals(right);
    }
}

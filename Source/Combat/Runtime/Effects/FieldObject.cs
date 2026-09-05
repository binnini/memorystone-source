using System;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    public readonly struct FieldObject : IEquatable<FieldObject>
    {
        public FieldObject(HexCoord position, int radius, int remainingTurns, FieldObjectKind kind, int value = 0, string sourceUnitId = "player", string visualRef = "", int hitsPerTick = 1, StatusEffectKind statusKind = default)
        {
            StatusKind = statusKind;
            Position = position;
            Radius = Math.Max(0, radius);
            RemainingTurns = Math.Max(0, remainingTurns);
            Kind = kind;
            Value = Math.Max(0, value);
            SourceUnitId = string.IsNullOrWhiteSpace(sourceUnitId) ? "field" : sourceUnitId;
            VisualRef = string.IsNullOrWhiteSpace(visualRef) ? string.Empty : visualRef.Trim();
            HitsPerTick = Math.Max(1, hitsPerTick);
        }

        public HexCoord Position { get; }
        public int Radius { get; }
        public int RemainingTurns { get; }
        public FieldObjectKind Kind { get; }
        public int Value { get; }
        public string SourceUnitId { get; }

        /// <summary>
        /// Stable visual key (typically the originating card id, e.g. "F02"). The Unity layer maps this to
        /// a dedicated prefab (신성한 램프 → Lantern) and falls back to a Kind-coloured marker when empty.
        /// </summary>
        public string VisualRef { get; }

        /// <summary>
        /// How many separate hits one tick delivers, each for <see cref="Value"/> (콩콩탄탄/F05 = 2). Authored
        /// in the card's <c>hitCount</c> column; 1 for every other field, which is the pre-F05 behaviour.
        /// Only <see cref="FieldObjectKind.FieldDamage"/> reads it today — the other kinds tick once by design,
        /// so a value &gt; 1 on them would be silently ignored rather than doubled.
        /// </summary>
        public int HitsPerTick { get; }

        /// <summary>
        /// <see cref="FieldObjectKind.StatusZone"/>이 밟은 플레이어에게 거는 상태이상.
        /// 다른 kind는 이 값을 읽지 않는다 — 종류마다 효과가 다른 지대를 하나의 kind로 다루기 위한
        /// 저작 축이며, 저작 표면은 패턴 CSV의 <c>zoneEffect</c>(<c>zone:Kind;턴</c>)다.
        /// </summary>
        public StatusEffectKind StatusKind { get; }

        public bool IsExpired => RemainingTurns <= 0;

        public FieldObject Tick()
        {
            return new FieldObject(Position, Radius, Math.Max(0, RemainingTurns - 1), Kind, Value, SourceUnitId, VisualRef, HitsPerTick, StatusKind);
        }

        public bool Contains(HexCoord coord)
        {
            return Position.DistanceTo(coord) <= Radius;
        }

        public bool Equals(FieldObject other)
        {
            return Position == other.Position
                && Radius == other.Radius
                && RemainingTurns == other.RemainingTurns
                && Kind == other.Kind
                && Value == other.Value
                && string.Equals(SourceUnitId, other.SourceUnitId, StringComparison.Ordinal)
                && string.Equals(VisualRef, other.VisualRef, StringComparison.Ordinal)
                && HitsPerTick == other.HitsPerTick
                && StatusKind == other.StatusKind;
        }

        public override bool Equals(object obj)
        {
            return obj is FieldObject other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Position.GetHashCode();
                hashCode = (hashCode * 397) ^ Radius;
                hashCode = (hashCode * 397) ^ RemainingTurns;
                hashCode = (hashCode * 397) ^ (int)Kind;
                hashCode = (hashCode * 397) ^ Value;
                hashCode = (hashCode * 397) ^ (SourceUnitId == null ? 0 : SourceUnitId.GetHashCode());
                hashCode = (hashCode * 397) ^ (VisualRef == null ? 0 : VisualRef.GetHashCode());
                hashCode = (hashCode * 397) ^ HitsPerTick;
                hashCode = (hashCode * 397) ^ (int)StatusKind;
                return hashCode;
            }
        }
    }
}

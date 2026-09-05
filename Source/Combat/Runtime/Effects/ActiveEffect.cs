using System;

namespace SeoulPlayup.Combat.Runtime
{
    public readonly struct ActiveEffect : IEquatable<ActiveEffect>
    {
        private readonly string targetUnitId;
        private readonly string sourceRef;

        public ActiveEffect(
            EffectType type,
            StatusEffectKind kind,
            string targetUnitId,
            int remainingTurns,
            int amount = 0,
            string sourceRef = "",
            bool skipNextTick = false)
        {
            Type = type;
            Kind = kind;
            this.targetUnitId = targetUnitId ?? string.Empty;
            RemainingTurns = Math.Max(0, remainingTurns);
            Amount = Math.Max(0, amount);
            this.sourceRef = sourceRef ?? string.Empty;
            SkipNextTick = skipNextTick;
        }

        public EffectType Type { get; }
        public StatusEffectKind Kind { get; }
        public string TargetUnitId => targetUnitId ?? string.Empty;
        public int RemainingTurns { get; }
        public int Amount { get; }
        public string SourceRef => sourceRef ?? string.Empty;
        public bool SkipNextTick { get; }
        public bool IsExpired => RemainingTurns <= 0;

        public ActiveEffect Tick()
        {
            return SkipNextTick
                ? WithSkipNextTick(false)
                : new ActiveEffect(Type, Kind, TargetUnitId, Math.Max(0, RemainingTurns - 1), Amount, SourceRef);
        }

        /// <summary>
        /// 수치만 바꾼 사본. 지속시간과 별개로 <b>매 턴 값이 줄어드는</b> 상태(횃불 C-14)를 위해 있다 —
        /// 그런 상태는 "언제 사라지는가"(RemainingTurns)와 "지금 얼마나 센가"(Amount)가 함께 깎인다.
        /// </summary>
        public ActiveEffect WithAmount(int amount)
        {
            return new ActiveEffect(Type, Kind, TargetUnitId, RemainingTurns, amount, SourceRef, SkipNextTick);
        }

        public ActiveEffect WithSkipNextTick(bool skipNextTick)
        {
            return new ActiveEffect(Type, Kind, TargetUnitId, RemainingTurns, Amount, SourceRef, skipNextTick);
        }

        public bool Equals(ActiveEffect other)
        {
            return Type == other.Type
                && Kind == other.Kind
                && TargetUnitId == other.TargetUnitId
                && RemainingTurns == other.RemainingTurns
                && Amount == other.Amount
                && SourceRef == other.SourceRef
                && SkipNextTick == other.SkipNextTick;
        }

        public override bool Equals(object obj)
        {
            return obj is ActiveEffect other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (int)Type;
                hashCode = (hashCode * 397) ^ (int)Kind;
                hashCode = (hashCode * 397) ^ TargetUnitId.GetHashCode();
                hashCode = (hashCode * 397) ^ RemainingTurns;
                hashCode = (hashCode * 397) ^ Amount;
                hashCode = (hashCode * 397) ^ SourceRef.GetHashCode();
                hashCode = (hashCode * 397) ^ SkipNextTick.GetHashCode();
                return hashCode;
            }
        }
    }
}

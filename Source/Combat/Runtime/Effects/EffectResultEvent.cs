using System.Collections.Generic;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    public readonly struct EffectResultEvent
    {
        public EffectResultEvent(
            EffectKind kind,
            string targetUnitId = "",
            int amount = 0,
            int appliedAmount = 0,
            int previousValue = 0,
            int currentValue = 0,
            HexCoord? center = null,
            int radius = 0,
            string sourceRef = "",
            string sourceUnitId = "",
            string sourceActorKind = "",
            string targetActorKind = "",
            string sourceCardId = "",
            string sourcePatternId = "",
            int hitIndex = 0,
            int hitCount = 0,
            string presentationGroupId = "",
            StatusEffectKind? statusKind = null,
            float delaySeconds = 0f,
            bool lethal = false,
            HexCoord? sourceCoord = null,
            IReadOnlyList<HexCoord> areaCoords = null,
            bool weakSpotHit = false)
        {
            Kind = kind;
            StatusKind = statusKind;
            DelaySeconds = delaySeconds < 0f ? 0f : delaySeconds;
            TargetUnitId = targetUnitId ?? string.Empty;
            Amount = amount;
            AppliedAmount = appliedAmount;
            PreviousValue = previousValue;
            CurrentValue = currentValue;
            Center = center;
            Radius = radius < 0 ? 0 : radius;
            SourceRef = sourceRef ?? string.Empty;
            SourceUnitId = sourceUnitId ?? string.Empty;
            SourceActorKind = sourceActorKind ?? string.Empty;
            TargetActorKind = targetActorKind ?? string.Empty;
            SourceCardId = sourceCardId ?? string.Empty;
            SourcePatternId = sourcePatternId ?? string.Empty;
            HitIndex = hitIndex < 0 ? 0 : hitIndex;
            HitCount = hitCount < 0 ? 0 : hitCount;
            PresentationGroupId = presentationGroupId ?? string.Empty;
            Lethal = lethal;
            SourceCoord = sourceCoord;
            AreaCoords = areaCoords;
            WeakSpotHit = weakSpotHit;
        }

        public EffectKind Kind { get; }
        public StatusEffectKind? StatusKind { get; }
        public float DelaySeconds { get; }
        public string TargetUnitId { get; }
        public int Amount { get; }
        public int AppliedAmount { get; }
        public int PreviousValue { get; }
        public int CurrentValue { get; }
        public HexCoord? Center { get; }
        public int Radius { get; }
        public string SourceRef { get; }
        public string SourceUnitId { get; }
        public string SourceActorKind { get; }
        public string TargetActorKind { get; }
        public string SourceCardId { get; }
        public string SourcePatternId { get; }
        public int HitIndex { get; }
        public int HitCount { get; }
        public string PresentationGroupId { get; }
        public bool Lethal { get; }
        public HexCoord? SourceCoord { get; }

        /// <summary>
        /// Exact affected-tile footprint captured at the moment the rules resolved this effect
        /// (null when the source doesn't author one). Monster attacks set this to their COMMITTED
        /// attack area so presentation never re-derives it from live state — by render time the
        /// player may have moved and the next turn's intent may already be computed.
        /// </summary>
        public IReadOnlyList<HexCoord> AreaCoords { get; }

        /// <summary>
        /// 이 타격이 보스 취약 부위(§20-A)를 덮어 피해 배율이 붙었는가. 플레이어 공격 카드의 Damage 연출
        /// 이벤트에만 찍힌다 — 표현층은 이 비트로 취약타 SFX(boss.weakspot.hit)를 얹는다. 규칙 계산에는 쓰이지 않는다.
        /// </summary>
        public bool WeakSpotHit { get; }
    }
}

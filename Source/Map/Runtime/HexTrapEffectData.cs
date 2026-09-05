using System;

namespace SeoulPlayup.Map.Runtime
{
    [Serializable]
    public readonly struct HexTrapEffectData
    {
        public HexTrapEffectData(
            HexTrapEffectKind kind,
            int amount,
            int durationTurns = 0,
            string monsterDefinitionId = null,
            string statusCardId = null)
        {
            StatusCardId = string.IsNullOrWhiteSpace(statusCardId) ? string.Empty : statusCardId.Trim();
            Kind = kind;
            Amount = Math.Max(0, amount);
            DurationTurns = Math.Max(0, durationTurns);
            MonsterDefinitionId = string.IsNullOrWhiteSpace(monsterDefinitionId)
                ? string.Empty
                : monsterDefinitionId.Trim();
        }

        public HexTrapEffectKind Kind { get; }
        public int Amount { get; }
        public int DurationTurns { get; }

        /// <summary>
        /// <see cref="HexTrapEffectKind.SpawnMonsters"/> 전용 저작값 — 무엇을 부를지(monster_catalog.csv의
        /// monsterId). 다른 kind에서는 비어 있다. 매개변수를 뒤에 붙인 이유는 기존 함정 저작이
        /// int 3개로 직렬화돼 있어서다(append-only).
        /// </summary>
        public string MonsterDefinitionId { get; }

        /// <summary>
        /// <see cref="HexTrapEffectKind.InjectStatusCard"/> 전용 저작값 — 무엇을 넣을지(cards.csv의 id).
        /// 다른 kind에서는 비어 있다. monsterDefinitionId와 같은 이유로 뒤에 붙였다(append-only).
        /// </summary>
        public string StatusCardId { get; }

        public bool IsConfigured => Amount > 0 || DurationTurns > 0 || Kind == HexTrapEffectKind.Stun;
    }
}

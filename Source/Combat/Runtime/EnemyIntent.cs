using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    public readonly struct EnemyIntent
    {
        public EnemyIntent(EnemyIntentType type, int distance, HexCoord enemyCoord, HexCoord playerCoord)
        {
            Type = type;
            Distance = distance;
            EnemyCoord = enemyCoord;
            PlayerCoord = playerCoord;
        }

        public EnemyIntentType Type { get; }
        public int Distance { get; }
        public HexCoord EnemyCoord { get; }
        public HexCoord PlayerCoord { get; }

        public string ToDisplayText(CombatConfig config)
        {
            switch (Type)
            {
                case EnemyIntentType.Attack:
                    return $"Normal Enemy: Attack {config.EnemyAttackDamage} at range {config.EnemyAttackRange} (distance {Distance})";
                case EnemyIntentType.Chase:
                    return $"Normal Enemy: Chase toward player (distance {Distance}, attacks at {config.EnemyAttackRange})";
                case EnemyIntentType.Search:
                    return $"Normal Enemy: Search last known player position (distance {Distance})";
                case EnemyIntentType.Return:
                    return $"Normal Enemy: Return to patrol route (distance {Distance})";
                case EnemyIntentType.Alert:
                    return $"Normal Enemy: Alert (distance {Distance})";
                default:
                    return $"Normal Enemy: Patrol (distance {Distance})";
            }
        }

        public override string ToString() => $"{Type} ({Distance})";
    }
}

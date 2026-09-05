using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// Named-argument factories for the <see cref="CombatConfig"/> shapes the suite keeps rebuilding
    /// with 9+ positional magic numbers. Call sites should only name what the test actually cares
    /// about; everything else stays at the de-facto standard small-arena values.
    /// </summary>
    public static class TestCombatConfigs
    {
        /// <summary>
        /// The standard small-arena test config (player 20 HP, enemy 10 HP, melee enemy that never
        /// chases, 3 damage). This was the exact positional prefix of the majority of
        /// new CombatConfig(...) call sites before consolidation.
        /// </summary>
        public static CombatConfig Standard(
            int playerMaxHp = 20,
            int enemyMaxHp = 10,
            int playerMovePoints = 2,
            int attackRange = 1,
            int attackDamage = 4,
            int defenseBlock = 4,
            int enemyChaseRange = 0,
            int enemyAttackRange = 1,
            int enemyAttackDamage = 3,
            int actionBudget = 4,
            int movementHandSize = 1,
            int actionHandSize = 5,
            int playerVisionRange = 7,
            int enemyDisengageRange = 4)
        {
            return new CombatConfig(
                playerMaxHp,
                enemyMaxHp,
                playerMovePoints,
                attackRange,
                attackDamage,
                defenseBlock,
                enemyChaseRange,
                enemyAttackRange,
                enemyAttackDamage,
                actionBudget,
                movementHandSize,
                actionHandSize,
                playerVisionRange,
                enemyDisengageRange);
        }
    }
}

namespace SeoulPlayup.Combat.Runtime
{
    public readonly struct CombatConfig
    {
        public CombatConfig(
            int playerMaxHp,
            int enemyMaxHp,
            int playerMovePoints,
            int attackRange,
            int attackDamage,
            int defenseBlock,
            int enemyChaseRange,
            int enemyAttackRange,
            int enemyAttackDamage,
            int actionBudget = 4,
            int movementHandSize = 1,
            int actionHandSize = 5,
            int playerVisionRange = 7,
            int enemyDisengageRange = 4,
            int eliteHpPercent = 100,
            int eliteDamagePercent = 100)
        {
            PlayerMaxHp = playerMaxHp <= 0 ? 80 : playerMaxHp;
            EnemyMaxHp = enemyMaxHp <= 0 ? 30 : enemyMaxHp;
            PlayerMovePoints = playerMovePoints < 0 ? 0 : playerMovePoints;
            AttackRange = attackRange <= 0 ? 1 : attackRange;
            AttackDamage = attackDamage < 0 ? 0 : attackDamage;
            DefenseBlock = defenseBlock < 0 ? 0 : defenseBlock;
            EnemyChaseRange = enemyChaseRange < 0 ? 0 : enemyChaseRange;
            EnemyAttackRange = enemyAttackRange <= 0 ? 1 : enemyAttackRange;
            EnemyAttackDamage = enemyAttackDamage < 0 ? 0 : enemyAttackDamage;
            ActionBudget = actionBudget <= 0 ? 4 : actionBudget;
            MovementHandSize = movementHandSize <= 0 ? 1 : movementHandSize;
            ActionHandSize = actionHandSize <= 0 ? 5 : actionHandSize;
            PlayerVisionRange = playerVisionRange < 0 ? 0 : playerVisionRange;
            EnemyDisengageRange = enemyDisengageRange < 0 ? EnemyChaseRange : enemyDisengageRange;
            EliteHpPercent = eliteHpPercent < 1 ? 100 : eliteHpPercent;
            EliteDamagePercent = eliteDamagePercent < 1 ? 100 : eliteDamagePercent;
        }

        public int PlayerMaxHp { get; }
        public int EnemyMaxHp { get; }
        public int PlayerMovePoints { get; }
        public int AttackRange { get; }
        public int AttackDamage { get; }
        public int DefenseBlock { get; }
        public int EnemyChaseRange { get; }
        public int EnemyDisengageRange { get; }
        public int EnemyAttackRange { get; }
        public int EnemyAttackDamage { get; }
        public int ActionBudget { get; }
        public int MaxKi => ActionBudget;
        public int MovementHandSize { get; }
        public int ActionHandSize { get; }

        /// <summary>
        /// 플레이어가 매 턴 자동으로 밝히는 시야 반경(헥스 칸 수). 이동 카드 사거리와 독립적으로 관리된다.
        /// </summary>
        public int PlayerVisionRange { get; }

        /// <summary>
        /// 엘리트 몬스터의 체력·피해 배율(퍼센트, 100 = 배율 없음 · 2026-08-20 #12).
        ///
        /// <para>저작면은 <c>stage_randomization.csv</c>의 <c>eliteHpPercent</c>·
        /// <c>eliteDamagePercent</c>이고(eliteMin과 같은 난이도 표), 컨트롤러가 프로파일을 읽어
        /// 여기로 넘긴다. 규칙층이 <b>role</b>만 보고 적용하므로 랜덤화로 뽑힌 엘리트와 손저작
        /// 엘리트가 같은 대우를 받는다 — 랜덤화 경로에만 심으면 저작 엘리트가 조용히 약해진다.</para>
        /// </summary>
        public int EliteHpPercent { get; }

        /// <inheritdoc cref="EliteHpPercent"/>
        public int EliteDamagePercent { get; }

        public static CombatConfig FromPlayerProfile(
            PlayerCombatProfile profile,
            int enemyMaxHp,
            int enemyChaseRange,
            int enemyAttackRange,
            int enemyAttackDamage,
            int enemyDisengageRange = 4,
            int eliteHpPercent = 100,
            int eliteDamagePercent = 100)
        {
            return profile.ToCombatConfig(
                enemyMaxHp, enemyChaseRange, enemyAttackRange, enemyAttackDamage, enemyDisengageRange,
                eliteHpPercent, eliteDamagePercent);
        }

        public static CombatConfig Default => new CombatConfig(80, 30, 2, 1, 4, 4, 6, 1, 5, 4, 1, 5, 7, 4);
    }
}

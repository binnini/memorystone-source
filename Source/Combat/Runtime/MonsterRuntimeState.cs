using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    public readonly struct MonsterRuntimeState
    {
        public MonsterRuntimeState(
            string id,
            HexCoord coord,
            int hp,
            int maxHp,
            EnemyIntent intent,
            bool hasAttackIntent,
            string catalogSourceId = "",
            string definitionId = "",
            string spawnRefId = "",
            string spawnRole = "",
            int attackSpeed = 1,
            int attackPatternIndex = 0,
            string selectedAttackPatternId = "",
            string selectedAttackPatternDisplayName = "",
            int selectedAttackPatternDamage = 0,
            int selectedAttackPatternRange = 1,
            int selectedAttackPatternAreaRadius = 0,
            string selectedAttackPatternShapeId = "",
            string selectedAttackPatternEffectRef = "",
            MonsterFsmState fsmState = MonsterFsmState.Patrol,
            MonsterActivityState activityState = MonsterActivityState.ActiveThreat,
            EnemyIntent lockedFacingIntent = default,
            int block = 0,
            int agitationStacks = 0,
            bool toughnessSpent = false,
            int stealthRevealTurnsRemaining = 0,
            int restoredMoney = 0,
            bool rewardClaimed = false)
        {
            RewardClaimed = rewardClaimed;
            AgitationStacks = agitationStacks;
            ToughnessSpent = toughnessSpent;
            StealthRevealTurnsRemaining = stealthRevealTurnsRemaining;
            RestoredMoney = restoredMoney;
            Block = block;
            Id = id;
            Coord = coord;
            Hp = hp;
            MaxHp = maxHp;
            Intent = intent;
            HasAttackIntent = hasAttackIntent;
            LockedFacingIntent = lockedFacingIntent;
            CatalogSourceId = catalogSourceId ?? string.Empty;
            DefinitionId = definitionId ?? string.Empty;
            SpawnRefId = spawnRefId ?? string.Empty;
            SpawnRole = spawnRole ?? string.Empty;
            AttackSpeed = attackSpeed;
            AttackPatternIndex = attackPatternIndex;
            SelectedAttackPatternId = selectedAttackPatternId ?? string.Empty;
            SelectedAttackPatternDisplayName = selectedAttackPatternDisplayName ?? string.Empty;
            SelectedAttackPatternDamage = selectedAttackPatternDamage;
            SelectedAttackPatternRange = selectedAttackPatternRange;
            SelectedAttackPatternAreaRadius = selectedAttackPatternAreaRadius;
            SelectedAttackPatternShapeId = selectedAttackPatternShapeId ?? string.Empty;
            SelectedAttackPatternEffectRef = selectedAttackPatternEffectRef ?? string.Empty;
            FsmState = fsmState;
            ActivityState = activityState;
        }

        public string Id { get; }
        public HexCoord Coord { get; }
        public int Hp { get; }
        public int MaxHp { get; }

        /// <summary>
        /// 방어막(피해를 먼저 흡수하는 <c>CombatantState.Block</c>의 투영 · §21.8 제안 4). 몬스터 Block은
        /// 플레이어와 달리 <b>턴 시작에 소거되지 않는다</b> — 부술 때까지 남는다. HUD·툴팁이 읽는다.
        /// </summary>
        public int Block { get; }
        public EnemyIntent Intent { get; }
        public bool HasAttackIntent { get; }
        public string CatalogSourceId { get; }
        public string DefinitionId { get; }
        public string SpawnRefId { get; }
        public string SpawnRole { get; }
        public int AttackSpeed { get; }
        public int AttackPatternIndex { get; }
        public string SelectedAttackPatternId { get; }
        public string SelectedAttackPatternDisplayName { get; }
        public int SelectedAttackPatternDamage { get; }
        public int SelectedAttackPatternRange { get; }
        public int SelectedAttackPatternAreaRadius { get; }
        public string SelectedAttackPatternShapeId { get; }
        public string SelectedAttackPatternEffectRef { get; }
        public MonsterFsmState FsmState { get; }
        public MonsterActivityState ActivityState { get; }
        public EnemyIntent LockedFacingIntent { get; }

        /// <summary>약오름(T7-2) 현재 힘 스택. 상한은 카탈로그 엔트리(AgitationMaxStacks)가 정본.</summary>
        public int AgitationStacks { get; }

        /// <summary>맷집(T7-2) 소진 여부. false = 다음 카드 피격이 반감된다(HUD·툴팁이 읽는다).</summary>
        public bool ToughnessSpent { get; }

        /// <summary>
        /// 은신이 풀린 뒤 <b>다시 숨기까지</b> 남은 턴(0 = 지금 숨어 있다). 배지가 이 숫자를 든다 —
        /// 「언제 다시 사라지는가」가 이 몬스터를 상대하는 유일한 시계이기 때문이다(2026-09-05).
        /// 정찰·피격으로 갱신되지 않으므로 한 번 켜지면 제 속도로 끝까지 간다.
        /// </summary>
        public int StealthRevealTurnsRemaining { get; }

        /// <summary>죽으면서 돌려준 엽전(2026-09-05·소매치기). 전리품 목록의 「(반환)」 줄이 읽는다.</summary>
        public int RestoredMoney { get; }

        /// <summary>처치 보상이 이미 지급된 시체(<see cref="MonsterRuntime.RewardClaimed"/>). 보상 흐름이 「방금 죽은 적」을 고를 때 거른다.</summary>
        public bool RewardClaimed { get; }

        public bool IsDead => Hp <= 0;
    }
}

using System;

namespace SeoulPlayup.Combat.Runtime
{
    public readonly struct MonsterCatalogEntry
    {
        public MonsterCatalogEntry(
            string id,
            string displayName,
            string archetype,
            string behaviorProfileRef,
            int detectionRange,
            int movePerTurn,
            int hp,
            string status = "sample_seed",
            int attackSpeed = 1,
            MonsterAttackPattern[] attackPatterns = null,
            string visualPrefabPath = "",
            int agitationMaxStacks = 0,
            int toughnessReloadTurns = 0,
            string onDeathEffectRef = "",
            string onDeathEffectParam = "",
            bool sturdyBlock = false,
            int hpVariancePct = 0,
            bool advanceAfterAttack = false,
            string hiddenTraitRef = "",
            string hiddenTraitParam = "",
            string agitationConditionRef = "",
            MonsterFootprintShape footprintShape = MonsterFootprintShape.Single,
            int guardRechargeTurns = 0)
        {
            Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Monster catalog entry id is required.", nameof(id)) : id;
            DisplayName = displayName ?? string.Empty;
            Archetype = archetype ?? string.Empty;
            BehaviorProfileRef = behaviorProfileRef ?? string.Empty;
            DetectionRange = detectionRange;
            MovePerTurn = movePerTurn;
            Hp = hp;
            Status = status ?? string.Empty;
            AttackSpeed = attackSpeed <= 0 ? 1 : attackSpeed;
            AttackPatterns = (attackPatterns != null && attackPatterns.Length > 0)
                ? attackPatterns
                : new[] { MonsterAttackPattern.CreateBasicDamage($"{Id}.basic-attack", "Basic Attack", 1, 0) };
            VisualPrefabPath = visualPrefabPath ?? string.Empty;
            AgitationMaxStacks = Math.Max(0, agitationMaxStacks);
            ToughnessReloadTurns = Math.Max(0, toughnessReloadTurns);
            OnDeathEffectRef = onDeathEffectRef ?? string.Empty;
            OnDeathEffectParam = onDeathEffectParam ?? string.Empty;
            HasSturdyBlock = sturdyBlock;
            HasAdvanceAfterAttack = advanceAfterAttack;
            HiddenTraitRef = hiddenTraitRef ?? string.Empty;
            HiddenTraitParam = hiddenTraitParam ?? string.Empty;
            AgitationConditionRef = agitationConditionRef ?? string.Empty;
            HpVariancePct = Math.Max(0, Math.Min(50, hpVariancePct));
            FootprintShape = footprintShape;
            GuardRechargeTurns = Math.Max(0, guardRechargeTurns);
        }

        /// <summary>
        /// 몸이 차지하는 칸 형상(2026-09-03). <see cref="MonsterFootprintShape.Triangle"/>이면 앵커+동+남동 세 칸을
        /// 점유하고 모델은 꼭짓점에 선다. CSV <c>footprint</c>가 저작 표면(빈 값 = 한 칸). 보스 원판과는 별개 축이다.
        /// </summary>
        public MonsterFootprintShape FootprintShape { get; }

        public string Id { get; }
        public string DisplayName { get; }
        public string Archetype { get; }
        public string BehaviorProfileRef { get; }
        public int DetectionRange { get; }
        public int MovePerTurn { get; }
        public int Hp { get; }
        public string Status { get; }
        /// <summary>몬스터 행동 우선순위: 값이 <b>높을수록 빠르고 먼저(선공)</b> 행동한다(GetMonsterActionOrder 내림차순).</summary>
        public int AttackSpeed { get; }
        public MonsterAttackPattern[] AttackPatterns { get; }
        public string VisualPrefabPath { get; }

        /// <summary>약오름(T7-2) 힘 스택 상한. 0 = 이 문법 없음. CSV <c>agitationMaxStacks</c>가 저작 표면.</summary>
        public int AgitationMaxStacks { get; }

        /// <summary>
        /// 약오름 스택이 <b>오르는 조건</b>. 빈 값 = 기본(감지 중 플레이어가 농성 — 삼목구·성난 황소).
        /// <c>agitation.dread</c> = 겁먹음(거구귀): 플레이어가 거리를 벌린 턴에 오르고 인접하면 0으로 꺼진다.
        ///
        /// <para>🔑 <b>조건만 갈아 끼운다</b>. 스택 저장·피해 보너스(+1/스택)·배지·툴팁·세이브는 전부
        /// 약오름 배관 그대로다 — 그래서 새 특성 하나에 드는 값이 조건 분기 한 줄이다.</para>
        /// </summary>
        public string AgitationConditionRef { get; }

        /// <summary>맷집(T7-2) 재장전에 필요한 연속 비감지 턴 수. 0 = 이 문법 없음. CSV <c>toughnessReloadTurns</c>.</summary>
        public int ToughnessReloadTurns { get; }

        /// <summary>뒤끝(T7-2) 효과 ref(<see cref="MonsterDeathAftermath"/>에 등록된 것만). 빈 값 = 없음.</summary>
        public string OnDeathEffectRef { get; }

        /// <summary>뒤끝 파라미터 blob(<c>key=value;key=value</c> — boss_profiles.csv mechanicParams 규약).</summary>
        public string OnDeathEffectParam { get; }

        /// <summary>
        /// 견고(2026-08-10 사용자 확정): 이 몬스터의 방어막(<c>CombatantState.Block</c>)은 <b>턴이 지나도
        /// 사라지지 않는다</b>. 기본 규칙은 플레이어와 같은 턴 소멸이고, 이 특성만 예외다.
        ///
        /// <para>🔑 예외를 특성으로 만든 이유: 불가살의 trap-volley 방어막(§21.8 제안 4)이 "부술 때까지
        /// 남는" 것을 전제로 설계됐다("깎아라" 퍼즐). 규칙을 전역으로 뒤집으면 그 기믹이 죽고, 예외를
        /// 코드에 숨기면 왜 이 놈만 다른지 화면에서 읽을 수 없다. 저작으로 올려 배지에 띄운다.</para>
        ///
        /// <para>CSV <c>sturdyBlock</c>이 저작 표면.</para>
        /// </summary>
        public bool HasSturdyBlock { get; }

        /// <summary>
        /// 「밀어붙이기」(요괴 §4-5 · 두억시니): 공격을 마치면 플레이어 쪽으로 1칸 전진한다.
        /// <para>CSV <c>advanceAfterAttack</c>이 저작 표면. 이동 1의 느린 몸이 그래도 붙는다는 압박이고,
        /// 넉백으로 떼어 놓아도 반 걸음을 되찾아 온다는 뜻이다.</para>
        /// </summary>
        public bool HasAdvanceAfterAttack { get; }

        /// <summary>은신 특성 ref(<see cref="MonsterHiddenTrait"/>에 등록된 것만). 빈 값 = 없음.</summary>
        public string HiddenTraitRef { get; }

        /// <summary>은신 파라미터 blob(<c>key=value;key=value</c> — 뒤끝 param과 같은 규약).</summary>
        public string HiddenTraitParam { get; }

        public bool HasHiddenTrait => !string.IsNullOrWhiteSpace(HiddenTraitRef);

        /// <summary>
        /// 은신(<see cref="MonsterHiddenTrait.StealthRef"/>) 특성인가. <see cref="HasHiddenTrait"/>는
        /// 「특성 슬롯이 채워졌는가」라 봉인 오라(aura.seal)도 참이다 — 배치 bans의 <c>monster:stealth</c>
        /// 태그처럼 <b>은신만</b> 물어야 하는 소비자는 이쪽을 쓴다(2026-09-05 #31: 그슨새가 stealth 태그를 받고 있었다).
        /// </summary>
        public bool HasStealthTrait =>
            string.Equals((HiddenTraitRef ?? string.Empty).Trim(), MonsterHiddenTrait.StealthRef, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 스폰 시 체력 변주 폭(±%, StS식 개체차). 0 = 현행 고정 체력. CSV <c>hpVariancePct</c>가 저작
        /// 표면(옵트인 — 컬럼이 없거나 비면 아무것도 안 바뀐다). 굴림은 배치 시드의 파생 스트림으로
        /// 스폰 id별 1회 — 같은 시드 = 같은 체력(세이브 재현 계약 §2-2). 상한 50: 그 이상은 최소
        /// 체력이 절반 밑으로 떨어져 저작 실수로 본다.
        /// </summary>
        public int HpVariancePct { get; }

        public bool HasHpVariance => HpVariancePct > 0;
        public bool HasAgitation => AgitationMaxStacks > 0;
        public bool HasToughness => ToughnessReloadTurns > 0;
        /// <summary>수호 재충전(2026-09-05 결정 1): 몇 턴마다 수호 충전을 1 얻는가. 0 = 이 특성 없음. CSV <c>guardRechargeTurns</c>.
        /// 충전 상한은 규칙 상수(<see cref="GuardRechargeMaxCharges"/>) — 저작 노브가 아니다(상한 0 같은 계약 파괴 저작을 막는다).</summary>
        public int GuardRechargeTurns { get; }
        public bool HasGuardRecharge => GuardRechargeTurns > 0;
        /// <summary>수호 재충전이 쌓을 수 있는 최대 충전. 2026-09-05 실플레이 후속 #5로 2→1(플레이어 상한과 동일 — Q13 「하나 높게」는 폐기).</summary>
        public const int GuardRechargeMaxCharges = 1;
        public bool HasDeathAftermath => !string.IsNullOrWhiteSpace(OnDeathEffectRef);
    }
}

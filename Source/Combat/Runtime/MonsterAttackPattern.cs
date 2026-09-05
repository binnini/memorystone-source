using System;
using System.Collections.Generic;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    public readonly struct MonsterAttackPattern
    {
        public MonsterAttackPattern(
            string id,
            string displayName,
            int range,
            int areaRadius,
            int damage,
            string effectRef = "attack.damage",
            string targeting = "player_in_range",
            int weight = 1,
            string shapeId = null,
            StatusEffectKind[] statusEffects = null,
            int statusEffectDurationTurns = 2,
            int statusEffectAmount = 1,
            string animationTrigger = null,
            int knockbackDistance = 0,
            int knockbackImpactDamage = 0,
            int cooldownTurns = 0,
            int phaseMin = 0,
            int phaseMax = int.MaxValue,
            int distMin = 0,
            int distMax = int.MaxValue,
            int leapRange = 0,
            string injectStatusCardId = null,
            int hitCount = 1,
            int shieldGain = 0,
            int damageJitter = 0,
            string[] injectStatusCardPool = null,
            StatusEffectKind zoneStatusKind = default,
            int zoneDurationTurns = 0,
            HexCoord[] zoneOffsets = null,
            string summonMonsterDefinitionId = null,
            int summonCount = 0,
            int summonMaxAlive = 0,
            int selfTeleportRadius = 0,
            int stealMoneyAmount = 0)
        {
            // 0/음수 저작은 "치지 않는 공격"이라는 뜻이 없다 — 1로 눌러 기존 단일 히트와 같게 만든다.
            HitCount = Math.Max(1, hitCount);
            Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Monster attack pattern id is required.", nameof(id)) : id;
            DisplayName = displayName ?? string.Empty;
            Range = range <= 0 ? 1 : range;
            AreaRadius = areaRadius < 0 ? 0 : areaRadius;
            Damage = damage < 0 ? 0 : damage;
            EffectRef = effectRef ?? string.Empty;
            Targeting = targeting ?? string.Empty;
            Weight = weight <= 0 ? 1 : weight;
            ShapeId = shapeId;
            StatusEffects = statusEffects ?? Array.Empty<StatusEffectKind>();
            StatusEffectDurationTurns = Math.Max(1, statusEffectDurationTurns);
            StatusEffectAmount = Math.Max(0, statusEffectAmount);
            AnimationTrigger = animationTrigger ?? string.Empty;
            // 음수 = 끌어당김(§16.1). 클램프하지 않는다 — 부호가 곧 방향이라 0으로 눌러 버리면
            // "당기는 공격"을 저작할 방법 자체가 사라진다.
            KnockbackDistance = knockbackDistance;
            KnockbackImpactDamage = Math.Max(0, knockbackImpactDamage);
            CooldownTurns = Math.Max(0, cooldownTurns);
            PhaseMin = Math.Max(0, phaseMin);
            PhaseMax = Math.Max(PhaseMin, phaseMax);
            DistMin = Math.Max(0, distMin);
            DistMax = Math.Max(DistMin, distMax);
            LeapRange = Math.Max(0, leapRange);
            InjectStatusCardId = injectStatusCardId ?? string.Empty;
            InjectStatusCardPool = injectStatusCardPool ?? Array.Empty<string>();
            ZoneStatusKind = zoneStatusKind;
            ZoneDurationTurns = Math.Max(0, zoneDurationTurns);
            ZoneOffsets = zoneOffsets ?? Array.Empty<HexCoord>();
            SummonMonsterDefinitionId = summonMonsterDefinitionId ?? string.Empty;
            SummonCount = Math.Max(0, summonCount);
            SummonMaxAlive = Math.Max(SummonCount, summonMaxAlive);
            SelfTeleportRadius = Math.Max(0, selfTeleportRadius);
            ShieldGain = Math.Max(0, shieldGain);
            DamageJitter = Math.Max(0, damageJitter);
            StealMoneyAmount = Math.Max(0, stealMoneyAmount);
        }

        /// <summary>
        /// 턴별 피해 변주 폭(±N flat, StS식 — 타격당 기본 피해에 더해진다). 0 = 현행 고정 수치.
        /// CSV <c>damageJitter</c>가 저작 표면(옵트인). 굴림 자체는 여기가 아니라 <b>의도 잠금 시
        /// 1회</b>(MonsterAiPlanner) 일어나 <c>MonsterRuntime.AttackDamageRollOffset</c>에 저장되고,
        /// 예고와 집행이 같은 저장값을 소비한다(R-8) — 이 필드는 폭의 저작값일 뿐이다.
        /// </summary>
        public int DamageJitter { get; }

        public bool HasDamageJitter => DamageJitter > 0;

        /// <summary>
        /// 이 패턴이 <b>자기 자신</b>에게 두르는 방어막(<c>CombatantState.Block</c> · 0 = 없음).
        /// 플레이어 방어도와 같은 필드라 흡수는 <c>ApplyDamage</c>가 공유하고, 상태이상이 아니므로
        /// 정화가 닿지 않는다(2026-08-10 사용자 확정).
        ///
        /// <para>공격 패턴에도, 자기부여 패턴(<see cref="IsSelfTargeted"/>)에도 붙일 수 있다 —
        /// "치면서 굳는다"와 "이번 턴은 몸만 굳힌다"가 저작 한 컬럼으로 갈린다. 부여 시점은 공격이
        /// 실제로 성립한 순간(쿨다운이 도는 그 자리)이라, 예고를 띄우기만 하고 불발한 턴에는
        /// 굳지 않는다 — 불가살 trap-volley의 "배치가 실제로 일어났을 때만" 규약과 같다.</para>
        ///
        /// <para>얻은 방어막은 <b>다음 전체 턴 시작에 사라진다</b>(플레이어와 같은 규칙).
        /// 예외는 「견고」 특성(<c>MonsterCatalogEntry.HasSturdyBlock</c>)뿐이다.</para>
        /// </summary>
        public int ShieldGain { get; }

        /// <summary>
        /// <c>targeting</c> 열의 자기부여 값. 현행 값 셋(player_in_range · player_area_in_range · self)
        /// 중 하나로, 미지·강화처럼 몬스터가 <b>스스로 두르는 버프</b>를 저작하는 유일한 길이다(§21.8 제안 8).
        /// </summary>
        public const string SelfTargetingValue = "self";

        /// <summary>
        /// 자기부여 패턴인가. 선택(항상 후보) · 커버 판정(제외 — 이동/도약이 "버프로 닿는다"고 오판하면
        /// 보스가 추격을 멈춘다) · 예고(위험 칸 없음) · 집행(상태를 자신에게)이 전부 이 술어 하나를 읽는다.
        /// </summary>
        public bool IsSelfTargeted => string.Equals(Targeting, SelfTargetingValue, StringComparison.OrdinalIgnoreCase);

        public string Id { get; }
        public string DisplayName { get; }
        public int Range { get; }
        public int AreaRadius { get; }
        public int Damage { get; }
        public string EffectRef { get; }
        public string Targeting { get; }
        public int Weight { get; }
        // When set, hit detection uses AttackShapeLibrary instead of the AreaRadius circle.
        public string ShapeId { get; }
        // Status effects applied to targets hit by this attack.
        public IReadOnlyList<StatusEffectKind> StatusEffects { get; }
        // Duration used for persistent StatusEffects applied by this attack.
        public int StatusEffectDurationTurns { get; }
        // Strength used for persistent StatusEffects applied by this attack.
        public int StatusEffectAmount { get; }
        // Optional Animator trigger name selected by designer data for this attack pattern.
        public string AnimationTrigger { get; }
        // Tiles the player is displaced when this attack lands (0 = none).
        // <b>양수 = 밀어냄 · 음수 = 끌어당김</b>(§16.1). 부호가 방향이고 크기가 칸 수다. 당기는 쪽은
        // 멈춤 조건(벽·기물·점유)이 밀어내는 쪽과 같아 보스 몸통에 박히지 않고 가장자리에서 멈춘다.
        public int KnockbackDistance { get; }
        // Extra damage applied if the knockback slams the target into a wall/obstacle.
        public int KnockbackImpactDamage { get; }
        // Monster turns this pattern is excluded from selection after it is used (0 = always available).
        // Used to stop status-effect patterns (stun/slow/etc.) from being spammed consecutively.
        public int CooldownTurns { get; }
        // 이 패턴이 후보가 되기 위한 최소 보스 페이즈 게이트 레벨(0 = 항상 사용 가능).
        // 몬스터가 아니라 <b>바인딩</b>이 소유하는 값이다 — 같은 패턴을 다른 보스가 다른 페이즈에
        // 걸어 쓸 수 있어야 하므로 monster_pattern_bindings.csv의 phaseMin이 저작 표면이다.
        // 게이트 레벨은 boss_phases.csv의 patternPhaseMin이 정하고, 비교는 계획부(MonsterAiPlanner)에서만
        // 한다(AttackPatterns 배열은 불변 — 쿨다운 딕셔너리 인덱스 키와 세이브 왕복이 배열 순서에 묶여 있다).
        public int PhaseMin { get; }
        // 이 패턴이 후보로 남는 마지막 보스 페이즈 게이트 레벨(기본 int.MaxValue = 은퇴 없음).
        // phaseMin과 짝을 이루는 바인딩 소유 값 — 페이즈가 오르면 저페이즈 약패턴을 추첨 풀에서
        // 은퇴시켜 상위 변형으로 세대 교체한다. 비교는 phaseMin과 마찬가지로 선택 단계에서만 한다.
        public int PhaseMax { get; }
        // 이 패턴이 후보가 되기 위한 최소 <b>플레이어 거리</b>(기본 0 = 제한 없음). phaseMin/phaseMax와
        // 똑같이 바인딩이 소유하는 값이고 비교도 선택 단계에서만 한다 — 배열은 여전히 불변이다.
        // 거리는 <b>몸통 가장자리 기준</b>(보스 footprint 반경을 뺀 값)이라 보스 크기가 커져도 의미가 같다.
        // 존재 이유: 보스는 봉인 중 이동이 0이라(§13.4 C-5) "플레이어를 덮는 패턴"만 뽑는 기존 규칙에서는
        // 플레이어가 멀면 원거리기만 커버 판정을 통과해 근접기가 영원히 안 나온다. 거리 창이 그 축을 연다.
        public int DistMin { get; }
        // 이 패턴이 후보로 남는 최대 플레이어 거리(기본 int.MaxValue = 제한 없음). distMin과 [min, max] 창을
        // 이룬다. 붙어야 안전한 패턴(donut)·멀어야 위험한 패턴(artillery)을 저작으로 표현하는 축이다.
        public int DistMax { get; }
        // 이 공격이 <b>도약형</b>이면 그 도약 거리(0 = 도약 없음 — 기존 모든 패턴).
        // phaseMin/distMin과 달리 <b>패턴 정의가 소유한다</b>(바인딩이 아니다): "이 공격이 내려찍기인가"는
        // 누가 쓰느냐와 무관한 공격 자체의 성질이라, monster_attack_patterns.csv의 leapRange가 저작 표면이다.
        // 존재 이유: 멀티셀 보스는 걷지 못한다(§14.3 — 원판 클리어런스 경로탐색이 없다). 경로를 <b>걷지 않고</b>
        // 착지 지점만 검사하는 도약은 그 이유가 성립하지 않으므로, 고정 보스에게 위협 반경을 되돌려 준다.
        // 빈도는 별도 주기를 저작하지 않고 <see cref="CooldownTurns"/>가 그대로 관장한다.
        public int LeapRange { get; }

        /// <summary>
        /// 해코지(T2): 이 공격이 플레이어에게 닿을 때 덱에 섞을 저주 카드 id(비면 없음).
        /// monster_attack_patterns.csv의 injectStatusCardId 컬럼이 저작 표면.
        /// </summary>
        public string InjectStatusCardId { get; }

        /// <summary>
        /// 도깨비 장난(요괴 트랙 §3-3): 이 공격이 플레이어에게 닿을 때 <b>이 중 한 장</b>을 덱에 섞는다.
        /// monster_attack_patterns.csv의 <c>injectStatusCardPool</c>(<c>X05;X07;X11</c>)이 저작 표면이고,
        /// <see cref="InjectStatusCardId"/>와는 <b>둘 중 하나만</b> 저작할 수 있다(임포트가 거부한다).
        /// 추첨은 <see cref="MonsterCurseCardPool"/>의 순수 함수라 같은 상황이면 같은 카드가 나온다.
        /// </summary>
        public IReadOnlyList<string> InjectStatusCardPool { get; }

        public bool HasInjectStatusCardPool => InjectStatusCardPool.Count > 0;

        /// <summary>
        /// 상태이상 지대(요괴 §3-3 · 두억시니): 형상 안 <b>일부 칸</b>에만 남는 장판. 저작 표면은
        /// <c>zoneEffect</c>(<c>zone:Kind;턴</c>) + <c>zoneOffsets</c>이고, 오프셋은 형상의
        /// <b>부분집합</b>이어야 한다(임포트가 거부 — 형상 밖에 깔면 예고=명중이 깨진다).
        /// 형상이 아니라 패턴이 소유하는 이유: 형상은 공유 자산이라 같은 형상을 쓸 다음 요괴가
        /// 원치 않는 장판을 물려받으면 안 된다.
        /// </summary>
        public IReadOnlyList<HexCoord> ZoneOffsets { get; }

        public StatusEffectKind ZoneStatusKind { get; }

        /// <summary>지대가 판에 남는 턴 수(상태이상 지속이 아니다 — 그쪽은 밟을 때마다 갱신된다).</summary>
        public int ZoneDurationTurns { get; }

        public bool HasStatusZone => ZoneOffsets.Count > 0 && ZoneDurationTurns > 0;

        /// <summary>
        /// 소환(요괴 §4-4 · 구미호 A047): 형상 칸에 이 몬스터를 부른다. 저작 표면은
        /// <c>summonSpec</c>(<c>summon:M007;2</c>)이고, 부른 적은 <b>정식 위협</b>으로 센다 —
        /// 처리하지 않아도 이기는 소환은 소환이 아니다.
        /// </summary>
        public string SummonMonsterDefinitionId { get; }

        public int SummonCount { get; }

        /// <summary>동시 생존 상한. 이 수만큼 살아 있으면 패턴이 후보에서 빠진다(무한 소환 방지).</summary>
        public int SummonMaxAlive { get; }

        /// <summary>
        /// 이 패턴이 해소된 뒤 시전자가 <b>스스로</b> 순간이동하는 반경(0 = 안 함).
        ///
        /// <para>야광귀의 「달아나기」가 유일한 사용자다(구 「그림자로 흩어지기」 A039는 은신 특성
        /// scatterPattern으로 같은 일을 했으나 2026-09-04 리워크로 삭제됐다). 목적지 선정은 결정적
        /// 함수를 쓴다(무시드 RNG 금지: 세이브 왕복에도 같은 칸이어야 한다).</para>
        /// </summary>
        public int SelfTeleportRadius { get; }

        /// <summary>
        /// 소매치기(야광귀 A051 · 2026-09-04 §2-D) — 명중 시 훔치는 엽전(0 = 안 훔침). 플레이어 보유가
        /// 이보다 적으면 있는 만큼만 훔친다(음수 금지). 훔친 액수는 개체(<c>MonsterRuntime.StolenMoney</c>)에
        /// 쌓이고 <b>처치 시에만</b> 반환된다(<c>aftermath.restore kind=Money</c>).
        /// CSV <c>stealMoneyAmount</c>가 저작 표면.
        /// </summary>
        public int StealMoneyAmount { get; }

        public bool HasSummon => SummonCount > 0 && !string.IsNullOrEmpty(SummonMonsterDefinitionId);

        /// <summary>
        /// 한 번의 공격이 나눠 때리는 히트 수(§21.8 제안 6 · 기본 1). <see cref="Damage"/>는 <b>히트당</b>
        /// 피해다(카드 쪽 F05 <c>HitsPerTick</c>과 같은 규약). 상태 부여·저주 삽입은 히트 수와 무관하게
        /// 공격당 1회다 — 히트당으로 돌리면 스택·카드 오염이 조용히 배가된다.
        /// </summary>
        public int HitCount { get; }

        public static MonsterAttackPattern CreateBasicDamage(string id, string displayName, int range, int damage)
        {
            return new MonsterAttackPattern(id, displayName, range, 0, damage);
        }
    }
}
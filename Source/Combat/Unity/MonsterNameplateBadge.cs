using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// 몬스터 네임플레이트 <b>이름 위</b> 배지 한 칸(실플레이 피드백 ②). 이번 턴 행동 의도(칼+피해,
    /// 다단 히트 ×N)와 몸에 걸린 상태이상·버프를 한 줄에 모은다 — 예전에는 피해가 타일 오버레이
    /// 숫자로, 상태가 체력바 아래 아이콘으로 흩어져 있어 "이 놈이 지금 뭘 하려는가"를 두 군데서
    /// 읽어야 했다.
    ///
    /// <para>🔑 피해는 <b>반드시</b> <c>MonsterIntentPreview.AttackPatternDamage</c>에서 온다 —
    /// 집행과 같은 함수를 지나온 R-8 실효값이라 약오름·쇠약·허점이 붙어도 화면과 규칙이 갈라지지
    /// 않는다. CSV 저작값(<c>pattern.Damage</c>)을 찍으면 그 순간 화면이 거짓말을 한다.</para>
    ///
    /// <para>렌더 방식(스프라이트/글리프/숫자)과 툴팁 문구는 이 구조체가 들고 있지 않다 —
    /// 각각 <see cref="CombatActorMarkerPresenter"/>와 <see cref="ObjectInfoTooltipContent"/> 소관이다.</para>
    /// </summary>
    internal readonly struct MonsterNameplateBadge
    {
        private MonsterNameplateBadge(
            MonsterNameplateBadgeKind kind, int amount, ActiveEffect effect,
            StatusEffectKind selfBuffKind = default, int hitCount = 1, string gimmickId = null,
            string traitId = null, bool showsNumber = false, bool dimmed = false)
        {
            Kind = kind;
            Amount = amount;
            Effect = effect;
            SelfBuffKind = selfBuffKind;
            HitCount = hitCount < 1 ? 1 : hitCount;
            GimmickId = gimmickId ?? string.Empty;
            TraitId = traitId ?? string.Empty;
            ShowsNumber = showsNumber;
            Dimmed = dimmed;
        }

        public MonsterNameplateBadgeKind Kind { get; }

        /// <summary>공격=실효 피해, 다단=히트 수, 약오름=스택, 밀치기=칸수(부호 = §16.1 규약).</summary>
        public int Amount { get; }

        /// <summary><see cref="MonsterNameplateBadgeKind.Status"/>일 때만 채워진다.</summary>
        public ActiveEffect Effect { get; }

        /// <summary><see cref="MonsterNameplateBadgeKind.SelfBuffStatus"/>일 때만 채워진다 —
        /// 아직 걸리지 않은 <b>예고</b>라 ActiveEffect가 없고 종류만 안다.</summary>
        public StatusEffectKind SelfBuffKind { get; }

        /// <summary><see cref="MonsterNameplateBadgeKind.BossGimmick"/>일 때만 채워진다 —
        /// 이번 턴 공격을 대체하는 기믹 id(<c>boss_profiles.csv</c> mechanicId).</summary>
        public string GimmickId { get; }

        /// <summary>
        /// <see cref="MonsterNameplateBadgeKind.Trait"/>일 때의 monster_traits.csv traitId.
        /// 색·글리프·정렬·툴팁·오버레이가 <b>전부</b> 이 한 값에서 유도되므로, 새 특성이 늘어도
        /// enum이 늘지 않는다 — 종전에는 특성 하나에 enum 1 + switch 6벌이었다.
        /// </summary>
        public string TraitId { get; }

        /// <summary>특성 배지 옆에 <see cref="Amount"/>를 찍는가. 숫자의 뜻은 특성마다 다르므로
        /// (스택·잠근 장수·훔친 액수) 찍을지 말지는 저작을 아는 조립부가 정한다.</summary>
        public bool ShowsNumber { get; }

        /// <summary>지금은 일하지 않는 상태인가(맷집 소진 등). 색을 눌러 「지금 때리면 온전히 들어간다」를 말한다.</summary>
        public bool Dimmed { get; }

        /// <summary>
        /// 이번 턴 공격의 반복 횟수(<see cref="MonsterNameplateBadgeKind.Attack"/> 전용, 기본 1).
        ///
        /// <para>2026-09-02 #10 사용자 확정: <b>연속 공격 배지를 없애고 ×N 표기만 남긴다.</b> 별도 칩이던
        /// 다단 히트를 공격 배지의 <b>글자</b>로 접어 넣는다 — ×N은 바로 옆 피해 숫자를 곱하는 값이라
        /// 떨어져 있을 이유가 없고, 칩 하나가 줄어 이름 위가 덜 붐빈다.</para>
        ///
        /// <para>⚠️ <see cref="MonsterNameplateBadgeKind.MultiHit"/>은 <b>지우지 않는다</b> —
        /// 이 enum은 append-only이고(2026-08-19 중간 삽입 오바인딩 실증) 카탈로그·정렬 표가 인덱스에
        /// 기대고 있다. 발행을 멈출 뿐이다.</para>
        /// </summary>
        public int HitCount { get; }

        public static MonsterNameplateBadge Attack(int effectiveDamage, int hitCount = 1) =>
            new MonsterNameplateBadge(MonsterNameplateBadgeKind.Attack, effectiveDamage, default, hitCount: hitCount);

        public static MonsterNameplateBadge MultiHit(int hitCount) =>
            new MonsterNameplateBadge(MonsterNameplateBadgeKind.MultiHit, hitCount, default);

        public static MonsterNameplateBadge Knockback(int distance) =>
            new MonsterNameplateBadge(MonsterNameplateBadgeKind.Knockback, distance, default);

        /// <summary>
        /// 방어막(<c>CombatantState.Block</c>) — 플레이어 방어도와 <b>같은 필드</b>다. 상태이상이 아니라
        /// 몸에 붙은 숫자라 정화가 닿을 표면이 없다(사용자 확정 2026-08-10).
        /// </summary>
        public static MonsterNameplateBadge Shield(int block) =>
            new MonsterNameplateBadge(MonsterNameplateBadgeKind.Shield, block, default);

        public static MonsterNameplateBadge Agitation(int stacks) =>
            new MonsterNameplateBadge(MonsterNameplateBadgeKind.Agitation, stacks, default);

        /// <summary>
        /// 맷집. <b>소진 상태도 정보다</b>("지금 때리면 온전히 들어간다") — 그래서 저작만 있으면
        /// 항상 띄우고 준비/소진을 색으로 가른다(툴팁 칩과 같은 규약).
        /// </summary>
        /// <summary>견고 — 저작만 있으면 항상 띄운다(방어막이 없을 때도 "이 놈은 굳으면 안 풀린다"가 정보다).</summary>
        public static MonsterNameplateBadge Sturdy() =>
            new MonsterNameplateBadge(MonsterNameplateBadgeKind.Sturdy, 0, default);

        public static MonsterNameplateBadge Toughness(bool ready) =>
            new MonsterNameplateBadge(MonsterNameplateBadgeKind.Toughness, ready ? 1 : 0, default);

        /// <summary>뒤끝 — 죽을 때 마지막 수를 남기는 특성. 견고처럼 저작만 있으면 상시 표시(2026-08-19 #14).</summary>
        public static MonsterNameplateBadge Aftermath() =>
            new MonsterNameplateBadge(MonsterNameplateBadgeKind.Aftermath, 0, default);

        /// <summary>
        /// 특성 배지(2026-09-04). 견고·맷집·약오름·뒤끝의 전용 종류를 <b>대체</b>한다 —
        /// 그 넷은 enum append-only 규약 때문에 남겨 두지만 더 이상 발행되지 않는다(다단 히트 선례).
        /// </summary>
        public static MonsterNameplateBadge Trait(
            string traitId, int amount = 0, bool showsNumber = false, bool dimmed = false) =>
            new MonsterNameplateBadge(
                MonsterNameplateBadgeKind.Trait, amount, default,
                traitId: traitId, showsNumber: showsNumber, dimmed: dimmed);

        public static MonsterNameplateBadge Status(ActiveEffect effect) =>
            new MonsterNameplateBadge(MonsterNameplateBadgeKind.Status, effect.Amount, effect);

        /// <summary>
        /// 자기부여 의도(2026-08-20 #16) — 이번 턴 자신에게 걸 상태이상 예고. Amount = 지속 턴.
        /// 걸린 뒤에는 <see cref="Status"/> 배지로 넘어가므로 이 배지는 예고 턴에만 산다.
        /// </summary>
        public static MonsterNameplateBadge SelfBuffStatus(StatusEffectKind kind, int durationTurns) =>
            new MonsterNameplateBadge(MonsterNameplateBadgeKind.SelfBuffStatus, durationTurns, default, kind);

        /// <summary>자기부여 의도 — 이번 턴 두를 방어막 예고. 현재 잔량(<see cref="Shield"/>)과
        /// 구별하려고 숫자에 +가 붙는다.</summary>
        public static MonsterNameplateBadge SelfBuffShield(int amount) =>
            new MonsterNameplateBadge(MonsterNameplateBadgeKind.SelfBuffShield, amount, default);

        /// <summary>
        /// 보스 기믹 의도(2026-09-03 ⑥) — 이번 턴 일반 공격을 <b>대체</b>하는 기믹(살포·함정 배치·전멸기).
        /// 기믹 턴에는 공격 배지가 서지 않으므로 이 배지가 "이번 턴 무엇을 하는가"의 유일한 답이다.
        /// </summary>
        public static MonsterNameplateBadge BossGimmick(string mechanicId) =>
            new MonsterNameplateBadge(MonsterNameplateBadgeKind.BossGimmick, 0, default, gimmickId: mechanicId);

        /// <summary>
        /// 기물(철조각) 성숙 카운트다운(2026-09-03 ⑥) — Amount = 흡수까지 남은 몬스터 페이즈 수
        /// (0 = 다음 몬스터 페이즈에 흡수·폭발). "3턴 안에 부순다"가 이 보스전의 핵심 퍼즐인데
        /// 지금까지는 호버 툴팁으로만 읽혔다 — 상시 배지로 격상한다.
        /// </summary>
        public static MonsterNameplateBadge PropMaturity(int turnsRemaining) =>
            new MonsterNameplateBadge(MonsterNameplateBadgeKind.PropMaturity, turnsRemaining < 0 ? 0 : turnsRemaining, default);
    }

    internal enum MonsterNameplateBadgeKind
    {
        /// <summary>이번 턴 공격 의도 + 실효 피해.</summary>
        Attack,

        /// <summary>다단 히트(×N). 히트당 피해는 <see cref="Attack"/> 배지가 들고 있다.</summary>
        MultiHit,

        /// <summary>밀치기(+)·끌어당김(−).</summary>
        Knockback,

        /// <summary>방어막(Block 잔량). 상태이상이 아니라 몸에 붙은 숫자다.</summary>
        Shield,

        /// <summary>약오름 스택. ActiveEffect가 아니라 몬스터 몸에 상시로 붙는 값이라 별도 종류다.</summary>
        Agitation,

        /// <summary>맷집(준비/소진). 약오름과 같은 이유로 별도 종류다.</summary>
        Toughness,

        /// <summary>견고 — 방어막이 턴 소멸을 받지 않는 특성.</summary>
        Sturdy,

        /// <summary>뒤끝 — 죽을 때 마지막 수(장판·폭발·디버프)를 남기는 특성.</summary>
        Aftermath,

        /// <summary>몸에 걸린 상태이상·버프.</summary>
        Status,

        /// <summary>자기부여 의도 — 이번 턴 자신에게 걸 상태이상(예고). 새 종류는 끝에만 붙인다(append-only).</summary>
        SelfBuffStatus,

        /// <summary>자기부여 의도 — 이번 턴 두를 방어막(예고).</summary>
        SelfBuffShield,

        /// <summary>보스 기믹 의도 — 이번 턴 일반 공격을 대체하는 기믹(예고). append-only 유지.</summary>
        BossGimmick,

        /// <summary>기물(철조각) 성숙 카운트다운 — Amount = 흡수까지 남은 몬스터 페이즈 수.</summary>
        PropMaturity,

        /// <summary>
        /// 몬스터 특성(monster_traits.csv). <see cref="MonsterNameplateBadge.TraitId"/>가 어느 특성인지
        /// 말하고 나머지는 전부 카탈로그가 정한다. ⚠️append-only — 반드시 끝에만 붙인다.
        ///
        /// <para>⚠️ <see cref="Sturdy"/>·<see cref="Agitation"/>·<see cref="Toughness"/>·
        /// <see cref="Aftermath"/>는 <b>지우지 않는다</b>(직렬화·정렬표가 인덱스에 기댄다). 발행만 멈춘다.</para>
        /// </summary>
        Trait,
    }
}

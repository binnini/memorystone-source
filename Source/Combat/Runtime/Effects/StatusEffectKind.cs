namespace SeoulPlayup.Combat.Runtime
{
    public enum StatusEffectKind
    {
        Immobilize,
        Poison,
        Stun,
        Slow,
        Rupture,
        Reflect,
        Agility,

        /// <summary>
        /// 강화(Strength): 보유 유닛의 가하는 피해를 증가시키는 버프(몬스터 outgoing +100%). 직렬화 enum 값을
        /// 안정적으로 유지하기 위해 맨 뒤에 추가한다.
        /// </summary>
        Strength,

        /// <summary>
        /// 실명(Blind): 보유한 플레이어의 시야 범위를 Amount만큼 줄이는 디버프(D-7: 기본 −1).
        /// 시야 합산은 <c>CombatState.GetEffectivePlayerVisionRange</c> 한 곳에서만 이뤄지고,
        /// 부여/만료/정화 시 <c>RefreshPlayerVision()</c>이 함께 돌아야 안개가 즉시 따라온다.
        /// 직렬화 enum 값을 안정적으로 유지하기 위해 맨 뒤에 추가한다.
        /// </summary>
        Blind,

        /// <summary>
        /// 무장 해제(Disarm, C-5 / D-11): 공격 카드만 사용할 수 없다 — 이동·방어·정찰·유틸리티는 그대로.
        /// 기절과 같은 게이트 문법(<c>CombatState.IsBlockedByDisarm</c>)을 쓰되 <b>하드CC가 아니다</b>:
        /// <c>IsMonsterControlStatus</c>에 넣지 않으므로 만료 후 재적용 면역 창을 받지 않고(부분 봉인이라
        /// 연쇄를 허용한다), 몬스터 의도 취소 텍스트도 뜨지 않는다.
        /// Amount를 읽지 않는 존재형 상태다(valueMode None).
        /// 직렬화 enum 값을 안정적으로 유지하기 위해 맨 뒤에 추가한다.
        /// </summary>
        Disarm,

        /// <summary>
        /// 쇠약(Weaken, C-2 / D-10 / O-10): 보유 유닛이 <b>가하는</b> 피해가 Amount% 줄어든다(기본 30).
        /// 강화(<see cref="Strength"/>)와 같은 축의 음수 대칭이라 소비 지점을 공유한다 —
        /// 부호는 축 이름(<c>DamageDealtBonusPercent</c> vs <c>DamageDealtPenaltyPercent</c>)이 들고 있으므로
        /// 저작 amount에 음수를 넣어 뒤집을 수 없다.
        /// 직렬화 enum 값을 안정적으로 유지하기 위해 맨 뒤에 추가한다.
        /// </summary>
        Weaken,

        /// <summary>
        /// 허점(Vulnerable, C-3 / D-10 / O-10): 보유 유닛이 <b>받는 타격 1회당</b> 피해가 Amount만큼
        /// 늘어난다(기본 +2). ⚠️<b>배율이 아니라 고정치다</b> — StS Vulnerable(×1.5)과 기계적으로 차별하기 위한
        /// D-10의 결정이고, 그래서 다타 카드와 시너지가 난다(타격마다 붙는다). 배율로 되돌리지 말 것.
        /// 직렬화 enum 값을 안정적으로 유지하기 위해 맨 뒤에 추가한다.
        /// </summary>
        Vulnerable,

        /// <summary>
        /// 봉인(Seal, C-16 / D-16): <b>Amount = 봉인 장수</b>. 손패의 카드 그만큼이 사용 불가로 잠긴다.
        ///
        /// 어느 카드가 잠기는지는 저장하지 않는다 — <c>(카드 InstanceId, OverallTurnNumber)</c>에서
        /// 결정적으로 유도한다(O-11 '재선정'). 그래서 ①매 턴 다시 뽑히고 ②손패가 바뀌면 즉시 따라오며
        /// ③"카드를 한 장 내서 봉인을 리롤한다"가 불가능하다(남은 카드의 순위가 변하지 않으므로).
        /// 손패에 있는 '정전' 상태 카드도 같은 장수 계산에 더해진다(C-17).
        /// 직렬화 enum 값을 안정적으로 유지하기 위해 맨 뒤에 추가한다.
        /// </summary>
        Seal,

        /// <summary>
        /// 횃불(TorchLight, C-14 / D-15): 시야 반경 +Amount. 실명의 대칭이지만 <b>수명 규칙이 다르다</b> —
        /// 매 턴 반경이 1씩 줄어들며 타 없어진다(소모성 시야). 부여 시 지속시간 = 초기 반경이라
        /// 두 시계가 함께 0에 닿는다.
        /// 유일한 Buff 계열 시야 상태라 <c>RefreshPlayerVisionIfVisionStatus</c>에 kind 하나만 더하면 됐다.
        /// 직렬화 enum 값을 안정적으로 유지하기 위해 맨 뒤에 추가한다.
        /// </summary>
        TorchLight,

        /// <summary>
        /// 보스 아우라(BossAura): 보스의 현재 페이즈가 부여하는 존재형 상태. <b>상태이상 시스템으로는
        /// 취급하지 않는다</b> — <c>ActiveEffect</c>로 부여되지 않고 페이즈 트랙에서 결정적으로 투영되어
        /// 아이콘/루프 VFX만 따라온다(제어·의도 취소 대상이 아님, docs/boss-raid-plan.md §8-2).
        /// 이 값은 단 두 소비처에서만 쓰인다: (1) <c>boss_phases.csv</c>의 <c>auraStatusKind</c> 컬럼,
        /// (2) <c>status.loop.BossAura</c> VFX 카탈로그 매칭 키.
        /// 직렬화 enum 값을 안정적으로 유지하기 위해 맨 뒤에 추가한다.
        /// </summary>
        BossAura,

        /// <summary>
        /// 힘(Might, D-2/D-3): 가하는 피해에 더해지는 <b>고정치</b>이며 <b>런 전체</b> 유지된다.
        /// 강화(<see cref="Strength"/>)와는 다른 축이다 — 강화는 퍼센트·턴 한정, 힘은 고정치·영구다.
        ///
        /// <para><b>상태이상 시스템으로는 취급하지 않는다</b> — <c>ActiveEffect</c>로 부여되지 않고
        /// <c>PlayerInventoryState.MightStacks</c>가 정본이다. 이 enum 값이 존재하는 이유는 단 하나,
        /// 상태이상 HUD 독에 아이콘/툴팁으로 <b>투영</b>하기 위해서다(D-9) —
        /// <see cref="BossAura"/>가 페이즈 트랙에서 투영되는 것과 같은 패턴이다.
        /// 그래서 턴 틱·정화·전투 스냅샷 어디에도 걸리지 않는다.</para>
        ///
        /// <para>소비 지점은 <c>CombatState.GetFlatAttackDamageBonus</c> 하나이고, 그 자리는
        /// 배율(강화·쇠약)보다 <b>앞</b>이며 <b>타격마다</b> 불린다 — D-1(힘을 먼저 더하고 %를 곱한다)과
        /// D-2(타격당)가 그 위치 하나로 동시에 만족된다.</para>
        ///
        /// 직렬화 enum 값을 안정적으로 유지하기 위해 맨 뒤에 추가한다.
        /// </summary>
        Might,

        /// <summary>
        /// 미지(Unknown, D-4/D-5): <b>몬스터 전용 버프</b>. 이 몬스터의 이동 예고와 공격 범위 예고가
        /// 화면에서 사라진다. 규칙은 그대로 굴러간다 — 몬스터는 평소처럼 이동하고 공격한다.
        /// <b>안 보일 뿐이다.</b>
        ///
        /// <para>🔴 기절(<see cref="Stun"/>)과 혼동하지 말 것. 기절도 예고를 비우지만 그건
        /// <c>IsMonsterAttackBlocked</c>가 <b>실제로 공격을 막기</b> 때문이다. 미지는 막지 않는다 —
        /// 그래서 예고 은폐 술어(<c>IsMonsterIntentHidden</c>)를 따로 세웠다. 둘을 합치면
        /// 미지 몬스터가 공격을 못 하게 된다.</para>
        ///
        /// <para>은폐가 성립하려면 새는 구멍을 전부 막아야 한다(D-5: 완전 은폐) — 예고 오버레이,
        /// 호버 시 단일 몬스터 예고, 몬스터 툴팁의 공격 패턴·부여 정보까지다. 대신 몬스터 머리 위에
        /// 이 아이콘이 떠서 "가려져 있다"는 사실 자체는 알린다.</para>
        ///
        /// 직렬화 enum 값을 안정적으로 유지하기 위해 맨 뒤에 추가한다.
        /// </summary>
        Unknown,

        /// <summary>
        /// 수호(Guard, T2 페이즈 C): <b>Amount = 남은 충전 수</b>. 플레이어에게 해로운 상태이상
        /// (<see cref="StatusEffectInfo.IsCleansable"/> 기준)이 부여되려는 순간 충전 1을 소모해 무효로
        /// 만든다(StS Artifact). 카운터 스택 문법의 첫 사례다 — 턴으로 만료되지 않고
        /// (<c>expirePolicy=OnConsume</c>) 소비로만 줄어든다. 소비 지점은
        /// <c>CombatState.AddDurationStatusEffectCore</c> 단일 관문이며, D04/D06의 지연 자기 속박은
        /// 그 관문을 구조적으로 우회하므로(<c>ApplyPendingSelfImmobilize</c>) 수호에 막히지 않는다
        /// — 카드가 청구한 비용은 무효화 대상이 아니다(D6 정화 불가 규칙과 같은 결).
        /// 직렬화 enum 값을 안정적으로 유지하기 위해 맨 뒤에 추가한다.
        /// </summary>
        Guard,

        /// <summary>
        /// 은신(Stealth, T2 페이즈 C): 보유한 플레이어가 몬스터에게 <b>감지되지 않는다</b>(존재형).
        /// 소비 지점은 몬스터 AI의 감지 판정 하나다 — <c>StealthMaskingMonsterAi</c>가 FSM 컨텍스트에
        /// <c>PlayerHidden</c>을 실어, 감지 전이(순찰→추격)가 불성립하고 이미 추격/공격 중이던 몬스터는
        /// 플레이어를 놓친 것으로 판정된다(추격→수색, 마지막 목격 좌표는 갱신되지 않는다).
        /// ⚠️<c>ActivityState</c>(Dormant 각성)는 건드리지 않는다 — 그건 감지가 아니라 시뮬레이션
        /// 분류라서, 은신 중에도 몬스터는 깨어나 순찰한다. 안 쫓아올 뿐이다.
        /// 직렬화 enum 값을 안정적으로 유지하기 위해 맨 뒤에 추가한다.
        /// </summary>
        Stealth,

        /// <summary>
        /// 무적(Invincible, WS-I I-19 / DEC-2026-08-19-08): 이번 턴 적의 공격 피해를 0으로 만든다(D02·D05).
        /// <b>판정의 정본은 이 상태가 아니라</b> <c>PendingEffects.IncomingDamageNullifiedThisMonsterAction</c>
        /// 불리언이다(턴 시작 리셋) — 이 값은 아이콘·툴팁·플로팅을 위해 같은 수명(1턴)으로 따라붙는
        /// <b>표시용 투영</b>이다(힘·보스 기운과 같은 패턴). 버프라 정화 대상이 아니므로 두 상태가
        /// 갈라질 경로가 없다.
        /// 직렬화 enum 값을 안정적으로 유지하기 위해 맨 뒤에 추가한다(cs:846 드리프트 교훈 — append-only).
        /// </summary>
        Invincible
    }
}

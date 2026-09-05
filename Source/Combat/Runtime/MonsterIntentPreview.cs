using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    public readonly struct MonsterIntentPreview
    {
        public MonsterIntentPreview(
            string monsterId,
            string definitionId,
            string spawnRefId,
            HexCoord currentCoord,
            HexCoord predictedMoveCoord,
            IEnumerable<HexCoord> attackRangeCoords,
            EnemyIntentType intentType,
            string attackPatternId = "",
            string attackPatternDisplayName = "",
            int attackPatternDamage = 0,
            int attackPatternRange = 1,
            int attackPatternAreaRadius = 0,
            string attackPatternEffectRef = "",
            IEnumerable<StatusEffectKind> attackPatternStatusEffects = null,
            int attackPatternStatusEffectDurationTurns = 0,
            int attackPatternStatusEffectAmount = 0,
            int attackPatternKnockbackDistance = 0,
            bool isIntentHidden = false,
            bool isIntentVeiledByStatus = false,
            int attackPatternShieldGain = 0,
            bool isSelfBuffIntent = false,
            IReadOnlyList<HexCoord> summonCoords = null,
            bool willAttackPlayer = false,
            HexCoord? advanceAfterAttackCoord = null,
            bool injectsCurse = false,
            IReadOnlyList<string> bossGimmickIds = null)
        {
            MonsterId = monsterId ?? string.Empty;
            DefinitionId = definitionId ?? string.Empty;
            SpawnRefId = spawnRefId ?? string.Empty;
            CurrentCoord = currentCoord;
            PredictedMoveCoord = predictedMoveCoord;
            AttackRangeCoords = attackRangeCoords == null
                ? Array.Empty<HexCoord>()
                : attackRangeCoords.ToArray();
            IntentType = intentType;
            AttackPatternId = attackPatternId ?? string.Empty;
            AttackPatternDisplayName = attackPatternDisplayName ?? string.Empty;
            AttackPatternDamage = attackPatternDamage;
            AttackPatternRange = attackPatternRange;
            AttackPatternAreaRadius = attackPatternAreaRadius;
            AttackPatternEffectRef = attackPatternEffectRef ?? string.Empty;
            AttackPatternStatusEffects = attackPatternStatusEffects == null
                ? Array.Empty<StatusEffectKind>()
                : attackPatternStatusEffects.ToArray();
            AttackPatternStatusEffectDurationTurns = Math.Max(0, attackPatternStatusEffectDurationTurns);
            AttackPatternStatusEffectAmount = Math.Max(0, attackPatternStatusEffectAmount);
            // 🔴 클램프하지 않는다 — 부호가 곧 방향이다(§16.1: 양수=밀치기 · 음수=끌어당김).
            // 예전에는 Math.Max(0, …)로 눌러 끌어당김을 "넉백 없음"으로 만들었고, 그래서 타일
            // 오버레이가 끌어당기는 공격에도 미는 그림을 띄웠다. 크기는 절댓값으로 읽는다.
            AttackPatternKnockbackDistance = attackPatternKnockbackDistance;
            IsIntentHidden = isIntentHidden;
            IsIntentVeiledByStatus = isIntentVeiledByStatus;
            SummonCoords = summonCoords ?? System.Array.Empty<HexCoord>();
            AttackPatternShieldGain = Math.Max(0, attackPatternShieldGain);
            IsSelfBuffIntent = isSelfBuffIntent;
            WillAttackPlayer = willAttackPlayer;
            AdvanceAfterAttackCoord = advanceAfterAttackCoord;
            InjectsCurse = injectsCurse;
            BossGimmickIds = bossGimmickIds ?? Array.Empty<string>();
        }

        /// <summary>
        /// 이 몬스터의 예고가 미지로 가려졌는가. true면 이동·공격 좌표와 패턴 정보가 <b>모두 비어 있다</b> —
        /// 오버레이가 그릴 것이 없다는 뜻이고, 대신 몬스터 자기 좌표에 '?' 표식을 띄우라는 신호다.
        /// 예고를 못 내는 다른 이유(기절)와 구별하려고 별도 축으로 싣는다: 기절은 공격이 실제로
        /// 취소된 것이라 표식을 띄우면 안 된다.
        /// </summary>
        public bool IsIntentHidden { get; }

        /// <summary>
        /// 은폐가 <b>「미지」 상태이상 때문인가</b>(은신은 여기 없다). 두 축을 가르는 이유는
        /// <c>CombatState.IsMonsterIntentVeiledByStatus</c>에 적힌 그대로다: 예고를 지우는 판정은
        /// 합친 술어를 쓰지만, 화면에 <b>'?' 표식</b>을 세우는 것은 이 술어여야 한다.
        ///
        /// <para>🔴 2026-09-05 실플레이 버그: 타일 오버레이가 합친 술어를 읽어서, 마커를 감춰 놓은
        /// 은신 몬스터의 <b>자기 좌표</b>에 물음표를 세우고 있었다 — 은신의 값이 통째로 새는
        /// 종류의 누수다. 네임플레이트 쪽은 이미 갈라져 있었고 타일 쪽만 남아 있었다.</para>
        /// </summary>
        public bool IsIntentVeiledByStatus { get; }

        /// <summary>
        /// 소환 예고 칸(요괴 §4-4 · 「여기 나타남」). 🔴 위험 예고(<see cref="AttackRangeCoords"/>)와
        /// <b>다른 채널</b>이다 — 붉은 위험 해치의 어휘는 "밟으면 아픈 칸"인데 소환 자리는 아프지 않고,
        /// 대신 다음 턴부터 적이 서 있다. 색만 바꾸고 같은 채널에 실으면 두 뜻이 한 어휘를 나눠 쓴다.
        /// </summary>
        public IReadOnlyList<HexCoord> SummonCoords { get; }

        public string MonsterId { get; }
        public string DefinitionId { get; }
        public string SpawnRefId { get; }
        public HexCoord CurrentCoord { get; }
        public HexCoord PredictedMoveCoord { get; }
        public IReadOnlyList<HexCoord> AttackRangeCoords { get; }
        public EnemyIntentType IntentType { get; }
        public string AttackPatternId { get; }
        public string AttackPatternDisplayName { get; }
        public int AttackPatternDamage { get; }
        public int AttackPatternRange { get; }
        public int AttackPatternAreaRadius { get; }
        public string AttackPatternEffectRef { get; }
        public IReadOnlyList<StatusEffectKind> AttackPatternStatusEffects { get; }
        public int AttackPatternStatusEffectDurationTurns { get; }
        public int AttackPatternStatusEffectAmount { get; }
        /// <summary>
        /// 이 공격이 적중할 때 플레이어가 밀려나는(양수) / 끌려오는(음수) 칸 수 · 0 = 변위 없음
        /// (§16.1 부호 규약 · <see cref="MonsterAttackPattern.KnockbackDistance"/>와 같은 값).
        /// </summary>
        public int AttackPatternKnockbackDistance { get; }

        /// <summary>이 패턴이 성립할 때 몬스터가 두르는 방어막(<see cref="MonsterAttackPattern.ShieldGain"/>).</summary>
        public int AttackPatternShieldGain { get; }

        /// <summary>
        /// 이번 턴 예고된 행동이 <b>자기부여 버프</b>인가(2026-08-20 #16). 자기부여 패턴은 커버 판정을
        /// 건너뛰어 이동 FSM 의도가 Chase/Search로 남으므로, <see cref="IntentType"/>만 보는 소비자는
        /// 이 턴을 "예고 없음"으로 오독한다 — 배지·오버레이는 이 축을 따로 읽어야 한다.
        /// </summary>
        public bool IsSelfBuffIntent { get; }

        /// <summary>
        /// 이번 턴 이 몬스터의 <b>확정된</b> 공격 범위가 플레이어를 덮는가(규칙층 <c>PendingAttackIntent</c>).
        ///
        /// <para>🔴 <see cref="IntentType"/>으로는 이것을 알 수 없다 — 그 값은 <b>이동</b> 의도라서
        /// 걸어와서 때리는 몬스터가 <c>Chase</c>로 남는다. 이름표 배지가 <c>IntentType == Attack</c>만
        /// 보다가 「걸어와서 순수 피해를 주는」 공격의 의도 배지를 통째로 빠뜨린 것이 2026-09-01 #1이다
        /// (자기부여 패턴이 같은 이유로 빠졌던 2026-08-20 #16의 재현 — 그때는 갈래를 하나 더 만들어
        /// 막았고, 이번에는 <b>규칙층이 이미 들고 있던 답</b>을 그대로 싣는다).</para>
        /// </summary>
        public bool WillAttackPlayer { get; }

        /// <summary>
        /// 공격 뒤 <b>한 칸 더</b> 다가올 자리(2026-09-01 #19 · 두억시니의 축). 없으면 null.
        ///
        /// <para>🔴 규칙은 예전부터 이렇게 움직였는데 <b>예고에는 없었다</b> — 「이동 1이라 뒤로 도는
        /// 것이 답인데 밀어붙이기가 그 반 걸음을 되찾아 온다」는 저작 의도가 화면에서 안 읽혔다.
        /// 값은 집행과 <b>같은 술어</b>(<c>TryResolveAdvanceAfterAttack</c>)가 계산하므로 예고 칸과
        /// 실제 도착 칸이 갈라질 수 없다.</para>
        /// </summary>
        public HexCoord? AdvanceAfterAttackCoord { get; }

        /// <summary>
        /// 이 공격이 닿으면 <b>덱에 저주 카드가 섞이는가</b>(2026-09-01 W2).
        ///
        /// <para>🔴 상태이상(<see cref="AttackPatternStatusEffects"/>)이 아니다 — 저주는 턴이 지나면
        /// 풀리는 것이 아니라 <b>덱에 영구히 남는 오염</b>이라, 상태이상 축에 실으면 "몇 턴 뒤 사라진다"는
        /// 어휘로 잘못 읽힌다. 그래서 넉백처럼 자기 축으로 탄다.</para>
        ///
        /// <para>저작 표면은 패턴의 <c>injectStatusCardId</c>(A028 쇳가루 휩쓸기 = X08 고정) ·
        /// <c>injectStatusCardPool</c>(A035 속삭임 = X05;X07;X12 중 한 장)이다. 어느 카드가 뽑히는지는
        /// 예고가 말하지 않는다 — 풀 추첨은 명중 시점에 일어나므로, 화면이 확정을 흉내내면 거짓말이 된다.</para>
        /// </summary>
        public bool InjectsCurse { get; }

        /// <summary>
        /// 이번 턴 이 보스의 일반 공격을 <b>대체</b>하는 기믹 id들(살포·함정 배치·전멸기 — 2026-09-03
        /// 피드백 ⑥). 기믹 턴에는 공격 예고가 통째로 비므로 이 축이 없으면 "보스가 아무것도 안 한다"로
        /// 읽힌다. 값은 공격 차단과 같은 순수 질의에서 오므로 예고=행동이 보장된다. 보스가 아니면 빈 목록.
        /// </summary>
        public IReadOnlyList<string> BossGimmickIds { get; }

        /// <summary>밀치기든 끌어당김이든 변위가 예고되어 있는가(방향은 부호가 말한다).</summary>
        public bool HasKnockback => AttackPatternKnockbackDistance != 0;

        /// <summary>예고된 변위가 <b>끌어당김</b>인가(§16.1).</summary>
        public bool IsPull => AttackPatternKnockbackDistance < 0;
        public bool WillMove => PredictedMoveCoord != CurrentCoord;
    }
}

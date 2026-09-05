using System.Collections.Generic;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    public readonly struct MonsterActionResolutionRecord
    {
        public MonsterActionResolutionRecord(
            string monsterId,
            MonsterActivityState activityBefore,
            HexCoord beforeCoord,
            HexCoord afterCoord,
            bool wasVisibleBefore,
            bool isVisibleAfter,
            bool attackedPlayer,
            bool affectedPlayer,
            int damageToPlayer,
            int actionOrder = -1,
            int attackOrder = -1,
            string attackPatternId = "",
            string attackAnimationTrigger = "",
            string presentationGroupId = "",
            IReadOnlyList<HexCoord> movePath = null,
            bool missedAttack = false,
            bool leapedMove = false,
            bool knockedBackPlayer = false,
            HexCoord? playerKnockbackFrom = null,
            HexCoord? playerKnockbackTo = null,
            bool visibleAtAttackTime = false,
            bool selfTargetedAttack = false,
            HexCoord? aimCoord = null,
            HexCoord? advancedFrom = null,
            HexCoord? advancedTo = null,
            bool hiddenByStealthDuringAction = false)
        {
            MonsterId = monsterId ?? string.Empty;
            ActivityBefore = activityBefore;
            BeforeCoord = beforeCoord;
            AfterCoord = afterCoord;
            WasVisibleBefore = wasVisibleBefore;
            IsVisibleAfter = isVisibleAfter;
            AttackedPlayer = attackedPlayer;
            AffectedPlayer = affectedPlayer;
            DamageToPlayer = damageToPlayer < 0 ? 0 : damageToPlayer;
            ActionOrder = actionOrder;
            AttackOrder = attackOrder;
            AttackPatternId = attackPatternId ?? string.Empty;
            AttackAnimationTrigger = attackAnimationTrigger ?? string.Empty;
            PresentationGroupId = presentationGroupId ?? string.Empty;
            MovePath = movePath ?? System.Array.Empty<HexCoord>();
            MissedAttack = missedAttack;
            LeapedMove = leapedMove;
            KnockedBackPlayer = knockedBackPlayer;
            PlayerKnockbackFrom = playerKnockbackFrom;
            PlayerKnockbackTo = playerKnockbackTo;
            VisibleAtAttackTime = visibleAtAttackTime;
            SelfTargetedAttack = selfTargetedAttack;
            AimCoord = aimCoord;
            AdvancedFrom = advancedFrom;
            AdvancedTo = advancedTo;
            HiddenByStealthDuringAction = hiddenByStealthDuringAction;
        }

        /// <summary>
        /// 「밀어붙이기」(요괴 §4-5)로 공격 <b>뒤에</b> 옮겨 간 칸. 없으면 전진하지 않은 것이다.
        /// <see cref="AfterCoord"/>는 전진 <b>전</b> 좌표라 공격 앵커와 걷기 비트가 흔들리지 않는다.
        /// </summary>
        public HexCoord? AdvancedFrom { get; }

        public HexCoord? AdvancedTo { get; }

        public string MonsterId { get; }
        public MonsterActivityState ActivityBefore { get; }
        public HexCoord BeforeCoord { get; }
        public HexCoord AfterCoord { get; }
        /// <summary>Ordered tile route (start..destination) the monster walked this turn, so the presentation
        /// layer can step it one hex at a time. Empty when no route was captured (falls back to a single hop).</summary>
        public IReadOnlyList<HexCoord> MovePath { get; }
        public bool WasVisibleBefore { get; }
        public bool IsVisibleAfter { get; }
        public bool Moved => BeforeCoord != AfterCoord;
        public bool AttackedPlayer { get; }
        public bool AffectedPlayer { get; }
        public int DamageToPlayer { get; }
        public int ActionOrder { get; }
        public int AttackOrder { get; }
        public string AttackPatternId { get; }
        public string AttackAnimationTrigger { get; }
        public string PresentationGroupId { get; }
        public bool MissedAttack { get; }
        /// <summary>이번 이동이 도약이었다(§28 W7) — 연출이 점프 비트를 낸다.</summary>
        public bool LeapedMove { get; }
        public bool KnockedBackPlayer { get; }
        public HexCoord? PlayerKnockbackFrom { get; }
        public HexCoord? PlayerKnockbackTo { get; }

        /// <summary>
        /// True when the monster's tile was Revealed at the moment its attack resolved (after monster movement,
        /// before any player knockback). Captures "보였던 공격" even for a monster that moved into view and was
        /// then knocked out of sight — its attack must still be presented to completion.
        /// </summary>
        public bool VisibleAtAttackTime { get; }

        /// <summary>
        /// True when this action resolved a self-targeted pattern (자기부여 버프). AttackedPlayer stays true so a
        /// visible monster still plays its wind-up/impact beats, but the pattern never touches the player —
        /// 암시야에서 "기습!"을 띄우면 거짓말이 된다.
        /// </summary>
        public bool SelfTargetedAttack { get; }

        /// <summary>
        /// 이 공격이 <b>예고(커밋) 시점에 겨눈</b> 칸(2026-08-20 #7). 규칙층은 이미 여기를 기준으로
        /// 형상 footprint를 굳히는데(<c>GetCommittedMonsterAttackFootprint</c>) 모델 회전만 집행 시점의
        /// 플레이어 실좌표를 봤다 — 플레이어가 예고 후 비켜서면 몸은 플레이어를 돌아보는데 판정은
        /// 예고 방향에 남아, 화면과 규칙이 서로 다른 방향을 가리켰다. 연출은 이 값을 봐야 한다.
        /// <see langword="null"/>이면 커밋 정보가 없는 경로(디버그 강제 공격 등)이므로 호출자가
        /// 종전대로 실좌표로 물러난다.
        /// </summary>
        public HexCoord? AimCoord { get; }

        /// <summary>
        /// 이 행동을 <b>은신한 채</b> 했다(2026-09-01 실플레이 #1). 안개 세 플래그는 셀 가시성만 보므로,
        /// 시야 안에 선 은신 몬스터는 「보이는 공격자」로 분류돼 걷기·휘두름 비트를 냈고 마커는 뷰가
        /// 감춰서 <b>아무도 없는데 피해만 뜨는</b> 화면이 됐다 — "기습!"도 그 분기에서만 나오므로 함께 죽어 있었다.
        ///
        /// <para>🔴 이 값은 <b>행동 시점</b>에 찍는다. 공격이 해소되면 은신이 풀리므로(노출 N턴),
        /// 그 뒤에 잰 <see cref="IsVisibleAfter"/>는 참이 되어 방금의 기습을 「보이던 공격」으로
        /// 되돌려 놓는다. 지금 보이느냐와 <b>그때 숨어 있었느냐</b>는 다른 질문이다.</para>
        /// </summary>
        public bool HiddenByStealthDuringAction { get; }

        /// <summary>
        /// True when the monster is shown to the player this turn: visible before its action, or revealed by
        /// where it ended up. Drives fog gating — a monster that stays hidden plays no move/attack animation.
        /// 은신은 안개와 <b>따로</b> 서는 술어라 여기서 곱해진다(뷰의 마커 판정과 같은 규칙).
        /// </summary>
        public bool IsMonsterVisible => !HiddenByStealthDuringAction && (WasVisibleBefore || IsVisibleAfter);

        /// <summary>
        /// True when the monster's attack should be presented as a visible attack (full wind-up/impact, even on
        /// a whiff): it was visible before, after, or at the attack moment. Differs from
        /// <see cref="IsMonsterVisible"/> by also honoring <see cref="VisibleAtAttackTime"/> so a monster
        /// visible from the player's original (pre-knockback) tile is never mistaken for a fog ambush.
        /// </summary>
        public bool IsAttackVisible =>
            !HiddenByStealthDuringAction && (WasVisibleBefore || IsVisibleAfter || VisibleAtAttackTime);

        public bool ShouldPresent =>
            IsMonsterVisible
            || MissedAttack
            || AttackedPlayer
            || AffectedPlayer;
    }
}

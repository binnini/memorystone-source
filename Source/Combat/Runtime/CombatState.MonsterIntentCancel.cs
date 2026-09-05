using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 「상태이상이 몬스터의 계획을 방금 취소했다」 공통 규칙(4-B · 2026-09-04, 옛 <c>CombatState.MonsterAi.cs</c>에서 본문 무변경 이동).
    /// 카드 효과 5곳·가방 아이템·정찰이 같은 3단(포착 → 제약 적용 → 취소 문안)을 부른다 — 파일 안에서 가장 넓게 교차되는 묶음이라
    /// AI 해소 코드와 떨어뜨려 둔다.
    /// </summary>
    public sealed partial class CombatState
    {
        internal void ApplyControlStatusConstraintToPlan(MonsterRuntime monster)
        {
            planner.ApplyControlStatusConstraintToPlan(monster);
        }

        // Delay before the intent-cancel cue floats, so it reads after the status-apply VFX lands.
        private const float IntentCancelTextDelaySeconds = 0.5f;

        // Snapshot a monster's pending intent (was it about to move / attack) before a control status is
        // applied, so EmitMonsterIntentCancelText can surface a "취소" cue for whatever was just cancelled.
        internal static (bool WasMoving, bool WasAttacking) CaptureMonsterIntentForCancel(MonsterRuntime monster)
        {
            if (monster == null)
            {
                return (false, false);
            }

            return (monster.IntentPredictedMoveCoord != monster.Coord, monster.PendingAttackIntent);
        }

        // Presentation-only: after a control status (속박/기절) cancels a monster's planned move/attack,
        // float a short "취소" cue. A cancelled attack is only surfaced for Stun (기절), since Immobilize
        // (속박) still lets the monster attack from its tile.
        internal void EmitMonsterIntentCancelText(MonsterRuntime monster, bool wasMoving, bool wasAttacking, StatusEffectKind statusKind)
        {
            if (monster == null || monster.Combatant.IsDead)
            {
                return;
            }

            var attackCancelled = wasAttacking && statusKind == StatusEffectKind.Stun;
            var amount = (wasMoving ? 1 : 0) | (attackCancelled ? 2 : 0);
            if (amount != 0)
            {
                RaiseEffect(EffectKind.AttackCancelled, monster.Coord, 0, amount, monster.Id, "intent.cancelled", monster.Id, "monster", "monster", delaySeconds: IntentCancelTextDelaySeconds);
            }
        }
    }
}

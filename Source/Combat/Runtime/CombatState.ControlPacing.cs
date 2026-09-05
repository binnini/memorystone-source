using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 제어 상태 페이싱(4-B · 2026-09-04, 옛 <c>CombatState.MonsterAi.cs</c>에서 본문 무변경 이동). 플레이어 하드 CC 재적용 면역 창과
    /// 턴 경계 틱(패턴 쿨다운·적 문법·은신·면역 감소)은 <b>플레이어 상태·턴 흐름 규칙</b>이지 몬스터 AI가 아니다 — 카드 효과·보스 기믹·
    /// 상태 부여 관문이 두루 부른다.
    /// </summary>
    public sealed partial class CombatState
    {
        private static bool IsMonsterControlStatus(StatusEffectKind kind)
        {
            return kind == StatusEffectKind.Immobilize || kind == StatusEffectKind.Stun;
        }

        // True while the player is still protected from RE-application of a hard control status that just
        // expired. Only hard control (stun/immobilize) grants immunity; DoT/debuffs (slow/poison/…) do not.
        private bool IsPlayerImmuneToControlStatus(StatusEffectKind kind)
        {
            return IsMonsterControlStatus(kind)
                && playerControlStatusImmunityTurns.TryGetValue(kind, out var remaining)
                && remaining > 0;
        }

        // Grant the player a short immunity window to re-application of a control status that just expired.
        private void RegisterPlayerControlStatusImmunity(StatusEffectKind kind)
        {
            if (!IsMonsterControlStatus(kind))
            {
                return;
            }

            playerControlStatusImmunityTurns[kind] = PlayerControlStatusImmunityTurns;
        }

        // Tick down per-turn timers that gate monster attack pattern reuse and player control-status immunity.
        // Runs once per monster turn boundary, before status expiry registers any new immunity window.
        private void TickControlPacingTimers()
        {
            foreach (var monster in monsters)
            {
                monster.TickAttackPatternCooldowns();
            }

            // 적 문법(T7-2) 턴 진행 — 쿨다운과 같은 경계·같은 순서를 공유한다(약오름 해소·맷집 재장전).
            TickEnemyGrammarPerTurn();
            TickMonsterStealthPerTurn();

            if (playerControlStatusImmunityTurns.Count == 0)
            {
                return;
            }

            foreach (var kind in playerControlStatusImmunityTurns.Keys.ToList())
            {
                var remaining = playerControlStatusImmunityTurns[kind] - 1;
                if (remaining <= 0)
                {
                    playerControlStatusImmunityTurns.Remove(kind);
                }
                else
                {
                    playerControlStatusImmunityTurns[kind] = remaining;
                }
            }
        }
    }
}

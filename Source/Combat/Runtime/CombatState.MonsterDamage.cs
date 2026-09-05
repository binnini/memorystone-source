using System;
using System.Linq;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 몬스터가 <b>맞는</b> 단일 관문(2026-09-05). 종전에는 <c>monster.Combatant.ApplyDamage(...)</c>가
    /// 열세 군데에 흩어져 있었고, 피격에 걸리는 규칙이 하나라도 생기면 그중 몇 곳만 고쳐지는 것이
    /// 시간 문제였다 — 이 저장소가 반복해서 밟은 함정이다(「순서가 아니라 구조가 지킨다」).
    ///
    /// <para>🔑 지금 이 관문에 걸린 규칙은 하나다: <b>맞으면 은신이 풀린다</b>. 플레이어의 부적이든
    /// 장판·함정이든 반사·가시든 「누가 때렸나」를 가리지 않는다 — 사용자 확정(2026-09-05):
    /// 「플레이어에게 또는 필드 오브젝트로 공격을 받거나 플레이어를 공격하면 곧바로 들킴」.</para>
    ///
    /// <para>🔴 <b>방어막에 전부 막혀도 드러난다.</b> 판정은 「HP가 줄었는가」가 아니라
    /// 「맞았는가」다 — 막혔다고 안 드러나면 방어막 있는 은신 몬스터가 때려도 안 보이는
    /// 다른 게임이 된다.</para>
    /// </summary>
    public sealed partial class CombatState
    {
        /// <summary>몬스터에게 피해를 준다. 실제로 들어간 피해(방어막 흡수 제외분)를 돌려준다.</summary>
        internal int DamageMonster(MonsterRuntime monster, int amount)
        {
            if (monster == null)
            {
                return 0;
            }

            var applied = monster.Combatant.ApplyDamage(amount);
            if (amount > 0)
            {
                RevealStealthMonsterOnHit(monster);
            }

            return applied;
        }

        /// <summary>
        /// 대상이 몬스터인지 모르는 자리(함정·필드 오브젝트는 플레이어도 때린다)에서 쓰는 갈래.
        /// 몬스터면 같은 관문을 지나고, 아니면 그냥 피해만 들어간다.
        /// </summary>
        internal int DamageCombatant(CombatantState combatant, int amount)
        {
            if (combatant == null)
            {
                return 0;
            }

            var monster = monsters.FirstOrDefault(candidate =>
                candidate?.Combatant != null
                && ReferenceEquals(candidate.Combatant, combatant));
            return monster != null ? DamageMonster(monster, amount) : combatant.ApplyDamage(amount);
        }
    }
}

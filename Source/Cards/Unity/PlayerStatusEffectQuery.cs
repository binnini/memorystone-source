using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Projects the player's active combat effects into the de-duplicated, ordered list the HUD
    /// presents. One entry per <see cref="EffectKind"/> (the longest-remaining instance wins),
    /// grouped debuffs-first then buffs (by <see cref="StatusEffectPolarity"/>) and ordered by kind
    /// within each group for stable slot assignment. Shared by the CardLane status-effect dock.
    /// </summary>
    public static class PlayerStatusEffectQuery
    {
        public static IReadOnlyList<ActiveEffect> GetPlayerStatusEffects(CombatState state)
        {
            if (state == null)
            {
                return Array.Empty<ActiveEffect>();
            }

            return state.ActiveEffects
                .Where(effect => effect.TargetUnitId == state.Player.Id && !effect.IsExpired)
                .Concat(ProjectRunPermanentEffects(state))
                .GroupBy(effect => effect.Kind)
                .Select(group => group.OrderByDescending(effect => effect.RemainingTurns).First())
                .OrderBy(effect => (int)StatusEffectInfo.GetPolarity(effect.Kind))
                .ThenBy(effect => effect.Kind)
                .ToArray();
        }

        /// <summary>
        /// 런 영구 효과를 상태이상 독에 <b>투영</b>한다(D-9: 영구 값도 별도 칸 없이 같은 독을 쓴다).
        /// <para>🔴 여기서 만드는 <see cref="ActiveEffect"/>는 <b>표시용 사본</b>이며
        /// <c>CombatState.activeEffects</c>에는 절대 들어가지 않는다. 넣는 순간 턴 틱이 지속시간을
        /// 깎고, 정화가 후보로 집고, 전투 스냅샷이 중복 저장하고, 피해 합산이 두 번 센다
        /// (힘의 정본은 <c>PlayerInventoryState.MightStacks</c> 하나다).
        /// <see cref="StatusEffectKind.BossAura"/>가 페이즈 트랙에서 투영되는 것과 같은 패턴이다.</para>
        /// <para>RemainingTurns를 0으로 두는 것이 "만료가 없다"의 표현이다 — 툴팁이 이 값을 보고
        /// 턴 수 대신 영구 표기를 낸다.</para>
        /// </summary>
        private static IEnumerable<ActiveEffect> ProjectRunPermanentEffects(CombatState state)
        {
            var might = state.PlayerInventory?.MightStacks ?? 0;
            if (might > 0)
            {
                yield return new ActiveEffect(
                    EffectType.Duration,
                    StatusEffectKind.Might,
                    state.Player.Id,
                    remainingTurns: 0,
                    amount: might,
                    sourceRef: "run.permanent");
            }
        }
    }
}

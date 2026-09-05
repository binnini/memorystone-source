using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 유물 트리거 규칙(4-B · 2026-09-04). 고슴도치 인형(인접 가시 반사)은 몬스터 AI가 아니라 유물 효과다 —
    /// 옛 <c>CombatState.MonsterAi.cs</c>에서 본문 무변경 이동. 트리거형 유물이 늘면 여기에 모은다.
    /// </summary>
    public sealed partial class CombatState
    {
        /// <summary>
        /// 고슴도치 인형(T2 페이즈 C)의 반사 피해 합 — Thorns의 공간화. 인접 판정은 호출부가 맡고
        /// 여기는 유물 수치만 합산한다.
        /// </summary>
        private int GetAdjacentThornsReflectDamage()
        {
            var items = PlayerInventory?.RelicsAndCurses?.Items;
            if (items == null)
            {
                return 0;
            }

            var total = 0;
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.IsActive && item.TriggerKind == RelicTriggerKind.ThornsAdjacent)
                {
                    total += System.Math.Max(0, item.EffectAmount);
                }
            }

            return total;
        }
    }
}

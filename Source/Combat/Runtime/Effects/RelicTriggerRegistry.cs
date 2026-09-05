using System;
using System.Collections.Generic;
using System.Linq;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 트리거 유물(T2 페이즈 C)의 트리거 종류. 스칼라 축(<see cref="PlayerPermanentItemEffectKind"/>)이
    /// "항상 켜져 있는 합산"이라면, 트리거는 "조건이 성립하는 순간 1회분을 적용"한다.
    /// 수치는 relics.csv의 effectAmount(⚠️effectKind는 None으로 저작 — SumEffect가 집계하지 않도록),
    /// 주기·문턱은 triggerParam 컬럼이 든다.
    /// </summary>
    public enum RelicTriggerKind
    {
        None,

        /// <summary>한 턴 triggerParam칸 이상 이동 시 그 턴 공격 피해 +effectAmount(무선 이어폰). kind 없음 — 피해 산정 지점에서 lastMovedDistance 직독.</summary>
        MoveDistanceAttackBonus,

        /// <summary>획득 시와 triggerParam턴마다 「수호」 충전 effectAmount 부여, 보유 중 재충전 없음 — 최대 1(수호 부적).</summary>
        GuardChargeCycle,

        /// <summary>triggerParam턴마다 「은신」 effectAmount턴 부여(도깨비 감투).</summary>
        StealthCycle,

        /// <summary>행동 카드 triggerParam장 사용마다 effectAmount장 드로우(코인 세탁기).</summary>
        DrawPerCardsUsed,

        /// <summary>매 턴 종료(액션 페이즈 EndAction) 시 방어막 effectAmount(붕어빵 틀).</summary>
        BlockOnTurnEnd,

        /// <summary>매 턴 시작 시 HP effectAmount 회복(찜질방 열쇠).</summary>
        HealOnTurnStart,

        /// <summary>몬스터 처치 시 기 effectAmount 회복(방범대 호루라기). 보스 기물은 제외.</summary>
        KiOnKill,

        /// <summary>인접 칸의 몬스터에게 공격당하면 시전자에게 피해 effectAmount 반사(고슴도치 인형).</summary>
        ThornsAdjacent
    }

    /// <summary>
    /// 구현된 유물 트리거의 등록부. relics.csv의 <c>triggerRef</c> 검증이 여기를 통하므로,
    /// 저작만으로 존재하지 않는 트리거를 가리키는 일이 임포트 시점에 막힌다
    /// (<see cref="BossMechanicRegistry"/> 선례). 트리거 자체는 상태를 갖지 않는다 —
    /// 런타임 상태(주기 판정)는 <c>OverallTurnNumber % N</c> 유도값이거나 CombatState 필드다.
    /// </summary>
    public static class RelicTriggerRegistry
    {
        private static readonly Dictionary<string, RelicTriggerKind> Triggers =
            new Dictionary<string, RelicTriggerKind>(StringComparer.Ordinal)
            {
                ["move-distance-attack-bonus"] = RelicTriggerKind.MoveDistanceAttackBonus,
                ["guard-charge-cycle"] = RelicTriggerKind.GuardChargeCycle,
                ["stealth-cycle"] = RelicTriggerKind.StealthCycle,
                ["draw-per-cards-used"] = RelicTriggerKind.DrawPerCardsUsed,
                ["block-on-turn-end"] = RelicTriggerKind.BlockOnTurnEnd,
                ["heal-on-turn-start"] = RelicTriggerKind.HealOnTurnStart,
                ["ki-on-kill"] = RelicTriggerKind.KiOnKill,
                ["thorns-adjacent"] = RelicTriggerKind.ThornsAdjacent
            };

        /// <summary>등록된 트리거 ref 목록(진단·에러 메시지용).</summary>
        public static IReadOnlyList<string> RegisteredRefs =>
            Triggers.Keys.OrderBy(id => id, StringComparer.Ordinal).ToList();

        public static bool TryGet(string triggerRef, out RelicTriggerKind kind)
        {
            if (string.IsNullOrWhiteSpace(triggerRef))
            {
                kind = RelicTriggerKind.None;
                return false;
            }

            return Triggers.TryGetValue(triggerRef, out kind);
        }

        /// <summary>
        /// 주기/문턱(triggerParam)이 반드시 양수여야 하는 트리거. 나머지는 param 0(미사용)으로 저작한다.
        /// </summary>
        public static bool RequiresParam(RelicTriggerKind kind)
        {
            switch (kind)
            {
                case RelicTriggerKind.MoveDistanceAttackBonus:
                case RelicTriggerKind.GuardChargeCycle:
                case RelicTriggerKind.StealthCycle:
                case RelicTriggerKind.DrawPerCardsUsed:
                    return true;
                default:
                    return false;
            }
        }
    }
}

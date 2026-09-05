using System;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 유물 효과·트리거를 플레이어가 읽는 한글로 옮기는 <b>단 하나의 어휘 정본</b>.
    ///
    /// <para>
    /// 🔴 <b>왜 한 곳인가</b>: 2026-08-31까지 이 어휘가 두 벌이었다 — 사이드바 칩이 쓰는 완전한 표와,
    /// 도감이 따로 들고 있던 <b>네 개짜리</b> 표. 도감 쪽은 나머지를 <c>kind.ToString()</c>으로
    /// 흘려서 격자에 <c>BlockGainBonus +1</c>·<c>MoneyGrantOnce +40</c> 같은 내부 이름이 그대로
    /// 떴고, 트리거 유물 6종은 <c>EffectKind</c>가 의도적으로 <see cref="PlayerPermanentItemEffectKind.None"/>
    /// (RC-9)이라 <b><c>None +1</c></b>로 떴다. 표를 둘로 두면 반드시 이렇게 갈린다.
    /// </para>
    ///
    /// <para>
    /// ⚠️ 축을 늘리면 <b>여기에 한 줄</b>을 더한다. 안 더하면 「효과 없음」으로 조용히 떨어진다 —
    /// 실제로 <see cref="PlayerPermanentItemEffectKind.BagSlotBonus"/>(배달 가방)가 그렇게 빠져 있었다.
    /// </para>
    /// </summary>
    public static class PlayerPermanentItemText
    {
        /// <summary>저작되지 않은 축의 폴백. 내부 이름을 흘리는 것보다 낫다.</summary>
        public const string Unknown = "효과 없음";

        /// <summary>스칼라 효과 한 줄(수치 포함). 예: <c>공격 피해 +1</c>.</summary>
        public static string Effect(PlayerPermanentItemEffectKind effectKind, int amount)
        {
            var signed = amount > 0 ? "+" + amount : amount.ToString();
            switch (effectKind)
            {
                case PlayerPermanentItemEffectKind.AttackDamageBonus:
                    return $"공격 피해 {signed}";
                case PlayerPermanentItemEffectKind.MovementRangeBonus:
                    return $"이동 사거리 {signed}";
                case PlayerPermanentItemEffectKind.IncomingDamageDelta:
                    return $"받는 피해 {signed}";
                case PlayerPermanentItemEffectKind.VisionRangeBonus:
                    return $"시야 범위 {signed}";
                case PlayerPermanentItemEffectKind.MaxHpBonus:
                    return $"최대 체력 {signed}";
                case PlayerPermanentItemEffectKind.BlockGainBonus:
                    return $"얻는 방어막 {signed}";
                case PlayerPermanentItemEffectKind.MaxKiBonus:
                    return $"최대 기 {signed}";
                case PlayerPermanentItemEffectKind.MovementHandSizeBonus:
                    return $"이동 손패 {signed}";
                case PlayerPermanentItemEffectKind.ActionHandSizeBonus:
                    return $"행동 손패 {signed}";
                case PlayerPermanentItemEffectKind.MoneyGrantOnce:
                    return $"획득 시 돈 {signed}";
                case PlayerPermanentItemEffectKind.MightGrantOnce:
                    return $"획득 시 힘 {signed}";
                case PlayerPermanentItemEffectKind.ShopDiscountPercent:
                    return $"상점 가격 -{Math.Abs(amount)}%";
                case PlayerPermanentItemEffectKind.MoneyGainBonus:
                    return $"돈 획득 {signed}";
                case PlayerPermanentItemEffectKind.TrapAutoReveal:
                    return "시야 안 함정 자동 발견";
                case PlayerPermanentItemEffectKind.BlockGainPenalty:
                    return $"얻는 방어막 -{Math.Abs(amount)}";
                case PlayerPermanentItemEffectKind.MaxHpPenalty:
                    return $"최대 체력 -{Math.Abs(amount)}";
                case PlayerPermanentItemEffectKind.BagSlotBonus:
                    return $"가방 슬롯 {signed}";
                default:
                    return Unknown;
            }
        }

        /// <summary>트리거 한 줄. 예: <c>행동 카드 10장마다 1장 드로우</c>.</summary>
        public static string Trigger(RelicTriggerKind triggerKind, int param, int amount)
        {
            switch (triggerKind)
            {
                case RelicTriggerKind.MoveDistanceAttackBonus:
                    return $"한 턴 {param}칸+ 이동 시 공격 피해 +{amount}";
                case RelicTriggerKind.GuardChargeCycle:
                    return $"획득 시·{param}턴마다 수호 {amount}";
                case RelicTriggerKind.StealthCycle:
                    return $"{param}턴마다 은신 {amount}턴";
                case RelicTriggerKind.DrawPerCardsUsed:
                    return $"행동 카드 {param}장마다 {amount}장 드로우";
                case RelicTriggerKind.BlockOnTurnEnd:
                    return $"턴 종료 시 방어막 +{amount}";
                case RelicTriggerKind.HealOnTurnStart:
                    return $"턴 시작 시 체력 +{amount}";
                case RelicTriggerKind.KiOnKill:
                    return $"처치 시 기 +{amount}";
                case RelicTriggerKind.ThornsAdjacent:
                    return $"인접 피격 시 반사 피해 {amount}";
                default:
                    return Unknown;
            }
        }

        /// <summary>
        /// 한 유물을 한 줄로. 트리거 유물은 <c>EffectKind</c>가 <b>의도적으로</b> None이므로
        /// (RC-9 — 상시 합산과 트리거 1회분의 이중 적용을 막는다) 트리거 쪽 문장을 쓴다.
        /// 도감 칩·상세 행이 이 판단을 각자 다시 하지 않도록 여기에 둔다.
        /// </summary>
        public static string Headline(
            PlayerPermanentItemEffectKind effectKind, int amount, RelicTriggerKind triggerKind, int triggerParam)
        {
            if (triggerKind != RelicTriggerKind.None)
            {
                return Trigger(triggerKind, triggerParam, amount);
            }

            return effectKind == PlayerPermanentItemEffectKind.None ? Unknown : Effect(effectKind, amount);
        }
    }
}

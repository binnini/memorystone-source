using System;
using System.Linq;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 캠핑카·공작소 서비스(camper-workshop-plan.md P2·P3)의 규칙 부분. 소비 대장은 상점과 같은
    /// <see cref="TryConsumeShopObject"/>(= <see cref="ClaimedEventObjectIds"/>)를 그대로 쓴다 —
    /// "떠나면 이용 여부와 무관하게 소비"(D-5, 상점 CR-6와 같은 원칙)와 세이브 왕복이 공짜로 따라온다.
    /// </summary>
    public sealed partial class CombatState
    {
        /// <summary>캠핑카 회복량(D-4): 최대 체력의 30%, 반올림, 최소 1.</summary>
        public int GetCamperHealAmount()
        {
            return Math.Max(1, (int)Math.Round(Player.MaxHp * 0.30, MidpointRounding.AwayFromZero));
        }

        /// <summary>
        /// 캠핑카 회복을 적용한다. 오버힐은 <see cref="CombatantState.Heal"/>이 최대 체력에서
        /// 클램프하고, 실제로 오른 양이 <paramref name="healed"/>로 나온다(만피면 0 — 택1의 대가는
        /// 플레이어의 선택이므로 만피 사용을 막지 않는다).
        /// </summary>
        public bool TryUseCamperHeal(out int healed, out string reason)
        {
            if (Player == null)
            {
                healed = 0;
                reason = "플레이어 상태가 없습니다.";
                return false;
            }

            healed = Player.Heal(GetCamperHealAmount());
            // 🔴 회복은 <b>일어났는데 화면에는 없었다</b>(2026-09-01 #11). 규칙이 Player.Heal만 부르고
            //    이펙트를 안 올려서 VFX·SFX·플로팅 「+N」이 통째로 빠졌다 — 회복 플로팅은 정책상
            //    절대 숨지 않으므로(ShouldShowFloatingText의 명시적 분기), 안 보였다는 것은 곧
            //    「그 경로를 안 지났다」는 뜻이었다. 카드 회복(A03·F02)과 <b>같은 sourceRef</b>를 써서
            //    카탈로그의 스케일·오프셋까지 그대로 물려받는다.
            //    만피(healed == 0)에는 올리지 않는다 — 「+0」은 정보가 아니라 잡음이다.
            if (healed > 0)
            {
                RaiseEffect(EffectKind.Heal, PlayerCoord, 0, healed, PlayerUnitId, CardEffectRefs.FieldHeal);
            }

            reason = string.Empty;
            return true;
        }

        /// <summary>연마 선택지의 활성 판정 — 덱(손패·뽑을 더미·버림 더미)에 연마 가능 카드가 있는가.</summary>
        public bool HasRefinableCard()
        {
            return EnumerateLiveDeckCards().Any(card => CanRefineCard(card, out _));
        }

        private System.Collections.Generic.IEnumerable<CardCore.CardDefinition> EnumerateLiveDeckCards()
        {
            return MovementDeck.Hand.Concat(MovementDeck.DrawPile).Concat(MovementDeck.DiscardPile)
                .Concat(ActionDeck.Hand).Concat(ActionDeck.DrawPile).Concat(ActionDeck.DiscardPile);
        }
    }
}

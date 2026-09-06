using System.Collections.Generic;
using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>D05 부적 방패 — 손의 임의의 행동 부적 한 장을 소멸시키고 이번 턴 무적이 됩니다.</summary>
    public sealed class D05_TalismanShield : BasicBlockCard
    {
        public override string Id => "D05";

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "무적", "소멸" };

        /// <summary>연마 가능 선언(효과 연마 2차, DEC-2026-09-06-08): 소멸 대신 버림. 문안은 cards.csv `descriptionUpgraded`.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With();

        public override bool TryApplyDefend(CombatState state, CardDefinition card, out int blockGranted)
        {
            // Same damage nullification as D02, without D02's 강화 backlash — the cost is paid up front by
            // exiling a card instead. The exile is random (not player-picked), so it needs no selection UI:
            // A10's 제물 flow is the player-picked variant of the same idea.
            if (card.UpgradeLevel >= 1)
            {
                // 연마(+): 대가가 소멸에서 버림으로 내려간다 — 버린 부적은 더미를 돌아 다시 온다.
                state.DiscardRandomActionHandCard(card);
            }
            else
            {
                state.ExileRandomActionHandCard(card);
            }
            state.pending.NullifyIncomingDamageThisMonsterAction();
            state.ApplyInvincibleStatusThisTurn(card.Id);
            blockGranted = 0;
            return true;
        }
    }
}

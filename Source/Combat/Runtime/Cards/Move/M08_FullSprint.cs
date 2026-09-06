using SeoulPlayup.CardCore;
using System.Collections.Generic;
namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>M08 전력 질주 — 기력을 전부 소모하고 소모한 기력만큼 이동합니다. (옛 behaviorId `move.basic`)</summary>
    public sealed class M08_FullSprint : BasicMoveCard
    {
        public override string Id => "M08";

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화) — 연마 문안의 민첩. 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "민첩" };

        /// <summary>연마 시 이동 뒤 다음 턴에 붙는 민첩(효과 연마 2차, DEC-2026-09-06-08). M05와 같은 예약 경로.</summary>
        public const int UpgradedAgility = 1;

        /// <summary>연마 가능 선언 — 규칙(이동 뒤 민첩 예약)이 UpgradeLevel로 갈린다. 문안은 cards.csv `descriptionUpgraded`.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With();

        public override void ApplyAfterMoveResolved(CombatState state, CardDefinition card)
        {
            if (card.UpgradeLevel < 1)
            {
                return;
            }

            state.pending.BookAgility(UpgradedAgility, 1);
            state.RaiseStatusEffect(StatusEffectKind.Agility, state.PlayerCoord, 0, UpgradedAgility, CombatState.PlayerUnitId, card.Id);
        }
    }
}

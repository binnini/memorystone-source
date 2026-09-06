using System.Collections.Generic;
using SeoulPlayup.CardCore;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>S02 보물찾기 — {Shape}를 탐색하고 드러난 보물 1개당 {Heal} 회복합니다.</summary>
    public sealed class S02_Treasurefinder : ScoutCard
    {
        public override string Id => "S02";

        /// <summary>연마(DEC-2026-09-06-08, 효과 연마 2차): 보물 1개당 회복 2→3 — 규칙은 Amount, {Heal} 토큰은 HealAmount를 읽으므로 둘 다.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With(amount: 3, healAmount: 3);

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "탐색" };

        public override void ApplyAfterScoutReveal(CombatState state, CardDefinition card, HexCoord target, int revealRadius)
        {
            var heal = state.CountTreasureObjects(target, revealRadius) * card.Amount;
            if (heal <= 0)
            {
                return;
            }

            var healed = state.Player.Heal(heal);
            if (healed > 0)
            {
                state.RaiseEffect(EffectKind.Heal, state.PlayerCoord, 0, healed, "player", card.Id);
            }
        }
    }
}

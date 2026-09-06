using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>S04 빙고! — {Shape}를 탐색하고 드러난 적이 3명 이상일 경우 {Heal} 회복합니다.</summary>
    public sealed class S04_Bingo : ScoutCard
    {
        public override string Id => "S04";

        /// <summary>회복이 나오는 최소 적 수 — 옛 behaviorParams=threshold:3. 「3명 이상」이 규칙이라 상수다.</summary>
        public const int Threshold = 3;

        /// <summary>연마 시 임계값(효과 연마 2차, DEC-2026-09-06-08): 「2명 이상」.</summary>
        public const int UpgradedThreshold = 2;

        /// <summary>연마 가능 선언 — 수치 축이 없고 규칙(임계값)이 UpgradeLevel로 갈린다. 문안은 cards.csv `descriptionUpgraded`.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With();

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "탐색" };

        public override void ApplyAfterScoutReveal(CombatState state, CardDefinition card, HexCoord target, int revealRadius)
        {
            // All-or-nothing heal, unlike S02 which scales per revealed object.
            var threshold = card.UpgradeLevel >= 1 ? UpgradedThreshold : Threshold;
            var revealed = state.monsters
                .Count(monster => !monster.Combatant.IsDead && target.DistanceTo(monster.Coord) <= revealRadius);
            if (revealed < threshold || card.Amount <= 0)
            {
                return;
            }

            var healed = state.Player.Heal(card.Amount);
            if (healed > 0)
            {
                state.RaiseEffect(EffectKind.Heal, state.PlayerCoord, 0, healed, "player", card.Id);
            }
        }
    }
}

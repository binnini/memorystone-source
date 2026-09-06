using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>S01 지뢰찾기 — {Shape}를 탐색하고 드러난 적 1명당 피해 {Damage}를 줍니다.</summary>
    public sealed class S01_Minefinder : ScoutCard
    {
        public override string Id => "S01";

        /// <summary>연마(DEC-2026-09-06-08, 효과 연마 2차): 드러난 적 1명당 피해 2→3.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With(amount: 3);

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "탐색" };

        public override void ApplyAfterScoutReveal(CombatState state, CardDefinition card, HexCoord target, int revealRadius)
        {
            var matchingMonsters = state.monsters
                .Where(monster => !monster.Combatant.IsDead && target.DistanceTo(monster.Coord) <= revealRadius)
                .ToList();
            var damage = matchingMonsters.Count * card.Amount;
            if (damage <= 0)
            {
                return;
            }

            foreach (var monster in matchingMonsters)
            {
                state.DamageMonster(monster, damage);
            }

            state.UpdateOccupancy();

            // One damage effect per hit monster (its own id + tile) so the timeline presents 지뢰찾기 as a
            // staggered per-target burst — each monster's flinch/death + number + hit SFX one at a time —
            // instead of a single number at the scout center. The rules already applied the damage above.
            var presentationGroupId = state.CreatePresentationGroupId(CombatState.PlayerUnitId, card.Id);
            foreach (var monster in matchingMonsters)
            {
                state.RaiseEffect(
                    EffectKind.Damage,
                    monster.Coord,
                    0,
                    damage,
                    monster.Id,
                    card.Id,
                    sourceUnitId: CombatState.PlayerUnitId,
                    sourceActorKind: "player",
                    targetActorKind: "monster",
                    sourceCardId: card.Id,
                    hitIndex: 0,
                    hitCount: 1,
                    presentationGroupId: presentationGroupId);
            }
        }
    }
}

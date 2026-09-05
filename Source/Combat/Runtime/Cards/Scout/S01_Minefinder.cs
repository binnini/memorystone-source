using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>S01 지뢰찾기 — {Shape}를 탐색하고 드러난 적 1명당 피해 {Damage}를 줍니다.</summary>
    public sealed class S01_Minefinder : ScoutCard
    {
        public override string Id => "S01";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.ScoutEnemyCountDamage;

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

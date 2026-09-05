using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>S03 기절초광 — {Shape}를 탐색하고 드러난 적에게 기절을 부여합니다.</summary>
    public sealed class S03_StunFlash : ScoutCard
    {
        public override string Id => "S03";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.ScoutEnemyStun;

        public override void ApplyAfterScoutReveal(CombatState state, CardDefinition card, HexCoord target, int revealRadius)
        {
            // Every enemy the scout just revealed is 기절, for the card's authored duration. Stun blocks movement
            // AND attack, so this goes through the same control-status path the debug bind card and traps use —
            // plan constraint + intent cancel + cue — instead of poking activeEffects directly, which would leave
            // a committed monster plan telegraphing an attack it can no longer make.
            var turns = System.Math.Max(1, card.DurationTurns);
            var targets = state.monsters
                .Where(monster => !monster.Combatant.IsDead && target.DistanceTo(monster.Coord) <= revealRadius)
                .ToList();
            if (targets.Count == 0)
            {
                return;
            }

            var hitIndex = 0;
            foreach (var monster in targets)
            {
                var intent = CombatState.CaptureMonsterIntentForCancel(monster);
                state.AddDurationStatusEffect(StatusEffectKind.Stun, monster.Id, turns, 0, CardEffectRefs.ScoutEnemyStun);
                state.ApplyControlStatusConstraintToPlan(monster);
                // Staggered like S01's per-target damage burst so the timeline reads as one monster at a
                // time rather than a single cue at the scout center.
                state.RaiseEffect(
                    EffectKind.StatusEffectApplied,
                    monster.Coord,
                    0,
                    turns,
                    monster.Id,
                    CardEffectRefs.ScoutEnemyStun,
                    sourceUnitId: CombatState.PlayerUnitId,
                    sourceActorKind: "player",
                    targetActorKind: "monster",
                    sourceCardId: card.Id,
                    hitIndex: hitIndex,
                    hitCount: targets.Count,
                    statusKind: StatusEffectKind.Stun,
                    delaySeconds: 0.12f + (0.06f * hitIndex));
                state.EmitMonsterIntentCancelText(monster, intent.WasMoving, intent.WasAttacking, StatusEffectKind.Stun);
                hitIndex++;
            }
        }
    }
}

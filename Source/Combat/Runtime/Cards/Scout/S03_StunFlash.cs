using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>S03 기절초광 — {Shape}를 탐색하고 드러난 적에게 기절을 부여합니다.</summary>
    public sealed class S03_StunFlash : ScoutCard
    {
        public override string Id => "S03";

        /// <summary>연마(DEC-2026-09-06-08, 효과 연마 2차): 기절 1→2턴. 문안에 턴 수가 없어 descriptionUpgraded.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With(durationTurns: 2);

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "탐색", "기절" };

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
                state.AddDurationStatusEffect(StatusEffectKind.Stun, monster.Id, turns, 0, card.Id);
                state.ApplyControlStatusConstraintToPlan(monster);
                // Staggered like S01's per-target damage burst so the timeline reads as one monster at a
                // time rather than a single cue at the scout center.
                state.RaiseEffect(
                    EffectKind.StatusEffectApplied,
                    monster.Coord,
                    0,
                    turns,
                    monster.Id,
                    card.Id,
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

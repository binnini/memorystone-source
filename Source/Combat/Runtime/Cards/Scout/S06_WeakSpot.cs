using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>S06 약점 간파 — {Shape}를 탐색하고 드러난 적에게 2턴 동안 허점을 남깁니다.</summary>
    public sealed class S06_WeakSpot : ScoutCard
    {
        public override string Id => "S06";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.ScoutEnemyVulnerable;

        public override void ApplyAfterScoutReveal(CombatState state, CardDefinition card, HexCoord target, int revealRadius)
        {
            // 드러난 적 전원에게 허점. 허점은 몬스터의 계획을 바꾸지 않으므로 S03의 인텐트 취소 경로가 필요 없다 —
            // 부여 + 큐만. 강도는 stateEffect 컬럼(Vulnerable:n), 지속은 duration 컬럼이 저작 표면.
            var turns = System.Math.Max(1, card.DurationTurns);
            var amount = CardBehaviorMetadata.GetEffectAmount(
                card.StateEffect,
                nameof(StatusEffectKind.Vulnerable),
                StatusEffectInfo.DefaultAmount(StatusEffectKind.Vulnerable));
            var targets = state.monsters
                .Where(monster => !monster.Combatant.IsDead && target.DistanceTo(monster.Coord) <= revealRadius)
                .ToList();

            var hitIndex = 0;
            foreach (var monster in targets)
            {
                state.AddDurationStatusEffect(StatusEffectKind.Vulnerable, monster.Id, turns, amount, CardEffectRefs.ScoutEnemyVulnerable);
                // S01/S03과 같은 per-target stagger — 타임라인이 몬스터 한 명씩 읽히게.
                state.RaiseEffect(
                    EffectKind.StatusEffectApplied,
                    monster.Coord,
                    0,
                    amount,
                    monster.Id,
                    CardEffectRefs.ScoutEnemyVulnerable,
                    sourceUnitId: CombatState.PlayerUnitId,
                    sourceActorKind: "player",
                    targetActorKind: "monster",
                    sourceCardId: card.Id,
                    hitIndex: hitIndex,
                    hitCount: targets.Count,
                    statusKind: StatusEffectKind.Vulnerable,
                    delaySeconds: 0.12f + (0.06f * hitIndex));
                hitIndex++;
            }
        }
    }
}

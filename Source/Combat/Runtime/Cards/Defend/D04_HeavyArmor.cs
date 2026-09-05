using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>D04 무거운 갑옷 — 방어막 {Shield}를 얻습니다. 다음 턴 속박을 부여받습니다.</summary>
    public sealed class D04_HeavyArmor : BasicBlockCard
    {
        public override string Id => "D04";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.DefendBlockDelayedImmobilize;

        public override bool TryApplyDefend(CombatState state, CardDefinition card, out int blockGranted)
        {
            // Free, heavy block, and the same booked 속박 as D06.
            blockGranted = state.AddBlockWithRupture(state.Player, CombatState.PlayerUnitId, card.Amount);
            state.SchedulePlayerDelayedImmobilize(card.DurationTurns);
            return true;
        }
    }
}

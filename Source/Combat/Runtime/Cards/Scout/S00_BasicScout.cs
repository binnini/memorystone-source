namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>S00 정찰의 기초 — {Shape}를 탐색합니다. (옛 behaviorId `scout.reveal`)</summary>
    public sealed class S00_BasicScout : ScoutCard
    {
        public override string Id => "S00";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.ScoutReveal;
    }
}

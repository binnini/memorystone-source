using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 런 시드 기반 <see cref="IRewardRandom"/>(seed-determinism-handoff P3, 스트림 7).
    /// 보상 후보·상점 재고·뽑기·전리품이 전부 이 인터페이스를 지나므로 구현 하나로 보상 계열이
    /// 한꺼번에 재현된다. <c>UnityEngine.Random</c> 구현과 달리 <b>전역 상태가 아니다</b> —
    /// 연출이 난수를 굴려도 이 시퀀스는 밀리지 않는다.
    /// </summary>
    public sealed class SeededRewardRandom : IRewardRandom
    {
        private readonly CountingRandom rng;

        /// <param name="fastForward">재개 시 되감을 커서(P5, 봉투 <c>RewardCursor</c>). 0 = 첫 칸.</param>
        public SeededRewardRandom(int seed, int fastForward = 0)
        {
            Seed = seed;
            rng = new CountingRandom(seed);
            rng.FastForward(fastForward);
        }

        /// <summary>진단용. 어느 시드로 만들어졌는지(재현 신고 대조).</summary>
        public int Seed { get; }

        /// <summary>지금까지 소비한 칸 = 봉투에 실을 커서(P5).</summary>
        public int Consumed => rng.Consumed;

        public int Next(int maxExclusive)
        {
            return maxExclusive <= 1 ? 0 : rng.Next(maxExclusive);
        }
    }
}

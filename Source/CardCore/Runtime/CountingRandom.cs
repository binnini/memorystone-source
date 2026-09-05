using System;

namespace SeoulPlayup.CardCore
{
    /// <summary>
    /// 소비 횟수를 세는 시드 난수(seed-determinism-handoff P5). 「같은 시드 → 같은 판」이 저장 지점을
    /// 넘으려면 재개 시 각 난수 스트림을 <b>저장 시점의 커서</b>까지 되감아야 한다. <see cref="Random"/>은
    /// 내부 상태를 내주지 않으므로, 대신 「몇 칸 썼는가」를 세고 재개 때 같은 시드로 새로 만들어
    /// <see cref="FastForward"/>로 그만큼 버린다.
    ///
    /// <para><see cref="Random"/> 파생이라 <c>Random</c>을 받는 호출 자리(전투 판정·보스 기물·덱 셔플·
    /// <c>IBossEncounterHost.PushRng</c>)를 한 줄도 안 고친다. 셈 단위는 <b>내부 표본</b>이다 —
    /// <see cref="Next(int, int)"/>의 큰 범위(int.MaxValue 초과)는 표본 둘, <see cref="NextBytes"/>는
    /// 바이트당 하나. 그래야 <see cref="FastForward"/>가 <c>Next()</c>만으로 정확히 같은 자리에 선다.</para>
    /// </summary>
    public sealed class CountingRandom : Random
    {
        private readonly Random inner;

        public CountingRandom(int seed)
            : base(seed)
        {
            Seed = seed;
            inner = new Random(seed);
        }

        /// <summary>진단용. 어느 시드로 만들어졌는지.</summary>
        public int Seed { get; }

        /// <summary>지금까지 소비한 내부 표본 수 = 저장할 커서. <see cref="FastForward"/>로 버린 칸도 포함한다.</summary>
        public int Consumed { get; private set; }

        public override int Next()
        {
            Consumed++;
            return inner.Next();
        }

        public override int Next(int maxValue)
        {
            Consumed++;
            return inner.Next(maxValue);
        }

        public override int Next(int minValue, int maxValue)
        {
            // System.Random은 범위가 int.MaxValue를 넘으면 표본을 두 번 뽑는다(GetSampleForLargeRange).
            Consumed += (long)maxValue - minValue > int.MaxValue ? 2 : 1;
            return inner.Next(minValue, maxValue);
        }

        public override double NextDouble()
        {
            Consumed++;
            return inner.NextDouble();
        }

        public override void NextBytes(byte[] buffer)
        {
            Consumed += buffer?.Length ?? 0;
            inner.NextBytes(buffer);
        }

        protected override double Sample()
        {
            Consumed++;
            return inner.NextDouble();
        }

        /// <summary>
        /// 앞의 <paramref name="count"/>칸을 버린다. 재개 직후 새 인스턴스에 저장된 커서를 넣는 용도 —
        /// 이후 첫 굴림이 무중단 판의 다음 굴림과 같아진다. 비용은 커서 × <c>Next()</c> 한 번.
        /// </summary>
        public void FastForward(int count)
        {
            for (var i = 0; i < count; i++)
            {
                inner.Next();
            }

            Consumed += Math.Max(0, count);
        }
    }
}

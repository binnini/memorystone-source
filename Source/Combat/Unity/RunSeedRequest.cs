namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// 사용자가 지정한 <b>다음 런의 시드</b>를 플로우 계층에 건네는 한 칸짜리 우편함
    /// (seed-determinism-handoff P1-1). 디버그 패널이 넣고, <c>MainGameplayController</c>가 새 진입
    /// 시 한 번 꺼내 쓴다 — 재개(세이브 시드)에는 적용되지 않으며, 꺼낸 뒤 비워지므로 그다음 새
    /// 진입은 다시 무작위다.
    ///
    /// Combat 어셈블리에 두는 이유: 입력면(디버그 패널)은 Combat, 소비면은 Flow이고 Flow→Combat만
    /// 참조가 열려 있어 둘이 다 볼 수 있는 가장 낮은 층이 여기다.
    /// </summary>
    public static class RunSeedRequest
    {
        private static int? pendingSeed;

        /// <summary>지정된 시드가 대기 중인가(표시용).</summary>
        public static bool HasPending => pendingSeed.HasValue;

        /// <summary>다음 새 진입에 쓸 시드를 예약한다. 비교용 값이 아니라 그대로 런 시드가 된다.</summary>
        public static void Set(int seed)
        {
            pendingSeed = seed;
        }

        public static void Clear()
        {
            pendingSeed = null;
        }

        /// <summary>예약된 시드를 꺼내며 비운다. 없으면 false.</summary>
        public static bool TryConsume(out int seed)
        {
            if (pendingSeed.HasValue)
            {
                seed = pendingSeed.Value;
                pendingSeed = null;
                return true;
            }

            seed = 0;
            return false;
        }
    }
}

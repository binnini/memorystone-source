namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 전투 중 "같은 상황이면 같은 결과"가 필요한 추첨의 시드 혼합. 이 프로젝트의 전투 RNG는 무시드이고
    /// 세이브는 full-snapshot이라(DEC-2026-07-18-02), 주변 RNG로 뽑으면 전투 중 저장·복원 뒤 결과가
    /// 달라진다 — 세이브가 이미 복원하는 값(유닛 id · 패턴 id · 전체 턴 번호)만 섞어 순수 함수로 만든다.
    ///
    /// <para>🔴 <c>string.GetHashCode</c>는 런타임 간 안정이 보장되지 않아 이 계약에 쓸 수 없다(FNV-1a).
    /// <c>CombatState.MixSpawnHpSeed</c>와 같은 규약이다.</para>
    /// </summary>
    internal static class CombatDeterministicSeed
    {
        public static int Mix(string text, int salt)
        {
            unchecked
            {
                var hash = 2166136261u;
                foreach (var ch in text ?? string.Empty)
                {
                    hash ^= ch;
                    hash *= 16777619u;
                }

                hash ^= (uint)salt;
                hash *= 16777619u;
                hash ^= hash >> 15;
                return (int)hash;
            }
        }
    }
}

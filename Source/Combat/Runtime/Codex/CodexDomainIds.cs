namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 도감 도메인의 안정 식별자. 계획 정본은 <c>docs/codex-plan.md</c>(P3 해금).
    /// <para>
    /// 🔴<b>이 상수가 왜 전투 런타임에 있는가</b> — 도감 모델(<c>SeoulPlayup.Codex</c>)은
    /// <c>SeoulPlayup.Cards.Unity</c>에 살고, 그 어셈블리는 <c>SeoulPlayup.Combat.Runtime</c>을
    /// 참조한다. 반대 방향은 순환이라 불가능하다. 그래서 "봤다"를 말하는 쪽(전투)과 그것을
    /// 기억하는 쪽(도감)이 <b>같은 문자열</b>을 쓰려면 상수가 둘 모두의 아래층에 있어야 한다.
    /// </para>
    /// <para>
    /// 값을 바꾸면 <b>이미 저장된 진행도가 그 도메인만 통째로 잠긴다</b>(저장 키가 곧 이 문자열이다).
    /// 바꿀 일이 생기면 마이그레이션을 같이 짤 것.
    /// </para>
    /// </summary>
    public static class CodexDomainIds
    {
        public const string Card = "card";
        public const string StatusEffect = "status";
        public const string Relic = "relic";
        public const string Trap = "trap";
        public const string Consumable = "consumable";
        public const string Monster = "monster";
        public const string AttackShape = "attack-shape";

        /// <summary>
        /// 맵에 배치되는 오브젝트(P6). 🔑값이 <c>"field-object"</c>가 아니라 <c>"object"</c>인 것은
        /// 일부러다 — 저작 파일 이름(<c>map_objects.csv</c>)은 언제든 바뀔 수 있지만 이 문자열이
        /// 바뀌면 <b>저장된 진행도가 통째로 잠긴다</b>(위 문단). 도메인의 뜻("맵 배치물")을 가리키는
        /// 이름이라야 파일 이름이 바뀌어도 따라 바꿀 이유가 생기지 않는다.
        /// </summary>
        public const string Object = "object";
    }
}

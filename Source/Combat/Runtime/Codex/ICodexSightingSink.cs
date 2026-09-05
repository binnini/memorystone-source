namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// "이것을 만났다"를 받아 적는 곳. 전투는 <b>말만 하고</b> 그것이 어디에 어떻게 저장되는지는
    /// 모른다 — 도감 진행도는 런을 넘어 사는 메타 진행이고, 전투 규칙은 그 층을 몰라야 한다.
    /// <para>
    /// 구현은 <c>SeoulPlayup.Codex.CodexProgress</c>(<c>SeoulPlayup.Cards.Unity</c>)다. 그 어셈블리를
    /// 여기서 참조할 수 없으므로(순환) 인터페이스만 아래층에 둔다 — 계획 §8-3.
    /// </para>
    /// <para>
    /// 🔑<b>집합이라서 두 번 표시해도 무해하다.</b> 그러니 판정이 애매하면 표시하는 쪽으로 붙인다.
    /// 반대로 한 번 놓치면 플레이어는 만난 것을 도감에서 못 본다.
    /// </para>
    /// </summary>
    public interface ICodexSightingSink
    {
        /// <summary>
        /// <paramref name="entryId"/>를 <paramref name="domainId"/>(<see cref="CodexDomainIds"/>)에서
        /// 열린 것으로 표시한다. 빈 문자열·이미 열린 항목은 조용히 무시한다 — 호출부가 매번
        /// 걸러야 한다면 훑기(sweep)를 쓸 수 없다.
        /// </summary>
        void MarkSeen(string domainId, string entryId);
    }
}

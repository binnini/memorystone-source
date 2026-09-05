using System;

namespace SeoulPlayup.Codex
{
    /// <summary>
    /// 화면이 "지금 무엇을 열어 보이는가"를 정하는 한 줄. <c>null</c>을 돌려주면 전량 공개다
    /// (<see cref="CodexListQuery"/>의 <c>isUnlocked: null</c> 규약과 같은 뜻).
    /// <para>
    /// 🔑 규칙이 뷰가 아니라 여기 있는 이유는 <b>시험</b>이다 — 뷰는 <c>SeoulPlayup.Flow</c>에 사는데
    /// EditMode 시험 어셈블리는 그것을 참조하지 않는다. 규칙을 뷰 안에 두면 P4의 완료 조건(토글에
    /// 따라 술어가 갈리는가)을 잴 방법이 없다. 뷰의 <c>UnlockPredicate</c>는 이 함수 하나만 부르고,
    /// 시험도 같은 함수를 잰다.
    /// </para>
    /// </summary>
    public static class CodexUnlockRules
    {
        /// <param name="isDebugView">
        /// 디버그 뷰가 켜져 있는가. 켜져 있으면 해금을 <b>무시</b>한다(Q4 확정). 진행도를 지우는 것이
        /// 아니라 술어를 걷어낼 뿐이므로, 토글을 끄면 원래 잠금이 그대로 돌아온다.
        /// </param>
        public static Func<CodexEntry, bool> PredicateFor(
            CodexProgress progress, ICodexDomain domain, bool isDebugView)
        {
            return isDebugView ? null : progress?.PredicateFor(domain);
        }
    }
}

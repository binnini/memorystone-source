using System;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.EditorTools
{
    /// <summary>
    /// 에디터 도구가 사람에게 결과를 알리는 창구. <b>자동 호출일 때는 창을 띄우지 않는다.</b>
    ///
    /// <para>🔴🔴 <b>왜 필요한가(2026-08-08 실측).</b> <c>EditorUtility.DisplayDialog</c>는 모달이라
    /// 누를 때까지 <b>에디터 메인 스레드가 그 자리에 선다.</b> 사람이 메뉴를 눌렀다면 당연한 동작이지만,
    /// 에이전트가 <c>script-execute</c>로 같은 메뉴를 부르면 <b>아무도 없는 화면에 창이 떠서</b>
    /// 에디터가 통째로 멈춘다 — 그때 MCP 플러그인도 같이 응답을 멈추므로, 바깥에서는 원인이
    /// 보이지 않는 채 <b>브리지가 죽은 것처럼</b> 보인다(`Bake Presentation Durations`로 20분을 잃었다).
    /// 로그에는 작업이 정상 종료한 것으로 찍혀 있어서 더 헷갈린다.</para>
    ///
    /// <para>쓰는 법은 둘이다:</para>
    /// <list type="number">
    ///   <item><b>가능하면 메뉴가 아니라 API를 부른다</b> — 메뉴 래퍼는 얇게 두고 실제 일은
    ///   보고 문자열을 돌려주는 <c>public static</c> 함수에 둔다. 창이 아예 없는 경로가 최선이다.</item>
    ///   <item>메뉴밖에 없다면 <see cref="RunSilently"/>로 감싼다 — 그 안에서는 창 대신 콘솔로 나간다.</item>
    /// </list>
    ///
    /// <para>보고 내용은 <b>창을 띄우든 안 띄우든 항상 콘솔에 남는다</b>. 창은 사람 눈에 띄게 하는
    /// 장치일 뿐이고 기록이 아니다.</para>
    /// </summary>
    public static class EditorReportDialog
    {
        /// <summary>참이면 모달을 띄우지 않는다. 직접 세우기보다 <see cref="RunSilently"/>를 쓸 것 —
        /// 예외가 나도 원상복구된다.</summary>
        public static bool Silent { get; set; }

        /// <summary>결과 보고. 선택지가 없는 알림이므로 조용할 때는 콘솔만으로 충분하다.</summary>
        public static void Report(string title, string message)
        {
            Debug.Log($"[{title}] {message}");
            if (!Silent)
            {
                EditorUtility.DisplayDialog(title, message, "OK");
            }
        }

        /// <summary>예/아니오 확인. 조용할 때는 <paramref name="silentAnswer"/>를 답으로 쓴다 —
        /// 되돌릴 수 없는 동작이라면 <b>반드시 false</b>를 기본으로 둘 것(무인 실행이 파괴적 경로를
        /// 스스로 승인해서는 안 된다).</summary>
        public static bool Confirm(string title, string message, string ok, string cancel, bool silentAnswer)
        {
            if (Silent)
            {
                Debug.Log($"[{title}] (무인 실행) {message} → {(silentAnswer ? ok : cancel)}");
                return silentAnswer;
            }

            return EditorUtility.DisplayDialog(title, message, ok, cancel);
        }

        /// <summary>이 블록 안에서는 모달이 뜨지 않는다. 에이전트가 메뉴를 부를 때 쓴다.</summary>
        public static T RunSilently<T>(Func<T> work)
        {
            if (work == null)
            {
                throw new ArgumentNullException(nameof(work));
            }

            var previous = Silent;
            Silent = true;
            try
            {
                return work();
            }
            finally
            {
                Silent = previous;
            }
        }

        public static void RunSilently(Action work)
        {
            if (work == null)
            {
                throw new ArgumentNullException(nameof(work));
            }

            RunSilently<object>(() =>
            {
                work();
                return null;
            });
        }
    }
}

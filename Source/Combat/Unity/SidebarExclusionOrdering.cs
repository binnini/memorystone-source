using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// 사이드바를 비워 두고 뜨는 모달(상점·캠핑카)의 <b>그리기 순서</b> 계약.
    ///
    /// <para>이 모달들은 사이드바 폭만 제외하고 화면을 덮으며(<c>ApplySidebarExclusionLayout</c>) 열릴 때
    /// <c>SetAsLastSibling</c>으로 맨 앞에 선다. 그런데 사이드바의 콜아웃 판(가방·유물·덱·설정)은
    /// 사이드바 <b>바깥</b>, 즉 모달이 덮는 영역에 뜨고, 그 판 층은 사이드바와 같은 프리팹 루트
    /// (<c>SidebarSystem</c>) 아래 있어 모달과 <b>같은 부모의 형제</b>다. 모달이 맨 앞에 서는 순간
    /// 콜아웃 판은 모달 뒤로 들어가 보이지도, 눌리지도 않는다 — 버튼은 눌리는데 판만 안 뜨는
    /// 「가방·유물이 안 열린다」의 정체(2026-09-05).</para>
    ///
    /// <para>사이드바를 비워 둔다는 것은 「모달 중에도 사이드바를 쓸 수 있다」는 뜻이므로, 모달이
    /// 앞에 선 직후 사이드바 시스템을 모달 <b>위</b>로 되올린다. 사이드바 루트는 이름이 아니라
    /// <see cref="SidebarRootMarker"/>가 붙은 RectTransform(모달이 이미 제외 폭 계산에 쓰는 것)에서
    /// 거슬러 올라가 모달과 부모를 공유하는 조상을 찾는다.</para>
    /// </summary>
    internal static class SidebarExclusionOrdering
    {
        /// <summary>
        /// <paramref name="popupRoot"/>와 같은 부모 아래에서 <paramref name="excludedSidebar"/>를 품은 형제를
        /// 팝업 위(마지막 형제)로 올린다. 사이드바가 다른 계층에 있으면 아무것도 하지 않는다.
        /// </summary>
        /// <returns>되올린 사이드바 시스템 루트, 없으면 null.</returns>
        public static Transform RaiseSidebarAbove(Transform popupRoot, Transform excludedSidebar)
        {
            if (popupRoot == null || excludedSidebar == null || popupRoot.parent == null)
            {
                return null;
            }

            var parent = popupRoot.parent;
            var cursor = excludedSidebar;
            while (cursor != null && cursor.parent != parent)
            {
                cursor = cursor.parent;
            }

            if (cursor == null || cursor == popupRoot)
            {
                return null;
            }

            cursor.SetAsLastSibling();
            return cursor;
        }
    }
}

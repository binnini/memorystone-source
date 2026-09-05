using System;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 은퇴한 소모품 목록(2026-09-05). 공작소 은퇴(<c>ServiceObjectAvailability</c>)와 같은 처방 —
    /// <b>스위치 하나</b>로 끄고, 정의·핸들러·아이콘은 남겨 둔다.
    ///
    /// <para>🔑 CSV 행을 지우지 않는 이유: 세이브에 아이템 id가 실려 있고(가방·도감 목격), 지우면
    /// 이전 판을 이어받을 때 「알 수 없는 아이템」으로 죽는다. 되살릴 때도 이 목록에서 한 줄 빼면 된다.</para>
    ///
    /// <para>🔴 관문은 <b>가방에 들어가는 자리</b>(<c>CombatState.TryAddBagItem</c>) 하나다 —
    /// 전리품·상점·뽑기·이벤트 오브젝트·디버그 지급이 전부 그 문을 지나므로 여기만 막으면 샐 곳이 없다.
    /// 추첨 풀에서도 함께 빼는 것은 「뽑았는데 안 들어오는」 헛수를 없애기 위해서다.</para>
    /// </summary>
    public static class ConsumableItemAvailability
    {
        /// <summary>은신 구슬(2026-09-05 사용자 확정: "사용이 어려움").</summary>
        public const string VeilBeadId = "item-veil-bead";

        private static readonly string[] RetiredIds = { VeilBeadId };

        public static bool IsRetired(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return false;
            }

            var id = itemId.Trim();
            for (var i = 0; i < RetiredIds.Length; i++)
            {
                if (string.Equals(RetiredIds[i], id, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

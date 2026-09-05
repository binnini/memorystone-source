using System;
using System.Collections.Generic;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 소모품 id 이관(DEC-2026-08-31-02). 12종을 구슬·호리병 한 계열로 리뉴얼하면서 표시명과 함께
    /// id도 갈았다 — <c>item-traffic-cone</c>이 「결계 구슬」이 되는 식으로 <b>12행 전부가 뜻에서
    /// 어긋났기</b> 때문이다(유물은 개명해도 뜻이 남아 id를 유지했다).
    ///
    /// 🔑 이관은 <b>세이브를 읽는 지점 한 곳</b>에서만 일어난다(<see cref="PlayerInventorySaveData"/>).
    ///    소모품은 스테이지 한정이라 살아 있는 세이브에 담기는 것은 진행 중인 전투 하나뿐이고
    ///    가방은 최대 3~4칸이다 — 이관 대상이 이보다 작을 수 없다.
    /// ⚠️ 표는 <b>덧붙이기만</b> 한다. 여기서 항목을 지우면 그 id가 담긴 옛 세이브의 아이템이
    ///    조용히 사라진다.
    /// </summary>
    public static class ConsumableItemIdMigration
    {
        private static readonly IReadOnlyDictionary<string, string> Renames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["item-spring-water"] = "item-vitality-flask",       // 약수 한 통 → 생기 호리병
            ["item-hand-warmer"] = "item-bulwark-flask",         // 손난로 → 철벽 호리병
            ["item-charm-burn"] = "item-cleanse-flask",          // 부적 태우기 → 정화 호리병
            ["item-salt-handful"] = "item-ward-flask",           // 소금 한 줌 → 벽사 호리병
            ["item-energy-drink"] = "item-vigor-flask",          // 에너지 드링크 → 활기 호리병
            ["item-tiger-balm"] = "item-tiger-flask",            // 호랑이 연고 → 맹호 호리병
            ["item-flashlight"] = "item-light-bead",             // 만능 손전등 → 광명 구슬
            ["item-smoke-bomb"] = "item-veil-bead",              // 연막탄 → 은신 구슬
            ["item-personal-alarm"] = "item-thunder-bead",       // 호신용 경보기 → 벽력 구슬
            ["item-firecracker-bundle"] = "item-flame-bead",     // 폭죽 다발 → 화염 구슬
            ["item-pepper-spray"] = "item-daze-bead",            // 최루 분사기 → 혼미 구슬
            ["item-traffic-cone"] = "item-barrier-bead",         // 공사장 고깔 → 결계 구슬
        };

        /// <summary>구 id면 신 id로, 아니면 그대로 돌려준다.</summary>
        public static string Migrate(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return itemId;
            }

            return Renames.TryGetValue(itemId, out var renamed) ? renamed : itemId;
        }

        /// <summary>진단·테스트용 이관표.</summary>
        public static IReadOnlyDictionary<string, string> Table => Renames;
    }
}

using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using System.Linq;

namespace SeoulPlayup.Combat.Unity
{
    // T4-2 가방 아이템 타게팅 — 카드 타게팅과 같은 입력 파이프라인(OnHexClicked)을 재사용한다.
    // 가방 슬롯 클릭이 대상 지정형 아이템이면 여기서 "다음 맵 클릭 = 대상"으로 전환하고,
    // 취소는 카드 취소 단축키·다른 카드/아이템 선택이 함께 지운다.
    public sealed partial class MapCombatController
    {
        private string pendingBagItemTargetingId = string.Empty;

        public bool HasPendingBagItemTargeting => !string.IsNullOrEmpty(pendingBagItemTargetingId);
        public string PendingBagItemTargetingId => pendingBagItemTargetingId;

        /// <summary>대상 지정형 소모품의 타게팅 시작. 무대상 아이템·가방에 없는 아이템은 거부한다.</summary>
        public bool BeginBagItemTargeting(string itemId)
        {
            if (State == null
                || !ConsumableItemCatalog.TryGet(itemId, out var item)
                || item.Targeting == ConsumableItemTargeting.None)
            {
                return false;
            }

            if (State.PlayerInventory?.Bag == null
                || State.PlayerInventory.Bag.Stacks.All(stack => stack.ItemId != itemId))
            {
                return false;
            }

            // 카드 선택과 아이템 타게팅은 상호 배타 — 겹치면 다음 클릭의 의미가 둘이 된다.
            CancelSelectedCard();
            pendingBagItemTargetingId = itemId;
            LastInputMessage = item.Targeting == ConsumableItemTargeting.Enemy
                ? $"{item.DisplayName}: 대상 몬스터를 클릭하세요."
                : $"{item.DisplayName}: 대상 칸을 클릭하세요.";
            RefreshView();
            return true;
        }

        public void CancelBagItemTargeting()
        {
            pendingBagItemTargetingId = string.Empty;
        }

        private bool TryUseBagItemOn(HexCoord coord)
        {
            var itemId = pendingBagItemTargetingId;
            var used = State != null && State.TryUseBagItem(itemId, coord);
            if (used)
            {
                pendingBagItemTargetingId = string.Empty;
                LastInputMessage = "아이템을 사용했습니다.";
            }
            else
            {
                LastInputMessage = string.IsNullOrEmpty(State?.LastFailureReason)
                    ? "아이템을 사용할 수 없습니다."
                    : State.LastFailureReason;
                RequestInvalidInputAudioCue(LastInputMessage);
            }

            RefreshView();
            return used;
        }
    }
}

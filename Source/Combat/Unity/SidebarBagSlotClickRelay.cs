using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// 가방 슬롯 클릭 릴레이(T4-1). <see cref="SidebarBagPanelView"/>가 런타임에 각 슬롯 루트에 붙인다 —
    /// 시각 요소가 없어서 프리팹 저작 모습과 어긋나지 않고, 클릭은 슬롯 Image의 raycast로 받는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SidebarBagSlotClickRelay : MonoBehaviour, IPointerClickHandler
    {
        private int slotIndex = -1;
        private Action<int> clicked;

        public void Configure(int index, Action<int> onClicked)
        {
            slotIndex = index;
            clicked = onClicked;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            clicked?.Invoke(slotIndex);
        }
    }
}

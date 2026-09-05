using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Reports pointer enter/exit on one relic/curse chip so the panel can show that item's name and effect
    /// summary in its detail line (P6 T2 follow-up). The chips are 58×58 icon plates carrying a single
    /// character, and the authored slot has no text target for the summary, so the effect text shipped
    /// invisible — the player could see *that* they owned something but never *what it does*.
    ///
    /// Attached at runtime by <see cref="SidebarRelicCursePanelView"/>; carries no state of its own beyond the
    /// callbacks, so re-binding a slot to a different item just re-points them.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SidebarRelicSlotHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private Action onEnter;
        private Action onExit;

        /// <summary>
        /// 지금 포인터가 이 칸 안에 있는가. 🔴 <b>이 한 비트가 2026-09-01 #7의 전부다.</b>
        ///
        /// <para>툴팁은 「들어왔다/나갔다」 <b>이벤트</b>로만 열리고 닫혔는데, 소모품을 다 쓰면 칸이
        /// 비면서 콜백만 지워졌다 — 포인터는 그대로 안에 있으니 나가는 이벤트가 영영 안 오고,
        /// 닫을 콜백마저 없어져 설명창이 화면에 남았다. 개수가 줄기만 한 경우도 같은 구멍이라
        /// 「3개 보유」가 계속 떠 있었다.</para>
        ///
        /// <para>🔑 고치는 자리가 <b>여기</b>인 이유: 가방과 유물 칸이 이 컴포넌트 하나를 공유한다.
        /// 패널 쪽에서 고치면 두 벌이 되고, 한쪽만 고치면 다른 쪽에 같은 버그가 남는다.</para>
        /// </summary>
        private bool pointerInside;

        /// <summary>
        /// 이 칸이 가리키는 물건이 바뀌었다(또는 비었다). 포인터가 안에 있으면 <b>옛 설명을 닫고
        /// 새 설명을 다시 연다</b> — 마우스를 움직이지 않아도 화면이 사실을 따라온다.
        /// </summary>
        public void Bind(Action enter, Action exit)
        {
            if (pointerInside)
            {
                onExit?.Invoke();
            }

            onEnter = enter;
            onExit = exit;

            if (pointerInside)
            {
                onEnter?.Invoke();
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            pointerInside = true;
            onEnter?.Invoke();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            pointerInside = false;
            onExit?.Invoke();
        }

        private void OnDisable()
        {
            if (!pointerInside)
            {
                return;
            }

            pointerInside = false;
            onExit?.Invoke();
        }
    }
}

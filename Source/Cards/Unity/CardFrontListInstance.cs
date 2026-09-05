using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// 손패가 아닌 화면(더미 오버레이·도감)이 <c>CardFront</c> 프리팹을 <b>읽기 전용 카드 한 장</b>으로
    /// 세우는 공용 경로. 더미 오버레이가 이미 하던 일을 떼어낸 것이고, 도감이 같은 함수를 쓴다.
    /// <para>
    /// 떼어낸 이유는 계획 정본(<c>docs/codex-plan.md</c> §5)의 전제 그대로다 — 카드 세우는 절차가
    /// 두 곳에 생기면 전투 카드가 바뀔 때 한쪽만 낡는다. 카드 <b>내용</b> 채우기는
    /// <see cref="DeckPileOverlayCardView.Bind"/>가, <b>프레임 갈아 끼우기</b>는
    /// <see cref="CardFrontPresentationFormatting"/>가 계속 맡는다.
    /// </para>
    /// </summary>
    public static class CardFrontListInstance
    {
        /// <summary>
        /// 카드 프리팹을 <paramref name="parent"/> 아래 세우고 상호작용을 떼어낸 뒤 내용을 채운다.
        /// </summary>
        /// <param name="prefab">
        /// <c>CardFront_Move</c> 또는 <c>CardFront_Action</c>. 🔴<b>호출자가 직렬화 참조로 넘겨야 한다</b> —
        /// <c>AssetDatabase</c>로 찾는 경로는 에디터에서만 살아 있어서 빌드에서 조용히 <c>null</c>이 된다.
        /// </param>
        /// <param name="statusFrameSprite">
        /// 상태(저주) 카드 프레임. 역시 직렬화 참조여야 한다. <c>null</c>이면 프레임을 갈지 않는다 —
        /// 저주 카드가 행동 카드 얼굴로 뜬다.
        /// </param>
        public static DeckPileOverlayCardView Create(
            GameObject prefab,
            Transform parent,
            CombatCardSnapshot card,
            Sprite statusFrameSprite)
        {
            if (prefab == null || parent == null)
            {
                return null;
            }

            var slot = Object.Instantiate(prefab, parent, false);
            slot.name = $"CardFront_{SafeObjectName(card.Name)}";
            StripInteraction(slot);

            var view = slot.GetComponent<DeckPileOverlayCardView>() ?? slot.AddComponent<DeckPileOverlayCardView>();
            view.Bind(card, statusFrameSprite);
            return view;
        }

        /// <summary>
        /// 손패용 입력 부품을 떼어낸다. 목록의 카드는 보기만 하는 물건이라 호버·클릭·선택이 남으면
        /// 손패처럼 반응한다.
        /// </summary>
        public static void StripInteraction(GameObject slot)
        {
            if (slot == null)
            {
                return;
            }

            var interaction = slot.GetComponent<HandCardInteraction>();
            if (interaction != null)
            {
                Object.DestroyImmediate(interaction);
            }

            foreach (var selectable in slot.GetComponentsInChildren<Selectable>(includeInactive: true))
            {
                Object.DestroyImmediate(selectable);
            }

            var group = slot.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = slot.AddComponent<CanvasGroup>();
            }

            group.interactable = false;
            group.blocksRaycasts = false;
        }

        /// <summary>
        /// 카드 종류에 맞는 프리팹을 고른다. 이동만 전용 프리팹이고 나머지는 전부 행동 프리팹을 쓴다 —
        /// 저주(상태) 카드도 행동 프리팹 위에 프레임만 갈아 끼운다.
        /// </summary>
        public static GameObject ChoosePrefab(CombatCardKind kind, GameObject movePrefab, GameObject actionPrefab)
        {
            return kind == CombatCardKind.Move ? movePrefab : actionPrefab;
        }

        private static string SafeObjectName(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "Card" : value.Replace('/', '_');
        }
    }
}

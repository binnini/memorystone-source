using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    public static class CardFrontPresentationFormatting
    {
        public static string KindToKorean(CombatCardKind kind)
        {
            switch (kind)
            {
                case CombatCardKind.Move:        return "이동";
                case CombatCardKind.Attack:      return "공격";
                case CombatCardKind.Defend:      return "방어";
                case CombatCardKind.Scout:
                case CombatCardKind.Investigate: return "정찰";
                case CombatCardKind.FieldObject: return "필드";
                case CombatCardKind.Buff:
                case CombatCardKind.Utility:     return "유틸리티";
                default:                         return kind.ToString();
            }
        }

        /// <summary>
        /// 낼 수 없는 것이 카드의 영구 속성인가 — 상태 카드(X01~X03)만 참이다.
        /// 코스트가 모자라거나 페이즈가 아니라서 지금 못 쓰는 카드는 <b>포함하지 않는다</b>:
        /// 그런 카드는 곧 쓸 수 있으므로 코스트·사거리를 계속 보여 줘야 한다.
        /// <para>
        /// 판정은 스냅샷의 <see cref="CombatCardSnapshot.IsStatusCard"/> 한 곳에서 온다 —
        /// 저작(<c>CardEffectType.Status</c>)이 정하는 값이라 카드가 손패에 있든 더미에 있든 같다.
        /// </para>
        /// </summary>
        public static bool IsStatusCard(CombatCardSnapshot card)
        {
            return card.IsStatusCard;
        }

        /// <summary>
        /// 카드 얼굴에 찍는 종류 라벨. 상태 카드는 <see cref="CombatCardKind"/>가 <c>Move</c>로
        /// 뭉개지므로(<c>ToKind</c>의 default 분기) <see cref="CombatCardKind"/>만 보고 찍으면
        /// 상태 카드에 "이동"이 붙는다 — 그래서 상태 여부를 먼저 본다.
        /// </summary>
        public static string KindLabel(CombatCardSnapshot card)
        {
            return IsStatusCard(card) ? "저주" : KindToKorean(card.Kind);
        }

        /// <summary>
        /// 카드 프레임을 종류에 맞게 갈아 끼운다. 상태 카드만 별도 스프라이트를 쓰고, 이동·행동은
        /// 각자의 프리팹이 이미 제 프레임을 들고 있으므로 <b>슬롯의 원본 프레임으로 되돌린다</b>.
        /// <para>
        /// 되돌림이 필요한 이유 = 손패 행동 슬롯은 풀링돼 상태 카드와 일반 행동 카드가 같은 슬롯을
        /// 번갈아 쓴다. <paramref name="originalFrameCache"/>는 슬롯별 원본을 기억하는 호출자 소유
        /// 캐시다(목록처럼 매번 새로 만드는 뷰는 <c>null</c>을 넘기면 된다).
        /// </para>
        /// </summary>
        public static void ApplyCardFrame(
            Transform root,
            CombatCardSnapshot card,
            Sprite statusFrameSprite,
            Dictionary<Image, Sprite> originalFrameCache = null)
        {
            if (statusFrameSprite == null)
            {
                return;
            }

            var frame = FindImage(root, "Card_Frame_Overlay");
            if (frame == null)
            {
                return;
            }

            if (originalFrameCache == null)
            {
                if (IsStatusCard(card))
                {
                    frame.sprite = statusFrameSprite;
                }

                return;
            }

            if (!originalFrameCache.TryGetValue(frame, out var defaultSprite))
            {
                defaultSprite = frame.sprite;
                originalFrameCache[frame] = defaultSprite;
            }

            frame.sprite = IsStatusCard(card) ? statusFrameSprite : defaultSprite;
        }

        public static void RefreshCardRange(Transform root, CombatCardSnapshot card)
        {
            // 상태 카드는 낼 수 없으므로 사거리 0이 "붙어서 쓴다"로 오독된다 — 값 자체를 감춘다.
            var showRange = !IsSelfTargeted(card) && !IsStatusCard(card);
            var rangeText = FindText(root, "RangeText_TMP");
            if (rangeText != null)
            {
                rangeText.text = card.Range.ToString(System.Globalization.CultureInfo.InvariantCulture);
                rangeText.gameObject.SetActive(showRange);
            }

            var rangeIcon = FindImage(root, "Range_Icon");
            if (rangeIcon != null)
            {
                rangeIcon.gameObject.SetActive(showRange);
            }
        }

        /// <summary>
        /// 상태 카드의 코스트 뱃지를 감춘다. 낼 수 없는 카드의 "0"은 지불할 값이 아니라 빈칸이고,
        /// 남겨 두면 "공짜로 쓸 수 있다"로 읽힌다(2026-08-02 실플레이 판정).
        /// 사거리와 같은 규칙을 같은 파일에 두는 이유 = 손패 레인과 더미 오버레이가 갈라지지 않게 하려는 것.
        ///
        /// <para>🔑 예외 = <b>실제로 낼 값이 있는 저주</b>(빚 문서 X04: 기력 1을 내면 소멸). 2026-08-02
        /// 판정의 근거는 "0은 지불할 값이 아니다"였지 "저주는 비용이 없다"가 아니었다 — 비용이 0보다
        /// 크면 그 숫자는 플레이어가 실제로 치를 값이므로 숨기면 규칙이 화면에서 사라진다(#18).</para>
        /// </summary>
        public static void RefreshCardCost(Transform root, CombatCardSnapshot card)
        {
            var showCost = !IsStatusCard(card) || card.Cost > 0;

            var costText = FindText(root, "CostText_TMP");
            if (costText != null)
            {
                costText.gameObject.SetActive(showCost);
            }

            var costIcon = FindImage(root, "Cost_Icon");
            if (costIcon != null)
            {
                costIcon.gameObject.SetActive(showCost);
            }
        }

        private static bool IsSelfTargeted(CombatCardSnapshot card)
        {
            return card.TargetMode == CardTargetMode.Self
                || card.PlayMode == CardPlayMode.Self
                || (SeoulPlayup.Combat.Runtime.Cards.CardBehaviorRegistry.TryGet(card.Id, out var behavior)
                    ? behavior.HasSelfTargetedChoice
                    : CardBehaviorMetadata.HasSelfTargetedChoiceOption(card.ChoiceOptions));
        }

        private static TMP_Text FindText(Transform root, string objectName)
        {
            return root != null
                ? root.GetComponentsInChildren<TMP_Text>(includeInactive: true)
                    .FirstOrDefault(text => text != null && text.name == objectName)
                : null;
        }

        private static Image FindImage(Transform root, string objectName)
        {
            return root != null
                ? root.GetComponentsInChildren<Image>(includeInactive: true)
                    .FirstOrDefault(image => image != null && image.name == objectName)
                : null;
        }
    }
}


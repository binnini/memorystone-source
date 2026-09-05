using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Unity
{
    public sealed class CombatHudCardSelectionBridge
    {
        public bool PlayCard(ICombatCardHudHost controller, CombatCardSnapshot card)
        {
            if (controller == null || controller.State == null)
            {
                return false;
            }

            if (!controller.CanSelectCardForTutorial(card, out var tutorialFailure))
            {
                controller.ShowTutorialBlockedFeedback(tutorialFailure);
                return false;
            }

            var cardKey = card.SelectionKey;
            bool played;
            // 갈림길 카드는 종류와 무관하게 선택지 패널로 간다. A03이 공격 카드라 이 분기가 오랫동안 공격
            // 케이스 안에 있었는데, U02 부적 끌어오기(유틸리티)는 그 경로로 들어오지 못해 UseUtility로
            // 떨어지고 "지원하지 않는 유틸리티 효과"로 거절된다 — 손에서 누를 수 없는 카드가 된다.
            if (card.PlayMode == CardPlayMode.Choice)
            {
                played = controller.BeginChoiceCardSelection(cardKey);
                if (played)
                {
                    controller.NotifyTutorialCardSelected(card);
                }

                return played;
            }

            // 빚 문서(X04, T2): 낼 수 있는 유일한 저주. 저주(구 상태) 카드는 CombatCardKind가 없어
            // 스위치 default(Move)로 새므로 유틸리티 경로로 직결한다.
            if (string.Equals(card.Id, SeoulPlayup.Combat.Runtime.Cards.CardIds.DebtNote, System.StringComparison.Ordinal))
            {
                played = controller.UseUtility(cardKey);
                if (played)
                {
                    controller.NotifyTutorialCardSelected(card);
                }

                return played;
            }

            switch (card.Kind)
            {
                case CombatCardKind.Move:
                    if (card.PlayMode == CardPlayMode.Self)
                    {
                        played = controller.PlayMovementSelf(cardKey);
                        break;
                    }

                    // Travel cards with an unknown destination resolve instantly to a random reachable
                    // tile instead of asking the player to pick a tile they cannot see.
                    played = card.TargetMode == CardTargetMode.RandomReachable
                        ? controller.PlayMovementRandom(cardKey)
                        : controller.BeginMoveSelection(cardKey);
                    break;
                case CombatCardKind.Attack:
                    // Self-centred area attacks (e.g. Sweep) auto-fire on the player's tile instead of
                    // asking the player to pick a target cell.
                    played = card.TargetMode == CardTargetMode.SelfArea
                        ? controller.PlaySelfAreaAttack(cardKey)
                        : controller.BeginAttackSelection(cardKey);
                    break;
                case CombatCardKind.Defend:
                    played = controller.UseDefense(cardKey);
                    break;
                case CombatCardKind.Scout:
                    played = controller.BeginScoutSelection(cardKey);
                    break;
                case CombatCardKind.Investigate:
                    played = controller.BeginInvestigateSelection();
                    break;
                case CombatCardKind.FieldObject:
                    played = card.PlayMode == CardPlayMode.Self
                        ? controller.PlayTorchAtPlayerForDev(cardKey)
                        : controller.BeginFieldObjectSelection(cardKey);
                    break;
                case CombatCardKind.Utility:
                    played = controller.UseUtility(cardKey);
                    break;
                default:
                    return false;
            }

            if (played)
            {
                controller.NotifyTutorialCardSelected(card);
            }

            return played;
        }
    }
}

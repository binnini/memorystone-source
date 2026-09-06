using System;
using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime.Cards;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 카드 연마(D-1~D-3 · P4 DEC-2026-09-06-05). 카드 클래스가 <see cref="CardBehavior.Upgrade"/>를 구현한 카드만 대상이고
    /// (null = 연마 불가, 옛 card_upgrades.csv 행 없음과 같은 뜻), 상한 「카드당 1회」는 세이브·데이터가 아니라 이 게이트
    /// (<c>UpgradeLevel >= 1</c> 거부)가 강제한다 — 다단 연마 확장이 열려 있도록 코드·세이브에는 상한 제약을 두지 않는다(D-2).
    /// 치환은 정의 시점(<see cref="CardUpgrades.Resolve"/>)에 일어나므로 필드 카드의 배치 시점 저작값에도 연마가 먹는다.
    /// </summary>
    public sealed partial class CombatState
    {
        /// <summary>연마 후보 필터 — 카드 클래스가 연마를 선언 && 미연마 && 임시·저주 아님.</summary>
        public bool CanRefineCard(string cardKey)
        {
            return CanRefineCard(cardKey, out _);
        }

        public bool CanRefineCard(string cardKey, out string reason)
        {
            var card = FindRefinableCard(cardKey);
            if (card == null)
            {
                reason = "연마할 카드를 덱에서 찾지 못했습니다.";
                return false;
            }

            return CanRefineCard(card, out reason);
        }

        public bool CanRefineCard(CardDefinition card, out string reason)
        {
            if (card == null)
            {
                reason = "연마할 카드가 필요합니다.";
                return false;
            }

            if (card.IsTemporary)
            {
                reason = "임시 카드는 연마할 수 없습니다.";
                return false;
            }

            if (card.EffectType == CardEffectType.Status)
            {
                reason = "저주 카드는 연마할 수 없습니다.";
                return false;
            }

            if (!CardBehaviorRegistry.Resolve(card).CanUpgrade(card))
            {
                reason = "이 카드는 연마가 저작되지 않았습니다(카드 클래스에 Upgrade 없음).";
                return false;
            }

            if (card.UpgradeLevel >= 1)
            {
                reason = "이미 연마된 카드입니다.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        /// <summary>
        /// 전/후 비교 화면용 미리보기 — 상태를 바꾸지 않고 「현재 정의 / 연마 후 정의」 쌍을 준다.
        /// </summary>
        public bool TryPreviewRefinedCard(string cardKey, out CardDefinition current, out CardDefinition refined, out string reason)
        {
            current = FindRefinableCard(cardKey);
            refined = null;
            if (current == null)
            {
                reason = "연마할 카드를 덱에서 찾지 못했습니다.";
                return false;
            }

            if (!CanRefineCard(current, out reason))
            {
                return false;
            }

            refined = ResolveRefinedDefinition(current);
            reason = string.Empty;
            return true;
        }

        /// <summary>
        /// 전/후 비교 화면용 스냅샷 쌍. 정의 쌍(<see cref="TryPreviewRefinedCard"/>)을 덱 목록과
        /// 같은 스냅샷 빌더로 투영해, 비교 화면의 카드 두 장이 실제 카드 프레임 렌더와 같은
        /// 데이터를 쓰게 한다.
        /// </summary>
        public bool TryPreviewRefinedCardSnapshots(
            string cardKey,
            out CombatCardSnapshot before,
            out CombatCardSnapshot after,
            out string reason)
        {
            before = default;
            after = default;
            if (!TryPreviewRefinedCard(cardKey, out var current, out var refined, out reason))
            {
                return false;
            }

            var pile = ResolveRefinePileLabel(current);
            before = CreateDeckListSnapshot(current, pile, discarded: false);
            after = CreateDeckListSnapshot(refined, pile, discarded: false);
            return true;
        }

        private string ResolveRefinePileLabel(CardDefinition card)
        {
            if (MovementDeck.Hand.Contains(card)) return "Movement hand";
            if (MovementDeck.DrawPile.Contains(card)) return "Movement draw";
            if (MovementDeck.DiscardPile.Contains(card)) return "Movement discard";
            if (ActionDeck.Hand.Contains(card)) return "Action hand";
            if (ActionDeck.DrawPile.Contains(card)) return "Action draw";
            return "Action discard";
        }

        /// <summary>
        /// 덱 어디에 있든(손패·뽑을 더미·버림 더미) 카드 한 장을 연마한다. 라이브 전투 덱의 카드는
        /// 같은 더미의 같은 자리에서 연마 정의로 바꿔치고, 런 지속 덱(<see cref="PlayerDeck"/>)의
        /// UpgradeLevel도 함께 올려 세이브 복원 후에도 연마가 유지되게 한다
        /// (<see cref="TryPermanentRemoveCardFromDeck"/>과 같은 이중 반영 계약).
        /// </summary>
        public bool TryRefineCard(string cardKey, out string reason)
        {
            var card = FindRefinableCard(cardKey);
            if (card == null)
            {
                reason = "연마할 카드를 덱에서 찾지 못했습니다.";
                return false;
            }

            if (!CanRefineCard(card, out reason))
            {
                return false;
            }

            var refined = ResolveRefinedDefinition(card);
            var deck = MovementDeck.Hand.Contains(card) || MovementDeck.DrawPile.Contains(card) || MovementDeck.DiscardPile.Contains(card)
                ? MovementDeck
                : ActionDeck;
            if (!deck.TryReplaceAnywhere(card, refined))
            {
                reason = "연마할 카드를 덱에서 찾지 못했습니다.";
                return false;
            }

            PlayerDeck = PlayerDeck.UpgradeCard(card.InstanceId, refined.UpgradeLevel);
            reason = string.Empty;
            return true;
        }

        private CardDefinition ResolveRefinedDefinition(CardDefinition card)
        {
            // 저작 정의에 다음 연마 단계를 실어 카드 클래스의 Upgrade로 치환한다 — 덱 생성·세이브 복원과 같은 지점.
            var entry = FindCatalogEntryById(card.Id);
            var next = entry?.ToCardDefinition(CardCatalog.SourceId, card.InstanceId, card.UpgradeLevel + 1, card.IsTemporary);
            return CardUpgrades.Resolve(next);
        }

        private CardDefinition FindRefinableCard(string cardKey)
        {
            return string.IsNullOrWhiteSpace(cardKey)
                ? null
                : FindRemovableCard(MovementDeck, cardKey) ?? FindRemovableCard(ActionDeck, cardKey);
        }

        private CardCatalogEntry FindCatalogEntryById(string cardId)
        {
            return CardCatalog?.Entries.FirstOrDefault(entry => string.Equals(entry.Id, cardId, StringComparison.Ordinal));
        }
    }
}

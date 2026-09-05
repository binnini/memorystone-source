using System;
using System.Linq;
using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 상점(잡화점) 거래. 결제는 전부 "차감 성공 → 지급, 지급 실패 시 환불" 순서다 —
    /// 지급을 먼저 하면 실패 시 되돌릴 수 없는 축(MaxHp)이 있고, 차감만 하고 지급이 실패하면
    /// 돈이 증발한다. 유물 지급은 반드시 <see cref="TryGrantPermanentItem"/>을 지난다(RC-5).
    /// </summary>
    public sealed partial class CombatState
    {
        /// <summary>카드 한 장을 사서 즉시 현재 손패에 넣는다(CR-8과 같은 행선지).</summary>
        public bool TryPurchaseShopCard(string cardId, int price, out string reason)
        {
            if (!TrySpendFromWallet(price, out reason))
            {
                return false;
            }

            if (!TryAddCardToCurrentHand(cardId, "shop", out reason))
            {
                PlayerInventory.Wallet.Add(price);
                return false;
            }

            reason = string.Empty;
            return true;
        }

        /// <summary>유물 하나를 산다. 중복 보유·미등록 id는 지급 관문이 거부하고 환불된다.</summary>
        public bool TryPurchaseShopRelic(string relicId, int price, out string reason)
        {
            if (!TrySpendFromWallet(price, out reason))
            {
                return false;
            }

            if (!TryGrantPermanentItem(relicId, out reason))
            {
                PlayerInventory.Wallet.Add(price);
                return false;
            }

            reason = string.Empty;
            return true;
        }

        /// <summary>소모품 하나를 산다(T4-3, SH-11). 가방 만원·미등록 id는 지급 관문이 거부하고 환불된다.</summary>
        public bool TryPurchaseShopItem(string itemId, int price, out string reason)
        {
            if (!TrySpendFromWallet(price, out reason))
            {
                return false;
            }

            if (!TryAddBagItem(itemId))
            {
                PlayerInventory.Wallet.Add(price);
                reason = "가방이 가득 찼습니다.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        /// <summary>
        /// 카드 제거를 산다. <paramref name="cardKey"/>는 instance id 또는 카드 id이며, 손패·뽑을
        /// 더미·버림 더미 전체가 대상이다(손패 한정인 <see cref="CardDeckState.PermanentRemoveFromHand"/>와
        /// 다르다 — 상점 제거는 덱 전체를 보고 골라야 의미가 있다).
        /// </summary>
        public bool TryPurchaseShopCardRemoval(string cardKey, int price, out string reason)
        {
            if (!TrySpendFromWallet(price, out reason))
            {
                return false;
            }

            if (!TryPermanentRemoveCardFromDeck(cardKey, out reason))
            {
                PlayerInventory.Wallet.Add(price);
                return false;
            }

            reason = string.Empty;
            return true;
        }

        /// <summary>
        /// 잡화점 진열용 — 카탈로그 카드 id를 덱 목록과 같은 스냅샷 빌더로 투영한다(상태 무변경).
        /// 진열대의 실물 카드 프레임이 실제 카드 렌더와 같은 데이터를 쓰게 하는 공개 통로다
        /// (<see cref="TryPreviewRefinedCardSnapshots"/>가 같은 빌더를 여는 선례, 보드 ⑩ T2).
        /// </summary>
        public bool TryCreateCatalogCardSnapshot(string cardId, out CombatCardSnapshot snapshot)
        {
            snapshot = default;
            var entry = CardCatalog.Entries.FirstOrDefault(
                candidate => string.Equals(candidate.Id, cardId, StringComparison.Ordinal));
            if (entry == null)
            {
                return false;
            }

            var card = entry.ToCardDefinition(CardCatalog.SourceId, CreateRuntimeInstanceId(entry.Id, "shop-display"));
            snapshot = CreateDeckListSnapshot(card, "Shop", discarded: false);
            return true;
        }

        /// <summary>
        /// 덱 전체(손패·뽑을 더미·버림 더미)에서 카드 한 장을 영구 제거한다. 이미 소멸된 카드는
        /// 대상이 아니다. 런 지속 덱(<see cref="PlayerDeck"/>)에서도 같은 인스턴스를 지워
        /// 세이브 복원 후에도 제거가 유지되게 한다.
        /// </summary>
        public bool TryPermanentRemoveCardFromDeck(string cardKey, out string reason)
        {
            if (string.IsNullOrWhiteSpace(cardKey))
            {
                reason = "Card to remove is required.";
                return false;
            }

            var card = FindRemovableCard(MovementDeck, cardKey) ?? FindRemovableCard(ActionDeck, cardKey);
            if (card == null)
            {
                reason = "Card to remove was not found in the deck.";
                return false;
            }

            var deck = MovementDeck.Hand.Contains(card) || MovementDeck.DrawPile.Contains(card) || MovementDeck.DiscardPile.Contains(card)
                ? MovementDeck
                : ActionDeck;
            if (!deck.PermanentRemoveAnywhere(card))
            {
                reason = "Card to remove was not found in the deck.";
                return false;
            }

            // 카드 효과가 만든 런타임 사본처럼 영구 덱에 없는 인스턴스면 RemoveCard는 조용히
            // 같은 덱을 돌려준다 — 전투 덱에서만 지워지는 것이 맞다.
            PlayerDeck = PlayerDeck.RemoveCard(card.InstanceId);
            reason = string.Empty;
            return true;
        }

        /// <summary>
        /// 상점 오브젝트를 소비한다(1회 방문 소비 — 닫으면 끝, 다시 열리지 않는다). 보상 이벤트
        /// 오브젝트와 같은 소비 대장(<see cref="ClaimedEventObjectIds"/>)을 쓰므로 세이브
        /// 중단→재개에서도 소비가 유지된다.
        /// </summary>
        public bool TryConsumeShopObject(string objectId, out string reason)
        {
            if (string.IsNullOrWhiteSpace(objectId))
            {
                reason = "Shop object id is required.";
                return false;
            }

            if (!claimedEventObjectIds.Add(objectId))
            {
                reason = "Shop was already consumed.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private bool TrySpendFromWallet(int price, out string reason)
        {
            if (PlayerInventory?.Wallet == null)
            {
                reason = "Player wallet is unavailable.";
                return false;
            }

            return PlayerInventory.Wallet.TrySpend(price, out reason);
        }

        private static CardDefinition FindRemovableCard(CardDeckState deck, string cardKey)
        {
            return deck.Hand.FirstOrDefault(candidate => MatchesCardKey(candidate, cardKey))
                   ?? deck.DrawPile.FirstOrDefault(candidate => MatchesCardKey(candidate, cardKey))
                   ?? deck.DiscardPile.FirstOrDefault(candidate => MatchesCardKey(candidate, cardKey));
        }
    }
}

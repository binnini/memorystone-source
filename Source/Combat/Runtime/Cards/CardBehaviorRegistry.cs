using System;
using System.Collections.Generic;
using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>
    /// 카드 id → <see cref="CardBehavior"/>. 등록 목록은 <c>CardBehaviorRegistry.Cards.cs</c>(카드당 한 줄).
    /// 등록 누락은 컴파일이 아니라 출하 데이터 테스트가 잡는다(카탈로그 전 카드에 클래스 존재 · 등록 id는 전부 카탈로그에 존재).
    /// </summary>
    public static partial class CardBehaviorRegistry
    {
        private static readonly CardBehavior Unregistered = new UnregisteredCardBehavior();
        private static readonly Dictionary<string, CardBehavior> ById = Build();

        public static IReadOnlyCollection<string> RegisteredIds => ById.Keys;

        public static bool TryGet(string cardId, out CardBehavior behavior)
        {
            if (string.IsNullOrEmpty(cardId))
            {
                behavior = null;
                return false;
            }

            return ById.TryGetValue(cardId, out behavior);
        }

        public static CardBehavior Get(string cardId)
        {
            if (TryGet(cardId, out var behavior))
            {
                return behavior;
            }

            throw new KeyNotFoundException($"No CardBehavior is registered for card id '{cardId}'.");
        }

        /// <summary>
        /// 카드 정의의 동작. 카탈로그 밖 카드(테스트 픽스처)는 <see cref="UnregisteredCardBehavior"/>를 받는다 —
        /// 그 클래스 주석의 이유로 출하 카드는 여기로 떨어지면 안 된다.
        /// </summary>
        public static CardBehavior Resolve(CardDefinition card)
        {
            return card != null && TryGet(card.Id, out var behavior) ? behavior : Unregistered;
        }

        private static Dictionary<string, CardBehavior> Build()
        {
            var map = new Dictionary<string, CardBehavior>(StringComparer.Ordinal);
            foreach (var behavior in CreateAll())
            {
                if (string.IsNullOrWhiteSpace(behavior.Id))
                {
                    throw new InvalidOperationException($"{behavior.GetType().Name} has an empty card id.");
                }

                if (map.ContainsKey(behavior.Id))
                {
                    throw new InvalidOperationException($"Card id '{behavior.Id}' is registered twice ({map[behavior.Id].GetType().Name}, {behavior.GetType().Name}).");
                }

                map.Add(behavior.Id, behavior);
            }

            return map;
        }
    }
}

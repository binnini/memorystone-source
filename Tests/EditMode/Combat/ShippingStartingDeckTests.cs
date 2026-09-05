using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Unity;
using UnityEditor;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 출하 시작 덱의 저작 계약(2026-09-05 실플레이 피드백: "빌드한 후 새로 시작했는데 강화된 카드가
    /// 섞여 있음").
    ///
    /// <para>🔴 원인은 세이브 누수가 아니라 <b>저작</b>이었다 — 프로토타입 시작 덱의 M03 한 장이
    /// <c>upgradeLevel: 1</c>로 박혀 있었다. 연마(공작소) 시험 흔적으로 보인다. 새 판은 이 에셋을
    /// 그대로 펴므로, 여기 남은 강화는 <b>모든 새 판</b>에 따라온다.</para>
    ///
    /// <para>🔑 이 게이트가 잡는 것은 「강화가 나쁘다」가 아니라 <b>「시작 덱에 우연히 남았다」</b>다.
    /// 시작부터 강화 카드를 주는 것이 설계라면 이 테스트를 고치고 그 결정을 여기에 적을 것.</para>
    /// </summary>
    [Category("ShippingData")]
    public sealed class ShippingStartingDeckTests
    {
        [Test]
        public void NoShippedStartingDeckCarriesPreUpgradedCards()
        {
            var guids = AssetDatabase.FindAssets("t:PlayerStartingDeckAsset");
            Assert.That(guids, Is.Not.Empty, "출하 시작 덱 에셋을 하나도 못 찾았다 — 경로가 바뀌었는지 확인할 것.");

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var deck = AssetDatabase.LoadAssetAtPath<PlayerStartingDeckAsset>(path);
                Assert.That(deck, Is.Not.Null, path);

                foreach (var card in deck.MovementCards.Concat(deck.ActionCards))
                {
                    Assert.That(card.UpgradeLevel, Is.Zero,
                        $"{path}: 시작 덱 카드 '{card.CardId}'가 강화된 채로 저작돼 있다 — 새 판마다 따라온다.");
                }
            }
        }
    }
}

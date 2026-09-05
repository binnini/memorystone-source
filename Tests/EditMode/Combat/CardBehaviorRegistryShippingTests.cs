#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Runtime.Cards;
using SeoulPlayup.Combat.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 카드 클래스 전환(DEC-2026-09-06-01)의 배선 감시자. 카드 클래스 등록은 컴파일이 아니라 이 테스트가 잡는다 —
    /// 클래스 파일을 쓰고 <c>CardBehaviorRegistry.Cards.cs</c>에 한 줄을 빠뜨리면 그 카드는
    /// <see cref="CardBehaviorRegistry.Resolve"/>에서 미등록 폴백으로 조용히 떨어진다(S11: 조용한 폴백 금지).
    /// 반대 방향(등록됐지만 카탈로그에 없는 id)은 지워진 카드의 클래스가 남는 드리프트를 잡는다.
    /// </summary>
    [Category("ShippingData")]
    public sealed class CardBehaviorRegistryShippingTests
    {
        [Test]
        public void EveryShippingCatalogCardHasARegisteredBehavior()
        {
            var catalog = ShippingCatalog();
            var missing = catalog.Entries
                .Select(entry => entry.Id)
                .Where(id => !CardBehaviorRegistry.TryGet(id, out _))
                .OrderBy(id => id, System.StringComparer.Ordinal)
                .ToList();

            Assert.That(missing, Is.Empty,
                "cards.csv에는 있는데 CardBehaviorRegistry에 클래스가 없는 카드: " + string.Join(", ", missing));
        }

        [Test]
        public void EveryRegisteredBehaviorIdExistsInTheShippingCatalog()
        {
            var catalogIds = ShippingCatalog().Entries.Select(entry => entry.Id).ToHashSet(System.StringComparer.Ordinal);
            var orphans = CardBehaviorRegistry.RegisteredIds
                .Where(id => !catalogIds.Contains(id))
                .OrderBy(id => id, System.StringComparer.Ordinal)
                .ToList();

            Assert.That(orphans, Is.Empty,
                "CardBehaviorRegistry에 등록됐지만 cards.csv에 없는 id: " + string.Join(", ", orphans));
        }

        [Test]
        public void ResolvedBehaviorIdMatchesTheCardItWasResolvedFor()
        {
            // 클래스가 남의 id를 돌려주면(복사·붙여넣기 실수) 레지스트리 키와 Id가 어긋난다 — Build()가 중복은 막지만
            // 「A01 클래스가 Id "A01"을, 파일명은 A02」 같은 어긋남은 여기서만 보인다.
            foreach (var entry in ShippingCatalog().Entries)
            {
                var behavior = CardBehaviorRegistry.Get(entry.Id);
                Assert.That(behavior.Id, Is.EqualTo(entry.Id), behavior.GetType().Name);
                StringAssert.StartsWith(entry.Id + "_", behavior.GetType().Name,
                    "클래스 이름은 카드 id 접두(D-4): " + behavior.GetType().Name);
            }
        }

        private static CardCatalogDefinition ShippingCatalog()
        {
            var asset = ScriptableObject.CreateInstance<CardCatalogAsset>();
            try
            {
                asset.SetRows(CardCatalogAsset.ParseCsvText(
                    File.ReadAllText(CombatCsvPaths.CardsCsv, new UTF8Encoding(false, true))));
                return asset.ToCardCatalogDefinition(CombatConfig.Default);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }
    }
}
#endif

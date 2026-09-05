#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 출하 <c>cards.csv</c>가 <b>실제로 효과를 내는지</b> 본다 — 픽스처가 아니라 저작 파일로.
    ///
    /// 이 테스트가 없어서 사고가 났다(2026-08-02 실플레이). U04 호롱불은
    /// <see cref="TorchAndTrapDisarmTests"/>가 4건이나 덮고 있었지만, 그 테스트들은 cards.csv를
    /// 옮겨 적은 <b>손으로 쓴 픽스처</b>(amount 3)를 쓴다. 정작 출하 CSV에는 그 3을 실어 나를 칸이
    /// 비어 있어 <c>ResolveAmount</c>가 0을 돌려줬고, <c>GrantTorchLight</c>가
    /// <c>amount &lt;= 0</c>에서 조용히 되돌아갔다. 결과: 초록 게이트 아래에서 <b>카드가 코스트만
    /// 쓰고 아무 일도 하지 않았다.</b> 픽스처는 저작과 어긋난 순간 거짓말을 시작한다.
    ///
    /// 그래서 여기서는 CSV를 런타임과 같은 파싱 경로(<c>ParseCsvText</c> →
    /// <c>ToCardCatalogDefinition</c>)로 태운 카탈로그로 <see cref="CombatState"/>를 만들고,
    /// 카드를 <b>실제로 내서</b> 결과를 확인한다. 저작 칸이 비면 픽스처가 아니라 이 테스트가 깨진다.
    /// </summary>
    [Category("ShippingData")]
    public sealed class ShippingCardCsvEffectTests
    {
        private const int BaseVisionRange = 3;
        private static readonly HexCoord TrapCoord = new HexCoord(1, 0);

        // ------------------------------------------------------------------ U04 호롱불

        /// <summary>
        /// 회귀 잠금: 출하 저작으로 낸 호롱불이 시야를 실제로 넓혀야 한다.
        /// duration 칸이 다시 비면 amount가 0이 되고 이 단언이 먼저 깨진다.
        /// </summary>
        [Test]
        public void ShippingTorchCardActuallyWidensVision()
        {
            var state = CreateShippingState();
            var edge = new HexCoord(BaseVisionRange + 2, 0);
            Assert.That(state.GetVisibility(edge), Is.Not.EqualTo(HexCellVisibility.Revealed), "전제: 아직 안 보인다.");

            Assert.That(
                state.TryPlayerUtility(ApprovedCardCatalogFactory.UtilityTorchId),
                Is.True,
                state.LastFailureReason);

            Assert.That(TorchAmount(state), Is.GreaterThan(0),
                "출하 CSV의 호롱불이 반경 0을 부여했다 — 카드가 코스트만 쓰고 아무 일도 하지 않는다.");
            Assert.That(state.GetVisibility(edge), Is.EqualTo(HexCellVisibility.Revealed),
                "부여 즉시 안개가 걷혀야 한다.");
        }

        /// <summary>
        /// C-14의 규칙 "지속시간 = 초기 반경"이 <b>저작 한 칸</b>에서 나오는지.
        /// 둘을 따로 저작할 수 있게 되면 "반경 3인데 5턴"이 표현 가능해진다.
        /// </summary>
        [Test]
        public void ShippingTorchTiesItsLifetimeToItsRadius()
        {
            var torch = ShippingEntry(ApprovedCardCatalogFactory.UtilityTorchId);

            Assert.That(torch.Amount, Is.GreaterThan(0), "저작된 초기 반경이 0이다.");
            Assert.That(torch.DurationTurns, Is.EqualTo(torch.Amount),
                "수명과 밝기가 갈라졌다 — 둘 중 하나가 무의미해지는 저작이 가능해진 상태다.");
        }

        // ------------------------------------------------------------------ 나머지 4종

        /// <summary>X02 깨진 유리: 손패에 남으면 턴 종료 시 저작된 피해를 준다.</summary>
        [Test]
        public void ShippingBrokenGlassCardCarriesItsDamage()
        {
            var glass = ShippingEntry(ApprovedCardCatalogFactory.StatusBrokenGlassId);

            Assert.That(glass.Amount, Is.GreaterThan(0),
                "깨진 유리의 피해가 0이다 — 손에 쥐고 있어도 아프지 않다.");
        }

        /// <summary>
        /// S05 돌 다리 두드리기(2026-08-20 #5): 출하 저작으로 낸 카드가 <b>UI와 같은 진입점</b>
        /// (TryPlayerScout)에서 범위를 탐색하고 함정을 실제로 없애야 한다 — 예전 버그의 정체가
        /// "룰 API는 멀쩡한데 UI 경로에서 no-op"이었으므로, 회귀 잠금도 그 경로에서 잰다.
        /// </summary>
        [Test]
        public void ShippingTrapDisarmCardActuallyRemovesTheTrap()
        {
            Assert.That(ShippingEntry(ApprovedCardCatalogFactory.ScoutTrapDisarmId).AreaRadius, Is.EqualTo(1),
                "S05의 scout 칸이 비면 탐색 반경이 조용히 기본 2로 벌어진다 — 저작은 1이다.");

            var state = CreateShippingState(withTrap: true);
            Assert.That(state.ConsumedTrapIds, Is.Empty, "전제: 아직 아무 함정도 소모되지 않았다.");

            Assert.That(
                state.TryPlayerScout(TrapCoord, ApprovedCardCatalogFactory.ScoutTrapDisarmId),
                Is.True,
                state.LastFailureReason);

            Assert.That(state.ConsumedTrapIds, Does.Contain("disarm-target"),
                "출하 저작으로 낸 해체 카드가 함정을 없애지 못했다(발견까지 카드 몫이다).");
        }

        /// <summary>
        /// X01·X02·X03은 어떤 페이즈에서도 못 내는 카드다. 저작이 그 계약을 지키는지 —
        /// includeInDecks=TRUE로 새면 정상 덱에 섞여 손패를 막는다.
        /// </summary>
        [Test]
        public void ShippingStatusCardsStayUnplayableAndOutOfNormalDecks()
        {
            foreach (var id in new[]
                     {
                         ApprovedCardCatalogFactory.StatusFineDustId,
                         ApprovedCardCatalogFactory.StatusBrokenGlassId,
                         ApprovedCardCatalogFactory.StatusBlackoutId,
                     })
            {
                var entry = ShippingEntry(id);
                Assert.That(entry.IncludeInGameplayDecks, Is.False, $"{id}가 정상 덱에 섞인다.");
                Assert.That(entry.Cost, Is.Zero, $"{id}는 낼 수 없으므로 코스트가 0이어야 한다.");
            }
        }

        /// <summary>
        /// 이 클래스가 막으려는 구멍 자체를 잠근다: <b>저작으로 값을 실어야 하는 behaviorId</b>가
        /// amount 0으로 출하되면 안 된다. 새 카드가 같은 방식으로 조용히 죽는 것을 막는다.
        /// </summary>
        [Test]
        public void NoShippingCardNeedsAnAmountItDoesNotAuthor()
        {
            // amount가 0이면 효과가 통째로 사라지는(또는 무의미해지는) 저작들.
            var amountBearing = new HashSet<string>(StringComparer.Ordinal)
            {
                CardEffectRefs.UtilityTorch,
            };

            var offenders = ShippingEntries()
                .Where(entry => amountBearing.Contains(entry.EffectRef) && entry.Amount <= 0)
                .Select(entry => $"{entry.Id}({entry.EffectRef})")
                .ToArray();

            Assert.That(offenders, Is.Empty,
                "저작 amount가 0이라 런타임에서 조용히 무발동하는 카드: " + string.Join(", ", offenders));
        }

        // ------------------------------------------------------------------ 픽스처가 아닌 실제 저작

        /// <summary>
        /// 출하 cards.csv를 런타임과 같은 경로로 파싱한다(card-catalog-audit 도구와 동일).
        ///
        /// ⚠️<b>캐시하지 않는다.</b> 처음엔 static 필드에 담아 뒀는데, Unity는 스크립트가 안 바뀌면
        /// 테스트 실행 사이에 도메인을 리로드하지 않아 <b>이전 실행이 읽은 CSV가 그대로 살아남았다</b> —
        /// 저작을 되돌려 놓고 돌려도 테스트가 통과해서, 이 감사가 저작을 보고 있지 않다는 걸
        /// 한참 뒤에야 알았다(2026-08-02). 저작을 읽는 테스트는 매번 디스크에서 다시 읽어야 한다.
        /// </summary>
        private static CardCatalogDefinition ShippingCatalog()
        {
            var asset = ScriptableObject.CreateInstance<CardCatalogAsset>();
            CardCatalogDefinition catalog;
            try
            {
                asset.SetRows(CardCatalogAsset.ParseCsvText(
                    File.ReadAllText(CombatCsvPaths.CardsCsv, new UTF8Encoding(false, true))));
                catalog = asset.ToCardCatalogDefinition(CombatConfig.Default);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }

            Assert.That(catalog.Entries, Is.Not.Empty, "출하 카드를 하나도 못 읽었다 — 감사가 빈 채로 통과하고 있다.");
            return catalog;
        }

        private static IEnumerable<CardCatalogEntry> ShippingEntries()
        {
            return ShippingCatalog().Entries;
        }

        private static CardCatalogEntry ShippingEntry(string cardId)
        {
            var entry = ShippingEntries().SingleOrDefault(candidate => candidate.Id == cardId);
            Assert.That(entry, Is.Not.Null, $"출하 cards.csv에 {cardId}가 없다.");
            return entry;
        }

        private static CombatState CreateShippingState(bool withTrap = false)
        {
            var traps = withTrap
                ? new[]
                {
                    new HexTrapData(
                        "disarm-target",
                        TrapCoord,
                        radius: 0,
                        effects: new[] { new HexTrapEffectData(HexTrapEffectKind.Damage, 5) })
                }
                : null;

            var config = new CombatConfig(20, 10, 3, 1, 4, 4, 0, 1, 0, 4, 1, 4, BaseVisionRange);
            var map = new HexMapData(
                Enumerable.Range(0, 13).Select(q => new HexCellData(new HexCoord(q, 0), $"cell-{q}", "street", 1, true, false)),
                trapRefs: traps);

            var state = new CombatState(
                map,
                new HexCoord(0, 0),
                Array.Empty<MonsterConfig>(),
                config,
                cardCatalog: ShippingCatalog());

            // 유틸리티·정찰은 행동 페이즈 카드다. 손에 쥐여 준 뒤 그 페이즈로 넘긴다.
            state.ActionDeck.InjectIntoHand(CardInstance(state, ApprovedCardCatalogFactory.UtilityTorchId));
            state.ActionDeck.InjectIntoHand(CardInstance(state, ApprovedCardCatalogFactory.ScoutTrapDisarmId));
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction));
            return state;
        }

        private static CardDefinition CardInstance(CombatState state, string cardId)
        {
            var entry = state.CardCatalog.Entries.Single(candidate => candidate.Id == cardId);
            return entry.ToCardDefinition(state.CardCatalog.SourceId, $"{cardId}#shipping-test");
        }

        private static int TorchAmount(CombatState state)
        {
            var torch = state.ActiveEffects.FirstOrDefault(effect => effect.Kind == StatusEffectKind.TorchLight);
            return torch.Kind == StatusEffectKind.TorchLight ? torch.Amount : 0;
        }

    }
}
#endif

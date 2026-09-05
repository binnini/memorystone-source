using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// T2 페이즈 B(2026-08-06) 유물 확장 계약 — 신규 축 7종·양날(extraEffects)·턴 제한(청사초롱)·
    /// 저주 유물 폐기. 확정 정본: docs/design/keyword-systematization-and-sts-insights.md §4 T2-D.
    /// </summary>
    public sealed class RelicExpansionTests
    {
        [Category("ShippingData")]
        [Test]
        public void CatalogShipsExpandedRelicSet()
        {
            var catalog = RelicCatalogCsv.ConvertFile(CombatCsvPaths.RelicsCsv);

            // 기존 8(지역명 개명) + 신규 11 − 금 간 기억 삭제 = 19, 페이즈 C 트리거 8종이 붙어 27.
            // T4-3(2026-08-07): 배달 가방(BagSlotBonus)이 붙어 28. 전부 Relic.
            // 감투 삭제(DEC-2026-08-31-01 D1)로 28 → 27.
            Assert.That(catalog.Entries.Count, Is.EqualTo(27));
            Assert.That(catalog.Entries.All(entry => entry.Kind == PlayerPermanentItemKind.Relic), Is.True);

            Assert.That(catalog.TryGet("relic-blue-lantern", out var lantern), Is.True);
            Assert.That(lantern.EffectKind, Is.EqualTo(PlayerPermanentItemEffectKind.VisionRangeBonus));
            Assert.That(lantern.DurationTurns, Is.EqualTo(20), "청사초롱은 턴 제한 유물의 원형.");

            Assert.That(catalog.TryGet("relic-nightmarket-firecracker", out var firecracker), Is.True);
            // 🔑 계약은 "대가가 있다"이지 "대가가 정확히 하나"가 아니다(2026-08-31 T4) —
            // 대가를 하나 더 저작하면 깨져야 할 이유가 없다. 어떤 대가인지는 아래 두 줄이 잠근다.
            Assert.That(firecracker.ExtraEffects, Is.Not.Empty, "양날 유물은 extraEffects로 대가를 진다.");
            Assert.That(firecracker.ExtraEffects[0].Kind, Is.EqualTo(PlayerPermanentItemEffectKind.IncomingDamageDelta));
            Assert.That(firecracker.ExtraEffects[0].Amount, Is.EqualTo(1));

            Assert.That(catalog.TryGet("relic-heavy-backpack", out var backpack), Is.True);
            Assert.That(backpack.ExtraEffects[0].Kind, Is.EqualTo(PlayerPermanentItemEffectKind.BlockGainPenalty));

            // 개명 확인(지역명 금지 원칙) — id는 세이브 안정 때문에 그대로다.
            Assert.That(catalog.TryGet(PlayerPermanentItemCatalog.HanriverShoesRelicId, out var shoes), Is.True);
            Assert.That(shoes.DisplayName, Is.EqualTo("강변 러닝화"));
        }

        [Test]
        public void CurseKindRowIsRejectedAtImport()
        {
            const string csv =
                "id,kind,displayNameKo,descriptionKo,effectKind,effectAmount,extraEffects,durationTurns\n" +
                "curse-x,Curse,저주 테스트,테스트.,IncomingDamageDelta,1,,\n";
            Assert.Throws<System.ArgumentException>(
                () => RelicCatalogCsv.ConvertText(csv),
                "저주 유물은 T2로 폐기 — 임포트가 거부해야 한다.");
        }

        [Test]
        public void GateAppliesOneTimeEffectsIncludingExtras()
        {
            var state = CreateState();
            var maxHpBefore = state.Player.MaxHp;

            // 마이너스 통장: 대가(MaxHpPenalty:5)가 extraEffects에 있어도 관문에서 1회 적용돼야 한다.
            Assert.That(state.TryGrantPermanentItem("relic-pawnshop-contract", out var reason), Is.True, reason);
            Assert.That(state.Player.MaxHp, Is.EqualTo(maxHpBefore - 5));

            Assert.That(state.TryGrantPermanentItem("relic-samjogo-emblem", out reason), Is.True, reason);
            Assert.That(state.PlayerInventory.MightStacks, Is.EqualTo(1), "삼족오 타투은 획득 즉시 힘 +1.");

            // 복주머니 +40에 전당포의 돈 획득 +10이 단일 이음매에서 가산된다.
            Assert.That(state.TryGrantPermanentItem("relic-lucky-pouch", out reason), Is.True, reason);
            Assert.That(state.PlayerInventory.Wallet.Balance, Is.EqualTo(50));
        }

        [Test]
        public void LanternExpiresAfterTwentyTicksButChipRemains()
        {
            var state = CreateState();
            Assert.That(state.TryGrantPermanentItem("relic-blue-lantern", out var reason), Is.True, reason);
            Assert.That(state.GetRelicEffectTotal(PlayerPermanentItemEffectKind.VisionRangeBonus), Is.EqualTo(1));

            var item = state.PlayerInventory.RelicsAndCurses.Items.Single(candidate => candidate.Id == "relic-blue-lantern");
            Assert.That(item.RemainingActiveTurns, Is.EqualTo(20));

            for (var i = 0; i < 20; i++)
            {
                state.PlayerInventory.RelicsAndCurses.TickLimitedRelicTurns();
            }

            Assert.That(state.GetRelicEffectTotal(PlayerPermanentItemEffectKind.VisionRangeBonus), Is.EqualTo(0),
                "만료된 청사초롱은 효과가 꺼진다.");
            Assert.That(item.IsActive, Is.False);
            Assert.That(item.EffectSummary, Does.Contain("만료"));
            Assert.That(state.PlayerInventory.RelicsAndCurses.Items.Any(candidate => candidate.Id == "relic-blue-lantern"),
                Is.True, "만료돼도 삭제하지 않는다 — 칩은 비활성으로 남는다(사용자 확정).");
        }

        [Test]
        public void LanternTicksOncePerPlayerTurn()
        {
            var state = CreateState();
            Assert.That(state.TryGrantPermanentItem("relic-blue-lantern", out var reason), Is.True, reason);

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            var item = state.PlayerInventory.RelicsAndCurses.Items.Single(candidate => candidate.Id == "relic-blue-lantern");
            Assert.That(item.RemainingActiveTurns, Is.EqualTo(19), "턴 제한은 플레이어 턴 시작마다 1씩 줄어든다.");
        }

        [Test]
        public void HeavyBackpackShavesBlockGain()
        {
            var state = CreateState();
            Assert.That(state.TryGrantPermanentItem("relic-heavy-backpack", out var reason), Is.True, reason);

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.TryPlayerDefend("D00"), Is.True, state.LastFailureReason);

            Assert.That(state.Player.Block, Is.EqualTo(2), "방어막 3 − 배낭 대가 1 = 2 (파열과 같은 감산 지점).");
        }

        [Test]
        public void ShopDiscountShavesEveryPriceWithFloorOfOne()
        {
            var discounted = ShopPrices.Default.WithDiscountPercent(20);
            foreach (var entry in ShopPrices.Default.Entries)
            {
                var match = discounted.Entries.Single(candidate => candidate.Kind == entry.Kind && candidate.Rarity == entry.Rarity);
                Assert.That(match.Price, Is.EqualTo(System.Math.Max(1, entry.Price * 80 / 100)));
            }

            Assert.That(ShopPrices.Default.WithDiscountPercent(0), Is.SameAs(ShopPrices.Default),
                "할인 0%면 표를 복제하지 않는다.");
        }

        [Test]
        public void SaveRoundtripKeepsRemainingLanternTurns()
        {
            var state = CreateState();
            Assert.That(state.TryGrantPermanentItem("relic-blue-lantern", out var reason), Is.True, reason);
            for (var i = 0; i < 5; i++)
            {
                state.PlayerInventory.RelicsAndCurses.TickLimitedRelicTurns();
            }

            var item = state.PlayerInventory.RelicsAndCurses.Items.Single(candidate => candidate.Id == "relic-blue-lantern");
            var restored = PlayerPermanentItemSaveData.FromState(item).ToState();
            Assert.That(restored.RemainingActiveTurns, Is.EqualTo(15));
            Assert.That(restored.IsActive, Is.True);

            // 구 세이브(필드 부재 → −1)는 새로 산 것처럼 전량 복원 — 청사초롱 출하 전 세이브라 손해 볼 사람이 없다.
            var legacy = new PlayerPermanentItemSaveData { Id = "relic-blue-lantern", Kind = PlayerPermanentItemKind.Relic };
            Assert.That(legacy.ToState().RemainingActiveTurns, Is.EqualTo(20));
        }

        // ------------------------------------------------------------------ helpers

        private static CombatState CreateState()
        {
            var config = TestCombatConfigs.Standard(actionBudget: 4, movementHandSize: 1, actionHandSize: 4);
            return new CombatState(
                CombatState.CreateDemoMap(3),
                new HexCoord(0, 0),
                new HexCoord(3, 0),
                config,
                cardCatalog: CreateCatalog());
        }

        private static CardCatalogDefinition CreateCatalog()
        {
            return new CardCatalogDefinition(
                "relic-expansion-test",
                "Relic expansion test catalog",
                new[]
                {
                    new CardCatalogEntry(
                        ApprovedCardCatalogFactory.Move1HexId, "Move 1", CardCategory.Movement, CardEffectType.Move,
                        1, 1, 1, CardEffectRefs.MoveBasic, "reachable_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(
                        "D00", "방어의 기초", CardCategory.Action, CardEffectType.Defend,
                        1, 0, 3, CardEffectRefs.DefendBlock, "self", status: CardCatalogStatus.Approved),
                });
        }
    }
}

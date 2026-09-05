using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.CardCore;
using System.Reflection;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// T4-1 소모품 가방(RC-8 개정) 감사. 계약: 슬롯 3(기본) · 사용 무비용 · 자기 턴(이동/액션)
    /// 중 언제든 · 성공 시 즉시 소비. 효과는 전부 기존 이음매(회복·방어막·정화·수호·등불·기절
    /// 부여·은신)로 배선되고 수치는 consumable_items.csv가 정본이다.
    /// </summary>
    public sealed class ConsumableItemTests
    {
        [Test]
        [Category("ShippingData")]
        public void ShippingCatalogParsesTwelveItems()
        {
            var catalog = ConsumableItemCatalogCsv.ConvertFile(CombatCsvPaths.ConsumableItemsCsv);

            // 🔑 개수 고정을 **일부러** 유지한다(2026-08-31 T4 판정). 12는 오늘의 저작이 아니라
            // 확정된 콘텐츠 범위이고(테스트 이름이 그렇게 말한다), 13번째가 생기는 것은 사고가
            // 아니라 결정이다 — 그 결정을 사람 손으로 통과시키게 하는 것이 이 줄의 값어치다.
            // 도감 쪽 사본은 T4에서 제거했다(CodexP2DomainTests) — 같은 숫자는 여기 한 곳뿐이다.
            Assert.That(catalog.Entries, Has.Count.EqualTo(12), "T4 확정 콘텐츠는 12종이다.");
            Assert.That(catalog.TryGet("item-vitality-flask", out var water), Is.True);
            Assert.That(water.Amount, Is.EqualTo(8));
            Assert.That(catalog.Entries.Where(item => item.Targeting != ConsumableItemTargeting.None).Select(item => item.Id),
                Is.EquivalentTo(new[] { "item-flame-bead", "item-daze-bead", "item-barrier-bead" }),
                "대상 지정형은 화염 구슬·혼미 구슬·결계 구슬 3종뿐이다.");
        }

        [Test]
        public void UsingAnItemRaisesBagItemUsedOnceAndOnlyOnSuccess()
        {
            var state = CreateState();
            var used = new List<string>();
            state.BagItemUsed += used.Add;
            Assert.That(state.TryAddBagItem("item-vitality-flask"), Is.True);

            Assert.That(state.TryUseBagItem("item-vitality-flask"), Is.True, state.LastFailureReason);
            Assert.That(used, Is.EqualTo(new[] { "item-vitality-flask" }), "사용음은 소비 한 번에 한 번이다.");

            Assert.That(state.TryUseBagItem("item-vitality-flask"), Is.False, "가방에 없으니 실패해야 한다.");
            Assert.That(used, Has.Count.EqualTo(1), "실패한 사용은 신호를 내지 않는다.");
        }

        [Test]
        public void HealItemHealsAndIsConsumed()
        {
            var state = CreateState();
            state.Player.ApplyDamage(10);
            var hpBefore = state.Player.Hp;
            Assert.That(state.TryAddBagItem("item-vitality-flask"), Is.True);

            Assert.That(state.TryUseBagItem("item-vitality-flask"), Is.True, state.LastFailureReason);

            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore + 8));
            Assert.That(state.PlayerInventory.Bag.UsedSlotCount, Is.EqualTo(0), "사용한 아이템은 즉시 소비된다.");
        }

        [Test]
        public void BlockItemGrantsBlockWithoutSpendingKi()
        {
            var state = CreateState();
            var kiBefore = state.ActionCostRemaining;
            Assert.That(state.TryAddBagItem("item-bulwark-flask"), Is.True);

            Assert.That(state.TryUseBagItem("item-bulwark-flask"), Is.True, state.LastFailureReason);

            Assert.That(state.Player.Block, Is.EqualTo(6));
            Assert.That(state.ActionCostRemaining, Is.EqualTo(kiBefore), "아이템 사용은 무비용이다.");
        }

        [Test]
        public void CleanseItemStripsPlayerDebuffs()
        {
            var state = CreateState();
            ApplyDurationStatus(state, StatusEffectKind.Poison, "player", 3, 3);
            Assert.That(HasPlayerEffect(state, StatusEffectKind.Poison), Is.True, "픽스처: 중독이 걸려 있어야 한다.");
            Assert.That(state.TryAddBagItem("item-cleanse-flask"), Is.True);

            Assert.That(state.TryUseBagItem("item-cleanse-flask"), Is.True, state.LastFailureReason);

            Assert.That(HasPlayerEffect(state, StatusEffectKind.Poison), Is.False);
        }

        [Test]
        public void GuardItemGrantsGuardAndRejectsWhileAlreadyGuarded()
        {
            var state = CreateState();
            Assert.That(state.TryAddBagItem("item-ward-flask"), Is.True);
            Assert.That(state.TryAddBagItem("item-ward-flask"), Is.True);

            Assert.That(state.TryUseBagItem("item-ward-flask"), Is.True, state.LastFailureReason);
            Assert.That(HasPlayerEffect(state, StatusEffectKind.Guard), Is.True);

            // 수호는 최대 1충전(GrantGuardCharge 보유 게이트) — 조용히 낭비되는 대신 사전 거부한다.
            Assert.That(state.TryUseBagItem("item-ward-flask"), Is.False);
            Assert.That(state.PlayerInventory.Bag.UsedSlotCount, Is.EqualTo(1), "거부된 사용은 소비되지 않는다.");
        }

        [Test]
        public void KiItemAddsKiThisTurn()
        {
            var state = CreateState();
            var kiBefore = state.ActionCostRemaining;
            Assert.That(state.TryAddBagItem("item-vigor-flask"), Is.True);

            Assert.That(state.TryUseBagItem("item-vigor-flask"), Is.True, state.LastFailureReason);

            Assert.That(state.ActionCostRemaining, Is.EqualTo(kiBefore + 2));
        }

        [Test]
        public void AttackBonusItemRaisesThisTurnAndExpiresNextTurn()
        {
            var state = CreateState();
            Assert.That(state.TryAddBagItem("item-tiger-flask"), Is.True);

            Assert.That(state.TryUseBagItem("item-tiger-flask"), Is.True, state.LastFailureReason);
            Assert.That(state.BagAttackBonusThisTurn, Is.EqualTo(3));

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            Assert.That(state.BagAttackBonusThisTurn, Is.EqualTo(0), "이번 턴 한정 — 턴 경계에서 소거된다.");
        }

        [Test]
        public void AlarmStunsMonstersWithinRadiusOnly()
        {
            var state = CreateState(enemyDistance: 1);
            Assert.That(state.TryAddBagItem("item-thunder-bead"), Is.True);

            Assert.That(state.TryUseBagItem("item-thunder-bead"), Is.True, state.LastFailureReason);

            Assert.That(HasMonsterEffect(state, StatusEffectKind.Stun), Is.True, "반경 1의 몬스터는 기절해야 한다.");

            var farState = CreateState(enemyDistance: 3);
            Assert.That(farState.TryAddBagItem("item-thunder-bead"), Is.True);
            Assert.That(farState.TryUseBagItem("item-thunder-bead"), Is.True, farState.LastFailureReason);
            Assert.That(HasMonsterEffect(farState, StatusEffectKind.Stun), Is.False, "반경 밖 몬스터는 무사하다.");
        }

        [Test]
        public void TorchItemGrantsTorchLight()
        {
            var state = CreateState();
            Assert.That(state.TryAddBagItem("item-light-bead"), Is.True);

            Assert.That(state.TryUseBagItem("item-light-bead"), Is.True, state.LastFailureReason);

            Assert.That(HasPlayerEffect(state, StatusEffectKind.TorchLight), Is.True);
        }

        [Test]
        public void RetiredConsumablesNeverEnterTheBag()
        {
            // 🔴 은신 구슬 은퇴(2026-09-05 사용자 확정: "사용이 어려움"). 정의·핸들러·아이콘은 남기고
            // <b>가방에 들어가는 문</b> 하나만 막는다 — 전리품·상점·뽑기·이벤트·디버그 지급이 전부
            // 그 문을 지나므로 여기가 유일한 관문이다. 되살리려면 은퇴 목록에서 한 줄 빼면 된다.
            var state = CreateState();

            Assert.That(state.TryAddBagItem(ConsumableItemAvailability.VeilBeadId), Is.False,
                "은퇴한 소모품은 어떤 경로로도 가방에 들어오지 않는다.");
            Assert.That(state.PlayerInventory.Bag.UsedSlotCount, Is.Zero);

            // 정의 자체는 살아 있다 — 세이브에 실린 id가 「알 수 없는 아이템」이 되면 이전 판이 깨진다.
            Assert.That(ConsumableItemCatalog.TryGet(ConsumableItemAvailability.VeilBeadId, out _), Is.True,
                "은퇴는 정의를 지우는 것이 아니다.");
        }

        [Test]
        public void UseIsRejectedOutsidePlayerPhases()
        {
            var state = CreateState();
            Assert.That(state.TryAddBagItem("item-vitality-flask"), Is.True);
            Assert.That(state.EndAction(), Is.True);
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.MonsterMovement));

            Assert.That(state.TryUseBagItem("item-vitality-flask"), Is.False, "몬스터 턴에는 아이템을 못 쓴다.");
            Assert.That(state.PlayerInventory.Bag.UsedSlotCount, Is.EqualTo(1));
        }

        // ------------------------------------------------------------------ T4-2 대상 지정형

        [Test]
        public void FirecrackerDamagesMonstersAroundTargetTile()
        {
            var state = CreateState(enemyDistance: 2);
            Assert.That(state.TryAddBagItem("item-flame-bead"), Is.True);

            // 반경 1 폭발 — 몬스터(2,0)의 이웃 칸을 겨냥해도 명중해야 한다.
            Assert.That(state.TryUseBagItem("item-flame-bead", new HexCoord(1, 0)), Is.True, state.LastFailureReason);

            Assert.That(state.Monsters.Single().Hp, Is.EqualTo(5), "피해 5는 CSV 수치가 정본이다.");
            Assert.That(state.PlayerInventory.Bag.UsedSlotCount, Is.EqualTo(0));
        }

        [Test]
        public void FirecrackerRejectsUnseenTileWithoutConsuming()
        {
            var state = CreateState(enemyDistance: 3, playerVisionRange: 1);
            Assert.That(state.TryAddBagItem("item-flame-bead"), Is.True);

            Assert.That(state.TryUseBagItem("item-flame-bead", new HexCoord(3, 0)), Is.False,
                "안개 속 투척은 금지다(시야 대원칙).");
            Assert.That(state.PlayerInventory.Bag.UsedSlotCount, Is.EqualTo(1), "거부된 사용은 소비되지 않는다.");
        }

        [Test]
        public void PepperSprayAppliesBlindAndWeakenToTargetMonster()
        {
            var state = CreateState(enemyDistance: 2);
            Assert.That(state.TryAddBagItem("item-daze-bead"), Is.True);

            Assert.That(state.TryUseBagItem("item-daze-bead", new HexCoord(2, 0)), Is.True, state.LastFailureReason);

            Assert.That(HasMonsterEffect(state, StatusEffectKind.Blind), Is.True);
            Assert.That(HasMonsterEffect(state, StatusEffectKind.Weaken), Is.True);
        }

        [Test]
        public void BlindedMonsterStopsSensingThePlayer()
        {
            // 몬스터 측 실명의 소비 지점(계획기 CreateMonsterFsmContext의 PlayerHidden 비트) — 걸린 몬스터만 나를 놓친다.
            var state = CreateState(enemyDistance: 3, enemyChaseRange: 6);
            Assert.That(state.Monsters[0].Intent.Type, Is.EqualTo(EnemyIntentType.Chase), "픽스처: 실명 전엔 추격.");
            Assert.That(state.TryAddBagItem("item-daze-bead"), Is.True);
            Assert.That(state.TryUseBagItem("item-daze-bead", new HexCoord(3, 0)), Is.True, state.LastFailureReason);

            RunFullTurn(state);

            Assert.That(
                state.Monsters[0].Intent.Type,
                Is.Not.EqualTo(EnemyIntentType.Chase).And.Not.EqualTo(EnemyIntentType.Attack),
                "실명 중에는 그 몬스터의 감지가 불성립한다(은신의 단일 몬스터판).");
        }

        [Test]
        public void TrafficConeBlocksTheCellAndExpiresAfterAuthoredTurns()
        {
            var state = CreateState();
            Assert.That(state.TryAddBagItem("item-barrier-bead"), Is.True);
            var cell = new HexCoord(0, 1);

            Assert.That(state.TryUseBagItem("item-barrier-bead", cell), Is.True, state.LastFailureReason);
            Assert.That(state.Monsters.Any(monster => monster.Coord == cell && !monster.IsDead), Is.True,
                "고깔은 몬스터 호스팅 기물로 칸을 점유한다.");
            Assert.That(state.TryPlayerMove(cell), Is.False, "설치된 고깔 칸으로는 이동할 수 없다.");

            // 수명 3턴(consumable_items.csv durationTurns) — 매 전체 턴 시작에 나이가 오르고 만료 시 제거.
            for (var turn = 0; turn < 3; turn++)
            {
                RunFullTurn(state);
            }

            Assert.That(state.Monsters.Any(monster => monster.Coord == cell && !monster.IsDead), Is.False,
                "수명이 다한 고깔은 제거된다(사망이 아니라 제거 — 보상·연출 없음).");
            Assert.That(state.TryPlayerMove(cell), Is.True, state.LastFailureReason);
        }

        [Test]
        public void TrafficConeRejectsOccupiedCellWithoutConsuming()
        {
            var state = CreateState(enemyDistance: 2);
            Assert.That(state.TryAddBagItem("item-barrier-bead"), Is.True);

            Assert.That(state.TryUseBagItem("item-barrier-bead", new HexCoord(2, 0)), Is.False,
                "몬스터가 선 칸에는 설치할 수 없다.");
            Assert.That(state.PlayerInventory.Bag.UsedSlotCount, Is.EqualTo(1));
        }

        [Test]
        public void TargetedItemsRequireATarget()
        {
            var state = CreateState();
            Assert.That(state.TryAddBagItem("item-flame-bead"), Is.True);

            Assert.That(state.TryUseBagItem("item-flame-bead"), Is.False, "대상 없는 사용은 거부된다.");
            Assert.That(state.PlayerInventory.Bag.UsedSlotCount, Is.EqualTo(1));
        }

        // ------------------------------------------------------------------ T4-3 획득 경로

        [Test]
        public void BagSlotBonusRelicExpandsTheBagLimit()
        {
            var state = CreateState();
            Assert.That(state.GetEffectiveBagSlotLimit(), Is.EqualTo(3));

            Assert.That(state.TryGrantPermanentItem("relic-bobusang-sling", out var reason), Is.True, reason);

            Assert.That(state.GetEffectiveBagSlotLimit(), Is.EqualTo(4), "배달 가방은 가방 슬롯 +1이다.");
            Assert.That(state.TryAddBagItem("item-vitality-flask"), Is.True);
            Assert.That(state.TryAddBagItem("item-bulwark-flask"), Is.True);
            Assert.That(state.TryAddBagItem("item-light-bead"), Is.True);
            Assert.That(state.TryAddBagItem("item-vigor-flask"), Is.True, "4번째 슬롯이 열려 있어야 한다.");
            Assert.That(state.TryAddBagItem("item-ward-flask"), Is.False, "5번째는 없다.");
        }

        [Test]
        public void ShopItemPurchaseGrantsToBagAndRefundsWhenFull()
        {
            var state = CreateState();
            state.PlayerInventory.Wallet.Add(100);

            Assert.That(state.TryPurchaseShopItem("item-vitality-flask", 40, out var reason), Is.True, reason);
            Assert.That(state.PlayerInventory.Wallet.Balance, Is.EqualTo(60));
            Assert.That(state.PlayerInventory.Bag.UsedSlotCount, Is.EqualTo(1));

            Assert.That(state.TryAddBagItem("item-bulwark-flask"), Is.True);
            Assert.That(state.TryAddBagItem("item-light-bead"), Is.True);
            Assert.That(state.TryPurchaseShopItem("item-vigor-flask", 40, out reason), Is.False,
                "가방이 가득이면 구매가 거부된다.");
            Assert.That(state.PlayerInventory.Wallet.Balance, Is.EqualTo(60), "거부된 구매는 환불된다.");
        }

        [Test]
        public void GachaItemOfferGrantsThroughTheClaimGate()
        {
            var state = CreateState();
            var offer = RewardEventObjectOffer.BagItem("item-vitality-flask");
            var machine = CombatEventObjectDefinition.ItemRewardMachine("gacha-item-1", new[] { offer });

            Assert.That(state.TryClaimRewardEventObject(machine, offer, out var reason), Is.True, reason);

            Assert.That(state.PlayerInventory.Bag.Stacks.Single().ItemId, Is.EqualTo("item-vitality-flask"));
            Assert.That(state.ClaimedEventObjectIds, Does.Contain("gacha-item-1"));
        }

        [Test]
        [Category("ShippingData")]
        public void ShippingShopPricesSellConsumablesByRarityTier()
        {
            var prices = ShopPricesCsvConverter.ConvertFile(CombatCsvPaths.ShopPricesCsv);

            // DEC-2026-08-31-02 Q2: 소모품도 유물과 같은 등급 기준가 + 행별 priceDelta로 간다.
            // 종전 「단일가 40」 근거(1회용이라 Rare 카드 50보다 싸게)는 Legendary 두 종이
            // 그 선을 넘으면서 갱신됐다 — 판을 뒤집는 1회용은 카드보다 비쌀 수 있다.
            Assert.That(prices.TryGetPrice(ShopItemKind.Item, CardRarity.Rare, out var rare), Is.True);
            Assert.That(prices.TryGetPrice(ShopItemKind.Item, CardRarity.Epic, out var epic), Is.True);
            Assert.That(prices.TryGetPrice(ShopItemKind.Item, CardRarity.Legendary, out var legendary), Is.True);
            Assert.That(new[] { rare, epic, legendary }, Is.EqualTo(new[] { 30, 45, 60 }));

            // 진열가는 기준가에 그 행의 변주가 더해진 값이다 — 벽력 구슬 60+5, 결계 구슬 30−5.
            Assert.That(ConsumableItemCatalog.TryGet("item-thunder-bead", out var thunder), Is.True);
            Assert.That(legendary + thunder.PriceDelta, Is.EqualTo(65));
            Assert.That(ConsumableItemCatalog.TryGet("item-barrier-bead", out var barrier), Is.True);
            Assert.That(rare + barrier.PriceDelta, Is.EqualTo(25));
        }

        [Test]
        public void ShopRollerStocksDistinctItems()
        {
            var prices = ShopPricesCsvConverter.ConvertFile(CombatCsvPaths.ShopPricesCsv);
            var itemPool = ConsumableItemCatalog.Definitions.Select(item => item.Id).ToList();
            var inventory = ShopInventoryRoller.Roll(
                System.Array.Empty<CardRewardCandidate>(),
                CardRewardRarityWeights.Default,
                prices,
                System.Array.Empty<string>(),
                new FixedRandom(),
                itemPool);

            Assert.That(inventory.Items, Has.Count.EqualTo(ShopInventoryRoller.ItemSlotCount));
            Assert.That(inventory.Items.Select(stock => stock.ItemId).Distinct().Count(),
                Is.EqualTo(ShopInventoryRoller.ItemSlotCount), "같은 아이템이 두 슬롯에 진열되면 안 된다.");
            // 단일가가 아니라 행별 값이다(DEC-2026-08-31-02 Q2) — 진열가 = 등급 기준가 + priceDelta.
            foreach (var stock in inventory.Items)
            {
                Assert.That(ConsumableItemCatalog.TryGet(stock.ItemId, out var definition), Is.True);
                Assert.That(prices.TryGetPrice(ShopItemKind.Item, definition.Rarity, out var tier), Is.True);
                Assert.That(stock.Price, Is.EqualTo(tier + definition.PriceDelta),
                    $"{stock.ItemId} 진열가는 기준가+변주여야 한다.");
            }

            Assert.That(inventory.Items.Select(stock => stock.Price).Distinct().Count(), Is.GreaterThan(0));
        }

        private sealed class FixedRandom : IRewardRandom
        {
            public int Next(int maxExclusive) => 0;
        }

        [Test]
        public void AddBagItemGatesUnknownIdsAndFullBag()
        {
            var state = CreateState();

            Assert.That(state.TryAddBagItem("item-not-authored"), Is.False, "미등록 id는 관문에서 거부된다.");
            Assert.That(state.TryAddBagItem("item-vitality-flask"), Is.True);
            Assert.That(state.TryAddBagItem("item-bulwark-flask"), Is.True);
            Assert.That(state.TryAddBagItem("item-light-bead"), Is.True);
            Assert.That(state.TryAddBagItem("item-vigor-flask"), Is.False, "기본 슬롯 3 초과는 거부 — 뽑기 돈 폴백 판정이 이 반환값을 쓴다.");
        }

        // ------------------------------------------------------------------ helpers

        private static CombatState CreateState(int enemyDistance = 2, int enemyChaseRange = 0, int playerVisionRange = 7)
        {
            var config = TestCombatConfigs.Standard(
                actionBudget: 4, movementHandSize: 1, actionHandSize: 3,
                enemyChaseRange: enemyChaseRange, playerVisionRange: playerVisionRange);
            return new CombatState(
                CombatState.CreateDemoMap(3),
                new HexCoord(0, 0),
                new HexCoord(enemyDistance, 0),
                config);
        }

        private static void RunFullTurn(CombatState state)
        {
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerMovement));
        }

        private static void ApplyDurationStatus(CombatState state, StatusEffectKind kind, string unitId, int remainingTurns, int amount)
        {
            typeof(CombatState)
                .GetMethod(
                    "AddDurationStatusEffect",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null,
                    types: new[] { typeof(StatusEffectKind), typeof(string), typeof(int), typeof(int), typeof(string) },
                    modifiers: null)
                .Invoke(state, new object[] { kind, unitId, remainingTurns, amount, "test" });
        }

        private static IReadOnlyList<ActiveEffect> ActiveEffects(CombatState state)
        {
            return ActiveEffectProbe.Registry(state).All;
        }

        private static bool HasPlayerEffect(CombatState state, StatusEffectKind kind)
        {
            return ActiveEffects(state).Any(effect => effect.Kind == kind && effect.TargetUnitId == "player" && !effect.IsExpired);
        }

        private static bool HasMonsterEffect(CombatState state, StatusEffectKind kind)
        {
            return ActiveEffects(state).Any(effect => effect.Kind == kind && effect.TargetUnitId != "player" && !effect.IsExpired);
        }
    }
}

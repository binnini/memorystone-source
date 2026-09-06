using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Runtime.Cards;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class PlayerInventoryStateTests
    {
        [Test]
        public void RelicAndCurseInventoryAddsPermanentItemsButRejectsRemoval()
        {
            var inventory = new PlayerRelicCurseInventory(slotLimit: 2);

            Assert.That(inventory.TryAdd(new PlayerPermanentItemState("relic-placeholder", PlayerPermanentItemKind.Relic, "Relic Placeholder"), out var relicReason), Is.True, relicReason);
            Assert.That(inventory.TryAdd(new PlayerPermanentItemState("curse-placeholder", PlayerPermanentItemKind.Curse, "Curse Placeholder"), out var curseReason), Is.True, curseReason);
            Assert.That(inventory.RelicCount, Is.EqualTo(1));
            Assert.That(inventory.CurseCount, Is.EqualTo(1));
            Assert.That(inventory.Items[0].CanRemove, Is.False);

            Assert.That(inventory.TryRemove("relic-placeholder", out var removeReason), Is.False);
            Assert.That(removeReason, Does.Contain("cannot be removed"));
            Assert.That(inventory.TryAdd(new PlayerPermanentItemState("third-placeholder", PlayerPermanentItemKind.Relic), out var limitReason), Is.False);
            Assert.That(limitReason, Does.Contain("slot limit"));
        }

        [Test]
        public void RelicAndCurseInventoryAddsCatalogDefinitionsAndSumsGameplayEffects()
        {
            var inventory = new PlayerRelicCurseInventory();

            Assert.That(inventory.TryAddDefinition(PlayerPermanentItemCatalog.TigerBadgeRelicId, out var attackReason), Is.True, attackReason);
            Assert.That(inventory.TryAddDefinition(PlayerPermanentItemCatalog.HanriverShoesRelicId, out var moveReason), Is.True, moveReason);
            // T2: 저주 유물은 카탈로그에서 폐기 — 인벤토리의 kind 구분 자체는 세이브 호환용으로 남아
            // 직접 구성한 상태로만 검증한다.
            Assert.That(
                inventory.TryAdd(
                    new PlayerPermanentItemState(
                        "curse-test", PlayerPermanentItemKind.Curse,
                        effectKind: PlayerPermanentItemEffectKind.IncomingDamageDelta, effectAmount: 1),
                    out var curseReason),
                Is.True, curseReason);

            Assert.That(inventory.SumEffect(PlayerPermanentItemEffectKind.AttackDamageBonus), Is.EqualTo(1));
            Assert.That(inventory.SumEffect(PlayerPermanentItemEffectKind.MovementRangeBonus), Is.EqualTo(1));
            Assert.That(inventory.SumEffect(PlayerPermanentItemEffectKind.IncomingDamageDelta), Is.EqualTo(1));
            Assert.That(inventory.Items[0].EffectSummary, Does.Contain("공격 피해 +1"));
            Assert.That(inventory.TryAddDefinition(PlayerPermanentItemCatalog.TigerBadgeRelicId, out var duplicateReason), Is.False);
            Assert.That(duplicateReason, Does.Contain("already acquired"));
        }

        [Test]
        public void CombatStateAppliesRelicDamageBonusWithoutChangingDefaultCombatState()
        {
            var noRelic = new CombatState(CombatState.CreateDemoMap(2), new SeoulPlayup.Map.Runtime.HexCoord(0, 0), new SeoulPlayup.Map.Runtime.HexCoord(1, 0), CombatConfig.Default);
            EnterActionPhase(noRelic);
            Assert.That(noRelic.TryPlayerAttack(new SeoulPlayup.Map.Runtime.HexCoord(1, 0)), Is.True);

            var relics = new PlayerRelicCurseInventory();
            Assert.That(relics.TryAddDefinition(PlayerPermanentItemCatalog.TigerBadgeRelicId, out var reason), Is.True, reason);
            var withRelic = new CombatState(
                CombatState.CreateDemoMap(2),
                new SeoulPlayup.Map.Runtime.HexCoord(0, 0),
                new SeoulPlayup.Map.Runtime.HexCoord(1, 0),
                CombatConfig.Default,
                playerInventory: new PlayerInventoryState(relics));
            EnterActionPhase(withRelic);
            Assert.That(withRelic.TryPlayerAttack(new SeoulPlayup.Map.Runtime.HexCoord(1, 0)), Is.True);

            Assert.That(withRelic.Monsters[0].Hp, Is.EqualTo(noRelic.Monsters[0].Hp - 2),
                "The currently selected approved attack is multi-hit, so the +1 damage relic applies per hit.");
            Assert.That(withRelic.PlayerInventory.RelicsAndCurses.Items[0].DisplayName, Is.EqualTo("호신 삼단봉"));
        }

        // 신규 스칼라 축 6종. 각 축이 대응 계산식에 실제로 닿는지를 상태값으로 확인한다 — enum과
        // FormatEffect만 추가하고 소비 지점을 빠뜨리면 유물이 조용히 아무 일도 하지 않는다.
        [Test]
        public void MaxKiRelicRaisesTheTurnBudgetAndTheStartingKi()
        {
            var baseline = CreateState();
            var boosted = CreateState(PlayerPermanentItemEffectKind.MaxKiBonus, 1);

            Assert.That(boosted.MaxKi, Is.EqualTo(baseline.MaxKi + 1));
            Assert.That(boosted.CurrentKi, Is.EqualTo(baseline.CurrentKi + 1),
                "생성자의 예산 리셋도 Config가 아니라 MaxKi를 읽어야 한다.");
        }

        [Test]
        public void MaxHpRelicRaisesMaxHpAndCurrentHpTogether()
        {
            var baseline = CreateState();
            var boosted = CreateState(PlayerPermanentItemEffectKind.MaxHpBonus, 10);

            Assert.That(boosted.Player.MaxHp, Is.EqualTo(baseline.Player.MaxHp + 10));
            Assert.That(boosted.Player.Hp, Is.EqualTo(baseline.Player.Hp + 10));
        }

        [Test]
        public void MaxHpRelicAcquiredMidCombatAlsoHealsForTheGainedAmount()
        {
            var state = CreateState();
            var maxHpBefore = state.Player.MaxHp;
            state.Player.ApplyDamage(20);
            var hpBefore = state.Player.Hp;

            Assert.That(state.TryGrantPermanentItem(ArmorRelicId, out var reason), Is.True, reason);

            Assert.That(state.Player.MaxHp, Is.EqualTo(maxHpBefore + 10));
            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore + 10),
                "상점이 전장 안에 있으므로 전투 중 획득도 현재 체력을 올려야 한다.");
        }

        [Test]
        public void MaxHpRelicDoesNotReviveADeadPlayer()
        {
            var state = CreateState();
            state.Player.ApplyDamage(state.Player.MaxHp);
            Assert.That(state.Player.IsDead, Is.True);

            Assert.That(state.TryGrantPermanentItem(ArmorRelicId, out _), Is.True);

            Assert.That(state.Player.Hp, Is.EqualTo(0));
            Assert.That(state.Player.IsDead, Is.True);
        }

        [Test]
        public void HandSizeRelicsAreScopedToTheirOwnDeck()
        {
            // 손패 3장은 승인 카탈로그의 행동 덱 장수보다 확실히 작다 — 손패가 덱보다 크면 +1을 줘도
            // 뽑을 카드가 없어 장수가 그대로고, 테스트가 조용히 무의미해진다.
            const int SmallActionHand = 3;
            var baseline = CreateState(actionHandSize: SmallActionHand);
            var movement = CreateState(PlayerPermanentItemEffectKind.MovementHandSizeBonus, 1, actionHandSize: SmallActionHand);
            var action = CreateState(PlayerPermanentItemEffectKind.ActionHandSizeBonus, 1, actionHandSize: SmallActionHand);

            Assert.That(movement.MovementDeck.Hand.Count, Is.EqualTo(baseline.MovementDeck.Hand.Count + 1));
            Assert.That(movement.ActionDeck.Hand.Count, Is.EqualTo(baseline.ActionDeck.Hand.Count),
                "이동 손패 유물이 행동 손패까지 늘리면 축을 나눈 의미가 없다.");

            Assert.That(action.ActionDeck.Hand.Count, Is.EqualTo(baseline.ActionDeck.Hand.Count + 1));
            Assert.That(action.MovementDeck.Hand.Count, Is.EqualTo(baseline.MovementDeck.Hand.Count));
        }

        [Test]
        public void VisionRelicRevealsFartherThanTheBaselineVisionRange()
        {
            // 기준 시야를 1로 좁힌 전용 config를 쓴다 — 기본값(7)은 반지름 3짜리 데모 맵을 통째로
            // 덮어버려서 시야가 늘어도 밝혀지는 칸 수가 그대로다(= 아무것도 증명 못 하는 테스트).
            var baseline = CreateState(visionRange: 1);
            var boosted = CreateState(PlayerPermanentItemEffectKind.VisionRangeBonus, 2, visionRange: 1);

            // 시야는 생성자가 아니라 이동/턴 진행에서 갱신된다. EnterActionPhase의 제자리 이동이
            // RefreshPlayerVision을 태우는 가장 짧은 경로다.
            EnterActionPhase(baseline);
            EnterActionPhase(boosted);

            Assert.That(CountRevealed(boosted), Is.GreaterThan(CountRevealed(baseline)));
        }

        [Test]
        public void BlockGainRelicRaisesTheBlockADefendCardActuallyGrants()
        {
            var baselineBlock = PlayDefendAndReadBlock(CreateState());
            var relicBlock = PlayDefendAndReadBlock(CreateState(PlayerPermanentItemEffectKind.BlockGainBonus, 1));

            Assert.That(baselineBlock, Is.GreaterThan(0), "기준선이 방어막을 못 얻으면 이 테스트는 아무것도 증명하지 못한다.");
            Assert.That(relicBlock, Is.EqualTo(baselineBlock + 1));
        }

        [Test]
        public void BlockGainRelicDoesNotManufactureBlockOutOfNothing()
        {
            // 방어막을 주지 않는 카드(D03 양날의 방패)는 유물이 있어도 방어막을 만들지 않아야 한다.
            var state = CreateState(PlayerPermanentItemEffectKind.BlockGainBonus, 1);
            EnterActionPhase(state);

            Assert.That(state.TryPlayerDefend(CardIds.DoubleEdgedShield), Is.True);
            Assert.That(state.Player.Block, Is.EqualTo(0));
        }

        /// <summary>RC-8 개정(T4-1): 가방은 더 이상 TBD 플레이스홀더가 아니라 슬롯 게이트가 있는 실 인벤토리다.</summary>
        [Test]
        public void BagEnforcesSlotLimitAndConsumesItems()
        {
            var player = new PlayerState();
            var bag = player.Inventory.Bag;

            Assert.That(bag.Stacks, Is.Empty);
            Assert.That(bag.TryAddItem("item-vitality-flask", PlayerBagState.BaseSlotCount), Is.True);
            Assert.That(bag.TryAddItem("item-vitality-flask", PlayerBagState.BaseSlotCount), Is.True,
                "같은 아이템도 슬롯을 하나씩 따로 차지한다(StS 포션 문법).");
            Assert.That(bag.TryAddItem("item-bulwark-flask", PlayerBagState.BaseSlotCount), Is.True);
            Assert.That(bag.TryAddItem("item-light-bead", PlayerBagState.BaseSlotCount), Is.False,
                "기본 슬롯 3이 차면 더 못 넣는다.");
            Assert.That(bag.UsedSlotCount, Is.EqualTo(3));

            Assert.That(bag.TryConsumeItem("item-bulwark-flask"), Is.True);
            Assert.That(bag.TryConsumeItem("item-bulwark-flask"), Is.False, "소비는 1개씩 — 없는 아이템은 실패.");
            Assert.That(bag.UsedSlotCount, Is.EqualTo(2));

            // 세이브 복원 표면은 슬롯 게이트를 지나지 않는다(세이브가 정본).
            player.Inventory.Bag.SetPlaceholderStack("debug-token", 3);
            Assert.That(player.Inventory.Bag.Stacks[0].ItemId, Is.EqualTo("debug-token"));
            Assert.That(player.Inventory.Bag.Stacks[0].Count, Is.EqualTo(3));
        }

        // 출하 CSV에 저작된 최대 체력 유물. TryGrantPermanentItem은 카탈로그를 거치므로 이 경로만은
        // 실제 저작 데이터를 쓴다(나머지 축 테스트는 아래 CreateState가 카탈로그와 무관하게 상태를 넣는다).
        private const string ArmorRelicId = "relic-gwanghwamun-armor";

        /// <summary>
        /// 축 하나만 켠 전투 상태. 효과량을 CSV가 아니라 인벤토리에 직접 넣으므로, 유물 콘텐츠의
        /// 수치가 바뀌어도 축 배선을 검사하는 이 테스트들은 흔들리지 않는다.
        /// </summary>
        private static CombatState CreateState(
            PlayerPermanentItemEffectKind effectKind = PlayerPermanentItemEffectKind.None,
            int amount = 0,
            int visionRange = 7,
            int actionHandSize = 15)
        {
            var inventory = new PlayerRelicCurseInventory();
            if (effectKind != PlayerPermanentItemEffectKind.None)
            {
                Assert.That(
                    inventory.TryAdd(
                        new PlayerPermanentItemState("relic-axis-under-test", PlayerPermanentItemKind.Relic, "축 테스트", string.Empty, effectKind, amount),
                        out var reason),
                    Is.True,
                    reason);
            }

            // 기본 actionHandSize는 넉넉히 잡아 방어 카드가 확실히 손에 있게 한다(승인 카탈로그 테스트의
            // 관례). 손패 크기 자체를 재는 테스트만 덱 장수보다 작은 값을 넘겨 +1이 실제로 드러나게 한다.
            var config = new CombatConfig(80, 30, 2, 1, 4, 4, 6, 1, 5, 4, 1, actionHandSize, visionRange, 4);
            return new CombatState(
                CombatState.CreateDemoMap(3),
                new SeoulPlayup.Map.Runtime.HexCoord(0, 0),
                new SeoulPlayup.Map.Runtime.HexCoord(2, 0),
                config,
                cardCatalog: DemoCardCatalog.Create(config),
                playerInventory: new PlayerInventoryState(inventory));
        }

        private static int CountRevealed(CombatState state)
        {
            return state.VisibilityStates.Count(pair => pair.Value == SeoulPlayup.Map.Runtime.HexCellVisibility.Revealed);
        }

        private static int PlayDefendAndReadBlock(CombatState state)
        {
            EnterActionPhase(state);
            Assert.That(state.TryPlayerDefend(CardIds.OldArmor), Is.True);
            return state.Player.Block;
        }

        private static void EnterActionPhase(CombatState state)
        {
            // DEC-2026-07-03-02: 확정된 턴 계약 — 이동만으로는 페이즈가 유지되고,
            // EndAction()이 MonsterMovement로 전환하며 몬스터 이동 해석 후 PlayerAction에 도달한다.
            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            Assert.That(state.EndAction(), Is.True);
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.MonsterMovement));
            state.ResolveMonsterMovement();
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction));
        }
    }
}



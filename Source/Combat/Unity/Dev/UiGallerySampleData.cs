using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Runtime.Cards;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Unity.Dev
{
    /// <summary>
    /// Synthetic data factory for <see cref="UiGalleryController"/>. Ports the minimal harness
    /// blueprints proven by the EditMode view tests (Assets/Tests/EditMode/Combat/) so gallery
    /// entries can drive shipping views with no combat runtime, no bridge, and no scene lookups.
    /// Pure data — no UnityEngine object creation.
    /// </summary>
    public static class UiGallerySampleData
    {
        /// <summary>
        /// A default demo <see cref="CombatState"/> with one card already discarded, so deck / draw /
        /// discard piles are all non-empty. Mirrors DeckPileListOverlayViewTests.CreateState().
        /// </summary>
        public static CombatState CreateState()
        {
            var state = new CombatState(
                CombatState.CreateDemoMap(2),
                new HexCoord(0, 0),
                new HexCoord(1, 0),
                CombatConfig.Default);

            var action = state.ActionDeck.Hand.FirstOrDefault();
            if (action != null)
            {
                state.ActionDeck.DiscardFromHand(action);
            }

            return state;
        }

        /// <summary>
        /// A combat state with a living boss sitting at <paramref name="targetPhase"/>, for the boss HUD entries.
        /// The phase is reached by actually running monster turns through the rules (metric = TurnCount with
        /// thresholds 0/1/2), not by poking the HUD — so the gallery shows what a real phase looks like and a
        /// regression in the phase track shows up here too.
        /// </summary>
        /// <summary>
        /// 연마 가능 카드가 든 데모 상태(camper-workshop P1 서비스 모달용). 연마 값은 카드 클래스(P4)가 주므로
        /// 출하 id(A01 휘둘러치기 3→5 · D01 낡은 방어구 5→8)로 엔트리를 만들면 갤러리에서 비교 화면을 시연할 수 있다.
        /// </summary>
        public static CombatState CreateRefineServiceState()
        {
            CardCatalogEntry Attack(int amount) =>
                new CardCatalogEntry(
                    CardIds.Sweep, "휘둘러치기", CardCategory.Action, CardEffectType.Attack,
                    1, 1, amount, "living_monster_in_range",
                    status: CardCatalogStatus.Approved);
            CardCatalogEntry Defend(int cost, int amount) =>
                new CardCatalogEntry(
                    CardIds.OldArmor, "낡은 방어구", CardCategory.Action, CardEffectType.Defend,
                    cost, 0, amount, "self",
                    playMode: CardPlayMode.Self, status: CardCatalogStatus.Approved);

            var catalog = new CardCatalogDefinition(
                "service-demo",
                "Service demo catalog",
                new[]
                {
                    new CardCatalogEntry(
                        CardIds.Move2Hex, "1칸 이동", CardCategory.Movement, CardEffectType.Move,
                        1, 1, 1, "reachable_hex", status: CardCatalogStatus.Approved),
                    Attack(3),
                    Defend(1, 5),
                });
            return new CombatState(
                CombatState.CreateDemoMap(2),
                new HexCoord(0, 0),
                new HexCoord(1, 0),
                CombatConfig.Default,
                cardCatalog: catalog);
        }

        public static CombatState CreateBossState(int targetPhase)
        {
            const string bossDefinitionId = "M002";
            const string bossUnitId = "gallery-boss";
            const int bossHp = 240;

            var playerCoord = new HexCoord(0, 0);
            var bossCoord = new HexCoord(2, 0);
            var map = new HexMapData(HexArea.CellsWithin(new HexCoord(1, 0), 3)
                .Select(coord => new HexCellData(coord, "tile", "street", 1, true, false))
                .ToList());

            var monsterCatalog = new MonsterCatalogDefinition(
                "ui-gallery-boss-catalog",
                "UI Gallery Boss Catalog",
                new[]
                {
                    new MonsterCatalogEntry(
                        bossDefinitionId,
                        "불가살",
                        "test-melee",
                        "B001",
                        detectionRange: 6,
                        movePerTurn: 1,
                        hp: bossHp,
                        attackPatterns: new[] { new MonsterAttackPattern("A100", "발톱", 1, 0, 3) })
                });

            const string profiles =
                "bossId,displayName,phaseMetric,mechanicId,mechanicParams,introCinematic,deathCinematic,bgmCueBase,designerNote\n" +
                bossDefinitionId + ",불가살,TurnCount,,,,,music.boss.bulgasal,\n";
            const string phases =
                "bossId,phaseIndex,threshold,strengthBonusPercent,maxHpBonus,patternPhaseMin,visualScale,footprintRadius,auraStatusKind,designerNote\n" +
                bossDefinitionId + ",1,0,0,0,0,1,0,,\n" +
                bossDefinitionId + ",2,1,50,0,1,1.25,1,,\n" +
                bossDefinitionId + ",3,2,120,0,2,1.5,2,,\n";
            var bossCatalog = BossCatalogCsvConverter.Convert(
                new BossCatalogCsvSource(profiles, phases, "ui-gallery-boss", "UI Gallery Boss"));

            var state = new CombatState(
                map,
                playerCoord,
                new[] { new MonsterConfig(bossUnitId, bossCoord, bossHp, definitionId: bossDefinitionId, spawnRole: MonsterSpawnRoles.Boss) },
                new CombatConfig(200, bossHp, 2, 1, 4, 4, 5, 1, 3, playerVisionRange: 6),
                monsterCatalog: monsterCatalog,
                bossCatalog: bossCatalog,
                drawOpeningHands: false);

            // 지표(TurnCount)는 몬스터 행동 결의 시점에 평가되므로 "N턴 = N페이즈"가 아니다.
            // 턴 수를 세는 대신 목표 페이즈에 도달할 때까지 돌린다.
            var clampedTarget = Math.Max(1, targetPhase);
            for (var guard = 0; guard < 20 && !state.IsTerminal; guard++)
            {
                if (state.BossPhases.Count > 0 && state.BossPhases[0].CurrentPhase >= clampedTarget)
                {
                    break;
                }

                state.EndAction();
                state.ResolveMonsterMovement();
                state.EndAction();
                state.ResolveMonsterAction();
            }

            return state;
        }

        /// <summary>
        /// A synthetic hand of <paramref name="moveCount"/> move cards + <paramref name="actionCount"/>
        /// action cards. Mirrors GameplayCardLaneViewTests.CreateHand(). Every card is playable and
        /// carries a unique instance id so the lane treats them as distinct slots.
        /// </summary>
        public static CombatCardSnapshot[] CreateHand(int moveCount, int actionCount)
        {
            var moves = Enumerable.Range(0, Math.Max(0, moveCount))
                .Select(i => new CombatCardSnapshot(
                    $"move-{i}", CombatCardKind.Move, $"이동 {i + 1}", "이동", 1, true, false, "준비됨",
                    instanceId: $"move-instance-{i}"));

            var actions = Enumerable.Range(0, Math.Max(0, actionCount))
                .Select(i => new CombatCardSnapshot(
                    $"action-{i}",
                    i % 2 == 0 ? CombatCardKind.Attack : CombatCardKind.Defend,
                    i % 2 == 0 ? $"공격 {i + 1}" : $"방어 {i + 1}",
                    i % 2 == 0 ? "공격" : "방어",
                    1, true, false, "준비됨",
                    instanceId: $"action-instance-{i}"));

            return moves.Concat(actions).ToArray();
        }

        /// <summary>
        /// 오염(상태 카드)·봉인이 섞인 손패. 카드 UI의 두 표현을 갤러리에서 눈으로 판정하기 위한 것이다:
        /// <list type="bullet">
        /// <item>상태 카드(X01~X03) = <see cref="CombatCardStatusText.StatusCard"/> → 불길한 아우라</item>
        /// <item>봉인(C-16) = <see cref="CombatCardStatusText.Sealed"/> → 빨간 X</item>
        /// </list>
        /// ⚠️두 표현은 <b>스냅샷의 Status 문자열만으로</b> 켜진다(런타임 규칙을 태우지 않는다) — 갤러리가
        /// 전투 상태를 합성하지 않고도 실제와 같은 그림을 낼 수 있는 이유이고, 동시에 이 화면이
        /// "규칙이 맞다"가 아니라 <b>"보이는 모양이 맞다"</b>만 증명한다는 뜻이기도 하다.
        /// </summary>
        /// <param name="statusCardCount">아우라를 켤 상태 카드 장수.</param>
        /// <param name="sealedCount">빨간 X를 켤 봉인 카드 장수(이동 카드부터 잠근다 — 봉인은 O-11에 따라 이동도 잠근다).</param>
        public static CombatCardSnapshot[] CreateAfflictedHand(int statusCardCount, int sealedCount)
        {
            var statusCardNames = new[] { "미세먼지", "깨진 유리", "정전" };
            var cards = new List<CombatCardSnapshot>
            {
                new CombatCardSnapshot("move-0", CombatCardKind.Move, "이동 1", "이동", 1, true, false, "준비됨",
                    instanceId: "move-instance-0"),
                new CombatCardSnapshot("action-0", CombatCardKind.Attack, "공격 1", "공격", 1, true, false, "준비됨",
                    instanceId: "action-instance-0"),
                new CombatCardSnapshot("action-1", CombatCardKind.Defend, "방어 1", "방어", 1, true, false, "준비됨",
                    instanceId: "action-instance-1"),
            };

            // 봉인은 멀쩡한 카드가 이번 턴만 잠긴 것이다 — 이름·설명은 정상 카드 그대로 두고 Status만 바꾼다.
            // 이동 카드부터 잠그는 이유는 O-11(봉인은 이동 카드도 포함)이 화면에서 읽히게 하기 위함.
            for (var i = 0; i < Math.Max(0, sealedCount); i++)
            {
                var isMove = i == 0;
                cards.Add(new CombatCardSnapshot(
                    isMove ? $"sealed-move-{i}" : $"sealed-action-{i}",
                    isMove ? CombatCardKind.Move : CombatCardKind.Attack,
                    isMove ? "이동 2" : "공격 2",
                    isMove ? "이동" : "공격",
                    1, false, false, CombatCardStatusText.Sealed,
                    instanceId: $"sealed-instance-{i}"));
            }

            // 상태 카드는 카드 자체가 오염물이다 — 낼 수 없고, 이름도 정상 카드 계열이 아니다.
            for (var i = 0; i < Math.Max(0, statusCardCount); i++)
            {
                cards.Add(new CombatCardSnapshot(
                    $"X0{(i % 3) + 1}",
                    CombatCardKind.Attack,
                    statusCardNames[i % statusCardNames.Length],
                    "낼 수 없다. 손에서 자리를 차지한다.",
                    0, false, false, CombatCardStatusText.StatusCard,
                    instanceId: $"statuscard-instance-{i}"));
            }

            return cards.ToArray();
        }

        private static readonly (string name, string desc, PlayerPermanentItemEffectKind effect, int amount)[] RelicTemplates =
        {
            ("호랑이 부적", "공격 시 추가 피해를 줍니다.", PlayerPermanentItemEffectKind.AttackDamageBonus, 1),
            ("바람의 신발", "이동 범위가 늘어납니다.", PlayerPermanentItemEffectKind.MovementRangeBonus, 1),
            ("청동 거울", "받는 피해가 줄어듭니다.", PlayerPermanentItemEffectKind.IncomingDamageDelta, -1),
            ("달빛 방울", "공격력이 소폭 오릅니다.", PlayerPermanentItemEffectKind.AttackDamageBonus, 2),
            ("은장도", "적에게 주는 피해가 늘어납니다.", PlayerPermanentItemEffectKind.AttackDamageBonus, 1),
            ("학의 깃털", "이동이 가벼워집니다.", PlayerPermanentItemEffectKind.MovementRangeBonus, 1),
            ("옥가락지", "방어에 유리해집니다.", PlayerPermanentItemEffectKind.IncomingDamageDelta, -1),
            ("범뼈 장식", "타격이 매서워집니다.", PlayerPermanentItemEffectKind.AttackDamageBonus, 1),
        };

        private static readonly (string name, string desc)[] CurseTemplates =
        {
            ("무거운 족쇄", "받는 피해가 늘어납니다."),
            ("깨진 부적", "공격이 약해집니다."),
            ("탁한 안개", "시야가 흐려집니다."),
        };

        /// <summary>
        /// A <see cref="CombatState"/> with a synthetic relic/curse inventory + bag stacks, for driving the
        /// sidebar panels. Mirrors SidebarRuntimeViewTests.CreateState() (relics via
        /// <c>PlayerRelicCurseInventory.TryAdd</c>, bag via <c>PlayerBagState.AddPlaceholderStack</c>).
        /// </summary>
        public static CombatState CreateInventoryState(int relicCount, int curseCount, bool emptyBag)
        {
            var inventory = new PlayerRelicCurseInventory();
            for (var i = 0; i < relicCount; i++)
            {
                var t = RelicTemplates[i % RelicTemplates.Length];
                inventory.TryAdd(new PlayerPermanentItemState(
                    $"relic-{i}", PlayerPermanentItemKind.Relic, t.name, t.desc, t.effect, t.amount), out _);
            }

            for (var i = 0; i < curseCount; i++)
            {
                var t = CurseTemplates[i % CurseTemplates.Length];
                inventory.TryAdd(new PlayerPermanentItemState(
                    $"curse-{i}", PlayerPermanentItemKind.Curse, t.name, t.desc,
                    PlayerPermanentItemEffectKind.IncomingDamageDelta, 1), out _);
            }

            var bag = new PlayerBagState();
            if (!emptyBag)
            {
                bag.AddPlaceholderStack("token-jade", 2);
                bag.AddPlaceholderStack("token-herb", 3);
                bag.AddPlaceholderStack("token-charm", 1);
            }

            return new CombatState(
                CombatState.CreateDemoMap(2),
                new HexCoord(0, 0),
                new HexCoord(1, 0),
                CombatConfig.Default,
                playerInventory: new PlayerInventoryState(inventory, bag));
        }

        /// <summary>
        /// A "choice" card snapshot (Holy Light) whose <see cref="ChoiceCardPanelModel.ForCard"/> yields
        /// heal / attack options — used to drive the 갈림길(choice) overlay standalone.
        /// </summary>
        public static CombatCardSnapshot CreateChoiceCard()
        {
            return new CombatCardSnapshot(
                CardIds.HolyLight,
                CombatCardKind.Attack,
                "성스러운 빛",
                "자신을 회복하거나 적을 공격합니다.",
                4, true, false, "",
                cost: 1,
                playMode: CardPlayMode.Choice,
                instanceId: "choice-holy");
        }

        /// <summary>
        /// A synthetic set of card-reward offers (pure struct, no catalog needed).
        /// Mirrors the shape MapCombatController hands to <see cref="CardRewardPopupView"/>.
        /// </summary>
        public static IReadOnlyList<CardRewardOffer> CreateRewardOffers()
        {
            return new List<CardRewardOffer>
            {
                new CardRewardOffer(
                    "reward-strike", "강타", "공격",
                    "적에게 피해를 6 줍니다.", 1, CardEffectType.Attack, range: 1),
                new CardRewardOffer(
                    "reward-guard", "철벽", "방어",
                    "이번 턴 방어도를 8 얻습니다.", 1, CardEffectType.Defend),
                new CardRewardOffer(
                    "reward-dash", "질주", "이동",
                    "최대 3칸 이동합니다.", 1, CardEffectType.Move, range: 3),
            };
        }
    }
}

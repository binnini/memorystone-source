using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 🔴 <b>테스트가 <see cref="CombatState"/>를 세우는 공용 입구.</b>
    ///
    /// 왜 있나(2026-08-31 T5): 테스트 84파일이 <c>new CombatState(...)</c>를 <b>193번</b> 직접 부르고,
    /// 그중 <c>CreateState</c>라는 <b>같은 이름의 서로 다른 헬퍼가 40개</b>다. 결과가 셋이다.
    /// ① 40가지 생성 방식 = 40번의 어긋날 기회(2026-08-02 U04 호롱불 사고가 이 메커니즘이었다).
    /// ② <c>CombatState</c>의 생성자나 초기 상태를 건드리면 그 파일들이 전부 따라온다 —
    /// 예약 효과 타입화(T6)와 턴 경계 파이프라인(T7)이 그만큼 비싸진다.
    /// ③ 테스트 코드의 상당량이 단언이 아니라 셋업이다.
    ///
    /// ⚠️ <b>전면 치환용이 아니다.</b> 193곳을 한꺼번에 옮기는 것은 이 트랙의 목표가 아니고,
    /// 손으로 쓴 픽스처가 전부 나쁜 것도 아니다 — 규칙을 좁게 보는 단위 테스트에는 픽스처가 맞다.
    /// 나쁜 것은 <b>저작을 옮겨 적은 픽스처</b>다. 그래서 <see cref="Builder.WithShippingCardCatalog"/>로
    /// 출하 카탈로그도 같은 입구에서 받을 수 있게 해 뒀다.
    ///
    /// 설계 원칙: <b>기본값은 조용하고, 그 테스트가 실제로 신경 쓰는 것만 말한다.</b>
    /// <code>
    /// var state = CombatStateFixture.Arena(4).WithEnemyEastAt(3).Build();
    /// var state = CombatStateFixture.Arena(3).WithCardCatalog(catalog).WithoutOpeningHands().Build();
    /// </code>
    ///
    /// 🔑 <b>생성자를 우회하지 않는다.</b> <see cref="Builder.Build"/>는 실제 <c>CombatState</c> 생성자
    /// 두 갈래(단일 적 좌표 / <see cref="MonsterConfig"/> 목록)를 <b>이름 있는 인자로</b> 그대로 부른다.
    /// 그래서 이 픽스처를 거친 상태와 손으로 만든 상태가 다를 수 없고, 생성자 시그니처가 바뀌면
    /// 여기 한 곳만 고치면 된다 — 그것이 이 파일의 존재 이유다.
    /// </summary>
    public static class CombatStateFixture
    {
        /// <summary>
        /// 반경 <paramref name="radius"/>의 원판 아레나(<c>CombatState.CreateDemoMap</c>).
        /// 스위트에서 압도적으로 흔한 형태라 기본 입구로 둔다.
        /// </summary>
        public static Builder Arena(int radius)
        {
            return new Builder(CombatState.CreateDemoMap(radius));
        }

        /// <summary>직선 회랑 (0,0)..(length-1,0) — <see cref="TestMaps.Line"/>.</summary>
        public static Builder Corridor(int length)
        {
            return new Builder(TestMaps.Line(length));
        }

        /// <summary>손으로 만든 맵(함정·오브젝트 참조를 얹은 경우 등).</summary>
        public static Builder OnMap(HexMapData map)
        {
            return new Builder(map);
        }

        public sealed class Builder
        {
            private readonly HexMapData map;

            private HexCoord playerCoord = new HexCoord(0, 0);
            private CombatConfig config = CombatConfig.Default;

            // 적은 두 갈래 중 하나로만 정해진다. enemyCoord가 있으면 단일 적 생성자를 타고
            // (몬스터 id·체력을 CombatState가 config에서 채운다), monsters가 있으면 목록 생성자를 탄다.
            // 둘 다 없으면 몬스터 없는 상태다 — "빈 배열"이 곧 그 뜻이라 별도 플래그를 두지 않는다.
            private HexCoord? enemyCoord;
            private IReadOnlyList<MonsterConfig> monsters;

            private CardCatalogDefinition cardCatalog;
            private MonsterCatalogDefinition monsterCatalog;
            private BossCatalogDefinition bossCatalog;
            private HexTerrainTable terrainTable;
            private HexTerrainTraits terrainTraits;
            private PlayerInventoryState playerInventory;
            private PlayerDeckData playerDeck;
            private CardDeckState movementDeck;
            private CardDeckState actionDeck;
            private bool drawOpeningHands = true;
            private bool shuffleDecks;
            private int? runSeed;

            internal Builder(HexMapData map)
            {
                this.map = map;
            }

            public Builder WithPlayerAt(HexCoord coord)
            {
                playerCoord = coord;
                return this;
            }

            /// <summary>단일 적 하나. <c>CombatState</c>가 id와 체력을 config에서 채운다.</summary>
            public Builder WithEnemyAt(HexCoord coord)
            {
                enemyCoord = coord;
                monsters = null;
                return this;
            }

            /// <summary>
            /// 동쪽 축 위 거리 <paramref name="distance"/>의 단일 적 — 스위트의 적 배치는 거의 전부
            /// <c>new HexCoord(n, 0)</c>이라 그 축약이다.
            /// </summary>
            public Builder WithEnemyEastAt(int distance)
            {
                return WithEnemyAt(new HexCoord(distance, 0));
            }

            /// <summary>id·좌표·체력을 직접 정하는 몬스터 목록. 인자가 없으면 몬스터 없는 상태다.</summary>
            public Builder WithMonsters(params MonsterConfig[] monsterConfigs)
            {
                monsters = monsterConfigs ?? System.Array.Empty<MonsterConfig>();
                enemyCoord = null;
                return this;
            }

            public Builder WithConfig(CombatConfig combatConfig)
            {
                config = combatConfig;
                return this;
            }

            public Builder WithCardCatalog(CardCatalogDefinition catalog)
            {
                cardCatalog = catalog;
                return this;
            }

            /// <summary>
            /// 출하 <c>cards.csv</c>를 런타임과 같은 파싱 경로로 읽어 쓴다.
            /// 🔑 저작을 옮겨 적은 픽스처는 저작과 어긋난 순간 거짓말을 시작하므로, 저작을 봐야 하는
            /// 테스트는 손으로 옮겨 적지 말고 이걸 쓴다.
            /// </summary>
            public Builder WithShippingCardCatalog()
            {
                return WithCardCatalog(ShippingCardCatalogSource.Load());
            }

            public Builder WithMonsterCatalog(MonsterCatalogDefinition catalog)
            {
                monsterCatalog = catalog;
                return this;
            }

            public Builder WithBossCatalog(BossCatalogDefinition catalog)
            {
                bossCatalog = catalog;
                return this;
            }

            public Builder WithTerrain(HexTerrainTable table = null, HexTerrainTraits traits = null)
            {
                terrainTable = table;
                terrainTraits = traits;
                return this;
            }

            public Builder WithPlayerInventory(PlayerInventoryState inventory)
            {
                playerInventory = inventory;
                return this;
            }

            public Builder WithPlayerDeck(PlayerDeckData deck)
            {
                playerDeck = deck;
                return this;
            }

            public Builder WithMovementDeck(CardDeckState deck)
            {
                movementDeck = deck;
                return this;
            }

            public Builder WithActionDeck(CardDeckState deck)
            {
                actionDeck = deck;
                return this;
            }

            /// <summary>이동 손패를 이 카드들로 고정한다(나머지 더미는 비운다).</summary>
            public Builder WithMovementHand(params CardDefinition[] cards)
            {
                return WithMovementDeck(HandOnly(cards));
            }

            /// <summary>행동 손패를 이 카드들로 고정한다(나머지 더미는 비운다).</summary>
            public Builder WithActionHand(params CardDefinition[] cards)
            {
                return WithActionDeck(HandOnly(cards));
            }

            /// <summary>
            /// 시작 손패를 뽑지 않는다. 손패 내용을 테스트가 직접 정할 때 필요하다 —
            /// 뽑아 두면 무엇이 손에 있는지가 덱 셔플에 좌우된다.
            /// </summary>
            public Builder WithoutOpeningHands()
            {
                drawOpeningHands = false;
                return this;
            }

            public Builder WithShuffledDecks()
            {
                shuffleDecks = true;
                return this;
            }

            /// <summary>런 시드(seed-determinism-handoff). 안 주면 무시드 — 기존 테스트의 전제 그대로.</summary>
            public Builder WithRunSeed(int seed)
            {
                runSeed = seed;
                return this;
            }

            /// <summary>
            /// 🔑 실제 <c>CombatState</c> 생성자를 <b>이름 있는 인자로</b> 부른다. 우회 경로가 없으므로
            /// 픽스처로 만든 상태와 손으로 만든 상태가 갈릴 수 없다.
            /// </summary>
            public CombatState Build()
            {
                if (enemyCoord.HasValue)
                {
                    return new CombatState(
                        map,
                        playerCoord,
                        enemyCoord.Value,
                        config,
                        terrainTable: terrainTable,
                        cardCatalog: cardCatalog,
                        monsterCatalog: monsterCatalog,
                        terrainTraits: terrainTraits,
                        playerInventory: playerInventory,
                        playerDeck: playerDeck,
                        movementDeck: movementDeck,
                        actionDeck: actionDeck,
                        drawOpeningHands: drawOpeningHands,
                        shuffleDecks: shuffleDecks,
                        bossCatalog: bossCatalog,
                        runSeed: runSeed);
                }

                return new CombatState(
                    map,
                    playerCoord,
                    monsters ?? System.Array.Empty<MonsterConfig>(),
                    config,
                    terrainTable: terrainTable,
                    cardCatalog: cardCatalog,
                    monsterCatalog: monsterCatalog,
                    terrainTraits: terrainTraits,
                    playerInventory: playerInventory,
                    playerDeck: playerDeck,
                    movementDeck: movementDeck,
                    actionDeck: actionDeck,
                    drawOpeningHands: drawOpeningHands,
                    shuffleDecks: shuffleDecks,
                    bossCatalog: bossCatalog,
                    runSeed: runSeed);
            }

            private static CardDeckState HandOnly(IEnumerable<CardDefinition> cards)
            {
                return new CardDeckState(
                    drawPile: null,
                    hand: cards?.ToArray(),
                    discardPile: null,
                    removedPile: null);
            }
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 패턴 확인 랩(계획 §25)의 런타임 계약.
    ///
    /// <para>🔑 고정할 값어치가 있는 것은 <b>핀이 계획 리프레시에 살아남는가</b> 하나다. 계획은 매 턴
    /// 다시 서고 패턴 선택도 그때 다시 이뤄지므로, 강제 커밋이 <see cref="MonsterAiPlanner"/>의 선택
    /// <b>앞</b>에서 처리되지 않으면 다음 리프레시가 조용히 덮는다 — 그러면 랩 버튼이 "눌리는데 아무
    /// 일도 안 일어나는" 상태가 되고, 그건 화면만 봐서는 저작 문제와 구분되지 않는다.</para>
    ///
    /// <para>강제 커밋은 선택기의 <b>우회</b>라 "실게임에서 이 패턴이 뽑히는가"는 답하지 않는다.
    /// 그 물음은 「조건 만들기」가 답하고, 그쪽은 술어를 복제하지 않으므로(§18.4) 별도 시험을 두지 않는다.</para>
    /// </summary>
    public sealed class MonsterPatternLabTests
    {
        private const string MonsterId = "mon-01";
        private const string DefinitionId = "M001";

        private static readonly HexCoord PlayerStart = new HexCoord(0, 0);
        private static readonly HexCoord MonsterStart = new HexCoord(1, 0);

        [Test]
        public void ForcedPatternSurvivesPlanRefresh()
        {
            var state = CreateState();
            Assert.That(state.DebugForceAttackPattern(MonsterId, 1, out _), Is.True);
            Assert.That(SelectedPatternId(state), Is.EqualTo("P002"), "강제 직후 커밋되어야 한다(거리 창 밖이어도 — 우회이므로).");

            // 계획이 다시 서는 지점을 여러 번 통과시킨다 — 핀이 선택 앞에 있지 않으면 여기서 덮인다.
            for (var turn = 0; turn < 4; turn++)
            {
                state.EndAction();
                state.ResolveMonsterMovement();
                state.EndAction();
                state.ResolveMonsterAction();
                Assert.That(SelectedPatternId(state), Is.EqualTo("P002"), $"{turn + 1}턴 뒤에도 핀이 유지되어야 한다.");
            }
        }

        [Test]
        public void ClearingTheForcedPatternHandsSelectionBackToTheSelector()
        {
            // 양성 대조: 위 시험이 "이 몬스터는 원래 P002만 쓴다"를 보고 통과하는 게 아님을 고정한다.
            // 픽스처는 플레이어를 인접(거리 1)에 두고 P002에 distMin 2를 줬으므로 P002는 거리 창 밖이다
            // → 해제하면 선택기가 고를 수 있는 것은 P001뿐이다(가중치 추첨에 기대지 않는 결정적 대조).
            var state = CreateState();
            Assert.That(state.DebugForceAttackPattern(MonsterId, 1, out _), Is.True);
            Assert.That(SelectedPatternId(state), Is.EqualTo("P002"));

            Assert.That(state.DebugClearForcedAttackPattern(MonsterId, out _), Is.True);

            Assert.That(SelectedPatternId(state), Is.EqualTo("P001"), "해제하면 선택기가 다시 고른다.");
        }

        [Test]
        public void GateDescriptionCoversEveryAuthoredPattern()
        {
            // 랩 목록과 저작이 어긋나면 "없는 패턴을 찾는" 시간을 쓰게 된다 — 개수만이라도 고정한다.
            var state = CreateState();
            var lines = state.DebugDescribeAttackPatternGates(MonsterId);

            Assert.That(lines, Has.Count.EqualTo(2));
            Assert.That(lines[0], Does.Contain("P001"));
            Assert.That(lines[1], Does.Contain("P002"));
            Assert.That(string.Join("\n", lines), Does.Contain("★"), "선택된 패턴은 표식이 있어야 한다.");
        }

        [Test]
        public void ShapePreviewComesFromTheRuntimeShapeLibrary()
        {
            // §25 미리보기 핀: 형상 계산을 랩에 복제하지 않는다 — 상태 API가 GetAffectedCells 결과를
            // 그대로 돌려주는지 형상 하나(cone-mid = 링 6 + 부채꼴 3 = 9칸)로 고정한다.
            var state = CreateState();

            Assert.That(state.DebugTryGetAttackPatternShapePreview(MonsterId, 0, out _), Is.False,
                "shape 없는 패턴(P001)은 그릴 것이 없다.");

            Assert.That(state.DebugTryGetAttackPatternShapePreview(MonsterId, 1, out var preview), Is.True);
            Assert.That(preview.ShapeId, Is.EqualTo(AttackShapeLibrary.ConeMid));
            Assert.That(preview.AffectedCells, Has.Count.EqualTo(9), "cone-mid = 인접 링 6 + 전방 부채꼴 3.");
            Assert.That(preview.AffectedCells, Does.Contain(preview.Origin.Neighbor(preview.AttackDirection)),
                "인접 링은 항상 조준 방향 앞 칸을 덮는다.");
            Assert.That(preview.AttackDirection, Is.EqualTo(preview.Origin.ApproximateDirection(state.PlayerCoord)),
                "방향은 실제 조준(원점→플레이어)이어야 한다.");
        }

        [Test]
        public void PropMonstersAreExcludedFromTheLabTargets()
        {
            // 철조각은 공격 패턴이 자리표시(A900)라 목록에 뜨면 혼란만 준다.
            var state = CreateState();
            var targets = state.DebugListPatternLabMonsters();

            Assert.That(targets.Select(monster => monster.Id), Is.EqualTo(new[] { MonsterId }));
        }

        // -----------------------------------------------------------------------------------------
        // 픽스처
        // -----------------------------------------------------------------------------------------

        private static string SelectedPatternId(CombatState state) =>
            state.Monsters.Single(monster => monster.Id == MonsterId).SelectedAttackPatternId;

        private static CombatState CreateState()
        {
            var move = new CardDefinition(
                "lab-move", "Lab Move", CardCategory.Movement, CardEffectType.Move, 0, 3, 0, targeting: "walkable_in_range",
                status: CardCatalogStatus.Approved, instanceId: "lab-move-instance");
            // 행동 덱 카드가 최소 하나 있어야 CombatState가 생성된다(카탈로그 가드).
            var strike = new CardDefinition(
                "lab-strike", "Lab Strike", CardCategory.Action, CardEffectType.Attack, 1, 1, 0, targeting: "living_monster_in_range",
                status: CardCatalogStatus.Approved, instanceId: "lab-strike-instance");
            var catalog = new CardCatalogDefinition(
                "test.pattern-lab", "Pattern lab test catalog",
                new[]
                {
                    new CardCatalogEntry(move.Id, move.DisplayName, move.Category, move.EffectType, move.Cost, move.Range, move.Amount, move.Targeting, status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(strike.Id, strike.DisplayName, strike.Category, strike.EffectType, strike.Cost, strike.Range, strike.Amount, strike.Targeting, status: CardCatalogStatus.Approved, targetMode: CardTargetMode.Enemy)
                });

            var cells = HexArea.CellsWithin(new HexCoord(0, 0), 6)
                .Select(coord => new HexCellData(coord, "tile", "street", 1, true, false))
                .ToList();

            var monsters = new List<MonsterConfig>
            {
                new MonsterConfig(MonsterId, MonsterStart, 200, definitionId: DefinitionId)
            };

            var hand = Enumerable.Range(0, 8).Select(_ => move).ToArray();
            return new CombatState(
                new HexMapData(cells),
                PlayerStart,
                monsters,
                new CombatConfig(2000, 200, 2, 1, 0, 8, 12, 1, 0, playerVisionRange: 12),
                cardCatalog: catalog,
                monsterCatalog: CreateMonsterCatalog(),
                movementDeck: new CardDeckState(null, hand, null, null),
                actionDeck: new CardDeckState(null, Enumerable.Range(0, 8).Select(_ => strike).ToArray(), null, null),
                drawOpeningHands: false);
        }

        /// <summary>
        /// 패턴 둘: 기본기(P001 · 인접 전용)와 광역(P002 · <b>distMin 2</b>). 플레이어가 인접에 서 있는
        /// 픽스처라 P002는 <b>거리 창 밖</b>이다 — 그래서 강제 커밋 시험이 "게이트를 우회한다"까지 함께
        /// 고정하고, 해제 대조는 가중치 추첨에 기대지 않고 결정적이 된다.
        /// 피해 0 — 관찰 대상은 <b>어느 패턴이 커밋됐는가</b>뿐이라 플레이어가 죽으면 안 된다.
        /// </summary>
        private static MonsterCatalogDefinition CreateMonsterCatalog()
        {
            return new MonsterCatalogDefinition(
                "pattern-lab-test-catalog",
                "Pattern Lab Test Catalog",
                new[]
                {
                    new MonsterCatalogEntry(
                        DefinitionId,
                        "테스트 몬스터",
                        "test-melee",
                        "B001",
                        detectionRange: 12,
                        movePerTurn: 1,
                        hp: 200,
                        attackSpeed: 1,
                        attackPatterns: new[]
                        {
                            new MonsterAttackPattern("P001", "기본기", 1, 0, 0, cooldownTurns: 0, phaseMin: 0),
                            new MonsterAttackPattern("P002", "광역", 2, 1, 0, shapeId: AttackShapeLibrary.ConeMid, cooldownTurns: 0, phaseMin: 0, distMin: 2)
                        })
                });
        }
    }
}

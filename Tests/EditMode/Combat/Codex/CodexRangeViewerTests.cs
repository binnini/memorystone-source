using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Codex;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using UnityEditor;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// P1 범위 뷰어의 <b>진짜 완료 조건</b>: 도감이 그리는 칸 집합 == 집행이 쓰는 칸 집합
    /// (<c>docs/codex-plan.md</c> §6.2).
    /// <para>
    /// 이 파일이 없으면 도감은 언제든 조용히 갈라진다. 그래서 비교 상대를 <b>도감이 부르지 않은
    /// 다른 집행 경로</b>로 잡는다 — 같은 함수를 두 번 부르는 시험은 무엇도 지키지 못한다:
    /// </para>
    /// <list type="bullet">
    ///   <item>몬스터 형상 → 살아 있는 <b>예고 파이프라인</b>(<see cref="MonsterIntentPreview.AttackRangeCoords"/>)</item>
    ///   <item>카드 착탄 → 실제로 카드를 <b>내서</b> 규칙이 때린 칸(<see cref="CombatState.EffectResolved"/>)</item>
    ///   <item>카드 사거리 → 그 칸에서 카드가 <b>실제로 나가는가</b></item>
    /// </list>
    /// </summary>
    public sealed class CodexRangeViewerTests
    {
        private static readonly HexDirection[] AllDirections =
        {
            HexDirection.East, HexDirection.NorthEast, HexDirection.NorthWest,
            HexDirection.West, HexDirection.SouthWest, HexDirection.SouthEast,
        };

        private static CardCatalogDefinition shippingCards;
        private static CodexRangeArena sharedArena;

        [OneTimeSetUp]
        public void LoadShippingData()
        {
            // 🔴 이걸 안 부르면 GetAffectedCells가 예외가 아니라 빈 집합을 돌려준다 — 시험이
            // "칸이 0개인 것끼리 같다"로 통과해 버린다. 아래 ShapeLibraryIsActuallyLoaded가 그 방지선이다.
            AttackShapeLibrary.Initialize(
                AttackShapeCatalogCsv.ConvertText(File.ReadAllText(CombatCsvPaths.AttackShapesCsv, Encoding.UTF8)));

            var asset = AssetDatabase.LoadAssetAtPath<CardCatalogAsset>(TestAssetPaths.CardCatalogAsset);
            Assert.That(asset, Is.Not.Null, "출하 카드 카탈로그를 찾지 못했다.");
            shippingCards = asset.ToCardCatalogDefinition(CombatConfig.Default);
            sharedArena = CodexRangeArena.Create(shippingCards);
        }

        private static IEnumerable<CardCatalogEntry> ApprovedCards =>
            shippingCards.Entries.Where(entry => entry != null && entry.Status == CardCatalogStatus.Approved);

        // ── 사전 조건 ──────────────────────────────────────────────────────

        [Test]
        [Category("ShippingData")]
        public void ShapeLibraryIsActuallyLoaded()
        {
            // 빈 카탈로그 위에서는 아래 전수 시험이 전부 "0 == 0"으로 통과한다 — 이 테스트가 막는 건
            // 그것이다. 🔑 그러므로 필요한 것은 "오늘 30개"가 아니라 **"저작 전부가 실렸다"**이고,
            // 상수를 두면 형상을 추가할 때마다 여기가 거짓 경보로 깨진다(2026-08-31 T4).
            Assert.That(AttackShapeLibrary.AllShapes, Is.Not.Empty, "형상 라이브러리가 비었다 — 전수 시험이 0 == 0으로 통과한다.");
            Assert.That(
                AttackShapeLibrary.AllShapes,
                Has.Count.EqualTo(AuthoredCsvRows.Count(CombatCsvPaths.AttackShapesCsv)),
                "라이브러리에 실린 형상 수가 저작과 다르다 — 일부만 실렸다면 전수 시험이 그만큼 눈을 감는다.");
        }

        [Test]
        [Category("ShippingData")]
        public void EveryApprovedCardIsCoveredByTheSuite()
        {
            Assert.That(ApprovedCards.Count(), Is.EqualTo(59), "카드 수가 바뀌었다면 전수 시험의 뜻도 바뀐다.");
        }

        // ── 몬스터 형상: 도해 == 집행 ─────────────────────────────────────

        [Test]
        [Category("ShippingData")]
        public void MonsterShapeDiagramNeverTransformsTheShapeLibrary()
        {
            // 형상 전종 × 방향 6 × 몸 반경 0~2 전수 — 도감이 회전이나 몸 반경 보정을 스스로 한 벌
            // 더 하고 있지 않은지 못 박는다(그 계산은 GetAffectedCells 안에 있고 밖에서 다시 하면 안 된다).
            var checkedCases = 0;
            foreach (var shape in AttackShapeLibrary.AllShapes)
            {
                foreach (var direction in AllDirections)
                {
                    for (var body = 0; body <= CodexMonsterPatternRange.MaxFootprintRadius; body++)
                    {
                        var diagram = CodexMonsterPatternRange.Resolve(shape.Id, direction, body);
                        var execution = AttackShapeLibrary
                            .GetAffectedCells(shape.Id, CodexRangeArena.Origin, direction, body)
                            .ToList();

                        Assert.That(execution, Is.Not.Empty, $"{shape.Id}/{direction}/r{body}: 전제가 깨졌다.");
                        Assert.That(
                            diagram.ShapeCells, Is.EquivalentTo(execution),
                            $"{shape.Id} · {direction} · 몸반경 {body}: 도해 칸이 집행 칸과 다르다.");
                        checkedCases++;
                    }
                }
            }

            Assert.That(checkedCases, Is.EqualTo(AttackShapeLibrary.AllShapes.Count * 6 * 3),
                "전수여야 한다 — 형상 × 방향 6 × 몸반경 3.");
        }

        [Test]
        [Category("ShippingData")]
        public void MonsterShapeDiagramMatchesTheLiveIntentPreview()
        {
            // 여기가 진짜 대조다. 도감은 AttackShapeLibrary를 부르고, 이 비교 상대는 몬스터가 실제로
            // 예고한 칸 — 플래너·집행·오버레이가 공유하는 그 경로를 통째로 지난 결과다.
            foreach (var shape in AttackShapeLibrary.AllShapes)
            {
                var state = CreateShapeIntentState(shape.Id, footprintRadius: 0);
                var preview = state.GetMonsterIntentPreviews(includeUnrevealed: true)
                    .Single(candidate => candidate.MonsterId == ShapeMonsterUnitId);

                Assert.That(preview.AttackRangeCoords, Is.Not.Empty, $"{shape.Id}: 예고가 비어 있으면 대조가 무의미하다.");

                // ⚠️ 앵커는 몬스터의 <b>지금</b> 칸이 아니라 예고가 쓰는 <b>이동 후</b> 칸이다 —
                // 예고는 "옮겨 간 자리에서 이렇게 친다"이므로 여기를 틀리면 한 칸씩 어긋난다.
                var anchor = preview.PredictedMoveCoord;

                // 예고는 맵에 실재하는 칸만 남긴다 — 도해도 같은 맵 경계로 자른 뒤 비교한다.
                var diagram = CodexMonsterPatternRange.Resolve(shape.Id, HexDirection.East, 0);
                var expected = diagram.ShapeCells
                    .Select(coord => new HexCoord(coord.Q + anchor.Q, coord.R + anchor.R))
                    .Where(coord => state.Map.TryGetCell(coord, out _))
                    .ToList();

                Assert.That(
                    preview.AttackRangeCoords, Is.EquivalentTo(expected),
                    $"{shape.Id}: 도감이 그리는 칸과 몬스터가 예고하는 칸이 다르다.");
            }
        }

        [Test]
        [Category("ShippingData")]
        public void MonsterShapeDiagramMatchesTheLiveIntentPreviewForAMultiCellBody()
        {
            // 몸 반경 보정(원점을 몸통 가장자리로 옮긴다)이 예고에서도 같게 나오는지 — 도해만 옮기고
            // 집행은 안 옮기면 멀티셀 보스의 도달 거리가 몸 반경만큼 틀어진다.
            const string ShapeId = AttackShapeLibrary.Line2;
            var state = CreateShapeIntentState(ShapeId, footprintRadius: 1);
            var preview = state.GetMonsterIntentPreviews(includeUnrevealed: true)
                .Single(candidate => candidate.MonsterId == ShapeMonsterUnitId);
            var anchor = preview.PredictedMoveCoord;

            var diagram = CodexMonsterPatternRange.Resolve(ShapeId, HexDirection.East, 1);
            var expected = diagram.ShapeCells
                .Select(coord => new HexCoord(coord.Q + anchor.Q, coord.R + anchor.R))
                .Where(coord => state.Map.TryGetCell(coord, out _))
                .ToList();

            Assert.That(preview.AttackRangeCoords, Is.EquivalentTo(expected));
        }

        [Test]
        public void MonsterShapeDiagramDrawsTheBodyItIsAnchoredOn()
        {
            var diagram = CodexMonsterPatternRange.Resolve(AttackShapeLibrary.Single, HexDirection.East, 2);
            // 개수 고정 유지: 육각 기하다(1 + 6 + 12 = 19). 저작이 늘어도 변하지 않는다.
            Assert.That(diagram.BodyCells, Has.Count.EqualTo(19), "반경 2 몸통은 19칸이다.");
            Assert.That(diagram.BodyCells, Does.Contain(CodexRangeArena.Origin));
        }

        // ── 카드: 도해 == 집행 ────────────────────────────────────────────

        [Test]
        [Category("ShippingData")]
        public void EveryCardDiagramCellIsExactlyWhatTheShippingValidatorAccepts()
        {
            // 카드 59장 전량. 도감이 낸 칸 집합을, 같은 벌판을 새로 세워 출하 검증기로 다시 훑은 집합과
            // 맞댄다 — 도감이 답을 받아 놓고 뒤에서 걸러 내거나 덧붙이지 않았음을 못 박는다.
            var fresh = CodexRangeArena.Create(shippingCards);
            var checkedCards = 0;
            var withCells = 0;

            foreach (var entry in ApprovedCards)
            {
                var diagram = CodexCardRange.Resolve(sharedArena, entry);
                var expected = SweepWithShippingValidator(fresh, entry);

                Assert.That(
                    diagram.RangeCells, Is.EquivalentTo(expected),
                    $"{entry.Id}({entry.DisplayName}): 도해 사거리가 검증기와 다르다.");
                checkedCards++;
                if (diagram.RangeCells.Count > 0)
                {
                    withCells++;
                }
            }

            Assert.That(checkedCards, Is.EqualTo(59));
            // 전부 빈 집합이면 위 비교는 아무것도 지키지 않는다.
            Assert.That(withCells, Is.GreaterThanOrEqualTo(30), "겨냥 칸을 가진 카드가 너무 적다 — 아레나가 죽었다.");
        }

        [Test]
        [Category("ShippingData")]
        public void EveryTargetedCardIsActuallyPlayableExactlyWhereTheDiagramSaysItIs()
        {
            // 사거리 도해의 뜻은 "여기에 낼 수 있다"이다. 그 뜻대로인지 <b>실제로 내 보고</b> 확인한다:
            // 도해 안의 칸에서는 나가고, 벌판 안이지만 도해 밖인 칸에서는 나가지 않아야 한다.
            var checkedCards = 0;

            foreach (var entry in ApprovedCards)
            {
                var diagram = CodexCardRange.Resolve(sharedArena, entry);
                if (diagram.RangeCells.Count == 0 || !CanPlayAtTile(entry))
                {
                    continue;
                }

                var inside = diagram.RangeCells[diagram.RangeCells.Count - 1];
                Assert.That(
                    PlayAt(entry, inside), Is.True,
                    $"{entry.Id}: 도해가 겨냥 가능하다고 그린 칸 {inside}에서 카드가 나가지 않는다.");

                var outside = sharedArena.Cells.FirstOrDefault(coord => !diagram.RangeCells.Contains(coord));
                if (outside != default || !diagram.RangeCells.Contains(default))
                {
                    Assert.That(
                        PlayAt(entry, outside), Is.False,
                        $"{entry.Id}: 도해 밖 칸 {outside}에서 카드가 나갔다 — 도해가 사거리를 좁게 그리고 있다.");
                }

                checkedCards++;
            }

            Assert.That(checkedCards, Is.GreaterThanOrEqualTo(20), "표적 카드가 너무 적게 검사됐다.");
        }

        [Test]
        [Category("ShippingData")]
        public void EveryAreaCardFootprintMatchesTheTilesTheRulesActuallyHit()
        {
            // 착탄 형상의 뜻은 "이 칸들이 맞는다"이다. 카드를 실제로 내고, 규칙이 올린
            // EffectResolved의 발자국과 도해를 맞댄다.
            var checkedCards = 0;

            foreach (var entry in ApprovedCards)
            {
                var diagram = CodexCardRange.Resolve(sharedArena, entry);
                // 반경 0짜리는 "겨눈 그 칸"이 곧 착탄이라 사거리 시험이 이미 덮는다. 게다가 정찰 계열은
                // 겨냥과 무관하게 시야를 여는 이벤트를 따로 올리므로(scout 칸이 빈 정찰 카드는 기본
                // 반경 2로 넓게 밝힌다), 반경으로 뜻이 정해지는 카드만 여기서 대조한다.
                if (entry.AreaRadius <= 0
                    || diagram.ShapeCells.Count == 0
                    || !diagram.ShapeCenter.HasValue
                    || !CanPlayAtTile(entry))
                {
                    continue;
                }

                var arena = CodexRangeArena.Create(shippingCards);
                var state = StateFor(arena, entry);
                var footprints = new List<HexCoord>();
                var buffer = new List<HexCoord>();
                void Capture(EffectResultEvent resolved)
                {
                    if (!resolved.Center.HasValue || resolved.Center.Value != diagram.ShapeCenter.Value)
                    {
                        return;
                    }

                    EffectAreaFootprint.Resolve(resolved, arena.Map, buffer);
                    footprints.AddRange(buffer);
                }

                state.EffectResolved += Capture;
                var played = Play(state, entry, diagram.ShapeCenter.Value);
                state.EffectResolved -= Capture;

                if (!played || footprints.Count == 0)
                {
                    // 발자국을 올리지 않는 카드(즉발 수치만 바꾸는 것들)는 대조할 것이 없다.
                    continue;
                }

                Assert.That(
                    footprints.Distinct(), Is.EquivalentTo(diagram.ShapeCells),
                    $"{entry.Id}({entry.DisplayName}): 도해가 칠한 칸과 규칙이 때린 칸이 다르다.");
                checkedCards++;
            }

            Assert.That(checkedCards, Is.GreaterThanOrEqualTo(5), "착탄 대조가 실제로 돌지 않았다.");
        }

        // ── 아레나가 거짓말하지 않는지 (실측으로 밟은 함정들) ──────────────

        [Test]
        [Category("ShippingData")]
        public void ArenaNeverPutsCursesInHandBecauseTheyRewriteTheRulesFromThere()
        {
            // X07 지각은 손에 있는 것만으로 이동 사거리를 1 깎고, X03·X11은 다른 카드를 봉인한다.
            // 아레나가 저주를 손에 올리면 모든 이동 카드가 한 칸씩 짧게, 봉인된 카드는 "범위 없음"으로 나온다.
            var move1 = ApprovedCards.Single(entry => entry.Id == "M01");
            var diagram = CodexCardRange.Resolve(sharedArena, move1);
            Assert.That(
                diagram.RangeCells, Has.Count.EqualTo(6),
                "1칸 이동의 도달 칸은 인접 6칸이다 — 줄었다면 저주가 아레나 손패에 들어갔다.");

            foreach (var curse in ApprovedCards.Where(entry => entry.ActionType == CardEffectType.Status))
            {
                Assert.That(
                    CodexCardRange.Resolve(sharedArena, curse).IsEmpty, Is.True,
                    $"{curse.Id}: 저주는 사용 불가라 겨냥할 칸이 없어야 한다.");
            }
        }

        [Test]
        [Category("ShippingData")]
        public void ArenaKeepsShippingKiSoSpendAllCardsDoNotOverstateTheirReach()
        {
            // 「전력 질주」(M08)는 기를 전부 소모하고 그만큼 간다. 아레나 기력을 부풀리면 도감이
            // "벌판 끝까지 간다"고 말한다.
            var sprint = ApprovedCards.Single(entry => entry.Id == "M08");
            var diagram = CodexCardRange.Resolve(sharedArena, sprint);
            var reach = diagram.RangeCells.Max(coord => CodexRangeArena.Origin.DistanceTo(coord));
            Assert.That(reach, Is.EqualTo(CombatConfig.Default.ActionBudget));
        }

        [Test]
        [Category("ShippingData")]
        public void ArenaIsWideEnoughThatNoFootprintIsClippedByItsOwnEdge()
        {
            // 벌판이 좁으면 사거리 끝에서 터지는 폭발이 벌판 밖으로 잘리고, 도감은 그 잘린 모습을
            // 카드의 진짜 형상인 양 그린다(반경 5에서 S01의 착탄이 19칸 대신 14칸으로 나왔다).
            foreach (var entry in ApprovedCards.Where(card => card.AreaRadius > 0))
            {
                var diagram = CodexCardRange.Resolve(sharedArena, entry);
                if (diagram.ShapeCells.Count == 0)
                {
                    continue;
                }

                var expected = HexArea.CellsWithin(diagram.ShapeCenter.Value, entry.AreaRadius).Count();
                Assert.That(
                    diagram.ShapeCells, Has.Count.EqualTo(expected),
                    $"{entry.Id}: 착탄 칸이 벌판 경계에 잘렸다 — 아레나 반경을 키워야 한다.");
            }
        }

        [Test]
        [Category("ShippingData")]
        public void MoveDiagramBandsEveryCellByThePathfindersOwnStepCount()
        {
            // Q26-B: 열린 벌판에서는 도달 칸이 곧 원판이라 한 색으로 칠하면 "사거리 N"과 그림이 같아진다.
            // 걸음 수는 도감이 거리를 다시 재서 만든 값이 아니라 경로탐색이 낸 이동 비용 그대로여야 한다.
            foreach (var entry in ApprovedCards.Where(card => card.ActionType == CardEffectType.Move))
            {
                var diagram = CodexCardRange.Resolve(sharedArena, entry);
                if (diagram.RangeCells.Count == 0)
                {
                    continue;
                }

                var reachable = sharedArena.MovementState.GetReachablePlayerMoves(entry.Id);

                Assert.That(diagram.RangeSteps, Is.Not.Null, $"{entry.Id}: 이동 도해는 걸음 수를 실어야 한다.");
                Assert.That(
                    diagram.RangeSteps.Count, Is.EqualTo(diagram.RangeCells.Count),
                    $"{entry.Id}: 칠하는 칸과 걸음 수를 아는 칸이 어긋나면 띠에 구멍이 난다.");

                foreach (var coord in diagram.RangeCells)
                {
                    Assert.That(
                        diagram.RangeSteps[coord], Is.EqualTo(reachable[coord]),
                        $"{entry.Id} {coord}: 걸음 수가 경로탐색 값과 다르다.");
                }

                Assert.That(diagram.MaxRangeStep, Is.EqualTo(diagram.RangeSteps.Values.Max()));
            }
        }

        [Test]
        [Category("ShippingData")]
        public void FourStepMoveCardBandsFourDeepAndNoDeeper()
        {
            var move4 = ApprovedCards.Single(entry => entry.Id == "M04");
            var diagram = CodexCardRange.Resolve(sharedArena, move4);

            Assert.That(diagram.MaxRangeStep, Is.EqualTo(4));
            // 벌판이 평평하므로 n걸음 칸은 정확히 거리 n 링이다 — 띠가 뒤집히거나 밀리지 않았음을 고정한다.
            foreach (var pair in diagram.RangeSteps)
            {
                Assert.That(pair.Value, Is.EqualTo(CodexRangeArena.Origin.DistanceTo(pair.Key)));
            }
        }

        [Test]
        public void NonMoveCardsCarryNoStepBandsSoTheyStaySolid()
        {
            var attack = ApprovedCards.First(entry =>
                entry.ActionType == CardEffectType.Attack && entry.TargetMode == CardTargetMode.Enemy);
            Assert.That(CodexCardRange.Resolve(sharedArena, attack).MaxRangeStep, Is.Zero);
        }

        [Test]
        public void EmptyDiagramCarriesAReasonInsteadOfAnEmptyGrid()
        {
            // 자기 대상 카드에 빈 격자를 그리면 "사거리 0"으로 읽힌다 — 문구가 그 자리를 대신한다.
            var defend = ApprovedCards.First(entry => entry.ActionType == CardEffectType.Defend);
            var diagram = CodexCardRange.Resolve(sharedArena, defend);
            Assert.That(diagram.IsEmpty, Is.True);
            Assert.That(diagram.Note, Is.Not.Empty);
        }

        // ── 도우미 ────────────────────────────────────────────────────────

        private static List<HexCoord> SweepWithShippingValidator(CodexRangeArena arena, CardCatalogEntry entry)
        {
            if (entry.ActionType == CardEffectType.Status
                || entry.TargetMode == CardTargetMode.SelfArea
                || entry.TargetMode == CardTargetMode.Self
                || entry.TargetMode == CardTargetMode.None)
            {
                return new List<HexCoord>();
            }

            if (entry.ActionType == CardEffectType.Move)
            {
                return arena.MovementState.GetReachablePlayerMoves(entry.Id).Keys
                    .Where(coord => coord != CodexRangeArena.Origin)
                    .ToList();
            }

            var state = entry.ActionType == CardEffectType.Attack ? arena.EnemyState : arena.OpenState;
            return arena.Cells
                .Where(coord => entry.ActionType switch
                {
                    CardEffectType.Attack => state.ValidateAttackTarget(coord, entry.Id).IsValid,
                    CardEffectType.Scout => state.ValidateScoutTarget(coord, entry.Id).IsValid,
                    CardEffectType.FieldObject => state.ValidateFieldObjectTarget(coord, entry.Id).IsValid,
                    _ => false,
                })
                .ToList();
        }

        /// <summary>겨냥한 칸에 실제로 내 볼 수 있는 종류인가(선택지 카드·자기 대상은 이 시험 밖이다).</summary>
        private static bool CanPlayAtTile(CardCatalogEntry entry)
        {
            if (entry.TargetMode == CardTargetMode.OptionThenTarget
                || entry.TargetMode == CardTargetMode.RandomReachable)
            {
                return false;
            }

            return entry.ActionType == CardEffectType.Attack
                || entry.ActionType == CardEffectType.Scout
                || entry.ActionType == CardEffectType.FieldObject
                || entry.ActionType == CardEffectType.Move;
        }

        private static bool PlayAt(CardCatalogEntry entry, HexCoord target)
        {
            var arena = CodexRangeArena.Create(shippingCards);
            return Play(StateFor(arena, entry), entry, target);
        }

        private static CombatState StateFor(CodexRangeArena arena, CardCatalogEntry entry) =>
            entry.ActionType switch
            {
                CardEffectType.Attack => arena.EnemyState,
                CardEffectType.Move => arena.MovementState,
                _ => arena.OpenState,
            };

        private static bool Play(CombatState state, CardCatalogEntry entry, HexCoord target)
        {
            return entry.ActionType switch
            {
                CardEffectType.Attack => state.TryPlayerAttack(target, entry.Id),
                CardEffectType.Scout => state.TryPlayerScout(target, entry.Id),
                CardEffectType.FieldObject => state.TryPlayerFieldObject(target, entry.Id),
                CardEffectType.Move => state.TryPlayerMove(target, entry.Id),
                _ => false,
            };
        }

        private const string ShapeMonsterDefinitionId = "M002";
        private const string ShapeMonsterUnitId = "codex-shape-monster";

        /// <summary>
        /// 형상 하나를 정동(East)으로 예고하는 몬스터 하나를 세운다. 플레이어를 몬스터의 정동 쪽에
        /// 두어 조준 방향이 East로 확정된다.
        /// </summary>
        private static CombatState CreateShapeIntentState(string shapeId, int footprintRadius)
        {
            var monsterCoord = new HexCoord(-4, 0);
            var playerCoord = new HexCoord(0, 0);

            var state = new CombatState(
                CombatState.CreateDemoMap(9),
                playerCoord,
                new[]
                {
                    new MonsterConfig(
                        ShapeMonsterUnitId,
                        monsterCoord,
                        40,
                        definitionId: ShapeMonsterDefinitionId,
                        spawnRole: footprintRadius > 0 ? "boss" : string.Empty),
                },
                new CombatConfig(200, 40, 2, 1, 4, 4, 9, 1, 0, playerVisionRange: 9),
                monsterCatalog: new MonsterCatalogDefinition(
                    "codex-shape-test-catalog",
                    "Codex Shape Test Catalog",
                    new[]
                    {
                        new MonsterCatalogEntry(
                            ShapeMonsterDefinitionId,
                            "형상 시험 몬스터",
                            "test-melee",
                            "B001",
                            detectionRange: 9,
                            movePerTurn: 0,
                            hp: 40,
                            attackSpeed: 1,
                            attackPatterns: new[]
                            {
                                new MonsterAttackPattern("A900", "형상", 9, 0, 0, cooldownTurns: 0, phaseMin: 0, shapeId: shapeId),
                            }),
                    }),
                bossCatalog: footprintRadius > 0 ? CreateFootprintBossCatalog(footprintRadius) : null,
                drawOpeningHands: false);

            if (footprintRadius > 0)
            {
                // 몸 반경은 보스 페이즈에서 유도된다 — 2페이즈로 넘어가야 반경이 붙는다.
                RunFullTurn(state);
                RunFullTurn(state);
            }

            return state;
        }

        private static BossCatalogDefinition CreateFootprintBossCatalog(int footprintRadius)
        {
            var profiles =
                BossCsv.ProfilesHeader + "\n" +
                ShapeMonsterDefinitionId + ",형상 시험 보스,TurnCount,,,,,music.boss.test,\n";
            var phases =
                BossCsv.PhasesHeader + "\n" +
                ShapeMonsterDefinitionId + ",1,0,0,0,0,1,0,,\n" +
                ShapeMonsterDefinitionId + ",2," + footprintRadius + ",0,0,0,1,1,,\n";

            return BossCatalogCsvConverter.Convert(
                new BossCatalogCsvSource(profiles, phases, "codex-shape-boss", "Codex Shape Boss"));
        }

        private static void RunFullTurn(CombatState state)
        {
            state.EndAction();
            state.ResolveMonsterMovement();
            state.EndAction();
            state.ResolveMonsterAction();
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// P2.7 잔여 계약: 횃불(C-14 / D-15)과 함정 해제(C-13 / D-14).
    ///
    /// 둘 다 "기존 축·기존 배관에 얹는다"가 설계의 요점이라, 계약도 그 얹힘이 실제로 성립하는지를 본다:
    /// 횃불은 실명과 <b>같은 시야 축</b>에서 상쇄돼야 하고, 해제는 밟아서 소모된 함정과 <b>같은 종착점</b>
    /// (consumedTrapIds)에 도달해야 한다.
    /// </summary>
    public sealed class TorchAndTrapDisarmTests
    {
        private const int BaseVisionRange = 3;
        private static readonly HexCoord TrapCoord = new HexCoord(1, 0);

        // ------------------------------------------------------------------ C-14 횃불

        [Test]
        public void TorchWidensVisionImmediatelyWhenPlayed()
        {
            var state = CreateState();
            var edge = new HexCoord(BaseVisionRange + 2, 0);
            Assert.That(state.GetVisibility(edge), Is.Not.EqualTo(HexCellVisibility.Revealed), "전제: 아직 안 보인다.");

            Assert.That(state.TryPlayerUtility(ApprovedCardCatalogFactory.UtilityTorchId), Is.True, state.LastFailureReason);

            Assert.That(state.GetVisibility(edge), Is.EqualTo(HexCellVisibility.Revealed),
                "부여 즉시 안개가 걷혀야 한다 — 갱신 배선(RefreshPlayerVisionIfVisionStatus)이 빠지면 다음 이동까지 안 보인다.");
        }

        /// <summary>
        /// D-15의 정체성: <b>소모성</b> 시야. 매 턴 반경이 1씩 줄고, 다 타면 원래 시야로 돌아온다.
        /// 지속시간과 수치가 함께 깎이므로 "반경은 0인데 상태는 남아 있다"가 생기지 않는다.
        /// </summary>
        [Test]
        public void TorchBurnsDownOneRadiusPerTurnAndThenGoesOut()
        {
            var state = CreateState();
            Assert.That(state.TryPlayerUtility(ApprovedCardCatalogFactory.UtilityTorchId), Is.True, state.LastFailureReason);
            var granted = TorchAmount(state);
            Assert.That(granted, Is.EqualTo(3), "전제: 저작된 초기 반경은 3이다.");

            var seen = new List<int>();
            for (var i = 0; i < 4; i++)
            {
                AdvanceTurn(state);
                seen.Add(TorchAmount(state));
            }

            Assert.That(seen, Is.EqualTo(new[] { 3, 2, 1, 0 }),
                "적용 턴은 유예되고(3 유지) 그 뒤로 매 턴 1씩 타 들어간 뒤 사라진다.");
            Assert.That(state.ActiveEffects.Any(effect => effect.Kind == StatusEffectKind.TorchLight), Is.False,
                "다 타면 상태가 남지 않는다.");
        }

        /// <summary>
        /// 횃불(+)과 실명(−)이 <b>같은 축</b>에 얹혔는지. 축이 갈라져 있으면 둘 다 걸었을 때 상쇄되지 않는다.
        /// </summary>
        [Test]
        public void TorchAndBlindCancelThroughTheSameVisionAxis()
        {
            var baseline = VisionReach(CreateState());

            var lit = CreateState();
            Assert.That(lit.TryPlayerUtility(ApprovedCardCatalogFactory.UtilityTorchId), Is.True, lit.LastFailureReason);
            Assert.That(VisionReach(lit), Is.EqualTo(baseline + 3));

            var both = CreateState();
            Assert.That(both.TryPlayerUtility(ApprovedCardCatalogFactory.UtilityTorchId), Is.True, both.LastFailureReason);
            Inject(both, StatusEffectKind.Blind, both.Player.Id, remainingTurns: 3, amount: 3);

            Assert.That(VisionReach(both), Is.EqualTo(baseline),
                "같은 크기의 횃불(+3)과 실명(−3)은 상쇄돼야 한다.");
        }

        [Test]
        public void TorchIsABuffAndSurvivesCleanse()
        {
            var state = CreateState();
            Assert.That(state.TryPlayerUtility(ApprovedCardCatalogFactory.UtilityTorchId), Is.True, state.LastFailureReason);

            Assert.That(StatusEffectInfo.IsCleansable(StatusEffectKind.TorchLight), Is.False,
                "정화로 자기 횃불을 꺼뜨리면 안 된다 — 버프다.");
        }

        // ------------------------------------------------------------------ C-13 함정 해체 (S05 돌 다리 두드리기)
        //
        // 2026-08-20 #5: 전용 진입점(TryPlayerDisarmTrap)은 UI가 부르지 않아 조용한 no-op이었다.
        // 셀 선택 UI가 모든 정찰 카드를 보내는 <b>같은 진입점</b>(TryPlayerScout)으로 계약을 다시 쓴다 —
        // 카드 스스로 범위를 탐색해 함정을 발견하고, 발견된 것을 해체한다.

        [Test]
        public void DisarmRemovesADiscoveredTrapSoItNeverFires()
        {
            var state = CreateState(withTrap: true);
            RevealTrap(state);

            Assert.That(state.TryPlayerScout(TrapCoord, ApprovedCardCatalogFactory.ScoutTrapDisarmId), Is.True,
                state.LastFailureReason);

            Assert.That(state.ConsumedTrapIds, Does.Contain("disarm-target"),
                "해체는 밟아서 소모된 함정과 같은 종착점에 도달해야 한다.");
            Assert.That(state.GetVisibilitySafeCellInfo(TrapCoord).TrapRevealed, Is.False, "표시도 함께 지워진다.");

            // 이동은 이동 페이즈에서만 된다 — 해체 직후에는 아직 행동 페이즈다.
            var hpBefore = state.Player.Hp;
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerMovement));
            Assert.That(state.TryPlayerMove(TrapCoord), Is.True, state.LastFailureReason);
            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore), "해체한 칸을 밟아도 터지지 않는다.");
        }

        /// <summary>
        /// 새 문안 「범위 1을 탐색하고 발견된 함정을 해체합니다」의 요점: 카드가 <b>스스로 발견까지 한다</b>.
        /// 정찰의 공유 배관(RevealTrapsInArea)이 먼저 돌므로, 숨어 있던 함정도 이 카드 한 장으로
        /// 발견 → 해체까지 간다(예전 D-14 「발견된 것만」 계약을 대체하는 사용자 확정 사양).
        /// </summary>
        [Test]
        public void DisarmScoutsFirstSoAHiddenTrapInAreaIsRevealedAndDisarmed()
        {
            var state = CreateState(withTrap: true);
            Assert.That(state.ConsumedTrapIds, Is.Empty, "전제: 아직 발견도 소모도 없다.");

            Assert.That(state.TryPlayerScout(TrapCoord, ApprovedCardCatalogFactory.ScoutTrapDisarmId), Is.True,
                state.LastFailureReason);

            Assert.That(state.ConsumedTrapIds, Does.Contain("disarm-target"),
                "탐색이 먼저 돌아 숨은 함정도 발견 → 해체되어야 한다.");
        }

        [Test]
        public void TrapsOutsideTheScoutAreaAreNotDisarmed()
        {
            var state = CreateState(withTrap: true, trapCoord: new HexCoord(4, 0));
            RevealTrapAt(state, new HexCoord(4, 0));

            Assert.That(state.TryPlayerScout(TrapCoord, ApprovedCardCatalogFactory.ScoutTrapDisarmId), Is.True,
                state.LastFailureReason);

            Assert.That(state.ConsumedTrapIds, Is.Empty, "사거리 1 + 범위 1 카드는 4칸 밖을 못 건드린다.");
        }

        /// <summary>「해체!」 플로팅의 데이터 근거 — 함정 좌표에 TrapDisarmed 이벤트가 정확히 한 번 나간다.</summary>
        [Test]
        public void DisarmRaisesOneTrapDisarmedEventAtTheTrapTile()
        {
            var state = CreateState(withTrap: true);
            RevealTrap(state);
            var events = new List<EffectResultEvent>();
            state.EffectResolved += events.Add;

            Assert.That(state.TryPlayerScout(TrapCoord, ApprovedCardCatalogFactory.ScoutTrapDisarmId), Is.True,
                state.LastFailureReason);

            var disarmed = events.Where(candidate => candidate.Kind == EffectKind.TrapDisarmed).ToList();
            Assert.That(disarmed, Has.Count.EqualTo(1), "함정 좌표당 한 번의 해체 이벤트.");
            Assert.That(disarmed[0].Center, Is.EqualTo(TrapCoord), "텍스트는 함정 자리에 떠야 한다.");
        }

        /// <summary>
        /// 해체 카드도 <b>기절 게이트를 지난다</b>(정찰 공유 경로의 `CanUseActionCard` 경유). 신규 실행
        /// 경로를 만들 때 기존 게이트를 우회하는 것이 가장 흔한 사고다.
        /// </summary>
        [Test]
        public void DisarmGoesThroughTheSharedActionCardGate()
        {
            var state = CreateState(withTrap: true);
            RevealTrap(state);
            Inject(state, StatusEffectKind.Stun, state.Player.Id, remainingTurns: 2, amount: 0);

            Assert.That(state.TryPlayerScout(TrapCoord, ApprovedCardCatalogFactory.ScoutTrapDisarmId), Is.False);
            Assert.That(state.LastFailureReason, Does.Contain("기절"));
            Assert.That(state.ConsumedTrapIds, Is.Empty);
        }

        // ------------------------------------------------------------------ helpers

        private static int TorchAmount(CombatState state)
        {
            var torch = state.ActiveEffects.FirstOrDefault(effect => effect.Kind == StatusEffectKind.TorchLight);
            return torch.Kind == StatusEffectKind.TorchLight ? torch.Amount : 0;
        }

        /// <summary>실제로 보이는 가장 먼 칸까지의 거리 — 내부 계산식이 아니라 관찰 가능한 결과로 잰다.</summary>
        private static int VisionReach(CombatState state)
        {
            var reach = 0;
            for (var q = 0; q <= 12; q++)
            {
                if (state.GetVisibility(new HexCoord(q, 0)) == HexCellVisibility.Revealed)
                {
                    reach = q;
                }
            }

            return reach;
        }

        private static void RevealTrap(CombatState state) => RevealTrapAt(state, TrapCoord);

        /// <summary>
        /// 정찰이 쓰는 것과 같은 발견 경로. ⚠️여기서 <c>GetVisibilitySafeCellInfo</c>로 확인하지 않는다 —
        /// 그 투영은 이름대로 <b>안전</b>해서 아직 모르는(먼) 칸의 세부를 감춘다. 시야 밖 함정을 발견
        /// 처리한 뒤 그걸로 단언하면 발견이 됐는데도 false가 나온다(실제로 이 함정에 빠졌다).
        /// </summary>
        private static void RevealTrapAt(CombatState state, HexCoord coord)
        {
            var runtime = typeof(CombatState)
                .GetField("visibilityRuntime", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(state);
            runtime.GetType().GetMethod("RevealTrapsInArea").Invoke(runtime, new object[] { coord, 0 });
            Assert.That(
                (bool)runtime.GetType().GetMethod("IsTrapRevealed").Invoke(runtime, new object[] { coord }),
                Is.True,
                "전제: 함정이 발견 상태다.");
        }

        private static CombatState CreateState(bool withTrap = false, HexCoord? trapCoord = null)
        {
            var coord = trapCoord ?? TrapCoord;
            var traps = withTrap
                ? new[]
                {
                    new HexTrapData(
                        "disarm-target",
                        coord,
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
                System.Array.Empty<MonsterConfig>(),
                config,
                cardCatalog: TorchAndDisarmCatalog());

            // 유틸리티·정찰 카드는 행동 페이즈 카드다. 손에 확실히 쥐여 준 뒤 그 페이즈로 넘긴다.
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
            return entry.ToCardDefinition(state.CardCatalog.SourceId, $"{cardId}#test");
        }

        /// <summary>cards.csv의 U04·S05를 옮긴 픽스처(실제 저작과 어긋나면 CSV 임포터 테스트가 잡는다).</summary>
        private static CardCatalogDefinition TorchAndDisarmCatalog()
        {
            return new CardCatalogDefinition(
                "torch-disarm-test",
                "Torch/disarm test catalog",
                new[]
                {
                    new CardCatalogEntry(
                        ApprovedCardCatalogFactory.MoveBasicId, "Move", CardCategory.Movement, CardEffectType.Move,
                        1, 2, 2, CardEffectRefs.MoveBasic, "reachable_known_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(
                        ApprovedCardCatalogFactory.UtilityTorchId, "호롱불", CardCategory.Action, CardEffectType.Utility,
                        1, 0, 3, CardEffectRefs.UtilityTorch, "self",
                        status: CardCatalogStatus.Approved, targetMode: CardTargetMode.Self, playMode: CardPlayMode.Self),
                    new CardCatalogEntry(
                        ApprovedCardCatalogFactory.ScoutTrapDisarmId, "돌 다리 두드리기", CardCategory.Action, CardEffectType.Scout,
                        1, 1, 0, CardEffectRefs.ScoutTrapDisarm, string.Empty,
                        areaRadius: 1, status: CardCatalogStatus.Approved, targetMode: CardTargetMode.Tile)
                });
        }

        private static void Inject(CombatState state, StatusEffectKind kind, string unitId, int remainingTurns, int amount)
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

        private static void AdvanceTurn(CombatState state)
        {
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerMovement));
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// P2.5(묶음 C) 계약: 예고형(주기) 함정 C-11 / D-13.
    ///
    /// 이 함정의 정체성은 피해가 아니라 <b>예고</b>다 — 그래서 계약의 절반은 "언제 터지는가"가 아니라
    /// "터지기 전에 보이는가"이고, 예고와 발동이 갈라지지 않는다는 것이 나머지 절반이다.
    /// </summary>
    public sealed class PeriodicTrapTests
    {
        private static readonly HexCoord TrapCoord = new HexCoord(1, 0);

        // ------------------------------------------------------------------ 발동 타이밍

        /// <summary>
        /// 주기 2 = 짝수 턴 말에 터진다. 발동 시점을 런타임 카운터가 아니라 <c>OverallTurnNumber</c>에서
        /// 유도하므로 off-by-one이 생길 자리가 없다 — 보스 쿨다운이 겪었던 종류의 버그를 구조로 뺐다.
        /// </summary>
        [Test]
        public void PeriodicTrapFiresOnEveryNthTurnBoundaryAndNotBetween()
        {
            var state = CreateState(periodTurns: 2, damage: 3);
            var log = new List<(int Turn, int Hp)>();

            for (var i = 0; i < 4; i++)
            {
                Assert.That(state.OverallTurnNumber, Is.EqualTo(i + 1), "전제: 턴이 1부터 하나씩 오른다.");
                var hpBefore = state.Player.Hp;
                AdvanceTurn(state);
                log.Add((i + 1, hpBefore - state.Player.Hp));
            }

            Assert.That(log.Select(entry => entry.Hp), Is.EqualTo(new[] { 0, 3, 0, 3 }),
                "턴 1·3은 무사, 턴 2·4 말에 3 피해 — 주기 2.");
        }

        [Test]
        public void PeriodicTrapOnlyHitsInsideItsRadius()
        {
            var state = CreateState(periodTurns: 1, damage: 4, radius: 0);
            Assert.That(state.TryPlayerMove(new HexCoord(2, 0)), Is.True, state.LastFailureReason);
            var hpBefore = state.Player.Hp;

            AdvanceTurn(state);

            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore), "반경 밖으로 걸어 나가면 맞지 않는다 — 예고의 값어치.");
        }

        // ------------------------------------------------------------------ 밟기와의 분리

        /// <summary>
        /// 예고형은 밟아도 터지지 않는다 — 저작이 <c>triggerOnEnter</c>를 켜 뒀더라도.
        /// 두 트리거를 겸하면 "예고를 보고 피한다"는 정체성이 무너진다.
        /// </summary>
        [Test]
        public void SteppingOnAPeriodicTrapDoesNothingEvenWithTriggerOnEnterAuthored()
        {
            var state = CreateState(periodTurns: 3, damage: 5, triggerOnEnter: true);
            var hpBefore = state.Player.Hp;

            Assert.That(state.TryPlayerMove(TrapCoord), Is.True, state.LastFailureReason);

            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore), "밟기로는 터지지 않는다.");
            Assert.That(state.ConsumedTrapIds, Does.Not.Contain("periodic-trap"), "밟기로 소진되지도 않는다.");
        }

        [Test]
        public void SteppingOnAStepTrapStillWorks()
        {
            // 대조군: periodTurns 0이면 기존 밟기형 그대로다(회귀 가드).
            var state = CreateState(periodTurns: 0, damage: 5, triggerOnEnter: true);
            var hpBefore = state.Player.Hp;

            Assert.That(state.TryPlayerMove(TrapCoord), Is.True, state.LastFailureReason);

            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore - 5));
        }

        // ------------------------------------------------------------------ 예고

        /// <summary>
        /// 🔑예고와 발동이 <b>같은 술어</b>를 쓴다는 계약. 예고에 뜬 턴에는 반드시 맞고,
        /// 안 뜬 턴에는 반드시 안 맞는다 — "예고했는데 안 터진다"도 "예고 없이 터진다"도 없다.
        /// </summary>
        [Test]
        public void TelegraphAppearsExactlyOnTheTurnsTheTrapActuallyFires()
        {
            var state = CreateState(periodTurns: 2, damage: 3);
            RevealTrap(state);

            for (var i = 0; i < 4; i++)
            {
                var telegraphed = state.GetArmedPeriodicTrapCells().Contains(TrapCoord);
                var hpBefore = state.Player.Hp;
                AdvanceTurn(state);
                var wasHit = state.Player.Hp < hpBefore;

                Assert.That(telegraphed, Is.EqualTo(wasHit),
                    $"턴 {i + 1}: 예고({telegraphed})와 실제 발동({wasHit})이 갈라졌다.");
            }
        }

        /// <summary>
        /// 사용자 확정: 예고도 <b>정찰해야 보인다</b>. 일반 함정과 같은 `trapRevealed` 규칙이라
        /// 예고가 안개에 구멍을 뚫지 않는다.
        /// </summary>
        [Test]
        public void TelegraphIsHiddenUntilTheTrapIsDiscovered()
        {
            var state = CreateState(periodTurns: 1, damage: 3);

            Assert.That(state.GetArmedPeriodicTrapCells(), Is.Empty, "미발견 함정은 예고하지 않는다.");

            RevealTrap(state);

            Assert.That(state.GetArmedPeriodicTrapCells(), Does.Contain(TrapCoord));
        }

        [Test]
        public void TelegraphCoversTheWholeBlastRadiusNotJustTheOrigin()
        {
            var state = CreateState(periodTurns: 1, damage: 3, radius: 1);
            RevealTrap(state);

            var cells = state.GetArmedPeriodicTrapCells();

            Assert.That(cells, Does.Contain(TrapCoord));
            Assert.That(cells, Does.Contain(new HexCoord(2, 0)), "반경 1이면 이웃 칸도 위험 표시 대상이다.");
        }

        // ------------------------------------------------------------------ 세이브

        /// <summary>
        /// 주기 상태는 <c>OverallTurnNumber</c>에서 유도되므로 <b>따로 저장할 것이 없다</b>.
        /// 계획 §7.1-2가 예정했던 카운터 왕복 필드가 필요 없다는 사실을 관찰로 고정한다 —
        /// 왕복 후에도 같은 턴에 같은 예고가 뜬다.
        /// </summary>
        [Test]
        public void PeriodicStateSurvivesSuspendWithoutAnyDedicatedSaveField()
        {
            var state = CreateState(periodTurns: 2, damage: 3);
            RevealTrap(state);
            AdvanceTurn(state); // 턴 2 진입: 이번 턴 말에 터진다.
            Assert.That(state.GetArmedPeriodicTrapCells(), Does.Contain(TrapCoord), "전제: 지금 무장 상태다.");

            var snapshot = state.CreateSuspendSnapshot();
            var restored = CreateState(periodTurns: 2, damage: 3);
            restored.RestoreFromSuspend(snapshot);

            Assert.That(restored.OverallTurnNumber, Is.EqualTo(state.OverallTurnNumber));
            Assert.That(restored.GetArmedPeriodicTrapCells(), Does.Contain(TrapCoord),
                "재개 후에도 같은 턴에 같은 예고가 떠야 한다.");
        }

        /// <summary>
        /// GAP-2(fog-of-war.md): 정찰로 발견한 함정이 세이브 후 도로 안개에 묻히던 결함.
        /// 예고가 이 집합에 걸려 있어 P2.5에서 더 이상 미룰 수 없었다 —
        /// 세이브 한 번에 예고가 사라지면 "보고 피한다"가 성립하지 않는다.
        /// </summary>
        [Test]
        public void DiscoveredTrapsSurviveSuspend()
        {
            var state = CreateState(periodTurns: 2, damage: 3);
            RevealTrap(state);

            var restored = CreateState(periodTurns: 2, damage: 3);
            restored.RestoreFromSuspend(state.CreateSuspendSnapshot());

            Assert.That(
                restored.GetVisibilitySafeCellInfo(TrapCoord).TrapRevealed,
                Is.True,
                "GAP-2: 재개 후에도 정찰로 찾아 둔 함정은 발견 상태로 남아야 한다.");
        }

        [Test]
        public void UndiscoveredTrapsStayUndiscoveredAcrossSuspend()
        {
            // 역방향 가드: 복원이 "전부 발견"으로 뭉개지지 않는다.
            var state = CreateState(periodTurns: 2, damage: 3);

            var restored = CreateState(periodTurns: 2, damage: 3);
            restored.RestoreFromSuspend(state.CreateSuspendSnapshot());

            Assert.That(restored.GetVisibilitySafeCellInfo(TrapCoord).TrapRevealed, Is.False);
        }

        // ------------------------------------------------------------------ helpers

        private static CombatState CreateState(
            int periodTurns,
            int damage,
            int radius = 0,
            bool triggerOnEnter = false)
        {
            var trap = new HexTrapData(
                "periodic-trap",
                TrapCoord,
                radius: radius,
                effects: new[] { new HexTrapEffectData(HexTrapEffectKind.Damage, damage) },
                affectsPlayer: true,
                affectsMonsters: false,
                oneShot: false,
                triggerOnEnter: triggerOnEnter,
                periodTurns: periodTurns);

            var map = new HexMapData(
                Enumerable.Range(-1, 8).Select(q => new HexCellData(new HexCoord(q, 0), $"cell-{q}", "street", 1, true, false)),
                trapRefs: new[] { trap });

            // ⚠️몬스터를 두지 않는다: 이 스위트는 체력 변화로 함정 발동을 세는데, 몬스터가 있으면
            // 그 공격 피해가 같은 숫자에 섞여 "두 번 터졌다"처럼 보인다(실제로 그렇게 오진했다).
            // 플레이어는 함정 칸에서 시작한다 — 걸어 나가지 않는 한 매 발동마다 맞는다.
            return new CombatState(
                map,
                TrapCoord,
                System.Array.Empty<MonsterConfig>(),
                CombatConfig.Default);
        }

        /// <summary>
        /// 정찰 카드가 쓰는 것과 <b>같은 발견 경로</b>(<c>HexVisibilityRuntime.RevealTrapsInArea</c>)를 직접 부른다.
        /// 정찰 카드를 실제로 쓰려면 카탈로그·페이즈·사거리 전제가 붙어 이 스위트의 관심사(예고 게이트)를
        /// 흐리므로, 리플렉션으로 같은 지점만 두드린다(<c>StatusEffectGameplayTests.Inject</c> 선례).
        /// </summary>
        private static void RevealTrap(CombatState state)
        {
            var runtime = typeof(CombatState)
                .GetField("visibilityRuntime", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(state);
            runtime.GetType()
                .GetMethod("RevealTrapsInArea")
                .Invoke(runtime, new object[] { TrapCoord, 0 });

            Assert.That(state.GetVisibilitySafeCellInfo(TrapCoord).TrapRevealed, Is.True, "전제: 함정이 발견 상태다.");
        }

        private static void AdvanceTurn(CombatState state)
        {
            // DEC-2026-07-03-02: 한 턴 완주 = EndAction → 몬스터 이동 해석 → PlayerAction →
            // EndAction → 몬스터 행동 해석 → 다음 턴 PlayerMovement.
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();
        }
    }
}

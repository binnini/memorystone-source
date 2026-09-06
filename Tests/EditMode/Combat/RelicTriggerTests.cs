using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Runtime.Cards;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// T2 페이즈 C(2026-08-06) 트리거 유물 계약. 감투 삭제(DEC-2026-08-31-01 D1)로 출하 트리거는 7종.
    /// 확정 정본: docs/design/keyword-systematization-and-sts-insights.md §4 T2-D ④.
    /// 저작 표면은 relics.csv의 triggerRef/triggerParam 트레일링 컬럼 + RelicTriggerRegistry
    /// (미등록 ref 임포트 거부 — BossMechanicRegistry 선례).
    /// </summary>
    public sealed class RelicTriggerTests
    {
        private const string PlayerUnitId = "player";

        [Category("ShippingData")]
        [Test]
        public void CatalogShipsTriggerRelicSet()
        {
            var catalog = RelicCatalogCsv.ConvertFile(CombatCsvPaths.RelicsCsv);

            // 페이즈 B의 19 + 트리거 8 = 27 + T4-3 배달 가방 = 28에서, 도깨비 감투를 지워 27
            // (DEC-2026-08-31-01 D1). 트리거 유물은 8 → 7종이 됐다.
            Assert.That(catalog.Entries.Count, Is.EqualTo(27));
            Assert.That(catalog.Entries.Count(entry => entry.TriggerKind != RelicTriggerKind.None),
                Is.EqualTo(7), "출하 트리거 유물 수.");
            Assert.That(catalog.Entries.Any(entry => entry.TriggerKind == RelicTriggerKind.StealthCycle),
                Is.False, "도깨비 감투 삭제 — stealth-cycle을 쓰는 유물은 없다.");

            void AssertTrigger(string id, RelicTriggerKind kind, int param, int amount)
            {
                Assert.That(catalog.TryGet(id, out var relic), Is.True, $"{id} missing");
                Assert.That(relic.TriggerKind, Is.EqualTo(kind), id);
                Assert.That(relic.TriggerParam, Is.EqualTo(param), id);
                Assert.That(relic.EffectAmount, Is.EqualTo(amount), id);
                // 트리거 수치는 SumEffect 이중 적용을 막기 위해 effectKind None으로 저작한다(임포터가 거부).
                Assert.That(relic.EffectKind, Is.EqualTo(PlayerPermanentItemEffectKind.None), id);
            }

            AssertTrigger("relic-kkwaenggwari", RelicTriggerKind.MoveDistanceAttackBonus, 3, 2);
            AssertTrigger("relic-guardian-amulet", RelicTriggerKind.GuardChargeCycle, 20, 1);
            AssertTrigger("relic-water-mill", RelicTriggerKind.DrawPerCardsUsed, 10, 1);
            AssertTrigger("relic-bungeoppang-mold", RelicTriggerKind.BlockOnTurnEnd, 0, 1);
            AssertTrigger("relic-jjimjilbang-key", RelicTriggerKind.HealOnTurnStart, 0, 1);
            AssertTrigger("relic-watchman-whistle", RelicTriggerKind.KiOnKill, 0, 1);
            AssertTrigger("relic-hedgehog-doll", RelicTriggerKind.ThornsAdjacent, 0, 2);
        }

        [Test]
        public void UnknownTriggerRefIsRejectedAtImport()
        {
            const string csv =
                "id,kind,displayNameKo,descriptionKo,effectKind,effectAmount,extraEffects,durationTurns,triggerRef,triggerParam\n" +
                "relic-x,Relic,테스트,테스트.,None,1,,,no-such-trigger,3\n";
            Assert.Throws<System.ArgumentException>(
                () => RelicCatalogCsv.ConvertText(csv),
                "미등록 triggerRef는 임포트가 거부해야 한다 — 저작만으로 죽은 트리거가 생기지 않게.");
        }

        [Test]
        public void TriggerRelicWithScalarEffectKindIsRejectedAtImport()
        {
            const string csv =
                "id,kind,displayNameKo,descriptionKo,effectKind,effectAmount,extraEffects,durationTurns,triggerRef,triggerParam\n" +
                "relic-x,Relic,테스트,테스트.,AttackDamageBonus,2,,,move-distance-attack-bonus,3\n";
            Assert.Throws<System.ArgumentException>(
                () => RelicCatalogCsv.ConvertText(csv),
                "트리거 수치가 스칼라 축으로도 합산되면 이중 적용이다 — effectKind는 None이어야 한다.");
        }

        [Test]
        public void CycleTriggerWithoutParamIsRejectedAtImport()
        {
            const string csv =
                "id,kind,displayNameKo,descriptionKo,effectKind,effectAmount,extraEffects,durationTurns,triggerRef,triggerParam\n" +
                "relic-x,Relic,테스트,테스트.,None,1,,,guard-charge-cycle,0\n";
            Assert.Throws<System.ArgumentException>(() => RelicCatalogCsv.ConvertText(csv));
        }

        // ------------------------------------------------------------------ 무선 이어폰

        [Test]
        public void KkwaenggwariRaisesFlatAttackBonusOnlyFromThreeCellsMoved()
        {
            var state = CreateState();
            Assert.That(state.TryGrantPermanentItem("relic-kkwaenggwari", out var reason), Is.True, reason);

            Assert.That(FlatAttackBonus(state), Is.EqualTo(0), "이동 0칸 — 미발동.");
            SetMovedDistance(state, 2);
            Assert.That(FlatAttackBonus(state), Is.EqualTo(0), "경계 아래(2칸) — 미발동.");
            SetMovedDistance(state, 3);
            Assert.That(FlatAttackBonus(state), Is.EqualTo(2), "경계(3칸)부터 +2.");
            SetMovedDistance(state, 5);
            Assert.That(FlatAttackBonus(state), Is.EqualTo(2), "초과 이동에도 고정 +2.");
        }

        // ------------------------------------------------------------------ 수호

        [Test]
        public void GuardianAmuletArrivesChargedAndNegatesNextHarmfulStatusOnce()
        {
            var state = CreateState();
            Assert.That(state.TryGrantPermanentItem("relic-guardian-amulet", out var reason), Is.True, reason);
            Assert.That(state.HasActiveGuardCharge(), Is.True, "수호 부적은 충전된 채 도착한다(StS Artifact).");

            // 1차 부여: 수호가 삼킨다 — 상태가 붙지 않고 충전이 꺼진다.
            Assert.That(AddStatus(state, StatusEffectKind.Weaken, PlayerUnitId, 2, 30), Is.False);
            Assert.That(PlayerHas(state, StatusEffectKind.Weaken), Is.False, "수호가 무효화한 상태가 붙어 있다.");
            Assert.That(state.HasActiveGuardCharge(), Is.False, "충전 1은 소비로 사라진다.");

            // 2차 부여: 충전이 없으니 정상 부여.
            Assert.That(AddStatus(state, StatusEffectKind.Weaken, PlayerUnitId, 2, 30), Is.True);
            Assert.That(PlayerHas(state, StatusEffectKind.Weaken), Is.True);
        }

        [Test]
        public void GuardNegationRaisesStatusNegatedEventNamingTheKind()
        {
            var state = CreateState();
            Assert.That(state.TryGrantPermanentItem("relic-guardian-amulet", out var reason), Is.True, reason);

            EffectResultEvent? negated = null;
            state.EffectResolved += evt =>
            {
                if (evt.Kind == EffectKind.StatusNegated)
                {
                    negated = evt;
                }
            };

            Assert.That(AddStatus(state, StatusEffectKind.Weaken, PlayerUnitId, 2, 30), Is.False, "수호가 삼켜야 한다.");
            Assert.That(negated.HasValue, Is.True, "무효화는 StatusNegated 이벤트로 알린다(\"{상태} 무효!\" — 사용자 확정).");
            Assert.That(negated.Value.StatusKind, Is.EqualTo(StatusEffectKind.Weaken), "무엇을 삼켰는지 StatusKind에 실린다.");
            Assert.That(negated.Value.TargetUnitId, Is.EqualTo(PlayerUnitId));
        }

        [Test]
        public void GuardDoesNotExpireByTurnsButBuffsPassThrough()
        {
            var state = CreateState();
            Assert.That(state.TryGrantPermanentItem("relic-guardian-amulet", out var reason), Is.True, reason);

            // 카운터 스택(OnConsume): 턴이 굴러도 수호는 만료되지 않는다.
            RunFullTurn(state);
            RunFullTurn(state);
            Assert.That(state.HasActiveGuardCharge(), Is.True, "수호는 턴으로 만료되지 않는다(OnConsume).");

            // 이로운 상태(비-정화 대상)는 수호를 소비하지 않는다.
            Assert.That(AddStatus(state, StatusEffectKind.Agility, PlayerUnitId, 1, 1), Is.True);
            Assert.That(state.HasActiveGuardCharge(), Is.True, "버프 부여가 수호를 소비하면 안 된다.");
        }

        [Test]
        public void GuardDoesNotBlockDelayedSelfImmobilize()
        {
            var state = CreateState();
            Assert.That(state.TryGrantPermanentItem("relic-guardian-amulet", out var reason), Is.True, reason);

            // D04/D06의 지연 자기 속박은 카드가 청구한 비용 — 수호 관문을 구조적으로 우회한다(D6 정합).
            Invoke(state, "SchedulePlayerDelayedImmobilize", 1);
            Invoke(state, "ApplyPendingSelfImmobilize");

            Assert.That(PlayerHas(state, StatusEffectKind.Immobilize), Is.True, "예약 자기 속박은 수호에 막히지 않는다.");
            Assert.That(state.HasActiveGuardCharge(), Is.True, "수호 충전도 소비되지 않는다.");
        }

        [Test]
        public void GuardRechargesOnCycleTurnOnlyWhenEmpty()
        {
            var state = CreateState(enemyDistance: 5, mapRadius: 5);
            Assert.That(state.TryGrantPermanentItem("relic-guardian-amulet", out var reason), Is.True, reason);

            // 충전 보유 중의 주기 턴: "최대 1" — 그대로 1이다.
            SetOverallTurn(state, 19);
            RunFullTurn(state); // → 턴 20
            Assert.That(GuardCharges(state), Is.EqualTo(1), "충전 보유 중 재충전은 넘치지 않는다(최대 1).");

            // 소비로 비운 뒤의 주기 턴: 재충전된다.
            Assert.That(AddStatus(state, StatusEffectKind.Weaken, PlayerUnitId, 2, 30), Is.False, "수호 소비.");
            Assert.That(state.HasActiveGuardCharge(), Is.False);
            SetOverallTurn(state, 39);
            RunFullTurn(state); // → 턴 40
            Assert.That(state.HasActiveGuardCharge(), Is.True, "주기 턴(20의 배수)에 빈 충전이 채워진다.");
        }

        // ------------------------------------------------------------------ 은신(휴면)

        [Test]
        public void StealthCycleTriggerStaysRegisteredEvenThoughNoRelicUsesIt()
        {
            // 도깨비 감투를 지우면서(DEC-2026-08-31-01 D1) stealth-cycle은 유일 사용자를 잃었다.
            // 그래도 레지스트리에 남긴다 — enum RelicTriggerKind에서 값을 빼면 직렬화 드리프트를
            // 부르고(append-only 원칙), 임포터는 미등록 ref만 거부하므로 남겨도 아무것도 깨지지
            // 않는다. 이 테스트는 「나중에 다른 유물이 그대로 쓸 수 있다」는 약속을 지킨다.
            Assert.That(RelicTriggerRegistry.RegisteredRefs, Does.Contain("stealth-cycle"));
            Assert.That(RelicTriggerRegistry.TryGet("stealth-cycle", out var kind), Is.True);
            Assert.That(kind, Is.EqualTo(RelicTriggerKind.StealthCycle));
        }

        // ------------------------------------------------------------------ 코인 세탁기

        [Test]
        public void WaterMillDrawsExactlyOnEveryTenthActionCardUse()
        {
            var state = CreateState();
            Assert.That(state.TryGrantPermanentItem("relic-water-mill", out var reason), Is.True, reason);
            AdvanceToPlayerAction(state);

            // 버림 더미를 만들어 드로우가 항상 가능하게 한다.
            Assert.That(state.TryPlayerDefend("D00"), Is.True, state.LastFailureReason);

            for (var use = 2; use <= 9; use++)
            {
                var before = state.ActionDeck.Hand.Count;
                Invoke(state, "RegisterActionCardUse", CombatCardKind.Utility);
                Assert.That(state.ActionDeck.Hand.Count, Is.EqualTo(before), $"{use}번째 사용 — 아직 드로우 없음.");
            }

            var handBeforeTenth = state.ActionDeck.Hand.Count;
            Invoke(state, "RegisterActionCardUse", CombatCardKind.Utility);
            Assert.That(state.ActionDeck.Hand.Count, Is.EqualTo(handBeforeTenth + 1), "10번째 사용에 1장 드로우.");
        }

        [Test]
        public void WaterMillCounterSurvivesSuspendRoundtrip()
        {
            var state = CreateState();
            Invoke(state, "RegisterActionCardUse", CombatCardKind.Utility);
            Invoke(state, "RegisterActionCardUse", CombatCardKind.Utility);
            Invoke(state, "RegisterActionCardUse", CombatCardKind.Utility);

            var snapshot = state.CreateSuspendSnapshot();
            Assert.That(snapshot.TotalActionCardsUsed, Is.EqualTo(3));

            var restored = CreateState();
            restored.RestoreFromSuspend(snapshot);
            var field = typeof(CombatState).GetField("totalActionCardsUsed", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(field.GetValue(restored), Is.EqualTo(3), "누적 카운터는 서스펜드를 왕복한다.");
        }

        // ------------------------------------------------------------------ 붕어빵 틀

        [Test]
        public void BungeoppangGrantsBlockAtActionPhaseEndOnly()
        {
            var state = CreateState();
            Assert.That(state.TryGrantPermanentItem("relic-bungeoppang-mold", out var reason), Is.True, reason);

            // 이동 페이즈 종료는 턴말 훅이 아니다.
            Assert.That(state.EndAction(), Is.True);
            Assert.That(state.Player.Block, Is.EqualTo(0), "이동 페이즈 EndAction에서는 방어막이 나오지 않는다.");
            state.ResolveMonsterMovement();

            // 액션 페이즈 종료 = 턴말 — 방어막 1. 이 방어막은 곧 이어질 몬스터 공격을 받는다.
            Assert.That(state.EndAction(), Is.True);
            Assert.That(state.Player.Block, Is.EqualTo(1), "턴 종료 시 방어막 1(Metallicize 문법).");
        }

        // ------------------------------------------------------------------ 찜질방 열쇠

        [Test]
        public void JjimjilbangHealsOneAtTurnStartUpToMax()
        {
            // 몬스터가 사이클 중 명중해 HP 산식을 흔들지 않도록 멀리 둔다.
            var state = CreateState(enemyDistance: 5, mapRadius: 5);
            Assert.That(state.TryGrantPermanentItem("relic-jjimjilbang-key", out var reason), Is.True, reason);

            state.Player.ApplyDamage(3);
            var hpBefore = state.Player.Hp;
            RunFullTurn(state);
            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore + 1), "턴 시작마다 HP 1 회복.");

            // 만피에서는 넘치지 않는다.
            state.Player.Heal(999);
            var maxHp = state.Player.Hp;
            RunFullTurn(state);
            Assert.That(state.Player.Hp, Is.EqualTo(maxHp), "회복은 최대 체력을 넘지 않는다.");
        }

        // ------------------------------------------------------------------ 호루라기

        [Test]
        public void WhistleRestoresKiOnKillOnceWithMaxKiCap()
        {
            var state = CreateState();
            Assert.That(state.TryGrantPermanentItem("relic-watchman-whistle", out var reason), Is.True, reason);
            AdvanceToPlayerAction(state);

            // 기를 깎아 회복 여지를 만든다.
            Assert.That(state.TryPlayerDefend("D00"), Is.True, state.LastFailureReason);
            var kiBefore = state.ActionCostRemaining;

            var monster = FirstMonster(state);
            monster.Combatant.ApplyDamage(monster.Combatant.Hp);
            Invoke(state, "ResetDeadMonsterToPatrolIntent", monster);
            Assert.That(state.ActionCostRemaining, Is.EqualTo(kiBefore + 1), "처치 시 기 1 회복.");

            // 같은 몬스터로 두 번 보상받지 않는다(래치) — 처치 수렴 지점이 중복 호출돼도 안전해야 한다.
            Invoke(state, "ResetDeadMonsterToPatrolIntent", monster);
            Assert.That(state.ActionCostRemaining, Is.EqualTo(kiBefore + 1), "중복 보상 없음.");
        }

        // ------------------------------------------------------------------ 고슴도치 인형

        [Test]
        public void HedgehogReflectsTwoDamagePerAdjacentHit()
        {
            var state = CreateState(enemyDistance: 1);
            Assert.That(state.TryGrantPermanentItem("relic-hedgehog-doll", out var reason), Is.True, reason);

            var monster = FirstMonster(state);
            var monsterHpBefore = monster.Combatant.Hp;
            var playerHpBefore = state.Player.Hp;

            // 인접 몬스터가 실제로 명중할 때까지 온전한 턴 사이클을 굴린다(CurseCardTests.RunMonsterHit 선례).
            for (var i = 0; i < 4 && state.Player.Hp == playerHpBefore; i++)
            {
                RunFullTurn(state);
            }

            Assert.That(state.Player.Hp, Is.LessThan(playerHpBefore), "몬스터 공격이 한 번은 명중해야 한다.");
            var hits = CountHitsFromDamage(monsterHpBefore - monster.Combatant.Hp);
            Assert.That(monster.Combatant.Hp, Is.EqualTo(monsterHpBefore - hits * 2),
                "인접 명중 1회당 반사 피해 2 — 그 외 어떤 경로로도 몬스터 HP가 줄지 않았어야 한다.");
            Assert.That(hits, Is.GreaterThanOrEqualTo(1));
        }

        // ------------------------------------------------------------------ helpers

        private static int CountHitsFromDamage(int totalMonsterDamage)
        {
            Assert.That(totalMonsterDamage % 2, Is.EqualTo(0), "몬스터가 잃은 HP는 전부 반사(2/회)여야 한다.");
            return totalMonsterDamage / 2;
        }

        private static CombatState CreateState(int enemyDistance = 3, int mapRadius = 3, int enemyChaseRange = 0)
        {
            var config = TestCombatConfigs.Standard(
                actionBudget: 4, movementHandSize: 1, actionHandSize: 4, enemyChaseRange: enemyChaseRange);
            return new CombatState(
                CombatState.CreateDemoMap(mapRadius),
                new HexCoord(0, 0),
                new HexCoord(enemyDistance, 0),
                config,
                cardCatalog: CreateCatalog());
        }

        private static CardCatalogDefinition CreateCatalog()
        {
            return new CardCatalogDefinition(
                "relic-trigger-test",
                "Relic trigger test catalog",
                new[]
                {
                    new CardCatalogEntry(
                        CardIds.Move1Hex, "Move 1", CardCategory.Movement, CardEffectType.Move,
                        1, 1, 1, "reachable_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(
                        "D00", "방어의 기초", CardCategory.Action, CardEffectType.Defend,
                        1, 0, 3, "self", status: CardCatalogStatus.Approved),
                });
        }

        private static void AdvanceToPlayerAction(CombatState state)
        {
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction));
        }

        /// <summary>이동 종료 → 몬스터 이동 → 액션 종료 → 몬스터 행동(다음 턴 시작까지)의 온전한 사이클.</summary>
        private static void RunFullTurn(CombatState state)
        {
            Assert.That(state.EndAction(), Is.True, state.LastFailureReason);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True, state.LastFailureReason);
            state.ResolveMonsterAction();
        }

        private static bool PlayerHas(CombatState state, StatusEffectKind kind)
        {
            return state.ActiveEffects.Any(effect =>
                effect.Kind == kind && effect.TargetUnitId == PlayerUnitId && !effect.IsExpired);
        }

        private static int GuardCharges(CombatState state)
        {
            return state.ActiveEffects
                .Where(effect => effect.Kind == StatusEffectKind.Guard && effect.TargetUnitId == PlayerUnitId && !effect.IsExpired)
                .Sum(effect => effect.Amount);
        }

        /// <summary>부여 관문(AddDurationStatusEffect) 직접 호출 — 반환값 = 실제 부여 여부(수호 무효 시 false).</summary>
        private static bool AddStatus(CombatState state, StatusEffectKind kind, string targetUnitId, int turns, int amount)
        {
            var method = typeof(CombatState).GetMethod(
                "AddDurationStatusEffect",
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new[] { typeof(StatusEffectKind), typeof(string), typeof(int), typeof(int), typeof(string) },
                null);
            return (bool)method.Invoke(state, new object[] { kind, targetUnitId, turns, amount, "test" });
        }

        private static int FlatAttackBonus(CombatState state)
        {
            var method = typeof(CombatState).GetMethod("GetFlatAttackDamageBonus", BindingFlags.NonPublic | BindingFlags.Instance);
            return (int)method.Invoke(state, null);
        }

        private static void SetMovedDistance(CombatState state, int distance)
        {
            typeof(CombatState)
                .GetField("lastMovedDistance", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(state, distance);
        }

        private static void SetOverallTurn(CombatState state, int turn)
        {
            typeof(CombatState)
                .GetProperty("OverallTurnNumber")
                .SetValue(state, turn);
        }

        private static object Invoke(CombatState state, string methodName, params object[] args)
        {
            var method = typeof(CombatState).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(method, Is.Not.Null, $"{methodName} not found");
            return method.Invoke(state, args);
        }

        /// <summary>공개 Monsters는 스냅샷 뷰라 런타임 조작이 안 된다 — 사설 monsters 리플렉션(전 스위트 선례).</summary>
        private static MonsterRuntime FirstMonster(CombatState state)
        {
            var monsters = (System.Collections.Generic.List<MonsterRuntime>)typeof(CombatState)
                .GetField("monsters", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(state);
            return monsters[0];
        }
    }
}

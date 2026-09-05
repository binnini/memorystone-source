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
    /// Locks the corrected status-effect semantics: every effect applies to both player and monster,
    /// 속박/기절 gate movement/action distinctly, 둔화/민첩 shift move range, 파열 reduces gained block, and
    /// 반사 mitigates the holder's incoming damage by a fixed 50% and reflects the remainder back.
    /// </summary>
    public sealed class StatusEffectGameplayTests
    {
        // --- Player-side consumption -------------------------------------------------------------

        [Test]
        public void RuptureReducesGainedBlock()
        {
            var state = CombatState.CreateDefaultDemo();
            Assert.That(state.EndAction(), Is.True); // movement -> action phase
            state.ResolveMonsterMovement(); // DEC-2026-07-03-02: 몬스터 이동 해석 후 PlayerAction 도달.
            Inject(state, StatusEffectKind.Rupture, state.Player.Id, remainingTurns: 2, amount: 100);

            Assert.That(state.TryPlayerDefend(), Is.True);

            Assert.That(state.Player.Block, Is.EqualTo(0),
                "Rupture (파열) should subtract from the block about to be gained.");
        }

        [Test]
        public void StunBlocksPlayerMovement()
        {
            var state = CombatStateFixture.Arena(3).WithEnemyEastAt(2).Build();
            Assert.That(state.GetReachablePlayerMoves().ContainsKey(new HexCoord(1, 0)), Is.True,
                "Sanity: the adjacent tile is reachable without any status.");

            Inject(state, StatusEffectKind.Stun, state.Player.Id, remainingTurns: 1, amount: 0);

            Assert.That(state.GetReachablePlayerMoves().ContainsKey(new HexCoord(1, 0)), Is.False,
                "Stunned (기절) player cannot move.");
        }

        [Test]
        public void StunBlocksPlayerAction()
        {
            var state = CombatState.CreateDefaultDemo();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement(); // DEC-2026-07-03-02: 몬스터 이동 해석 후 PlayerAction 도달.
            Inject(state, StatusEffectKind.Stun, state.Player.Id, remainingTurns: 1, amount: 0);

            Assert.That(state.TryPlayerDefend(), Is.False, "Stunned (기절) player cannot use action cards.");
        }

        [Test]
        public void ImmobilizeBlocksPlayerMovement()
        {
            var state = CombatStateFixture.Arena(3).WithEnemyEastAt(2).Build();
            Inject(state, StatusEffectKind.Immobilize, state.Player.Id, remainingTurns: 1, amount: 0);

            Assert.That(state.GetReachablePlayerMoves().ContainsKey(new HexCoord(1, 0)), Is.False,
                "Immobilized (속박) player cannot move.");
        }

        [Test]
        public void ImmobilizeDoesNotBlockPlayerAction()
        {
            var state = CombatState.CreateDefaultDemo();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement(); // DEC-2026-07-03-02: 몬스터 이동 해석 후 PlayerAction 도달.
            Inject(state, StatusEffectKind.Immobilize, state.Player.Id, remainingTurns: 1, amount: 0);

            Assert.That(state.TryPlayerDefend(), Is.True,
                "Immobilize (속박) only roots movement — actions are unaffected (unlike 기절).");
        }

        [Test]
        public void SelfModeMovementCardIsPlayableWithoutControlStatuses()
        {
            // Premise guard for the two lockout tests below: M05-style self-mode movement cards resolve
            // through TryPlayerMovementSelf, not TryPlayerMove.
            var state = CreateSelfMoveState();

            Assert.That(state.TryPlayerMovementSelf(ApprovedCardCatalogFactory.MoveMomentumId), Is.True, state.LastFailureReason);
        }

        [Test]
        public void StunBlocksSelfModeMovementCard()
        {
            // Regression: TryPlayerMovementSelf skipped IsPlayerMovementBlocked, so a stunned player could
            // still play M05 추진력 even though the UI usable flag greys every Move card under stun.
            var state = CreateSelfMoveState();
            Inject(state, StatusEffectKind.Stun, state.Player.Id, remainingTurns: 1, amount: 0);

            Assert.That(state.TryPlayerMovementSelf(ApprovedCardCatalogFactory.MoveMomentumId), Is.False,
                "Stunned (기절) player must not play a self-mode movement card either.");
            Assert.That(state.LastFailureReason, Does.Contain("기절"));
        }

        [Test]
        public void ImmobilizeBlocksSelfModeMovementCard()
        {
            var state = CreateSelfMoveState();
            Inject(state, StatusEffectKind.Immobilize, state.Player.Id, remainingTurns: 1, amount: 0);

            Assert.That(state.TryPlayerMovementSelf(ApprovedCardCatalogFactory.MoveMomentumId), Is.False,
                "속박 blocks self-mode movement cards too, matching the UI usable flag (IsCardUsable).");
            Assert.That(state.LastFailureReason, Does.Contain("속박"));
        }

        [Test]
        public void AgilityIncreasesPlayerMoveRange()
        {
            var state = CombatStateFixture.Arena(8).WithEnemyEastAt(7).Build();
            var baseRange = state.GetReachablePlayerMoves().Values.Max();

            Inject(state, StatusEffectKind.Agility, state.Player.Id, remainingTurns: 1, amount: 2);

            Assert.That(state.GetReachablePlayerMoves().Values.Max(), Is.EqualTo(baseRange + 2),
                "Agility (민첩) adds to the player's move range.");
        }

        [Test]
        public void SlowReducesPlayerMoveRange()
        {
            var state = CombatStateFixture.Arena(8).WithEnemyEastAt(7).Build();
            var baseRange = state.GetReachablePlayerMoves().Values.Max();

            Inject(state, StatusEffectKind.Slow, state.Player.Id, remainingTurns: 1, amount: 1);

            Assert.That(state.GetReachablePlayerMoves().Values.Max(), Is.EqualTo(System.Math.Max(0, baseRange - 1)),
                "Slow (둔화) subtracts from the player's move range.");
        }

        [Test]
        public void ReappliedSlowRefreshesDurationWithoutStackingMovePenalty()
        {
            var state = CombatStateFixture.Arena(8).WithEnemyEastAt(7).Build();
            var baseRange = state.GetReachablePlayerMoves().Values.Max();

            ApplyDurationStatus(state, StatusEffectKind.Slow, state.Player.Id, remainingTurns: 2, amount: 1);
            ApplyDurationStatus(state, StatusEffectKind.Slow, state.Player.Id, remainingTurns: 1, amount: 1);

            var slows = state.ActiveEffects
                .Where(effect => effect.Kind == StatusEffectKind.Slow && effect.TargetUnitId == state.Player.Id)
                .ToArray();
            Assert.That(slows, Has.Length.EqualTo(1));
            Assert.That(slows[0].RemainingTurns, Is.EqualTo(2));
            Assert.That(slows[0].Amount, Is.EqualTo(1));
            Assert.That(state.GetReachablePlayerMoves().Values.Max(), Is.EqualTo(System.Math.Max(0, baseRange - 1)),
                "Repeated Slow should refresh the debuff, not double the movement penalty.");
        }

        [Test]
        public void RandomJourneyUsesSlowAdjustedAreaMoveRange()
        {
            var config = CombatConfig.Default;
            var catalog = ApprovedCardCatalogFactory.CreateApprovedCatalog(config);
            var randomJourney = catalog.Entries
                .Single(entry => entry.Id == ApprovedCardCatalogFactory.MoveRandomJourneyId)
                .ToCardDefinition(catalog.SourceId);
            var state = CombatStateFixture.Arena(8)
                .WithConfig(config)
                .WithEnemyEastAt(7)
                .WithMovementHand(randomJourney)
                .Build();
            ApplyDurationStatus(state, StatusEffectKind.Slow, state.Player.Id, remainingTurns: 1, amount: 1);

            Assert.That(state.TryPlayerMove(state.PlayerCoord, ApprovedCardCatalogFactory.MoveRandomJourneyId), Is.True);
            Assert.That(new HexCoord(0, 0).DistanceTo(state.PlayerCoord), Is.LessThanOrEqualTo(1),
                "M06's blast-2 random move should be reduced to radius 1 by Slow 1.");
        }

        // --- Monster-side consumption ------------------------------------------------------------

        [Test]
        public void ImmobilizedMonsterStaysButStillAttacks()
        {
            var state = CombatStateFixture.Arena(3)
                .WithEnemyEastAt(1)
                .WithMonsterCatalog(CreateConfigMonsterCatalog(CombatConfig.Default))
                .Build();
            EnterActionPhase(state);
            var monsterId = state.Monsters.Single().Id;
            Inject(state, StatusEffectKind.Immobilize, monsterId, remainingTurns: 1, amount: 0);
            var hpBefore = state.Player.Hp;

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            Assert.That(state.Monsters.Single().Coord, Is.EqualTo(new HexCoord(1, 0)), "Immobilized monster does not move.");
            Assert.That(state.Player.Hp, Is.LessThan(hpBefore), "Immobilized (속박) monster can still attack.");
        }

        [Test]
        public void StunnedMonsterNeitherMovesNorAttacks()
        {
            var state = CombatStateFixture.Arena(3)
                .WithEnemyEastAt(1)
                .WithMonsterCatalog(CreateConfigMonsterCatalog(CombatConfig.Default))
                .Build();
            EnterActionPhase(state);
            var monsterId = state.Monsters.Single().Id;
            Inject(state, StatusEffectKind.Stun, monsterId, remainingTurns: 1, amount: 0);
            var hpBefore = state.Player.Hp;

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            Assert.That(state.Monsters.Single().Coord, Is.EqualTo(new HexCoord(1, 0)), "Stunned monster does not move.");
            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore), "Stunned (기절) monster cannot attack.");
        }

        [Test]
        public void SlowedMonsterDoesNotMove()
        {
            var state = CombatStateFixture.Arena(4)
                .WithEnemyEastAt(2)
                .WithMonsterCatalog(CreateConfigMonsterCatalog(CombatConfig.Default))
                .Build();
            var monsterId = state.Monsters.Single().Id;
            // Slow ≥ the monster's move budget drops the effective budget to 0, rooting it this turn.
            // DEC-2026-07-03-02: 몬스터 이동은 MonsterMovement 페이즈에서 해석되므로,
            // 그 해석 전에 둔화를 주입해야 이동 차단을 검증할 수 있다.
            Inject(state, StatusEffectKind.Slow, monsterId, remainingTurns: 1, amount: 99);

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();

            Assert.That(state.Monsters.Single().Coord, Is.EqualTo(new HexCoord(2, 0)),
                "Slow (둔화) ≥ move budget keeps the monster from advancing.");
        }

        [Test]
        public void PlayerReflectMitigatesHalfAndReflectsRemainder()
        {
            var state = CombatStateFixture.Arena(3)
                .WithEnemyEastAt(1)
                .WithMonsterCatalog(CreateConfigMonsterCatalog(CombatConfig.Default))
                .Build();
            EnterActionPhase(state);
            var monster = state.Monsters.Single();
            var attackDamage = monster.SelectedAttackPatternDamage;
            var reflected = attackDamage / 2;                 // 고정 50% → 공격자에게 되돌리는 양
            var damageToPlayer = attackDamage - reflected;    // 경감 후 남은 피해 = 플레이어 실피격
            var playerHpBefore = state.Player.Hp;
            var monsterHpBefore = monster.Hp;

            Inject(state, StatusEffectKind.Reflect, state.Player.Id, remainingTurns: 1, amount: 50);

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            Assert.That(state.Player.Hp, Is.EqualTo(playerHpBefore - damageToPlayer),
                "Reflect (반사) 고정 50% 경감: 플레이어는 절반만 피격한다 (양날의 방패).");
            Assert.That(state.Monsters.Single().Hp, Is.EqualTo(monsterHpBefore - reflected),
                "Reflect (반사) 경감 후 남은 피해를 공격자에게 그대로 되돌린다.");
        }

        [Test]
        public void StrengthenedMonsterDealsIncreasedDamage()
        {
            var state = CombatStateFixture.Arena(3)
                .WithEnemyEastAt(1)
                .WithMonsterCatalog(CreateConfigMonsterCatalog(CombatConfig.Default))
                .Build();
            EnterActionPhase(state);
            var monster = state.Monsters.Single();
            var attackDamage = monster.SelectedAttackPatternDamage;
            var playerHpBefore = state.Player.Hp;

            // Inject bypasses ApplyStrengthToAllMonsters' visibility filter so this test locks the
            // outgoing-damage multiplier independently from visibility.
            Inject(state, StatusEffectKind.Strength, monster.Id, remainingTurns: 2, amount: 100);

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            Assert.That(state.Player.Hp, Is.EqualTo(playerHpBefore - attackDamage * 2),
                "Strength (강화) +100% doubles the monster's outgoing damage.");
        }
        // --- Card "unusable" integration (속박/기절 → 흑백 + 사유 + 실제 차단) --------------------

        [Test]
        public void ImmobilizeMarksMoveCardImmobilizedAndBlocksMove()
        {
            var state = CombatStateFixture.Arena(3).WithEnemyEastAt(2).Build();
            Inject(state, StatusEffectKind.Immobilize, state.Player.Id, remainingTurns: 1, amount: 0);

            var move = state.GetCombatCards().First(c => c.Kind == CombatCardKind.Move);
            Assert.That(move.IsUsable, Is.False, "Immobilized (속박) player's move card is grayscaled (unusable).");
            Assert.That(move.Status, Is.EqualTo(CombatCardStatusText.Immobilized));

            Assert.That(state.TryPlayerMove(new HexCoord(1, 0)), Is.False, "Immobilized player cannot actually move.");
            Assert.That(state.LastFailureReason, Does.Contain("속박"));
        }

        [Test]
        public void StunMarksMoveCardStunnedAndBlocksMove()
        {
            var state = CombatStateFixture.Arena(3).WithEnemyEastAt(2).Build();
            Inject(state, StatusEffectKind.Stun, state.Player.Id, remainingTurns: 1, amount: 0);

            var move = state.GetCombatCards().First(c => c.Kind == CombatCardKind.Move);
            Assert.That(move.IsUsable, Is.False, "Stunned (기절) player's move card is grayscaled (unusable).");
            Assert.That(move.Status, Is.EqualTo(CombatCardStatusText.Stunned));

            Assert.That(state.TryPlayerMove(new HexCoord(1, 0)), Is.False, "Stunned player cannot actually move.");
            Assert.That(state.LastFailureReason, Does.Contain("기절"));
        }

        [Test]
        public void ImmobilizeKeepsActionCardUsable()
        {
            var state = CombatState.CreateDefaultDemo();
            Assert.That(state.EndAction(), Is.True); // movement -> action phase
            state.ResolveMonsterMovement(); // DEC-2026-07-03-02: 몬스터 이동 해석 후 PlayerAction 도달.
            var actionId = state.GetCombatCards().First(c => c.Kind != CombatCardKind.Move && c.IsUsable).Id;

            Inject(state, StatusEffectKind.Immobilize, state.Player.Id, remainingTurns: 1, amount: 0);

            var action = state.GetCombatCards().First(c => c.Id == actionId);
            Assert.That(action.IsUsable, Is.True, "Immobilize (속박) roots movement only — action cards stay usable.");
            Assert.That(action.Status, Is.EqualTo(CombatCardStatusText.Usable));
        }

        [Test]
        public void StunMarksActionCardStunnedAndBlocksAction()
        {
            var state = CombatState.CreateDefaultDemo();
            Assert.That(state.EndAction(), Is.True); // movement -> action phase
            state.ResolveMonsterMovement(); // DEC-2026-07-03-02: 몬스터 이동 해석 후 PlayerAction 도달.
            var actionId = state.GetCombatCards().First(c => c.Kind != CombatCardKind.Move && c.IsUsable).Id;

            Inject(state, StatusEffectKind.Stun, state.Player.Id, remainingTurns: 1, amount: 0);

            var action = state.GetCombatCards().First(c => c.Id == actionId);
            Assert.That(action.IsUsable, Is.False, "Stun (기절) grays out action cards as well.");
            Assert.That(action.Status, Is.EqualTo(CombatCardStatusText.Stunned));

            Assert.That(state.TryPlayerDefend(), Is.False, "Stunned (기절) player cannot use action cards.");
            Assert.That(state.LastFailureReason, Does.Contain("기절"));
        }

        // --- Helpers -----------------------------------------------------------------------------

        // Movement hand holds only an M05-mirror (self-mode momentum) card so TryPlayerMovementSelf is the
        // only legal movement play; the action entry just keeps the action deck non-empty.
        private static CombatState CreateSelfMoveState()
        {
            var config = TestCombatConfigs.Standard(actionBudget: 4, movementHandSize: 1, actionHandSize: 1);
            var catalog = new CardCatalogDefinition(
                "self-move-test",
                "Self-mode movement test catalog",
                new[]
                {
                    new CardCatalogEntry(
                        ApprovedCardCatalogFactory.MoveMomentumId, "Momentum", CardCategory.Movement, CardEffectType.Move,
                        1, 0, 2, CardEffectRefs.MoveDeferredMomentum, "self", durationTurns: 1,
                        gameplayType: CardGameplayType.Buff, playMode: CardPlayMode.Self, targetMode: CardTargetMode.Self,
                        buffDebuff: "Agility:2", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(
                        "D00", "Defend", CardCategory.Action, CardEffectType.Defend,
                        1, 0, 5, CardEffectRefs.DefendBlock, "self", playMode: CardPlayMode.Self,
                        targetMode: CardTargetMode.Self, gameplayType: CardGameplayType.Defend,
                        status: CardCatalogStatus.Approved)
                });
            return CombatStateFixture.Arena(3)
                .WithConfig(config)
                .WithEnemyEastAt(2)
                .WithCardCatalog(catalog)
                .Build();
        }

        private static void Inject(CombatState state, StatusEffectKind kind, string unitId, int remainingTurns, int amount)
        {
            ActiveEffectProbe.Registry(state).Add(new ActiveEffect(EffectType.Duration, kind, unitId, remainingTurns, amount, "test"));
        }

        private static void ApplyDurationStatus(CombatState state, StatusEffectKind kind, string unitId, int remainingTurns, int amount)
        {
            // AddDurationStatusEffect는 오버로드가 여러 개라 시그니처를 명시해야 Ambiguous가 나지 않는다.
            typeof(CombatState)
                .GetMethod(
                    "AddDurationStatusEffect",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null,
                    types: new[] { typeof(StatusEffectKind), typeof(string), typeof(int), typeof(int), typeof(string) },
                    modifiers: null)
                .Invoke(state, new object[] { kind, unitId, remainingTurns, amount, "test" });
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

        private static MonsterCatalogDefinition CreateConfigMonsterCatalog(CombatConfig config)
        {
            return new MonsterCatalogDefinition(
                "status-effect-config-monsters",
                "Status Effect Config Monsters",
                new[]
                {
                    new MonsterCatalogEntry(
                        CombatCatalogFactory.ThreeEyeDogMonsterId,
                        "Config Monster",
                        "test",
                        "test.config",
                        config.EnemyMaxHp,
                        config.EnemyChaseRange,
                        10,
                        attackPatterns: new[]
                        {
                            new MonsterAttackPattern("config-attack", "Config Attack", config.EnemyAttackRange, 0, config.EnemyAttackDamage)
                        })
                });
        }
    }
}

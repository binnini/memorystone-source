using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// Locks the 정화(cleanse) core (docs/new-cards-plan.md P0): what a cleanse removes, that removals announce
    /// themselves through the existing expiry event, and that the per-card 기절 exemption reaches every stun gate.
    /// No shipping card uses either yet — these are the contracts the P2 cleanse cards will build on.
    /// </summary>
    public sealed class CleanseCoreTests
    {
        private static readonly StatusEffectKind[] Cleansable =
        {
            StatusEffectKind.Immobilize,
            StatusEffectKind.Stun,
            StatusEffectKind.Poison,
            StatusEffectKind.Slow,
            StatusEffectKind.Rupture,
        };

        private static readonly StatusEffectKind[] NotCleansable =
        {
            StatusEffectKind.Agility,
            StatusEffectKind.Reflect,
            StatusEffectKind.Strength,
        };

        [Test]
        public void IsCleansableCoversDebuffsOnly()
        {
            foreach (var kind in Cleansable)
            {
                Assert.That(StatusEffectInfo.IsCleansable(kind), Is.True, $"{kind} should be cleansable.");
            }

            foreach (var kind in NotCleansable)
            {
                Assert.That(StatusEffectInfo.IsCleansable(kind), Is.False, $"{kind} is a buff and must survive a cleanse.");
            }
        }

        [Test]
        public void CleanseRemovesEveryDebuffAndKeepsBuffs()
        {
            var state = CombatState.CreateDefaultDemo();
            foreach (var kind in Cleansable.Concat(NotCleansable))
            {
                Inject(state, kind, state.Player.Id);
            }

            var removed = Cleanse(state, state.Player.Id);

            Assert.That(removed, Is.EqualTo(Cleansable.Length), "Cleanse must report exactly how many effects it stripped.");
            Assert.That(
                state.ActiveEffects.Select(effect => effect.Kind),
                Is.EquivalentTo(NotCleansable),
                "Only the buffs may remain.");
        }

        [Test]
        public void CleanseLeavesOtherUnitsAlone()
        {
            var state = CombatState.CreateDefaultDemo();
            var monsterId = state.Monsters[0].Id;
            Inject(state, StatusEffectKind.Poison, state.Player.Id);
            Inject(state, StatusEffectKind.Poison, monsterId);

            var removed = Cleanse(state, state.Player.Id);

            Assert.That(removed, Is.EqualTo(1));
            Assert.That(
                state.ActiveEffects.Select(effect => effect.TargetUnitId),
                Is.EqualTo(new[] { monsterId }),
                "A cleanse targets one unit; the monster's poison is untouched.");
        }

        [Test]
        public void CleanseWithNothingToRemoveIsANoOp()
        {
            // D8: a cleanse card stays usable on a clean player and simply removes nothing.
            var state = CombatState.CreateDefaultDemo();

            Assert.That(Cleanse(state, state.Player.Id), Is.EqualTo(0));
        }

        [Test]
        public void CleanseRaisesStatusEffectExpiredPerRemoval()
        {
            var state = CombatState.CreateDefaultDemo();
            Inject(state, StatusEffectKind.Poison, state.Player.Id);
            Inject(state, StatusEffectKind.Slow, state.Player.Id);
            Inject(state, StatusEffectKind.Agility, state.Player.Id);

            var observed = new List<EffectResultEvent>();
            void Observe(EffectResultEvent resultEvent) => observed.Add(resultEvent);
            state.EffectResolved += Observe;
            Cleanse(state, state.Player.Id, "utility.cleanse_draw");
            state.EffectResolved -= Observe;

            var expired = observed.Where(e => e.Kind == EffectKind.StatusEffectExpired).ToList();
            Assert.That(
                expired.Select(e => e.StatusKind),
                Is.EquivalentTo(new StatusEffectKind?[] { StatusEffectKind.Poison, StatusEffectKind.Slow }),
                "HUD icons clear off the shared expiry event, so a cleanse must raise one per removed effect.");
            Assert.That(
                expired.Select(e => e.SourceRef),
                Is.All.EqualTo("utility.cleanse_draw"),
                "The cleanse tags its own removals so presentation can branch on them later.");
        }

        [Test]
        public void ExpiryOnADeadMonsterAnnouncesNothing()
        {
            // A corpse has no icons or loop VFX left to clear, so a 해제/종료 label there would just float
            // over an empty tile. The effects still have to leave the list — only the announcement is dropped.
            var state = CombatState.CreateDefaultDemo();
            var monsterId = state.Monsters[0].Id;
            Inject(state, StatusEffectKind.Poison, monsterId);
            Inject(state, StatusEffectKind.Slow, monsterId);
            Kill(state, monsterId);

            var observed = new List<EffectResultEvent>();
            void Observe(EffectResultEvent resultEvent) => observed.Add(resultEvent);
            state.EffectResolved += Observe;
            var removed = Cleanse(state, monsterId);
            state.EffectResolved -= Observe;

            Assert.That(removed, Is.EqualTo(2), "Removal itself must still happen on a dead unit.");
            Assert.That(
                observed.Where(e => e.Kind == EffectKind.StatusEffectExpired),
                Is.Empty,
                "A dead target must not surface a status-expiry label.");
        }

        [Test]
        public void CleanseGrantsNoReapplicationImmunity()
        {
            // D4: a cleanse only removes. Standing on a field object must be able to re-apply next turn,
            // unlike the wear-off path which deliberately hands out a brief control-status immunity.
            var state = CombatState.CreateDefaultDemo();
            Inject(state, StatusEffectKind.Stun, state.Player.Id);

            Assert.That(Cleanse(state, state.Player.Id), Is.EqualTo(1));
            ApplyDurationStatus(state, StatusEffectKind.Stun, state.Player.Id);

            Assert.That(
                state.ActiveEffects.Any(effect => effect.Kind == StatusEffectKind.Stun),
                Is.True,
                "Stun must be re-applicable immediately after a cleanse.");
        }

        // --- 기절 exemption (usableWhileStunned) --------------------------------------------------

        [Test]
        public void StunGatesRejectANonExemptCard()
        {
            var state = StunnedActionPhaseState();
            var card = Card(usableWhileStunned: false);

            Assert.That(CanUseActionCard(state, card, out var reason), Is.False, "Execution validation must refuse.");
            Assert.That(reason, Does.Contain("기절"));
            Assert.That(IsCardUsable(state, card), Is.False, "The hand must render it unusable.");
            Assert.That(GetCardStatus(state, card), Is.EqualTo(CombatCardStatusText.Stunned));
        }

        [Test]
        public void StunGatesAcceptAnExemptCard()
        {
            var state = StunnedActionPhaseState();
            var card = Card(usableWhileStunned: true);

            // Both gates must move together: exempting only one produces a live button that rejects the play
            // (or a greyed card that would have worked).
            Assert.That(CanUseActionCard(state, card, out var reason), Is.True, reason);
            Assert.That(IsCardUsable(state, card), Is.True, "The hand must keep an exempt card live while stunned.");
            Assert.That(
                GetCardStatus(state, card),
                Is.Not.EqualTo(CombatCardStatusText.Stunned),
                "The grey 기절 label must not contradict a card that is actually playable.");
        }

        // --- helpers ------------------------------------------------------------------------------

        private static CombatState StunnedActionPhaseState()
        {
            var state = CombatState.CreateDefaultDemo();
            Assert.That(state.EndAction(), Is.True); // movement -> action phase
            state.ResolveMonsterMovement(); // DEC-2026-07-03-02: 몬스터 이동 해석 후 PlayerAction 도달.
            Inject(state, StatusEffectKind.Stun, state.Player.Id);
            return state;
        }

        private static CardDefinition Card(bool usableWhileStunned)
        {
            return new CardDefinition(
                "TEST_CLEANSE",
                "정화 테스트",
                CardCategory.Action,
                CardEffectType.Defend,
                cost: 0,
                range: 0,
                amount: 0,
                usableWhileStunned: usableWhileStunned);
        }

        private static void Inject(CombatState state, StatusEffectKind kind, string unitId)
        {
            ActiveEffectProbe.Registry(state).Add(new ActiveEffect(EffectType.Duration, kind, unitId, remainingTurns: 2, amount: 1, "test"));
        }

        private static void Kill(CombatState state, string unitId)
        {
            var monsters = (System.Collections.IEnumerable)typeof(CombatState)
                .GetField("monsters", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(state);
            foreach (var monster in monsters)
            {
                if (!string.Equals((string)monster.GetType().GetProperty("Id").GetValue(monster), unitId))
                {
                    continue;
                }

                var combatant = (CombatantState)monster.GetType().GetProperty("Combatant").GetValue(monster);
                combatant.ApplyDamage(combatant.Hp);
                Assert.That(combatant.IsDead, Is.True, "Test setup must actually kill the monster.");
                return;
            }

            Assert.Fail($"No monster with id {unitId}.");
        }

        private static void ApplyDurationStatus(CombatState state, StatusEffectKind kind, string unitId)
        {
            // AddDurationStatusEffect는 오버로드가 여러 개라 시그니처를 명시해야 Ambiguous가 나지 않는다.
            typeof(CombatState)
                .GetMethod(
                    "AddDurationStatusEffect",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null,
                    types: new[] { typeof(StatusEffectKind), typeof(string), typeof(int), typeof(int), typeof(string) },
                    modifiers: null)
                .Invoke(state, new object[] { kind, unitId, 1, 0, "test" });
        }

        private static int Cleanse(CombatState state, string unitId, string sourceRef = "")
        {
            return (int)typeof(CombatState)
                .GetMethod("CleanseStatusEffects", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(state, new object[] { unitId, sourceRef });
        }

        private static bool CanUseActionCard(CombatState state, CardDefinition card, out string reason)
        {
            var args = new object[] { card, null };
            var usable = (bool)typeof(CombatState)
                .GetMethod("CanUseActionCard", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(state, args);
            reason = (string)args[1];
            return usable;
        }

        private static bool IsCardUsable(CombatState state, CardDefinition card)
        {
            return (bool)typeof(CombatState)
                .GetMethod("IsCardUsable", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(state, new object[] { card });
        }

        private static string GetCardStatus(CombatState state, CardDefinition card)
        {
            return (string)typeof(CombatState)
                .GetMethod("GetCardStatus", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(state, new object[] { card, false });
        }
    }
}

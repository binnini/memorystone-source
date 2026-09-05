using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Runtime.Timeline;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class CombatTimelineAssemblerTests
    {
        private static MonsterActionResolutionRecord MovedVisible(string id, HexCoord from, HexCoord to)
        {
            return new MonsterActionResolutionRecord(
                id, MonsterActivityState.ActiveThreat, from, to,
                wasVisibleBefore: true, isVisibleAfter: true,
                attackedPlayer: false, affectedPlayer: false, damageToPlayer: 0);
        }

        private static MonsterActionResolutionRecord MovedHidden(string id, HexCoord from, HexCoord to)
        {
            return new MonsterActionResolutionRecord(
                id, MonsterActivityState.Dormant, from, to,
                wasVisibleBefore: false, isVisibleAfter: false,
                attackedPlayer: false, affectedPlayer: false, damageToPlayer: 0);
        }

        private static MonsterActionResolutionRecord Attacker(string id, HexCoord at, string group, int dmg)
        {
            return new MonsterActionResolutionRecord(
                id, MonsterActivityState.ActiveThreat, at, at,
                wasVisibleBefore: true, isVisibleAfter: true,
                attackedPlayer: true, affectedPlayer: true, damageToPlayer: dmg,
                actionOrder: 0, attackOrder: 0, attackPatternId: "p", attackAnimationTrigger: "trig", presentationGroupId: group);
        }

        [Test]
        public void BuildEndAction_EmitsBossPhaseTransitionAfterMonsterActionsBeforeTurnBoundary()
        {
            var records = new[] { Attacker("boss", new HexCoord(2, 0), "g", 1) };
            var effects = new List<EffectResultEvent>
            {
                new EffectResultEvent(EffectKind.Damage, targetUnitId: "player", presentationGroupId: "g"),
            };
            var transitions = new[] { new BossPhaseTransition("boss", 1, 2) };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: false, playerBefore: new HexCoord(0, 0), playerAfter: new HexCoord(0, 0),
                playerDied: false, effects, bossPhaseTransitions: transitions);
            var kinds = timeline.Events.Select(e => e.Kind).ToList();

            var transitionIdx = kinds.IndexOf(CombatTimelineEventKind.BossPhaseTransition);
            Assert.That(transitionIdx, Is.GreaterThanOrEqualTo(0), "전환 비트가 방출되어야 한다.");
            var beat = timeline.Events[transitionIdx];
            Assert.That(beat.ActorId, Is.EqualTo("boss"));
            Assert.That(beat.To, Is.EqualTo((HexCoord?)new HexCoord(2, 0)), "프레이밍 좌표 = 보스의 이번 결의 위치.");

            // 몬스터 공격 임팩트 뒤, 턴 경계 알림(OverallTurnStart) 앞.
            Assert.That(transitionIdx, Is.GreaterThan(kinds.IndexOf(CombatTimelineEventKind.AttackImpact)));
            Assert.That(transitionIdx, Is.LessThan(kinds.IndexOf(CombatTimelineEventKind.OverallTurnStart)));
        }

        [Test]
        public void BuildEndAction_EmitsScrapChainHitBeforeMonsterActionsAndPinsItsEffectsRightAfter()
        {
            // 2026-09-05 후속 #7: 사슬 명중 비트는 몬스터 공격 앞(기믹 순서), 그 사슬의 피해·속박 효과는 바로 뒤에 붙는다 —
            // 붙이지 않으면 시퀀스 끝 일괄 방출로 밀려 선이 사라진 뒤 숫자가 뜬다.
            var records = new[] { Attacker("boss", new HexCoord(2, 0), "g", 1) };
            var effects = new List<EffectResultEvent>
            {
                new EffectResultEvent(EffectKind.Damage, targetUnitId: "player", presentationGroupId: "g"),
                new EffectResultEvent(EffectKind.Damage, targetUnitId: "player", sourceRef: CombatState.BossScrapChainSourceRefs.Hit, sourceUnitId: "boss"),
                new EffectResultEvent(EffectKind.StatusEffectApplied, targetUnitId: "player", sourceRef: CombatState.BossScrapChainSourceRefs.Hit, sourceUnitId: "boss"),
            };
            var hits = new[]
            {
                new BossScrapChainHit("boss", new HexCoord(2, 0), new IReadOnlyList<HexCoord>[] { new[] { new HexCoord(1, 0), new HexCoord(0, 0) } }, hitPlayer: true, playerCoord: new HexCoord(0, 0))
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: false, playerBefore: new HexCoord(0, 0), playerAfter: new HexCoord(0, 0),
                playerDied: false, effects, bossScrapChainHits: hits);
            var events = timeline.Events;
            var chainIdx = events.ToList().FindIndex(e => e.Kind == CombatTimelineEventKind.BossScrapChainHit);
            Assert.That(chainIdx, Is.GreaterThanOrEqualTo(0));
            Assert.That(events[chainIdx].From, Is.EqualTo((HexCoord?)new HexCoord(2, 0)));
            Assert.That(events[chainIdx].To, Is.EqualTo((HexCoord?)new HexCoord(0, 0)));
            Assert.That(events[chainIdx + 1].Kind, Is.EqualTo(CombatTimelineEventKind.Effect));
            Assert.That(events[chainIdx + 1].EffectIndex, Is.EqualTo(1), "사슬 피해 효과가 선 바로 뒤에 온다.");
            Assert.That(events[chainIdx + 2].Kind, Is.EqualTo(CombatTimelineEventKind.Effect));
            Assert.That(events[chainIdx + 2].EffectIndex, Is.EqualTo(2), "속박 부여도 함께 붙는다.");
            Assert.That(chainIdx, Is.LessThan(events.ToList().FindIndex(e => e.Kind == CombatTimelineEventKind.AttackImpact)), "몬스터 공격보다 앞.");
            Assert.That(events.Count(e => e.Kind == CombatTimelineEventKind.Effect && e.EffectIndex == 1), Is.EqualTo(1), "고정한 효과는 끝의 일괄 방출에서 빠진다(두 번 뜨지 않는다).");

            var missed = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: false, playerBefore: new HexCoord(0, 0), playerAfter: new HexCoord(0, 0),
                playerDied: false, effects,
                bossScrapChainHits: new[] { new BossScrapChainHit("boss", new HexCoord(2, 0), hits[0].Strands, hitPlayer: false, playerCoord: new HexCoord(5, 5)) });
            var missedBeat = missed.Events.Single(e => e.Kind == CombatTimelineEventKind.BossScrapChainHit);
            Assert.That(missedBeat.To, Is.Null, "빗나가면 To가 비고 효과 비트가 바로 뒤에 붙지 않는다.");
            var missedIdx = missed.Events.ToList().FindIndex(e => e.Kind == CombatTimelineEventKind.BossScrapChainHit);
            Assert.That(missed.Events[missedIdx + 1].Kind == CombatTimelineEventKind.Effect && missed.Events[missedIdx + 1].EffectIndex == 1, Is.False);
        }

        [Test]
        public void BuildEndAction_OmitsBossPhaseTransitionWhenPlayerDied()
        {
            var records = new[] { Attacker("boss", new HexCoord(2, 0), "g", 9) };
            var effects = new List<EffectResultEvent>
            {
                new EffectResultEvent(EffectKind.Damage, targetUnitId: "player", presentationGroupId: "g"),
            };
            var transitions = new[] { new BossPhaseTransition("boss", 1, 2) };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: false, playerBefore: new HexCoord(0, 0), playerAfter: new HexCoord(0, 0),
                playerDied: true, effects, bossPhaseTransitions: transitions);

            Assert.That(
                timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.BossPhaseTransition), Is.False,
                "사망 연출이 시퀀스를 소유하므로 전환 비트는 생략된다.");
        }

        [Test]
        public void BuildPlayerMove_EmitsPlayerStepsThenMonsterSteps()
        {
            var path = new List<HexCoord> { new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0) };
            var records = new List<MonsterActionResolutionRecord> { MovedVisible("m1", new HexCoord(5, 5), new HexCoord(5, 6)) };

            var timeline = CombatTimelineAssembler.BuildPlayerMove(path, records);

            Assert.That(timeline.Count, Is.EqualTo(3));
            Assert.That(timeline.Events[0].Kind, Is.EqualTo(CombatTimelineEventKind.PlayerMoveStep));
            Assert.That(timeline.Events[1].Kind, Is.EqualTo(CombatTimelineEventKind.PlayerMoveStep));
            Assert.That(timeline.Events[2].Kind, Is.EqualTo(CombatTimelineEventKind.EnemyMoveStep));
            Assert.That(timeline.Events[2].ActorId, Is.EqualTo("m1"));
        }

        [Test]
        public void BuildPlayerMove_SkipsStationaryAndNonPresentableMonsters()
        {
            var path = new List<HexCoord> { new HexCoord(0, 0), new HexCoord(1, 0) };
            var records = new List<MonsterActionResolutionRecord>
            {
                MovedVisible("stationary", new HexCoord(3, 3), new HexCoord(3, 3)), // no move
                MovedHidden("hidden", new HexCoord(4, 4), new HexCoord(4, 5)),       // moved but not presentable
            };

            var timeline = CombatTimelineAssembler.BuildPlayerMove(path, records);

            Assert.That(timeline.Count, Is.EqualTo(1)); // only the single player step
            Assert.That(timeline.Events[0].Kind, Is.EqualTo(CombatTimelineEventKind.PlayerMoveStep));
        }

        [Test]
        public void BuildPlayerAttack_OrdersStartImpactEffectsThenHit()
        {
            var effects = new List<EffectResultEvent>
            {
                new EffectResultEvent(EffectKind.Damage, presentationGroupId: "g"),
                new EffectResultEvent(EffectKind.Damage, presentationGroupId: "g"),
                new EffectResultEvent(EffectKind.Damage, presentationGroupId: "g"),
            };

            var timeline = CombatTimelineAssembler.BuildPlayerAttack(
                "attack", "card.atk", new HexCoord(0, 0), "m1", new HexCoord(1, 0), new HexCoord(1, 0),
                targetHit: true, targetKnockedBack: false, targetDied: false, effects);

            // Order: start, impact, reaction flash (before numbers), the 3 effects, then hit-stop.
            Assert.That(timeline.Count, Is.EqualTo(7));
            Assert.That(timeline.Events[0].Kind, Is.EqualTo(CombatTimelineEventKind.PlayerAttackStart));
            Assert.That(timeline.Events[0].TimingKey, Is.EqualTo("card.atk"));
            Assert.That(timeline.Events[1].Kind, Is.EqualTo(CombatTimelineEventKind.AttackImpact));
            Assert.That(timeline.Events[1].GroupId, Is.EqualTo("g"));
            Assert.That(timeline.Events[2].Kind, Is.EqualTo(CombatTimelineEventKind.ActorHit));
            Assert.That(timeline.Events[3].EffectIndex, Is.EqualTo(0));
            Assert.That(timeline.Events[4].EffectIndex, Is.EqualTo(1));
            Assert.That(timeline.Events[5].EffectIndex, Is.EqualTo(2));
            Assert.That(timeline.Events[6].Kind, Is.EqualTo(CombatTimelineEventKind.HitStop));
        }

        [Test]
        public void BuildPlayerAttack_LethalEmitsActorDeathNotHit()
        {
            var timeline = CombatTimelineAssembler.BuildPlayerAttack(
                "attack", "k", new HexCoord(0, 0), "m1", new HexCoord(1, 0), new HexCoord(1, 0),
                targetHit: true, targetKnockedBack: false, targetDied: true,
                new List<EffectResultEvent>());

            // A kill ends on ActorDeath (lethal); the scheduler holds for the death delay after it. No ActorHit.
            var death = timeline.Events[timeline.Count - 1];
            Assert.That(death.Kind, Is.EqualTo(CombatTimelineEventKind.ActorDeath));
            Assert.That(death.Lethal, Is.True);
            Assert.That(death.TimingKey, Is.EqualTo("k"));
            foreach (var evt in timeline.Events)
            {
                Assert.That(evt.Kind, Is.Not.EqualTo(CombatTimelineEventKind.ActorHit));
            }
        }

        [Test]
        public void BuildPlayerAttack_KnockbackEmitsReactionThenStep()
        {
            var timeline = CombatTimelineAssembler.BuildPlayerAttack(
                "attack", "k", new HexCoord(0, 0), "m1", new HexCoord(1, 0), new HexCoord(2, 0),
                targetHit: false, targetKnockedBack: true, targetDied: false,
                new List<EffectResultEvent>());

            var reaction = timeline.Events[timeline.Count - 3];
            var step = timeline.Events[timeline.Count - 2];
            var hitstop = timeline.Events[timeline.Count - 1];
            Assert.That(reaction.Kind, Is.EqualTo(CombatTimelineEventKind.ActorKnockback));
            Assert.That(step.Kind, Is.EqualTo(CombatTimelineEventKind.EnemyKnockbackStep));
            Assert.That(step.From.Value, Is.EqualTo(new HexCoord(1, 0)));
            Assert.That(step.To.Value, Is.EqualTo(new HexCoord(2, 0)));
            Assert.That(hitstop.Kind, Is.EqualTo(CombatTimelineEventKind.HitStop));
        }

        [Test]
        public void BuildEndAction_AttackEmitsStartImpactGroupedEffectThenPlayerHit()
        {
            var records = new List<MonsterActionResolutionRecord> { Attacker("m1", new HexCoord(2, 2), "grp", 3) };
            var effects = new List<EffectResultEvent>
            {
                new EffectResultEvent(EffectKind.Damage, presentationGroupId: "other"),
                new EffectResultEvent(EffectKind.Damage, presentationGroupId: "grp"),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: false, new HexCoord(0, 0), new HexCoord(0, 0), playerDied: false, effects);

            // Order: start, impact, player hit flash (before numbers), this monster's grouped effect, hit-stop.
            Assert.That(timeline.Events[0].Kind, Is.EqualTo(CombatTimelineEventKind.EnemyAttackStart));
            Assert.That(timeline.Events[0].ActorId, Is.EqualTo("m1"));
            Assert.That(timeline.Events[1].Kind, Is.EqualTo(CombatTimelineEventKind.AttackImpact));
            Assert.That(timeline.Events[2].Kind, Is.EqualTo(CombatTimelineEventKind.PlayerHit));
            Assert.That(timeline.Events[3].Kind, Is.EqualTo(CombatTimelineEventKind.Effect));
            Assert.That(timeline.Events[3].EffectIndex, Is.EqualTo(1), "Only the effect in this monster's group is replayed.");
            Assert.That(timeline.Events[4].Kind, Is.EqualTo(CombatTimelineEventKind.HitStop));
            Assert.That(timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.MonsterPhaseCompleteGap), Is.True,
                "After the visible monster attack finishes, the scheduler holds before the next turn starts.");
            Assert.That(
                timeline.Events.Single(e => e.Kind == CombatTimelineEventKind.MonsterPhaseCompleteGap).Sequence,
                Is.LessThan(timeline.Events.Single(e => e.Kind == CombatTimelineEventKind.OverallTurnStart).Sequence));
        }

        [Test]
        public void BuildEndAction_AttackQueuesGroupedStatusBeforeDamage()
        {
            var records = new List<MonsterActionResolutionRecord> { Attacker("m1", new HexCoord(2, 2), "grp", 3) };
            var effects = new List<EffectResultEvent>
            {
                new EffectResultEvent(EffectKind.Damage, targetUnitId: "player", presentationGroupId: "grp"),
                new EffectResultEvent(EffectKind.StatusEffectApplied, targetUnitId: "player", presentationGroupId: "grp", statusKind: StatusEffectKind.Poison),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: false, new HexCoord(0, 0), new HexCoord(0, 0), playerDied: false, effects);

            var damageBeat = timeline.Events.Single(e => e.Kind == CombatTimelineEventKind.Effect && e.EffectIndex == 0);
            var statusBeat = timeline.Events.Single(e => e.Kind == CombatTimelineEventKind.Effect && e.EffectIndex == 1);
            Assert.That(statusBeat.Sequence, Is.LessThan(damageBeat.Sequence), "몬스터 공격 그룹의 플로팅 텍스트는 상태이상 -> 데미지 순서로 직렬화된다.");
            Assert.That(statusBeat.TextQueue, Is.True);
            Assert.That(damageBeat.TextQueue, Is.True);
        }

        [Test]
        public void BuildEndAction_MissedVisibleAttackQueuesMissText()
        {
            var records = new List<MonsterActionResolutionRecord>
            {
                new MonsterActionResolutionRecord(
                    "m1", MonsterActivityState.ActiveThreat, new HexCoord(2, 2), new HexCoord(2, 2),
                    wasVisibleBefore: true, isVisibleAfter: true,
                    attackedPlayer: true, affectedPlayer: false, damageToPlayer: 0,
                    attackPatternId: "p", attackAnimationTrigger: "trig", presentationGroupId: "miss",
                    missedAttack: true),
            };
            var effects = new List<EffectResultEvent>
            {
                new EffectResultEvent(EffectKind.AttackMissed, targetUnitId: "m1", presentationGroupId: "miss"),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: false, new HexCoord(0, 0), new HexCoord(0, 0), playerDied: false, effects);

            var missBeat = timeline.Events.Single(e => e.Kind == CombatTimelineEventKind.Effect && e.EffectIndex == 0);
            Assert.That(missBeat.TextQueue, Is.True);
            Assert.That(timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.PlayerHit), Is.False);
            Assert.That(timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.HitStop), Is.False);
        }

        [Test]
        public void BuildEndAction_HiddenAttackSuppressesMonsterAnimationButKeepsPlayerFeedback()
        {
            var records = new List<MonsterActionResolutionRecord>
            {
                new MonsterActionResolutionRecord(
                    "m1", MonsterActivityState.ActiveThreat, new HexCoord(2, 2), new HexCoord(2, 2),
                    wasVisibleBefore: false, isVisibleAfter: false,
                    attackedPlayer: true, affectedPlayer: true, damageToPlayer: 3,
                    attackPatternId: "p", attackAnimationTrigger: "trig", presentationGroupId: "hidden"),
            };
            var effects = new List<EffectResultEvent>
            {
                new EffectResultEvent(EffectKind.Damage, targetUnitId: "player", presentationGroupId: "hidden"),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: false, new HexCoord(0, 0), new HexCoord(0, 0), playerDied: false, effects);

            Assert.That(timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.EnemyAttackStart), Is.False);
            Assert.That(timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.AttackImpact), Is.False);
            Assert.That(timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.PlayerHit), Is.True);
            Assert.That(timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.Effect && e.EffectIndex == 0), Is.True);
            Assert.That(timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.MonsterPhaseCompleteGap), Is.False);
        }

        [Test]
        public void BuildEndAction_KnockbackVisionKeepsPreviouslyOrNewlyVisibleMonstersPresentable()
        {
            var records = new List<MonsterActionResolutionRecord>
            {
                new MonsterActionResolutionRecord(
                    "before", MonsterActivityState.ActiveThreat, new HexCoord(1, 0), new HexCoord(1, 0),
                    wasVisibleBefore: true, isVisibleAfter: false,
                    attackedPlayer: true, affectedPlayer: false, damageToPlayer: 0,
                    attackPatternId: "p", attackAnimationTrigger: "trig", presentationGroupId: "before"),
                new MonsterActionResolutionRecord(
                    "after", MonsterActivityState.ActiveThreat, new HexCoord(4, 0), new HexCoord(4, 0),
                    wasVisibleBefore: false, isVisibleAfter: true,
                    attackedPlayer: true, affectedPlayer: false, damageToPlayer: 0,
                    attackPatternId: "p", attackAnimationTrigger: "trig", presentationGroupId: "after"),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: true, new HexCoord(0, 0), new HexCoord(3, 0), playerDied: false,
                new List<EffectResultEvent>());

            Assert.That(timeline.Events.Count(e => e.Kind == CombatTimelineEventKind.EnemyAttackStart), Is.EqualTo(2));
            Assert.That(timeline.Events.Any(e => e.ActorId == "before" && e.Kind == CombatTimelineEventKind.EnemyAttackStart), Is.True);
            Assert.That(timeline.Events.Any(e => e.ActorId == "after" && e.Kind == CombatTimelineEventKind.EnemyAttackStart), Is.True);
        }

        [Test]
        public void BuildEndAction_AppendsUngroupedTurnStartStatusEffectsForSchedulerPacing()
        {
            var records = new List<MonsterActionResolutionRecord> { Attacker("m1", new HexCoord(2, 2), "attack-group", 3) };
            var effects = new List<EffectResultEvent>
            {
                new EffectResultEvent(EffectKind.Damage, targetUnitId: "player", presentationGroupId: "attack-group"),
                new EffectResultEvent(EffectKind.StatusEffectApplied, targetUnitId: "m2", statusKind: StatusEffectKind.Immobilize),
                new EffectResultEvent(EffectKind.StatusEffectExpired, targetUnitId: "player", statusKind: StatusEffectKind.Slow),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: false, new HexCoord(0, 0), new HexCoord(0, 0), playerDied: false, effects);

            var statusBeat = timeline.Events.Single(e => e.Kind == CombatTimelineEventKind.Effect && e.EffectIndex == 1);
            var expiredBeat = timeline.Events.Single(e => e.Kind == CombatTimelineEventKind.Effect && e.EffectIndex == 2);
            Assert.That(statusBeat.StatusApply, Is.True, "상태 부여 효과만 StatusApply로 태깅한다.");
            Assert.That(expiredBeat.StatusApply, Is.True, "Expiry is also a status beat and uses the status stagger.");
            Assert.That(timeline.Events.Count(e => e.Kind == CombatTimelineEventKind.Effect && e.EffectIndex == 0), Is.EqualTo(1),
                "Monster attack grouped effects must not be duplicated as unscheduled turn-start effects.");
        }

        [Test]
        public void BuildEndAction_PlayerDeath_EndsWithLethalPlayerDeathAndNoHit()
        {
            var records = new List<MonsterActionResolutionRecord> { Attacker("m1", new HexCoord(2, 2), "grp", 9) };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: false, new HexCoord(0, 0), new HexCoord(0, 0), playerDied: true,
                new List<EffectResultEvent>());

            var last = timeline.Events[timeline.Count - 1];
            Assert.That(last.Kind, Is.EqualTo(CombatTimelineEventKind.PlayerDeath));
            Assert.That(last.Lethal, Is.True);
            foreach (var evt in timeline.Events)
            {
                Assert.That(evt.Kind, Is.Not.EqualTo(CombatTimelineEventKind.PlayerHit));
            }
        }

        [Test]
        public void BuildEndAction_KnockbackPresentedOnce()
        {
            var records = new List<MonsterActionResolutionRecord>
            {
                Attacker("m1", new HexCoord(2, 2), "g1", 1),
                Attacker("m2", new HexCoord(3, 3), "g2", 1),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: true, new HexCoord(0, 0), new HexCoord(0, 1), playerDied: false,
                new List<EffectResultEvent>());

            var knockbackCount = 0;
            foreach (var evt in timeline.Events)
            {
                if (evt.Kind == CombatTimelineEventKind.PlayerKnockbackStep)
                {
                    knockbackCount++;
                }
            }

            Assert.That(knockbackCount, Is.EqualTo(1));
        }

        [Test]
        public void BuildEndAction_AttachesPlayerKnockbackToKnockbackingMonsterNotFirstHit()
        {
            var before = new HexCoord(0, 0);
            var after = new HexCoord(0, 1);
            var records = new List<MonsterActionResolutionRecord>
            {
                new MonsterActionResolutionRecord(
                    "tiger", MonsterActivityState.ActiveThreat, new HexCoord(2, 2), new HexCoord(2, 2),
                    wasVisibleBefore: true, isVisibleAfter: true,
                    attackedPlayer: true, affectedPlayer: true, damageToPlayer: 1,
                    actionOrder: 0, attackOrder: 0, attackPatternId: "fast-hit", attackAnimationTrigger: "trig",
                    presentationGroupId: "tiger-group"),
                new MonsterActionResolutionRecord(
                    "bull", MonsterActivityState.ActiveThreat, new HexCoord(3, 3), new HexCoord(3, 3),
                    wasVisibleBefore: true, isVisibleAfter: true,
                    attackedPlayer: true, affectedPlayer: true, damageToPlayer: 1,
                    actionOrder: 1, attackOrder: 1, attackPatternId: "knockback-hit", attackAnimationTrigger: "trig",
                    presentationGroupId: "bull-group",
                    knockedBackPlayer: true,
                    playerKnockbackFrom: before,
                    playerKnockbackTo: after),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: true, before, after, playerDied: false,
                new List<EffectResultEvent>());

            var tigerImpact = timeline.Events.Select((evt, index) => (evt, index))
                .Single(pair => pair.evt.Kind == CombatTimelineEventKind.AttackImpact && pair.evt.ActorId == "tiger")
                .index;
            var bullImpact = timeline.Events.Select((evt, index) => (evt, index))
                .Single(pair => pair.evt.Kind == CombatTimelineEventKind.AttackImpact && pair.evt.ActorId == "bull")
                .index;
            var knockback = timeline.Events.Select((evt, index) => (evt, index))
                .Single(pair => pair.evt.Kind == CombatTimelineEventKind.PlayerKnockbackStep)
                .index;

            Assert.That(knockback, Is.GreaterThan(bullImpact));
            Assert.That(knockback, Is.GreaterThan(tigerImpact));
            Assert.That(timeline.Events[knockback].From.HasValue, Is.True);
            Assert.That(timeline.Events[knockback].To.HasValue, Is.True);
            Assert.That(timeline.Events[knockback].From.Value, Is.EqualTo(before));
            Assert.That(timeline.Events[knockback].To.Value, Is.EqualTo(after));
        }

        [Test]
        public void BuildEndAction_MovementKnockbackTextPlaysImmediatelyAfterKnockbackStep()
        {
            var before = new HexCoord(0, 0);
            var after = new HexCoord(0, 1);
            var records = new List<MonsterActionResolutionRecord>
            {
                new MonsterActionResolutionRecord(
                    "charger", MonsterActivityState.ActiveThreat, new HexCoord(1, 0), new HexCoord(0, 0),
                    wasVisibleBefore: true, isVisibleAfter: true,
                    attackedPlayer: false, affectedPlayer: true, damageToPlayer: 0,
                    knockedBackPlayer: true,
                    playerKnockbackFrom: before,
                    playerKnockbackTo: after),
            };
            var effects = new List<EffectResultEvent>
            {
                new EffectResultEvent(
                    EffectKind.Knockback,
                    targetUnitId: "player",
                    sourceRef: "knockback",
                    sourceUnitId: "charger"),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: true, before, after, playerDied: false, effects);

            var knockbackStep = timeline.Events.Select((evt, index) => (evt, index))
                .Single(pair => pair.evt.Kind == CombatTimelineEventKind.PlayerKnockbackStep)
                .index;
            var knockbackText = timeline.Events.Select((evt, index) => (evt, index))
                .Single(pair => pair.evt.Kind == CombatTimelineEventKind.Effect && pair.evt.EffectIndex == 0)
                .index;
            var turnStart = timeline.Events.Select((evt, index) => (evt, index))
                .Single(pair => pair.evt.Kind == CombatTimelineEventKind.OverallTurnStart)
                .index;

            Assert.That(knockbackText, Is.EqualTo(knockbackStep + 1), "이동 충돌의 '밀려남' 텍스트는 실제 넉백 이동 직후 재생되어야 한다.");
            Assert.That(knockbackText, Is.LessThan(turnStart), "넉백 텍스트가 몬스터 턴 종료/다음 턴 시작까지 지연되면 안 된다.");
        }

        [Test]
        public void BuildEndAction_InsertsGapBetweenMonstersNotBeforeFirst()
        {
            var records = new List<MonsterActionResolutionRecord>
            {
                Attacker("m1", new HexCoord(2, 2), "g1", 1),
                Attacker("m2", new HexCoord(3, 3), "g2", 1),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: false, new HexCoord(0, 0), new HexCoord(0, 0), playerDied: false,
                new List<EffectResultEvent>());

            var gapCount = 0;
            foreach (var evt in timeline.Events)
            {
                if (evt.Kind == CombatTimelineEventKind.MonsterActionGap)
                {
                    gapCount++;
                }
            }

            Assert.That(gapCount, Is.EqualTo(1), "Exactly one gap between the two acting monsters.");
            Assert.That(timeline.Events[0].Kind, Is.Not.EqualTo(CombatTimelineEventKind.MonsterActionGap), "No gap before the first monster.");
        }

        [Test]
        public void BuildEndAction_UsesMonsterMovePathForPerTileSteps()
        {
            var path = new HexCoord[] { new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0) };
            var moved = new MonsterActionResolutionRecord(
                "m1", MonsterActivityState.ActiveThreat, path[0], path[path.Length - 1],
                wasVisibleBefore: true, isVisibleAfter: true,
                attackedPlayer: false, affectedPlayer: false, damageToPlayer: 0,
                movePath: path);
            var records = new List<MonsterActionResolutionRecord> { moved };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: false, new HexCoord(0, 0), new HexCoord(0, 0), playerDied: false,
                new List<EffectResultEvent>());

            var steps = new List<CombatTimelineEvent>();
            foreach (var evt in timeline.Events)
            {
                if (evt.Kind == CombatTimelineEventKind.EnemyMoveStep)
                {
                    steps.Add(evt);
                }
            }

            Assert.That(steps.Count, Is.EqualTo(2), "3-tile route -> 2 per-tile steps.");
            Assert.That(steps[0].From.Value, Is.EqualTo(path[0]));
            Assert.That(steps[0].To.Value, Is.EqualTo(path[1]));
            Assert.That(steps[1].From.Value, Is.EqualTo(path[1]));
            Assert.That(steps[1].To.Value, Is.EqualTo(path[2]));
        }

        [Test]
        public void BuildEndAction_SuppressesUnscheduledHiddenMonsterTargetStatusEffects()
        {
            var records = new List<MonsterActionResolutionRecord>
            {
                new MonsterActionResolutionRecord(
                    "hidden", MonsterActivityState.SimulatedBackground, new HexCoord(4, 0), new HexCoord(4, 0),
                    wasVisibleBefore: false, isVisibleAfter: false,
                    attackedPlayer: false, affectedPlayer: false, damageToPlayer: 0)
            };
            var effects = new List<EffectResultEvent>
            {
                new EffectResultEvent(
                    EffectKind.StatusEffectExpired,
                    targetUnitId: "hidden",
                    center: new HexCoord(4, 0),
                    targetActorKind: "monster",
                    statusKind: StatusEffectKind.Poison)
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: false, new HexCoord(0, 0), new HexCoord(0, 0), playerDied: false,
                effects);

            Assert.That(timeline.Events.Any(evt => evt.Kind == CombatTimelineEventKind.Effect), Is.False);
        }

        [Test]
        public void BuildEndAction_KeepsUnscheduledVisibleMonsterTargetStatusEffects()
        {
            var records = new List<MonsterActionResolutionRecord>
            {
                new MonsterActionResolutionRecord(
                    "visible", MonsterActivityState.ActiveThreat, new HexCoord(4, 0), new HexCoord(4, 0),
                    wasVisibleBefore: true, isVisibleAfter: false,
                    attackedPlayer: false, affectedPlayer: false, damageToPlayer: 0)
            };
            var effects = new List<EffectResultEvent>
            {
                new EffectResultEvent(
                    EffectKind.StatusEffectExpired,
                    targetUnitId: "visible",
                    center: new HexCoord(4, 0),
                    targetActorKind: "monster",
                    statusKind: StatusEffectKind.Poison)
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: false, new HexCoord(0, 0), new HexCoord(0, 0), playerDied: false,
                effects);

            Assert.That(timeline.Events.Any(evt => evt.Kind == CombatTimelineEventKind.Effect && evt.EffectIndex == 0), Is.True);
        }

        // --- 작업 8: 상태이상 부여 stagger 태깅 ---------------------------------------------------------------------------------------------------

        [Test]
        public void BuildEffectBurst_TagsStatusAppliesForDedicatedStagger()
        {
            // 같은 장판(F03)이 여러 몬스터를 동시에 속박할 때처럼 다수의 StatusEffectApplied가 한 번에 들어온다.
            var effects = new List<EffectResultEvent>
            {
                new EffectResultEvent(EffectKind.StatusEffectApplied, targetUnitId: "m1", statusKind: StatusEffectKind.Immobilize),
                new EffectResultEvent(EffectKind.StatusEffectApplied, targetUnitId: "m2", statusKind: StatusEffectKind.Immobilize),
                new EffectResultEvent(EffectKind.StatusEffectApplied, targetUnitId: "m3", statusKind: StatusEffectKind.Immobilize),
            };

            var timeline = CombatTimelineAssembler.BuildEffectBurst(effects);

            var effectBeats = timeline.Events.Where(e => e.Kind == CombatTimelineEventKind.Effect).ToList();
            Assert.That(effectBeats.Count, Is.EqualTo(3));
            foreach (var beat in effectBeats)
            {
                Assert.That(beat.StatusApply, Is.True, "상태 부여 비트는 전용 stagger를 위해 StatusApply로 태깅한다.");
            }
            // 상태 부여 비트에는 데미지 대상 반응(ActorHit/ActorDeath)을 붙이지 않는다.
            Assert.That(timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.ActorHit), Is.False);
        }

        [Test]
        public void BuildEffectBurst_DispatchesFieldDamageAreaCueWithFirstMonsterHit()
        {
            var effects = new List<EffectResultEvent>
            {
                new EffectResultEvent(EffectKind.Damage, targetUnitId: "field", center: new HexCoord(1, 0), radius: 1, sourceRef: CardEffectRefs.FieldDamage),
                new EffectResultEvent(EffectKind.Damage, targetUnitId: "m1", center: new HexCoord(1, 0), sourceRef: CardEffectRefs.FieldDamage, targetActorKind: "monster"),
                new EffectResultEvent(EffectKind.Damage, targetUnitId: "m2", center: new HexCoord(2, 0), sourceRef: CardEffectRefs.FieldDamage, targetActorKind: "monster"),
            };

            var timeline = CombatTimelineAssembler.BuildEffectBurst(effects);

            var fieldCue = timeline.Events.Single(e => e.Kind == CombatTimelineEventKind.Effect && e.EffectIndex == 0);
            var firstHitEffect = timeline.Events.Single(e => e.Kind == CombatTimelineEventKind.Effect && e.EffectIndex == 1);
            Assert.That(fieldCue.Sequence, Is.EqualTo(firstHitEffect.Sequence - 1), "폭탄 중앙 VFX는 첫 몬스터 피격 이펙트 직전에 붙어 같은 프레임에 dispatch되어야 한다.");
            Assert.That(fieldCue.NoWaitAfterEffect, Is.True, "중앙 VFX가 자체 AoE 간격을 소비하면 피격 플로팅 텍스트/VFX보다 먼저 재생된다.");
        }

        [Test]
        public void BuildPlayerAttack_TagsOnlyStatusApplyEffect_NotDamage()
        {
            // 동일 타겟 공격: 데미지 효과 + 상태 부여 효과가 섞여 있어도 상태 부여만 StatusApply로 태깅한다.
            var effects = new List<EffectResultEvent>
            {
                new EffectResultEvent(EffectKind.Damage, targetUnitId: "m1", presentationGroupId: "g"),
                new EffectResultEvent(EffectKind.StatusEffectApplied, targetUnitId: "m1", presentationGroupId: "g", statusKind: StatusEffectKind.Immobilize),
            };

            var timeline = CombatTimelineAssembler.BuildPlayerAttack(
                "attack", "card.atk", new HexCoord(0, 0), "m1", new HexCoord(1, 0), new HexCoord(1, 0),
                targetHit: true, targetKnockedBack: false, targetDied: false, effects);

            var damageBeat = timeline.Events.Single(e => e.Kind == CombatTimelineEventKind.Effect && e.EffectIndex == 0);
            var statusBeat = timeline.Events.Single(e => e.Kind == CombatTimelineEventKind.Effect && e.EffectIndex == 1);
            Assert.That(damageBeat.StatusApply, Is.False, "데미지 효과는 상태 부여 stagger를 쓰지 않는다.");
            Assert.That(statusBeat.StatusApply, Is.True, "상태 부여 효과만 StatusApply로 태깅한다.");
            Assert.That(statusBeat.Sequence, Is.LessThan(damageBeat.Sequence), "같은 프레젠테이션의 플로팅 텍스트는 상태이상 -> 데미지 순서로 직렬화된다.");
            Assert.That(statusBeat.TextQueue, Is.True);
            Assert.That(damageBeat.TextQueue, Is.True);
        }

        // --- 충돌 넉백(이동 페이즈) 슬라이드 -----------------------------------------------------------------------------------

        [Test]
        public void BuildMonsterMovementPhase_EmitsPlayerKnockbackSlideOnCollision()
        {
            var before = new HexCoord(0, 0);
            var after = new HexCoord(0, 1);
            var records = new List<MonsterActionResolutionRecord>
            {
                new MonsterActionResolutionRecord(
                    "charger", MonsterActivityState.ActiveThreat, new HexCoord(1, 0), new HexCoord(0, 0),
                    wasVisibleBefore: true, isVisibleAfter: true,
                    attackedPlayer: false, affectedPlayer: true, damageToPlayer: 0,
                    movePath: new[] { new HexCoord(1, 0), new HexCoord(0, 0) },
                    knockedBackPlayer: true, playerKnockbackFrom: before, playerKnockbackTo: after),
            };

            var timeline = CombatTimelineAssembler.BuildMonsterMovementPhase(records, playerKnockedBack: true, before, after);

            var knockback = timeline.Events.Single(e => e.Kind == CombatTimelineEventKind.PlayerKnockbackStep);
            Assert.That(knockback.From.Value, Is.EqualTo(before));
            Assert.That(knockback.To.Value, Is.EqualTo(after));
            // 몬스터가 플레이어 칸으로 밀고 들어온(이동 스텝) 뒤에 플레이어가 밀려난다.
            var enemyStep = timeline.Events.First(e => e.Kind == CombatTimelineEventKind.EnemyMoveStep);
            Assert.That(enemyStep.Sequence, Is.LessThan(knockback.Sequence));
        }

        [Test]
        public void BuildMonsterMovementPhase_NoKnockbackStepWhenPlayerNotKnockedBack()
        {
            var records = new List<MonsterActionResolutionRecord> { MovedVisible("m1", new HexCoord(1, 0), new HexCoord(2, 0)) };

            var timeline = CombatTimelineAssembler.BuildMonsterMovementPhase(records);

            Assert.That(timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.PlayerKnockbackStep), Is.False);
        }

        // --- 암시야(기습) 공격 경고 -------------------------------------------------------------------------------------------

        [Test]
        public void BuildEndAction_HiddenAttackEmitsAmbushAlertBeforeHitAndEffects()
        {
            var records = new List<MonsterActionResolutionRecord>
            {
                new MonsterActionResolutionRecord(
                    "ambusher", MonsterActivityState.ActiveThreat, new HexCoord(2, 2), new HexCoord(2, 2),
                    wasVisibleBefore: false, isVisibleAfter: false,
                    attackedPlayer: true, affectedPlayer: true, damageToPlayer: 3,
                    attackPatternId: "p", attackAnimationTrigger: "trig", presentationGroupId: "hidden"),
            };
            var effects = new List<EffectResultEvent>
            {
                new EffectResultEvent(EffectKind.Damage, targetUnitId: "player", presentationGroupId: "hidden"),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: false, new HexCoord(0, 0), new HexCoord(0, 0), playerDied: false, effects);

            var ambush = timeline.Events.Single(e => e.Kind == CombatTimelineEventKind.AmbushAlert);
            var playerHit = timeline.Events.First(e => e.Kind == CombatTimelineEventKind.PlayerHit);
            var effect = timeline.Events.First(e => e.Kind == CombatTimelineEventKind.Effect);
            Assert.That(ambush.Sequence, Is.LessThan(playerHit.Sequence), "'기습!'은 피격보다 먼저 표시된다.");
            Assert.That(ambush.Sequence, Is.LessThan(effect.Sequence), "'기습!'은 데미지 이펙트보다 먼저 표시된다.");
            // 공격자는 끝까지 숨어있으므로 모델 연출은 없다.
            Assert.That(timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.EnemyAttackStart), Is.False);
        }

        [Test]
        public void BuildEndAction_StealthAttackerInALitCellIsStillAnAmbush()
        {
            // 🔴 2026-09-01 실플레이 #1의 정체. 안개 세 플래그는 <b>셀 가시성</b>만 본다 — 은신 몬스터는
            // 시야 안에 서 있으므로 셋 다 참이 되어 「보이는 공격자」로 분류됐고, 걷기·휘두름 비트를 내는
            // 동안 마커는 뷰가 감춰서 화면엔 <b>아무도 없는데 피해만</b> 떴다. "기습!"은 반대 분기에만
            // 있으므로 함께 죽어 있었다("기습 문구가 안 보인다"는 사용자 보고와 같은 뿌리).
            var records = new List<MonsterActionResolutionRecord>
            {
                new MonsterActionResolutionRecord(
                    "shade", MonsterActivityState.ActiveThreat, new HexCoord(3, 0), new HexCoord(2, 0),
                    wasVisibleBefore: true, isVisibleAfter: true,
                    attackedPlayer: true, affectedPlayer: true, damageToPlayer: 6,
                    attackPatternId: "A038", attackAnimationTrigger: "Attack2", presentationGroupId: "shadow",
                    movePath: new[] { new HexCoord(3, 0), new HexCoord(2, 0) },
                    visibleAtAttackTime: true,
                    hiddenByStealthDuringAction: true),
            };
            var effects = new List<EffectResultEvent>
            {
                new EffectResultEvent(EffectKind.Damage, targetUnitId: "player", presentationGroupId: "shadow"),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: false, new HexCoord(0, 0), new HexCoord(0, 0), playerDied: false, effects);

            Assert.That(timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.AmbushAlert), Is.True,
                "은신 공격은 셀이 밝아도 기습이다 — 플레이어에게는 공격자가 보이지 않았다.");
            Assert.That(timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.EnemyAttackStart), Is.False,
                "숨은 채 때렸으므로 모델 휘두름은 없다.");
            Assert.That(timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.EnemyMoveStep), Is.False,
                "숨은 채 걸었으므로 이동 모션도 없다.");
            Assert.That(timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.PlayerHit), Is.True,
                "피해는 그대로 들어간다 — 감추는 것은 공격자지 결과가 아니다.");
        }

        [Test]
        public void BuildEndAction_StealthRevealSurvivesTheHiddenMonsterFilter()
        {
            // 🔴 은폐 필터(IsHiddenMonsterTargetEffect)는 숨은 몬스터를 겨눈 이벤트를 통째로 삼킨다.
            //    은신 공격의 「들킴!」은 정의상 <b>숨은 채 한 행동</b>에 딸려 있어서 정확히 여기서 죽는다 —
            //    드러나는 순간을 알리는 이벤트가 드러나지 않는 자기모순.
            var records = new List<MonsterActionResolutionRecord>
            {
                new MonsterActionResolutionRecord(
                    "shade", MonsterActivityState.ActiveThreat, new HexCoord(2, 0), new HexCoord(2, 0),
                    wasVisibleBefore: true, isVisibleAfter: true,
                    attackedPlayer: true, affectedPlayer: true, damageToPlayer: 3,
                    attackPatternId: "A038", presentationGroupId: "shadow",
                    visibleAtAttackTime: true,
                    hiddenByStealthDuringAction: true),
            };
            var effects = new List<EffectResultEvent>
            {
                new EffectResultEvent(
                    EffectKind.FogReveal,
                    targetUnitId: "shade",
                    sourceRef: CombatState.StealthAttackRevealRef,
                    targetActorKind: "monster"),
                new EffectResultEvent(
                    EffectKind.StatusEffectApplied,
                    targetUnitId: "shade",
                    sourceRef: "some.other.effect",
                    targetActorKind: "monster",
                    statusKind: StatusEffectKind.Blind),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: false, new HexCoord(0, 0), new HexCoord(0, 0), playerDied: false, effects);

            var effectBeats = timeline.Events.Where(e => e.Kind == CombatTimelineEventKind.Effect).ToList();
            Assert.That(effectBeats.Any(e => e.EffectIndex == 0), Is.True,
                "노출 알림은 은폐 필터를 지나야 한다 — 띄울지는 규칙층이 이미 정했다.");
            Assert.That(effectBeats.Any(e => e.EffectIndex == 1), Is.False,
                "특성 알림이 아닌 이벤트까지 통과시키면 은폐가 통째로 뚫린다.");
        }

        [Test]
        public void BuildEndAction_StealthRevealInTheAttackGroupPlaysBeforeThePhaseEnds()
        {
            // 🔴 2026-09-05 사용자 피드백("들킴에 들어가는 타이밍이 공격한 직후여야 함")의 정본 검증.
            //    그룹 없는 노출 이벤트는 AppendUnscheduledTurnStartEffects로 흘러가 <b>턴 경계 뭉치</b>와
            //    함께 뜬다 — 화면에서는 몬스터 페이즈가 다 끝난 뒤다. 그룹을 달면 공격 비트에 붙는다.
            //    그래서 재는 것은 "떴는가"가 아니라 <b>어느 비트보다 앞인가</b>다.
            var records = new List<MonsterActionResolutionRecord>
            {
                new MonsterActionResolutionRecord(
                    "shade", MonsterActivityState.ActiveThreat, new HexCoord(2, 0), new HexCoord(2, 0),
                    wasVisibleBefore: true, isVisibleAfter: true,
                    attackedPlayer: true, affectedPlayer: true, damageToPlayer: 3,
                    attackPatternId: "A038", presentationGroupId: "shadow",
                    visibleAtAttackTime: true),
            };
            var effects = new List<EffectResultEvent>
            {
                new EffectResultEvent(EffectKind.Damage, targetUnitId: "player", presentationGroupId: "shadow"),
                new EffectResultEvent(
                    EffectKind.FogReveal,
                    targetUnitId: "shade",
                    sourceRef: CombatState.StealthAttackRevealRef,
                    targetActorKind: "monster",
                    presentationGroupId: "shadow"),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: false, new HexCoord(0, 0), new HexCoord(0, 0), playerDied: false, effects);

            var revealAt = timeline.Events.ToList().FindIndex(
                e => e.Kind == CombatTimelineEventKind.Effect && e.EffectIndex == 1);
            var impactAt = timeline.Events.ToList().FindIndex(e => e.Kind == CombatTimelineEventKind.AttackImpact);
            var phaseEndAt = timeline.Events.ToList().FindIndex(
                e => e.Kind == CombatTimelineEventKind.MonsterPhaseCompleteGap);

            Assert.That(revealAt, Is.GreaterThan(impactAt),
                "「들킴!」은 공격이 닿은 <b>뒤</b>다 — 예고 시점이 아니다.");
            Assert.That(phaseEndAt, Is.GreaterThan(revealAt),
                "몬스터 페이즈가 끝나기 전에 떠야 '공격한 직후'로 읽힌다.");
        }

        [Test]
        public void BuildEndAction_AmbushAlertEmittedOncePerPhase()
        {
            var records = new List<MonsterActionResolutionRecord>
            {
                new MonsterActionResolutionRecord(
                    "hidden1", MonsterActivityState.ActiveThreat, new HexCoord(2, 2), new HexCoord(2, 2),
                    wasVisibleBefore: false, isVisibleAfter: false,
                    attackedPlayer: true, affectedPlayer: true, damageToPlayer: 1, presentationGroupId: "h1"),
                new MonsterActionResolutionRecord(
                    "hidden2", MonsterActivityState.ActiveThreat, new HexCoord(3, 3), new HexCoord(3, 3),
                    wasVisibleBefore: false, isVisibleAfter: false,
                    attackedPlayer: true, affectedPlayer: true, damageToPlayer: 1, presentationGroupId: "h2"),
            };
            var effects = new List<EffectResultEvent>
            {
                new EffectResultEvent(EffectKind.Damage, targetUnitId: "player", presentationGroupId: "h1"),
                new EffectResultEvent(EffectKind.Damage, targetUnitId: "player", presentationGroupId: "h2"),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: false, new HexCoord(0, 0), new HexCoord(0, 0), playerDied: false, effects);

            Assert.That(timeline.Events.Count(e => e.Kind == CombatTimelineEventKind.AmbushAlert), Is.EqualTo(1),
                "여러 기습 공격이 있어도 '기습!'은 페이즈당 한 번만 띄운다.");
        }

        [Test]
        public void BuildEndAction_KnockbackVisionChangeIsNotAmbush()
        {
            // 공격 시 보이던 몬스터가 플레이어 넉백으로 시야 밖으로 나간 경우(WasVisibleBefore=true, IsVisibleAfter=false)는
            // 기습으로 간주하지 않는다 → AmbushAlert 없음, 가시 공격으로 연출된다.
            var records = new List<MonsterActionResolutionRecord>
            {
                new MonsterActionResolutionRecord(
                    "wasVisible", MonsterActivityState.ActiveThreat, new HexCoord(1, 0), new HexCoord(1, 0),
                    wasVisibleBefore: true, isVisibleAfter: false,
                    attackedPlayer: true, affectedPlayer: true, damageToPlayer: 1,
                    attackPatternId: "p", attackAnimationTrigger: "trig", presentationGroupId: "kb",
                    knockedBackPlayer: true, playerKnockbackFrom: new HexCoord(0, 0), playerKnockbackTo: new HexCoord(0, 2)),
            };
            var effects = new List<EffectResultEvent>
            {
                new EffectResultEvent(EffectKind.Damage, targetUnitId: "player", presentationGroupId: "kb"),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: true, new HexCoord(0, 0), new HexCoord(0, 2), playerDied: false, effects);

            Assert.That(timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.AmbushAlert), Is.False,
                "넉백으로 시야가 바뀐 경우는 기습이 아니다.");
            Assert.That(timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.EnemyAttackStart), Is.True,
                "공격 시 보이던 몬스터는 가시 공격(모델 연출)으로 표현된다.");
        }

        [Test]
        public void BuildEndAction_VisibleAtAttackTimeShownToCompletionNotAmbushEvenWhenKnockedOutOfView()
        {
            // 이동해 시야에 들어와 공격(VisibleAtAttackTime=true)했지만, 넉백으로 이동 전(WasVisibleBefore=false)·
            // 종료 시점(IsVisibleAfter=false) 모두 안 보이는 몬스터. '기존 위치에서 보이던' 공격이므로 기습이 아니라
            // 끝까지 가시 공격으로 연출되어야 한다.
            var records = new List<MonsterActionResolutionRecord>
            {
                new MonsterActionResolutionRecord(
                    "rusher", MonsterActivityState.ActiveThreat, new HexCoord(5, 0), new HexCoord(1, 0),
                    wasVisibleBefore: false, isVisibleAfter: false,
                    attackedPlayer: true, affectedPlayer: true, damageToPlayer: 2,
                    attackPatternId: "p", attackAnimationTrigger: "trig", presentationGroupId: "rush",
                    knockedBackPlayer: true, playerKnockbackFrom: new HexCoord(0, 0), playerKnockbackTo: new HexCoord(0, 3),
                    visibleAtAttackTime: true),
            };
            var effects = new List<EffectResultEvent>
            {
                new EffectResultEvent(EffectKind.Damage, targetUnitId: "player", presentationGroupId: "rush"),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: true, new HexCoord(0, 0), new HexCoord(0, 3), playerDied: false, effects);

            Assert.That(timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.AmbushAlert), Is.False,
                "공격 시점에 보이던 몬스터는 기습이 아니다.");
            Assert.That(
                timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.EnemyAttackStart && e.ActorId == "rusher"), Is.True,
                "공격 시점에 보이던 몬스터는 넉백으로 시야 밖이 되어도 끝까지 가시 공격으로 연출된다.");
        }

        [Test]
        public void BuildEndAction_HiddenSelfBuffIsNotAmbush()
        {
            // 2026-08-20 #2의 정체: 숨은 몬스터의 자기부여 패턴(A029/A030 불가살·A031 돼지)은
            // AttackedPlayer=true로 기록되지만 플레이어를 건드리지 않는다 — 화면엔 아무 일도 없는데
            // "기습!"만 떠서 원인불명 출력으로 보고됐다. SelfTargetedAttack 가드가 이를 막는다.
            var records = new List<MonsterActionResolutionRecord>
            {
                new MonsterActionResolutionRecord(
                    "buffer", MonsterActivityState.ActiveThreat, new HexCoord(2, 2), new HexCoord(2, 2),
                    wasVisibleBefore: false, isVisibleAfter: false,
                    attackedPlayer: true, affectedPlayer: false, damageToPlayer: 0,
                    attackPatternId: "A031", attackAnimationTrigger: "Attack2", presentationGroupId: "buff",
                    selfTargetedAttack: true),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: false, new HexCoord(0, 0), new HexCoord(0, 0), playerDied: false,
                new List<EffectResultEvent>());

            Assert.That(timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.AmbushAlert), Is.False,
                "숨은 자기부여는 플레이어를 건드리지 않으므로 기습이 아니다.");
            Assert.That(timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.EnemyAttackStart), Is.False,
                "끝까지 숨은 몬스터는 모델 연출도 없다.");
        }

        [Test]
        public void BuildEndAction_VisibleSelfBuffStillPlaysItsAttackBeats()
        {
            // 자기부여 가드가 가시 연출까지 삼키면 안 된다 — 보이는 몬스터의 버프 모션(AttackedPlayer
            // 경유 EnemyAttackStart/AttackImpact)은 그대로 살아야 한다.
            var records = new List<MonsterActionResolutionRecord>
            {
                new MonsterActionResolutionRecord(
                    "buffer", MonsterActivityState.ActiveThreat, new HexCoord(2, 2), new HexCoord(2, 2),
                    wasVisibleBefore: true, isVisibleAfter: true,
                    attackedPlayer: true, affectedPlayer: false, damageToPlayer: 0,
                    attackPatternId: "A031", attackAnimationTrigger: "Attack2", presentationGroupId: "buff",
                    selfTargetedAttack: true),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: false, new HexCoord(0, 0), new HexCoord(0, 0), playerDied: false,
                new List<EffectResultEvent>());

            Assert.That(timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.EnemyAttackStart), Is.True,
                "보이는 자기부여는 공격 비트(버프 모션)를 그대로 재생한다.");
            Assert.That(timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.AmbushAlert), Is.False);
        }

        [Test]
        public void BuildEndAction_HiddenWhiffIsNotAmbush()
        {
            // 조건 매트릭스의 공백이었던 칸: 숨은 빗맞음(§28 W6). MissedAttack 가드가 이미 막고
            // 있었지만 테스트가 없어 회귀에 무방비였다 — 자기부여 가드와 같은 자리라 함께 잠근다.
            var records = new List<MonsterActionResolutionRecord>
            {
                new MonsterActionResolutionRecord(
                    "whiffer", MonsterActivityState.ActiveThreat, new HexCoord(2, 2), new HexCoord(2, 2),
                    wasVisibleBefore: false, isVisibleAfter: false,
                    attackedPlayer: true, affectedPlayer: false, damageToPlayer: 0,
                    attackPatternId: "p", attackAnimationTrigger: "trig", presentationGroupId: "whiff",
                    missedAttack: true),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: false, new HexCoord(0, 0), new HexCoord(0, 0), playerDied: false,
                new List<EffectResultEvent>());

            Assert.That(timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.AmbushAlert), Is.False,
                "아무 일도 없었던 숨은 빗맞음에 '기습!'이 뜨면 거짓말이다.");
        }

        [Test]
        public void BuildEndAction_HiddenWhiffPresentsNoEffects()
        {
            // 2026-08-20 #14: 숨은 빗맞음은 통째로 침묵한다 — 예전에는 안개 속에서 「빗맞음」 텍스트·
            // 타일 플래시·공격 SFX가 새어 숨은 몬스터 위치를 누설했고, 빗맞음에 공격 VFX가 실리면서
            // 누설이 더 커질 자리였다(§28 W6: 아무 일도 없었다).
            var records = new List<MonsterActionResolutionRecord>
            {
                new MonsterActionResolutionRecord(
                    "whiffer", MonsterActivityState.ActiveThreat, new HexCoord(2, 2), new HexCoord(2, 2),
                    wasVisibleBefore: false, isVisibleAfter: false,
                    attackedPlayer: true, affectedPlayer: false, damageToPlayer: 0,
                    attackPatternId: "p", attackAnimationTrigger: "trig", presentationGroupId: "whiff",
                    missedAttack: true),
            };
            var effects = new List<EffectResultEvent>
            {
                new EffectResultEvent(EffectKind.AttackMissed, targetUnitId: "whiffer", presentationGroupId: "whiff"),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: false, new HexCoord(0, 0), new HexCoord(0, 0), playerDied: false, effects);

            Assert.That(timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.Effect), Is.False,
                "숨은 빗맞음의 이펙트 그룹은 재생되지 않는다.");
        }

        [Test]
        public void BuildEndAction_WhiffAfterMovingOutOfSightStillPresents()
        {
            // 사용자 확정(2026-08-20 #14): 그 턴 처음에 보였다가 이동으로 시야 밖이 된 몬스터의 빗맞음은
            // 보여야 하고, 처음부터 한 번도 안 보였던 몬스터만 완전 침묵이다. WasVisibleBefore 한 축만
            // 참이어도 가시 빗맞음으로 끝까지 연출된다는 계약을 잠근다.
            var records = new List<MonsterActionResolutionRecord>
            {
                new MonsterActionResolutionRecord(
                    "walker", MonsterActivityState.ActiveThreat, new HexCoord(1, 0), new HexCoord(3, 0),
                    wasVisibleBefore: true, isVisibleAfter: false,
                    attackedPlayer: true, affectedPlayer: false, damageToPlayer: 0,
                    attackPatternId: "p", attackAnimationTrigger: "trig", presentationGroupId: "walk-whiff",
                    missedAttack: true, visibleAtAttackTime: false),
            };
            var effects = new List<EffectResultEvent>
            {
                new EffectResultEvent(EffectKind.AttackMissed, targetUnitId: "walker", presentationGroupId: "walk-whiff"),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: false, new HexCoord(0, 0), new HexCoord(0, 0), playerDied: false, effects);

            Assert.That(timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.Effect), Is.True,
                "이동으로 시야 밖이 된 빗맞음은 이펙트 그룹(공격 VFX·빗맞음)을 재생한다.");
            Assert.That(timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.EnemyAttackStart), Is.True);
            Assert.That(timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.AmbushAlert), Is.False);
        }

        [Test]
        public void BuildEndAction_AttackFacesTheCommittedAimNotTheLivePlayerCoord()
        {
            // 사용자 요구(2026-08-20 #7): 예고 후 플레이어가 비켜서도 몬스터는 <b>예고한 방향</b>을 향한
            // 채로 공격 애니를 재생한다. 규칙층은 이미 커밋 방향으로 형상 footprint를 굳히므로
            // (GetCommittedMonsterAttackFootprint), 연출만 실좌표를 보면 몸과 판정이 갈라진다.
            var aim = new HexCoord(5, 0);
            var records = new List<MonsterActionResolutionRecord>
            {
                new MonsterActionResolutionRecord(
                    "aimer", MonsterActivityState.ActiveThreat, new HexCoord(0, 0), new HexCoord(0, 0),
                    wasVisibleBefore: true, isVisibleAfter: true,
                    attackedPlayer: true, affectedPlayer: false, damageToPlayer: 0,
                    attackPatternId: "p", attackAnimationTrigger: "trig", presentationGroupId: "aim",
                    aimCoord: aim),
            };

            // 플레이어는 예고 후 (0,5)로 비켜섰다.
            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: false, new HexCoord(0, 5), new HexCoord(0, 5), playerDied: false,
                new List<EffectResultEvent>());

            var attackStart = timeline.Events.First(e => e.Kind == CombatTimelineEventKind.EnemyAttackStart);
            Assert.That(attackStart.To, Is.EqualTo(aim), "공격 애니의 조준 방향은 커밋 시점의 칸이어야 한다.");
        }

        [Test]
        public void BuildEndAction_AttackFallsBackToLivePlayerCoordWhenNoAimWasCommitted()
        {
            // 커밋 정보가 없는 경로(디버그 강제 공격·레거시 호출)는 종전대로 실좌표를 본다 —
            // 조준 칸이 null일 때 방향이 통째로 사라지면 애니가 엉뚱한 쪽을 본다.
            var playerCoord = new HexCoord(0, 5);
            var records = new List<MonsterActionResolutionRecord>
            {
                new MonsterActionResolutionRecord(
                    "legacy", MonsterActivityState.ActiveThreat, new HexCoord(0, 0), new HexCoord(0, 0),
                    wasVisibleBefore: true, isVisibleAfter: true,
                    attackedPlayer: true, affectedPlayer: false, damageToPlayer: 0,
                    attackPatternId: "p", attackAnimationTrigger: "trig", presentationGroupId: "legacy"),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: false, playerCoord, playerCoord, playerDied: false,
                new List<EffectResultEvent>());

            var attackStart = timeline.Events.First(e => e.Kind == CombatTimelineEventKind.EnemyAttackStart);
            Assert.That(attackStart.To, Is.EqualTo(playerCoord));
        }

        [Test]
        public void BuildEndAction_VisibleWhiffStillPresentsItsEffectGroup()
        {
            // 숨은 빗맞음 침묵 가드가 가시 빗맞음(공격 VFX + 「빗맞음」)까지 삼키면 안 된다(#14 회귀 잠금).
            var records = new List<MonsterActionResolutionRecord>
            {
                new MonsterActionResolutionRecord(
                    "whiffer", MonsterActivityState.ActiveThreat, new HexCoord(2, 2), new HexCoord(2, 2),
                    wasVisibleBefore: true, isVisibleAfter: true,
                    attackedPlayer: true, affectedPlayer: false, damageToPlayer: 0,
                    attackPatternId: "p", attackAnimationTrigger: "trig", presentationGroupId: "whiff",
                    missedAttack: true),
            };
            var effects = new List<EffectResultEvent>
            {
                new EffectResultEvent(EffectKind.AttackMissed, targetUnitId: "whiffer", presentationGroupId: "whiff"),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: false, new HexCoord(0, 0), new HexCoord(0, 0), playerDied: false, effects);

            Assert.That(timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.Effect), Is.True,
                "가시 빗맞음은 이펙트 그룹(공격 VFX·빗맞음 텍스트)을 그대로 재생한다.");
            Assert.That(timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.EnemyAttackStart), Is.True);
        }

        // --- Action camera focus, P2 (docs/monster-action-camera-focus-plan.md §5 P2) ---
        //
        // These pin the *emission* contract only. Whether the camera actually moves is decided later by the
        // sink's frustum test, so a spotlight beat here means "a framing decision belongs at this seam", not
        // "the camera pans". The two properties that matter most: emission is opt-in (so P2 alone changes no
        // shipped timeline), and a spotlight that stands in for a MonsterActionGap says so via FallbackGap —
        // without that flag an already-framed first monster would gain a pause it never had.

        private static List<CombatTimelineEvent> Spotlights(CombatTimeline timeline)
            => timeline.Events.Where(e => e.Kind == CombatTimelineEventKind.ActorSpotlight).ToList();

        [Test]
        public void ActionSpotlight_IsNotEmittedUnlessRequested()
        {
            var records = new List<MonsterActionResolutionRecord>
            {
                MovedVisible("far", new HexCoord(20, 0), new HexCoord(21, 0)),
                Attacker("near", new HexCoord(1, 0), "g", 3),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: false, new HexCoord(0, 0), new HexCoord(0, 0), playerDied: false,
                new List<EffectResultEvent>());

            // The default keeps every shipped call site byte-identical, which is what lets P2 land before the
            // Unity-side enforcement exists.
            Assert.That(Spotlights(timeline), Is.Empty);
            Assert.That(
                timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.MonsterActionGap), Is.True,
                "기존 텀은 그대로 남아 있어야 한다");
        }

        [Test]
        public void ActionSpotlight_TakesTheGapSlotAndFlagsTheGapItReplaced()
        {
            var records = new List<MonsterActionResolutionRecord>
            {
                Attacker("first", new HexCoord(20, 0), "g1", 3),
                Attacker("second", new HexCoord(0, 20), "g2", 3),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: false, new HexCoord(0, 0), new HexCoord(0, 0), playerDied: false,
                new List<EffectResultEvent>(), emitActionSpotlights: true);

            var spotlights = Spotlights(timeline);
            Assert.That(spotlights.Count, Is.EqualTo(2), "두 몬스터가 멀리 떨어져 있으므로 각각 프레이밍이 필요하다");
            Assert.That(spotlights[0].ActorId, Is.EqualTo("first"));
            Assert.That(spotlights[0].To, Is.EqualTo(new HexCoord(20, 0)), "프레이밍 좌표는 To에 실린다");

            // 첫 몬스터 자리에는 원래 텀이 없다 → 폴백하면 없던 0.7초가 생긴다.
            Assert.That(spotlights[0].FallbackGap, Is.False);
            // 두 번째부터는 텀 자리를 대신하므로, 화면 안이면 그 텀을 그대로 흘려야 한다.
            Assert.That(spotlights[1].FallbackGap, Is.True);
            Assert.That(
                timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.MonsterActionGap), Is.False,
                "스포트라이트가 텀 자리를 차지했으므로 텀 비트는 중복 방출되지 않는다");
        }

        [Test]
        public void ActionSpotlight_DoesNotCoalesceAtAssemblyTimeEvenForAdjacentActors()
        {
            var records = new List<MonsterActionResolutionRecord>
            {
                Attacker("first", new HexCoord(20, 0), "g1", 3),
                Attacker("neighbour", new HexCoord(21, 0), "g2", 3),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: false, new HexCoord(0, 0), new HexCoord(0, 0), playerDied: false,
                new List<EffectResultEvent>(), emitActionSpotlights: true);

            // 반경 병합은 sink이 한다(§10.6). 어셈블러가 "직전 스포트라이트 근처"라고 지우면, 그 직전 것이 화면
            // 안이라 거부됐을 때 카메라가 가지도 않은 좌표를 기준으로 뒤의 화면 밖 이벤트까지 조용히 사라진다.
            Assert.That(
                Spotlights(timeline).Count, Is.EqualTo(2),
                "인접해도(거리 1) 후보는 둘 다 방출된다 — 실제 프레이밍을 아는 sink만 반경 병합을 판단할 수 있다");

            // 스포트라이트가 텀 자리를 차지하므로 텀 비트가 따로 붙지는 않는다. 카메라가 실제로 안 움직였다면
            // FallbackGap을 보고 sink이 그 텀을 되돌려준다.
            Assert.That(
                timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.MonsterActionGap), Is.False);
            Assert.That(Spotlights(timeline)[1].FallbackGap, Is.True);
        }

        [Test]
        public void ActionSpotlight_IsNotEmittedForHiddenMonsters()
        {
            var records = new List<MonsterActionResolutionRecord>
            {
                MovedHidden("lurker", new HexCoord(20, 0), new HexCoord(21, 0)),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: false, new HexCoord(0, 0), new HexCoord(0, 0), playerDied: false,
                new List<EffectResultEvent>(), emitActionSpotlights: true);

            // 암시야 몬스터는 애초에 연출되지 않는다 — 카메라가 그쪽을 보면 빈 땅을 비추며 위치를 누설한다.
            Assert.That(Spotlights(timeline), Is.Empty);
        }

        [Test]
        public void ActionSpotlight_IsSuppressedOnThePlayerDeathPath()
        {
            var records = new List<MonsterActionResolutionRecord>
            {
                Attacker("killer", new HexCoord(20, 0), "g", 99),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: false, new HexCoord(0, 0), new HexCoord(0, 0), playerDied: true,
                new List<EffectResultEvent>(), emitActionSpotlights: true);

            // 죽음 줌이 카메라를 소유한다. 두 연출이 같은 카메라를 두고 다투면 안 된다.
            Assert.That(Spotlights(timeline), Is.Empty);
            Assert.That(timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.PlayerDeath), Is.True);
        }

        [Test]
        public void ActionSpotlight_FramesDistantFieldTickTargetsAndTurnBoundaryEffects()
        {
            var fieldCoord = new HexCoord(12, 0);
            var effects = new List<EffectResultEvent>
            {
                // 필드 장판 틱: 플레이어가 걸어서 멀어진 뒤에도 계속 발동한다(§2.3) — 이 기능의 대표 사례.
                new EffectResultEvent(
                    EffectKind.Damage, targetUnitId: "m1", center: fieldCoord, presentationGroupId: "field"),
                // 턴 경계 상태이상 만료: 공격 그룹에 묶이지 않은 원거리 이벤트.
                new EffectResultEvent(
                    EffectKind.StatusEffectExpired, targetUnitId: "m2", center: new HexCoord(0, 14),
                    presentationGroupId: "expiry"),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                new List<MonsterActionResolutionRecord>(),
                playerKnockedBack: false, new HexCoord(0, 0), new HexCoord(0, 0), playerDied: false,
                effects, emitActionSpotlights: true);

            var spotlights = Spotlights(timeline);
            Assert.That(spotlights.Count, Is.EqualTo(2));
            Assert.That(spotlights[0].To, Is.EqualTo(fieldCoord));

            // 이 구간에는 원래 텀이 없다 — 폴백하면 조용히 느려진다.
            Assert.That(spotlights.All(s => !s.FallbackGap), Is.True);
        }

        [Test]
        public void ActionSpotlight_SkipsEffectsWithNoTileOfTheirOwn()
        {
            var effects = new List<EffectResultEvent>
            {
                // 좌표가 없는 이펙트(플레이어 스탯/덱 계열)는 맵 위 사건이 아니다. 플레이어 칸으로 폴백하면
                // 카메라가 이미 있는 곳으로 "이동"하는 무의미한 컷이 생긴다.
                new EffectResultEvent(EffectKind.Damage, targetUnitId: "player", presentationGroupId: "stat"),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                new List<MonsterActionResolutionRecord>(),
                playerKnockedBack: false, new HexCoord(0, 0), new HexCoord(0, 0), playerDied: false,
                effects, emitActionSpotlights: true);

            Assert.That(Spotlights(timeline), Is.Empty);
        }

        [Test]
        public void ActionSpotlight_ChainsAcrossTheMovementPhaseWithoutClaimingAGap()
        {
            var records = new List<MonsterActionResolutionRecord>
            {
                MovedVisible("m1", new HexCoord(20, 0), new HexCoord(24, 0)),
                MovedVisible("m2", new HexCoord(0, 20), new HexCoord(0, 21)),
            };

            var timeline = CombatTimelineAssembler.BuildMonsterMovementPhase(
                records, emitActionSpotlights: true);

            var spotlights = Spotlights(timeline);
            Assert.That(spotlights.Count, Is.EqualTo(2));
            Assert.That(
                spotlights[0].To, Is.EqualTo(new HexCoord(22, 0)),
                "프레이밍 좌표는 목적지가 아니라 경로 중점이다 — 몬스터는 플레이어 쪽으로 오므로 "
                + "목적지만 보면 이동의 화면 밖 구간을 통째로 놓친다(§10.6)");

            // 이동 페이즈 타임라인에는 MonsterActionGap이 애초에 없다(§2.5) — 대신할 텀이 없으므로 폴백도 없다.
            Assert.That(spotlights.All(s => !s.FallbackGap), Is.True);
            Assert.That(timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.MonsterActionGap), Is.False);
        }

        [Test]
        public void ActionSpotlight_CountsTheContentBeatsItCoversButNotTheBoundaryBeats()
        {
            var fieldCoord = new HexCoord(12, 0);
            var effects = new List<EffectResultEvent>
            {
                // One field ticking three monsters: a long event that deserves an unhurried arrival.
                new EffectResultEvent(EffectKind.Damage, targetUnitId: "m1", center: fieldCoord, presentationGroupId: "f"),
                new EffectResultEvent(EffectKind.Damage, targetUnitId: "m2", center: fieldCoord, presentationGroupId: "f"),
                new EffectResultEvent(EffectKind.Damage, targetUnitId: "m3", center: fieldCoord, presentationGroupId: "f"),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                new List<MonsterActionResolutionRecord>(),
                playerKnockedBack: false, new HexCoord(0, 0), new HexCoord(0, 0), playerDied: false,
                effects, emitActionSpotlights: true);

            var spotlights = Spotlights(timeline);
            Assert.That(spotlights.Count, Is.EqualTo(1), "one field, one framing");

            // 3 targets x (ActorHit + Effect) = 6 content beats. OverallTurnStart / PlayerTurnStart sit inside
            // the same stretch and must NOT count — they are the pause between events, not the event.
            Assert.That(spotlights[0].CoveredBeats, Is.EqualTo(6));
            Assert.That(
                timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.PlayerTurnStart), Is.True,
                "fixture precondition: a boundary beat is present in the covered stretch");
        }

        [Test]
        public void PlayerTurnStart_IsPrecededByTheCameraReleaseBeat()
        {
            // 플레이어가 본 증상: 마지막 필드 오브젝트에서 카메라가 아직 돌아오지 않았는데 다음 턴의 카드
            // 뽑기 애니메이션이 재생된다(§10.13). 원인은 카메라 정리가 스케줄러 바깥에 있어서 턴 시작 비트가
            // 그것보다 먼저 재생된 것 — 즉 순서가 타임라인에 없었다. 여기서 순서를 고정한다.
            var fieldCoord = new HexCoord(12, 0);
            var effects = new List<EffectResultEvent>
            {
                new EffectResultEvent(
                    EffectKind.Damage, targetUnitId: "m1", center: fieldCoord, sourceRef: "field.damage"),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                new List<MonsterActionResolutionRecord>(),
                playerKnockedBack: false, new HexCoord(0, 0), new HexCoord(0, 0), playerDied: false,
                effects, emitActionSpotlights: true, includeMonsterMovement: false);

            var kinds = timeline.Events.Select(e => e.Kind).ToList();
            var turnStart = kinds.IndexOf(CombatTimelineEventKind.PlayerTurnStart);
            Assert.That(turnStart, Is.GreaterThanOrEqualTo(0), "fixture precondition: the turn does start");
            Assert.That(
                kinds[turnStart - 1], Is.EqualTo(CombatTimelineEventKind.ActionFocusRelease),
                "카메라 복귀는 턴 시작 '직전'이어야 한다 — 사이에 다른 비트가 끼면 그 비트가 카메라 밖에서 재생된다");
        }

        [Test]
        public void PlayerTurnStart_HasNoReleaseBeatWhenThePlayerDied()
        {
            // 사망하면 턴이 시작되지 않고 사망 연출이 카메라를 따로 가져간다. 정리할 턴 경계가 없으므로
            // 경계 비트도 없어야 한다 — 있으면 사망 줌 앞에 의미 없는 복귀 대기가 끼어든다.
            var timeline = CombatTimelineAssembler.BuildEndAction(
                new List<MonsterActionResolutionRecord>(),
                playerKnockedBack: false, new HexCoord(0, 0), new HexCoord(0, 0), playerDied: true,
                new List<EffectResultEvent>(), emitActionSpotlights: true, includeMonsterMovement: false);

            Assert.That(
                timeline.Events.Any(e => e.Kind == CombatTimelineEventKind.ActionFocusRelease), Is.False);
        }

        [Test]
        public void ActionSpotlight_FramesFieldTickDamageThroughItsTargetMonster()
        {
            // The pairing this asserts lives half in the rules layer: EffectRuntime.ApplyDamage must pass the
            // ticked tile as the centre for "field.damage". While it did not, the headline case of this whole
            // feature — a field ticking monsters the player walked away from — emitted no spotlight at all and
            // the camera never went, and the dev metrics hid it by resolving the coordinate a different way
            // (plan §10.8). EffectRuntimeFieldTickCoordinateTests holds up the other half.
            var monsterCoord = new HexCoord(14, 0);
            var records = new List<MonsterActionResolutionRecord>();
            var effects = new List<EffectResultEvent>
            {
                new EffectResultEvent(
                    EffectKind.Damage, targetUnitId: "m1", center: monsterCoord, sourceRef: "field.damage"),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: false, new HexCoord(0, 0), new HexCoord(0, 0), playerDied: false,
                effects, emitActionSpotlights: true, includeMonsterMovement: false);

            var spotlights = Spotlights(timeline);
            Assert.That(spotlights.Count, Is.EqualTo(1), "좌표 없는 필드 틱도 대상 몬스터로 프레이밍된다");
            Assert.That(spotlights[0].To, Is.EqualTo(monsterCoord));
        }

        [Test]
        public void ActionSpotlight_SameTileEffectsStayOneFramingSoTheirLengthIsNotSplit()
        {
            var fieldCoord = new HexCoord(12, 0);
            var effects = new List<EffectResultEvent>
            {
                new EffectResultEvent(EffectKind.Damage, targetUnitId: "m1", center: fieldCoord, presentationGroupId: "f"),
                new EffectResultEvent(EffectKind.Damage, targetUnitId: "m2", center: fieldCoord, presentationGroupId: "f"),
                new EffectResultEvent(EffectKind.Damage, targetUnitId: "m3", center: fieldCoord, presentationGroupId: "f"),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                new List<MonsterActionResolutionRecord>(),
                playerKnockedBack: false, new HexCoord(0, 0), new HexCoord(0, 0), playerDied: false,
                effects, emitActionSpotlights: true);

            var spotlights = Spotlights(timeline);

            // 같은 칸의 세 타격은 하나의 사건이다. 비트를 셋으로 쪼개면 커버리지도 셋으로 쪼개져서, 그 사건보다
            // 오래 버티라고 만든 체류가 사건의 3분의 1 길이로 계산된다.
            Assert.That(spotlights.Count, Is.EqualTo(1));
            Assert.That(spotlights[0].CoveredBeats, Is.EqualTo(6), "세 타격의 ActorHit+Effect 6비트를 전부 덮는다");
        }

        [Test]
        public void ActionSpotlight_CoverageStopsAtTheNextSpotlight()
        {
            var records = new List<MonsterActionResolutionRecord>
            {
                Attacker("first", new HexCoord(20, 0), "g1", 3),
                Attacker("second", new HexCoord(0, 20), "g2", 3),
            };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: false, new HexCoord(0, 0), new HexCoord(0, 0), playerDied: false,
                new List<EffectResultEvent>(), emitActionSpotlights: true);

            var spotlights = Spotlights(timeline);
            Assert.That(spotlights.Count, Is.EqualTo(2));

            // Each framing is sized by its own event, not by everything that follows it — otherwise the first
            // spotlight of a phase would always look like the longest event and pan the slowest.
            Assert.That(spotlights[0].CoveredBeats, Is.GreaterThan(0));
            Assert.That(
                spotlights[0].CoveredBeats,
                Is.LessThan(timeline.Count - 1),
                "the first framing must not absorb the second monster's beats");
        }

        [Test]
        public void ActionSpotlight_CoverageIsZeroWhenEmissionIsOff()
        {
            var records = new List<MonsterActionResolutionRecord> { Attacker("m", new HexCoord(20, 0), "g", 3) };

            var timeline = CombatTimelineAssembler.BuildEndAction(
                records, playerKnockedBack: false, new HexCoord(0, 0), new HexCoord(0, 0), playerDied: false,
                new List<EffectResultEvent>());

            // The annotation pass runs unconditionally; with no spotlights it must be a no-op rather than
            // writing coverage onto unrelated beats.
            Assert.That(timeline.Events.All(e => e.CoveredBeats == 0), Is.True);
        }

        [Test]
        public void ActionSpotlight_LeavesThePlayerTurnTimelineAlone()
        {
            var path = new List<HexCoord> { new HexCoord(0, 0), new HexCoord(1, 0) };
            var records = new List<MonsterActionResolutionRecord>
            {
                MovedVisible("m1", new HexCoord(20, 0), new HexCoord(21, 0)),
            };

            var timeline = CombatTimelineAssembler.BuildPlayerMove(path, records);

            // 플레이어 턴엔 카메라가 행동 주체인 플레이어를 따라간다 — 이 기능의 범위 밖(§1).
            Assert.That(Spotlights(timeline), Is.Empty);
        }
    }
}


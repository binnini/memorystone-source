using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Runtime.Presentation;
using SeoulPlayup.Combat.Runtime.Timeline;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Combat.Unity.Presentation;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class PresentationSchedulerTests
    {
        private sealed class RecordingSink : ICombatPresentationSink
        {
            public readonly List<string> Log = new List<string>();
            public readonly List<float> Waits = new List<float>();
            public readonly List<int> Dispatched = new List<int>();
            public float LastPlayerStepSeconds = -1f;

            private static IEnumerator Empty() { yield break; }

            public IEnumerator Wait(float seconds) { Log.Add("wait"); Waits.Add(seconds); return Empty(); }
            public IEnumerator MovePlayerStep(HexCoord from, HexCoord to, float seconds) { Log.Add("playerstep"); LastPlayerStepSeconds = seconds; return Empty(); }
            public IEnumerator MoveEnemyStep(string monsterId, HexCoord from, HexCoord to, float seconds) { Log.Add("enemystep:" + monsterId); return Empty(); }
            public IEnumerator LeapEnemyStep(string unitId, HexCoord from, HexCoord to, float seconds) { Log.Add("enemyleap:" + unitId); return Empty(); }
            public IEnumerator KnockbackPlayerStep(HexCoord from, HexCoord to, float seconds) { Log.Add("playerknock"); return Empty(); }
            public IEnumerator KnockbackEnemyStep(string monsterId, HexCoord from, HexCoord to, float seconds) { Log.Add("enemyknock:" + monsterId); return Empty(); }
            public void StartPlayerAttack(HexCoord from, HexCoord to) { Log.Add("startplayerattack"); }
            public void StartEnemyAttack(string monsterId, string trigger, HexCoord from, HexCoord to) { Log.Add("startenemyattack:" + monsterId); }
            public IEnumerator AttackWindup(string timingKey) { Log.Add("windup"); return Empty(); }
            public IEnumerator AttackImpactWait(string timingKey, string actorId) { Log.Add("impactwait:" + actorId); return Empty(); }
            public void CommitImpact(string groupId) { Log.Add("commit:" + groupId); }
            public void ReactActorHit(string monsterId, int effectIndex) { Log.Add("hit:" + monsterId); }
            public void ReactActorDeath(string monsterId, int effectIndex) { Log.Add("death:" + monsterId); }
            public void ReactActorKnockback(string monsterId, int effectIndex) { Log.Add("knockreact:" + monsterId); }
            public void ReactPlayerHit(int effectIndex) { Log.Add("playerhit"); }
            public void ReactPlayerDeath(int effectIndex) { Log.Add("playerdeath"); }
            public void DispatchEffect(int bufferedIndex) { Log.Add("dispatch:" + bufferedIndex); Dispatched.Add(bufferedIndex); }
            public IEnumerator HitStop(string timingKey, string actorId, bool lethal) { Log.Add("hitstop:" + actorId); return Empty(); }
            public IEnumerator DeathHold(string timingKey) { Log.Add("deathhold"); return Empty(); }
            public IEnumerator MonsterActionGap() { Log.Add("gap"); return Empty(); }

            /// <summary>
            /// Stands in for the live sink's frustum gate. <see cref="FocusMoves"/> chooses which branch is
            /// exercised: false is the already-framed case, where the beat must fall back to the gap it
            /// displaced (and only when it displaced one).
            /// </summary>
            public bool FocusMoves;

            /// <summary>The lead-in the scheduler forwarded on the last focus beat, so a test can assert it arrives.</summary>
            public float LastCoveredLeadInSeconds;

            public IEnumerator FocusOnAction(
                HexCoord coord,
                string actorId,
                bool fallbackGap,
                int coveredBeats,
                float coveredSeconds,
                float coveredLeadInSeconds)
            {
                LastCoveredLeadInSeconds = coveredLeadInSeconds;
                if (FocusMoves)
                {
                    Log.Add($"focus:{actorId}@{coord.Q},{coord.R}+{coveredBeats}");
                    return Empty();
                }

                Log.Add("focus:noop");
                return fallbackGap ? MonsterActionGap() : Empty();
            }
            public IEnumerator ActionFocusRelease() { Log.Add("focusrelease"); return Empty(); }
            public IEnumerator OverallTurnStart() { Log.Add("overallturnstart"); return Empty(); }
            public IEnumerator PlayerTurnStart() { Log.Add("playerturnstart"); return Empty(); }
            public void ShowAmbushAlert() { Log.Add("ambush"); } // 기습 경고 비트 기록
            public IEnumerator BossPhaseTransition(string bossUnitId, HexCoord? coord) { Log.Add("bossphase:" + bossUnitId); return Empty(); }
            public IEnumerator BossPropVolleyCast(string bossUnitId, HexCoord? bossCoord) { Log.Add("propcast:" + bossUnitId); return Empty(); }
            public IEnumerator BossPropPlaced(string bossUnitId, HexCoord? coord) { Log.Add("propplaced:" + coord); return Empty(); }
            public IEnumerator BossPropAbsorb(string bossUnitId, HexCoord? propCoord, HexCoord? bossCoord) { Log.Add("propabsorb:" + propCoord + "->" + bossCoord); return Empty(); }
            public IEnumerator BossScrapChainHit(string bossUnitId, HexCoord? bossCoord, HexCoord? hitPlayerCoord) { Log.Add("scrapchain:" + bossUnitId + ":" + bossCoord + "->" + hitPlayerCoord); return Empty(); }
        }

        private static void Run(IEnumerator routine)
        {
            while (routine.MoveNext())
            {
            }
        }

        private static MonsterActionResolutionRecord MovedVisible(string id, HexCoord from, HexCoord to)
        {
            return new MonsterActionResolutionRecord(
                id, MonsterActivityState.ActiveThreat, from, to,
                wasVisibleBefore: true, isVisibleAfter: true,
                attackedPlayer: false, affectedPlayer: false, damageToPlayer: 0);
        }

        [Test]
        public void Play_WhenTracing_RecordsEveryBeatInSchedulerOrder()
        {
            var path = new List<HexCoord> { new HexCoord(0, 0), new HexCoord(1, 0) };
            var records = new List<MonsterActionResolutionRecord> { MovedVisible("m1", new HexCoord(5, 5), new HexCoord(5, 6)) };
            var timeline = CombatTimelineAssembler.BuildPlayerMove(path, records);
            var profile = ScriptableObject.CreateInstance<CombatTimingProfile>();
            var time = 0f;
            try
            {
                CombatPresentationTrace.Begin("test", () => time);
                Run(new PresentationScheduler().Play(timeline, profile, new RecordingSink()));
                var log = CombatPresentationTrace.End();

                CollectionAssert.AreEqual(
                    new[] { "PlayerMoveStep", "EnemyMoveStep" },
                    log.Entries.Where(entry => entry.Channel == CombatTraceChannel.Beat)
                        .Select(entry => entry.Label).ToArray());
                Assert.That(log.Entries.Last().Detail, Does.Contain("m1"));
            }
            finally
            {
                CombatPresentationTrace.Reset();
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void Play_BossScrapChainHit_DispatchesTheBeatWithBossAndPlayerCoords()
        {
            // 2026-09-05 후속 #7: 사슬 명중 비트가 sink에 보스 좌표(From)·맞은 플레이어 좌표(To)로 도착한다.
            var timeline = new CombatTimeline();
            timeline.Append(CombatTimelineEventKind.BossScrapChainHit, "boss", from: new HexCoord(3, 0), to: new HexCoord(1, 0));
            var profile = ScriptableObject.CreateInstance<CombatTimingProfile>();
            var sink = new RecordingSink();
            try
            {
                Run(new PresentationScheduler().Play(timeline, profile, sink));
                Assert.That(sink.Log, Has.Member("scrapchain:boss:" + new HexCoord(3, 0) + "->" + new HexCoord(1, 0)));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void Play_WhenNotTracing_RecordsNothing()
        {
            var path = new List<HexCoord> { new HexCoord(0, 0), new HexCoord(1, 0) };
            var timeline = CombatTimelineAssembler.BuildPlayerMove(path, new List<MonsterActionResolutionRecord>());
            var profile = ScriptableObject.CreateInstance<CombatTimingProfile>();
            try
            {
                Run(new PresentationScheduler().Play(timeline, profile, new RecordingSink()));

                Assert.That(CombatPresentationTrace.IsRecording, Is.False);
                Assert.That(CombatPresentationTrace.End(), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void Play_PlayerMove_StepsPlayerPerTileThenMovesMonster()
        {
            var path = new List<HexCoord> { new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0) };
            var records = new List<MonsterActionResolutionRecord> { MovedVisible("m1", new HexCoord(5, 5), new HexCoord(5, 6)) };
            var timeline = CombatTimelineAssembler.BuildPlayerMove(path, records);
            var profile = ScriptableObject.CreateInstance<CombatTimingProfile>();
            var sink = new RecordingSink();
            try
            {
                Run(new PresentationScheduler().Play(timeline, profile, sink));
                CollectionAssert.AreEqual(new[] { "playerstep", "playerstep", "enemystep:m1" }, sink.Log);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void Play_PlayerAttack_DispatchesEffectsInOrderWithStaggerThenReactionAndHitStop()
        {
            var effects = new List<EffectResultEvent>
            {
                new EffectResultEvent(EffectKind.Damage, presentationGroupId: "g"),
                new EffectResultEvent(EffectKind.Damage, presentationGroupId: "g"),
                new EffectResultEvent(EffectKind.Damage, presentationGroupId: "g"),
            };
            var timeline = CombatTimelineAssembler.BuildPlayerAttack(
                "attack", "key", new HexCoord(0, 0), "m1", new HexCoord(1, 0), new HexCoord(1, 0),
                targetHit: true, targetKnockedBack: false, targetDied: false, effects);
            var profile = ScriptableObject.CreateInstance<CombatTimingProfile>();
            var sink = new RecordingSink();
            try
            {
                Run(new PresentationScheduler().Play(timeline, profile, sink));

                Assert.That(sink.Log[0], Is.EqualTo("startplayerattack"));
                Assert.That(sink.Log[sink.Log.Count - 1], Is.EqualTo("hitstop:m1"));
                CollectionAssert.AreEqual(new[] { 0, 1, 2 }, sink.Dispatched);
                // Each of the 3 effects is followed by a stagger wait so they read individually.
                Assert.That(sink.Waits.Count, Is.GreaterThanOrEqualTo(3));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void Play_GlobalSpeedMultiplier_ShortensStepDuration()
        {
            var path = new List<HexCoord> { new HexCoord(0, 0), new HexCoord(1, 0) };
            var timeline = CombatTimelineAssembler.BuildPlayerMove(path, new List<MonsterActionResolutionRecord>());
            var profile = ScriptableObject.CreateInstance<CombatTimingProfile>();
            var sink = new RecordingSink();
            try
            {
                var baseline = profile.PlayerMoveSeconds;
                profile.TunableGlobalSpeedMultiplier = 2f;
                Run(new PresentationScheduler().Play(timeline, profile, sink));

                Assert.That(sink.LastPlayerStepSeconds, Is.EqualTo(baseline / 2f).Within(1e-4f));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void Play_TextQueueEffectUsesFloatingTextQueueStagger()
        {
            var timeline = new CombatTimeline();
            timeline.Append(CombatTimelineEventKind.Effect, effectIndex: 0, textQueue: true);
            var profile = ScriptableObject.CreateInstance<CombatTimingProfile>();
            var sink = new RecordingSink();
            try
            {
                Run(new PresentationScheduler().Play(timeline, profile, sink));

                CollectionAssert.AreEqual(new[] { 0 }, sink.Dispatched);
                Assert.That(sink.Waits.Single(), Is.EqualTo(profile.FloatingTextQueueStaggerSeconds).Within(1e-4f));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void Play_NoWaitAfterEffectDispatchesNextEffectSameBeat()
        {
            var timeline = new CombatTimeline();
            timeline.Append(CombatTimelineEventKind.Effect, effectIndex: 0, noWaitAfterEffect: true);
            timeline.Append(CombatTimelineEventKind.Effect, effectIndex: 1, aoeTarget: true);
            var profile = ScriptableObject.CreateInstance<CombatTimingProfile>();
            var sink = new RecordingSink();
            try
            {
                Run(new PresentationScheduler().Play(timeline, profile, sink));

                CollectionAssert.AreEqual(new[] { 0, 1 }, sink.Dispatched);
                Assert.That(sink.Waits.Count, Is.EqualTo(1), "NoWaitAfterEffect must not consume a stagger before the paired hit effect.");
                Assert.That(sink.Waits[0], Is.EqualTo(profile.AoeTargetIntervalSeconds).Within(1e-4f));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void Play_MonsterPhaseCompleteGapUsesPostMonsterAttackPause()
        {
            var timeline = new CombatTimeline();
            timeline.Append(CombatTimelineEventKind.MonsterPhaseCompleteGap);
            var profile = ScriptableObject.CreateInstance<CombatTimingProfile>();
            var sink = new RecordingSink();
            try
            {
                Run(new PresentationScheduler().Play(timeline, profile, sink));

                Assert.That(sink.Waits.Single(), Is.EqualTo(profile.PostMonsterAttackPauseSeconds).Within(1e-4f));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void Play_HandsTheCameraBackBeforeItStartsThePlayerTurn()
        {
            // 어셈블러가 방출한 순서를 스케줄러가 그대로 흘리는지 — 두 계층 사이가 어긋나면 §10.13의 증상
            // (카메라가 밖에 있는데 카드가 뽑힘)이 그대로 돌아온다.
            var timeline = new CombatTimeline();
            timeline.Append(CombatTimelineEventKind.ActionFocusRelease);
            timeline.Append(CombatTimelineEventKind.PlayerTurnStart);
            var profile = ScriptableObject.CreateInstance<CombatTimingProfile>();
            var sink = new RecordingSink();
            try
            {
                Run(new PresentationScheduler().Play(timeline, profile, sink));

                Assert.That(sink.Log, Is.EqualTo(new[] { "focusrelease", "playerturnstart" }));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void Play_EndAction_CommitsFinalAttackImpactBeforePlayerDeath()
        {
            var effects = new List<EffectResultEvent>
            {
                new EffectResultEvent(EffectKind.Damage, targetUnitId: "player", presentationGroupId: "g1"),
                new EffectResultEvent(EffectKind.Damage, targetUnitId: "player", presentationGroupId: "g2"),
                new EffectResultEvent(EffectKind.Damage, targetUnitId: "player", presentationGroupId: "g3"),
            };
            var records = new[]
            {
                AttackRecord("monster-a", "g1", damage: 1, order: 0),
                AttackRecord("monster-b", "g2", damage: 1, order: 1),
                AttackRecord("monster-c", "g3", damage: 9, order: 2),
            };
            var timeline = CombatTimelineAssembler.BuildEndAction(
                records,
                playerKnockedBack: false,
                playerBefore: new HexCoord(0, 0),
                playerAfter: new HexCoord(0, 0),
                playerDied: true,
                effects);
            var profile = ScriptableObject.CreateInstance<CombatTimingProfile>();
            var sink = new RecordingSink();
            try
            {
                Run(new PresentationScheduler().Play(timeline, profile, sink));

                var finalCommit = sink.Log.IndexOf("commit:g3");
                var finalDispatch = sink.Log.IndexOf("dispatch:2");
                var death = sink.Log.IndexOf("playerdeath");
                Assert.That(finalCommit, Is.GreaterThanOrEqualTo(0));
                Assert.That(finalDispatch, Is.GreaterThan(finalCommit));
                Assert.That(death, Is.GreaterThan(finalDispatch));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        // --- Action camera focus, P3 (docs/monster-action-camera-focus-plan.md §5 P3) ---

        [Test]
        public void ActorSpotlight_ForwardsTheFramingPointAndDoesNotAlsoPlayTheGap()
        {
            var timeline = new CombatTimeline();
            timeline.Append(
                CombatTimelineEventKind.ActorSpotlight, "m1", to: new HexCoord(9, 0), fallbackGap: true);
            var sink = new RecordingSink { FocusMoves = true };
            var profile = ScriptableObject.CreateInstance<CombatTimingProfile>();

            try
            {
                Run(new PresentationScheduler().Play(timeline, profile, sink));

                // The camera move is itself the breathing beat — adding the gap on top is what would blow
                // the enemy-turn budget (§3.2).
                Assert.That(sink.Log, Is.EqualTo(new[] { "focus:m1@9,0+0" }));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ActorSpotlight_FallsBackToTheGapItDisplacedWhenNothingMoved()
        {
            var timeline = new CombatTimeline();
            timeline.Append(
                CombatTimelineEventKind.ActorSpotlight, "m1", to: new HexCoord(1, 0), fallbackGap: true);
            var sink = new RecordingSink { FocusMoves = false };
            var profile = ScriptableObject.CreateInstance<CombatTimingProfile>();

            try
            {
                Run(new PresentationScheduler().Play(timeline, profile, sink));

                // Already-framed action: the beat must be indistinguishable from today's timeline, which is
                // the plan's zero-regression promise (§7).
                Assert.That(sink.Log, Is.EqualTo(new[] { "focus:noop", "gap" }));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ActorSpotlight_AddsNothingWhereThereWasNeverAGap()
        {
            var timeline = new CombatTimeline();
            timeline.Append(
                CombatTimelineEventKind.ActorSpotlight, "m1", to: new HexCoord(1, 0), fallbackGap: false);
            var sink = new RecordingSink { FocusMoves = false };
            var profile = ScriptableObject.CreateInstance<CombatTimingProfile>();

            try
            {
                Run(new PresentationScheduler().Play(timeline, profile, sink));

                // The first monster, the movement phase and the turn-boundary effects have no gap to restore.
                // Falling back unconditionally there would silently add 0.7s to an on-screen event.
                Assert.That(sink.Log, Is.EqualTo(new[] { "focus:noop" }));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void Play_BossPhaseTransition_ForwardsTheBeatToTheSink()
        {
            var timeline = new CombatTimeline();
            timeline.Append(CombatTimelineEventKind.BossPhaseTransition, "boss-1", to: new HexCoord(0, 0));
            var profile = ScriptableObject.CreateInstance<CombatTimingProfile>();
            var sink = new RecordingSink();
            try
            {
                Run(new PresentationScheduler().Play(timeline, profile, sink));
                Assert.That(sink.Log, Is.EqualTo(new[] { "bossphase:boss-1" }));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        private static MonsterActionResolutionRecord AttackRecord(string id, string groupId, int damage, int order)
        {
            return new MonsterActionResolutionRecord(
                id,
                MonsterActivityState.ActiveThreat,
                new HexCoord(order + 1, 0),
                new HexCoord(order + 1, 0),
                wasVisibleBefore: true,
                isVisibleAfter: true,
                attackedPlayer: true,
                affectedPlayer: true,
                damageToPlayer: damage,
                actionOrder: order,
                attackOrder: order,
                attackPatternId: "attack-" + order,
                attackAnimationTrigger: "Attack",
                presentationGroupId: groupId);
        }
    }
}

using System.Collections.Generic;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime.Timeline;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class CombatTimelineTests
    {
        [Test]
        public void Append_AssignsMonotonicSequence()
        {
            var timeline = new CombatTimeline();
            timeline.Append(CombatTimelineEventKind.PlayerAttackStart, actorId: "player");
            timeline.Append(CombatTimelineEventKind.AttackImpact, actorId: "player");
            timeline.Append(CombatTimelineEventKind.Effect, effectIndex: 0);

            Assert.That(timeline.Count, Is.EqualTo(3));
            for (var i = 0; i < timeline.Count; i++)
            {
                Assert.That(timeline.Events[i].Sequence, Is.EqualTo(i));
            }
        }

        [Test]
        public void AppendMovePath_EmitsOneStepPerTileTransition()
        {
            var timeline = new CombatTimeline();
            var path = new List<HexCoord>
            {
                new HexCoord(0, 0),
                new HexCoord(1, 0),
                new HexCoord(2, 0),
                new HexCoord(3, 0),
            };

            timeline.AppendMovePath(CombatTimelineEventKind.PlayerMoveStep, "player", path);

            Assert.That(timeline.Count, Is.EqualTo(3)); // 4 tiles -> 3 transitions
            Assert.That(timeline.Events[0].From.Value, Is.EqualTo(path[0]));
            Assert.That(timeline.Events[0].To.Value, Is.EqualTo(path[1]));
            Assert.That(timeline.Events[2].From.Value, Is.EqualTo(path[2]));
            Assert.That(timeline.Events[2].To.Value, Is.EqualTo(path[3]));
            foreach (var evt in timeline.Events)
            {
                Assert.That(evt.Kind, Is.EqualTo(CombatTimelineEventKind.PlayerMoveStep));
                Assert.That(evt.IsMoveStep, Is.True);
            }
        }

        [Test]
        public void AppendMovePath_SingleTileOrNull_EmitsNothing()
        {
            var timeline = new CombatTimeline();
            timeline.AppendMovePath(CombatTimelineEventKind.PlayerMoveStep, "player",
                new List<HexCoord> { new HexCoord(0, 0) });
            timeline.AppendMovePath(CombatTimelineEventKind.EnemyMoveStep, "m1", null);

            Assert.That(timeline.IsEmpty, Is.True);
        }

        [Test]
        public void GroupId_PreservedAcrossImpactAndEffects()
        {
            var timeline = new CombatTimeline();
            timeline.Append(CombatTimelineEventKind.AttackImpact, actorId: "m1", groupId: "g1");
            timeline.Append(CombatTimelineEventKind.Effect, effectIndex: 0, groupId: "g1");
            timeline.Append(CombatTimelineEventKind.Effect, effectIndex: 1, groupId: "g1");

            foreach (var evt in timeline.Events)
            {
                Assert.That(evt.GroupId, Is.EqualTo("g1"));
            }
        }

        [Test]
        public void Clear_ResetsTimeline()
        {
            var timeline = new CombatTimeline();
            timeline.Append(CombatTimelineEventKind.PlayerHit);
            timeline.Clear();

            Assert.That(timeline.IsEmpty, Is.True);
            Assert.That(timeline.Count, Is.EqualTo(0));
        }
    }
}

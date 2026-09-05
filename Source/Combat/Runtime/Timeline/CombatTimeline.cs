using System.Collections.Generic;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime.Timeline
{
    /// <summary>
    /// Ordered, presentation-agnostic record of everything that happened during one combat rules
    /// resolution (a single player action, or one end-of-turn monster phase). It carries only semantic
    /// beats — no seconds — so it can live in the pure-C# rules layer; the Unity presentation scheduler
    /// consumes it and decides the actual on-screen timing.
    ///
    /// <see cref="CombatTimelineEvent.Sequence"/> is assigned on append and is therefore monotonic by
    /// construction, giving the scheduler a stable replay order.
    /// </summary>
    public sealed class CombatTimeline
    {
        private readonly List<CombatTimelineEvent> events = new List<CombatTimelineEvent>();

        public IReadOnlyList<CombatTimelineEvent> Events => events;
        public int Count => events.Count;
        public bool IsEmpty => events.Count == 0;

        public void Clear()
        {
            events.Clear();
        }

        /// <summary>Append one beat, stamping it with the next sequence index. Returns the appended beat.</summary>
        public CombatTimelineEvent Append(
            CombatTimelineEventKind kind,
            string actorId = "",
            HexCoord? from = null,
            HexCoord? to = null,
            string trigger = "",
            int effectIndex = -1,
            string groupId = "",
            bool lethal = false,
            string timingKey = "",
            bool multiHitStrike = false,
            bool aoeTarget = false,
            bool statusApply = false,
            bool textQueue = false,
            bool noWaitAfterEffect = false,
            bool fallbackGap = false,
            int coveredBeats = 0)
        {
            var evt = new CombatTimelineEvent(kind, events.Count, actorId, from, to, trigger, effectIndex, groupId, lethal, timingKey, multiHitStrike, aoeTarget, statusApply, textQueue, noWaitAfterEffect, fallbackGap, coveredBeats);
            events.Add(evt);
            return evt;
        }

        /// <summary>
        /// Fills in each <see cref="CombatTimelineEventKind.ActorSpotlight"/>'s
        /// <see cref="CombatTimelineEvent.CoveredBeats"/>: how many content beats it covers before the next
        /// spotlight takes over.
        ///
        /// Done as a pass after assembly rather than at emission because the assembler streams — when a
        /// spotlight is appended, the beats it will cover have not been built yet.
        ///
        /// Boundary beats are excluded on purpose: a gap or a turn-start announcement is the pause between
        /// events, so counting them would make the last spotlight of a phase look like a long event and
        /// stretch its pan for no reason.
        /// </summary>
        public void AnnotateActorSpotlightCoverage()
        {
            for (var i = 0; i < events.Count; i++)
            {
                if (events[i].Kind != CombatTimelineEventKind.ActorSpotlight)
                {
                    continue;
                }

                var covered = 0;
                for (var j = i + 1; j < events.Count; j++)
                {
                    if (events[j].Kind == CombatTimelineEventKind.ActorSpotlight)
                    {
                        break;
                    }

                    if (IsContentBeat(events[j].Kind))
                    {
                        covered++;
                    }
                }

                events[i] = events[i].WithCoveredBeats(covered);
            }
        }

        /// <summary>
        /// Fills each <see cref="CombatTimelineEventKind.ActorSpotlight"/>'s
        /// <see cref="CombatTimelineEvent.CoveredSeconds"/>: the real presentation length of the beats it
        /// covers, summed from the measured duration data (docs/presentation-duration-data-plan.md P4). This
        /// replaces the trigger-count approximation <see cref="CombatTimelineEvent.CoveredBeats"/> was
        /// standing in for — a count cannot tell a 0.3s spark from a 6s explosion.
        ///
        /// A separate pass from <see cref="AnnotateActorSpotlightCoverage"/> (which the pure assembler runs)
        /// because resolving a cue's length needs the Unity VFX catalog. The caller supplies
        /// <paramref name="effectSecondsByBufferedIndex"/>, so this stays pure and testable: pass a fake and
        /// the arithmetic is asserted without a catalog.
        ///
        /// Only <see cref="CombatTimelineEventKind.Effect"/> beats contribute — they are the beats that fire
        /// a cue. Reaction beats (hit/death/knockback) play animations whose lengths are out of this track's
        /// consumer scope, so they add nothing rather than a guess.
        /// </summary>
        public void AnnotateActorSpotlightSeconds(System.Func<int, float> effectSecondsByBufferedIndex)
        {
            if (effectSecondsByBufferedIndex == null)
            {
                return;
            }

            for (var i = 0; i < events.Count; i++)
            {
                if (events[i].Kind != CombatTimelineEventKind.ActorSpotlight)
                {
                    continue;
                }

                var seconds = 0f;
                for (var j = i + 1; j < events.Count; j++)
                {
                    if (events[j].Kind == CombatTimelineEventKind.ActorSpotlight)
                    {
                        break;
                    }

                    if (events[j].Kind != CombatTimelineEventKind.Effect || events[j].EffectIndex < 0)
                    {
                        continue;
                    }

                    var contribution = effectSecondsByBufferedIndex(events[j].EffectIndex);
                    if (contribution > 0f && !float.IsNaN(contribution) && !float.IsInfinity(contribution))
                    {
                        seconds += contribution;
                    }
                }

                events[i] = events[i].WithCoveredSeconds(seconds);
            }
        }

        /// <summary>
        /// Fills each <see cref="CombatTimelineEventKind.ActorSpotlight"/>'s
        /// <see cref="CombatTimelineEvent.CoveredLeadInSeconds"/>: the longest authored playback delay among
        /// the cues its covered beats fire.
        ///
        /// ⚠ This is a MAX, not a sum — unlike <see cref="AnnotateActorSpotlightSeconds"/>. A lead-in is dead
        /// time that every covered cue waits out *concurrently* from the same dispatch, so summing them would
        /// park the camera on an empty patch for the total. What the hold needs is "when does the last of them
        /// start showing", which is the largest single delay.
        ///
        /// Same seam as the seconds pass: the caller supplies the resolver so this stays pure and testable
        /// without a Unity VFX catalog.
        /// </summary>
        public void AnnotateActorSpotlightLeadIn(System.Func<int, float> effectLeadInByBufferedIndex)
        {
            if (effectLeadInByBufferedIndex == null)
            {
                return;
            }

            for (var i = 0; i < events.Count; i++)
            {
                if (events[i].Kind != CombatTimelineEventKind.ActorSpotlight)
                {
                    continue;
                }

                var leadIn = 0f;
                for (var j = i + 1; j < events.Count; j++)
                {
                    if (events[j].Kind == CombatTimelineEventKind.ActorSpotlight)
                    {
                        break;
                    }

                    if (events[j].Kind != CombatTimelineEventKind.Effect || events[j].EffectIndex < 0)
                    {
                        continue;
                    }

                    var contribution = effectLeadInByBufferedIndex(events[j].EffectIndex);
                    if (contribution > leadIn && !float.IsNaN(contribution) && !float.IsInfinity(contribution))
                    {
                        leadIn = contribution;
                    }
                }

                events[i] = events[i].WithCoveredLeadInSeconds(leadIn);
            }
        }

        private static bool IsContentBeat(CombatTimelineEventKind kind)
        {
            switch (kind)
            {
                case CombatTimelineEventKind.MonsterActionGap:
                case CombatTimelineEventKind.MonsterPhaseCompleteGap:
                case CombatTimelineEventKind.OverallTurnStart:
                case CombatTimelineEventKind.PlayerTurnStart:
                // Boundary, not content: the release beat exists to close the phase's framing, so counting it
                // as covered content would let a spotlight claim a hold for the beat that ends it.
                case CombatTimelineEventKind.ActionFocusRelease:
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>
        /// Decompose an ordered tile path (start..destination, inclusive) into one move-step beat per
        /// tile transition, so the scheduler can animate the actor one hex at a time. A path with fewer
        /// than two tiles produces nothing (no movement happened).
        /// </summary>
        public void AppendMovePath(CombatTimelineEventKind stepKind, string actorId, IReadOnlyList<HexCoord> path, string groupId = "")
        {
            if (path == null || path.Count < 2)
            {
                return;
            }

            for (var i = 1; i < path.Count; i++)
            {
                Append(stepKind, actorId, path[i - 1], path[i], groupId: groupId);
            }
        }
    }
}

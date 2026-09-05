using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SeoulPlayup.Combat.Runtime.Presentation
{
    /// <summary>Which presentation channel an entry belongs to.</summary>
    public enum CombatTraceChannel
    {
        /// <summary>A scheduler beat boundary (attack start, impact, hit-stop, gap, wait).</summary>
        Beat,

        /// <summary>A buffered effect was dispatched — the instant its four channels are released.</summary>
        Dispatch,

        /// <summary>A VFX prefab actually spawned (after the cue's authored delay).</summary>
        Vfx,

        /// <summary>A sound cue was requested.</summary>
        Sfx,

        /// <summary>A camera shake fired.</summary>
        Shake,

        /// <summary>A flinch / death / knockback reaction was triggered.</summary>
        React,

        /// <summary>Floating text was spawned.</summary>
        Text
    }

    public readonly struct CombatTraceEntry
    {
        public CombatTraceEntry(float timeSeconds, CombatTraceChannel channel, string label, string detail)
        {
            TimeSeconds = timeSeconds;
            Channel = channel;
            Label = label ?? string.Empty;
            Detail = detail ?? string.Empty;
        }

        /// <summary>Seconds since the traced sequence began.</summary>
        public float TimeSeconds { get; }
        public CombatTraceChannel Channel { get; }
        public string Label { get; }
        public string Detail { get; }
    }

    /// <summary>A completed recording, with a human-readable rendering.</summary>
    public sealed class CombatPresentationTraceLog
    {
        public CombatPresentationTraceLog(string label, IReadOnlyList<CombatTraceEntry> entries, bool truncated)
        {
            Label = label ?? string.Empty;
            Entries = entries ?? Array.Empty<CombatTraceEntry>();
            Truncated = truncated;
        }

        public string Label { get; }
        public IReadOnlyList<CombatTraceEntry> Entries { get; }

        /// <summary>True when the entry cap was hit and later entries were dropped.</summary>
        public bool Truncated { get; }

        public float DurationSeconds => Entries.Count == 0 ? 0f : Entries[Entries.Count - 1].TimeSeconds;

        /// <summary>
        /// Renders the recording as an ordered table. Channel landings are indented under the dispatch they
        /// belong to, so a channel that drifts away from its beat is visible as a time gap in one column
        /// rather than something to be judged by eye in slow motion.
        /// </summary>
        public string Format()
        {
            var builder = new StringBuilder();
            builder.Append("[presentation-trace] ").Append(Label)
                .Append("  총 ").Append(DurationSeconds.ToString("0.000", CultureInfo.InvariantCulture)).Append('s')
                .Append("  (").Append(Entries.Count).Append(" entries)");
            if (Truncated)
            {
                builder.Append(" — 항목 상한 초과로 이후 기록 누락");
            }

            builder.AppendLine();

            foreach (var entry in Entries)
            {
                var indent = entry.Channel == CombatTraceChannel.Beat || entry.Channel == CombatTraceChannel.Dispatch
                    ? string.Empty
                    : "    ";
                builder
                    .Append(entry.TimeSeconds.ToString("0.000", CultureInfo.InvariantCulture).PadLeft(7))
                    .Append("  ")
                    .Append(indent)
                    .Append(ChannelTag(entry.Channel))
                    .Append(' ')
                    .Append(entry.Label);

                if (!string.IsNullOrEmpty(entry.Detail))
                {
                    builder.Append("  ").Append(entry.Detail);
                }

                builder.AppendLine();
            }

            return builder.ToString();
        }

        private static string ChannelTag(CombatTraceChannel channel)
        {
            switch (channel)
            {
                case CombatTraceChannel.Beat: return "BEAT ";
                case CombatTraceChannel.Dispatch: return "EFFECT";
                case CombatTraceChannel.Vfx: return "VFX  ";
                case CombatTraceChannel.Sfx: return "SFX  ";
                case CombatTraceChannel.Shake: return "SHAKE";
                case CombatTraceChannel.React: return "REACT";
                case CombatTraceChannel.Text: return "TEXT ";
                default: return channel.ToString();
            }
        }
    }

    /// <summary>
    /// Records when each presentation channel actually lands during one combat sequence.
    ///
    /// The four channels of a single hit (VFX, SFX, camera shake, hit reaction) are released from four
    /// different files and each waits its own delay, so a mismatch between them is only observable as
    /// "something felt off" in slow motion. This turns that into timestamps.
    ///
    /// It is an ambient static recorder on purpose: the emitting sites live in three assemblies
    /// (Combat, Audio, Combat.Runtime) and are separate MonoBehaviours reached through no common object,
    /// so threading an instance through every one of them would cost far more plumbing than the diagnostic
    /// is worth. The trade is global state, contained as follows: recording is off unless
    /// <see cref="Begin"/> was called, every <see cref="Record"/> call is a single boolean test when off,
    /// the clock is injected so this layer stays free of UnityEngine, and <see cref="Reset"/> exists so a
    /// test can guarantee a clean slate. Presentation runs on the main thread only, so no locking.
    /// </summary>
    public static class CombatPresentationTrace
    {
        /// <summary>Cap so a recorder left on by accident cannot grow without bound.</summary>
        public const int MaxEntries = 512;

        private static readonly List<CombatTraceEntry> Entries = new List<CombatTraceEntry>();
        private static Func<float> clock;
        private static float startTime;
        private static string activeLabel = string.Empty;
        private static bool truncated;

        public static bool IsRecording { get; private set; }

        /// <summary>
        /// Starts a recording. <paramref name="clock"/> supplies "now" in seconds (unscaled realtime at the
        /// call sites, so hit-stop's time-scale changes do not distort the measured gaps). A Begin while
        /// already recording discards the in-progress recording rather than nesting.
        /// </summary>
        public static void Begin(string label, Func<float> clock)
        {
            if (clock == null)
            {
                return;
            }

            Entries.Clear();
            truncated = false;
            CombatPresentationTrace.clock = clock;
            startTime = clock();
            activeLabel = label ?? string.Empty;
            IsRecording = true;
        }

        /// <summary>Ends the recording and returns it. Returns null when nothing was being recorded.</summary>
        public static CombatPresentationTraceLog End()
        {
            if (!IsRecording)
            {
                return null;
            }

            var log = new CombatPresentationTraceLog(activeLabel, Entries.ToArray(), truncated);
            IsRecording = false;
            clock = null;
            activeLabel = string.Empty;
            Entries.Clear();
            truncated = false;
            return log;
        }

        public static void Record(CombatTraceChannel channel, string label, string detail = "")
        {
            if (!IsRecording)
            {
                return;
            }

            if (Entries.Count >= MaxEntries)
            {
                truncated = true;
                return;
            }

            Entries.Add(new CombatTraceEntry(clock() - startTime, channel, label, detail));
        }

        /// <summary>Drops any in-progress recording. For tests and for recovering from an aborted sequence.</summary>
        public static void Reset()
        {
            Entries.Clear();
            truncated = false;
            IsRecording = false;
            clock = null;
            activeLabel = string.Empty;
        }
    }
}

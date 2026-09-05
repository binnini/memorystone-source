using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime.Presentation
{
    /// <summary>
    /// One presented action event, as seen by the action-focus gate: where it happened, how far that was
    /// from the player, and whether the camera was already showing it.
    /// </summary>
    public readonly struct CombatActionFocusSample
    {
        public CombatActionFocusSample(
            int overallTurnNumber,
            string phaseLabel,
            string eventLabel,
            string actorId,
            HexCoord coord,
            int hexDistanceFromPlayer,
            bool framed,
            bool framedWithMargin)
        {
            OverallTurnNumber = overallTurnNumber;
            PhaseLabel = phaseLabel ?? string.Empty;
            EventLabel = eventLabel ?? string.Empty;
            ActorId = actorId ?? string.Empty;
            Coord = coord;
            HexDistanceFromPlayer = hexDistanceFromPlayer;
            Framed = framed;
            FramedWithMargin = framedWithMargin;
        }

        public int OverallTurnNumber { get; }

        /// <summary>The presentation sequence the event belonged to (e.g. <c>monster-movement</c>).</summary>
        public string PhaseLabel { get; }

        /// <summary>What was presented (e.g. <c>enemy-move</c>, <c>effect:Damage</c>).</summary>
        public string EventLabel { get; }

        public string ActorId { get; }
        public HexCoord Coord { get; }
        public int HexDistanceFromPlayer { get; }

        /// <summary>Framed at margin 0 — the exact viewport test.</summary>
        public bool Framed { get; }

        /// <summary>
        /// Framed at the margin the feature will actually gate on. Recorded alongside <see cref="Framed"/> so
        /// the margin can be re-judged from one playtest instead of requiring the session to be replayed.
        /// </summary>
        public bool FramedWithMargin { get; }
    }

    /// <summary>
    /// How many camera cuts a given coalesce radius would have cost, measured over the recorded samples.
    /// </summary>
    public readonly struct CombatActionFocusClusterStats
    {
        public CombatActionFocusClusterStats(
            int coalesceRadiusHexes,
            int phaseCount,
            int clusterCount,
            int maxClustersInOnePhase,
            int maxClusterSize,
            double meanClustersPerPhase,
            double meanClusterSize)
        {
            CoalesceRadiusHexes = coalesceRadiusHexes;
            PhaseCount = phaseCount;
            ClusterCount = clusterCount;
            MaxClustersInOnePhase = maxClustersInOnePhase;
            MaxClusterSize = maxClusterSize;
            MeanClustersPerPhase = meanClustersPerPhase;
            MeanClusterSize = meanClusterSize;
        }

        public int CoalesceRadiusHexes { get; }

        /// <summary>Phase instances (turn + phase label) that had at least one off-screen event.</summary>
        public int PhaseCount { get; }

        public int ClusterCount { get; }

        /// <summary>The worst phase — this is what <c>maxFocusPerPhase</c> has to cover.</summary>
        public int MaxClustersInOnePhase { get; }

        public int MaxClusterSize { get; }
        public double MeanClustersPerPhase { get; }
        public double MeanClusterSize { get; }
    }

    /// <summary>
    /// Session-long accumulator for the action-focus measurement pass
    /// (docs/monster-action-camera-focus-plan.md §5 P0). It answers the two questions the plan leaves
    /// open — how wide <c>coalesceRadiusHexes</c> has to be, and how many cuts <c>maxFocusPerPhase</c>
    /// has to allow — from a real playthrough rather than from the estimate in §6.
    ///
    /// Deliberately NOT built on <see cref="CombatPresentationTrace"/>: that recorder is scoped to a single
    /// sequence, caps at 512 entries, and holds each sequence open for a tail wait, which perturbs exactly
    /// the timings this feature must leave untouched. This one accumulates across the whole session, records
    /// only on the calling thread with no waits, and never influences presentation.
    ///
    /// Ambient static for the same reason as the trace recorder: the emitting sites are sink methods on a
    /// MonoBehaviour reached through no common object, and recording is a single boolean test when off.
    /// Kept free of UnityEngine so the summary maths is unit-testable.
    /// </summary>
    public static class CombatActionFocusMetrics
    {
        /// <summary>
        /// Backstop for a recorder left armed for a very long session. Generous because the whole point is a
        /// multi-turn accumulation; a 5–10 turn scenario produces low hundreds of samples.
        /// </summary>
        public const int MaxSamples = 20000;

        /// <summary>Candidate coalesce radii the summary evaluates, spanning §6's provisional 5.</summary>
        public static readonly int[] CandidateCoalesceRadii = { 1, 2, 3, 4, 5, 6, 7, 8 };

        private static readonly List<CombatActionFocusSample> Samples = new List<CombatActionFocusSample>();

        /// <summary>When false, <see cref="Record"/> returns immediately.</summary>
        public static bool IsEnabled { get; private set; }

        /// <summary>True once <see cref="MaxSamples"/> was hit and later samples were dropped.</summary>
        public static bool Truncated { get; private set; }

        public static int SampleCount => Samples.Count;

        public static IReadOnlyList<CombatActionFocusSample> RecordedSamples => Samples;

        /// <summary>Arms recording. Keeps whatever was already accumulated, so a session can be paused.</summary>
        public static void Enable() => IsEnabled = true;

        /// <summary>Disarms recording without discarding what was accumulated.</summary>
        public static void Disable() => IsEnabled = false;

        /// <summary>Drops every accumulated sample. Leaves the armed state alone.</summary>
        public static void Clear()
        {
            Samples.Clear();
            Truncated = false;
        }

        /// <summary>Disarms and drops everything. For tests, so one cannot leak into the next.</summary>
        public static void Reset()
        {
            Clear();
            IsEnabled = false;
        }

        public static void Record(CombatActionFocusSample sample)
        {
            if (!IsEnabled)
            {
                return;
            }

            if (Samples.Count >= MaxSamples)
            {
                Truncated = true;
                return;
            }

            Samples.Add(sample);
        }

        /// <summary>
        /// Groups the off-screen samples by the phase instance they occurred in and single-link clusters each
        /// group by hex distance, which is what one camera framing can cover. Single linkage (transitive:
        /// A–B and B–C merge even when A–C exceeds the radius) matches the plan's model — the framing point is
        /// the cluster's bounding-box centre, so a chain stays in one shot.
        ///
        /// <paramref name="useMargin"/> selects which framed flag counts as "already on screen".
        /// </summary>
        public static CombatActionFocusClusterStats ComputeClusterStats(int coalesceRadiusHexes, bool useMargin)
        {
            var radius = Math.Max(0, coalesceRadiusHexes);
            var phases = GroupOffScreenSamplesByPhase(useMargin);

            var clusterCount = 0;
            var maxClustersInOnePhase = 0;
            var maxClusterSize = 0;
            var totalClustered = 0;

            foreach (var phase in phases)
            {
                var sizes = ClusterSizes(phase.Value, radius);
                clusterCount += sizes.Count;
                maxClustersInOnePhase = Math.Max(maxClustersInOnePhase, sizes.Count);
                for (var i = 0; i < sizes.Count; i++)
                {
                    maxClusterSize = Math.Max(maxClusterSize, sizes[i]);
                    totalClustered += sizes[i];
                }
            }

            var phaseCount = phases.Count;
            return new CombatActionFocusClusterStats(
                radius,
                phaseCount,
                clusterCount,
                maxClustersInOnePhase,
                maxClusterSize,
                phaseCount == 0 ? 0d : (double)clusterCount / phaseCount,
                clusterCount == 0 ? 0d : (double)totalClustered / clusterCount);
        }

        /// <summary>
        /// The whole measurement, rendered for the console: what fraction of events were already framed, the
        /// per-phase off-screen counts, the distance histogram, and the cluster table that fixes §6's two
        /// provisional values. Both margin variants are reported so the margin choice is visible too.
        /// </summary>
        public static string FormatSummary(bool useMargin = true)
        {
            var builder = new StringBuilder();
            builder.Append("[action-focus-metrics] samples=").Append(Samples.Count);
            if (Truncated)
            {
                builder.Append(" (상한 초과로 이후 표본 누락)");
            }

            builder.AppendLine();

            if (Samples.Count == 0)
            {
                builder.AppendLine("  표본 없음 — 계측을 켠 뒤 몬스터 페이즈를 진행하세요.");
                return builder.ToString();
            }

            var framedNoMargin = 0;
            var framedWithMargin = 0;
            for (var i = 0; i < Samples.Count; i++)
            {
                if (Samples[i].Framed) framedNoMargin++;
                if (Samples[i].FramedWithMargin) framedWithMargin++;
            }

            builder.Append("  화면 내 비율: margin 0 = ").Append(Percent(framedNoMargin, Samples.Count))
                .Append(" (").Append(framedNoMargin).Append('/').Append(Samples.Count).Append(')')
                .Append(", margin 적용 = ").Append(Percent(framedWithMargin, Samples.Count))
                .Append(" (").Append(framedWithMargin).Append('/').Append(Samples.Count).Append(')')
                .AppendLine();
            builder.Append("  판정 기준: ").Append(useMargin ? "margin 적용" : "margin 0").AppendLine();

            AppendPerPhaseSection(builder, useMargin);
            AppendDistanceSection(builder, useMargin);
            AppendEventLabelSection(builder, useMargin);
            AppendClusterSection(builder, useMargin);

            return builder.ToString();
        }

        private static void AppendPerPhaseSection(StringBuilder builder, bool useMargin)
        {
            var phases = GroupOffScreenSamplesByPhase(useMargin);
            builder.AppendLine();
            builder.Append("  ■ 페이즈당 화면 밖 이벤트 수 (화면 밖 이벤트가 있던 페이즈 ")
                .Append(phases.Count).AppendLine("개)");

            if (phases.Count == 0)
            {
                builder.AppendLine("    없음 — 모든 이벤트가 화면 안이었습니다.");
                return;
            }

            var histogram = new SortedDictionary<int, int>();
            var max = 0;
            var total = 0;
            foreach (var phase in phases)
            {
                var count = phase.Value.Count;
                histogram.TryGetValue(count, out var seen);
                histogram[count] = seen + 1;
                max = Math.Max(max, count);
                total += count;
            }

            foreach (var bucket in histogram)
            {
                builder.Append("    ").Append(bucket.Key).Append("건: ")
                    .Append(bucket.Value).AppendLine(" 페이즈");
            }

            builder.Append("    최대 ").Append(max).Append("건 / 평균 ")
                .Append(Number((double)total / phases.Count)).AppendLine("건");
        }

        private static void AppendDistanceSection(StringBuilder builder, bool useMargin)
        {
            builder.AppendLine();
            builder.AppendLine("  ■ 플레이어로부터의 헥스 거리 분포 (화면 밖 이벤트만)");

            var histogram = new SortedDictionary<int, int>();
            var total = 0;
            var max = 0;
            for (var i = 0; i < Samples.Count; i++)
            {
                if (IsFramed(Samples[i], useMargin))
                {
                    continue;
                }

                var distance = Samples[i].HexDistanceFromPlayer;
                histogram.TryGetValue(distance, out var seen);
                histogram[distance] = seen + 1;
                total += distance;
                max = Math.Max(max, distance);
            }

            if (histogram.Count == 0)
            {
                builder.AppendLine("    없음");
                return;
            }

            var count = 0;
            foreach (var bucket in histogram)
            {
                builder.Append("    r").Append(bucket.Key).Append(": ").Append(bucket.Value).AppendLine("건");
                count += bucket.Value;
            }

            builder.Append("    최대 r").Append(max).Append(" / 평균 r")
                .Append(Number((double)total / count)).AppendLine();
        }

        private static void AppendEventLabelSection(StringBuilder builder, bool useMargin)
        {
            builder.AppendLine();
            builder.AppendLine("  ■ 이벤트 종류별 (화면 밖 / 전체) — 게이트는 종류를 보지 않지만 무엇이 잡히는지 확인용");

            var offScreen = new Dictionary<string, int>(StringComparer.Ordinal);
            var totals = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < Samples.Count; i++)
            {
                var label = Samples[i].EventLabel;
                totals.TryGetValue(label, out var seenTotal);
                totals[label] = seenTotal + 1;
                if (!IsFramed(Samples[i], useMargin))
                {
                    offScreen.TryGetValue(label, out var seenOff);
                    offScreen[label] = seenOff + 1;
                }
            }

            var labels = new List<string>(totals.Keys);
            labels.Sort(StringComparer.Ordinal);
            foreach (var label in labels)
            {
                offScreen.TryGetValue(label, out var off);
                builder.Append("    ").Append(label).Append(": ").Append(off)
                    .Append(" / ").Append(totals[label]).AppendLine();
            }
        }

        private static void AppendClusterSection(StringBuilder builder, bool useMargin)
        {
            builder.AppendLine();
            builder.AppendLine("  ■ 병합 반경별 클러스터 (→ coalesceRadiusHexes / maxFocusPerPhase 확정 근거)");
            builder.AppendLine("    반경 | 클러스터 | 페이즈당최대 | 페이즈당평균 | 최대크기 | 평균크기");

            for (var i = 0; i < CandidateCoalesceRadii.Length; i++)
            {
                var stats = ComputeClusterStats(CandidateCoalesceRadii[i], useMargin);
                builder.Append("    ").Append(stats.CoalesceRadiusHexes.ToString(CultureInfo.InvariantCulture).PadLeft(4))
                    .Append(" | ").Append(stats.ClusterCount.ToString(CultureInfo.InvariantCulture).PadLeft(8))
                    .Append(" | ").Append(stats.MaxClustersInOnePhase.ToString(CultureInfo.InvariantCulture).PadLeft(12))
                    .Append(" | ").Append(Number(stats.MeanClustersPerPhase).PadLeft(12))
                    .Append(" | ").Append(stats.MaxClusterSize.ToString(CultureInfo.InvariantCulture).PadLeft(8))
                    .Append(" | ").Append(Number(stats.MeanClusterSize).PadLeft(8))
                    .AppendLine();
            }
        }

        /// <summary>
        /// Off-screen samples bucketed by the phase instance they belong to (turn number + phase label),
        /// which is the unit <c>maxFocusPerPhase</c> is budgeted against. Ordered by first appearance so the
        /// output reads in play order.
        /// </summary>
        private static List<KeyValuePair<string, List<CombatActionFocusSample>>> GroupOffScreenSamplesByPhase(bool useMargin)
        {
            var order = new List<string>();
            var buckets = new Dictionary<string, List<CombatActionFocusSample>>(StringComparer.Ordinal);

            for (var i = 0; i < Samples.Count; i++)
            {
                var sample = Samples[i];
                if (IsFramed(sample, useMargin))
                {
                    continue;
                }

                var key = sample.OverallTurnNumber.ToString(CultureInfo.InvariantCulture) + "|" + sample.PhaseLabel;
                if (!buckets.TryGetValue(key, out var bucket))
                {
                    bucket = new List<CombatActionFocusSample>();
                    buckets[key] = bucket;
                    order.Add(key);
                }

                bucket.Add(sample);
            }

            var result = new List<KeyValuePair<string, List<CombatActionFocusSample>>>(order.Count);
            for (var i = 0; i < order.Count; i++)
            {
                result.Add(new KeyValuePair<string, List<CombatActionFocusSample>>(order[i], buckets[order[i]]));
            }

            return result;
        }

        private static bool IsFramed(CombatActionFocusSample sample, bool useMargin)
            => useMargin ? sample.FramedWithMargin : sample.Framed;

        /// <summary>
        /// Single-link cluster sizes for one phase's samples. O(n²) over the samples of a single phase, which
        /// is a handful of events — the clarity is worth more here than the asymptotics.
        /// </summary>
        private static List<int> ClusterSizes(List<CombatActionFocusSample> samples, int radius)
        {
            var sizes = new List<int>();
            if (samples.Count == 0)
            {
                return sizes;
            }

            var assigned = new bool[samples.Count];
            var pending = new List<int>();

            for (var seed = 0; seed < samples.Count; seed++)
            {
                if (assigned[seed])
                {
                    continue;
                }

                assigned[seed] = true;
                pending.Clear();
                pending.Add(seed);
                var size = 0;

                while (pending.Count > 0)
                {
                    var current = pending[pending.Count - 1];
                    pending.RemoveAt(pending.Count - 1);
                    size++;

                    for (var other = 0; other < samples.Count; other++)
                    {
                        if (assigned[other]
                            || samples[current].Coord.DistanceTo(samples[other].Coord) > radius)
                        {
                            continue;
                        }

                        assigned[other] = true;
                        pending.Add(other);
                    }
                }

                sizes.Add(size);
            }

            return sizes;
        }

        private static string Percent(int part, int total)
            => total == 0 ? "0%" : (100d * part / total).ToString("0.#", CultureInfo.InvariantCulture) + "%";

        private static string Number(double value)
            => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}

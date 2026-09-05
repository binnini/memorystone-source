using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 보스 한 마리의 확장 저작 데이터(<c>boss_profiles.csv</c> 한 행 + 딸린
    /// <c>boss_phases.csv</c> 행들). 보스도 <c>monster_catalog.csv</c>의 평범한 몬스터 행을
    /// 그대로 쓰고, 이 프로필은 <see cref="BossId"/>(=monsterId)로 조인되는 순수 추가 데이터다.
    /// </summary>
    public sealed class BossProfileDefinition
    {
        private readonly List<BossPhaseDefinition> phases;

        private readonly Dictionary<string, string> mechanicParams;

        public BossProfileDefinition(
            string bossId,
            string displayName,
            BossPhaseMetricKind phaseMetric,
            string mechanicId,
            IReadOnlyDictionary<string, string> mechanicParams,
            string introCinematic,
            string deathCinematic,
            string bgmCueBase,
            string designerNote,
            IEnumerable<BossPhaseDefinition> phases)
        {
            BossId = string.IsNullOrWhiteSpace(bossId)
                ? throw new ArgumentException("Boss profile bossId is required.", nameof(bossId))
                : bossId;
            DisplayName = displayName ?? string.Empty;
            PhaseMetric = phaseMetric;
            MechanicId = mechanicId ?? string.Empty;
            this.mechanicParams = mechanicParams == null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(
                    mechanicParams.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                    StringComparer.Ordinal);
            IntroCinematic = introCinematic ?? string.Empty;
            DeathCinematic = deathCinematic ?? string.Empty;
            BgmCueBase = bgmCueBase ?? string.Empty;
            DesignerNote = designerNote ?? string.Empty;
            this.phases = (phases ?? Array.Empty<BossPhaseDefinition>())
                .OrderBy(phase => phase.PhaseIndex)
                .ToList();
            if (this.phases.Count == 0)
            {
                throw new ArgumentException($"Boss '{BossId}' has no phase rows.", nameof(phases));
            }
        }

        /// <summary>보스 몬스터 id(=<c>monster_catalog.csv</c>의 monsterId).</summary>
        public string BossId { get; }

        public string DisplayName { get; }
        public BossPhaseMetricKind PhaseMetric { get; }

        /// <summary>
        /// 보스 고유 기믹 디스패치 키. 빈 값이면 기믹 없는 보스(페이즈만)다.
        /// 비어 있지 않으면 <see cref="BossMechanicRegistry"/>에 구현이 등록되어 있어야 하며,
        /// 파서가 미등록 id를 거부한다.
        /// </summary>
        public string MechanicId { get; }

        /// <summary>
        /// 기믹 튜너블. <c>mechanicParams</c> 컬럼의 <c>key=value;key=value</c> 저작을 그대로 담는다.
        /// 이름 있는 키를 쓰는 이유: 익명 param1..N은 CSV를 보고 무슨 수치인지 알 수 없고, 기믹마다
        /// 필요한 개수도 달라 스키마가 계속 늘어난다. 필요한 키는 기믹이 선언하고
        /// (<see cref="IBossMechanic.RequiredParamKeys"/>) 파서가 누락을 거부한다.
        /// </summary>
        public IReadOnlyDictionary<string, string> MechanicParams => mechanicParams;

        /// <summary>정수 튜너블. 키가 없거나 정수가 아니면 <paramref name="fallback"/>.</summary>
        public int GetMechanicInt(string key, int fallback = 0)
        {
            return mechanicParams.TryGetValue(key ?? string.Empty, out var value)
                   && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;
        }

        /// <summary>
        /// 정수 리스트 튜너블(<c>5|6|7</c> 형태). 페이즈별로 값이 갈리는 수치를 페이즈 표를 오염시키지 않고
        /// 기믹 국소적으로 저작하기 위한 것이다. 키가 없거나 비어 있으면 빈 목록.
        /// </summary>
        /// <exception cref="ArgumentException">조각 하나라도 정수가 아니면. 조용히 건너뛰면 저작자가 오타를 알 수 없다.</exception>
        public IReadOnlyList<int> GetMechanicIntList(string key)
        {
            return ParseIntList(mechanicParams.TryGetValue(key ?? string.Empty, out var value) ? value : string.Empty, key);
        }

        /// <summary>
        /// 리스트 저작의 파싱 정본. 파서(저작 시점 검증)와 런타임(값 읽기)이 같은 함수를 써야
        /// "파싱은 통과했는데 런타임에서 다르게 읽히는" 어긋남이 생기지 않는다.
        /// </summary>
        public static IReadOnlyList<int> ParseIntList(string value, string keyForError)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Array.Empty<int>();
            }

            var parts = value.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new int[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result[i]))
                {
                    throw new ArgumentException(
                        $"mechanicParams '{keyForError}' entry '{parts[i].Trim()}' must be an integer (list form is 'a|b|c').");
                }
            }

            return result;
        }

        /// <summary>문자열 튜너블(예: 기믹이 소환할 prop의 monsterId). 키가 없으면 <paramref name="fallback"/>.</summary>
        public string GetMechanicString(string key, string fallback = "")
        {
            return mechanicParams.TryGetValue(key ?? string.Empty, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : fallback;
        }

        public string IntroCinematic { get; }
        public string DeathCinematic { get; }

        /// <summary>페이즈별 BGM 큐의 베이스(예: <c>music.boss.bulgasal</c> → <c>.p1/.p2/.p3</c>).</summary>
        public string BgmCueBase { get; }

        public string DesignerNote { get; }

        public IReadOnlyList<BossPhaseDefinition> Phases => phases;
        public int PhaseCount => phases.Count;

        public bool HasMechanic => !string.IsNullOrWhiteSpace(MechanicId);

        /// <summary>
        /// <c>|</c> 구분 다중 저작을 푼 개별 기믹 id 목록(§13.5). 단일 저작이면 원소 1개,
        /// 기믹 없는 보스면 빈 목록이다. 디스패치는 이 목록을 순회한다.
        /// </summary>
        public IReadOnlyList<string> MechanicIds => BossMechanicRegistry.SplitMechanicIds(MechanicId);

        /// <summary>1-based 페이즈 정의. 범위를 벗어난 값은 양끝으로 클램프한다.</summary>
        public BossPhaseDefinition GetPhase(int phaseIndex)
        {
            var clamped = Math.Min(Math.Max(phaseIndex, 1), phases.Count);
            return phases[clamped - 1];
        }

        /// <summary>페이즈별 BGM 큐 id. 베이스가 비어 있으면 빈 문자열.</summary>
        public string GetBgmCueId(int phaseIndex)
        {
            if (string.IsNullOrWhiteSpace(BgmCueBase))
            {
                return string.Empty;
            }

            return $"{BgmCueBase}.p{Math.Min(Math.Max(phaseIndex, 1), phases.Count)}";
        }

        /// <summary>
        /// 정규화된 progress 값이 도달한 최고 페이즈. 지표가 한 번에 크게 뛰어도 정확한 페이즈로 간다.
        /// </summary>
        public int ResolvePhaseForProgress(int progress)
        {
            var resolved = 1;
            for (var i = 0; i < phases.Count; i++)
            {
                if (progress >= phases[i].ProgressThreshold)
                {
                    resolved = phases[i].PhaseIndex;
                }
            }

            return resolved;
        }
    }
}

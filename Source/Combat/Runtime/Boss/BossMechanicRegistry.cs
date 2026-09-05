using System;
using System.Collections.Generic;
using System.Linq;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 구현된 보스 기믹의 등록부. <c>boss_profiles.csv</c>의 <c>mechanicId</c>·<c>mechanicParams</c> 검증도
    /// 여기를 통해 이뤄지므로, 저작만으로 존재하지 않는 기믹을 가리키거나 필수 튜너블을 빼먹는 일이
    /// 파싱 시점에 막힌다(<c>shapeId</c> 검증과 같은 선례).
    ///
    /// 기믹은 상태를 갖지 않으므로 싱글턴 인스턴스를 공유한다.
    /// </summary>
    public static class BossMechanicRegistry
    {
        private static readonly Dictionary<string, IBossMechanic> Mechanics = Create(
            // 기믹 구현체를 추가하면 여기에 한 줄 등록한다.
            new IronScrapMechanic(),
            new AnnihilationMechanic(),
            new WeakSpotMechanic(),
            new TrapVolleyMechanic(),
            new ScrapChainMechanic(),
            new GuardMechanic());

        private static Dictionary<string, IBossMechanic> Create(params IBossMechanic[] mechanics)
        {
            return mechanics.ToDictionary(mechanic => mechanic.MechanicId, StringComparer.Ordinal);
        }

        /// <summary>등록된 기믹 id 목록(진단·에러 메시지용).</summary>
        public static IReadOnlyList<string> RegisteredIds =>
            Mechanics.Keys.OrderBy(id => id, StringComparer.Ordinal).ToList();

        /// <summary>
        /// <c>mechanicId</c> 컬럼의 <c>|</c> 구분 다중 저작을 개별 id로 푼다(§13.5 — 한 보스가
        /// 철조각과 전멸기를 함께 가질 수 있어야 한다). 빈 값은 빈 목록.
        /// </summary>
        public static IReadOnlyList<string> SplitMechanicIds(string mechanicId)
        {
            return string.IsNullOrWhiteSpace(mechanicId)
                ? Array.Empty<string>()
                : mechanicId.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(id => id.Trim())
                    .Where(id => id.Length > 0)
                    .ToArray();
        }

        /// <summary>빈 값은 "기믹 없음"이라 유효하다. <c>|</c> 목록이면 전부 등록되어 있어야 한다.</summary>
        public static bool IsValidMechanicId(string mechanicId)
        {
            return SplitMechanicIds(mechanicId).All(Mechanics.ContainsKey);
        }

        /// <summary>
        /// 이 기믹(들)이 요구하는 <c>mechanicParams</c> 키들. 미등록/빈 id면 빈 목록(요구 없음).
        /// </summary>
        public static IReadOnlyList<string> GetRequiredParamKeys(string mechanicId)
        {
            return SplitMechanicIds(mechanicId)
                .SelectMany(id => TryGet(id, out var mechanic) ? mechanic.RequiredParamKeys : Array.Empty<string>())
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// 기믹별 추가 저작 검증(<c>|</c> 목록이면 전부). 미등록/빈 id면 아무것도 하지 않는다.
        /// 페이즈 수를 요구하므로 파서가 페이즈 행을 다 읽은 뒤에 부른다.
        /// </summary>
        public static void ValidateParams(string mechanicId, IReadOnlyDictionary<string, string> mechanicParams, int phaseCount)
        {
            foreach (var id in SplitMechanicIds(mechanicId))
            {
                if (TryGet(id, out var mechanic))
                {
                    mechanic.ValidateParams(
                        mechanicParams ?? new Dictionary<string, string>(StringComparer.Ordinal),
                        phaseCount);
                }
            }
        }

        internal static bool TryGet(string mechanicId, out IBossMechanic mechanic)
        {
            if (string.IsNullOrWhiteSpace(mechanicId))
            {
                mechanic = null;
                return false;
            }

            return Mechanics.TryGetValue(mechanicId, out mechanic);
        }
    }
}

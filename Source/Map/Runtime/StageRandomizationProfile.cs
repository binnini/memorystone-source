using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SeoulPlayup.Map.Runtime
{
    /// <summary>슬롯의 진행도 지대(placement-randomization-plan §4). PlayerSpawn 기준 보행 BFS 거리로 계산한다.</summary>
    /// <summary>
    /// 풀 행이 설 수 있는 <b>가장 이른</b> 지대. 순서가 의미다(Early &lt; Mid &lt; Late) —
    /// 저작값은 <b>하한</b>이고, 그 지대<b>부터 그 뒤로 전부</b> 설 수 있다.
    ///
    /// <para>🔑 이것이 「초반에는 약한 몬스터」의 손잡이다(2026-09-01 #14). 종전에는 정확히 일치로
    /// 봤는데, 그러면 「중반부터 나온다」를 한 행으로 못 적고 mid·late 두 행으로 쪼개야 했다 —
    /// 가중치가 두 벌이 되어 저작이 조용히 갈린다. <b>현행 저작에는 영향이 없다</b>: 쓰이는 값이
    /// <c>any</c>와 <c>late</c>뿐이고 late는 마지막 지대라 하한과 일치가 같은 뜻이기 때문이다.</para>
    /// </summary>
    public enum PlacementZone
    {
        Any,
        Early,
        Mid,
        Late,
    }

    /// <summary>
    /// 스테이지별 배치 랜덤화 정책(§2-3b). 정본은 CSV 두 장 —
    /// stage_randomization.csv(스테이지 헤더) + stage_randomization_pools.csv(풀 행).
    /// 몬스터별 위협 비용(threatCost)은 이 표가 정본이다(monster_catalog.csv 무접촉).
    /// </summary>
    public sealed class StageRandomizationProfile
    {
        public StageRandomizationProfile(
            string stageId,
            bool enabled,
            int threatBudget,
            int budgetTolerancePct,
            int safeRadius,
            int rerollLimit,
            int densityRadius,
            int densityCap,
            int zoneEarlyMaxBfs,
            int zoneMidMaxBfs,
            IReadOnlyList<StageRandomizationPoolEntry> pools,
            int trapThreatBudget = 0,
            int chestMinDistance = -1,
            IReadOnlyList<PlacementBanRule> bans = null,
            int trapMinDistance = 0,
            int eliteMin = 0,
            int monsterMinKinds = 0,
            int eliteHpPercent = 100,
            int eliteDamagePercent = 100,
            int serviceShopCount = 0,
            int serviceCamperCount = 0,
            int serviceMinDistance = 0,
            int serviceRouteMin = 0,
            int eliteMinDistance = 0)
        {
            StageId = stageId ?? string.Empty;
            Enabled = enabled;
            ThreatBudget = Math.Max(0, threatBudget);
            BudgetTolerancePct = Math.Max(0, budgetTolerancePct);
            SafeRadius = Math.Max(0, safeRadius);
            RerollLimit = Math.Max(1, rerollLimit);
            DensityRadius = Math.Max(1, densityRadius);
            DensityCap = Math.Max(1, densityCap);
            ZoneEarlyMaxBfs = Math.Max(0, zoneEarlyMaxBfs);
            ZoneMidMaxBfs = Math.Max(ZoneEarlyMaxBfs, zoneMidMaxBfs);
            Pools = pools ?? Array.Empty<StageRandomizationPoolEntry>();
            TrapThreatBudget = Math.Max(0, trapThreatBudget);
            ChestMinDistance = Math.Max(-1, chestMinDistance);
            Bans = bans ?? Array.Empty<PlacementBanRule>();
            TrapMinDistance = Math.Max(0, trapMinDistance);
            EliteMin = Math.Max(0, eliteMin);
            MonsterMinKinds = Math.Max(0, monsterMinKinds);
            EliteHpPercent = Math.Max(1, eliteHpPercent);
            EliteDamagePercent = Math.Max(1, eliteDamagePercent);
            ServiceShopCount = Math.Max(0, serviceShopCount);
            ServiceCamperCount = Math.Max(0, serviceCamperCount);
            ServiceMinDistance = Math.Max(0, serviceMinDistance);
            ServiceRouteMin = Math.Max(0, serviceRouteMin);
            EliteMinDistance = Math.Max(0, eliteMinDistance);
        }

        public string StageId { get; }
        public bool Enabled { get; }
        public int ThreatBudget { get; }
        public int BudgetTolerancePct { get; }
        public int SafeRadius { get; }
        public int RerollLimit { get; }
        public int DensityRadius { get; }
        public int DensityCap { get; }
        public int ZoneEarlyMaxBfs { get; }
        public int ZoneMidMaxBfs { get; }
        public IReadOnlyList<StageRandomizationPoolEntry> Pools { get; }

        /// <summary>
        /// 한 판에 나와야 하는 <b>서로 다른 몬스터 종류</b>의 최소 수(2026-08-20 #12). 0 = 규칙 없음.
        /// 가중치 추첨만으로는 무거운 가중치 하나가 판을 독식해 "종류가 단일하게 나온다"는 체감이
        /// 생긴다 — capGroup은 <b>상한</b>만 걸므로 하한은 EliteMin과 같은 재롤 게이트로 보장한다.
        /// </summary>
        public int MonsterMinKinds { get; }

        /// <summary>
        /// 엘리트 몬스터의 체력 배율(퍼센트, 100 = 배율 없음 · 2026-08-20 #12 사용자 확정).
        /// 「엘리트」가 분류일 뿐 스탯 차별화가 없던 것을 이 표 한 곳으로 연다 — 몬스터별 엘리트
        /// 변형 행을 두면 패턴 바인딩까지 복제해야 하고 값이 두 곳으로 갈라진다.
        /// </summary>
        public int EliteHpPercent { get; }

        /// <summary>엘리트 몬스터의 가하는 피해 배율(퍼센트, 100 = 배율 없음). <see cref="EliteHpPercent"/>와 한 쌍.</summary>
        public int EliteDamagePercent { get; }

        /// <summary>P2 함정 위협 예산(몬스터 예산과 별도, §5). 0 = 함정 랜덤화 비활성.</summary>
        public int TrapThreatBudget { get; }

        /// <summary>P2 상자 간 최소 거리(§5, Q8 확정 4). 음수 = 상자 셔플 비활성.</summary>
        public int ChestMinDistance { get; }

        /// <summary>
        /// P3 서비스 추첨 — 한 판에 세울 잡화점 수(2026-09-01 확정 3). 0 = 서비스 추첨 비활성이며,
        /// 이 경우 저작된 자리에 그대로 선다(폴백과 같은 그림).
        /// </summary>
        public int ServiceShopCount { get; }

        /// <summary>P3 서비스 추첨 — 한 판에 세울 캠핑카 수(확정 3). <see cref="ServiceShopCount"/>와 한 쌍.</summary>
        public int ServiceCamperCount { get; }

        /// <summary>
        /// 같은 종류 서비스 두 개 사이의 최소 거리(확정 10). 종류가 다르면 보지 않는다 —
        /// 잡화점 옆 캠핑카는 「한 번에 두 서비스」라 오히려 좋은 그림이라는 판단.
        /// </summary>
        public int ServiceMinDistance { get; }

        /// <summary>
        /// 어느 루트를 걷든 각 종류를 최소 몇 번 만나야 하는가(확정 2). 0 = 루트 검증 없음.
        /// 루트가 어디인지는 코드가 모른다 — 저작 슬롯의 randomizationGroup 태그가 정본이다.
        /// </summary>
        public int ServiceRouteMin { get; }

        /// <summary>P2 금지 조합 표(stage_randomization_bans.csv, Q11).</summary>
        public IReadOnlyList<PlacementBanRule> Bans { get; }

        /// <summary>
        /// 함정 간 최소 거리(Q-A2, 2026-08-19 #4). 0 = 규칙 꺼짐(기존 슬롯 선택과 동작 동일).
        /// N&gt;0이면 선택된 함정 슬롯의 쌍별 헥스 거리가 전 그룹에 걸쳐 N 이상이어야 한다 —
        /// 저작 점유 슬롯이 서로 붙어 있으면 예비 슬롯 없이는 만족할 수 없다.
        /// </summary>
        public int TrapMinDistance { get; }

        /// <summary>
        /// role=elite 스폰 최소 보장(#18, 난이도 손잡이). 0 = 하한 없음(기존과 동일).
        /// 추첨 결과의 elite 수가 미달이면 스테이지 단위 재롤로 다시 뽑는다.
        /// </summary>
        public int EliteMin { get; }

        /// <summary>
        /// role=elite 스폰 <b>사이</b>의 최소 거리(2026-09-01 #17). 0 = 규칙 꺼짐(기존과 동일).
        ///
        /// <para>🔴 정예끼리는 여태 <b>아무 거리 제약도 없었다</b>. bans 표의
        /// <c>SpawnMonsters↔elite adjacent 1</c>은 <b>함정</b>과 정예 사이의 것이고, 밀도 상한도
        /// 정예 둘(3+3=6)은 반경 2 상한 8 아래라 통과시킨다 — 나란히 선 정예 둘이 규칙상 정상이었다.
        /// 문법은 <see cref="TrapMinDistance"/>의 복제다(새 축을 만들지 않는다).</para>
        /// </summary>
        public int EliteMinDistance { get; }

        public int BudgetMin => (int)Math.Floor(ThreatBudget * (100 - BudgetTolerancePct) / 100.0);
        public int BudgetMax => (int)Math.Ceiling(ThreatBudget * (100 + BudgetTolerancePct) / 100.0);

        /// <summary>함정 예산 창 — 허용 오차는 몬스터와 같은 budgetTolerancePct를 공유한다.</summary>
        public int TrapBudgetMin => (int)Math.Floor(TrapThreatBudget * (100 - BudgetTolerancePct) / 100.0);
        public int TrapBudgetMax => (int)Math.Ceiling(TrapThreatBudget * (100 + BudgetTolerancePct) / 100.0);

        /// <summary>
        /// 서비스 개수만 바꾼 사본. 시드 스트림 독립 감사(「서비스를 껐을 때와 켰을 때 몬스터·함정이
        /// 같은가」)가 쓴다.
        ///
        /// <para>🔴 <b>손으로 재조립하지 말 것.</b> 감사가 생성자를 직접 부르던 시절, 새로 붙은
        /// <c>eliteMinDistance</c>가 조용히 기본값 0으로 떨어져 「서비스가 몬스터를 흔들었다」는
        /// <b>가짜 실패</b>가 났다(2026-09-01 #17). 컬럼이 늘어도 여기 한 곳만 지나면 안 빠진다 —
        /// 순서가 아니라 구조가 지킨다.</para>
        /// </summary>
        public StageRandomizationProfile WithServiceCounts(int shopCount, int camperCount)
        {
            return new StageRandomizationProfile(
                StageId, Enabled, ThreatBudget, BudgetTolerancePct, SafeRadius, RerollLimit,
                DensityRadius, DensityCap, ZoneEarlyMaxBfs, ZoneMidMaxBfs, Pools,
                TrapThreatBudget, ChestMinDistance, Bans, TrapMinDistance, EliteMin,
                MonsterMinKinds, EliteHpPercent, EliteDamagePercent,
                shopCount, camperCount, ServiceMinDistance, ServiceRouteMin,
                EliteMinDistance);
        }

        public PlacementZone ZoneForBfsDistance(int bfsDistance)
        {
            if (bfsDistance <= ZoneEarlyMaxBfs) return PlacementZone.Early;
            if (bfsDistance <= ZoneMidMaxBfs) return PlacementZone.Mid;
            return PlacementZone.Late;
        }
    }

    public sealed class StageRandomizationPoolEntry
    {
        public StageRandomizationPoolEntry(
            string group,
            string entryKind,
            string entryRef,
            int threatCost,
            int maxCount,
            string capGroup,
            int weight,
            PlacementZone zone,
            string role)
        {
            Group = group ?? string.Empty;
            EntryKind = entryKind ?? string.Empty;
            EntryRef = entryRef ?? string.Empty;
            ThreatCost = Math.Max(0, threatCost);
            MaxCount = Math.Max(1, maxCount);
            CapGroup = capGroup ?? string.Empty;
            Weight = Math.Max(0, weight);
            Zone = zone;
            Role = role ?? string.Empty;
        }

        public string Group { get; }
        public string EntryKind { get; }
        public string EntryRef { get; }
        public int ThreatCost { get; }
        public int MaxCount { get; }
        /// <summary>합산 상한 키(예: 터렛류 M902~904 공동 상한). 빈 값이면 EntryRef 단독 상한.</summary>
        public string CapGroup { get; }
        public int Weight { get; }
        public PlacementZone Zone { get; }
        /// <summary>배정 시 스폰에 실릴 role(elite 등). 구성이 판마다 바뀌므로 role은 풀 행이 정본이다.</summary>
        public string Role { get; }

        public string CapKey => string.IsNullOrWhiteSpace(CapGroup) ? EntryRef : CapGroup;
        public bool IsMonster => string.Equals(EntryKind, "monster", StringComparison.OrdinalIgnoreCase);

        /// <summary>P2 함정 풀 행 — EntryRef는 TrapPresetCatalog의 presetId다(함정 추첨은 presetId 치환).</summary>
        public bool IsTrapPreset => string.Equals(EntryKind, "trapPreset", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>bans 관계(Q11): adjacent = 두 좌표의 헥스 거리 ≤ Radius, overlap = 함정의 효과 반경 안.</summary>
    public enum PlacementBanRelation
    {
        Adjacent,
        Overlap,
    }

    /// <summary>
    /// 금지 조합 규칙(stage_randomization_bans.csv 한 행). 한쪽은 <c>trap:&lt;HexTrapEffectKind&gt;</c>,
    /// 다른 쪽은 <c>monster:&lt;태그&gt;</c>여야 한다(태그 = 스폰 role 또는 브리지가 붙인 특성 태그).
    /// 파서가 방향을 정규화하므로 CSV의 kindA/kindB 순서는 자유다.
    /// </summary>
    public sealed class PlacementBanRule
    {
        public PlacementBanRule(string trapEffectKind, string monsterTag, PlacementBanRelation relation, int radius)
        {
            TrapEffectKind = trapEffectKind ?? string.Empty;
            MonsterTag = monsterTag ?? string.Empty;
            Relation = relation;
            Radius = Math.Max(0, radius);
        }

        public string TrapEffectKind { get; }
        public string MonsterTag { get; }
        public PlacementBanRelation Relation { get; }

        /// <summary>adjacent 전용 금지 거리(0은 1로 승격). overlap은 함정 자신의 효과 반경을 쓰므로 무시된다.</summary>
        public int Radius { get; }
    }

    /// <summary>
    /// stage_randomization*.csv 파서(순수 — EditMode 스윕 감사의 전제). 헤더 이름으로 컬럼을
    /// 찾으므로 컬럼 순서와 designerNote 추가에 안전하다. 실패는 예외가 아니라 error 문자열 —
    /// 저작 CSV 오류가 맵 로드를 죽이면 안 된다(호출자가 저작 원본 폴백).
    /// </summary>
    public static class StageRandomizationCsv
    {
        public static bool TryBuildProfile(
            string stagesCsvText,
            string poolsCsvText,
            string stageId,
            out StageRandomizationProfile profile,
            out string error)
        {
            return TryBuildProfile(stagesCsvText, poolsCsvText, null, stageId, out profile, out error);
        }

        public static bool TryBuildProfile(
            string stagesCsvText,
            string poolsCsvText,
            string bansCsvText,
            string stageId,
            out StageRandomizationProfile profile,
            out string error)
        {
            profile = null;
            if (!TryParseRows(stagesCsvText, out var stageRows, out error) ||
                !TryParseRows(poolsCsvText, out var poolRows, out error))
            {
                return false;
            }

            var stageRow = stageRows.FirstOrDefault(row => Get(row, "stageId") == stageId);
            if (stageRow == null)
            {
                error = $"stage_randomization.csv has no row for stage '{stageId}'.";
                return false;
            }

            var pools = new List<StageRandomizationPoolEntry>();
            foreach (var row in poolRows.Where(row => Get(row, "stageId") == stageId))
            {
                var kind = Get(row, "entryKind");
                if (!string.Equals(kind, "monster", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(kind, "trapPreset", StringComparison.OrdinalIgnoreCase))
                {
                    // 알 수 없는 entryKind는 이후 확장 예약 — 조용히 건너뛴다.
                    continue;
                }

                if (!TryParseZone(Get(row, "zone"), out var zone))
                {
                    error = $"Pool row '{Get(row, "entryRef")}' has unknown zone '{Get(row, "zone")}' (early|mid|late|any).";
                    return false;
                }

                if (!TryGetInt(row, "threatCost", out var threatCost, out error) ||
                    !TryGetInt(row, "maxCount", out var maxCount, out error) ||
                    !TryGetInt(row, "weight", out var weight, out error))
                {
                    return false;
                }

                pools.Add(new StageRandomizationPoolEntry(
                    Get(row, "group"), kind, Get(row, "entryRef"), threatCost, maxCount,
                    Get(row, "capGroup"), weight, zone, Get(row, "role")));
            }

            // 합산 상한 키를 공유하는 행들의 maxCount는 일치해야 한다 — 어긋나면 저작 오류.
            foreach (var capGroup in pools.GroupBy(entry => entry.CapKey, StringComparer.Ordinal))
            {
                if (capGroup.Select(entry => entry.MaxCount).Distinct().Count() > 1)
                {
                    error = $"Pool entries sharing cap key '{capGroup.Key}' disagree on maxCount.";
                    return false;
                }
            }

            if (!TryGetInt(stageRow, "monsterThreatBudget", out var budget, out error) ||
                !TryGetInt(stageRow, "budgetTolerancePct", out var tolerance, out error) ||
                !TryGetInt(stageRow, "safeRadius", out var safeRadius, out error) ||
                !TryGetInt(stageRow, "rerollLimit", out var rerollLimit, out error) ||
                !TryGetInt(stageRow, "densityRadius", out var densityRadius, out error) ||
                !TryGetInt(stageRow, "densityCap", out var densityCap, out error) ||
                !TryGetInt(stageRow, "zoneEarlyMaxBfs", out var zoneEarly, out error) ||
                !TryGetInt(stageRow, "zoneMidMaxBfs", out var zoneMid, out error))
            {
                return false;
            }

            // P2 컬럼은 선택적 — 없거나 비어 있으면 해당 기능이 꺼진 채(0 / -1) P0~P1과 동작이 같다.
            // trapMinDistance·eliteMin(2026-08-19 #4·#18)도 같은 계약 — 없으면 규칙이 꺼진다.
            if (!TryGetOptionalInt(stageRow, "trapThreatBudget", 0, out var trapBudget, out error) ||
                !TryGetOptionalInt(stageRow, "chestMinDistance", -1, out var chestMinDistance, out error) ||
                !TryGetOptionalInt(stageRow, "trapMinDistance", 0, out var trapMinDistance, out error) ||
                !TryGetOptionalInt(stageRow, "eliteMin", 0, out var eliteMin, out error) ||
                !TryGetOptionalInt(stageRow, "monsterMinKinds", 0, out var monsterMinKinds, out error) ||
                !TryGetOptionalInt(stageRow, "eliteHpPercent", 100, out var eliteHpPercent, out error) ||
                !TryGetOptionalInt(stageRow, "eliteDamagePercent", 100, out var eliteDamagePercent, out error) ||
                !TryGetOptionalInt(stageRow, "serviceShopCount", 0, out var serviceShopCount, out error) ||
                !TryGetOptionalInt(stageRow, "serviceCamperCount", 0, out var serviceCamperCount, out error) ||
                !TryGetOptionalInt(stageRow, "serviceMinDistance", 0, out var serviceMinDistance, out error) ||
                !TryGetOptionalInt(stageRow, "serviceRouteMin", 0, out var serviceRouteMin, out error) ||
                !TryGetOptionalInt(stageRow, "eliteMinDistance", 0, out var eliteMinDistance, out error))
            {
                return false;
            }

            IReadOnlyList<PlacementBanRule> bans = Array.Empty<PlacementBanRule>();
            if (!string.IsNullOrWhiteSpace(bansCsvText) && !TryParseBans(bansCsvText, stageId, out bans, out error))
            {
                return false;
            }

            var enabledText = Get(stageRow, "enabled");
            var enabled = string.Equals(enabledText, "TRUE", StringComparison.OrdinalIgnoreCase) ||
                          enabledText == "1";
            profile = new StageRandomizationProfile(
                stageId, enabled, budget, tolerance, safeRadius, rerollLimit,
                densityRadius, densityCap, zoneEarly, zoneMid, pools,
                trapBudget, chestMinDistance, bans, trapMinDistance, eliteMin,
                monsterMinKinds, eliteHpPercent, eliteDamagePercent,
                serviceShopCount, serviceCamperCount, serviceMinDistance, serviceRouteMin,
                eliteMinDistance);
            error = null;
            return true;
        }

        /// <summary>
        /// stage_randomization_bans.csv 파서(Q11). 행 형식 = (stageId, kindA, kindB, relation, radius).
        /// kindA/kindB는 순서 무관하게 <c>trap:</c> 하나 + <c>monster:</c> 하나여야 한다.
        /// </summary>
        public static bool TryParseBans(
            string bansCsvText,
            string stageId,
            out IReadOnlyList<PlacementBanRule> bans,
            out string error)
        {
            bans = Array.Empty<PlacementBanRule>();
            if (!TryParseRows(bansCsvText, out var rows, out error))
            {
                return false;
            }

            var parsed = new List<PlacementBanRule>();
            foreach (var row in rows.Where(row => Get(row, "stageId") == stageId))
            {
                var kindA = Get(row, "kindA");
                var kindB = Get(row, "kindB");
                var trapKind = ExtractPrefixed(kindA, "trap:") ?? ExtractPrefixed(kindB, "trap:");
                var monsterTag = ExtractPrefixed(kindA, "monster:") ?? ExtractPrefixed(kindB, "monster:");
                if (trapKind == null || monsterTag == null)
                {
                    error = $"Ban row '{kindA}'/'{kindB}' must pair one 'trap:<kind>' with one 'monster:<tag>'.";
                    return false;
                }

                var relationText = Get(row, "relation").ToLowerInvariant();
                PlacementBanRelation relation;
                switch (relationText)
                {
                    case "adjacent": relation = PlacementBanRelation.Adjacent; break;
                    case "overlap": relation = PlacementBanRelation.Overlap; break;
                    default:
                        error = $"Ban row '{kindA}'/'{kindB}' has unknown relation '{relationText}' (adjacent|overlap).";
                        return false;
                }

                if (!TryGetOptionalInt(row, "radius", 0, out var radius, out error))
                {
                    return false;
                }

                parsed.Add(new PlacementBanRule(trapKind, monsterTag, relation, radius));
            }

            bans = parsed;
            error = null;
            return true;
        }

        private static string ExtractPrefixed(string value, string prefix)
        {
            return value != null && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? value.Substring(prefix.Length).Trim()
                : null;
        }

        private static bool TryParseZone(string text, out PlacementZone zone)
        {
            switch ((text ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "":
                case "any": zone = PlacementZone.Any; return true;
                case "early": zone = PlacementZone.Early; return true;
                case "mid": zone = PlacementZone.Mid; return true;
                case "late": zone = PlacementZone.Late; return true;
                default: zone = PlacementZone.Any; return false;
            }
        }

        private static string Get(IReadOnlyDictionary<string, string> row, string column)
        {
            return row.TryGetValue(column, out var value) ? value.Trim() : string.Empty;
        }

        private static bool TryGetOptionalInt(
            IReadOnlyDictionary<string, string> row,
            string column,
            int defaultValue,
            out int value,
            out string error)
        {
            var text = Get(row, column);
            if (string.IsNullOrWhiteSpace(text))
            {
                value = defaultValue;
                error = null;
                return true;
            }

            return TryGetInt(row, column, out value, out error);
        }

        private static bool TryGetInt(IReadOnlyDictionary<string, string> row, string column, out int value, out string error)
        {
            var text = Get(row, column);
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                error = $"Column '{column}' has a non-integer value '{text}'.";
                return false;
            }

            error = null;
            return true;
        }

        private static bool TryParseRows(
            string csvText,
            out List<Dictionary<string, string>> rows,
            out string error)
        {
            rows = new List<Dictionary<string, string>>();
            if (string.IsNullOrWhiteSpace(csvText))
            {
                error = "CSV text is empty.";
                return false;
            }

            var lines = csvText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            string[] header = null;
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                var fields = SplitCsvLine(line);
                if (header == null)
                {
                    header = fields.Select(field => field.Trim()).ToArray();
                    continue;
                }

                var row = new Dictionary<string, string>(StringComparer.Ordinal);
                for (var i = 0; i < header.Length && i < fields.Count; i++)
                {
                    row[header[i]] = fields[i];
                }

                rows.Add(row);
            }

            if (header == null)
            {
                error = "CSV has no header row.";
                return false;
            }

            error = null;
            return true;
        }

        private static List<string> SplitCsvLine(string line)
        {
            var fields = new List<string>();
            var current = new System.Text.StringBuilder();
            var inQuotes = false;
            for (var i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            current.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        current.Append(ch);
                    }
                }
                else if (ch == '"')
                {
                    inQuotes = true;
                }
                else if (ch == ',')
                {
                    fields.Add(current.ToString());
                    current.Length = 0;
                }
                else
                {
                    current.Append(ch);
                }
            }

            fields.Add(current.ToString());
            return fields;
        }
    }
}

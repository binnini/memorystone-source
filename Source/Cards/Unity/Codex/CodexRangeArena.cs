using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Codex
{
    /// <summary>
    /// 도감 전용 가상 아레나 — 장애물 없는 반경 N 원판 위에 <b>실제 <see cref="CombatState"/></b>를 세운다.
    /// <para>
    /// 왜 이런 것이 필요한가: 카드의 "겨냥할 수 있는 칸"을 아는 함수는 하나가 아니라 넷이고
    /// (<see cref="CombatState.ValidateAttackTarget"/> · <see cref="CombatState.ValidateScoutTarget"/> ·
    /// <see cref="CombatState.ValidateFieldObjectTarget"/> · <see cref="CombatState.GetReachablePlayerMoves"/>),
    /// 넷 다 맵과 전투 상태를 요구한다. 도감에는 맵이 없다. 그래서 <b>맵을 만들어 같은 함수에 먹인다</b> —
    /// 계산을 도감이 다시 짜는 대신(<c>docs/codex-plan.md</c> §3-1 금지 사항) 집행 코드를 그대로 부른다.
    /// </para>
    /// <para>
    /// 아레나는 <b>탁 트인 벌판</b>이다. 모든 칸이 걸을 수 있고 시야가 전부 열려 있다.
    /// 따라서 도해가 말하는 것은 <b>지형에 가리지 않은 카드 자체의 사거리</b>다 — 참고서로서 맞는 답이고,
    /// 실전에서 벽에 잘리는 모습까지 보여 주지는 않는다.
    /// </para>
    /// <para>
    /// 🔴 <b>같은 벌판 위에 상태가 둘이다.</b> 공격 검증기는 "대상 칸에 살아 있는 몬스터"를 요구하므로
    /// 적이 없으면 사거리가 아니라 "적이 없다"는 이유로 전부 거절된다 → <see cref="EnemyState"/>는
    /// 원점을 뺀 모든 칸에 더미 몬스터를 세운다. 반대로 이동·기물 설치는 <b>빈 칸</b>을 요구하므로
    /// 그 상태에서 물으면 갈 곳이 하나도 없다 → <see cref="OpenState"/>는 몬스터가 없다.
    /// 두 상태를 한 몸에 합칠 수 없는 것은 규칙이 그렇게 생겼기 때문이고, 어느 상태에 물을지는
    /// <see cref="CodexCardRange"/>가 카드 종류로 고른다.
    /// </para>
    /// <para>
    /// ⚠️ 더미 몬스터는 <see cref="CombatConfig.EnemyAttackDamage"/> 0 · 추적 0으로 세운다. 아레나는
    /// 플레이어 행동 페이즈에서 얼어 있고 몬스터 페이즈로 넘어가지 않으므로 아무도 움직이지 않는다.
    /// </para>
    /// </summary>
    public sealed class CodexRangeArena
    {
        /// <summary>
        /// 기본 반경. 출하 카드의 <b>최대 사거리 + 최대 착탄 반경</b>보다 넉넉해야 한다 —
        /// 벌판이 좁으면 사거리 끝에서 터지는 폭발이 벌판 밖으로 잘려 나가고, 도감은 그 잘린 모습을
        /// 카드의 진짜 형상인 양 그린다(실측: 반경 5에서 정찰 S01의 착탄이 19칸 대신 14칸으로 나왔다).
        /// 현재 출하 최대는 사거리 4 + 반경 2 = 6이고, 여유 두 칸을 더 둔다.
        /// <para>도해가 실제로 그리는 격자는 이것이 아니라 칸 집합에서 다시 계산한다.</para>
        /// </summary>
        public const int DefaultRadius = 8;

        public static readonly HexCoord Origin = new HexCoord(0, 0);

        private const int DummyHp = 9999;

        private CodexRangeArena(
            HexMapData map,
            CombatState openState,
            CombatState enemyState,
            CombatState movementState,
            int radius,
            IReadOnlyList<HexCoord> cells)
        {
            Map = map;
            OpenState = openState;
            EnemyState = enemyState;
            MovementState = movementState;
            Radius = radius;
            Cells = cells;
        }

        public HexMapData Map { get; }

        /// <summary>몬스터가 없고 <b>행동 페이즈</b>인 벌판. 정찰·기물 설치 칸을 여기에 묻는다.</summary>
        public CombatState OpenState { get; }

        /// <summary>
        /// 몬스터가 없고 <b>이동 페이즈</b>인 벌판. 이동 카드는 이동 페이즈에서만 나가므로,
        /// "여기까지 간다"를 실제로 내 보며 확인할 수 있는 상태가 따로 있어야 한다.
        /// </summary>
        public CombatState MovementState { get; }

        /// <summary>원점을 뺀 모든 칸에 더미 몬스터가 선 벌판. 공격 사거리를 여기에 묻는다.</summary>
        public CombatState EnemyState { get; }

        public int Radius { get; }

        /// <summary>아레나의 모든 칸(원점 포함), 중심에서 바깥으로.</summary>
        public IReadOnlyList<HexCoord> Cells { get; }

        public static CodexRangeArena Create(CardCatalogDefinition catalog, int radius = DefaultRadius)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            radius = Math.Max(1, radius);
            var cells = HexArea.CellsWithin(Origin, radius).ToList();
            var map = CreateMap(radius);

            var config = BuildArenaConfig(radius);
            var openState = BuildState(map, catalog, config, Array.Empty<MonsterConfig>());
            var enemyState = BuildState(map, catalog, config, BuildDummyMonsters(cells));
            var movementState = BuildState(map, catalog, config, Array.Empty<MonsterConfig>(), freezeInActionPhase: false);

            return new CodexRangeArena(map, openState, enemyState, movementState, radius, cells);
        }

        private static CombatState BuildState(
            HexMapData map,
            CardCatalogDefinition catalog,
            CombatConfig config,
            IEnumerable<MonsterConfig> monsters,
            bool freezeInActionPhase = true)
        {
            var state = new CombatState(
                map,
                Origin,
                monsters,
                config,
                cardCatalog: catalog,
                drawOpeningHands: true,
                shuffleDecks: false);

            // 행동 페이즈 + 기력 만땅으로 얼린다. 이 경로는 아무도 이동시키지 않으므로 더미 몬스터가
            // 깔아 둔 자리를 그대로 지킨다(게임플레이로 페이즈를 넘기면 몬스터가 움직여 구멍이 난다).
            // 이동 카드용 상태만은 생성 직후의 이동 페이즈를 그대로 둔다.
            if (freezeInActionPhase)
            {
                state.DebugRestoreCombatantsForTuning();
            }

            // 검증기는 "손에 있는 그 카드"를 찾는다. 도감은 전량을 한 번에 물어보므로 전량을 손에 올린다.
            //
            // 🔴🔴 단, 저주(CardEffectType.Status)는 절대 올리지 않는다. 저주는 <b>손에 있는 것만으로</b>
            // 규칙을 바꾸는 카드라, 손에 올리는 순간 아레나가 오염되고 도감이 조용히 틀린 그림을 그린다.
            // 실측으로 두 번 밟았다:
            //   · X07 지각  — "손에 있는 동안 이동 사거리 1 감소" → 모든 이동 카드가 한 칸씩 짧게 나왔다
            //                 (1칸 이동은 아예 갈 곳이 없는 카드로 보였다)
            //   · X03 정전 / X11 도깨비 장난 — "다른 카드 1장 봉인" → 봉인된 두 장(A09·U02)이
            //                 "범위 없음"으로 나왔다
            // 저주 자신은 어차피 사용 불가라 아레나에 물을 것이 없다(CodexCardRange가 먼저 걸러 낸다).
            foreach (var entry in catalog.Entries)
            {
                if (entry == null
                    || entry.Status != CardCatalogStatus.Approved
                    || entry.ActionType == CardEffectType.Status)
                {
                    continue;
                }

                state.DebugInjectCardIntoHand(entry.Id);
            }

            return state;
        }

        /// <summary>
        /// 장애물 없는 반경 N 원판. 전투 상태가 필요 없는 도해(함정·소모품 반경)는 이것만 쓴다 —
        /// 상태 3벌을 세우는 값을 치를 이유가 없다.
        /// </summary>
        public static HexMapData CreateMap(int radius)
        {
            return new HexMapData(HexArea.CellsWithin(Origin, Math.Max(1, radius)).Select(coord => new HexCellData(
                coord,
                $"codex-arena-{coord.Q}-{coord.R}",
                "street",
                1,
                true,
                false)));
        }

        private static IEnumerable<MonsterConfig> BuildDummyMonsters(IEnumerable<HexCoord> cells)
        {
            return cells
                .Where(coord => coord != Origin)
                .Select(coord => new MonsterConfig($"codex-dummy-{coord.Q}-{coord.R}", coord, DummyHp));
        }

        /// <summary>
        /// 아레나 규칙: 아무도 죽지 않고(체력 9999·피해 0), 아무도 쫓아오지 않으며(추적 0),
        /// 시야가 격자 전체를 덮는다. 이 값들은 <b>사거리 판정에 남는 이유를 거리 하나로 줄이기 위한</b>
        /// 설정이다.
        /// <para>
        /// ⚠️ 기력만은 <b>출하 기본값 그대로</b> 둔다(막으려는 게 아니라 <b>거짓말을 막으려고</b>).
        /// 「전력 질주」(M08)는 기를 전부 소모하고 소모한 만큼 이동하는 카드라, 기력을 99로 부풀리면
        /// 도감이 "아레나 전체를 간다"고 말한다. 출하 카드 최대 비용은 4이므로 기본 기력으로 전량이 검증된다.
        /// </para>
        /// </summary>
        private static CombatConfig BuildArenaConfig(int radius)
        {
            return new CombatConfig(
                playerMaxHp: DummyHp,
                enemyMaxHp: DummyHp,
                playerMovePoints: radius,
                attackRange: 1,
                attackDamage: 0,
                defenseBlock: 0,
                enemyChaseRange: 0,
                enemyAttackRange: 1,
                enemyAttackDamage: 0,
                actionBudget: CombatConfig.Default.ActionBudget,
                movementHandSize: 1,
                actionHandSize: 5,
                playerVisionRange: radius + 2,
                enemyDisengageRange: 0);
        }
    }
}

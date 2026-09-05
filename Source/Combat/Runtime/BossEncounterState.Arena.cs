using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>옛 <c>CombatState.BossArena.cs</c>의 본문(3-B · 본문 무변경 이동).</summary>
    internal sealed partial class BossEncounterState
    {
        /// <summary>봉인된 아레나의 id. 빈 문자열이면 결계 없음. 서스펜드 왕복 대상.</summary>
        private string sealedBossArenaId = string.Empty;

        /// <summary>
        /// 조우가 시작된 경계 칸(강제 정지 좌표). 봉인 시 <see cref="TryPlayerMove"/>가 기록한다.
        /// 표현 전용 힌트일 뿐이라(걷기 연출 끝점) 서스펜드 왕복 대상이 아니다 — 재개 시점엔 이미
        /// 전투가 진행 중이라 조우 연출을 다시 재생하지 않는다.
        /// </summary>
        private HexCoord? sealedBossArenaEntryCoord;

        /// <summary>서스펜드 왕복·조우 이동 확정(호스트)이 쓰는 봉인 아레나 id(원시값 — 공개 <c>SealedBossArenaId</c>는 결계 활성일 때만 값을 낸다).</summary>
        internal string SealedArenaIdRaw
        {
            get => sealedBossArenaId;
            set => sealedBossArenaId = value ?? string.Empty;
        }

        /// <summary>조우 이동에서 강제 정지한 좌표(호스트가 쓴다 · 표현 전용).</summary>
        internal HexCoord? SealedArenaEntryCoord
        {
            get => sealedBossArenaEntryCoord;
            set => sealedBossArenaEntryCoord = value;
        }

        /// <summary>봉인이 시작된 경계 칸(강제 정지 좌표). 봉인 이력이 없으면 null.</summary>
        public HexCoord? LastBossArenaEntryStopCoord => sealedBossArenaEntryCoord;

        /// <summary>봉인된 아레나에 묶인 살아 있는 보스의 unit id(없으면 빈 문자열). 조우 연출이 이 보스를
        /// 프레이밍·리빌한다.</summary>
        public string SealedBossArenaBossUnitId
        {
            get
            {
                if (!TryGetSealedArena(out var area) || string.IsNullOrWhiteSpace(area.BossSpawnRefId))
                {
                    return string.Empty;
                }

                foreach (var monster in host.Monsters)
                {
                    if (!monster.Combatant.IsDead &&
                        string.Equals(monster.SpawnRefId, area.BossSpawnRefId, StringComparison.Ordinal))
                    {
                        return monster.Id;
                    }
                }

                return string.Empty;
            }
        }

        /// <summary>현재 봉인된 아레나 id(없으면 빈 문자열).</summary>
        public string SealedBossArenaId => IsBossArenaBarrierActive ? sealedBossArenaId : string.Empty;

        /// <summary>
        /// 결계가 실제로 막고 있는가. 봉인 기록이 있어도 <b>묶인 보스가 죽으면 즉시 열린다</b> —
        /// 이 성질을 상태로 저장하지 않고 매번 유도하는 이유는, 보스 사망 경로가 여러 갈래(카드·장판·
        /// 기믹)라 어느 하나를 놓치면 플레이어가 영원히 갇히기 때문이다.
        /// </summary>
        public bool IsBossArenaBarrierActive =>
            !string.IsNullOrEmpty(sealedBossArenaId) &&
            TryGetSealedArena(out var area) &&
            HasLivingBoundBoss(area);

        /// <summary>봉인된 아레나의 셀들(표현용). 결계가 없으면 빈 목록.</summary>
        public IReadOnlyList<HexCoord> SealedBossArenaCoords =>
            IsBossArenaBarrierActive && TryGetSealedArena(out var area)
                ? area.Coords
                : Array.Empty<HexCoord>();

        /// <summary>
        /// 결계가 막고 있는 링(아레나 바깥 한 겹). 오버레이가 이 좌표를 그린다.
        /// 맵 밖 좌표는 애초에 이동 불가라 걸러 낸다 — 없는 셀에 오버레이를 그리면 허공에 뜬다.
        /// </summary>
        public IReadOnlyList<HexCoord> SealedBossArenaBoundaryCoords =>
            IsBossArenaBarrierActive && TryGetSealedArena(out var area)
                ? area.EnumerateBoundaryRing().Where(coord => host.Map != null && host.Map.Contains(coord)).ToList()
                : (IReadOnlyList<HexCoord>)Array.Empty<HexCoord>();

        /// <summary>
        /// 조우한(등장 연출을 거친) 살아 있는 보스가 있는가. 보스 HUD가 이걸로 게이트한다 — 보스가 맵에
        /// 존재하기만 해도 상단 바를 띄우면 결계 조우로 처음 만나기 <b>전</b>에 보스가 노출된다(레이드 문법
        /// 위반). 보스 아레나에 묶인 보스는 그 아레나가 <b>봉인(조우)</b>됐을 때만, 아레나 없는(고정) 보스는
        /// 존재만으로 조우로 본다(현행 유지). 봉인 여부는 서스펜드 왕복되므로 재개 후에도 정확하다.
        /// </summary>
        public bool HasEncounteredLivingBoss
        {
            get
            {
                foreach (var monster in host.Monsters)
                {
                    if (!monster.Combatant.IsDead && CombatState.IsBossMonster(monster) && IsBossEncountered(monster))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// 아레나에 들어가기 <b>전</b>의 보스인가 — 그렇다면 판 위에 서지도, 움직이지도, 때리지도 않는다
        /// (2026-09-02 #4 · 사용자 확정: "보스는 아레나에 들어가기 전에는 스폰되어서도 안 되고
        /// 움직여서도 안 된다").
        ///
        /// <para>🔴 <b>봉인은 여태 「조우했는가」만 답하고 있었다.</b> 배관(아레나 저작·경로 위 진입 판정·
        /// 중단 저장 왕복)은 처음부터 다 있었는데 <b>보스의 행동과 표시에는 아무 게이트가 없어서</b>,
        /// 보스가 첫 턴부터 서 있고 플레이어를 만나기도 전에 걸어 다녔다.</para>
        ///
        /// <para>🔑 <b>새 축을 만들지 않는다</b>: 판정은 <see cref="IsBossEncountered"/> 하나를 그대로 쓰고,
        /// 행동은 이미 있는 <c>Dormant</c>(기물 선례)에, 표시는 이미 있는 은신 게이트 <b>옆에</b> 붙인다.
        /// 옆에 붙이는 이유는 안개·은신이 그랬듯 <b>합치면 한쪽 규칙이 다른 쪽을 덮어쓰기</b> 때문이다.</para>
        /// </summary>
        internal bool IsBossAwaitingArenaEncounter(MonsterRuntime monster)
        {
            return monster != null
                   && !monster.Combatant.IsDead
                   && CombatState.IsBossMonster(monster)
                   && !IsBossEncountered(monster);
        }

        /// <summary>뷰가 쓰는 표면 — 마커·모델은 <c>MonsterRuntime</c>에 닿지 못하므로 id로 묻는다.</summary>
        public bool IsMonsterHiddenBeforeBossArena(string monsterId)
        {
            if (string.IsNullOrEmpty(monsterId))
            {
                return false;
            }

            var monster = host.Monsters.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, monsterId, StringComparison.Ordinal));
            return IsBossAwaitingArenaEncounter(monster);
        }

        private bool IsBossEncountered(MonsterRuntime boss)
        {
            if (host.Map != null && !string.IsNullOrWhiteSpace(boss.SpawnRefId))
            {
                foreach (var area in host.Map.Areas)
                {
                    if (area.IsBossArena && string.Equals(area.BossSpawnRefId, boss.SpawnRefId, StringComparison.Ordinal))
                    {
                        // 이 보스를 묶은 아레나가 있다 — 그 아레나가 봉인됐을 때만 조우로 본다.
                        return string.Equals(sealedBossArenaId, area.Id, StringComparison.Ordinal);
                    }
                }
            }

            // 묶인 보스 아레나가 없으면(고정 보스 등) 존재만으로 조우로 본다.
            return true;
        }

        private bool TryGetSealedArena(out HexMapAreaRef area)
        {
            area = default;
            if (host.Map == null || string.IsNullOrEmpty(sealedBossArenaId))
            {
                return false;
            }

            foreach (var candidate in host.Map.Areas)
            {
                if (candidate.IsBossArena && string.Equals(candidate.Id, sealedBossArenaId, StringComparison.Ordinal))
                {
                    area = candidate;
                    return true;
                }
            }

            return false;
        }

        private bool HasLivingBoundBoss(HexMapAreaRef area)
        {
            if (string.IsNullOrWhiteSpace(area.BossSpawnRefId))
            {
                return false;
            }

            return host.Monsters.Any(monster =>
                !monster.Combatant.IsDead &&
                string.Equals(monster.SpawnRefId, area.BossSpawnRefId, StringComparison.Ordinal));
        }

        /// <summary>
        /// 플레이어 이동 경로에서 보스 아레나 조우를 판정한다. 경로를 <b>순서대로</b> 훑어 첫 아레나 셀에서
        /// 멈추므로, 사거리 긴 이동 카드로 아레나를 가로질러 보스 옆에 착지하거나 아레나를 통과해
        /// 지나가 버리는 일이 없다 — 조우는 항상 "경계를 넘은 그 칸"에서 시작한다.
        ///
        /// 1회성: 이미 봉인된 상태면 아무것도 하지 않는다(<see cref="sealedBossArenaId"/>가 게이트).
        /// 묶인 보스가 이미 죽었다면 봉인하지 않는다 — 시체 앞에서 갇힐 이유가 없다.
        /// </summary>
        /// <param name="path">이동 경로(시작 셀 포함, 목적지까지 순서대로).</param>
        /// <param name="stopCoord">조우가 시작된 셀. 반환값이 false면 의미 없다.</param>
        internal bool TryResolveBossArenaEntry(IReadOnlyList<HexCoord> path, out HexCoord stopCoord)
        {
            stopCoord = default;
            if (path == null || path.Count == 0 || host.Map == null || !string.IsNullOrEmpty(sealedBossArenaId))
            {
                return false;
            }

            foreach (var coord in path)
            {
                foreach (var area in host.Map.Areas)
                {
                    if (!area.IsBossArena || !area.Contains(coord) || !HasLivingBoundBoss(area))
                    {
                        continue;
                    }

                    sealedBossArenaId = area.Id;
                    stopCoord = coord;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 봉인 직후(플레이어 전투 시작 좌표까지 확정된 뒤) 묶인 보스의 기믹에 봉인을 알린다(2026-09-05 후속 #1).
        /// 호출 시점이 규칙이다: <c>PlayerCoord</c> 확정 <b>전</b>에 부르면 살포 예고가 플레이어 시작 칸을 고를 수
        /// 있고, 살포는 그 칸을 막힌 것으로 보고 다른 칸을 뽑아 「예고=배치」가 깨진다. 점유 캐시도 그래서 먼저 깐다.
        /// </summary>
        internal void NotifyBossArenaSealed()
        {
            if (!TryGetSealedArena(out var area))
            {
                return;
            }

            host.UpdateOccupancy();
            foreach (var track in bossPhaseTracks.ToList())
            {
                var boss = host.Monsters.FirstOrDefault(monster => string.Equals(monster.Id, track.BossUnitId, StringComparison.Ordinal));
                if (boss == null || boss.Combatant.IsDead || !area.Contains(boss.Coord)
                    || !bossCatalog.TryGetProfile(track.BossDefinitionId, out var profile))
                {
                    continue;
                }

                foreach (var mechanicId in profile.MechanicIds)
                {
                    if (BossMechanicRegistry.TryGet(mechanicId, out var mechanic) && mechanic is IBossArenaSealedMechanic sealedHook)
                    {
                        sealedHook.OnArenaSealed(new BossMechanicContext(host.Self, boss, track, profile));
                    }
                }
            }
        }

        /// <summary>
        /// 봉인된 아레나 전체를 영구 공개한다. 결계 안에서까지 암시야를 더듬는 것은 레이드 문법이 아니고,
        /// 무엇보다 나갈 수 없는 방의 지형을 감추는 것은 정보를 주지 않고 불편만 준다.
        ///
        /// <see cref="HexVisibilityRuntime.RevealPermanently"/>를 쓰는 이유: 평범한 <c>Reveal</c>은
        /// 그 칸이 임시 공개 목록에 있으면 다음 시야 갱신에서 Hinted로 강등된다(플레이어가 걸어 들어온
        /// 그 칸이 정확히 그 경우다). 그리고 <b>다시 가리지 않는다</b> — 보스가 죽어 결계가 열려도
        /// 밝은 채로 둔다. 가시성 단조 계약(내려가지 않는다)을 깨지 않는 유일한 방향이다.
        /// </summary>
        internal void RevealSealedBossArena()
        {
            if (!TryGetSealedArena(out var area))
            {
                return;
            }

            foreach (var coord in area.Coords)
            {
                host.Visibility.RevealPermanently(coord);
            }
        }

        /// <summary>
        /// 보스 기물 배치의 기준점. 봉인된 아레나가 있으면 그 <b>중심</b>, 없으면 보스 자신의 좌표다.
        ///
        /// 중심을 보스가 아니라 아레나로 잡는 이유: 보스 기준이면 보스가 플레이어를 쫓아 움직일 때마다
        /// 살포 패턴이 통째로 따라다녀 "어느 정도 고정된 자리"라는 설계 의도가 사라진다. 반대로
        /// 아레나가 없는 보스전(고정형 보스 등)에는 중심이랄 것이 없으므로 보스 좌표로 폴백해야 한다 —
        /// 이 분기를 빼먹으면 아레나 없는 보스의 기믹이 통째로 죽는다.
        /// </summary>
        internal HexCoord ResolveBossPropAnchor(HexCoord bossCoord)
        {
            return IsBossArenaBarrierActive && TryGetSealedArena(out var area) && area.Coords.Count > 0
                ? area.GetCenter()
                : bossCoord;
        }

        /// <summary>
        /// 결계가 닫혀 있을 때 이 좌표가 아레나 <b>안</b>인가. 결계가 없으면 항상 참이다 —
        /// 아레나 없는 보스전(고정형 보스 등)의 동작을 바꾸지 않기 위해서다.
        ///
        /// 보스 기물 스폰이 이걸 본다. 기물은 보스 반경 안에서 <b>가장 먼 고리를 선호</b>하는데,
        /// 보스가 플레이어를 쫓아 아레나 가장자리에 붙으면 그 반경이 결계를 넘어간다. 그러면
        /// 결계 안에 갇힌 플레이어가 부술 수 없는 곳에 기물이 자라 <b>기믹의 압박이 통째로 무력화</b>된다
        /// (실제로 보스 랩에서 관측됐다: 보스가 (-4,0)에 붙자 기물이 결계 밖 (-6,0)에 났다).
        /// </summary>
        internal bool IsInsideActiveBossArena(HexCoord coord)
        {
            if (!IsBossArenaBarrierActive || !TryGetSealedArena(out var area))
            {
                return true;
            }

            return area.Contains(coord);
        }

        /// <summary>
        /// 결계 셀을 점유 테이블에 등록한다. <see cref="UpdateOccupancy"/> <b>내부</b>에서만 불린다 —
        /// 점유 테이블은 매 상태 변화마다 통째로 재구축되므로, 밖에서 <c>TemporaryBlocked</c>를 한 번
        /// 찍어 두면 다음 갱신에 소멸한다. 결계가 지속되려면 재구축 소스 자체여야 한다.
        ///
        /// 막는 것은 아레나 <b>바깥</b> 링이다(<see cref="HexMapAreaRef.EnumerateBoundaryRing"/>).
        /// 플레이어와 몬스터가 같은 점유 테이블로 경로를 찾으므로 출입 금지가 자동으로 대칭이 된다.
        /// </summary>
        /// <summary>중앙 전방 후보를 고를 때 중심에서 최대 몇 칸 앞까지 나갈지(진입 쪽). 넓은 아레나에서
        /// 플레이어를 보스 정면 몇 칸 앞에 세운다. 좁은 아레나에서는 중심~경계 거리에 맞춰 줄어든다.</summary>
        private const int BossArenaBattleStartFrontOffset = 3;

        /// <summary>
        /// 현재 봉인된 아레나의 전투 시작 좌표("중앙 전방"). 봉인이 없으면 마지막 진입 칸(없으면 플레이어 좌표).
        ///
        /// 강제 정지 좌표(경계 칸)와 <b>분리</b>한다(§8-9 P5 개정): 경계 칸은 걷기 연출·경로 절단·트랩 기준으로
        /// 남고, 전투는 아레나 중앙 전방에서 시작한다. 이 값을 규칙상 플레이어 좌표에 실제로 반영하는 것은
        /// <b>표현 계층이 화면을 덮는 동안(암전)</b>이어야 눈에 띄는 순간이동이 안 보인다.
        /// </summary>
        public HexCoord ResolveSealedBossArenaBattleStart()
        {
            var entry = sealedBossArenaEntryCoord ?? host.PlayerCoord;
            return ResolveBossArenaBattleStart(entry);
        }

        /// <summary>
        /// 진입 칸(<paramref name="entryCoord"/>) 기준으로 전투 시작 좌표를 유도한다. 저작 필드를 새로 만들지
        /// 않고(§8-9 P5 개정 근거) 아레나 중심에서 진입 방향으로 앞선 칸부터 안쪽으로 훑어, 아레나 안·이동
        /// 가능·living 몬스터 비점유인 첫 칸을 고른다. 폴백은 중심, 그마저 막히면 진입 칸이다. 봉인된 아레나가
        /// 없으면 진입 칸을 그대로 돌려준다 — 아레나 없는 보스전의 동작을 바꾸지 않는다.
        /// </summary>
        internal HexCoord ResolveBossArenaBattleStart(HexCoord entryCoord)
        {
            if (!TryGetSealedArena(out var area) || area.Coords.Count == 0)
            {
                return entryCoord;
            }

            var center = area.GetCenter();
            var line = HexLineToward(center, entryCoord);

            // 중심에서 몇 칸 앞을 선호하되, 좁은 아레나에서는 경계 바로 그 칸(=강제 정지 좌표)에 서지 않도록
            // 최소 한 칸은 안쪽으로 둔다. preferred..1은 항상 라인의 내부 인덱스(중심도 경계도 아님)다.
            var centerToEntry = center.DistanceTo(entryCoord);
            var preferred = System.Math.Max(1, System.Math.Min(BossArenaBattleStartFrontOffset, centerToEntry - 1));
            for (var i = System.Math.Min(preferred, line.Count - 1); i >= 1; i--)
            {
                var candidate = line[i];
                if (area.Contains(candidate) && IsBossArenaBattleStartCandidate(candidate))
                {
                    return candidate;
                }
            }

            return IsBossArenaBattleStartCandidate(center) ? center : entryCoord;
        }

        /// <summary>전투 시작 후보로 설 수 있는 칸인가: 이동 가능 지형 + 이동 차단 오브젝트 없음 + living 몬스터
        /// 비점유(보스·기물 포함). 이동 경로 판정과 같은 규약을 쓴다.</summary>
        private bool IsBossArenaBattleStartCandidate(HexCoord coord)
        {
            return host.Map != null
                && host.Map.TryGetCell(coord, out var cell)
                && cell.BaseWalkable
                && host.TerrainTraits.IsWalkable(cell.TerrainTypeId)
                && !host.Map.HasMovementBlockingObject(coord)
                && host.FindLivingMonsterAt(coord) == null;
        }

        /// <summary>
        /// <paramref name="from"/>에서 <paramref name="to"/>까지의 헥스 라인(양끝 포함). 매 걸음 목표에 가장
        /// 가까워지는 이웃(방향 순서 고정 = 결정적)으로 나아간다. 헥스 그리드에서 목표를 향한 이동은 거리를
        /// 한 칸씩 줄이므로 단조 경로가 나온다.
        /// </summary>
        private static List<HexCoord> HexLineToward(HexCoord from, HexCoord to)
        {
            var line = new List<HexCoord> { from };
            var current = from;
            var guard = 0;
            while (current != to && guard++ < 128)
            {
                var best = current;
                var bestDistance = int.MaxValue;
                foreach (var neighbor in current.NeighborsInDirectionOrder())
                {
                    var distance = neighbor.DistanceTo(to);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = neighbor;
                    }
                }

                if (best == current)
                {
                    break;
                }

                current = best;
                line.Add(current);
            }

            return line;
        }

        internal void ApplyBossArenaBarrierToOccupancy()
        {
            if (!IsBossArenaBarrierActive || !TryGetSealedArena(out var area))
            {
                return;
            }

            foreach (var coord in area.EnumerateBoundaryRing())
            {
                host.MarkTemporaryBlocked(coord);
            }
        }
    }
}

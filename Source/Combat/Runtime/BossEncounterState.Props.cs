using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>옛 <c>CombatState.BossProps.cs</c>의 본문(3-B · 본문 무변경 이동).</summary>
    internal sealed partial class BossEncounterState
    {
        private readonly List<BossPropVolleyCast> lastBossPropVolleyCasts = new List<BossPropVolleyCast>();

        private readonly List<BossPropAbsorption> lastBossPropAbsorptions = new List<BossPropAbsorption>();

        /// <summary>
        /// 직전 몬스터 행동 결의에서 일어난 기물 살포들(표현 전용). 보스의 "캐스트" 연출 비트
        /// (카메라 흔들림·보스 애니메이션·VFX/SFX) 뒤에 <see cref="BossPropVolleyCast.PlacedCoords"/>가
        /// 하나씩 솟아오르는 연출로 소비된다.
        /// </summary>
        public IReadOnlyList<BossPropVolleyCast> LastBossPropVolleyCasts => lastBossPropVolleyCasts;

        /// <summary>
        /// 직전 몬스터 행동 결의에서 보스가 흡수한 기물들(표현 전용). 각 항목이 "기물 자리에서 폭발 →
        /// 힘이 보스로 빨려 들어감" 연출 비트 하나가 된다. 흡수는 사망이 아니므로 사망 연출 경로를
        /// 타지 않고, 이 기록이 유일한 연출 원천이다.
        /// </summary>
        public IReadOnlyList<BossPropAbsorption> LastBossPropAbsorptions => lastBossPropAbsorptions;

        private void ClearLastBossPropEvents()
        {
            lastBossPropVolleyCasts.Clear();
            lastBossPropAbsorptions.Clear();
            lastBossScrapChainHits.Clear();
        }

        internal void RecordBossPropVolleyCast(string bossUnitId, HexCoord bossCoord, IReadOnlyList<HexCoord> placedCoords)
        {
            lastBossPropVolleyCasts.Add(new BossPropVolleyCast(
                bossUnitId,
                bossCoord,
                placedCoords == null ? Array.Empty<HexCoord>() : placedCoords.ToList()));
        }

        /// <summary>
        /// 흡수 폭발 해소 + 기록. 피해는 전멸기 폭발과 <b>같은 계약</b>이다(Block 먼저, 방어 성공 시 "방어!").
        /// 대상은 플레이어뿐이다 — 다른 기물까지 맞으면 연쇄로 무더기가 자멸해 "성숙 전에 부순다"는
        /// 이 기믹의 유일한 대응 수단이 무의미해진다.
        /// </summary>
        internal void RecordBossPropAbsorption(
            string bossUnitId,
            HexCoord bossCoord,
            string propUnitId,
            HexCoord propCoord,
            int blastRadius,
            int blastDamage)
        {
            var radius = Math.Max(0, blastRadius);
            var blastCoords = HexArea.CellsWithin(propCoord, radius).ToList();
            var appliedToPlayer = 0;

            if (blastDamage > 0 && !host.Player.IsDead && blastCoords.Contains(host.PlayerCoord))
            {
                var blockBefore = host.Player.Block;
                appliedToPlayer = host.Player.ApplyDamage(blastDamage);
                if (appliedToPlayer > 0)
                {
                    host.RaiseEffect(
                        EffectKind.Damage,
                        host.PlayerCoord,
                        0,
                        appliedToPlayer,
                        CombatState.PlayerUnitId,
                        BossPropSourceRefs.AbsorbBlast,
                        sourceUnitId: bossUnitId,
                        sourceActorKind: "monster",
                        targetActorKind: "player");
                }
                else if (host.Player.Block < blockBefore)
                {
                    host.RaiseEffect(
                        EffectKind.DamageBlocked,
                        host.PlayerCoord,
                        0,
                        0,
                        CombatState.PlayerUnitId,
                        BossPropSourceRefs.AbsorbBlast,
                        sourceUnitId: bossUnitId,
                        sourceActorKind: "monster",
                        targetActorKind: "player");
                }
            }

            lastBossPropAbsorptions.Add(new BossPropAbsorption(
                bossUnitId,
                bossCoord,
                propUnitId,
                propCoord,
                blastCoords,
                appliedToPlayer));
        }

        /// <summary>
        /// 살아 있는 기물이 흡수되기까지 남은 몬스터 페이즈 수(툴팁용). 기물이 아니거나 소유 보스의
        /// 프로필을 찾을 수 없으면 false.
        ///
        /// 나이는 기믹 스텝 진입부에서 <b>먼저</b> 오르고 그 직후 <c>AgeTurns &gt;= maturityTurns</c>인
        /// 것이 흡수되므로, 남은 턴은 <c>maturityTurns - AgeTurns</c>다(0이면 다음 몬스터 페이즈에 터진다).
        /// </summary>
        public bool TryGetBossPropAbsorptionCountdown(string propUnitId, out int turnsRemaining, out int maturityTurns)
        {
            turnsRemaining = 0;
            maturityTurns = 0;
            if (string.IsNullOrEmpty(propUnitId) || bossCatalog == null)
            {
                return false;
            }

            var prop = host.Monsters.FirstOrDefault(monster =>
                string.Equals(monster.Id, propUnitId, StringComparison.Ordinal)
                && MonsterSpawnRoles.IsBossProp(monster.SpawnRole)
                && !monster.Combatant.IsDead);
            if (prop == null)
            {
                return false;
            }

            var track = FindBossPhaseTrack(prop.OwnerUnitId);
            if (track == null || !bossCatalog.TryGetProfile(track.BossDefinitionId, out var profile))
            {
                return false;
            }

            maturityTurns = profile.GetMechanicInt(IronScrapMechanicParams.MaturityTurns);
            if (maturityTurns <= 0)
            {
                return false;
            }

            turnsRemaining = Math.Max(0, maturityTurns - prop.AgeTurns);
            return true;
        }

        /// <summary>
        /// 흡수 폭발이 저작된 반경/피해(툴팁용). 저작이 없으면 0/0이며 그 경우 폭발은 연출뿐이다.
        /// </summary>
        public bool TryGetBossPropBlastAuthoring(string propUnitId, out int blastRadius, out int blastDamage)
        {
            blastRadius = 0;
            blastDamage = 0;
            if (string.IsNullOrEmpty(propUnitId) || bossCatalog == null)
            {
                return false;
            }

            var prop = host.Monsters.FirstOrDefault(monster =>
                string.Equals(monster.Id, propUnitId, StringComparison.Ordinal)
                && MonsterSpawnRoles.IsBossProp(monster.SpawnRole)
                && !monster.Combatant.IsDead);
            var track = prop == null ? null : FindBossPhaseTrack(prop.OwnerUnitId);
            if (track == null || !bossCatalog.TryGetProfile(track.BossDefinitionId, out var profile))
            {
                return false;
            }

            blastRadius = profile.GetMechanicInt(IronScrapMechanicParams.BlastRadius);
            blastDamage = profile.GetMechanicInt(IronScrapMechanicParams.BlastDamage);
            return true;
        }

        /// <summary>
        /// 이 몬스터가 이번 몬스터 페이즈에 기믹 때문에 공격을 건너뛰는가. 기믹이 스스로 답하며
        /// (<see cref="IBossMechanic.SuppressesMonsterAttackThisTurn"/>), 규칙 코드는 어떤 보스인지
        /// 알지 못한다. 예고 커밋 시점과 공격 결의 시점 <b>양쪽</b>에서 불리는 순수 질의다.
        /// </summary>
        internal bool IsMonsterAttackReplacedByBossMechanic(MonsterRuntime monster)
        {
            return GetBossActionReplacingMechanicIds(monster).Count > 0;
        }

        /// <summary>
        /// 이번 턴 이 보스의 일반 공격을 <b>대체</b>하는 기믹 id들(예고 배지의 원천 · 2026-09-03 피드백 ⑥).
        /// 공격 차단 술어(<see cref="IsMonsterAttackBlocked"/>)와 <b>같은 순수 질의</b>
        /// (<see cref="IBossMechanic.SuppressesMonsterAttackThisTurn"/>)를 돌므로 "배지는 떴는데 공격이
        /// 나가는"/"배지 없이 기믹이 도는" 어긋남이 구조적으로 없다. 보스가 아니면 빈 목록.
        /// </summary>
        internal IReadOnlyList<string> GetBossActionReplacingMechanicIds(MonsterRuntime monster)
        {
            if (monster == null || bossPhaseTracks.Count == 0 || bossCatalog == null)
            {
                return Array.Empty<string>();
            }

            var track = FindBossPhaseTrack(monster.Id);
            if (track == null || !bossCatalog.TryGetProfile(track.BossDefinitionId, out var profile))
            {
                return Array.Empty<string>();
            }

            List<string> ids = null;
            foreach (var mechanicId in profile.MechanicIds)
            {
                if (BossMechanicRegistry.TryGet(mechanicId, out var mechanic)
                    && mechanic.SuppressesMonsterAttackThisTurn(new BossMechanicContext(host.Self, monster, track, profile)))
                {
                    (ids ?? (ids = new List<string>())).Add(mechanicId);
                }
            }

            return (IReadOnlyList<string>)ids ?? Array.Empty<string>();
        }

        // ── 보스 기물(boss-prop) 수명 관리(4-A · 2026-09-04): 옛 CombatState.MonsterSpawn.cs에서 본문 무변경 이동.
        //    「Spawn」 이름표 아래 숨어 3단계 조사에 안 잡혔던 보스 소유 코드. 기믹은 BossMechanicContext → CombatState 위임을 통해서만 닿는다.

        /// <summary>
        /// 살아있는 보스 기물 목록(나이 오래된 것부터, 동나이는 좌표 순으로 결정적). 죽은 기물은 제외한다 —
        /// 플레이어가 파괴한 기물이 흡수되면 기믹의 유일한 대응 수단이 무의미해진다.
        /// </summary>
        internal IReadOnlyList<BossPropView> GetLivingBossProps(string ownerBossUnitId, string propDefinitionId)
        {
            return host.Monsters
                .Where(monster => !monster.Combatant.IsDead
                                  && MonsterSpawnRoles.IsBossProp(monster.SpawnRole)
                                  && string.Equals(monster.DefinitionId, propDefinitionId, StringComparison.Ordinal)
                                  && string.Equals(monster.OwnerUnitId, ownerBossUnitId, StringComparison.Ordinal))
                .OrderByDescending(monster => monster.AgeTurns)
                .ThenBy(monster => monster.Coord)
                .Select(monster => new BossPropView(monster.Id, monster.DefinitionId, monster.Coord, monster.AgeTurns))
                .ToList();
        }

        /// <summary>
        /// 기물을 흡수해 보드에서 제거한다. 사망 처리가 아니라 <b>제거</b>다: 사망 연출·보상·사망 기록이
        /// 생기지 않는다(흡수는 플레이어의 성과가 아니다).
        /// </summary>
        internal bool AbsorbBossProp(
            string ownerBossUnitId,
            HexCoord bossCoord,
            string propUnitId,
            int blastRadius,
            int blastDamage)
        {
            var prop = host.Monsters.FirstOrDefault(monster =>
                string.Equals(monster.Id, propUnitId, StringComparison.Ordinal)
                && MonsterSpawnRoles.IsBossProp(monster.SpawnRole)
                && !monster.Combatant.IsDead);
            if (prop == null)
            {
                return false;
            }

            var propCoord = prop.Coord;
            host.RemoveMonster(prop);
            host.UpdateOccupancy();
            // 제거를 <b>먼저</b> 한다: 폭발은 사라진 기물이 남긴 힘이고, 기물이 아직 판에 있는 상태에서
            // 피해를 주면 넉백·점유 판정이 유령 기물을 본다.
            RecordBossPropAbsorption(ownerBossUnitId, bossCoord, propUnitId, propCoord, blastRadius, blastDamage);
            return true;
        }

        /// <summary>
        /// 기물 배치에만 쓰는 RNG. 볼리마다 링 패턴을 임의 각도로 돌리는 데 쓴다.
        ///
        /// 세이브 무결성에 영향이 없다: 기물은 몬스터로 왕복 저장되므로 재개 후 위치를 다시 뽑지 않는다.
        /// 런 시드가 있는 전투는 <c>CombatState</c>가 스트림 6(<c>RunSeedStreams.BossProps</c>)으로 꽂고,
        /// 없으면 무시드 폴백. 테스트는 <see cref="ConfigureBossPropRandom"/>으로 고정 RNG를 꽂는다.
        /// </summary>
        private System.Random bossPropRng = new System.Random();

        /// <summary>기물 배치 RNG 주입. null = 무시드 폴백. 보상 상자의 고정 RNG 주입과 같은 선례다.</summary>
        internal void ConfigureBossPropRandom(System.Random random)
        {
            bossPropRng = random ?? new System.Random();
        }

        /// <summary>
        /// 마지막 기물 볼리의 요청/실제 배치 기록. 요청한 개수를 다 놓지 못했을 때 <b>조용히 넘어가지 않기</b>
        /// 위한 흔적이다 — 최소 간격 + 좁은 아레나에서는 자리 부족이 실제로 발생하고, 기록이 없으면
        /// "왜 5개가 아니라 3개지"를 추적할 방법이 없다. 표현/디버그 전용이며 규칙은 이 값을 읽지 않는다.
        /// </summary>
        public string LastBossPropVolleyReport { get; private set; } = string.Empty;

        /// <summary>
        /// 기물 한 볼리를 배치한다. 실제로 놓인 개수를 돌려준다(요청보다 적을 수 있다).
        ///
        /// 배치 규칙(§9.4 확정): <b>아레나 중심</b>(없으면 보스 좌표) 기준 반경 <paramref name="ringRadius"/>의
        /// 고정 링을 볼리마다 임의 각도로 회전시키고, 그 위에 <paramref name="count"/>개를 균등 분할해
        /// 놓는다. 서로 <paramref name="minSpacing"/>칸 이상 떨어뜨리는 것은 그리디로 강제한다.
        /// 🔴 2026-09-05: 밴드가 막히면 <b>아레나 전역</b> 폴백으로 개수를 채운다(살포는 쿨이 돌면 반드시 나온다).
        /// 🔑 2026-09-05 예고: 트랙에 <see cref="BossPhaseTrack.PropVolleyTelegraphCells"/>가 있으면 그 칸을
        /// <b>먼저</b> 쓴다(예고=배치). 예고 칸이 그새 막혔으면(플레이어·기물) 그 칸만 새로 뽑는다.
        /// </summary>
        internal int SpawnBossPropVolley(
            string ownerBossUnitId,
            string propDefinitionId,
            int count,
            int ringRadius,
            int minSpacing)
        {
            if (count <= 0)
            {
                return 0;
            }

            var owner = host.Monsters.FirstOrDefault(monster => string.Equals(monster.Id, ownerBossUnitId, StringComparison.Ordinal));
            if (owner == null)
            {
                return 0;
            }

            var track = FindBossPhaseTrack(ownerBossUnitId);
            var placedCoords = new List<HexCoord>();
            var placed = 0;
            if (track != null && track.PropVolleyTelegraphCells.Count > 0)
            {
                foreach (var planned in track.PropVolleyTelegraphCells.ToList())
                {
                    if (placed >= count)
                    {
                        break;
                    }

                    if (placedCoords.All(taken => taken.DistanceTo(planned) >= Math.Max(1, minSpacing))
                        && TrySpawnBossPropAt(ownerBossUnitId, propDefinitionId, planned))
                    {
                        placedCoords.Add(planned);
                        placed++;
                    }
                }

                track.PropVolleyTelegraphCells.Clear();
            }

            if (placed < count)
            {
                foreach (var coord in SelectBossPropVolleyCoords(owner, count - placed, ringRadius, minSpacing, placedCoords))
                {
                    if (TrySpawnBossPropAt(ownerBossUnitId, propDefinitionId, coord))
                    {
                        placedCoords.Add(coord);
                        placed++;
                    }
                }
            }

            if (placed > 0)
            {
                RecordBossPropVolleyCast(ownerBossUnitId, owner.Coord, placedCoords);
            }

            LastBossPropVolleyReport = placed == count
                ? $"boss-prop volley: {placed}/{count} placed (ringRadius {Math.Max(1, ringRadius)} · minSpacing {Math.Max(1, minSpacing)})."
                : $"boss-prop volley TRUNCATED: {placed}/{count} placed (ringRadius {Math.Max(1, ringRadius)} · minSpacing {Math.Max(1, minSpacing)}); not enough spaced-out room.";
            return placed;
        }

        /// <summary>
        /// 다음 몬스터 페이즈의 살포 칸을 미리 뽑아 트랙에 저장한다(예고 · 2026-09-05). 오버레이는
        /// <see cref="GetBossPropVolleyTelegraphCells"/>로 읽는다. 뽑힌 칸 수를 돌려준다.
        /// </summary>
        internal int PlanBossPropVolley(string ownerBossUnitId, int count, int ringRadius, int minSpacing)
        {
            var owner = host.Monsters.FirstOrDefault(monster => string.Equals(monster.Id, ownerBossUnitId, StringComparison.Ordinal));
            var track = FindBossPhaseTrack(ownerBossUnitId);
            if (owner == null || track == null)
            {
                return 0;
            }

            track.PropVolleyTelegraphCells.Clear();
            if (count <= 0)
            {
                return 0;
            }

            track.PropVolleyTelegraphCells.AddRange(SelectBossPropVolleyCoords(owner, count, ringRadius, minSpacing, Array.Empty<HexCoord>()));
            return track.PropVolleyTelegraphCells.Count;
        }

        /// <summary>살아있는 보스들의 살포 예고 칸(표현용). 조우 전 보스·죽은 보스는 뺀다.</summary>
        internal IReadOnlyList<HexCoord> GetBossPropVolleyTelegraphCells()
        {
            var cells = new List<HexCoord>();
            foreach (var track in bossPhaseTracks)
            {
                if (track.PropVolleyTelegraphCells.Count == 0)
                {
                    continue;
                }

                var boss = host.Monsters.FirstOrDefault(monster => string.Equals(monster.Id, track.BossUnitId, StringComparison.Ordinal));
                if (boss == null || boss.Combatant.IsDead || IsBossAwaitingArenaEncounter(boss))
                {
                    continue;
                }

                cells.AddRange(track.PropVolleyTelegraphCells);
            }

            return cells;
        }

        /// <summary>
        /// 살포 좌표 선택(순수 — 스폰하지 않는다). 예고와 실제 배치가 <b>같은 선택</b>을 지나야 예고=배치가 성립한다.
        /// 링 밴드(r-1~r+1) 균등 분할 → 부족분은 아레나 전역(없으면 보스 기준 반경 4) 폴백.
        /// </summary>
        private List<HexCoord> SelectBossPropVolleyCoords(
            MonsterRuntime owner, int count, int ringRadius, int minSpacing, IReadOnlyList<HexCoord> alreadyTaken)
        {
            var result = new List<HexCoord>();
            if (count <= 0)
            {
                return result;
            }

            var anchor = ResolveBossPropAnchor(owner.Coord);
            var radius = Math.Max(1, ringRadius);
            var spacing = Math.Max(1, minSpacing);
            var taken = new List<HexCoord>(alreadyTaken ?? Array.Empty<HexCoord>());

            var band = HexArea.CellsInBand(anchor, Math.Max(1, radius - 1), radius + 1)
                .Where(candidate => host.IsSpawnableCoord(candidate) && IsInsideActiveBossArena(candidate))
                .ToList();

            var rotation = bossPropRng.NextDouble() * 2 * Math.PI;
            for (var slot = 0; slot < count; slot++)
            {
                var targetAngle = rotation + (2 * Math.PI * slot / count);
                var chosen = band
                    .Where(candidate => taken.All(t => t.DistanceTo(candidate) >= spacing))
                    .OrderBy(candidate => HexArea.AngleDistance(HexArea.AngleFrom(anchor, candidate), targetAngle))
                    .ThenBy(candidate => Math.Abs(anchor.DistanceTo(candidate) - radius))
                    .ThenBy(candidate => candidate)
                    .Select(candidate => (HexCoord?)candidate)
                    .FirstOrDefault();
                if (!chosen.HasValue)
                {
                    continue;
                }

                taken.Add(chosen.Value);
                result.Add(chosen.Value);
            }

            if (result.Count < count)
            {
                IEnumerable<HexCoord> fallbackArea = SealedBossArenaCoords.Count > 0
                    ? SealedBossArenaCoords
                    : HexArea.CellsWithin(anchor, Math.Max(radius + 1, 4));
                var fallback = fallbackArea
                    .Where(candidate => host.IsSpawnableCoord(candidate)
                                        && candidate.DistanceTo(owner.Coord) >= 1
                                        && !band.Contains(candidate))
                    .OrderBy(candidate => candidate)
                    .ToList();
                while (result.Count < count)
                {
                    var open = fallback.Where(candidate => taken.All(t => t.DistanceTo(candidate) >= spacing)).ToList();
                    if (open.Count == 0)
                    {
                        break;
                    }

                    var chosen = open[bossPropRng.Next(open.Count)];
                    fallback.Remove(chosen);
                    taken.Add(chosen);
                    result.Add(chosen);
                }
            }

            return result;
        }

        /// <summary>좌표를 정한 뒤의 실제 스폰 + 기물 소유/나이 초기화.</summary>
        private bool TrySpawnBossPropAt(string ownerBossUnitId, string propDefinitionId, HexCoord coord)
        {
            if (!host.TrySpawnMonsterAt(propDefinitionId, coord, MonsterSpawnRoles.BossProp, maxHp: 0, monsterId: null, out var newId))
            {
                return false;
            }

            var prop = host.Monsters.FirstOrDefault(monster => string.Equals(monster.Id, newId, StringComparison.Ordinal));
            if (prop != null)
            {
                prop.OwnerUnitId = ownerBossUnitId;
                prop.AgeTurns = 0;
            }

            return true;
        }

        /// <summary>
        /// 모든 보스 기물의 나이를 한 몬스터 페이즈만큼 늘린다. 보스가 여러 마리여도 페이즈당 한 번만
        /// 돌아야 하므로 기믹 안이 아니라 기믹 스텝 진입부에서 호출한다.
        /// </summary>
        private void AdvanceBossPropAges()
        {
            foreach (var monster in host.Monsters)
            {
                if (!monster.Combatant.IsDead && MonsterSpawnRoles.IsBossProp(monster.SpawnRole))
                {
                    monster.AgeTurns++;
                }
            }
        }
    }
}

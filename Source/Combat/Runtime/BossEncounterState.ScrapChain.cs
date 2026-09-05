using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>옛 <c>CombatState.BossScrapChain.cs</c>의 본문(3-B · 본문 무변경 이동).</summary>
    internal sealed partial class BossEncounterState
    {
        private readonly List<BossScrapChainHit> lastBossScrapChainHits = new List<BossScrapChainHit>();

        /// <summary>직전 결의의 사슬 명중 투영(표현 전용). <see cref="ClearLastBossPropEvents"/>가 결의 머리에서 비운다.</summary>
        public IReadOnlyList<BossScrapChainHit> LastBossScrapChainHits => lastBossScrapChainHits;

        /// <summary>
        /// 예고를 개시한다: 살아있는 <paramref name="propDefinitionId"/> 기물 각각으로 보스 중심에서
        /// 헥스 직선을 긋고, 그 칸들을 가닥 단위로 굳힌다(예고=명중). 기물이 없으면 아무 일도 없다.
        /// 보스 점유 칸은 뺀다 — 설 수 없는 칸의 위험 표시는 점유 오버레이와 어휘가 충돌한다(§20-B 선례).
        /// </summary>
        internal void BeginBossScrapChainTelegraph(BossPhaseTrack track, MonsterRuntime boss, string propDefinitionId)
        {
            if (track == null || boss == null || string.IsNullOrWhiteSpace(propDefinitionId))
            {
                return;
            }

            track.ScrapChainStrands.Clear();
            foreach (var prop in GetLivingBossProps(boss.Id, propDefinitionId))
            {
                var cells = HexArea.CellsOnLine(boss.Coord, prop.Coord)
                    .Where(coord => host.Map.TryGetCell(coord, out _) && !host.IsMonsterOccupying(boss, coord))
                    .Distinct()
                    .ToList();
                if (cells.Count > 0)
                {
                    track.ScrapChainStrands.Add(new ScrapChainStrand(prop.UnitId, cells));
                }
            }
        }

        /// <summary>
        /// 명중 해소: 살아있는 가닥의 칸에 플레이어가 서 있으면 피해(필드 피해 계약 — Block 먼저,
        /// 전부 막히면 "방어!") + 속박. 여러 가닥이 겹쳐도 <b>한 번만</b> 맞는다(교차점 서기가 두 배로
        /// 벌 받으면 안전한 칸 고르기가 퍼즐이 아니라 함정이 된다). 해소 후 예고는 항상 지워진다.
        /// </summary>
        internal void ResolveBossScrapChainHit(BossPhaseTrack track, MonsterRuntime boss, int damage, int rootTurns)
        {
            if (track == null || track.ScrapChainStrands.Count == 0)
            {
                return;
            }

            var hitPlayer = !host.Player.IsDead
                && EnumerateLiveScrapChainCells(track).Contains(host.PlayerCoord);
            // 표현 투영(후속 #7): 살아 있는 가닥만, 지우기 <b>전에</b> 굳힌다 — 지운 뒤엔 선을 그을 셀이 없다.
            var liveStrands = track.ScrapChainStrands
                .Where(strand => host.Monsters.Any(monster => !monster.Combatant.IsDead && string.Equals(monster.Id, strand.PropUnitId, StringComparison.Ordinal)))
                .Select(strand => (IReadOnlyList<HexCoord>)strand.Cells.ToList())
                .ToList();
            if (liveStrands.Count > 0)
            {
                lastBossScrapChainHits.Add(new BossScrapChainHit(boss?.Id, boss?.Coord ?? default, liveStrands, hitPlayer, host.PlayerCoord));
            }

            track.ScrapChainStrands.Clear();
            if (!hitPlayer)
            {
                return;
            }

            if (damage > 0)
            {
                var blockBefore = host.Player.Block;
                var applied = host.Player.ApplyDamage(damage);
                if (applied > 0)
                {
                    host.RaiseEffect(
                        EffectKind.Damage,
                        host.PlayerCoord,
                        0,
                        applied,
                        CombatState.PlayerUnitId,
                        CombatState.BossScrapChainSourceRefs.Hit,
                        sourceUnitId: boss?.Id ?? string.Empty,
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
                        CombatState.BossScrapChainSourceRefs.Hit,
                        sourceUnitId: boss?.Id ?? string.Empty,
                        sourceActorKind: "monster",
                        targetActorKind: "player");
                }
            }

            // 속박은 기존 부여 관문을 그대로 탄다: 하드 CC 면역창이면 건너뛰고, 수호가 무효화하면
            // 부여 VFX도 올리지 않는다(몬스터 공격 상태 부여와 같은 관용구).
            if (rootTurns > 0 && !host.Player.IsDead && !host.IsPlayerImmuneToControlStatus(StatusEffectKind.Immobilize))
            {
                var amount = StatusEffectInfo.DefaultAmount(StatusEffectKind.Immobilize);
                if (host.AddDurationStatusEffect(
                        StatusEffectKind.Immobilize,
                        CombatState.PlayerUnitId,
                        rootTurns,
                        amount,
                        CombatState.BossScrapChainSourceRefs.Hit))
                {
                    host.RaiseStatusEffect(
                        StatusEffectKind.Immobilize,
                        host.PlayerCoord,
                        0,
                        amount,
                        CombatState.PlayerUnitId,
                        CombatState.BossScrapChainSourceRefs.Hit,
                        sourceUnitId: boss?.Id ?? string.Empty);
                }
            }
        }

        /// <summary>
        /// 살아있는 보스들의 사슬 예고 칸(표현용 · 죽은 철조각의 가닥은 걸러진다 = 파괴 보상이 화면에
        /// 즉시 반영된다). 오버레이 빌더가 전멸기 예고와 같은 위험 레이어로 그린다.
        /// </summary>
        public IReadOnlyList<HexCoord> GetBossScrapChainTelegraphCells()
        {
            List<HexCoord> result = null;
            foreach (var track in bossPhaseTracks)
            {
                if (track.ScrapChainStrands.Count == 0)
                {
                    continue;
                }

                var boss = host.Monsters.FirstOrDefault(monster =>
                    string.Equals(monster.Id, track.BossUnitId, StringComparison.Ordinal));
                if (boss == null || boss.Combatant.IsDead)
                {
                    continue;
                }

                result ??= new List<HexCoord>();
                result.AddRange(EnumerateLiveScrapChainCells(track));
            }

            return (IReadOnlyList<HexCoord>)result?.Distinct().ToList() ?? Array.Empty<HexCoord>();
        }

        /// <summary>살아있는 가닥(철조각 생존)의 칸들 — 예고 오버레이와 명중 판정의 <b>단일</b> 필터.</summary>
        private IEnumerable<HexCoord> EnumerateLiveScrapChainCells(BossPhaseTrack track)
        {
            foreach (var strand in track.ScrapChainStrands)
            {
                var propAlive = host.Monsters.Any(monster =>
                    !monster.Combatant.IsDead
                    && string.Equals(monster.Id, strand.PropUnitId, StringComparison.Ordinal));
                if (!propAlive)
                {
                    continue;
                }

                foreach (var cell in strand.Cells)
                {
                    yield return cell;
                }
            }
        }
    }
}

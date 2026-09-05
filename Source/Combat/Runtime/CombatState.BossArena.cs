using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 보스 아레나: 진입 트리거와 동적 결계. 전투 진입 시엔 열려 있고 보스 조우 시 닫힌다.
    ///
    /// 특정 보스를 아는 코드는 여기에도 없다 — 아레나는 저작된 <see cref="HexMapAreaRef"/>이고
    /// 그 영역이 어떤 보스 스폰 ref에 묶였는지도 저작값이다. 맵에 세트를 저작하기만 하면 성립한다.
    /// </summary>
    public sealed partial class CombatState
    {
        // ── 3-B(2026-09-04): 본문은 BossEncounterState.Arena.cs. 여기는 시그니처 불변 위임과 중첩 타입만 남는다.
        public HexCoord? LastBossArenaEntryStopCoord => Boss.LastBossArenaEntryStopCoord;
        public string SealedBossArenaBossUnitId => Boss.SealedBossArenaBossUnitId;
        public string SealedBossArenaId => Boss.SealedBossArenaId;
        public bool IsBossArenaBarrierActive => Boss.IsBossArenaBarrierActive;
        public IReadOnlyList<HexCoord> SealedBossArenaCoords => Boss.SealedBossArenaCoords;
        public IReadOnlyList<HexCoord> SealedBossArenaBoundaryCoords => Boss.SealedBossArenaBoundaryCoords;
        public bool HasEncounteredLivingBoss => Boss.HasEncounteredLivingBoss;
        private bool IsBossAwaitingArenaEncounter(MonsterRuntime monster) => Boss.IsBossAwaitingArenaEncounter(monster);
        public bool IsMonsterHiddenBeforeBossArena(string monsterId) => Boss.IsMonsterHiddenBeforeBossArena(monsterId);
        private bool TryResolveBossArenaEntry(IReadOnlyList<HexCoord> path, out HexCoord stopCoord) => Boss.TryResolveBossArenaEntry(path, out stopCoord);
        private void NotifyBossArenaSealed() => Boss.NotifyBossArenaSealed();
        private void RevealSealedBossArena() => Boss.RevealSealedBossArena();
        private HexCoord ResolveBossPropAnchor(HexCoord bossCoord) => Boss.ResolveBossPropAnchor(bossCoord);
        private bool IsInsideActiveBossArena(HexCoord coord) => Boss.IsInsideActiveBossArena(coord);
        public HexCoord ResolveSealedBossArenaBattleStart() => Boss.ResolveSealedBossArenaBattleStart();
        private HexCoord ResolveBossArenaBattleStart(HexCoord entryCoord) => Boss.ResolveBossArenaBattleStart(entryCoord);
        private void ApplyBossArenaBarrierToOccupancy() => Boss.ApplyBossArenaBarrierToOccupancy();

        /// <summary>
        /// 결계가 닫힌 직후 발화한다(아레나 id). 표현 전용 — 규칙(좌표 강제 정지·점유 재구축)은
        /// 발화 <b>전에</b> 이미 끝나 있다.
        /// </summary>
        public event Action<string> BossArenaSealed;
    }
}

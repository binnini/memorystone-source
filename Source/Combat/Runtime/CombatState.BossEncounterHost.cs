using System;
using System.Collections.Generic;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 보스 조우 협력자의 호스트 구현(3-B). 명시적 구현이라 CombatState 공개 API는 늘지 않는다.
    /// 협력자는 생성자에서 만든다(<c>bossCatalog</c>는 생성자 인자 그대로 넘긴다) — 페이즈 트랙 보장이 생성자 안에서 돌기 때문.
    /// </summary>
    public sealed partial class CombatState : IBossEncounterHost
    {
        private readonly BossEncounterState boss;

        private BossEncounterState Boss => boss;

        /// <summary>테스트·서스펜드용 살아 있는 트랙 목록(옛 리플렉션 <c>GetField("bossPhaseTracks")</c>의 대체).</summary>
        internal List<BossPhaseTrack> BossPhaseTracksForTests => boss.Tracks;

        CombatState IBossEncounterHost.Self => this;
        IReadOnlyList<MonsterRuntime> IBossEncounterHost.Monsters => monsters;
        HexTerrainTraits IBossEncounterHost.TerrainTraits => terrainTraits;
        IReadOnlyDictionary<HexCoord, HexCellRuntimeState> IBossEncounterHost.RuntimeStates => runtimeStates;
        HexVisibilityRuntime IBossEncounterHost.Visibility => visibilityRuntime;
        Random IBossEncounterHost.PushRng => pushRng;

        bool IBossEncounterHost.IsTrapConsumed(string trapId) => consumedTrapIds.Contains(trapId);
        void IBossEncounterHost.RemoveMonster(MonsterRuntime monster) => monsters.Remove(monster);
        void IBossEncounterHost.SetPlayerCoord(HexCoord coord) => PlayerCoord = coord;
        void IBossEncounterHost.MarkTemporaryBlocked(HexCoord coord)
        {
            if (runtimeStates.TryGetValue(coord, out var existing))
            {
                existing.TemporaryBlocked = true;
            }
            else
            {
                runtimeStates[coord] = new HexCellRuntimeState(null, temporaryBlocked: true);
            }
        }
        void IBossEncounterHost.UpdateOccupancy() => UpdateOccupancy();
        void IBossEncounterHost.ResolveTrapTriggersAt(HexCoord coord) => ResolveTrapTriggersAt(coord);
        void IBossEncounterHost.RefreshPlayerVision() => RefreshPlayerVision();
        void IBossEncounterHost.RaiseBossPhaseChanged(string bossUnitId, int fromPhase, int toPhase) => BossPhaseChanged?.Invoke(bossUnitId, fromPhase, toPhase);
        void IBossEncounterHost.RaiseEffect(
            EffectKind kind, HexCoord center, int radius, int amount, string targetUnitId, string sourceRef,
            string sourceUnitId, string sourceActorKind, string targetActorKind,
            string sourceCardId, string sourcePatternId, int hitIndex, int hitCount, string presentationGroupId)
            => RaiseEffect(kind, center, radius, amount, targetUnitId, sourceRef, sourceUnitId, sourceActorKind, targetActorKind, sourceCardId, sourcePatternId, hitIndex, hitCount, presentationGroupId);
        void IBossEncounterHost.RaiseStatusEffect(
            StatusEffectKind kind, HexCoord center, int radius, int amount, string targetUnitId, string sourceRef,
            string sourceUnitId, string sourceActorKind, string targetActorKind,
            string sourceCardId, string sourcePatternId, int hitIndex, int hitCount, string presentationGroupId)
            => RaiseStatusEffect(kind, center, radius, amount, targetUnitId, sourceRef, sourceUnitId, sourceActorKind, targetActorKind, sourceCardId, sourcePatternId, hitIndex, hitCount, presentationGroupId);
        bool IBossEncounterHost.AddDurationStatusEffect(StatusEffectKind kind, string targetUnitId, int turns, int amount, string sourceRef) => AddDurationStatusEffect(kind, targetUnitId, turns, amount, sourceRef);
        bool IBossEncounterHost.IsMultiCellMonster(MonsterRuntime monster) => IsMultiCellMonster(monster);
        MonsterRuntime IBossEncounterHost.FindLivingMonsterAt(HexCoord target) => FindLivingMonsterAt(target);
        IReadOnlyList<HexCoord> IBossEncounterHost.GetMonsterFootprintOffsets(MonsterRuntime monster) => GetMonsterFootprintOffsets(monster);
        bool IBossEncounterHost.IsSpawnableCoord(HexCoord coord) => IsSpawnableCoord(coord);
        bool IBossEncounterHost.IsPlayerImmuneToControlStatus(StatusEffectKind kind) => IsPlayerImmuneToControlStatus(kind);
        IWeakSpotSlot IBossEncounterHost.ResolveWeakSpotSlot(MonsterRuntime monster) => ResolveWeakSpotSlot(monster);
        void IBossEncounterHost.ReselectWeakSpot(IWeakSpotSlot slot, MonsterRuntime monster) => ReselectWeakSpot(slot, monster);
        bool IBossEncounterHost.IsMonsterOccupying(MonsterRuntime monster, HexCoord coord) => IsMonsterOccupying(monster, coord);
        IEnumerable<HexCoord> IBossEncounterHost.EnumerateMonsterOccupiedCoords(MonsterRuntime monster) => EnumerateMonsterOccupiedCoords(monster);
        void IBossEncounterHost.AddRuntimeTrap(HexCoord coord, HexTrapEffectData effect) => AddRuntimeTrap(coord, effect);
        bool IBossEncounterHost.TrySpawnMonsterAt(string definitionId, HexCoord coord, string spawnRole, int maxHp, string monsterId, out string spawnedId) => TrySpawnMonsterAt(definitionId, coord, spawnRole, maxHp, monsterId, out spawnedId);
    }
}

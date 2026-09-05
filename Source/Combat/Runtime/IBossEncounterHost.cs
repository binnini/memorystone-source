using System;
using System.Collections.Generic;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// <see cref="BossEncounterState"/>가 <see cref="CombatState"/>에서 필요로 하는 조각(3-B). 읽기는 공개 속성 그대로,
    /// 쓰기는 <b>호스트 메서드</b>(몬스터 제거·플레이어 좌표·결계 점유)로만 — 컬렉션을 통째로 내주지 않는다.
    /// RNG(<c>PushRng</c>)는 <b>같은 인스턴스</b>를 노출한다(전투 RNG는 네 곳에 흩어져 있어 시드 재현이 불가하고, 결과는 서스펜드
    /// 저장으로 지킨다는 기존 계약 — 협력자가 새 RNG를 만들면 그 계약이 깨진다).
    /// <c>Self</c>는 <see cref="BossMechanicContext"/>를 만들 때만 쓴다(기믹의 이음새가 CombatState라는 계약은 그대로).
    /// </summary>
    internal interface IBossEncounterHost
    {
        CombatState Self { get; }
        HexMapData Map { get; }
        CombatantState Player { get; }
        HexCoord PlayerCoord { get; }
        int OverallTurnNumber { get; }
        IReadOnlyList<MonsterRuntime> Monsters { get; }
        HexTerrainTraits TerrainTraits { get; }
        IReadOnlyDictionary<HexCoord, HexCellRuntimeState> RuntimeStates { get; }
        HexVisibilityRuntime Visibility { get; }
        Random PushRng { get; }
        FieldObjectRegistry FieldObjects { get; }
        FieldObjectRegistry PendingFieldObjects { get; }
        IEnumerable<HexTrapData> AllTrapRefs { get; }

        bool IsTrapConsumed(string trapId);
        void RemoveMonster(MonsterRuntime monster);
        void SetPlayerCoord(HexCoord coord);
        void MarkTemporaryBlocked(HexCoord coord);
        void UpdateOccupancy();
        void ResolveTrapTriggersAt(HexCoord coord);
        void RefreshPlayerVision();
        void RaiseBossPhaseChanged(string bossUnitId, int fromPhase, int toPhase);
        void RaiseEffect(
            EffectKind kind, HexCoord center, int radius, int amount, string targetUnitId, string sourceRef,
            string sourceUnitId = "", string sourceActorKind = "", string targetActorKind = "",
            string sourceCardId = "", string sourcePatternId = "", int hitIndex = 0, int hitCount = 0, string presentationGroupId = "");
        void RaiseStatusEffect(
            StatusEffectKind kind, HexCoord center, int radius, int amount, string targetUnitId, string sourceRef,
            string sourceUnitId = "", string sourceActorKind = "", string targetActorKind = "",
            string sourceCardId = "", string sourcePatternId = "", int hitIndex = 0, int hitCount = 0, string presentationGroupId = "");
        bool AddDurationStatusEffect(StatusEffectKind kind, string targetUnitId, int turns, int amount, string sourceRef);
        bool IsMultiCellMonster(MonsterRuntime monster);
        MonsterRuntime FindLivingMonsterAt(HexCoord target);
        IReadOnlyList<HexCoord> GetMonsterFootprintOffsets(MonsterRuntime monster);
        bool IsSpawnableCoord(HexCoord coord);
        bool IsPlayerImmuneToControlStatus(StatusEffectKind kind);
        IWeakSpotSlot ResolveWeakSpotSlot(MonsterRuntime monster);
        void ReselectWeakSpot(IWeakSpotSlot slot, MonsterRuntime monster);
        bool IsMonsterOccupying(MonsterRuntime monster, HexCoord coord);
        IEnumerable<HexCoord> EnumerateMonsterOccupiedCoords(MonsterRuntime monster);
        void AddRuntimeTrap(HexCoord coord, HexTrapEffectData effect);
        bool TrySpawnMonsterAt(string definitionId, HexCoord coord, string spawnRole, int maxHp, string monsterId, out string spawnedId);
    }
}

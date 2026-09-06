using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    // Save DTO contract (mirrors PlayerRunSaveData): JsonUtility persistence serializes public fields
    // only, so every member here is a public field (never an auto-property) and Nullable<T> is not
    // allowed. Optional values use a has-flag + plain value pair. This is the ② full-snapshot suspend
    // payload: unlike the ① PlayerRunSaveData (turn-boundary player loadout only), it captures the
    // entire combat so a resumed session re-enters the exact mid-run board state.
    [Serializable]
    public sealed class CombatSuspendData
    {
        // Combat context. Turn/phase/Ki are authoritative here (the player payload carries a copy for
        // the loadout projection, but RestoreFromSuspend reads the context fields).
        public int OverallTurn;
        public CombatPhase Phase;
        public int ActionCostRemaining;
        // 은퇴(P3-b, 2026-09-06): 빠른 거북(옛 팩토리 전용 Draft 카드)이 사라져 더는 읽거나 쓰지 않는다.
        // 세이브 와이어 포맷을 바꾸지 않으려고 필드만 남긴다 — 항상 0.
        public int RevealedFastTurtleDistance;
        public int PendingMovementRangeBonus;
        // Duration of the carried Agility grant. Envelopes written before this field existed deserialize to
        // 0; RestoreFromSuspend reads that as "1 turn", which is what the value always was when hardcoded.
        public int PendingMovementRangeBonusTurns;
        // 속박 booked by D04/D06 for the next turn. Must survive suspend/resume: dropping it would let a
        // player bank the block and then save-scum the penalty away.
        public int PendingSelfImmobilizeTurns;

        /// <summary>D02 「다음 턴 적 강화」 예약분(WS-I I-14). 지연 자기 속박과 같은 이유로 서스펜드를 넘는다.</summary>
        public int PendingProvokeStrengthAmount;
        public int PendingProvokeStrengthTurns;

        /// <summary>미련(X06, T2): 다음 턴 기 감소 예약분.</summary>
        public int PendingNextTurnKiPenalty;
        public int ActiveMovementRangeModifier;
        public int LastMovedDistance;
        public int ActionCardsUsedThisTurn;

        /// <summary>코인 세탁기(T2 페이즈 C)의 전투 누적 행동 카드 사용 수. 구 세이브는 0으로 열린다.</summary>
        public int TotalActionCardsUsed;

        /// <summary>맹호 호리병(T4-1)의 이번 턴 한정 공격 보너스. 구 세이브는 0으로 열린다.</summary>
        public int BagAttackBonusThisTurn;

        // 약오름(T7-2) 적립 신호 — 턴 한정 값이지만 서스펜드가 턴 중간에 올 수 있어 왕복한다
        // (bagAttackBonusThisTurn과 같은 이유).
        public bool DefensiveCardUsedThisTurn;
        public bool ObjectiveCompleted;
        public string MarkedMonsterId = string.Empty;
        public List<string> ConsumedTrapIds = new List<string>();

        // 정찰로 발견한 함정 좌표(FW-6). 판에서 재유도할 수 없는 순수 진행 상태라 저장하지 않으면 재개 후
        // 찾아 둔 함정이 도로 안개에 묻힌다(fog-of-war.md GAP-2). 주기 함정(C-11)의 예고가 이 집합에
        // 걸려 있어 더 이상 미룰 수 없었다 — 예고가 세이브 한 번에 사라지면 "보고 피한다"가 성립하지 않는다.
        // 이 필드가 없던 엔벨로프는 빈 목록으로 역직렬화되어 예전과 같은(=발견 기록 없음) 상태가 된다.
        public List<HexCoordSaveData> RevealedTrapCoords = new List<HexCoordSaveData>();
        // 영구 공개 칸(보스 아레나 · 2026-09-05). 없던 세이브는 빈 목록 = 영구 공개 없음으로 열린다.
        public List<HexCoordSaveData> PermanentlyRevealedCoords = new List<HexCoordSaveData>();
        public List<string> ClaimedEventObjectIds = new List<string>();

        // Player unit + owned decks/inventory/objective (reuses the ① serialization surface).
        public PlayerRunSaveData Player = new PlayerRunSaveData();

        // Non-player runtime state that cannot be re-derived from the board alone.
        public List<MonsterRuntimeSaveData> Monsters = new List<MonsterRuntimeSaveData>();

        // 2026-09-05 #7: 이 스냅샷이 몬스터별 RewardClaimed를 기록했는가. 이 필드가 없던 옛 저장(false)은
        // 죽은 몬스터 전부를 「보상 지급됨」으로 복원한다 — 복원마다 보상을 다시 뿌리는 것보다 낫다.
        public bool TracksRewardClaims;
        public List<ActiveEffectSaveData> ActiveEffects = new List<ActiveEffectSaveData>();
        public List<HexCellVisibilitySaveData> Visibility = new List<HexCellVisibilitySaveData>();

        // Active board field objects (deployed field-effect tiles: fog reveals, damage/heal/immobilize
        // fields) and any not-yet-activated pending ones. Authored traps are NOT here — those are map data
        // reconstructed from consumedTrapIds.
        public List<FieldObjectSaveData> FieldObjects = new List<FieldObjectSaveData>();
        public List<FieldObjectSaveData> PendingFieldObjects = new List<FieldObjectSaveData>();

        // 런타임(보스 배치) 함정(§21.5). 저작 함정은 맵 데이터라 consumedTrapIds로 재구성되지만
        // 런타임 함정은 판에서 재유도할 수 없다 — 왕복하지 않으면 재개 후 깔린 함정이 통째로 사라진다.
        // 시퀀스를 함께 왕복하지 않으면 재개 후 새 함정이 살아 있는 함정과 id가 충돌해 소진 기록이 엉킨다.
        // 이 필드가 없던 옛 엔벨로프는 빈 목록/0으로 열려 예전과 같은(=런타임 함정 없음) 상태가 된다.
        public List<RuntimeTrapSaveData> RuntimeTraps = new List<RuntimeTrapSaveData>();
        public int RuntimeTrapSequence;

        // 보스 페이즈 트랙. 페이즈는 순수 신규 상태라 재유도가 불가능하다(지표가 누적형이면 더더욱):
        // 저장하지 않으면 재개 시 보스가 1페이즈로 되돌아가 이미 쌓은 성장이 사라진다.
        public List<BossPhaseTrackSaveData> BossPhaseTracks = new List<BossPhaseTrackSaveData>();

        // 봉인된 보스 아레나 id(없으면 빈 문자열). 결계는 조우 시 한 번 닫히는 1회성 상태라 판에서
        // 재유도할 수 없다 — 저장하지 않으면 재개 후 결계가 풀려 보스전을 걸어 나갈 수 있다.
        // 결계가 실제로 막고 있는지는 "묶인 보스 생존"과 함께 매번 유도되므로 여기 담지 않는다.
        public string SealedBossArenaId = string.Empty;

        // 난수 커서(seed-determinism-handoff P5). 「굴린 결과를 저장」 전략에 「굴림 위치」를 하나 더 싣는다 —
        // 없으면 재개 시 모든 스트림이 첫 칸으로 되감겨 k+1턴 이후의 셔플·판정·패턴이 무중단 판과 갈린다.
        // 전부 int 기본값 0 → 이 필드가 없던 세이브는 지금과 같은 동작. SchemaVersion은 올리지 않는다(additive).
        public RngCursorsSaveData RngCursors = new RngCursorsSaveData();
    }

    /// <summary>
    /// 스트림별 소비 횟수(P5). 런 시드 하나를 스트림별 <c>System.Random</c> 인스턴스로 갈라 쓰므로 커서도 인스턴스마다
    /// 하나다 — 총합 하나로는 되돌릴 수 없다. 보상 커서(스트림 7)는 컨트롤러 소유라 봉투(<see cref="CombatSuspendEnvelope.RewardCursor"/>)에 있다.
    /// </summary>
    [Serializable]
    public sealed class RngCursorsSaveData
    {
        public int Judgement;
        public int AttackPattern;
        public int DamageJitter;
        public int BossProps;
        public int MovementShuffle;
        public int ActionShuffle;
    }

    [Serializable]
    public sealed class BossPhaseTrackSaveData
    {
        public string BossUnitId = string.Empty;
        public string BossDefinitionId = string.Empty;
        public int CurrentPhase = 1;
        public int MetricProgress;
        public int AbsorbedStacks;
        // 이미 적용한 최대 체력 보너스. 몬스터 MaxHp에는 이 보너스가 이미 포함되어 저장되므로,
        // 이 값을 복원하지 않으면 재개 후 첫 페이즈 적용이 보너스를 한 번 더 얹는다.
        public int AppliedMaxHpBonus;
        // 기믹 턴 카운터(철조각의 살포 쿨다운). 왕복하지 않으면 저장/재개로 주기가 0으로 되감겨
        // 재개할 때마다 볼리가 한 번 더 터진다 — 기물 나이(AgeTurns)를 왕복시키는 것과 같은 이유다.
        public int MechanicCooldownTurns;

        // 함정 배치(trap-volley · §21.5) 주기 카운터. 왕복 근거는 MechanicCooldownTurns와 같다.
        public int TrapVolleyCooldownTurns;

        // 수호(guard · DEC-2026-09-03-03) 페이즈당 1회 부여 래치. 왕복하지 않으면 재개할 때마다
        // 현재 페이즈 몫의 충전이 다시 부여된다(세이브 스컴).
        public int GuardChargesGrantedPhase;

        // 철조각 사슬(scrap-chain · §21.8 제안 2). 쿨다운을 왕복하지 않으면 재개마다 주기가 되감기고,
        // 가닥(예고=명중의 저장본)을 왕복하지 않으면 재개 직후 예고 없이 명중하거나 예고가 증발한다.
        public int ScrapChainCooldownTurns;
        public List<ScrapChainStrandSaveData> ScrapChainStrands = new List<ScrapChainStrandSaveData>();

        // 철조각 살포 예고(2026-09-05): 다음 몬스터 페이즈에 놓일 칸. 왕복하지 않으면 재개 직후 예고 없이 살포된다.
        public List<HexCellSaveData> PropVolleyTelegraphCells = new List<HexCellSaveData>();

        // 전멸기(annihilation · §13.5) 상태. 쿨다운을 왕복하지 않으면 재개마다 주기가 되감기고,
        // 예고(칸 집합 + 남은 턴)를 왕복하지 않으면 재개 직후 행동에 예고 없이 터지거나 증발한다.
        // 이 필드가 없던 예전 세이브는 기본값(0/빈 목록) = "예고 없음·쿨다운 0"으로 안전하게 열린다.
        public int AnnihilationCooldownTurns;
        public int AnnihilationTelegraphTurnsRemaining;
        public List<HexCellSaveData> AnnihilationTelegraphCells = new List<HexCellSaveData>();

        // 안전지대 후보와 진위 배정(§20-B). 왕복하지 않으면 저장/재개로 진위를 다시 굴리는
        // 세이브 스컴이 된다. 판별 기록까지 함께 왕복해야 재개 후 `?`가 되살아나지 않는다.
        public List<HexCellSaveData> AnnihilationCandidateCells = new List<HexCellSaveData>();
        public List<HexCellSaveData> AnnihilationRealSafeCells = new List<HexCellSaveData>();
        public List<HexCellSaveData> AnnihilationRevealedCandidates = new List<HexCellSaveData>();

        // 취약 부위(§20-A). 왕복하지 않으면 저장/재개로 자리를 다시 굴리는 세이브 스컴이 된다.
        // 옛 세이브는 기본값(HasWeakSpot=false / 0)으로 열리고 다음 결의에서 즉시 재선정되므로 안전하다.
        public bool HasWeakSpot;
        public int WeakSpotOffsetQ;
        public int WeakSpotOffsetR;
        public int WeakSpotKnownTurnsRemaining;
    }

    /// <summary>JsonUtility가 직렬화하는 헥스 셀 좌표(HexCoord는 readonly struct라 직접 직렬화 불가).</summary>
    [Serializable]
    public sealed class HexCellSaveData
    {
        public int Q;
        public int R;
    }

    /// <summary>
    /// 런타임(보스 배치) 함정 한 개(§21.5). <c>HexTrapData</c>가 readonly struct라 직접 직렬화가 안 되므로
    /// 필드를 펼쳐 담는다(<see cref="FieldObjectSaveData"/>와 같은 규약).
    /// </summary>
    [Serializable]
    public sealed class RuntimeTrapSaveData
    {
        public string TrapId = string.Empty;

        /// <summary>함정의 <b>종류</b>(도감 해금 키). 인스턴스 id인 <see cref="TrapId"/>와 다르다.</summary>
        public string PresetId = string.Empty;

        public int Q;
        public int R;
        public int Radius;
        public bool AffectsPlayer = true;
        public bool AffectsMonsters;
        public bool OneShot = true;
        public bool TriggerOnEnter = true;
        public int PeriodTurns;
        public List<TrapEffectSaveData> Effects = new List<TrapEffectSaveData>();

        public static RuntimeTrapSaveData FromTrap(SeoulPlayup.Map.Runtime.HexTrapData trap)
        {
            var save = new RuntimeTrapSaveData
            {
                TrapId = trap.TrapId,
                PresetId = trap.PresetId,
                Q = trap.Coord.Q,
                R = trap.Coord.R,
                Radius = trap.Radius,
                AffectsPlayer = trap.AffectsPlayer,
                AffectsMonsters = trap.AffectsMonsters,
                OneShot = trap.OneShot,
                TriggerOnEnter = trap.TriggerOnEnter,
                PeriodTurns = trap.PeriodTurns
            };
            foreach (var effect in trap.Effects)
            {
                save.Effects.Add(new TrapEffectSaveData
                {
                    Kind = effect.Kind,
                    Amount = effect.Amount,
                    DurationTurns = effect.DurationTurns,
                    MonsterDefinitionId = effect.MonsterDefinitionId,
                    StatusCardId = effect.StatusCardId
                });
            }

            return save;
        }

        public SeoulPlayup.Map.Runtime.HexTrapData ToTrap()
        {
            return new SeoulPlayup.Map.Runtime.HexTrapData(
                TrapId,
                new HexCoord(Q, R),
                Radius,
                (Effects ?? new List<TrapEffectSaveData>()).Select(effect => new SeoulPlayup.Map.Runtime.HexTrapEffectData(
                    effect.Kind,
                    effect.Amount,
                    effect.DurationTurns,
                    effect.MonsterDefinitionId,
                    effect.StatusCardId)),
                AffectsPlayer,
                AffectsMonsters,
                OneShot,
                TriggerOnEnter,
                PeriodTurns,
                PresetId);
        }
    }

    /// <summary>사슬 가닥 한 개(§21.8 제안 2): 대상 철조각 유닛 id + 예고 시점에 굳힌 경로 칸.</summary>
    [Serializable]
    public sealed class ScrapChainStrandSaveData
    {
        public string PropUnitId = string.Empty;
        public List<HexCellSaveData> Cells = new List<HexCellSaveData>();
    }

    /// <summary>런타임 함정의 효과 한 항.</summary>
    [Serializable]
    public sealed class TrapEffectSaveData
    {
        // JsonUtility가 enum을 int로 직렬화하므로 HexTrapEffectKind는 append-only여야 한다(기존 규약).
        public SeoulPlayup.Map.Runtime.HexTrapEffectKind Kind;
        public int Amount;
        public int DurationTurns;
        public string MonsterDefinitionId = string.Empty;
        public string StatusCardId = string.Empty;
    }

    [Serializable]
    public sealed class FieldObjectSaveData
    {
        public int Q;
        public int R;
        public int Radius;
        public int RemainingTurns;
        public int Value;
        // JsonUtility persists this enum by its underlying int, so FieldObjectKind must stay append-only
        // (never reorder existing values) for save compatibility.
        public FieldObjectKind Kind;
        public string SourceUnitId = string.Empty;
        public string VisualRef = string.Empty;
        // Hits per tick (F05 = 2). Defaults to 1 so an envelope written before this field existed still
        // round-trips to the pre-F05 single-hit behaviour instead of a 0-hit no-op field.
        public int HitsPerTick = 1;
        // 상태이상 지대(StatusZone)가 거는 상태이상. StatusEffectKind도 int로 실리므로 append-only가
        // 계약이다(cs:846 드리프트 실증). 이 필드가 없던 봉투는 기본값으로 내려앉고, 그 기본값을 읽는
        // kind는 StatusZone뿐이라 기존 장판은 영향이 없다.
        public StatusEffectKind StatusKind;

        public static FieldObjectSaveData FromFieldObject(FieldObject fieldObject)
        {
            return new FieldObjectSaveData
            {
                Q = fieldObject.Position.Q,
                R = fieldObject.Position.R,
                Radius = fieldObject.Radius,
                RemainingTurns = fieldObject.RemainingTurns,
                Value = fieldObject.Value,
                Kind = fieldObject.Kind,
                SourceUnitId = fieldObject.SourceUnitId,
                VisualRef = fieldObject.VisualRef,
                HitsPerTick = fieldObject.HitsPerTick,
                StatusKind = fieldObject.StatusKind
            };
        }

        public FieldObject ToFieldObject()
        {
            return new FieldObject(new HexCoord(Q, R), Radius, RemainingTurns, Kind, Value, SourceUnitId, VisualRef, HitsPerTick, StatusKind);
        }
    }

    [Serializable]
    public sealed class HexCoordSaveData
    {
        public int Q;
        public int R;

        public HexCoordSaveData() { }

        public HexCoordSaveData(HexCoord coord)
        {
            Q = coord.Q;
            R = coord.R;
        }

        public HexCoord ToCoord() => new HexCoord(Q, R);
    }

    [Serializable]
    public sealed class MonsterRuntimeSaveData
    {
        public string Id = string.Empty;
        public string CatalogSourceId = string.Empty;
        public string DefinitionId = string.Empty;
        public string SpawnRefId = string.Empty;
        public string SpawnRole = string.Empty;
        public int Q;
        public int R;
        public int SpawnQ;
        public int SpawnR;
        public int Hp;
        public int MaxHp;
        public int Block;
        public MonsterActivityState ActivityState;
        public int AttackPatternIndex;
        public bool PendingAttackIntent;

        // 턴별 피해 변주(damageJitter)의 이번 의도 굴림값. RNG 상태가 아니라 굴린 결과를 싣는다
        // (DEC-2026-07-18-02 스냅샷 원칙) — 안 실으면 재개 직후 한 턴의 예고가 집행과 어긋난다.
        // 구세이브에는 키가 없어 0(변주 없음)으로 내려앉는다.
        public int AttackDamageRollOffset;

        // 보스 기물(boss-prop)용. 일반 몬스터에서는 빈 문자열 / 0으로 남는다. 나이를 잃으면 재개 후
        // 성숙이 초기화되어 세이브 스컴 수단이 되므로 반드시 왕복해야 한다.
        public string OwnerUnitId = string.Empty;
        public int AgeTurns;

        // 적 문법(T7-2). 잃으면 재개가 약오름 스택을 씻고 맷집을 재충전하는 세이브 스컴이 된다.
        // 구 세이브에는 필드가 없어 기본값(스택 0·맷집 준비됨·진행 0)으로 복원된다 — 문법 미저작
        // 몬스터와 같은 상태라 안전하다.
        public int AgitationStacks;
        public bool RewardClaimed;

        // 형상 footprint 정예의 취약 부위(2026-09-03). 보스는 BossPhaseTrack 쪽에 따로 실린다. 안 실으면
        // 재개가 자리를 다시 굴리는 세이브 스컴이 된다. 구 세이브·한 칸 몬스터는 HasWeakSpot=false로 내려앉는다.
        public bool HasWeakSpot;
        public int WeakSpotOffsetQ;
        public int WeakSpotOffsetR;
        public int WeakSpotKnownTurnsRemaining;

        /// <summary>소매치기(야광귀 · 2026-09-04)가 훔친 엽전 누적액. 구 세이브(겁먹음 LastPlayerDistance
        /// 필드가 있던 봉투)는 이 필드가 없어 0으로 열린다 — 소매치기 미저작 몬스터와 같은 상태라 무해하다.</summary>
        public int StolenMoney;
        public bool ToughnessSpent;
        public int ToughnessReloadProgress;
        /// <summary>수호 재충전 진행(2026-09-05). 이 필드가 없던 봉투는 0으로 열린다 — 특성 없는 몬스터에게는 읽히지 않는 값이라 무해하다.</summary>
        public int GuardRechargeProgress;
        // 은신 노출 잔여 턴(요괴 §4-1). 이 필드가 없던 봉투는 0(=숨음)으로 열리는데, 은신 특성이 없는
        // 몬스터에게는 어차피 읽히지 않는 값이라 기존 세이브에 무해하다.
        public int StealthRevealTurnsRemaining;

        // FSM memory (7 fields). LastKnownPlayerCoord is Nullable in the runtime, stored here as a
        // has-flag + plain Q/R pair per the JsonUtility contract.
        public MonsterFsmState FsmState;
        public MonsterFsmState FsmPreAlertState;
        public bool HasLastKnownPlayerCoord;
        public int LastKnownPlayerQ;
        public int LastKnownPlayerR;
        public int SearchTurnsRemaining;
        public int AlertTurnsRemaining;
        public int AlertRangeBonus;
        public int PatrolCursor;

        // Per-attack-pattern-index remaining cooldown (private runtime dictionary projected to a list).
        public List<MonsterCooldownSaveData> AttackPatternCooldowns = new List<MonsterCooldownSaveData>();

        // Board-derived immutable that InitializeMonsters resolves from the spawn ref; saved so the
        // catalog-based rebuild in RestoreFromSuspend stays lossless for patrol behavior.
        public List<HexCoordSaveData> PatrolArea = new List<HexCoordSaveData>();
    }

    [Serializable]
    public sealed class MonsterCooldownSaveData
    {
        public int Index;
        public int Remaining;
    }

    [Serializable]
    public sealed class ActiveEffectSaveData
    {
        public EffectType Type;
        public StatusEffectKind Kind;
        public string TargetUnitId = string.Empty;
        public int RemainingTurns;
        public int Amount;
        public string SourceRef = string.Empty;
        public bool SkipNextTick;

        public static ActiveEffectSaveData FromEffect(ActiveEffect effect)
        {
            return new ActiveEffectSaveData
            {
                Type = effect.Type,
                Kind = effect.Kind,
                TargetUnitId = effect.TargetUnitId,
                RemainingTurns = effect.RemainingTurns,
                Amount = effect.Amount,
                SourceRef = effect.SourceRef,
                SkipNextTick = effect.SkipNextTick
            };
        }

        public ActiveEffect ToEffect()
        {
            return new ActiveEffect(Type, Kind, TargetUnitId, RemainingTurns, Amount, SourceRef, SkipNextTick);
        }
    }

    [Serializable]
    public sealed class HexCellVisibilitySaveData
    {
        public int Q;
        public int R;
        public HexCellVisibility Visibility;

        public HexCellVisibilitySaveData() { }

        public HexCellVisibilitySaveData(HexCoord coord, HexCellVisibility visibility)
        {
            Q = coord.Q;
            R = coord.R;
            Visibility = visibility;
        }

        public HexCoord ToCoord() => new HexCoord(Q, R);
    }
}

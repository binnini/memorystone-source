using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime.Cards;
using SeoulPlayup.Map.Runtime;
using CardDefinition = SeoulPlayup.CardCore.CardDefinition;
using CardEffectType = SeoulPlayup.CardCore.CardEffectType;

namespace SeoulPlayup.Combat.Runtime
{
    public sealed partial class CombatState
    {
        internal const string PlayerUnitId = "player";
        private const string EnemyUnitId = "normal-enemy";
        private const int ScoutHintRadius = 2;

        /// <summary>
        /// 다단 히트의 <b>박자</b>(2026-09-02 #3 · 사용자 실플레이: "반복 공격인데 VFX·사운드가 한 번만
        /// 난다"). 첫 히트는 타격 순간 그대로 두고, 뒤 히트를 <b>시간으로</b> 벌린다.
        ///
        /// <para>🔴 <b>횟수가 아니라 시간차가 「반복」을 만든다.</b> 종전에는 몬스터·플레이어 다단이
        /// 히트마다 별개 이벤트를 내면서도 <b>지연을 0으로</b> 뒀다 — 세 발이 같은 프레임에 한 점으로
        /// 겹치니 그림도 소리도 한 발이었다. 사운드는 그 위에 큐 쿨다운(<c>combat.*.hit</c> 0.05초)까지
        /// 얹혀 뒤 히트가 통째로 삼켜졌다. 간격 0.06은 그 쿨다운보다 크다.</para>
        ///
        /// <para>🔑 값의 모양은 새로 만든 것이 아니라 <b>이미 쓰던 것</b>이다(장판 다단 틱
        /// <c>CombatState.FieldObjectCardEffects</c>와 같은 <c>0.12 + 0.06×n</c>).</para>
        /// </summary>
        private const float MultiHitLeadBeatDelaySeconds = 0.12f;
        private const float MultiHitBeatSpacingSeconds = 0.06f;

        /// <summary>히트 0은 타격 그 순간(0초), 그 뒤는 한 박씩 물러난다.</summary>
        private static float MultiHitBeatDelaySeconds(int hitIndex)
        {
            return hitIndex <= 0 ? 0f : MultiHitLeadBeatDelaySeconds + (MultiHitBeatSpacingSeconds * hitIndex);
        }

        private readonly Dictionary<HexCoord, HexCellRuntimeState> runtimeStates = new Dictionary<HexCoord, HexCellRuntimeState>();
        // 전투 판정 난수(취약 부위·빠른 거북·순간이동지·전염 대상·되돌릴 부적·제거될 카드).
        // 런 시드가 있으면 스트림 5, 없으면 무시드 — 생성자에서 정한다(seed-determinism P4).
        // 시드가 있으면 CountingRandom이고 서스펜드 복원이 저장된 커서로 다시 만든다(P5, CombatState.RngCursors.cs).
        private System.Random pushRng;
        // 런타임 카드 인스턴스 id의 결정적 카운터(P4). Guid 대신 쓴다 — 봉인 순위가 InstanceId를
        // 섞으므로 Guid는 「같은 시드 → 같은 판」을 곧장 깼다. 재개 뒤엔 0부터 다시 세므로
        // 저장된 id와 겹칠 수 있고, 그래서 발급부가 사용 중인 id를 건너뛴다.
        private int runtimeCardInstanceSequence;
        // Remaining turns the player is immune to RE-application of a hard control status (stun/immobilize)
        // after it expires. Guarantees at least one free turn between control applications so the player can
        // never be perma-locked by chained status attacks.
        private readonly Dictionary<StatusEffectKind, int> playerControlStatusImmunityTurns = new Dictionary<StatusEffectKind, int>();
        private const int PlayerControlStatusImmunityTurns = 1;
        internal readonly List<MonsterRuntime> monsters = new List<MonsterRuntime>();
        private readonly HexVisibilityRuntime visibilityRuntime;
        private readonly HexCoord? objectiveTargetCoord;
        private readonly HexObjectiveBinding objectiveBinding;
        private readonly HexTerrainTable terrainTable;
        private readonly HexTerrainTraits terrainTraits;
        private readonly MonsterCatalogDefinition monsterCatalog;
        // 보스 확장 데이터(페이즈/기믹). 보스가 없는 전투가 압도적으로 많으므로 기본값은 Empty이고,
        // 디스크/TextAsset 로딩은 호출부(MapCombatController)가 책임진다 — 여기서 파일을 읽으면
        // 보스와 무관한 모든 전투 생성이 CSV 입출력에 묶인다.
        private readonly List<MonsterActionResolutionRecord> lastMonsterActionRecords = new List<MonsterActionResolutionRecord>();
        private Dictionary<string, MonsterActionResolutionBuilder> pendingMonsterActionRecords;
        // Cells revealed by a Scout card during the current overall turn. Unlike field objects — which
        // RefreshPlayerVision re-unions every refresh while they live — a bare ScoutReveal would decay back
        // to Hinted on the very next vision refresh. We keep the scouted cells as a live reveal source so a
        // scouted tile stays Revealed until the turn ends (cleared in BeginNextOverallTurn), matching the
        // field-card vision lifetime. This stops a monster the player scouted this turn from being
        // misclassified as a fog ambush during the end-of-turn monster phase.
        private readonly HashSet<HexCoord> scoutRevealedThisTurn = new HashSet<HexCoord>();
        private readonly ActiveEffectRegistry activeEffects = new ActiveEffectRegistry();
        private readonly HashSet<string> consumedTrapIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> claimedEventObjectIds = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// 🔴 <b>예약 효과 한 덩어리</b>(2026-08-31 T6). 예전에는 <c>pending*</c> 정수·불리언 8개가
        /// 이 클래스에 흩어져 있었고, 그중 셋이 (Amount, Turns) 쌍이라 같은 병합 규칙이 두 번
        /// 복사돼 있었다. 자세한 사정은 <see cref="PendingEffects"/> 주석.
        ///
        /// ⚠️ 미련(X06)이 드로우 시점 트리거로 바뀌면서(2026-08-20 #18) 기 차감 예약은 더 이상
        /// 적립되지 않는다. 그래도 남겨 둔다 — 세이브 스키마를 깎는 것이 이 변경의 몫이 아니고,
        /// 예전 세이브가 들고 있던 값을 복원 후 한 번 소비하고 0이 되는 것이 정확한 마이그레이션이다.
        /// </summary>
        internal readonly PendingEffects pending = new PendingEffects();

        // --- 텔레포트 함정(C-8 / D-3) 해소 상태. 전부 한 번의 함정 해소 사이클 안에서만 살아 있으므로
        // 세이브에 실리지 않는다(ConsumedTrapIds와 달리 사이클을 넘겨 관찰되는 상태가 아니다).
        // 텔레포트가 예약된 목적지. ApplyTrapEffect가 채우고 ResolveTrapTriggersAt이 순회를 끝낸 뒤 소비한다.
        private HexCoord? pendingTeleportDestination;
        // 이 사이클에서 텔레포트가 이미 한 번 걸렸는가. 착지 칸의 텔레포트 함정만 막고 다른 함정은 통과시킨다.
        private bool teleportResolvedThisTrapCycle;
        // ResolveTrapTriggersAt 재귀 깊이. 0으로 돌아오는 순간이 "사이클 종료"다.
        private int trapResolutionDepth;
        private const string TrapTeleportSourceRef = "trap.teleport";

        /// <summary>
        /// 마지막 스폰 함정의 요청/실제 배치 기록. 보스 기물 볼리(<see cref="LastBossPropVolleyReport"/>)와
        /// 같은 이유로 둔다 — 자리가 모자라 덜 놓였는지, 저작한 definitionId가 카탈로그에 없었는지를
        /// <b>조용히 넘기지 않기</b> 위한 흔적이다. 표현/디버그 전용이며 규칙은 이 값을 읽지 않는다.
        /// </summary>
        public string LastTrapSpawnReport { get; private set; } = string.Empty;
        private MonsterCatalogBindingEvidence monsterCatalogEvidence;
        private int lastMovedDistance;
        private int actionCardsUsedThisTurn;

        /// <summary>
        /// 전투 누적 행동 카드 사용 수(코인 세탁기, T2 페이즈 C). 턴 리셋되는
        /// <see cref="actionCardsUsedThisTurn"/>과 달리 전투 내내 누적되며 서스펜드를 왕복한다
        /// (`PendingEffects`의 기 차감 예약이 선례). 전투 경계에서는 리셋된다 — 트리거는 전투 스코프다.
        /// </summary>
        private int totalActionCardsUsed;

        /// <summary>방범대 호루라기(T2 페이즈 C) 처치 보상 래치 — 같은 몬스터로 두 번 보상받지 않는다.</summary>
        private readonly HashSet<string> relicKillRewardedMonsterIds = new HashSet<string>(StringComparer.Ordinal);
        private MonsterRuntime markedMonster;
        private int revealedFastTurtleDistance;

        public CombatState(HexMapData map, HexCoord playerCoord, HexCoord enemyCoord, CombatConfig config, HexTerrainTable terrainTable = null, CardCatalogDefinition cardCatalog = null, MonsterCatalogDefinition monsterCatalog = null, HexTerrainTraits terrainTraits = null, PlayerInventoryState playerInventory = null, PlayerDeckData playerDeck = null, CardDeckState movementDeck = null, CardDeckState actionDeck = null, bool drawOpeningHands = true, bool shuffleDecks = false, BossCatalogDefinition bossCatalog = null, int? runSeed = null)
            : this(map, playerCoord, new[] { new MonsterConfig(EnemyUnitId, enemyCoord, config.EnemyMaxHp) }, config, terrainTable, cardCatalog, monsterCatalog, terrainTraits, playerInventory, playerDeck, movementDeck, actionDeck, drawOpeningHands, shuffleDecks, bossCatalog, runSeed)
        {
        }

        public CombatState(HexMapData map, HexCoord playerCoord, IEnumerable<MonsterConfig> monsterConfigs, CombatConfig config, HexTerrainTable terrainTable = null, CardCatalogDefinition cardCatalog = null, MonsterCatalogDefinition monsterCatalog = null, HexTerrainTraits terrainTraits = null, PlayerInventoryState playerInventory = null, PlayerDeckData playerDeck = null, CardDeckState movementDeck = null, CardDeckState actionDeck = null, bool drawOpeningHands = true, bool shuffleDecks = false, BossCatalogDefinition bossCatalog = null, int? runSeed = null)
        {
            // 🔑 런 시드(seed-determinism-handoff). null = 무시드(테스트·랩·구세이브) — 아래 난수원들이
            // 각자 무시드 폴백을 탄다. 값이 있으면 RunSeedStreams 표의 스트림으로 갈라 쓴다.
            RunSeed = runSeed;
            pushRng = runSeed.HasValue
                ? CreateStreamRandom(RunSeedStreams.CombatJudgement)
                : new System.Random();
            // 계획 레이어는 컨텍스트 참조만 들고 있으므로 여기서 가장 먼저 만들어도 안전하다
            // (필드 이니셜라이저에서는 this를 쓸 수 없어 생성자에서 만든다).
            // 은신·실명 마스킹과 프로파일(매복)은 IMonsterPlanningContext의 술어 셋으로 계획기가 직접 읽는다
            // (DEC-2026-09-05-03 — 종전 IMonsterAi 데코레이터 스택을 접었다). 실계획·미리보기가 같은 술어를 지난다.
            planner = new MonsterAiPlanner(
                this,
                attackPatternSeed: runSeed.HasValue ? RunSeedStreams.Derive(runSeed.Value, RunSeedStreams.MonsterAttackPattern) : 0);
            Map = map;
            PlayerCoord = playerCoord;
            Config = config;
            PlayerInventory = (playerInventory ?? new PlayerInventoryState()).Clone();
            this.terrainTable = terrainTable;
            this.terrainTraits = terrainTraits ?? HexTerrainTraits.Default;
            this.monsterCatalog = monsterCatalog ?? CreateMonsterCatalog(config);
            boss = new BossEncounterState(this, bossCatalog ?? BossCatalogDefinition.Empty);
            if (runSeed.HasValue)
            {
                bossPropRng = CreateStreamRandom(RunSeedStreams.BossProps);
                boss.ConfigureBossPropRandom(bossPropRng);
            }
            var resolvedMonsterConfigs = ResolveCatalogBoundMonsterConfigs(map, monsterConfigs, config, this.monsterCatalog);
            EnsureSpawnable(map, playerCoord, "Player spawn");
            Player = new CombatantState(PlayerUnitId, config.PlayerMaxHp);
            // 전투 시작 시점에 이미 보유한 유물의 최대 체력 보너스를 얹는다. 현재 체력도 함께 오르므로
            // (IncreaseMaxHp) 만피로 시작하는 성질은 그대로 유지된다.
            Player.IncreaseMaxHp(GetPermanentItemEffectTotal(PlayerPermanentItemEffectKind.MaxHpBonus));
            FieldObjects = new FieldObjectRegistry();
            PendingFieldObjects = new FieldObjectRegistry();
            InitializeMonsters(resolvedMonsterConfigs, config);
            EnsureBossPhaseTracks();
            monsterCatalogEvidence = CreateMonsterCatalogEvidence(this.monsterCatalog);
            CardCatalog = cardCatalog ?? CreateCardCatalog(config);
            CardCatalogEvidence = CardCatalog.CreateBindingEvidence();
            if (!CardCatalogEvidence.IsValid)
            {
                throw new ArgumentException(CardCatalogEvidence.FailureReason, nameof(cardCatalog));
            }

            PlayerDeck = playerDeck ?? PlayerDeckData.FromCatalog(CardCatalog);
            MovementDeck = movementDeck ?? CreateDeck(CardCategory.Movement, shuffleDecks);
            ActionDeck = actionDeck ?? CreateDeck(CardCategory.Action, shuffleDecks);
            OverallTurnNumber = 1;
            Phase = CombatPhase.PlayerMovement;
            ActionCostRemaining = MaxKi;
            LastFailureReason = string.Empty;
            visibilityRuntime = new HexVisibilityRuntime(map, playerCoord, config.PlayerVisionRange);
            objectiveBinding = CombatObjectiveBindingResolver.Resolve(map, out objectiveTargetCoord);
            ObjectiveCompleted = false;
            LastInvestigateResult = string.Empty;
            if (drawOpeningHands)
            {
                DrawNewTurnHands();
            }
            else
            {
                playerTurnDrawPending = true;
            }
            UpdateOccupancy();
            RefreshMonsterIntentStep();
        }

        /// <summary>
        /// Raised when a combat action resolves into a presentable effect, carrying the affected hex
        /// (<see cref="EffectResultEvent.Center"/>) and area radius. Presentation-only consumers use
        /// this to place VFX on the exact tile/area; raising it never changes combat rules.
        /// </summary>
        public event System.Action<EffectResultEvent> EffectResolved;

        /// <summary>
        /// Raised after <see cref="Phase"/> transitions (previous, next). Presentation-only consumers
        /// (e.g. the turn-phase dock) subscribe to react to phase changes; raising it never changes
        /// combat rules. Use <see cref="SetPhase"/> for all transitions so this fires consistently.
        /// </summary>
        public event System.Action<CombatPhase, CombatPhase> PhaseChanged;

        /// <summary>
        /// Raised when an overall rules turn starts. The initial constructor-created turn is exposed as
        /// <see cref="OverallTurnNumber"/> 1 but does not raise this event because subscriptions are not wired yet.
        /// </summary>
        public event System.Action<int> OverallTurnStarted;

        /// <summary>
        /// Raised after the player's new-turn hands have been drawn. Presentation-only (the draw SFX);
        /// raising it never changes combat rules. Not raised for the constructor-time opening draw,
        /// which happens before any subscription exists.
        /// </summary>
        public event System.Action PlayerHandsDrawn;

        /// <summary>
        /// Raised with the card count when hand cards are spent as a card's additional cost (sacrifice
        /// discard / exile). Presentation-only (the discard SFX). Cards discarded by simply being played
        /// do not raise this — those moments already sound through their card cast cue.
        /// </summary>
        public event System.Action<int> HandCardsDiscarded;

        /// <summary>Raised after monster resolution completes and before the next overall turn starts.</summary>
        public event System.Action<int> OverallTurnEnded;

        /// <summary>
        /// 스폰 함정이 몬스터를 실제로 세웠을 때(함정 id, 세운 수). 발동 자체는 EffectResolved의 함정 이벤트가
        /// 알리고, 이것은 「튀어나왔다」는 표현(SFX trap.spawn)만을 위한 신호다. 0마리면 나가지 않는다.
        /// </summary>
        public event System.Action<string, int> TrapMonstersSpawned;

        // Ordered tile route (start..destination, inclusive) of the most recent player movement, captured so
        // the presentation layer can animate the move one hex at a time instead of a single straight slide.
        private IReadOnlyList<HexCoord> lastPlayerMovePath = System.Array.Empty<HexCoord>();

        // 연출 전용 효과 버퍼: 큐·플러시 정책·그룹 채번이 전부 EffectPresentationBuffer 안에 있다.
        // 버퍼링은 EffectResolved 발사 시점만 미룰 뿐 전투 규칙에 영향을 주지 않는다.
        private readonly EffectPresentationBuffer presentationBuffer = new EffectPresentationBuffer();

        // ⚠️ 이름이 비슷하지만 위의 버퍼와 성격이 다르다 — 이쪽은 연출 전용이 아니다.
        // 연출 비트에 맞춰 플레이어 HP·좌표·페이즈·상태이상을 실제로 되감았다가 다시 적용하고
        // 패배 판정(SetPhase(Defeat))까지 집행한다. 의도된 설계이고
        // MonsterRuntimeTests.DeferredMonsterActionCommitsPlayerDeathOnlyOnFinalAttackImpact가 잠근다.
        private DeferredMonsterActionState deferredMonsterActionState;

        /// <summary>True while effect dispatch is buffered for deferred presentation replay.</summary>
        public bool IsBufferingEffects => presentationBuffer.IsBuffering;

        /// <summary>
        /// True only while a buffered batch is being dispatched (the attack impact flush). Presentation
        /// subscribers read this to apply per-channel impact offsets to attack effects without affecting
        /// immediately-raised effects (status ticks, traps), which never flush.
        /// </summary>
        public bool IsFlushingBufferedEffects => presentationBuffer.IsFlushing;

        /// <summary>
        /// Begin buffering EffectResolved dispatch. Until <see cref="FlushBufferedEffects"/> is called,
        /// effects are queued (in resolution order) rather than raised. Presentation-only.
        /// </summary>
        public void BeginEffectBuffering()
        {
            presentationBuffer.Begin();
        }

        /// <summary>
        /// Stop buffering and dispatch any queued effects in resolution order. Idempotent: a second
        /// call with nothing queued is a no-op, so it is safe to call from both the impact beat and a
        /// cancellation path without double-firing.
        /// </summary>
        public void FlushBufferedEffects()
        {
            presentationBuffer.Flush(EffectResolved);
        }

        /// <summary>
        /// Dispatch only buffered effects that match <paramref name="predicate"/> and keep the
        /// remaining effects queued. Used by presentation sequences that need per-impact replay
        /// for multi-actor turns. Passing null is equivalent to <see cref="FlushBufferedEffects"/>.
        /// </summary>
        public void FlushBufferedEffects(Func<EffectResultEvent, bool> predicate)
        {
            presentationBuffer.Flush(EffectResolved, predicate);
        }

        /// <summary>
        /// Dispatch a single buffered effect by index WITHOUT removing it, so the presentation scheduler can
        /// replay effects one at a time (spaced out) while indices stay stable across the replay. Pair with
        /// <see cref="EndBufferedEffectDispatch"/> to drop the buffer once replay finishes. Out-of-range
        /// indices and a null handler are no-ops.
        /// </summary>
        public void DispatchBufferedEffectAt(int index)
        {
            presentationBuffer.DispatchAt(index, EffectResolved);
        }

        /// <summary>Finish a scheduler-driven replay: drop any remaining buffered effects and stop buffering.</summary>
        public void EndBufferedEffectDispatch()
        {
            presentationBuffer.EndDispatch();
        }

        /// <summary>Read-only view of effects currently queued for deferred presentation (see <see cref="BeginEffectBuffering"/>).</summary>
        public IReadOnlyList<EffectResultEvent> BufferedEffects => presentationBuffer.Queued;

        public bool HasDeferredMonsterActionPlayerDeath => deferredMonsterActionState != null && deferredMonsterActionState.FinalPlayerHp <= 0;

        public void BeginDeferredMonsterActionState()
        {
            deferredMonsterActionState = new DeferredMonsterActionState(
                Player.Hp,
                Player.Block,
                PlayerCoord,
                Phase,
                activeEffects.All);
        }

        public void HoldDeferredMonsterActionStateUntilPresentation()
        {
            var deferred = deferredMonsterActionState;
            if (deferred == null)
            {
                return;
            }

            deferred.CaptureFinal(Player.Hp, Player.Block, PlayerCoord, Phase, activeEffects.All);
            RestoreDeferredMonsterActionSnapshot(deferred.Initial);
        }

        public void CommitDeferredMonsterActionImpact(string presentationGroupId)
        {
            var deferred = deferredMonsterActionState;
            if (deferred == null || string.IsNullOrEmpty(presentationGroupId))
            {
                return;
            }

            if (deferred.TryGetAttackSnapshot(presentationGroupId, out var snapshot))
            {
                RestoreDeferredMonsterActionSnapshot(snapshot);
                if (Player.IsDead)
                {
                    SetPhase(CombatPhase.Defeat);
                }
            }
        }

        public void CommitDeferredMonsterActionTurnStart()
        {
            var deferred = deferredMonsterActionState;
            if (deferred?.Final == null)
            {
                return;
            }

            RestoreDeferredMonsterActionSnapshot(deferred.Final);
        }

        public void FinishDeferredMonsterActionState()
        {
            CommitDeferredMonsterActionTurnStart();
            deferredMonsterActionState = null;
        }

        private void RestoreDeferredMonsterActionSnapshot(DeferredMonsterActionSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            Player.RestoreVitals(snapshot.PlayerHp, snapshot.PlayerBlock);
            PlayerCoord = snapshot.PlayerCoord;
            Phase = snapshot.Phase;
            activeEffects.RestoreFrom(snapshot.ActiveEffects);
            UpdateOccupancy();
        }

        /// <summary>
        /// Applies the persisted run HP to the freshly constructed player. Save/continue boot path
        /// only (call before combat begins); HP is clamped to [1, MaxHp] so a corrupt save can
        /// never resume with a dead player.
        /// </summary>
        public void RestorePlayerRunHp(int hp)
        {
            Player.RestoreVitals(System.Math.Min(System.Math.Max(hp, 1), Player.MaxHp), Player.Block);
        }

        /// <summary>Ordered tile route of the most recent player movement (start..destination). Empty when no move occurred.</summary>
        public IReadOnlyList<HexCoord> LastPlayerMovePath => lastPlayerMovePath;

        public HexMapData Map { get; }
        public HexTerrainTraits TerrainTraits => terrainTraits;
        public CombatConfig Config { get; }
        public CombatantState Player { get; }
        public PlayerInventoryState PlayerInventory { get; private set; }
        public FieldObjectRegistry FieldObjects { get; }
        public FieldObjectRegistry PendingFieldObjects { get; }
        public int OverallTurnNumber { get; private set; }
        public CardCatalogDefinition CardCatalog { get; }
        public PlayerDeckData PlayerDeck { get; private set; }
        public MonsterCatalogDefinition MonsterCatalog => monsterCatalog;
        public MonsterCatalogBindingEvidence MonsterCatalogEvidence => monsterCatalogEvidence;
        public string MonsterCatalogEvidenceText => monsterCatalogEvidence.ToEvidenceText();
        public IReadOnlyList<string> ActiveMonsterDefinitionIds => Monsters.Select(monster => monster.DefinitionId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        public IReadOnlyList<string> ActiveMonsterSpawnRefIds => Monsters.Select(monster => monster.SpawnRefId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        public CardCatalogBindingEvidence CardCatalogEvidence { get; }
        public string CardCatalogEvidenceText => CardCatalogEvidence.ToEvidenceText();
        public IReadOnlyList<string> ActiveCardCatalogIds => MovementDeck.DrawPile
            .Concat(MovementDeck.Hand)
            .Concat(MovementDeck.DiscardPile)
            .Concat(MovementDeck.RemovedPile)
            .Concat(ActionDeck.DrawPile)
            .Concat(ActionDeck.Hand)
            .Concat(ActionDeck.DiscardPile)
            .Concat(ActionDeck.RemovedPile)
            .Select(card => card.Id)
            .ToList();
        public IReadOnlyList<string> ActiveCardInstanceIds => MovementDeck.DrawPile
            .Concat(MovementDeck.Hand)
            .Concat(MovementDeck.DiscardPile)
            .Concat(MovementDeck.RemovedPile)
            .Concat(ActionDeck.DrawPile)
            .Concat(ActionDeck.Hand)
            .Concat(ActionDeck.DiscardPile)
            .Concat(ActionDeck.RemovedPile)
            .Select(card => card.InstanceId)
            .ToList();
        public CardDeckState MovementDeck { get; private set; }
        public CardDeckState ActionDeck { get; private set; }

        /// <summary>이 전투의 런 시드. null이면 무시드로 만들어진 상태(재현 계약 없음).</summary>
        public int? RunSeed { get; }
        private HexCoord playerCoord;

        /// <summary>
        /// 플레이어가 서 있는 칸. <b>세터가 단일 관문</b>이다 — 좌표가 실제로 바뀔 때마다
        /// <see cref="RefreshPositionDerivedEffects"/>가 돌아 「서 있는 자리가 정하는 효과」
        /// (두억시니 지대 · 그슨새 홀림)를 실시간으로 붙이고 뗀다(2026-09-05).
        ///
        /// <para>🔴 여기 말고 다른 곳에서 지대를 재는 방법은 쓰지 않는다: 좌표를 쓰는 자리가
        /// 15곳이 넘어서(이동·넉백·밀침·순간이동·서스펜드 복원·보스 배치·디버그) 호출부마다
        /// 손으로 부르면 반드시 한 곳이 빠진다. 세터에 걸면 「어떻게 옮겨졌든」 같은 답이 나온다.</para>
        /// </summary>
        public HexCoord PlayerCoord
        {
            get => playerCoord;
            private set
            {
                if (playerCoord.Equals(value))
                {
                    return;
                }

                playerCoord = value;
                RefreshPositionDerivedEffects();
            }
        }
        public CombatPhase Phase { get; private set; }
        public CombatCardKind? LastDiscardedCard { get; private set; }

        /// <summary>
        /// Id of the move card behind the most recent player move. Presentation needs it to select the
        /// card's own movement cue, and the move timeline carries only step events (no Effect beat), so the
        /// Push this card raises is never dispatched and cannot carry the id there.
        /// </summary>
        public string LastPlayerMoveCardId { get; private set; } = string.Empty;
        public string LastFailureReason { get; private set; }
        public string LastInvestigateResult { get; private set; }
        public int ActionCostRemaining { get; private set; }
        public int CurrentKi => ActionCostRemaining;
        // 턴당 기 예산의 정본. 유물 MaxKiBonus가 여기서 한 번만 가산되므로, 예산을 리셋하는 지점과
        // MaxKi 코스트를 산정하는 지점(GetRequiredKi)은 Config.ActionBudget/Config.MaxKi가 아니라
        // 반드시 이 프로퍼티를 읽어야 한다. Config.MaxKi는 카탈로그 빌드 시점의 저작 표시값이라
        // 유물을 모른다.
        public int MaxKi => Math.Max(0, Config.ActionBudget + GetPermanentItemEffectTotal(PlayerPermanentItemEffectKind.MaxKiBonus));
        public int RevealedFastTurtleDistance => revealedFastTurtleDistance;
        public HexCoord? MarkedMonsterCoord => markedMonster != null && !markedMonster.Combatant.IsDead ? markedMonster.Coord : (HexCoord?)null;
        public IReadOnlyDictionary<HexCoord, HexCellRuntimeState> RuntimeStates => runtimeStates;
        public IReadOnlyList<MonsterRuntimeState> Monsters => monsters.Select(CreateMonsterSnapshot).ToList();

        /// <summary>
        /// 처치 보상 지급 완료 래치(2026-09-05 #7). 보상 흐름이 목록을 닫을 때 찍는다 — 뷰의 HashSet과
        /// 달리 중단 저장에 실려 이어하기 뒤에도 같은 시체에 보상을 다시 뿌리지 않는다.
        /// </summary>
        public bool MarkMonsterRewardClaimed(string monsterId)
        {
            if (string.IsNullOrEmpty(monsterId))
            {
                return false;
            }

            for (var i = 0; i < monsters.Count; i++)
            {
                if (monsters[i].Id == monsterId)
                {
                    monsters[i].RewardClaimed = true;
                    return true;
                }
            }

            return false;
        }
        public IReadOnlyList<MonsterActionResolutionRecord> LastMonsterActionRecords => lastMonsterActionRecords;
        public IReadOnlyList<ActiveEffect> ActiveEffects => activeEffects.All;

        /// <summary>
        /// 카드 수치 미리보기가 "지금 겨누는 중"으로 취급할 칸. <b>표시 전용 상태다</b> —
        /// 규칙은 이 값을 절대 읽지 않고, 세이브·서스펜드에도 실리지 않는다. 뷰(호버·타겟 선택)가
        /// 설정하고 <see cref="CreateSnapshot"/>만 소비한다.
        /// <para>null이면 대상 무관분(강화·쇠약·유물·지형)만 반영되고, 대상 의존분(허점·표식)은
        /// 빠진다(D-7). 카드 뷰가 <c>state.GetHandCards()</c>를 스스로 당겨 쓰는 구조라 호버 좌표를
        /// 인자로 흘려보낼 자리가 없어서, 상태에 한 칸을 두고 기존 갱신 경로가 그대로 읽게 했다.</para>
        /// </summary>
        public HexCoord? CardPreviewTargetCoord { get; set; }
        public IReadOnlyCollection<string> ConsumedTrapIds => consumedTrapIds;
        public IReadOnlyCollection<string> ClaimedEventObjectIds => claimedEventObjectIds;
        public IReadOnlyDictionary<HexCoord, HexCellVisibility> VisibilityStates => visibilityRuntime.States;

        // Monotonic visibility-mutation counter; lets the view skip a full-map visibility rescan when
        // nothing changed since the last refresh. See AtlasTilePresentationView.ApplyVisibility.
        public int VisibilityVersion => visibilityRuntime.Version;
        public bool HasObjective => objectiveTargetCoord.HasValue;
        public HexCoord? ObjectiveTargetCoord => objectiveTargetCoord;
        public bool HasMemoryStoneObjective => objectiveTargetCoord.HasValue &&
                                               Map.GetObjectsAt(objectiveTargetCoord.Value).Any(IsMemoryStoneObject);
        public bool HasBossMonster => monsters.Any(IsBossMonster);
        public bool HasLivingBossMonster => monsters.Any(monster => IsBossMonster(monster) && !monster.Combatant.IsDead);
        public bool IsMemoryStoneAwakened => HasMemoryStoneObjective &&
                                             (HasBossMonster
                                                 ? !HasLivingBossMonster
                                                 // 보스 기물은 "몬스터를 전부 처치했는가" 판정에서 제외한다:
                                                 // 기믹이 매 턴 새로 뿌리는 기물이 카운트되면 목표가 영원히 각성하지 않는다.
                                                 : monsters.All(monster => monster.Combatant.IsDead || IsPropMonster(monster)));
        public HexObjectiveBinding ObjectiveBinding => objectiveBinding;
        public string ObjectiveBindingEvidenceText => objectiveBinding.IsConfigured
            ? $"Objective {objectiveBinding.ObjectiveId} targets landmark {objectiveBinding.LandmarkId}."
            : "Objective binding unavailable.";
        public bool ObjectiveCompleted { get; private set; }
        public string ObjectiveStatusText => HasMemoryStoneObjective
            ? ObjectiveCompleted
                ? $"Objective complete: {ObjectiveDisplayName} restored."
                : IsMemoryStoneAwakened
                    ? $"Objective ready: move onto and interact with the revealed {ObjectiveDisplayName}."
                    : HasBossMonster
                        ? $"Objective dormant: defeat the boss to awaken the {ObjectiveDisplayName}."
                        : $"Objective dormant: defeat all monsters to awaken the {ObjectiveDisplayName}."
            : ObjectiveCompleted
                ? $"Objective complete: {ObjectiveDisplayName} investigated."
                : IsObjectiveTargetRevealed
                    ? $"Objective incomplete: investigate the revealed {ObjectiveDisplayName}."
                    : HasObjective
                        ? "Objective incomplete: reveal and investigate the MVP landmark."
                        : "Objective unavailable: MVP landmark not configured.";
        public bool IsTerminal => Phase == CombatPhase.Victory || Phase == CombatPhase.Defeat;

        private bool IsObjectiveTargetRevealed => objectiveTargetCoord.HasValue &&
                                                  visibilityRuntime.GetVisibility(objectiveTargetCoord.Value) == HexCellVisibility.Revealed;

        private string ObjectiveDisplayName => string.IsNullOrWhiteSpace(objectiveBinding.DisplayName)
            ? "MVP landmark"
            : objectiveBinding.DisplayName;

        private static bool IsMemoryStoneObject(HexMapObjectData objectData)
        {
            return objectData.IsConfigured &&
                   string.Equals(objectData.ObjectType, "MemoryStone", StringComparison.Ordinal);
        }

        internal static bool IsBossMonster(MonsterRuntime monster)
        {
            return monster != null && MonsterSpawnRoles.IsBoss(monster.SpawnRole);
        }

        /// <summary>
        /// 보스 소속 기물인가. 기물은 HP·피격·사망·세이브를 위해 몬스터로 호스팅되지만 "몬스터"로
        /// 세어지지 않는다 — 제외 지점은 <see cref="MonsterSpawnRoles.BossProp"/> 문서를 참고.
        /// </summary>
        private static bool IsPropMonster(MonsterRuntime monster)
        {
            // 보스 기물 + 플레이어 기물(T4-2 결계 구슬) — 승리 판정·인텐트 예고·Dormant 고정·처치 유물
            // 훅 제외가 전부 이 술어 하나를 지난다.
            return monster != null && MonsterSpawnRoles.IsProp(monster.SpawnRole);
        }

        public static CombatState CreateDefaultDemo()
        {
            return new CombatState(CreateDemoMap(2), new HexCoord(-1, 0), new HexCoord(2, 0), CombatConfig.Default);
        }

        public bool TryAddRewardCardToCurrentHand(string cardId, out string reason)
        {
            return TryAddCardToCurrentHand(cardId, "reward", out reason);
        }

        // 보상과 상점 구매가 공유하는 지급 코어. instance id의 소스 태그만 다르다 — 어느 경로로
        // 들어온 카드인지 디버그/세이브에서 구분할 수 있어야 한다.
        internal bool TryAddCardToCurrentHand(string cardId, string source, out string reason)
        {
            var entry = CardCatalog.Entries.FirstOrDefault(candidate => string.Equals(candidate.Id, cardId, StringComparison.Ordinal));
            if (entry == null)
            {
                reason = "Unknown card reward.";
                return false;
            }

            var instanceId = CreateRuntimeInstanceId(entry.Id, source);
            PlayerDeck = PlayerDeck.AddCard(CardCatalog, cardId, instanceId);
            var card = entry.ToCardDefinition(CardCatalog.SourceId, instanceId);
            if (entry.DeckType == CardCategory.Movement)
            {
                MovementDeck.InjectIntoHand(card);
            }
            else
            {
                ActionDeck.InjectIntoHand(card);
            }

            // 보상·상점으로 들어온 카드는 페이즈 전이를 기다리지 않고 바로 표시한다 — 받자마자
            // 쓰는 것이 정상 동작이라 훑기가 도착하기 전에 손을 떠날 수 있다.
            MarkCodexSighting(CodexDomainIds.Card, entry.Id);
            reason = string.Empty;
            return true;
        }

        public IReadOnlyDictionary<HexCoord, int> GetReachablePlayerMoves()
        {
            return GetReachablePlayerMoves(string.Empty);
        }

        public IReadOnlyDictionary<HexCoord, int> GetReachablePlayerMoves(string cardId)
        {
            var moveCard = string.IsNullOrEmpty(cardId)
                ? MovementDeck.Hand.FirstOrDefault(card => card.EffectType == CardEffectType.Move)
                : MovementDeck.Hand.FirstOrDefault(card => card.EffectType == CardEffectType.Move && MatchesCardKey(card, cardId));
            var range = GetEffectiveMoveRange(moveCard);
            if (moveCard != null && moveCard.EffectRef == CardEffectRefs.MoveFastTurtle)
            {
                var revealed = RevealFastTurtleDistanceForSelection(moveCard.Id);
                range = ResolveMoveRange(revealed);
            }
            else if (moveCard != null && moveCard.AreaRadius > 0)
            {
                range = Math.Max(range, GetEffectiveAreaMoveRange(moveCard));
            }

            var reachable = HexPathfinder.GetReachableCells(Map, new MovementQuery(PlayerCoord, range, includeStart: true, unitId: PlayerUnitId), runtimeStates, terrainTraits)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            AddHiddenMonsterTerminalMoveCells(reachable, range);
            return reachable;
        }

        // IMonsterPlanningContext — 감지 마스킹·프로파일 술어(DEC-2026-09-05-03). 🔴 마스킹 두 겹(은신·실명)은
        // 모든 프로파일에 공통으로 걸린다 — 감지 마스킹은 AI의 성격이 아니라 규칙이기 때문이다.
        // 은신(전역)과 실명(단일)이 겹치면 결과는 같다(둘 다 「안 보인다」).
        bool IMonsterPlanningContext.IsPlayerHiddenFromMonsters => IsPlayerHiddenFromMonsters;

        bool IMonsterPlanningContext.IsMonsterSenseBlindedAt(HexCoord monsterCoord) => IsMonsterSenseBlindedAt(monsterCoord);

        string IMonsterPlanningContext.GetBehaviorProfileRef(MonsterRuntime monster) => ResolveBehaviorProfileRef(monster);

        /// <summary>요괴 §4-6 ① — <c>behaviorProfileRef</c>가 몬스터의 의도 선택 분기(매복)를 고른다. 비면 기본.</summary>
        private string ResolveBehaviorProfileRef(MonsterRuntime monster)
        {
            return TryGetEnemyGrammarEntry(monster, out var entry)
                    && !string.IsNullOrWhiteSpace(entry.BehaviorProfileRef)
                ? entry.BehaviorProfileRef.Trim()
                : MonsterBehaviorProfileRegistry.DefaultProfileRef;
        }

        public IReadOnlyList<MonsterIntentPreview> GetMonsterIntentPreviews(bool includeUnrevealed = false)
        {
            var previews = new List<MonsterIntentPreview>();
            foreach (var monster in monsters.Where(candidate => !candidate.Combatant.IsDead))
            {
                // 보스 기물은 인텐트가 없다(항상 Dormant). 예고를 내보내면 공격 범위 오버레이·텔레그래프
                // 아이콘이 기물마다 뜨면서 실제 위협을 가린다.
                if (IsPropMonster(monster))
                {
                    continue;
                }

                if (!includeUnrevealed && visibilityRuntime.GetVisibility(monster.Coord) != HexCellVisibility.Revealed)
                {
                    continue;
                }

                var plan = monster.TurnPlan;
                var currentIntent = plan.IsActive ? plan.MovementIntent : monster.Intent;
                var predictedMove = plan.IsActive ? plan.PlannedMoveCoord : monster.IntentPredictedMoveCoord;
                var attackPattern = monster.CurrentAttackPattern;

                // 미지(D-4/D-5): 이동 예고와 공격 예고를 <b>둘 다</b> 지운다. 규칙은 건드리지 않는다 —
                // 이 몬스터는 평소처럼 이동하고 공격하며, 화면에만 안 보인다.
                // 이동 예고를 지우는 방법은 예측 좌표를 현재 좌표로 되돌리는 것이다: WillMove가 false가
                // 되어 이동 하이라이트가 사라지고, 공격 범위도 (비우기 전이라면) 제자리 기준이 된다.
                if (IsMonsterIntentHidden(monster))
                {
                    previews.Add(new MonsterIntentPreview(
                        monster.Id,
                        monster.DefinitionId,
                        monster.SpawnRefId,
                        monster.Coord,
                        monster.Coord,
                        System.Array.Empty<HexCoord>(),
                        currentIntent.Type,
                        // 패턴 정보를 통째로 비운다 — id/이름/피해/사거리가 새면 툴팁과 디버그 표면이
                        // 숨긴 것을 그대로 말한다(D-5 완전 은폐).
                        string.Empty,
                        string.Empty,
                        0,
                        0,
                        0,
                        string.Empty,
                        System.Array.Empty<StatusEffectKind>(),
                        0,
                        0,
                        0,
                        isIntentHidden: true,
                        isIntentVeiledByStatus: IsMonsterIntentVeiledByStatus(monster.Id)));
                    continue;
                }

                IReadOnlyList<HexCoord> attackRange;
                if (IsMonsterAttackBlocked(monster))
                {
                    // 기절한 몬스터는 이번 턴 공격이 취소된 상태다. 규칙 쪽은 이미 IsMonsterAttackBlocked로
                    // 공격을 막고 SetMonsterWaitingIntent로 계획까지 비우는데, 예고(preview)만 패턴 사거리를
                    // 그대로 뱉어서 공격 범위 오버레이·상태 아이콘이 계속 떠 있었다. 판정 표면을 늘리지 않도록
                    // 같은 술어를 재사용해 예고 자체를 비운다 — AttackRangeCoords가 비면 오버레이도, 그 좌표에서
                    // 파생되는 텔레그래프 아이콘도 함께 사라진다.
                    attackRange = System.Array.Empty<HexCoord>();
                }
                else if (attackPattern.IsSelfTargeted)
                {
                    // 자기부여 패턴(§21.8 제안 8)은 위험 칸이 없다 — 사거리 원판을 그대로 뱉으면
                    // "서 있으면 맞는 칸"이라는 붉은 예고 어휘가 0피해 버프에 거짓말을 한다.
                    attackRange = System.Array.Empty<HexCoord>();
                }
                else if (!string.IsNullOrEmpty(attackPattern.ShapeId))
                {
                    var lockedPlayerCoord = plan.IsActive ? plan.AttackFacingIntent.PlayerCoord : monster.LockedFacingIntent.PlayerCoord;
                    var attackDir = predictedMove.ApproximateDirection(lockedPlayerCoord);
                    attackRange = AttackShapeLibrary.GetAffectedCells(
                        attackPattern.ShapeId, predictedMove, attackDir, GetMonsterBody(monster)).ToList();
                }
                else
                {
                    // 비-shape 사거리는 몸통 가장자리 기준(§13.4 C-3) — 원판·형상 공통 유도.
                    attackRange = GetAttackRangeCoords(predictedMove, attackPattern.Range, GetMonsterBody(monster));
                }
                previews.Add(new MonsterIntentPreview(
                    monster.Id,
                    monster.DefinitionId,
                    monster.SpawnRefId,
                    monster.Coord,
                    predictedMove,
                    attackRange,
                    currentIntent.Type,
                    attackPattern.Id,
                    attackPattern.DisplayName,
                    // 패턴의 저작 피해가 아니라 <b>실제로 들어올 피해</b>다(R-8): 지형 방어·강화·쇠약·허점·
                    // 유물 보정이 전부 반영된다. 집행 경로와 같은 함수를 부르므로 둘이 갈라질 수 없다.
                    ResolveMonsterAttackDamageToPlayer(monster, attackPattern),
                    attackPattern.Range,
                    attackPattern.AreaRadius,
                    attackPattern.EffectRef,
                    attackPattern.StatusEffects,
                    attackPattern.StatusEffectDurationTurns,
                    attackPattern.StatusEffectAmount,
                    attackPattern.KnockbackDistance,
                    attackPatternShieldGain: attackPattern.ShieldGain,
                    // 자기부여(2026-08-20 #16): 커버 판정을 건너뛰는 패턴이라 이동 FSM 의도(IntentType)는
                    // Chase/Search로 남는다 — "이번 턴 자기 버프"는 이 플래그가 유일한 신호다. 기절은
                    // 공격 자체가 취소된 상태라 위의 IsMonsterAttackBlocked 분기와 같은 술어로 거른다.
                    isSelfBuffIntent: attackPattern.IsSelfTargeted &&
                        !IsMonsterAttackBlocked(monster) &&
                        monster.ActivityState != MonsterActivityState.Dormant,
                    // 소환 예고(요괴 §4-4): 집행과 <b>같은 함수</b>가 자리를 계산한다 — 예고 칸과 실제
                    // 스폰 칸이 갈라지면 「예고=명중」의 소환판이 깨진다. 기절로 공격이 취소된 턴에는
                    // 아무도 나오지 않으므로 예고도 비운다(위 분기와 같은 술어).
                    summonCoords: attackPattern.HasSummon && !IsMonsterAttackBlocked(monster)
                        ? GetSummonSeatCoords(monster, attackPattern)
                        : System.Array.Empty<HexCoord>(),
                    // 🔴 「이번 턴 나를 때린다」는 IntentType으로 알 수 없다 — 그건 <b>이동</b> 의도라
                    // 걸어와서 때리는 놈이 Chase로 남는다(2026-09-01 #1). 규칙층이 이미 확정해 둔
                    // 답(PendingAttackIntent = 이동 종료 시점에 공격 범위가 플레이어를 덮었는가)을
                    // 그대로 싣는다. 기절로 공격이 취소된 턴은 위 분기와 같은 술어로 거른다.
                    willAttackPlayer: monster.PendingAttackIntent && !IsMonsterAttackBlocked(monster),
                    // 공격 뒤 전진(2026-09-01 #19). 집행과 <b>같은 술어</b>가 계산한다 — 예고 칸과 실제
                    // 도착 칸이 갈라지면 「예고=명중」의 전진판이 깨진다. 기점은 계획된 이동 도착지다.
                    // 공격이 취소된 턴에는 전진도 없으므로 같은 술어로 거른다.
                    advanceAfterAttackCoord: IsMonsterAttackBlocked(monster)
                        ? null
                        : PredictAdvanceAfterAttack(monster, predictedMove),
                    // 저주 부여 예고(2026-09-01 W2). 고정 한 장(injectStatusCardId)이든 풀 추첨
                    // (injectStatusCardPool)이든 화면이 말하는 것은 같다 — "이 칸에 서면 덱이 더러워진다".
                    // 어느 카드인지는 명중 시점에 뽑히므로 예고가 앞질러 말하지 않는다.
                    // 기절로 공격이 취소된 턴은 위 분기와 같은 술어로 거른다.
                    injectsCurse: (!string.IsNullOrEmpty(attackPattern.InjectStatusCardId) ||
                            attackPattern.HasInjectStatusCardPool) &&
                        !IsMonsterAttackBlocked(monster),
                    // 기믹 턴 예고(2026-09-03 ⑥): 기믹이 공격을 대체하는 턴에는 위의 공격 예고 축이
                    // 전부 비므로(IsMonsterAttackBlocked) 이 축이 유일한 "이번 턴 무엇을 하는가"다.
                    // 공격 차단과 같은 순수 질의라 배지=행동이 갈라질 수 없다.
                    bossGimmickIds: GetBossActionReplacingMechanicIds(monster)));
            }

            return previews;
        }

        /// <summary>
        /// Cheap (O(monsters)) signature covering every input that monster intent / chase overlay
        /// highlight cells depend on: each considered monster's id, coord, predicted move, attack
        /// pattern (id/shape/range), locked-facing player coord and visibility, plus the chase range
        /// and the reveal-all flag. Overlay queries cache their (expensive, map-wide) results keyed on
        /// this value and recompute only when it changes. Read-only ??no mutation-point instrumentation.
        /// </summary>
        public long ComputeMonsterIntentOverlaySignature(bool includeUnrevealed)
        {
            unchecked
            {
                long hash = 17;
                hash = (hash * 31) + Config.EnemyChaseRange;
                hash = (hash * 31) + (includeUnrevealed ? 1 : 0);
                foreach (var monster in monsters)
                {
                    if (monster.Combatant.IsDead)
                    {
                        continue;
                    }

                    // Include non-visible monsters too so a visibility change flips the signature even
                    // when includeUnrevealed is false (they affect which monsters the previews keep).
                    var visible = includeUnrevealed ||
                        visibilityRuntime.GetVisibility(monster.Coord) == HexCellVisibility.Revealed;
                    var pattern = monster.CurrentAttackPattern;
                    hash = (hash * 31) + (monster.Id != null ? monster.Id.GetHashCode() : 0);
                    hash = (hash * 31) + monster.Coord.GetHashCode();
                    var plan = monster.TurnPlan;
                    hash = (hash * 31) + (plan.IsActive ? plan.PlannedMoveCoord : monster.IntentPredictedMoveCoord).GetHashCode();
                    hash = (hash * 31) + (pattern.Id != null ? pattern.Id.GetHashCode() : 0);
                    hash = (hash * 31) + (pattern.ShapeId != null ? pattern.ShapeId.GetHashCode() : 0);
                    hash = (hash * 31) + pattern.Range;
                    hash = (hash * 31) + (plan.IsActive ? plan.AttackFacingIntent.PlayerCoord : monster.LockedFacingIntent.PlayerCoord).GetHashCode();
                    hash = (hash * 31) + (visible ? 1 : 0);
                    // 기절 여부는 예고의 공격 범위를 통째로 비우므로 서명에 반드시 들어가야 한다. 빠지면 제자리에서
                    // 기절만 걸린 몬스터는 좌표·패턴·조준이 모두 그대로라 서명이 안 변하고, 오버레이 캐시가
                    // 갱신되지 않아 공격 범위가 화면에 그대로 남는다.
                    hash = (hash * 31) + (IsMonsterAttackBlocked(monster) ? 1 : 0);
                    // 미지도 같은 이유로 반드시 들어간다. 미지는 이동 예고까지 지우므로 빠뜨리면
                    // 걸어도 예고가 그대로 떠 있고 풀려도 안 돌아온다 — 오버레이가 이 값이 바뀔 때만
                    // 다시 계산하기 때문이다.
                    hash = (hash * 31) + (IsMonsterIntentHidden(monster) ? 1 : 0);
                }

                return hash;
            }
        }

        /// <summary>
        /// The first living monster (spawn order), or null when every monster is dead. Replaces the
        /// former single-enemy <c>Enemy</c>/<c>EnemyCoord</c>/<c>EnemyIntent</c> aliases.
        /// </summary>
        public MonsterRuntimeState? RepresentativeLivingMonster
        {
            get
            {
                var monster = GetRepresentativeLivingMonster();
                return monster != null ? (MonsterRuntimeState?)CreateMonsterSnapshot(monster) : null;
            }
        }

        public MonsterRuntimeState? GetMonsterAtCoord(HexCoord coord)
        {
            // 멤버십은 원판 기준이다(FindLivingMonsterAt와 같은 계약) — 보스 몸통 어느 칸을 가리켜도
            // 그 보스가 잡혀야 툴팁·호버가 히트테스트와 같은 것을 가리킨다.
            var monster = monsters.FirstOrDefault(m => !m.Combatant.IsDead && IsMonsterOccupying(m, coord));
            return monster != null ? (MonsterRuntimeState?)CreateMonsterSnapshot(monster) : null;
        }

        public bool TryGetMonsterCatalogEntry(string definitionId, out MonsterCatalogEntry entry)
        {
            entry = default;
            return monsterCatalog != null && monsterCatalog.TryGetEntry(definitionId, out entry);
        }

        public HexCellVisibility GetVisibility(HexCoord coord)
        {
            return visibilityRuntime.GetVisibility(coord);
        }

        public HexVisibilitySafeCellInfo GetVisibilitySafeCellInfo(HexCoord coord)
        {
            return visibilityRuntime.GetSafeCellInfo(coord);
        }

        public void RevealForTests(HexCoord coord)
        {
            RevealVictoryPathCell(coord);
        }

        public void RevealVictoryPathCell(HexCoord coord)
        {
            visibilityRuntime.Reveal(coord);
        }

        public bool CanTargetForInvestigation(HexCoord target, out string reason)
        {
            reason = string.Empty;
            if (!Map.Contains(target))
            {
                reason = "Investigation target is not on the map.";
                return false;
            }

            var visibility = visibilityRuntime.GetVisibility(target);
            if (visibility == HexCellVisibility.Unknown)
            {
                reason = "Cannot investigate an unknown hex.";
                return false;
            }

            if (visibility == HexCellVisibility.Hinted)
            {
                reason = "Hinted hexes must be revealed before investigation.";
                return false;
            }

            return true;
        }

        public CardTargetValidationResult ValidateScoutTarget(HexCoord target)
        {
            return ValidateScoutTarget(target, string.Empty);
        }

        public CardTargetValidationResult ValidateScoutTarget(HexCoord target, string cardId)
        {
            var card = FindActionCard(CardEffectType.Scout, cardId);
            if (!CanUseActionCard(card, out var reason))
            {
                return CardTargetValidationResult.Failure(reason);
            }

            if (!Map.TryGetCell(target, out var cell))
            {
                return CardTargetValidationResult.Failure("Scout target is not on the map.");
            }

            if (!cell.BaseWalkable || Map.HasMovementBlockingObject(target))
            {
                return CardTargetValidationResult.Failure("Scout target must be an unblocked walkable map cell.");
            }

            if (card.Range > 0 && PlayerCoord.DistanceTo(target) > card.Range)
            {
                return CardTargetValidationResult.Failure("Scout target is out of range.");
            }

            return CardTargetValidationResult.Success();
        }

        public CardTargetValidationResult ValidateInvestigateCard()
        {
            var card = FindActionCard(CardEffectType.Investigate);
            return CanUseActionCard(card, out var reason)
                ? CardTargetValidationResult.Success()
                : CardTargetValidationResult.Failure(reason);
        }

        public CardTargetValidationResult ValidateInvestigateTarget(HexCoord target)
        {
            var card = FindActionCard(CardEffectType.Investigate);
            if (!CanUseActionCard(card, out var reason))
            {
                return CardTargetValidationResult.Failure(reason);
            }

            if (!Map.Contains(target))
            {
                return CardTargetValidationResult.Failure("Investigation target is not on the map.");
            }

            var result = CombatObjectiveCompletionPolicy.Resolve(
                ObjectiveCompleted,
                objectiveTargetCoord,
                PlayerCoord,
                target,
                visibilityRuntime.GetVisibility(target),
                card.Range);
            return result.CompletedNow
                ? CardTargetValidationResult.Success()
                : CardTargetValidationResult.Failure(FormatObjectiveFailureReason(result.Outcome));
        }

        public bool CanInteractMemoryStoneAtPlayer(out string reason)
        {
            reason = string.Empty;
            if (IsTerminal)
            {
                reason = "Combat ended. Restart to play again.";
                return false;
            }

            if (!HasMemoryStoneObjective || !objectiveTargetCoord.HasValue)
            {
                reason = "No MemoryStone objective is configured.";
                return false;
            }

            if (ObjectiveCompleted)
            {
                reason = "MemoryStone objective is already complete.";
                return false;
            }

            if (PlayerCoord != objectiveTargetCoord.Value)
            {
                reason = "Move onto the MemoryStone tile to interact.";
                return false;
            }

            if (visibilityRuntime.GetVisibility(objectiveTargetCoord.Value) != HexCellVisibility.Revealed)
            {
                reason = "MemoryStone must be revealed before interaction.";
                return false;
            }

            if (!IsMemoryStoneAwakened)
            {
                reason = HasBossMonster
                    ? "MemoryStone is dormant. Defeat the boss first."
                    : "MemoryStone is dormant. Defeat all monsters first.";
                return false;
            }

            return true;
        }

        public bool TryInteractMemoryStoneAtPlayer(out string reason)
        {
            if (!CanInteractMemoryStoneAtPlayer(out reason))
            {
                return Fail(reason);
            }

            ObjectiveCompleted = true;
            LastInvestigateResult = $"Objective complete: awakened the {ObjectiveDisplayName}.";
            SetPhase(CombatPhase.Victory);
            reason = LastInvestigateResult;
            return true;
        }

        public CardTargetValidationResult ValidateAttackTarget(HexCoord target)
        {
            return ValidateAttackTarget(target, string.Empty);
        }

        public CardTargetValidationResult ValidateAttackTarget(HexCoord target, string cardId)
        {
            var card = FindAttackCardForTarget(target, cardId);
            if (!CanUseActionCard(card, out var reason))
            {
                return CardTargetValidationResult.Failure(reason);
            }

            if (card != null && card.TargetMode == CardTargetMode.SelfArea)
            {
                // Self-centred AoE ignores the clicked tile; it is valid when at least one living monster
                // sits within AreaRadius of the player (the blast is always centred on the player).
                // 거리는 중심이 아니라 <b>가장 가까운 점유 칸</b> 기준이다 — 보스 몸통이 폭발 반경 안에
                // 들어와 있는데 중심이 밖이라는 이유로 "사거리에 몬스터가 없다"고 거절하면, 해소부는
                // 맞히는데 검증부만 막는 어긋남이 된다.
                if (!monsters.Any(candidate => IsVisibleLivingMonster(candidate) && GetDistanceToMonster(candidate, PlayerCoord) <= card.AreaRadius))
                {
                    return CardTargetValidationResult.Failure("No monster is within range of the area attack.");
                }

                return CardTargetValidationResult.Success();
            }

            var monster = FindLivingMonsterAt(target);
            if (monster == null)
            {
                return CardTargetValidationResult.Failure("Attack target must contain a living monster.");
            }

            if (!IsVisibleLivingMonster(monster))
            {
                return CardTargetValidationResult.Failure("Attack target must be visible.");
            }

            // Effective reach = Range + AreaRadius so self-centred AoE cards (e.g. Sweep: range 0,
            // blast-1) can target adjacent enemies the blast would hit. Single-target/shape cards have
            // AreaRadius 0, so their reach is unchanged.
            if (PlayerCoord.DistanceTo(target) > card.Range + card.AreaRadius)
            {
                return CardTargetValidationResult.Failure("Attack target is out of card range.");
            }

            return CardTargetValidationResult.Success();
        }

        public IReadOnlyList<CombatCardSnapshot> GetCombatCards()
        {
            var snapshots = new List<CombatCardSnapshot>();
            snapshots.AddRange(GetHandCards());
            snapshots.AddRange(MovementDeck.DiscardPile.Select(card => CreateDiscardSnapshot(card, "Movement discard")));
            snapshots.AddRange(ActionDeck.DiscardPile.Select(card => CreateDiscardSnapshot(card, "Action discard")));
            return snapshots;
        }

        public IReadOnlyList<CombatCardSnapshot> GetHandCards()
        {
            var snapshots = new List<CombatCardSnapshot>();
            snapshots.AddRange(MovementDeck.Hand.Select(card => CreateSnapshot(card, "Movement hand")));
            snapshots.AddRange(ActionDeck.Hand.Select(card => CreateSnapshot(card, "Action hand")));
            return snapshots;
        }

        public IReadOnlyList<CombatCardSnapshot> GetDeckListCards()
        {
            var snapshots = new List<CombatCardSnapshot>();
            AppendDeckListSnapshots(snapshots, MovementDeck.Hand, "Movement hand", false);
            AppendDeckListSnapshots(snapshots, MovementDeck.DrawPile, "Movement draw", false);
            AppendDeckListSnapshots(snapshots, MovementDeck.DiscardPile, "Movement discard", true);
            AppendDeckListSnapshots(snapshots, ActionDeck.Hand, "Action hand", false);
            AppendDeckListSnapshots(snapshots, ActionDeck.DrawPile, "Action draw", false);
            AppendDeckListSnapshots(snapshots, ActionDeck.DiscardPile, "Action discard", true);
            return snapshots;
        }

        public IReadOnlyList<CombatCardSnapshot> GetDrawPileCards()
        {
            var snapshots = new List<CombatCardSnapshot>();
            AppendDeckListSnapshots(snapshots, MovementDeck.DrawPile, "Movement draw", false);
            AppendDeckListSnapshots(snapshots, ActionDeck.DrawPile, "Action draw", false);
            return snapshots;
        }

        public IReadOnlyList<CombatCardSnapshot> GetDiscardPileCards()
        {
            var snapshots = new List<CombatCardSnapshot>();
            AppendDeckListSnapshots(snapshots, MovementDeck.DiscardPile, "Movement discard", true);
            AppendDeckListSnapshots(snapshots, ActionDeck.DiscardPile, "Action discard", true);
            return snapshots;
        }

        public IReadOnlyList<CombatCardSnapshot> GetExilePileCards()
        {
            var snapshots = new List<CombatCardSnapshot>();
            AppendDeckListSnapshots(snapshots, MovementDeck.RemovedPile, "Movement removed", true);
            AppendDeckListSnapshots(snapshots, ActionDeck.RemovedPile, "Action removed", true);
            return snapshots;
        }

        private void AppendDeckListSnapshots(
            ICollection<CombatCardSnapshot> snapshots,
            IEnumerable<CardDefinition> cards,
            string pile,
            bool discarded)
        {
            if (snapshots == null || cards == null)
            {
                return;
            }

            foreach (var card in cards)
            {
                if (card != null)
                {
                    snapshots.Add(CreateDeckListSnapshot(card, pile, discarded));
                }
            }
        }

        public bool TryPlayerMove(HexCoord destination)
        {
            return TryPlayerMove(destination, string.Empty);
        }

        public bool TryPlayerMove(HexCoord destination, string cardId)
        {
            LastFailureReason = string.Empty;
            lastPlayerMovePath = System.Array.Empty<HexCoord>();
            if (IsTerminal)
            {
                return Fail("Combat ended. Restart to play again.");
            }

            if (Phase != CombatPhase.PlayerMovement)
            {
                return Fail("Movement cards can only be used during player movement.");
            }

            if (IsMomentumMovementLocked())
            {
                return Fail("\uCD94\uC9C4 \uD6A8\uACFC\uB85C \uC0AC\uC6A9 \uBD88\uAC00");
            }

            // Stun and downed states explicitly block movement; action cards are handled separately by CanUseActionCard.
            if (IsPlayerMovementBlocked())
            {
                return Fail(HasActivePlayerEffect(StatusEffectKind.Stun)
                    ? "기절 상태라 이동할 수 없습니다."
                    : "속박 상태라 이동할 수 없습니다.");
            }

            var moveCard = string.IsNullOrEmpty(cardId)
                ? MovementDeck.Hand.FirstOrDefault(card => card.EffectType == CardEffectType.Move)
                : MovementDeck.Hand.FirstOrDefault(card => card.EffectType == CardEffectType.Move && MatchesCardKey(card, cardId));
            if (moveCard == null)
            {
                return Fail("No movement card is available.");
            }

            // 봉인(C-16)은 이동 카드도 잠근다 — 라벨(GetCardStatus)과 같은 술어를 실행 경로도 지난다.
            var moveRestriction = GetCardRestriction(moveCard);
            if (moveRestriction != CardRestriction.None)
            {
                return Fail(RestrictionFailureText(moveRestriction, isMoveCard: true));
            }

            if (moveCard.Cost > ActionCostRemaining)
            {
                return Fail("Not enough Ki remains.");
            }

            var effectiveRange = GetEffectiveMoveRange(moveCard);
            if (moveCard.EffectRef == CardEffectRefs.MoveFastTurtle)
            {
                RevealFastTurtleDistanceForSelection(moveCard.Id);
                effectiveRange = ResolveMoveRange(revealedFastTurtleDistance);
            }
            // Random-radius travel cards (e.g. M06) declare their base reach via AreaRadius while Range stays 0
            // (no tile-targeting step). Apply the same movement modifiers as normal movement so Slow/Agility
            // affect the random destination radius instead of being bypassed by the authored AreaRadius.
            if (moveCard.AreaRadius > 0)
            {
                effectiveRange = Math.Max(effectiveRange, GetEffectiveAreaMoveRange(moveCard));
            }
            if (!CardBehaviorRegistry.Resolve(moveCard).TryResolveMoveDestination(this, moveCard, effectiveRange, destination, out destination, out var moveFailureReason))
            {
                return Fail(moveFailureReason);
            }

            // A monster sitting on a tile the player cannot yet see (Hinted/Unknown) must not block path
            // selection, otherwise "movement blocked" leaks the monster's presence through fog. The tile is
            // allowed only as the terminal destination (its occupancy is lifted just for this query); every
            // other occupied tile still blocks, so the player can never transit through an unseen monster.
            var moveOccupancy = IsHiddenMonsterCell(destination)
                ? BuildPlayerMoveOccupancyExcluding(destination)
                : runtimeStates;
            var path = HexPathfinder.FindPath(Map, new MovementQuery(PlayerCoord, effectiveRange, unitId: PlayerUnitId), destination, moveOccupancy, terrainTraits);
            if (path.Count == 0)
            {
                return Fail("Cannot move there: hex is blocked, occupied, or out of move range.");
            }

            // Captured for presentation: the ordered route the player walks this move, so the scheduler
            // can step one tile at a time. Knockback-on-collision is presented separately from this route.
            lastPlayerMovePath = path;

            var previousCoord = PlayerCoord;
            // Captured before the player advances: a non-null monster here can only be one hidden by fog
            // (a revealed monster would have blocked the path above), so the player collides with it and is
            // knocked back one tile along the route after contact reveals it.
            var collidedMonster = FindLivingMonsterAt(destination);
            PlayerCoord = destination;

            // 보스 아레나 조우: 경로상 첫 아레나 셀에서 멈추고 결계가 닫힌다. 트랩 결의보다 먼저 도는 이유는
            // 강제 정지 뒤에는 목적지 칸을 밟은 적이 없기 때문 — 거기 트랩이 터지면 안 된다.
            var sealedArenaThisMove = TryResolveBossArenaEntry(path, out var arenaStopCoord);
            if (sealedArenaThisMove)
            {
                // destination = 강제 정지 좌표(경계 칸). 걷기 연출 끝점·Push 거리·트랩/이동 거리의 기준으로
                // 남는다. 전투 시작 좌표(중앙 전방)는 이것과 분리해 아래에서 PlayerCoord로 확정한다.
                destination = arenaStopCoord;
                Boss.SealedArenaEntryCoord = arenaStopCoord;
                // 목적지까지 가지 않았으므로 목적지에서 잡아 둔 충돌(넉백)도 성립하지 않는다.
                collidedMonster = null;
                // 연출이 실제로 걸은 만큼만 재생하도록 경로도 정지 지점에서 자른다.
                var walkedLength = path.ToList().IndexOf(arenaStopCoord) + 1;
                path = path.Take(walkedLength).ToList();
                lastPlayerMovePath = path;
                // 봉인이 성립한 순간 아레나 전체를 영구 공개한다. 아래 RefreshPlayerVision보다 먼저 걸어도
                // 안전하다 — RevealPermanently는 임시 공개 사이드카에서 셀을 떼어 내므로 갱신에 강등되지 않는다.
                RevealSealedBossArena();
                // §8-9 P5 개정: 전투 시작 좌표(중앙 전방)를 규칙상 최종 위치로 확정한다 — 강제 정지 좌표와
                // 분리. 걷기 연출은 경계 칸까지만 재생하고, 마커를 이 좌표로 스냅하는 것은 조우 연출이 암전으로
                // 화면을 덮는 동안 한다(RunMoveTimeline이 봉인 시 경계→시작 좌표 넉백-슬라이드를 억제).
                // 규칙상 위치를 여기서 확정하므로 스킵·헤드리스에서도 플레이어는 중앙 전방에 선다.
                PlayerCoord = ResolveBossArenaBattleStart(arenaStopCoord);
                // 조우 턴의 첫 살포를 예고한다(2026-09-05 후속 #1). 플레이어 시작 좌표가 확정된 <b>뒤</b>여야
                // 예고 칸이 그 자리를 피한다(예고=배치 계약).
                NotifyBossArenaSealed();
            }

            // 봉인 시 걸은 거리·트랩은 실제로 밟은 칸(경계=destination) 기준이다 — 전투 시작 좌표로 순간이동한
            // 것은 밟은 적이 없다. 비봉인 이동에서는 destination == PlayerCoord이라 동작이 같다.
            lastMovedDistance += previousCoord.DistanceTo(destination);
            ResolveTrapTriggersAt(destination);
            if (CheckTerminalOutcomeStep())
            {
                return true;
            }

            RefreshPlayerVision();
            // SpendCardKi로 수렴(T5-3): 일반 카드는 저작 cost 그대로, SpendAll(전력 질주)은 잔량 전부.
            SpendCardKi(moveCard);
            LastPlayerMoveCardId = moveCard.Id;
            CardBehaviorRegistry.Resolve(moveCard).ApplyAfterMoveResolved(this, moveCard);
            RaiseEffect(
                EffectKind.Push,
                previousCoord,
                0,
                previousCoord.DistanceTo(destination),
                PlayerUnitId,
                moveCard.Id,
                sourceUnitId: PlayerUnitId,
                sourceActorKind: "player",
                targetActorKind: "player",
                sourceCardId: moveCard.Id,
                presentationGroupId: CreatePresentationGroupId(PlayerUnitId, moveCard.Id));

            if (collidedMonster != null && path.Count >= 2)
            {
                KnockbackPlayerToPreviousTile(destination, path[path.Count - 2], collidedMonster.Id);
            }

            ConsumePlayedCard(MovementDeck, moveCard);
            LastDiscardedCard = CombatCardKind.Move;
            UpdateOccupancy();
            // 결계는 위 UpdateOccupancy에서 실제로 닫힌다. 이벤트는 그 뒤에 발화해야 구독자가 보는 상태가
            // 이미 봉인이 끝난 상태다(BossPhaseChanged와 같은 규약).
            if (sealedArenaThisMove)
            {
                BossArenaSealed?.Invoke(Boss.SealedArenaIdRaw);
            }

            CommitMonsterAttackIntentsAfterPlayerMovementEnd();

            return true;
        }


        public int RevealFastTurtleDistanceForSelection(string cardId = "")
        {
            var card = string.IsNullOrEmpty(cardId)
                ? MovementDeck.Hand.FirstOrDefault(candidate => candidate.EffectRef == CardEffectRefs.MoveFastTurtle)
                : MovementDeck.Hand.FirstOrDefault(candidate => MatchesCardKey(candidate, cardId) && candidate.EffectRef == CardEffectRefs.MoveFastTurtle);
            if (card == null)
            {
                return 0;
            }

            if (revealedFastTurtleDistance == 0)
            {
                revealedFastTurtleDistance = pushRng.Next(2) == 0 ? 1 : 6;
            }

            return revealedFastTurtleDistance;
        }


        public bool TryPlayerMovementSelf(string cardId)
        {
            LastFailureReason = string.Empty;
            if (IsTerminal)
            {
                return Fail("Combat ended. Restart to play again.");
            }

            if (Phase != CombatPhase.PlayerMovement)
            {
                return Fail("Movement cards can only be used during player movement.");
            }

            if (IsMomentumMovementLocked())
            {
                return Fail("\uCD94\uC9C4 \uD6A8\uACFC\uB85C \uC0AC\uC6A9 \uBD88\uAC00");
            }

            // Mirrors TryPlayerMove: the UI usable flag (IsCardUsable) greys every Move card - self-mode
            // included - under stun/immobilize via IsPlayerMovementBlocked, so execution must reject them too.
            if (IsPlayerMovementBlocked())
            {
                return Fail(HasActivePlayerEffect(StatusEffectKind.Stun)
                    ? "\uAE30\uC808 \uC0C1\uD0DC\uB77C \uC774\uB3D9\uD560 \uC218 \uC5C6\uC2B5\uB2C8\uB2E4."
                    : "\uC18D\uBC15 \uC0C1\uD0DC\uB77C \uC774\uB3D9\uD560 \uC218 \uC5C6\uC2B5\uB2C8\uB2E4.");
            }

            var moveCard = MovementDeck.Hand.FirstOrDefault(card => card.EffectType == CardEffectType.Move && MatchesCardKey(card, cardId));
            if (moveCard == null)
            {
                return Fail("No movement card is available.");
            }

            if (moveCard.PlayMode != CardPlayMode.Self)
            {
                return Fail("Movement card requires a map target.");
            }

            var selfMoveRestriction = GetCardRestriction(moveCard);
            if (selfMoveRestriction != CardRestriction.None)
            {
                return Fail(RestrictionFailureText(selfMoveRestriction, isMoveCard: true));
            }

            if (GetRequiredKi(moveCard) > ActionCostRemaining)
            {
                return Fail("Not enough Ki remains.");
            }

            SpendCardKi(moveCard);
            LastPlayerMoveCardId = moveCard.Id;
            CardBehaviorRegistry.Resolve(moveCard).ApplyAfterMoveResolved(this, moveCard);
            RaiseEffect(
                EffectKind.Push,
                PlayerCoord,
                0,
                1,
                PlayerUnitId,
                moveCard.Id,
                sourceUnitId: PlayerUnitId,
                sourceActorKind: "player",
                targetActorKind: "player",
                sourceCardId: moveCard.Id,
                presentationGroupId: CreatePresentationGroupId(PlayerUnitId, moveCard.Id));
            ConsumePlayedCard(MovementDeck, moveCard);
            LastDiscardedCard = CombatCardKind.Move;
            LastFailureReason = string.Empty;
            LastInvestigateResult = string.Empty;
            return true;
        }

        private void ResolveMonsterMovementStep(IReadOnlyDictionary<string, MonsterActionResolutionBuilder> actionRecords = null)
        {
            foreach (var monster in GetMonsterActionOrder())
            {
                if (monster.ActivityState == MonsterActivityState.Dormant)
                {
                    SetMonsterPlannedMove(monster, monster.Coord, canAttack: false);
                    continue;
                }

                // 도약(§17)은 걷기 게이트보다 <b>앞에서</b> 해소한다. 뒤에 두면 계획은 서지만
                // 이동 차단·이동력 게이트가 보스를 제자리로 되돌려, 예고만 뜨고 아무 일도 일어나지 않는다.
                // 경로를 걷지 않으므로 이동력도 경로탐색도 쓰지 않는다 — 착지 지점만 다시 검증한다
                // (계획과 해소 사이에 플레이어가 움직였을 수 있다).
                if (monster.PlannedLeap)
                {
                    var landing = monster.TurnPlan.IsActive ? monster.TurnPlan.PlannedMoveCoord : monster.Coord;
                    if (landing != monster.Coord
                        && !IsMonsterMovementRooted(monster)
                        && !IsMonsterAttackBlocked(monster)
                        && planner.IsLeapLandingStillClear(monster, landing))
                    {
                        var leapFrom = monster.Coord;
                        monster.Coord = landing;
                        SetMonsterPlannedMove(monster, landing, canAttack: true);
                        // 점유는 유도값이 아니라 캐시다 — 여기서 갱신하지 않으면 오버레이·히트테스트는
                        // 멀쩡한데 이동 판정만 낡은 자리를 가리킨다.
                        UpdateOccupancy();
                        if (actionRecords != null && actionRecords.TryGetValue(monster.Id, out var leapRecord))
                        {
                            leapRecord.MovePath = new[] { leapFrom, landing };
                            leapRecord.LeapedMove = true;
                        }

                        continue;
                    }

                    // 착지가 막혔다: 뛰지 못했을 뿐 공격은 제자리에서 그대로 휘두른다(기절이 아닌 한).
                    monster.PlannedLeap = false;
                    SetMonsterPlannedMove(monster, monster.Coord, canAttack: !IsMonsterAttackBlocked(monster));
                    continue;
                }

                if (IsMonsterMovementBlocked(monster))
                {
                    // Rooted in place. A merely-immobilized monster may still attack from its current
                    // tile (area coverage is re-checked later); a stunned one cannot.
                    SetMonsterPlannedMove(monster, monster.Coord, canAttack: !IsMonsterAttackBlocked(monster));
                    continue;
                }

                var moveBudget = GetMonsterEffectiveMoveBudget(monster);
                if (moveBudget <= 0)
                {
                    // Slowed to a standstill (둔화 → 이동력): rooted this turn but may still attack unless stunned.
                    SetMonsterPlannedMove(monster, monster.Coord, canAttack: !IsMonsterAttackBlocked(monster));
                    continue;
                }

                var intent = monster.TurnPlan.IsActive ? monster.TurnPlan.MovementIntent : monster.Intent;
                if (IsMovementIntent(intent.Type))
                {
                    var next = monster.TurnPlan.IsActive ? monster.TurnPlan.PlannedMoveCoord : monster.IntentPredictedMoveCoord;
                    if (next != monster.Coord)
                    {
                        if (next == PlayerCoord && !Player.IsDead)
                        {
                            var playerBeforeKnockback = PlayerCoord;
                            var playerKnockedBack = TryKnockbackPlayerFromCollision(monster.Id);
                            if (playerKnockedBack && actionRecords != null && actionRecords.TryGetValue(monster.Id, out var record))
                            {
                                record.AffectedPlayer = true;
                                record.KnockedBackPlayer = true;
                                record.PlayerKnockbackFrom = playerBeforeKnockback;
                                record.PlayerKnockbackTo = PlayerCoord;
                            }

                            UpdateOccupancy();

                            if (!HexPathfinder.CanEnter(Map, monster.Coord, next, new MovementQuery(monster.Coord, 1, unitId: monster.Id, footprintRadius: GetMonsterFootprintRadius(monster), footprintOffsets: GetMonsterFootprintOffsets(monster)), runtimeStates, terrainTraits, out _))
                            {
                                SetMonsterPlannedMove(monster, monster.Coord, canAttack: false);
                                continue;
                            }

                            var collisionStart = monster.Coord;
                            monster.Coord = next;
                            SetMonsterPlannedMove(monster, monster.Coord, canAttack: true);
                            UpdateOccupancy();
                            if (actionRecords != null && actionRecords.TryGetValue(monster.Id, out var collisionRecord))
                            {
                                collisionRecord.MovePath = new[] { collisionStart, next };
                            }
                        }
                        else
                        {
                // Walk up to the effective move budget after Slow/Agility modifiers.
                            // toward the planned (telegraphed) destination, re-validated against current
                            // occupancy. A budget of 1 reproduces the legacy single-step move exactly.
                            var stepBudget = moveBudget;
                            var movePath = HexPathfinder.FindPath(
                                Map,
                                new MovementQuery(
                                    monster.Coord,
                                    stepBudget,
                                    unitId: monster.Id,
                                    footprintRadius: GetMonsterFootprintRadius(monster),
                                    footprintOffsets: GetMonsterFootprintOffsets(monster)),
                                next,
                                runtimeStates,
                                terrainTraits);
                            if (movePath.Count < 2)
                            {
                                SetMonsterPlannedMove(monster, monster.Coord, canAttack: false);
                                continue;
                            }

                            monster.Coord = movePath[movePath.Count - 1];
                            SetMonsterPlannedMove(monster, monster.Coord, canAttack: true);
                            UpdateOccupancy();
                            if (actionRecords != null && actionRecords.TryGetValue(monster.Id, out var walkRecord))
                            {
                                walkRecord.MovePath = movePath;
                            }
                        }
                    }
                }
            }
        }

        private bool TryKnockbackPlayerFromCollision(string sourceUnitId)
        {
            var startCoord = PlayerCoord;
            for (var knockbackDistance = 1; knockbackDistance <= 3; knockbackDistance++)
            {
                var candidates = new System.Collections.Generic.List<HexCoord>();
                foreach (var cell in Map.AllCells)
                {
                    if (!cell.BaseWalkable || !terrainTraits.IsWalkable(cell.TerrainTypeId) || Map.HasMovementBlockingObject(cell.Coord)) continue;
                    if (cell.Coord == startCoord) continue;
                    if (runtimeStates.TryGetValue(cell.Coord, out _)) continue;
                    if (startCoord.DistanceTo(cell.Coord) == knockbackDistance)
                        candidates.Add(cell.Coord);
                }

                if (candidates.Count == 0)
                {
                    continue;
                }

                PlayerCoord = candidates[pushRng.Next(candidates.Count)];
                ResolveTrapTriggersAt(PlayerCoord);
                RefreshPlayerVision();
                RaiseEffect(
                    EffectKind.Knockback,
                    startCoord,
                    0,
                    startCoord.DistanceTo(PlayerCoord),
                    PlayerUnitId,
                    "knockback",
                    sourceUnitId: sourceUnitId,
                    sourceActorKind: "monster",
                    targetActorKind: "player");

                return true;
            }

            return false;
        }

        /// <summary>
        /// 넉백 한 걸음이 들어갈 수 있는 칸인가 — 변위 루프와 예고(밀어붙이기 밀침 전진 예측)가
        /// <b>같은 술어</b>를 본다. 두 벌이면 예고가 그린 칸과 실제 멈춘 칸이 갈린다.
        /// </summary>
        private bool IsKnockbackStepOpen(HexCoord next)
        {
            return Map.TryGetCell(next, out var nextCell)
                   && nextCell.BaseWalkable
                   && terrainTraits.IsWalkable(nextCell.TerrainTypeId)
                   && !Map.HasMovementBlockingObject(next)
                   && !runtimeStates.ContainsKey(next);
        }

        public void ApplyDirectionalKnockback(
            HexCoord sourceCoord,
            string targetUnitId,
            int distance,
            int impactDamage,
            string presentationGroupId = "",
            string sourceUnitId = "",
            string sourceActorKind = "",
            string targetActorKind = "")
        {
            if (!TryResolveCombatant(targetUnitId, out var combatant, out var startCoord) || combatant.IsDead)
                return;
            // distance < 0 = <b>끌어당김</b>(§16.1). 밀어내기와 같은 변위 경로를 반대 방향으로 걷는다 —
            // 멈춤 조건(벽·기물·점유)이 같아야 "당겨서 몸에 박히는" 사고가 안 난다. 소스 칸 자체는
            // 점유 검사에 걸려 도달하지 못하므로 겹침이 생기지 않는다.
            if (sourceCoord == startCoord || distance == 0)
                return;

            var pulling = distance < 0;
            var steps = Math.Abs(distance);
            // P6(§13.4 C-5): 멀티셀 보스는 넉백 면역. footprint 원판을 안전하게 밀어낼 규칙이 없고
            // (밀린 자리가 결계 링·기물과 겹친다), "봉인 중 이동 0" 결정과도 한 몸이다. 이 함수는
            // 변위만 담당하므로(피해는 히트 쪽에서 이미 해소) 조용히 무시해도 계약이 어긋나지 않는다.
            var knockbackTargetMonster = monsters.FirstOrDefault(m => string.Equals(m.Id, targetUnitId, StringComparison.Ordinal));
            if (knockbackTargetMonster != null && IsMultiCellMonster(knockbackTargetMonster))
                return;

            var direction = pulling
                ? startCoord.ApproximateDirection(sourceCoord)
                : sourceCoord.ApproximateDirection(startCoord);
            var stepOffset = HexCoord.Directions[(int)direction];
            var current = startCoord;
            var hitObstacle = false;

            for (var i = 0; i < steps; i++)
            {
                var next = current + stepOffset;
                if (!IsKnockbackStepOpen(next))
                {
                    hitObstacle = true;
                    break;
                }
                current = next;
            }

            if (hitObstacle && impactDamage > 0 && !combatant.IsDead)
            {
                var blockBefore = combatant.Block;
                var applied = combatant.ApplyDamage(impactDamage);
                if (applied > 0)
                    RaiseEffect(
                        EffectKind.Damage,
                        current,
                        0,
                        applied,
                        targetUnitId,
                        "knockback.impact",
                        sourceUnitId: sourceUnitId,
                        sourceActorKind: sourceActorKind,
                        targetActorKind: targetActorKind,
                        presentationGroupId: presentationGroupId,
                        sourceCoord: sourceCoord);
                else if (combatant.Block < blockBefore)
                    RaiseEffect(
                        EffectKind.DamageBlocked,
                        current,
                        0,
                        0,
                        targetUnitId,
                        "knockback.impact",
                        sourceUnitId: sourceUnitId,
                        sourceActorKind: sourceActorKind,
                        targetActorKind: targetActorKind,
                        presentationGroupId: presentationGroupId,
                        sourceCoord: sourceCoord);
                if (combatant.IsDead)
                {
                    var deadMonster = monsters.FirstOrDefault(m => m.Combatant == combatant);
                    if (deadMonster != null)
                        ResetDeadMonsterToPatrolIntent(deadMonster);
                }
            }

            if (current == startCoord)
                return;

            if (string.Equals(targetUnitId, PlayerUnitId, StringComparison.Ordinal))
            {
                PlayerCoord = current;
                UpdateOccupancy();
                ResolveTrapTriggersAt(current);
                RefreshPlayerVision();
            }
            else
            {
                var monster = monsters.FirstOrDefault(m => m.Id == targetUnitId);
                if (monster != null)
                {
                    monster.Coord = current;
                    UpdateOccupancy();
                }
            }

            // 🔴 <b>부호가 방향이다</b>(§16.1) — 여기서 부호를 버리면 표현층이 끌어당김과 밀치기를
            //    구별할 방법이 <b>없다</b>(2026-09-01 #5: 둘 다 「밀려남」으로 떴다). sourceRef를 가르지
            //    않는 이유는 "knockback"을 <b>정확히 일치</b>로 보는 소비자가 여럿이라(타임라인 어셈블러·
            //    반응 정렬) 갈래를 늘리면 조용히 연출이 빠지기 때문이다. 프로젝트가 이미 쓰는 규약대로
            //    <b>수치의 부호</b>에 실어 보낸다.
            var travelled = startCoord.DistanceTo(current);
            RaiseEffect(
                EffectKind.Knockback,
                startCoord,
                0,
                pulling ? -travelled : travelled,
                targetUnitId,
                "knockback",
                sourceUnitId: sourceUnitId,
                sourceActorKind: sourceActorKind,
                targetActorKind: targetActorKind,
                presentationGroupId: presentationGroupId);
        }

        private static bool IsDebugEffectRef(string effectRef)
        {
            return effectRef == CardEffectRefs.DebugApplyBind
                || effectRef == CardEffectRefs.DebugApplySlow
                || effectRef == CardEffectRefs.DebugApplyRupture
                || effectRef == CardEffectRefs.DebugKnockback;
        }

        private void ApplyDebugCardEffect(CardDefinition card, HexCoord target)
        {
            var turns = Math.Max(1, card.DurationTurns);
            var amount = Math.Max(1, card.Amount);
            switch (card.EffectRef)
            {
                case CardEffectRefs.DebugApplyBind:
                {
                    var monster = FindLivingMonsterAt(target);
                    if (monster == null) return;
                    var intent = CaptureMonsterIntentForCancel(monster);
                    AddDurationStatusEffect(StatusEffectKind.Immobilize, monster.Id, turns, 0, card.EffectRef);
                    ApplyControlStatusConstraintToPlan(monster);
                    RaiseStatusEffect(StatusEffectKind.Immobilize, target, 0, turns, monster.Id, card.EffectRef);
                    EmitMonsterIntentCancelText(monster, intent.WasMoving, intent.WasAttacking, StatusEffectKind.Immobilize);
                    break;
                }
                case CardEffectRefs.DebugApplySlow:
                {
                    var monster = FindLivingMonsterAt(target);
                    if (monster == null) return;
                    AddDurationStatusEffect(StatusEffectKind.Slow, monster.Id, turns, amount, card.EffectRef);
                    RaiseStatusEffect(StatusEffectKind.Slow, target, 0, amount, monster.Id, card.EffectRef);
                    break;
                }
                case CardEffectRefs.DebugApplyRupture:
                {
                    AddDurationStatusEffect(StatusEffectKind.Rupture, PlayerUnitId, turns, amount, card.EffectRef);
                    RaiseStatusEffect(StatusEffectKind.Rupture, PlayerCoord, 0, turns, PlayerUnitId, card.EffectRef);
                    break;
                }
                case CardEffectRefs.DebugKnockback:
                {
                    var monster = FindLivingMonsterAt(target);
                    if (monster == null) return;
                    var knockbackDist = card.Amount > 0 ? card.Amount : 1;
                    ApplyDirectionalKnockback(PlayerCoord, monster.Id, knockbackDist, knockbackDist);
                    break;
                }
            }
        }

        public bool TryPlayerAttack()
        {
            var target = monsters
                .Where(monster => !monster.Combatant.IsDead)
                .OrderBy(monster => PlayerCoord.DistanceTo(monster.Coord))
                .ThenBy(monster => monster.Coord.Q)
                .ThenBy(monster => monster.Coord.R)
                .ThenBy(monster => monster.Id)
                .Select(monster => monster.Coord)
                .Cast<HexCoord?>()
                .FirstOrDefault();

            return target.HasValue
                ? TryPlayerAttack(target.Value)
                : Fail("Attack target must contain a living monster.");
        }

        public bool TryPlayerAttack(HexCoord target)
        {
            return TryPlayerAttack(target, string.Empty);
        }

        public bool TryPlayerAttack(HexCoord target, string cardId)
        {
            var card = FindAttackCardForTarget(target, cardId);
            if (card != null && card.TargetMode == CardTargetMode.SelfArea)
            {
                // Self-centred area attacks always detonate on the player's own tile regardless of the
                // requested target, hitting every monster within AreaRadius.
                target = PlayerCoord;
                if (string.IsNullOrEmpty(cardId))
                {
                    cardId = card.Id;
                }
            }

            var validation = ValidateAttackTarget(target, cardId);
            if (!validation.IsValid)
            {
                return Fail(validation.FailureReason);
            }

            if (RequiresSelectedHandCards(card))
            {
                var sacrifices = ActionDeck.Hand
                    .Where(candidate => !ReferenceEquals(candidate, card))
                    .ToArray();
                // Forward the *resolved* card's identity, not the caller's cardId: an auto-targeted attack
                // arrives with cardId empty, and TryPlayerSacrificeAttack re-resolves it through
                // FindActionCard(Attack, "") — which returns the first Attack card in hand, ignoring the
                // range/area ordering FindAttackCardForTarget used to pick this one. When those two disagree
                // the callee saw a non-sacrifice card and rejected the attack as "only supports the sacrifice
                // attack card". InstanceId (never empty; falls back to Id) re-resolves this exact card.
                return TryPlayerSacrificeAttack(target, card.InstanceId, sacrifices);
            }

            var spentKi = SpendCardKi(card);
            var attackBonus = GetPlayerTerrainAttackBonus();
            var damage = GetAttackDamage(card, attackBonus, spentKi, target);
            if (IsDebugEffectRef(card.EffectRef)) damage = 0;
            var hitCount = GetAttackHitCount(card);
            var anyKilled = false;
            // 이 공격이 보드에서 덮는 칸(취약 부위 판정의 입력 · §20-A-5). 타격 루프 밖에서 한 번만
            // 푼다 — 방향·형상은 타격마다 바뀌지 않고, 연출 루프도 같은 값을 봐야 화면과 규칙이 갈리지 않는다.
            var coverage = ResolveAttackCoverage(card, target);
            // 🔴🔴 연출 숫자는 <b>집행 시점에</b> 받아 둔다(2026-09-02 #5).
            //
            // <para>연출 루프는 피해 루프가 끝난 <b>뒤</b>에 돌면서 숫자를 다시 풀었는데, 그 사이 맷집
            // 래치(<c>ToughnessSpent</c>)가 이미 타 있어 <b>반감되지 않은 값</b>이 떴다 — HP는 5가 깎이는데
            // 화면에는 10이 뜨니 "맷집!"만 뜨고 피해는 그대로인 것처럼 보인다. 소진성 축(맷집)이 하나라도
            // 있으면 「나중에 다시 계산한다」는 성립하지 않는다.</para>
            //
            // <para>표식 배율은 <b>일부러</b> 빼고 받는다 — 예전부터 연출 숫자에 반영되지 않았고, 이 수정은
            // 맷집만 바로잡는다(같은 변경에서 두 축을 함께 바꾸면 무엇이 고쳐졌는지 판정할 수 없다).</para>
            var presentationDamageByHitTarget = new Dictionary<(int Hit, string UnitId), int>();
            // 취약타(§20-A) 표식: 연출 Damage 이벤트에 WeakSpotHit 비트로 실린다(SFX용 — 배율 자체는 아래 계산이 정본).
            var weakSpotHitTargets = new HashSet<(int Hit, string UnitId)>();
            for (var hit = 0; hit < hitCount; hit++)
            {
                if (!string.IsNullOrEmpty(card.ShapeId))
                {
                    var attackDir = PlayerCoord.ApproximateDirection(target);
                    var shapeCells = AttackShapeLibrary.GetAffectedCells(card.ShapeId, PlayerCoord, attackDir);
                    foreach (var splashTarget in monsters
                        .Where(m => IsVisibleLivingMonster(m) && shapeCells.Any(cell => IsMonsterOccupying(m, cell)))
                        .ToList())
                    {
                        presentationDamageByHitTarget[(hit, splashTarget.Id)] =
                            ResolvePlayerAttackDamageTo(splashTarget, damage, coverage);
                        if (IsBossWeakSpotHit(splashTarget, coverage))
                        {
                            weakSpotHitTargets.Add((hit, splashTarget.Id));
                        }
                        DamageMonster(
                            splashTarget,
                            ResolvePlayerAttackDamageTo(splashTarget, damage * ConsumeMarkMultiplier(splashTarget), coverage, execute: true));
                        if (splashTarget.Combatant.IsDead)
                        {
                            ResetDeadMonsterToPatrolIntent(splashTarget);
                            anyKilled = true;
                        }
                    }
                }
                else if (card.AreaRadius > 0)
                {
                    foreach (var splashTarget in monsters
                        .Where(monster => IsVisibleLivingMonster(monster) && GetDistanceToMonster(monster, target) <= card.AreaRadius)
                        .ToList())
                    {
                        presentationDamageByHitTarget[(hit, splashTarget.Id)] =
                            ResolvePlayerAttackDamageTo(splashTarget, damage, coverage);
                        if (IsBossWeakSpotHit(splashTarget, coverage))
                        {
                            weakSpotHitTargets.Add((hit, splashTarget.Id));
                        }
                        DamageMonster(
                            splashTarget,
                            ResolvePlayerAttackDamageTo(splashTarget, damage * ConsumeMarkMultiplier(splashTarget), coverage, execute: true));
                        if (splashTarget.Combatant.IsDead)
                        {
                            ResetDeadMonsterToPatrolIntent(splashTarget);
                            anyKilled = true;
                        }
                    }
                }
                else
                {
                    var monster = FindLivingMonsterAt(target);
                    if (monster == null)
                    {
                        break;
                    }

                    presentationDamageByHitTarget[(hit, monster.Id)] =
                        ResolvePlayerAttackDamageTo(monster, damage, coverage);
                    if (IsBossWeakSpotHit(monster, coverage))
                    {
                        weakSpotHitTargets.Add((hit, monster.Id));
                    }
                    DamageMonster(
                        monster,
                        ResolvePlayerAttackDamageTo(monster, damage * ConsumeMarkMultiplier(monster), coverage, execute: true));
                    if (monster.Combatant.IsDead)
                    {
                        ResetDeadMonsterToPatrolIntent(monster);
                        anyKilled = true;
                    }
                }
            }

            ApplyDebugCardEffect(card, target);
            ConsumePlayedCard(ActionDeck, card);
            ApplyPostActions(card, target);
            LastDiscardedCard = CombatCardKind.Attack;
            RegisterActionCardUse(CombatCardKind.Attack);
            LastFailureReason = string.Empty;
            LastInvestigateResult = string.Empty;
            if (anyKilled)
            {
                UpdateOccupancy();
            }

            // Per-target presentation: raise one Damage effect per hit monster so each gets its own
            // flinch/number/SFX (staggered by the AoE target interval) instead of a single bundled event.
            // Single-target keeps the card's AreaRadius so its area VFX still reads; multi-target uses 0.
            var presentationTargets = CollectAttackPresentationTargets(card, target);
            var attackGroupId = CreatePresentationGroupId(PlayerUnitId, card.Id);
            var presentationRadius = presentationTargets.Count <= 1 ? card.AreaRadius : 0;
            for (var hit = 0; hit < hitCount; hit++)
            {
                foreach (var presentationTarget in presentationTargets)
                {
                    // 대상마다 다시 푼다: 허점은 맞는 쪽에 걸리므로 같은 카드라도 몬스터별로 숫자가 다르다.
                    // (표식 배율은 위 피해 루프에서 이미 소진됐다 — 예전부터 연출 숫자에 반영되지 않았고,
                    //  여기서 바꾸지 않는다.)
                    RaiseEffect(
                        EffectKind.Damage,
                        presentationTarget.Coord,
                        presentationRadius,
                        // 집행 시점에 받아 둔 값이 있으면 그것이 정본이다(맷집 래치가 이미 타 있으므로
                        // 여기서 다시 풀면 반감이 사라진다 — 2026-09-02 #5). 기록이 없는 경우는 산 몬스터를
                        // 하나도 못 맞힌 폴백 대상("monster")뿐이고, 그때는 예전 그대로 다시 푼다.
                        presentationDamageByHitTarget.TryGetValue((hit, presentationTarget.UnitId), out var recordedDamage)
                            ? recordedDamage
                            : ResolvePlayerAttackDamageTo(
                                monsters.FirstOrDefault(candidate =>
                                    string.Equals(candidate.Id, presentationTarget.UnitId, StringComparison.Ordinal)),
                                damage,
                                coverage),
                        presentationTarget.UnitId,
                        card.Id,
                        sourceUnitId: PlayerUnitId,
                        sourceActorKind: "player",
                        targetActorKind: "monster",
                        sourceCardId: card.Id,
                        hitIndex: hit,
                        hitCount: hitCount,
                        presentationGroupId: attackGroupId,
                        delaySeconds: MultiHitBeatDelaySeconds(hit),
                        weakSpotHit: weakSpotHitTargets.Contains((hit, presentationTarget.UnitId)));
                }
            }

            return true;
        }

        // Collects the tiles + unit ids that an attack visibly hits, so each can get its own presentation
        // beat. Falls back to the aimed tile ("monster") when nothing living is hit.
        private List<(HexCoord Coord, string UnitId)> CollectAttackPresentationTargets(CardDefinition card, HexCoord target)
        {
            var targets = new List<(HexCoord Coord, string UnitId)>();
            if (!string.IsNullOrEmpty(card.ShapeId))
            {
                var attackDir = PlayerCoord.ApproximateDirection(target);
                var shapeCells = AttackShapeLibrary.GetAffectedCells(card.ShapeId, PlayerCoord, attackDir);
                foreach (var hitMonster in monsters.Where(m => IsVisibleLivingMonster(m) && shapeCells.Any(cell => IsMonsterOccupying(m, cell))))
                {
                    targets.Add((hitMonster.Coord, hitMonster.Id));
                }
            }
            else if (card.AreaRadius > 0)
            {
                foreach (var hitMonster in monsters.Where(m => IsVisibleLivingMonster(m) && GetDistanceToMonster(m, target) <= card.AreaRadius))
                {
                    targets.Add((hitMonster.Coord, hitMonster.Id));
                }
            }
            else
            {
                var monster = FindLivingMonsterAt(target);
                targets.Add((target, monster != null ? monster.Id : "monster"));
            }

            if (targets.Count == 0)
            {
                targets.Add((target, "monster"));
            }

            return targets;
        }

        private bool IsVisibleLivingMonster(MonsterRuntime monster)
        {
            return !monster.Combatant.IsDead && visibilityRuntime.GetVisibility(monster.Coord) == HexCellVisibility.Revealed;
        }

        private string ResolveAttackEffectTargetUnitId(HexCoord target, int areaRadius)
        {
            if (areaRadius > 0)
            {
                return "monster";
            }

            var monster = FindLivingMonsterAt(target);
            return monster != null ? monster.Id : "monster";
        }

        public bool TryPlayerSacrificeAttack(HexCoord target, string cardId, IReadOnlyList<CardDefinition> sacrificeCards)
        {
            var card = FindActionCard(CardEffectType.Attack, cardId);
            var validation = ValidateAttackTarget(target, cardId);
            if (!validation.IsValid)
            {
                return Fail(validation.FailureReason);
            }

            if (!RequiresSelectedHandCards(card))
            {
                return Fail("This method only supports the sacrifice attack card.");
            }

            if (!ValidateAdditionalCosts(card, sacrificeCards, out var costFailureReason))
            {
                return Fail(costFailureReason);
            }

            SpendCardKi(card);
            var attackBonus = GetPlayerTerrainAttackBonus();
            var damage = sacrificeCards.Count * card.Amount + attackBonus;
            var attackEffectTargetUnitId = ResolveAttackEffectTargetUnitId(target, 0);

            PayAdditionalCosts(card, sacrificeCards);

            var monster = FindLivingMonsterAt(target);
            var anyKilled = false;
            // 연출 숫자는 집행 <b>전에</b> 받아 둔다 — 아래 RaiseEffect가 다시 풀면 맷집 래치가 이미 타 있어
            // 반감되지 않은 값이 뜬다(2026-09-02 #5, 일반 공격 경로와 같은 결함). null이면 예전과 같이 0이 뜬다.
            var sacrificePresentationDamage = monster != null
                ? ResolvePlayerAttackDamageTo(monster, damage, AttackCoverage.AtCell(target))
                : 0;
            if (monster != null)
            {
                DamageMonster(
                    monster,
                    ResolvePlayerAttackDamageTo(monster, damage * ConsumeMarkMultiplier(monster), AttackCoverage.AtCell(target), execute: true));
                if (monster.Combatant.IsDead)
                {
                    ResetDeadMonsterToPatrolIntent(monster);
                    anyKilled = true;
                }
            }

            ConsumePlayedCard(ActionDeck, card);
            ApplyPostActions(card, target);
            LastDiscardedCard = CombatCardKind.Attack;
            RegisterActionCardUse(CombatCardKind.Attack);
            LastFailureReason = string.Empty;
            LastInvestigateResult = string.Empty;
            if (anyKilled)
            {
                UpdateOccupancy();
            }

            RaiseEffect(
                EffectKind.Damage,
                target,
                0,
                // 쇠약·허점·맷집을 반영한 숫자로 띄운다(표식 배율은 위에서 소진됐고 예전부터 연출에 반영되지 않는다).
                sacrificePresentationDamage,
                attackEffectTargetUnitId,
                card.Id,
                sourceUnitId: PlayerUnitId,
                sourceActorKind: "player",
                targetActorKind: "monster",
                sourceCardId: card.Id,
                hitIndex: 0,
                hitCount: 1,
                presentationGroupId: CreatePresentationGroupId(PlayerUnitId, card.Id),
                weakSpotHit: monster != null && IsBossWeakSpotHit(monster, AttackCoverage.AtCell(target)));
            return true;
        }

        public bool TryPlayerSacrificeAttack(HexCoord target, string cardId, IReadOnlyList<string> sacrificeCardKeys)
        {
            var card = FindActionCard(CardEffectType.Attack, cardId);
            if (card == null)
            {
                return Fail("Sacrifice attack card is not in the action hand.");
            }

            var selectedCards = ResolveActionHandCards(sacrificeCardKeys)
                .Where(candidate => !ReferenceEquals(candidate, card))
                .ToArray();
            return TryPlayerSacrificeAttack(target, cardId, selectedCards);
        }

        public bool RequiresDiscardSelectedHandCards(string cardId)
        {
            var card = FindActionCard(CardEffectType.Attack, cardId);
            return RequiresSelectedHandCards(card);
        }

        public bool TryPlayerChoiceOption(string cardId, string optionId, HexCoord? target = null)
        {
            // Choice used to be an attack-card-only concept (A03). U02 부적 끌어오기 is a utility card, so the
            // lookup keys off the card itself; the empty-id path keeps its old "first attack card" meaning.
            var card = string.IsNullOrEmpty(cardId)
                ? FindActionCard(CardEffectType.Attack)
                : ActionDeck.Hand.FirstOrDefault(candidate => MatchesCardKey(candidate, cardId));
            if (!CanUseActionCard(card, out var reason))
            {
                return Fail(reason);
            }

            if (!TryResolveChoiceOption(card, optionId, out var option))
            {
                return Fail("Choice card effect is not supported.");
            }

            if (string.Equals(option.EffectRef, CardBehaviorMetadata.ChoiceEffectHealPlayer, StringComparison.Ordinal))
            {
                SpendCardKi(card);
                // I-08(WS-I): 회복은 heal 축을 쓴다 — Amount는 damage 우선으로 접혀 있어 A03(damage 3·heal 4)의
                // 회복이 3으로 새고 있었다.
                var healed = Player.Heal(card.EffectiveHealAmount);
                ConsumePlayedCard(ActionDeck, card);
                LastDiscardedCard = CombatCardKind.Attack;
                RegisterActionCardUse(CombatCardKind.Attack);
                LastFailureReason = string.Empty;
                LastInvestigateResult = string.Empty;
            // Sunlit Rain (A03) and Field Heal (F02) should share the same VFX path.
            // Preserve sourceRef as FieldHeal so catalog entries including scale/offset resolve consistently.
                RaiseEffect(EffectKind.Heal, PlayerCoord, 0, healed, "player", CardEffectRefs.FieldHeal);
                return true;
            }

            if (string.Equals(option.EffectRef, CardBehaviorMetadata.ChoiceEffectAttackDamage, StringComparison.Ordinal))
            {
                if (!target.HasValue)
                {
                    return Fail("Choice attack target is required.");
                }

                return TryPlayerAttack(target.Value, cardId);
            }

            if (string.Equals(option.EffectRef, CardBehaviorMetadata.ChoiceEffectDrawActionCards, StringComparison.Ordinal))
            {
                SpendCardKi(card);
                // Discard first: the played card must not be one of the cards it draws back.
                ConsumePlayedCard(ActionDeck, card);
                DrawActionCards(Math.Max(0, ChoiceDrawCountOf(card)));
                ResolveChoiceUtilityBookkeeping();
                return true;
            }

            if (string.Equals(option.EffectRef, CardBehaviorMetadata.ChoiceEffectRecoverExiledCard, StringComparison.Ordinal))
            {
                SpendCardKi(card);
                ConsumePlayedCard(ActionDeck, card);
                // D8-consistent: an empty 소멸 더미 does not refuse the option, it simply returns nothing.
                RecoverRandomExiledCard();
                ResolveChoiceUtilityBookkeeping();
                return true;
            }

            return Fail("Unknown choice option effect.");
        }

        private void ResolveChoiceUtilityBookkeeping()
        {
            LastDiscardedCard = CombatCardKind.Utility;
            RegisterActionCardUse(CombatCardKind.Utility);
            LastFailureReason = string.Empty;
            LastInvestigateResult = string.Empty;
        }

        // 임의의 소멸된 부적 1장을 손으로 되돌린다 (U02). Random, not player-picked: the 소멸 더미 overlay is a
        // read-only viewer, and giving it a pick mode is a UI track of its own.
        private bool RecoverRandomExiledCard()
        {
            var exiledCount = CountExiledCards();
            if (exiledCount <= 0)
            {
                return false;
            }

            var index = pushRng.Next(exiledCount);
            return index < MovementDeck.RemovedPile.Count
                ? MovementDeck.TryRecoverFromRemovedPile(MovementDeck.RemovedPile[index])
                : ActionDeck.TryRecoverFromRemovedPile(ActionDeck.RemovedPile[index - MovementDeck.RemovedPile.Count]);
        }

        public bool TryPlayerHolyLightHeal()
        {
            var holyLight = ActionDeck.Hand.FirstOrDefault(card =>
                card?.Id == CardIds.HolyLight || HasChoiceOption(card, "heal"));
            return holyLight != null
                ? TryPlayerChoiceOption(holyLight.InstanceId, "heal")
                : Fail("Holy Light is not in the action hand.");
        }

        private void ResetDeadMonsterToPatrolIntent(MonsterRuntime monster)
        {
            // 처치 수렴 지점(T2 페이즈 C): 공격·스플래시·반사·함정·필드 어느 경로로 죽든 전부 여기를
            // 지나므로, 별도 훅을 곳곳에 심는 대신 이 자리에서 처치 트리거를 알린다.
            NotifyMonsterKilledForRelics(monster);
            // 뒤끝(T7-2): 죽은 자리 효과. 아래 인텐트 리셋은 Coord를 바꾸지 않지만, "죽은 자리"가
            // 유효한 시점이라는 계약을 코드 순서로도 못박는다.
            ResolveMonsterDeathAftermath(monster);
            monster.PendingAttackIntent = false;
            monster.Intent = new EnemyIntent(EnemyIntentType.Patrol, PlayerCoord.DistanceTo(monster.Coord), monster.Coord, PlayerCoord);
            monster.LockedFacingIntent = monster.Intent;
            monster.IntentPredictedMoveCoord = monster.Coord;
            monster.TurnPlan = MonsterTurnPlan.Inactive(monster.Coord);
        }

        /// <summary>
        /// 방범대 호루라기(T2 페이즈 C): 몬스터 처치 시 기 +N(최대 기 상한 — "회복" 문법).
        /// 이 게임의 모든 피해 출처는 플레이어이므로 함정·필드·반사 처치도 전부 인정한다(테스트로 잠금).
        /// 보스 기물(철조각 등)은 처치로 치지 않고, 같은 몬스터 중복 보상은 래치로 막는다.
        /// </summary>
        private void NotifyMonsterKilledForRelics(MonsterRuntime monster)
        {
            if (monster == null || !monster.Combatant.IsDead || IsPropMonster(monster))
            {
                return;
            }

            if (!relicKillRewardedMonsterIds.Add(monster.Id))
            {
                return;
            }

            var items = PlayerInventory?.RelicsAndCurses?.Items;
            if (items == null)
            {
                return;
            }

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.IsActive && item.TriggerKind == RelicTriggerKind.KiOnKill)
                {
                    ActionCostRemaining = Math.Min(MaxKi, ActionCostRemaining + Math.Max(0, item.EffectAmount));
                }
            }
        }

        internal string CreatePresentationGroupId(string ownerId, string actionId)
        {
            return presentationBuffer.NextGroupId(ownerId, actionId);
        }

        internal void RaiseEffect(
            EffectKind kind,
            HexCoord center,
            int radius,
            int amount,
            string targetUnitId,
            string sourceRef,
            string sourceUnitId = "",
            string sourceActorKind = "",
            string targetActorKind = "",
            string sourceCardId = "",
            string sourcePatternId = "",
            int hitIndex = 0,
            int hitCount = 0,
            string presentationGroupId = "",
            StatusEffectKind? statusKind = null,
            float delaySeconds = 0f,
            bool lethal = false,
            HexCoord? sourceCoord = null,
            IReadOnlyList<HexCoord> areaCoords = null,
            bool weakSpotHit = false)
        {
            if (!presentationBuffer.IsBuffering && EffectResolved == null)
            {
                return;
            }

            // Presentation-only HP telemetry: every call site applies the damage/heal before raising,
            // so the target's HP right now is the post-effect value. Stamping it here lets the health
            // bar land on an absolute value at the moment the floating number shows, even when the
            // event is buffered and presented much later than the simulation resolved it.
            var previousValue = 0;
            var currentValue = 0;
            if ((kind == EffectKind.Damage || kind == EffectKind.Heal) && TryGetUnitCurrentHp(targetUnitId, out var hpAfter))
            {
                currentValue = hpAfter;
                previousValue = kind == EffectKind.Damage
                    ? hpAfter + Math.Max(0, amount)
                    : Math.Max(0, hpAfter - Math.Max(0, amount));
            }

            var resultEvent = new EffectResultEvent(
                kind,
                targetUnitId: targetUnitId,
                amount: amount,
                appliedAmount: amount,
                previousValue: previousValue,
                currentValue: currentValue,
                center: center,
                radius: radius,
                sourceRef: sourceRef,
                sourceUnitId: sourceUnitId,
                sourceActorKind: sourceActorKind,
                targetActorKind: targetActorKind,
                sourceCardId: sourceCardId,
                sourcePatternId: sourcePatternId,
                hitIndex: hitIndex,
                hitCount: hitCount,
                presentationGroupId: presentationGroupId,
                statusKind: statusKind,
                delaySeconds: delaySeconds,
                lethal: lethal,
                sourceCoord: sourceCoord,
                areaCoords: areaCoords);

            if (presentationBuffer.TryEnqueue(resultEvent))
            {
                return;
            }

            EffectResolved.Invoke(resultEvent);
        }

        // Current HP of the unit an effect event targets; false for non-unit targets ("field", traps).
        private bool TryGetUnitCurrentHp(string unitId, out int hp)
        {
            hp = 0;
            if (string.IsNullOrWhiteSpace(unitId))
            {
                return false;
            }

            if (string.Equals(unitId, PlayerUnitId, StringComparison.Ordinal))
            {
                hp = Math.Max(0, Player.Hp);
                return true;
            }

            var monster = monsters.FirstOrDefault(candidate => string.Equals(candidate.Id, unitId, StringComparison.Ordinal));
            if (monster == null)
            {
                return false;
            }

            hp = Math.Max(0, monster.Combatant.Hp);
            return true;
        }

        internal void RaiseStatusEffect(
            StatusEffectKind kind,
            HexCoord center,
            int radius,
            int amount,
            string targetUnitId,
            string sourceRef,
            string sourceUnitId = "",
            string sourceActorKind = "",
            string targetActorKind = "",
            string sourceCardId = "",
            string sourcePatternId = "",
            int hitIndex = 0,
            int hitCount = 0,
            string presentationGroupId = "")
        {
            if (!presentationBuffer.IsBuffering && EffectResolved == null)
            {
                return;
            }

            var resultEvent = new EffectResultEvent(
                EffectKind.StatusEffectApplied,
                targetUnitId: targetUnitId,
                amount: amount,
                appliedAmount: amount,
                center: center,
                radius: radius,
                sourceRef: sourceRef,
                sourceUnitId: sourceUnitId,
                sourceActorKind: sourceActorKind,
                targetActorKind: targetActorKind,
                sourceCardId: sourceCardId,
                sourcePatternId: sourcePatternId,
                hitIndex: hitIndex,
                hitCount: hitCount,
                presentationGroupId: presentationGroupId,
                statusKind: kind);

            if (presentationBuffer.TryEnqueue(resultEvent))
            {
                return;
            }

            EffectResolved.Invoke(resultEvent);
        }

        public bool TryPlayerDefend()
        {
            return TryPlayerDefend(string.Empty);
        }

        public bool TryPlayerDefend(string cardId)
        {
            var card = FindActionCard(CardEffectType.Defend, cardId);
            if (!CanUseActionCard(card, out var reason))
            {
                return Fail(reason);
            }

            SpendCardKi(card);
            if (!CardBehaviorRegistry.Resolve(card).TryApplyDefend(this, card, out var blockReported))
            {
                blockReported = AddBlockWithRupture(Player, PlayerUnitId, card.Amount);
            }

            ConsumePlayedCard(ActionDeck, card);
            LastDiscardedCard = CombatCardKind.Defend;
            RegisterActionCardUse(CombatCardKind.Defend);
            LastFailureReason = string.Empty;
            // Protection Zone in Rain (D02) applies damage immunity instead of block.
            // Double Edged Shield (D03) gets a separate reflected-damage status instead of the generic block text.
            // Other defense cards use the generic "Block +N" status text.
            var defendSourceRef = EffectSourceRefOf(card);
            if (CombatEffectSourceClassifier.IsDamageImmunitySource(defendSourceRef))
            {
                // Named as the source card as well as the behaviour: the behaviour ref is what presentation
                // matches by default, so without the card id a per-card VFX cue can never be selected.
                RaiseEffect(EffectKind.Block, PlayerCoord, 0, 0, "player", defendSourceRef, sourceCardId: card.Id);
            }
            else if (defendSourceRef != CardEffectRefs.DefendHalfReflect && blockReported > 0)
            {
                RaiseEffect(EffectKind.Block, PlayerCoord, 0, blockReported, "player", card.Id);
            }
            return true;
        }

        public bool TryPlayerScout(HexCoord target)
        {
            return TryPlayerScout(target, string.Empty);
        }

        public bool TryPlayerScout(HexCoord target, string cardId)
        {
            var card = FindActionCard(CardEffectType.Scout, cardId);
            var validation = ValidateScoutTarget(target, cardId);
            if (!validation.IsValid)
            {
                return Fail(validation.FailureReason);
            }

            var revealRadius = card.AreaRadius > 0 ? card.AreaRadius : ScoutHintRadius;
            visibilityRuntime.ScoutReveal(target, revealRadius);
            RecordScoutRevealedCells(target, revealRadius);
            // 취약 부위 판명(§20-A-4). 정찰만이 유일한 판명 경로다 — "때려서 알아내기"를 남기면
            // 다단 히트 카드 한 장이 정찰 카드를 통째로 대체한다.
            RevealBossWeakSpotsInScoutArea(target, revealRadius);
            // 전멸기 안전지대 후보의 진위 판별(§20-B-4). 바로 아래 함정 발견과 <b>같은 카드 한 장</b>이
            // "여기가 진짜인가"와 "여기 함정이 있는가"를 동시에 답한다 — 그래서 함정과 겹친 후보에
            // 강제 발견 특례를 두지 않는다(특례를 두면 함정 발견의 의미가 두 갈래로 갈라진다).
            RevealBossAnnihilationCandidatesInScoutArea(target, revealRadius);
            visibilityRuntime.RevealTrapsInArea(target, revealRadius);
            // 런타임(보스 배치) 함정은 위 맵 순회에 걸리지 않는다 — 같은 정찰 한 장이 두 출처를
            // 함께 발견해야 "정찰한 것만 보인다" 계약이 출처를 가리지 않는다(2026-09-03 함정 숨김 전환).
            RevealRuntimeTrapsInArea(target, revealRadius);
            // 은신 몬스터도 이 한 장에 드러난다(Q1 확정) — 정찰이 "여기 뭐가 있나"를 통째로 답한다.
            RevealStealthMonstersInScoutArea(target, revealRadius);
            // 정찰(밝혀짐)을 데미지보다 먼저 발행해 표현 순서가 reveal → (AoE 간격) → 범위 → 몬스터별 타격이 되게 한다.
            // Buffer ordering: FogReveal presentation should run before damage from ApplyAfterScoutReveal.
            RaiseEffect(EffectKind.FogReveal, target, revealRadius, revealRadius, "player", card.Id);
            CardBehaviorRegistry.Resolve(card).ApplyAfterScoutReveal(this, card, target, revealRadius);

            ResolveActionCard(card, CombatCardKind.Scout);
            return true;
        }

        public CardTargetValidationResult ValidateFieldObjectTarget(HexCoord target, string cardId = "")
        {
            var card = FindActionCard(CardEffectType.FieldObject, cardId);
            if (!CanUseActionCard(card, out var reason))
            {
                return CardTargetValidationResult.Failure(reason);
            }

            if (card.FieldObjectKind == CardFieldObjectKind.None)
            {
                return CardTargetValidationResult.Failure("FieldObject card has no field object kind.");
            }

            if (!Map.TryGetCell(target, out var cell))
            {
                return CardTargetValidationResult.Failure("FieldObject target is not on the map.");
            }

            if (!cell.BaseWalkable || Map.HasMovementBlockingObject(target))
            {
                return CardTargetValidationResult.Failure("FieldObject target must be an unblocked walkable map cell.");
            }

            if (FieldObjects.Objects.Any(existing => existing.Position == target)
                || PendingFieldObjects.Objects.Any(existing => existing.Position == target))
            {
                return CardTargetValidationResult.Failure("A field object is already placed on this tile.");
            }

            // Ranged (non-Self) placements cannot target the player's or a living monster's tile.
            if (card.PlayMode != CardPlayMode.Self)
            {
                if (!Player.IsDead && target == PlayerCoord)
                {
                    return CardTargetValidationResult.Failure("Cannot place a field object on the player's tile.");
                }

                if (monsters.Any(monster => !monster.Combatant.IsDead && IsMonsterOccupying(monster, target)))
                {
                    return CardTargetValidationResult.Failure("Cannot place a field object on an occupied tile.");
                }
            }

            if (card.PlayMode == CardPlayMode.Self && target != PlayerCoord)
            {
                return CardTargetValidationResult.Failure("This FieldObject card must be placed on the player tile.");
            }

            if (card.PlayMode != CardPlayMode.Self && PlayerCoord.DistanceTo(target) > card.Range)
            {
                return CardTargetValidationResult.Failure("FieldObject target is out of range.");
            }

            return CardTargetValidationResult.Success();
        }

        public bool TryPlayerFieldObject(HexCoord target, string cardId = "")
        {
            var card = FindActionCard(CardEffectType.FieldObject, cardId);
            var validation = ValidateFieldObjectTarget(target, cardId);
            if (!validation.IsValid)
            {
                return Fail(validation.FailureReason);
            }

            var kind = ToRuntimeFieldObjectKind(card.FieldObjectKind);
            // hitCount is the card's authored hits-per-tick (F05 콩콩탄탄 = 2). Every other field row leaves the
            // column empty, which the importer resolves to 1 — the pre-F05 single-hit tick.
            var fieldObject = new FieldObject(target, card.AreaRadius, card.DurationTurns, kind, card.Amount, PlayerUnitId, card.Id, card.HitCount);
            FieldObjects.Add(fieldObject);
            RaiseFieldPlacementVfx(kind, fieldObject, card.Id);
            UpdateOccupancy();
            RefreshPlayerVision();
            ResolveActionCard(card, CombatCardKind.FieldObject);
            return true;
        }

        // Presentation-only: surface the field object's footprint when it is installed. Gameplay tick effects
        // still run at the next overall-turn start; occupancy and field-vision begin immediately.
        private void RaiseFieldPlacementVfx(FieldObjectKind kind, FieldObject fieldObject, string sourceCardId)
        {
            const string placementSourceRef = CardEffectRefs.FieldPlacement;
            switch (kind)
            {
                case FieldObjectKind.FieldDamage:
                case FieldObjectKind.LifestealDamage:
                    RaiseEffect(EffectKind.Damage, fieldObject.Position, fieldObject.Radius, 0, "field", placementSourceRef, sourceCardId: sourceCardId);
                    break;
                case FieldObjectKind.ConditionalHeal:
                    RaiseEffect(EffectKind.Heal, fieldObject.Position, fieldObject.Radius, 0, "field", placementSourceRef, sourceCardId: sourceCardId);
                    break;
                case FieldObjectKind.MassImmobilize:
                    RaiseEffect(EffectKind.StatusEffectApplied, fieldObject.Position, fieldObject.Radius, 0, "field", placementSourceRef, sourceCardId: sourceCardId, statusKind: StatusEffectKind.Immobilize);
                    break;
                case FieldObjectKind.StatusZone:
                    // 지대는 종류가 저작으로 갈리므로 kind를 하드코딩하지 않고 장판이 든 값을 그대로 쓴다.
                    RaiseEffect(EffectKind.StatusEffectApplied, fieldObject.Position, fieldObject.Radius, 0, "field", placementSourceRef, sourceCardId: sourceCardId, statusKind: fieldObject.StatusKind);
                    break;
            }
        }

        public bool TryClaimRewardEventObject(CombatEventObjectDefinition eventObject, RewardEventObjectOffer selectedOffer, out string reason)
        {
            reason = string.Empty;
            if (eventObject == null)
            {
                reason = "Reward event object is required.";
                return false;
            }

            if (eventObject.Category != CombatEventObjectCategory.Reward)
            {
                reason = "Event object is not a reward event.";
                return false;
            }

            if (claimedEventObjectIds.Contains(eventObject.ObjectId))
            {
                reason = "Reward event object was already claimed.";
                return false;
            }

            if (!eventObject.RewardOffers.Contains(selectedOffer))
            {
                reason = "Selected reward is not offered by this event object.";
                return false;
            }

            if (!TryApplyRewardEventOffer(eventObject, selectedOffer, out reason))
            {
                return false;
            }

            claimedEventObjectIds.Add(eventObject.ObjectId);
            return true;
        }

        public bool TrySkipRewardEventObject(CombatEventObjectDefinition eventObject, out string reason)
        {
            reason = string.Empty;
            if (eventObject == null)
            {
                reason = "Reward event object is required.";
                return false;
            }

            if (eventObject.Category != CombatEventObjectCategory.Reward)
            {
                reason = "Event object is not a reward event.";
                return false;
            }

            if (claimedEventObjectIds.Contains(eventObject.ObjectId))
            {
                reason = "Reward event object was already claimed.";
                return false;
            }

            claimedEventObjectIds.Add(eventObject.ObjectId);
            return true;
        }

        public bool TryPlayerUtility(string cardId)
        {
            // 빚 문서(X04, T2): 낼 수 있는 유일한 저주. 효과가 "기 1을 내고 자신을 소멸"이 전부라
            // 핸들러 없이 여기서 끝낸다 — 소멸 더미로 가므로 회수(U02)·소멸 스케일(A13)과도 이어진다.
            var playableCurse = string.IsNullOrEmpty(cardId)
                ? null
                : ActionDeck.Hand.FirstOrDefault(candidate =>
                    IsPlayableCurseCard(candidate) && MatchesCardKey(candidate, cardId));
            if (playableCurse != null)
            {
                if (!CanUseActionCard(playableCurse, out var curseReason))
                {
                    return Fail(curseReason);
                }

                SpendCardKi(playableCurse);
                ActionDeck.PermanentRemoveFromHand(playableCurse);
                LastDiscardedCard = CombatCardKind.Utility;
                RegisterActionCardUse(CombatCardKind.Utility);
                LastFailureReason = string.Empty;
                LastInvestigateResult = string.Empty;
                return true;
            }

            var card = FindActionCard(CardEffectType.Utility, cardId);
            if (!CanUseActionCard(card, out var reason))
            {
                return Fail(reason);
            }

            var hasHandler = CardBehaviorRegistry.Resolve(card).HasUtilityEffect(this, card);
            if (!hasHandler && card.Id != CardIds.Redraw)
            {
                return Fail("Utility card effect is not supported.");
            }

            SpendCardKi(card);
            if (hasHandler)
            {
                // U03 정화 뽑기 draws into the action hand itself, so unlike U01 it has to discard the
                // played card explicitly — U01's whole-hand redraw below already sweeps it away.
                CardBehaviorRegistry.Resolve(card).TryApplyUtility(this, card);
                ConsumePlayedCard(ActionDeck, card);
            }
            else
            {
                var movementRedrawCount = MovementDeck.HandCount;
                var actionRedrawCount = ActionDeck.HandCount;
                MovementDeck.DiscardHand();
                ActionDeck.DiscardHand();
                MovementDeck.Draw(movementRedrawCount);
                DrawActionCards(actionRedrawCount);
            }

            LastDiscardedCard = CombatCardKind.Utility;
            RegisterActionCardUse(CombatCardKind.Utility);
            LastFailureReason = string.Empty;
            LastInvestigateResult = string.Empty;
            return true;
        }

        private bool TryApplyRewardEventOffer(CombatEventObjectDefinition eventObject, RewardEventObjectOffer offer, out string reason)
        {
            reason = string.Empty;
            switch (offer.Kind)
            {
                case RewardEventObjectOfferKind.Card:
                    if (eventObject.RewardKind != RewardEventObjectKind.CardDrawMachine)
                    {
                        reason = "Card rewards must come from a card reward event object.";
                        return false;
                    }

                    return TryAddRewardCardToDeck(offer.RewardId, out reason);
                case RewardEventObjectOfferKind.PermanentItem:
                    if (eventObject.RewardKind != RewardEventObjectKind.RelicDrawMachine)
                    {
                        reason = "Permanent item rewards must come from a relic reward event object.";
                        return false;
                    }

                    return TryGrantPermanentItem(offer.RewardId, out reason);
                case RewardEventObjectOfferKind.Money:
                    if (eventObject.RewardKind != RewardEventObjectKind.MoneyDrawMachine)
                    {
                        reason = "Money rewards must come from a money reward event object.";
                        return false;
                    }

                    if (offer.Amount <= 0)
                    {
                        reason = "Money reward amount must be positive.";
                        return false;
                    }

                    GrantMoneyWithRelicBonus(offer.Amount);
                    return true;
                case RewardEventObjectOfferKind.BagItem:
                    if (eventObject.RewardKind != RewardEventObjectKind.ItemDrawMachine)
                    {
                        reason = "Bag item rewards must come from an item reward event object.";
                        return false;
                    }

                    if (!TryAddBagItem(offer.RewardId))
                    {
                        // 호출부(뽑기)는 사전에 만원을 걸러 돈 폴백으로 돌리므로 여기 도달은 저작/경합 오류다.
                        reason = "가방이 가득 차 있거나 알 수 없는 아이템입니다.";
                        return false;
                    }

                    return true;
                default:
                    reason = "Unsupported reward offer kind.";
                    return false;
            }
        }

        private bool TryAddRewardCardToDeck(string cardId, out string reason)
        {
            return TryAddRewardCardToCurrentHand(cardId, out reason);
        }

        public bool TryPlayerInvestigate(HexCoord target)
        {
            var card = FindActionCard(CardEffectType.Investigate);
            var validation = ValidateInvestigateTarget(target);
            if (!validation.IsValid)
            {
                return Fail(validation.FailureReason);
            }

            var result = CombatObjectiveCompletionPolicy.Resolve(
                ObjectiveCompleted,
                objectiveTargetCoord,
                PlayerCoord,
                target,
                visibilityRuntime.GetVisibility(target),
                card.Range);
            ApplyObjectiveCompletionResult(result);
            ResolveActionCard(card, CombatCardKind.Investigate);
            LastInvestigateResult = FormatObjectiveCompletionMessage(result.Outcome);
            return result.CompletedNow;
        }

        public bool EndAction()
        {
            LastFailureReason = string.Empty;
            if (IsTerminal)
            {
                return Fail("Combat ended. Restart to play again.");
            }

            if (Phase == CombatPhase.PlayerMovement)
            {
                lastMonsterActionRecords.Clear();
                pendingMonsterActionRecords = null;
                CommitMonsterAttackIntentsAfterPlayerMovementEnd();
                SetPhase(CombatPhase.MonsterMovement);
                return true;
            }

            if (Phase != CombatPhase.PlayerAction)
            {
                return Fail("End Action is only available during player movement or action.");
            }

            // 순서가 중요하다: 피해 판정이 소멸보다 먼저다. 미세먼지는 여기서 사라지지만
            // 깨진 유리는 남아 있으므로 둘의 순서가 뒤집혀도 결과는 같다 — 그래도 "턴 끝에 손에
            // 남아 있었는가"를 재는 쪽을 먼저 두어 규칙과 코드 순서를 일치시킨다.
            ResolveStatusCardTurnEndDamage();
            ResolveCurseCardTurnEndEffects();
            // 붕어빵 틀(T2 페이즈 C): 턴 종료 방어막 — 이 시점의 방어막은 곧 이어질 몬스터 공격을 받고
            // 다음 턴 시작(ClearBlock)에 사라진다(StS Metallicize 문법). ⚠️턴말 훅은 액션 페이즈
            // EndAction에서만 돈다 — 이동 페이즈 분기는 위에서 이미 빠져나갔다.
            ResolveTurnEndRelicTriggers();
            PurgeCopiesFromActionHand();
            SetPhase(CombatPhase.MonsterAction);
            ActionCostRemaining = 0;
            return true;
        }

        /// <summary>트리거 유물(T2 페이즈 C)의 턴 시작 훅. BeginNextOverallTurn에서만 부른다.</summary>
        private void ResolveTurnStartRelicTriggers()
        {
            var items = PlayerInventory?.RelicsAndCurses?.Items;
            if (items == null || Player.IsDead)
            {
                return;
            }

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (!item.IsActive)
                {
                    continue;
                }

                switch (item.TriggerKind)
                {
                    case RelicTriggerKind.HealOnTurnStart:
                    {
                        var healed = Player.Heal(Math.Max(0, item.EffectAmount));
                        if (healed > 0)
                        {
                            RaiseEffect(EffectKind.Heal, PlayerCoord, 0, healed, PlayerUnitId, RelicTriggerSourceRef(item));
                        }

                        break;
                    }

                    case RelicTriggerKind.GuardChargeCycle:
                        if (item.TriggerParam > 0 && OverallTurnNumber % item.TriggerParam == 0)
                        {
                            GrantGuardCharge(item.EffectAmount, RelicTriggerSourceRef(item));
                        }

                        break;

                    case RelicTriggerKind.StealthCycle:
                        if (item.TriggerParam > 0 && OverallTurnNumber % item.TriggerParam == 0)
                        {
                            GrantStealth(item.EffectAmount, RelicTriggerSourceRef(item));
                        }

                        break;
                }
            }
        }

        /// <summary>트리거 유물(T2 페이즈 C)의 턴 종료 훅(붕어빵 틀). 액션 페이즈 EndAction에서만 부른다.</summary>
        private void ResolveTurnEndRelicTriggers()
        {
            var items = PlayerInventory?.RelicsAndCurses?.Items;
            if (items == null || Player.IsDead)
            {
                return;
            }

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (!item.IsActive || item.TriggerKind != RelicTriggerKind.BlockOnTurnEnd)
                {
                    continue;
                }

                // 파열·한옥 기와·무거운 배낭과 같은 단일 방어막 관문을 지난다 — 우회하면 감산 축이 새 나간다.
                var granted = AddBlockWithRupture(Player, PlayerUnitId, Math.Max(0, item.EffectAmount));
                if (granted > 0)
                {
                    RaiseEffect(EffectKind.Block, PlayerCoord, 0, granted, PlayerUnitId, RelicTriggerSourceRef(item));
                }
            }
        }

        /// <summary>
        /// 수호(T2 페이즈 C) 부여의 단일 관문 — 획득 시(TryGrantPermanentItem)와 20턴 주기가 함께 쓴다.
        /// "최대 1 충전"(사용자 확정)은 여기의 보유 게이트가 강제한다.
        /// </summary>
        private void GrantGuardCharge(int amount, string sourceRef)
        {
            if (HasActiveGuardCharge())
            {
                return;
            }

            var clamped = Math.Max(1, amount);
            // 수명은 턴이 아니라 소비다(OnConsume) — turns 값은 IsExpired(>0)만 만족하면 된다.
            AddDurationStatusEffect(StatusEffectKind.Guard, PlayerUnitId, 1, clamped, sourceRef);
            RaiseStatusEffect(StatusEffectKind.Guard, PlayerCoord, 0, clamped, PlayerUnitId, sourceRef);
        }

        /// <summary>은신(T2 페이즈 C) 부여 — stealth-cycle 트리거가 쓴다. turns = 유물 effectAmount.
        /// ⚠️도깨비 감투 삭제(DEC-2026-08-31-01 D1)로 <b>현재 이 경로를 쓰는 유물은 없다</b> — 트리거는
        /// 레지스트리에 남아 있어 새 유물이 그대로 쓸 수 있다.</summary>
        private void GrantStealth(int turns, string sourceRef)
        {
            var clamped = Math.Max(1, turns);
            // skipNextTick: false — 부여가 플레이어 턴 시작에 일어나므로 유예를 주면 "N턴간"이 N+1턴이 된다.
            AddDurationStatusEffect(StatusEffectKind.Stealth, PlayerUnitId, clamped, 0, sourceRef, skipNextTick: false);
            RaiseStatusEffect(StatusEffectKind.Stealth, PlayerCoord, 0, 0, PlayerUnitId, sourceRef);
        }

        private static string RelicTriggerSourceRef(PlayerPermanentItemState item) => $"relic.{item.Id}";

        public void ResolveMonsterMovement()
        {
            if (Phase != CombatPhase.MonsterMovement || IsTerminal)
            {
                return;
            }

            pendingMonsterActionRecords = BeginMonsterMovementResolution();
            ResolveMonsterMovementForCurrentAction(pendingMonsterActionRecords);
            CompleteMonsterActionResolution(pendingMonsterActionRecords);

            if (CheckTerminalOutcomeStep())
            {
                return;
            }

            SetPhase(CombatPhase.PlayerAction);
        }

        public void ResolveMonsterAction(bool drawPlayerTurnHands = true)
        {
            if (Phase != CombatPhase.MonsterAction || IsTerminal)
            {
                return;
            }

            var statusEffectsBeforeMonsterAction = BeginMonsterAttackResolution();
            // 보스 기믹은 공격 결의 직전에 돈다: 이펙트 버퍼링 창 안이라 RaiseEffect가 연출 타임라인에
            // 자동으로 실리고, 이 창에서 올라간 페이즈의 스탯(강화·최대 체력)이 같은 턴 공격에 즉시 반영된다.
            // 패턴 게이트 해금은 예고와 실제 명중이 어긋나지 않게 다음 턴 계획부터 적용된다
            // (패턴 선택은 플레이어 이동 종료 시점에 이미 커밋되어 있다).
            ResolveBossMechanicsStep();
            // 형상 footprint 정예의 취약 부위(2026-09-03)는 보스 weak-spot 기믹과 같은 자리에서 같은 규칙으로 진행한다.
            AdvanceMonsterWeakSpots();
            // 주기 함정(C-11)은 몬스터 공격 직전에 터진다 — 보스 기믹과 같은 이유로 이 버퍼링 창 안이다.
            ResolvePeriodicTrapsStep();
            var actionRecords = pendingMonsterActionRecords ?? BeginMonsterActionRecords();
            ResolveMonsterAttacksForCurrentAction(actionRecords);
            CompleteMonsterActionResolution(actionRecords);
            pendingMonsterActionRecords = null;

            if (CheckTerminalOutcomeStep())
            {
                return;
            }

            FinishMonsterActionTurnBoundary(statusEffectsBeforeMonsterAction);
            if (CheckTerminalOutcomeStep())
            {
                return;
            }

            if (drawPlayerTurnHands)
            {
                StartPlayerTurn();
            }
        }

        private Dictionary<string, MonsterActionResolutionBuilder> BeginMonsterMovementResolution()
        {
            LastFailureReason = string.Empty;
            RefreshMonsterActivityStatesForAction();
            return BeginMonsterActionRecords();
        }

        private int BeginMonsterAttackResolution()
        {
            LastFailureReason = string.Empty;
            // Status effects present before monsters attack. Anything added during this monster attack phase
            // (e.g. debuffs from monster attacks) is "fresh" and gets a one-turn decrement grace so a
            // duration-1 presence debuff survives into the player's upcoming turn. See ApplyActiveEffectTurnStart.
            return activeEffects.Count;
        }

        private void ResolveMonsterMovementForCurrentAction(IReadOnlyDictionary<string, MonsterActionResolutionBuilder> actionRecords)
        {
            ResolveMonsterMovementStep(actionRecords);
        }

        private void ResolveMonsterAttacksForCurrentAction(IReadOnlyDictionary<string, MonsterActionResolutionBuilder> actionRecords)
        {
            ResolveMonsterAttackStep(actionRecords);
        }

        private void CompleteMonsterActionResolution(IReadOnlyDictionary<string, MonsterActionResolutionBuilder> actionRecords)
        {
            CompleteMonsterActionRecords(actionRecords);
        }

        private void FinishMonsterActionTurnBoundary(int statusEffectsBeforeMonsterAction)
        {
            EndCurrentOverallTurn();
            BeginNextOverallTurn(statusEffectsBeforeMonsterAction);
        }

        private bool playerTurnDrawPending;

        private void EndCurrentOverallTurn()
        {
            OverallTurnEnded?.Invoke(OverallTurnNumber);
            DiscardRemainingHands();
            playerTurnDrawPending = true;
        }

        /// <summary>
        /// 턴 경계. <b>여기 줄 순서가 곧 게임 규칙인 곳이 있다</b> — 각 단계의 「앞이어야 하는 이유 /
        /// 순서 무관」 판정과 그 실증은 <c>docs/core-refactor-plan.md</c> §7(T7-1·T7-2)에 있다.
        ///
        /// 2026-08-31 T7-3에서 인라인 대입과 조건부 블록을 <b>이름 붙인 단계</b>로 포장했다. 동작은
        /// 한 줄도 바뀌지 않았고, 바뀐 것은 이 메서드가 <b>단계 목록으로 읽힌다</b>는 것뿐이다 —
        /// 예전에는 「기 회복」과 「예약 민첩 실현」이 여섯 줄짜리 인라인 코드라 무엇이 한 단계인지
        /// 세는 것부터 사람 몫이었다.
        ///
        /// 🔴 <b>실증된 계약</b>(어기면 아래 테스트가 빨개진다):
        /// <list type="bullet">
        /// <item><c>ApplyPendingSelfImmobilize</c>는 백스톱을 <b>구조적으로 우회</b>한다 —
        /// 정상 경로로 보내면 지난 턴 속박당한 플레이어가 D04 방어를 공짜로 얻는다
        /// (<c>CleanseCardsTests.BookedImmobilizeIgnoresTheControlStatusImmunityWindow</c>).</item>
        /// <item><c>ResetPerTurnSignalsStep</c>은 <c>ApplyActiveEffectTurnStart</c> <b>뒤</b>여야 한다 —
        /// 약오름 적립 판정이 그 안에서 돈다(<c>EnemyGrammarTests.AgitationStacksWhenDetectingAndDefendCardUsed</c>).</item>
        /// <item><c>ClearPlayerBlockStep</c>은 필드 틱 <b>앞</b>이어야 한다 — 뒤면 지난 턴 방어도가
        /// 이번 턴 장판 피해를 흡수한다(<c>TurnSequenceTests.TurnStartFieldDamageResolvesAfterPreviousBlockClears</c>).</item>
        /// </list>
        ///
        /// ⚠️ 반대로 <b>줄 순서가 관찰되지 않는 곳도 있다</b>(T7-2 실증): 예약 소진 두 단계를
        /// <c>ApplyActiveEffectTurnStart</c> 앞으로 옮겨도 아무 테스트도 빨개지지 않는다
        /// (<c>freshStatusStartIndex</c> 유예 때문). 그러니 <b>여기 배치를 근거로 규칙을 추론하지 말 것</b> —
        /// 규칙은 위 테스트들이 들고 있다.
        /// </summary>
        private void BeginNextOverallTurn(int freshStatusStartIndex)
        {
            AdvanceOverallTurnCounterStep();
            ClearPlayerBlockStep();
            ActivatePendingFieldObjects();
            ResolveFieldObjectTickStep();
            ApplyActiveEffectTurnStart(freshStatusStartIndex);
            RefillKiForNewTurnStep();
            TickLimitedRelicTurnsStep();
            ResolveTurnStartRelicTriggers();
            ApplyCarriedMovementBonusStep();
            ApplyPendingSelfImmobilize();
            ApplyPendingProvokeStrength();
            ResetPerTurnSignalsStep();
            AdvanceAndExpirePlayerProps();
            RefreshVisionForNewTurnStep();
            RefreshMonsterIntentStep();
            EnterPlayerMovementPhaseStep();
        }

        // ---------------------------------------------------------------- 턴 경계 단계

        private void AdvanceOverallTurnCounterStep()
        {
            OverallTurnNumber++;
            OverallTurnStarted?.Invoke(OverallTurnNumber);
        }

        /// <summary>
        /// 지난 턴 방어도는 새 턴으로 넘어오지 않는다. 🔴 <b>필드 틱보다 앞</b>이어야 한다 —
        /// 뒤로 가면 지난 턴 방어도가 이번 턴 장판 피해를 흡수한다.
        /// </summary>
        private void ClearPlayerBlockStep()
        {
            Player.ClearBlock();
        }

        /// <summary>
        /// 새 턴 기를 채우고, 예약된 차감분을 한 번 뺀다.
        /// 미련(X06, T2): 지난 턴말 손에 있던 장수만큼 깎인다 — 꺼내면서 비우는 1회성 예약이다.
        /// </summary>
        private void RefillKiForNewTurnStep()
        {
            ActionCostRemaining = Math.Max(0, MaxKi - pending.TakeNextTurnKiPenalty());
        }

        /// <summary>
        /// 청사초롱(T2 페이즈 B): 턴 제한 유물의 남은 턴 감소 — 만료돼도 칩은 남고 효과만 꺼진다.
        /// 🔴 <c>ResolveTurnStartRelicTriggers</c>보다 <b>앞</b>이어야 한다: 이번 턴 만료된 유물이
        /// 턴 시작 훅을 한 번 더 돌면 안 된다.
        /// </summary>
        private void TickLimitedRelicTurnsStep()
        {
            PlayerInventory?.RelicsAndCurses?.TickLimitedRelicTurns();
        }

        /// <summary>
        /// 추진(Momentum)이 이번 턴 이동을 포기하고 넘긴 민첩을 <b>여기서</b> 실현한다 — 1턴짜리
        /// 민첩 상태로 부여해 HUD·만료가 통합 상태이상 배관을 타게 한다(사설 필드로 들고 있지 않는다).
        /// </summary>
        private void ApplyCarriedMovementBonusStep()
        {
            pending.ClearMovementRangeModifier();
            var carried = pending.TakeAgility();
            if (carried.Amount > 0)
            {
                ApplyAgilityToPlayer(carried.Amount, carried.Turns);
            }
        }

        /// <summary>
        /// 「이번 턴」 스코프 값들을 한꺼번에 되돌린다(이동 거리·빠른거북 뽑은 값·행동 카드 수·
        /// 맹호 호리병 공격 보너스·약오름 적립 신호).
        ///
        /// 🔴 <b><c>ApplyActiveEffectTurnStart</c> 뒤여야 한다</b>: 약오름 적립 판정
        /// (<c>TickEnemyGrammarPerTurn</c>)이 그 안에서 돌면서 <c>defensiveCardUsedThisTurn</c>을 읽는다.
        /// 앞으로 옮기면 적립이 판정 전에 지워진다 — 이 순서가 곧 계약이다.
        /// </summary>
        private void ResetPerTurnSignalsStep()
        {
            lastMovedDistance = 0;
            revealedFastTurtleDistance = 0;
            actionCardsUsedThisTurn = 0;
            bagAttackBonusThisTurn = 0;
            defensiveCardUsedThisTurn = false;
        }

        /// <summary>
        /// 새 턴 시야를 다시 만든다. 🔑 <b>지난 턴 정찰 공개를 먼저 버리고</b> 갱신한다 — 순서를
        /// 뒤집으면 정찰로 밝혔다가 더는 안 보이는 칸이 한 턴 더 Revealed로 남는다.
        /// 두 줄을 한 단계로 묶어 둔 이유가 그것이다(따로 두면 사이에 뭔가 끼어들 자리가 생긴다).
        /// </summary>
        private void RefreshVisionForNewTurnStep()
        {
            scoutRevealedThisTurn.Clear();
            RefreshPlayerVision();
        }

        /// <summary>
        /// 플레이어 이동 페이즈로 넘기고, <b>몬스터 행동 단위</b> 플래그를 되돌린다(D02·D05의 피해
        /// 무효 창). ⚠️ 이 리셋은 턴 소진이 아니라 <b>행동 경계</b> 수명이라 예약 소진과 섞지 않는다.
        /// </summary>
        private void EnterPlayerMovementPhaseStep()
        {
            SetPhase(CombatPhase.PlayerMovement);
            pending.ResetPerMonsterAction();
        }

        public void StartPlayerTurn()
        {
            if (IsTerminal || !playerTurnDrawPending)
            {
                return;
            }

            DrawNewTurnHands();
            playerTurnDrawPending = false;
        }

        private void ActivatePendingFieldObjects()
        {
            if (PendingFieldObjects.Objects.Count == 0)
            {
                return;
            }

            var pending = PendingFieldObjects.Objects.ToList();
            PendingFieldObjects.Clear();
            foreach (var fieldObject in pending)
            {
                FieldObjects.Add(fieldObject);
            }

            UpdateOccupancy();
            RefreshPlayerVision();
        }

        private void RefreshMonsterActivityStatesForAction()
        {
            foreach (var monster in monsters.Where(candidate => !candidate.Combatant.IsDead))
            {
                var previous = monster.ActivityState;
                monster.ActivityState = ClassifyMonsterActivity(monster);
                if (previous == MonsterActivityState.Dormant && monster.ActivityState != MonsterActivityState.Dormant)
                {
                    RefreshMonsterTurnPlan(monster);
                }
                else if (monster.ActivityState == MonsterActivityState.Dormant)
                {
                    monster.PendingAttackIntent = false;
                    monster.IntentPredictedMoveCoord = monster.Coord;
                    monster.TurnPlan = MonsterTurnPlan.Inactive(monster.Coord);
                }
            }
        }

        private void CommitMonsterAttackIntentsAfterPlayerMovementEnd()
        {
            foreach (var monster in monsters.Where(candidate => !candidate.Combatant.IsDead))
            {
                if (monster.ActivityState == MonsterActivityState.Dormant || !monster.TurnPlan.IsActive)
                {
                    monster.PendingAttackIntent = false;
                    continue;
                }

                monster.PendingAttackIntent = IsActiveMonsterAttackAreaCoveringPlayer(monster, monster.TurnPlan.PlannedMoveCoord);
            }
        }

        private Dictionary<string, MonsterActionResolutionBuilder> BeginMonsterActionRecords()
        {
            var records = new Dictionary<string, MonsterActionResolutionBuilder>(StringComparer.Ordinal);
            var actionOrderByMonsterId = GetMonsterActionOrder()
                .Select((monster, index) => new { monster.Id, Index = index })
                .ToDictionary(entry => entry.Id, entry => entry.Index, StringComparer.Ordinal);
            foreach (var monster in monsters.Where(candidate => !candidate.Combatant.IsDead))
            {
                actionOrderByMonsterId.TryGetValue(monster.Id, out var actionOrder);
                records[monster.Id] = new MonsterActionResolutionBuilder(
                    monster.Id,
                    monster.ActivityState,
                    monster.Coord,
                    visibilityRuntime.GetVisibility(monster.Coord) == HexCellVisibility.Revealed,
                    actionOrder)
                {
                    // 은신은 안개와 따로 서는 술어라 셀 가시성 세 플래그로는 잡히지 않는다 —
                    // 여기서 한 번 찍어 두지 않으면 시야 안의 은신 몬스터가 「보이는 공격자」로
                    // 분류돼 걷기·휘두름을 연출하고, "기습!"은 반대로 영영 뜨지 않는다(2026-09-01 #1).
                    HiddenByStealthDuringAction = IsMonsterHiddenByStealth(monster),
                };
            }

            return records;
        }

        private void CompleteMonsterActionRecords(IReadOnlyDictionary<string, MonsterActionResolutionBuilder> actionRecords)
        {
            lastMonsterActionRecords.Clear();
            if (actionRecords == null)
            {
                return;
            }

            // Present monsters in the same order the rules resolved them (attack-speed order captured as
            // ActionOrder), not alphabetically by id ??otherwise the animation/effects play in an order
            // unrelated to (and often the reverse of) the numeric turn order. Ties fall back to id for stability.
            foreach (var record in actionRecords.Values.OrderBy(record => record.ActionOrder).ThenBy(record => record.MonsterId))
            {
                var monster = monsters.FirstOrDefault(candidate => candidate.Id == record.MonsterId);
                // 밀어붙이기 전진은 공격 뒤 별도 비트라 afterCoord에 섞지 않는다(AdvancedFrom 주석 참조).
                var afterCoord = record.AdvancedFrom ?? monster?.Coord ?? record.BeforeCoord;
                lastMonsterActionRecords.Add(record.ToRecord(afterCoord, visibilityRuntime.GetVisibility(afterCoord) == HexCellVisibility.Revealed));
            }
        }

        private MonsterActivityState ClassifyMonsterActivity(MonsterRuntime monster)
        {
            if (monster == null || monster.Combatant.IsDead)
            {
                return MonsterActivityState.Dormant;
            }

            // 보스 기물은 움직이지도 공격하지도 않는다. Dormant로 고정하면 계획 레이어가 인텐트·이동·공격을
            // 전부 건너뛰므로(RefreshAllIntents의 Dormant 분기) 기물마다 별도 분기를 심을 필요가 없다.
            if (IsPropMonster(monster))
            {
                return MonsterActivityState.Dormant;
            }

            // 아레나에 들어가기 전의 보스도 같은 자리에 선다(2026-09-02 #4) — 기물과 <b>같은 이유</b>로
            // Dormant다: 계획 레이어가 인텐트·이동·공격을 통째로 건너뛰므로 보스 전용 분기가 필요 없다.
            if (IsBossAwaitingArenaEncounter(monster))
            {
                return MonsterActivityState.Dormant;
            }

            var distance = PlayerCoord.DistanceTo(monster.Coord);
            if (visibilityRuntime.GetVisibility(monster.Coord) == HexCellVisibility.Revealed
                || monster.PendingAttackIntent
                || distance <= Config.EnemyAttackRange + 1
                || distance <= Config.PlayerVisionRange)
            {
                return MonsterActivityState.ActiveThreat;
            }

            var simulatedDistance = System.Math.Max(Config.PlayerVisionRange + Config.EnemyChaseRange, Config.EnemyChaseRange * 2);
            return distance <= simulatedDistance
                ? MonsterActivityState.SimulatedBackground
                : MonsterActivityState.Dormant;
        }


        private void ResolveFieldObjectTickStep()
        {
            if (FieldObjects.Objects.Count == 0)
            {
                return;
            }

            FieldObjects.Tick(fieldObject =>
            {
                ApplyFieldObjectTurnEffect(fieldObject);
                return fieldObject.Tick();
            });
            UpdateOccupancy();
            // 지대가 만료됐을 수 있다 — 「서 있는 집합」이 바뀌는 두 순간 중 하나다(다른 하나는 이동).
            RefreshStandingStatusZoneEffects();
        }

        private void ResolveTrapTriggersAt(HexCoord coord)
        {
            trapResolutionDepth++;
            try
            {
                // 예고형(C-11)은 밟기로 터지지 않는다 — 저작이 triggerOnEnter를 켜 뒀더라도.
                // 두 트리거를 겸하면 "예고를 보고 피한다"는 정체성이 무너지고, 그 조합 자체를
                // ShippingMapTrapAuditTests가 저작 단계에서 막는다.
                foreach (var trap in AllTrapRefs.Where(trap => !trap.IsPeriodic && trap.TriggerOnEnter && trap.Contains(coord)).ToList())
                {
                    if (trap.OneShot && consumedTrapIds.Contains(trap.TrapId))
                    {
                        continue;
                    }

                    // 🔴밟는 순간이 유일한 기회다 — 일회성 함정은 바로 아래에서 발견 표시가 지워지므로
                    // (소진된 함정의 마커를 숨기는 규칙) 페이즈 전이 훑기가 도착할 때는 이미 흔적이 없다.
                    // 안 보고 밟은 함정도 "만난" 것이다.
                    MarkCodexSighting(CodexDomainIds.Trap, trap.PresetId);

                    ApplyTrapEffects(trap);
                    if (trap.OneShot)
                    {
                        consumedTrapIds.Add(trap.TrapId);
                        // A consumed one-shot trap is gone: clear its reveal so the renderer hides the marker.
                        // Data (Map.TrapRefs) is left intact; only the presentation-facing reveal flag is reset.
                        visibilityRuntime.ClearTrapReveal(trap.Coord);
                    }

                    // 텔레포트가 걸렸으면 플레이어는 더 이상 이 칸에 없다 — 남은 함정은 밟은 적이 없으므로
                    // 발동시키지 않는다. 실제 이동은 이 순회를 끝낸 뒤에 실행한다(순회 중 좌표를 바꾸면
                    // 위 Where가 이미 굳힌 "이 칸의 함정" 목록과 어긋난다).
                    if (pendingTeleportDestination.HasValue)
                    {
                        break;
                    }
                }

                UpdateOccupancy();

                if (pendingTeleportDestination.HasValue)
                {
                    var destination = pendingTeleportDestination.Value;
                    pendingTeleportDestination = null;
                    ExecutePlayerTrapTeleport(destination);
                }
            }
            finally
            {
                trapResolutionDepth--;
                if (trapResolutionDepth == 0)
                {
                    teleportResolvedThisTrapCycle = false;
                }
            }
        }

        /// <summary>
        /// 텔레포트 착지 실행(D-3). 착지 칸의 다른 함정은 <b>연쇄 발동을 허용</b>하되, 이 해소 사이클에서
        /// 텔레포트가 이미 한 번 걸렸다면 착지 칸의 텔레포트 함정은 발동하지 않는다(텔레포트→텔레포트만 차단).
        /// 강제 이동 후처리는 <c>CombatState.DebugSandbox.TryDebugMovePlayer</c>의 체크리스트를 따르되
        /// 종료 판정·몬스터 의도 갱신은 호출원(이동/넉백 경로)이 이미 자기 자리에서 하므로 여기서는 돌지 않는다 —
        /// 기존 넉백 경로(<c>TryKnockbackPlayerFromCollision</c>)와 같은 규약이다.
        /// </summary>
        private void ExecutePlayerTrapTeleport(HexCoord destination)
        {
            var origin = PlayerCoord;
            PlayerCoord = destination;
            UpdateOccupancy();
            // 암시야 착지 허용(D-3): 후보 수집이 가시성을 보지 않으므로 여기서 걸러낼 것도 없다.
            // 새 위치 기준으로 안개를 먼저 갱신해야 착지 칸의 연쇄 함정 연출이 보이는 화면 위에서 난다.
            RefreshPlayerVision();
            RaiseEffect(
                EffectKind.Knockback,
                origin,
                0,
                origin.DistanceTo(destination),
                PlayerUnitId,
                TrapTeleportSourceRef,
                targetActorKind: "player");

            ResolveTrapTriggersAt(destination);
            RefreshPlayerVision();
            UpdateOccupancy();
        }

        /// <summary>
        /// 이 함정이 <b>이번 턴 말에 터지는가</b>(C-11 / D-13). 런타임 카운터가 아니라
        /// <see cref="OverallTurnNumber"/>에서 유도한다 — 순수 함수라 세이브에 실을 상태가 없고,
        /// 재개 후 카운터가 어긋나거나 세이브 스컴으로 주기를 밀어낼 표면도 생기지 않는다.
        /// (계획 §7.1-2는 함정별 카운터 + 왕복 필드를 예정했지만, 그 상태는 전부 재유도 가능하다.)
        ///
        /// 소진된 일회성 함정은 무장하지 않는다. 예고와 발동이 <b>같은 술어</b>를 쓰므로
        /// "예고했는데 안 터진다"가 구조적으로 불가능하다.
        /// </summary>
        private bool IsPeriodicTrapArmed(HexTrapData trap)
        {
            return trap.IsPeriodic
                && !(trap.OneShot && consumedTrapIds.Contains(trap.TrapId))
                && OverallTurnNumber % trap.PeriodTurns == 0;
        }

        /// <summary>
        /// 주기 함정 해소(C-11). 몬스터 공격 <b>직전</b>에 돈다: 이펙트 버퍼링 창 안이라 연출이
        /// 타임라인에 자동으로 실리고(보스 기믹과 같은 자리), 플레이어는 자기 턴 내내 예고를 보고
        /// 피할 기회를 이미 다 쓴 뒤다.
        /// </summary>
        private void ResolvePeriodicTrapsStep()
        {
            if (Map.TrapRefs.Count == 0 && runtimeTrapRefs.Count == 0)
            {
                return;
            }

            foreach (var trap in AllTrapRefs.Where(IsPeriodicTrapArmed).ToList())
            {
                ApplyTrapEffects(trap);
                if (trap.OneShot)
                {
                    consumedTrapIds.Add(trap.TrapId);
                    visibilityRuntime.ClearTrapReveal(trap.Coord);
                }
            }
        }

        /// <summary>
        /// 이번 턴 말에 터질 주기 함정이 덮는 칸들(예고 표시용). <b>정찰로 발견된 함정만</b> 예고한다
        /// (사용자 확정) — 일반 함정과 같은 <c>trapRevealed</c> 규칙이라 안개에 구멍을 뚫지 않는다.
        /// 발견 기록은 함정 원점 타일에만 남으므로(<c>RevealTrapsInArea</c>) 반경형은 원점이 발견됐을 때
        /// 반경 전체를 예고한다.
        /// </summary>
        /// <summary>
        /// <b>부서진 땅</b>이 덮은 칸들(2026-09-01 #18). 두억시니의 파열·둔화 지대처럼 지형이 상해
        /// 남아 있는 자리다.
        ///
        /// <para>🔴 이것은 <b>오브젝트가 아니라 지형 상태</b>다(사용자 정정). 종전에는 장판마다 실린더
        /// 또는 프리팹이 칸 <b>위에</b> 서서 「누가 뭘 놓았다」로 읽혔다 — 실제로는 그 칸의 땅이 부서진
        /// 것이고, 그래서 표현도 타일 자신이어야 한다.</para>
        ///
        /// <para>예고(<see cref="GetArmedPeriodicTrapCells"/>)와 달리 <b>페이즈 게이트가 없다</b>:
        /// 예고는 이번 턴의 일이라 지나면 잔상이지만, 부서진 땅은 몇 턴이고 남아 있는 판의 사실이다.</para>
        /// </summary>
        public IReadOnlyList<HexCoord> GetRupturedGroundCells()
        {
            List<HexCoord> cells = null;
            foreach (var fieldObject in FieldObjects.Objects)
            {
                if (fieldObject.Kind != FieldObjectKind.StatusZone || fieldObject.IsExpired)
                {
                    continue;
                }

                foreach (var cell in Map.AllCells)
                {
                    if (!fieldObject.Contains(cell.Coord))
                    {
                        continue;
                    }

                    cells ??= new List<HexCoord>();
                    if (!cells.Contains(cell.Coord))
                    {
                        cells.Add(cell.Coord);
                    }
                }
            }

            return (IReadOnlyList<HexCoord>)cells ?? System.Array.Empty<HexCoord>();
        }

        public IReadOnlyList<HexCoord> GetArmedPeriodicTrapCells()
        {
            List<HexCoord> cells = null;
            foreach (var trap in AllTrapRefs)
            {
                if (!IsPeriodicTrapArmed(trap) || !visibilityRuntime.IsTrapRevealed(trap.Coord))
                {
                    continue;
                }

                cells ??= new List<HexCoord>();
                foreach (var cell in Map.AllCells)
                {
                    if (trap.Contains(cell.Coord))
                    {
                        cells.Add(cell.Coord);
                    }
                }
            }

            return (IReadOnlyList<HexCoord>)cells ?? Array.Empty<HexCoord>();
        }

        private void ApplyTrapEffects(HexTrapData trap)
        {
            var targets = CollectTrapTargets(trap).ToList();
            foreach (var effect in trap.Effects)
            {
                foreach (var target in targets.Where(target => !target.Combatant.IsDead))
                {
                    ApplyTrapEffect(trap, effect, target);
                }

                if (pendingTeleportDestination.HasValue)
                {
                    break;
                }
            }
        }

        private IEnumerable<FieldObjectTarget> CollectTrapTargets(HexTrapData trap)
        {
            if (trap.AffectsPlayer && !Player.IsDead && trap.Contains(PlayerCoord))
            {
                yield return new FieldObjectTarget(Player, PlayerCoord, FieldObjectTargetKind.Player);
            }

            if (!trap.AffectsMonsters)
            {
                yield break;
            }

            foreach (var monster in monsters.Where(monster => !monster.Combatant.IsDead && trap.Contains(monster.Coord)))
            {
                yield return new FieldObjectTarget(monster.Combatant, monster.Coord, FieldObjectTargetKind.Monster);
            }
        }

        private void ApplyTrapEffect(HexTrapData trap, HexTrapEffectData effect, FieldObjectTarget target)
        {
            var sourceRef = $"trap.{trap.TrapId}";
            if (effect.Kind == HexTrapEffectKind.Damage)
            {
                var previousHp = target.Combatant.Hp;
                var blockBefore = target.Combatant.Block;
                var applied = DamageCombatant(target.Combatant, effect.Amount);
                if (applied > 0)
                {
                    RaiseEffect(EffectKind.Damage, trap.Coord, trap.Radius, applied, target.Combatant.Id, sourceRef);
                }
                else if (effect.Amount > 0 && target.Combatant.Block < blockBefore)
                {
                    // Trap damage fully absorbed by Block: surface "방어!" instead of a "-0" hit.
                    RaiseEffect(EffectKind.DamageBlocked, trap.Coord, trap.Radius, 0, target.Combatant.Id, sourceRef);
                }
                if (target.Kind == FieldObjectTargetKind.Monster && previousHp > 0 && target.Combatant.IsDead)
                {
                    var monster = monsters.FirstOrDefault(candidate => candidate.Combatant == target.Combatant);
                    if (monster != null)
                    {
                        ResetDeadMonsterToPatrolIntent(monster);
                    }
                }

                return;
            }

            if (effect.Kind == HexTrapEffectKind.Teleport)
            {
                ApplyTrapTeleportEffect(trap, effect, target);
                return;
            }

            if (effect.Kind == HexTrapEffectKind.SpawnMonsters)
            {
                ApplyTrapSpawnEffect(trap, effect, target);
                return;
            }

            if (effect.Kind == HexTrapEffectKind.InjectStatusCard)
            {
                // D-4: 상태 카드는 플레이어 덱에만 존재한다. 몬스터는 덱이 없으므로 조용히 무시한다.
                if (target.Kind == FieldObjectTargetKind.Player)
                {
                    for (var i = 0; i < Math.Max(0, effect.Amount); i++)
                    {
                        TryInjectStatusCard(effect.StatusCardId);
                    }
                }

                return;
            }

            var kind = ToTrapStatusEffectKind(effect.Kind);
            var remainingTurns = Math.Max(1, effect.DurationTurns);
            MonsterRuntime controlledMonster = null;
            (bool WasMoving, bool WasAttacking) cancelledIntent = (false, false);
            if (target.Kind == FieldObjectTargetKind.Monster && IsMonsterControlStatus(kind))
            {
                controlledMonster = monsters.FirstOrDefault(candidate => candidate.Combatant == target.Combatant);
                cancelledIntent = CaptureMonsterIntentForCancel(controlledMonster);
            }

            // 수호(T2 페이즈 C)가 플레이어 대상 부여를 무효화하면 VFX·제어 후처리도 함께 삼킨다.
            if (!AddDurationStatusEffect(kind, target.Combatant.Id, remainingTurns, effect.Amount, sourceRef))
            {
                return;
            }

            if (controlledMonster != null)
            {
                ApplyControlStatusConstraintToPlan(controlledMonster);
            }

            RaiseStatusEffect(kind, trap.Coord, trap.Radius, effect.Amount, target.Combatant.Id, sourceRef);
            if (controlledMonster != null)
            {
                EmitMonsterIntentCancelText(controlledMonster, cancelledIntent.WasMoving, cancelledIntent.WasAttacking, kind);
            }
        }

        /// <summary>
        /// 텔레포트 함정 효과(C-8 / D-3). <c>effect.Amount</c>는 반경 R이다. 실제 이동은 여기서 하지 않고
        /// <see cref="pendingTeleportDestination"/>에 예약만 한다 — 함정 순회 도중 좌표를 바꾸면 그 순회가
        /// 이미 굳혀 둔 "이 칸의 함정" 목록과 어긋나기 때문이다(<see cref="ResolveTrapTriggersAt"/> 참고).
        /// </summary>
        private void ApplyTrapTeleportEffect(HexTrapData trap, HexTrapEffectData effect, FieldObjectTarget target)
        {
            // D-4: 함정은 플레이어 전용이다. 몬스터 순간이동은 모델링돼 있지 않으므로(의도 예고·경로 계획이
            // 전부 어긋난다) affectsMonsters로 저작되더라도 조용히 무시한다.
            if (target.Kind != FieldObjectTargetKind.Player)
            {
                return;
            }

            // 텔레포트→텔레포트 차단(D-3). 다른 함정과의 연쇄는 허용된다.
            if (teleportResolvedThisTrapCycle || pendingTeleportDestination.HasValue)
            {
                return;
            }

            // 함정 해소는 이동 직후에 불려서 runtimeStates가 아직 이전 위치를 물고 있을 수 있다 —
            // 점유 필터가 한 턴 늦은 판을 보지 않도록 여기서 한 번 굳힌다.
            UpdateOccupancy();
            var candidates = CollectTeleportCandidates(PlayerCoord, effect.Amount);
            if (candidates.Count == 0)
            {
                // 후보 0이면 no-op — 발동 소모(one-shot 소진)는 그대로 유지된다.
                return;
            }

            teleportResolvedThisTrapCycle = true;
            pendingTeleportDestination = candidates[pushRng.Next(candidates.Count)];
        }

        /// <summary>
        /// 스폰 함정(C-7 터렛 + C-10 사이렌 / D-12). <c>effect.Amount</c>는 스폰 수,
        /// <c>effect.MonsterDefinitionId</c>가 무엇을 부를지 정한다 — 터렛과 사이렌의 차이는 전부 저작에 있다.
        ///
        /// 중심은 <b>함정 좌표가 아니라 밟은 칸</b>(플레이어 위치)이다. 반경 있는 함정에서 둘이 갈라지는데,
        /// 위협은 발동시킨 사람 옆에 서야 "밟아서 불렀다"가 읽힌다.
        ///
        /// 자리는 <b>가까운 순서로 결정적으로</b> 고른다(무작위 아님). 스폰 함정은 읽히는 위협이 정체성이고,
        /// 무작위를 넣으면 세이브 재개마다 다른 판이 되는 것이 아니라(스폰된 몬스터는 왕복 저장된다)
        /// 같은 함정이 어디에 적을 놓을지 플레이어가 학습할 수 없게 될 뿐이다.
        /// </summary>
        private void ApplyTrapSpawnEffect(HexTrapData trap, HexTrapEffectData effect, FieldObjectTarget target)
        {
            // D-4: 함정은 플레이어 전용이다. 몬스터가 밟아도 증원이 나오지 않는다.
            if (target.Kind != FieldObjectTargetKind.Player)
            {
                return;
            }

            var requested = Math.Max(0, effect.Amount);
            if (requested == 0 || string.IsNullOrWhiteSpace(effect.MonsterDefinitionId))
            {
                LastTrapSpawnReport = $"trap spawn '{trap.TrapId}': 저작이 비었다(amount {requested}, "
                    + $"definitionId '{effect.MonsterDefinitionId}') — 아무것도 부르지 않았다.";
                return;
            }

            // 함정 해소는 이동 직후에 불려서 runtimeStates가 아직 이전 위치를 물고 있을 수 있다 —
            // 텔레포트 후보 수집과 같은 이유로 여기서 한 번 굳힌다.
            UpdateOccupancy();

            var placed = 0;
            for (var slot = 0; slot < requested; slot++)
            {
                // 매 슬롯마다 다시 모은다: 직전 스폰이 칸을 점유했으므로 후보 집합이 줄어든다.
                var coord = FindNearestTrapSpawnCoord(PlayerCoord);
                if (!coord.HasValue)
                {
                    break;
                }

                if (!TrySpawnMonsterAt(
                        effect.MonsterDefinitionId,
                        coord.Value,
                        MonsterSpawnRoles.TrapSpawn,
                        maxHp: 0,
                        monsterId: null,
                        out _))
                {
                    // 카탈로그에 없는 definitionId 등 — 다음 슬롯을 시도해도 같은 이유로 실패한다.
                    break;
                }

                placed++;
            }

            if (placed > 0)
            {
                TrapMonstersSpawned?.Invoke(trap.TrapId, placed);
            }

            LastTrapSpawnReport = placed == requested
                ? $"trap spawn '{trap.TrapId}': {placed}/{requested} × '{effect.MonsterDefinitionId}' placed."
                : $"trap spawn TRUNCATED '{trap.TrapId}': {placed}/{requested} × '{effect.MonsterDefinitionId}' placed"
                  + $" (반경 {TrapSpawnSearchRadius} 안에 빈 칸이 부족하거나 정의를 찾지 못했다: {LastFailureReason}).";
        }

        /// <summary>
        /// 스폰 함정이 자리를 찾는 반경. 밟은 칸에서 이만큼 안쪽만 본다 — 더 멀리서 나오면 "밟아서 불렀다"가
        /// 읽히지 않고, 그냥 맵 어딘가에 적이 늘어난 것이 된다.
        ///
        /// <b>1 = 플레이어 인접 칸</b>(사용자 확정). 자동 공격 터렛은 사거리가 1~2뿐이라 반경 2에서
        /// 나오면 LV1·LV2가 소환된 자리에서 아무것도 못 하고 굳는다 — 스스로 움직일 수 없으므로
        /// "사거리 밖에 소환됨"은 곧 "영원히 무해함"이다.
        /// </summary>
        private const int TrapSpawnSearchRadius = 1;

        /// <summary>
        /// <paramref name="origin"/>에서 가장 가까운 스폰 가능 칸(동거리는 좌표 순으로 결정적).
        /// 후보가 없으면 null.
        /// </summary>
        private HexCoord? FindNearestTrapSpawnCoord(HexCoord origin)
        {
            HexCoord? best = null;
            var bestDistance = int.MaxValue;
            foreach (var cell in Map.AllCells)
            {
                var distance = origin.DistanceTo(cell.Coord);
                if (distance == 0 || distance > TrapSpawnSearchRadius || distance > bestDistance)
                {
                    continue;
                }

                if (!IsSpawnableCoord(cell.Coord))
                {
                    continue;
                }

                if (distance < bestDistance || !best.HasValue || cell.Coord.CompareTo(best.Value) < 0)
                {
                    best = cell.Coord;
                    bestDistance = distance;
                }
            }

            return best;
        }

        /// <summary>
        /// 반경 <paramref name="radius"/> 안의 착지 가능 칸. 맵 안 + 지형 통행 가능 + 이동 차단 오브젝트 없음 +
        /// 점유되지 않음 + 현재 칸 제외. 점유 판정은 넉백 경로와 같은 <c>runtimeStates</c>를 쓴다 — 플레이어·
        /// 몬스터·필드 오브젝트·보스 결계가 모두 여기 모이므로, 몬스터가 다칸 점유(footprint)를 실제로 쓰게 되는
        /// 날에도 <c>UpdateOccupancy</c> 한 곳만 고치면 이 필터가 따라온다.
        /// 가시성은 보지 않는다 — 암시야 착지는 D-3이 허용한 동작이다.
        /// </summary>
        private List<HexCoord> CollectTeleportCandidates(HexCoord origin, int radius)
        {
            var candidates = new List<HexCoord>();
            if (radius <= 0)
            {
                return candidates;
            }

            foreach (var cell in Map.AllCells)
            {
                if (cell.Coord == origin || origin.DistanceTo(cell.Coord) > radius)
                {
                    continue;
                }

                if (!cell.BaseWalkable
                    || !terrainTraits.IsWalkable(cell.TerrainTypeId)
                    || Map.HasMovementBlockingObject(cell.Coord)
                    || runtimeStates.ContainsKey(cell.Coord))
                {
                    continue;
                }

                candidates.Add(cell.Coord);
            }

            return candidates;
        }

        private void ApplyActiveEffectTurnStart(int freshStartIndex = 0)
        {
            // Decrement pattern cooldowns and control-status immunity first, before any status expiring this
            // boundary registers a fresh immunity window (which must survive into the next monster turn).
            TickControlPacingTimers();

            for (var i = activeEffects.Count - 1; i >= 0; i--)
            {
                var effect = activeEffects[i];
                if (effect.SkipNextTick)
                {
                    activeEffects[i] = effect.WithSkipNextTick(false);
                    continue;
                }

                ApplyActiveEffectTick(effect);

                // 카운터 스택(수호, T2 페이즈 C): 턴으로 만료되지 않는다 — 소비 지점
                // (TryConsumeGuardCharge)이 Amount를 깎아 0이 되는 순간 제거하므로
                // 턴 경계 감소를 통째로 건너뛴다.
                if (StatusEffectInfo.ExpirePolicy(effect.Kind) == StatusEffectExpirePolicy.OnConsume)
                {
                    continue;
                }

                // 적용 턴 유예: 몬스터 행동 중에 걸린 존재형 디버프(기절/속박/둔화 등)는 플레이어의
                // 다음 턴을 한 번은 막아야 하므로 이 경계에서 깎지 않는다. 틱형(중독)은 위에서 이미
                // 이번 경계의 틱을 맞았으므로 유예 없이 정상 감소한다.
                // P1.5(D-6): 이 분기는 `effect.Kind != Poison` 하드코딩이었다 — 이제 저작된
                // expirePolicy가 결정하므로, 두 번째 틱형 상태이상이 생겨도 여기를 고칠 필요가 없다.
                var isFresh = i >= freshStartIndex;
                if (isFresh && StatusEffectInfo.ExpirePolicy(effect.Kind) != StatusEffectExpirePolicy.TurnStartAfterTick)
                {
                    continue;
                }

                var ticked = effect.Tick();
                if (ticked.IsExpired)
                {
                    RaiseStatusEffectExpired(effect);
                    if (string.Equals(effect.TargetUnitId, PlayerUnitId, StringComparison.Ordinal))
                    {
                        // A hard control status just wore off the player: grant a brief re-application immunity
                        // so chained stun/immobilize attacks can't lock the player indefinitely.
                        RegisterPlayerControlStatusImmunity(effect.Kind);
                    }

                    activeEffects.RemoveAt(i);
                    RefreshPlayerVisionIfVisionStatus(effect.Kind, effect.TargetUnitId);
                }
                else
                {
                    // 횃불(C-14 / D-15)은 지속시간과 <b>수치</b>가 함께 깎이는 유일한 상태다 — 매 턴
                    // 반경이 1씩 줄며 타 들어간다. 이건 값 축의 성질이 아니라 이 종류의 수명 규칙이라
                    // valueMode가 아니라 kind로 분기한다(축은 "얼마나 밝은가"만 말한다).
                    if (effect.Kind == StatusEffectKind.TorchLight)
                    {
                        ticked = ticked.WithAmount(Math.Max(0, ticked.Amount - 1));
                        activeEffects[i] = ticked;
                        RefreshPlayerVisionIfVisionStatus(effect.Kind, effect.TargetUnitId);
                        continue;
                    }

                    activeEffects[i] = ticked;
                }
            }

            UpdateOccupancy();
        }

        // Presentation-only: announce that a status effect just expired so the HUD can clear its loop VFX
        // and surface a 해제 label. Resolves the target's tile for placement, falling back to the player.
        private void RaiseStatusEffectExpired(ActiveEffect effect)
        {
            RaiseStatusEffectExpired(effect, string.Empty);
        }

        // sourceRefOverride lets a non-tick removal (정화) tag the event so presentation can branch on it later;
        // empty keeps the effect's own source, which is what the natural wear-off path reports.
        private void RaiseStatusEffectExpired(ActiveEffect effect, string sourceRefOverride)
        {
            // A dead or already-removed target has no body on the board: its status icons and loop VFX were
            // torn down with the unit, so announcing 해제/종료 would only float a label over an empty tile.
            if (!TryResolveCombatant(effect.TargetUnitId, out var target, out var coord)
                || target == null
                || target.IsDead)
            {
                return;
            }

            var center = coord;
            var targetActorKind = string.Equals(effect.TargetUnitId, PlayerUnitId, StringComparison.Ordinal)
                ? "player"
                : monsters.Any(monster => string.Equals(monster.Id, effect.TargetUnitId, StringComparison.Ordinal))
                    ? "monster"
                    : string.Empty;
            RaiseEffect(
                EffectKind.StatusEffectExpired,
                center,
                0,
                effect.Amount,
                effect.TargetUnitId,
                string.IsNullOrWhiteSpace(sourceRefOverride) ? effect.SourceRef : sourceRefOverride,
                targetActorKind: targetActorKind,
                statusKind: effect.Kind);
        }

        private void ApplyActiveEffectTick(ActiveEffect effect)
        {
            if (!TryResolveCombatant(effect.TargetUnitId, out var target, out var coord) || target.IsDead)
            {
                return;
            }

            // P1.5(D-6): 종류가 아니라 값 축으로 분기한다 — 턴당 피해를 주는 상태이상을 추가할 때
            // status_effects.csv에 valueMode=DamagePerTurn만 저작하면 여기 코드는 그대로다.
            switch (StatusEffectInfo.ValueMode(effect.Kind))
            {
                case StatusEffectValueMode.DamagePerTurn:
                    var applied = target.ApplyDamage(effect.Amount);
                    RaiseEffect(EffectKind.Damage, coord, 0, applied, effect.TargetUnitId, effect.SourceRef, statusKind: effect.Kind);
                    if (target.IsDead)
                    {
                        var monster = monsters.FirstOrDefault(candidate => candidate.Combatant == target);
                        if (monster != null)
                        {
                            ResetDeadMonsterToPatrolIntent(monster);
                        }
                    }

                    return;
            }
        }

        private bool TryResolveCombatant(string unitId, out CombatantState combatant, out HexCoord coord)
        {
            if (string.Equals(unitId, PlayerUnitId, StringComparison.Ordinal))
            {
                combatant = Player;
                coord = PlayerCoord;
                return true;
            }

            var monster = monsters.FirstOrDefault(candidate => candidate.Id == unitId || candidate.Combatant.Id == unitId);
            if (monster != null)
            {
                combatant = monster.Combatant;
                coord = monster.Coord;
                return true;
            }

            combatant = null;
            coord = default;
            return false;
        }

        private bool HasActivePlayerEffect(StatusEffectKind kind)
        {
            return HasActiveEffect(PlayerUnitId, kind);
        }

        // Player cannot move while Immobilized (속박) or Stunned (기절).
        private bool IsPlayerMovementBlocked()
        {
            return HasActivePlayerEffect(StatusEffectKind.Immobilize)
                || HasActivePlayerEffect(StatusEffectKind.Stun);
        }

        /// <summary>
        /// 한 유닛에게 걸린 상태이상 중 <paramref name="valueMode"/> 축에 속하는 것들의 Amount 합.
        /// **소비 지점은 종류 이름이 아니라 이 축으로 묻는다** — 같은 축의 상태이상을 새로 추가할 때
        /// (쇠약·허점·횃불) 소비 코드를 건드리지 않아도 되게 만드는 것이 P1.5(D-6)의 목적이다.
        /// 부호는 축 이름이 들고 있다(MoveRangeBonus vs MoveRangePenalty) — 소비 지점이 더할지 뺄지를
        /// 정하므로, 저작이 부호를 뒤집어 규칙을 깨는 경로가 없다.
        /// </summary>
        private int SumActiveEffectAmountByValueMode(string unitId, StatusEffectValueMode valueMode)
        {
            return activeEffects.SumAmountByValueMode(unitId, valueMode);
        }

        private int SumActivePlayerEffectAmountByValueMode(StatusEffectValueMode valueMode)
        {
            return SumActiveEffectAmountByValueMode(PlayerUnitId, valueMode);
        }

        private int SumActivePlayerEffectAmount(StatusEffectKind kind)
        {
            return SumActiveEffectAmount(PlayerUnitId, kind);
        }

        private bool HasActiveEffect(string unitId, StatusEffectKind kind)
        {
            return activeEffects.Has(unitId, kind);
        }

        private int SumActiveEffectAmount(string unitId, StatusEffectKind kind)
        {
            return activeEffects.SumAmount(unitId, kind);
        }

        /// <summary>
        /// 정화: strips every cleansable status effect (<see cref="StatusEffectInfo.IsCleansable"/>) from a unit
        /// and returns how many were removed. Instant and stateless — nothing is left behind to serialize.
        ///
        /// Each removal raises the same <see cref="EffectKind.StatusEffectExpired"/> the natural wear-off path
        /// raises, so HUD icons and loop VFX clear through the existing subscriber with no extra wiring.
        /// Unlike wear-off this grants no re-application immunity: a cleansed player standing on a field object
        /// gets the debuff again next turn (by design).
        /// </summary>
        internal int CleanseStatusEffects(string targetUnitId, string sourceRef = "")
        {
            if (string.IsNullOrEmpty(targetUnitId))
            {
                return 0;
            }

            var removed = 0;
            for (var i = activeEffects.Count - 1; i >= 0; i--)
            {
                var effect = activeEffects[i];
                if (!string.Equals(effect.TargetUnitId, targetUnitId, StringComparison.Ordinal)
                    || !StatusEffectInfo.IsCleansable(effect.Kind))
                {
                    continue;
                }

                activeEffects.RemoveAt(i);
                RaiseStatusEffectExpired(effect, sourceRef);
                RefreshPlayerVisionIfVisionStatus(effect.Kind, effect.TargetUnitId);
                removed++;
            }

            if (removed > 0)
            {
                UpdateOccupancy();
            }

            return removed;
        }

        /// <summary>
        /// 플레이어에게 걸린 <b>특정 종류</b>의 상태이상을 전부 걷어 낸다(야광귀의 반환 뒤끝).
        /// 정화(<see cref="CleanseStatusEffects"/>)와 같은 만료 경로를 지나므로 HUD·VFX 정리에 추가 배선이 없고,
        /// 다른 점은 <b>종류를 지목한다</b>는 것과 정화 가능 여부를 묻지 않는다는 것뿐이다 —
        /// 훔쳐 간 자가 돌려주는 것이라 「정화될 수 있는 상태인가」는 물음이 아니다.
        /// </summary>
        private int RemovePlayerStatusEffectsOfKind(StatusEffectKind kind, string sourceRef)
        {
            var removed = 0;
            for (var i = activeEffects.Count - 1; i >= 0; i--)
            {
                var effect = activeEffects[i];
                if (effect.Kind != kind
                    || !string.Equals(effect.TargetUnitId, PlayerUnitId, StringComparison.Ordinal))
                {
                    continue;
                }

                activeEffects.RemoveAt(i);
                RaiseStatusEffectExpired(effect, sourceRef);
                RefreshPlayerVisionIfVisionStatus(effect.Kind, effect.TargetUnitId);
                removed++;
            }

            return removed;
        }

        internal bool AddDurationStatusEffect(string targetUnitId, StatusEffectKind kind, int turns, int amount, string sourceRef)
        {
            return AddDurationStatusEffect(kind, targetUnitId, turns, amount, sourceRef, skipNextTick: true);
        }

        internal bool AddDurationStatusEffect(StatusEffectKind kind, string targetUnitId, int turns, int amount, string sourceRef)
        {
            return AddDurationStatusEffect(kind, targetUnitId, turns, amount, sourceRef, skipNextTick: true);
        }

        /// <summary>
        /// 반환값 = 실제로 부여됐는가. false는 제어 면역 백스톱 또는 수호(T2 페이즈 C) 무효 —
        /// 부여 직후 상태 VFX를 올리는 호출부는 이 값으로 게이트해야 "걸리지도 않은 상태가
        /// HUD에 뜨는" 거짓 연출이 없다.
        /// </summary>
        internal bool AddDurationStatusEffect(
            StatusEffectKind kind,
            string targetUnitId,
            int turns,
            int amount,
            string sourceRef,
            bool skipNextTick)
        {
            var applied = AddDurationStatusEffectCore(kind, targetUnitId, turns, amount, sourceRef, skipNextTick);
            if (!applied)
            {
                return false;
            }

            ClampStatusEffectStacks(kind, targetUnitId);
            // 시야를 바꾸는 상태이상만 안개를 다시 굳힌다 — 모든 부여마다 전체 셀을 훑지 않기 위한 조건이다.
            RefreshPlayerVisionIfVisionStatus(kind, targetUnitId);
            return true;
        }

        /// <summary>
        /// 시야에 관여하는 상태이상(현재 실명)이 플레이어에게서 붙거나 떨어졌을 때만 안개를 다시 굳힌다.
        /// <see cref="GetEffectivePlayerVisionRange"/>는 호출될 때마다 다시 계산되지만
        /// <c>visibilityRuntime</c>의 임시 공개 집합은 <see cref="RefreshPlayerVision"/>이 돌아야 갱신된다 —
        /// 이 배선이 없으면 실명이 다음 이동/턴 전환 전까지 화면에 반영되지 않는다.
        /// </summary>
        private void RefreshPlayerVisionIfVisionStatus(StatusEffectKind kind, string targetUnitId)
        {
            // 시야 축에 얹히는 상태이상은 전부 여기를 지난다(C-14 횃불이 추가된 지점이 kind 하나뿐인 이유).
            if ((kind == StatusEffectKind.Blind || kind == StatusEffectKind.TorchLight)
                && string.Equals(targetUnitId, PlayerUnitId, StringComparison.Ordinal))
            {
                RefreshPlayerVision();
            }
        }

        private bool AddDurationStatusEffectCore(
            StatusEffectKind kind,
            string targetUnitId,
            int turns,
            int amount,
            string sourceRef,
            bool skipNextTick)
        {
            // Hard control immunity backstop: while the player is immune (just recovered from stun/immobilize),
            // no source — monster attack, trap, or field — may re-apply that control status.
            if (string.Equals(targetUnitId, PlayerUnitId, StringComparison.Ordinal) && IsPlayerImmuneToControlStatus(kind))
            {
                return false;
            }

            // 수호(T2 페이즈 C): 해로운 상태이상(IsCleansable 기준 — 폴라리티는 UI 계약이라
            // 게임플레이 게이트로 쓰지 않는다)이 부여되려는 순간 대상의 충전 1을 소모해 무효화한다.
            // 2026-09-03 유닛 일반화: 종전 플레이어 하드코딩을 걷어 몬스터(보스 guard 기믹)도 같은
            // 관문 하나를 탄다 — 유닛별 분기가 갈라지면 한쪽만 열리는 사고가 난다(DEC-2026-09-02-02 교훈).
            // 이 관문이 소비의 단일 지점이다 — D04/D06의 지연 자기 속박은 ApplyPendingSelfImmobilize가
            // 이 관문을 구조적으로 우회하므로 수호에 막히지 않는다(카드 비용은 무효화 대상이 아님).
            if (StatusEffectInfo.IsCleansable(kind)
                && TryConsumeGuardCharge(targetUnitId, kind, sourceRef))
            {
                return false;
            }

            activeEffects.AddOrMerge(kind, targetUnitId, turns, amount, sourceRef, skipNextTick);
            return true;
        }

        /// <summary>
        /// 수호(T2 페이즈 C) 충전 1 소비 — 대상 유닛 일반화(2026-09-03: 플레이어·몬스터가 같은 관문).
        /// 소비로 0이 되면 상태 자체를 제거하고 해제 이벤트를 올려
        /// HUD 아이콘·루프 VFX가 함께 걷힌다. 부여 관문(AddDurationStatusEffectCore)만 부른다.
        /// 무효화 자체는 "{상태} 무효!" 플로팅(StatusNegated — 사용자 확정)으로 알린다.
        /// </summary>
        private bool TryConsumeGuardCharge(string targetUnitId, StatusEffectKind negatedKind, string negatedSourceRef)
        {
            var index = activeEffects.IndexOfCharged(targetUnitId, StatusEffectKind.Guard);
            if (index < 0)
            {
                return false;
            }

            var guard = activeEffects[index];
            var remaining = guard.Amount - 1;
            if (remaining <= 0)
            {
                activeEffects.RemoveAt(index);
                RaiseStatusEffectExpired(guard);
            }
            else
            {
                activeEffects[index] = guard.WithAmount(remaining);
            }

            RaiseStatusNegated(targetUnitId, negatedKind, negatedSourceRef);
            return true;
        }

        /// <summary>
        /// 수호 무효화 알림 — 어떤 상태가 삼켜졌는지(StatusKind)를 실어 프레젠테이션이
        /// "{상태} 무효!"를 띄운다. sourceRef는 무효화된 부여의 출처를 유지해 사운드/VFX 분류가
        /// 원래 부여와 같은 문맥을 본다. 대상이 몬스터면 그 몬스터의 좌표·actorKind로 띄운다.
        /// </summary>
        private void RaiseStatusNegated(string targetUnitId, StatusEffectKind negatedKind, string sourceRef)
        {
            if (!presentationBuffer.IsBuffering && EffectResolved == null)
            {
                return;
            }

            var isPlayer = string.Equals(targetUnitId, PlayerUnitId, StringComparison.Ordinal);
            var monster = isPlayer
                ? null
                : monsters.FirstOrDefault(candidate => string.Equals(candidate.Id, targetUnitId, StringComparison.Ordinal));
            var resultEvent = new EffectResultEvent(
                EffectKind.StatusNegated,
                targetUnitId: targetUnitId,
                amount: 0,
                appliedAmount: 0,
                center: isPlayer ? PlayerCoord : (monster?.Coord ?? PlayerCoord),
                radius: 0,
                sourceRef: sourceRef,
                targetActorKind: isPlayer ? "player" : "monster",
                statusKind: negatedKind);

            if (presentationBuffer.TryEnqueue(resultEvent))
            {
                return;
            }

            EffectResolved.Invoke(resultEvent);
        }

        /// <summary>
        /// 전투 누적 행동 카드 사용 수(코인 세탁기 카운터). 유물 칩 배지(SidebarRelicCursePanelView)가
        /// 진행도(N/10)를 그리는 데 읽는다 — 사용자 확정(2026-08-06).
        /// </summary>
        internal int TotalActionCardsUsed => totalActionCardsUsed;

        /// <summary>수호 충전 보유 여부 — 수호 부적의 "최대 1 충전" 재충전 게이트가 읽는다.</summary>
        internal bool HasActiveGuardCharge()
        {
            return activeEffects.HasCharged(PlayerUnitId, StatusEffectKind.Guard);
        }

        /// <summary>
        /// 은신(T2 페이즈 C) 활성 여부 — 계획기(<c>MonsterAiPlanner.CreateMonsterFsmContext</c>)가
        /// FSM 컨텍스트의 PlayerHidden에 실어 몬스터 감지 판정을 마스킹한다.
        /// </summary>
        internal bool IsPlayerHiddenFromMonsters =>
            activeEffects.Has(PlayerUnitId, StatusEffectKind.Stealth);

        /// <summary>
        /// 저작된 maxStacks를 넘는 인스턴스를 가장 오래된 것부터 버린다. 현행 정책은 전부 하나로
        /// 병합하므로 <b>이 클램프는 오늘 한 번도 발동하지 않는다</b> — 그게 P1.5의 "배선했지만 동작은
        /// 안 변했다"의 일부다. 다중 인스턴스 정책이 생기는 날 상한을 실제로 강제하는 자리이고,
        /// 그때 저작 실수가 activeEffects를 무한히 불리는 것을 막는다.
        /// </summary>
        private void ClampStatusEffectStacks(StatusEffectKind kind, string targetUnitId)
        {
            var maxStacks = Math.Max(1, StatusEffectInfo.MaxStacks(kind));
            while (true)
            {
                var oldestIndex = activeEffects.IndexOf(targetUnitId, kind);
                if (activeEffects.CountOf(targetUnitId, kind) <= maxStacks || oldestIndex < 0)
                {
                    return;
                }

                activeEffects.RemoveAt(oldestIndex);
            }
        }

        // Block gain reduced by Rupture (파열): each point of active Rupture removes one point of the
        // block about to be gained. Returns the block actually added so callers can report the real value.
        // 유물 BlockGainBonus는 같은 축이며 **파열보다 먼저** 더해진다 — 파열이 먼저면 유물이 파열을 상쇄한
        // 뒤 남은 보너스가 사라져 "방어막 +N"이 파열 중에만 조용히 약해진다. 유물은 플레이어 소유물이므로
        // 플레이어가 방어막을 얻을 때만 적용한다(같은 함수를 몬스터가 쓰게 되어도 새지 않도록).
        /// <summary>
        /// 이 유닛이 방어도 <paramref name="amount"/>를 얻으려 할 때 <b>실제로 얻게 되는 값</b>.
        /// 순수 함수 — 카드 수치 미리보기와 집행(<see cref="AddBlockWithRupture"/>)이 **같은 함수**를
        /// 부르므로 둘이 갈라질 수 없다(몬스터 공격 예고가 <c>ResolveMonsterAttackDamageToPlayer</c>를
        /// 공유하는 것과 같은 수법).
        /// </summary>
        private int ResolveBlockGain(string unitId, int amount)
        {
            var baseAmount = Math.Max(0, amount);
            // 실제로 방어막을 얻는 순간에만 보너스가 붙는다. 0에 더하면 방어막을 주지 않는 카드가
            // 유물 때문에 방어막을 주게 된다.
            var relicBonus = baseAmount > 0 && string.Equals(unitId, PlayerUnitId, StringComparison.Ordinal)
                ? GetPermanentItemEffectTotal(PlayerPermanentItemEffectKind.BlockGainBonus)
                : 0;
            // 무거운 배낭(T2 페이즈 B)의 대가 — 유물 보너스와 같은 게이트(획득량>0·플레이어 한정)로 감산.
            var relicPenalty = baseAmount > 0 && string.Equals(unitId, PlayerUnitId, StringComparison.Ordinal)
                ? GetPermanentItemEffectTotal(PlayerPermanentItemEffectKind.BlockGainPenalty)
                : 0;
            var rupture = SumActiveEffectAmountByValueMode(unitId, StatusEffectValueMode.BlockGainPenalty);
            return Math.Max(0, baseAmount + relicBonus - relicPenalty - rupture);
        }

        internal int AddBlockWithRupture(CombatantState combatant, string unitId, int amount)
        {
            var effective = ResolveBlockGain(unitId, amount);
            combatant.AddBlock(effective);
            return effective;
        }

        // Reflect (반사) 활성 퍼센트 합. 저작 amount(기본 50, 연마 D03+ 75)가 곧 경감·반사 비율이다 —
        // 여러 반사가 겹쳐도 100%를 넘지 않게 소비 지점에서 클램프한다.
        private int GetActiveReflectPercent(string unitId)
        {
            return SumActiveEffectAmountByValueMode(unitId, StatusEffectValueMode.ReflectGate);
        }

        /// <summary>
        /// 들어온 피해에서 반사로 되돌아갈 몫. 퍼센트가 저작값 그대로 소비되는 단일 지점(I-05) —
        /// 50%는 예전 ÷2와 비트 단위로 같고(내림), 연마 75%는 이제 실제로 4→3을 되돌린다.
        /// </summary>
        internal int ComputeReflectedDamage(int incoming)
        {
            if (incoming <= 0)
            {
                return 0;
            }

            var percent = Math.Min(100, Math.Max(0, GetActiveReflectPercent(PlayerUnitId)));
            return incoming * percent / 100;
        }

        internal void ApplyReflectToPlayer(int percent, int durationTurns, string sourceCardId = "")
        {
            var clamped = Math.Max(0, percent);
            var clampedTurns = Math.Max(1, durationTurns);
            // RefreshDuration / maxStacks 1: keep a single Reflect instance for the upcoming monster action.
            activeEffects.ReplaceSingle(StatusEffectKind.Reflect, PlayerUnitId, clampedTurns, clamped, CardEffectRefs.DefendHalfReflect);
            // As with D02 above: carry the card id alongside the behaviour ref so a per-card cue is reachable.
            RaiseStatusEffect(StatusEffectKind.Reflect, PlayerCoord, 0, clamped, PlayerUnitId, CardEffectRefs.DefendHalfReflect, sourceCardId: sourceCardId);
        }

        private void ApplyAgilityToPlayer(int amount, int durationTurns)
        {
            var clamped = Math.Max(0, amount);
            if (clamped <= 0)
            {
                return;
            }

            var clampedTurns = Math.Max(1, durationTurns);
            // RefreshDuration / maxStacks 1: a single Agility instance active for the upcoming player turn.
            activeEffects.ReplaceSingle(StatusEffectKind.Agility, PlayerUnitId, clampedTurns, clamped, CardEffectRefs.MoveDeferredMomentumApply);
            RaiseStatusEffect(StatusEffectKind.Agility, PlayerCoord, 0, clamped, PlayerUnitId, CardEffectRefs.MoveDeferredMomentumApply);
        }

        /// <summary>
        /// Books the self-속박 that D04/D06 charge for their block, to be paid at the start of the next turn.
        /// Repeat bookings merge by taking the longer one (D10) rather than adding up.
        /// </summary>
        internal void SchedulePlayerDelayedImmobilize(int turns)
        {
            pending.BookSelfImmobilize(turns);
        }

        private void ApplyPendingSelfImmobilize()
        {
            if (!pending.TryTakeSelfImmobilize(out var turns))
            {
                return;
            }

            // Deliberately bypasses the control-status immunity backstop in AddDurationStatusEffect. That
            // window exists to stop monsters/traps/fields from chain-locking the player right after a 속박
            // or 기절 wore off — and the wear-off happens in ApplyActiveEffectTurnStart, just above this
            // call, so routing the booked penalty through the normal path would let a player who was
            // immobilized last turn take D04's block for free. This 속박 is self-inflicted, not a chain-lock.
            activeEffects.Add(new ActiveEffect(
                EffectType.Duration,
                StatusEffectKind.Immobilize,
                PlayerUnitId,
                turns,
                StatusEffectInfo.DefaultAmount(StatusEffectKind.Immobilize),
                CardEffectRefs.SelfDelayedImmobilizeApply));
            UpdateOccupancy();
            RaiseStatusEffect(
                StatusEffectKind.Immobilize,
                PlayerCoord,
                0,
                StatusEffectInfo.DefaultAmount(StatusEffectKind.Immobilize),
                PlayerUnitId,
                CardEffectRefs.SelfDelayedImmobilizeApply);
        }

        /// <summary>
        /// 무적(D02·D05)의 HUD 투영(WS-I I-19). 판정 정본은 <c>PendingEffects.IncomingDamageNullifiedThisMonsterAction</c>
        /// 불리언(턴 시작 리셋)이고, 이 상태는 아이콘·툴팁·「무적 종료」 플로팅을 위해 같은 수명
        /// (지속 1 — 다음 턴 시작 경계에서 만료)으로 따라붙는 표시용 그림자다. 자기 버프 직접 추가
        /// 문법(반사·민첩과 동일 — skip 플래그 없음)이라 불리언 리셋과 같은 경계에서 사라진다.
        /// </summary>
        internal void ApplyInvincibleStatusThisTurn(string sourceRef, string sourceCardId)
        {
            activeEffects.ReplaceSingle(StatusEffectKind.Invincible, PlayerUnitId, 1, 0, sourceRef);
            RaiseStatusEffect(StatusEffectKind.Invincible, PlayerCoord, 0, 0, PlayerUnitId, sourceRef, sourceCardId: sourceCardId);
        }

        /// <summary>
        /// D02의 「다음 턴 적 강화」 예약. 반복 예약은 큰 쪽으로 병합한다(지연 자기 속박과 같은 규칙).
        /// </summary>
        internal void ScheduleProvokeStrength(int amount, int durationTurns)
        {
            pending.BookProvokeStrength(amount, durationTurns);
        }

        private void ApplyPendingProvokeStrength()
        {
            if (!pending.TryTakeProvokeStrength(out var amount, out var turns))
            {
                return;
            }

            ApplyStrengthToAllMonsters(amount, turns);
        }

        // Grant 강화 (Strength) to every living monster. Each point is +1% outgoing damage.
        // WS-I I-14(DEC-2026-08-19-08): 가시성(Revealed) 필터를 제거했다 — 문안(「적을 강화」)과
        // 사용자 판정(다음 턴 · 전체)에 맞춰, 안 보이는 몬스터도 도발에 응한다.
        internal void ApplyStrengthToAllMonsters(int amount, int durationTurns)
        {
            var clampedAmount = Math.Max(0, amount);
            var clampedTurns = Math.Max(1, durationTurns);
            foreach (var monster in monsters.Where(candidate => !candidate.Combatant.IsDead))
            {
                // RefreshDuration / maxStacks 1: keep a single Strength instance per monster.
                activeEffects.ReplaceSingle(StatusEffectKind.Strength, monster.Id, clampedTurns, clampedAmount, CardEffectRefs.DefendZeroThenDouble);
                RaiseStatusEffect(StatusEffectKind.Strength, monster.Coord, 0, clampedAmount, monster.Id, CardEffectRefs.DefendZeroThenDouble);
            }
        }

        private void RefreshPlayerVision()
        {
            // Temporary visibility is the union of every live reveal source: the player's own sight
            // plus the footprint of each active field object. Field-object reveals are intentionally
            // temporary (not the monotonic Reveal/SetVisibility path): they hold their tiles Revealed
            // only while the object lives, then decay to Hinted on the first refresh after it expires.
            var visionRange = GetEffectivePlayerVisionRange();
            var revealed = new HashSet<HexCoord>();
            foreach (var cell in Map.AllCells)
            {
                if (PlayerCoord.DistanceTo(cell.Coord) <= visionRange)
                {
                    revealed.Add(cell.Coord);
                }
            }

            foreach (var fieldObject in FieldObjects.Objects)
            {
                if (fieldObject.IsExpired)
                {
                    continue;
                }

                foreach (var cell in Map.AllCells)
                {
                    if (fieldObject.Contains(cell.Coord))
                    {
                        revealed.Add(cell.Coord);
                    }
                }
            }

            // Scout reveals from this turn are a live source too, so a scouted tile holds Revealed until the
            // turn ends (scoutRevealedThisTurn is cleared in BeginNextOverallTurn) instead of decaying on the
            // next refresh.
            foreach (var coord in scoutRevealedThisTurn)
            {
                revealed.Add(coord);
            }

            visibilityRuntime.RefreshTemporaryRevealedCells(revealed);

            // 골목 CCTV(T2 페이즈 B): 시야에 들어온 함정을 자동 발각한다. 정찰 발각과 같은 배관을
            // 타므로 예고·툴팁·해체(S05)가 전부 그대로 이어진다.
            if (GetPermanentItemEffectTotal(PlayerPermanentItemEffectKind.TrapAutoReveal) > 0)
            {
                foreach (var trap in AllTrapRefs)
                {
                    if (revealed.Contains(trap.Coord))
                    {
                        visibilityRuntime.RevealTrapAt(trap.Coord);
                    }
                }
            }
        }

        // Records the cells a Scout card just revealed so RefreshPlayerVision keeps re-asserting them as
        // Revealed for the rest of the turn (mirrors how field objects are unioned in). Cleared at the start
        // of the next overall turn.
        private void RecordScoutRevealedCells(HexCoord center, int radius)
        {
            if (radius < 0 || !Map.Contains(center))
            {
                return;
            }

            foreach (var cell in Map.AllCells)
            {
                if (center.DistanceTo(cell.Coord) <= radius)
                {
                    scoutRevealedThisTurn.Add(cell.Coord);
                }
            }
        }

        /// <summary>
        /// 플레이어 시야 반경의 단일 산출 지점. 유물 보너스(+)와 실명(−)이 같은 축에서 합산되고
        /// 0으로 클램프된다. 실명은 지속시간형 상태이상이므로 부여·만료·정화 시 반드시
        /// <see cref="RefreshPlayerVision"/>이 함께 돌아야 안개가 즉시 따라온다.
        /// </summary>
        private int GetEffectivePlayerVisionRange()
        {
            return Math.Max(
                0,
                Config.PlayerVisionRange
                + GetPermanentItemEffectTotal(PlayerPermanentItemEffectKind.VisionRangeBonus)
                // 횃불(C-14)과 실명(C-1)은 같은 축의 부호 반대 항이다 — 축 이름이 부호를 들고 있으므로
                // 저작 amount로 뒤집을 수 없고, 둘이 동시에 걸리면 자연스럽게 상쇄된다.
                + SumActivePlayerEffectAmountByValueMode(StatusEffectValueMode.VisionRangeBonus)
                - SumActivePlayerEffectAmountByValueMode(StatusEffectValueMode.VisionRangePenalty)
                // 악몽(X05, T2): 손에 있는 동안 시야 −1 — 정전(C-16)과 같은 "손에 있는 동안" 규약.
                - CountHandCurse(CardIds.Nightmare));
        }

        /// <summary>
        /// 함정 효과가 어떤 상태이상으로 해소되는지 묻는다. 상태이상이 아닌 효과
        /// (<see cref="HexTrapEffectKind.Damage"/>·<see cref="HexTrapEffectKind.Teleport"/> 등)와
        /// 폐기값 <see cref="HexTrapEffectKind.Burn"/>에서는 <see langword="false"/>를 돌려준다.
        /// <para>
        /// 집행은 이 대응이 반드시 있어야 하므로 <see cref="ToTrapStatusEffectKind"/>가 감싸서 던지고,
        /// <b>물어만 보는 쪽</b>(도감 함정 도메인이 어떤 상태이상 아이콘을 쓸지 고를 때)은 이걸 쓴다 —
        /// 판정을 밖에서 다시 짜면 아이콘과 실제 효과가 갈라진다.
        /// </para>
        /// </summary>
        public static bool TryGetTrapStatusEffectKind(HexTrapEffectKind kind, out StatusEffectKind statusKind)
        {
            switch (kind)
            {
                case HexTrapEffectKind.Poison:
                    statusKind = StatusEffectKind.Poison;
                    return true;
                case HexTrapEffectKind.Stun:
                    statusKind = StatusEffectKind.Stun;
                    return true;
                case HexTrapEffectKind.Slow:
                    statusKind = StatusEffectKind.Slow;
                    return true;
                case HexTrapEffectKind.VisionDown:
                    statusKind = StatusEffectKind.Blind;
                    return true;
                case HexTrapEffectKind.Weaken:
                    statusKind = StatusEffectKind.Weaken;
                    return true;
                case HexTrapEffectKind.Disarm:
                    statusKind = StatusEffectKind.Disarm;
                    return true;
                default:
                    statusKind = default;
                    return false;
            }
        }

        internal static StatusEffectKind ToTrapStatusEffectKind(HexTrapEffectKind kind)
        {
            if (TryGetTrapStatusEffectKind(kind, out var statusKind))
            {
                return statusKind;
            }

            // Burn은 D-1로 제거된 유령 값이다(대응 StatusEffectKind 없음). 저작 표면에서는
            // 빠졌지만 직렬화 값 유지를 위해 enum 멤버가 남아 있으므로 여기로 떨어질 수 있다.
            // Damage/Teleport는 상태이상이 아니라 ApplyTrapEffect가 앞단에서 처리한다.
            throw new NotSupportedException(
                $"Trap effect kind '{kind}' is not a supported status effect. "
                + "(Burn은 D-1로 제거됨 — 맵 저작에서 Poison 등으로 교체할 것.)");
        }

        private static FieldObjectKind ToRuntimeFieldObjectKind(CardFieldObjectKind kind)
        {
            switch (kind)
            {
                case CardFieldObjectKind.FogReveal:
                    return FieldObjectKind.FogReveal;
                case CardFieldObjectKind.FieldDamage:
                    return FieldObjectKind.FieldDamage;
                case CardFieldObjectKind.ConditionalHeal:
                    return FieldObjectKind.ConditionalHeal;
                case CardFieldObjectKind.MassImmobilize:
                    return FieldObjectKind.MassImmobilize;
                case CardFieldObjectKind.LifestealDamage:
                    return FieldObjectKind.LifestealDamage;
                default:
                    throw new NotSupportedException($"Card field object kind '{kind}' is not supported by CombatState.");
            }
        }

        private bool CheckTerminalOutcomeStep()
        {
            if (!Player.IsDead)
            {
                return false;
            }

            SetPhase(CombatPhase.Defeat);
            return true;
        }

        // Single transition point for Phase so presentation-only consumers (turn-phase dock) receive a
        // PhaseChanged notification on every real transition. The initial phase set in the constructor
        // stays a direct assignment (no listeners exist yet at construction time).
        private void SetPhase(CombatPhase next)
        {
            var previous = Phase;
            if (previous == next)
            {
                return;
            }

            Phase = next;
            // 도감 해금(P3)의 단일 수집 지점. 페이즈가 바뀌는 순간이 "판이 한 번 정돈된" 시점이라
            // 손패·시야·발견 함정이 모두 확정돼 있다 — CombatState.CodexSightings 참조.
            SweepCodexSightings();
            PhaseChanged?.Invoke(previous, next);
        }

        private bool UsesApprovedCardCatalog()
        {
            return string.Equals(CardCatalog.SourceId, ApprovedCardCatalogFactory.SourceId, StringComparison.Ordinal);
        }

        private bool HasPlayableMovementCard()
        {
            return MovementDeck.Hand.Any(card =>
                card.EffectType == CardEffectType.Move
                && GetRequiredKi(card) <= ActionCostRemaining);
        }

        private bool IsMomentumMovementLocked()
        {
            return false;
        }

        private void PrepareActionHandForPlayerActionPhase()
        {
            // Movement and action hands are drawn together at turn start.
            // Entering the action phase must not top up or replace the already-drawn action hand.
        }

        private int CountPlayerActionPhaseCards()
        {
            return ActionDeck.Hand
                .Concat(ActionDeck.DrawPile)
                .Concat(ActionDeck.DiscardPile)
                .Count(IsPlayerActionPhaseCard);
        }

        private int CountPlayerActionPhaseCardsInHand()
        {
            return ActionDeck.Hand.Count(IsPlayerActionPhaseCard);
        }

        private static bool IsPlayerActionPhaseCard(CardDefinition card)
        {
            return card != null && card.PhaseAvailability != CardUsePhase.Movement;
        }

        /// <summary>
        /// 표식 배율을 <b>소모하지 않고</b> 읽는다. 카드 수치 미리보기 전용 —
        /// <see cref="ConsumeMarkMultiplier"/>는 읽는 순간 표식을 지우므로 미리보기가 부르면
        /// 카드를 쓰기도 전에 표식이 사라진다.
        /// </summary>
        private int PeekMarkMultiplier(MonsterRuntime monster)
        {
            return markedMonster != null && ReferenceEquals(markedMonster, monster) ? 2 : 1;
        }

        private int ConsumeMarkMultiplier(MonsterRuntime monster)
        {
            var multiplier = PeekMarkMultiplier(monster);
            if (multiplier > 1)
            {
                markedMonster = null;
            }

            return multiplier;
        }

        private static bool HasAdditionalCost(CardDefinition card, string costId)
        {
            if (card == null)
            {
                return false;
            }

            return string.Equals(AdditionalCostOf(card), costId, StringComparison.Ordinal);
        }

        private bool ValidateAdditionalCosts(
            CardDefinition card,
            IReadOnlyList<CardDefinition> selectedCards,
            out string failureReason)
        {
            failureReason = string.Empty;
            if (!RequiresSelectedHandCards(card))
            {
                return true;
            }

            if (selectedCards == null || selectedCards.Count == 0)
            {
                failureReason = "At least one card must be selected as sacrifice.";
                return false;
            }

            var availableHand = ActionDeck.Hand.Where(c => !ReferenceEquals(c, card)).ToList();
            foreach (var selectedCard in selectedCards)
            {
                if (!availableHand.Remove(selectedCard))
                {
                    failureReason = $"Sacrifice card '{selectedCard?.Id}' not found in action hand.";
                    return false;
                }
            }

            return true;
        }

        private void PayAdditionalCosts(CardDefinition card, IReadOnlyList<CardDefinition> selectedCards)
        {
            if (!RequiresSelectedHandCards(card)
                || selectedCards == null)
            {
                return;
            }

            // Exile cost permanently removes the selected cards (소멸); the default discard sends them to the
            // discard pile.
            var exile = HasAdditionalCost(card, CardBehaviorMetadata.AdditionalCostExileSelectedHandCards);
            foreach (var selectedCard in selectedCards)
            {
                if (exile)
                {
                    ActionDeck.PermanentRemoveFromHand(selectedCard);
                }
                else
                {
                    ActionDeck.DiscardFromHand(selectedCard);
                }
            }

            if (selectedCards.Count > 0)
            {
                HandCardsDiscarded?.Invoke(selectedCards.Count);
            }
        }

        // A sacrifice/exile attack requires the player to pick other hand cards to spend as its cost.
        private static bool RequiresSelectedHandCards(CardDefinition card)
        {
            return HasAdditionalCost(card, CardBehaviorMetadata.AdditionalCostDiscardSelectedHandCards)
                || HasAdditionalCost(card, CardBehaviorMetadata.AdditionalCostExileSelectedHandCards);
        }

        public bool RequiresSelectedHandCards(string cardId)
        {
            return RequiresSelectedHandCards(FindActionCard(CardEffectType.Attack, cardId));
        }

        public bool UsesExileSelectedHandCards(string cardId)
        {
            return HasAdditionalCost(FindActionCard(CardEffectType.Attack, cardId), CardBehaviorMetadata.AdditionalCostExileSelectedHandCards);
        }

        /// <summary>
        /// cards.csv `stateEffect` 컬럼(kind:amount;…)의 범용 부여 지점(T1, 2026-08-06). 공격 카드가
        /// 겨눈 칸의 몬스터에게 저작된 상태이상을 건다 — A11의 ApplyImmobilize post-action과 같은
        /// 계약(대상 칸 하나, 광역 확산 없음)을 kind 전반으로 일반화한 것. 하드 CC(속박·기절)는
        /// 함정과 같은 처방(계획 제약 + 인텐트 취소 큐)을 태운다. 지속은 duration 컬럼(하한 1턴).
        /// 허용 kind는 임포터의 StateEffectKinds 허용목록이 걸러 준다. 첫 소비 카드=A14 으름장(Weaken:30).
        /// </summary>
        private void ApplyCardStateEffects(CardDefinition card, HexCoord target)
        {
            var entries = CardBehaviorMetadata.ParseEffectList(card?.StateEffect);
            if (entries.Count == 0)
            {
                return;
            }

            var monster = FindLivingMonsterAt(target);
            if (monster == null)
            {
                return;
            }

            var turns = Math.Max(1, card.DurationTurns);
            foreach (var entry in entries)
            {
                if (!Enum.TryParse<StatusEffectKind>(entry.Kind, true, out var kind))
                {
                    continue;
                }

                var isControl = kind == StatusEffectKind.Immobilize || kind == StatusEffectKind.Stun;
                var intent = isControl ? CaptureMonsterIntentForCancel(monster) : default;
                AddDurationStatusEffect(kind, monster.Id, turns, entry.Amount, EffectSourceRefOf(card));
                if (isControl)
                {
                    ApplyControlStatusConstraintToPlan(monster);
                }

                // I-13(WS-I): 플로팅의 수치 슬롯 의미는 종류별이다 — 제어형(속박/기절)은 「N턴」이라 턴을,
                // 값형(파열·쇠약 등)은 수치를 싣는다. 예전엔 전부 turns를 실어 값형이 턴수를 수치처럼 찍었다.
                RaiseStatusEffect(kind, target, 0, isControl ? turns : entry.Amount, monster.Id, EffectSourceRefOf(card), sourceCardId: card.Id);
                if (isControl)
                {
                    EmitMonsterIntentCancelText(monster, intent.WasMoving, intent.WasAttacking, kind);
                }
            }
        }

        private void ApplyPostActions(CardDefinition card, HexCoord target)
        {
            ApplyCardStateEffects(card, target);
            foreach (var action in ResolvePostActions(card))
            {
                if (string.Equals(action.ActionId, CardBehaviorMetadata.PostActionApplyMark, StringComparison.Ordinal))
                {
                    markedMonster = FindLivingMonsterAt(target);
                    continue;
                }

                if (string.Equals(action.ActionId, CardBehaviorMetadata.PostActionApplyImmobilize, StringComparison.Ordinal))
                {
                    // A11 속박: immobilize the targeted monster for the payload's turn count, mirroring the
                    // debug.bind path (constraint refresh + intent-cancel cue).
                    var immobilizeTarget = FindLivingMonsterAt(target);
                    if (immobilizeTarget != null && int.TryParse(action.Payload, out var turns) && turns > 0)
                    {
                        var intent = CaptureMonsterIntentForCancel(immobilizeTarget);
                        AddDurationStatusEffect(StatusEffectKind.Immobilize, immobilizeTarget.Id, turns, 0, EffectSourceRefOf(card));
                        ApplyControlStatusConstraintToPlan(immobilizeTarget);
                        // sourceRef stays the shared behaviour ref (attack.damage), which every attack card
                        // emits; sourceCardId names the card so presentation can scope a cue to it.
                        RaiseStatusEffect(
                            StatusEffectKind.Immobilize, target, 0, turns, immobilizeTarget.Id, EffectSourceRefOf(card),
                            sourceCardId: card.Id);
                        EmitMonsterIntentCancelText(immobilizeTarget, intent.WasMoving, intent.WasAttacking, StatusEffectKind.Immobilize);
                    }

                    continue;
                }

                if (string.Equals(action.ActionId, CardBehaviorMetadata.PostActionSpreadStatus, StringComparison.Ordinal))
                {
                    SpreadTargetDebuffsToNeighbours(
                        card,
                        target,
                        int.TryParse(action.Payload, out var radius) && radius > 0 ? radius : 1);
                    continue;
                }

                if (string.Equals(action.ActionId, CardBehaviorMetadata.PostActionInjectCopy, StringComparison.Ordinal))
                {
                    // Only the permanent original (e.g. A08) spawns a copy. Temporary copies
                    // (A09) must not re-copy themselves, otherwise the effect chains forever.
                    if (!card.IsTemporary)
                    {
                        InjectCardCopy(action.Payload);
                    }
                }
            }
        }

        private static bool IsMultiplyingStrikeCard(CardDefinition card)
        {
            return card != null &&
                (card.Id == CardIds.MultiplyingStrike || card.Id == CardIds.MultiplyingStrikeCopy);
        }

        internal int CountMultiplyingStrikeCards()
        {
            return ActionDeck.Hand.Count(IsMultiplyingStrikeCard)
                + ActionDeck.DrawPile.Count(IsMultiplyingStrikeCard)
                + ActionDeck.DiscardPile.Count(IsMultiplyingStrikeCard);
        }

        private static IReadOnlyList<CardBehaviorMetadata.PostAction> ResolvePostActions(CardDefinition card)
        {
            if (card == null)
            {
                return Array.Empty<CardBehaviorMetadata.PostAction>();
            }

            // 등록된 카드는 클래스가 선언한 후속 규칙, 카탈로그 밖 카드(테스트 픽스처)는 아직 문자열 컬럼(P2-d에서 제거).
            return CardBehaviorRegistry.TryGet(card.Id, out var behavior)
                ? behavior.PostActions
                : CardBehaviorMetadata.ParsePostActions(card.PostActions);
        }

        private void InjectCardCopy(string payload)
        {
            // 옛 postActions 문법 `InjectCopy:attack.multiplying_strike`는 복사본 카드 id(A09)로 읽는다.
            var copyKey = string.IsNullOrWhiteSpace(payload) || payload.Trim() == CardEffectRefs.AttackMultiplyingStrike
                ? CardIds.MultiplyingStrikeCopy
                : payload.Trim();
            var copyEntry = CardCatalog.Entries
                .FirstOrDefault(e => !e.IncludeInGameplayDecks && string.Equals(e.Id, copyKey, StringComparison.Ordinal));
            if (copyEntry != null)
            {
                ActionDeck.InjectIntoDrawPile(copyEntry.ToCardDefinition(
                    CardCatalog.SourceId,
                    CreateRuntimeInstanceId(copyEntry.Id, "copy"),
                    isTemporary: true));
            }
        }

        private void PurgeCopiesFromActionHand()
        {
            // 아지랑이(T2 키워드): 임시 카드는 턴 끝에 손패에서 소멸 더미로 간다. A09 복사본·미세먼지가
            // 원형이고, 도깨비 장난(X11)부터는 effectRef 목록 대신 IsTemporary 단일 술어가 규칙이다 —
            // 임시 플래그는 주입 경로만 세우므로(IsEphemeralCurseRef·복사 주입) 목록과 동치다.
            var copies = ActionDeck.Hand
                .Where(card => card != null && card.IsTemporary)
                .ToList();
            foreach (var copy in copies)
            {
                ActionDeck.PermanentRemoveFromHand(copy);
            }
        }

        /// <summary>
        /// 깨진 유리(C-17): 턴 종료 시 손패에 남아 있으면 피해. <b>버리면 아프지 않다</b> —
        /// "버릴 것인가, 쓸 카드를 포기할 것인가"가 이 카드의 전부이므로 무조건 피해로 만들면 선택이 사라진다.
        /// 소멸형(미세먼지)과 달리 덱에 남아 전투 내내 다시 돌아온다.
        /// </summary>
        private void ResolveStatusCardTurnEndDamage()
        {
            var total = 0;
            foreach (var card in ActionDeck.Hand.Concat(MovementDeck.Hand))
            {
                if (card != null && string.Equals(card.Id, CardIds.BrokenGlass, StringComparison.Ordinal))
                {
                    total += Math.Max(0, card.Amount);
                }
            }

            if (total <= 0)
            {
                return;
            }

            var applied = Player.ApplyDamage(total);
            if (applied > 0)
            {
                RaiseEffect(EffectKind.Damage, PlayerCoord, 0, applied, PlayerUnitId, CardEffectRefs.StatusBrokenGlass, targetActorKind: "player");
            }
        }

        /// <summary>
        /// T2 저주 카드의 턴말 효과 3종 — 깨진 유리와 같은 "턴 끝에 손에 남아 있었는가" 규약.
        /// 미련은 부여가 아니라 카운터 예약이라 정화 대상이 아니고(지연 예약 D6과 같은 결),
        /// 부적·안개는 평범한 자기 디버프라 걸린 뒤에는 정화로 풀린다.
        /// </summary>
        private void ResolveCurseCardTurnEndEffects()
        {
            // 미련(X06)은 더 이상 턴말 트리거가 아니다(2026-08-20 #18) — 「뽑을 시 사용 가능한 기력
            // 1 감소」로 즉시성이 옮겨졌고, 집행은 드로우 이음새(DrawActionCards)에 있다.

            // 원귀(X12): 손에 있는 동안 허점 1턴. 부적·안개(X09·X10)와 같은 Refresh 병합 규약이라
            // 장수와 무관하게 1회 부여된다. 종전의 「받는 피해 flat +1」은 산식에서 제거했다 —
            // 두 축을 함께 두면 같은 저주가 두 번 세진다.
            if (CountHandCurse(CardIds.VengefulGhost) > 0
                && AddDurationStatusEffect(
                    StatusEffectKind.Vulnerable, PlayerUnitId, 1,
                    StatusEffectInfo.DefaultAmount(StatusEffectKind.Vulnerable), CardEffectRefs.StatusVengefulGhost))
            {
                RaiseStatusEffect(StatusEffectKind.Vulnerable, PlayerCoord, 0, 1, PlayerUnitId, CardEffectRefs.StatusVengefulGhost);
            }

            // 부정 탄 부적(X09)·궂은 안개(X10): Refresh 병합이라 장수와 무관하게 1회 부여.
            if (CountHandCurse(CardIds.CursedCharm) > 0
                && AddDurationStatusEffect(
                    StatusEffectKind.Weaken, PlayerUnitId, 1,
                    StatusEffectInfo.DefaultAmount(StatusEffectKind.Weaken), CardEffectRefs.StatusCursedCharm))
            {
                // 수호(T2 페이즈 C)가 무효화하면 부여 VFX도 함께 삼킨다 — 반환값 게이트.
                RaiseStatusEffect(StatusEffectKind.Weaken, PlayerCoord, 0, 1, PlayerUnitId, CardEffectRefs.StatusCursedCharm);
            }

            if (CountHandCurse(CardIds.MurkyFog) > 0
                // 시야 갱신은 AddDurationStatusEffect 내부(RefreshPlayerVisionIfVisionStatus)가 처리한다.
                && AddDurationStatusEffect(
                    StatusEffectKind.Blind, PlayerUnitId, 1,
                    StatusEffectInfo.DefaultAmount(StatusEffectKind.Blind), CardEffectRefs.StatusMurkyFog))
            {
                RaiseStatusEffect(StatusEffectKind.Blind, PlayerCoord, 0, 1, PlayerUnitId, CardEffectRefs.StatusMurkyFog);
            }
        }

        /// <summary>
        /// 상태 카드를 이번 전투의 행동 덱에 밀어 넣는다(C-17). A09 <c>InjectCardCopy</c>가 깔아 둔 배관을
        /// 그대로 쓴다: 카탈로그의 <c>includeInDecks=FALSE</c> 엔트리를 뽑아 임시 인스턴스로 주입한다.
        ///
        /// ⚠️소멸형만 <paramref name="temporary"/>다. 깨진 유리·정전은 전투 내내 순환해야 하므로
        /// 임시로 넣으면 턴 끝에 조용히 사라져 아무 일도 일어나지 않는다.
        /// </summary>
        /// <summary>저주 주입 신호의 source ref(2026-09-01 #9). 표현층이 이 ref로 연출을 고른다.</summary>
        public const string StatusCardInjectionRef = "deck.status_card_injected";

        private bool TryInjectStatusCard(string statusCardId)
        {
            if (string.IsNullOrWhiteSpace(statusCardId))
            {
                return false;
            }

            var entry = CardCatalog.Entries.FirstOrDefault(candidate =>
                !candidate.IncludeInGameplayDecks
                && string.Equals(candidate.Id, statusCardId, StringComparison.Ordinal));
            if (entry == null)
            {
                return false;
            }

            var temporary = IsEphemeralCurseCard(entry.Id);
            ActionDeck.InjectIntoDrawPile(entry.ToCardDefinition(
                CardCatalog.SourceId,
                CreateRuntimeInstanceId(entry.Id, "status"),
                isTemporary: temporary));

            // 🔴 종전에는 여기서 <b>아무 신호도 나가지 않았다</b>(2026-09-01 #9) — 덱만 조용히
            //    오염되고, 플레이어는 몇 턴 뒤 그 카드를 뽑고 나서야 무슨 일이 있었는지 알았다.
            //    원인(때린 놈)과 결과(덱에 든 저주)가 화면에서 이어지지 않는 것이 결함의 전부다.
            //    카드 id를 실어 보내면 표현층이 그 카드를 보여주고 더미로 빨아들일 수 있다.
            RaiseEffect(
                EffectKind.StatusCardInjected,
                PlayerCoord,
                0,
                1,
                PlayerUnitId,
                StatusCardInjectionRef,
                sourceCardId: entry.Id);
            return true;
        }

        private static bool HasChoiceOption(CardDefinition card, string optionId)
        {
            return TryResolveChoiceOption(card, optionId, out _);
        }

        private static bool TryResolveChoiceOption(
            CardDefinition card,
            string optionId,
            out CardBehaviorMetadata.ChoiceOption option)
        {
            option = default;
            if (card == null)
            {
                return false;
            }

            foreach (var choice in ChoicesOf(card))
            {
                if (string.Equals(choice.OptionId, optionId, StringComparison.OrdinalIgnoreCase))
                {
                    option = choice;
                    return true;
                }
            }

            return false;
        }

        private int GetEffectiveMoveRange(CardDefinition moveCard)
        {
            if (IsPlayerMovementBlocked()) return 0;
            // X코스트 이동(T5-3 전력 질주): SpendAll 이동 카드의 기본 거리는 저작 range가 아니라
            // 남은 기 전부다. 미리보기(도달 가능 칸)와 집행이 같은 이 함수를 지나므로 갈라질 수 없고,
            // 기 차감은 목적지 확정 뒤 SpendCardKi가 한다 — 여기서 읽는 값은 차감 전 잔량이다.
            var baseRange = moveCard != null && moveCard.CostMode == CardCostMode.SpendAll
                ? ActionCostRemaining
                : (moveCard?.Range ?? Config.PlayerMovePoints);
            return ResolveMoveRange(baseRange);
        }

        private int GetEffectiveAreaMoveRange(CardDefinition moveCard)
        {
            return ResolveMoveRange(moveCard?.AreaRadius ?? 0);
        }

        /// <summary>
        /// 🔴 <b>플레이어 이동 사거리를 계산하는 유일한 곳.</b> 기준값만 다르고 보정은 전부 같으므로
        /// 사거리를 묻는 모든 경로가 여기를 지난다 — 미리보기(도달 가능 칸)와 집행(실제 이동)이
        /// 같은 함수를 지나야 "미리 본 거리와 실제로 갈 수 있는 거리가 다르다"가 생길 수 없다.
        ///
        /// 왜 하나로 합쳤나(2026-08-31 T2): 항이 여섯 개인 같은 식이 글자 그대로 <b>네 번</b>
        /// 복사돼 있었다(기본 이동 · 빠른거북 미리보기 · 빠른거북 집행 · 이동 카드 AreaRadius).
        /// 밸런스 조정으로 항이 하나 늘 때 넷 중 셋만 고쳐도 컴파일된다 — 증상은 조용하고,
        /// "미리보기와 실제 이동 거리가 다르다"로만 나타난다. 사본이 없으면 어긋날 수 없다.
        ///
        /// 부수 효과로 <c>MoveRangeAxesCancelEachOtherThroughTheSameFormula</c>의 사정거리가
        /// 1 → 4가 됐다. 새 항을 추가할 때는 <b>여기만</b> 고치면 된다.
        ///
        /// 이동 불가(속박·기절)는 사거리 0이다 — 네 경로 모두 같은 가드를 갖고 있었으므로
        /// 인자로 뺄 이유가 없다. 예외가 필요해지면 그때 인자를 만든다.
        /// </summary>
        /// <param name="baseValue">
        /// 보정 전 기준 거리. 저작 <c>Range</c> · X코스트 잔량 · 빠른거북이 뽑은 거리 ·
        /// 이동 카드의 <c>AreaRadius</c> 중 하나가 들어온다.
        /// </param>
        private int ResolveMoveRange(int baseValue)
        {
            if (IsPlayerMovementBlocked()) return 0;
            return Math.Max(0, baseValue
                + pending.MovementRangeModifier
                + GetPermanentItemEffectTotal(PlayerPermanentItemEffectKind.MovementRangeBonus)
                + SumActivePlayerEffectAmountByValueMode(StatusEffectValueMode.MoveRangeBonus)
                - SumActivePlayerEffectAmountByValueMode(StatusEffectValueMode.MoveRangePenalty)
                - CountHandCurse(CardIds.Tardiness));
        }

        internal HexCoord? ChooseRandomMovementDestination(int range)
        {
            var candidates = HexPathfinder.GetReachableCells(Map, new MovementQuery(PlayerCoord, range, includeStart: false, unitId: PlayerUnitId), runtimeStates, terrainTraits)
                .Keys
                .OrderBy(coord => coord.Q)
                .ThenBy(coord => coord.R)
                .ToList();
            return candidates.Count == 0 ? (HexCoord?)null : candidates[pushRng.Next(candidates.Count)];
        }

        private int GetAttackDamage(CardDefinition card, int attackBonus, int spentKi, HexCoord target)
        {
            if (card == null)
            {
                return 0;
            }

            switch (card.ScalingMode)
            {
                case CardScalingMode.AttackCardsInHand:
                    return card.Amount + attackBonus + GetFlatAttackDamageBonus();
                case CardScalingMode.SpentKi:
                    return spentKi * card.Amount + attackBonus + GetFlatAttackDamageBonus();
                case CardScalingMode.MovedThisTurn:
                    return System.Math.Max(0, lastMovedDistance * card.Amount + attackBonus + GetFlatAttackDamageBonus());
                default:
                    return CardBehaviorRegistry.Resolve(card).GetAttackDamage(this, card, attackBonus + GetFlatAttackDamageBonus(), target);
            }
        }

        /// <summary>
        /// 이 카드가 <paramref name="target"/>을 겨눴을 때 보드에서 덮는 칸(§20-A-5).
        /// 집행(<see cref="TryPlayerAttack"/>)과 미리보기(<see cref="GetDisplayAttackDamage"/>)가
        /// <b>같은 이 함수</b>를 부르므로, 취약 부위 판정이 화면과 규칙에서 갈라질 수 없다.
        ///
        /// 형상 카드는 실제 셀 목록, 범위 카드는 원판(물질화하지 않고 거리 비교), 단일 대상은 겨냥 칸 하나다.
        /// </summary>
        private AttackCoverage ResolveAttackCoverage(CardDefinition card, HexCoord target)
        {
            if (card == null)
            {
                return AttackCoverage.AtCell(target);
            }

            if (!string.IsNullOrEmpty(card.ShapeId))
            {
                var attackDir = PlayerCoord.ApproximateDirection(target);
                return AttackCoverage.Cells(
                    AttackShapeLibrary.GetAffectedCells(card.ShapeId, PlayerCoord, attackDir).ToList());
            }

            return card.AreaRadius > 0
                ? AttackCoverage.Disk(target, card.AreaRadius)
                : AttackCoverage.AtCell(target);
        }

        /// <summary>
        /// 플레이어 공격 카드 <b>한 타격</b>이 이 몬스터에게 실제로 넣는 피해. 쇠약(가하는 −%)과
        /// 허점(받는 타격당 +N)이 만나는 단 하나의 지점이다 — 공격 경로가 네 갈래(단일·범위·형상·희생)라
        /// 계산을 흩뿌리면 그중 하나에서 반드시 빠진다.
        ///
        /// 순서: <b>배율 먼저, 고정치 나중</b>. 허점은 퍼센트가 아니라 타격당 고정치이므로(D-10)
        /// 쇠약이 그 값까지 깎으면 "다타 카드와 시너지"라는 설계 의도가 무너진다.
        /// 몬스터가 플레이어를 때리는 반대 방향은 <c>CombatState.MonsterAi</c>의 같은 두 축이 담당한다.
        ///
        /// <para><paramref name="coverage"/>는 이 타격이 보드에서 <b>덮은 칸</b>이다. 취약 부위(§20-A)가
        /// 칸 단위 판정이라 필요해졌다 — 기본값(<see cref="AttackCoverage.None"/>)이면 취약타가 되지
        /// 않으므로, 좌표를 모르는 경로는 기존 동작 그대로다.</para>
        /// </summary>
        /// <summary>이 타격이 판명된 취약 부위를 덮었는가(배율 &gt; 100%). 연출 이벤트의 <c>WeakSpotHit</c> 표식용.</summary>
        private bool IsBossWeakSpotHit(MonsterRuntime monster, AttackCoverage coverage)
        {
            return monster != null && ResolveBossWeakSpotDamagePercent(monster, coverage) > 100;
        }

        private int ResolvePlayerAttackDamageTo(MonsterRuntime monster, int rawDamage, AttackCoverage coverage = default, bool execute = false)
        {
            // Math.Max(0, …)은 쇠약 100% 초과 저작에 대한 백스톱(현행 30%에서는 발동하지 않는다).
            // 취약 부위는 쇠약·강화와 같은 배율 축이므로 여기서 함께 곱해진다(고정치인 허점보다 앞).
            var outgoingPercent = Math.Max(0, 100
                + SumActivePlayerEffectAmountByValueMode(StatusEffectValueMode.DamageDealtBonusPercent)
                - SumActivePlayerEffectAmountByValueMode(StatusEffectValueMode.DamageDealtPenaltyPercent));
            var scaled = Math.Max(0, rawDamage) * outgoingPercent / 100
                         * ResolveBossWeakSpotDamagePercent(monster, coverage) / 100;
            var vulnerable = monster == null
                ? 0
                : SumActiveEffectAmountByValueMode(monster.Id, StatusEffectValueMode.IncomingDamageBonusFlat);
            var total = Math.Max(0, scaled + vulnerable);

            // 맷집(T7-2): 첫 카드 피격 반감(올림 — 1은 1로 남는다). 반감은 <b>읽기</b>고 소진은
            // <c>execute</c>가 true인 실행 경로에서만 일어난다 — 이 함수는 미리보기(GetDisplayAttackDamage)와
            // 실행이 공유하므로, 여기서 무조건 소진하면 카드 호버만으로 맷집이 벗겨진다.
            // 다타 카드는 첫 히트가 래치를 태우고 이후 히트는 온전히 들어간다.
            if (total > 0 && IsMonsterToughnessReady(monster))
            {
                total = (total + 1) / 2;
                if (execute)
                {
                    monster.ToughnessSpent = true;
                    monster.ToughnessReloadProgress = 0;
                    // 🔴 알림도 <b>실행 경로에서만</b> — 이 함수는 카드 호버 미리보기와 공유하므로,
                    //    게이트 밖에 두면 손에 카드를 든 것만으로 "맷집!"이 뜬다(래치가 타는 것과 같은 함정).
                    RaiseMonsterTraitAnnouncement(monster, MonsterTraitAnnouncement.ToughnessAbsorbedRef);
                }
            }

            return total;
        }

        /// <summary>
        /// 카드가 <b>지금</b> 화면에 찍어야 할 수치 — 저작값이 아니라 실효값이다.
        /// 몬스터 공격 예고가 <c>ResolveMonsterAttackDamageToPlayer</c>를 그대로 쓰는 것(R-8)의
        /// 플레이어 방향 대칭이며, 집행과 <b>같은 함수들</b>(<see cref="ResolvePlayerAttackDamageTo"/>,
        /// <see cref="ResolveBlockGain"/>)을 부르므로 표시와 규칙이 갈라질 수 없다.
        ///
        /// <para><paramref name="previewTarget"/>가 없으면 <b>대상 무관분만</b> 반영한다(D-7):
        /// 힘·강화·쇠약·유물·지형은 손에 들고만 있어도 확정이지만, 허점과 표식은 어느 몬스터를
        /// 겨누느냐로 갈리므로 타겟팅 전에는 합류하지 않는다.</para>
        ///
        /// <para>⚠️ 공격 수치는 <b>1타 기준</b>이다. 카드 텍스트의 <c>{Damage}</c>는 "한 번의 타격"을
        /// 뜻하며(A12 전염병은 한 문장에 <c>{Damage}</c>를 두 번 쓴다 — 총합을 넣으면 그 카드가
        /// 즉시 거짓말을 한다), 반복 횟수는 <c>{HitCount}</c>가 따로 말한다. 그래서 여기서는
        /// 총합을 내는 <see cref="GetAttackDamage"/>의 scaling 분기를 타지 않는다.</para>
        /// </summary>
        internal int GetDisplayValue(CardDefinition card, HexCoord? previewTarget = null)
        {
            if (card == null)
            {
                return 0;
            }

            switch (card.EffectType)
            {
                case CardEffectType.Attack:
                    return GetDisplayAttackDamage(card, previewTarget);
                case CardEffectType.Defend:
                    return ResolveBlockGain(PlayerUnitId, card.Amount);
                default:
                    // 회복·정찰·필드 오브젝트 등은 보정 축이 없다. 특히 필드 피해는 배치 시점의
                    // 저작값 그대로 들어가므로(FieldObjectCardEffects) 여기서 보정하면 화면만 거짓이 된다.
                    return card.Amount;
            }
        }

        /// <summary>
        /// 카드 얼굴에 찍을 <b>실효 사거리</b>(2026-08-20 #13). 이동 카드만 보정 축이 있다 —
        /// 민첩·둔화·지각(X07)·유물 MovementRangeBonus가 실제 도달 거리를 바꾸는데 카드에는 저작
        /// 거리가 그대로 찍혀 있었다. 공격 사거리는 보정 축이 없으므로 저작값 그대로다.
        ///
        /// <para>🔑 규칙과 <b>같은 함수</b>(<see cref="GetEffectiveMoveRange"/>)를 부른다 — 표시용 산식을
        /// 따로 쓰면 도달 오버레이와 카드 숫자가 갈라진다(공격 피해가 GetDisplayAttackDamage에서
        /// 집행과 같은 커버리지를 푸는 것과 같은 이유).</para>
        /// </summary>
        private int GetDisplayRange(CardDefinition card)
        {
            return card != null && ToKind(card.EffectType) == CombatCardKind.Move
                ? GetEffectiveMoveRange(card)
                : (card?.Range ?? 0);
        }

        /// <summary>범위 이동(M06)의 실효 반경. <see cref="GetDisplayRange"/>와 한 쌍이다.</summary>
        private int GetDisplayAreaRadius(CardDefinition card)
        {
            return card != null && ToKind(card.EffectType) == CombatCardKind.Move && card.AreaRadius > 0
                ? GetEffectiveAreaMoveRange(card)
                : (card?.AreaRadius ?? 0);
        }

        private int GetDisplayAttackDamage(CardDefinition card, HexCoord? previewTarget)
        {
            // 1타분 원값: 저작 피해 + 지형 + 유물/힘 flat. GetAttackDamage의 default 분기와 같은 구성이며,
            // 배율(강화·쇠약)보다 앞에 있다 = flat 보너스가 배율의 혜택을 받는다.
            var raw = GetDisplayRawAttackDamage(card);

            var monster = previewTarget.HasValue ? FindLivingMonsterAt(previewTarget.Value) : null;
            if (monster != null)
            {
                // 표식은 읽기만 한다 — 소모하면 카드를 쓰기도 전에 표식이 사라진다.
                raw *= PeekMarkMultiplier(monster);
            }

            // 미리보기도 집행과 <b>같은 커버리지</b>를 푼다. 취약 부위 누출(§20-A-6)이 별도 분기가
            // 아니라 구조로 막히는 지점이다: 미판명이면 배수 자체가 100이므로 화면과 규칙이 함께 100이고,
            // 판명 중에는 둘 다 200이다. "미리보기만 100으로 깎는" 특례를 두면 판명 중에 거짓말을 한다.
            var coverage = previewTarget.HasValue ? ResolveAttackCoverage(card, previewTarget.Value) : AttackCoverage.None;
            return ResolvePlayerAttackDamageTo(monster, raw, coverage);
        }

        /// <summary>
        /// 표시용 원값(배율·허점 적용 <b>전</b>). 기본은 <b>1타분</b>이지만, 규칙이 실제로 한 번에
        /// 합산해서 때리는 두 scaling 모드만 총합을 낸다.
        ///
        /// <para>왜 갈리는가: <c>{Damage}</c>는 "한 번의 타격"을 뜻하고 반복은 <c>{HitCount}</c>가 따로
        /// 말한다. 진짜 다타인 모드(손패 공격 수·소멸 수·증식)는 1타분이 맞고, A12 전염병처럼 한 문장에
        /// <c>{Damage}</c>를 두 번 쓰는 카드도 1타분이라야 말이 된다. 반면 <c>MovedThisTurn</c>과
        /// <c>SpentKi</c>는 <see cref="GetAttackHitCount"/>가 다루지 않아 <b>hitCount가 1</b>이다 —
        /// 즉 규칙상 총합을 한 번에 때리므로, 1타분을 찍으면 카드가 실제 피해를 과소 보고한다.</para>
        ///
        /// <para>⚠️ flat 보너스(유물·힘)는 <see cref="GetAttackDamage"/>와 똑같이 <b>배수에 곱해지지 않고
        /// 한 번만</b> 더해진다. 여기서 곱하면 표시가 규칙을 앞서간다.</para>
        /// </summary>
        private int GetDisplayRawAttackDamage(CardDefinition card)
        {
            var flat = GetPlayerTerrainAttackBonus() + GetFlatAttackDamageBonus();
            switch (card.ScalingMode)
            {
                case CardScalingMode.MovedThisTurn:
                    return Math.Max(0, lastMovedDistance * card.Amount + flat);
                case CardScalingMode.SpentKi:
                    return Math.Max(0, PeekCardKiSpend(card) * card.Amount + flat);
                default:
                    return card.Amount + flat;
            }
        }

        private int GetAttackHitCount(CardDefinition card)
        {
            if (card == null)
            {
                return 1;
            }

            if (card.ScalingMode == CardScalingMode.AttackCardsInHand)
            {
                return Math.Max(1, CountAttackCardsInHandBeforeUse());
            }

            if (card.ScalingMode == CardScalingMode.ExiledCards)
            {
                return Math.Max(1, CountExiledCards());
            }

            if (IsMultiplyingStrikeCard(card))
            {
                return Math.Max(1, CountMultiplyingStrikeCards());
            }

            if (card.HitCount > 1)
            {
                return card.HitCount;
            }

            // hitCount is authored in cards.csv. There used to be an AttackDoubleHit → 2 fallback here; it was
            // already unreachable (A06 authors hitCount=2) but would have silently revived the hardcoded 2 the
            // moment that column was cleared, hiding the mistake instead of surfacing it.
            return 1;
        }

        private int GetPermanentItemEffectTotal(PlayerPermanentItemEffectKind effectKind)
        {
            return PlayerInventory?.RelicsAndCurses?.SumEffect(effectKind) ?? 0;
        }

        /// <summary>
        /// 공격 <b>한 타격</b>에 더해지는 고정치의 합 — 유물 <c>AttackDamageBonus</c>와 힘(D-2).
        /// <para>이 자리가 결정을 두 개 동시에 만족시킨다: 호출자가 타격마다 부르므로 <b>타격당</b>이고
        /// (D-2), <see cref="ResolvePlayerAttackDamageTo"/>의 배율보다 <b>앞</b>이라 강화·쇠약이
        /// 힘까지 함께 스케일한다(D-1: 힘을 먼저 더하고 %를 곱한다).</para>
        /// <para>⚠️ 필드 오브젝트 피해는 이 함수를 부르지 않는다 — 배치 시점의 저작값이 그대로 들어가고
        /// 공격 보정 축을 타지 않는 것이 계약이다(CardDisplayValueTests가 고정한다).</para>
        /// </summary>
        /// <summary>
        /// 힘 부여의 <b>단일 관문</b>. 카드·유물·보상 어느 경로든 인벤토리를 직접 건드리지 말고 여기를
        /// 지날 것 — 우회하면 수치는 오르는데 획득 연출(플로팅 텍스트/아이콘 갱신)만 조용히 빠진다.
        /// <see cref="TryGrantPermanentItem"/>이 영구 아이템에 대해 하는 역할과 같다.
        /// </summary>
        public int GrantMight(int amount)
        {
            if (PlayerInventory == null || amount == 0)
            {
                return PlayerInventory?.MightStacks ?? 0;
            }

            var total = PlayerInventory.AddMight(amount);
            // 누적 총량을 실어 보낸다 — 독 아이콘이 보여주는 값과 같아야 "힘 5"가 두 곳에서 일치한다.
            RaiseStatusEffect(StatusEffectKind.Might, PlayerCoord, 0, total, PlayerUnitId, "run.permanent");
            return total;
        }

        private int GetFlatAttackDamageBonus()
        {
            return GetPermanentItemEffectTotal(PlayerPermanentItemEffectKind.AttackDamageBonus)
                + (PlayerInventory?.MightStacks ?? 0)
                + GetMoveDistanceTriggerAttackBonus()
                // 맹호 호리병(T4-1): 이번 턴 한정 소모품 보너스 — 무선 이어폰와 같은 합산 지점이라
                // 카드 수치 미리보기와 집행이 함께 오른다.
                + bagAttackBonusThisTurn;
        }

        /// <summary>
        /// 무선 이어폰(T2 페이즈 C): 이번 턴 이동이 문턱(triggerParam) 이상이면 공격 피해 +N.
        /// kind 없는 조건부 스칼라(사용자 확정) — <see cref="lastMovedDistance"/>를 직독하고,
        /// 발동 피드백은 카드 수치 미리보기 갱신이다(미리보기와 집행이 GetFlatAttackDamageBonus를
        /// 공유하므로 구조적으로 갈라질 수 없다).
        /// </summary>
        private int GetMoveDistanceTriggerAttackBonus()
        {
            var items = PlayerInventory?.RelicsAndCurses?.Items;
            if (items == null)
            {
                return 0;
            }

            var total = 0;
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.IsActive
                    && item.TriggerKind == RelicTriggerKind.MoveDistanceAttackBonus
                    && item.TriggerParam > 0
                    && lastMovedDistance >= item.TriggerParam)
                {
                    total += item.EffectAmount;
                }
            }

            return total;
        }

        /// <summary>
        /// 전투 중 영구 아이템 획득의 단일 관문. 다른 축들은 계산 시점에 총량을 다시 읽으므로 인벤토리에
        /// 넣는 것만으로 즉시 반영되지만, <see cref="PlayerPermanentItemEffectKind.MaxHpBonus"/>만은
        /// 저장된 상태(<see cref="CombatantState.MaxHp"/>)라 획득 순간에 한 번 적용해야 한다. 새 획득
        /// 경로(뽑기·상점)는 인벤토리를 직접 건드리지 말고 이 메서드를 지날 것 — 우회하면 최대 체력
        /// 유물만 조용히 아무 일도 하지 않는다.
        /// </summary>
        public bool TryGrantPermanentItem(string definitionId, out string reason)
        {
            if (PlayerInventory?.RelicsAndCurses == null)
            {
                reason = "Player inventory is unavailable.";
                return false;
            }

            if (!PlayerInventory.RelicsAndCurses.TryAddDefinition(definitionId, out reason))
            {
                return false;
            }

            if (PlayerPermanentItemCatalog.TryGet(definitionId, out var definition))
            {
                // T2 페이즈 B: 저장 상태를 건드리는 1회성 축들을 획득 순간에 한 번 적용한다(MaxHp 선례의
                // 일반화 — 양날 유물의 extraEffects까지 같은 목록으로 순회). 스칼라 축들은 인벤토리에
                // 있는 것만으로 SumEffect가 집계하므로 여기서 할 일이 없다.
                ApplyOneTimeGrantEffect(definition.EffectKind, definition.EffectAmount);
                for (var i = 0; i < definition.ExtraEffects.Count; i++)
                {
                    ApplyOneTimeGrantEffect(definition.ExtraEffects[i].Kind, definition.ExtraEffects[i].Amount);
                }

                // 획득의 단일 관문이므로 여기 한 줄이면 어느 경로(보상·상점·저작 시작 유물)로 와도 열린다.
                MarkCodexSighting(CodexDomainIds.Relic, definition.Id);

                if (definition.TriggerKind == RelicTriggerKind.GuardChargeCycle)
                {
                    // 수호 부적(T2 페이즈 C)은 충전된 채 도착한다(StS Artifact 선례) — 이후 재충전은
                    // 20턴 주기가, "최대 1"은 GrantGuardCharge의 보유 게이트가 맡는다.
                    GrantGuardCharge(definition.EffectAmount, $"relic.{definition.Id}");
                }
            }

            return true;
        }

        private void ApplyOneTimeGrantEffect(PlayerPermanentItemEffectKind kind, int amount)
        {
            switch (kind)
            {
                case PlayerPermanentItemEffectKind.MaxHpBonus:
                    Player.IncreaseMaxHp(amount);
                    break;
                case PlayerPermanentItemEffectKind.MaxHpPenalty:
                    Player.ReduceMaxHp(amount);
                    break;
                case PlayerPermanentItemEffectKind.MoneyGrantOnce:
                    GrantMoneyWithRelicBonus(amount);
                    break;
                case PlayerPermanentItemEffectKind.MightGrantOnce:
                    PlayerInventory?.AddMight(amount);
                    break;
            }
        }

        /// <summary>
        /// 돈 지급의 단일 이음매(T2 페이즈 B) — 적립 카드·마이너스 통장(MoneyGainBonus)가 여기서
        /// 가산된다. 지급이 이 함수를 우회하면 그 경로만 보너스가 조용히 빠진다.
        /// </summary>
        internal void GrantMoneyWithRelicBonus(int amount)
        {
            if (amount <= 0 || PlayerInventory?.Wallet == null)
            {
                return;
            }

            PlayerInventory.Wallet.Add(amount + GetPermanentItemEffectTotal(PlayerPermanentItemEffectKind.MoneyGainBonus));
        }

        /// <summary>유물 효과 총량의 공개 조회 — 컨트롤러 소비용(상점 할인 등).</summary>
        public int GetRelicEffectTotal(PlayerPermanentItemEffectKind effectKind)
        {
            return GetPermanentItemEffectTotal(effectKind);
        }

        private int CountAttackCardsInHandBeforeUse()
        {
            return ActionDeck.Hand.Count(card => card.EffectType == CardEffectType.Attack);
        }

        // True while the unit carries at least one 정화 가능한 디버프. Reuses the P0 cleanse predicate rather
        // than "has any ActiveEffect": 강화/민첩 같은 버프까지 세면 A12가 D02로 강화된 적을 상대로 공짜 보너스를
        // 받는다.
        internal bool HasCleansableDebuff(string unitId)
        {
            return activeEffects.HasAnyCleansable(unitId);
        }

        /// <summary>
        /// A12 전염병: copies <b>exactly one</b> 정화 가능한 디버프 the struck monster carries onto exactly
        /// ONE living monster within <paramref name="radius"/> — nearest first, random among ties. Runs as a
        /// post-action, after damage — deliberately: the source monster may have died to the hit, and 전염
        /// from a corpse is still 전염. Effects are not cleared on death, so the snapshot survives long
        /// enough to spread.
        /// <para>
        /// 🔑 2026-08-20 #18(사용자 확정): 옮기는 것은 <b>가진 상태이상 중 무작위 1가지</b>이고, 범위는
        /// 인접이 아니라 <b>2칸 내 최근접</b>이다(저작면 = <c>postActions=SpreadStatus:2</c>). 전부를
        /// 옮기던 종전 규칙은 디버프를 쌓아 둔 대상 하나가 판 전체를 오염시켰다.
        /// </para>
        /// <para>
        /// The card text says "옮깁니다", but the debuff is COPIED — it stays on the original target too.
        /// That is the authored rule, not a bug: "옮긴다" is the colloquial reading of 전염(spread). Do not
        /// "fix" this into a move (사용자 재확정 2026-08-20).
        /// </para>
        /// </summary>
        private void SpreadTargetDebuffsToNeighbours(CardDefinition card, HexCoord target, int radius)
        {
            var source = monsters.FirstOrDefault(monster => IsMonsterOccupying(monster, target));
            if (source == null)
            {
                return;
            }

            var carried = activeEffects.CleansableOn(source.Id);
            if (carried.Count == 0)
            {
                return;
            }

            var infected = PickContagionTarget(source, target, radius);
            if (infected == null)
            {
                return;
            }

            // 무작위 1가지만 옮긴다(#18). pushRng를 공유하는 이유는 PickContagionTarget과 같다 —
            // 전투 RNG는 이미 4벌로 흩어져 있어 다섯 번째를 더하면 시드 재현이 더 멀어진다.
            var spread = carried[pushRng.Next(carried.Count)];
            ApplyStatusToMonster(infected, spread.Kind, spread.RemainingTurns, spread.Amount, EffectSourceRefOf(card));
        }

        /// <summary>
        /// The single monster 전염병 infects: closest to the struck tile, breaking ties at random. 출하
        /// 반경은 2(#18) — 거리 1의 후보가 있으면 그쪽이 먼저이고, 없을 때만 거리 2로 넘어간다.
        /// Uses <c>pushRng</c> rather than a new Random: combat RNG is already spread across four instances,
        /// and adding a fifth would push seed reproduction further out of reach.
        /// </summary>
        private MonsterRuntime PickContagionTarget(MonsterRuntime source, HexCoord target, int radius)
        {
            var candidates = monsters
                .Where(monster => !monster.Combatant.IsDead
                    && !ReferenceEquals(monster, source)
                    && GetDistanceToMonster(monster, target) <= radius)
                .ToList();
            if (candidates.Count == 0)
            {
                return null;
            }

            var nearestDistance = candidates.Min(monster => target.DistanceTo(monster.Coord));
            var nearest = candidates
                .Where(monster => target.DistanceTo(monster.Coord) == nearestDistance)
                .ToList();
            return nearest[pushRng.Next(nearest.Count)];
        }

        /// <summary>
        /// Applies a status to a monster the way a card should: hard control (기절/속박) also refreshes the
        /// monster's committed plan and emits the intent-cancel cue, otherwise a stunned monster keeps
        /// telegraphing an attack it can no longer make.
        /// </summary>
        private void ApplyStatusToMonster(MonsterRuntime monster, StatusEffectKind kind, int turns, int amount, string sourceRef)
        {
            if (monster == null || monster.Combatant.IsDead)
            {
                return;
            }

            var clampedTurns = Math.Max(1, turns);
            var isControl = IsMonsterControlStatus(kind);
            var intent = CaptureMonsterIntentForCancel(monster);
            AddDurationStatusEffect(kind, monster.Id, clampedTurns, amount, sourceRef);
            if (isControl)
            {
                ApplyControlStatusConstraintToPlan(monster);
            }

            RaiseStatusEffect(kind, monster.Coord, 0, amount, monster.Id, sourceRef);
            if (isControl)
            {
                EmitMonsterIntentCancelText(monster, intent.WasMoving, intent.WasAttacking, kind);
            }
        }

        // Exiles one random card from the action hand, excluding the card doing the exiling (D05). The hand
        // may hold nothing else, in which case the card still resolves and simply pays no cost — the same
        // "never refuse, just do less" rule the other new cards follow (D8).
        internal bool ExileRandomActionHandCard(CardDefinition exclude)
        {
            var candidates = ActionDeck.Hand.Where(candidate => !ReferenceEquals(candidate, exclude)).ToList();
            if (candidates.Count == 0)
            {
                return false;
            }

            return ActionDeck.PermanentRemoveFromHand(candidates[pushRng.Next(candidates.Count)]);
        }

        // 소멸 더미 전체 — both decks, matching what GetExilePileCards shows the player. A13 잔혼 공격 repeats
        // this many times, so the number the pile overlay lists is the number the card delivers.
        /// <summary>
        /// 저주받은 인형뽑기(T2-C)의 저주 추첨 풀 — (effectRef, weight). 아지랑이류(미세먼지·도깨비 장난)는
        /// 한 턴짜리 오염이라 상자의 대가로는 싱거워 제외(함정 살포 전용). 원귀만 무겁게 저가중.
        /// 문서 정본: docs/design/keyword-systematization-and-sts-insights.md §4 T2-C.
        /// </summary>
        private static readonly (string CardId, int Weight)[] CursedGachaCursePool =
        {
            (CardIds.BrokenGlass, 3),
            (CardIds.Blackout, 3),
            (CardIds.DebtNote, 4),
            (CardIds.Nightmare, 3),
            (CardIds.Lingering, 3),
            (CardIds.Tardiness, 3),
            (CardIds.Nuisance, 4),
            (CardIds.CursedCharm, 3),
            (CardIds.MurkyFog, 3),
            (CardIds.VengefulGhost, 1),
        };

        /// <summary>
        /// 저주받은 인형뽑기(T2-C): 유물 1개 확정 + 저주 카드 1장 덱 삽입 확정. 일반 인형뽑기(유물 10%)와
        /// 대비되는 프리미엄 계약이라 확률이 아니라 확정 지급이다 — 대가가 원망이 아닌 계약으로 읽히게.
        /// 유물 풀 고갈(전부 보유) 시 돈 40 폴백(후반 보너스 허용). 소비 대장은 보물상자와 같은
        /// claimedEventObjectIds — 세이브 왕복도 같이 탄다.
        /// </summary>
        public bool TryOpenCursedGachaMachine(string objectId, IRewardRandom random, out string message)
        {
            message = string.Empty;
            if (random == null || string.IsNullOrWhiteSpace(objectId) || claimedEventObjectIds.Contains(objectId))
            {
                return false;
            }

            string relicPart;
            var pool = GachaRewardRoller.BuildRelicPool(PlayerPermanentItemCatalog.Definitions, PlayerInventory?.RelicsAndCurses);
            if (pool.Count > 0 && TryGrantPermanentItem(pool[random.Next(pool.Count)], out _))
            {
                relicPart = "유물을 획득했습니다";
            }
            else
            {
                GrantMoneyWithRelicBonus(40);
                relicPart = "돈 +40";
            }

            var curseInjected = TryInjectStatusCard(PickWeightedCurse(random));
            claimedEventObjectIds.Add(objectId);
            message = curseInjected
                ? $"저주받은 인형뽑기 — {relicPart}. 저주 카드가 덱에 섞였습니다."
                : $"저주받은 인형뽑기 — {relicPart}.";
            return true;
        }

        private static string PickWeightedCurse(IRewardRandom random)
        {
            var total = 0;
            foreach (var entry in CursedGachaCursePool)
            {
                total += entry.Weight;
            }

            var roll = random.Next(total);
            foreach (var entry in CursedGachaCursePool)
            {
                roll -= entry.Weight;
                if (roll < 0)
                {
                    return entry.CardId;
                }
            }

            return CursedGachaCursePool[0].CardId;
        }

        private int CountExiledCards()
        {
            return MovementDeck.RemovedPile.Count + ActionDeck.RemovedPile.Count;
        }

        internal int CountTreasureObjects(HexCoord center, int radius)
        {
            return Map.ObjectRefs.Count(objectRef =>
                center.DistanceTo(objectRef.Coord) <= radius &&
                (string.Equals(objectRef.ObjectType, "TreasureChest", StringComparison.Ordinal) ||
                 string.Equals(objectRef.ObjectType, "Relic", StringComparison.Ordinal) ||
                 string.Equals(objectRef.Role, "Relic", StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>
        /// 카드가 <b>지금 어떤 상태 제약</b>으로 사용 불가인가 — 사용 불가 판정의 <b>단일 술어</b>(2026-09-05, S5 §10.4).
        /// 종전에는 실행 검증(<see cref="CanUseActionCard"/>)·이동 실행(<see cref="TryPlayerMove(HexCoord, string)"/>·
        /// <see cref="TryPlayerMovementSelf"/>)·UI usable(<see cref="IsCardUsable"/>)·회색 라벨(<see cref="GetCardStatus"/>)
        /// 네 면이 각자 술어를 나열했고, 그 결과 <b>이동 실행 경로에 봉인 검사가 빠져 있었다</b>(라벨은 「봉인」인데
        /// <c>TryPlayerMove</c>는 통과). 새 사유는 여기 한 곳과 두 문안 표에만 추가한다.
        ///
        /// <para>우선순위(겹칠 때 무엇이 원인으로 읽히는가)는 종전 <see cref="GetCardStatus"/>의 순서를 정본으로 삼았다:
        /// 상태 카드 → 봉인(어떤 상황에서도 못 쓰는 것이 먼저 — 「기다리면 풀린다」로 읽히면 안 된다) →
        /// 기절/속박(더 넓게 막는 것) → 무장 해제(공격만). 페이즈·기력·사거리는 <b>상황</b> 제약이라 각 면이 따로 본다.</para>
        /// </summary>
        private enum CardRestriction
        {
            None,
            StatusCard,
            Sealed,
            Stunned,
            Immobilized,
            Disarmed,
        }

        private CardRestriction GetCardRestriction(CardDefinition card)
        {
            if (card == null)
            {
                return CardRestriction.None;
            }

            if (IsUnplayableStatusCard(card))
            {
                return CardRestriction.StatusCard;
            }

            // 봉인은 카드 종류를 가리지 않는다(O-11: 이동 카드 포함).
            if (IsSealedCard(card))
            {
                return CardRestriction.Sealed;
            }

            if (card.EffectType == CardEffectType.Move)
            {
                // 속박(이동만 차단)/기절(이동+행동 차단) 모두 이동을 막는다. 둘 다면 기절이 원인으로 읽힌다.
                if (IsPlayerMovementBlocked())
                {
                    return HasActivePlayerEffect(StatusEffectKind.Stun) ? CardRestriction.Stunned : CardRestriction.Immobilized;
                }

                // 무장 해제는 이동을 막지 않는다(C-5).
                return CardRestriction.None;
            }

            if (IsBlockedByStun(card))
            {
                return CardRestriction.Stunned;
            }

            // 기절 뒤에 둔다: 둘 다 걸린 상태라면 더 넓게 막는 기절이 원인으로 읽혀야 한다.
            if (IsBlockedByDisarm(card))
            {
                return CardRestriction.Disarmed;
            }

            return CardRestriction.None;
        }

        /// <summary>회색 라벨 문안(<see cref="CombatCardStatusText"/>). <see cref="CardRestriction.None"/>은 null.</summary>
        private static string RestrictionStatusText(CardRestriction restriction)
        {
            switch (restriction)
            {
                case CardRestriction.StatusCard: return CombatCardStatusText.StatusCard;
                case CardRestriction.Sealed: return CombatCardStatusText.Sealed;
                case CardRestriction.Stunned: return CombatCardStatusText.Stunned;
                case CardRestriction.Immobilized: return CombatCardStatusText.Immobilized;
                case CardRestriction.Disarmed: return CombatCardStatusText.Disarmed;
                default: return null;
            }
        }

        /// <summary>실행 거부 사유 문장(<c>LastFailureReason</c>). 이동 카드는 「이동할 수 없습니다」 꼴을 유지한다.</summary>
        private static string RestrictionFailureText(CardRestriction restriction, bool isMoveCard)
        {
            switch (restriction)
            {
                case CardRestriction.StatusCard: return "상태 카드는 사용할 수 없습니다.";
                case CardRestriction.Sealed: return "봉인된 카드는 사용할 수 없습니다.";
                case CardRestriction.Stunned: return isMoveCard ? "기절 상태라 이동할 수 없습니다." : "기절 상태라 행동 카드를 사용할 수 없습니다.";
                case CardRestriction.Immobilized: return "속박 상태라 이동할 수 없습니다.";
                case CardRestriction.Disarmed: return "무장 해제 상태라 공격 카드를 사용할 수 없습니다.";
                default: return string.Empty;
            }
        }

        /// <summary>
        /// The single 기절(stun) action lockout predicate. Execution validation, the UI usable flag, and the
        /// grey status label all route through here — they gate the same card, so they must never disagree
        /// (one-sided edits produce "the button is live but the play is rejected", or the reverse).
        /// <see cref="CardDefinition.UsableWhileStunned"/> is the per-card, data-authored exemption.
        /// </summary>
        private bool IsBlockedByStun(CardDefinition card)
        {
            // Both player phases: BothIfApproved cards (Scout/Utility) are playable during PlayerMovement,
            // so gating PlayerAction alone lets a stunned player slip them in through the movement phase.
            return (Phase == CombatPhase.PlayerAction || Phase == CombatPhase.PlayerMovement)
                && card != null
                && !card.UsableWhileStunned
                && HasActivePlayerEffect(StatusEffectKind.Stun);
        }

        /// <summary>
        /// 무장 해제(C-5 / D-11)의 단일 술어. 종전에는 <see cref="IsBlockedByStun"/>와 같은 3면(실행 검증 / UI usable /
        /// 회색 라벨)에 각자 배선했으나, 2026-09-05부터 <see cref="GetCardRestriction"/> 한 곳만 이 술어를 부른다 —
        /// 한쪽만 고치면 "버튼은 살아 있는데 실행은 거부"가 되던 구조를 술어 하나로 접었다.
        ///
        /// 기절과 다른 점은 <b>범위</b>뿐이다: 공격 카드만 막는다. 이동 카드는 애초에
        /// <see cref="IsCardUsable"/>의 Move 조기 분기로 빠져 이 게이트를 타지 않는데, 무장 해제는
        /// 이동을 막지 않으므로 그것이 곧 옳은 동작이다(봉인 C-16은 그 4번째 지점을 따로 뚫어야 한다).
        ///
        /// <see cref="CardDefinition.UsableWhileStunned"/> 면제는 <b>적용하지 않는다</b>: 그 컬럼은
        /// "기절해도 쓸 수 있다"를 뜻하고 저작된 두 장(U03·D06)은 공격 카드가 아니다. 무장 해제에까지
        /// 재사용하면 한 컬럼이 두 규칙을 뜻하게 된다.
        /// </summary>
        private bool IsBlockedByDisarm(CardDefinition card)
        {
            return (Phase == CombatPhase.PlayerAction || Phase == CombatPhase.PlayerMovement)
                && card != null
                && card.EffectType == CardEffectType.Attack
                && HasActivePlayerEffect(StatusEffectKind.Disarm);
        }

        /// <summary>
        /// 상태 카드(C-17)는 <b>언제나</b> 사용 불가다. 게이트는 <see cref="GetCardRestriction"/> 한 곳을 지나며
        /// (2026-09-05 통합), 한 면이라도 그 술어를 우회하면 "사용 가능한 상태 카드"가 된다(계획 R-3).
        /// </summary>
        private static bool IsUnplayableStatusCard(CardDefinition card)
        {
            // 저주 카드(구 상태 카드)는 기본 사용 불가 — 빚 문서(X04)만 예외(T2: 기 1로 사용 시 소멸).
            return card != null && card.EffectType == CardEffectType.Status && !IsPlayableCurseCard(card);
        }

        /// <summary>
        /// 아지랑이(T2 키워드) 저주의 단일 목록 — 주입 시 IsTemporary를 세워 턴말 purge가 집게 한다.
        /// 미세먼지(X01)·도깨비 장난(X11). 카드가 늘면 여기만 늘린다.
        /// </summary>
        private static bool IsEphemeralCurseCard(string cardId)
        {
            return string.Equals(cardId, CardIds.FineDust, StringComparison.Ordinal)
                || string.Equals(cardId, CardIds.GoblinPrank, StringComparison.Ordinal);
        }

        /// <summary>낼 수 있는 저주(T2). 현재 X04 빚 문서뿐 — 늘면 여기가 단일 목록이다.</summary>
        private static bool IsPlayableCurseCard(CardDefinition card)
        {
            return card != null && string.Equals(card.Id, CardIds.DebtNote, StringComparison.Ordinal);
        }

        /// <summary>손패의 특정 저주 카드 장수 — "손에 있는 동안 X" 계열(악몽·지각·원귀·도깨비 장난)의 공통 항.</summary>
        private int CountHandCurse(string cardId)
        {
            return CountCardsInHands(card => string.Equals(card.Id, cardId, StringComparison.Ordinal));
        }

        /// <summary>
        /// 지금 봉인되어 있는 카드 수(C-16 / D-16). 두 출처를 <b>한 숫자로 합산</b>한다:
        /// 상태이상 봉인의 Amount(값 축 <c>SealedCardCount</c>)와 손패에 들고 있는 '정전' 상태 카드 수.
        /// 정전은 지속시간 상태가 아니라 <b>손에 있는 동안</b> 잠그므로(사용자 확정) 여기서 세는 것이
        /// 곧 그 규칙이다 — 별도 턴 훅이 없다.
        /// </summary>
        private int GetSealedCardCount()
        {
            var fromStatus = SumActivePlayerEffectAmountByValueMode(StatusEffectValueMode.SealedCardCount);
            var fromBlackoutCards = CountCardsInHands(card =>
                string.Equals(card.Id, CardIds.Blackout, StringComparison.Ordinal)
                // 도깨비 장난(X11, T2): 정전과 같은 잠금을 아지랑이(턴말 소멸)로 한 턴만 건다.
                || string.Equals(card.Id, CardIds.GoblinPrank, StringComparison.Ordinal));
            return fromStatus + fromBlackoutCards;
        }

        private int CountCardsInHands(Func<CardDefinition, bool> predicate)
        {
            var count = 0;
            for (var i = 0; i < ActionDeck.Hand.Count; i++)
            {
                if (ActionDeck.Hand[i] != null && predicate(ActionDeck.Hand[i]))
                {
                    count++;
                }
            }

            for (var i = 0; i < MovementDeck.Hand.Count; i++)
            {
                if (MovementDeck.Hand[i] != null && predicate(MovementDeck.Hand[i]))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 봉인 대상 후보: 두 손패의 <b>사용 가능한</b> 카드 전부. 이동 카드도 포함한다(O-11 확정) —
        /// 봉인은 카드 종류를 가리지 않으므로 <see cref="IsCardUsable"/>의 Move 조기 분기가
        /// 이 게이트의 <b>네 번째</b> 배선 지점이 된다.
        /// 상태 카드는 제외한다: 이미 사용 불가라 봉인해도 아무것도 잠기지 않는다(봉인 낭비).
        /// </summary>
        private IEnumerable<CardDefinition> SealCandidates()
        {
            return ActionDeck.Hand
                .Concat(MovementDeck.Hand)
                .Where(card => card != null && !IsUnplayableStatusCard(card));
        }

        /// <summary>
        /// 이 카드가 지금 봉인되어 있는가(C-16). 어느 카드가 잠기는지는 <b>저장하지 않고</b>
        /// <c>(InstanceId, OverallTurnNumber)</c>에서 결정적으로 유도한다 — 주기 함정(C-11)과 같은 처방이다.
        ///
        /// 이 설계가 사 온 것 셋:
        /// ①<b>재선정</b>(O-11): 턴이 바뀌면 순위가 통째로 다시 매겨진다.
        /// ②<b>즉시 반응</b>: 정전을 뽑는 순간 봉인 장수가 늘고 그 턴에 바로 한 장이 더 잠긴다.
        /// ③<b>리롤 악용 불가</b>: 순위는 카드마다 독립이라 다른 카드를 내도 남은 카드의 순위가 변하지 않는다.
        ///   (손패 집합을 시드로 썼다면 싼 카드를 한 장 내서 봉인을 다시 뽑을 수 있었다.)
        /// </summary>
        private bool IsSealedCard(CardDefinition card)
        {
            if (card == null || IsUnplayableStatusCard(card))
            {
                return false;
            }

            var sealCount = GetSealedCardCount();
            if (sealCount <= 0)
            {
                return false;
            }

            var target = SealRank(card);
            var lowerRanked = 0;
            foreach (var candidate in SealCandidates())
            {
                if (ReferenceEquals(candidate, card))
                {
                    continue;
                }

                // 동점은 InstanceId로 깬다 — 순서 의존 없이 전역적으로 하나의 순위가 나온다.
                var rank = SealRank(candidate);
                if (rank < target
                    || (rank == target && string.CompareOrdinal(candidate.InstanceId, card.InstanceId) < 0))
                {
                    lowerRanked++;
                }
            }

            return lowerRanked < sealCount;
        }

        /// <summary>
        /// 봉인 순위. 턴이 섞여 있어야 <b>재선정</b>이 되고, 카드 인스턴스가 섞여 있어야
        /// <b>손패가 바뀌어도 남은 카드의 순위가 안 변한다</b>. 결정적이어야 하므로
        /// <c>string.GetHashCode</c>(런타임마다 다를 수 있다) 대신 고정 해시를 쓴다.
        /// </summary>
        private int SealRank(CardDefinition card)
        {
            return StableSealHash(card.InstanceId, OverallTurnNumber);
        }

        /// <summary>
        /// 🔴 <b>턴을 맨 앞에서 섞고 끝에 아발란치를 돌린다</b>. 예전 판은 id를 다 흡수한 뒤
        /// 맨 끝에서 <c>hash ^ turn</c>을 한 번 곱하는 것이 전부였는데, 작은 턴 번호는 낮은
        /// 비트만 건드리므로 곱 한 번으로는 <b>순위를 뒤집지 못했다</b> — 순위가 사실상 id 해시로
        /// 고정돼 O-11(재선정)이 지켜지지 않았다.
        /// 실측(2026-08-31): 손패 6장이 고정일 때 <b>37%가 10턴 내내 같은 카드</b>를 잠갔고,
        /// 고정 id 픽스처에서는 11턴 연속 같은 카드가 나왔다. 이 값은 <b>저장하지 않고</b>
        /// 매번 유도하므로 세이브 와이어 포맷에는 영향이 없다.
        /// </summary>
        private static int StableSealHash(string instanceId, int turn)
        {
            unchecked
            {
                // 턴을 먼저 흩어 놓아야 뒤따르는 id 흡수가 턴마다 다른 궤적을 그린다.
                var hash = (int)(2166136261u ^ (uint)turn * 2654435761u);
                hash *= 16777619;

                var id = instanceId ?? string.Empty;
                for (var i = 0; i < id.Length; i++)
                {
                    hash = (hash ^ id[i]) * 16777619;
                }

                // 최종 아발란치(murmur3 fmix32) — 상위 비트까지 고루 섞어 근접한 두 해시가
                // 턴이 바뀌면 실제로 자리를 바꾸게 한다.
                var mixed = (uint)hash;
                mixed ^= mixed >> 16;
                mixed *= 2246822507u;
                mixed ^= mixed >> 13;
                mixed *= 3266489909u;
                mixed ^= mixed >> 16;
                return (int)(mixed & 0x7fffffff);
            }
        }

        /// <summary>지금 봉인된 카드의 InstanceId 집합(표현·테스트용 투영). 규칙은 이 값을 읽지 않는다.</summary>
        public IReadOnlyList<string> SealedCardInstanceIds =>
            SealCandidates().Where(IsSealedCard).Select(card => card.InstanceId).ToList();

        private bool CanUseActionCard(CardDefinition card, out string reason)
        {
            reason = string.Empty;
            if (IsTerminal)
            {
                reason = "Combat ended. Restart to play again.";
                return false;
            }

            if (card == null)
            {
                reason = "No matching action card is available.";
                return false;
            }

            if (!CanUseCardInCurrentPhase(card))
            {
                reason = card.PhaseAvailability == CardUsePhase.Movement
                    ? "This action card can only be used during player movement."
                    : "Action cards can only be used during player action.";
                return false;
            }

            var restriction = GetCardRestriction(card);
            if (restriction != CardRestriction.None)
            {
                reason = RestrictionFailureText(restriction, isMoveCard: false);
                return false;
            }

            if (GetRequiredKi(card) > ActionCostRemaining)
            {
                reason = "Not enough Ki remains.";
                return false;
            }

            return true;
        }

        private bool CanUseCardInCurrentPhase(CardDefinition card)
        {
            if (card == null)
            {
                return false;
            }

            switch (card.PhaseAvailability)
            {
                case CardUsePhase.Movement:
                    return Phase == CombatPhase.PlayerMovement;
                case CardUsePhase.BothIfApproved:
                    return Phase == CombatPhase.PlayerMovement || Phase == CombatPhase.PlayerAction;
                default:
                    return Phase == CombatPhase.PlayerAction;
            }
        }

        private CardDefinition FindActionCard(CardEffectType effectType)
        {
            return FindActionCard(effectType, string.Empty);
        }

        private CardDefinition FindActionCard(CardEffectType effectType, string cardId)
        {
            return string.IsNullOrEmpty(cardId)
                ? ActionDeck.Hand.FirstOrDefault(candidate => candidate.EffectType == effectType)
                : ActionDeck.Hand.FirstOrDefault(candidate => candidate.EffectType == effectType && MatchesCardKey(candidate, cardId));
        }

        private CardDefinition FindAttackCardForTarget(HexCoord target, string cardId)
        {
            if (!string.IsNullOrEmpty(cardId))
            {
                return FindActionCard(CardEffectType.Attack, cardId);
            }

            return ActionDeck.Hand
                .Select((card, index) => new { card, index })
                .Where(candidate => candidate.card.EffectType == CardEffectType.Attack)
                .Where(candidate => PlayerCoord.DistanceTo(target) <= candidate.card.Range + candidate.card.AreaRadius)
                .OrderBy(candidate => candidate.card.AreaRadius)
                .ThenBy(candidate => candidate.index)
                .Select(candidate => candidate.card)
                .FirstOrDefault()
                ?? FindActionCard(CardEffectType.Attack);
        }

        private static bool MatchesCardKey(CardDefinition card, string cardKey)
        {
            return card != null &&
                   (string.IsNullOrEmpty(cardKey) ||
                    string.Equals(card.InstanceId, cardKey, StringComparison.Ordinal) ||
                    string.Equals(card.Id, cardKey, StringComparison.Ordinal));
        }

        private IReadOnlyList<CardDefinition> ResolveActionHandCards(IReadOnlyList<string> cardKeys)
        {
            if (cardKeys == null || cardKeys.Count == 0)
            {
                return Array.Empty<CardDefinition>();
            }

            var resolved = new List<CardDefinition>();
            foreach (var cardKey in cardKeys)
            {
                var card = ActionDeck.Hand.FirstOrDefault(candidate => MatchesCardKey(candidate, cardKey));
                if (card != null && !resolved.Contains(card))
                {
                    resolved.Add(card);
                }
            }

            return resolved;
        }

        private string CreateRuntimeInstanceId(string cardId, string source)
        {
            var safeSource = string.IsNullOrWhiteSpace(source) ? "runtime" : source.Trim();
            var safeCardId = string.IsNullOrWhiteSpace(cardId) ? "card" : cardId.Trim();
            var prefix = $"{CardCatalog.SourceId}.{safeSource}.{safeCardId}.r";
            while (true)
            {
                var candidate = prefix + (++runtimeCardInstanceSequence).ToString("D4");
                if (!IsCardInstanceIdInUse(candidate))
                {
                    return candidate;
                }
            }
        }

        // 세이브 왕복 뒤 카운터가 0부터 다시 시작하므로, 저장된 덱·소유 목록에 이미 있는 id는 건너뛴다.
        private bool IsCardInstanceIdInUse(string instanceId)
        {
            return PlayerDeck.MovementCards.Any(card => string.Equals(card.InstanceId, instanceId, StringComparison.Ordinal))
                || PlayerDeck.ActionCards.Any(card => string.Equals(card.InstanceId, instanceId, StringComparison.Ordinal))
                || ActiveCardInstanceIds.Contains(instanceId);
        }

        private bool Fail(string reason)
        {
            LastFailureReason = reason;
            LastInvestigateResult = string.Empty;
            return false;
        }

        private void ResolveActionCard(CardDefinition card, CombatCardKind kind)
        {
            SpendCardKi(card);
            ConsumePlayedCard(ActionDeck, card);
            LastDiscardedCard = kind;
            RegisterActionCardUse(kind);
            LastFailureReason = string.Empty;
        }

        /// <summary>
        /// 사용된 카드의 단일 배출 지점(T5-1 「소멸」 통일). <c>exhaustOnPlay</c> 저작 카드는 버림 더미
        /// 대신 소멸 더미로 보낸다. 기존 3경로(additionalCost의 선택 카드 소멸·전용 behaviorId·
        /// 아지랑이의 턴말 purge)는 이 지점을 지나지 않으며 그대로 유지된다 — 신규 저작만 컬럼을 쓴다.
        /// </summary>
        /// <summary>
        /// 카드가 효과를 올릴 때 쓰는 sourceRef(<see cref="CardBehavior.EffectSourceRef"/>). 카탈로그 밖 카드(테스트 픽스처)는
        /// 아직 <see cref="CardDefinition.EffectRef"/>를 돌려준다 — P2-d에서 그 필드가 사라지면 카드 id가 된다.
        /// </summary>
        private static string EffectSourceRefOf(CardDefinition card)
        {
            if (card == null)
            {
                return string.Empty;
            }

            return CardBehaviorRegistry.TryGet(card.Id, out var behavior) ? behavior.EffectSourceRef : card.EffectRef;
        }

        /// <summary>추가 비용(<see cref="CardBehavior.AdditionalCost"/>). 카탈로그 밖 카드는 아직 문자열 컬럼(P2-d에서 제거).</summary>
        private static string AdditionalCostOf(CardDefinition card)
        {
            if (card == null)
            {
                return string.Empty;
            }

            if (CardBehaviorRegistry.TryGet(card.Id, out var behavior))
            {
                return behavior.AdditionalCost;
            }

            return CardBehaviorMetadata.HasToken(card.AdditionalCost, CardBehaviorMetadata.AdditionalCostExileSelectedHandCards)
                ? CardBehaviorMetadata.AdditionalCostExileSelectedHandCards
                : CardBehaviorMetadata.HasToken(card.AdditionalCost, CardBehaviorMetadata.AdditionalCostDiscardSelectedHandCards)
                    ? CardBehaviorMetadata.AdditionalCostDiscardSelectedHandCards
                    : string.Empty;
        }

        /// <summary>선택지(<see cref="CardBehavior.Choices"/>). 카탈로그 밖 카드는 아직 문자열 컬럼(P2-d에서 제거).</summary>
        private static IReadOnlyList<CardBehaviorMetadata.ChoiceOption> ChoicesOf(CardDefinition card)
        {
            if (card == null)
            {
                return Array.Empty<CardBehaviorMetadata.ChoiceOption>();
            }

            return CardBehaviorRegistry.TryGet(card.Id, out var behavior)
                ? behavior.Choices
                : CardBehaviorMetadata.ParseChoiceOptions(card.ChoiceOptions);
        }

        private static int ChoiceDrawCountOf(CardDefinition card)
        {
            return CardBehaviorRegistry.TryGet(card.Id, out var behavior)
                ? behavior.ChoiceDrawCount
                : CardBehaviorMetadata.GetBehaviorParam(card.BehaviorParams, "drawCount", 0);
        }

        private static void ConsumePlayedCard(CardDeckState deck, CardDefinition card)
        {
            if (card != null && card.ExhaustOnPlay)
            {
                deck.PermanentRemoveFromHand(card);
                return;
            }

            deck.DiscardFromHand(card);
        }

        /// <summary>
        /// 행동 카드 사용 1회의 단일 집계 지점 — 흩어져 있던 <c>actionCardsUsedThisTurn++</c>를 전부
        /// 이 함수로 수렴시켰다(T2 페이즈 C). 코인 세탁기의 전투 누적 카운터가 여기서만 오르므로,
        /// 새 카드 경로가 이 함수를 지나치면 턴 집계까지 함께 새서 기존 테스트가 잡는다.
        /// T7-2부터 카드 종류를 받는다 — 약오름(방어·정찰 반응)이 같은 수렴점에서 신호를 세운다.
        /// </summary>
        private void RegisterActionCardUse(CombatCardKind kind)
        {
            actionCardsUsedThisTurn++;
            totalActionCardsUsed++;
            RegisterEnemyGrammarCardUse(kind);
            ResolveCardsUsedRelicTriggers();
        }

        /// <summary>코인 세탁기(T2 페이즈 C): 행동 카드 triggerParam장 사용마다 effectAmount장 드로우.</summary>
        private void ResolveCardsUsedRelicTriggers()
        {
            var items = PlayerInventory?.RelicsAndCurses?.Items;
            if (items == null)
            {
                return;
            }

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.IsActive
                    && item.TriggerKind == RelicTriggerKind.DrawPerCardsUsed
                    && item.TriggerParam > 0
                    && totalActionCardsUsed % item.TriggerParam == 0)
                {
                    // 방금 낸 카드는 이미 버림 더미에 있다(호출부가 Discard 후 집계) — 그 카드를 되뽑을 수 있는
                    // 것까지 포함해 선택 드로우(ChoiceEffectDrawActionCards)와 같은 계약이다.
                    DrawActionCards(Math.Max(0, item.EffectAmount));
                }
            }
        }

        private int GetRequiredKi(CardDefinition card)
        {
            if (card == null)
            {
                return 0;
            }

            switch (card.CostMode)
            {
                case CardCostMode.Free:
                case CardCostMode.SpendAll:
                    return 0;
                case CardCostMode.MaxKi:
                    // Config.MaxKi가 아니라 CombatState.MaxKi를 읽는다 — 유물 MaxKiBonus로 예산이 5가 된
                    // 상황에서 Config(4)를 상한으로 쓰면 A05(최후의 일격)가 남은 1을 못 쓰고 버린다.
                    return ActionCostRemaining > 0
                        ? System.Math.Min(MaxKi, ActionCostRemaining)
                        : MaxKi;
                default:
                    return card.Cost;
            }
        }

        /// <summary>
        /// 이 카드를 지금 쓰면 소모될 기력. 기력을 <b>실제로 깎지 않는다</b> —
        /// SpendAll 카드(최후의 일격)의 수치 미리보기가 이 값을 곱하기 때문에 순수해야 한다.
        /// <see cref="SpendCardKi"/>가 같은 함수를 쓰므로 미리보기와 집행이 갈라질 수 없다.
        /// </summary>
        private int PeekCardKiSpend(CardDefinition card)
        {
            var spend = GetRequiredKi(card);
            if (card != null && card.CostMode == CardCostMode.SpendAll)
            {
                spend = ActionCostRemaining;
            }

            return System.Math.Min(ActionCostRemaining, System.Math.Max(0, spend));
        }

        private int SpendCardKi(CardDefinition card)
        {
            var spend = PeekCardKiSpend(card);
            ActionCostRemaining -= spend;
            return spend;
        }

        private void ApplyObjectiveCompletionResult(CombatObjectiveCompletionResult result)
        {
            if (result.CompletedNow)
            {
                ObjectiveCompleted = true;
            }

            LastInvestigateResult = FormatObjectiveCompletionMessage(result.Outcome);
        }

        private string FormatObjectiveCompletionMessage(CombatObjectiveCompletionOutcome outcome)
        {
            switch (outcome)
            {
                case CombatObjectiveCompletionOutcome.AlreadyComplete:
                    return "Objective already complete.";
                case CombatObjectiveCompletionOutcome.NoObjectiveConfigured:
                    return "Investigated a revealed cell, but no MVP objective target is configured.";
                case CombatObjectiveCompletionOutcome.NonObjectiveTarget:
                    return "Investigated a revealed non-objective cell.";
                case CombatObjectiveCompletionOutcome.TargetNotRevealed:
                    return "Objective target must be revealed before investigation.";
                case CombatObjectiveCompletionOutcome.OutOfRange:
                    return "Objective target is out of investigate range.";
                case CombatObjectiveCompletionOutcome.Completed:
                    return $"Objective complete: investigated the {ObjectiveDisplayName}.";
                default:
                    return string.Empty;
            }
        }


        private string FormatObjectiveFailureReason(CombatObjectiveCompletionOutcome outcome)
        {
            switch (outcome)
            {
                case CombatObjectiveCompletionOutcome.AlreadyComplete:
                    return "Objective already complete.";
                case CombatObjectiveCompletionOutcome.NoObjectiveConfigured:
                    return "No objective target is configured.";
                case CombatObjectiveCompletionOutcome.NonObjectiveTarget:
                    return "Investigate target is not the objective.";
                case CombatObjectiveCompletionOutcome.TargetNotRevealed:
                    return "Objective target must be revealed before investigation.";
                case CombatObjectiveCompletionOutcome.OutOfRange:
                    return "Objective target is out of investigate range.";
                default:
                    return "Objective cannot be completed.";
            }
        }

        private CombatCardSnapshot CreateSnapshot(CardDefinition card, string pile)
        {
            var kind = ToKind(card.EffectType);
            var usable = IsCardUsable(card);
            // 손패만 대상 의존분(허점·표식)을 합류시킨다 — 겨눈 칸이 있을 때만이고, 없으면 null이라
            // 대상 무관분(강화·쇠약·유물·지형)만 반영된다(D-7).
            var value = GetDisplayValue(card, CardPreviewTargetCoord);
            return new CombatCardSnapshot(
                card.Id,
                kind,
                card.DisplayName,
                Describe(card, GetDisplayHitCount(card), value, GetDisplayRange(card), GetDisplayAreaRadius(card)),
                value,
                usable,
                false,
                GetCardStatus(card, usable),
                GetDisplayCost(card),
                card.Range,
                pile,
                card.CatalogSourceId,
                EffectSourceRefOf(card),
                card.PhaseAvailability,
                card.PlayMode,
                card.FieldObjectKind,
                card.DurationTurns,
                card.AreaRadius,
                card.InstanceId,
                card.UpgradeLevel,
                card.IsTemporary,
                card.ChoiceOptions,
                card.ChoiceOptionTexts,
                card.PresentationRef.IllustrationId,
                baseCost: card.Cost,
                targetMode: card.TargetMode,
                isStatusCard: IsUnplayableStatusCard(card),
                baseValue: card.Amount,
                healValue: card.EffectiveHealAmount);
        }

        private CombatCardSnapshot CreateDiscardSnapshot(CardDefinition card, string pile)
        {
            // 버린 더미는 겨눌 수 없으므로 대상 의존분 없이 실효값만 보여준다.
            var value = GetDisplayValue(card);
            return new CombatCardSnapshot(
                card.Id,
                ToKind(card.EffectType),
                card.DisplayName,
                Describe(card, GetDisplayHitCount(card), value, GetDisplayRange(card), GetDisplayAreaRadius(card)),
                value,
                false,
                true,
                "Discard",
                GetDisplayCost(card),
                card.Range,
                pile,
                card.CatalogSourceId,
                EffectSourceRefOf(card),
                card.PhaseAvailability,
                card.PlayMode,
                card.FieldObjectKind,
                card.DurationTurns,
                card.AreaRadius,
                card.InstanceId,
                card.UpgradeLevel,
                card.IsTemporary,
                card.ChoiceOptions,
                card.ChoiceOptionTexts,
                card.PresentationRef.IllustrationId,
                baseCost: card.Cost,
                targetMode: card.TargetMode,
                isStatusCard: IsUnplayableStatusCard(card),
                baseValue: card.Amount,
                healValue: card.EffectiveHealAmount);
        }

        /// <summary>
        /// 목록류(덱 목록 · 잡화점 · 카드 제거 · 카드 연마 · 더미 목록) 한 장.
        ///
        /// <para>🔑 <b>손패와 목록은 쓰임새가 다르다</b>(2026-09-02 #7 · 사용자 확정). 손패는 "지금 쓰면
        /// 얼마인가"를 답해야 하므로 강화·쇠약·지형·유물·손패 수 같은 <b>그 순간의 값</b>을 실시간으로
        /// 보여준다(<see cref="CreateSnapshot"/>). 목록은 "이 카드가 무엇인가"를 답하는 자리라
        /// <b>저작 기본값</b>을 보여줘야 한다 — 전투 상황에 따라 덱 목록의 숫자가 흔들리면 무엇을 사고
        /// 무엇을 연마할지 판단할 기준선이 사라진다.</para>
        ///
        /// <para>종전에는 두 쓰임새가 <b>같은 스냅샷 하나</b>를 나눠 썼다. 판정을 두 벌로 가르는 대신
        /// <b>만드는 자리</b>를 둘로 갈랐다 — 값·타격 수·사거리·비용 네 축이 함께 갈려야 하며,
        /// 하나라도 실시간으로 남으면 「연마 UI에서만 숫자가 다른」 종류의 어긋남이 남는다.</para>
        ///
        /// <para>연마된 카드는 여기서도 <b>연마된</b> 값을 보여준다 — 연마는 카드 자체가 바뀐 것이지
        /// 상황 보정이 아니다(<c>card.Amount</c>가 이미 치환된 값이다).</para>
        /// </summary>
        private CombatCardSnapshot CreateDeckListSnapshot(CardDefinition card, string pile, bool discarded)
        {
            var value = card.Amount;
            return new CombatCardSnapshot(
                card.Id,
                ToKind(card.EffectType),
                card.DisplayName,
                Describe(card, Math.Max(1, card.HitCount), value, card.Range, card.AreaRadius),
                value,
                false,
                discarded,
                pile,
                card.Cost,
                card.Range,
                pile,
                card.CatalogSourceId,
                EffectSourceRefOf(card),
                card.PhaseAvailability,
                card.PlayMode,
                card.FieldObjectKind,
                card.DurationTurns,
                card.AreaRadius,
                card.InstanceId,
                card.UpgradeLevel,
                card.IsTemporary,
                card.ChoiceOptions,
                card.ChoiceOptionTexts,
                card.PresentationRef.IllustrationId,
                baseCost: card.Cost,
                targetMode: card.TargetMode,
                isStatusCard: IsUnplayableStatusCard(card),
                baseValue: card.Amount,
                healValue: card.EffectiveHealAmount);
        }

        private bool IsCardUsable(CardDefinition card)
        {
            if (IsTerminal)
            {
                return false;
            }

            // 상태 제약(상태 카드·봉인·기절/속박·무장 해제)은 단일 술어 한 곳에서 본다.
            if (GetCardRestriction(card) != CardRestriction.None)
            {
                return false;
            }

            if (card.EffectType == CardEffectType.Move)
            {
                if (IsMomentumMovementLocked())
                {
                    return false;
                }

                return Phase == CombatPhase.PlayerMovement && GetRequiredKi(card) <= ActionCostRemaining;
            }

            if (!CanUseCardInCurrentPhase(card) || GetRequiredKi(card) > ActionCostRemaining)
            {
                return false;
            }

            if (card.EffectType != CardEffectType.Attack)
            {
                return true;
            }

            // Self-targeted choice cards can remain usable even without a target in range.
            // Range validation is deferred until an attack-mode choice is selected.
            if (ChoicesOf(card).Any(choice => string.Equals(choice.Target, CardBehaviorMetadata.ChoiceTargetSelf, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return HasLivingMonsterInAttackRange(card.Range + card.AreaRadius);
        }

        private int GetDisplayCost(CardDefinition card)
        {
            if (card == null)
            {
                return 0;
            }

            switch (card.CostMode)
            {
                case CardCostMode.SpendAll:
                    return Math.Max(0, ActionCostRemaining);
                case CardCostMode.MaxKi:
                    return GetRequiredKi(card);
                default:
                    return card.Cost;
            }
        }

        private string GetCardStatus(CardDefinition card, bool isUsable)
        {
            if (IsTerminal)
            {
                return CombatCardStatusText.CombatEnded;
            }

            if (isUsable)
            {
                return CombatCardStatusText.Usable;
            }

            // 상태 제약은 페이즈·사거리보다 앞선다: 그 카드는 어떤 상황에서도 못 쓰므로
            // "사거리 부족" 같은 라벨이 뜨면 고칠 수 있는 문제처럼 읽힌다. 우선순위는 GetCardRestriction 한 곳이 정한다.
            var restrictionText = RestrictionStatusText(GetCardRestriction(card));
            if (restrictionText != null)
            {
                return restrictionText;
            }

            if (card.EffectType == CardEffectType.Move && IsMomentumMovementLocked())
            {
                return CombatCardStatusText.MomentumLocked;
            }

            if (card.EffectType == CardEffectType.Attack && Phase == CombatPhase.PlayerAction && !HasLivingMonsterInAttackRange(card.Range + card.AreaRadius))
            {
                return CombatCardStatusText.AttackOutOfRange;
            }

            if (GetRequiredKi(card) > ActionCostRemaining &&
                ((card.Category == CardCategory.Action && CanUseCardInCurrentPhase(card)) ||
                 (card.Category == CardCategory.Movement && Phase == CombatPhase.PlayerMovement)))
            {
                return CombatCardStatusText.NotEnoughKi;
            }

            return CombatCardStatusText.Waiting;
        }

        /// <summary>
        /// 보정 없는 <b>저작 그대로</b>의 카드 설명. 전투 상태가 없는 표면(보상 화면 등)이 쓴다 —
        /// 그쪽에는 적용할 상태이상도, 겨눌 대상도 없으므로 실효값이라는 개념 자체가 없다.
        /// 전투 중 손패·덱 목록은 <see cref="CreateSnapshot"/> 경로가 실효값을 넘긴다.
        /// </summary>
        internal static string Describe(CardDefinition card)
            => Describe(card, null, null);

        /// <summary>
        /// 저작 설명의 <c>{Damage}</c>·<c>{Shape}</c> 같은 토큰만 <b>저작값</b>으로 채운다.
        /// 키워드 장식(<see cref="CardKeywordDecorator"/>)은 <b>하지 않는다</b> — 카드 얼굴을 채우는
        /// 쪽이 그릴 때 한 번 더 태우므로 여기서 같이 하면 두 번 장식된다.
        /// <para>
        /// 전투 상태가 없는 표면(도감)이 쓴다. 치환하지 않으면 카드에 <c>{Damage}</c>가 글자 그대로
        /// 뜬다 — 도감 P0.5에서 실제로 밟았다.
        /// </para>
        /// </summary>
        public static string ResolveAuthoredDescription(CardDefinition card)
        {
            return string.IsNullOrWhiteSpace(card.Description)
                // 빈 설명은 종류별 템플릿으로 떨어지고, 그 경로는 장식을 태우지 않는다.
                ? Describe(card)
                : ResolveDescriptionTokens(card.Description, card);
        }

        private static string Describe(CardDefinition card, int? hitCountOverride, int? valueOverride, int? rangeOverride = null, int? areaRadiusOverride = null)
        {
            if (!string.IsNullOrWhiteSpace(card.Description))
            {
                // Single choke point: token-resolve then emphasise game keywords (+link for hover tooltip).
                // Keyword decoration is a no-op unless a catalog is active (EditMode/plain surfaces keep raw text).
                return CardKeywordDecorator.Decorate(ResolveDescriptionTokens(card.Description, card, hitCountOverride, valueOverride, rangeOverride, areaRadiusOverride));
            }

            // Authored as templates rather than interpolated strings so the generated fallbacks get the
            // same 조사 agreement as authored card text ("피해 1을", not "피해 1를").
            switch (card.EffectType)
            {
                case CardEffectType.Attack:
                    return ResolveDescriptionTokens("사거리 {Range} 내 적에게 피해 {Damage}를 줍니다.", card, hitCountOverride, valueOverride, rangeOverride, areaRadiusOverride);
                case CardEffectType.Defend:
                    return ResolveDescriptionTokens("방어 {Amount}를 얻습니다.", card, hitCountOverride, valueOverride, rangeOverride, areaRadiusOverride);
                case CardEffectType.Scout:
                    return "지형을 정찰합니다.";
                case CardEffectType.Investigate:
                    return ResolveDescriptionTokens("사거리 {Range} 내 목표를 조사합니다.", card, hitCountOverride, valueOverride, rangeOverride, areaRadiusOverride);
                case CardEffectType.FieldObject:
                    return ResolveDescriptionTokens("{Shape} 범위에 {Duration}턴 동안 배치합니다.", card, hitCountOverride, valueOverride, rangeOverride, areaRadiusOverride);
                case CardEffectType.Buff:
                    return "버프를 적용합니다.";
                case CardEffectType.Utility:
                    return "효과를 적용합니다.";
                default:
                    return ResolveDescriptionTokens("최대 {Range}칸 이동합니다.", card, hitCountOverride, valueOverride, rangeOverride, areaRadiusOverride);
            }
        }

        /// <summary>
        /// 저작값과 달라진 수치를 카드 텍스트에서 강조하는 색(D-6: 숫자와 색이 함께 바뀐다).
        /// 증가/감소를 가르는 이유는 카드가 저작값을 함께 보여주지 않기 때문이다 — 색이 없으면
        /// 플레이어는 6이 원래 6인지 3이 오른 것인지 알 수 없다.
        /// 비용 강조(<c>GameplayCardLaneView.DefaultModifiedCostColor</c>)와 같은 역할의 상수이며,
        /// 런타임은 UiTheme을 읽을 수 없으므로 여기서 상수로 든다.
        /// </summary>
        private const string ModifiedValueUpColorHex = "#6FE07A";
        private const string ModifiedValueDownColorHex = "#FF7A7A";

        private static string FormatValueToken(int effective, int authored)
        {
            var text = effective.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (effective == authored)
            {
                return text;
            }

            var color = effective > authored ? ModifiedValueUpColorHex : ModifiedValueDownColorHex;
            // KoreanParticle.LastMeaningfulChar가 닫는 태그를 건너뛰므로 조사는 숫자가 정한다.
            return $"<color={color}>{text}</color>";
        }

        private static string ResolveDescriptionTokens(string description, CardDefinition card, int? hitCountOverride = null, int? valueOverride = null, int? rangeOverride = null, int? areaRadiusOverride = null)
        {
            // {Range}도 실효값 축이다(2026-08-20 #13): 민첩·둔화·지각(X07)·유물 MovementRangeBonus가
            // 실제 도달 거리를 바꾸는데 카드에는 저작 거리가 그대로 찍혀 있었다. 피해와 같은
            // FormatValueToken을 태우므로 색 강조(증가 초록·감소 빨강)도 함께 붙는다.
            var range = FormatValueToken(rangeOverride ?? card.Range, card.Range);
            var amount = FormatValueToken(valueOverride ?? card.Amount, card.Amount);
            // I-08(WS-I): {Heal}은 heal 축으로 해소한다 — Amount(damage 우선 접힘)로 돌리면 A03의
            // 회복 문안이 damage 값을 말한다.
            var heal = FormatValueToken(card.EffectiveHealAmount, card.EffectiveHealAmount);
            var areaRadius = FormatValueToken(areaRadiusOverride ?? card.AreaRadius, card.AreaRadius);
            var duration = card.DurationTurns.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var hitCount = (hitCountOverride ?? card.HitCount).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var shape = DescribeShape(card, areaRadiusOverride);

            // Resolved through KoreanParticle so a trailing 조사 agrees with what the token actually
            // expanded to: "{Shape}를" over "범위 1" has to print "범위 1을", and "피해 {Damage}를"
            // with damage 1 has to print "피해 1을". Unknown tokens are left in place.
            return KoreanParticle.ResolveTokens(description, token =>
            {
                switch (token)
                {
                    case "Range":
                    case "range":
                    case "AttackRange":
                    case "attackRange":
                        return range;
                    case "Amount":
                    case "amount":
                    case "Damage":
                    case "damage":
                    case "AttackDamage":
                    case "attackDamage":
                    case "Shield":
                    case "shield":
                        return amount;
                    case "Heal":
                    case "heal":
                        return heal;
                    case "Shape":
                    case "shape":
                        return shape;
                    case "ShapeRadius":
                    case "shapeRadius":
                    case "AreaRadius":
                    case "areaRadius":
                        return areaRadius;
                    case "Duration":
                    case "duration":
                        return duration;
                    case "HitCount":
                    case "hitCount":
                        return hitCount;
                    default:
                        return null;
                }
            });
        }

        private int GetDisplayHitCount(CardDefinition card)
        {
            if (card == null)
            {
                return 1;
            }

            if (card.ScalingMode == CardScalingMode.AttackCardsInHand)
            {
                return Math.Max(1, CountAttackCardsInHandBeforeUse());
            }

            // Kept in step with GetAttackHitCount: {HitCount} is what the card text prints, so a scaling mode
            // that only lands in the rules would make the card lie about its own repeat count.
            if (card.ScalingMode == CardScalingMode.ExiledCards)
            {
                return Math.Max(1, CountExiledCards());
            }

            if (IsMultiplyingStrikeCard(card))
            {
                return Math.Max(1, CountMultiplyingStrikeCards());
            }

            return Math.Max(1, card.HitCount);
        }

        private static string DescribeShape(CardDefinition card, int? areaRadiusOverride = null)
        {
            // 범위 이동(M06 도깨비 걸음)의 반경도 민첩·둔화를 탄다 — {Shape}가 저작값을 말하면
            // 「범위 2」라고 쓰인 카드가 실제로는 4칸을 덮는다(#13).
            var effectiveAreaRadius = areaRadiusOverride ?? card.AreaRadius;
            if (effectiveAreaRadius > 0)
            {
                return $"범위 {FormatValueToken(effectiveAreaRadius, card.AreaRadius)}";
            }

            switch (card.ShapeId)
            {
                case AttackShapeLibrary.Line2: return "Line 2";
                case AttackShapeLibrary.Line3: return "Line 3";
                case AttackShapeLibrary.Line4: return "Line 4";
                case AttackShapeLibrary.ConeNear: return "가까운 부채꼴";
                case AttackShapeLibrary.ConeMid: return "중간 부채꼴";
                case AttackShapeLibrary.ConeWide: return "넓은 부채꼴";
                case AttackShapeLibrary.TForward: return "Forward T";
                case AttackShapeLibrary.CrossNear: return "인접 십자";
                case AttackShapeLibrary.CrossFar: return "원거리 십자";
                case AttackShapeLibrary.RingNear: return "인접 고리";
                case AttackShapeLibrary.VSplit: return "V 분할";
                case AttackShapeLibrary.Pincer: return "협공";
                case AttackShapeLibrary.Tremor: return "방사형 진동";
                case AttackShapeLibrary.Single:
                default:
                    return "Single target";
            }
        }
        private static CombatCardKind ToKind(CardEffectType effect)
        {
            switch (effect)
            {
                case CardEffectType.Attack:
                    return CombatCardKind.Attack;
                case CardEffectType.Defend:
                    return CombatCardKind.Defend;
                case CardEffectType.Scout:
                    return CombatCardKind.Scout;
                case CardEffectType.Investigate:
                    return CombatCardKind.Investigate;
                case CardEffectType.FieldObject:
                    return CombatCardKind.FieldObject;
                case CardEffectType.Buff:
                    return CombatCardKind.Buff;
                case CardEffectType.Utility:
                    return CombatCardKind.Utility;
                default:
                    return CombatCardKind.Move;
            }
        }

        private void DiscardRemainingHands()
        {
            // 유지(T5-2): retainOnTurnEnd 카드는 턴말 버림에서 제외돼 손에 남는다.
            // ⚠️ 「자리를 차지한다」는 예전 규칙은 2026-09-02 #6에서 뒤집혔다 — 유지 카드는 정원 <b>밖</b>이며
            //    다음 턴 드로우를 깎지 않는다(<see cref="CountRetainedInHand"/>).
            MovementDeck.DiscardHandExcept(IsRetainedOnTurnEnd);
            ActionDeck.DiscardHandExcept(IsRetainedOnTurnEnd);
        }

        /// <summary>
        /// 손에 남아 있는 유지 카드 수. 턴 시작 드로우가 정원에서 <b>제외</b>할 몫이다.
        ///
        /// <para>🔴🔴 <b>규칙 변경</b>(2026-09-02 #6 · 사용자 확정): 예전에는 유지 카드가 정원을 차지해
        /// 「신규 유입 −1」이 유지의 비용이었고, 2026-08-19 라운드 #11에서 그것이 <b>규칙</b>으로
        /// 판정된 적이 있다. 지금은 뒤집는다 — 유지 카드를 들고 턴을 넘기면 다음 턴 손은
        /// 「유지분 + 정원」이 된다. 그래야 「남긴다」가 손해가 아니라 선택이 된다.</para>
        /// </summary>
        private static int CountRetainedInHand(CardDeckState deck)
        {
            return deck == null ? 0 : deck.Hand.Count(IsRetainedOnTurnEnd);
        }

        private static bool IsRetainedOnTurnEnd(CardDefinition card)
        {
            return card != null && card.RetainOnTurnEnd;
        }

        // 손패는 두 벌(이동/행동)이고 유물도 축을 둘로 나눠 가진다 — "카드를 더 뽑는다"가 어느 덱을
        // 가리키는지 저작으로 정할 수 있어야 정찰형 유물과 기동형 유물이 갈린다.
        private void DrawNewTurnHands()
        {
            // 유지 카드는 정원 밖이다(2026-09-02 #6 규칙 변경) — 목표를 「정원 + 유지분」으로 올려
            // 잡으므로, 유지가 없으면 예전과 정확히 같은 수를 뽑는다.
            MovementDeck.Draw(Math.Max(0,
                GetEffectiveMovementHandSize() + CountRetainedInHand(MovementDeck) - MovementDeck.HandCount));
            DrawActionCards(Math.Max(0,
                GetEffectiveActionHandSize() + CountRetainedInHand(ActionDeck) - ActionDeck.HandCount));
            // 🔴페이즈 전이 훑기만으로는 새 손패를 놓친다 — 턴 시작 드로우는 PlayerMovement로 넘어간
            // <b>뒤에</b> 일어나므로, 이동 페이즈에서 뽑자마자 쓴 카드는 다음 훑기 때 이미 손에 없다.
            SweepCodexSightings();
            PlayerHandsDrawn?.Invoke();
        }

        /// <summary>
        /// 행동 덱 드로우의 <b>단일 이음새</b>. 미련(X06, 2026-08-20 #18)이 「뽑을 시 사용 가능한 기력
        /// 1 감소」로 바뀌면서 트리거가 턴말 잔존 → <b>드로우 시점</b>으로 옮겨졌고, 그 즉시성은
        /// 드로우가 일어나는 모든 자리에서 성립해야 한다(턴 시작 보충 · 갈림길 드로우 · 유물 드로우 ·
        /// U01 재드로우). 드로우 호출을 여기로 모아 두면 새 드로우 경로가 생겨도 저절로 따라온다.
        /// </summary>
        private void DrawActionCards(int count)
        {
            if (count <= 0)
            {
                return;
            }

            var before = CountHandCurse(CardIds.Lingering);
            ActionDeck.Draw(count);
            var drawn = CountHandCurse(CardIds.Lingering) - before;
            if (drawn <= 0)
            {
                return;
            }

            // 이번 턴의 남은 기에서 즉시 깎는다. 하한 0 — 기가 이미 0이면 더 깎을 것이 없다.
            ActionCostRemaining = Math.Max(0, ActionCostRemaining - drawn);
        }

        private int GetEffectiveMovementHandSize()
        {
            return Math.Max(0, Config.MovementHandSize + GetPermanentItemEffectTotal(PlayerPermanentItemEffectKind.MovementHandSizeBonus));
        }

        private int GetEffectiveActionHandSize()
        {
            return Math.Max(0, Config.ActionHandSize + GetPermanentItemEffectTotal(PlayerPermanentItemEffectKind.ActionHandSizeBonus));
        }

        internal MonsterRuntime FindLivingMonsterAt(HexCoord target)
        {
            // P6(§13.4 C-2): 멤버십은 footprint 원판 기준이다. 멀티셀 보스는 7칸 중 어느 칸을 겨냥해도
            // 명중해야 한다. 반경 0 몬스터는 기존 중심 비교와 동일하게 동작한다.
            return monsters.FirstOrDefault(monster => !monster.Combatant.IsDead && IsMonsterOccupying(monster, target));
        }

        // A tile is "hidden-monster" when a living monster stands on it but the player cannot currently see
        // it (anything other than Revealed). Such tiles stay selectable/enterable so fog hides their contents.
        private bool IsHiddenMonsterCell(HexCoord coord)
        {
            return FindLivingMonsterAt(coord) != null
                && visibilityRuntime.GetVisibility(coord) != HexCellVisibility.Revealed;
        }

        // Clones the occupancy map with a single tile's occupant removed, so pathfinding may enter that tile
        // as a terminal destination while every other occupied tile keeps blocking. Returns the shared map
        // unchanged when nothing would be removed.
        private IReadOnlyDictionary<HexCoord, HexCellRuntimeState> BuildPlayerMoveOccupancyExcluding(HexCoord excluded)
        {
            if (!runtimeStates.ContainsKey(excluded))
            {
                return runtimeStates;
            }

            var clone = new Dictionary<HexCoord, HexCellRuntimeState>(runtimeStates);
            clone.Remove(excluded);
            return clone;
        }

        // Augments the player's reachable-move set with hidden-monster tiles, but only where the tile can be
        // reached as a terminal step (entered from an already-reachable neighbour within the move budget).
        // Hidden-monster tiles are never used as transit nodes, so the player cannot path through them.
        private void AddHiddenMonsterTerminalMoveCells(Dictionary<HexCoord, int> reachable, int range)
        {
            if (reachable == null)
            {
                return;
            }

            // Snapshot the transit-valid cells before augmentation so one hidden-monster tile can never serve
            // as the stepping-stone to another.
            var transitCells = new Dictionary<HexCoord, int>(reachable);
            var query = new MovementQuery(PlayerCoord, range, includeStart: true, unitId: PlayerUnitId);
            foreach (var monster in monsters)
            {
                if (monster.Combatant.IsDead)
                {
                    continue;
                }

                var coord = monster.Coord;
                if (visibilityRuntime.GetVisibility(coord) == HexCellVisibility.Revealed || reachable.ContainsKey(coord))
                {
                    continue;
                }

                var occupancy = BuildPlayerMoveOccupancyExcluding(coord);
                var best = int.MaxValue;
                foreach (var neighbor in coord.NeighborsInDirectionOrder())
                {
                    if (!transitCells.TryGetValue(neighbor, out var costToNeighbor))
                    {
                        continue;
                    }

                    if (!HexPathfinder.CanEnter(Map, neighbor, coord, query, occupancy, terrainTraits, out var enterCost))
                    {
                        continue;
                    }

                    var total = costToNeighbor + enterCost;
                    if (total <= range && total < best)
                    {
                        best = total;
                    }
                }

                if (best != int.MaxValue)
                {
                    reachable[coord] = best;
                }
            }
        }

        // Knocks the player back one tile to <paramref name="retreatCoord"/> (the tile travelled immediately
        // before the collision) after colliding with a fog-hidden monster. The move/action turn is not ended;
        // only the player's position changes, with a knockback cue raised for presentation.
        private void KnockbackPlayerToPreviousTile(HexCoord collisionCoord, HexCoord retreatCoord, string sourceUnitId)
        {
            PlayerCoord = retreatCoord;
            ResolveTrapTriggersAt(PlayerCoord);
            RefreshPlayerVision();
            RaiseEffect(
                EffectKind.Knockback,
                collisionCoord,
                0,
                collisionCoord.DistanceTo(retreatCoord),
                PlayerUnitId,
                "knockback",
                sourceUnitId: sourceUnitId,
                sourceActorKind: "monster",
                targetActorKind: "player");
        }

        private bool HasLivingMonsterInAttackRange(int range)
        {
            // 거리 판정은 중심이 아니라 가장 가까운 점유 칸 기준(§13.4 C-3) — 사거리 1로 보스 가장자리를 때린다.
            return monsters.Any(monster => !monster.Combatant.IsDead && GetDistanceToMonster(monster, PlayerCoord) <= range);
        }

        private int GetPlayerTerrainDefenseBonus()
        {
            if (terrainTable == null || !Map.TryGetCell(PlayerCoord, out var cell))
            {
                return 0;
            }

            return terrainTable.GetCombatDefenseBonus(cell.TerrainTypeId);
        }

        private int GetPlayerTerrainAttackBonus()
        {
            if (terrainTable == null || !Map.TryGetCell(PlayerCoord, out var cell))
            {
                return 0;
            }

            return terrainTable.GetCombatAttackBonus(cell.TerrainTypeId);
        }

        internal void UpdateOccupancy()
        {
            runtimeStates.Clear();
            if (!Player.IsDead)
            {
                runtimeStates[PlayerCoord] = new HexCellRuntimeState(PlayerUnitId);
            }

            foreach (var monster in monsters.Where(candidate => !candidate.Combatant.IsDead))
            {
                runtimeStates[monster.Coord] = new HexCellRuntimeState(monster.Id);
                // P6(§13.4 C-1): 멀티셀 보스는 footprint 원판 전체를 점유한다. 먼저 등록된 항목
                // (플레이어·다른 몬스터 중심)은 덮어쓰지 않는다 — 전환 순간의 일시적 겹침에서
                // 점유 주인이 바뀌면 히트테스트·이동이 엉킨다.
                if (IsMultiCellMonster(monster))
                {
                    foreach (var coord in EnumerateMonsterOccupiedCoords(monster))
                    {
                        if (!runtimeStates.ContainsKey(coord))
                        {
                            runtimeStates[coord] = new HexCellRuntimeState(monster.Id);
                        }
                    }
                }
            }

            // Active field objects block movement on their tile (released automatically once expired,
            // since UpdateOccupancy is re-run after every state change).
            foreach (var fieldObject in FieldObjects.Objects.Where(candidate => !candidate.IsExpired))
            {
                if (runtimeStates.TryGetValue(fieldObject.Position, out var existing))
                {
                    existing.TemporaryBlocked = true;
                }
                else
                {
                    runtimeStates[fieldObject.Position] = new HexCellRuntimeState(null, temporaryBlocked: true);
                }
            }

            // 보스 아레나 결계. 반드시 이 재구축 안에 있어야 지속된다 (CombatState.BossArena.cs 참고).
            ApplyBossArenaBarrierToOccupancy();
        }

        private void InitializeMonsters(IEnumerable<MonsterConfig> monsterConfigs, CombatConfig config)
        {
            var configured = monsterConfigs == null
                ? new List<MonsterConfig>()
                : monsterConfigs.ToList();
            // No auto-injected default enemy: a board with no authored spawners starts combat with
            // zero monsters. (Previously a ThreeEyeDog "normal-enemy" was forced in at (2,0), which
            // made monsters appear on maps that authored none.)

            for (var i = 0; i < configured.Count; i++)
            {
                var monster = configured[i];
                var id = string.IsNullOrEmpty(monster.Id) ? $"monster-{i + 1}" : monster.Id;
                var combatant = new CombatantState(id, monster.MaxHp);
                var attackSpeed = 1;
                var movePerTurn = 1;
                MonsterAttackPattern[] attackPatterns = null;
                if (monsterCatalog.TryGetEntry(monster.DefinitionId, out var entry))
                {
                    attackSpeed = entry.AttackSpeed;
                    movePerTurn = entry.MovePerTurn;
                    attackPatterns = entry.AttackPatterns;
                }
                var runtime = new MonsterRuntime(id, monster.StartCoord, combatant, monster.CatalogSourceId, monster.DefinitionId, monster.SpawnRefId, monster.SpawnRole, attackSpeed, attackPatterns, monster.PatrolArea, movePerTurn);
                if (monsterCatalog.TryGetEntry(monster.DefinitionId, out var footprintEntry))
                {
                    runtime.FootprintShape = footprintEntry.FootprintShape;
                }

                monsters.Add(runtime);
            }
        }

        private MonsterRuntimeState CreateMonsterSnapshot(MonsterRuntime monster)
        {
            var attackPattern = monster.CurrentAttackPattern;
            return new MonsterRuntimeState(
                monster.Id,
                monster.Coord,
                monster.Combatant.Hp,
                monster.Combatant.MaxHp,
                monster.Intent,
                !monster.Combatant.IsDead && monster.TurnPlan.IsActive && monster.ActivityState != MonsterActivityState.Dormant,
                monster.CatalogSourceId,
                monster.DefinitionId,
                monster.SpawnRefId,
                monster.SpawnRole,
                monster.AttackSpeed,
                monster.AttackPatternIndex,
                attackPattern.Id,
                attackPattern.DisplayName,
                // 굴림 반영값(저작 원값이 아니라). HUD 피해의 정본은 여전히 예고(R-8)지만, 이
                // 스냅샷 필드가 저작 원값을 들고 있으면 미래의 소비자가 조용히 거짓 수치를 띄운다.
                System.Math.Max(0, attackPattern.Damage
                    + (attackPattern.HasDamageJitter ? monster.AttackDamageRollOffset : 0)),
                attackPattern.Range,
                attackPattern.AreaRadius,
                attackPattern.ShapeId ?? string.Empty,
                attackPattern.EffectRef,
                monster.FsmMemory.State,
                monster.ActivityState,
                monster.LockedFacingIntent,
                block: monster.Combatant.Block,
                agitationStacks: monster.AgitationStacks,
                toughnessSpent: monster.ToughnessSpent,
                stealthRevealTurnsRemaining: monster.StealthRevealTurnsRemaining,
                restoredMoney: monster.RestoredMoney,
                rewardClaimed: monster.RewardClaimed);
        }

        private MonsterCatalogBindingEvidence CreateMonsterCatalogEvidence(MonsterCatalogDefinition catalog)
        {
            var baseEvidence = catalog.CreateBindingEvidence(
                monsters.Select(monster => monster.DefinitionId),
                monsters.Select(monster => monster.SpawnRefId).Where(id => !string.IsNullOrWhiteSpace(id)));
            if (!baseEvidence.IsValid)
            {
                throw new ArgumentException(baseEvidence.FailureReason, nameof(catalog));
            }

            return baseEvidence;
        }

        public static IReadOnlyList<MonsterConfig> ResolveCatalogBoundMonsterConfigs(HexMapData map, IEnumerable<MonsterConfig> explicitMonsterConfigs, CombatConfig config, MonsterCatalogDefinition catalog)
        {
            if (explicitMonsterConfigs != null)
            {
                return explicitMonsterConfigs
                    .Select(monster => BindExplicitMonsterConfig(map, monster, config, catalog))
                    .ToList();
            }

            return ResolveMonsterConfigsFromBoardSpawns(map, config, catalog);
        }

        /// <summary>
        /// <paramref name="hpVarianceSeed"/>: 스폰 시 체력 변주(StS식 개체차)의 시드. null = 변주 없음
        /// (카탈로그 고정값 — 랜덤화 off 스테이지·구세이브·기존 테스트 전부 불변). 값이 있으면
        /// <c>hpVariancePct</c>가 저작된 몬스터만 ±% 범위에서 굴린다. 굴림은 스폰 id 기반이라 스폰
        /// 목록 순서와 무관하게 결정적이다 — 같은 시드 = 같은 체력(런 세이브 재현 계약). 서스펜드
        /// 재개는 몬스터 상태를 스냅샷 통째 복원하므로 이 경로를 다시 타지 않는다.
        /// </summary>
        public static IReadOnlyList<MonsterConfig> ResolveMonsterConfigsFromBoardSpawns(HexMapData map, CombatConfig config, MonsterCatalogDefinition catalog, int? hpVarianceSeed = null)
        {
            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            catalog = catalog ?? CreateMonsterCatalog(config);
            var catalogEvidence = catalog.CreateBindingEvidence();
            if (!catalogEvidence.IsValid)
            {
                throw new ArgumentException(catalogEvidence.FailureReason, nameof(catalog));
            }

            var spawnRefs = map.MonsterSpawnRefs == null ? new List<HexMonsterSpawnRef>() : map.MonsterSpawnRefs.Where(spawnRef => spawnRef.IsConfigured).ToList();
            if (spawnRefs.Count == 0)
            {
                throw new ArgumentException("Board data has no configured monster spawn refs.", nameof(map));
            }

            var configs = new List<MonsterConfig>();
            foreach (var spawnRef in spawnRefs)
            {
                if (!catalog.TryGetEntry(spawnRef.MonsterId, out var entry))
                {
                    throw new ArgumentException($"Monster spawn ref '{spawnRef.Id}' references missing catalog monster '{spawnRef.MonsterId}'.", nameof(map));
                }

                if (!map.TryGetCell(spawnRef.Coord, out var cell) || !cell.BaseWalkable || map.HasMovementBlockingObject(spawnRef.Coord))
                {
                    throw new ArgumentException($"Monster spawn ref '{spawnRef.Id}' targets missing, unwalkable, or movement-blocked coordinate {spawnRef.Coord}.", nameof(map));
                }

                configs.Add(new MonsterConfig(
                    spawnRef.Id,
                    spawnRef.Coord,
                    ResolveSpawnHp(entry, spawnRef.Id, config, hpVarianceSeed, spawnRef.SpawnRole),
                    catalog.SourceId,
                    entry.Id,
                    spawnRef.Id,
                    spawnRef.SpawnRole,
                    ResolvePatrolArea(map, spawnRef)));
            }

            return configs;
        }

        /// <summary>
        /// 스폰 시 체력 결정. 변주는 반올림 경계 [round(hp·(100−v)/100), round(hp·(100+v)/100)]의
        /// 균일 정수 추첨, 하한 1. 시드는 스폰 id의 FNV-1a 해시와 섞으므로 스폰 목록 순서·다른
        /// 몬스터의 굴림에 영향받지 않는다.
        /// </summary>
        private static int ResolveSpawnHp(MonsterCatalogEntry entry, string spawnRefId, CombatConfig config, int? hpVarianceSeed, string spawnRole = "")
        {
            var baseHp = entry.Hp > 0 ? entry.Hp : config.EnemyMaxHp;

            // 엘리트 강화(#12): 배율은 <b>변주 이전</b>의 기준 체력에 곱한다 — 변주 뒤에 곱하면
            // 같은 시드에서 엘리트만 변주 폭이 함께 부풀어 "가끔 두 배로 단단한 엘리트"가 나온다.
            if (MonsterSpawnRoles.IsElite(spawnRole) && config.EliteHpPercent != 100)
            {
                baseHp = Math.Max(1, (int)Math.Round(baseHp * config.EliteHpPercent / 100.0, MidpointRounding.AwayFromZero));
            }

            if (!hpVarianceSeed.HasValue || !entry.HasHpVariance)
            {
                return baseHp;
            }

            var min = Math.Max(1, (int)Math.Round(baseHp * (100 - entry.HpVariancePct) / 100.0, MidpointRounding.AwayFromZero));
            var max = Math.Max(min, (int)Math.Round(baseHp * (100 + entry.HpVariancePct) / 100.0, MidpointRounding.AwayFromZero));
            var rng = new Random(MixSpawnHpSeed(hpVarianceSeed.Value, spawnRefId));
            return min + rng.Next(max - min + 1);
        }

        private static int MixSpawnHpSeed(int seed, string spawnRefId)
        {
            unchecked
            {
                // FNV-1a. string.GetHashCode는 런타임 간 안정이 보장되지 않아 시드 재현 계약에 못 쓴다.
                var hash = 2166136261u;
                foreach (var ch in spawnRefId ?? string.Empty)
                {
                    hash ^= ch;
                    hash *= 16777619u;
                }

                hash ^= (uint)seed;
                hash *= 16777619u;
                hash ^= hash >> 15;
                return (int)hash;
            }
        }


        private static IReadOnlyList<HexCoord> ResolvePatrolArea(HexMapData map, HexMonsterSpawnRef spawnRef)
        {
            if (string.IsNullOrWhiteSpace(spawnRef.PatrolAreaId))
            {
                return Array.Empty<HexCoord>();
            }

            var area = map.PatrolAreas.FirstOrDefault(candidate => candidate.Id == spawnRef.PatrolAreaId);
            if (!area.IsConfigured)
            {
                throw new ArgumentException($"Monster spawn ref '{spawnRef.Id}' references missing patrol area '{spawnRef.PatrolAreaId}'.", nameof(map));
            }

            return area.Coords;
        }

        private static MonsterConfig BindExplicitMonsterConfig(HexMapData map, MonsterConfig monster, CombatConfig config, MonsterCatalogDefinition catalog)
        {
            catalog = catalog ?? CreateMonsterCatalog(config);
            var definitionId = string.IsNullOrWhiteSpace(monster.DefinitionId) || monster.DefinitionId == monster.Id ? CombatCatalogFactory.ThreeEyeDogMonsterId : monster.DefinitionId;
            if (!catalog.TryGetEntry(definitionId, out var entry))
            {
                throw new ArgumentException($"Monster config '{monster.Id}' references missing catalog monster '{definitionId}'.", nameof(monster));
            }

            EnsureSpawnable(map, monster.StartCoord, $"Monster config '{monster.Id}'");
            return new MonsterConfig(
                monster.Id,
                monster.StartCoord,
                monster.MaxHp > 0 ? monster.MaxHp : entry.Hp,
                string.IsNullOrWhiteSpace(monster.CatalogSourceId) ? catalog.SourceId : monster.CatalogSourceId,
                entry.Id,
                monster.SpawnRefId,
                monster.SpawnRole,
                monster.PatrolArea);
        }

        private static void EnsureSpawnable(HexMapData map, HexCoord coord, string label)
        {
            if (map == null)
            {
                return;
            }

            if (!map.TryGetCell(coord, out var cell) || !cell.BaseWalkable || map.HasMovementBlockingObject(coord))
            {
                throw new ArgumentException($"{label} targets missing, unwalkable, or movement-blocked coordinate {coord}.", nameof(map));
            }
        }

        public static MonsterCatalogDefinition CreateMonsterCatalog(CombatConfig config)
        {
            return CombatCatalogFactory.CreateMonsterCatalog(config);
        }

        public static CardCatalogDefinition CreateApprovedCardCatalog(CombatConfig config)
        {
            return ApprovedCardCatalogFactory.CreateApprovedCatalog(config);
        }

        public static CardCatalogDefinition CreateCardCatalog(CombatConfig config)
        {
            return CombatCatalogFactory.CreateCardCatalog(config);
        }

        private static void NoShuffle<T>(IList<T> cards)
        {
        }

        // Builds a gameplay deck for the given category. When <paramref name="shuffle"/> is true the
        // opening draw pile is randomized so the starting hand differs every play; tests leave it false
        // to keep deterministic ordering. Decks passed explicitly into CombatState are never touched.
        private CardDeckState CreateDeck(CardCategory category, bool shuffle)
        {
            var deck = new CardDeckState(PlayerDeck.CreateDeck(CardCatalog, category), DeckShuffleFor(category));
            if (shuffle)
            {
                deck.ShuffleDrawPile();
            }

            return deck;
        }

        /// <summary>
        /// 덱 셔플 난수원(P2). 이동덱·행동덱은 <b>다른 스트림</b>이다 — 한쪽의 재셔플 횟수가
        /// 다른 쪽의 순서를 밀면 「카드 한 장 추가」가 판 전체를 바꾼다. 무시드면 null(덱이 무시드 폴백).
        /// </summary>
        private Action<IList<CardDefinition>> DeckShuffleFor(CardCategory category)
        {
            if (!RunSeed.HasValue)
            {
                return null;
            }

            // 셔플 난수원은 상태가 들고 있어야 커서를 읽어 저장할 수 있다(P5). 처음 요청될 때 연다.
            var rng = category == CardCategory.Movement
                ? movementShuffleRng ??= CreateStreamRandom(RunSeedStreams.MovementDeckShuffle)
                : actionShuffleRng ??= CreateStreamRandom(RunSeedStreams.ActionDeckShuffle);
            return CardDeckState.CreateSeededShuffle(rng);
        }

        private DeferredMonsterActionSnapshot CaptureDeferredMonsterActionSnapshot()
        {
            return new DeferredMonsterActionSnapshot(Player.Hp, Player.Block, PlayerCoord, Phase, activeEffects.All);
        }

        public static HexMapData CreateDemoMap(int radius)
        {
            var cells = new List<HexCellData>();
            for (var q = -radius; q <= radius; q++)
            {
                for (var r = -radius; r <= radius; r++)
                {
                    var coord = new HexCoord(q, r);
                    if (System.Math.Abs(coord.S) <= radius)
                    {
                        var blocked = coord == new HexCoord(1, -1);
                        cells.Add(new HexCellData(coord, blocked ? "m2-blocked" : "m2-ground", "demo", 1, !blocked, false));
                    }
                }
            }

            return new HexMapData(cells);
        }
    }
}




using System.Collections.Generic;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// PlayerStateTest presentation bridge. When the live <see cref="CombatState"/> exposes
    /// <see cref="CombatState.EffectResolved"/> (the normal case), the bridge is event-driven: each
    /// resolved action is projected onto its exact tile via <see cref="MapCombatController"/> and
    /// replayed through the existing <see cref="EffectPresentationController"/> — AoE events
    /// (radius &gt; 0) render a range highlight over the hex disk plus a center burst, single-target
    /// events render one burst. If no event source is present it falls back to diffing player state
    /// (<see cref="PlayerStateEffectDeltaResolver"/>) and monster state
    /// (<see cref="CombatTileEffectDeltaResolver"/>). It never duplicates the particle system and
    /// never mutates combat rules.
    /// </summary>
    public sealed class PlayerStateEffectPresentationBridge : MonoBehaviour
    {
        [SerializeField] private MapCombatController controller;
        [SerializeField] private EffectPresentationController presentation;
        [Tooltip("Optional world anchor for spawned effects. When unset the effect is placed from the player's hex position.")]
        [SerializeField] private Transform effectAnchor;
        [SerializeField] private bool playOnUpdate = true;
        [SerializeField] private bool snapFacingToHexSides = true;
        [SerializeField] private float hexFacingYawOffset = CombatFacingUtility.DefaultHexSideYawOffset;

        private readonly PlayerStateEffectDeltaResolver resolver = new PlayerStateEffectDeltaResolver();
        private readonly CombatTileEffectDeltaResolver tileResolver = new CombatTileEffectDeltaResolver();
        private CombatState subscribedState;
        private bool useEventSource;
        private int playedEffectCount;

        public MapCombatController Controller => controller;
        public EffectPresentationController Presentation => presentation;
        public Transform EffectAnchor => effectAnchor;
        public bool HasBaseline => resolver.HasBaseline;

        /// <summary>Total number of effects this bridge has requested since the last <see cref="ResetBaseline"/>; test hook.</summary>
        public int PlayedEffectCount => playedEffectCount;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            ResetBaseline();
            EnsureEventSubscription();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Update()
        {
            EnsureEventSubscription();
            if (playOnUpdate && !useEventSource)
            {
                Tick();
            }
        }

        /// <summary>True once the bridge is driven by CombatState.EffectResolved events (vs the state-diff fallback).</summary>
        public bool UsesEventSource => useEventSource;

        /// <summary>Explicitly wire references and re-baseline; mirrors the other PlayerStateTest bridges.</summary>
        public void Bind(MapCombatController mapCombatController, EffectPresentationController effectPresentation, Transform anchor = null)
        {
            controller = mapCombatController;
            presentation = effectPresentation;
            if (anchor != null)
            {
                effectAnchor = anchor;
            }

            ResolveReferences();
            ResetBaseline();
            EnsureEventSubscription();
        }

        /// <summary>Re-capture the current snapshot as baseline so no pending delta is replayed.</summary>
        public void ResetBaseline()
        {
            playedEffectCount = 0;
            ResolveReferences();
            if (controller != null && controller.State != null)
            {
                resolver.CaptureBaseline(controller.State.CreatePlayerStateSnapshot());
                tileResolver.CaptureBaseline(CaptureMonsterSnapshots());
            }
            else
            {
                resolver.Reset();
                tileResolver.Reset();
            }
        }

        /// <summary>
        /// Resolve and play any pending player and monster/tile deltas. Returns the number of effects
        /// played this call so tests can assert one-shot playback per delta. The first tick after a
        /// reset only captures the baseline and plays nothing.
        /// </summary>
        public int Tick()
        {
            ResolveReferences();
            if (controller == null || controller.State == null || presentation == null)
            {
                return 0;
            }

            var playerEvents = resolver.Resolve(controller.State.CreatePlayerStateSnapshot());
            foreach (var resultEvent in playerEvents)
            {
                Play(resultEvent);
            }

            var tileEvents = tileResolver.Resolve(CaptureMonsterSnapshots());
            foreach (var resultEvent in tileEvents)
            {
                Play(resultEvent);
            }

            var played = playerEvents.Count + tileEvents.Count;
            playedEffectCount += played;
            return played;
        }

        private List<MonsterEffectSnapshot> CaptureMonsterSnapshots()
        {
            var snapshots = new List<MonsterEffectSnapshot>();
            if (controller == null || controller.State == null)
            {
                return snapshots;
            }

            foreach (var monster in controller.State.Monsters)
            {
                snapshots.Add(new MonsterEffectSnapshot(monster.Id, monster.Coord, monster.Hp, monster.IsDead));
            }

            return snapshots;
        }

        private void Play(EffectResultEvent resultEvent)
        {
            if (TryResolveWorldPosition(resultEvent, out var worldPosition))
            {
                if (TryResolveFacingRotation(resultEvent, worldPosition, out var facingRotation))
                {
                    presentation.Play(resultEvent, worldPosition, facingRotation);
                }
                else
                {
                    presentation.Play(resultEvent, worldPosition);
                }
            }
            else
            {
                presentation.Play(resultEvent);
            }
        }

        /// <summary>
        /// Resolve the exact world position for an effect. Prefer projecting the event's target hex
        /// through the live map view so effects land on the real player/tile; fall back to an explicit
        /// serialized anchor, otherwise let the presentation controller place it.
        /// </summary>
        private bool TryResolveWorldPosition(EffectResultEvent resultEvent, out Vector3 worldPosition)
        {
            var entry = ResolveVfxEntry(resultEvent);
            if (TryResolveCatalogSpawnAnchorWorldPosition(entry, resultEvent, out worldPosition))
            {
                return true;
            }

            if (!EffectVfxAnchorPolicy.IsAreaLike(resultEvent) &&
                controller != null &&
                controller.TryGetCombatantVfxAnchorWorldPosition(
                    resultEvent.TargetUnitId,
                    EffectVfxAnchorPolicy.ResolveTargetAnchor(entry, resultEvent),
                    out worldPosition))
            {
                return true;
            }

            if (controller != null && resultEvent.Center.HasValue
                && controller.TryGetTileWorldPosition(resultEvent.Center.Value, out worldPosition))
            {
                return true;
            }

            if (effectAnchor != null)
            {
                worldPosition = effectAnchor.position;
                return true;
            }

            worldPosition = default;
            return false;
        }

        /// <summary>
        /// Subscribe to the current CombatState's EffectResolved event, re-subscribing when the
        /// controller rebuilds its state (InitializeIntegration/RestartDemo). Once subscribed, events
        /// drive playback and the state-diff path is disabled to avoid double-firing.
        /// </summary>
        private void EnsureEventSubscription()
        {
            ResolveReferences();
            var state = controller != null ? controller.State : null;
            if (ReferenceEquals(state, subscribedState))
            {
                return;
            }

            Unsubscribe();
            subscribedState = state;
            if (state != null)
            {
                state.EffectResolved += OnEffectResolved;
                useEventSource = true;
            }

            ResetBaseline();
        }

        private void Unsubscribe()
        {
            if (subscribedState != null)
            {
                subscribedState.EffectResolved -= OnEffectResolved;
                subscribedState = null;
            }

            useEventSource = false;
        }

        private void OnEffectResolved(EffectResultEvent resultEvent)
        {
            // During an impact-synced attack flush, the impact VFX + floating damage number can lead/trail the
            // other channels by the visual offset. Non-attack effects never flush, so they present instantly.
            var visualImpactDelay = ResolveVisualImpactDelay();
            if (visualImpactDelay > 0f && isActiveAndEnabled)
            {
                StartCoroutine(PresentEffectAfterDelay(resultEvent, visualImpactDelay));
                return;
            }

            PresentEffect(resultEvent);
        }

        /// <summary>
        /// 덱에 섞인 저주 카드를 한 번 보여주고 뽑을 더미로 빨아들인다(2026-09-01 #9).
        /// 카드 얼굴은 <c>SourceCardId</c>로 카탈로그에서 찾는다 — 신호가 id를 나르는 이유가 이것이다.
        /// </summary>
        private void PresentStatusCardInjection(EffectResultEvent resultEvent)
        {
            var state = controller != null ? controller.State : null;
            if (state == null || string.IsNullOrWhiteSpace(resultEvent.SourceCardId))
            {
                return;
            }

            if (!state.TryCreateCatalogCardSnapshot(resultEvent.SourceCardId, out var snapshot))
            {
                return;
            }

            controller.CurseCardInjectionPresenter.Play(snapshot);
        }

        private float ResolveVisualImpactDelay()
        {
            var profile = controller != null ? controller.TimingProfile : null;
            if (profile == null || !profile.AlignImpactToAnimation)
            {
                return 0f;
            }

            if (subscribedState == null || !subscribedState.IsFlushingBufferedEffects)
            {
                return 0f;
            }

            // Honor the per-attack visual offset published by the controller for the in-progress flush
            // (equals the global profile value when no per-attack timing is active).
            return controller.ActiveVisualImpactDelay;
        }

        private System.Collections.IEnumerator PresentEffectAfterDelay(EffectResultEvent resultEvent, float delay)
        {
            yield return new WaitForSeconds(delay);
            PresentEffect(resultEvent);
        }

        private void PresentEffect(EffectResultEvent resultEvent)
        {
            if (presentation == null)
            {
                return;
            }

            // "기습!" 경고는 타임라인의 AmbushAlert 비트가 단일 정본이다(CombatTimelineAssembler —
            // 페이즈당 1회·빗맞음 제외·이동 전/공격 시점 가시성 판정). 예전에는 여기서도 피해
            // 이벤트마다 발원지 미가시를 따져 텍스트를 띄웠는데, 타임라인이 같은 이벤트를 재생하므로
            // 진짜 기습은 두 번 뜨고, 다단 히트·재생 시점 시야 변화에서는 오발했다(2026-08-19 #7·#9).

            // 저주 주입(2026-09-01 #9)은 파티클이 아니라 <b>카드 그 자체</b>가 그림이다 — 카탈로그에
            // 엔트리가 없으므로 여기서 갈라 전용 연출로 보내고 되돌아간다. 배선이 없으면 연출만
            // 접히고 규칙은 이미 끝났다(전리품 비행과 같은 계약).
            if (resultEvent.Kind == EffectKind.StatusCardInjected)
            {
                PresentStatusCardInjection(resultEvent);
                playedEffectCount++;
                return;
            }

            // Area-effect footprint flash (shader hex overlay) — the replacement for the legacy quad
            // tile highlights. Fired here so it shares this event's impact-aligned presentation
            // moment, and before the multi-entry branch so authored multi-cue effects flash too.
            // Which events flash (and how) is decided entirely by CombatTileFlashStyles — including
            // monster attack impacts, which flash the attacker's pattern footprint even at radius 0,
            // so the gate here is only "has a board position".
            if (resultEvent.Center.HasValue && controller != null)
            {
                controller.PlayAreaEffectFlash(resultEvent);
            }

            // 2026-08-20 #14: 빗맞음에도 <b>공격 VFX</b>는 태운다. 패턴 큐는 전부 Damage·Player로
            // 구워져 있어 AttackMissed 이벤트로는 카탈로그가 못 잡는다 — 조회용 프로브(kind=Damage·
            // target=player)로 명중과 같은 바인딩을 해소하되, 공격자 몸에서 나가는 Source 앵커 큐만
            // 남긴다. 진짜 Damage 이벤트는 존재하지 않으므로 피격 VFX(Target 앵커 큐)·데미지 숫자·
            // HitStop은 구조적으로 재생 불가 — 요구사항 그대로다. 텍스트는 표시용 이벤트가
            // AttackMissed를 유지하므로 「빗맞음」 한 줄만 남는다.
            if (TryResolveWhiffAttackVfxEntries(resultEvent, out var whiffEntries))
            {
                PlayResolvedEntries(BuildWhiffDisplayEvent(resultEvent), whiffEntries);
                playedEffectCount++;
                return;
            }

            var entries = ResolveVfxEntries(resultEvent);
            if (TryPresentPerTileArea(resultEvent, entries))
            {
                playedEffectCount++;
                return;
            }

            if (entries.Length > 1)
            {
                PlayResolvedEntries(resultEvent, entries);
                playedEffectCount++;
                return;
            }

            if (!TryResolveWorldPosition(resultEvent, out var centerWorld))
            {
                return;
            }

            var hasFacingRotation = TryResolveFacingRotation(resultEvent, centerWorld, out var facingRotation);

            // Player movement dust (CVM cues: Push, SourceGround, player self-push) must trail the marker
            // as it animates from->to, not stay pinned to the start tile. Spawn it following the player's
            // live ground anchor instead of a fixed world point. Other cues keep the one-shot placement.
            var moveEntry = ResolveVfxEntry(resultEvent);
            if (IsPlayerMovementCue(moveEntry, resultEvent))
            {
                // A self-push has source==target==player, so the generic source->target facing degenerates to
                // identity and the dust loses its orientation (the VFX lab previews it facing the move
                // direction, so the runtime looked reversed). Recover the real move direction from the event's
                // origin tile (Center) to the player's now-updated coord, then face the dust opposite that
                // travel direction so the burst trails behind the mover. Fall back to the generic facing if
                // the direction is unknown.
                var moveFacing = TryResolvePlayerMoveFacing(resultEvent, out var moveRotation)
                    ? moveRotation
                    : (hasFacingRotation ? facingRotation : Quaternion.identity);
                presentation.PlayFollowing(
                    resultEvent,
                    MakeSourceAnchorFollowProvider(moveEntry, resultEvent, centerWorld),
                    moveFacing);
                playedEffectCount++;
                return;
            }

            if (hasFacingRotation)
            {
                presentation.Play(resultEvent, centerWorld, facingRotation, ResolveSourceFollowAnchor(moveEntry, resultEvent));
            }
            else
            {
                presentation.Play(resultEvent, centerWorld);
            }

            playedEffectCount++;
        }

        // Resolves the live source-actor anchor transform for cues authored followSourceAnchor, so the
        // spawned one-shot can keep tracking the anchor (e.g. a breath cone sweeping with the monster's
        // head). Null for every other cue — the presentation controller then keeps the fixed placement.
        private Transform ResolveSourceFollowAnchor(EffectVfxCatalog.Entry entry, EffectResultEvent resultEvent)
        {
            if (entry == null || !entry.FollowSourceAnchor || controller == null)
            {
                return null;
            }

            var anchorKind = EffectVfxAnchorPolicy.ResolveSourceAnchor(entry, resultEvent);
            if (!string.IsNullOrWhiteSpace(resultEvent.SourceUnitId) &&
                controller.TryGetCombatantVfxAnchor(resultEvent.SourceUnitId, anchorKind, out var anchor))
            {
                return anchor;
            }

            if (!string.IsNullOrWhiteSpace(resultEvent.SourceRef) &&
                controller.TryGetCombatantVfxAnchor(resultEvent.SourceRef, anchorKind, out anchor))
            {
                return anchor;
            }

            return null;
        }

        private readonly List<Vector3> areaTileWorldScratch = new List<Vector3>();

        // Tile-anchored area VFX (mode C): when any resolved cue is authored PerTile, project the
        // event's committed footprint — the same resolution the tile flash uses, so the flashing
        // tiles and the spawning tiles cannot diverge — and hand the whole set to PlayArea. When the
        // footprint or the center can't be resolved the event falls through to the normal paths.
        private bool TryPresentPerTileArea(EffectResultEvent resultEvent, IReadOnlyList<EffectVfxCatalog.Entry> entries)
        {
            if (!EffectPresentationController.HasPerTileAreaEntry(entries) ||
                controller == null ||
                !controller.TryGetAreaEffectFootprintWorldPositions(resultEvent, areaTileWorldScratch) ||
                !TryResolveWorldPosition(resultEvent, out var centerWorld))
            {
                return false;
            }

            var facing = TryResolveFacingRotation(resultEvent, centerWorld, out var facingRotation)
                ? facingRotation
                : Quaternion.identity;
            presentation.PlayArea(resultEvent, centerWorld, areaTileWorldScratch, facing);
            return true;
        }

        private void PlayResolvedEntries(EffectResultEvent resultEvent, IReadOnlyList<EffectVfxCatalog.Entry> entries)
        {
            var playedAny = false;
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null)
                {
                    continue;
                }

                Vector3 entryWorldPosition;
                if (!TryResolveWorldPosition(entry, resultEvent, out entryWorldPosition))
                {
                    continue;
                }

                var facing = TryResolveFacingRotation(entry, resultEvent, entryWorldPosition, out var facingRotation)
                    ? facingRotation
                    : Quaternion.identity;
                presentation.PlayResolvedEntry(
                    resultEvent,
                    entry,
                    entryWorldPosition,
                    facing,
                    showFloatingText: !playedAny,
                    sourceFollowAnchor: ResolveSourceFollowAnchor(entry, resultEvent));
                playedAny = true;
            }

            if (!playedAny && TryResolveWorldPosition(resultEvent, out var fallbackWorldPosition))
            {
                var facing = TryResolveFacingRotation(resultEvent, fallbackWorldPosition, out var facingRotation)
                    ? facingRotation
                    : Quaternion.identity;
                presentation.Play(resultEvent, fallbackWorldPosition, facing);
            }
        }

        private bool TryResolveFacingRotation(EffectResultEvent resultEvent, Vector3 targetWorld, out Quaternion rotation)
        {
            return TryResolveFacingRotation(ResolveVfxEntry(resultEvent), resultEvent, targetWorld, out rotation);
        }

        private bool TryResolveFacingRotation(EffectVfxCatalog.Entry entry, EffectResultEvent resultEvent, Vector3 targetWorld, out Quaternion rotation)
        {
            // 🔴🔴 공격자의 몸이 실제로 보는 방향이 <b>정본</b>이다(2026-09-02 #4 · 사용자 확정:
            //   "몬스터가 공격 시 플레이어를 바라보게 돌리던 설정을 없앴는데 VFX 쪽에 그 회전이 남아 있다").
            //
            //   <para>종전에는 source 앵커 VFX를 <b>대상 유닛의 현재 좌표</b> 쪽으로 돌렸다. 몬스터 몸은
            //   커밋된 겨눈 칸(<c>AimCoord</c>)을 보므로, 겨눈 칸과 플레이어의 실좌표가 다른 순간
            //   (플레이어가 예고 뒤 움직였거나, 형상 공격이 칸을 겨눈 경우) 몸과 이펙트가 서로 다른 쪽을 본다.
            //   이제 <see cref="MapCombatController.TryGetCombatantFacingDirection"/>로 <b>몸을 돌린 그 값을
            //   되읽어</b> 쓰므로 둘이 갈라질 수 없다.</para>
            //
            //   <para>축은 그대로 「공격 방향 공간」이라 VFX 랩에서 저작한 오프셋·회전은 유효하다 —
            //   바뀐 것은 그 축의 정본이 「대상이 지금 어디 있나」에서 「공격자가 어디를 보나」로 옮겨간 것뿐이다.
            //   몸 방향을 못 읽는 경우(마커 없음·랩 문맥)는 예전 동작으로 떨어진다.</para>
            if (TryResolveSourceWorldPosition(entry, resultEvent, out var sourceWorld))
            {
                if (IsSourceAnchored(entry) &&
                    controller != null &&
                    controller.TryGetCombatantFacingDirection(resultEvent.SourceUnitId, out var bodyForward))
                {
                    rotation = CombatFacingUtility.ResolveHexSideRotation(
                        bodyForward, snapFacingToHexSides, hexFacingYawOffset);
                    return true;
                }

                // Source-anchored VFX (e.g. a monster's attack burst) spawn at the attacker, so facing
                // toward the spawn point would degenerate to identity and apply authored offsets/rotations
                // in world space. Face the actual target unit instead so they live in attack-direction
                // space and stay consistent regardless of where the target stands (matching the VFX lab).
                var facingTarget = targetWorld;
                if (IsSourceAnchored(entry) &&
                    TryResolveFacingTargetWorldPosition(entry, resultEvent, out var targetUnitWorld))
                {
                    facingTarget = targetUnitWorld;
                }

                var direction = facingTarget - sourceWorld;
                direction.y = 0f;
                if (direction.sqrMagnitude > 0.0001f)
                {
                    rotation = CombatFacingUtility.ResolveHexSideRotation(direction, snapFacingToHexSides, hexFacingYawOffset);
                    return true;
                }
            }

            rotation = Quaternion.identity;
            return false;
        }

        private static bool IsSourceAnchored(EffectVfxCatalog.Entry entry)
        {
            return entry != null &&
                   (entry.SpawnAnchor == EffectVfxSpawnAnchor.SourceAttack ||
                    entry.SpawnAnchor == EffectVfxSpawnAnchor.SourceGround);
        }

        /// <summary>이 이벤트가 몬스터 패턴의 빗맞음인가(#14) — 공격 VFX 정규화의 진입 조건.</summary>
        internal static bool IsWhiffMonsterPatternEvent(EffectResultEvent resultEvent)
        {
            return resultEvent.Kind == EffectKind.AttackMissed &&
                   resultEvent.SourceRef.StartsWith("monster.pattern.", System.StringComparison.Ordinal);
        }

        /// <summary>
        /// 빗맞음 이벤트를 카탈로그 조회용 Damage 프로브로 정규화한다(#14). 패턴 엔트리는
        /// kind=Damage·targetFilter=Player로 구워져 있으므로 그 모양으로 조회해야 명중과 같은
        /// 큐(딜레이·앵커 포함)가 잡힌다. 조회 전용 — 재생·텍스트에는 쓰지 않는다.
        /// </summary>
        internal static EffectResultEvent BuildWhiffVfxProbe(EffectResultEvent resultEvent)
        {
            return new EffectResultEvent(
                EffectKind.Damage,
                targetUnitId: "player",
                center: resultEvent.Center,
                radius: resultEvent.Radius,
                sourceRef: resultEvent.SourceRef,
                sourceUnitId: resultEvent.SourceUnitId,
                sourceActorKind: resultEvent.SourceActorKind,
                targetActorKind: resultEvent.TargetActorKind,
                sourcePatternId: resultEvent.SourcePatternId,
                hitIndex: 0,
                hitCount: 1,
                presentationGroupId: resultEvent.PresentationGroupId,
                sourceCoord: resultEvent.SourceCoord,
                areaCoords: resultEvent.AreaCoords);
        }

        /// <summary>해소된 엔트리에서 공격자 몸에서 나가는 Source 앵커 큐만 남긴다(#14).</summary>
        internal static EffectVfxCatalog.Entry[] FilterSourceAnchoredEntries(EffectVfxCatalog.Entry[] entries)
        {
            var count = 0;
            for (var i = 0; i < entries.Length; i++)
            {
                if (IsSourceAnchored(entries[i]))
                {
                    count++;
                }
            }

            if (count == entries.Length)
            {
                return entries;
            }

            var filtered = new EffectVfxCatalog.Entry[count];
            var index = 0;
            for (var i = 0; i < entries.Length; i++)
            {
                if (IsSourceAnchored(entries[i]))
                {
                    filtered[index++] = entries[i];
                }
            }

            return filtered;
        }

        private bool TryResolveWhiffAttackVfxEntries(EffectResultEvent resultEvent, out EffectVfxCatalog.Entry[] entries)
        {
            entries = System.Array.Empty<EffectVfxCatalog.Entry>();
            if (!IsWhiffMonsterPatternEvent(resultEvent))
            {
                return false;
            }

            entries = FilterSourceAnchoredEntries(ResolveVfxEntries(BuildWhiffVfxProbe(resultEvent)));
            return entries.Length > 0;
        }

        /// <summary>
        /// 빗맞음 표시용 이벤트(#14): kind는 AttackMissed를 유지해 텍스트가 「빗맞음」으로 남고,
        /// target만 player로 바꿔 Source 앵커 큐의 조준(facing)이 명중과 같이 플레이어를 향하게 한다
        /// (원본 이벤트는 target=몬스터 자신이라 조준이 퇴화한다).
        /// </summary>
        private static EffectResultEvent BuildWhiffDisplayEvent(EffectResultEvent resultEvent)
        {
            return new EffectResultEvent(
                EffectKind.AttackMissed,
                targetUnitId: "player",
                center: resultEvent.Center,
                radius: resultEvent.Radius,
                sourceRef: resultEvent.SourceRef,
                sourceUnitId: resultEvent.SourceUnitId,
                sourceActorKind: resultEvent.SourceActorKind,
                targetActorKind: resultEvent.TargetActorKind,
                sourcePatternId: resultEvent.SourcePatternId,
                hitIndex: resultEvent.HitIndex,
                hitCount: resultEvent.HitCount,
                presentationGroupId: resultEvent.PresentationGroupId,
                delaySeconds: resultEvent.DelaySeconds,
                sourceCoord: resultEvent.SourceCoord,
                areaCoords: resultEvent.AreaCoords);
        }

        private bool TryResolveFacingTargetWorldPosition(EffectVfxCatalog.Entry entry, EffectResultEvent resultEvent, out Vector3 worldPosition)
        {
            if (controller != null &&
                !string.IsNullOrWhiteSpace(resultEvent.TargetUnitId) &&
                controller.TryGetCombatantVfxAnchorWorldPosition(
                    resultEvent.TargetUnitId,
                    EffectVfxAnchorPolicy.ResolveTargetAnchor(entry, resultEvent),
                    out worldPosition))
            {
                return true;
            }

            if (controller != null && resultEvent.Center.HasValue &&
                controller.TryGetTileWorldPosition(resultEvent.Center.Value, out worldPosition))
            {
                return true;
            }

            worldPosition = default;
            return false;
        }

        private bool TryResolveSourceWorldPosition(EffectResultEvent resultEvent, out Vector3 worldPosition)
        {
            return TryResolveSourceWorldPosition(ResolveVfxEntry(resultEvent), resultEvent, out worldPosition);
        }

        private bool TryResolveSourceWorldPosition(EffectVfxCatalog.Entry entry, EffectResultEvent resultEvent, out Vector3 worldPosition)
        {
            if (controller == null || controller.State == null)
            {
                worldPosition = default;
                return false;
            }

            if (!string.IsNullOrWhiteSpace(resultEvent.SourceUnitId) &&
                controller.TryGetCombatantVfxAnchorWorldPosition(
                    resultEvent.SourceUnitId,
                    EffectVfxAnchorPolicy.ResolveSourceAnchor(entry, resultEvent),
                    out worldPosition))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(resultEvent.SourceRef) &&
                controller.TryGetCombatantVfxAnchorWorldPosition(
                    resultEvent.SourceRef,
                    EffectVfxAnchorPolicy.ResolveSourceAnchor(entry, resultEvent),
                    out worldPosition))
            {
                return true;
            }

            if (IsPlayerAuthoredTargetedEffect(resultEvent))
            {
                return controller.TryGetCombatantVfxAnchorWorldPosition(
                           "player",
                           EffectVfxAnchorPolicy.ResolveSourceAnchor(entry, resultEvent),
                           out worldPosition) ||
                       controller.TryGetPlayerWorldPosition(out worldPosition);
            }

            worldPosition = default;
            return false;
        }

        private EffectVfxCatalog.Entry ResolveVfxEntry(EffectResultEvent resultEvent)
        {
            var catalog = presentation != null ? presentation.VfxCatalog : null;
            return catalog != null && catalog.TryResolve(resultEvent, out var entry) ? entry : null;
        }

        private EffectVfxCatalog.Entry[] ResolveVfxEntries(EffectResultEvent resultEvent)
        {
            var catalog = presentation != null ? presentation.VfxCatalog : null;
            return catalog != null ? catalog.ResolveAll(resultEvent) : System.Array.Empty<EffectVfxCatalog.Entry>();
        }

        private bool TryResolveWorldPosition(
            EffectVfxCatalog.Entry entry,
            EffectResultEvent resultEvent,
            out Vector3 worldPosition)
        {
            if (TryResolveCatalogSpawnAnchorWorldPosition(entry, resultEvent, out worldPosition))
            {
                return true;
            }

            if (!EffectVfxAnchorPolicy.IsAreaLike(resultEvent) &&
                controller != null &&
                controller.TryGetCombatantVfxAnchorWorldPosition(
                    resultEvent.TargetUnitId,
                    EffectVfxAnchorPolicy.ResolveTargetAnchor(entry, resultEvent),
                    out worldPosition))
            {
                return true;
            }

            if (controller != null && resultEvent.Center.HasValue
                && controller.TryGetTileWorldPosition(resultEvent.Center.Value, out worldPosition))
            {
                return true;
            }

            if (effectAnchor != null)
            {
                worldPosition = effectAnchor.position;
                return true;
            }

            worldPosition = default;
            return false;
        }

        private bool TryResolveCatalogSpawnAnchorWorldPosition(
            EffectVfxCatalog.Entry entry,
            EffectResultEvent resultEvent,
            out Vector3 worldPosition)
        {
            if (entry == null || controller == null)
            {
                worldPosition = default;
                return false;
            }

            switch (entry.SpawnAnchor)
            {
                case EffectVfxSpawnAnchor.SourceAttack:
                case EffectVfxSpawnAnchor.SourceGround:
                    return TryResolveSourceWorldPosition(entry, resultEvent, out worldPosition);
                case EffectVfxSpawnAnchor.TargetHitCenter:
                case EffectVfxSpawnAnchor.TargetGround:
                    return controller.TryGetCombatantVfxAnchorWorldPosition(
                        resultEvent.TargetUnitId,
                        EffectVfxAnchorPolicy.ResolveTargetAnchor(entry, resultEvent),
                        out worldPosition);
                case EffectVfxSpawnAnchor.FieldCenter:
                    if (resultEvent.Center.HasValue &&
                        controller.TryGetTileWorldPosition(resultEvent.Center.Value, out worldPosition))
                    {
                        return true;
                    }

                    break;
            }

            worldPosition = default;
            return false;
        }

        private bool IsPlayerAuthoredTargetedEffect(EffectResultEvent resultEvent)
        {
            if (!resultEvent.Center.HasValue)
            {
                return false;
            }

            if (string.Equals(resultEvent.TargetUnitId, "player", System.StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !EffectVfxAnchorPolicy.IsTrapLike(resultEvent) &&
                   !EffectVfxAnchorPolicy.IsFieldLike(resultEvent) &&
                   !IsKnockbackEffect(resultEvent);
        }

        private static bool IsKnockbackEffect(EffectResultEvent resultEvent)
        {
            return !string.IsNullOrWhiteSpace(resultEvent.SourceRef) &&
                   resultEvent.SourceRef.StartsWith("knockback", System.StringComparison.Ordinal);
        }

        /// <summary>
        /// True for the player's own movement dust cues (CVM*): a SourceGround-anchored Push that the player
        /// applies to itself while stepping. These are the only SourceGround Push cues, so the anchor + kind
        /// test uniquely selects them; knockback (a separate Push) is excluded for safety.
        /// </summary>
        private static bool IsPlayerMovementCue(EffectVfxCatalog.Entry entry, EffectResultEvent resultEvent)
        {
            return entry != null &&
                   entry.SpawnAnchor == EffectVfxSpawnAnchor.SourceGround &&
                   resultEvent.Kind == EffectKind.Push &&
                   !IsKnockbackEffect(resultEvent);
        }

        /// <summary>
        /// Builds a per-frame provider that re-resolves the effect's source (the player) ground anchor so the
        /// followed dust stays glued under the marker as it animates. Reuses <see cref="TryResolveSourceWorldPosition"/>
        /// (the same resolution used for the initial spawn) and falls back to the captured spawn point.
        /// </summary>
        private System.Func<Vector3> MakeSourceAnchorFollowProvider(
            EffectVfxCatalog.Entry entry,
            EffectResultEvent resultEvent,
            Vector3 fallbackWorld)
        {
            return () => TryResolveSourceWorldPosition(entry, resultEvent, out var sourceWorld)
                ? sourceWorld
                : fallbackWorld;
        }

        /// <summary>
        /// Resolves the hex-snapped facing for a player movement cue from the step's origin tile
        /// (<see cref="EffectResultEvent.Center"/>) to the player's current coord. CombatState updates
        /// PlayerCoord to the destination before raising the move effect, so this is the actual move
        /// direction. The cue is authored as a trailing burst, so the resolved rotation points opposite
        /// the movement vector.
        /// </summary>
        private bool TryResolvePlayerMoveFacing(EffectResultEvent resultEvent, out Quaternion rotation)
        {
            rotation = Quaternion.identity;
            if (controller == null || controller.State == null || !resultEvent.Center.HasValue)
            {
                return false;
            }

            if (!controller.TryGetTileWorldPosition(resultEvent.Center.Value, out var fromWorld) ||
                !controller.TryGetTileWorldPosition(controller.State.PlayerCoord, out var toWorld))
            {
                return false;
            }

            // Movement dust is authored as a trailing burst, so face it opposite the actor's travel direction.
            var direction = fromWorld - toWorld;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            rotation = CombatFacingUtility.ResolveHexSideRotation(direction, snapFacingToHexSides, hexFacingYawOffset);
            return true;
        }

        private void ResolveReferences()
        {
            if (controller == null)
            {
                controller = FindFirstObjectByType<MapCombatController>();
            }

            if (presentation == null)
            {
                presentation = GetComponent<EffectPresentationController>()
                    ?? gameObject.AddComponent<EffectPresentationController>();
            }
        }
    }
}

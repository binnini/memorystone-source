using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime.Timeline
{
    /// <summary>
    /// The kind of a single semantic beat on a <see cref="CombatTimeline"/>. These describe *what*
    /// happened during rules resolution, never *how long* it should take on screen — durations live in
    /// the Unity presentation scheduler so this stays UnityEngine-free (pure rules layer).
    /// </summary>
    public enum CombatTimelineEventKind
    {
        /// <summary>One tile transition of the player along a movement path (<see cref="CombatTimelineEvent.From"/> → To).</summary>
        PlayerMoveStep,

        /// <summary>One tile transition of a monster along its movement path.</summary>
        EnemyMoveStep,
        // 도약 이동(§28 W7): 걷기와 달리 sink가 점프 애니메이션을 얹는다(살포 캐스트와 같은 트리거).
        EnemyLeapStep,

        /// <summary>One tile transition of the player being knocked back.</summary>
        PlayerKnockbackStep,

        /// <summary>One tile transition of a monster being knocked back.</summary>
        EnemyKnockbackStep,

        /// <summary>The player begins an attack (wind-up anchor; the impact follows).</summary>
        PlayerAttackStart,

        /// <summary>A monster begins an attack (wind-up anchor; the impact follows).</summary>
        EnemyAttackStart,

        /// <summary>The impact beat that anchors a group of buffered effects (same <see cref="CombatTimelineEvent.GroupId"/>).</summary>
        AttackImpact,

        /// <summary>A single buffered effect, referenced by <see cref="CombatTimelineEvent.EffectIndex"/>.</summary>
        Effect,

        /// <summary>A monster plays its hit reaction.</summary>
        ActorHit,

        /// <summary>A monster plays its death reaction.</summary>
        ActorDeath,

        /// <summary>A monster plays its knockback reaction.</summary>
        ActorKnockback,

        /// <summary>The player plays its hit reaction.</summary>
        PlayerHit,

        /// <summary>The player plays its death reaction.</summary>
        PlayerDeath,

        /// <summary>A hit-stop pause; the sink resolves its length from the beat's timing key, actor and lethality.</summary>
        HitStop,

        /// <summary>A breathing pause inserted between consecutive monsters' actions so the turn reads clearly.</summary>
        MonsterActionGap,

        /// <summary>A longer pause after the last visible monster attack before the next overall turn begins.</summary>
        MonsterPhaseCompleteGap,

        /// <summary>Presentation-only boundary before next-turn start effects resolve.</summary>
        OverallTurnStart,

        /// <summary>Presentation-only player-turn-start announcement after next-turn start effects resolve.</summary>
        PlayerTurnStart,

        /// <summary>
        /// 암시야(숨은 몬스터)에게 기습당했을 때 플레이어 위치에 "기습!" 경고 텍스트를 띄우는 표현 전용 비트.
        /// 공격자가 보이지 않으므로 모델 연출 대신 이 경고를 가장 먼저 표시한다.
        /// </summary>
        AmbushAlert,

        /// <summary>
        /// 화면 밖에서 벌어지려는 액션을 카메라로 잠깐 프레이밍하는 비트
        /// (docs/monster-action-camera-focus-plan.md). <see cref="CombatTimelineEvent.To"/>가 프레이밍 좌표,
        /// <see cref="CombatTimelineEvent.ActorId"/>가 클러스터 대표 액터다.
        ///
        /// 게이트는 여기가 아니라 sink에 있다: 좌표가 이미 화면 안이면 sink는 아무것도 하지 않고
        /// 스케줄러가 <see cref="CombatTimelineEvent.FallbackGap"/>에 따라 기존 텀을 그대로 흘린다.
        /// 즉 이 비트가 있다는 것 자체가 카메라가 움직인다는 뜻은 아니다.
        /// </summary>
        ActorSpotlight,

        /// <summary>
        /// 카메라를 소유한 연출을 정리하고(체류 대기 → 해제 → 복귀 안착) 카메라를 플레이어에게 돌려주는
        /// 경계 비트 (docs/monster-action-camera-focus-plan.md §10.13).
        ///
        /// <see cref="PlayerTurnStart"/> **앞에** 놓여서 "필드 오브젝트 페이즈가 다 끝난 뒤에 비로소 턴이
        /// 시작된다"는 순서를 타임라인 자체에 드러낸다. 이전에는 이 정리가 스케줄러 바깥(`Play()` 반환 뒤)에
        /// 있어서, 카드 뽑기 애니메이션이 카메라가 아직 필드에 나가 있는 동안 재생됐다.
        ///
        /// <see cref="ActorSpotlight"/>와 마찬가지로 게이트는 sink에 있다: 카메라가 나가 있지 않으면 sink는
        /// 아무것도 하지 않으므로, 이 비트가 있다는 것 자체가 대기를 뜻하지는 않는다.
        ///
        /// ⚠️ 값은 항상 enum 맨 뒤에만 추가할 것 — 직렬화된 값이 밀린다.
        /// </summary>
        ActionFocusRelease,

        /// <summary>
        /// 보스 페이즈 전환 연출 비트. <see cref="CombatTimelineEvent.ActorId"/>가 전환하는 보스의 unitId,
        /// <see cref="CombatTimelineEvent.To"/>가 프레이밍 좌표(있으면). 규칙상 전환은 이미 해결된 상태이고
        /// 이 비트는 표현 전용이다 — sink가 카메라 프레이밍·버스트·셰이크·아우라 전환을 수행한다(P4).
        /// 보스 행동 페이즈 끝(데미지 버스트 뒤)에 방출되며, 플레이어 턴 시작 시에는 방출되지 않는다.
        ///
        /// ⚠️ 값은 항상 enum 맨 뒤에만 추가할 것 — 직렬화된 값이 밀린다.
        /// </summary>
        BossPhaseTransition,

        /// <summary>
        /// 보스가 기물(철조각)을 살포하는 <b>캐스트</b> 비트. <see cref="CombatTimelineEvent.ActorId"/>가 보스의
        /// unitId, <see cref="CombatTimelineEvent.To"/>가 살포 시점의 보스 좌표다. 규칙상 기물은 이미 놓였고
        /// 이 비트는 표현 전용이다 — sink가 카메라 흔들림·보스 애니메이션·캐스트 VFX/SFX를 연주한다.
        /// 살포한 턴에는 보스가 일반 공격을 하지 않으므로(규칙), 이 비트가 그 턴 보스 행동의 얼굴이다.
        ///
        /// ⚠️ 값은 항상 enum 맨 뒤에만 추가할 것 — 직렬화된 값이 밀린다.
        /// </summary>
        BossPropVolleyCast,

        /// <summary>
        /// 기물 한 개가 판에 꽂히는 비트. <see cref="CombatTimelineEvent.To"/>가 그 칸.
        /// 캐스트 비트 뒤에 놓인 기물 수만큼 이어지며, sink가 짧은 텀으로 흩어 재생한다.
        ///
        /// ⚠️ 값은 항상 enum 맨 뒤에만 추가할 것.
        /// </summary>
        BossPropPlaced,

        /// <summary>
        /// 성숙한 기물이 터지며 보스에게 흡수되는 비트. <see cref="CombatTimelineEvent.From"/>이 기물이 서
        /// 있던 칸(폭발 원점), <see cref="CombatTimelineEvent.To"/>가 힘이 빨려 들어갈 보스 좌표다.
        /// 흡수는 사망이 아니라 <b>제거</b>라서 사망 연출 경로를 타지 않는다 — 이 비트가 유일한 연출이다.
        ///
        /// ⚠️ 값은 항상 enum 맨 뒤에만 추가할 것.
        /// </summary>
        BossPropAbsorb,

        /// <summary>
        /// 철조각 사슬 명중 비트(2026-09-05 후속 #7). <see cref="CombatTimelineEvent.ActorId"/>가 보스 unitId,
        /// <see cref="CombatTimelineEvent.From"/>이 보스 좌표(선의 시작), <see cref="CombatTimelineEvent.To"/>가 맞은
        /// 플레이어 좌표(빗나갔으면 null). 가닥 셀 목록은 좌표 두 개에 담기지 않으므로 sink가 결의 직후 투영
        /// (<c>CombatState.LastBossScrapChainHits</c>)을 보스 id로 찾는다 — 그 투영은 다음 결의까지 불변이다.
        /// 규칙상 피해·속박은 이미 적용됐고, 이 비트 뒤에 그 효과 비트(피해 숫자)가 붙는다.
        ///
        /// ⚠️ 값은 항상 enum 맨 뒤에만 추가할 것.
        /// </summary>
        BossScrapChainHit,
    }

    /// <summary>
    /// One ordered, presentation-agnostic beat produced during a single combat rules resolution.
    /// Holds only semantic data (coords, actor ids, an index into the buffered-effect list); the
    /// Unity <c>PresentationScheduler</c> turns these into timed animation/SFX/VFX. Immutable value type.
    /// </summary>
    public readonly struct CombatTimelineEvent
    {
        public CombatTimelineEvent(
            CombatTimelineEventKind kind,
            int sequence,
            string actorId = "",
            HexCoord? from = null,
            HexCoord? to = null,
            string trigger = "",
            int effectIndex = -1,
            string groupId = "",
            bool lethal = false,
            string timingKey = "",
            bool multiHitStrike = false,
            bool aoeTarget = false,
            bool statusApply = false,
            bool textQueue = false,
            bool noWaitAfterEffect = false,
            bool fallbackGap = false,
            int coveredBeats = 0,
            float coveredSeconds = 0f,
            float coveredLeadInSeconds = 0f)
        {
            Kind = kind;
            Sequence = sequence;
            ActorId = actorId ?? string.Empty;
            From = from;
            To = to;
            Trigger = trigger ?? string.Empty;
            EffectIndex = effectIndex;
            GroupId = groupId ?? string.Empty;
            Lethal = lethal;
            TimingKey = timingKey ?? string.Empty;
            MultiHitStrike = multiHitStrike;
            AoeTarget = aoeTarget;
            StatusApply = statusApply;
            TextQueue = textQueue;
            NoWaitAfterEffect = noWaitAfterEffect;
            FallbackGap = fallbackGap;
            CoveredBeats = coveredBeats < 0 ? 0 : coveredBeats;
            CoveredSeconds = coveredSeconds > 0f && !float.IsNaN(coveredSeconds) && !float.IsInfinity(coveredSeconds)
                ? coveredSeconds
                : 0f;
            CoveredLeadInSeconds =
                coveredLeadInSeconds > 0f && !float.IsNaN(coveredLeadInSeconds) && !float.IsInfinity(coveredLeadInSeconds)
                    ? coveredLeadInSeconds
                    : 0f;
        }

        public CombatTimelineEventKind Kind { get; }

        /// <summary>Global monotonic order index within the owning timeline (0-based).</summary>
        public int Sequence { get; }

        /// <summary>Unit id this beat belongs to ("player", a monster id), or empty for global beats.</summary>
        public string ActorId { get; }

        /// <summary>Source tile for movement/knockback steps; null otherwise.</summary>
        public HexCoord? From { get; }

        /// <summary>Destination tile for movement/knockback steps; null otherwise.</summary>
        public HexCoord? To { get; }

        /// <summary>Optional animator trigger name carried by attack/reaction beats.</summary>
        public string Trigger { get; }

        /// <summary>Index into the rules' buffered-effect list for <see cref="CombatTimelineEventKind.Effect"/> beats; -1 otherwise.</summary>
        public int EffectIndex { get; }

        /// <summary>Groups an impact with its effects/reactions so the scheduler can flush them on one beat.</summary>
        public string GroupId { get; }

        /// <summary>True when the beat is a lethal hit/death, so the scheduler can apply lethal timing.</summary>
        public bool Lethal { get; }

        /// <summary>
        /// Per-attack timing key (a card id or monster attack-pattern id) the sink uses to resolve attack-beat
        /// durations (wind-up, impact, hit-stop) from the per-attack timing table, falling back to the global
        /// profile when empty.
        /// </summary>
        public string TimingKey { get; }

        /// <summary>
        /// True for an <see cref="CombatTimelineEventKind.Effect"/> beat that is one strike of a multi-hit
        /// attack, so the scheduler spaces it by the (larger) multi-hit strike interval instead of the generic
        /// effect stagger, letting each strike read as a distinct hit.
        /// </summary>
        public bool MultiHitStrike { get; }

        /// <summary>
        /// True for an <see cref="CombatTimelineEventKind.Effect"/> beat that is one target of an area/blast
        /// attack or burst (휘둘러치기/지뢰찾기/폭탄 투하), so the scheduler spaces it by the (dedicated) AoE
        /// target interval — each target's flinch + number + hit SFX reads one at a time, sequentially.
        /// </summary>
        public bool AoeTarget { get; }

        /// <summary>
        /// True for an <see cref="CombatTimelineEventKind.Effect"/> beat that presents a status effect
        /// (apply/expire/tick-style status beat). When several status beats land at once, the scheduler spaces
        /// them by the dedicated status-effect stagger so each beat (VFX/SFX/icon/number) reads one at a time
        /// instead of all on a single frame. Takes
        /// precedence over <see cref="AoeTarget"/>/<see cref="MultiHitStrike"/> for the wait choice.
        /// </summary>
        public bool StatusApply { get; }

        /// <summary>
        /// True for grouped attack/card effect beats that can spawn several floating texts from one impact.
        /// The scheduler spaces these by a short text-queue interval so status text and damage numbers do not
        /// visually stack on the same frame.
        /// </summary>
        public bool TextQueue { get; }

        /// <summary>
        /// True for a companion effect that must dispatch on the same visual beat as the next effect.
        /// Used for field/bomb area VFX so the center blast starts with the first target hit instead of
        /// consuming its own stagger interval before damage text appears.
        /// </summary>
        public bool NoWaitAfterEffect { get; }

        /// <summary>
        /// <see cref="CombatTimelineEventKind.ActorSpotlight"/> 전용: 이 비트가 원래 있던
        /// <see cref="CombatTimelineEventKind.MonsterActionGap"/> 자리를 대신하고 있다는 표시.
        ///
        /// 카메라가 실제로 움직였다면 그 이동 자체가 호흡 비트가 되므로 텀을 따로 두지 않고, 좌표가
        /// 이미 화면 안이어서 아무것도 하지 않았다면 스케줄러가 그 텀을 대신 흘려 **현행과 완전히 같은
        /// 타이밍**이 된다. 이 플래그가 필요한 이유는 텀이 없던 자리(첫 몬스터, 이동 페이즈, 턴 경계
        /// 이펙트)에도 스포트라이트가 방출되기 때문이다 — 거기서 무조건 텀으로 폴백하면 화면 안
        /// 이벤트인데도 없던 0.7초가 생긴다.
        /// </summary>
        public bool FallbackGap { get; }

        /// <summary>
        /// <see cref="CombatTimelineEventKind.ActorSpotlight"/> 전용: 이 스포트라이트가 덮는 연출 비트 수
        /// (다음 스포트라이트 직전까지). 카메라가 얼마나 여유롭게 들어가야 하는지를 여기서 끌어낸다 —
        /// 몬스터 하나가 한 칸 움직이는 것과 필드가 세 마리를 때리는 것은 길이가 다른 사건인데,
        /// 패닝 시간이 상수면 긴 사건에서 카메라가 급하게 도착해 놓고 가만히 있게 된다.
        ///
        /// 턴/페이즈 경계 비트(gap·turn start 등)는 세지 않는다 — 그건 사건의 길이가 아니라 사건 사이의 텀이다.
        /// </summary>
        public int CoveredBeats { get; }

        /// <summary>
        /// For an <see cref="CombatTimelineEventKind.ActorSpotlight"/>: how many seconds of presentation the
        /// covered beats actually play, summed from the measured VFX/SFX length data
        /// (docs/presentation-duration-data-plan.md P4). 0 when nothing supplied durations — callers then
        /// fall back to the <see cref="CoveredBeats"/> count.
        ///
        /// Filled by <see cref="CombatTimeline.AnnotateActorSpotlightSeconds"/>, a separate optional pass
        /// from <see cref="CombatTimeline.AnnotateActorSpotlightCoverage"/>: resolving a cue's length needs
        /// the Unity VFX catalog, which the pure assembler cannot reach.
        /// </summary>
        public float CoveredSeconds { get; }

        /// <summary>
        /// For an <see cref="CombatTimelineEventKind.ActorSpotlight"/>: the longest authored playback delay
        /// among the cues the covered beats fire — the dead time between an effect being *dispatched* and
        /// anything appearing on screen.
        ///
        /// This exists because <see cref="CoveredSeconds"/> alone let the camera leave before the event it
        /// framed had even started: the field-damage cue is authored with a 1.1s lead-in, so a framing whose
        /// hold was computed from dispatch handed the camera back while the explosion was still pending, and
        /// it then went off next to the player (plan §10.11). A hold must outlast the lead-in, not just the
        /// content length.
        ///
        /// Filled by <see cref="CombatTimeline.AnnotateActorSpotlightLeadIn"/> — like
        /// <see cref="CombatTimeline.AnnotateActorSpotlightSeconds"/> it needs the Unity VFX catalog, so the
        /// pure assembler cannot fill it.
        /// </summary>
        public float CoveredLeadInSeconds { get; }

        /// <summary>Copy with <see cref="CoveredBeats"/> replaced (the struct is immutable).</summary>
        public CombatTimelineEvent WithCoveredBeats(int coveredBeats)
            => new CombatTimelineEvent(
                Kind, Sequence, ActorId, From, To, Trigger, EffectIndex, GroupId, Lethal, TimingKey,
                MultiHitStrike, AoeTarget, StatusApply, TextQueue, NoWaitAfterEffect, FallbackGap, coveredBeats,
                CoveredSeconds, CoveredLeadInSeconds);

        /// <summary>Copy with <see cref="CoveredSeconds"/> replaced (the struct is immutable).</summary>
        public CombatTimelineEvent WithCoveredSeconds(float coveredSeconds)
            => new CombatTimelineEvent(
                Kind, Sequence, ActorId, From, To, Trigger, EffectIndex, GroupId, Lethal, TimingKey,
                MultiHitStrike, AoeTarget, StatusApply, TextQueue, NoWaitAfterEffect, FallbackGap, CoveredBeats,
                coveredSeconds, CoveredLeadInSeconds);

        /// <summary>Copy with <see cref="CoveredLeadInSeconds"/> replaced (the struct is immutable).</summary>
        public CombatTimelineEvent WithCoveredLeadInSeconds(float coveredLeadInSeconds)
            => new CombatTimelineEvent(
                Kind, Sequence, ActorId, From, To, Trigger, EffectIndex, GroupId, Lethal, TimingKey,
                MultiHitStrike, AoeTarget, StatusApply, TextQueue, NoWaitAfterEffect, FallbackGap, CoveredBeats,
                CoveredSeconds, coveredLeadInSeconds);

        public bool IsMoveStep =>
            Kind == CombatTimelineEventKind.PlayerMoveStep
            || Kind == CombatTimelineEventKind.EnemyMoveStep
            || Kind == CombatTimelineEventKind.EnemyLeapStep
            || Kind == CombatTimelineEventKind.PlayerKnockbackStep
            || Kind == CombatTimelineEventKind.EnemyKnockbackStep;
    }
}

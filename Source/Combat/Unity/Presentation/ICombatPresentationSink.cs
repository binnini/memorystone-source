using System.Collections;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Unity.Presentation
{
    /// <summary>
    /// The presentation actions a <see cref="PresentationScheduler"/> drives as it replays a combat
    /// timeline. Implemented by the live combat controller (which forwards to the tile view, actor
    /// markers, audio/VFX dispatch, hit-stop) and by test fakes.
    ///
    /// Movement/stagger waits are scheduled by the scheduler from the global timing profile, but the
    /// attack-shaped beats (wind-up, impact, hit-stop) delegate their *durations* to the sink via a
    /// timing key, so the controller can honor the per-attack timing table while the scheduler stays a
    /// pure ordering authority. Every wait/animation is an <see cref="IEnumerator"/> so the whole
    /// sequence is steppable in EditMode tests.
    /// </summary>
    public interface ICombatPresentationSink
    {
        /// <summary>Idle for <paramref name="seconds"/> (already speed-scaled by the scheduler).</summary>
        IEnumerator Wait(float seconds);

        IEnumerator MovePlayerStep(HexCoord from, HexCoord to, float seconds);
        IEnumerator MoveEnemyStep(string monsterId, HexCoord from, HexCoord to, float seconds);
        IEnumerator KnockbackPlayerStep(HexCoord from, HexCoord to, float seconds);
        IEnumerator KnockbackEnemyStep(string monsterId, HexCoord from, HexCoord to, float seconds);

        /// <summary>Begin the player's attack, facing from <paramref name="from"/> toward <paramref name="to"/>.</summary>
        void StartPlayerAttack(HexCoord from, HexCoord to);

        /// <summary>Begin a monster's attack, facing from <paramref name="from"/> toward <paramref name="to"/>.</summary>
        void StartEnemyAttack(string monsterId, string trigger, HexCoord from, HexCoord to);

        /// <summary>Wind-up hold whose length the sink resolves from the per-attack timing key.</summary>
        IEnumerator AttackWindup(string timingKey);

        /// <summary>
        /// Pre-impact hold. By default the sink resolves a fixed length from the per-attack timing key; when
        /// clip-driven alignment is enabled it instead waits for <paramref name="actorId"/>'s attack
        /// animation to reach its strike frame.
        /// </summary>
        IEnumerator AttackImpactWait(string timingKey, string actorId);

        /// <summary>Commit any state mutations queued for this impact group after the impact wait lands.</summary>
        void CommitImpact(string groupId);

        // Reactions take the buffered-effect index they should sync to, so the flinch/death/knockback fires
        // together with that effect's VFX/SFX/shake (which wait the cue's per-cue delay). -1 = fire now.
        void ReactActorHit(string monsterId, int effectIndex);
        void ReactActorDeath(string monsterId, int effectIndex);
        void ReactActorKnockback(string monsterId, int effectIndex);
        void ReactPlayerHit(int effectIndex);
        void ReactPlayerDeath(int effectIndex);

        /// <summary>Dispatch the buffered effect at <paramref name="bufferedIndex"/> (fires SFX/VFX/number now).</summary>
        void DispatchEffect(int bufferedIndex);

        /// <summary>
        /// Hold a hit-stop pause. The sink resolves the length from the per-attack timing key and whether the
        /// affected actor is the player (monster attack) or a monster (player attack), plus lethality.
        /// </summary>
        IEnumerator HitStop(string timingKey, string actorId, bool lethal);

        /// <summary>Post-kill hold so a death animation can read; length resolved from the timing key's death delay.</summary>
        IEnumerator DeathHold(string timingKey);

        /// <summary>Breathing pause between consecutive monsters' actions during the monster phase.</summary>
        IEnumerator MonsterActionGap();

        /// <summary>
        /// Frame the action about to happen at <paramref name="coord"/>
        /// (docs/monster-action-camera-focus-plan.md).
        ///
        /// The sink owns the whole beat, including doing nothing: it decides — from live camera geometry,
        /// the per-phase focus budget and the master toggle — whether a camera move is warranted at all.
        /// When it does not move, it yields the breathing pause this beat displaced iff
        /// <paramref name="fallbackGap"/> says one was there, so an already-framed action keeps exactly the
        /// timing it has today and a beat in a slot that never had a gap adds nothing.
        ///
        /// Structured as one call rather than "ask, then wait" so the scheduler never holds a decision the
        /// sink could invalidate a frame later.
        /// </summary>
        /// <param name="coveredBeats">
        /// How many content beats this framing covers, so the sink can give a long event an unhurried pan and a
        /// single step a tight one. 0 means "no information" and yields the base pan.
        /// </param>
        /// <param name="coveredLeadInSeconds">
        /// The longest authored playback delay among the cues those beats fire — how long after dispatch the
        /// event first becomes visible. The sink must not hand the camera back inside that window; measuring
        /// the hold from dispatch alone is what sent every field explosion off screen (plan §10.11).
        /// </param>
        IEnumerator FocusOnAction(
            HexCoord coord,
            string actorId,
            bool fallbackGap,
            int coveredBeats,
            float coveredSeconds,
            float coveredLeadInSeconds);

        /// <summary>
        /// Close out whatever framing <see cref="FocusOnAction"/> left up and hand the camera back to the
        /// player, before the turn-boundary beats that follow (plan §10.13): hold the last framing's dwell,
        /// release it, then wait for the return to settle.
        ///
        /// A no-op when nothing owns the camera — the scheduler emits this beat unconditionally, so the sink
        /// is the one that knows whether there is anything to wait for.
        /// </summary>
        IEnumerator ActionFocusRelease();

        /// <summary>Announce the next overall turn and hold before turn-start effects begin.</summary>
        IEnumerator OverallTurnStart();

        /// <summary>Announce that the player can act after start-of-turn effects finish.</summary>
        IEnumerator PlayerTurnStart();

        /// <summary>Show the "기습!" ambush warning at the player's position (fog/hidden-monster attack).</summary>
        void ShowAmbushAlert();

        /// <summary>
        /// Play the boss phase-transition beat for <paramref name="bossUnitId"/> — the rules change is already
        /// applied, so this is presentation only (camera punch-in / shake / hit-stop / aura swap). The sink
        /// owns the whole beat, including its length and whether it moves the camera at all. When
        /// <paramref name="coord"/> is set it is a framing hint (the boss's tile this resolution); otherwise
        /// the sink resolves the boss's live position from its unit id.
        /// </summary>
        IEnumerator BossPhaseTransition(string bossUnitId, HexCoord? coord);

        /// <summary>
        /// 보스가 기물(철조각)을 살포하는 캐스트 비트 — 카메라 흔들림 + 보스 공격 애니메이션 + 캐스트
        /// VFX/SFX. 규칙상 기물은 이미 놓였고, 이어지는 <see cref="BossPropPlaced"/> 비트들이 그것을
        /// 하나씩 화면에 꽂는다. 살포한 턴에는 보스가 일반 공격을 하지 않으므로 이 비트가 보스 행동이다.
        /// </summary>
        IEnumerator BossPropVolleyCast(string bossUnitId, HexCoord? bossCoord);

        /// <summary>도약 이동(§28 W7): 점프 애니메이션(살포 캐스트와 같은 트리거)을 얹고 착지 지점으로 옮긴다.</summary>
        IEnumerator LeapEnemyStep(string unitId, HexCoord from, HexCoord to, float seconds);

        /// <summary>기물 한 개가 <paramref name="coord"/>에 꽂히는 비트(착지 VFX/SFX + 짧은 텀).</summary>
        IEnumerator BossPropPlaced(string bossUnitId, HexCoord? coord);

        /// <summary>
        /// 기물이 <paramref name="propCoord"/>에서 터지고 그 힘이 <paramref name="bossCoord"/>의 보스에게
        /// 빨려 들어가는 비트. 흡수는 사망이 아니므로 사망 연출 경로가 없다 — 이것이 유일한 연출이다.
        /// </summary>
        IEnumerator BossPropAbsorb(string bossUnitId, HexCoord? propCoord, HexCoord? bossCoord);

        /// <summary>
        /// 철조각 사슬 명중 비트(2026-09-05 후속 #7): 보스(<paramref name="bossCoord"/>)에서 각 철조각까지 선이
        /// 점진적으로 그어지고, <paramref name="hitPlayerCoord"/>가 있으면 선이 닿는 순간 그 칸에 피격 큐가 난다.
        /// 가닥의 셀 목록은 <c>CombatState.LastBossScrapChainHits</c>에서 보스 id로 찾는다.
        /// </summary>
        IEnumerator BossScrapChainHit(string bossUnitId, HexCoord? bossCoord, HexCoord? hitPlayerCoord);
    }
}

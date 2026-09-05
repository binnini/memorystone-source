using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// 디버그·랩·트레일러·실플레이 판정 조작의 협력자(2단계 구조 리팩토링 2-B). 옛
    /// <c>MapCombatController.DebugTools.cs</c>·<c>.DebugPlaytest.cs</c>에서 <b>본문 무변경</b>으로 옮겼고,
    /// 호스트 상태에는 <see cref="ICombatDebugHost"/>를 통해서만 닿는다 — 사설 필드 직접 접근 0.
    ///
    /// <para>공개 API는 그대로 <see cref="MapCombatController"/>에 있다(Dev·PlayMode 어셈블리는 IVT가 없어
    /// 호출부 ~230곳이 public에 묶여 있다). 컨트롤러 쪽은 한 줄 위임뿐이고 규칙·연출은 여기서도 만들지 않는다 —
    /// 전부 호스트의 출하 경로(<c>State.Debug*</c>·타임라인·보상 팝업)를 그대로 부른다.</para>
    ///
    /// <para>호스트에 남긴 것: 직렬화 필드를 직접 쓰는 테스트 이음새·토글(<c>MapCombatController.TestSeams.cs</c>)과
    /// 매 프레임 인스펙터 동기화(<c>.RuntimeInspectorSync.cs</c>). 그것까지 뽑으면 세터 수십 개짜리 인터페이스가 된다.</para>
    /// </summary>
    internal sealed class CombatDebugFacade
    {
        private readonly ICombatDebugHost host;

        public CombatDebugFacade(ICombatDebugHost host)
        {
            this.host = host ?? throw new System.ArgumentNullException(nameof(host));
        }

        /// <summary>촬영용 사망 프리즈 배율(1 = 저작값). 호스트의 사망 연출이 읽는다.</summary>
        public float PlayerDeathFreezeDurationScale => debugPlayerDeathFreezeDurationScale;

        /// <summary>전환 비트가 재생 중인가 — 랩 버튼이 겹침 방지 게이트로 읽는다.</summary>
        public bool IsBossPhaseTransitionBeatActive => host.IsBossPhaseTransitionViewActive;

        /// <summary>
        /// MonsterLab 원클릭: 규칙을 건드리지 않고 P4d 전환 비트(프레이밍·버스트·셰이크·펀치인·히트스톱)만
        /// 현재 보스 위치에서 재생한다. 카메라 감각 튜닝을 위해 반복 호출을 전제로 한다.
        /// </summary>
        public bool DebugPlayBossPhaseTransitionBeat(out string report)
        {
            if (host.State == null || !host.State.HasBossPhaseTrack)
            {
                report = "보스 페이즈 트랙 없음 — 연출을 재생할 보스가 없다.";
                return false;
            }

            if (host.IsBossPhaseTransitionViewActive)
            {
                report = "전환 연출이 이미 재생 중이다.";
                return false;
            }

            var bossUnitId = host.State.BossPhases[0].BossUnitId;
            host.StartCoroutine(host.RunBossPhaseTransition(bossUnitId));
            report = $"전환 연출 재생(규칙 무변경): {bossUnitId}";
            return true;
        }

        /// <summary>
        /// MonsterLab 원클릭: 보스를 실제로 다음 페이즈로 올리고(스탯·아우라 투영·BGM 이벤트 = 실플레이와
        /// 같은 규칙 경로) 뷰를 따라붙인 뒤 전환 비트를 재생한다. 실전과의 차이는 트리거(지표 대신 버튼)뿐이다.
        /// </summary>
        public bool DebugAdvanceBossPhaseWithBeat(out string report)
        {
            if (host.State == null || !host.State.HasBossPhaseTrack)
            {
                report = "보스 페이즈 트랙 없음 — 전환할 보스가 없다.";
                return false;
            }

            if (host.IsBossPhaseTransitionViewActive)
            {
                report = "전환 연출이 이미 재생 중이다.";
                return false;
            }

            var bossUnitId = host.State.BossPhases[0].BossUnitId;
            if (!host.State.DebugAdvanceBossPhase(bossUnitId, out var fromPhase, out var toPhase))
            {
                report = "전환 불가 — 이미 최종 페이즈이거나 보스가 죽었다.";
                return false;
            }

            // 규칙 전환은 끝났다 — 마커 배율과 아우라 루프가 새 페이즈를 따라오도록 뷰를 갱신한다.
            host.RefreshView();
            host.ReconcileStatusLoopVfx();
            host.StartCoroutine(host.RunBossPhaseTransition(bossUnitId));
            report = $"페이즈 {fromPhase}→{toPhase} 전환 + 연출 재생: {bossUnitId}";
            return true;
        }

        /// <summary>조우 연출이 재생 중인가 — 랩 버튼이 겹침 방지 게이트로 읽는다.</summary>
        public bool IsBossArenaEncounterBeatActive => host.IsBossEncounterViewActive;

        /// <summary>
        /// MonsterLab 원클릭: 규칙을 건드리지 않고 조우(입장) 연출(암전 → 보스 프레이밍·낙하/포효 → 페이드인 →
        /// 홀드 → 카메라 복귀)만 현재 보스에서 재생한다. 실제 봉인이 없으면 좌표 반영은 무의미하지만
        /// 카메라·애니 리빌 감각을 반복 확인할 수 있다(P4d 전환 비트 버튼과 같은 워크플로).
        /// </summary>
        public bool DebugPlayBossArenaEncounterBeat(out string report)
        {
            if (host.State == null || !host.State.HasBossPhaseTrack)
            {
                report = "보스 페이즈 트랙 없음 — 조우 연출을 재생할 보스가 없다.";
                return false;
            }

            if (host.IsBossEncounterViewActive)
            {
                report = "조우 연출이 이미 재생 중이다.";
                return false;
            }

            host.StartCoroutine(host.RunBossArenaEncounterCinematic());
            report = "조우 연출 재생(규칙 무변경).";
            return true;
        }

        /// <summary>
        /// 도약(§17)이 발동하는 자리로 플레이어를 옮긴다(랩 전용 · §18). 규칙은 그대로다 —
        /// 도약의 트리거는 위치 관계이므로 그 조건을 만족하는 칸을 찾아 세워 줄 뿐이다.
        /// </summary>
        public bool DebugArrangeBossLeap(out string report)
        {
            if (host.State == null || host.IsSequencePlaying)
            {
                report = "지금은 배치할 수 없다(전투 미초기화 또는 연출 재생 중).";
                return false;
            }

            var arranged = host.State.DebugTryArrangeBossLeap(out report);
            host.ClearSelection();
            host.RefreshView();
            return arranged;
        }

        /// <summary>Restore all combatants to full health and a fresh player action phase (tuning sandbox).</summary>
        public void DebugRestoreCombatants()
        {
            if (host.State == null || host.IsSequencePlaying)
            {
                return;
            }

            host.State.DebugRestoreCombatantsForTuning();
            host.LastInputMessage = "Tuning: combatants restored to full health.";
            host.RefreshView();
        }

        /// <summary>
        /// Replay a player attack on the nearest monster through the real presentation sequence. Restores the
        /// target to full first so every replay is identical; <paramref name="lethal"/> forces a killing blow.
        /// Uses the currently selected timing id (<see cref="MapCombatController.DebugReplayPlayerAttackTimingId"/>).
        /// </summary>
        public void DebugReplayPlayerAttack(bool lethal) => DebugReplayPlayerAttack(host.ReplayPlayerAttackTimingId, lethal);

        /// <summary>
        /// Replay a specific player attack card by id so its own VFX cue, hit SFX and per-attack timing show.
        /// The card id also becomes the active timing id. Falls back to the basic attack when blank.
        /// </summary>
        public void DebugReplayPlayerAttack(string attackCardId, bool lethal)
        {
            if (host.State == null || host.IsSequencePlaying)
            {
                return;
            }

            host.ReplayPlayerAttackTimingId = attackCardId; // normalizes blank -> "A01"
            var resolvedCardId = host.ReplayPlayerAttackTimingId;

            // Reset to full health first so before/after diffs always read as a fresh hit or kill.
            host.State.DebugRestoreCombatantsForTuning();
            var before = host.CapturePresentationSnapshot();
            var bufferAttackEffects = host.EffectiveAlignImpactToAnimation && host.ShouldPlayPresentationSequence();
            if (bufferAttackEffects)
            {
                host.State.BeginEffectBuffering();
            }

            if (!host.State.DebugReplayPlayerAttack(resolvedCardId, lethal, out var targetCoord, out var targetId))
            {
                if (bufferAttackEffects && host.State.IsBufferingEffects)
                {
                    host.State.FlushBufferedEffects();
                }

                host.LastInputMessage = host.State.LastFailureReason;
                host.RefreshView();
                return;
            }

            var after = host.CapturePresentationSnapshot();
            host.LastInputMessage = lethal ? "Tuning: lethal attack replay." : "Tuning: attack replay.";
            if (host.ShouldPlayPresentationSequence())
            {
                host.StartPresentationSequence(
                    "Replaying attack...",
                    host.RunAttackTimeline(before, after, targetCoord, targetId, host.LastInputMessage, host.ReplayPlayerAttackTimingId));
                return;
            }

            if (bufferAttackEffects && host.State.IsBufferingEffects)
            {
                host.State.FlushBufferedEffects();
            }

            host.RefreshView();
        }

        /// <summary>
        /// Replay any authored card by id through its own real <c>TryPlayer*</c> path, so scout / field /
        /// defend / utility / move cards can be tuned in the lab instead of only attacks.
        ///
        /// This differs from <see cref="DebugReplayPlayerAttack(string,bool)"/>, which applies synthetic
        /// damage for a repeatable feel test: here the card's actual effect resolves, so area, ticks, status
        /// application and floating text are the real ones. Attack cards are forwarded to the dedicated
        /// replay above rather than duplicated here.
        /// </summary>
        public void DebugReplayCard(string cardId)
        {
            if (host.State == null || host.IsSequencePlaying)
            {
                return;
            }

            if (!host.State.DebugPrepareCardForTuning(cardId, out var card, out var prepareReason))
            {
                host.LastInputMessage = prepareReason;
                host.RefreshView();
                return;
            }

            if (card.EffectType == CardEffectType.Attack)
            {
                DebugReplayPlayerAttack(card.Id, false);
                return;
            }

            var before = host.CapturePresentationSnapshot();
            var bufferEffects = host.EffectiveAlignImpactToAnimation && host.ShouldPlayPresentationSequence();
            if (bufferEffects)
            {
                host.State.BeginEffectBuffering();
            }

            if (!TryPlayCardForTuning(card))
            {
                if (bufferEffects && host.State.IsBufferingEffects)
                {
                    host.State.FlushBufferedEffects();
                }

                host.LastInputMessage = string.IsNullOrEmpty(host.State.LastFailureReason)
                    ? $"Tuning: {card.Id} 재생 실패 (유효한 대상 없음)."
                    : host.State.LastFailureReason;
                host.RefreshView();
                return;
            }

            var after = host.CapturePresentationSnapshot();
            host.LastInputMessage = $"Tuning: {card.Id} replay.";
            if (host.ShouldPlayPresentationSequence())
            {
                // Chosen by whether the player actually moved, not by card type. A self-target move card
                // (M05 추진력 grants next-turn 민첩 and stays put) would otherwise take the move path, which
                // replays the controller's last recorded path and never dispatches the buffered effects — so
                // the card's own buff VFX/text would be missing and a stale step would animate instead.
                var moved = before.PlayerCoord != after.PlayerCoord;
                host.StartPresentationSequence(
                    "Replaying card...",
                    moved
                        ? host.RunMoveTimeline(before, after, host.LastInputMessage)
                        : host.RunBurstTimeline(before, after, host.LastInputMessage));
                return;
            }

            if (bufferEffects && host.State.IsBufferingEffects)
            {
                host.State.FlushBufferedEffects();
            }

            host.RefreshView();
        }

        // Targets are chosen by asking the card's own validator, so range/walkability/occupancy rules are the
        // rules layer's and cannot drift from what a real play would accept.
        private bool TryPlayCardForTuning(CardDefinition card)
        {
            var searchRadius = Mathf.Max(1, card.Range + card.AreaRadius + 1);
            switch (card.EffectType)
            {
                case CardEffectType.Scout:
                    return host.State.DebugTryPickTargetForTuning(
                               searchRadius,
                               candidate => host.State.ValidateScoutTarget(candidate, card.Id).IsValid,
                               out var scoutTarget)
                           && host.State.TryPlayerScout(scoutTarget, card.Id);

                case CardEffectType.FieldObject:
                    return host.State.DebugTryPickTargetForTuning(
                               searchRadius,
                               candidate => host.State.ValidateFieldObjectTarget(candidate, card.Id).IsValid,
                               out var fieldTarget)
                           && host.State.TryPlayerFieldObject(fieldTarget, card.Id);

                case CardEffectType.Defend:
                    return host.State.TryPlayerDefend(card.Id);

                case CardEffectType.Utility:
                    return host.State.TryPlayerUtility(card.Id);

                case CardEffectType.Move:
                    if (card.PlayMode == CardPlayMode.Self)
                    {
                        return host.State.TryPlayerMovementSelf(card.Id);
                    }

                    var destination = host.State.GetReachablePlayerMoves(card.Id)
                        .Where(pair => pair.Value > 0)
                        .OrderByDescending(pair => pair.Value)
                        .ThenBy(pair => pair.Key)
                        .Select(pair => (HexCoord?)pair.Key)
                        .FirstOrDefault();
                    return destination.HasValue && host.State.TryPlayerMove(destination.Value, card.Id);

                default:
                    return false;
            }
        }

        /// <summary>Catalog cards of one effect type for the lab's replay buttons (id + display name).</summary>
        public IReadOnlyList<KeyValuePair<string, string>> GetCatalogCardChoices(CardEffectType effectType)
        {
            return host.State == null
                ? new List<KeyValuePair<string, string>>()
                : host.State.DebugGetCatalogCardChoices(effectType);
        }

        /// <summary>
        /// Replay the nearest monster's attack on the player through the real presentation sequence (opposite
        /// direction of <see cref="DebugReplayPlayerAttack"/>). Restores combatants first so every replay is
        /// identical; <paramref name="lethal"/> forces a killing blow on the player.
        /// </summary>
        public void DebugReplayMonsterAttack(bool lethal)
        {
            if (host.State == null || host.IsSequencePlaying)
            {
                return;
            }

            host.State.DebugRestoreCombatantsForTuning();
            var before = host.CapturePresentationSnapshot();
            var bufferAttackEffects = host.EffectiveAlignImpactToAnimation && host.ShouldPlayPresentationSequence();
            if (bufferAttackEffects)
            {
                host.State.BeginEffectBuffering();
            }

            if (!host.State.DebugReplayMonsterAttack(lethal, out var attackerCoord, out var attackerId, out var animationTrigger, out var playerDied, out var attackerPatternId))
            {
                if (bufferAttackEffects && host.State.IsBufferingEffects)
                {
                    host.State.FlushBufferedEffects();
                }

                host.LastInputMessage = host.State.LastFailureReason;
                host.RefreshView();
                return;
            }

            var after = host.CapturePresentationSnapshot();
            host.LastInputMessage = lethal ? "Tuning: lethal monster attack replay." : "Tuning: monster attack replay.";
            if (host.ShouldPlayPresentationSequence())
            {
                host.StartPresentationSequence(
                    "Replaying monster attack...",
                    host.RunMonsterAttackReplayTimeline(before, after, attackerId, attackerCoord, animationTrigger, playerDied, host.LastInputMessage, attackerPatternId, debugMonsterAttackReplayImpactDelayBonus));
                return;
            }

            if (bufferAttackEffects && host.State.IsBufferingEffects)
            {
                host.State.FlushBufferedEffects();
            }

            host.RefreshView();
        }

        /// <summary>Current global playback speed (Time.timeScale); 1 = realtime.</summary>
        public float DebugPlaybackTimeScale => Time.timeScale;

        /// <summary>
        /// Slow-motion scrub for frame-accurate inspection of impact timing. Scales global time so the whole
        /// presentation sequence (waits, animations, VFX) slows together. Hit stop saves/restores around this.
        /// </summary>
        public void DebugSetPlaybackTimeScale(float scale)
        {
            Time.timeScale = Mathf.Clamp(scale, 0.02f, 1f);
        }

        // --- Card sandbox (docs/card-sandbox-scene-plan.md) ---
        // Presentation-side wrappers for the CombatState.DebugSandbox rules API. Each one guards the same way the
        // tuning helpers above do (no state, or a sequence already playing => do nothing), reports through
        // LastInputMessage, and ends with RefreshView so the HUD/markers follow the mutation. Editor/dev-build only
        // in practice: the sole caller is CombatDebugControlPanel, which is gated by DebugUiAvailable.

        /// <summary>Spawn a catalog dummy near the player so a card under test has something to hit.</summary>
        public bool DebugSandboxSpawnMonster(string definitionId, int maxHp, int preferredDistance)
        {
            if (host.State == null || host.IsSequencePlaying)
            {
                return false;
            }

            if (!host.State.DebugTryFindFreeCoordNearPlayer(preferredDistance, definitionId, out var coord))
            {
                host.LastInputMessage = host.State.LastFailureReason;
                return false;
            }

            if (!host.State.DebugSandboxSpawnMonsterAt(definitionId, coord, maxHp, out var spawnedId))
            {
                host.LastInputMessage = host.State.LastFailureReason;
                return false;
            }

            host.LastInputMessage = $"Sandbox: spawned {definitionId} ({spawnedId}) at {coord}.";
            host.RefreshView();
            return true;
        }

        // ── 몸집 조절(2026-09-03) ────────────────────────────────────────
        // 모델 몸집의 저작면은 프리팹 루트 localScale 하나뿐이라, 값을 바꿀 때마다 프리팹 저장 → 재소환을
        // 돌리면 「나란히 놓고 비교」가 안 된다. 살아 있는 마커에 배율을 얹어 실시간으로 보고, 마음에 들면
        // 그 자리에서 프리팹에 쓴다. 배율은 규칙·세이브 어디에도 안 들어간다(순수 연출).

        /// <summary>유닛에 얹힌 디버그 배율(1 = 없음).</summary>
        public float DebugGetMonsterVisualScaleOverride(string monsterUnitId)
        {
            return host.ResolveDebugMonsterVisualScaleOverride(monsterUnitId);
        }

        /// <summary>유닛의 모델 배율을 실시간으로 바꾼다. 프리팹 루트 배율 × 페이즈 배율 × 이 값이 화면 크기다.</summary>
        public void DebugSetMonsterVisualScaleOverride(string monsterUnitId, float multiplier)
        {
            if (string.IsNullOrEmpty(monsterUnitId))
            {
                return;
            }

            if (multiplier <= 0f || Mathf.Approximately(multiplier, 1f))
            {
                host.MonsterVisualScaleOverrides.Remove(monsterUnitId);
            }
            else
            {
                host.MonsterVisualScaleOverrides[monsterUnitId] = multiplier;
            }

            host.ActorMarkers?.SetMarkerVisualScale(monsterUnitId, host.EnemyVisualLocalScale * host.ResolveMonsterVisualScale(monsterUnitId));
        }

        /// <summary>마커가 인스턴스화될 때의 프리팹 루트 배율(y). 마커가 없으면 false.</summary>
        public bool DebugTryGetMonsterPrefabRootScale(string monsterUnitId, out float rootScale)
        {
            rootScale = 1f;
            if (host.ActorMarkers != null && host.ActorMarkers.TryGetMarkerPrefabBaseScale(monsterUnitId, out var baseScale))
            {
                rootScale = baseScale.y;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 현재 화면 배율을 프리팹 루트 localScale로 굳힌다(에디터 전용). 같은 정의의 살아 있는 유닛은
        /// 전부 디버그 배율이 1로 돌아가고 기준 배율을 새 프리팹 값으로 다시 읽으므로 화면 크기는 그대로다.
        /// </summary>
        public bool DebugWriteMonsterVisualScaleToPrefab(string definitionId, float rootScale, out string message)
        {
#if UNITY_EDITOR
            message = string.Empty;
            if (host.State == null || !host.State.TryGetMonsterCatalogEntry(definitionId, out var entry) || string.IsNullOrWhiteSpace(entry.VisualPrefabPath))
            {
                message = $"{definitionId}: 카탈로그에 visualPrefabPath가 없습니다.";
                return false;
            }

            if (rootScale <= 0f)
            {
                message = "루트 스케일은 0보다 커야 합니다.";
                return false;
            }

            var root = PrefabUtility.LoadPrefabContents(entry.VisualPrefabPath);
            try
            {
                root.transform.localScale = Vector3.one * rootScale;
                PrefabUtility.SaveAsPrefabAsset(root, entry.VisualPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            foreach (var monster in host.State.Monsters)
            {
                if (monster.IsDead || !string.Equals(monster.DefinitionId, definitionId, System.StringComparison.Ordinal))
                {
                    continue;
                }

                host.MonsterVisualScaleOverrides.Remove(monster.Id);
                host.ActorMarkers?.RefreshMarkerPrefabBaseScale(monster.Id, host.EnemyVisualLocalScale * host.ResolveMonsterVisualScale(monster.Id));
            }

            message = $"{definitionId} 프리팹 루트 스케일 {rootScale:0.00} 저장: {entry.VisualPrefabPath}";
            host.LastInputMessage = message;
            return true;
#else
            message = "프리팹 저장은 에디터에서만 됩니다.";
            return false;
#endif
        }

        /// <summary>Remove every dummy this sandbox spawned, leaving map-authored monsters alone.</summary>
        public int DebugSandboxRemoveSpawnedMonsters()
        {
            if (host.State == null || host.IsSequencePlaying)
            {
                return 0;
            }

            var removed = host.State.DebugRemoveSandboxMonsters();
            host.LastInputMessage = removed > 0
                ? $"Sandbox: removed {removed} spawned dummy monster(s)."
                : "Sandbox: no spawned dummies to remove.";
            host.RefreshView();
            return removed;
        }

        /// <summary>Put a status on the player (D02/D05/U03 정화 setups).</summary>
        public bool DebugSandboxApplyStatusToPlayer(StatusEffectKind kind, int turns, int amount)
        {
            if (host.State == null || host.IsSequencePlaying)
            {
                return false;
            }

            if (!host.State.DebugApplyStatusToPlayer(kind, turns, amount))
            {
                host.LastInputMessage = host.State.LastFailureReason;
                return false;
            }

            host.LastInputMessage = $"Sandbox: {kind} x{turns} on player.";
            host.RefreshView();
            return true;
        }

        /// <summary>Put a status on every living monster (A12 전염병 needs an afflicted target).</summary>
        public int DebugSandboxApplyStatusToMonsters(StatusEffectKind kind, int turns, int amount)
        {
            if (host.State == null || host.IsSequencePlaying)
            {
                return 0;
            }

            var hit = host.State.DebugApplyStatusToAllMonsters(kind, turns, amount);
            host.LastInputMessage = hit > 0
                ? $"Sandbox: {kind} x{turns} on {hit} monster(s)."
                : "Sandbox: no living monster to apply a status to.";
            host.RefreshView();
            return hit;
        }

        /// <summary>Fill the 소멸 더미 so A13's hit count and U02's recover have something to work with.</summary>
        public int DebugSandboxFillExilePile(int count)
        {
            if (host.State == null || host.IsSequencePlaying)
            {
                return 0;
            }

            var added = host.State.DebugFillExilePile(count);
            host.LastInputMessage = $"Sandbox: exiled {added} filler card(s); pile now holds {host.State.GetExilePileCards().Count}.";
            host.RefreshView();
            return added;
        }

        /// <summary>Damage the player directly (Block still absorbs) to check 피해 무효 / 방어막 readouts.</summary>
        public int DebugSandboxDamagePlayer(int amount)
        {
            if (host.State == null || host.IsSequencePlaying)
            {
                return 0;
            }

            var applied = host.State.DebugDamagePlayer(amount);
            host.LastInputMessage = applied > 0
                ? $"Sandbox: player took {applied} damage."
                : $"Sandbox: {amount} damage fully absorbed (or player dead).";
            host.RefreshView();
            return applied;
        }

        // --- Trailer capture seam (docs/trailer-capture-plan.md Phase 2-3) ---
        // Thin dev-only accessors so TrailerShotRunner can assemble a filming take out of parts that already
        // exist for the stage intro, without reaching into the controller's private presentation state or
        // duplicating any of it. Every one of these is a pass-through: no new behavior lives here, and the
        // sole caller is the Dev assembly's runner (itself gated behind CombatDebugControlPanel.DebugUiAvailable).

        /// <summary>
        /// Hides the game-piece dressing on every actor marker (name/HP nameplate, accent badge), exactly as
        /// the stage intro does. Restore with true — nothing else turns it back on.
        /// </summary>
        public void DebugSetActorMarkerUiVisible(bool visible)
        {
            host.ActorMarkers?.SetMarkerUiVisible(visible);
        }

        // The public SetOverlayDebugLayerVisible(layer, visible) the runner needs already exists above.

        /// <summary>
        /// Spawns a catalog monster at an explicit coordinate for a trailer framing. Unlike
        /// <see cref="DebugSandboxSpawnMonster"/> (which only picks a free tile near the player) the caller
        /// chooses the photogenic spot. Same sandbox bookkeeping, so
        /// <see cref="DebugSandboxRemoveSpawnedMonsters"/> cleans it up.
        /// </summary>
        /// <remarks>
        /// No-op while a presentation sequence is playing, like every other sandbox wrapper: spawn before the
        /// take, then run the shot.
        /// </remarks>
        public bool DebugSandboxSpawnMonsterAtCoord(string definitionId, HexCoord coord, int maxHp, out string spawnedId)
        {
            spawnedId = string.Empty;
            if (host.State == null || host.IsSequencePlaying)
            {
                return false;
            }

            if (!host.State.DebugSandboxSpawnMonsterAt(definitionId, coord, maxHp, out spawnedId))
            {
                host.LastInputMessage = host.State.LastFailureReason;
                return false;
            }

            host.LastInputMessage = $"Trailer: spawned {definitionId} ({spawnedId}) at {coord}.";
            host.RefreshView();
            return true;
        }

        /// <summary>Turns a monster's model to face a world point (horizontal only), as the intro's spawn beats do.</summary>
        public void DebugFaceMonsterAt(string monsterId, Vector3 monsterWorld, Vector3 lookFromWorld)
        {
            host.FaceStageIntroMonsterAtCamera(monsterId, monsterWorld, lookFromWorld);
        }

        /// <summary>Fires a monster's attack swing (dynamic motion for a showcase beat). Safe no-op if the marker is missing.</summary>
        public void DebugTriggerMonsterAttack(string monsterId)
        {
            host.ActorMarkers?.TriggerAttack(monsterId);
        }

        /// <summary>
        /// Same, but fires a specific authored trigger (monsters author Attack1..5; see
        /// monster_animation_attack_clips.csv) instead of the resolver's default pick. Empty falls back to
        /// the default overload.
        /// </summary>
        public void DebugTriggerMonsterAttack(string monsterId, string animationTrigger)
        {
            host.ActorMarkers?.TriggerAttack(monsterId, animationTrigger);
        }

        /// <summary>
        /// Presentation-only monster hide set for a trailer take, exactly the stage intro's pre-reveal
        /// mechanism (markers vanish, the sim keeps running). Pass null to clear. Rides the intro's own
        /// hide-set field, so it must not be used while a stage intro / finale take is in flight — the
        /// intro's teardown nulls the set, and a showcase take never overlaps one (both gate on
        /// <see cref="MapCombatController.IsStageIntroPlaying"/> at the runner).
        /// </summary>
        public void DebugSetPresentationHiddenMonsters(IEnumerable<string> monsterIds)
        {
            host.SetStageIntroHiddenMonsters(monsterIds == null
                ? null
                : new HashSet<string>(monsterIds, System.StringComparer.Ordinal));
            host.UpdateEnemyMarker();
        }

        /// <summary>
        /// Presentation-only per-cell forced reveal for a trailer take: cells in the set read as explored
        /// while every other cell keeps the real fog. Exactly the override the intro's fog ripple drives,
        /// which is why a take that wants partial fog hands its cells here instead of touching visibility.
        /// Pass null to clear.
        /// </summary>
        /// <remarks>
        /// Rides the intro's own override field, so it must not be used while a stage intro / finale take
        /// is in flight. Set the cells BEFORE dropping the reveal-all debug flag — that order is what keeps
        /// the handoff flash-free (see the ripple in <c>AnimateStageIntroFinale</c>). On clear, the caller
        /// must still flip reveal-all: this refresh can take the epoch fast path, and the flag flip is what
        /// guarantees the whole-map rescan that puts the real fog back.
        /// </remarks>
        public void DebugSetForcedRevealCells(IEnumerable<HexCoord> coords)
        {
            host.SetCinematicForcedRevealCells(coords == null ? null : new HashSet<HexCoord>(coords));
            host.RefreshMapVisibilityAndHighlights();
            host.UpdateEnemyMarker();
        }

        /// <summary>Plays the intro's trap-style spawn burst at a tile (VFX only, no floating text).</summary>
        public void DebugPlayMonsterSpawnVfx(HexCoord coord, Vector3 tileWorld)
        {
            host.PlayStageIntroMonsterSpawnVfx(coord, tileWorld);
        }

        /// <summary>
        /// Teleports the player to a coordinate with no move presentation (trailer beat prep — a recorded
        /// take must hard-cut between backdrops, not glide across the map). Same validation as the trap
        /// debug move (walkable, unblocked, unoccupied); traps on the destination still fire, so beats are
        /// authored on trap-free tiles.
        /// </summary>
        public bool DebugTeleportPlayerTo(HexCoord coord)
        {
            if (host.State == null || host.IsSequencePlaying)
            {
                return false;
            }

            if (!host.State.TryDebugMovePlayer(coord))
            {
                host.LastInputMessage = host.State.LastFailureReason;
                return false;
            }

            host.TileView?.SetPlayerPosition(coord);
            host.LastInputMessage = $"Trailer: teleported player to {coord}.";
            host.RefreshView();
            return true;
        }

        /// <summary>
        /// ObjectLab: 현재 플레이어 칸의 오브젝트 상호작용 체인을 밟기 연출 없이 실행한다.
        /// 프로덕션 이동 확정 후와 **같은 폴백 체인**(기억결 → 상자 → 상점)을 그대로 태우므로,
        /// 여기서 발생하는 이벤트는 실게임과 동일한 경로다 — 랩 전용 지름길 로직을 두지 않는다.
        /// </summary>
        public bool DebugTriggerTileInteractionAtPlayerCoord()
        {
            if (host.State == null)
            {
                return false;
            }

            return host.ResolveTileInteractionAtPlayerCoord();
        }

        /// <summary>Turns the player marker to face a map cell (trailer framing seam on the view).</summary>
        public void DebugFacePlayerTowards(HexCoord target)
        {
            host.TileView?.FacePlayerTowards(target);
        }

        /// <summary>
        /// Rebinds the player marker's animator to its default state — trailer teardown seam that brings
        /// the marker back from the death pose after a lethal replay beat.
        /// </summary>
        public void DebugResetPlayerVisual()
        {
            host.TileView?.ResetPlayerVisualState();
        }

        // Filming-only stretch of the monster-attack replay's swing-to-impact gap. See its use in
        // ResolveMonsterAttackReplaySequence.
        private float debugMonsterAttackReplayImpactDelayBonus;

        // Filming-only scale on the death freeze (hit stop + slow motion durations). 1 = authored feel.
        private float debugPlayerDeathFreezeDurationScale = 1f;

        /// <summary>
        /// Scales the player-death freeze (hit-stop and slow-motion durations, and the zoom that rides
        /// them) during trailer takes without changing the authored gameplay values. The runner sets this
        /// per take and restores 1 on teardown.
        /// </summary>
        public void DebugSetPlayerDeathFreezeDurationScale(float scale)
        {
            debugPlayerDeathFreezeDurationScale = Mathf.Clamp(scale, 0f, 2f);
        }

        /// <summary>
        /// Adds extra seconds between the monster's attack animation trigger and the impact flush during
        /// <see cref="DebugReplayMonsterAttack"/> — trailer seam so the swing visibly starts before the
        /// victim reacts. Pass 0 to restore the authored timing; the runner clears it on take teardown.
        /// </summary>
        public void DebugSetMonsterAttackReplayImpactDelayBonus(float seconds)
        {
            debugMonsterAttackReplayImpactDelayBonus = Mathf.Max(0f, seconds);
        }

        /// <summary>
        /// Takes the camera over with the same bare Cinemachine vcam the stage intro uses (priority 10000,
        /// pose driven by hand each frame). Pair with <see cref="DebugApplyCinematicCameraPose"/> and
        /// <see cref="DebugEndCinematicCamera"/>.
        /// </summary>
        public void DebugBeginCinematicCamera(Vector3 startCameraPosition, Vector3 startLookAt, float fieldOfView)
        {
            host.CameraController.BeginStageIntroCinemachineCamera(
                startCameraPosition, startLookAt, host.GameplayCamera, host.HostTransform, fieldOfView);
        }

        public void DebugApplyCinematicCameraPose(Vector3 cameraPosition, Vector3 lookAtPoint)
        {
            host.CameraController.ApplyIntroCameraPose(cameraPosition, lookAtPoint);
        }

        /// <summary>Hands the view back to gameplay (the brain then blends out over the next frames).</summary>
        public void DebugEndCinematicCamera()
        {
            host.CameraController.EndStageIntroCinemachineCamera();
            host.RecenterGameplayCameraOnPlayer(immediate: true);
        }

        /// <summary>
        /// Snaps the gameplay follow camera onto the player without touching any cinematic state —
        /// ingame-style trailer beats film through the real play camera, so after the beat's teleport the
        /// view must land on the player instantly instead of easing across the map.
        /// </summary>
        public void DebugRecenterGameplayCameraOnPlayer()
        {
            host.RecenterGameplayCameraOnPlayer(immediate: true);
            // The Cinemachine rig damps toward its follow target; after a beat's teleport that is a
            // seconds-long flight across black fog. Warp it instead.
            host.ResolveCameraBinder()?.SnapToPlayerNow();
        }

        /// <summary>
        /// Suppresses (and restores) the CinemachineBrain's OnGUI debug text — it burns into recorded
        /// frames. The cinematic camera hijack already handles this inside Begin/End; ingame-style trailer
        /// takes film without a hijack and need the toggle standalone.
        /// </summary>
        public void DebugSetCinemachineDebugTextSuppressed(bool suppressed)
        {
            host.CameraController.SetBrainDebugTextSuppressed(host.GameplayCamera, suppressed);
        }

        /// <summary>
        /// Poses the real play camera the way a player might have left it (orbit yaw + zoom distance with
        /// a per-take jitter offset) — ingame-style trailer beats vary the framing per take. Restore with
        /// <see cref="DebugClearIngameFilmingCameraPose"/>; zoomDistanceOverride &lt;= 0 keeps the session's
        /// own zoom as the jitter base.
        /// </summary>
        public void DebugSetIngameFilmingCameraPose(float orbitYawDegrees, float zoomDistanceOverride, float zoomOffset)
        {
            host.ResolveCameraBinder()?.SetFilmingCameraPose(orbitYawDegrees, zoomDistanceOverride, zoomOffset);
        }

        /// <summary>
        /// Installs a real field object straight into the live registry the cards write to.
        ///
        /// This is the only way a dev tool can reproduce the situation the action-focus feature exists for:
        /// the fog debug toggle is a *view* filter and never touches rules-layer visibility, so a monster the
        /// player cannot really see is still never presented and produces no event to frame. A field, by
        /// contrast, is re-unioned into the revealed set every vision refresh for as long as it lives
        /// (CombatState.RefreshPlayerVision, no Kind filter), which is exactly what keeps distant tiles lit
        /// after the player has walked away from them.
        ///
        /// Vision picks it up at the next refresh (the coming turn boundary), matching how a played card behaves.
        /// </summary>
        public bool DebugSandboxInstallFieldObject(
            HexCoord coord, int radius, int remainingTurns, FieldObjectKind kind, int value, string visualRef = "F01")
        {
            if (host.State == null || host.IsSequencePlaying)
            {
                return false;
            }

            host.State.FieldObjects.Add(new FieldObject(
                coord, radius, remainingTurns, kind, value, sourceUnitId: "player", visualRef: visualRef));
            host.LastInputMessage = $"Lab: installed {kind} field r{radius} at {coord} for {remainingTurns} turns.";
            host.RefreshView();
            return true;
        }

        /// <summary>
        /// The live gameplay camera, for dev tools that need to ask a geometry question about the current
        /// view (the camera lab's framing readout builds a
        /// <see cref="CombatCameraController.CameraFrame"/> from it). Read-only access on purpose — posing
        /// the camera still goes through the binder seams above.
        /// </summary>
        public Camera DebugGameplayCamera => host.GameplayCamera;

        /// <summary>
        /// The camera profile the rig actually reads, so a dev panel can display the values the gate uses
        /// instead of a second copy that would drift from them.
        /// </summary>
        public CombatCinemachineCameraProfile DebugCameraProfile => host.ResolveCameraBinder()?.Profile;

        /// <summary>Current player-controllable zoom distance and its authored bounds; -1 when unbound.</summary>
        public float DebugCameraZoomDistance => host.ResolveCameraBinder()?.DebugZoomDistance ?? -1f;
        public float DebugCameraMinZoomDistance => host.ResolveCameraBinder()?.DebugMinZoomDistance ?? -1f;
        public float DebugCameraMaxZoomDistance => host.ResolveCameraBinder()?.DebugMaxZoomDistance ?? -1f;

        /// <summary>Jumps the play camera's zoom without touching orbit yaw (see the binder's note).</summary>
        public void DebugSetCameraZoomDistance(float distance)
        {
            host.ResolveCameraBinder()?.DebugSetZoomDistance(distance);
        }

        /// <summary>Restores the play camera pose captured by the first filming-pose apply.</summary>
        public void DebugClearIngameFilmingCameraPose()
        {
            host.ResolveCameraBinder()?.ClearFilmingCameraPose();
        }

        /// <summary>True while the Cinemachine brain is still blending back after <see cref="DebugEndCinematicCamera"/>.</summary>
        public bool IsCinematicCameraBlending => host.CameraController.IsCameraBlending;

        /// <summary>
        /// The aim lift the intro uses to vertically center a TALL landmark in an orbit shot — the memory
        /// stone is exactly such a landmark, so a trailer take framing it reuses this instead of authoring
        /// a second magic number that would silently drift from the intro's framing.
        /// </summary>
        public float StageIntroLookAtHeightOffset => host.StageIntroLookAtHeightOffset;

        /// <summary>
        /// Presses the runtime environment look to the intro's DAY preset for a trailer take, or releases it
        /// back to the stage's authored night look. Same mechanism the intro's day→night crossfade uses,
        /// just parked at t=0 instead of ramped: <see cref="LookPresetCrossfader.Begin"/> applies the day
        /// preset, and <see cref="LookPresetCrossfader.Complete"/> is the authoritative snap back to night.
        /// The presets are the runtime-injected pair (see <see cref="MapCombatController.SetStageIntroLookPresets"/>); without
        /// them this is a no-op and the take films under whatever look the stage already has.
        /// </summary>
        /// <remarks>
        /// Refuses to engage while a stage intro / dolly / finale take is playing: those own the crossfader
        /// for their whole run, and a second Begin would strand their rigs.
        /// </remarks>
        public void DebugSetTrailerDayLook(bool useDayLook)
        {
            if (useDayLook)
            {
                if (host.IsStageIntroActive || host.StageIntroLookCrossfader != null ||
                    host.StageIntroDayLookPreset == null || host.StageIntroNightLookPreset == null)
                {
                    return;
                }

                var crossfader = new LookPresetCrossfader();
                crossfader.Begin(host.StageIntroDayLookPreset, host.StageIntroNightLookPreset);
                host.StageIntroLookCrossfader = crossfader;
                // Building window emission / prop lights are a separate night dressing layer; without
                // this the city keeps its lit windows under a noon sky.
                host.EngageStageIntroBuildingNightDim();

                return;
            }

            if (host.IsStageIntroActive || host.StageIntroLookCrossfader == null)
            {
                return;
            }

            host.StageIntroLookCrossfader.Complete();
            host.StageIntroLookCrossfader = null;
            host.SnapStageIntroBuildingNightLook();
        }

        // ── 실플레이 판정 편의 표면(옛 DebugPlaytest.cs) ──────────────────────────
        /// <summary>디버그 전리품 목록이 태우는 엽전. 실제 추첨 구간(kill_drop_rates.csv) 한가운데 값.</summary>
        private const int DebugLootMoneyAmount = 25;

        // ── 보상뽑기 상자 ────────────────────────────────────────────────
        // 서비스 3종과 달리 상자는 맵에 24개가 흩어져 있고 소비 원장(ClaimedEventObjectIds)을
        // 공유한다. 「가장 가까운 것」을 고르는 이유는 판마다 좌표가 셔플되기 때문이다
        // (배치 랜덤화 P2 — 고정 좌표를 적어 둘 수 없다).

        /// <summary>아직 안 연 상자 중 플레이어에게 가장 가까운 것으로 순간이동.</summary>
        public bool DebugTeleportToTreasureChest(out string message)
        {
            message = string.Empty;
            if (!TryFindNearestUnclaimedChest(out var chest))
            {
                message = "열 수 있는 상자가 맵에 없습니다(전부 소비했다면 아래 리셋을 누르세요).";
                return false;
            }

            if (host.State == null || !host.State.TryDebugMovePlayer(chest.Coord))
            {
                message = host.State != null ? host.State.LastFailureReason : "State가 없습니다.";
                return false;
            }

            host.CommitPresentationFromState(immediateCamera: false);
            host.RefreshHudOnly();
            message = $"{chest.ObjectId}로 이동했습니다.";
            return true;
        }

        /// <summary>좌표와 무관하게 상자 보상을 즉시 연다(뽑기 결과는 실제 추첨기가 정한다).</summary>
        public bool DebugOpenTreasureChest(out string message)
        {
            message = string.Empty;
            if (host.State == null || host.IsSequencePlaying)
            {
                message = "연출 재생 중에는 열 수 없습니다.";
                return false;
            }

            if (!TryFindNearestUnclaimedChest(out var chest))
            {
                message = "열 수 있는 상자가 맵에 없습니다(전부 소비했다면 아래 리셋을 누르세요).";
                return false;
            }

            // 서비스 모달과 같은 이유로 결과를 확인한다 — 다른 창이 떠 있으면 조용히 거절된다.
            host.ShowTreasureChestReward(chest);
            if (!host.IsRewardPopupOpen())
            {
                message = "다른 창이 열려 있어 상자를 열지 못했습니다. 먼저 닫아 주세요.";
                return false;
            }

            message = $"{chest.ObjectId}를 열었습니다.";
            return true;
        }

        private bool TryFindNearestUnclaimedChest(out HexMapObjectData chest)
        {
            chest = default;
            if (host.LoadedMap == null || host.State == null)
            {
                return false;
            }

            var playerCoord = host.State.PlayerCoord;
            chest = host.LoadedMap.ObjectRefs
                .Where(candidate => candidate.IsTreasureChest
                                    && candidate.Interactable
                                    && !host.State.ClaimedEventObjectIds.Contains(candidate.ObjectId))
                .OrderBy(candidate => playerCoord.DistanceTo(candidate.Coord))
                .FirstOrDefault();
            return chest.IsConfigured;
        }

        // ── 전리품·경제 ──────────────────────────────────────────────────
        // 전리품 목록(DEC-2026-08-31-03)의 사이드바 흡입 연출과 목록 클릭 흐름은 코루틴이라
        // 에디트 모드 캡처로 볼 수 없다 — 실플레이에서 몬스터를 잡아야만 열리던 것을 버튼 하나로 연다.

        /// <summary>
        /// 전리품 목록을 네 줄(엽전·소모품·유물·부적 추가) 전부 채워 연다. 실제 처치와 같은
        /// <c>BuildLootRows</c>를 통과하므로 아이콘 해소·지급·흡입 연출이 전부 출하 경로다.
        /// </summary>
        public bool DebugOpenLootPopup(out string message)
        {
            message = string.Empty;
            if (host.State == null || host.IsSequencePlaying)
            {
                message = "연출 재생 중에는 열 수 없습니다.";
                return false;
            }

            if (host.IsLootPopupOpen())
            {
                message = "전리품 목록이 이미 열려 있습니다.";
                return false;
            }

            var itemId = host.BuildGachaItemPool().FirstOrDefault() ?? string.Empty;
            var relicId = GachaRewardRoller
                .BuildRelicPool(PlayerPermanentItemCatalog.Definitions, host.State.PlayerInventory?.RelicsAndCurses)
                .FirstOrDefault() ?? string.Empty;

            host.OpenLootPopupForDrop(new KillDropOutcome(itemId, relicId, DebugLootMoneyAmount));

            var missing = new List<string>(2);
            if (string.IsNullOrEmpty(itemId))
            {
                missing.Add("소모품 풀 없음");
            }

            if (string.IsNullOrEmpty(relicId))
            {
                missing.Add("남은 유물 없음");
            }

            message = missing.Count == 0
                ? "전리품 목록을 열었습니다(엽전·소모품·유물·부적)."
                : $"전리품 목록을 열었습니다({string.Join(" · ", missing)}).";
            return true;
        }

        /// <summary>엽전을 지급한다(잡화점 가격 재조정 판정용).</summary>
        public int DebugGrantMoney(int amount)
        {
            var wallet = host.State?.PlayerInventory?.Wallet;
            if (wallet == null || amount == 0)
            {
                return 0;
            }

            wallet.Add(amount);
            host.LastInputMessage = $"디버그: 엽전 {amount:+#;-#;0} (보유 {wallet.Balance})";
            host.RefreshHudOnly();
            return wallet.Balance;
        }

        /// <summary>유물을 지급한다 — 지급 관문(RC-5)을 그대로 지난다(중복·상한도 실제와 같이 거부).</summary>
        public bool DebugGrantPermanentItem(string definitionId, out string message)
        {
            message = string.Empty;
            if (host.State == null)
            {
                message = "State가 없습니다.";
                return false;
            }

            if (!host.State.TryGrantPermanentItem(definitionId, out var reason))
            {
                message = string.IsNullOrWhiteSpace(reason) ? "유물을 받을 수 없습니다." : reason;
                return false;
            }

            message = $"유물 지급: {definitionId}";
            host.LastInputMessage = message;
            host.RefreshHudOnly();
            return true;
        }

        /// <summary>소모품을 가방에 넣는다(가방이 만원이면 실제와 같이 거부된다).</summary>
        public bool DebugGrantConsumable(string itemId, out string message)
        {
            message = string.Empty;
            if (host.State == null)
            {
                message = "State가 없습니다.";
                return false;
            }

            if (!host.State.TryAddBagItem(itemId))
            {
                message = "가방이 가득 찼거나 없는 소모품입니다.";
                return false;
            }

            message = $"소모품 지급: {itemId}";
            host.LastInputMessage = message;
            host.RefreshHudOnly();
            return true;
        }

        // ── 소환 프리셋 ──────────────────────────────────────────────────
        // 모델·연출 판정은 「나란히 놓고 비교」가 전부인데(터렛 3레벨의 실루엣 차 등) 기존 더미 소환은
        // 한 번에 한 마리라 카탈로그를 세 번 훑어야 했다. 거리를 1씩 벌려 순서대로 세운다.

        /// <summary>여러 더미를 한 번에 세운다. 반환값은 실제로 선 마릿수.</summary>
        public int DebugSandboxSpawnSquad(IReadOnlyList<string> definitionIds, int startDistance, out string message)
        {
            message = string.Empty;
            if (definitionIds == null || definitionIds.Count == 0)
            {
                message = "소환 목록이 비어 있습니다.";
                return 0;
            }

            if (host.State == null || host.IsSequencePlaying)
            {
                message = "연출 재생 중에는 소환할 수 없습니다.";
                return 0;
            }

            var spawned = 0;
            var failed = new List<string>();
            for (var i = 0; i < definitionIds.Count; i++)
            {
                // 거리를 벌리는 이유는 겹침 회피가 아니라 <b>비교 가능한 줄 세우기</b>다 —
                // 빈 칸 탐색(DebugTryFindFreeCoordNearPlayer)은 어차피 겹치면 알아서 옆으로 민다.
                var distance = UnityEngine.Mathf.Clamp(startDistance + i, 1, 12);
                if (DebugSandboxSpawnMonster(definitionIds[i], 0, distance))
                {
                    spawned++;
                }
                else
                {
                    failed.Add(definitionIds[i]);
                }
            }

            message = failed.Count == 0
                ? $"{spawned}마리를 세웠습니다."
                : $"{spawned}마리를 세웠습니다(실패: {string.Join(", ", failed)}).";
            host.LastInputMessage = message;
            return spawned;
        }
    }
}

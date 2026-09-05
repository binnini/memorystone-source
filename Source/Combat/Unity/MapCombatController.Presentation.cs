using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Runtime.Presentation;
using SeoulPlayup.Combat.Runtime.Timeline;
using SeoulPlayup.Combat.Unity.Tutorial;
using SeoulPlayup.Combat.Unity.Presentation;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using static SeoulPlayup.Combat.Unity.CombatCameraController;
using Cinemachine;
using TMPro;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace SeoulPlayup.Combat.Unity
{
    public sealed partial class MapCombatController
    {
        // 2-B: CombatDebugFacade가 타임라인 인자로 주고받아 internal(어셈블리 밖 노출 없음).
        internal readonly struct CombatPresentationSnapshot
        {
            public CombatPresentationSnapshot(CombatState state)
            {
                PlayerCoord = state.PlayerCoord;
                PlayerHp = state.Player.Hp;
                PlayerDead = state.Player.IsDead;
                Phase = state.Phase;
                SealedBossArenaId = state.SealedBossArenaId;
                Monsters = state.Monsters
                    .Select(monster => new MonsterPresentationSnapshot(
                        monster,
                        // 안개 ∧ 은신 ∧ 아레나 전 보스 — 셋을 하나로 합치지 않는다
                        // (요괴 §4-1의 소비 지점 + 2026-09-02 #4).
                        state.GetVisibility(monster.Coord) == HexCellVisibility.Revealed
                        && !state.IsMonsterHiddenByStealth(monster.Id)
                        && !state.IsMonsterHiddenBeforeBossArena(monster.Id)))
                    .ToList();
            }

            public HexCoord PlayerCoord { get; }
            public int PlayerHp { get; }
            public bool PlayerDead { get; }
            public CombatPhase Phase { get; }
            public string SealedBossArenaId { get; }
            public IReadOnlyList<MonsterPresentationSnapshot> Monsters { get; }

            public MonsterPresentationSnapshot FirstLivingMonster =>
                Monsters.FirstOrDefault(monster => !monster.IsDead);

            public MonsterPresentationSnapshot FindMonster(string id)
            {
                return Monsters.FirstOrDefault(monster => monster.Id == id);
            }
        }

        internal readonly struct MonsterPresentationSnapshot
        {
            public MonsterPresentationSnapshot(MonsterRuntimeState monster, bool isVisible)
            {
                Id = monster.Id;
                Coord = monster.Coord;
                Hp = monster.Hp;
                MaxHp = monster.MaxHp;
                IsDead = monster.IsDead;
                IsVisible = isVisible;
                HasAttackIntent = monster.HasAttackIntent;
                IntentType = monster.Intent.Type;
                DefinitionId = monster.DefinitionId;
                IsBoss = IsBossRole(monster.SpawnRole);
                IsElite = IsEliteRole(monster.SpawnRole);
            }

            public string Id { get; }
            public HexCoord Coord { get; }
            public int Hp { get; }
            public int MaxHp { get; }
            public bool IsDead { get; }
            public bool IsVisible { get; }
            public bool HasAttackIntent { get; }
            public EnemyIntentType IntentType { get; }
            public string DefinitionId { get; }
            public bool IsBoss { get; }
            public bool IsElite { get; }
        }

        private readonly struct EnemyMovePresentation
        {
            public EnemyMovePresentation(string monsterId, Vector3 fromLocal, Vector3 toLocal)
            {
                MonsterId = monsterId;
                FromLocal = fromLocal;
                ToLocal = toLocal;
            }

            public string MonsterId { get; }
            public Vector3 FromLocal { get; }
            public Vector3 ToLocal { get; }
        }

        public bool UsesImmediatePresentationSequencesForTests => resolvePresentationSequencesImmediately;

        public float PlayerMoveSeconds => EffectivePlayerMoveSeconds;

        public float EnemyMoveSeconds => EffectiveEnemyMoveSeconds;

        public float AttackImpactDelay => EffectiveAttackImpactDelay;

        // Presentation timing resolvers: prefer the assigned profile, else fall back to the
        // per-field inspector values. Keeps existing scenes/tests behaving identically when no
        // profile is set while letting a CombatTimingProfile asset centrally override feel.
        private float EffectivePlayerMoveSeconds => CombatTimingDriver.ResolvePlayerMoveSeconds(timingProfile, playerMoveSeconds);

        private float EffectiveEnemyMoveStartDelay => CombatTimingDriver.ResolveEnemyMoveStartDelay(timingProfile, enemyMoveStartDelay);

        private float EffectiveEnemyMoveSeconds => CombatTimingDriver.ResolveEnemyMoveSeconds(timingProfile, enemyMoveSeconds);

        private float EffectiveAttackWindupDelay => CombatTimingDriver.ResolveAttackWindupDelay(timingProfile, attackWindupDelay);

        private float EffectiveAttackImpactDelay => CombatTimingDriver.ResolveAttackImpactDelay(timingProfile, attackImpactDelay);

        private float EffectiveDeathDelay => CombatTimingDriver.ResolveDeathDelay(timingProfile, deathDelay);

        private float EffectiveHitStopAnimationSpeed => CombatTimingDriver.ResolveHitStopAnimationSpeed(timingProfile);

        private float EffectiveHitStopTimeScale => CombatTimingDriver.ResolveHitStopTimeScale(timingProfile);

        private bool EffectiveAlignImpactToAnimation => CombatTimingDriver.ResolveAlignImpactToAnimation(timingProfile);

        private float ResolvePlayerAttackHitStopSeconds(bool lethal)
        {
            return CombatTimingDriver.ResolvePlayerAttackHitStopSeconds(timingProfile, lethal);
        }

        private float ResolveMonsterAttackHitStopSeconds(bool lethal)
        {
            return CombatTimingDriver.ResolveMonsterAttackHitStopSeconds(timingProfile, lethal);
        }

        // --- Per-attack timing resolution (cardId / patternId override layer over the global profile) ---
        // Resolved presentation timing for a single attack. Each value starts from the global profile (or the
        // legacy inspector fields when no profile is assigned) and is replaced only by an authored override row.
        // With no matching row ??or an empty table ??every value equals the global-only result, so the
        // presentation is byte-identical to before this layer existed.
        private readonly struct ResolvedAttackTiming
        {
            public readonly float WindupDelay;
            public readonly float ImpactDelay;
            public readonly float DeathDelay;
            public readonly float PlayerHitStopSeconds;   // already gated by EnableHitStop (0 when hit stop is off)
            public readonly float MonsterHitStopSeconds;
            public readonly float LethalHitStopSeconds;
            public readonly float VisualImpactOffset;
            public readonly float ShakeImpactOffset;
            public readonly float HitStopImpactOffset;

            public ResolvedAttackTiming(
                float windupDelay, float impactDelay, float deathDelay,
                float playerHitStopSeconds, float monsterHitStopSeconds, float lethalHitStopSeconds,
                float visualImpactOffset, float shakeImpactOffset, float hitStopImpactOffset)
            {
                WindupDelay = windupDelay;
                ImpactDelay = impactDelay;
                DeathDelay = deathDelay;
                PlayerHitStopSeconds = playerHitStopSeconds;
                MonsterHitStopSeconds = monsterHitStopSeconds;
                LethalHitStopSeconds = lethalHitStopSeconds;
                VisualImpactOffset = visualImpactOffset;
                ShakeImpactOffset = shakeImpactOffset;
                HitStopImpactOffset = hitStopImpactOffset;
            }

            // Mirrors CombatTimingProfile: the most-negative channel anchors the impact instant; the rest trail.
            public float MinChannelOffset =>
                Mathf.Min(VisualImpactOffset, Mathf.Min(ShakeImpactOffset, HitStopImpactOffset));
            private float ChannelDelay(float offset) => Mathf.Max(0f, offset - MinChannelOffset);
            public float VisualImpactDelay => ChannelDelay(VisualImpactOffset);
            public float ShakeImpactDelay => ChannelDelay(ShakeImpactOffset);
            public float HitStopImpactDelay => ChannelDelay(HitStopImpactOffset);
            public float ResolvePlayerHitStop(bool lethal) => lethal && LethalHitStopSeconds > 0f ? LethalHitStopSeconds : PlayerHitStopSeconds;
            public float ResolveMonsterHitStop(bool lethal) => lethal && LethalHitStopSeconds > 0f ? LethalHitStopSeconds : MonsterHitStopSeconds;
        }

        // The resolved timing of the attack flush currently being dispatched. Set immediately before a buffered
        // attack flush and cleared right after, so the SFX/VFX/shake subscribers (which react synchronously
        // during the flush) honor this attack's per-channel offsets. Null outside an attack flush, where the
        // Active*ImpactDelay properties fall back to the global profile so unthreaded/immediate effects are
        // unchanged.
        private ResolvedAttackTiming? activeImpactTiming;

        public float ActiveVisualImpactDelay => activeImpactTiming?.VisualImpactDelay ?? (timingProfile != null ? timingProfile.VisualImpactDelay : 0f);

        public float ActiveShakeImpactDelay => activeImpactTiming?.ShakeImpactDelay ?? (timingProfile != null ? timingProfile.ShakeImpactDelay : 0f);

        private ResolvedAttackTiming ResolveAttackTiming(string attackId)
        {
            // Global baselines (mirror the Effective* resolvers so no-profile scenes stay identical).
            var windup = EffectiveAttackWindupDelay;
            var impact = EffectiveAttackImpactDelay;
            var death = EffectiveDeathDelay;
            var hitStopOn = timingProfile != null && timingProfile.EnableHitStop;
            var playerHitStop = timingProfile != null ? timingProfile.PlayerAttackHitStopSeconds : 0f;   // already gated
            var monsterHitStop = timingProfile != null ? timingProfile.MonsterAttackHitStopSeconds : 0f;
            var lethalHitStop = timingProfile != null ? timingProfile.LethalHitStopSeconds : 0f;
            var visual = timingProfile != null ? timingProfile.VisualImpactOffset : 0f;
            var shake = timingProfile != null ? timingProfile.ShakeImpactOffset : 0f;
            var hitStopOffset = timingProfile != null ? timingProfile.HitStopImpactOffset : 0f;

            if (attackTimingTable != null && attackTimingTable.TryGet(attackId, out var entry) && entry.HasAnyOverride)
            {
                windup = Mathf.Max(0f, entry.windupDelay.Or(windup));
                impact = Mathf.Max(0f, entry.impactDelay.Or(impact));
                death = Mathf.Max(0f, entry.deathDelay.Or(death));
                if (hitStopOn)
                {
                    // Hit-stop overrides still respect the global EnableHitStop master switch.
                    playerHitStop = Mathf.Max(0f, entry.playerHitStopSeconds.Or(playerHitStop));
                    monsterHitStop = Mathf.Max(0f, entry.monsterHitStopSeconds.Or(monsterHitStop));
                    lethalHitStop = Mathf.Max(0f, entry.lethalHitStopSeconds.Or(lethalHitStop));
                }

                visual = entry.visualImpactOffset.Or(visual);
                shake = entry.shakeImpactOffset.Or(shake);
                hitStopOffset = entry.hitStopImpactOffset.Or(hitStopOffset);
            }

            return new ResolvedAttackTiming(
                windup, impact, death,
                playerHitStop, monsterHitStop, lethalHitStop,
                visual, shake, hitStopOffset);
        }

        // Default timing id for the lab's generic player-attack replay (sourceRef "A01" classifies as a basic
        // player attack). The lab may point the replay at any authored card id via DebugReplayPlayerAttackTimingId.
        private const string PlayerBasicAttackTimingId = "A01";

        private string debugReplayPlayerAttackTimingId = PlayerBasicAttackTimingId;

        /// <summary>Timing-table id used for the lab's player-attack replay. Falls back to the basic-attack id.</summary>
        public string DebugReplayPlayerAttackTimingId
        {
            get => debugReplayPlayerAttackTimingId;
            set => debugReplayPlayerAttackTimingId = string.IsNullOrWhiteSpace(value) ? PlayerBasicAttackTimingId : value;
        }

        /// <summary>The per-attack timing table this controller resolves overrides from (may be null).</summary>
        public CombatAttackTimingTable AttackTimingTable => attackTimingTable;

        // Catalog id (e.g. "A01") of a hand/combat card by its selection key, for per-attack timing lookup.
        // Falls back to the selection key so an authored row can also key on the instance/selection key, and to
        // an empty string when nothing is selected (ResolveAttackTiming then yields the global profile values).
        private string ResolveAttackCardCatalogId(string selectionKey)
        {
            if (State == null || string.IsNullOrWhiteSpace(selectionKey))
            {
                return selectionKey;
            }

            var card = State.GetCombatCards()
                .FirstOrDefault(c => string.Equals(c.SelectionKey, selectionKey, System.StringComparison.Ordinal));
            return string.IsNullOrWhiteSpace(card.Id) ? selectionKey : card.Id;
        }

        /// <summary>
        /// Distinct attack cards (catalog id + display name) currently in the player's deck/hand, for the
        /// timing lab's per-card replay buttons. Empty when no combat state or no attack cards are present.
        /// </summary>
        public IReadOnlyList<KeyValuePair<string, string>> GetPlayerAttackCardChoices()
        {
            var choices = new List<KeyValuePair<string, string>>();
            if (State == null)
            {
                return choices;
            }

            var seen = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var card in State.GetCombatCards())
            {
                if (card.Kind != CombatCardKind.Attack || string.IsNullOrWhiteSpace(card.Id) || !seen.Add(card.Id))
                {
                    continue;
                }

                choices.Add(new KeyValuePair<string, string>(card.Id, string.IsNullOrWhiteSpace(card.Name) ? card.Id : card.Name));
            }

            return choices;
        }

        private float ResolvePlayerMoveSeconds(HexCoord from, HexCoord to)
        {
            if (timingProfile != null)
            {
                return timingProfile.ResolvePlayerMoveSeconds(from.DistanceTo(to));
            }

            return playerMoveSeconds;
        }

        private float ResolveEnemyMoveSeconds(HexCoord from, HexCoord to)
        {
            if (timingProfile != null)
            {
                return timingProfile.ResolveEnemyMoveSeconds(from.DistanceTo(to));
            }

            return enemyMoveSeconds;
        }

        // --- Dev/tuning hooks (editor/dev-build CombatDebugControlPanel "Timing" tab only) ---
        // Expose the live timing profile and a repeatable attack replay so designers can tune the
        // attack wind-up, impact, and reaction beat without managing cards/ki/targets. These reuse the real
        // ResolveAttackSequence + effect buffering, so what is tuned here matches live combat exactly.

        /// <summary>The timing profile this controller resolves presentation beats from (may be null).</summary>
        public CombatTimingProfile TimingProfile => timingProfile;

        private CombatPresentationSnapshot CapturePresentationSnapshot()
        {
            return new CombatPresentationSnapshot(State);
        }

        private static MonsterPresentationSnapshot ResolvePresentationAttackTarget(CombatPresentationSnapshot before, HexCoord target)
        {
            var directTarget = before.Monsters.FirstOrDefault(monster => !monster.IsDead && monster.Coord == target);
            return directTarget.Id != null ? directTarget : before.FirstLivingMonster;
        }

        private bool ShouldPlayPresentationSequence()
        {
            return Application.isPlaying && playPresentationSequences && !resolvePresentationSequencesImmediately;
        }

        // Record judgments live in MonsterActionPresentationFilters (pure runtime layer); these
        // wrappers keep the public test contract on this type.
        public static IReadOnlyList<MonsterActionResolutionRecord> GetPresentableMonsterMoveRecordsForTests(IEnumerable<MonsterActionResolutionRecord> records)
        {
            return MonsterActionPresentationFilters.GetPresentableMonsterMoveRecords(records);
        }

        public static string ResolvePresentableAttackingMonsterIdForTests(IEnumerable<MonsterActionResolutionRecord> records)
        {
            return MonsterActionPresentationFilters.ResolvePresentableAttackingMonsterId(records);
        }

        public static IReadOnlyList<MonsterActionResolutionRecord> GetPresentableMonsterAttackRecordsForTests(IEnumerable<MonsterActionResolutionRecord> records)
        {
            return MonsterActionPresentationFilters.GetPresentableMonsterAttackRecords(records);
        }

        public static IReadOnlyList<MonsterActionResolutionRecord> GetPresentableMonsterActionRecordsForTests(IEnumerable<MonsterActionResolutionRecord> records)
        {
            return MonsterActionPresentationFilters.GetPresentableMonsterActionRecords(records);
        }

        private void StartPresentationSequence(string resolvingText, IEnumerator sequence, bool refreshHudAtStart = true)
        {
            CancelActivePresentationSequence(commitCurrentState: false);
            isSequencePlaying = true;
            presentationPhase = CombatPresentationPhase.None;
            resolvingStatusText = resolvingText;
            presentationSequenceVersion++;
            activeSequenceRoutine = StartCoroutine(sequence);
            RefreshMapVisibilityAndHighlights();
            if (refreshHudAtStart)
            {
                RefreshHudOnly();
            }
        }

        private bool IsCurrentPresentationToken(int capturedGeneration, int capturedSequenceVersion)
        {
            return isActiveAndEnabled
                && State != null
                && capturedGeneration == stateGeneration
                && capturedSequenceVersion == presentationSequenceVersion;
        }

        private IEnumerator ResolveMoveSequence(CombatPresentationSnapshot before, CombatPresentationSnapshot after, string finalMessage)
        {
            var capturedGeneration = stateGeneration;
            var capturedVersion = presentationSequenceVersion;
            SetPresentationAnchors(before);
            EnterPresentationPhase(CombatPresentationPhase.PlayerMoving, "Moving player...");
            yield return AnimatePlayerMove(before.PlayerCoord, after.PlayerCoord, ResolvePlayerMoveSeconds(before.PlayerCoord, after.PlayerCoord));
            if (!IsCurrentPresentationToken(capturedGeneration, capturedVersion))
            {
                yield break;
            }

            var enemyMoveStartDelaySeconds = EffectiveEnemyMoveStartDelay;
            foreach (var beforeEnemy in before.Monsters.Where(monster => !monster.IsDead))
            {
                var afterEnemy = after.FindMonster(beforeEnemy.Id);
                if (beforeEnemy.Id == null || afterEnemy.Id != beforeEnemy.Id || afterEnemy.IsDead || beforeEnemy.Coord == afterEnemy.Coord)
                {
                    continue;
                }

                if (enemyMoveStartDelaySeconds > 0f)
                {
                    yield return new WaitForSeconds(enemyMoveStartDelaySeconds);
                }

                EnterPresentationPhase(CombatPresentationPhase.MonstersMoving, "Monsters are moving...");
                yield return AnimateEnemyMove(beforeEnemy.Id, beforeEnemy.Coord, afterEnemy.Coord, ResolveEnemyMoveSeconds(beforeEnemy.Coord, afterEnemy.Coord));
            }

            if (!IsCurrentPresentationToken(capturedGeneration, capturedVersion))
            {
                yield break;
            }

            CompletePresentationSequence(finalMessage);
        }

        private IEnumerator ResolveAttackSequence(CombatPresentationSnapshot before, CombatPresentationSnapshot after, HexCoord target, string targetMonsterId, string finalMessage, string attackId)
        {
            var capturedGeneration = stateGeneration;
            var capturedVersion = presentationSequenceVersion;
            // Per-attack timing (cardId override over the global profile; equals the global values when the
            // table has no row for this id, so unthreaded callers keep the legacy feel).
            var timing = ResolveAttackTiming(attackId);
            SetPresentationAnchors(before);
            EnterPresentationPhase(CombatPresentationPhase.PlayerAttacking, "Resolving player attack...");
            ApplyPlayerHexFacing(before.PlayerCoord, target);
            atlasTilePresentationView?.TriggerPlayerAttack();
            var playerAttackWindupDelay = timing.WindupDelay;
            if (playerAttackWindupDelay > 0f)
            {
                yield return new WaitForSeconds(playerAttackWindupDelay);
            }

            // Per-channel impact offsets: shift the flush (and the target hit-reaction motion that fires with
            // it) earlier/later by the most-negative channel offset, so each channel can lead or trail. When no
            // offsets are authored this is 0 and timing is identical to before.
            var impactSynced = timingProfile != null
                && timingProfile.AlignImpactToAnimation
                && State != null
                && State.IsBufferingEffects;
            var impactChannelBase = impactSynced ? timing.MinChannelOffset : 0f;

            var playerAttackImpactDelay = timing.ImpactDelay + impactChannelBase;
            if (playerAttackImpactDelay > 0f)
            {
                yield return new WaitForSeconds(playerAttackImpactDelay);
            }

            if (!IsCurrentPresentationToken(capturedGeneration, capturedVersion))
            {
                yield break;
            }

            var beforeTarget = before.FindMonster(targetMonsterId);
            var afterTarget = after.FindMonster(targetMonsterId);
            var hitStopSeconds = 0f;
            var targetKnockedBack = beforeTarget.Id != null
                && afterTarget.Id == targetMonsterId
                && !beforeTarget.IsDead
                && !afterTarget.IsDead
                && beforeTarget.Coord != afterTarget.Coord;
            if (beforeTarget.Id != null && afterTarget.Id == targetMonsterId)
            {
                if (!beforeTarget.IsDead && afterTarget.IsDead)
                {
                    actorMarkerPresenter.TriggerDead(targetMonsterId);
                    hitStopSeconds = timing.ResolvePlayerHitStop(lethal: true);
                }
                else if (targetKnockedBack)
                {
                    actorMarkerPresenter.TriggerKnockback(targetMonsterId);
                    hitStopSeconds = timing.ResolvePlayerHitStop(lethal: false);
                }
                else if (afterTarget.Hp < beforeTarget.Hp)
                {
                    actorMarkerPresenter.TriggerHit(targetMonsterId);
                    hitStopSeconds = timing.ResolvePlayerHitStop(lethal: false);
                }
            }

            // Impact frame: release buffered attack effects so damage numbers, hit SFX/VFX, and
            // camera-shake land on the same beat as the target hit/death motion. Publish this attack's
            // resolved per-channel delays for the duration of the (synchronous) flush so the SFX/VFX/shake
            // subscribers honor per-attack offsets; cleared right after so other paths read the global profile.
            activeImpactTiming = timing;
            State?.FlushBufferedEffects();
            activeImpactTiming = null;

            if (targetKnockedBack)
            {
                yield return AnimateEnemyKnockback(
                    targetMonsterId,
                    beforeTarget.Coord,
                    afterTarget.Coord,
                    ResolveEnemyMoveSeconds(beforeTarget.Coord, afterTarget.Coord));

                if (!IsCurrentPresentationToken(capturedGeneration, capturedVersion))
                {
                    yield break;
                }
            }

            if (hitStopSeconds > 0f)
            {
                // Offset the hit-stop relative to the impact beat (>=0 from the shifted flush).
                var hitStopImpactDelay = impactSynced ? timing.HitStopImpactDelay : 0f;
                if (hitStopImpactDelay > 0f)
                {
                    yield return new WaitForSeconds(hitStopImpactDelay);
                }

                yield return HitStop.PlayHitStop(hitStopSeconds, true, targetMonsterId);
            }

            if (!IsCurrentPresentationToken(capturedGeneration, capturedVersion))
            {
                yield break;
            }

            if (beforeTarget.Id != null && afterTarget.Id == targetMonsterId && !beforeTarget.IsDead && afterTarget.IsDead)
            {
                var targetDeathDelay = timing.DeathDelay;
                if (targetDeathDelay > 0f)
                {
                    yield return new WaitForSeconds(targetDeathDelay);
                }
            }

            CompletePresentationSequence(finalMessage);
        }

        private IEnumerator ResolveEndActionSequence(CombatPresentationSnapshot before, CombatPresentationSnapshot after, IReadOnlyList<MonsterActionResolutionRecord> monsterActionRecords, bool playerDied, string finalMessage)
        {
            var capturedGeneration = stateGeneration;
            var capturedVersion = presentationSequenceVersion;
            SetPresentationAnchors(before);
            var projectedPlayerHp = before.PlayerHp;
            var playerKnockedBack = before.PlayerCoord != after.PlayerCoord;
            var playerKnockbackPresented = false;

            foreach (var actionRecord in MonsterActionPresentationFilters.GetPresentableMonsterActionRecords(monsterActionRecords))
            {
                if (!IsCurrentPresentationToken(capturedGeneration, capturedVersion))
                {
                    yield break;
                }

                if (actionRecord.Moved)
                {
                    EnterPresentationPhase(
                        CombatPresentationPhase.MonstersMoving,
                        $"{FormatMonsterDisplayName(actionRecord.MonsterId)} is moving...",
                        $"{FormatMonsterDisplayName(actionRecord.MonsterId)} moved.");
                    yield return AnimateEnemyMove(actionRecord.MonsterId, actionRecord.BeforeCoord, actionRecord.AfterCoord, ResolveEnemyMoveSeconds(actionRecord.BeforeCoord, actionRecord.AfterCoord));

                    if (!IsCurrentPresentationToken(capturedGeneration, capturedVersion))
                    {
                        yield break;
                    }
                }

                if (playerKnockedBack && !playerKnockbackPresented && actionRecord.AffectedPlayer)
                {
                    EnterPresentationPhase(CombatPresentationPhase.PlayerMoving, "Player is knocked back...");
                    atlasTilePresentationView?.TriggerPlayerKnockback();
                    yield return AnimatePlayerKnockback(before.PlayerCoord, after.PlayerCoord, ResolvePlayerMoveSeconds(before.PlayerCoord, after.PlayerCoord));
                    FlushMonsterKnockbackEffectsAtImpact(actionRecord);
                    playerKnockbackPresented = true;

                    if (!IsCurrentPresentationToken(capturedGeneration, capturedVersion))
                    {
                        yield break;
                    }
                }

                if (actionRecord.AttackedPlayer)
                {
                    EnterPresentationPhase(
                        CombatPresentationPhase.MonsterAttacking,
                        $"{FormatMonsterDisplayName(actionRecord.MonsterId)} is attacking...",
                        FormatMonsterAttackMessage(actionRecord));

                    // 예고한 방향 유지(#7) — 실좌표가 아니라 커밋 시점의 조준 칸을 본다. 어셈블러 경로와
                    // 같은 규칙이라 두 경로가 갈라지지 않는다.
                    ApplyMonsterHexFacing(actionRecord.MonsterId, actionRecord.AfterCoord, actionRecord.AimCoord ?? before.PlayerCoord);

                    // Per-pattern timing for this monster's attack (equals global when no override row exists,
                    // so live combat is unchanged unless a patternId is authored in the timing table).
                    var monsterTiming = ResolveAttackTiming(actionRecord.AttackPatternId);

                    var monsterAttackWindupDelay = monsterTiming.WindupDelay;
                    if (monsterAttackWindupDelay > 0f)
                    {
                        yield return new WaitForSeconds(monsterAttackWindupDelay);
                    }

                    actorMarkerPresenter.TriggerAttack(actionRecord.MonsterId, actionRecord.AttackAnimationTrigger);

                    var monsterAttackImpactDelay = monsterTiming.ImpactDelay;
                    if (monsterAttackImpactDelay > 0f)
                    {
                        yield return new WaitForSeconds(monsterAttackImpactDelay);
                    }

                    if (!IsCurrentPresentationToken(capturedGeneration, capturedVersion))
                    {
                        yield break;
                    }

                    var lethalHit = actionRecord.DamageToPlayer > 0
                        && projectedPlayerHp > 0
                        && projectedPlayerHp - actionRecord.DamageToPlayer <= 0;
                    if (actionRecord.AffectedPlayer && actionRecord.DamageToPlayer > 0 && !playerDied)
                    {
                        atlasTilePresentationView?.TriggerPlayerHit();
                    }

                    // Publish this pattern's per-channel delays for the duration of the impact flush so the
                    // SFX/VFX/shake subscribers honor any per-pattern offset (matches the global profile when
                    // none is authored).
                    activeImpactTiming = monsterTiming;
                    FlushMonsterAttackEffectsAtImpact(actionRecord);
                    activeImpactTiming = null;

                    var monsterHitStopSeconds = monsterTiming.ResolveMonsterHitStop(lethalHit);
                    if (monsterHitStopSeconds > 0f)
                    {
                        yield return HitStop.PlayHitStop(monsterHitStopSeconds, true, actionRecord.MonsterId);
                    }

                    projectedPlayerHp = Mathf.Max(0, projectedPlayerHp - actionRecord.DamageToPlayer);
                }
            }

            if (playerKnockedBack && !playerKnockbackPresented)
            {
                EnterPresentationPhase(CombatPresentationPhase.PlayerMoving, "Player is knocked back...");
                atlasTilePresentationView?.TriggerPlayerKnockback();
                yield return AnimatePlayerKnockback(before.PlayerCoord, after.PlayerCoord, ResolvePlayerMoveSeconds(before.PlayerCoord, after.PlayerCoord));

                if (!IsCurrentPresentationToken(capturedGeneration, capturedVersion))
                {
                    yield break;
                }
            }

            State?.FlushBufferedEffects();

            if (!IsCurrentPresentationToken(capturedGeneration, capturedVersion))
            {
                yield break;
            }

            if (playerDied)
            {
                EnterPresentationPhase(CombatPresentationPhase.Completing, "Player defeated...");
                RecordPlayerDeathSource(monsterActionRecords);
                BeginPlayerDeathSequence();
                yield return HoldPlayerDeathSequence(EffectiveDeathDelay);
            }

            if (!IsCurrentPresentationToken(capturedGeneration, capturedVersion))
            {
                yield break;
            }

            CompletePresentationSequence(finalMessage);
        }

        private void FlushMonsterAttackEffectsAtImpact(MonsterActionResolutionRecord actionRecord)
        {
            if (State == null || !State.IsBufferingEffects)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(actionRecord.PresentationGroupId))
            {
                var groupId = actionRecord.PresentationGroupId;
                State.FlushBufferedEffects(effect => string.Equals(effect.PresentationGroupId, groupId, System.StringComparison.Ordinal));
                return;
            }

            if (!string.IsNullOrWhiteSpace(actionRecord.MonsterId))
            {
                var monsterId = actionRecord.MonsterId;
                State.FlushBufferedEffects(effect => string.Equals(effect.SourceUnitId, monsterId, System.StringComparison.Ordinal));
            }
        }

        private void FlushMonsterKnockbackEffectsAtImpact(MonsterActionResolutionRecord actionRecord)
        {
            if (State == null || !State.IsBufferingEffects)
            {
                return;
            }

            State.FlushBufferedEffects(effect =>
                effect.Kind == EffectKind.Knockback
                && string.Equals(effect.SourceRef, "knockback", System.StringComparison.Ordinal)
                && string.Equals(effect.SourceUnitId, actionRecord.MonsterId, System.StringComparison.Ordinal));
        }

        private void BeginPlayerDeathSequence()
        {
            if (playerDeathSequenceAwaitingHold)
            {
                return;
            }

            playerDeathSequenceAwaitingHold = true;

            atlasTilePresentationView?.TriggerPlayerDead();
            PlayPlayerDeathVfx();

            if (!string.IsNullOrWhiteSpace(playerDeathAudioCueId))
            {
                RequestAudioCue(playerDeathAudioCueId, "player-death");
            }

            PerformPlayerDeathCameraShake();
            BeginPlayerDeathImpactHold();
        }

        private void RecordPlayerDeathSource(IReadOnlyList<MonsterActionResolutionRecord> monsterActionRecords)
        {
            MonsterActionResolutionRecord? lethalRecord = monsterActionRecords?
                .Where(record => record.DamageToPlayer > 0)
                .OrderByDescending(record => record.AttackOrder)
                .ThenByDescending(record => record.ActionOrder)
                .FirstOrDefault();

            if (lethalRecord.HasValue && !string.IsNullOrWhiteSpace(lethalRecord.Value.MonsterId))
            {
                RecordPlayerDeathSource(lethalRecord.Value.MonsterId);
            }
            else
            {
                lastPlayerDeathSourceText = "\uC54C \uC218 \uC5C6\uB294 \uC801 \uACF5\uACA9";
            }
        }

        private void RecordPlayerDeathSource(string monsterId)
        {
            if (string.IsNullOrWhiteSpace(monsterId))
            {
                lastPlayerDeathSourceText = "\uC54C \uC218 \uC5C6\uB294 \uC801 \uACF5\uACA9";
                return;
            }

            var displayName = FormatMonsterDisplayName(monsterId);
            if (State != null)
            {
                var monster = State.Monsters.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, monsterId, System.StringComparison.Ordinal));
                if (!string.IsNullOrWhiteSpace(monster.DefinitionId) &&
                    State.MonsterCatalog.TryGetEntry(monster.DefinitionId, out var entry) &&
                    !string.IsNullOrWhiteSpace(entry.DisplayName))
                {
                    displayName = entry.DisplayName;
                }
            }

            lastPlayerDeathSourceText = displayName;
        }

        private IEnumerator HoldPlayerDeathSequence(float fallbackHoldSeconds)
        {
            var hold = ResolvePlayerDeathSequenceHold(fallbackHoldSeconds);
            var slowMotionHold = Mathf.Min(hold, Mathf.Max(0f, EffectivePlayerDeathSlowMotionDuration));
            if (slowMotionHold > 0f)
            {
                yield return WaitCinematicRealtime(slowMotionHold);
            }

            RestorePlayerDeathSlowMotion();

            var remaining = hold - slowMotionHold;
            if (remaining > 0f)
            {
                yield return WaitCinematicRealtime(remaining);
            }

            playerDeathSequenceAwaitingHold = false;
            ShowGameOverOverlay();
            PlayerDeathSequenceCompleted?.Invoke();
        }

        // Filming-only scale on the death-freeze pair (hit stop + slow motion + the zoom that follows
        // them). 1 everywhere outside a trailer take; the runner sets/clears it so the trailer can shorten
        // the freeze without touching the authored gameplay feel.
        private float EffectivePlayerDeathHitStopDuration =>
            playerDeathHitStopDuration * DebugFacade.PlayerDeathFreezeDurationScale;

        private float EffectivePlayerDeathSlowMotionDuration =>
            playerDeathSlowMotionDuration * DebugFacade.PlayerDeathFreezeDurationScale;

        // Realtime hold that stays correct under fixed-rate frame capture: WaitForSecondsRealtime runs on
        // the wall clock, which the capture pipeline records far slower than realtime, so every realtime
        // wait in the death beat came out compressed in footage (the same failure the cinematics fixed via
        // CinematicDeltaTime — see DebugSetCinematicCaptureTimeSource). Off-capture this accumulates
        // unscaledDeltaTime, i.e. the same clock WaitForSecondsRealtime uses.
        private IEnumerator WaitCinematicRealtime(float seconds)
        {
            var elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += CinematicUnscaledDeltaTime;
                yield return null;
            }
        }

        private float ResolvePlayerDeathSequenceHold(float fallbackHoldSeconds)
        {
            if (playerDeathSequenceDuration > 0f)
            {
                return playerDeathSequenceDuration;
            }

            var clipLength = ResolvePlayerDeathAudioClipLength();
            return clipLength > 0f ? clipLength : Mathf.Max(0f, fallbackHoldSeconds);
        }

        private float ResolvePlayerDeathAudioClipLength()
        {
            if (soundCatalog == null || string.IsNullOrWhiteSpace(playerDeathAudioCueId))
            {
                return 0f;
            }

            return soundCatalog.TryGetEntry(playerDeathAudioCueId, out var entry) && entry != null && entry.Clip != null
                ? Mathf.Max(0f, entry.Clip.length)
                : 0f;
        }

        private void PlayPlayerDeathVfx()
        {
            var presentation = ResolveMovementEffectPresentation();
            var catalog = presentation != null ? presentation.VfxCatalog : null;
            if (presentation == null || catalog == null)
            {
                return;
            }

            var resultEvent = new EffectResultEvent(
                EffectKind.Damage,
                targetUnitId: State != null ? State.Player.Id : "player",
                amount: 0,
                appliedAmount: 0,
                sourceRef: PlayerDeathVfxSourceRef);
            var entries = catalog.ResolveAll(resultEvent);
            if (entries == null || entries.Length == 0 || !entries.Any(entry => entry != null && entry.HasSourceRefRule))
            {
                return;
            }

            if (TryGetCombatantVfxAnchorWorldPosition("player", CharacterVfxAnchorKind.Ground, out var worldPosition))
            {
                PlayPlayerDeathVfxEntries(presentation, entries, resultEvent, worldPosition);
            }
            else
            {
                PlayPlayerDeathVfxEntries(presentation, entries, resultEvent, transform.position);
            }
        }

        private static void PlayPlayerDeathVfxEntries(
            EffectPresentationController presentation,
            IEnumerable<EffectVfxCatalog.Entry> entries,
            EffectResultEvent resultEvent,
            Vector3 worldPosition)
        {
            foreach (var entry in entries)
            {
                if (entry == null)
                {
                    continue;
                }

                presentation.PlayResolvedEntry(resultEvent, entry, worldPosition, Quaternion.identity, showFloatingText: false);
            }
        }

        private void PerformPlayerDeathCameraShake()
            => PerformPlayerDeathCameraShake(
                playerDeathCameraShakeStrength,
                playerDeathCameraShakeDuration,
                playerDeathCameraShakeVibrato,
                playerDeathCameraShakeRandomness);

        private void PerformPlayerDeathSettleCameraShake()
            => PerformPlayerDeathCameraShake(
                playerDeathSettleCameraShakeStrength,
                playerDeathSettleCameraShakeDuration,
                playerDeathSettleCameraShakeVibrato,
                playerDeathSettleCameraShakeRandomness);

        private void PerformPlayerDeathCameraShake(float strength, float duration, int vibrato, float randomness)
        {
            AddCinemachineCameraShake(
                strength,
                duration,
                vibrato,
                randomness);
        }

        private void BeginPlayerDeathImpactHold()
        {
            StopPlayerDeathHitStopRoutine();
            if (!Application.isPlaying || EffectivePlayerDeathHitStopDuration <= 0f)
            {
                BeginPlayerDeathSlowMotion();
                BeginPlayerDeathCameraZoom();
                PerformPlayerDeathSettleCameraShake();
                return;
            }

            playerDeathHitStopRoutine = StartCoroutine(PlayPlayerDeathImpactHold());
        }

        private IEnumerator PlayPlayerDeathImpactHold()
        {
            // Let the death trigger advance one frame so the frozen pose is the actual death impact.
            yield return null;

            HitStop.BeginAnimationPause(includePlayer: true, affectedMonsterIds: null);
            HitStop.BeginTimeScale(Mathf.Clamp(playerDeathHitStopTimeScale, 0.01f, 1f));
            yield return WaitCinematicRealtime(EffectivePlayerDeathHitStopDuration);
            HitStop.RestoreTimeScale();
            HitStop.RestoreAnimationSpeeds();

            playerDeathHitStopRoutine = null;
            BeginPlayerDeathSlowMotion();
            BeginPlayerDeathCameraZoom();
            PerformPlayerDeathSettleCameraShake();
        }

        private void StopPlayerDeathHitStopRoutine()
        {
            if (playerDeathHitStopRoutine != null)
            {
                StopCoroutine(playerDeathHitStopRoutine);
                playerDeathHitStopRoutine = null;
            }
        }

        private void BeginPlayerDeathSlowMotion() =>
            HitStop.BeginPlayerDeathSlowMotion(playerDeathSlowMotionScale, EffectivePlayerDeathSlowMotionDuration);

        private void RestorePlayerDeathSlowMotion() =>
            hitStopController?.RestorePlayerDeathSlowMotion();

        private void BeginPlayerDeathCameraZoom()
        {
            if (!Application.isPlaying || !playerDeathZoomInEnabled || playerDeathZoomInMultiplier >= 0.999f)
            {
                return;
            }

            var duration = playerDeathZoomInDuration > 0f
                ? playerDeathZoomInDuration
                : Mathf.Max(0f, EffectivePlayerDeathSlowMotionDuration);
            var multiplier = Mathf.Clamp(playerDeathZoomInMultiplier, 0.25f, 1f);

            CameraController.BeginPlayerDeathZoom(
                prototype3DCamera,
                ResolveCinemachineCombatCameraBinder(),
                duration,
                multiplier,
                cameraOrthographicSize,
                playerDeathCameraYawOffsetDegrees,
                playerDeathCameraPitchOffsetDegrees);
        }

        private void RestorePlayerDeathCameraZoom()
        {
            CameraController.RestorePlayerDeathZoom(prototype3DCamera);
        }

        private CinemachineCombatCameraBinder ResolveCinemachineCombatCameraBinder()
        {
            var cameraTransform = prototype3DCamera != null ? prototype3DCamera.transform : null;
            if (cameraTransform != null)
            {
                var binder = cameraTransform.GetComponent<CinemachineCombatCameraBinder>()
                    ?? cameraTransform.GetComponentInChildren<CinemachineCombatCameraBinder>();
                if (binder != null)
                {
                    return binder;
                }
            }

            return FindFirstObjectByType<CinemachineCombatCameraBinder>();
        }

        private void EnterPresentationPhase(CombatPresentationPhase phase, string resolvingText, string inputMessage = null)
        {
            presentationPhase = phase;
            resolvingStatusText = resolvingText ?? string.Empty;
            if (!string.IsNullOrEmpty(inputMessage))
            {
                LastInputMessage = inputMessage;
            }

            RefreshHudOnly();
        }

        private static string FormatMonsterAttackMessage(MonsterActionResolutionRecord record)
        {
            var monsterName = FormatMonsterDisplayName(record.MonsterId);
            if (record.DamageToPlayer > 0)
            {
                return $"{monsterName} attacked for {record.DamageToPlayer} damage.";
            }

            return record.AffectedPlayer
                ? $"{monsterName} attacked."
                : $"{monsterName} attacked, but dealt no damage.";
        }

        private static string FormatMonsterDisplayName(string monsterId)
        {
            return string.IsNullOrWhiteSpace(monsterId) ? "Monster" : monsterId;
        }

        private IEnumerator AnimateEnemyMovesConcurrently(IReadOnlyList<MonsterActionResolutionRecord> moveRecords, float duration)
        {
            if (moveRecords == null || moveRecords.Count == 0)
            {
                yield break;
            }

            var moves = moveRecords
                .Where(record => !string.IsNullOrEmpty(record.MonsterId) && record.Moved)
                .Select(record => new EnemyMovePresentation(
                    record.MonsterId,
                    ProjectEnemyMarkerLocalPosition(record.BeforeCoord),
                    ProjectEnemyMarkerLocalPosition(record.AfterCoord)))
                .ToList();
            if (moves.Count == 0)
            {
                yield break;
            }

            foreach (var move in moves)
            {
                EnsureEnemyMarker(move.MonsterId);
                actorMarkerPresenter.SetActive(move.MonsterId, true);
                actorMarkerPresenter.FaceToward(move.MonsterId, move.FromLocal, move.ToLocal);
                actorMarkerPresenter.SetMoveSpeed(move.MonsterId, 1f);
            }

            if (!Application.isPlaying || duration <= 0f)
            {
                foreach (var move in moves)
                {
                    SetEnemyMarkerPresentationPosition(move.MonsterId, move.ToLocal);
                    actorMarkerPresenter.SetMoveSpeed(move.MonsterId, 0f);
                }

                yield break;
            }

            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                foreach (var move in moves)
                {
                    SetEnemyMarkerPresentationPosition(move.MonsterId, Vector3.Lerp(move.FromLocal, move.ToLocal, t));
                }

                yield return null;
            }

            foreach (var move in moves)
            {
                SetEnemyMarkerPresentationPosition(move.MonsterId, move.ToLocal);
                actorMarkerPresenter.SetMoveSpeed(move.MonsterId, 0f);
            }
        }

        private IEnumerator AnimatePlayerMove(HexCoord from, HexCoord to, float duration)
        {
            PreparePlayerMovePresentation(from, to);
            if (!from.Equals(to))
            {
                RequestAudioCue(AudioCueIds.CardMoveResolve, $"player-move:{from}->{to}");
            }

            if (atlasTilePresentationView != null)
            {
                yield return atlasTilePresentationView.AnimatePlayerMove(from, to, duration);
                CompletePlayerMovePresentation();
                yield break;
            }

            if (duration > 0f)
            {
                yield return new WaitForSeconds(duration);
            }

            CompletePlayerMovePresentation();
        }

        private IEnumerator AnimatePlayerKnockback(HexCoord from, HexCoord to, float duration)
        {
            if (atlasTilePresentationView != null)
            {
                yield return atlasTilePresentationView.AnimatePlayerKnockback(from, to, duration);
                yield break;
            }

            if (duration > 0f)
            {
                yield return new WaitForSeconds(duration);
            }
        }

        private void PreparePlayerMovePresentation(HexCoord from, HexCoord to)
        {
            if (from.Equals(to))
            {
                return;
            }

            if (atlasTilePresentationView != null)
            {
                var fromLocal = atlasTilePresentationView.ProjectOverlaySurface(from);
                var toLocal = atlasTilePresentationView.ProjectOverlaySurface(to);
                var localDirection = toLocal - fromLocal;
                localDirection.y = 0f;
                if (localDirection.sqrMagnitude > 0.0001f)
                {
                    var facingDirection = snapPlayerMoveVfxToHexSides
                        ? CombatFacingUtility.SnapToNearestHexSideDirection(localDirection, playerMoveVfxYawOffset)
                        : localDirection.normalized;
                    atlasTilePresentationView.SetPlayerFacingDirectionLocal(facingDirection);
                    atlasTilePresentationView.SetPlayerMoveSpeed(1f);
                }
            }

            PlayPlayerMoveVfx(from, to);
        }

        private void CompletePlayerMovePresentation()
        {
            if (atlasTilePresentationView != null)
            {
                atlasTilePresentationView.SetPlayerMoveSpeed(0f);
            }

            if (playerMoveDustLoop != null && playerMoveDustLoop.IsAlive && playerMoveDustStopRoutine == null && isActiveAndEnabled)
            {
                // Emission is cut right now, not after the grace: the actor has already stopped, so any
                // further puff would bloom next to a standing character. If another path segment follows,
                // Prepare resumes emission a frame or two later — an invisible gap in a smoke stream.
                var presentation = ResolveMovementEffectPresentation();
                if (presentation != null)
                {
                    presentation.PauseFollowingLoopEmission(playerMoveDustLoop);
                }

                playerMoveDustStopRoutine = StartCoroutine(StopPlayerMoveDustAfterGrace());
            }
        }

        private void ApplyPlayerHexFacing(HexCoord from, HexCoord to)
        {
            if (atlasTilePresentationView == null || from == to)
            {
                return;
            }

            var fromLocal = atlasTilePresentationView.ProjectOverlaySurface(from);
            var toLocal = atlasTilePresentationView.ProjectOverlaySurface(to);
            var direction = toLocal - fromLocal;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            var facing = snapPlayerMoveVfxToHexSides
                ? CombatFacingUtility.SnapToNearestHexSideDirection(direction, playerMoveVfxYawOffset)
                : direction.normalized;
            atlasTilePresentationView.SetPlayerFacingDirectionLocal(facing);
        }

        private CurseCardInjectionPresenter curseCardInjectionPresenter;

        /// <summary>
        /// 저주 주입 연출(2026-09-01 #9)의 주인. 하단 HUD가 런타임에 서므로 여기서 붙잡고 있다가
        /// <c>GameplayHudBridge</c>가 매 갱신에 비행 기구·목적지를 물려 준다 — 배선이 늦게 도착해도
        /// 다음 갱신에 저절로 맞는다.
        /// </summary>
        public CurseCardInjectionPresenter CurseCardInjectionPresenter
        {
            get
            {
                if (curseCardInjectionPresenter == null)
                {
                    curseCardInjectionPresenter = gameObject.GetComponent<CurseCardInjectionPresenter>()
                        ?? gameObject.AddComponent<CurseCardInjectionPresenter>();
                }

                return curseCardInjectionPresenter;
            }
        }

        private void ApplyMonsterHexFacing(string monsterId, HexCoord from, HexCoord to)
        {
            if (string.IsNullOrEmpty(monsterId) || actorMarkerPresenter == null || from == to)
            {
                return;
            }

            var facingDir = from.ApproximateDirection(to);
            if (!TryGetTileWorldPosition(from, out var fromWorld) ||
                !TryGetTileWorldPosition(from.Neighbor(facingDir), out var toWorld))
            {
                return;
            }

            var forward = toWorld - fromWorld;
            forward.y = 0f;
            if (forward.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            actorMarkerPresenter.SetFacingDirection(monsterId, forward.normalized);
        }

        /// <summary>
        /// The id of the move card driving the current step, read back from the effect the rules layer
        /// already raised for it (<c>TryPlayerMove</c> / <c>TryPlayerMovementSelf</c> publish a Push on the
        /// player with the card id as its source ref). Empty when a move had no card behind it — a knockback
        /// slide, or a replay that bypassed the buffer.
        ///
        /// Read from the buffer rather than threaded through the timeline because the move timeline carries
        /// only step events: it never appends an Effect beat, so this Push is never dispatched and the id
        /// would otherwise be unavailable to presentation.
        /// </summary>
        private string ResolveActiveMoveCardId()
        {
            if (State == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrEmpty(State.LastPlayerMoveCardId))
            {
                return State.LastPlayerMoveCardId;
            }

            var buffered = State.BufferedEffects;
            for (var i = buffered.Count - 1; i >= 0; i--)
            {
                var candidate = buffered[i];
                if (candidate.Kind == EffectKind.Push
                    && string.Equals(candidate.TargetUnitId, "player", System.StringComparison.Ordinal)
                    && !string.IsNullOrEmpty(candidate.SourceRef))
                {
                    return candidate.SourceRef;
                }
            }

            return string.Empty;
        }

        private void PlayPlayerMoveVfx(HexCoord from, HexCoord to)
        {
            // Card-owned dust (CVM01…) is selected by the move card's own id, so it plays whenever the card
            // authored a cue — "no dust for this card" is expressed by not authoring a row, not by a flag.
            // The playPlayerMoveVfx flag predates per-card authoring and now governs only the generic
            // cardless dust (knockback slides), which is why the shipping scene can keep it off.
            var moveCardId = ResolveActiveMoveCardId();
            var cardOwned = !string.IsNullOrEmpty(moveCardId);
            if (!cardOwned && !playPlayerMoveVfx)
            {
                return;
            }

            var presentation = ResolveMovementEffectPresentation();
            if (presentation == null ||
                !TryGetTileWorldPosition(from, out var fromWorld) ||
                !TryGetTileWorldPosition(to, out var toWorld))
            {
                return;
            }

            var moveDirection = toWorld - fromWorld;
            moveDirection.y = 0f;
            if (moveDirection.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            var distance = Mathf.Max(1, from.DistanceTo(to));
            // Faces the way the player is travelling. This used to be negated, which pointed the dust the
            // opposite way; per-card fine tuning belongs in the cue's authored rotationY, not here.
            var facingRotation = CombatFacingUtility.ResolveHexSideRotation(moveDirection, snapPlayerMoveVfxToHexSides, playerMoveVfxYawOffset);
            var resultEvent = new EffectResultEvent(
                EffectKind.Push,
                targetUnitId: "player",
                amount: distance,
                appliedAmount: distance,
                center: from,
                // The card id, so CVM01…CVM06 (authored on exactly those ids) resolve. The old hardcoded
                // "player.move" matched no authored cue at all, which is why the dust never appeared.
                sourceRef: cardOwned ? moveCardId : "player.move",
                sourceCardId: cardOwned ? moveCardId : string.Empty);
            if (TryGetCombatantVfxAnchorWorldPosition("player", CharacterVfxAnchorKind.Ground, out var moveVfxWorld))
            {
                // One persistent looping emitter per move, glued to the live Ground anchor: every path
                // segment lands here, so the first segment starts the loop (particles fade in as the
                // emitter builds up) and later segments just steer its facing, which the pivot slews to
                // smoothly. CompletePlayerMovePresentation arms the wind-down.
                if (playerMoveDustStopRoutine != null)
                {
                    StopCoroutine(playerMoveDustStopRoutine);
                    playerMoveDustStopRoutine = null;
                }

                if (playerMoveDustLoop != null && playerMoveDustLoop.IsAlive)
                {
                    presentation.SetFollowingLoopFacing(playerMoveDustLoop, facingRotation);
                    presentation.ResumeFollowingLoopEmission(playerMoveDustLoop);
                }
                else
                {
                    playerMoveDustLoop = presentation.PlayFollowingLoop(resultEvent, ResolvePlayerGroundAnchorProvider(moveVfxWorld), facingRotation);
                }
            }
            else
            {
                presentation.Play(resultEvent, fromWorld, facingRotation);
            }
        }

        // The dust emitter for the move currently animating. Held here (not in the presentation
        // controller) because only the move presentation knows when steps start and stop.
        private EffectPresentationController.FollowingLoopHandle playerMoveDustLoop;
        private Coroutine playerMoveDustStopRoutine;

        // A short grace instead of stopping in CompletePlayerMovePresentation directly: between two path
        // segments Complete fires and the next Prepare follows within a frame or two, and stopping there
        // would flicker the emitter off and on at every tile boundary. Only a Complete with no follow-up
        // Prepare inside the grace is a real end of movement.
        private System.Collections.IEnumerator StopPlayerMoveDustAfterGrace()
        {
            yield return new WaitForSeconds(0.2f);
            playerMoveDustStopRoutine = null;
            var presentation = ResolveMovementEffectPresentation();
            if (presentation != null)
            {
                presentation.StopFollowingLoop(playerMoveDustLoop);
            }

            playerMoveDustLoop = null;
        }

        // Tracks the player's live Ground anchor so the move dust follows the marker as it animates between
        // tiles. Falls back to the captured spawn position if the anchor is momentarily unavailable.
        private System.Func<Vector3> ResolvePlayerGroundAnchorProvider(Vector3 fallbackWorld)
        {
            return () => TryGetCombatantVfxAnchorWorldPosition("player", CharacterVfxAnchorKind.Ground, out var live)
                ? live
                : fallbackWorld;
        }

        private EffectPresentationController ResolveMovementEffectPresentation()
        {
            if (movementEffectPresentation != null)
            {
                return movementEffectPresentation;
            }

            movementEffectPresentation = Object.FindFirstObjectByType<EffectPresentationController>();
            return movementEffectPresentation;
        }

        private IEnumerator AnimateEnemyMove(string monsterId, HexCoord from, HexCoord to, float duration)
        {
            EnsureEnemyMarker(monsterId);
            if (!from.Equals(to))
            {
                RequestAudioCue(AudioCueIds.MonsterMoveResolve, $"monster-move:{monsterId}:{from}->{to}");
            }

            var fromLocal = ProjectEnemyMarkerLocalPosition(from);
            var toLocal = ProjectEnemyMarkerLocalPosition(to);
            // A monster can become presentable because it moved into view. In that case the before-snapshot
            // anchor pass may have hidden its marker, so explicitly reveal and place it on the start tile
            // before the first interpolation frame. This prevents the following attack beat from appearing
            // to happen without a visible move.
            actorMarkerPresenter.SetActive(monsterId, true);
            SetEnemyMarkerPresentationPosition(monsterId, fromLocal);
            actorMarkerPresenter.FaceToward(monsterId, fromLocal, toLocal);
            actorMarkerPresenter.SetMoveSpeed(monsterId, 1f);

            if (!Application.isPlaying || duration <= 0f)
            {
                SetEnemyMarkerPresentationPosition(monsterId, toLocal);
                actorMarkerPresenter.SetMoveSpeed(monsterId, 0f);
                yield break;
            }

            yield return null;

            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                SetEnemyMarkerPresentationPosition(monsterId, Vector3.Lerp(fromLocal, toLocal, Mathf.Clamp01(elapsed / duration)));
                yield return null;
            }

            SetEnemyMarkerPresentationPosition(monsterId, toLocal);
            actorMarkerPresenter.SetMoveSpeed(monsterId, 0f);
        }

        private IEnumerator AnimateEnemyKnockback(string monsterId, HexCoord from, HexCoord to, float duration)
        {
            EnsureEnemyMarker(monsterId);
            var fromLocal = ProjectEnemyMarkerLocalPosition(from);
            var toLocal = ProjectEnemyMarkerLocalPosition(to);
            actorMarkerPresenter.SetActive(monsterId, true);
            SetEnemyMarkerPresentationPosition(monsterId, fromLocal);
            actorMarkerPresenter.FaceToward(monsterId, toLocal, fromLocal);

            if (!Application.isPlaying || duration <= 0f)
            {
                SetEnemyMarkerPresentationPosition(monsterId, toLocal);
                yield break;
            }

            yield return null;

            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                SetEnemyMarkerPresentationPosition(monsterId, Vector3.Lerp(fromLocal, toLocal, Mathf.Clamp01(elapsed / duration)));
                yield return null;
            }

            SetEnemyMarkerPresentationPosition(monsterId, toLocal);
        }

        private void SetPresentationAnchors(CombatPresentationSnapshot snapshot)
        {
            SetSelectedPlayerPosition(snapshot.PlayerCoord);
            ShowMonsterMarkers(snapshot.Monsters.Where(monster => !monster.IsDead));
        }

        private void CompletePresentationSequence(string finalMessage)
        {
            hitStopController?.RestoreTimeScale();
            hitStopController?.RestoreAnimationSpeeds();
            LastInputMessage = finalMessage;
            isSequencePlaying = false;
            presentationPhase = CombatPresentationPhase.None;
            resolvingStatusText = string.Empty;
            activeSequenceRoutine = null;
            // 안전망(§14.2): 전환 연출이 돌지 않은 경로(디버그 전진·서스펜드 복원·중단)에서도 시퀀스가
            // 끝나는 순간 크기는 저작값과 일치한다. 연출이 정상적으로 돌았다면 이미 같은 값이라 no-op다.
            SnapBossVisualScalesToAuthored();
            CommitPresentationFromState(immediateCamera: false);
            RefreshMapVisibilityAndHighlights();
            StatusLoopVfx.Reconcile();
            SyncFieldObjectVisuals();
            Physics.SyncTransforms();
            RefreshHudOnly();
        }

        private void CancelActivePresentationSequence(bool commitCurrentState)
        {
            presentationSequenceVersion++;
            if (activeSequenceRoutine != null)
            {
                StopCoroutine(activeSequenceRoutine);
                activeSequenceRoutine = null;
            }

            isSequencePlaying = false;
            presentationPhase = CombatPresentationPhase.None;
            resolvingStatusText = string.Empty;
            StopPlayerDeathHitStopRoutine();
            hitStopController?.RestoreTimeScale();
            hitStopController?.RestoreAnimationSpeeds();
            RestorePlayerDeathSlowMotion();
            RestorePlayerDeathCameraZoom();
            playerDeathSequenceAwaitingHold = false;
            // NOTE: do NOT flush buffered effects here. StartPresentationSequence cancels the prior
            // sequence *after* the new attack has already buffered its effects, so flushing on cancel
            // would dispatch them at t=0 (card-use frame) instead of the impact beat. The buffer is
            // owned solely by the attack flow: ResolveAttackSequence flushes at impact, and the
            // post-resolve guard in TryUseSelectedTargetCard flushes when no sequence took over.
            foreach (var monster in State?.Monsters ?? Enumerable.Empty<MonsterRuntimeState>())
            {
                actorMarkerPresenter.SetMoveSpeed(monster.Id, 0f);
            }
            CompletePlayerMovePresentation();
            if (commitCurrentState && State != null)
            {
                State.FinishDeferredMonsterActionState();
                CommitPresentationFromState(immediateCamera: !Application.isPlaying);
                RefreshHudOnly();
            }
        }

        private Vector3 ProjectEnemyMarkerLocalPosition(HexCoord coord)
        {
            return TryProjectSelected3DView(coord, out var projected)
                ? new Vector3(projected.x, projected.y + enemyMarkerHeightOffset, projected.z)
                : Vector3.zero;
        }

        private void SetEnemyMarkerPresentationPosition(Vector3 localPosition)
        {
            actorMarkerPresenter.SetLocalPosition(localPosition);
        }

        private void SetEnemyMarkerPresentationPosition(string monsterId, Vector3 localPosition)
        {
            actorMarkerPresenter.SetLocalPosition(monsterId, localPosition);
        }

        // Timeline scheduler integration (ICombatPresentationSink + Run*Timeline).
        // The scheduler is the pure ordering authority; these sink methods are thin wrappers that forward
        // each beat to the existing presentation primitives (tile view, actor markers, effect dispatch).

        private CombatTimingProfile ResolveTimingProfile()
        {
            if (timingProfile != null)
            {
                return timingProfile;
            }

            if (runtimeTimingProfileFallback == null)
            {
                runtimeTimingProfileFallback = ScriptableObject.CreateInstance<CombatTimingProfile>();
            }

            return runtimeTimingProfileFallback;
        }

        IEnumerator ICombatPresentationSink.Wait(float seconds)
        {
            if (seconds > 0f)
            {
                yield return new WaitForSeconds(seconds);
            }
        }

        IEnumerator ICombatPresentationSink.MovePlayerStep(HexCoord from, HexCoord to, float seconds)
            => AnimatePlayerMove(from, to, seconds);

        IEnumerator ICombatPresentationSink.MoveEnemyStep(string monsterId, HexCoord from, HexCoord to, float seconds)
        {
            // Measured at the destination: that is where the monster ends up and therefore what a focus
            // would have to frame.
            RecordActionFocusSample("enemy-move", monsterId, to);
            return AnimateEnemyMove(monsterId, from, to, seconds);
        }

        IEnumerator ICombatPresentationSink.KnockbackPlayerStep(HexCoord from, HexCoord to, float seconds)
            => AnimatePlayerKnockback(from, to, seconds);

        IEnumerator ICombatPresentationSink.KnockbackEnemyStep(string monsterId, HexCoord from, HexCoord to, float seconds)
            => AnimateEnemyKnockback(monsterId, from, to, seconds);

        void ICombatPresentationSink.StartPlayerAttack(HexCoord from, HexCoord to)
        {
            ApplyPlayerHexFacing(from, to);
            atlasTilePresentationView?.TriggerPlayerAttack();
        }

        void ICombatPresentationSink.StartEnemyAttack(string monsterId, string trigger, HexCoord from, HexCoord to)
        {
            // Measured at the attacker's own tile: the wind-up animation plays there, and that is what the
            // player needs to see to understand what is hitting them.
            RecordActionFocusSample("enemy-attack", monsterId, from);
            BeginMeleeAttackEmphasis(from);
            ApplyMonsterHexFacing(monsterId, from, to);
            actorMarkerPresenter.TriggerAttack(monsterId, trigger);
        }

        IEnumerator ICombatPresentationSink.AttackWindup(string timingKey)
        {
            var profile = ResolveTimingProfile();
            if (profile.AlignImpactToAnimationClip)
            {
                yield return null;
                yield break;
            }

            var windup = profile.ScaleDuration(ResolveAttackTiming(timingKey).WindupDelay);
            if (windup > 0f)
            {
                yield return new WaitForSeconds(windup);
            }
        }

        IEnumerator ICombatPresentationSink.AttackImpactWait(string timingKey, string actorId)
        {
            var profile = ResolveTimingProfile();
            var impact = profile.ScaleDuration(ResolveAttackTiming(timingKey).ImpactDelay);
            if (profile.AlignImpactToAnimationClip)
            {
                var strikeFraction = profile.AttackStrikeFraction;
                var maxWait = profile.AttackStrikeMaxWaitSeconds;
                if (string.Equals(actorId, "player", System.StringComparison.Ordinal))
                {
                    if (atlasTilePresentationView != null)
                    {
                        yield return atlasTilePresentationView.WaitForPlayerAttackStrike(strikeFraction, impact, maxWait);
                        yield break;
                    }
                }
                else if (!string.IsNullOrEmpty(actorId))
                {
                    yield return actorMarkerPresenter.WaitForAttackStrike(actorId, strikeFraction, impact, maxWait);
                    yield break;
                }
            }

            if (impact > 0f)
            {
                yield return new WaitForSeconds(impact);
            }
        }

        void ICombatPresentationSink.CommitImpact(string groupId)
        {
            State?.CommitDeferredMonsterActionImpact(groupId);
            RefreshHudOnly();
        }

        void ICombatPresentationSink.ReactActorHit(string monsterId, int effectIndex)
            => TriggerReactionAlignedToEffect(effectIndex, "hit", monsterId, () => actorMarkerPresenter.TriggerHit(monsterId));

        void ICombatPresentationSink.ReactActorDeath(string monsterId, int effectIndex)
            => TriggerReactionAlignedToEffect(effectIndex, "death", monsterId, () => actorMarkerPresenter.TriggerDead(monsterId));

        void ICombatPresentationSink.ReactActorKnockback(string monsterId, int effectIndex)
            => TriggerReactionAlignedToEffect(effectIndex, "knockback", monsterId, () => actorMarkerPresenter.TriggerKnockback(monsterId));

        void ICombatPresentationSink.ReactPlayerHit(int effectIndex)
            => TriggerReactionAlignedToEffect(effectIndex, "hit", "player", () => atlasTilePresentationView?.TriggerPlayerHit());

        void ICombatPresentationSink.ReactPlayerDeath(int effectIndex)
            => BeginPlayerDeathSequence();

        // Fires a flinch/death/knockback reaction in sync with the effect it belongs to: when that buffered
        // effect carries a per-cue playback delay, the reaction is held the same amount so they land together.
        private void TriggerReactionAlignedToEffect(int effectIndex, string reaction, string actorId, System.Action trigger)
        {
            var delay = 0f;
            if (State != null && effectIndex >= 0 && effectIndex < State.BufferedEffects.Count)
            {
                delay = ResolveEffectVfxDelaySeconds(State.BufferedEffects[effectIndex]);
            }

            if (delay > 0f && isActiveAndEnabled)
            {
                StartCoroutine(TriggerReactionAfterDelay(delay, reaction, actorId, trigger));
            }
            else
            {
                RecordReactionTrace(reaction, actorId, 0f);
                trigger();
            }
        }

        private IEnumerator TriggerReactionAfterDelay(float seconds, string reaction, string actorId, System.Action trigger)
        {
            yield return new WaitForSeconds(seconds);
            RecordReactionTrace(reaction, actorId, seconds);
            trigger();
        }

        private static void RecordReactionTrace(string reaction, string actorId, float delaySeconds)
            => CombatPresentationTrace.Record(
                CombatTraceChannel.React, $"{actorId} {reaction}", $"delay={delaySeconds:0.###}");

        // 은신 재진입 연출이 쏘는 애니메이터 트리거(2026-09-04 피드백). 은신 특성 보유자는 어둑시니뿐이고
        // 그 Attack4가 은신 전용 동작(eodukshini_utility)으로 저작돼 있다 — 새 은신 몬스터를 만들면
        // 같은 규약(Attack4 = 은신 진입 동작)을 따른다. 정본: monster_animation_attack_clips.csv M009 행.
        private const string StealthHideAnimationTrigger = "Attack4";

        void ICombatPresentationSink.DispatchEffect(int bufferedIndex)
        {
            if (State != null)
            {
                if (CombatPresentationTrace.IsRecording
                    && bufferedIndex >= 0
                    && bufferedIndex < State.BufferedEffects.Count)
                {
                    var effect = State.BufferedEffects[bufferedIndex];
                    CombatPresentationTrace.Record(
                        CombatTraceChannel.Dispatch,
                        $"#{bufferedIndex} {effect.Kind}",
                        $"src={effect.SourceRef} card={effect.SourceCardId} target={effect.TargetUnitId} amount={effect.AppliedAmount}");
                }

                if (CombatActionFocusMetrics.IsEnabled
                    && bufferedIndex >= 0
                    && bufferedIndex < State.BufferedEffects.Count)
                {
                    var measured = State.BufferedEffects[bufferedIndex];
                    if (TryResolveEffectFocusCoord(measured, out var effectCoord))
                    {
                        RecordActionFocusSample($"effect:{measured.Kind}", measured.TargetUnitId, effectCoord);
                    }
                }

                if (bufferedIndex >= 0 && bufferedIndex < State.BufferedEffects.Count)
                {
                    var buffered = State.BufferedEffects[bufferedIndex];
                    // 은신 재진입(trait.stealth.hidden)은 텍스트 전용 kind라 패턴 애니 채널이 없다 — 어둑시니가
                    // 연막 속으로 사라지는 동작(Attack4 = eodukshini_utility·연출 전용 트리거)을 여기서 쏜다.
                    // 마커 숨김(IsMonsterHiddenByStealth)은 타임라인 뒤 스냅샷 동기화에서 오므로 이 시점엔
                    // 아직 모델이 보인다 — 동작과 연막(V026)이 소실을 가리는 순서다.
                    if (buffered.Kind == EffectKind.MonsterTraitTriggered
                        && string.Equals(
                            buffered.SourceRef,
                            MonsterTraitAnnouncement.StealthHiddenRef,
                            System.StringComparison.Ordinal)
                        && actorMarkerPresenter != null)
                    {
                        actorMarkerPresenter.TriggerAttack(buffered.TargetUnitId, StealthHideAnimationTrigger);
                    }
                }

                State.DispatchBufferedEffectAt(bufferedIndex);
                dispatchedEffectIndices.Add(bufferedIndex);
            }
        }

        IEnumerator ICombatPresentationSink.HitStop(string timingKey, string actorId, bool lethal)
        {
            var timing = ResolveAttackTiming(timingKey);
            var seconds = string.Equals(actorId, "player", System.StringComparison.Ordinal)
                ? timing.ResolveMonsterHitStop(lethal)
                : timing.ResolvePlayerHitStop(lethal);
            return HitStop.PlayHitStop(ResolveTimingProfile().ScaleDuration(seconds), includePlayer: true, actorId);
        }

        IEnumerator ICombatPresentationSink.DeathHold(string timingKey)
        {
            var hold = ResolveTimingProfile().ScaleDuration(ResolveAttackTiming(timingKey).DeathDelay);
            if (playerDeathSequenceAwaitingHold)
            {
                yield return HoldPlayerDeathSequence(hold);
                yield break;
            }

            if (hold > 0f)
            {
                yield return new WaitForSeconds(hold);
            }
        }

        IEnumerator ICombatPresentationSink.MonsterActionGap()
        {
            var gap = ResolveTimingProfile().MonsterActionGapSeconds;
            if (gap > 0f)
            {
                yield return new WaitForSeconds(gap);
            }
        }

        /// <summary>
        /// The phase's camera cleanup, now a beat inside the timeline instead of work the controller did after
        /// the scheduler returned (plan §10.13). Everything here already existed — only its position moved —
        /// except the return-settle wait, which is what makes the turn start with the player on screen rather
        /// than merely on the way there.
        ///
        /// ⚠ This beat sits BETWEEN events, not inside one: it is the one place in this feature where waiting
        /// is allowed to delay what follows, because delaying what follows is the entire point (§10.12 is the
        /// opposite case — a wait added inside a volley serialised it).
        /// </summary>
        IEnumerator ICombatPresentationSink.ActionFocusRelease()
        {
            yield return WaitOutActionFocusDwell();

            // Read before releasing: ReleaseActionFocus clears the flag, and only a framing that was actually
            // up has a return trip to wait for.
            var wasEngaged = actionFocusEngaged;
            ReleaseActionFocus();

            if (wasEngaged)
            {
                yield return WaitForActionFocusReturn();
            }
        }

        /// <summary>
        /// Holds until the rig has settled back on the player, bounded by
        /// <see cref="CombatTimingProfile.FocusReturnMaxSeconds"/>.
        ///
        /// The timeout runs on unscaled time so a hit-stop cannot stretch it, and a rig that cannot report its
        /// position ends the wait immediately — a camera probe must never be able to stall a turn.
        /// </summary>
        private IEnumerator WaitForActionFocusReturn()
        {
            var remaining = ResolveTimingProfile().FocusReturnMaxSeconds;
            if (remaining <= 0f)
            {
                yield break;
            }

            var binder = ResolveCinemachineCombatCameraBinder();
            if (binder == null)
            {
                yield break;
            }

            var startedAt = Time.unscaledTime;
            while (remaining > 0f
                && binder.TryGetFramingSettleFraction(out var offBy)
                && offBy > ActionFocusReturnSettleFraction)
            {
                remaining -= Time.unscaledDeltaTime;
                yield return null;
            }

            var waited = Time.unscaledTime - startedAt;
            DebugActionFocusReturnWaitedSeconds += waited;

            // Same clock as ENGAGE/RELEASE so a trace answers "was the player back on screen when the cards
            // were dealt?" — stills and recordings have already answered that kind of question wrongly three
            // times on this track (§10.11, §10.12).
            CombatPresentationTrace.Record(
                CombatTraceChannel.Beat,
                "ActionFocus RETURNED",
                $"waited={waited:0.###}s timedOut={(remaining <= 0f ? "yes" : "no")}");
        }

        /// <summary>
        /// How close (as a fraction of the framing distance) the rig has to be to the player before the turn is
        /// allowed to start. Not zero: damping approaches asymptotically, so an exact match never arrives.
        /// </summary>
        private const float ActionFocusReturnSettleFraction = 0.02f;

        IEnumerator ICombatPresentationSink.OverallTurnStart()
        {
            State?.CommitDeferredMonsterActionTurnStart();
            // The rules state is between turns here: remaining hand cards have moved to discard,
            // but the next player hand is not drawn yet. Refresh now so the card HUD sees only the
            // discard transition; draw is triggered at PlayerTurnStart below as a separate diff.
            RefreshHudOnly();
            HudHost.AnnounceOverallTurnStartForDock();
            yield return new WaitForSecondsRealtime(1f);
        }

        IEnumerator ICombatPresentationSink.PlayerTurnStart()
        {
            HudHost.AnnouncePlayerTurnStartForDock();
            State?.StartPlayerTurn();
            RefreshHudOnly();
            yield break;
        }

        // 기습(암시야 공격) 경고: 보이지 않는 공격자 대신 플레이어 위치에 "기습!" 플로팅 텍스트를 띄운다.
        // 플레이어 피격 보이스/SFX/데미지 텍스트는 플레이어 대상 버퍼 이펙트 디스패치가 별도로 재생한다.
        private const string AmbushAlertText = "기습!";

        private static readonly Color AmbushAlertColor = new Color(1f, 0.45f, 0.2f, 1f);

        void ICombatPresentationSink.ShowAmbushAlert()
        {
            // 「기습!」 소리는 글자와 같은 비트에 묶인다(타임라인 AmbushAlert 비트가 단일 정본이라 두 번 나지 않는다).
            RequestAudioCue(AudioCueIds.CombatAmbush, "ambush");
            var presentation = ResolveMovementEffectPresentation();
            if (presentation == null || !TryGetPlayerWorldPosition(out var worldPosition))
            {
                return;
            }

            presentation.PlayFloatingText(AmbushAlertText, worldPosition, AmbushAlertColor);
        }

        // 보스 페이즈 전환 비트(P4d). 규칙 측 전환(스탯·아우라 투영·BGM 크로스페이드·스팅어)은 이 비트
        // 이전에 모두 적용을 마쳤으므로(P4a~c) 여기는 순수 표현이다: 보스 프레이밍 → 전환 버스트 VFX +
        // 셰이크 + 카메라 펀치인 → 히트스톱 → 홀드 → 복귀. 연출 중에는 bossPhaseTransitionViewActive가
        // IsCinematicViewActive를 올려 호버/툴팁/게임플레이 오버레이 누수를 막는다.
        [Header("Boss Phase Transition Beat")]
        [Tooltip("보스 좌표로 카메라 포커스를 옮긴 뒤 버스트까지 기다리는 팬 시간. 프레이밍에 실패하면(팬 모드 등) 기다리지 않는다.")]
        [SerializeField] private float bossPhaseTransitionFocusPanSeconds = 0.5f;
        [Tooltip("전환 순간의 카메라 펀치인 배율(1보다 작을수록 강하게 당긴다).")]
        [SerializeField] private float bossPhaseTransitionPunchZoomMultiplier = 0.78f;
        [SerializeField] private float bossPhaseTransitionPunchInSeconds = 0.18f;
        [SerializeField] private float bossPhaseTransitionPunchOutSeconds = 0.35f;
        [Tooltip("버스트가 보이는 채로 페이즈 변화(아우라·크기)를 읽게 하는 홀드.")]
        [SerializeField] private float bossPhaseTransitionHoldSeconds = 0.8f;
        [Tooltip("보스가 새 페이즈 크기로 자라는 데 걸리는 시간. 버스트와 같은 프레임에 시작하므로 홀드보다 짧아야 성장이 폭발 안에서 끝난다.")]
        [SerializeField] private float bossPhaseTransitionScaleGrowSeconds = 0.55f;
        [SerializeField] private float bossPhaseTransitionHitStopSeconds = 0.12f;
        [SerializeField] private float bossPhaseTransitionShakeStrength = 0.55f;
        [SerializeField] private float bossPhaseTransitionShakeDuration = 0.45f;

        // 전환 버스트는 전용 sourceRef 스탬프 + 수제 카탈로그 항목으로 해석된다(계획 §2.9 — EffectKind를
        // 늘리지 않는다). 오디오 스팅어(AudioCueIds.BossPhaseTransitionStinger)와 같은 이름을 공유하지만
        // 서로 다른 카탈로그(SoundCatalog/EffectVfxCatalog)에서 해석되는 별개 항목이다.
        private const string BossPhaseTransitionVfxSourceRef = "boss.phase.transition";
        private const int BossPhaseTransitionShakeVibrato = 10;
        private const float BossPhaseTransitionShakeRandomness = 90f;

        private bool bossPhaseTransitionViewActive;

        IEnumerator ICombatPresentationSink.BossPhaseTransition(string bossUnitId, HexCoord? coord)
        {
            var focusCoord = coord ?? ResolveBossUnitCoord(bossUnitId);
            if (!focusCoord.HasValue || !TryGetTileWorldPosition(focusCoord.Value, out var bossWorld))
            {
                yield break;
            }

            var binder = ResolveCinemachineCombatCameraBinder();
            var focused = false;
            var punched = false;
            Coroutine scaleRoutine = null;
            Coroutine slideRoutine = null;
            // 성장 재배치(§22.5-1 · 2026-09-05 후속 #3): 커진 몸이 결계·철조각을 덮어 규칙이 보스를 가장 가까운 유효
            // 중심으로 옮겼으면, 마커는 아직 옛 칸에 서 있다(이 페이즈 타임라인은 몬스터 이동을 재생하지 않고
            // 끝나서야 스냅한다) — 그래서 「순간이동」으로 읽혔다. 성장 트윈과 <b>같은 시간</b>에 새 칸으로 미끄러진다.
            var slideTarget = ProjectEnemyMarkerLocalPosition(focusCoord.Value);
            var needsSlide = actorMarkerPresenter != null
                && actorMarkerPresenter.TryGetMarkerLocalPosition(bossUnitId, out var slideFrom)
                && (slideFrom - slideTarget).sqrMagnitude > 0.0025f;
            bossPhaseTransitionViewActive = true;
            try
            {
                // 앰비언트 액션 포커스와 달리 게이트(마스터 토글·예산·지오메트리)를 타지 않는다 — 페이즈
                // 전환은 이 비트 자체가 사건이다. 플레이어 팬 입력과 시네마틱 블렌드에만 양보한다.
                if (binder != null && !CameraController.IsCameraBlending)
                {
                    focused = binder.TrySetActionFocus(bossWorld);
                }

                if (focused)
                {
                    var pan = ResolveTimingProfile().ScaleDuration(bossPhaseTransitionFocusPanSeconds);
                    if (pan > 0f)
                    {
                        yield return new WaitForSeconds(pan);
                    }
                }

                PlayBossPhaseTransitionBurst(focusCoord.Value, bossWorld);
                // 크기 성장은 <b>버스트와 같은 프레임에</b> 시작한다(§14.2). 규칙은 이미 새 페이즈지만
                // 화면 배율은 표시값 래치가 붙들고 있었고, 여기서부터 목표치로 자란다 — 성장이 폭발
                // 안에서 읽히는 것이 이 비트의 원래 의도다(아래 hold 주석 참조).
                // yield하지 않고 병렬로 돌린다: 펀치줌·히트스톱·홀드와 겹쳐야 한 사건으로 읽힌다.
                scaleRoutine = StartCoroutine(EaseBossPhaseVisualScale(bossUnitId, bossPhaseTransitionScaleGrowSeconds));
                if (needsSlide)
                {
                    slideRoutine = StartCoroutine(SlideBossMarkerDuringGrowth(bossUnitId, slideTarget, bossPhaseTransitionScaleGrowSeconds));
                }

                CameraController.AddCinemachineShake(
                    bossPhaseTransitionShakeStrength,
                    bossPhaseTransitionShakeDuration,
                    BossPhaseTransitionShakeVibrato,
                    BossPhaseTransitionShakeRandomness);

                if (binder != null)
                {
                    punched = true;
                    yield return EaseBossPhaseTransitionPunch(
                        binder, 1f, bossPhaseTransitionPunchZoomMultiplier, bossPhaseTransitionPunchInSeconds);
                }

                var hitStopSeconds = ResolveTimingProfile().ScaleDuration(bossPhaseTransitionHitStopSeconds);
                if (hitStopSeconds > 0f)
                {
                    yield return HitStop.PlayHitStop(hitStopSeconds, includePlayer: true, bossUnitId);
                }

                var hold = ResolveTimingProfile().ScaleDuration(bossPhaseTransitionHoldSeconds);
                if (hold > 0f)
                {
                    yield return new WaitForSeconds(hold);
                }

                if (punched)
                {
                    yield return EaseBossPhaseTransitionPunch(
                        binder, bossPhaseTransitionPunchZoomMultiplier, 1f, bossPhaseTransitionPunchOutSeconds);
                }
            }
            finally
            {
                // 중단(코루틴 Stop 포함)되어도 카메라·뷰 상태는 반드시 원복된다.
                bossPhaseTransitionViewActive = false;
                if (scaleRoutine != null)
                {
                    // 트윈이 도중에 끊겨도 크기는 저작값에 도달한다 — 어중간한 배율로 굳으면
                    // 그 뒤 모든 프레임이 거짓말을 한다.
                    StopCoroutine(scaleRoutine);
                    SetBossVisualScaleDisplayed(bossUnitId, ResolveAuthoredMonsterVisualScale(bossUnitId));
                }

                if (slideRoutine != null)
                {
                    // 끊겨도 규칙 좌표에 선다 — 어중간한 자리에 남으면 다음 스냅까지 판과 어긋난다.
                    StopCoroutine(slideRoutine);
                    SetEnemyMarkerPresentationPosition(bossUnitId, slideTarget);
                }

                if (punched)
                {
                    binder.ClearTemporaryZoomMultiplier();
                }

                if (focused)
                {
                    binder.ClearActionFocus();
                }
            }
        }

        /// <summary>
        /// 성장 재배치 슬라이드(후속 #3): 마커를 지금 자리에서 규칙 좌표까지 성장 트윈과 같은 길이로 옮긴다.
        /// 걷기 애니메이션은 켜지 않는다 — 걸어간 게 아니라 커지며 밀려난 것이다. unscaled 시간(히트스톱과 겹친다).
        /// </summary>
        private IEnumerator SlideBossMarkerDuringGrowth(string bossUnitId, Vector3 toLocal, float seconds)
        {
            if (actorMarkerPresenter == null || !actorMarkerPresenter.TryGetMarkerLocalPosition(bossUnitId, out var fromLocal))
            {
                yield break;
            }

            seconds = ResolveTimingProfile().ScaleDuration(seconds);
            var elapsed = 0f;
            while (seconds > 0f && elapsed < seconds)
            {
                var t = Mathf.Clamp01(elapsed / seconds);
                var eased = t * t * (3f - 2f * t);
                SetEnemyMarkerPresentationPosition(bossUnitId, Vector3.Lerp(fromLocal, toLocal, eased));
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            SetEnemyMarkerPresentationPosition(bossUnitId, toLocal);
        }

        /// <summary>
        /// 보스 모델 배율을 현재 표시값에서 저작값까지 이징한다(§14.2). 히트스톱(timeScale≈0)과 겹쳐도
        /// 진행해야 하므로 펀치줌과 같은 규약으로 unscaled 시간을 쓴다.
        /// 표시값이 이미 목표와 같으면(= 규칙 무변경 재생, 디버그 "연출만 재생") 아무것도 하지 않는다 —
        /// 크기가 안 바뀐 전환에서 억지 성장 연출을 만들지 않는다.
        /// </summary>
        private IEnumerator EaseBossPhaseVisualScale(string bossUnitId, float seconds)
        {
            var target = ResolveAuthoredMonsterVisualScale(bossUnitId);
            var from = ResolveMonsterVisualScale(bossUnitId);
            seconds = ResolveTimingProfile().ScaleDuration(seconds);
            if (seconds <= 0f || Mathf.Approximately(from, target))
            {
                SetBossVisualScaleDisplayed(bossUnitId, target);
                yield break;
            }

            var elapsed = 0f;
            while (elapsed < seconds)
            {
                var t = Mathf.Clamp01(elapsed / seconds);
                var eased = t * t * (3f - 2f * t);
                SetBossVisualScaleDisplayed(bossUnitId, Mathf.Lerp(from, target, eased));
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            SetBossVisualScaleDisplayed(bossUnitId, target);
        }

        // 펀치인 이징: 히트스톱(timeScale≈0)과 겹쳐도 진행되도록 unscaled 시간으로 돈다.
        private IEnumerator EaseBossPhaseTransitionPunch(
            CinemachineCombatCameraBinder binder, float fromMultiplier, float toMultiplier, float seconds)
        {
            seconds = ResolveTimingProfile().ScaleDuration(seconds);
            var elapsed = 0f;
            while (elapsed < seconds && binder != null)
            {
                var t = Mathf.Clamp01(elapsed / seconds);
                var eased = t * t * (3f - 2f * t);
                binder.SetTemporaryCameraMotion(Mathf.Lerp(fromMultiplier, toMultiplier, eased), 0f, 0f);
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (binder != null)
            {
                binder.SetTemporaryCameraMotion(toMultiplier, 0f, 0f);
            }
        }

        // amount 0 + "field" 타깃은 플로팅 텍스트를 내지 않는다(보상 상자 개봉/승리 폭죽 큐와 같은 규약).
        private void PlayBossPhaseTransitionBurst(HexCoord coord, Vector3 worldPosition)
        {
            var presentation = ResolveMovementEffectPresentation();
            if (presentation == null)
            {
                return;
            }

            var resultEvent = new EffectResultEvent(
                EffectKind.StatusEffectApplied,
                targetUnitId: "field",
                center: coord,
                sourceRef: BossPhaseTransitionVfxSourceRef);
            presentation.Play(resultEvent, worldPosition, Quaternion.identity);
        }

        // 프레이밍 힌트 좌표가 비어 있을 때의 폴백: 보스의 라이브 위치. 전환 비트는 보스가 살아 있는
        // 결의에서만 방출되지만, 표현 계층은 방어적으로 부재를 허용한다(그 경우 연출 생략).
        private HexCoord? ResolveBossUnitCoord(string bossUnitId)
        {
            if (State == null || string.IsNullOrEmpty(bossUnitId))
            {
                return null;
            }

            foreach (var monster in State.Monsters)
            {
                if (string.Equals(monster.Id, bossUnitId, System.StringComparison.Ordinal))
                {
                    return monster.Coord;
                }
            }

            return null;
        }

        // Per-cue playback delay authored on the resolved VFX catalog entry (0 when none). Used to keep
        // reactions and camera shake in lockstep with the cue's own delayed VFX/floating number.
        public float ResolveEffectVfxDelaySeconds(EffectResultEvent effect)
        {
            return AudioHost.ResolveEffectVfxDelaySeconds(effect);
        }

        // After the scheduler has dispatched the effects it scheduled, flush any buffered effects it did not
        // (defensive: nothing should be left), then end the buffered-dispatch window.
        private void FinishBufferedEffectReplay(IReadOnlyList<MonsterActionResolutionRecord> monsterActionRecords = null)
        {
            if (State == null)
            {
                return;
            }

            var bufferedEffects = State.BufferedEffects;
            for (var i = 0; i < bufferedEffects.Count; i++)
            {
                if (!dispatchedEffectIndices.Contains(i))
                {
                    if (MonsterActionPresentationFilters.ShouldSuppressUndispatchedMonsterEffect(bufferedEffects[i], monsterActionRecords))
                    {
                        continue;
                    }

                    State.DispatchBufferedEffectAt(i);
                }
            }

            dispatchedEffectIndices.Clear();
            State.EndBufferedEffectDispatch();
        }

        private static HashSet<string> CollectDiedUnitIds(CombatPresentationSnapshot before, CombatPresentationSnapshot after)
        {
            var died = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var monster in before.Monsters)
            {
                if (monster.Id == null || monster.IsDead)
                {
                    continue;
                }

                var post = after.FindMonster(monster.Id);
                if (post.Id != monster.Id || post.IsDead)
                {
                    died.Add(monster.Id);
                }
            }

            return died;
        }

        private IEnumerator WaitForMonsterDeathAnimations(HashSet<string> diedUnitIds)
        {
            if (diedUnitIds == null || diedUnitIds.Count == 0)
            {
                yield break;
            }

            var profile = ResolveTimingProfile();
            var fallback = profile != null ? profile.ScaleDuration(EffectiveDeathDelay) : EffectiveDeathDelay;
            foreach (var diedUnitId in diedUnitIds)
            {
                if (!string.IsNullOrEmpty(diedUnitId) && !string.Equals(diedUnitId, "player", System.StringComparison.Ordinal))
                {
                    yield return actorMarkerPresenter.WaitForDead(diedUnitId, fallback, 4f);
                }
            }
        }

        private IEnumerator RunMoveTimeline(CombatPresentationSnapshot before, CombatPresentationSnapshot after, string finalMessage)
        {
            SetPresentationAnchors(before);
            EnterPresentationPhase(CombatPresentationPhase.PlayerMoving, "Moving player...");
            var timeline = new CombatTimeline();
            var movePath = State != null ? State.LastPlayerMovePath : null;
            HexCoord moveStepEnd;
            if ((movePath == null || movePath.Count < 2) && before.PlayerCoord != after.PlayerCoord)
            {
                timeline.Append(CombatTimelineEventKind.PlayerMoveStep, "player", before.PlayerCoord, after.PlayerCoord);
                moveStepEnd = after.PlayerCoord;
            }
            else if (movePath != null && movePath.Count >= 2)
            {
                timeline.AppendMovePath(CombatTimelineEventKind.PlayerMoveStep, "player", movePath);
                moveStepEnd = movePath[movePath.Count - 1];
            }
            else
            {
                moveStepEnd = before.PlayerCoord;
            }

            // 이 이동에서 보스 아레나 결계가 닫혔는가(before/after 봉인 id 비교). 봉인 시 전투 시작 좌표로의
            // 이동은 순간이동이라(조우 연출이 암전 뒤 마커를 스냅한다) 아래 넉백 슬라이드로 그리지 않는다.
            var sealedBossArenaThisMove = !string.IsNullOrEmpty(after.SealedBossArenaId)
                && before.SealedBossArenaId != after.SealedBossArenaId;

            // 암시야 몬스터 칸으로 이동해 충돌 넉백이 일어난 경우: 걸어간 목적지(충돌 칸)에서 실제 도착 칸으로 밀려나는
            // 슬라이드를 표현한다. 이전에는 마지막에 최종 좌표로 스냅(텔레포트)됐다.
            if (moveStepEnd != after.PlayerCoord && !sealedBossArenaThisMove)
            {
                timeline.Append(CombatTimelineEventKind.PlayerKnockbackStep, "player", moveStepEnd, after.PlayerCoord);
            }

            foreach (var monster in before.Monsters.Where(m => !m.IsDead))
            {
                var post = after.FindMonster(monster.Id);
                if (monster.Id != null && post.Id == monster.Id && !post.IsDead && monster.Coord != post.Coord)
                {
                    timeline.Append(CombatTimelineEventKind.EnemyMoveStep, monster.Id, monster.Coord, post.Coord);
                }
            }

            yield return PlayTracedTimeline(timeline, "move");

            // 결계가 이번 이동에서 닫혔으면 조우(입장) 연출을 재생한다: 암전 → 마커를 전투 시작 좌표로 스냅 →
            // 홀드 → 페이드 인. 규칙 상태는 이미 확정돼 있으므로 순수 표현이다(§8-9 P5·계획 §12).
            if (sealedBossArenaThisMove)
            {
                yield return PlayBossArenaEncounterCinematic(after);
            }

            CompletePresentationSequence(finalMessage);
        }

        private IEnumerator RunMoveTimelineThenResolveTileInteraction(CombatPresentationSnapshot before, CombatPresentationSnapshot after, string finalMessage)
        {
            yield return RunMoveTimeline(before, after, finalMessage);

            if (State == null || State.IsTerminal)
            {
                yield break;
            }

            ResolveTileInteractionAtPlayerCoord();
        }

        // Single monster->player attack presentation for the timing lab (DebugReplayMonsterAttack).
        // 2-A: 다른 타임라인 곁으로 옮겼다 — 디버그 코드가 아니라 연출 타임라인이다. 촬영용 임팩트 지연 보너스는 인자로 받는다. Mirrors the monster-attack beat in
        // ResolveEndActionSequence (facing, wind-up, attack trigger, impact flush, player hit/death, hit stop)
        // and applies the same per-channel impact offsets as the player path.
        private IEnumerator RunMonsterAttackReplayTimeline(
            CombatPresentationSnapshot before,
            CombatPresentationSnapshot after,
            string attackerId,
            HexCoord attackerCoord,
            string animationTrigger,
            bool playerDied,
            string finalMessage,
            string attackId,
            float impactDelayBonus)
        {
            var capturedGeneration = stateGeneration;
            var capturedVersion = presentationSequenceVersion;
            // Per-attack timing keyed by the monster's patternId (equals global when no override row).
            var timing = ResolveAttackTiming(attackId);
            SetPresentationAnchors(before);
            EnterPresentationPhase(CombatPresentationPhase.MonsterAttacking, "Resolving monster attack...");
            ApplyMonsterHexFacing(attackerId, attackerCoord, before.PlayerCoord);

            var monsterWindupDelay = timing.WindupDelay;
            if (monsterWindupDelay > 0f)
            {
                yield return new WaitForSeconds(monsterWindupDelay);
            }

            actorMarkerPresenter.TriggerAttack(attackerId, animationTrigger);

            var impactSynced = timingProfile != null
                && timingProfile.AlignImpactToAnimation
                && State != null
                && State.IsBufferingEffects;
            var impactChannelBase = impactSynced ? timing.MinChannelOffset : 0f;

            // The trailer runner stretches the swing-to-impact gap: at the authored timing the player's
            // death reaction can land before the attack animation reads on film, which looks like dying
            // to nothing. Zero outside a filming take.
            var monsterImpactDelay = timing.ImpactDelay + impactChannelBase + impactDelayBonus;
            if (monsterImpactDelay > 0f)
            {
                yield return new WaitForSeconds(monsterImpactDelay);
            }

            if (!IsCurrentPresentationToken(capturedGeneration, capturedVersion))
            {
                yield break;
            }

            if (!playerDied)
            {
                atlasTilePresentationView?.TriggerPlayerHit();
            }

            activeImpactTiming = timing;
            State?.FlushBufferedEffects();
            activeImpactTiming = null;

            var hitStopSeconds = timing.ResolveMonsterHitStop(playerDied);
            if (hitStopSeconds > 0f)
            {
                var hitStopImpactDelay = impactSynced ? timing.HitStopImpactDelay : 0f;
                if (hitStopImpactDelay > 0f)
                {
                    yield return new WaitForSeconds(hitStopImpactDelay);
                }

                yield return HitStop.PlayHitStop(hitStopSeconds, true, attackerId);
            }

            if (!IsCurrentPresentationToken(capturedGeneration, capturedVersion))
            {
                yield break;
            }

            if (playerDied)
            {
                EnterPresentationPhase(CombatPresentationPhase.Completing, "Player defeated...");
                RecordPlayerDeathSource(attackerId);
                BeginPlayerDeathSequence();
                yield return HoldPlayerDeathSequence(timing.DeathDelay);
            }

            CompletePresentationSequence(finalMessage);
        }

        private IEnumerator RunAttackTimeline(CombatPresentationSnapshot before, CombatPresentationSnapshot after, HexCoord target, string targetMonsterId, string finalMessage, string attackId)
        {
            SetPresentationAnchors(before);
            EnterPresentationPhase(CombatPresentationPhase.PlayerAttacking, "Resolving player attack...");
            var targetBeforeSnapshot = before.FindMonster(targetMonsterId);
            var targetAfterSnapshot = after.FindMonster(targetMonsterId);
            var hasTarget = targetBeforeSnapshot.Id != null && targetAfterSnapshot.Id == targetMonsterId;
            var targetDied = hasTarget && !targetBeforeSnapshot.IsDead && targetAfterSnapshot.IsDead;
            var targetKnockedBack = hasTarget && !targetBeforeSnapshot.IsDead && !targetAfterSnapshot.IsDead && targetBeforeSnapshot.Coord != targetAfterSnapshot.Coord;
            var targetHit = hasTarget && !targetDied && !targetKnockedBack && targetAfterSnapshot.Hp < targetBeforeSnapshot.Hp;
            var targetBefore = hasTarget ? targetBeforeSnapshot.Coord : target;
            var targetAfter = hasTarget ? targetAfterSnapshot.Coord : target;
            dispatchedEffectIndices.Clear();
            var diedUnitIds = CollectDiedUnitIds(before, after);
            var timeline = CombatTimelineAssembler.BuildPlayerAttack(string.Empty, attackId, before.PlayerCoord, targetMonsterId, targetBefore, targetAfter, targetHit, targetKnockedBack, targetDied, State != null ? State.BufferedEffects : null, diedUnitIds);
            yield return PlayTracedTimeline(timeline, $"player-attack {attackId}");
            FinishBufferedEffectReplay();
            RequestMonsterDeathAudioCues(diedUnitIds, before, "player-attack");
            yield return WaitForMonsterDeathAnimations(diedUnitIds);
            CompletePresentationSequence(finalMessage);
        }

        private IEnumerator RunBurstTimeline(CombatPresentationSnapshot before, CombatPresentationSnapshot after, string finalMessage)
        {
            SetPresentationAnchors(before);
            EnterPresentationPhase(CombatPresentationPhase.PlayerAttacking, "Resolving effect...");
            dispatchedEffectIndices.Clear();
            var diedUnitIds = CollectDiedUnitIds(before, after);
            var timeline = CombatTimelineAssembler.BuildEffectBurst(State != null ? State.BufferedEffects : null, diedUnitIds);
            yield return PlayTracedTimeline(timeline, "effect-burst");
            FinishBufferedEffectReplay();
            RequestMonsterDeathAudioCues(diedUnitIds, before, "effect-burst");
            yield return WaitForMonsterDeathAnimations(diedUnitIds);
            CompletePresentationSequence(finalMessage);
        }

        private IEnumerator RunEndActionTimeline(CombatPresentationSnapshot before, CombatPresentationSnapshot after, IReadOnlyList<MonsterActionResolutionRecord> monsterActionRecords, bool playerDied, string finalMessage)
        {
            SetPresentationAnchors(before);
            EnterPresentationPhase(CombatPresentationPhase.MonstersMoving, "Monsters are acting...");
            dispatchedEffectIndices.Clear();
            if (enemyTurnBeginPresentationDelay > 0f)
            {
                yield return new WaitForSeconds(enemyTurnBeginPresentationDelay);
            }

            if (playerDied)
            {
                RecordPlayerDeathSource(monsterActionRecords);
            }

            var diedUnitIds = CollectDiedUnitIds(before, after);
            var timeline = CombatTimelineAssembler.BuildEndAction(
                monsterActionRecords,
                before.PlayerCoord != after.PlayerCoord,
                before.PlayerCoord,
                after.PlayerCoord,
                playerDied,
                State != null ? State.BufferedEffects : null,
                diedUnitIds,
                includeMonsterMovement: false,
                emitActionSpotlights: enableActionCameraFocus,
                bossPhaseTransitions: State != null ? State.LastBossPhaseTransitions : null,
                bossPropAbsorptions: State != null ? State.LastBossPropAbsorptions : null,
                bossPropVolleyCasts: State != null ? State.LastBossPropVolleyCasts : null,
                bossScrapChainHits: State != null ? State.LastBossScrapChainHits : null);
            yield return PlayTracedTimeline(timeline, "end-action");
            State?.FinishDeferredMonsterActionState();
            FinishBufferedEffectReplay(monsterActionRecords);
            RequestMonsterDeathAudioCues(diedUnitIds, before, "end-action");
            yield return WaitForMonsterDeathAnimations(diedUnitIds);
            yield return WaitForMonsterPhaseCompleteHold();
            CompletePresentationSequence(finalMessage);
        }

        private IEnumerator RunMonsterMovementPhaseTimeline(
            CombatPresentationSnapshot before,
            CombatPresentationSnapshot after,
            IReadOnlyList<MonsterActionResolutionRecord> monsterMovementRecords,
            string finalMessage)
        {
            SetPresentationAnchors(before);
            EnterPresentationPhase(CombatPresentationPhase.MonstersMoving, "Monsters are moving...");
            dispatchedEffectIndices.Clear();
            var playerKnockedBack = before.PlayerCoord != after.PlayerCoord;
            var timeline = CombatTimelineAssembler.BuildMonsterMovementPhase(
                monsterMovementRecords,
                playerKnockedBack,
                before.PlayerCoord,
                after.PlayerCoord,
                emitActionSpotlights: enableActionCameraFocus);
            yield return PlayTracedTimeline(timeline, "monster-movement");
            yield return WaitForMonsterPhaseCompleteHold();
            CompletePresentationSequence(finalMessage);
        }

        [Header("Presentation Trace (dev)")]
        [Tooltip("Records when each presentation channel (VFX / SFX / shake / reaction) actually lands during " +
            "a combat sequence and logs the result. Editor and development builds only; off by default.")]
        [SerializeField] private bool tracePresentationTimeline;

        /// <summary>The most recent completed trace, so a dev panel can show it without re-reading the console.</summary>
        public CombatPresentationTraceLog LastPresentationTrace { get; private set; }

        /// <summary>True when timeline tracing is armed. Ignored outside the editor / development builds.</summary>
        public bool TracePresentationTimeline
        {
            get => tracePresentationTimeline;
            set => tracePresentationTimeline = value;
        }

        /// <summary>
        /// Runs a timeline through the scheduler, wrapped in a presentation trace when tracing is armed.
        /// The clock is unscaled realtime on purpose: hit-stop drops <see cref="Time.timeScale"/>, so scaled
        /// time would compress exactly the moments the trace exists to measure.
        /// </summary>
        /// <summary>
        /// Fills each spotlight's covered presentation length from the measured duration data
        /// (docs/presentation-duration-data-plan.md P4). Done here rather than in the assembler because
        /// resolving a cue needs the Unity VFX catalog, which the pure assembler cannot reach.
        ///
        /// Skipped entirely when action focus is off: with the master toggle off no spotlight beats exist, so
        /// the walk would be pure cost. This is also what keeps the shipping configuration byte-identical.
        /// </summary>
        private void AnnotateSpotlightDurations(CombatTimeline timeline)
        {
            if (timeline == null || !enableActionCameraFocus || State == null)
            {
                return;
            }

            var buffered = State.BufferedEffects;
            timeline.AnnotateActorSpotlightSeconds(index =>
                index >= 0 && index < buffered.Count
                    ? AudioHost.ResolveEffectPresentationSeconds(buffered[index])
                    : 0f);

            // Second pass, same resolver the trace tail and the reaction delays already use: how long after
            // dispatch the cue actually shows anything. Without it a framing's hold is measured against an
            // event that has not started (plan §10.11).
            timeline.AnnotateActorSpotlightLeadIn(index =>
                index >= 0 && index < buffered.Count
                    ? ResolveEffectVfxDelaySeconds(buffered[index])
                    : 0f);
        }

        private IEnumerator PlayTracedTimeline(CombatTimeline timeline, string label)
        {
            var tracing = tracePresentationTimeline && CombatDebugControlPanel.DebugUiAvailable;
            if (tracing)
            {
                CombatPresentationTrace.Begin(label, () => Time.realtimeSinceStartup);
            }

            // The serialized checkbox is the single source of truth for whether metrics are armed, so a value
            // set in the inspector (or a panel toggle, which writes the same field) takes effect without a
            // separate wiring step.
            SyncActionFocusMetricsArmed();

            // Set unconditionally (not only when tracing): the action-focus metrics buckets its samples by
            // this label, and arming the trace as well would perturb the timings that pass is measuring.
            var previousPresentationLabel = activePresentationLabel;
            activePresentationLabel = label ?? string.Empty;

            // Budget and framing are scoped to one replayed timeline, which is one phase's worth of beats.
            actionFocusUsedThisPhase = 0;
            actionFocusFramedCoord = null;
            actionFocusHoldUntilRealtime = 0f;

            AnnotateSpotlightDurations(timeline);

            // The dwell has to survive the last beat too: the final framing of a phase has no following
            // spotlight to wait it out, and handing the camera back on the beat the last cue fired is the
            // same early cut, just at the end (plan §10.6).
            //
            // A timeline that ends the player's turn does this cleanup from inside the scheduler instead, as an
            // ActionFocusRelease beat placed before PlayerTurnStart (plan §10.13) — the turn must not start
            // with the camera still out. What is left here is the catch-all for every other timeline (move,
            // player attack, effect burst, monster movement), which has no turn boundary to hang it on. Both
            // paths call the same two methods and both are idempotent, so the beat running first simply makes
            // this pair a no-op.
            var focusDwellSubscribed = SubscribeActionFocusDwellSignals();
            try
            {
                yield return presentationScheduler.Play(timeline, ResolveTimingProfile(), this);

                yield return WaitOutActionFocusDwell();
            }
            finally
            {
                if (focusDwellSubscribed)
                {
                    EffectPresentationController.FloatingTextPresented -= OnFocusDwellFloatingTextPresented;
                }
            }

            ReleaseActionFocus();
            activePresentationLabel = previousPresentationLabel;

            if (!tracing)
            {
                yield break;
            }

            // A cue's authored delay can outlast the timeline that released it — a scout's reveal VFX waits
            // 0.7s while its whole burst timeline finishes in 4ms — so closing the recording the instant the
            // scheduler returns would drop exactly the late channel a designer is looking for. Hold the window
            // open for the longest delay this sequence could still be waiting on.
            //
            // This does hold the sequence for that extra beat, so a traced run is slightly slower than an
            // untraced one. Accepted: tracing is an explicit dev opt-in, and blocking keeps one recording from
            // overlapping the next (the recorder holds a single active recording).
            var tail = ResolveTraceTailSeconds();
            if (tail > 0f)
            {
                yield return new WaitForSecondsRealtime(tail);
            }

            var log = CombatPresentationTrace.End();
            if (log != null && log.Entries.Count > 0)
            {
                LastPresentationTrace = log;
                Debug.Log(log.Format());
            }
        }

        /// <summary>
        /// How long to keep a trace recording open after the scheduler finishes: the largest authored cue
        /// delay among the effects this sequence dispatched, so late channels are recorded rather than cut off.
        /// Zero when nothing was delayed, which keeps the common case free of an extra wait.
        /// </summary>
        private float ResolveTraceTailSeconds()
        {
            if (State == null)
            {
                return 0f;
            }

            var longest = 0f;
            var buffered = State.BufferedEffects;
            for (var i = 0; i < buffered.Count; i++)
            {
                longest = Mathf.Max(longest, ResolveEffectVfxDelaySeconds(buffered[i]));
            }

            // Small margin so a channel landing exactly on its delay is inside the window, not on its edge.
            return longest > 0f ? longest + 0.05f : 0f;
        }

        [Header("Action Camera Focus (docs/monster-action-camera-focus-plan.md)")]
        [Tooltip("Master switch, ON since the P4 play check (plan §8 P4). With it off no ActorSpotlight beats " +
            "are emitted and FocusOnAction never moves the camera, which is the pre-feature presentation — " +
            "still the fastest way to A/B the whole feature or to rule it out of a bug.\n\n" +
            "⚠ The effective value is this C# initializer: no shipping scene serializes this field (only " +
            "CameraLab does, and it pins ON), so changing it here changes every scene at once. A scene that " +
            "gets saved after touching it starts overriding this default.")]
        [SerializeField] private bool enableActionCameraFocus = true;

        /// <summary>Camera moves already spent in the timeline currently replaying (the per-phase budget).</summary>
        private int actionFocusUsedThisPhase;

        /// <summary>True while an action focus is held, so the phase end knows to hand the camera back.</summary>
        private bool actionFocusEngaged;

        /// <summary>
        /// The tile the camera is really framing, or null when it is on the player. This — not the last
        /// spotlight the assembler emitted — is what coalescing has to compare against; see
        /// <see cref="CombatTimelineAssembler"/>'s emitter note.
        /// </summary>
        private HexCoord? actionFocusFramedCoord;

        /// <summary>
        /// Unscaled-time deadline the current framing must be held to before another spotlight may take the
        /// camera. It is a deadline rather than a duration precisely so that the covered beats count toward
        /// it: a field volley that already played for 2s owes nothing, while a cue that fired and left its VFX
        /// running holds the camera for the remainder.
        /// </summary>
        private float actionFocusHoldUntilRealtime;

        public bool EnableActionCameraFocus
        {
            get => enableActionCameraFocus;
            set => enableActionCameraFocus = value;
        }

        IEnumerator ICombatPresentationSink.FocusOnAction(
            HexCoord coord,
            string actorId,
            bool fallbackGap,
            int coveredBeats,
            float coveredSeconds,
            float coveredLeadInSeconds)
        {
            var timing = ResolveTimingProfile();

            // Coalescing is decided BEFORE the dwell wait, and that ordering is the whole point.
            //
            // A coalesced spotlight is not a separate event waiting its turn — it is the SAME event: one field
            // ticking three monsters raises one spotlight per tile, and those beats have to fire together to
            // read as one volley (plan §10.7.2: "한 필드의 3연타는 한 사건"). Making them wait out the
            // framing's dwell first serialised the volley into one monster every 3s, which is a change to the
            // EVENT's timing, not the camera's — a regression well outside this feature's remit (§10.12).
            if (WouldCoalesceIntoCurrentFraming(coord))
            {
                JournalActionFocusDecision(coord, "coalesced");

                // Raise only the visibility floor: "do not release the framing before this hit is on screen".
                // Deliberately NOT the content dwell — the cluster's length is already charged to the engage
                // that opened this framing, and charging it again per member is what re-armed 3s a time.
                ExtendActionFocusDwell(
                    timing.ResolveFocusDwellSeconds(0f, coveredLeadInSeconds), coveredLeadInSeconds);

                if (fallbackGap)
                {
                    yield return ((ICombatPresentationSink)this).MonsterActionGap();
                }

                yield break;
            }

            // Anything that could move the camera waits out the previous framing's dwell first, so the event
            // that framing existed to show is still on screen when it finishes (plan §10.6). Ordered before
            // the gate because the gate's own answer depends on where the camera is pointing right now.
            yield return WaitOutActionFocusDwell();

            if (TryBeginActionFocus(coord))
            {
                // The camera move IS the breathing beat (§3.2) — no gap on top of it, which is what keeps the
                // enemy turn from growing by a gap per focused event.
                //
                // The pan is a constant arrival lead, NOT scaled by the event's length: the beats it covers
                // play *after* it, so paying the length here bought an empty frame to stare at and still let
                // the camera leave mid-event. The length is charged on the far side instead, as the dwell.
                DebugActionFocusLastCoveredSeconds = coveredSeconds;
                var pan = timing.FocusPanSeconds;
                if (pan > 0f)
                {
                    yield return new WaitForSeconds(pan);
                }

                // Start the dwell clock once the camera has arrived, not when the beat began — the pan is not
                // time the player spent looking at the event.
                BeginActionFocusDwell(
                    timing.ResolveFocusDwellSeconds(coveredSeconds, coveredLeadInSeconds), coveredLeadInSeconds);
                yield break;
            }

            // The gate declined for some other reason (already framed, budget, pan mode). The camera does not
            // move, but the event still plays inside whatever framing is up, so raise that framing's floor to
            // the point this hit becomes visible — otherwise the next spotlight yanks the camera away while
            // this cue is still inside its lead-in (plan §10.11).
            //
            // Visibility floor only, never the content dwell: re-arming a full dwell per declined beat is what
            // pushed a field volley's members three seconds apart (§10.12).
            if (actionFocusEngaged)
            {
                ExtendActionFocusDwell(
                    timing.ResolveFocusDwellSeconds(0f, coveredLeadInSeconds), coveredLeadInSeconds);
            }

            // Nothing moved. Restore the pause this beat displaced — and only that one: a spotlight in a slot
            // that never had a gap (first monster, movement phase, turn-boundary effects) must add nothing,
            // or every already-framed action would silently get slower than it is today.
            if (fallbackGap)
            {
                yield return ((ICombatPresentationSink)this).MonsterActionGap();
            }
        }

        /// <summary>
        /// Subscribes the dwell to floating-text presentation for the length of one timeline. Skipped when the
        /// feature is off so the shipping path carries no extra handler on a per-effect event.
        /// </summary>
        private bool SubscribeActionFocusDwellSignals()
        {
            if (!enableActionCameraFocus)
            {
                return false;
            }

            EffectPresentationController.FloatingTextPresented += OnFocusDwellFloatingTextPresented;
            return true;
        }

        /// <summary>
        /// Dev counters for the dwell, readable from the camera lab. The dwell is invisible in a recording —
        /// a camera that lingers looks the same as a camera that had nothing left to show — so without these
        /// the only way to tell "the hold worked" from "the hold never ran" is to guess from clip length.
        /// </summary>
        public int DebugActionFocusEngageCount { get; private set; }
        public int DebugActionFocusTextExtensionCount { get; private set; }
        public float DebugActionFocusDwellWaitedSeconds { get; private set; }
        public float DebugActionFocusLastCoveredSeconds { get; private set; }

        /// <summary>
        /// Time spent waiting for the camera to settle back on the player before the turn started (plan
        /// §10.13). Separate from the dwell: the dwell is time spent looking AT the event, this is the trip home.
        /// </summary>
        public float DebugActionFocusReturnWaitedSeconds { get; private set; }

        public void DebugResetActionFocusDwellCounters()
        {
            DebugActionFocusEngageCount = 0;
            DebugActionFocusTextExtensionCount = 0;
            DebugActionFocusDwellWaitedSeconds = 0f;
            DebugActionFocusLastCoveredSeconds = 0f;
            DebugActionFocusReturnWaitedSeconds = 0f;
            actionFocusJournal.Clear();
        }

        private void BeginActionFocusDwell(float seconds, float leadInSeconds)
        {
            DebugActionFocusEngageCount++;
            actionFocusDwellStartedRealtime = Time.unscaledTime;
            actionFocusHoldUntilRealtime = actionFocusDwellStartedRealtime + Mathf.Max(0f, seconds);
            actionFocusLeadInSeconds = Mathf.Max(0f, leadInSeconds);

            // Traced on the same clock as Dispatch/Vfx/Text so a recording answers the one question stills
            // cannot: was the camera still here when the explosion actually spawned?
            CombatPresentationTrace.Record(
                CombatTraceChannel.Beat,
                "ActionFocus ENGAGE",
                $"dwell={Mathf.Max(0f, seconds):0.###}s leadIn={actionFocusLeadInSeconds:0.###}s");
        }

        /// <summary>
        /// Pushes the current framing's deadline out for an event that rides it without moving the camera.
        /// Only ever extends — a short event arriving after a long one must not cut the long one short.
        /// </summary>
        private void ExtendActionFocusDwell(float seconds, float leadInSeconds)
        {
            var deadline = Time.unscaledTime + Mathf.Max(0f, seconds);
            if (deadline <= actionFocusHoldUntilRealtime)
            {
                return;
            }

            actionFocusHoldUntilRealtime = deadline;
            actionFocusLeadInSeconds = Mathf.Max(actionFocusLeadInSeconds, Mathf.Max(0f, leadInSeconds));
            CombatPresentationTrace.Record(
                CombatTraceChannel.Beat,
                "ActionFocus EXTEND",
                $"+{Mathf.Max(0f, seconds):0.###}s leadIn={Mathf.Max(0f, leadInSeconds):0.###}s");
        }

        /// <summary>
        /// The lead-in of the framing currently held, so the text-driven extension's ceiling can allow for a
        /// cue that has not started yet. Without this the ceiling is measured from the camera's arrival and
        /// clamps away exactly the late signal it exists to honour.
        /// </summary>
        private float actionFocusLeadInSeconds;

        /// <summary>
        /// Extends the current framing's dwell so it outlives the last floating text it showed.
        ///
        /// A number appearing is where reading *starts*, so the hold runs from the last text, not to it — and
        /// because compound effects raise several texts, only the last one can be known in advance by waiting
        /// for each of them in turn. Bounded by the same ceiling as the measured length so a status stack
        /// cannot park the camera indefinitely.
        /// </summary>
        private void OnFocusDwellFloatingTextPresented(EffectResultEvent effect)
        {
            if (!actionFocusEngaged)
            {
                return;
            }

            var timing = ResolveTimingProfile();
            var extended = Time.unscaledTime + timing.FocusDwellTextReadSeconds;

            // The ceiling budgets how long the camera may linger ON THE EVENT, so it starts when the event
            // can first be seen — not when the camera arrived. Anchoring it to arrival made a cue with a
            // lead-in unreachable: the field cue's 1.1s delay ate a third of a 3s ceiling before its first
            // number existed, and the text that was supposed to extend the hold always landed outside it.
            var ceiling = actionFocusDwellStartedRealtime + actionFocusLeadInSeconds + timing.FocusDwellMaxSeconds;
            var next = Mathf.Max(actionFocusHoldUntilRealtime, Mathf.Min(extended, ceiling));
            if (next > actionFocusHoldUntilRealtime)
            {
                DebugActionFocusTextExtensionCount++;
                actionFocusHoldUntilRealtime = next;
            }
        }

        /// <summary>When the current framing began, so the text-driven extension has a ceiling to clamp to.</summary>
        private float actionFocusDwellStartedRealtime;

        private IEnumerator WaitOutActionFocusDwell()
        {
            var startedAt = Time.unscaledTime;
            while (actionFocusEngaged && Time.unscaledTime < actionFocusHoldUntilRealtime)
            {
                yield return null;
            }

            DebugActionFocusDwellWaitedSeconds += Time.unscaledTime - startedAt;
        }

        [Tooltip("Leans the framing toward a monster that is attacking the player, so a melee blow reads as an " +
            "event rather than something happening beside the player. Separate from the action focus, and it " +
            "has to be: a melee attacker is already on screen, so the geometry gate correctly declines to MOVE " +
            "for it — this only tilts the framing it is already in.\n\n" +
            "ON since 2026-07-31 (plan §10.16). It shipped OFF while the action focus was being proven, which " +
            "made the lab and the real game disagree about adjacent attacks for a while — the lab scene " +
            "serializes this ON, every shipping scene falls through to this initializer.")]
        [SerializeField] private bool enableMeleeAttackEmphasis = true;

        /// <summary>
        /// Guards against a stale release: each attack replaces the lean and schedules its own release, so
        /// without a token the first attack's timer would drop the second attack's lean early.
        /// </summary>
        private int meleeAttackEmphasisToken;

        public bool EnableMeleeAttackEmphasis
        {
            get => enableMeleeAttackEmphasis;
            set => enableMeleeAttackEmphasis = value;
        }

        /// <summary>
        /// Leans the camera toward an attacking monster for the emphasis hold. Not a relocation: the binder
        /// blends between the player and the attacker, and refuses outright if an action focus already owns
        /// the camera or the player is panning.
        /// </summary>
        private void BeginMeleeAttackEmphasis(HexCoord attackerCoord)
        {
            if (!enableMeleeAttackEmphasis
                || !isActiveAndEnabled
                || !TryGetTileWorldPosition(attackerCoord, out var world))
            {
                return;
            }

            var binder = ResolveCinemachineCombatCameraBinder();
            if (binder == null || !binder.TrySetAttackEmphasis(world))
            {
                return;
            }

            StartCoroutine(ReleaseMeleeAttackEmphasisAfterHold(++meleeAttackEmphasisToken));
        }

        private IEnumerator ReleaseMeleeAttackEmphasisAfterHold(int token)
        {
            var hold = ResolveTimingProfile().AttackEmphasisSeconds;
            if (hold > 0f)
            {
                yield return new WaitForSeconds(hold);
            }

            // A newer attack has taken the lean; that one owns the release.
            if (token != meleeAttackEmphasisToken)
            {
                yield break;
            }

            ResolveCinemachineCombatCameraBinder()?.ClearAttackEmphasis();
        }

        /// <summary>
        /// The gate: geometry first, then budget. Returns false — meaning "no camera move" — for an action
        /// that is already on screen, which is the zero-regression case the whole design is built around.
        /// </summary>
        private bool TryBeginActionFocus(HexCoord coord)
        {
            var timing = ResolveTimingProfile();
            if (!enableActionCameraFocus)
            {
                return false;
            }

            // Budget before geometry, and journalled separately from every other decline: an engage count that
            // stops exactly at the ceiling looks identical to one that ran out of off-screen events, and that
            // ambiguity is what let a saturated budget read as a working feature (plan §10.9).
            if (actionFocusUsedThisPhase >= timing.MaxFocusPerPhase)
            {
                JournalActionFocusDecision(coord, "budget");
                return false;
            }

            if (!TryGetTileWorldPosition(coord, out var world))
            {
                JournalActionFocusDecision(coord, "no-world");
                return false;
            }

            // Coalescing is handled by the caller before it waits out the dwell (see FocusOnAction), so by the
            // time we get here the point is known not to ride the current framing. Asserted rather than
            // re-tested: two copies of this predicate could drift, and the caller's copy is the one that also
            // decides whether the beat waits.
            if (WouldCoalesceIntoCurrentFraming(coord))
            {
                return false;
            }

            // Already framed → the player can see it happen, so moving the camera would be pure motion sickness.
            if (IsPointFramed(CameraFrame.FromCamera(prototype3DCamera), world, ResolveFrustumMarginFraction()))
            {
                JournalActionFocusDecision(coord, "framed");
                return false;
            }

            // A cinematic (stage intro, victory, death zoom) owns the camera while it blends; those play
            // outside the monster-phase timelines that emit spotlights, so this is a belt-and-braces check.
            if (CameraController.IsCameraBlending)
            {
                JournalActionFocusDecision(coord, "blending");
                return false;
            }

            // The binder refuses while the player is free-panning with Y — that input outranks ambient framing.
            var binder = ResolveCinemachineCombatCameraBinder();
            if (binder == null || !binder.TrySetActionFocus(world))
            {
                JournalActionFocusDecision(coord, binder == null ? "no-binder" : "pan-mode");
                return false;
            }

            actionFocusUsedThisPhase++;
            actionFocusEngaged = true;
            actionFocusFramedCoord = coord;
            JournalActionFocusDecision(coord, "ENGAGED");
            return true;
        }

        private const int MaxActionFocusJournalEntries = 128;

        private readonly List<string> actionFocusJournal = new List<string>();

        /// <summary>
        /// Per-spotlight verdict log for the camera lab: which tile was offered, and why the camera did or did
        /// not go there. <see cref="DebugActionFocusEngageCount"/> answers "how many cuts", which cannot
        /// distinguish "nothing else was off screen" from "the budget ran out before the events that matter" —
        /// and field ticks are appended last, so they are exactly what a first-come budget drops (plan §10.9).
        /// Only written while the feature is on, so the shipping path allocates nothing.
        /// </summary>
        public IReadOnlyList<string> DebugActionFocusJournal => actionFocusJournal;

        private void JournalActionFocusDecision(HexCoord coord, string verdict)
        {
            if (actionFocusJournal.Count >= MaxActionFocusJournalEntries)
            {
                return;
            }

            var distance = State != null ? State.PlayerCoord.DistanceTo(coord).ToString() : "?";
            actionFocusJournal.Add($"{verdict} {coord} r{distance} used={actionFocusUsedThisPhase}");
        }

        /// <summary>
        /// Whether a spotlight at <paramref name="coord"/> rides the framing that is already up, rather than
        /// asking for a new one.
        ///
        /// Compared against the tile the camera is REALLY framing, not the last spotlight the assembler
        /// emitted: a spotlight the gate declined never moved the camera, and treating that declined
        /// coordinate as the current framing is what silently swallowed whole phases' worth of cuts
        /// (plan §10.6). More forgiving than the frustum test on purpose — actions just past the frame edge
        /// still belong to the framing they came with.
        /// </summary>
        private bool WouldCoalesceIntoCurrentFraming(HexCoord coord)
        {
            return enableActionCameraFocus
                && actionFocusEngaged
                && actionFocusFramedCoord.HasValue
                && actionFocusFramedCoord.Value.DistanceTo(coord) <= ResolveTimingProfile().CoalesceRadiusHexes;
        }

        private float ResolveFrustumMarginFraction()
        {
            var profile = ResolveCinemachineCombatCameraBinder()?.Profile;
            return profile != null ? profile.FrustumMarginFraction : actionFocusMetricsMarginFraction;
        }

        /// <summary>
        /// Hands the camera back to the player, once, at the end of the phase. Never mid-phase: a return trip
        /// per event costs ~2.1s of damping and would turn a three-monster turn into half a minute (§3.3).
        /// </summary>
        private void ReleaseActionFocus()
        {
            actionFocusUsedThisPhase = 0;
            actionFocusFramedCoord = null;
            actionFocusHoldUntilRealtime = 0f;
            actionFocusLeadInSeconds = 0f;
            if (!actionFocusEngaged)
            {
                return;
            }

            actionFocusEngaged = false;
            CombatPresentationTrace.Record(CombatTraceChannel.Beat, "ActionFocus RELEASE");
            ResolveCinemachineCombatCameraBinder()?.ClearActionFocus();
        }

        [Header("Action Focus Metrics (dev)")]
        [Tooltip("Accumulates, for every presented action event, how far from the player it happened and " +
            "whether the camera was already framing it — the measurement pass behind " +
            "docs/monster-action-camera-focus-plan.md §5 P0. Editor and development builds only; off by default. " +
            "Purely observational: it never touches presentation timing.")]
        [SerializeField] private bool measureActionFocus;

        [Tooltip("Viewport inset used for the margin-applied framed flag (the plan's frustumMarginFraction). " +
            "Both the margin-0 and margin-applied verdicts are recorded per sample, so this can be re-judged " +
            "from one recording.")]
        [Range(0f, 0.49f)]
        [SerializeField] private float actionFocusMetricsMarginFraction = 0.1f;

        /// <summary>The presentation sequence currently replaying, used as the phase label for metrics.</summary>
        private string activePresentationLabel = string.Empty;

        /// <summary>
        /// The viewport inset the margin-applied framed verdict uses. Exposed so a dev readout states the
        /// margin its verdict was taken at instead of hardcoding a second copy of the number.
        /// </summary>
        public float ActionFocusMetricsMarginFraction => actionFocusMetricsMarginFraction;

        /// <summary>True when action-focus measurement is armed. Ignored outside editor / development builds.</summary>
        public bool MeasureActionFocus
        {
            get => measureActionFocus;
            set
            {
                measureActionFocus = value;
                SyncActionFocusMetricsArmed();
            }
        }

        private void SyncActionFocusMetricsArmed()
        {
            var armed = measureActionFocus && CombatDebugControlPanel.DebugUiAvailable;
            if (armed == CombatActionFocusMetrics.IsEnabled)
            {
                return;
            }

            if (armed)
            {
                CombatActionFocusMetrics.Enable();
            }
            else
            {
                CombatActionFocusMetrics.Disable();
            }
        }

        /// <summary>
        /// Records one presented action event for the P0 measurement. Off-screen is decided by the same pure
        /// geometry the feature itself will gate on (<see cref="CombatCameraController.IsPointFramed"/>
        /// against the live camera), so the numbers this produces are the numbers the gate would see.
        /// </summary>
        private void RecordActionFocusSample(string eventLabel, string actorId, HexCoord coord)
        {
            if (!CombatActionFocusMetrics.IsEnabled
                || State == null
                || !TryGetTileWorldPosition(coord, out var world))
            {
                return;
            }

            var frame = CameraFrame.FromCamera(prototype3DCamera);
            CombatActionFocusMetrics.Record(new CombatActionFocusSample(
                State.OverallTurnNumber,
                string.IsNullOrEmpty(activePresentationLabel) ? "immediate" : activePresentationLabel,
                eventLabel,
                actorId,
                coord,
                State.PlayerCoord.DistanceTo(coord),
                IsPointFramed(frame, world),
                IsPointFramed(frame, world, actionFocusMetricsMarginFraction)));
        }

        /// <summary>
        /// The coordinate a buffered effect should be framed at: its authored centre, else the source tile,
        /// else the tile of the unit it landed on. Returns false when the effect carries no position at all
        /// (deck/economy effects), which is exactly what should not be measured as an on-map event.
        /// </summary>
        private bool TryResolveEffectFocusCoord(EffectResultEvent effect, out HexCoord coord)
        {
            if (effect.Center.HasValue)
            {
                coord = effect.Center.Value;
                return true;
            }

            if (effect.SourceCoord.HasValue)
            {
                coord = effect.SourceCoord.Value;
                return true;
            }

            if (!string.IsNullOrEmpty(effect.TargetUnitId))
            {
                if (string.Equals(effect.TargetUnitId, "player", System.StringComparison.Ordinal))
                {
                    coord = State.PlayerCoord;
                    return true;
                }

                var monster = State.Monsters.FirstOrDefault(candidate => candidate.Id == effect.TargetUnitId);
                if (!string.IsNullOrEmpty(monster.Id))
                {
                    coord = monster.Coord;
                    return true;
                }
            }

            coord = default;
            return false;
        }

        private IEnumerator WaitForMonsterPhaseCompleteHold()
        {
            if (monsterPhaseCompleteHoldSeconds > 0f)
            {
                // Capture-safe: a raw WaitForSecondsRealtime runs on the wall clock, which fixed-rate frame
                // capture advances far slower than realtime, so this hold came out compressed in recorded
                // footage while playing correctly live. Off-capture WaitCinematicRealtime accumulates the
                // same unscaled clock, so live timing is unchanged.
                yield return WaitCinematicRealtime(monsterPhaseCompleteHoldSeconds);
            }
        }

        private void RequestMonsterDeathAudioCues(HashSet<string> diedUnitIds, CombatPresentationSnapshot before, string context)
        {
            if (diedUnitIds == null || diedUnitIds.Count == 0)
            {
                return;
            }

            foreach (var diedUnitId in diedUnitIds)
            {
                if (string.IsNullOrEmpty(diedUnitId) || string.Equals(diedUnitId, "player", System.StringComparison.Ordinal))
                {
                    continue;
                }

                var monster = before.FindMonster(diedUnitId);

                // Routed through the death-specific seam (not RequestAudioCue) so the audio presenter can
                // recognise the unit and stay silent when the lethal effect already sounded this death at
                // impact. Without that, the same death sounded twice ~0.1s+ apart, past the cue cooldown.
                AudioHost.RequestMonsterDeathAudioCues(diedUnitId, monster.DefinitionId, $"monster-death:{context}");
            }
        }

        private void RaiseCardCastCue(CombatCardKind kind, string cardSelectionKey = "")
        {
            AudioHost.RaiseCardCastCue(kind, cardSelectionKey);
        }

        // True only while CombatState is dispatching a buffered attack flush with impact-sync on. Used to
        // apply per-channel impact offsets to attack presentation without touching immediate effects.
        private bool IsImpactSyncedAttackFlush =>
            State != null && State.IsFlushingBufferedEffects
            && timingProfile != null && timingProfile.AlignImpactToAnimation;
    }
}

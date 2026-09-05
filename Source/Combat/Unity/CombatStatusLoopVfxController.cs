using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    // Persistent looping status-VFX reconciliation extracted from MapCombatController (P4 Stage 4).
    // Owns the live loop handles keyed by (unit, status kind) and keeps them matched to
    // CombatState.ActiveEffects. Plain class owned by MapCombatController; state/presentation/
    // visibility/anchor access arrives as delegates so no scene reference moves.
    internal sealed class CombatStatusLoopVfxController
    {
        private readonly System.Func<EffectPresentationController> resolvePresentation;
        private readonly System.Func<EffectPresentationController> peekPresentation;
        private readonly System.Func<CombatState> resolveState;
        private readonly System.Func<string, bool> isMonsterVisibleForPresentation;
        private readonly System.Func<string, CharacterVfxAnchorKind, Vector3?> tryGetAnchorWorldPosition;
        private readonly System.Func<Vector3> fallbackAnchorPosition;
        // 몸집 실측(발자국 지름). 바닥 링의 비례 스케일에만 쓰인다 — WS-2.
        private readonly System.Func<string, float?> tryGetFootprintDiameter;

        private readonly Dictionary<(string UnitId, StatusEffectKind Kind), GameObject> loopHandles =
            new Dictionary<(string, StatusEffectKind), GameObject>();

        public CombatStatusLoopVfxController(
            System.Func<EffectPresentationController> resolvePresentation,
            System.Func<EffectPresentationController> peekPresentation,
            System.Func<CombatState> resolveState,
            System.Func<string, bool> isMonsterVisibleForPresentation,
            System.Func<string, CharacterVfxAnchorKind, Vector3?> tryGetAnchorWorldPosition,
            System.Func<Vector3> fallbackAnchorPosition,
            System.Func<string, float?> tryGetFootprintDiameter = null)
        {
            this.resolvePresentation = resolvePresentation;
            this.peekPresentation = peekPresentation;
            this.resolveState = resolveState;
            this.isMonsterVisibleForPresentation = isMonsterVisibleForPresentation;
            this.tryGetAnchorWorldPosition = tryGetAnchorWorldPosition;
            this.fallbackAnchorPosition = fallbackAnchorPosition;
            this.tryGetFootprintDiameter = tryGetFootprintDiameter;
        }

        /// <summary>
        /// Attaches/detaches persistent looping status VFX so the live set matches the player's and
        /// monsters' active status effects. Idempotent: spawns a loop on a status's first appearance and
        /// stops it when the status expires, the unit dies, or combat resets. Each loop follows its actor's
        /// resolved anchor world position every frame (markers are pooled/animated).
        /// </summary>
        public void Reconcile()
        {
            var controller = resolvePresentation();
            var state = resolveState();
            if (controller == null || state == null)
            {
                Clear();
                return;
            }

            var catalog = controller.VfxCatalog;
            if (catalog == null)
            {
                return;
            }

            // Drop handles whose VFX GameObject was destroyed externally (e.g. a non-looping prefab that
            // self-terminated) so they get respawned while the status is still active.
            PruneDestroyedHandles();

            // 기준 체구 = 플레이어의 발자국 지름. 이 패스 안에서 한 번만 재고 모든 링이 공유한다
            // (WS-2). 못 재면 0 → 모든 계수가 1로 물러나 현행 크기 그대로다.
            var referenceFootprint = ResolveFootprintDiameter(state.Player.Id);

            var desired = new HashSet<(string UnitId, StatusEffectKind Kind)>();
            foreach (var effect in state.ActiveEffects)
            {
                if (effect.IsExpired || string.IsNullOrEmpty(effect.TargetUnitId))
                {
                    continue;
                }

                var isPlayer = string.Equals(effect.TargetUnitId, state.Player.Id, System.StringComparison.Ordinal);
                if (!isPlayer && !isMonsterVisibleForPresentation(effect.TargetUnitId))
                {
                    continue;
                }

                var targetFilter = isPlayer ? EffectVfxTargetFilter.Player : EffectVfxTargetFilter.Monster;
                if (!catalog.TryResolveStatusLoop(effect.Kind, targetFilter, out var entry) || entry == null)
                {
                    continue;
                }

                var key = (effect.TargetUnitId, effect.Kind);
                desired.Add(key);
                if (loopHandles.ContainsKey(key))
                {
                    continue;
                }

                var handle = controller.PlayLoopFollowing(
                    entry,
                    MakeAnchorProvider(effect.TargetUnitId, entry.LoopAnchor),
                    bodyScale: ResolveBodyScale(entry, effect.TargetUnitId, referenceFootprint));
                if (handle != null)
                {
                    loopHandles[key] = handle;
                }
            }

            // 보스 페이즈 아우라는 ActiveEffect가 아니라 페이즈 트랙에서 직접 읽는 영구 속성이다: 절대 만료되지
            // 않아야 하고 상태 아이콘/툴팁/정화 경로에 도달해서도 안 되기 때문에 상태이상 파이프라인을 태우지
            // 않는다(강화%가 트랙에서 직접 읽히는 것과 같은 이유 — docs/boss-raid-plan.md §8-2). 그러면서도 여기서
            // 같은 status.loop 카탈로그와 팔로우 머신을 재사용해 아우라 루프를 붙인다. 페이즈가 아우라 없는
            // 구간으로 가거나 보스가 죽으면 desired에서 빠져 아래 stale 제거가 걷어 간다.
            foreach (var boss in state.BossPhases)
            {
                // §28 W8: 페이즈 아우라 파티클 루프 OFF(링 bossAuraRingEnabled와 한 쌍 — 실플레이 판정).
                // 되살릴 땐 아래 게이트만 켜면 된다. 저작(아우라 kind)은 그대로 둔다.
                if (!BossPhaseAuraLoopEnabled)
                {
                    break;
                }

                if (boss.AuraStatusKind is not { } auraKind)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(boss.BossUnitId) || !isMonsterVisibleForPresentation(boss.BossUnitId))
                {
                    continue;
                }

                if (!catalog.TryResolveStatusLoop(auraKind, EffectVfxTargetFilter.Monster, out var auraEntry) || auraEntry == null)
                {
                    continue;
                }

                var key = (boss.BossUnitId, auraKind);
                desired.Add(key);
                if (loopHandles.ContainsKey(key))
                {
                    continue;
                }

                var auraHandle = controller.PlayLoopFollowing(
                    auraEntry,
                    MakeAnchorProvider(boss.BossUnitId, auraEntry.LoopAnchor),
                    bodyScale: ResolveBodyScale(auraEntry, boss.BossUnitId, referenceFootprint));
                if (auraHandle != null)
                {
                    loopHandles[key] = auraHandle;
                }
            }

            if (loopHandles.Count > 0)
            {
                var stale = loopHandles.Keys.Where(key => !desired.Contains(key)).ToList();
                foreach (var key in stale)
                {
                    StopHandle(controller, loopHandles[key], immediate: true);
                    loopHandles.Remove(key);
                }
            }
        }

        // §28 W8 게이트. static readonly라 CS0162(도달 불가) 경고 없이 컴파일된다.
        private static readonly bool BossPhaseAuraLoopEnabled = false;

        private void PruneDestroyedHandles()
        {
            if (loopHandles.Count == 0)
            {
                return;
            }

            var dead = loopHandles.Where(pair => pair.Value == null).Select(pair => pair.Key).ToList();
            foreach (var key in dead)
            {
                loopHandles.Remove(key);
            }
        }

        public void Clear()
        {
            if (loopHandles.Count == 0)
            {
                return;
            }

            // Peek (never create) the presentation controller during teardown paths.
            var controller = peekPresentation();
            foreach (var handle in loopHandles.Values)
            {
                StopHandle(controller, handle, immediate: true);
            }

            loopHandles.Clear();
        }

        private static void StopHandle(EffectPresentationController controller, GameObject handle, bool immediate)
        {
            if (handle == null)
            {
                return;
            }

            if (controller != null)
            {
                controller.StopLoop(handle, immediate);
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(handle);
            }
            else
            {
                Object.DestroyImmediate(handle);
            }
        }

        /// <summary>
        /// 이 큐에 적용할 몸집 계수(WS-2). <b>바닥(Ground) 앵커 루프에만</b> 건다 — 기절은 머리 위
        /// 실측 앵커(HeadTop)로 이미 몸집을 따라가고 사용자 판정으로 확정된 자리라 손대지 않는다.
        /// </summary>
        private float ResolveBodyScale(EffectVfxCatalog.Entry entry, string unitId, float referenceFootprint)
        {
            if (entry == null || entry.LoopAnchor != CharacterVfxAnchorKind.Ground)
            {
                return 1f;
            }

            return StatusLoopBodyScale.Resolve(ResolveFootprintDiameter(unitId), referenceFootprint);
        }

        private float ResolveFootprintDiameter(string unitId)
        {
            if (tryGetFootprintDiameter == null || string.IsNullOrEmpty(unitId))
            {
                return 0f;
            }

            return tryGetFootprintDiameter(unitId) ?? 0f;
        }

        private System.Func<Vector3> MakeAnchorProvider(string unitId, CharacterVfxAnchorKind anchorKind)
        {
            var lastKnown = Vector3.zero;
            var hasLastKnown = false;
            return () =>
            {
                if (tryGetAnchorWorldPosition(unitId, anchorKind) is { } position)
                {
                    lastKnown = position;
                    hasLastKnown = true;
                    return position;
                }

                return hasLastKnown ? lastKnown : fallbackAnchorPosition();
            };
        }
    }
}

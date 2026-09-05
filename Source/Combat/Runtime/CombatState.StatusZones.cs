using System;
using System.Collections.Generic;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 상태이상 지대(두억시니 §4-3)의 <b>실시간 판정</b>(2026-09-05 사용자 요구).
    ///
    /// <para>🔑 계약이 바뀌었다. 종전에는 지대가 「필드 오브젝트 틱이 돌 때 밟고 있으면 2턴짜리
    /// 상태이상을 새로 건다」였다 — 그래서 ①이동 카드로 들어간 순간에는 아무 일도 없다가 턴이
    /// 끝나야 걸리고 ②벗어난 뒤에도 2턴이 남아 있었다. 지금은 <b>서 있는 동안만 걸려 있는
    /// 파생 상태</b>다: 진입하는 순간 붙고 벗어나는 순간 떨어진다.</para>
    ///
    /// <para>🔴 파생 상태의 표식은 <see cref="MonsterStatusZone.SourceRef"/>다. 이 ref를 단 상태만
    /// 이 파일이 걷어 간다 — 함정·카드가 건 같은 종류의 상태이상은 건드리지 않는다. 그래서
    /// 「올무로 걸린 둔화」가 지대를 벗어났다고 함께 풀리지 않는다.</para>
    ///
    /// <para>🔴 지속 턴은 상태이상이 아니라 <b>지대 자신</b>이 센다(<c>FieldObject.RemainingTurns</c>).
    /// 파생 상태에 남은 턴을 적어 두면 두 시계가 갈라진다 — 그래서 부여할 때마다 고정 값
    /// (<see cref="StandingRefreshTurns"/>)으로 다시 쓰고, 실제 해제는 「서 있는가」가 정한다.</para>
    /// </summary>
    public sealed partial class CombatState
    {
        /// <summary>
        /// 파생 상태에 적어 두는 남은 턴. 값 자체는 의미가 없다(해제는 「서 있는가」가 정한다) —
        /// 다만 턴 틱이 한 번 도는 사이에 스스로 만료되지 않을 만큼은 커야 한다.
        /// </summary>
        internal const int StandingRefreshTurns = 2;

        private readonly List<StatusEffectKind> reusableStandingZoneKinds = new List<StatusEffectKind>();

        /// <summary>
        /// 지금 플레이어가 서 있는 칸의 지대 효과를 상태이상에 반영한다 — 붙일 것은 붙이고,
        /// 벗어난 것은 즉시 뗀다.
        ///
        /// <para>부르는 곳은 둘뿐이다: <see cref="PlayerCoord"/> 세터(플레이어가 움직였다)와
        /// 필드 오브젝트 틱 뒤(지대가 만료됐다). 둘 다 「서 있는 집합이 바뀔 수 있는 순간」이다.</para>
        /// </summary>
        private void RefreshStandingStatusZoneEffects()
        {
            // 생성자·서스펜드 복원 도중에는 협력자가 아직 없다. 좌표만 세워 두고 조용히 빠진다 —
            // 복원 끝에서 호출자가 다시 부른다(RestoreStandingStatusZoneEffects).
            if (FieldObjects == null || activeEffects == null || Player == null || Player.IsDead)
            {
                return;
            }

            var standing = reusableStandingZoneKinds;
            standing.Clear();
            foreach (var fieldObject in FieldObjects.Objects)
            {
                if (fieldObject.Kind != FieldObjectKind.StatusZone
                    || fieldObject.IsExpired
                    || string.Equals(fieldObject.SourceUnitId, PlayerUnitId, StringComparison.Ordinal)
                    || !fieldObject.Contains(playerCoord))
                {
                    continue;
                }

                if (!standing.Contains(fieldObject.StatusKind))
                {
                    standing.Add(fieldObject.StatusKind);
                }
            }

            RemoveStandingZoneEffectsNotIn(standing);
            ApplyStandingZoneEffects(standing);
        }

        /// <summary>
        /// <b>서 있는 자리가 정하는 효과</b>를 전부 다시 맞춘다(2026-09-05). 지대와 홀림이 같은 관문을
        /// 쓴다 — 둘 다 「거기 있는 동안만」이고, 둘 다 좌표가 바뀌는 순간 답이 달라진다.
        ///
        /// <para>🔑 홀림은 규칙 문서상 이미 「반경 안에 있는 동안만 잠근다」였는데 집행이 턴 틱에만
        /// 걸려 있어, 이동 카드로 들어가도 턴이 끝나야 잠기고 벗어나도 턴이 끝나야 풀렸다.
        /// 판정 자체(<see cref="TickAuraSealPerTurn"/>)는 멱등이고 위치만 보므로, 여기서 한 번 더
        /// 부르는 것으로 실시간이 된다 — 새 배관을 만들지 않는다.</para>
        /// </summary>
        private void RefreshPositionDerivedEffects()
        {
            RefreshStandingStatusZoneEffects();
            if (monsters != null && monsterCatalog != null && Player != null)
            {
                TickAuraSealPerTurn();
            }
        }

        /// <summary>지대를 벗어났으면 즉시 뗀다 — 지대 유래(<c>SourceRef</c>) 상태만 본다.</summary>
        private void RemoveStandingZoneEffectsNotIn(List<StatusEffectKind> standing)
        {
            for (var i = activeEffects.Count - 1; i >= 0; i--)
            {
                var effect = activeEffects[i];
                if (!string.Equals(effect.SourceRef, MonsterStatusZone.SourceRef, StringComparison.Ordinal)
                    || !string.Equals(effect.TargetUnitId, PlayerUnitId, StringComparison.Ordinal)
                    || standing.Contains(effect.Kind))
                {
                    continue;
                }

                activeEffects.RemoveAt(i);
                RaiseStatusEffectExpired(effect, MonsterStatusZone.SourceRef);
                RefreshPlayerVisionIfVisionStatus(effect.Kind, PlayerUnitId);
            }
        }

        /// <summary>서 있는 지대의 효과를 붙인다(이미 걸려 있으면 갱신만 — 새 연출을 다시 올리지 않는다).</summary>
        private void ApplyStandingZoneEffects(List<StatusEffectKind> standing)
        {
            for (var i = 0; i < standing.Count; i++)
            {
                var kind = standing[i];
                var alreadyStanding = HasStandingZoneEffect(kind);
                var amount = StatusEffectInfo.DefaultAmount(kind);
                if (!AddDurationStatusEffect(kind, PlayerUnitId, StandingRefreshTurns, amount, MonsterStatusZone.SourceRef))
                {
                    continue;
                }

                if (alreadyStanding)
                {
                    // 계속 서 있는 것뿐이다 — 매 턴 "또 걸렸다"를 띄우면 화면이 소리친다.
                    continue;
                }

                RaiseStatusEffect(
                    kind,
                    playerCoord,
                    0,
                    amount,
                    PlayerUnitId,
                    MonsterStatusZone.SourceRef,
                    sourceActorKind: "field",
                    targetActorKind: "player");
            }
        }

        private bool HasStandingZoneEffect(StatusEffectKind kind)
        {
            for (var i = 0; i < activeEffects.Count; i++)
            {
                var effect = activeEffects[i];
                if (effect.Kind == kind
                    && !effect.IsExpired
                    && string.Equals(effect.TargetUnitId, PlayerUnitId, StringComparison.Ordinal)
                    && string.Equals(effect.SourceRef, MonsterStatusZone.SourceRef, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

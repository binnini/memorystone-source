using System;
using System.Collections.Generic;
using System.Linq;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 지금 걸려 있는 상태이상 전부(플레이어·몬스터 공통)의 보관소. 예전에는
    /// <see cref="CombatState"/>의 <c>List&lt;ActiveEffect&gt;</c> 필드 하나였고, 그 리스트를
    /// 직접 순회·수정하는 코드가 파일 곳곳에 흩어져 <b>같은 판정을 손으로 여러 벌 적어 두고</b> 있었다.
    ///
    /// 🔑 <b>이 타입이 사 온 것은 「같은 개념을 한 번만 적는 것」이다.</b> 실측(2026-08-31):
    /// ①「같은 유닛·같은 종류·안 만료」 술어가 **8벌** ②「이 종류를 싹 지우고 하나만 새로 넣는다」가
    /// **4벌**(반사·민첩·무적·강화) 손으로 적혀 있었다. 이제 각각 <see cref="Has"/>·<see cref="IndexOf"/>
    /// 와 <see cref="ReplaceSingle"/> 한 곳이다.
    ///
    /// 🔴 <b>여기 없는 것</b>: 「만료되면 무슨 일이 일어나는가」는 이 타입의 몫이 아니다.
    /// 만료 이벤트(<c>RaiseStatusEffectExpired</c>)·시야 재계산·점유 갱신·제어 면역 등록은 전부
    /// 규칙과 연출의 일이라 <see cref="CombatState"/>에 남아 있다. 이 타입은 <b>담고·찾고·합칠</b> 뿐
    /// 아무에게도 알리지 않는다 — 그래서 콜백을 하나도 받지 않는다.
    ///
    /// ⚠️ <c>Effects/EffectRuntime.cs</c>에도 같은 이름의 <c>activeEffects</c> 필드가 있지만
    /// <b>다른 물건</b>이다(출하 전투가 쓰지 않는 병렬 구현 — 개발 씬과 자기 테스트만 쓴다).
    /// </summary>
    internal sealed class ActiveEffectRegistry
    {
        private readonly List<ActiveEffect> effects = new List<ActiveEffect>();

        public int Count => effects.Count;

        /// <summary>만료 틱처럼 자리를 지키며 값을 갈아 끼워야 하는 순회용.</summary>
        public ActiveEffect this[int index]
        {
            get => effects[index];
            set => effects[index] = value;
        }

        /// <summary>읽기 전용 뷰(공개 투영·세이브 기록·스냅샷 원본).</summary>
        public IReadOnlyList<ActiveEffect> All => effects;

        public void RemoveAt(int index) => effects.RemoveAt(index);

        public void Add(ActiveEffect effect) => effects.Add(effect);

        // ------------------------------------------------------------------ 조회
        // 「같은 유닛·같은 종류·안 만료」의 정의가 이 아래 한 곳이다. 예전에는 이 술어가
        // Any/Exists/FindIndex/Where/손 루프로 8벌 흩어져 있었다.

        private static bool Matches(ActiveEffect effect, string unitId, StatusEffectKind kind)
        {
            return effect.Kind == kind
                && string.Equals(effect.TargetUnitId, unitId, StringComparison.Ordinal)
                && !effect.IsExpired;
        }

        public bool Has(string unitId, StatusEffectKind kind)
        {
            return IndexOf(unitId, kind) >= 0;
        }

        /// <summary>가장 오래된(=먼저 들어온) 인스턴스의 인덱스. 없으면 -1.</summary>
        public int IndexOf(string unitId, StatusEffectKind kind)
        {
            for (var i = 0; i < effects.Count; i++)
            {
                if (Matches(effects[i], unitId, kind))
                {
                    return i;
                }
            }

            return -1;
        }

        public int CountOf(string unitId, StatusEffectKind kind)
        {
            var count = 0;
            for (var i = 0; i < effects.Count; i++)
            {
                if (Matches(effects[i], unitId, kind))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 충전이 <b>남아 있는</b>(Amount &gt; 0) 인스턴스의 인덱스. 수호처럼 턴이 아니라 소비로
        /// 줄어드는 상태가 쓴다 — Amount 0짜리는 이미 다 쓴 껍데기라 있어도 못 쓴다.
        /// </summary>
        public int IndexOfCharged(string unitId, StatusEffectKind kind)
        {
            for (var i = 0; i < effects.Count; i++)
            {
                if (Matches(effects[i], unitId, kind) && effects[i].Amount > 0)
                {
                    return i;
                }
            }

            return -1;
        }

        public bool HasCharged(string unitId, StatusEffectKind kind)
        {
            return IndexOfCharged(unitId, kind) >= 0;
        }

        public int SumAmount(string unitId, StatusEffectKind kind)
        {
            var total = 0;
            for (var i = 0; i < effects.Count; i++)
            {
                if (Matches(effects[i], unitId, kind))
                {
                    total += Math.Max(0, effects[i].Amount);
                }
            }

            return total;
        }

        /// <summary>
        /// 한 유닛의 <paramref name="valueMode"/> 축에 속하는 상태이상 Amount 합.
        /// 소비 지점은 종류 이름이 아니라 이 축으로 묻는다(P1.5 / D-6).
        /// </summary>
        public int SumAmountByValueMode(string unitId, StatusEffectValueMode valueMode)
        {
            var total = 0;
            for (var i = 0; i < effects.Count; i++)
            {
                var effect = effects[i];
                if (string.Equals(effect.TargetUnitId, unitId, StringComparison.Ordinal)
                    && !effect.IsExpired
                    && StatusEffectInfo.ValueMode(effect.Kind) == valueMode)
                {
                    total += Math.Max(0, effect.Amount);
                }
            }

            return total;
        }

        /// <summary>
        /// ⚠️ 정화 계열 조회 둘은 <b>일부러 만료 여부를 묻지 않는다</b>(기존 동작 보존) —
        /// 이 두 곳만 술어가 다르므로 <see cref="Matches"/>를 쓰지 않는다.
        /// </summary>
        public bool HasAnyCleansable(string unitId)
        {
            return effects.Any(effect =>
                string.Equals(effect.TargetUnitId, unitId, StringComparison.Ordinal)
                && StatusEffectInfo.IsCleansable(effect.Kind));
        }

        public List<ActiveEffect> CleansableOn(string unitId)
        {
            return effects
                .Where(effect => string.Equals(effect.TargetUnitId, unitId, StringComparison.Ordinal)
                    && StatusEffectInfo.IsCleansable(effect.Kind))
                .ToList();
        }

        // ------------------------------------------------------------------ 부여

        /// <summary>
        /// 부여·병합. 저작이 「지속만 쌓는다」고 말한 종류는 같은 대상의 기존 인스턴스와 합친다 —
        /// <b>지속은 더 긴 쪽</b>, 수치는 저작에 따라 <b>가산 또는 더 큰 쪽</b>, 출처는 기존 것을 지킨다.
        ///
        /// ⚠️ <b>합친 것을 꼬리로 옮기는 동작은 원본 그대로 보존했지만, 그 이유는 확인되지 않았다.</b>
        /// 원본 주석은 「몬스터 행동 턴 경계의 유예 판정이 <c>BeginMonsterAttackResolution</c>의
        /// 행동 전 개수를 신선도 표식으로 쓰므로, 갱신된 디버프가 새것 취급이 되어야 한다」고 적혀
        /// 있었다. 2026-08-31 P3에서 실제로 뒤집어 봤더니(꼬리 이동 제거) <b>스위트가 전부 초록이고</b>,
        /// 갱신된 둔화의 남은 턴도 그대로였다.
        /// 🔑 <b>실제로 지키는 것은 인덱스가 아니라 <c>SkipNextTick</c>이다</b> — 몬스터가 거는
        /// 상태이상은 <c>skipNextTick: true</c>로 들어오고, 틱 루프의 <b>첫 분기</b>가 그 경계를
        /// 통째로 건너뛴다(<c>ApplyActiveEffectTurnStart</c>). 즉 <c>isFresh</c> 인덱스 유예는 이
        /// 경우엔 덧그물이다.
        /// 동작 무변경이 이 작업의 계약이라 이동은 남겼다. 지우려면 <b>먼저</b> 이 성질을 무는
        /// 테스트가 있어야 한다(인계문 §8 참고).
        /// </summary>
        public void AddOrMerge(
            StatusEffectKind kind,
            string targetUnitId,
            int turns,
            int amount,
            string sourceRef,
            bool skipNextTick)
        {
            var clampedTurns = Math.Max(1, turns);
            var clampedAmount = Math.Max(0, amount);

            if (!StatusEffectInfo.UsesDurationOnlyStacking(kind) || string.IsNullOrEmpty(targetUnitId))
            {
                effects.Add(new ActiveEffect(
                    EffectType.Duration, kind, targetUnitId, clampedTurns, clampedAmount, sourceRef, skipNextTick));
                return;
            }

            var existingIndex = IndexOf(targetUnitId, kind);
            if (existingIndex < 0)
            {
                effects.Add(new ActiveEffect(
                    EffectType.Duration, kind, targetUnitId, clampedTurns, clampedAmount, sourceRef, skipNextTick));
                return;
            }

            var existing = effects[existingIndex];
            var mergedTurns = Math.Max(existing.RemainingTurns, clampedTurns);
            var mergedAmount = StatusEffectInfo.UsesAdditiveAmountStacking(kind)
                ? existing.Amount + clampedAmount
                : Math.Max(existing.Amount, clampedAmount);
            var mergedSource = string.IsNullOrWhiteSpace(existing.SourceRef)
                ? sourceRef
                : existing.SourceRef;

            effects.RemoveAt(existingIndex);
            effects.Add(new ActiveEffect(
                EffectType.Duration,
                kind,
                targetUnitId,
                mergedTurns,
                mergedAmount,
                mergedSource,
                existing.SkipNextTick || skipNextTick));
        }

        /// <summary>
        /// 「이 대상의 이 종류는 항상 한 벌」 갱신(RefreshDuration / maxStacks 1). 반사·민첩·무적·강화
        /// 네 곳이 각자 손으로 적어 두던 <c>RemoveAll</c> + <c>Add</c> 짝이다.
        /// ⚠️ 제거는 <b>만료 여부를 묻지 않는다</b> — 다 쓴 껍데기도 함께 걷어야 한 벌이 유지된다
        /// (네 곳의 원래 동작 그대로).
        /// </summary>
        public void ReplaceSingle(StatusEffectKind kind, string unitId, int turns, int amount, string sourceRef)
        {
            effects.RemoveAll(effect =>
                effect.Kind == kind && string.Equals(effect.TargetUnitId, unitId, StringComparison.Ordinal));
            effects.Add(new ActiveEffect(EffectType.Duration, kind, unitId, turns, amount, sourceRef));
        }

        /// <summary>
        /// <b>같은 출처</b>가 건 같은 종류를 제자리에서 갱신한다(장판 속박). 여기서만 꼬리로 안 옮기는
        /// 이유는 계획부가 보는 순서를 흔들지 않기 위해서다 — 장판은 매 턴 다시 거는 것이라
        /// 꼬리로 옮기면 매번 "새것"이 되어 유예를 무한히 갱신한다.
        /// 갈아 끼웠으면 true, 대상이 없으면 false(호출부가 <see cref="Add"/>한다).
        /// </summary>
        public bool TryReplaceInPlaceFromSameSource(ActiveEffect refreshed)
        {
            for (var i = 0; i < effects.Count; i++)
            {
                var existing = effects[i];
                if (existing.Kind == refreshed.Kind
                    && string.Equals(existing.TargetUnitId, refreshed.TargetUnitId, StringComparison.Ordinal)
                    && string.Equals(existing.SourceRef, refreshed.SourceRef, StringComparison.Ordinal))
                {
                    effects[i] = refreshed;
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------ 스냅샷 · 복원

        /// <summary>
        /// 통째 교체. 세이브 복원과 <b>지연 몬스터 행동 되감기</b>가 쓴다 — 후자는 연출 비트에 맞춰
        /// 규칙 상태를 되감는 자리라 특히 조심할 것
        /// (<c>MonsterRuntimeTests.DeferredMonsterActionRewinds...</c>가 잠근다).
        /// </summary>
        public void RestoreFrom(IEnumerable<ActiveEffect> snapshot)
        {
            // ⚠️ 먼저 확정한 뒤에 비운다. 호출부가 이 보관소 자신의 뷰(All)나 그 위의 지연 질의를
            // 넘기면, 비우고 나서 읽는 순서에서는 원본이 이미 사라진다. 오늘은 그런 호출부가 없지만
            // 되감기·세이브 복원 둘 다 "통째 교체"라 언젠가 그렇게 부르기 쉬운 자리다.
            var replacement = snapshot?.ToList();
            effects.Clear();
            if (replacement != null)
            {
                effects.AddRange(replacement);
            }
        }
    }
}

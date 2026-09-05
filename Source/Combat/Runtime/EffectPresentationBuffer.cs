using System;
using System.Collections.Generic;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 연출 전용 효과 버퍼. <see cref="CombatState.EffectResolved"/> 발사를 규칙이 해결한 순간이
    /// 아니라 연출층이 고른 순간(공격 임팩트 프레임 등)으로 미루기 위한 큐와 그 정책을 담는다.
    ///
    /// 🔑 <b>이 타입은 전투 규칙을 모른다.</b> 핸들러를 <b>인자로만</b> 받고(스스로 찾아 나서지
    /// 않는다), 규칙 상태를 읽지도 쓰지도 않는다. 그래서 「연출을 언제 재생할지」가
    /// <see cref="CombatState"/>의 필드 네 개로 흩어져 있던 것이 여기 한 곳으로 모인다.
    ///
    /// ⚠️ <b>버퍼링과 「몬스터 행동 되감기」를 같은 것으로 보지 말 것.</b> 이 타입은 발사 시점만
    /// 미룰 뿐 규칙에 영향을 주지 않지만, 이웃한 <see cref="DeferredMonsterActionState"/>는
    /// 성격이 전혀 다르다 — 그쪽은 연출 비트에 맞춰 플레이어 HP·좌표·페이즈·상태이상을
    /// 실제로 되감았다가 다시 적용하고 패배 판정까지 집행한다(의도된 설계다).
    /// 2026-08-31 조사에서 이 둘이 한 묶음으로 취급돼 온 것이 확인됐고, 그래서 여기서 갈랐다.
    /// </summary>
    internal sealed class EffectPresentationBuffer
    {
        private readonly List<EffectResultEvent> queue = new List<EffectResultEvent>();
        private int groupSequence;

        /// <summary>버퍼링 중이면 <see cref="TryEnqueue"/>가 발사 대신 큐에 쌓는다.</summary>
        public bool IsBuffering { get; private set; }

        /// <summary>
        /// 큐에 쌓인 배치를 실제로 발사하는 동안에만 true. 연출 구독자가 이 값을 보고 즉시 발사된
        /// 효과(상태이상 틱·함정 — 이들은 플러시를 타지 않는다)와 임팩트 배치를 구분한다.
        /// </summary>
        public bool IsFlushing { get; private set; }

        /// <summary>대기 중인 효과의 읽기 전용 뷰.</summary>
        public IReadOnlyList<EffectResultEvent> Queued => queue;

        /// <summary>버퍼링을 시작한다. 이전 배치가 남아 있으면 버린다.</summary>
        public void Begin()
        {
            IsBuffering = true;
            queue.Clear();
        }

        /// <summary>
        /// 버퍼링 중이면 큐에 넣고 true를 돌려준다(호출부는 즉시 반환하면 된다).
        /// 아니면 false — 호출부가 직접 발사한다.
        /// </summary>
        public bool TryEnqueue(EffectResultEvent resultEvent)
        {
            if (!IsBuffering)
            {
                return false;
            }

            queue.Add(resultEvent);
            return true;
        }

        /// <summary>
        /// 버퍼링을 끝내고 쌓인 효과를 해결 순서대로 발사한다. 멱등이다 — 큐가 비어 있으면
        /// 아무 일도 하지 않으므로 임팩트 비트와 취소 경로 양쪽에서 불러도 두 번 발사되지 않는다.
        /// ⚠️ 구독자가 없어도 큐는 비운다(원래 동작).
        /// </summary>
        public void Flush(Action<EffectResultEvent> handler)
        {
            IsBuffering = false;
            if (queue.Count == 0)
            {
                return;
            }

            var pending = queue.ToArray();
            queue.Clear();
            if (handler == null)
            {
                return;
            }

            IsFlushing = true;
            try
            {
                foreach (var queued in pending)
                {
                    handler.Invoke(queued);
                }
            }
            finally
            {
                IsFlushing = false;
            }
        }

        /// <summary>
        /// <paramref name="predicate"/>에 걸리는 효과만 발사하고 나머지는 큐에 남긴다(다중 행동
        /// 턴의 행동별 재생). 남은 것이 없으면 버퍼링도 함께 끝난다.
        /// </summary>
        public void Flush(Action<EffectResultEvent> handler, Func<EffectResultEvent, bool> predicate)
        {
            if (predicate == null)
            {
                Flush(handler);
                return;
            }

            if (queue.Count == 0)
            {
                return;
            }

            var remaining = new List<EffectResultEvent>(queue.Count);
            IsFlushing = true;
            try
            {
                foreach (var queued in queue)
                {
                    if (predicate(queued))
                    {
                        handler?.Invoke(queued);
                    }
                    else
                    {
                        remaining.Add(queued);
                    }
                }
            }
            finally
            {
                IsFlushing = false;
            }

            queue.Clear();
            queue.AddRange(remaining);
            IsBuffering = queue.Count > 0;
        }

        /// <summary>
        /// 인덱스 하나를 <b>큐에서 빼지 않고</b> 발사한다 — 연출 스케줄러가 하나씩 간격을 두고
        /// 재생하는 동안 인덱스가 흔들리지 않아야 하기 때문이다. 범위 밖·핸들러 없음은 무시.
        /// 재생이 끝나면 <see cref="EndDispatch"/>로 큐를 버린다.
        /// </summary>
        public void DispatchAt(int index, Action<EffectResultEvent> handler)
        {
            if (index < 0 || index >= queue.Count || handler == null)
            {
                return;
            }

            IsFlushing = true;
            try
            {
                handler.Invoke(queue[index]);
            }
            finally
            {
                IsFlushing = false;
            }
        }

        /// <summary>스케줄러 재생 종료: 남은 큐를 버리고 버퍼링을 끝낸다.</summary>
        public void EndDispatch()
        {
            queue.Clear();
            IsBuffering = false;
        }

        /// <summary>
        /// 연출 그룹 id 채번. 한 행동에서 나온 효과들을 연출층이 묶어 볼 수 있게 하는 라벨이라
        /// 규칙은 이 값을 읽지 않는다.
        /// </summary>
        public string NextGroupId(string ownerId, string actionId)
        {
            groupSequence++;
            var safeOwner = string.IsNullOrWhiteSpace(ownerId) ? "unknown" : ownerId;
            var safeAction = string.IsNullOrWhiteSpace(actionId) ? "effect" : actionId;
            return $"{safeOwner}:{safeAction}:{groupSequence}";
        }
    }
}

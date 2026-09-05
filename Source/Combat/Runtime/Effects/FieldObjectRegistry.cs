using System;
using System.Collections.Generic;

namespace SeoulPlayup.Combat.Runtime
{
    public sealed class FieldObjectRegistry
    {
        private readonly List<FieldObject> objects = new List<FieldObject>();

        public IReadOnlyList<FieldObject> Objects => objects;

        public void Add(FieldObject fieldObject)
        {
            objects.Add(fieldObject);
        }

        public void Clear()
        {
            objects.Clear();
        }

        /// <summary>
        /// 술어에 걸리는 오브젝트를 전부 제거하고 제거된 수를 돌려준다(전멸기 폭발의 장판 파괴 · §21.6).
        /// 별도 이벤트는 없다 — 만료 제거(<see cref="Tick"/>)와 같은 규약으로, 표현 계층이 상태를
        /// 다시 읽어 리컨실한다.
        /// </summary>
        public int RemoveAll(Predicate<FieldObject> match)
        {
            if (match == null)
            {
                throw new ArgumentNullException(nameof(match));
            }

            return objects.RemoveAll(match);
        }

        public int Tick(Func<FieldObject, FieldObject> tick)
        {
            if (tick == null)
            {
                throw new ArgumentNullException(nameof(tick));
            }

            var expiredCount = 0;
            for (var i = 0; i < objects.Count; i++)
            {
                objects[i] = tick(objects[i]);
            }

            for (var i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i].IsExpired)
                {
                    objects.RemoveAt(i);
                    expiredCount++;
                }
            }

            return expiredCount;
        }
    }
}

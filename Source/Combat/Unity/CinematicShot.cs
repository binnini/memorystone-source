using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    // 4B-A(2026-09-04): MapCombatController.Cinematics.cs의 private 중첩 타입에서 최상위 internal로 재배치.
    // 스테이지 인트로(시네마틱 협력자)와 보스 조우 비트(MapCombatController.BossEncounter.cs)가 같이 쓰는
    // 공유 프리미티브라 한쪽의 사설 타입으로 둘 수 없다. 본문 무변경.
    // Easing applied across a shot's dolly. Auto shots (overview/landmark/finale) use EaseInOut (the
    // prior behavior); an authored dolly chain uses EaseIn on the first segment, Linear in the middle,
    // and EaseOut on the last so the whole chain accelerates once and decelerates once.
    internal enum CinematicShotEase
    {
        EaseInOut,
        Linear,
        EaseIn,
        EaseOut
    }

    // A single cinematic shot: a straight camera dolly from one pose to another, holding the look-at.
    // Shots are cut between (no blend), so each carries its own opening pose. This is the shared shot
    // primitive: the stage-intro trailer builds these, and the boss encounter reveal beat composes its
    // own low-angle arc as CinematicShots and plays them through AnimateCinematicShot — one shot player,
    // no second copy of the cut+dolly loop.
    internal readonly struct CinematicShot
    {
        public readonly Vector3 FromPos;
        public readonly Vector3 ToPos;
        public readonly Vector3 FromLookAt;
        public readonly Vector3 ToLookAt;
        public readonly float Duration;
        public readonly CinematicShotEase Ease;

        public CinematicShot(Vector3 fromPos, Vector3 toPos, Vector3 fromLookAt, Vector3 toLookAt, float duration,
            CinematicShotEase ease = CinematicShotEase.EaseInOut)
        {
            FromPos = fromPos;
            ToPos = toPos;
            FromLookAt = fromLookAt;
            ToLookAt = toLookAt;
            Duration = duration;
            Ease = ease;
        }
    }
}

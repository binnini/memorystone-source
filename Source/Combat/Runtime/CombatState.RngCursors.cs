using SeoulPlayup.CardCore;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    // 난수 커서(seed-determinism-handoff P5). 런 시드가 있는 전투는 스트림별 System.Random을 CountingRandom으로
    // 들고, 서스펜드 스냅샷에 「몇 칸 썼는가」를 싣는다. 재개는 같은 시드로 인스턴스를 새로 만들어 그만큼 되감는다 —
    // 그래야 k턴에 저장·재개한 판의 k+1턴 이후가 무중단 판과 같다. 무시드(runSeed == null)면 커서를 세지 않고
    // 모든 필드가 0으로 남는다(구세이브·테스트 폴백, 지금과 같은 동작).
    public sealed partial class CombatState
    {
        // 스트림 6·8·9. pushRng(5)는 CombatState.cs의 기존 필드, 계획기 RNG(4·4')는 MonsterAiPlanner가 소유한다.
        private CountingRandom bossPropRng;
        private CountingRandom movementShuffleRng;
        private CountingRandom actionShuffleRng;

        private CountingRandom CreateStreamRandom(int stream)
        {
            return new CountingRandom(RunSeedStreams.Derive(RunSeed.Value, stream));
        }

        /// <summary>
        /// 지금 커서. 스냅샷(<see cref="CreateSuspendSnapshot"/>)이 싣고, 테스트가 「복원 후 소비만 세는가」를 잰다.
        /// 아직 안 만든 인스턴스(예: 덱을 밖에서 받아 셔플 스트림을 연 적이 없음)는 0.
        /// </summary>
        public RngCursorsSaveData CaptureRngCursors()
        {
            return new RngCursorsSaveData
            {
                Judgement = (pushRng as CountingRandom)?.Consumed ?? 0,
                AttackPattern = planner.AttackPatternRngCursor,
                DamageJitter = planner.DamageJitterRngCursor,
                BossProps = bossPropRng?.Consumed ?? 0,
                MovementShuffle = movementShuffleRng?.Consumed ?? 0,
                ActionShuffle = actionShuffleRng?.Consumed ?? 0
            };
        }

        /// <summary>
        /// 일곱 인스턴스 중 이 상태가 소유한 여섯을 <b>다시 만들어</b> 저장된 커서까지 되감는다(보상 커서는 컨트롤러 몫).
        /// 생성자가 이미 소비한 칸(개시 셔플·첫 계획)은 새 인스턴스와 함께 버려진다 — 그래서 되감기 뒤 커서는
        /// 정확히 저장값이다. 덱 셔플 스트림은 <c>RestorePlayerFromSuspend</c>가 클로저에 잡기 <b>전에</b> 만들어야 한다.
        /// </summary>
        private void RestoreRngCursors(RngCursorsSaveData cursors)
        {
            if (!RunSeed.HasValue)
            {
                return;
            }

            cursors ??= new RngCursorsSaveData();
            pushRng = CreateStreamRandom(RunSeedStreams.CombatJudgement);
            ((CountingRandom)pushRng).FastForward(cursors.Judgement);
            planner.RestoreRngCursors(cursors.AttackPattern, cursors.DamageJitter);
            bossPropRng = CreateStreamRandom(RunSeedStreams.BossProps);
            bossPropRng.FastForward(cursors.BossProps);
            boss.ConfigureBossPropRandom(bossPropRng);
            movementShuffleRng = CreateStreamRandom(RunSeedStreams.MovementDeckShuffle);
            movementShuffleRng.FastForward(cursors.MovementShuffle);
            actionShuffleRng = CreateStreamRandom(RunSeedStreams.ActionDeckShuffle);
            actionShuffleRng.FastForward(cursors.ActionShuffle);
        }
    }
}

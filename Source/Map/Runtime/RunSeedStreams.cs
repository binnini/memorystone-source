namespace SeoulPlayup.Map.Runtime
{
    /// <summary>
    /// 런 시드에서 갈라지는 <b>난수 스트림 번호표</b>(seed-determinism-handoff §4 P1-3).
    ///
    /// <para>「같은 시드 → 같은 판」은 시드 하나를 여러 소비자가 <b>독립 스트림</b>으로 나눠 써야
    /// 성립한다 — 한 소비자의 굴림 횟수가 바뀌어도(밸런스 패치·카드 추가) 다른 소비자의
    /// 결과가 밀리지 않아야 하기 때문이다. 파생은 <see cref="PlacementRandomizer.DeriveSeed"/> 하나,
    /// 번호는 이 표 하나다. 번호가 코드 여기저기에 리터럴로 흩어지면 반드시 충돌한다.</para>
    ///
    /// <para>🔴 <b>append-only.</b> 기존 번호를 바꾸면 그 시드로 이미 공유된 판이 달라진다.
    /// 새 축은 마지막 번호 뒤에 잇는다.</para>
    ///
    /// <para>⚠️ 몬스터 배치(<see cref="MonsterPlacement"/>)는 파생 없이 <b>원시 시드</b>를 그대로 쓴다
    /// (P0~P1 시드 호환 — <c>HexMapPlacementRandomization</c>). 스폰 체력 변주는 서비스 배치와
    /// 같은 번호 3을 쓰지만 <c>spawnRefId</c>로 한 번 더 섞이므로(<c>CombatState.MixSpawnHpSeed</c>)
    /// 시퀀스는 겹치지 않는다 — 이 트랙 착수 전부터 그랬고, 바꾸면 기존 시드의 체력이 달라져 둔다.</para>
    /// </summary>
    public static class RunSeedStreams
    {
        /// <summary>몬스터 슬롯 셔플·풀 추첨. 파생 없이 원시 시드(번호는 문서용).</summary>
        public const int MonsterPlacement = 0;
        /// <summary>함정 슬롯 승격·프리셋 치환.</summary>
        public const int Traps = 1;
        /// <summary>상자 좌표 셔플.</summary>
        public const int Chests = 2;
        /// <summary>서비스 오브젝트(잡화점·캠핑카) 배치.</summary>
        public const int Services = 3;
        /// <summary>스폰 시 체력 변주(hpVariancePct). 3을 공유하되 spawnRefId로 재혼합.</summary>
        public const int SpawnHpVariance = 3;
        /// <summary>몬스터 공격 패턴 선택 + 피해 변주(<c>MonsterAiPlanner</c>).</summary>
        public const int MonsterAttackPattern = 4;
        /// <summary>전투 판정(<c>CombatState.pushRng</c>): 취약 부위·빠른 거북·순간이동지·전염 대상·되돌릴 부적·제거될 카드.</summary>
        public const int CombatJudgement = 5;
        /// <summary>보스 기물 볼리의 링 회전각.</summary>
        public const int BossProps = 6;
        /// <summary>보상·상점·뽑기·전리품(<c>IRewardRandom</c>).</summary>
        public const int Rewards = 7;
        /// <summary>이동덱 셔플. 행동덱과 갈라 둔다 — 한쪽 소비량이 다른 쪽을 밀면 안 된다.</summary>
        public const int MovementDeckShuffle = 8;
        /// <summary>행동덱 셔플.</summary>
        public const int ActionDeckShuffle = 9;

        /// <summary>런 시드에서 <paramref name="stream"/>번 스트림의 시드를 뽑는다.</summary>
        public static int Derive(int runSeed, int stream)
        {
            return PlacementRandomizer.DeriveSeed(runSeed, stream);
        }
    }
}

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>
    /// 출하 카드 id 상수(cards.csv `id` 열과 1:1). 규칙층·표현층이 「어느 카드인가」를 물을 때 쓰는 유일한 키 —
    /// behaviorId(<c>CardEffectRefs</c>)로 카드를 식별하던 비교는 P2-b에서 전부 여기로 왔다.
    /// <c>CardEffectRefs</c>는 효과 <b>발신 키</b>(VFX 큐·오디오·상태이상 sourceRef)로만 남는다.
    /// </summary>
    public static class CardIds
    {
        // ---- 이동
        /// <summary>M01 1칸 이동</summary>
        public const string Move1Hex = "M01";
        /// <summary>M02 2칸 이동</summary>
        public const string Move2Hex = "M02";
        /// <summary>M03 3칸 이동</summary>
        public const string Move3Hex = "M03";
        /// <summary>M04 4칸 이동</summary>
        public const string Move4Hex = "M04";
        /// <summary>M05 추진력</summary>
        public const string Momentum = "M05";
        /// <summary>M06 도착지를 모르는 여행</summary>
        public const string RandomJourney = "M06";
        /// <summary>M07 지름길</summary>
        public const string Shortcut = "M07";
        /// <summary>M08 전력 질주</summary>
        public const string FullSprint = "M08";

        // ---- 공격
        /// <summary>A00 공격의 기초</summary>
        public const string BasicStrike = "A00";
        /// <summary>A01 휘둘러치기</summary>
        public const string Sweep = "A01";
        /// <summary>A02 가속 타격</summary>
        public const string MoveLinkedStrike = "A02";
        /// <summary>A03 성스러운 빛</summary>
        public const string HolyLight = "A03";
        /// <summary>A04 몰아치는 공세</summary>
        public const string FinishingTouch = "A04";
        /// <summary>A05 최후의 일격</summary>
        public const string FinalBlow = "A05";
        /// <summary>A06 두 번 치기</summary>
        public const string DoubleHit = "A06";
        /// <summary>A07 일격이면 충분</summary>
        public const string OneStrikeEnough = "A07";
        /// <summary>A08 증식 공격</summary>
        public const string MultiplyingStrike = "A08";
        /// <summary>A09 증식 공격 복사본</summary>
        public const string MultiplyingStrikeCopy = "A09";
        /// <summary>A10 제물을 바쳐서</summary>
        public const string Sacrifice = "A10";
        /// <summary>A11 얼어버려라</summary>
        public const string TargetShot = "A11";
        /// <summary>A12 전염병</summary>
        public const string Plague = "A12";
        /// <summary>A13 잔혼 공격</summary>
        public const string Remnant = "A13";
        /// <summary>A14 으름장</summary>
        public const string Intimidate = "A14";

        // ---- 방어
        /// <summary>D00 방어의 기초</summary>
        public const string BasicBlock = "D00";
        /// <summary>D01 낡은 방어구</summary>
        public const string OldArmor = "D01";
        /// <summary>D02 보호구역 안에서 도발</summary>
        public const string ShelterTaunt = "D02";
        /// <summary>D03 양날의 방패</summary>
        public const string DoubleEdgedShield = "D03";
        /// <summary>D04 무거운 갑옷</summary>
        public const string HeavyArmor = "D04";
        /// <summary>D05 부적 방패</summary>
        public const string TalismanShield = "D05";
        /// <summary>D06 입원</summary>
        public const string Hospitalization = "D06";
        /// <summary>D07 만반의 준비</summary>
        public const string FullyPrepared = "D07";

        // ---- 정찰
        /// <summary>S00 정찰의 기초</summary>
        public const string BasicScout = "S00";
        /// <summary>S01 지뢰찾기</summary>
        public const string Minefinder = "S01";
        /// <summary>S02 보물찾기</summary>
        public const string Treasurefinder = "S02";
        /// <summary>S03 기절초광</summary>
        public const string StunFlash = "S03";
        /// <summary>S04 빙고!</summary>
        public const string Bingo = "S04";
        /// <summary>S05 돌 다리 두드리기</summary>
        public const string StoneBridgeTap = "S05";
        /// <summary>S06 약점 간파</summary>
        public const string WeakSpot = "S06";

        // ---- 필드
        /// <summary>F01 폭탄 투하</summary>
        public const string Firebomb = "F01";
        /// <summary>F02 신성한 램프</summary>
        public const string SacredLamp = "F02";
        /// <summary>F03 섬광</summary>
        public const string Flashbang = "F03";
        /// <summary>F04 흡수진</summary>
        public const string LifestealZone = "F04";
        /// <summary>F05 콩콩탄탄</summary>
        public const string BounceBomb = "F05";

        // ---- 유틸리티
        /// <summary>U01 다시 뽑기</summary>
        public const string Redraw = "U01";
        /// <summary>U02 부적 끌어오기</summary>
        public const string DrawOrRecover = "U02";
        /// <summary>U03 정화 뽑기</summary>
        public const string CleanseDraw = "U03";
        /// <summary>U04 호롱불</summary>
        public const string Torch = "U04";

        // ---- 저주
        /// <summary>X01 미세먼지</summary>
        public const string FineDust = "X01";
        /// <summary>X02 깨진 유리</summary>
        public const string BrokenGlass = "X02";
        /// <summary>X03 정전</summary>
        public const string Blackout = "X03";
        /// <summary>X04 빚 문서</summary>
        public const string DebtNote = "X04";
        /// <summary>X05 악몽</summary>
        public const string Nightmare = "X05";
        /// <summary>X06 미련</summary>
        public const string Lingering = "X06";
        /// <summary>X07 지각</summary>
        public const string Tardiness = "X07";
        /// <summary>X08 골칫거리</summary>
        public const string Nuisance = "X08";
        /// <summary>X09 부정 탄 부적</summary>
        public const string CursedCharm = "X09";
        /// <summary>X10 궂은 안개</summary>
        public const string MurkyFog = "X10";
        /// <summary>X11 도깨비 장난</summary>
        public const string GoblinPrank = "X11";
        /// <summary>X12 원귀</summary>
        public const string VengefulGhost = "X12";
    }
}

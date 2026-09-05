namespace SeoulPlayup.Combat.Runtime
{
    public static class CombatCardStatusText
    {
        public const string CombatEnded = "전투 종료";
        public const string Usable = "사용 가능";
        public const string AttackOutOfRange = "사거리 부족";
        public const string NotEnoughKi = "기력 부족";
        public const string Stunned = "기절";
        public const string Immobilized = "속박";
        public const string Disarmed = "무장 해제";
        public const string StatusCard = "사용 불가";
        public const string Sealed = "봉인";
        public const string MomentumLocked = "추진력";
        public const string Waiting = "대기 중";

        public static bool IsAttackOutOfRange(string status)
        {
            return string.Equals(status, AttackOutOfRange, System.StringComparison.Ordinal);
        }
    }
}

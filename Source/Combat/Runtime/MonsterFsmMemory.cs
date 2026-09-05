using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    public sealed class MonsterFsmMemory
    {
        public MonsterFsmState State { get; set; } = MonsterFsmState.Patrol;
        public MonsterFsmState PreAlertState { get; set; } = MonsterFsmState.Patrol;
        public HexCoord? LastKnownPlayerCoord { get; set; }
        public int SearchTurnsRemaining { get; set; }
        public int AlertTurnsRemaining { get; set; }
        public int AlertRangeBonus { get; set; }
        public int PatrolCursor { get; set; }

        public MonsterFsmMemory Clone()
        {
            return new MonsterFsmMemory
            {
                State = State,
                PreAlertState = PreAlertState,
                LastKnownPlayerCoord = LastKnownPlayerCoord,
                SearchTurnsRemaining = SearchTurnsRemaining,
                AlertTurnsRemaining = AlertTurnsRemaining,
                AlertRangeBonus = AlertRangeBonus,
                PatrolCursor = PatrolCursor
            };
        }
    }
}

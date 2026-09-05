using System;

namespace SeoulPlayup.Map.Runtime
{
    [Serializable]
    public sealed class HexCellRuntimeState
    {
        public HexCellRuntimeState(string occupyingUnitId = null, bool temporaryBlocked = false, bool revealed = false, bool visited = false)
        {
            OccupyingUnitId = occupyingUnitId ?? string.Empty;
            TemporaryBlocked = temporaryBlocked;
            Revealed = revealed;
            Visited = visited;
        }

        public string OccupyingUnitId { get; set; }
        public bool TemporaryBlocked { get; set; }
        public bool Revealed { get; set; }
        public bool Visited { get; set; }
    }
}

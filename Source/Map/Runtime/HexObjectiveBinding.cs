using System;

namespace SeoulPlayup.Map.Runtime
{
    public readonly struct HexObjectiveBinding : IEquatable<HexObjectiveBinding>
    {
        public HexObjectiveBinding(
            string objectiveId,
            string landmarkId,
            string displayName = null,
            string requiredAction = null,
            int investigateRange = 1)
        {
            ObjectiveId = objectiveId ?? string.Empty;
            LandmarkId = landmarkId ?? string.Empty;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? LandmarkId : displayName;
            RequiredAction = string.IsNullOrWhiteSpace(requiredAction) ? "Investigate" : requiredAction;
            InvestigateRange = Math.Max(0, investigateRange);
        }

        public string ObjectiveId { get; }
        public string LandmarkId { get; }
        public string DisplayName { get; }
        public string RequiredAction { get; }
        public int InvestigateRange { get; }
        public bool IsConfigured => !string.IsNullOrWhiteSpace(LandmarkId);

        public bool Equals(HexObjectiveBinding other)
        {
            return ObjectiveId == other.ObjectiveId &&
                   LandmarkId == other.LandmarkId &&
                   DisplayName == other.DisplayName &&
                   RequiredAction == other.RequiredAction &&
                   InvestigateRange == other.InvestigateRange;
        }

        public override bool Equals(object obj)
        {
            return obj is HexObjectiveBinding other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = ObjectiveId != null ? ObjectiveId.GetHashCode() : 0;
                hashCode = (hashCode * 397) ^ (LandmarkId != null ? LandmarkId.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (DisplayName != null ? DisplayName.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (RequiredAction != null ? RequiredAction.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ InvestigateRange;
                return hashCode;
            }
        }
    }
}

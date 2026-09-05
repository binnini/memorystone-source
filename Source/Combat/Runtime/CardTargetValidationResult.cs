namespace SeoulPlayup.Combat.Runtime
{
    public readonly struct CardTargetValidationResult
    {
        private CardTargetValidationResult(bool isValid, string failureReason)
        {
            IsValid = isValid;
            FailureReason = failureReason ?? string.Empty;
        }

        public bool IsValid { get; }
        public string FailureReason { get; }

        public static CardTargetValidationResult Success()
        {
            return new CardTargetValidationResult(true, string.Empty);
        }

        public static CardTargetValidationResult Failure(string reason)
        {
            return new CardTargetValidationResult(false, string.IsNullOrWhiteSpace(reason) ? "Target is invalid." : reason);
        }
    }
}

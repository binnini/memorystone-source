#nullable enable
using System.Collections.Generic;

namespace AIGD
{
    // Projection of SeoulPlayup.CardCore.CardCatalogBindingEvidence for JSON return.
    public sealed class CardBindingEvidence
    {
        public bool isValid;
        public string failureReason = string.Empty;
        public List<string> moveDeckCardIds = new();
        public List<string> actionDeckCardIds = new();
    }
}

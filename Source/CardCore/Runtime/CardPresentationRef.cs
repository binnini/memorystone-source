using System;

namespace SeoulPlayup.CardCore
{
    public readonly struct CardPresentationRef
    {
        public const string PlaceholderPresentationId = "card.presentation.placeholder";
        public const string PlaceholderFrameId = "card.frame.placeholder";
        public const string PlaceholderIconId = "card.icon.placeholder";
        public const string PlaceholderVfxId = "card.vfx.placeholder";
        public const string PlaceholderSfxId = "card.sfx.placeholder";

        public CardPresentationRef(
            string presentationId,
            string frameId = "",
            string iconId = "",
            string illustrationId = "",
            string vfxId = "",
            string sfxId = "")
        {
            PresentationId = string.IsNullOrWhiteSpace(presentationId) ? PlaceholderPresentationId : presentationId;
            FrameId = string.IsNullOrWhiteSpace(frameId) ? PlaceholderFrameId : frameId;
            IconId = string.IsNullOrWhiteSpace(iconId) ? PlaceholderIconId : iconId;
            IllustrationId = illustrationId ?? string.Empty;
            VfxId = string.IsNullOrWhiteSpace(vfxId) ? PlaceholderVfxId : vfxId;
            SfxId = string.IsNullOrWhiteSpace(sfxId) ? PlaceholderSfxId : sfxId;
        }

        public string PresentationId { get; }
        public string FrameId { get; }
        public string IconId { get; }
        public string IllustrationId { get; }
        public string VfxId { get; }
        public string SfxId { get; }

        public bool UsesPlaceholderFrame => string.Equals(FrameId, PlaceholderFrameId, StringComparison.Ordinal);

        public static CardPresentationRef Placeholder(string presentationId = "")
        {
            return new CardPresentationRef(presentationId);
        }
    }
}

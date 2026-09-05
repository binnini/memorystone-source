using System.Runtime.CompilerServices;

// Preserves EditMode test access to internal members (e.g. CardLaneStatusEffectDockView.HoverRelay,
// StatusEffectIconCatalog.CreateForTests) now that these types live in SeoulPlayup.Cards.Unity
// instead of SeoulPlayup.Combat (Stage 3 asmdef split).
[assembly: InternalsVisibleTo("SeoulPlayup.Combat.EditModeTests")]

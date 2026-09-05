using System.Runtime.CompilerServices;

// CombatAudioPresenter's internal helpers (MapEffectToCueIds, *ForTests) were accessible
// from the Combat presentation layer and EditMode tests while audio lived in the single
// SeoulPlayup.Combat assembly. Preserve that access after extracting SeoulPlayup.Audio.
[assembly: InternalsVisibleTo("SeoulPlayup.Combat")]
[assembly: InternalsVisibleTo("SeoulPlayup.Combat.EditModeTests")]
// Dev tooling (CardPresentationScenarioPlayer) uses the internal MapEffectToCueIds helper.
[assembly: InternalsVisibleTo("SeoulPlayup.Dev")]

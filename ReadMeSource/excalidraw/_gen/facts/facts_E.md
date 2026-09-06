All data confirmed. Here is the verified fact sheet.

E1. CombatState.RefreshPlayerVision — file `Source/Combat/Runtime/CombatState.cs`, method at line 4382-4436 (partial class file `Source/Combat/Runtime/CombatState.cs`; `CombatState` is split across many partials but this method itself lives in `CombatState.cs`).

Three sources merged into `revealed` (a `HashSet<HexCoord>`), each then fed into one call to `visibilityRuntime.RefreshTemporaryRevealedCells(revealed)` at line 4422:

- Vision radius (player sight), lines 4388-4396:
```
var visionRange = GetEffectivePlayerVisionRange();
var revealed = new HashSet<HexCoord>();
foreach (var cell in Map.AllCells)
{
    if (PlayerCoord.DistanceTo(cell.Coord) <= visionRange)
    {
        revealed.Add(cell.Coord);
    }
}
```

- Field objects, lines 4398-4412:
```
foreach (var fieldObject in FieldObjects.Objects)
{
    if (fieldObject.IsExpired)
    {
        continue;
    }

    foreach (var cell in Map.AllCells)
    {
        if (fieldObject.Contains(cell.Coord))
        {
            revealed.Add(cell.Coord);
        }
    }
}
```

- Scouting (정찰), lines 4414-4420:
```
// Scout reveals from this turn are a live source too, so a scouted tile holds Revealed until the
// turn ends (scoutRevealedThisTurn is cleared in BeginNextOverallTurn) instead of decaying on the
// next refresh.
foreach (var coord in scoutRevealedThisTurn)
{
    revealed.Add(coord);
}
```

Then merge call, line 4422: `visibilityRuntime.RefreshTemporaryRevealedCells(revealed);`

Additional note: `RecordScoutRevealedCells` (lines 4441-4455) is what populates `scoutRevealedThisTurn` from Scout card resolution.

E2. `Source/Map/Runtime/HexVisibilityRuntime.cs`

Four sets/dict, all fields declared lines 8-27:
- `states` — `Dictionary<HexCoord, HexCellVisibility>`, line 8.
- `temporaryRevealed` — `HashSet<HexCoord>`, line 9.
- `permanentlyRevealed` — `HashSet<HexCoord>`, line 21.
- `trapRevealed` — `HashSet<HexCoord>`, line 27.

`SetVisibility` (lines 163-189) — monotonic promotion only, quote (lines 182-188):
```
if (states.TryGetValue(coord, out var current) && current >= visibility)
{
    return;
}

states[coord] = visibility;
Version++;
```

`ForceVisibility` (private, lines 347-373) — allows demotion (no `>=` guard, only an equality short-circuit), quote (lines 366-372):
```
if (states.TryGetValue(coord, out var existing) && existing == visibility)
{
    return;
}

states[coord] = visibility;
Version++;
```
Comment at lines 48-51 confirms the monotonic-vs-demotion distinction: "SetVisibility is monotonic (it never lowers a cell below its current level)... a clean restore of persisted fog must reset first."

Visibility enum values — file `Source/Map/Runtime/HexCellVisibility.cs`:
```
public enum HexCellVisibility
{
    Unknown,
    Hinted,
    Revealed
}
```

E3. `Source/Map/Runtime/HexVisibilitySafeCellInfo.cs`

Struct fields (properties, lines 37-55): `Coord` (HexCoord), `Visibility` (HexCellVisibility), `Exists` (bool), `ExposesTerrain` (bool), `ExposesFullDetails` (bool), `TileDefinitionId` (string), `TerrainTypeId` (string), `BaseMoveCost` (int), `BaseWalkable` (bool), `BaseBlocksVision` (bool, noted unused by any consumer per comment lines 46-47), `EventId` (string), `LandmarkId` (string), `VisualFloor` (int), `TrapRevealed` (bool).

`GetSafeCellInfo` location: `Source/Map/Runtime/HexVisibilityRuntime.cs`, lines 375-423 (method defined on `HexVisibilityRuntime`, not on the SafeCellInfo file itself).

Per-state filtering logic inside `GetSafeCellInfo`:
- Cell missing from map → `HexVisibilitySafeCellInfo.Missing(coord)` (line 379).
- `Unknown` visibility → `HexVisibilitySafeCellInfo.Unknown(coord)` (lines 383-386).
- `Hinted` visibility (lines 389-406) → returns info with `exposesFullDetails: false`, `tileDefinitionId: string.Empty`, terrain/move/walkable exposed, but `eventId`/`landmarkId` blanked out (`string.Empty`).
- `Revealed` (or higher; default fallthrough, lines 408-422) → full details exposed: `exposesFullDetails: true`, real `TileDefinitionId`, `EventId`, `LandmarkId`.
- `isTrapRevealed` (line 388) is computed independently from the `trapRevealed` set and threaded through regardless of visibility tier (orthogonal to fog, per the class-level comment at lines 23-26).

Consumers confirmed:
- `CombatVisibilityPresenter` (tooltip) — `Source/Combat/Unity/CombatVisibilityPresenter.cs`: `GetSafeCellInfo(...)` method header line 8, delegates to `state.GetVisibilitySafeCellInfo(coord)` at line 19; `GetTooltipText(HexVisibilitySafeCellInfo info)` at line 22.
- Minimap class — `TacticalMinimapView` in `Source/Combat/Unity/TacticalMinimapView.cs` (confirmed via grep match on class name).
- `MapObjectVisualRegistry.ShouldShowForVisibility` — `Source/Map/Unity/MapObjectVisualRegistry.cs`, line 424: `private bool ShouldShowForVisibility(MapObjectVisualEntry entry, out HexCellVisibility bestVisibility)`.

E4. `Source/Map/Unity/VisibilityLightingMaskService.cs`

State → mask mapping: `VisibilityLightingLevels.Resolve(HexCellVisibility)` (lines 28-39) maps `Revealed`→`Revealed` brightness, `Hinted`→`Hinted` brightness, default (`Unknown`)→`Unknown` brightness. This is consumed per-cell in `Update()` at lines 209-213:
```
var lighting = safeInfo.Exists
    ? levels.Resolve(safeInfo.Visibility)
    : levels.Unknown;
bySlot[slot] = LightingToByte(lighting);
```

Epoch/delta early-out ("epoch early-out" = the `contentValid`/delta-skip mechanism), lines 219-230, quote:
```
var lightingChanged = !contentValid;
if (!lightingChanged)
{
    for (var slot = 0; slot < bySlot.Length; slot++)
    {
        if (bySlot[slot] != bySlotPrev[slot])
        {
            lightingChanged = true;
            break;
        }
    }
}
```
(Class doc, lines 104-111, calls this "delta skip": "if no slot's byte changed since the last upload, fill+blur+upload are skipped entirely. `contentValid` starts false so every invalidation forces a full rebuild.")

Shader property it sets: `_SP_VisibilityMask` — `Shader.SetGlobalTexture(MaskTexturePropertyId, maskTexture);` at line 267, with `MaskTexturePropertyId = Shader.PropertyToID("_SP_VisibilityMask")` declared at line 118. (It also sets several companion globals: `_SP_VisibilityWorldToLocal`, `_SP_VisibilityMaskBounds`, `_SP_VisibilityMaskEnabled`, `_SP_VisibilityEmissionFloor`, `_SP_VisibilityHardEdge`, `_SP_VisibilityLightingLow/High`, `_SP_VisibilityCellSnap`, `_SP_VisibilityTileRadius`, lines 118-127, 267-285.)

E5. `Source/Shaders/MapVisibilityLit.shader`

Confirmed URP Lit variant: Tags at lines 59-65 —
```
Tags
{
    "RenderType" = "Opaque"
    "RenderPipeline" = "UniversalPipeline"
    "UniversalMaterialType" = "Lit"
    "IgnoreProjector" = "True"
}
```
It also includes `LitInput.hlsl`/`LitForwardPass.hlsl` (line 131-132) and `UsePass "Universal Render Pipeline/Lit/ShadowCaster"` etc. (lines 263-266), confirming it's a Lit-pipeline derivative, not a from-scratch unlit shader.

World-position mask sampling — `SampleVisibilityLighting(float3 positionWS)`, lines 174-191, quote (176-182):
```
float3 mapLocal = mul(_SP_VisibilityWorldToLocal, float4(positionWS, 1.0)).xyz;
float2 sampleXZ = _SP_VisibilityCellSnap > 0.5 && _SP_VisibilityTileRadius > 0.0001
    ? SnapToHexCellCenter(mapLocal.xz, _SP_VisibilityTileRadius)
    : mapLocal.xz;
float2 uv = (sampleXZ - _SP_VisibilityMaskBounds.xy) * _SP_VisibilityMaskBounds.zw;
half inside = step(0.0, uv.x) * step(0.0, uv.y) * step(uv.x, 1.0) * step(uv.y, 1.0);
half mask = SAMPLE_TEXTURE2D(_SP_VisibilityMask, sampler_SP_VisibilityMask, saturate(uv)).r;
```
Called at line 232: `half visibilityLighting = SampleVisibilityLighting(inputData.positionWS);`

NaN scrub, lines 239-252, quote:
```
// Sanitize the HDR output before it reaches the post pipeline. In LightingMask mode
// this shader is swapped onto tiles AND runtime-generated side-chunk / fog meshes,
// some of which carry degenerate normals/tangents that make the PBR light loop emit
// NaN/Inf. A single non-finite texel is invisible without post, but Bloom scatters it
// across the entire frame → full-screen white blow-out.
//
// Scrub via hardware min/max rather than a comparison test: the shader compiler assumes
// no-NaN and folds "(x >= 0 || x < 0)" to a constant true, defeating a comparison-based
// guard, whereas min/max are spec-defined to return the non-NaN operand (so max(NaN,0)=0)
// and cannot be folded away. max flushes NaN and negatives to 0; min caps +Inf and any
// pathological magnitude at a generous ceiling that still preserves real bright/bloom
// sources.
color.rgb = max(color.rgb, half3(0.0h, 0.0h, 0.0h));
color.rgb = min(color.rgb, half3(64.0h, 64.0h, 64.0h));
```

E6. First 32 lines of `GetSafeCellInfo`, verbatim with source line numbers (`Source/Map/Runtime/HexVisibilityRuntime.cs`, lines 375-406):

```
375	        public HexVisibilitySafeCellInfo GetSafeCellInfo(HexCoord coord)
376	        {
377	            if (!Map.TryGetCell(coord, out var cell))
378	            {
379	                return HexVisibilitySafeCellInfo.Missing(coord);
380	            }
381	
382	            var visibility = GetVisibility(coord);
383	            if (visibility == HexCellVisibility.Unknown)
384	            {
385	                return HexVisibilitySafeCellInfo.Unknown(coord);
386	            }
387	
388	            var isTrapRevealed = trapRevealed.Contains(coord);
389	            if (visibility == HexCellVisibility.Hinted)
390	            {
391	                return new HexVisibilitySafeCellInfo(
392	                    coord,
393	                    visibility,
394	                    true,
395	                    true,
396	                    false,
397	                    string.Empty,
398	                    cell.TerrainTypeId,
399	                    cell.BaseMoveCost,
400	                    cell.BaseWalkable,
401	                    cell.BaseBlocksVision,
402	                    string.Empty,
403	                    string.Empty,
404	                    cell.VisualFloor,
405	                    isTrapRevealed);
406	            }
```

Note: the method body from line 375 to its closing brace at line 423 is 49 lines total; the block above (375-406) is the first 32 lines of the method body as requested.

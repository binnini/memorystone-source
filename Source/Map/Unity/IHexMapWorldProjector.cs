using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Map.Unity
{
    /// <summary>
    /// Minimal map geometry seam for systems that need to draw above hex cells
    /// without depending on the full tile presentation implementation.
    /// Coordinates are map-space <see cref="HexCoord"/> values; positions are
    /// local to the map presentation root, matching <see cref="AtlasTilePresentationView"/>.
    /// </summary>
    public interface IHexMapWorldProjector
    {
        float TileRadius { get; }
        Vector3 Project(HexCoord coord);
        Vector3 ProjectTop(HexCoord coord);
    }

    /// <summary>
    /// Optional extension for presentation views whose visible tile mesh surface
    /// sits above the map cell elevation returned by <see cref="IHexMapWorldProjector.ProjectTop"/>.
    /// Overlay renderers use this when available so batched geometry is drawn
    /// above the actual visual tile surface rather than inside the tile prefab.
    /// </summary>
    public interface IHexMapOverlaySurfaceProjector
    {
        Vector3 ProjectOverlaySurface(HexCoord coord);
    }
}

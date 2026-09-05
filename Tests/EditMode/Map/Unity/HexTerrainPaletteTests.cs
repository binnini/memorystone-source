using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Map.Unity;
using UnityEngine;

namespace SeoulPlayup.Map.Unity.Tests.EditMode
{
    public sealed class HexTerrainPaletteTests
    {
        [Test]
        public void TerrainTypeIdsReturnsTrimmedNonEmptyEntries()
        {
            var palette = ScriptableObject.CreateInstance<HexTerrainPalette>();
            try
            {
                palette.ConfigureForTests(new[]
                {
                    new HexTerrainPalette.Entry(" guide-empty "),
                    new HexTerrainPalette.Entry(string.Empty),
                    new HexTerrainPalette.Entry("hanriver-water")
                });

                Assert.That(palette.TerrainTypeIds.ToArray(), Is.EqualTo(new[] { "guide-empty", "hanriver-water" }));
                Assert.That(palette.ContainsTerrainTypeId("GUIDE-EMPTY"), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(palette);
            }
        }
    }
}


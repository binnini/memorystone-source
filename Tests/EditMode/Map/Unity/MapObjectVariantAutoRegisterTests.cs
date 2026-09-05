using NUnit.Framework;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.MapDesign.Editor;
using UnityEngine;

namespace SeoulPlayup.Map.Unity.Tests.EditMode
{
    /// <summary>
    /// Covers the catalog side of the folder-drop auto-registration: adding a new entry, no-op on repeat,
    /// prefab-ref refresh, and category → catalog resolution. The AssetPostprocessor that drives this on
    /// import is thin glue over these methods.
    /// </summary>
    public sealed class MapObjectVariantAutoRegisterTests
    {
        [Test]
        public void AutoRegisterAddsNewEntryWithCatalogType()
        {
            var catalog = ScriptableObject.CreateInstance<MapObjectTypedCatalog>();
            catalog.ConfigureForTests(HexMapObjectType.Building, System.Array.Empty<RuntimeMapObjectPrefabCatalog.Entry>(), "Building");
            var prefab = new GameObject("variant_a");
            try
            {
                var added = MapObjectPrefabCatalog.AutoRegister(catalog, "variant_a", prefab, out var message);

                Assert.That(added, Is.True);
                Assert.That(message, Does.Contain("registered"));
                Assert.That(catalog.TryFindEntry("variant_a", out var entry), Is.True);
                Assert.That(entry.Prefab, Is.SameAs(prefab));
                Assert.That(entry.ObjectType, Is.EqualTo(HexMapObjectType.Building));
            }
            finally
            {
                Object.DestroyImmediate(prefab);
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void AutoRegisterIsNoOpWhenPrefabUnchanged()
        {
            var catalog = ScriptableObject.CreateInstance<MapObjectTypedCatalog>();
            catalog.ConfigureForTests(HexMapObjectType.Building, System.Array.Empty<RuntimeMapObjectPrefabCatalog.Entry>(), "Building");
            var prefab = new GameObject("variant_b");
            try
            {
                MapObjectPrefabCatalog.AutoRegister(catalog, "variant_b", prefab, out _);
                var second = MapObjectPrefabCatalog.AutoRegister(catalog, "variant_b", prefab, out _);

                Assert.That(second, Is.False, "re-registering the same prefab should not report a change");
                Assert.That(catalog.Entries, Has.Count.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(prefab);
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void AutoRegisterRefreshesPrefabReferenceOnChange()
        {
            var catalog = ScriptableObject.CreateInstance<MapObjectTypedCatalog>();
            catalog.ConfigureForTests(HexMapObjectType.Building, System.Array.Empty<RuntimeMapObjectPrefabCatalog.Entry>(), "Building");
            var first = new GameObject("variant_c");
            var second = new GameObject("variant_c_replacement");
            try
            {
                MapObjectPrefabCatalog.AutoRegister(catalog, "variant_c", first, out _);
                var changed = MapObjectPrefabCatalog.AutoRegister(catalog, "variant_c", second, out _);

                Assert.That(changed, Is.True);
                Assert.That(catalog.Entries, Has.Count.EqualTo(1));
                Assert.That(catalog.TryFindEntry("variant_c", out var entry), Is.True);
                Assert.That(entry.Prefab, Is.SameAs(second));
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void ResolveCatalogByCategoryMatchesCategoryLabelCaseInsensitively()
        {
            var building = ScriptableObject.CreateInstance<MapObjectTypedCatalog>();
            building.ConfigureForTests(HexMapObjectType.Building, System.Array.Empty<RuntimeMapObjectPrefabCatalog.Entry>(), "Building");
            var prop = ScriptableObject.CreateInstance<MapObjectTypedCatalog>();
            prop.ConfigureForTests(HexMapObjectType.Building, System.Array.Empty<RuntimeMapObjectPrefabCatalog.Entry>(), "Prop");
            var set = ScriptableObject.CreateInstance<MapObjectCatalogSet>();
            set.ConfigureForTests(new[] { building, prop });
            try
            {
                Assert.That(MapObjectPrefabCatalog.ResolveCatalogByCategory(set, "prop"), Is.SameAs(prop));
                Assert.That(MapObjectPrefabCatalog.ResolveCatalogByCategory(set, "Building"), Is.SameAs(building));
                Assert.That(MapObjectPrefabCatalog.ResolveCatalogByCategory(set, "Nope"), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(building);
                Object.DestroyImmediate(prop);
                Object.DestroyImmediate(set);
            }
        }
    }
}

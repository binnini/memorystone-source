using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class CombatCardVfxCsvConverterTests
    {
        [Category("ShippingData")]
        [Test]
        public void ConvertsDesignerCardVfxCsv()
        {
            var cues = CombatCardVfxCsvConverter.ConvertFile(CombatCsvPaths.CardVfxCuesCsv);

            Assert.That(cues, Is.Not.Empty);
            Assert.That(cues.Select(cue => cue.CueId), Does.Contain("CVA01"));

            var sweep = cues.Single(cue => cue.CueId == "CVA01");
            Assert.That(sweep.CardId, Is.EqualTo("A01"));
            Assert.That(sweep.EffectRef, Is.EqualTo("attack.damage"));
            Assert.That(sweep.EffectKind, Is.EqualTo(EffectKind.Damage));
            Assert.That(sweep.TargetFilter, Is.EqualTo("Monster"));
            Assert.That(sweep.SourceRef, Is.EqualTo("A01"));
        }

        [Test]
        public void ParsesPlaybackSpeedAndDefaultsToOneWhenUnset()
        {
            var source =
                "\"cueId\",\"cardId\",\"effectKind\",\"targetFilter\",\"prefabPath\",\"playbackSpeed\"\n" +
                "\"FAST\",\"A01\",\"Damage\",\"Monster\",\"Assets/Fast.prefab\",\"1.5\"\n" +
                "\"PLAIN\",\"A02\",\"Damage\",\"Monster\",\"Assets/Plain.prefab\",\"\"\n";
            var cues = CombatCardVfxCsvConverter.Convert(source);

            Assert.That(cues.Single(cue => cue.CueId == "FAST").PlaybackSpeed, Is.EqualTo(1.5f));
            Assert.That(cues.Single(cue => cue.CueId == "PLAIN").PlaybackSpeed, Is.EqualTo(1f),
                "An empty cell must fall back to the prefab's authored speed (1x).");

            var withoutColumn = CombatCardVfxCsvConverter.Convert(
                "\"cueId\",\"cardId\",\"effectKind\",\"targetFilter\",\"prefabPath\"\n" +
                "\"OLD\",\"A03\",\"Damage\",\"Monster\",\"Assets/Old.prefab\"\n");
            Assert.That(withoutColumn.Single().PlaybackSpeed, Is.EqualTo(1f),
                "A CSV predating the column must keep parsing with 1x playback.");
        }

        [Test]
        public void ParsesAxisScaleAndDefaultsToUniformWhenUnset()
        {
            var source =
                "\"cueId\",\"cardId\",\"effectKind\",\"targetFilter\",\"prefabPath\",\"scaleX\",\"scaleY\",\"scaleZ\"\n" +
                "\"WIDE\",\"A01\",\"Damage\",\"Monster\",\"Assets/Wide.prefab\",\"2\",\"\",\"0.5\"\n" +
                "\"PLAIN\",\"A02\",\"Damage\",\"Monster\",\"Assets/Plain.prefab\",\"\",\"\",\"\"\n";
            var cues = CombatCardVfxCsvConverter.Convert(source);

            var wide = cues.Single(cue => cue.CueId == "WIDE");
            Assert.That(wide.ScaleX, Is.EqualTo(2f));
            Assert.That(wide.ScaleY, Is.EqualTo(1f), "An empty axis cell must stay uniform (1x).");
            Assert.That(wide.ScaleZ, Is.EqualTo(0.5f));

            var plain = cues.Single(cue => cue.CueId == "PLAIN");
            Assert.That((plain.ScaleX, plain.ScaleY, plain.ScaleZ), Is.EqualTo((1f, 1f, 1f)),
                "All-empty axis cells must keep the cue fully uniform.");
        }

        [Category("ShippingData")]
        [Test]
        public void DesignerCardVfxCsvReferencesExistingPrefabPaths()
        {
            var cues = CombatCardVfxCsvConverter.ConvertFile(CombatCsvPaths.CardVfxCuesCsv);

            foreach (var cue in cues)
            {
                Assert.That(File.Exists(cue.PrefabPath), Is.True, $"{cue.CueId}: {cue.PrefabPath}");
            }
        }

        [Test]
        public void TuningCsvWriterUpdatesOnlyTuningAndSpawnAnchorCellsThenRoundTrips()
        {
            var source =
                "\"cueId\",\"cardId\",\"effectRef\",\"effectKind\",\"targetFilter\",\"prefabPath\",\"scaleMultiplier\",\"scaleWithRadius\",\"offsetX\",\"offsetY\",\"offsetZ\",\"rotationX\",\"rotationY\",\"rotationZ\",\"lifetimeOverride\",\"sourceRef\",\"matchSourceRefPrefix\",\"spawnAnchor\",\"designerNote\",\"floatingTextMode\",\"floatingTextOverride\",\"delaySeconds\"\n" +
                "\"KEEP\",\"A00\",\"attack.keep\",\"Damage\",\"Monster\",\"Assets/Keep.prefab\",\"1\",\"true\",\"0\",\"0\",\"0\",\"0\",\"0\",\"0\",\"0\",\"A00\",\"false\",\"Auto\",\"unchanged\",\"Auto\",\"\",\"0\"\n" +
                "\"TUNE\",\"A01\",\"attack.tune\",\"Damage\",\"Monster\",\"Assets/Tune.prefab\",\"1\",\"true\",\"0\",\"0\",\"0\",\"0\",\"0\",\"0\",\"0\",\"A01\",\"false\",\"Auto\",\"keep note\",\"Show\",\"quoted \"\"text\"\" stays\",\"0\"\n";
            var path = Path.Combine(Path.GetTempPath(), $"combat_card_vfx_cues_{Guid.NewGuid():N}.csv");
            try
            {
                File.WriteAllText(path, source);

                CombatCardVfxCueTuningCsvWriter.UpdateFile(
                    path,
                    "TUNE",
                    new CombatCardVfxCueTuningUpdate(1.25f, false, 0.1f, 0.2f, 0.3f, 10f, 20f, 30f, 2.5f, "SourceAttack", 0.35f));

                var written = File.ReadAllText(path);
                Assert.That(File.Exists(path + ".bak"), Is.True, "A reversible .bak copy should be written before save.");
                Assert.That(written.Split('\n')[1], Is.EqualTo(source.Split('\n')[1]), "Non-target rows should remain byte-identical within the same newline style.");

                var cue = CombatCardVfxCsvConverter.ConvertFile(path).Single(item => item.CueId == "TUNE");
                Assert.That(cue.ScaleMultiplier, Is.EqualTo(1.25f));
                Assert.That(cue.ScaleWithRadius, Is.False);
                Assert.That(cue.OffsetX, Is.EqualTo(0.1f));
                Assert.That(cue.OffsetY, Is.EqualTo(0.2f));
                Assert.That(cue.OffsetZ, Is.EqualTo(0.3f));
                Assert.That(cue.RotationX, Is.EqualTo(10f));
                Assert.That(cue.RotationY, Is.EqualTo(20f));
                Assert.That(cue.RotationZ, Is.EqualTo(30f));
                Assert.That(cue.LifetimeOverride, Is.EqualTo(2.5f));
                Assert.That(cue.SpawnAnchor, Is.EqualTo("SourceAttack"));
                Assert.That(cue.DelaySeconds, Is.EqualTo(0.35f));
                Assert.That(cue.DesignerNote, Is.EqualTo("keep note"));
                Assert.That(cue.FloatingTextOverride, Is.EqualTo("quoted \"text\" stays"));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
            }
        }

        [Test]
        public void TuningCsvWriterUpdatesPrefabPathOnlyWhenProvided()
        {
            var source =
                "\"cueId\",\"cardId\",\"effectRef\",\"effectKind\",\"targetFilter\",\"prefabPath\",\"scaleMultiplier\",\"scaleWithRadius\",\"offsetX\",\"offsetY\",\"offsetZ\",\"rotationX\",\"rotationY\",\"rotationZ\",\"lifetimeOverride\",\"sourceRef\",\"matchSourceRefPrefix\",\"spawnAnchor\",\"designerNote\",\"floatingTextMode\",\"floatingTextOverride\",\"delaySeconds\"\n" +
                "\"KEEP\",\"A00\",\"attack.keep\",\"Damage\",\"Monster\",\"Assets/Keep.prefab\",\"1\",\"true\",\"0\",\"0\",\"0\",\"0\",\"0\",\"0\",\"0\",\"A00\",\"false\",\"Auto\",\"unchanged\",\"Auto\",\"\",\"0\"\n" +
                "\"TUNE\",\"A01\",\"attack.tune\",\"Damage\",\"Monster\",\"Assets/Tune.prefab\",\"1\",\"true\",\"0\",\"0\",\"0\",\"0\",\"0\",\"0\",\"0\",\"A01\",\"false\",\"Auto\",\"keep note\",\"Auto\",\"\",\"0\"\n";
            var path = Path.Combine(Path.GetTempPath(), $"combat_card_vfx_cues_{Guid.NewGuid():N}.csv");
            try
            {
                File.WriteAllText(path, source);

                CombatCardVfxCueTuningCsvWriter.UpdateFile(
                    path,
                    "TUNE",
                    new CombatCardVfxCueTuningUpdate(1f, true, 0f, 0f, 0f, 0f, 0f, 0f, 0f, "Auto", 0f, prefabPath: "Assets/Repointed.prefab"));

                var cue = CombatCardVfxCsvConverter.ConvertFile(path).Single(item => item.CueId == "TUNE");
                Assert.That(cue.PrefabPath, Is.EqualTo("Assets/Repointed.prefab"));
                Assert.That(File.ReadAllText(path).Split('\n')[1], Is.EqualTo(source.Split('\n')[1]), "Non-target rows should remain byte-identical.");

                // A null prefabPath (the default) must leave the repointed cell untouched.
                CombatCardVfxCueTuningCsvWriter.UpdateFile(
                    path,
                    "TUNE",
                    new CombatCardVfxCueTuningUpdate(1f, true, 0f, 0f, 0f, 0f, 0f, 0f, 0f, "Auto", 0f));
                var untouched = CombatCardVfxCsvConverter.ConvertFile(path).Single(item => item.CueId == "TUNE");
                Assert.That(untouched.PrefabPath, Is.EqualTo("Assets/Repointed.prefab"));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
            }
        }
    }
}


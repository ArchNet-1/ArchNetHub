using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ArchNet.Installer.Editor.Tests
{
    public class RecipeContextTests
    {
        string _folder;
        string _logFile;
        ServiceRecord _record;
        RecipeContext _context;

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "ArchNetHubTests", Guid.NewGuid().ToString("N")).Replace('\\', '/');
            _logFile = ArchNetLog.FilePath;
            ArchNetLog.FilePath = Path.Combine(_folder, "hub.log");
            ArchNetLog.KeepEntries = false;
            _record = new ServiceRecord { id = "credits" };
            _context = new RecipeContext("credits", _folder, "MyCompany.MyGame", _record);
        }

        [TearDown]
        public void TearDown()
        {
            ArchNetLog.FilePath = _logFile;
            ArchNetLog.KeepEntries = true;
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        }

        [Test]
        public void CreatesAMissingFileAndRemembersIt()
        {
            string path = _context.WriteText("Scripts/GameCredits.cs", "class A {}");

            Assert.AreEqual("class A {}", File.ReadAllText(path));
            Assert.AreEqual(ServiceManifest.Hash("class A {}"), _record.Find(path).hash);
            Assert.IsTrue(_context.WroteScripts);
        }

        [Test]
        public void ReplacesAFileTheDeveloperDidNotChange()
        {
            string path = _context.WriteText("Scripts/GameCredits.cs", "class A {}");
            _context.WriteText("Scripts/GameCredits.cs", "class B {}");

            Assert.AreEqual("class B {}", File.ReadAllText(path));
            Assert.IsFalse(File.Exists(path + RecipeContext.NewSuffix));
        }

        [Test]
        public void KeepsTheDevelopersChangesAndWritesTheNewVersionAside()
        {
            string path = _context.WriteText("Scripts/GameCredits.cs", "class A {}");
            File.WriteAllText(path, "class Mine {}");
            LogAssert.Expect(LogType.Warning, new Regex("Kept your changes"));

            string written = _context.WriteText("Scripts/GameCredits.cs", "class B {}");

            Assert.AreEqual("class Mine {}", File.ReadAllText(path));
            Assert.AreEqual(path + RecipeContext.NewSuffix, written);
            Assert.AreEqual("class B {}", File.ReadAllText(written));
        }

        [Test]
        public void FillsTemplatePlaceholders()
        {
            string path = _context.WriteTemplate("Data/info.txt", "{{NAMESPACE}}/{{SERVICE}}/{{TITLE}}",
                new System.Collections.Generic.Dictionary<string, string> { ["TITLE"] = "Credits" });

            Assert.AreEqual("MyCompany.MyGame/credits/Credits", File.ReadAllText(path));
            Assert.IsFalse(_context.WroteScripts);
        }

        [Test]
        public void KeepsConditionalBlocksOnlyForTrueValues()
        {
            var values = new System.Collections.Generic.Dictionary<string, string> { ["credits"] = "true", ["settings"] = "false" };
            string text = RecipeContext.ApplyConditions("A{{#if credits}}C{{/if}}{{#if settings}}S\nS{{/if}}{{#if popups}}P{{/if}}B", values);
            Assert.AreEqual("ACB", text);
        }

        [Test]
        public void RefusesPathsOutsideTheServiceFolder()
        {
            Assert.Throws<ArgumentException>(() => _context.PathOf("../Other/file.cs"));
            Assert.Throws<ArgumentException>(() => _context.PathOf("C:/file.cs"));
        }
    }

    public class RecipeContextAssetTests
    {
        string _folder;

        [SetUp]
        public void SetUp()
        {
            _folder = "Assets/ArchNetHubTest_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            ArchNetLog.KeepEntries = false;
        }

        [TearDown]
        public void TearDown()
        {
            ArchNetLog.KeepEntries = true;
            UnityEditor.AssetDatabase.DeleteAsset(_folder);
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
            if (File.Exists(_folder + ".meta")) File.Delete(_folder + ".meta");
            UnityEditor.AssetDatabase.Refresh();
        }

        [Test]
        public void SavesAPrefabInAFolderJustWrittenOnDisk()
        {
            var context = new RecipeContext("test", _folder, "MyCompany.MyGame", new ServiceRecord { id = "test" });
            context.WriteText("Data/info.json", "{}"); // creates Data/ on disk only

            GameObject prefab = context.SavePrefab("Data/Thing.prefab", new GameObject("Thing"));

            Assert.IsNotNull(prefab);
            Assert.IsTrue(UnityEditor.AssetDatabase.IsValidFolder(_folder + "/Data"));
            Assert.IsFalse(UnityEditor.AssetDatabase.IsValidFolder(_folder + "/Data 1"));
        }
    }

    public class ServiceManifestTests
    {
        [TestCase("my cool game!", "MyCoolGame")]
        [TestCase("123 studio", "_123Studio")]
        [TestCase("Été", "T")]
        [TestCase("", "Fallback")]
        public void MakesAValidIdentifier(string text, string expected) =>
            Assert.AreEqual(expected, ServiceManifest.ToIdentifier(text, "Fallback"));

        [TestCase("MyCompany.MyGame", true)]
        [TestCase("_Studio.Game2", true)]
        [TestCase("My Game", false)]
        [TestCase("2Game", false)]
        [TestCase("Game.", false)]
        public void ChecksNamespaces(string value, bool valid) =>
            Assert.AreEqual(valid, ServiceManifest.IsValidNamespace(value));

        [Test]
        public void HashIgnoresLineEndings() =>
            Assert.AreEqual(ServiceManifest.Hash("a\nb"), ServiceManifest.Hash("a\r\nb"));

        [Test]
        public void LoadsAMissingManifestWithADefaultNamespace()
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "services.json");
            ServiceManifest manifest = ServiceManifest.Load(path);

            Assert.IsNotNull(manifest);
            Assert.IsTrue(ServiceManifest.IsValidNamespace(manifest.rootNamespace));
            Assert.IsNull(manifest.Find("credits"));
        }
    }

    public class PackageCatalogTests
    {
        [Test]
        public void TheOnlineCatalogUpdatesEntriesWithoutDroppingLocalOnes()
        {
            var shipped = new PackageCatalog
            {
                packages = new[] { new CatalogEntry { id = "a", version = "1.0.0" }, new CatalogEntry { id = "core", version = "2.0.0" } },
                services = new[] { new ServiceEntry { id = "bootstrap" } },
            };
            var online = new PackageCatalog { packages = new[] { new CatalogEntry { id = "a", version = "1.1.0" }, new CatalogEntry { id = "b" } } };

            PackageCatalog merged = shipped.MergedWith(online);

            Assert.AreEqual("1.1.0", merged.Find("a").version);
            Assert.IsNotNull(merged.Find("b"));
            Assert.IsNotNull(merged.Find("core"));
            Assert.AreEqual(1, merged.services.Length);
            Assert.AreEqual("1.0.0", shipped.Find("a").version, "the shipped catalog is left untouched");
        }
    }

    public class PackageInstallerTests
    {
        [Test]
        public void RemovingAPackageTakesAwayEverythingThatNeedsIt()
        {
            var catalog = new PackageCatalog
            {
                packages = new[]
                {
                    new CatalogEntry { id = "core" },
                    new CatalogEntry { id = "credits", dependencies = new[] { "core" } },
                    new CatalogEntry { id = "menu", dependencies = new[] { "credits" } },
                    new CatalogEntry { id = "other" },
                },
            };
            var installed = new[] { "core", "credits", "menu", "other" };

            var remove = PackageInstaller.PackagesToRemove(catalog, "core", installed);

            CollectionAssert.AreEquivalent(new[] { "core", "credits", "menu" }, remove);
            CollectionAssert.AreEquivalent(new[] { "other" }, PackageInstaller.PackagesToRemove(catalog, "other", installed));
        }

        [Test]
        public void InstallAllSkipsDeprecatedAndInstalledPackages()
        {
            var catalog = new PackageCatalog
            {
                packages = new[]
                {
                    new CatalogEntry { id = "core" },
                    new CatalogEntry { id = "settings" },
                    new CatalogEntry { id = "audio", deprecated = true },
                },
            };

            CollectionAssert.AreEquivalent(new[] { "settings" }, PackageInstaller.PackagesForInstallAll(catalog, new[] { "core" }));
        }

        [Test]
        public void ExplainsAPrivateRepository() =>
            StringAssert.Contains("private", PackageInstaller.HintFor("fatal: Repository not found."));

        [Test]
        public void ExplainsAMissingNetwork() =>
            StringAssert.Contains("Internet", PackageInstaller.HintFor("Could not resolve host: github.com"));
    }
}

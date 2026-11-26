// ArchNet · Hub — recipe context
// What a recipe uses to write into the game: scripts from templates, data assets and
// prefabs, all under Assets/ArchNet/<Service>/. A script the developer changed is never
// overwritten: the new version is written next to it as <name>.new.
// Docs: https://archnet-plateform.fr/#ArchNetHub

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: InternalsVisibleTo("ArchNet.Installer.Editor.Tests")]

namespace ArchNet.Installer.Editor
{
    public sealed class RecipeContext
    {
        public const string NewSuffix = ".new";

        public string ServiceId { get; }

        /// <summary>The service folder, e.g. "Assets/ArchNet/Credits".</summary>
        public string Folder { get; }

        /// <summary>The project's namespace for generated code, e.g. "MyCompany.MyGame".</summary>
        public string Namespace { get; }

        /// <summary>True once a .cs file was created or changed on disk.</summary>
        public bool WroteScripts { get; private set; }

        internal ServiceRecord Record { get; }

        internal RecipeContext(string serviceId, string folder, string rootNamespace, ServiceRecord record)
        {
            ServiceId = serviceId;
            Folder = folder.TrimEnd('/');
            Namespace = rootNamespace;
            Record = record;
        }

        /// <summary>Path inside the service folder. Refuses absolute paths and "..".</summary>
        public string PathOf(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
                throw new ArgumentException($"\"{relativePath}\" must be a path inside {Folder}.", nameof(relativePath));
            string clean = relativePath.Replace('\\', '/');
            if (Array.IndexOf(clean.Split('/'), "..") >= 0)
                throw new ArgumentException($"\"{relativePath}\" must stay inside {Folder}.", nameof(relativePath));
            return $"{Folder}/{clean}";
        }

        /// <summary>
        /// Writes a text file (script, JSON…). Created when missing, replaced when the developer
        /// has not changed it, otherwise written as &lt;path&gt;.new. Returns the path written.
        /// </summary>
        public string WriteText(string relativePath, string content)
        {
            string path = PathOf(relativePath);
            string hash = ServiceManifest.Hash(content);
            string target = path;

            if (File.Exists(path))
            {
                string current = ServiceManifest.Hash(File.ReadAllText(path));
                if (current == hash)
                {
                    Record.Remember(path, hash);
                    return path;
                }
                GeneratedFile known = Record.Find(path);
                if (known == null || known.hash != current)
                {
                    target = path + NewSuffix;
                    ArchNetLog.Warning($"Kept your changes in {path}.", $"The new version is in {target}: compare them, then delete {Path.GetFileName(target)}.");
                }
                else ArchNetLog.Info($"Updated {path}.");
            }
            else ArchNetLog.Info($"Created {path}.");

            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.WriteAllText(target, content);
            if (target == path) Record.Remember(path, hash);
            if (target.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) WroteScripts = true;
            return target;
        }

        /// <summary>
        /// Writes a template, replacing {{NAMESPACE}}, {{SERVICE}} and each {{KEY}} of values.
        /// </summary>
        public string WriteTemplate(string relativePath, string template, IReadOnlyDictionary<string, string> values = null)
        {
            string text = ApplyConditions(template ?? throw new ArgumentNullException(nameof(template)), values)
                .Replace("{{NAMESPACE}}", Namespace)
                .Replace("{{SERVICE}}", ServiceId);
            if (values != null)
                foreach (KeyValuePair<string, string> pair in values) text = text.Replace("{{" + pair.Key + "}}", pair.Value);
            if (text.Contains("{{"))
                ArchNetLog.Warning($"{relativePath}: a {{{{…}}}} placeholder was left unfilled.", "The recipe misses a value; report it on the package's GitHub page.");
            return WriteText(relativePath, text);
        }

        static readonly Regex ConditionBlock = new Regex(@"\{\{#if (\w+)\}\}(.*?)\{\{/if\}\}", RegexOptions.Singleline);

        /// <summary>
        /// Keeps the text between {{#if KEY}} and {{/if}} only when values[KEY] is "true", so
        /// generated code mentions only the services that exist (and always compiles).
        /// </summary>
        public static string ApplyConditions(string template, IReadOnlyDictionary<string, string> values) =>
            ConditionBlock.Replace(template, match =>
                values != null && values.TryGetValue(match.Groups[1].Value, out string value) && value == "true" ? match.Groups[2].Value : "");

        /// <summary>Records an asset the recipe created by other means (a scene, for instance) in services.json.</summary>
        public void Track(string assetPath) => Record.Remember(assetPath, null);

        /// <summary>Reads a template shipped in a package, e.g. "Packages/com.archnet.module.credit/Editor/Templates/Credits.cs.txt".</summary>
        public static string LoadTemplate(string packagePath)
        {
            var file = AssetDatabase.LoadAssetAtPath<TextAsset>(packagePath);
            return file != null ? file.text : throw new FileNotFoundException($"Template not found: {packagePath}. Reinstall the package from the Hub.");
        }

        /// <summary>Saves a new asset, or returns the one already there (never replaced).</summary>
        public T CreateAsset<T>(string relativePath, T asset) where T : Object
        {
            string path = PathOf(relativePath);
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                Object.DestroyImmediate(asset);
                Record.Remember(path, null);
                ArchNetLog.Info($"Kept the existing {path}.");
                return existing;
            }
            EnsureAssetFolder(Path.GetDirectoryName(path));
            AssetDatabase.CreateAsset(asset, path);
            Record.Remember(path, null);
            ArchNetLog.Info($"Created {path}.");
            return asset;
        }

        /// <summary>Saves the GameObject as a prefab and destroys it; keeps an existing prefab.</summary>
        public GameObject SavePrefab(string relativePath, GameObject root)
        {
            string path = PathOf(relativePath);
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                Object.DestroyImmediate(root);
                Record.Remember(path, null);
                ArchNetLog.Info($"Kept the existing {path}.");
                return existing;
            }
            EnsureAssetFolder(Path.GetDirectoryName(path));
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
            Object.DestroyImmediate(root);
            if (!saved) throw new IOException($"Unity could not save the prefab {path}.");
            Record.Remember(path, null);
            ArchNetLog.Info($"Created {path}.");
            return prefab;
        }

        static void EnsureAssetFolder(string folder)
        {
            folder = folder.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(folder)) return;
            if (Directory.Exists(folder))
            {
                // Created on disk by WriteText but not imported yet: import it rather than create "Folder 1".
                AssetDatabase.ImportAsset(folder, ImportAssetOptions.ForceSynchronousImport);
                if (AssetDatabase.IsValidFolder(folder)) return;
            }
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent)) return;
            EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}

// ArchNet · Hub — services manifest
// Assets/ArchNet/services.json lists the services created in this project and every file
// written for them with its fingerprint. That is how the Hub knows whether the developer
// changed a file, so an update never overwrites their work.
// Docs: https://archnet-plateform.fr/#ArchNetHub

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace ArchNet.Installer.Editor
{
    [Serializable]
    public class GeneratedFile
    {
        public string path;
        public string hash; // empty for Unity assets (prefabs, ScriptableObjects): never overwritten
    }

    [Serializable]
    public class ServiceRecord
    {
        public string id;
        public string created;
        public string updated;
        public List<GeneratedFile> files = new List<GeneratedFile>();

        public GeneratedFile Find(string path) => files.FirstOrDefault(f => f.path == path);

        /// <summary>False once every file written for the service was deleted by hand.</summary>
        public bool IsPresent() => files.Count == 0 || files.Any(f => File.Exists(f.path));

        public void Remember(string path, string hash)
        {
            GeneratedFile file = Find(path);
            if (file == null) files.Add(new GeneratedFile { path = path, hash = hash ?? "" });
            else file.hash = hash ?? "";
        }
    }

    [Serializable]
    public class ServiceManifest
    {
        public const string DefaultPath = "Assets/ArchNet/services.json";
        static readonly Regex NamespacePattern = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$");

        public string rootNamespace;
        public List<ServiceRecord> services = new List<ServiceRecord>();

        [NonSerialized] string _path = DefaultPath;

        /// <summary>The project's manifest, or null when the file exists but cannot be read.</summary>
        public static ServiceManifest Load(string path = DefaultPath)
        {
            var manifest = new ServiceManifest();
            if (File.Exists(path))
            {
                try
                {
                    manifest = JsonUtility.FromJson<ServiceManifest>(File.ReadAllText(path)) ?? new ServiceManifest();
                }
                catch (ArgumentException exception)
                {
                    ArchNetLog.Error($"{path} is not valid JSON ({exception.Message}).", "Fix the file or restore it from Git. ArchNet leaves it untouched until then.");
                    return null;
                }
            }
            manifest._path = path;
            manifest.services ??= new List<ServiceRecord>();
            if (!IsValidNamespace(manifest.rootNamespace)) manifest.rootNamespace = DefaultNamespace();
            return manifest;
        }

        public void Save()
        {
            // Written to a temporary file first, so a crash never leaves a half-written manifest.
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(this, true));
            if (File.Exists(_path)) File.Replace(temp, _path, null);
            else File.Move(temp, _path);
        }

        public ServiceRecord Find(string id) => services.FirstOrDefault(s => s.id == id);

        /// <summary>True when the service was created and its files are still in the project.</summary>
        public bool Has(string id) => Find(id)?.IsPresent() == true;

        public ServiceRecord GetOrAdd(string id)
        {
            ServiceRecord record = Find(id);
            if (record != null) return record;
            record = new ServiceRecord { id = id, created = DateTime.Now.ToString("yyyy-MM-dd HH:mm") };
            services.Add(record);
            return record;
        }

        /// <summary>SHA-256 of the text with line endings normalised (Git may rewrite them).</summary>
        public static string Hash(string text)
        {
            using var sha = SHA256.Create();
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n")));
            return string.Concat(bytes.Select(b => b.ToString("x2")));
        }

        public static bool IsValidNamespace(string value) => !string.IsNullOrEmpty(value) && NamespacePattern.IsMatch(value);

        /// <summary>Company.Product from the Player Settings, as a valid C# namespace.</summary>
        public static string DefaultNamespace() => $"{ToIdentifier(PlayerSettings.companyName, "MyCompany")}.{ToIdentifier(PlayerSettings.productName, "MyGame")}";

        /// <summary>"my cool game!" → "MyCoolGame". Falls back when nothing usable is left.</summary>
        public static string ToIdentifier(string text, string fallback)
        {
            var result = new StringBuilder();
            bool upper = true;
            foreach (char c in text ?? "")
            {
                if (char.IsLetterOrDigit(c) && c < 128)
                {
                    result.Append(upper ? char.ToUpperInvariant(c) : c);
                    upper = false;
                }
                else upper = true;
            }
            if (result.Length == 0) return fallback;
            if (char.IsDigit(result[0])) result.Insert(0, '_');
            return result.ToString();
        }
    }
}

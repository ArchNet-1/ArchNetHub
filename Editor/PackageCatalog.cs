// ArchNet · Installer — catalog
// The list of ArchNet packages: id, name, category, description, GitHub repository and the
// ArchNet packages each one needs. Shipped in the package (catalog.json) and refreshed
// from the website when it publishes one.
// Docs: https://archnet-plateform.fr/#ArchNetInstaller

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ArchNet.Installer.Editor
{
    [Serializable]
    public class CatalogEntry
    {
        public string id;
        public string name;
        public string category;
        public string version;
        public string description;
        public string repo;
        public string[] dependencies = Array.Empty<string>();
    }

    [Serializable]
    public class PackageCatalog
    {
        public const string OnlineUrl = "https://archnet-plateform.fr/catalog.json";
        const string EmbeddedPath = "Packages/com.archnet.installer/Editor/catalog.json";

        public CatalogEntry[] packages = Array.Empty<CatalogEntry>();

        public static PackageCatalog Parse(string json)
        {
            var catalog = JsonUtility.FromJson<PackageCatalog>(json);
            return catalog?.packages != null && catalog.packages.Length > 0 ? catalog : null;
        }

        /// <summary>The catalog shipped inside the installer package.</summary>
        public static PackageCatalog LoadEmbedded()
        {
            var file = AssetDatabase.LoadAssetAtPath<TextAsset>(EmbeddedPath);
            return (file != null ? Parse(file.text) : null) ?? new PackageCatalog();
        }

        public CatalogEntry Find(string id) => Array.Find(packages, p => p.id == id);

        /// <summary>The package and every ArchNet package it needs, dependencies first, each once.</summary>
        public List<CatalogEntry> WithDependencies(string id)
        {
            var ordered = new List<CatalogEntry>();
            Visit(id, ordered, new HashSet<string>());
            return ordered;
        }

        void Visit(string id, List<CatalogEntry> ordered, HashSet<string> seen)
        {
            if (!seen.Add(id)) return;
            CatalogEntry entry = Find(id);
            if (entry == null) return;
            foreach (string dependency in entry.dependencies) Visit(dependency, ordered, seen);
            ordered.Add(entry);
        }
    }
}

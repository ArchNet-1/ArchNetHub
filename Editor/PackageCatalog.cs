// ArchNet · Installer — catalog
// The list of ArchNet packages: id, name, category, description, GitHub repository and the
// ArchNet packages each one needs. Shipped in the package (catalog.json) and refreshed
// from the website when it publishes one.
// Docs: https://archnet-plateform.fr/#ArchNetHub

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
        /// <summary>Replaced by another package: still installable, but left out of Install all.</summary>
        public bool deprecated;
    }

    /// <summary>A service the Services tab offers: its recipe ships in one of the packages listed.</summary>
    [Serializable]
    public class ServiceEntry
    {
        public string id;
        public string name;
        public string category;
        public string description;
        public string[] packages = Array.Empty<string>();
    }

    [Serializable]
    public class PackageCatalog
    {
        public const string OnlineUrl = "https://archnet-plateform.fr/catalog.json";
        const string EmbeddedPath = "Packages/com.archnet.hub/Editor/catalog.json";

        public CatalogEntry[] packages = Array.Empty<CatalogEntry>();
        public ServiceEntry[] services = Array.Empty<ServiceEntry>();

        public static PackageCatalog Parse(string json)
        {
            PackageCatalog catalog;
            try { catalog = JsonUtility.FromJson<PackageCatalog>(json); }
            catch (ArgumentException) { return null; }
            if (catalog?.packages == null || catalog.packages.Length == 0) return null;
            catalog.services ??= Array.Empty<ServiceEntry>();
            return catalog;
        }

        /// <summary>The catalog shipped inside the installer package.</summary>
        public static PackageCatalog LoadEmbedded()
        {
            var file = AssetDatabase.LoadAssetAtPath<TextAsset>(EmbeddedPath);
            return (file != null ? Parse(file.text) : null) ?? new PackageCatalog();
        }

        public CatalogEntry Find(string id) => Array.Find(packages, p => p.id == id);

        /// <summary>
        /// A new catalog: this one, with the entries of the other replacing those of the same id
        /// and its new entries added. The site may lag behind the Hub, so nothing is ever dropped.
        /// </summary>
        public PackageCatalog MergedWith(PackageCatalog other) => new PackageCatalog
        {
            packages = Merge(packages, other.packages, p => p.id),
            services = Merge(services, other.services, s => s.id),
        };

        static T[] Merge<T>(T[] current, T[] incoming, Func<T, string> idOf)
        {
            var merged = new List<T>(current);
            foreach (T entry in incoming ?? Array.Empty<T>())
            {
                int index = merged.FindIndex(e => idOf(e) == idOf(entry));
                if (index >= 0) merged[index] = entry;
                else merged.Add(entry);
            }
            return merged.ToArray();
        }

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

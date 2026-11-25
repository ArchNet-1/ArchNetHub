// ArchNet · Installer — install and remove
// Adds an ArchNet package and the ArchNet packages it needs in a single Package Manager
// operation. Unity cannot resolve dependencies between Git packages on its own; adding them
// together is what makes a one-click install work.
// Docs: https://archnet-plateform.fr/#ArchNetInstaller

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace ArchNet.Installer.Editor
{
    public static class PackageInstaller
    {
        const string LocalFolderKey = "ArchNet.Installer.LocalFolder";
        const string GitHubOrganization = "https://github.com/ArchNet-1/";

        /// <summary>
        /// Developer option: a folder holding the package repositories side by side. When set,
        /// packages install from there (file:) instead of GitHub.
        /// </summary>
        public static string LocalFolder
        {
            get => EditorPrefs.GetString(LocalFolderKey, "");
            set => EditorPrefs.SetString(LocalFolderKey, value ?? "");
        }

        /// <summary>Where the Package Manager gets this package from.</summary>
        public static string SourceOf(CatalogEntry entry) => string.IsNullOrEmpty(LocalFolder)
            ? $"{GitHubOrganization}{entry.repo}.git"
            : "file:" + Path.Combine(LocalFolder, entry.repo).Replace('\\', '/');

        /// <summary>
        /// Installs (or updates) the package, plus every ArchNet dependency not installed yet.
        /// Returns the running request, or null when the id is not in the catalog.
        /// </summary>
        public static AddAndRemoveRequest Install(PackageCatalog catalog, string id, ICollection<string> installedIds)
        {
            string[] sources = catalog.WithDependencies(id)
                .Where(entry => entry.id == id || !installedIds.Contains(entry.id))
                .Select(SourceOf)
                .ToArray();
            return sources.Length == 0 ? null : Client.AddAndRemove(sources, null);
        }

        public static RemoveRequest Uninstall(string id) => Client.Remove(id);

        /// <summary>Installed packages that need this one.</summary>
        public static List<CatalogEntry> InstalledDependents(PackageCatalog catalog, string id, ICollection<string> installedIds) =>
            catalog.packages.Where(p => installedIds.Contains(p.id) && p.dependencies.Contains(id)).ToList();
    }
}

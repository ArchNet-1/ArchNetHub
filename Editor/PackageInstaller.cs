// ArchNet · Installer — install and remove
// Adds an ArchNet package and the ArchNet packages it needs in a single Package Manager
// operation. Unity cannot resolve dependencies between Git packages on its own; adding them
// together is what makes a one-click install work.
// Docs: https://archnet-plateform.fr/#ArchNetHub

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

        /// <summary>Installs every package listed that is missing, with what they need, in one operation.</summary>
        public static AddAndRemoveRequest InstallMissing(PackageCatalog catalog, IEnumerable<string> ids, ICollection<string> installedIds)
        {
            string[] sources = ids.SelectMany(catalog.WithDependencies)
                .Where(entry => !installedIds.Contains(entry.id))
                .Select(entry => entry.id).Distinct()
                .Select(id => SourceOf(catalog.Find(id)))
                .ToArray();
            return sources.Length == 0 ? null : Client.AddAndRemove(sources, null);
        }

        /// <summary>What to do about a Package Manager error, in plain words.</summary>
        public static string HintFor(string error)
        {
            string text = error ?? "";
            bool Has(string part) => text.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (Has("Repository not found") || Has("Authentication failed") || Has("could not read Username"))
                return "The GitHub repository is private or does not exist: make it public, or use the Developer option with a local folder.";
            if (Has("Could not resolve host") || Has("unable to access") || Has("timed out"))
                return "No Internet connection to GitHub: check the network, then click again.";
            if (Has("No 'git' executable") || Has("git is not installed") || Has("Cannot find git"))
                return "Git is not installed: install it from git-scm.com, then restart Unity.";
            if (Has("does not exist") && Has("file:"))
                return "The local folder does not hold this package: check Developer > Local packages folder.";
            return "See the Unity console for the full Package Manager error.";
        }

        public static RemoveRequest Uninstall(string id) => Client.Remove(id);

        /// <summary>The packages Install all adds: every one not installed yet, except the deprecated ones.</summary>
        public static IEnumerable<string> PackagesForInstallAll(PackageCatalog catalog, ICollection<string> installedIds) =>
            catalog.packages.Where(p => !p.deprecated && !installedIds.Contains(p.id)).Select(p => p.id);

        /// <summary>Installs every catalog package not installed yet (deprecated ones aside), in one operation.</summary>
        public static AddAndRemoveRequest InstallAll(PackageCatalog catalog, ICollection<string> installedIds) =>
            InstallMissing(catalog, PackagesForInstallAll(catalog, installedIds), installedIds);

        /// <summary>Removes several packages in one operation.</summary>
        public static AddAndRemoveRequest UninstallMany(IEnumerable<string> ids)
        {
            string[] remove = ids.Distinct().ToArray();
            return remove.Length == 0 ? null : Client.AddAndRemove(null, remove);
        }

        /// <summary>Installed packages that need this one, directly or through another package.</summary>
        public static List<CatalogEntry> AllInstalledDependents(PackageCatalog catalog, string id, ICollection<string> installedIds)
        {
            var found = new List<CatalogEntry>();
            var queue = new Queue<string>(new[] { id });
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                foreach (CatalogEntry dependent in InstalledDependents(catalog, current, installedIds))
                {
                    if (dependent.id == id || found.Any(f => f.id == dependent.id)) continue;
                    found.Add(dependent);
                    queue.Enqueue(dependent.id);
                }
            }
            return found;
        }

        /// <summary>The package and everything installed that needs it: what removing it takes away.</summary>
        public static List<string> PackagesToRemove(PackageCatalog catalog, string id, ICollection<string> installedIds) =>
            new[] { id }.Concat(AllInstalledDependents(catalog, id, installedIds).Select(d => d.id)).ToList();

        /// <summary>Installed packages that need this one.</summary>
        public static List<CatalogEntry> InstalledDependents(PackageCatalog catalog, string id, ICollection<string> installedIds) =>
            catalog.packages.Where(p => installedIds.Contains(p.id) && p.dependencies.Contains(id)).ToList();
    }
}

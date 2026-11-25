// ArchNet · Installer — window
// ArchNet > Package Installer: browse every ArchNet package, search, filter by category,
// and install one (with what it needs) or remove it in one click.
// Docs: https://archnet-plateform.fr/#ArchNetInstaller

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.Networking;

namespace ArchNet.Installer.Editor
{
    public class InstallerWindow : EditorWindow
    {
        PackageCatalog _catalog;
        readonly Dictionary<string, string> _installed = new Dictionary<string, string>(); // id -> version
        string[] _categories = { "All" };
        int _category;
        string _search = "";
        Vector2 _scroll;
        bool _showDeveloper;
        string _status = "";

        ListRequest _listRequest;
        Request _operation;
        UnityWebRequest _download;

        [MenuItem("ArchNet/Package Installer", priority = 0)]
        public static void Open() => GetWindow<InstallerWindow>("ArchNet Installer").minSize = new Vector2(420, 300);

        void OnEnable()
        {
            SetCatalog(PackageCatalog.LoadEmbedded());
            RefreshInstalled();
            _download = UnityWebRequest.Get(PackageCatalog.OnlineUrl);
            _download.timeout = 10;
            _download.SendWebRequest();
            EditorApplication.update += Poll;
        }

        void OnDisable()
        {
            EditorApplication.update -= Poll;
            _download?.Dispose();
        }

        void SetCatalog(PackageCatalog catalog)
        {
            _catalog = catalog;
            _categories = new[] { "All" }.Concat(catalog.packages.Select(p => p.category).Distinct().OrderBy(c => c)).ToArray();
            _category = Mathf.Min(_category, _categories.Length - 1);
        }

        void RefreshInstalled() => _listRequest = Client.List(true, false);

        // Follows the running Package Manager and web requests.
        void Poll()
        {
            if (_listRequest != null && _listRequest.IsCompleted)
            {
                _installed.Clear();
                if (_listRequest.Status == StatusCode.Success)
                    foreach (var package in _listRequest.Result) _installed[package.name] = package.version;
                _listRequest = null;
                Repaint();
            }
            if (_operation != null && _operation.IsCompleted)
            {
                _status = _operation.Status == StatusCode.Success ? "Done." : $"Failed: {_operation.Error?.message}";
                _operation = null;
                RefreshInstalled();
            }
            if (_download != null && _download.isDone)
            {
                if (_download.result == UnityWebRequest.Result.Success)
                {
                    PackageCatalog online = PackageCatalog.Parse(_download.downloadHandler.text);
                    if (online != null) SetCatalog(online);
                }
                _download.Dispose();
                _download = null;
                Repaint();
            }
        }

        bool Busy => _operation != null || _listRequest != null;

        void OnGUI()
        {
            DrawToolbar();
            DrawDeveloperOptions();
            if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, MessageType.None);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (CatalogEntry entry in Filtered()) DrawEntry(entry);
            EditorGUILayout.EndScrollView();
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
                _category = EditorGUILayout.Popup(_category, _categories, EditorStyles.toolbarPopup, GUILayout.Width(120));
                using (new EditorGUI.DisabledScope(Busy))
                    if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60))) RefreshInstalled();
            }
        }

        void DrawDeveloperOptions()
        {
            _showDeveloper = EditorGUILayout.Foldout(_showDeveloper, "Developer", true);
            if (!_showDeveloper) return;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Local packages folder", GUILayout.Width(140));
                PackageInstaller.LocalFolder = EditorGUILayout.TextField(PackageInstaller.LocalFolder);
                if (GUILayout.Button("…", GUILayout.Width(24)))
                {
                    string folder = EditorUtility.OpenFolderPanel("Folder holding the ArchNet repositories", PackageInstaller.LocalFolder, "");
                    if (!string.IsNullOrEmpty(folder)) PackageInstaller.LocalFolder = folder;
                }
            }
            EditorGUILayout.HelpBox(string.IsNullOrEmpty(PackageInstaller.LocalFolder)
                ? "Packages install from GitHub."
                : "Packages install from this folder (file:), not from GitHub.", MessageType.Info);
        }

        IEnumerable<CatalogEntry> Filtered() => _catalog.packages.Where(p =>
            (_category == 0 || p.category == _categories[_category]) &&
            (string.IsNullOrEmpty(_search) ||
             p.name.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
             p.description.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) >= 0));

        void DrawEntry(CatalogEntry entry)
        {
            bool installed = _installed.TryGetValue(entry.id, out string version);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(entry.name, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(installed ? $"Installed {version}" : entry.category, EditorStyles.miniLabel, GUILayout.Width(110));
                }
                EditorGUILayout.LabelField(entry.description, EditorStyles.wordWrappedMiniLabel);
                if (entry.dependencies.Length > 0)
                {
                    string needs = string.Join(", ", entry.dependencies.Select(d => _catalog.Find(d)?.name ?? d));
                    EditorGUILayout.LabelField($"Needs: {needs}", EditorStyles.miniLabel);
                }

                using (new EditorGUILayout.HorizontalScope())
                using (new EditorGUI.DisabledScope(Busy))
                {
                    GUILayout.FlexibleSpace();
                    if (installed && GUILayout.Button("Remove", GUILayout.Width(80))) Remove(entry);
                    string install = !installed ? "Install" : version != entry.version ? $"Update to {entry.version}" : "Reinstall";
                    if (GUILayout.Button(install, GUILayout.Width(110))) Install(entry);
                }
            }
        }

        void Install(CatalogEntry entry)
        {
            var added = _catalog.WithDependencies(entry.id).Where(e => e.id == entry.id || !_installed.ContainsKey(e.id)).Select(e => e.name);
            _operation = PackageInstaller.Install(_catalog, entry.id, _installed.Keys);
            _status = $"Installing {string.Join(", ", added)}…";
        }

        void Remove(CatalogEntry entry)
        {
            List<CatalogEntry> dependents = PackageInstaller.InstalledDependents(_catalog, entry.id, _installed.Keys);
            if (dependents.Count > 0 && !EditorUtility.DisplayDialog("Remove package",
                    $"{string.Join(", ", dependents.Select(d => d.name))} need{(dependents.Count == 1 ? "s" : "")} {entry.name}. Remove it anyway?", "Remove", "Cancel"))
                return;
            _operation = PackageInstaller.Uninstall(entry.id);
            _status = $"Removing {entry.name}…";
        }
    }
}

// ArchNet · Hub — window
// ArchNet > Hub: add complete services to the game (Services), install packages (Packages),
// browse the content libraries (Contents) and read what happened (Journal).
// Docs: https://archnet-plateform.fr/#ArchNetHub

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace ArchNet.Installer.Editor
{
    public class HubWindow : EditorWindow
    {
        const string StylePath = "Packages/com.archnet.hub/Editor/UI/Hub.uss";
        const string TabKey = "ArchNet.Hub.Tab";
        const string SiteUrl = "https://archnet-plateform.fr";
        const string GitHubUrl = "https://github.com/ArchNet-1";
        const string AllCategories = "All";
        static readonly string[] TabNames = { "Services", "Packages", "Contents", "Journal" };
        static readonly string[] JournalFilters = { "Everything", "Warnings and errors", "Errors" };

        PackageCatalog _catalog;
        readonly Dictionary<string, string> _installed = new Dictionary<string, string>(); // id -> version
        const int MaxListRetries = 3;
        const long ListRetryDelayMs = 2000;
        const long NoReloadDelayMs = 10000;

        ListRequest _listRequest;
        int _listRetries;
        Request _operation;
        string _operationLabel;
        UnityWebRequest _download;
        bool _wasBusy;

        readonly List<Button> _tabButtons = new List<Button>();
        readonly List<VisualElement> _pages = new List<VisualElement>();
        VisualElement _servicesList, _packagesList, _contentsList, _contentEditor;
        DropdownField _newLibraryType;
        Button _installAll, _uninstallAll;
        const string HubPackage = "com.archnet.hub";
        Object _editedContent;
        TextField _namespaceField, _search;
        DropdownField _category, _journalFilter;
        ListView _journal;
        readonly List<LogEntry> _journalRows = new List<LogEntry>();
        Label _status;

        bool Busy => _operation != null || _listRequest != null;

        [MenuItem("ArchNet/Hub", priority = 0)]
        public static void Open()
        {
            var window = GetWindow<HubWindow>();
            window.titleContent = new GUIContent("ArchNet Hub");
            window.minSize = new Vector2(540, 400);
        }

        void OnEnable()
        {
            _catalog = PackageCatalog.LoadEmbedded();
            if (_catalog.packages.Length == 0)
                ArchNetLog.Error("The catalog shipped with the Hub could not be read.", "Reinstall the Hub package.");
            _download = UnityWebRequest.Get(PackageCatalog.OnlineUrl);
            _download.timeout = 10;
            _download.SendWebRequest();
            RefreshInstalled();
            ArchNetLog.Added += OnLogAdded;
            ArchNetLog.Cleared += RefreshJournal;
            RecipeRunner.Changed += RefreshServices;
        }

        void OnDisable()
        {
            ArchNetLog.Added -= OnLogAdded;
            ArchNetLog.Cleared -= RefreshJournal;
            RecipeRunner.Changed -= RefreshServices;
            _download?.Dispose();
            _download = null;
        }

        void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.AddToClassList("archnet-hub");
            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);
            if (style != null) root.styleSheets.Add(style);

            root.Add(BuildHeader());
            root.Add(BuildTabBar());
            var pages = new VisualElement();
            pages.AddToClassList("pages");
            root.Add(pages);
            foreach (VisualElement page in new[] { BuildServicesPage(), BuildPackagesPage(), BuildContentsPage(), BuildJournalPage() })
            {
                page.AddToClassList("page");
                _pages.Add(page);
                pages.Add(page);
            }
            _status = new Label();
            _status.AddToClassList("status-bar");
            root.Add(_status);

            SelectTab(SessionState.GetInt(TabKey, 0));
            RefreshAll();
            root.schedule.Execute(Poll).Every(200);
        }

        // ---------- Layout ----------

        VisualElement BuildHeader()
        {
            var header = Row("header");
            var logo = new Label("ArchNet");
            logo.AddToClassList("logo");
            header.Add(logo);
            var title = new Label("Hub");
            title.AddToClassList("title");
            header.Add(title);
            var version = new Label(UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(HubWindow).Assembly)?.version ?? "");
            version.AddToClassList("muted");
            header.Add(version);
            header.Add(Spacer());
            header.Add(LinkButton("Docs", SiteUrl));
            header.Add(LinkButton("GitHub", GitHubUrl));
            return header;
        }

        VisualElement BuildTabBar()
        {
            var bar = Row("tab-bar");
            for (int i = 0; i < TabNames.Length; i++)
            {
                int index = i;
                var tab = new Button(() => SelectTab(index)) { text = TabNames[i] };
                tab.AddToClassList("tab");
                _tabButtons.Add(tab);
                bar.Add(tab);
            }
            return bar;
        }

        void SelectTab(int index)
        {
            index = Mathf.Clamp(index, 0, TabNames.Length - 1);
            SessionState.SetInt(TabKey, index);
            for (int i = 0; i < _pages.Count; i++)
            {
                _pages[i].style.display = i == index ? DisplayStyle.Flex : DisplayStyle.None;
                _tabButtons[i].EnableInClassList("tab--selected", i == index);
            }
            if (index == 2) RefreshContents();
        }

        VisualElement BuildServicesPage()
        {
            var page = new VisualElement();
            page.Add(Intro("Add a complete service to your game in one click: scripts, UI, data and a tool to edit them. " +
                           "Files go to Assets/ArchNet/ and are yours to change."));
            var row = Row("toolbar");
            _namespaceField = new TextField("Namespace") { tooltip = "Namespace of the scripts created for your game." };
            _namespaceField.AddToClassList("grow");
            _namespaceField.RegisterValueChangedCallback(evt => SaveNamespace(evt.newValue));
            row.Add(_namespaceField);
            page.Add(row);
            _servicesList = new ScrollView();
            _servicesList.AddToClassList("list");
            page.Add(_servicesList);
            return page;
        }

        VisualElement BuildPackagesPage()
        {
            var page = new VisualElement();
            var row = Row("toolbar");
            _search = new TextField();
            _search.textEdition.placeholder = "Search packages…";
            _search.AddToClassList("grow");
            _search.RegisterValueChangedCallback(_ => RefreshPackages());
            row.Add(_search);
            _category = new DropdownField(new List<string> { AllCategories }, 0);
            _category.AddToClassList("category");
            _category.RegisterValueChangedCallback(_ => RefreshPackages());
            row.Add(_category);
            row.Add(new Button(RefreshInstalled) { text = "Refresh" });
            _installAll = new Button(InstallAll) { text = "Install all" };
            _installAll.AddToClassList("primary");
            row.Add(_installAll);
            _uninstallAll = new Button(UninstallAll) { text = "Uninstall all" };
            row.Add(_uninstallAll);
            page.Add(row);

            var developer = new Foldout { text = "Developer", value = !string.IsNullOrEmpty(PackageInstaller.LocalFolder) };
            developer.AddToClassList("developer");
            var folderRow = Row();
            var folder = new TextField("Local packages folder") { value = PackageInstaller.LocalFolder };
            folder.AddToClassList("grow");
            var help = new Label();
            help.AddToClassList("muted");
            void ShowSource() => help.text = string.IsNullOrEmpty(PackageInstaller.LocalFolder)
                ? "Packages install from GitHub."
                : "Packages install from this folder (file:), not from GitHub.";
            folder.RegisterValueChangedCallback(evt =>
            {
                PackageInstaller.LocalFolder = evt.newValue;
                ShowSource();
            });
            folderRow.Add(folder);
            folderRow.Add(new Button(() =>
            {
                string picked = EditorUtility.OpenFolderPanel("Folder holding the ArchNet repositories", PackageInstaller.LocalFolder, "");
                if (!string.IsNullOrEmpty(picked)) folder.value = picked;
            }) { text = "…" });
            developer.Add(folderRow);
            developer.Add(help);
            ShowSource();
            page.Add(developer);

            _packagesList = new ScrollView();
            _packagesList.AddToClassList("list");
            page.Add(_packagesList);
            return page;
        }

        VisualElement BuildContentsPage()
        {
            var page = new VisualElement();
            var row = Row("toolbar");
            row.Add(Intro("Every content library of the project (colors, images, audio, prefabs…): pick one on the left, edit it on the right."));
            row.Add(Spacer());
            List<string> kinds = LibraryTypes().Select(t => ObjectNames.NicifyVariableName(t.Name)).ToList();
            _newLibraryType = new DropdownField(kinds, kinds.Count > 0 ? 0 : -1) { tooltip = "Kind of library to create." };
            _newLibraryType.AddToClassList("category");
            row.Add(_newLibraryType);
            row.Add(new Button(CreateLibrary) { text = "New library" });
            row.Add(new Button(RefreshContents) { text = "Refresh" });
            page.Add(row);

            var split = new TwoPaneSplitView(0, 300, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1;
            _contentsList = new ScrollView();
            _contentsList.AddToClassList("list");
            _contentEditor = new ScrollView();
            _contentEditor.style.paddingLeft = 8;
            split.Add(_contentsList);
            split.Add(_contentEditor);
            page.Add(split);
            return page;
        }

        VisualElement BuildJournalPage()
        {
            var page = new VisualElement();
            var row = Row("toolbar");
            _journalFilter = new DropdownField(JournalFilters.ToList(), 0);
            _journalFilter.RegisterValueChangedCallback(_ => RefreshJournal());
            row.Add(_journalFilter);
            row.Add(Spacer());
            row.Add(new Button(() =>
            {
                EditorGUIUtility.systemCopyBuffer = ArchNetLog.Report(_installed.Keys.Where(id => id.StartsWith("com.archnet.")).Select(id => $"{id}@{_installed[id]}"));
                ShowStatus("Report copied: paste it in a GitHub issue.");
            }) { text = "Copy report" });
            row.Add(new Button(() =>
            {
                string file = Path.GetFullPath(ArchNetLog.FilePath);
                if (File.Exists(file)) EditorUtility.RevealInFinder(file);
                else ShowStatus("No log file yet.");
            }) { text = "Open log file" });
            row.Add(new Button(ArchNetLog.Clear) { text = "Clear" });
            page.Add(row);

            _journal = new ListView(_journalRows, -1, MakeJournalRow, BindJournalRow)
            {
                virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                selectionType = SelectionType.None,
            };
            _journal.AddToClassList("journal");
            page.Add(_journal);
            return page;
        }

        // ---------- Services ----------

        void RefreshServices()
        {
            if (_servicesList == null) return;
            ServiceManifest manifest = ServiceManifest.Load();
            if (manifest != null) _namespaceField.SetValueWithoutNotify(manifest.rootNamespace);

            _servicesList.Clear();
            var known = new HashSet<string>();
            foreach (ServiceEntry entry in _catalog.services)
            {
                known.Add(entry.id);
                _servicesList.Add(ServiceCard(entry, RecipeRunner.Find(entry.id), manifest));
            }
            // Recipes from packages the catalog does not list yet (local development).
            foreach (ServiceRecipe recipe in RecipeRunner.Recipes.Where(r => !known.Contains(r.Id)))
            {
                var entry = new ServiceEntry { id = recipe.Id, name = recipe.DisplayName, category = recipe.Category, description = recipe.Description };
                _servicesList.Add(ServiceCard(entry, recipe, manifest));
            }
            if (_servicesList.childCount == 0) _servicesList.Add(Intro("No service yet."));
        }

        // The packages of the service and the ArchNet packages they need, minus those installed.
        string[] MissingFor(ServiceEntry entry) => entry.packages
            .SelectMany(id => _catalog.WithDependencies(id).Select(e => e.id).DefaultIfEmpty(id))
            .Distinct()
            .Where(id => !_installed.ContainsKey(id))
            .ToArray();

        VisualElement ServiceCard(ServiceEntry entry, ServiceRecipe recipe, ServiceManifest manifest)
        {
            string[] missing = MissingFor(entry);
            bool created = manifest?.Find(entry.id) != null;

            var card = Card();
            var top = Row();
            top.Add(Title(entry.name));
            top.Add(Chip(entry.category, "chip"));
            top.Add(Spacer());
            if (created) top.Add(Chip("Created", "chip--ok"));
            else if (missing.Length > 0) top.Add(Chip("Package missing", "chip--muted"));
            card.Add(top);
            card.Add(Description(entry.description));
            if (entry.packages.Length > 0)
                card.Add(Muted("Uses: " + string.Join(", ", entry.packages.Select(id => (_catalog.Find(id)?.name ?? id) + (missing.Contains(id) ? " (will be installed)" : "")))));

            var actions = Row("actions");
            actions.Add(Spacer());
            if (created)
            {
                string folder = recipe != null ? RecipeRunner.FolderOf(recipe) : RecipeRunner.RootFolder;
                actions.Add(new Button(() => Reveal(folder)) { text = "Show files" });
                if (recipe != null && recipe.HasTool) actions.Add(new Button(recipe.OpenTool) { text = "Open tool" });
            }
            var add = new Button(() => AddService(entry)) { text = created ? "Update" : "Add to game" };
            add.AddToClassList("primary");
            add.SetEnabled(!Busy && !RecipeRunner.HasPending && (recipe != null || missing.Length > 0));
            if (recipe == null && missing.Length == 0) add.tooltip = "The installed package has no recipe for this service yet.";
            actions.Add(add);
            card.Add(actions);
            return card;
        }

        void AddService(ServiceEntry entry)
        {
            string[] missing = MissingFor(entry);
            if (missing.Length == 0)
            {
                ServiceRecipe recipe = RecipeRunner.Find(entry.id);
                if (recipe == null)
                {
                    ArchNetLog.Error($"{entry.name}: no recipe found in the installed packages.", "Update the package from the Packages tab.");
                    return;
                }
                RecipeRunner.Run(recipe);
                RefreshServices();
                return;
            }

            string names = string.Join(", ", missing.Select(id => _catalog.Find(id)?.name ?? id));
            AddAndRemoveRequest request = PackageInstaller.InstallMissing(_catalog, missing, _installed.Keys);
            if (request == null)
            {
                ArchNetLog.Error($"{entry.name}: the catalog does not know {names}.", "Refresh the Hub or update it.");
                return;
            }
            RecipeRunner.RunAfterInstall(entry.id);
            StartOperation(request, $"Installing {names} for {entry.name}");
        }

        void SaveNamespace(string value)
        {
            bool valid = ServiceManifest.IsValidNamespace(value);
            _namespaceField.EnableInClassList("invalid", !valid);
            if (!valid)
            {
                ShowStatus("Namespace: letters, digits and _, parts separated by dots (e.g. MyCompany.MyGame).");
                return;
            }
            ServiceManifest manifest = ServiceManifest.Load();
            if (manifest == null || manifest.rootNamespace == value) return;
            manifest.rootNamespace = value;
            manifest.Save();
            AssetDatabase.ImportAsset(ServiceManifest.DefaultPath);
        }

        // ---------- Packages ----------

        void RefreshPackages()
        {
            if (_packagesList == null) return;
            List<string> categories = new[] { AllCategories }.Concat(_catalog.packages.Select(p => p.category).Distinct().OrderBy(c => c)).ToList();
            if (!_category.choices.SequenceEqual(categories))
            {
                _category.choices = categories;
                if (!categories.Contains(_category.value)) _category.SetValueWithoutNotify(AllCategories);
            }

            string search = _search.value ?? "";
            string category = _category.value;
            _packagesList.Clear();
            foreach (CatalogEntry entry in _catalog.packages.Where(p =>
                         (category == AllCategories || p.category == category) &&
                         (search.Length == 0 ||
                          p.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                          p.description.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)))
                _packagesList.Add(PackageCard(entry));
            if (_packagesList.childCount == 0) _packagesList.Add(Intro("No package matches."));
        }

        VisualElement PackageCard(CatalogEntry entry)
        {
            bool installed = _installed.TryGetValue(entry.id, out string version);
            var card = Card();
            var top = Row();
            top.Add(Title(entry.name));
            top.Add(Chip(entry.category, "chip"));
            if (entry.deprecated) top.Add(Chip("Deprecated", "chip--warning"));
            top.Add(Spacer());
            top.Add(installed ? Chip($"Installed {version}", "chip--ok") : Chip(entry.version, "chip--muted"));
            card.Add(top);
            card.Add(Description(entry.description));
            if (entry.dependencies.Length > 0)
                card.Add(Muted("Needs: " + string.Join(", ", entry.dependencies.Select(d => _catalog.Find(d)?.name ?? d))));

            var actions = Row("actions");
            actions.Add(LinkButton("Docs", $"{SiteUrl}/#{entry.repo}"));
            actions.Add(Spacer());
            if (installed)
            {
                var remove = new Button(() => RemovePackage(entry)) { text = "Remove" };
                remove.SetEnabled(!Busy);
                actions.Add(remove);
            }
            var install = new Button(() => InstallPackage(entry))
            {
                text = !installed ? "Install" : version != entry.version ? $"Update to {entry.version}" : "Reinstall",
            };
            install.AddToClassList("primary");
            install.SetEnabled(!Busy);
            actions.Add(install);
            card.Add(actions);
            return card;
        }

        void InstallPackage(CatalogEntry entry)
        {
            IEnumerable<string> added = _catalog.WithDependencies(entry.id).Where(e => e.id == entry.id || !_installed.ContainsKey(e.id)).Select(e => e.name);
            StartOperation(PackageInstaller.Install(_catalog, entry.id, _installed.Keys), $"Installing {string.Join(", ", added)}");
        }

        void RemovePackage(CatalogEntry entry)
        {
            List<string> remove = PackageInstaller.PackagesToRemove(_catalog, entry.id, _installed.Keys);
            string message = remove.Count > 1
                ? $"{Names(remove.Skip(1))} need{(remove.Count == 2 ? "s" : "")} {entry.name}: they will be removed too."
                : $"Remove {entry.name}?";
            if (!EditorUtility.DisplayDialog("Remove package", message + BrokenServicesWarning(remove), "Remove", "Cancel")) return;
            if (remove.Count > 1) ArchNetLog.Warning($"Removing {entry.name} also removes {Names(remove.Skip(1))}, which need it.");
            StartOperation(PackageInstaller.UninstallMany(remove), $"Removing {Names(remove)}");
        }

        void InstallAll()
        {
            int missing = PackageInstaller.PackagesForInstallAll(_catalog, _installed.Keys).Count();
            if (missing == 0)
            {
                ShowStatus("Every ArchNet package is already installed.");
                return;
            }
            if (!EditorUtility.DisplayDialog("Install all", $"Install the {missing} ArchNet packages that are missing?", "Install", "Cancel")) return;
            StartOperation(PackageInstaller.InstallAll(_catalog, _installed.Keys), $"Installing {missing} packages");
        }

        void UninstallAll()
        {
            // The Hub stays: without it, nothing could install them again.
            List<string> remove = _catalog.packages.Select(p => p.id).Where(id => _installed.ContainsKey(id) && id != HubPackage).ToList();
            if (remove.Count == 0)
            {
                ShowStatus("No ArchNet package to remove (the Hub stays).");
                return;
            }
            if (!EditorUtility.DisplayDialog("Uninstall all",
                    $"Remove the {remove.Count} installed ArchNet packages? The Hub stays." + BrokenServicesWarning(remove), "Uninstall all", "Cancel"))
                return;
            ArchNetLog.Warning($"Uninstalling every ArchNet package ({remove.Count}).");
            StartOperation(PackageInstaller.UninstallMany(remove), $"Removing {remove.Count} packages");
        }

        // Services created in Assets/ArchNet whose scripts use a package about to go.
        string BrokenServicesWarning(ICollection<string> removed)
        {
            ServiceManifest manifest = ServiceManifest.Load();
            string[] broken = _catalog.services
                .Where(s => manifest?.Find(s.id) != null && s.packages.SelectMany(p => _catalog.WithDependencies(p).Select(e => e.id)).Any(removed.Contains))
                .Select(s => s.name).ToArray();
            return broken.Length == 0 ? "" :
                $"\n\nWarning: the services {string.Join(", ", broken)} were created in Assets/ArchNet with these packages. Their scripts will not compile until you install the packages again (or delete those files).";
        }

        string Names(IEnumerable<string> ids) => string.Join(", ", ids.Select(id => _catalog.Find(id)?.name ?? id));

        // ---------- Contents ----------

        void RefreshContents()
        {
            if (_contentsList == null) return;
            _contentsList.Clear();
            int count = 0;
            foreach (Type type in LibraryTypes())
            {
                foreach (string guid in AssetDatabase.FindAssets("t:" + type.Name))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    Object asset = AssetDatabase.LoadMainAssetAtPath(path);
                    if (asset == null || asset.GetType() != type) continue;
                    _contentsList.Add(ContentRow(asset, type, path));
                    count++;
                }
            }
            if (count == 0)
                _contentsList.Add(Intro(LibraryTypes().Any()
                    ? "No library yet: pick a kind at the top, then New library."
                    : "No library package yet: install one from the Packages tab (category Library)."));
            if (_editedContent == null) ShowContent(null);
        }

        void ShowContent(Object asset)
        {
            _editedContent = asset;
            _contentEditor.Clear();
            if (asset == null)
            {
                _contentEditor.Add(Intro("Select a library on the left to edit it here."));
                return;
            }
            var header = Row("toolbar");
            header.Add(Title(asset.name));
            header.Add(Spacer());
            header.Add(new Button(() => EditorGUIUtility.PingObject(asset)) { text = "Show in Project" });
            _contentEditor.Add(header);
            _contentEditor.Add(new UnityEditor.UIElements.InspectorElement(asset));
        }

        void CreateLibrary()
        {
            Type type = LibraryTypes().ElementAtOrDefault(_newLibraryType.index);
            if (type == null) return;
            const string folder = RecipeRunner.RootFolder + "/Contents";
            Directory.CreateDirectory(folder);
            AssetDatabase.ImportAsset(folder);
            string path = EditorUtility.SaveFilePanelInProject("New library", type.Name, "asset", "Name of the new library.", folder);
            if (string.IsNullOrEmpty(path)) return;
            var library = CreateInstance(type);
            AssetDatabase.CreateAsset(library, path);
            ArchNetLog.Info($"Created the library {path}.");
            RefreshContents();
            ShowContent(library);
        }

        // Concrete libraries: anything deriving from ArchNet's KeyedLibrary<T>, or living in an ArchNet.Library namespace.
        static IEnumerable<Type> LibraryTypes() => TypeCache.GetTypesDerivedFrom<ScriptableObject>()
            .Where(type => !type.IsAbstract && !type.IsGenericType &&
                           (DerivesFromKeyedLibrary(type) || (type.Namespace ?? "").StartsWith("ArchNet.Library")))
            .OrderBy(type => type.Name);

        static bool DerivesFromKeyedLibrary(Type type)
        {
            for (Type parent = type.BaseType; parent != null; parent = parent.BaseType)
                if (parent.IsGenericType && parent.GetGenericTypeDefinition().Name == "KeyedLibrary`1") return true;
            return false;
        }

        VisualElement ContentRow(Object asset, Type type, string path)
        {
            var row = Card();
            row.AddToClassList("content-row");
            var line = Row();
            var icon = new Image { image = AssetPreview.GetMiniThumbnail(asset) };
            icon.AddToClassList("icon");
            line.Add(icon);
            line.Add(Title(asset.name));
            line.Add(Chip(ObjectNames.NicifyVariableName(type.Name), "chip"));
            line.Add(Spacer());
            var edit = new Button(() => ShowContent(asset)) { text = "Edit" };
            edit.AddToClassList("primary");
            line.Add(edit);
            row.Add(line);
            row.Add(Muted(path));
            return row;
        }

        // ---------- Journal ----------

        static VisualElement MakeJournalRow()
        {
            var row = new VisualElement();
            row.AddToClassList("journal-row");
            var line = Row();
            line.Add(new Label { name = "time" });
            line.Add(new Label { name = "level" });
            var message = new Label { name = "message" };
            message.AddToClassList("grow");
            line.Add(message);
            row.Add(line);
            row.Add(new Label { name = "hint" });
            return row;
        }

        void BindJournalRow(VisualElement row, int index)
        {
            LogEntry entry = _journalRows[index];
            row.Q<Label>("time").text = entry.time.Length > 11 ? entry.time.Substring(11) : entry.time;
            var level = row.Q<Label>("level");
            level.text = entry.level.ToString().ToUpperInvariant();
            level.EnableInClassList("level--warning", entry.level == LogLevel.Warning);
            level.EnableInClassList("level--error", entry.level == LogLevel.Error);
            row.Q<Label>("message").text = entry.message;
            var hint = row.Q<Label>("hint");
            hint.text = string.IsNullOrEmpty(entry.hint) ? "" : "→ " + entry.hint;
            hint.style.display = string.IsNullOrEmpty(entry.hint) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        void RefreshJournal()
        {
            if (_journal == null) return;
            int filter = Mathf.Max(0, _journalFilter.index);
            _journalRows.Clear();
            for (int i = ArchNetLog.Entries.Count - 1; i >= 0; i--) // newest first
            {
                LogEntry entry = ArchNetLog.Entries[i];
                if (filter == 1 && entry.level == LogLevel.Info) continue;
                if (filter == 2 && entry.level != LogLevel.Error) continue;
                _journalRows.Add(entry);
            }
            _journal.RefreshItems();
        }

        void OnLogAdded(LogEntry entry)
        {
            ShowStatus(entry.message, entry.level);
            RefreshJournal();
        }

        // ---------- Package Manager ----------

        void RefreshInstalled()
        {
            if (_listRequest == null) _listRequest = Client.List(true, false);
        }

        void StartOperation(Request request, string label)
        {
            if (request == null)
            {
                ArchNetLog.Info($"{label}: nothing to do.");
                return;
            }
            _operation = request;
            _operationLabel = label;
            ArchNetLog.Info(label + "…");
        }

        void Poll()
        {
            if (_listRequest != null && _listRequest.IsCompleted)
            {
                ListRequest finished = _listRequest;
                _listRequest = null;
                if (finished.Status == StatusCode.Success)
                {
                    _listRetries = 0;
                    _installed.Clear();
                    foreach (UnityEditor.PackageManager.PackageInfo package in finished.Result) _installed[package.name] = package.version;
                }
                else if (_listRetries < MaxListRetries)
                {
                    // The Package Manager refuses to list while it resolves or reloads: try again shortly.
                    _listRetries++;
                    rootVisualElement.schedule.Execute(RefreshInstalled).StartingIn(ListRetryDelayMs);
                }
                else
                {
                    _listRetries = 0;
                    string error = finished.Error?.message;
                    ArchNetLog.Warning($"Could not list the installed packages{(string.IsNullOrEmpty(error) ? "" : ": " + error)}.",
                        "Click Refresh in the Packages tab once Unity has finished loading.");
                }
                RefreshAll();
            }

            if (_operation != null && _operation.IsCompleted)
            {
                if (_operation.Status == StatusCode.Success)
                {
                    ArchNetLog.Info($"{_operationLabel}: done.");
                    // Usually the install reloads the scripts and the recipe resumes from there; if nothing
                    // needed recompiling, no reload comes, so resume it here (a reload would cancel this call).
                    rootVisualElement.schedule.Execute(RecipeRunner.ResumePending).StartingIn(NoReloadDelayMs);
                }
                else
                {
                    RecipeRunner.CancelPending();
                    ArchNetLog.Error($"{_operationLabel} failed: {_operation.Error?.message}", PackageInstaller.HintFor(_operation.Error?.message));
                }
                _operation = null;
                RefreshInstalled();
            }

            if (_download != null && _download.isDone)
            {
                if (_download.result == UnityWebRequest.Result.Success)
                {
                    PackageCatalog online = PackageCatalog.Parse(_download.downloadHandler.text);
                    if (online != null)
                    {
                        _catalog = _catalog.MergedWith(online);
                        RefreshAll();
                    }
                    else ArchNetLog.Warning("The online catalog could not be read; using the one shipped with the Hub.");
                }
                else ArchNetLog.Info($"Online catalog unavailable ({_download.error}); using the one shipped with the Hub.");
                _download.Dispose();
                _download = null;
            }

            if (Busy != _wasBusy)
            {
                _wasBusy = Busy;
                RefreshServices();
                RefreshPackages();
                if (!Busy) ShowStatus($"Ready · {_installed.Keys.Count(id => id.StartsWith("com.archnet."))} ArchNet packages installed.");
            }
            if (Busy) ShowStatus(_operation != null ? _operationLabel + "…" : "Reading installed packages…");
        }

        void RefreshAll()
        {
            RefreshServices();
            RefreshPackages();
            RefreshJournal();
        }

        void ShowStatus(string message, LogLevel level = LogLevel.Info)
        {
            if (_status == null) return;
            _status.text = message;
            _status.EnableInClassList("status--warning", level == LogLevel.Warning);
            _status.EnableInClassList("status--error", level == LogLevel.Error);
        }

        static void Reveal(string folder)
        {
            Object asset = AssetDatabase.LoadAssetAtPath<Object>(folder);
            if (asset == null)
            {
                ArchNetLog.Warning($"{folder} does not exist any more.", "Click Update to create the service again.");
                return;
            }
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        // ---------- Small builders ----------

        static VisualElement Row(string className = null)
        {
            var row = new VisualElement();
            row.AddToClassList("row");
            if (className != null) row.AddToClassList(className);
            return row;
        }

        static VisualElement Spacer()
        {
            var spacer = new VisualElement();
            spacer.AddToClassList("grow");
            return spacer;
        }

        static VisualElement Card()
        {
            var card = new VisualElement();
            card.AddToClassList("card");
            return card;
        }

        static Label Title(string text)
        {
            var label = new Label(text);
            label.AddToClassList("card-title");
            return label;
        }

        static Label Description(string text)
        {
            var label = new Label(text);
            label.AddToClassList("description");
            return label;
        }

        static Label Muted(string text)
        {
            var label = new Label(text);
            label.AddToClassList("muted");
            return label;
        }

        static Label Intro(string text)
        {
            var label = new Label(text);
            label.AddToClassList("intro");
            return label;
        }

        static Label Chip(string text, string className)
        {
            var chip = new Label(text);
            chip.AddToClassList("chip");
            chip.AddToClassList(className);
            return chip;
        }

        static Button LinkButton(string text, string url)
        {
            var button = new Button(() => Application.OpenURL(url)) { text = text, tooltip = url };
            button.AddToClassList("link");
            return button;
        }
    }
}

// ArchNet · Hub — recipe runner
// Finds every recipe, runs one, and carries on across script reloads: installing a package
// and compiling new scripts both restart the Editor's scripts, so the next step is kept in
// SessionState and resumed afterwards.
// Docs: https://archnet-plateform.fr/#ArchNetHub

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace ArchNet.Installer.Editor
{
    [InitializeOnLoad]
    public static class RecipeRunner
    {
        public const string RootFolder = "Assets/ArchNet";
        const string PendingKey = "ArchNet.Hub.Pending";
        const string CreateStage = "create";
        const string AfterCompileStage = "after";

        static List<ServiceRecipe> _recipes;

        /// <summary>Raised when a service was created or updated.</summary>
        public static event Action Changed;

        const string ReloadKey = "ArchNet.Hub.ReloadCount";

        static RecipeRunner()
        {
            SessionState.SetInt(ReloadKey, SessionState.GetInt(ReloadKey, 0) + 1);
            EditorApplication.delayCall += ResumePending;
        }

        public static IReadOnlyList<ServiceRecipe> Recipes => _recipes ??= TypeCache.GetTypesDerivedFrom<ServiceRecipe>()
            .Where(type => !type.IsAbstract && type.GetConstructor(Type.EmptyTypes) != null)
            .Select(type => (ServiceRecipe)Activator.CreateInstance(type))
            .OrderBy(recipe => recipe.DisplayName)
            .ToList();

        public static ServiceRecipe Find(string id) => Recipes.FirstOrDefault(recipe => recipe.Id == id);

        public static string FolderOf(ServiceRecipe recipe) => $"{RootFolder}/{recipe.FolderName}";

        /// <summary>True when the service was created in this project (listed in services.json).</summary>
        public static bool IsCreated(string id) => ServiceManifest.Load()?.Find(id) != null;

        /// <summary>"true" / "false" for each id, ready for {{#if id}} blocks in templates.</summary>
        public static Dictionary<string, string> CreatedFlags(params string[] ids) =>
            ids.ToDictionary(id => id, id => IsCreated(id) ? "true" : "false");

        public static bool HasPending => Pending().Count > 0;

        /// <summary>Runs the recipe once the packages being installed have loaded.</summary>
        public static void RunAfterInstall(string id) => Push(CreateStage, id);

        public static void CancelPending() => SessionState.EraseString(PendingKey);

        static List<string> Pending() => SessionState.GetString(PendingKey, "")
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();

        static void Push(string stage, string id)
        {
            List<string> pending = Pending();
            pending.Add($"{stage}|{id}");
            SessionState.SetString(PendingKey, string.Join(";", pending));
        }

        /// <summary>Creates (or updates) the service and the services it needs. Returns false on error.</summary>
        public static bool Run(ServiceRecipe recipe) => Run(recipe, new HashSet<string>());

        static bool Run(ServiceRecipe recipe, HashSet<string> running)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                ArchNetLog.Error($"{recipe.DisplayName} cannot be created during Play Mode.", "Leave Play Mode, then click again.");
                return false;
            }
            if (!running.Add(recipe.Id))
            {
                ArchNetLog.Error($"{recipe.DisplayName} needs itself through its required services.", "Report it on the package's GitHub page.");
                return false;
            }

            ServiceManifest manifest = ServiceManifest.Load();
            if (manifest == null) return false;

            foreach (string required in recipe.RequiredServices)
            {
                if (manifest.Find(required) != null) continue;
                ServiceRecipe dependency = Find(required);
                if (dependency == null)
                {
                    ArchNetLog.Error($"{recipe.DisplayName} needs the \"{required}\" service, which is not installed.", "Install it from the Services tab first.");
                    return false;
                }
                if (!Run(dependency, running)) return false;
                if (Pending().Any(step => step.StartsWith($"{AfterCompileStage}|{dependency.Id}|")))
                {
                    // The dependency's scripts compile first; this service carries on after the reload.
                    Push(CreateStage, recipe.Id);
                    return true;
                }
                manifest = ServiceManifest.Load();
                if (manifest == null) return false;
            }

            ArchNetLog.Info($"Creating {recipe.DisplayName}…");
            RecipeContext context = ContextFor(recipe, manifest);
            if (!Try(recipe, manifest, () => recipe.Create(context))) return false;

            if (context.WroteScripts)
            {
                Push(AfterCompileStage, $"{recipe.Id}|{ReloadCount}");
                ArchNetLog.Info($"Compiling the scripts of {recipe.DisplayName}…");
                AssetDatabase.Refresh();
                return true;
            }
            return Finish(recipe, manifest, context);
        }

        static bool Finish(ServiceRecipe recipe, ServiceManifest manifest, RecipeContext context)
        {
            if (!Try(recipe, manifest, () => recipe.AfterScriptsCompiled(context))) return false;
            context.Record.updated = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            manifest.Save();
            AssetDatabase.Refresh();
            ArchNetLog.Info($"{recipe.DisplayName} is ready in {context.Folder}.");
            Changed?.Invoke();
            return true;
        }

        static bool Try(ServiceRecipe recipe, ServiceManifest manifest, Action step)
        {
            try
            {
                step();
                manifest.Save();
                return true;
            }
            catch (Exception exception)
            {
                manifest.Save(); // keep track of the files already written
                AssetDatabase.Refresh();
                ArchNetLog.Error($"{recipe.DisplayName} could not be created: {exception.Message}",
                    "The full error is in the Unity console. Files already written are kept; click again once it is fixed.");
                UnityEngine.Debug.LogException(exception);
                return false;
            }
        }

        static RecipeContext ContextFor(ServiceRecipe recipe, ServiceManifest manifest) =>
            new RecipeContext(recipe.Id, FolderOf(recipe), manifest.rootNamespace, manifest.GetOrAdd(recipe.Id));

        /// <summary>Runs the steps left waiting for a reload. Safe to call at any time.</summary>
        public static void ResumePending()
        {
            if (Pending().Count == 0) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                // Recipes cannot run in Play Mode: carry on once back in Edit Mode.
                EditorApplication.playModeStateChanged -= ResumeAfterPlayMode;
                EditorApplication.playModeStateChanged += ResumeAfterPlayMode;
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += ResumePending;
                return;
            }
            // One step at a time, saved before it runs: a step that fails or reloads never loses the next ones.
            while (true)
            {
                List<string> pending = Pending();
                if (pending.Count == 0) return;
                string step = pending[0];
                if (WaitsForReload(step)) return; // resumed by the static constructor after the reload
                SessionState.SetString(PendingKey, string.Join(";", pending.Skip(1)));
                try { Resume(step); }
                catch (Exception exception)
                {
                    ArchNetLog.Error($"Could not resume \"{step}\": {exception.Message}", "The full error is in the Unity console; click the service again.");
                    UnityEngine.Debug.LogException(exception);
                }
                if (EditorApplication.isCompiling)
                {
                    EditorApplication.delayCall += ResumePending;
                    return;
                }
            }
        }

        // An "after compile" step pushed before the latest script reload must wait for the next one.
        static bool WaitsForReload(string step)
        {
            string[] parts = step.Split('|');
            return parts[0] == AfterCompileStage && parts.Length > 2 && int.TryParse(parts[2], out int pushedAt) && pushedAt >= ReloadCount;
        }

        static int ReloadCount => SessionState.GetInt(ReloadKey, 0);

        static void ResumeAfterPlayMode(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.playModeStateChanged -= ResumeAfterPlayMode;
            ResumePending();
        }

        static void Resume(string step)
        {
            string[] parts = step.Split('|');
            string stage = parts[0];
            string id = parts.Length > 1 ? parts[1] : "";
            ServiceRecipe recipe = Find(id);
            if (recipe == null)
            {
                ArchNetLog.Error($"No recipe \"{id}\" was found after installing its packages.",
                    "Check the Packages tab: an install may have failed, or this package version has no recipe yet.");
                return;
            }

            if (stage == AfterCompileStage)
            {
                ServiceManifest manifest = ServiceManifest.Load();
                if (manifest != null) Finish(recipe, manifest, ContextFor(recipe, manifest));
            }
            else Run(recipe);
        }
    }
}

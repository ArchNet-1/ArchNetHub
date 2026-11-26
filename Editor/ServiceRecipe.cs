// ArchNet · Hub — service recipe
// A recipe is what an "Add … to the game" button runs. Each ArchNet package ships its own
// recipe (in an Editor assembly that only compiles when the Hub is installed); the Hub finds
// every recipe by itself, so adding a service never means changing the Hub.
// Docs: https://archnet-plateform.fr/#ArchNetHub

using System;

namespace ArchNet.Installer.Editor
{
    public abstract class ServiceRecipe
    {
        /// <summary>Stable id, the same as the service's id in catalog.json (e.g. "credits").</summary>
        public abstract string Id { get; }

        public abstract string DisplayName { get; }

        public abstract string Description { get; }

        public virtual string Category => "Game";

        /// <summary>Folder under Assets/ArchNet/ that receives the files created for the game.</summary>
        public virtual string FolderName => DisplayName.Replace(" ", "");

        /// <summary>Ids of services this one plugs into; the Hub creates them first.</summary>
        public virtual string[] RequiredServices => Array.Empty<string>();

        /// <summary>Writes the scripts, data, prefabs and settings of the service.</summary>
        public abstract void Create(RecipeContext context);

        /// <summary>
        /// Runs once the scripts written by Create have compiled (e.g. to add a generated
        /// component to a prefab). Runs at once when Create wrote no script.
        /// </summary>
        public virtual void AfterScriptsCompiled(RecipeContext context) { }

        /// <summary>True when the service has its own window to edit what was created.</summary>
        public virtual bool HasTool => false;

        public virtual void OpenTool() { }
    }
}

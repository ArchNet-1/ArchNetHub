<p align="center">
  <a href="https://archnet-plateform.fr"><img src="https://archnet-plateform.fr/ArchNetLogoWithBck.png" alt="ArchNet" width="240"></a>
</p>

# ArchNet · Hub

One window to build your game from ArchNet's free packages: add complete services in one click, install packages with what they need, browse your content libraries, and read a journal of everything that happened.

- Unity **6000.0** or newer · Editor only · No dependency
- Part of [ArchNet](https://archnet-plateform.fr), free and open-source Unity packages

## Install

*Window > Package Manager > + > Install package from git URL…*

```
https://github.com/ArchNet-1/ArchNetHub.git
```

Then open *ArchNet > Hub*.

## The four tabs

| Tab | What it does |
|---|---|
| **Services** | "Add to game" buttons. The Hub installs the packages the service needs, then creates its scripts, data and prefabs under `Assets/ArchNet/<Service>/`. Those files are yours: change them freely. **Update** never overwrites a file you changed; the new version is written next to it as `<file>.new`. |
| **Packages** | Every ArchNet package: search, category filter, **Install** / **Update** / **Remove**. The packages it needs are added in the same step (Unity cannot resolve dependencies between Git packages by itself). **Install all** / **Uninstall all** (the Hub stays). Removing a package also removes what needs it, after a warning. |
| **Contents** | Every content library of the project (colors, images, audio, prefabs…), one click to edit it. |
| **Journal** | Every action and error, newest first, each error with a hint saying what to do. Also written to `Library/ArchNet/hub.log`. **Copy report** gives what to paste in a GitHub issue. |

**Namespace** (Services tab): the namespace of the scripts created for your game, `Company.Product` from the Player Settings by default. Stored in `Assets/ArchNet/services.json`, which also lists every file the Hub created; keep it in version control.

*Developer* (Packages tab): set **Local packages folder** to a folder holding the repositories side by side to install from disk instead of GitHub.

The catalog ships with the Hub and is completed from `https://archnet-plateform.fr/catalog.json` when the site publishes it.

## Write a recipe for your package

A service is a class deriving from `ServiceRecipe`, in an Editor assembly of your package that only compiles when the Hub is there:

```json
"references": ["ArchNet.Installer.Editor"],
"defineConstraints": ["ARCHNET_HUB"],
"versionDefines": [{ "name": "com.archnet.hub", "expression": "2.0.0", "define": "ARCHNET_HUB" }]
```

```csharp
public sealed class CreditsRecipe : ServiceRecipe
{
    public override string Id => "credits";
    public override string DisplayName => "Credits";
    public override string Description => "A scrolling credits screen and a tool to edit its names.";
    public override string[] RequiredServices => new[] { "bootstrap" };

    public override void Create(RecipeContext context)
    {
        context.WriteTemplate("Scripts/GameCredits.cs", RecipeContext.LoadTemplate("Packages/com.archnet.module.credit/Editor/Templates/GameCredits.cs.txt"));
    }

    // Runs once GameCredits.cs has compiled: add it to a prefab, etc.
    public override void AfterScriptsCompiled(RecipeContext context) { }
}
```

The Hub finds the recipe by itself. List the service in `catalog.json` (`services`) so the Services tab can offer it before the package is installed.

## License

MIT, see [LICENSE.md](LICENSE.md).

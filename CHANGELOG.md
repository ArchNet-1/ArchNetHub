# Changelog

## [2.0.0] - 2026-11-25

### Renamed
- Repository `ArchNetInstaller` is now `ArchNetHub`, package id `com.archnet.installer` is now the one in package.json. Remove the old id from Packages/manifest.json and install this one.

The Package Installer becomes the **ArchNet Hub**.

### Added
- *ArchNet > Hub* window rebuilt with UI Toolkit, in four tabs: Services, Packages, Contents, Journal.
- **Services**: "Add to game" buttons that install the packages a service needs, then run its recipe (scripts, data, prefabs under `Assets/ArchNet/<Service>/`). Recipes ship in each package and are found automatically (`ServiceRecipe`).
- `Assets/ArchNet/services.json` remembers every file created; a file the developer changed is never overwritten (the new version is written as `<file>.new`).
- Namespace of the generated code, defaulting to `Company.Product` from the Player Settings.
- Templates: `{{NAMESPACE}}`, `{{KEY}}` values and `{{#if service}} … {{/if}}` blocks (`RecipeRunner.CreatedFlags`), so generated code only mentions services that exist.
- Services in the catalog: Bootstrap, Credits, Popups, Save, Data, Settings, Loading screen, Gameplay, Main menu, Inputs, Controls, Responsive UI, Localization, Notifications, API.
- **Contents**: every content library of the project on the left, edited on the right; **New library** creates one of any kind under `Assets/ArchNet/Contents`.
- **Journal**: every action and error, with a hint saying what to do, also written to `Library/ArchNet/hub.log`; "Copy report" for GitHub issues.
- **Install all** / **Uninstall all** (Packages tab; the Hub stays). Removing a package also removes the installed packages that need it, after a warning that lists them and the services created in Assets/ArchNet that would stop compiling.
- Deprecated packages (catalog `deprecated`) show a **Deprecated** chip and are left out of **Install all**: Audio, Video and Shadow, replaced by the Settings Module.
- Plain-words hints for Package Manager errors (private repository, no network, Git missing, wrong local folder).
- Edit Mode tests.

### Changed
- The menu is now *ArchNet > Hub* (was *ArchNet > Package Installer*).

## [1.0.0] - 2026-11-25

### Renamed
- Repository `ArchNetInstaller` is now `ArchNetHub`, package id `com.archnet.installer` is now the one in package.json. Remove the old id from Packages/manifest.json and install this one.

### Added
- *ArchNet > Package Installer* window: search, category filter, installed version, Install / Update / Remove.
- One-click install of a package and its ArchNet dependencies in a single Package Manager operation.
- Catalog shipped with the package, refreshed from archnet-plateform.fr/catalog.json when available.
- Developer option to install from a local folder.

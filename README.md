<p align="center">
  <a href="https://archnet-plateform.fr"><img src="https://archnet-plateform.fr/ArchNetLogoWithBck.png" alt="ArchNet" width="240"></a>
</p>

# ArchNet · Package Installer

Browse every ArchNet package from one window and install any of them, with the ArchNet packages it needs, in one click.

- Unity **6000.0** or newer · Editor only · No dependency
- Part of [ArchNet](https://archnet-plateform.fr), free and open-source Unity packages

## Install

*Window > Package Manager > + > Install package from git URL…*

```
https://github.com/ArchNet-1/ArchNetInstaller.git
```

## Use

1. Open *ArchNet > Package Installer*.
2. Search or filter by category, then click **Install**. The packages it needs (for example Library Core for every library) are added in the same step.
3. **Update** appears when a newer version is in the catalog; **Remove** warns you when another installed package needs it.

Why: Unity's Package Manager cannot resolve dependencies between Git packages by itself. The installer adds them together, so every package installs in one click.

*Developer*: set **Local packages folder** to a folder holding the repositories side by side to install from disk instead of GitHub.

The list comes with the installer and is refreshed from `https://archnet-plateform.fr/catalog.json` when the site publishes it.

## License

See [LICENSE.md](LICENSE.md).

# Contributing to MarkLens

Thanks for helping out. This page covers what is specific to this repository; the
[README](README.md#building) has the build commands.

## Branches and pull requests

- Branch from `main` and open your pull request against `main`. Pull requests are squash-merged.
- Keep a pull request to one topic. Describe what changed and why; the template asks a few questions.
- Every pull request runs **Pull request checks**, whose job **Build and smoke test** must pass before
  merging. It has read-only access and no secrets. For pull requests from forks the maintainer approves
  each run first, so a check that stays at "waiting" for a while is expected.
- Releases are separate from merging: the maintainer pushes a `vX.Y.Z` tag, the **Release** workflow
  checks the tag is exactly `vX.Y.Z`, builds and smoke tests that version, and publishing waits for the
  maintainer's approval.

## What not to change

- **The version** in `src/MarkLens/MarkLens.csproj` stays `0.0.0-dev`. A release takes its version from its
  tag, so there is no version bump to merge.
- **The workflows** in `.github/workflows` and the shared build steps in `.github/actions/package`.
  Actions are pinned to commit hashes and publishing is gated by an environment; changes there are the
  maintainer's (`.github/CODEOWNERS`).
- **Pinned checksums** in `tools/fetch-vendor.ps1` and `tools/smoke-test.ps1`, except as part of an upgrade
  described below.

## Local loop

```powershell
.\tools\package.ps1                                              # build + zip into dist\
.\tools\package.ps1 -NppPluginsDir "C:\Program Files\Notepad++\plugins"   # ...and install (as administrator)
.\tools\smoke-test.ps1 -Zip dist\MarkLens-<version>-x64.zip      # cold install into portable 8.3.1
```

- The smoke test closes every running Notepad++ and deletes `%TEMP%\MarkLens.log` before it starts.
- Errors inside the plugin are written to `%TEMP%\MarkLens.log` and shown once in a dialog.
- A Debug build (`package.ps1 -Configuration Debug`) reads `DevToolsPort` from `MarkLens.ini` and lets you
  attach browser DevTools to the preview at `http://localhost:<port>` for debugging `viewer.js`.
- The bundled libraries are only fetched when `src\MarkLens\assets\vendor\.versions` is missing. After editing
  `tools\fetch-vendor.ps1`, run it with `-Force` or delete that file, or the build keeps the old copies.
- Files are stored with LF and checked out with CRLF (see `.gitattributes`); `.editorconfig` has the
  indentation rules. Build from a normal Windows checkout to avoid every file showing as modified.

## Things that must change together

| If you change | Also update |
|---|---|
| A bundled library version (highlight.js, KaTeX, Mermaid) | version and SHA-256 in `tools/fetch-vendor.ps1`; the version heading in `THIRD-PARTY-NOTICES.txt`; for KaTeX also the CDN font URL in `ViewerPanel.cs` used by Export as HTML |
| A bundled library file name | `tools/fetch-vendor.ps1`, `Libraries.cs`, `tools/package.ps1`, `assets/viewer.js` and the export code in `ViewerPanel.cs` |
| A NuGet package | the DLL list in `tools/package.ps1` (the package step fails with "missing build output" otherwise) and `THIRD-PARTY-NOTICES.txt` |
| The menu text "Markdown Viewer", the plugin name or the panel title | `tools/smoke-test.ps1`, which finds the viewer by those names |
| The menu text "Show in Preview" | nothing in code (`Main.ShowInPreviewCommand` feeds both the menu and `Npp/ContextMenuXml.cs`), but users who added it to the right-click menu must tick the Settings box again |
| A message between the page and the plugin | both ends: the `switch` in `assets/viewer.js` and `HandleWebMessage` in `ViewerPanel.cs` |
| `docs/sample.md` | nothing. The smoke test only opens it; its content is free to improve |
| A field in `Settings.cs` | nothing, but the INI key is the field name, so renaming a field resets that setting for every user |
| The minimum Notepad++ version | the badge and requirements in `README.md`, `npp-compatible-versions` in `.github/workflows/release.yml`, and the version and SHA-256 in `tools/smoke-test.ps1` |

## How the plugin is put together

- `src/MarkLens/Npp/` talks to Notepad++ and Scintilla. `PluginExports.cs` holds the five native entry
  points, which `tools/DllExporter` rewrites after the build; keep that class free of Markdig and WebView2
  types so the assembly resolver is installed before they are needed. Structures and offsets are x64 only.
- `src/MarkLens/Rendering/` turns Markdown into HTML with Markdig.
- `src/MarkLens/UI/ViewerPanel.cs` hosts WebView2. The viewer assets are served from a virtual host, the
  document folder is mapped for relative images, and `assets/viewer.js` talks back through
  `chrome.webview.postMessage`.
- Scope: x64, .NET Framework 4.8, and no new runtime dependencies without discussing it first, because every
  DLL ships inside the zip.

## Security

Please report vulnerabilities privately as described in [SECURITY.md](SECURITY.md), not in an issue.

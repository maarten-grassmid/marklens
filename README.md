<div align="center">

# MarkLens

**A fast, modern Markdown preview for Notepad++**

Live preview · Table of contents · Jump from preview to source · Works with huge files

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
![Notepad++ 8.x x64](https://img.shields.io/badge/Notepad%2B%2B-8.x%20x64-90E59A.svg)
![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6.svg)

[Features](#features) · [Install](#install) · [Usage](#usage) · [Settings](#settings) · [Building](#building)

<img src="docs/images/screenshot.png" alt="MarkLens showing a Markdown file next to its live preview in Notepad++" width="100%">

</div>

## Features

- **Select and copy anything** in the preview, with a right-click menu for Copy and Copy as Markdown.
- **Jump to the source.** Select text in the preview and choose *Edit Selection*. The cursor moves to the
  Markdown that produced it, and those lines are highlighted in the editor.
- **Table of contents** sidebar with a filter box. It highlights the section you are reading.
- **Two-way scroll sync** between the editor and the preview.
- **Clickable task lists**: ticking a box updates `[ ]` / `[x]` in your file.
- **Built for large files**: rendering runs in the background and only changed parts are redrawn.
- **Rich Markdown**: tables, footnotes, alerts (`> [!NOTE]`), definition lists, task lists, highlight,
  sub/superscript, abbreviations and more.
- **Optional extras**: code highlighting, Mermaid diagrams, KaTeX math and emoji, bundled so the preview
  works offline.
- **Light and dark themes** that follow Notepad++, plus your own stylesheet if you want one.
- **Export** to HTML or PDF, or print.

## Install

> [!NOTE]
> **Requirements:** Windows 10 or 11, **64-bit** Notepad++ 8.x, .NET Framework 4.8 and the
> [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/). Current Windows
> versions include .NET Framework 4.8 and WebView2. Some older or LTSC installations may need WebView2 installed.

1. Download `MarkLens-<version>-x64.zip` from the [Releases](../../releases) page.
2. Close Notepad++.
3. Create the folder `C:\Program Files\Notepad++\plugins\MarkLens\` and extract the zip into it.
   `MarkLens.dll` should end up directly inside that folder. Writing to Program Files needs administrator rights.
4. Start Notepad++ and open **Plugins › MarkLens › Markdown Viewer**.

**Uninstall:** close Notepad++ and delete `C:\Program Files\Notepad++\plugins\MarkLens\`. To also remove your
settings, delete `%AppData%\Notepad++\plugins\config\MarkLens\`.

**Try it:** open [`docs/sample.md`](docs/sample.md), which shows everything MarkLens can render.

## Usage

Open a Markdown file and press <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>M</kbd> to show or hide the preview.
You can dock the panel on any side or drag it out to float, and Notepad++ remembers where you put it.

| Action | How |
|---|---|
| Show / hide the preview | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>M</kbd> or **Plugins › MarkLens › Markdown Viewer** |
| Jump to the Markdown behind a selection | Select, then right-click › **Edit Selection** or <kbd>Ctrl</kbd>+<kbd>E</kbd> |
| Jump to the Markdown behind a block | <kbd>Ctrl</kbd>+double-click it |
| Copy text / copy as Markdown | <kbd>Ctrl</kbd>+<kbd>C</kbd> / right-click › **Copy as Markdown** |
| Find in the preview | <kbd>Ctrl</kbd>+<kbd>F</kbd>, then <kbd>Enter</kbd> or <kbd>F3</kbd> |
| Go to a heading | Click it in the table of contents |
| Tick a task | Click its checkbox |
| Zoom | <kbd>Ctrl</kbd>+mouse wheel |
| Export or print | Right-click › **Export as HTML / PDF** or **Print** |

> [!TIP]
> The yellow source highlight stays while you move around inside it. It disappears when you click
> elsewhere, edit the text or switch documents.

## Settings

Open **Plugins › MarkLens › Settings…**

| Setting | Default |
|---|---|
| Theme (follow Notepad++, light, dark) | Follow Notepad++ |
| Table of contents | On |
| Synchronize scrolling | On |
| Markdown file extensions | `.md` `.markdown` `.mdown` `.mkd` and more |
| Live update delay | 250 ms |
| Custom stylesheet | None |
| Code highlighting · Mermaid · Math · Emoji | Off |

> [!NOTE]
> Code highlighting, Mermaid and Math use libraries that ship inside the release. If one is missing, MarkLens
> keeps working without that feature, and Settings marks it "not installed" with a link to reinstall.
> Emoji is built in and needs no extra library.

> [!NOTE]
> **Export as HTML** writes a single file with the rendered document in the light theme. Code highlighting
> and diagrams are kept as rendered. With Math on, the file loads the KaTeX fonts from the internet.

## Troubleshooting

| Problem | What to do |
|---|---|
| MarkLens is not in the Plugins menu | Check that you use **64-bit** Notepad++ and that `MarkLens.dll` is directly inside `plugins\MarkLens\`. |
| The panel says the WebView2 runtime is required | Install the [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) and restart Notepad++. |
| "is not a Markdown file" | Add the file's extension in **Settings › Markdown file extensions**. |
| An error message appeared | Details are written to `%TEMP%\MarkLens.log`. Please include it when you open an issue. |

## Issues and contributions

Found a bug or have an idea? [Open an issue](../../issues). Pull requests are welcome too.

## Building

You need **Windows**, **PowerShell** and the **.NET SDK 8 or newer**. The first build needs an internet
connection to restore NuGet packages and download the optional client libraries.

```powershell
git clone https://github.com/maarten-grassmid/marklens.git
cd marklens

# build and package into dist\
.\tools\package.ps1

# or build, package and install into Notepad++ (run as administrator)
.\tools\package.ps1 -NppPluginsDir "C:\Program Files\Notepad++\plugins"
```

The libraries (highlight.js, KaTeX, Mermaid) are downloaded from npm once into `src\MarkLens\assets\vendor\`.
After the build, `tools\DllExporter` adds the native entry points Notepad++ needs to load the plugin.

**Releases:** pushing a tag such as `v1.2.0` builds the zip with that version and publishes it as a GitHub
release.

## License

MarkLens is released under the [MIT License](LICENSE).

It bundles Markdig, the WebView2 SDK, highlight.js, KaTeX and Mermaid, each under its own license. See
[THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt), which is also included in every release.

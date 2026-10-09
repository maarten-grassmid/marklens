# Builds the plugin and produces dist/MarkLens/ plus a release zip
# laid out the way Notepad++ expects (plugins/MarkLens/...).
param(
    [string]$Configuration = 'Release',
    [string]$NppPluginsDir,  # optional: also deploy into this Notepad++ plugins folder
    [string]$Version         # optional: overrides the version in the project (e.g. from a git tag)
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $root 'src\MarkLens\MarkLens.csproj'

$buildArgs = @('build', $project, '-c', $Configuration, '-p:Platform=x64')
if ($Version) { $buildArgs += "-p:Version=$Version" }
dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw 'build failed' }

$bin = Join-Path $root "src\MarkLens\bin\x64\$Configuration"
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist 'MarkLens'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

$files = @(
    'MarkLens.dll',
    'Markdig.dll',
    'Microsoft.Web.WebView2.Core.dll',
    'Microsoft.Web.WebView2.WinForms.dll',
    'WebView2Loader.dll',
    'System.Buffers.dll',
    'System.Memory.dll',
    'System.Numerics.Vectors.dll',
    'System.Runtime.CompilerServices.Unsafe.dll'
)
foreach ($f in $files) {
    $src = Join-Path $bin $f
    # Never produce a package that would fail to load in Notepad++.
    if (-not (Test-Path $src)) { throw "missing build output: $f" }
    Copy-Item $src $stage
}
foreach ($lib in 'highlight\highlight.min.js', 'katex\katex.min.js', 'katex\fonts', 'mermaid\mermaid.min.js') {
    if (-not (Test-Path (Join-Path $bin "assets\vendor\$lib"))) { throw "missing bundled library: $lib" }
}
Copy-Item (Join-Path $bin 'assets') $stage -Recurse
Copy-Item (Join-Path $root 'LICENSE') $stage
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.txt') $stage

$version = (Get-Item (Join-Path $stage 'MarkLens.dll')).VersionInfo.ProductVersion -replace '\+.*$', ''
$zip = Join-Path $dist "MarkLens-$version-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Write-Host "package: $zip"

if ($NppPluginsDir) {
    $target = Join-Path $NppPluginsDir 'MarkLens'
    New-Item -ItemType Directory -Force $target | Out-Null
    Copy-Item (Join-Path $stage '*') $target -Recurse -Force
    Write-Host "deployed to $target"
}

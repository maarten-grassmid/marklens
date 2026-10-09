# Downloads the optional client-side libraries (highlight.js, KaTeX, Mermaid)
# from the npm registry into src/MarkLens/assets/vendor.
# They are bundled with the plugin so rendering works fully offline.
param(
    [string]$Destination = (Join-Path $PSScriptRoot '..\src\MarkLens\assets\vendor'),
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$packages = @(
    @{ Name = '@highlightjs/cdn-assets'; Version = '11.11.1'; Dir = 'highlight';
       Files = @('highlight.min.js', 'styles/github.min.css', 'styles/github-dark.min.css') },
    @{ Name = 'katex'; Version = '0.16.22'; Dir = 'katex';
       Files = @('dist/katex.min.js', 'dist/katex.min.css', 'dist/contrib/auto-render.min.js', 'dist/fonts') },
    @{ Name = 'mermaid'; Version = '11.12.0'; Dir = 'mermaid';
       Files = @('dist/mermaid.min.js') }
)

$Destination = [IO.Path]::GetFullPath($Destination)
$stamp = Join-Path $Destination '.versions'
$wanted = ($packages | ForEach-Object { "$($_.Name)@$($_.Version)" }) -join "`n"

if (-not $Force -and (Test-Path $stamp) -and ((Get-Content $stamp -Raw).Trim() -eq $wanted.Trim())) {
    Write-Host 'vendor assets up to date'
    return
}

$work = Join-Path ([IO.Path]::GetTempPath()) ("marklens-vendor-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $work | Out-Null
try {
    if (Test-Path $Destination) { Remove-Item $Destination -Recurse -Force }
    New-Item -ItemType Directory -Force $Destination | Out-Null

    foreach ($p in $packages) {
        $leaf = ($p.Name -split '/')[-1]
        $url = "https://registry.npmjs.org/$($p.Name)/-/$leaf-$($p.Version).tgz"
        $tgz = Join-Path $work "$leaf.tgz"
        Write-Host "fetching $($p.Name)@$($p.Version)"
        Invoke-WebRequest -Uri $url -OutFile $tgz -UseBasicParsing

        $extract = Join-Path $work $leaf
        New-Item -ItemType Directory -Force $extract | Out-Null
        tar -xzf $tgz -C $extract
        if ($LASTEXITCODE -ne 0) { throw "failed to extract $tgz" }

        $pkgRoot = Join-Path $extract 'package'
        $outDir = Join-Path $Destination $p.Dir
        New-Item -ItemType Directory -Force $outDir | Out-Null
        foreach ($f in $p.Files) {
            $src = Join-Path $pkgRoot $f
            if (-not (Test-Path $src)) { throw "missing $f in $($p.Name)" }
            $target = Join-Path $outDir (Split-Path $f -Leaf)
            Copy-Item $src $target -Recurse -Force
        }
        $license = Get-ChildItem $pkgRoot -Filter 'LICENSE*' | Select-Object -First 1
        if ($license) { Copy-Item $license.FullName (Join-Path $outDir 'LICENSE') -Force }
    }

    Set-Content -Path $stamp -Value $wanted -NoNewline
    Write-Host "vendor assets written to $Destination"
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}

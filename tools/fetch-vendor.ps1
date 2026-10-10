# Downloads the optional client-side libraries (highlight.js, KaTeX, Mermaid)
# from the npm registry into src/MarkLens/assets/vendor.
# They are bundled with the plugin so rendering works fully offline.
# Each tarball is checked against a pinned SHA-256 before anything is extracted.
param(
    [string]$Destination = (Join-Path $PSScriptRoot '..\src\MarkLens\assets\vendor'),
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$packages = @(
    @{ Name = '@highlightjs/cdn-assets'; Version = '11.11.1'; Dir = 'highlight';
       Sha256 = '28a06bf6ebcc2218c6e842bdb4f1c8353508a8cada59e85110f91892f5726195';
       Files = @('highlight.min.js', 'styles/github.min.css', 'styles/github-dark.min.css') },
    # Export as HTML reads the KaTeX version from the .versions stamp to pick matching CDN fonts.
    @{ Name = 'katex'; Version = '0.16.22'; Dir = 'katex';
       Sha256 = 'e9e0d167db3175481cbadaff38e8d90b130f6a3ddb451a47e43c577fd511f365';
       Files = @('dist/katex.min.js', 'dist/katex.min.css', 'dist/contrib/auto-render.min.js', 'dist/fonts') },
    @{ Name = 'mermaid'; Version = '11.17.2'; Dir = 'mermaid';
       Sha256 = '6ad2f42c3fc26bbf9e45cbb6d11898972573ea52b33a5f4ff51952899f950ffd';
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

        # Hash through .NET directly: the Get-FileHash cmdlet is not available everywhere.
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try { $actual = [BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($tgz))).Replace('-', '').ToLowerInvariant() }
        finally { $sha.Dispose() }
        if ($actual -ne $p.Sha256.ToLowerInvariant()) {
            throw "checksum mismatch for $($p.Name)@$($p.Version): expected $($p.Sha256), got $actual"
        }

        $extract = Join-Path $work $leaf
        New-Item -ItemType Directory -Force $extract | Out-Null
        # Prefer the tar that ships with Windows: other tars on PATH may read C: as a host name.
        $tar = Join-Path $env:SystemRoot 'System32\tar.exe'
        if (-not (Test-Path $tar)) { $tar = 'tar' }
        & $tar -xzf $tgz -C $extract
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

    # npm tarballs carry decades-old timestamps, so an existing build output would be
    # treated as newer and the refreshed files would never be copied.
    $now = Get-Date
    Get-ChildItem $Destination -Recurse -File | ForEach-Object { $_.LastWriteTime = $now }

    Set-Content -Path $stamp -Value $wanted -NoNewline
    Write-Host "vendor assets written to $Destination"
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}

param([string]$DalamudLibPath = '')
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\SrankFlag\SrankFlag.csproj'
$buildArgs = @('build', $project, '-c', 'Release', '--nologo')
if ($DalamudLibPath) { $buildArgs += "-p:DalamudLibPath=$DalamudLibPath" }
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)." }
$output = Join-Path $PSScriptRoot 'src\SrankFlag\bin\Release'
$manifest = Get-Content -LiteralPath (Join-Path $output 'SrankFlag.json') -Raw | ConvertFrom-Json
$release = Join-Path $PSScriptRoot ('artifacts\release\v' + $manifest.AssemblyVersion)
New-Item -ItemType Directory -Path $release -Force | Out-Null
$zipPath = Join-Path $release 'SrankFlag.zip'
# Explicit allowlist: never package runtime preferences, caches or user data.
$files = @('SrankFlag.dll', 'SrankFlag.json', 'SrankFlag.deps.json', 'KamiToolKit.dll', 'FFXIVClientStructs.Expansion.dll', 'icon.png', 'srank_data.json')
Add-Type -AssemblyName System.IO.Compression
$stream = [IO.File]::Open($zipPath, [IO.FileMode]::Create)
$zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($name in $files) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $output $name), $name, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $PSScriptRoot 'lib\KamiToolKit.LICENSE'), 'KamiToolKit.LICENSE') | Out-Null
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.md'), 'THIRD-PARTY-NOTICES.md') | Out-Null
} finally { $zip.Dispose(); $stream.Dispose() }
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $release 'SHA256SUMS.txt'), "$hash  SrankFlag.zip`n", [Text.UTF8Encoding]::new($false))
Write-Output "Release: $zipPath"
Write-Output "SHA256: $hash"

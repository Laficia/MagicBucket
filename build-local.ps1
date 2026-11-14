param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$DalamudLibPath = ''
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\SrankFlag\SrankFlag.csproj'
$buildArgs = @('build', $project, '-c', $Configuration, '--nologo')
if ($DalamudLibPath) { $buildArgs += "-p:DalamudLibPath=$DalamudLibPath" }
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)." }

$output = Join-Path $PSScriptRoot "src\SrankFlag\bin\$Configuration"
$dev = Join-Path $PSScriptRoot 'artifacts\dev\SrankFlag'
New-Item -ItemType Directory -Path $dev -Force | Out-Null
# Copy dependencies and the manifest before the watched plugin DLL.
foreach ($name in @('KamiToolKit.dll', 'FFXIVClientStructs.Expansion.dll', 'SrankFlag.deps.json', 'SrankFlag.pdb', 'SrankFlag.json', 'icon.png')) {
    Copy-Item -LiteralPath (Join-Path $output $name) -Destination (Join-Path $dev $name) -Force
}
# Runtime settings/caches live next to the DLL. Seed hunt data only on first build.
$data = Join-Path $dev 'srank_data.json'
if (-not (Test-Path -LiteralPath $data)) {
    Copy-Item -LiteralPath (Join-Path $output 'srank_data.json') -Destination $data
}
Copy-Item -LiteralPath (Join-Path $output 'SrankFlag.dll') -Destination (Join-Path $dev 'SrankFlag.dll') -Force
Write-Output "Developer plugin: $(Join-Path $dev 'SrankFlag.dll')"

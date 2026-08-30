param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$StageRoot = '',
    [string]$WorkerBinary = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$stageBase = [System.IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'RawPreview\worker'))
if ([string]::IsNullOrWhiteSpace($StageRoot)) {
    $StageRoot = Join-Path $stageBase $RuntimeIdentifier
}
$StageRoot = [System.IO.Path]::GetFullPath($StageRoot)
$stageRelativePath = [System.IO.Path]::GetRelativePath($stageBase, $StageRoot)
if ([string]::IsNullOrWhiteSpace($stageRelativePath) -or [System.IO.Path]::IsPathRooted($stageRelativePath) -or $stageRelativePath -eq '..' -or $stageRelativePath.StartsWith('..' + [System.IO.Path]::DirectorySeparatorChar)) {
    throw "StageRoot must be a dedicated child of $stageBase."
}
$stageMarker = Join-Path $StageRoot '.rawpreview-worker-stage'

$scriptRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$bundleRoot = if (Test-Path -LiteralPath (Join-Path $scriptRoot 'worker\RawPreview.Worker.exe') -PathType Leaf) {
    $scriptRoot
} else {
    [System.IO.Path]::GetFullPath((Join-Path $scriptRoot '..'))
}
if ([string]::IsNullOrWhiteSpace($WorkerBinary)) {
    $WorkerBinary = Join-Path $bundleRoot 'worker\RawPreview.Worker.exe'
}
$WorkerBinary = [System.IO.Path]::GetFullPath($WorkerBinary)

$photosPackage = @(Get-AppxPackage -Name 'Microsoft.Windows.Photos' | Sort-Object Version -Descending | Select-Object -First 1)
if ($photosPackage.Count -eq 0) { throw 'Microsoft Photos is not installed.' }
$providerPath = Join-Path $photosPackage[0].InstallLocation 'Photos.Models.CppWinRT.dll'
if (-not (Test-Path -LiteralPath $providerPath -PathType Leaf)) { throw 'Photos.Models.CppWinRT.dll was not found.' }

$installedPackages = @(Get-AppxPackage -Name 'RawPreview.Worker')
foreach ($installedPackage in $installedPackages) {
    Remove-AppxPackage -Package $installedPackage.PackageFullName
}

if ([System.IO.Directory]::Exists($StageRoot)) {
    if (-not (Test-Path -LiteralPath $stageMarker -PathType Leaf)) {
        throw "Refusing to replace an unmarked directory: $StageRoot"
    }
    Remove-Item -LiteralPath $StageRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $StageRoot -Force | Out-Null
Set-Content -LiteralPath $stageMarker -Value 'RawPreview.Worker staging directory' -Encoding ascii -NoNewline

$sourceWorkerProject = Join-Path $repoRoot 'src\RawPreview.Worker\RawPreview.Worker.csproj'
$sourceManifest = Join-Path $repoRoot 'packaging\RawPreview.Worker\AppxManifest.xml'
$prebuiltWorker = Test-Path -LiteralPath $WorkerBinary -PathType Leaf
if ($prebuiltWorker) {
    Copy-Item -LiteralPath $WorkerBinary -Destination (Join-Path $StageRoot 'RawPreview.Worker.exe') -Force
} else {
    if (-not (Test-Path -LiteralPath $sourceWorkerProject -PathType Leaf)) { throw 'Worker project was not found. Supply -WorkerBinary for a release bundle.' }
    $publishArgs = @(
        'publish'
        $sourceWorkerProject
        '-c'
        $Configuration
        '-r'
        $RuntimeIdentifier
        '--self-contained'
        'true'
        '-p:PublishSingleFile=true'
        '-p:PublishTrimmed=false'
        '-o'
        $StageRoot
    )
    $dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
    & $dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }
}

$bundleManifest = Join-Path $bundleRoot 'AppxManifest.xml'
if (Test-Path -LiteralPath $sourceManifest -PathType Leaf) {
    Copy-Item -LiteralPath $sourceManifest -Destination (Join-Path $StageRoot 'AppxManifest.xml') -Force
} elseif (Test-Path -LiteralPath $bundleManifest -PathType Leaf) {
    Copy-Item -LiteralPath $bundleManifest -Destination (Join-Path $StageRoot 'AppxManifest.xml') -Force
} else {
    throw 'AppxManifest.xml was not found.'
}
$manifestPath = Join-Path $StageRoot 'AppxManifest.xml'
$processorArchitecture = $RuntimeIdentifier -replace '^win-', ''
$manifest = Get-Content -LiteralPath $manifestPath -Raw
$architecturePattern = 'ProcessorArchitecture="(?:x64|arm64)"'
if ([regex]::Matches($manifest, $architecturePattern).Count -ne 1) { throw 'AppxManifest.xml must declare exactly one supported processor architecture.' }
[regex]::Replace($manifest, $architecturePattern, ('ProcessorArchitecture="' + $processorArchitecture + '"'), 1) | Set-Content -LiteralPath $manifestPath -Encoding utf8
Copy-Item -LiteralPath $providerPath -Destination (Join-Path $StageRoot 'Photos.Models.CppWinRT.dll') -Force
$logoSource = Join-Path $env:WINDIR 'Web\Screen\img100.jpg'
if (-not (Test-Path -LiteralPath $logoSource -PathType Leaf)) { throw 'A Windows logo source image was not found.' }
Add-Type -AssemblyName System.Drawing
$logo = [System.Drawing.Image]::FromFile($logoSource)
try {
    $logo.Save((Join-Path $StageRoot 'Logo.png'), [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $logo.Dispose()
}

Add-AppxPackage -Register -Path $manifestPath -DisableDevelopmentMode:$false
Write-Output "Registered RawPreview.Worker from $StageRoot"

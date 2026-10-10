<#
.SYNOPSIS
    Builds a versioned RoboXopy release into .\artifacts.

.DESCRIPTION
    Versioning follows Semantic Versioning 2.0 (MAJOR.MINOR.PATCH[-PRERELEASE]); the version
    lives in Version.props at the repository root.

      MAJOR  incompatible changes (profile/settings format, command line)
      MINOR  new features, backwards compatible
      PATCH  bug fixes only

    Every publish increments BuildNumber in Version.props (it is never reset), which becomes
    the 4th part of the Windows file version (e.g. 1.2.0.14). The informational version adds
    build metadata: 1.2.0+build.14.20260518 (plus .sha.<commit> inside a git repository).

    The script runs the tests, publishes a single-file RoboXopy.exe and writes
      artifacts\RoboXopy-<version>-<runtime>\               RoboXopy.exe, README.md, CHANGELOG.md
      artifacts\RoboXopy-<version>-<runtime>.zip
      artifacts\RoboXopy-<version>-<runtime>.zip.sha256
      artifacts\RoboXopy-<version>-<runtime>-setup.exe      (self-contained builds; needs Inno Setup 7)
      artifacts\RoboXopy-<version>-<runtime>-setup.exe.sha256

.PARAMETER Bump
    Increment part of the version before building: major, minor or patch.

.PARAMETER Prerelease
    Optional pre-release label, e.g. beta.1 or rc.2 (gives 1.2.0-beta.1).

.PARAMETER Runtime
    Target runtime identifier. Default win-x64.

.PARAMETER FrameworkDependent
    Build a small exe that requires the .NET 10 Desktop Runtime instead of a self-contained one.

.PARAMETER SkipTests
    Don't run the unit tests first.

.PARAMETER SkipInstaller
    Don't build the Inno Setup installer.

.PARAMETER InnoSetupPath
    Path to ISCC.exe or the Inno Setup 7 folder. By default the script looks in the usual
    Inno Setup 7 install folders, then on PATH.

.EXAMPLE
    .\tools\publish.ps1
    Rebuilds the current version with the next build number.

.EXAMPLE
    .\tools\publish.ps1 -Bump minor
    1.0.3 -> 1.1.0

.EXAMPLE
    .\tools\publish.ps1 -Bump major -Prerelease beta.1
    1.4.2 -> 2.0.0-beta.1
#>
[CmdletBinding()]
param(
    [ValidateSet('none', 'patch', 'minor', 'major')]
    [string]$Bump = 'none',

    [ValidatePattern('^$|^[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*$')]
    [string]$Prerelease = '',

    [ValidateSet('win-x64', 'win-arm64', 'win-x86')]
    [string]$Runtime = 'win-x64',

    [switch]$FrameworkDependent,

    [switch]$SkipTests,

    [switch]$SkipInstaller,

    [string]$InnoSetupPath = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path $PSScriptRoot -Parent
$versionFile = Join-Path $root 'Version.props'
$project = Join-Path $root 'src\RoboXopy\RoboXopy.csproj'
$tests = Join-Path $root 'tests\RoboXopy.Core.Tests'
$artifacts = Join-Path $root 'artifacts'

function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args[0]) failed with exit code $LASTEXITCODE." }
}

function Invoke-Git {
    # Native stderr must not become a terminating error (Windows PowerShell 5.1).
    $ErrorActionPreference = 'Continue'
    $output = & git -C $root @args 2>$null
    [pscustomobject]@{ Ok = ($LASTEXITCODE -eq 0); Output = $output }
}

function Find-InnoSetupCompiler {
    if ($InnoSetupPath) {
        $candidate = if (Test-Path $InnoSetupPath -PathType Container) { Join-Path $InnoSetupPath 'ISCC.exe' } else { $InnoSetupPath }
        if (-not (Test-Path $candidate -PathType Leaf)) { throw "ISCC.exe not found at '$InnoSetupPath'." }
        return (Resolve-Path $candidate).Path
    }

    $folders = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7')
    )
    foreach ($folder in $folders) {
        $candidate = Join-Path $folder 'ISCC.exe'
        if (Test-Path $candidate -PathType Leaf) { return $candidate }
    }

    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    return $null
}

function Write-Sha256([string]$Path) {
    $hash = (Get-FileHash $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -Path "$Path.sha256" -Value "$hash  $(Split-Path $Path -Leaf)" -Encoding ascii
    return $hash
}

function Get-Property([string]$Text, [string]$Name) {
    $match = [regex]::Match($Text, "<$Name>\s*([^<]*?)\s*</$Name>")
    if (-not $match.Success) { throw "$Name is missing from $versionFile." }
    return $match.Groups[1].Value
}

function Set-Property([string]$Text, [string]$Name, [string]$Value) {
    return [regex]::Replace($Text, "<$Name>[^<]*</$Name>", "<$Name>$Value</$Name>")
}

# --- Work out the version -------------------------------------------------------------------

$propsText = [IO.File]::ReadAllText($versionFile)
$current = [version](Get-Property $propsText 'VersionPrefix')
$lastBuild = [int](Get-Property $propsText 'BuildNumber')

$prefix = switch ($Bump) {
    'major' { [version]::new($current.Major + 1, 0, 0) }
    'minor' { [version]::new($current.Major, $current.Minor + 1, 0) }
    'patch' { [version]::new($current.Major, $current.Minor, $current.Build + 1) }
    default { [version]::new($current.Major, $current.Minor, $current.Build) }
}

$build = $lastBuild + 1
if ($build -gt 65535) { throw 'BuildNumber exceeds 65535, the limit of a Windows file version part.' }

$semVer = if ($Prerelease) { "$prefix-$Prerelease" } else { "$prefix" }
$metadata = "build.$build.$(Get-Date -Format 'yyyyMMdd')"

$commit = $null
if (Get-Command git -ErrorAction SilentlyContinue) {
    $head = Invoke-Git rev-parse --short HEAD
    if ($head.Ok) {
        $commit = "$($head.Output)".Trim()
        $metadata += ".sha.$commit"
        if (-not (Invoke-Git diff --quiet HEAD).Ok) { $metadata += '.dirty' }
    }
}

$flavor = if ($FrameworkDependent) { "$Runtime-framework-dependent" } else { $Runtime }
$name = "RoboXopy-$semVer-$flavor"
$outDir = Join-Path $artifacts $name
$zip = "$outDir.zip"

Write-Host ''
Write-Host "RoboXopy $semVer+$metadata" -ForegroundColor Cyan
Write-Host "  File version  $prefix.$build"
Write-Host "  Runtime       $Runtime ($(if ($FrameworkDependent) { 'framework-dependent' } else { 'self-contained' }))"
Write-Host ''

$changelog = Join-Path $root 'CHANGELOG.md'
if ((Test-Path $changelog) -and -not (Select-String -Path $changelog -SimpleMatch "## [$semVer]" -Quiet)) {
    Write-Warning "CHANGELOG.md has no '## [$semVer]' section yet."
}

# The installer bundles the self-contained exe; a framework-dependent build is only shipped as a zip.
$iscc = $null
$setup = $null
if (-not $SkipInstaller -and -not $FrameworkDependent) {
    $iscc = Find-InnoSetupCompiler
    if ($iscc) {
        $setup = Join-Path $artifacts "$name-setup.exe"
    }
    else {
        Write-Warning 'Inno Setup 7 (ISCC.exe) not found, so no installer will be built. Use -InnoSetupPath or -SkipInstaller.'
    }
}

# --- Test and publish -----------------------------------------------------------------------

if (-not $SkipTests) {
    Write-Host 'Running tests...' -ForegroundColor Cyan
    Invoke-Dotnet test $tests -c Release --nologo -v q
}

Write-Host 'Publishing...' -ForegroundColor Cyan
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
foreach ($old in @($zip, "$zip.sha256", $setup, "$setup.sha256")) {
    if ($old -and (Test-Path $old)) { Remove-Item $old -Force }
}

Invoke-Dotnet publish $project -c Release -r $Runtime `
    --self-contained $(if ($FrameworkDependent) { 'false' } else { 'true' }) `
    -o $outDir --nologo -v q `
    "-p:VersionPrefix=$prefix" `
    "-p:VersionSuffix=$Prerelease" `
    "-p:BuildNumber=$build" `
    "-p:SourceRevisionId=$metadata"

$exe = Join-Path $outDir 'RoboXopy.exe'
if (-not (Test-Path $exe)) { throw "Publish did not produce $exe." }

$fileVersion = (Get-Item $exe).VersionInfo.FileVersion
if ($fileVersion -ne "$prefix.$build") { throw "Unexpected file version '$fileVersion' (expected $prefix.$build)." }

Copy-Item (Join-Path $root 'README.md') $outDir
if (Test-Path $changelog) { Copy-Item $changelog $outDir }

Compress-Archive -Path (Join-Path $outDir '*') -DestinationPath $zip -CompressionLevel Optimal
$hash = Write-Sha256 $zip

if ($setup) {
    Write-Host 'Building installer...' -ForegroundColor Cyan
    & $iscc /Q `
        "/DPublishDir=$outDir" `
        "/DAppVersion=$semVer" `
        "/DFileVersion=$prefix.$build" `
        "/DRuntime=$Runtime" `
        "/DOutputDir=$artifacts" `
        "/DOutputBaseFilename=$name-setup" `
        (Join-Path $root 'installer\RoboXopy.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE." }
    if (-not (Test-Path $setup)) { throw "Inno Setup did not produce $setup." }
    $setupHash = Write-Sha256 $setup
}

# Only record the new version once the build has succeeded.
$propsText = Set-Property $propsText 'VersionPrefix' "$prefix"
$propsText = Set-Property $propsText 'BuildNumber' "$build"
[IO.File]::WriteAllText($versionFile, $propsText, [Text.UTF8Encoding]::new($false))

$sizeMb = '{0:N1} MB' -f ((Get-Item $exe).Length / 1MB)
Write-Host ''
Write-Host "Built RoboXopy $semVer (build $build)" -ForegroundColor Green
Write-Host "  Exe     $exe ($sizeMb)"
Write-Host "  Zip     $zip"
Write-Host "  SHA256  $hash"
if ($setup) {
    Write-Host "  Setup   $setup ($('{0:N1} MB' -f ((Get-Item $setup).Length / 1MB)))"
    Write-Host "  SHA256  $setupHash"
}

# From https://janjones.me/posts/clickonce-installer-build-publish-github/.

[CmdletBinding(PositionalBinding=$false)]
param (
    [switch]$OnlyBuild = $false,
    [string]$Version
)

$appName = "Arius.Explorer"
$projDir = "src\Arius.Explorer"
Set-StrictMode -version 2.0
$ErrorActionPreference = "Stop"

Write-Output "Working directory: $pwd"
# Find MSBuild.
$msBuildPath = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" `
    -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe `
    -prerelease | Select-Object -First 1
Write-Output "MSBuild: $((Get-Command $msBuildPath).Path)"

# Determine version.
$version = $null
if ($PSBoundParameters.ContainsKey('Version') -and $null -ne $Version -and $Version.Trim().Length -gt 0) {
    $version = $Version.Trim()
} elseif ($env:VERSION) {
    $version = $env:VERSION.Trim()
}

if (-not $version) {
    throw "Unable to determine version. Provide -Version parameter or set VERSION environment variable."
}

$baseVersion = $version.Split('-', 2)[0]
$versionParts = $baseVersion -split '\.'
$numericParts = @()
foreach ($part in $versionParts) {
    if ($part -notmatch '^[0-9]+$') {
        throw "Version segment '$part' is not numeric. Unable to derive ClickOnce version from '$version'."
    }
    $numericParts += $part
}
if ($numericParts.Length -gt 4) {
    $numericParts = $numericParts[0..3]
}
while ($numericParts.Length -lt 4) {
    $numericParts += '0'
}
$normalizedVersion = ($numericParts -join '.')
$applicationRevision = $numericParts[-1]
Write-Output "Version: $normalizedVersion"

# Clean output directory.
$publishDir = "bin/publish"
$outDir = "$projDir/$publishDir"
if (Test-Path $outDir) {
    Remove-Item -Path $outDir -Recurse -Force
}

$outDirFullPath = $null

# Publish the application.
Push-Location $projDir
try {
    Write-Output "Restoring:"
    dotnet build -r win-x64 -c Release
    Write-Output "Publishing:"
    $msBuildVerbosityArg = "/v:m"
    if ($env:CI) {
        $msBuildVerbosityArg = ""
    }
    $msbuildArgs = @(
        '/target:publish',
        '/p:PublishProfile=ClickOnceProfile',
        "/p:ApplicationVersion=$normalizedVersion",
        "/p:ApplicationRevision=$applicationRevision",
        '/p:Configuration=Release',
        "/p:PublishDir=$publishDir",
        "/p:PublishUrl=$publishDir",
        '/p:SignManifests=false'
    )

    if ($msBuildVerbosityArg) {
        $msbuildArgs += $msBuildVerbosityArg
    }

    & $msBuildPath @msbuildArgs

    $outDirFullPath = (Resolve-Path $publishDir).Path

    # Measure publish size.
    $publishSize = (Get-ChildItem -Path "$publishDir/Application Files" -Recurse |
        Measure-Object -Property Length -Sum).Sum / 1Mb
    Write-Output ("Published size: {0:N2} MB" -f $publishSize)
}
finally {
    Pop-Location
}

if ($OnlyBuild) {
    Write-Output "Build finished."
    return
}

# Clone `gh-pages` branch.
$ghPagesDir = "gh-pages"
if (-Not (Test-Path $ghPagesDir)) {
    git clone $(git config --get remote.origin.url) -b gh-pages `
        --depth 1 --single-branch $ghPagesDir
}

Push-Location $ghPagesDir
try {
    if ($env:GITHUB_TOKEN) {
        $originUrl = git remote get-url origin
        if (-not $originUrl) {
            $originUrl = "https://github.com/${env:GITHUB_REPOSITORY}"
        }

        if ($originUrl -like 'https://*') {
            $authUrl = $originUrl -replace '^https://', "https://x-access-token:${env:GITHUB_TOKEN}@"
        } else {
            $authUrl = "https://x-access-token:${env:GITHUB_TOKEN}@github.com/${env:GITHUB_REPOSITORY}"
        }

        git remote set-url origin $authUrl | Out-Null
    }

    if (-not (git config user.email)) {
        git config user.email "github-actions@github.com"
    }
    if (-not (git config user.name)) {
        git config user.name "github-actions"
    }

    $deployRoot = Join-Path (Get-Location) 'explorer'
    if (-not (Test-Path $deployRoot)) {
        New-Item -ItemType Directory -Path $deployRoot | Out-Null
    }

    Push-Location $deployRoot
    try {
        # Remove previous application files.
        Write-Output "Removing previous files..."
        if (Test-Path "Application Files") {
            Remove-Item -Path "Application Files" -Recurse -Force
        }
        if (Test-Path "$appName.application") {
            Remove-Item -Path "$appName.application" -Force
        }

        if (-not $outDirFullPath) {
            throw "Publish directory was not resolved."
        }

        # Copy new application files.
        Write-Output "Copying new files..."
        $appFilesPath = Join-Path $outDirFullPath 'Application Files'
        $manifestPath = Join-Path $outDirFullPath "$appName.application"
        Copy-Item -Path $appFilesPath,$manifestPath -Destination . -Recurse -Force
    }
    finally {
        Pop-Location
    }

    # Stage and commit.
    Write-Output "Staging..."
    git add -A
    $status = git status --porcelain
    if (-not $status) {
        Write-Output "No changes to commit."
    } else {
        Write-Output "Committing..."
        git commit -m "Update to v$normalizedVersion"

        # Push.
        git push
    }
}
finally {
    Pop-Location
}

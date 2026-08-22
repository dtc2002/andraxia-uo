<#
.SYNOPSIS
Publishes and overlays the current runtime onto Andraxia-Dev.

.DESCRIPTION
Run this script from the repository root only after stopping Andraxia-Dev in
Pterodactyl. It cannot stop or start the server. The transfer uses the WinSCP
saved session named Andraxia-Dev and refuses to upload unless /.andraxia-dev
exists on that session with the exact content ANDRAXIA-DEV.

Create the marker once on the Dev server using the WinSCP GUI. Configure the
saved session with its credentials and pinned SSH host key; do not put secrets
in this script. The saved session name and remote target cannot be overridden.

.EXAMPLE
.\scripts\andraxia\deploy-dev.ps1 -Deploy -ConfirmServerStopped
#>
[CmdletBinding()]
param(
    [switch]$Deploy,
    [switch]$ConfirmServerStopped
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$devSessionName = "Andraxia-Dev"
$devMarkerPath = "/.andraxia-dev"
$devMarkerContent = "ANDRAXIA-DEV"
$remoteRoot = "/"

function Stop-Deployment {
    param([Parameter(Mandatory = $true)][string]$Message)

    throw $Message
}

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)][string]$Command,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$Description
    )

    Write-Host $Description
    & $Command @Arguments

    if ($LASTEXITCODE -ne 0)
    {
        Stop-Deployment "$Description failed with exit code $LASTEXITCODE."
    }
}

function Find-WinScpConsole {
    $command = Get-Command "WinSCP.com" -ErrorAction SilentlyContinue

    if ($null -ne $command)
    {
        return $command.Source
    }

    $candidates = @()
    $programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
    $programFilesX86 = [Environment]::GetEnvironmentVariable("ProgramFiles(x86)")

    if (-not [string]::IsNullOrWhiteSpace($programFiles))
    {
        $candidates += (Join-Path $programFiles "WinSCP\WinSCP.com")
    }

    if (-not [string]::IsNullOrWhiteSpace($programFilesX86))
    {
        $candidates += (Join-Path $programFilesX86 "WinSCP\WinSCP.com")
    }

    foreach ($candidate in $candidates)
    {
        if (Test-Path -LiteralPath $candidate -PathType Leaf)
        {
            return $candidate
        }
    }

    Stop-Deployment "WinSCP.com was not found. Install WinSCP or add it to PATH."
}

function Quote-WinScpValue {
    param([Parameter(Mandatory = $true)][string]$Value)

    return '"' + $Value.Replace('"', '""') + '"'
}

function Invoke-WinScpScript {
    param(
        [Parameter(Mandatory = $true)][string]$WinScpPath,
        [Parameter(Mandatory = $true)][string[]]$Commands,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $scriptPath = [IO.Path]::GetTempFileName()

    try
    {
        # WinSCP requires a BOM for Unicode script files.
        Set-Content -LiteralPath $scriptPath -Value $Commands -Encoding Unicode
        Invoke-CheckedCommand $WinScpPath @("/script=$scriptPath") $Description
    }
    finally
    {
        Remove-Item -LiteralPath $scriptPath -Force -ErrorAction SilentlyContinue
    }
}

function Add-DeploymentFile {
    param(
        [Parameter(Mandatory = $true)][Collections.Generic.Dictionary[string, string]]$Files,
        [Parameter(Mandatory = $true)][string]$DistributionPath,
        [Parameter(Mandatory = $true)][string]$FilePath
    )

    $fullDistributionPath = [IO.Path]::GetFullPath($DistributionPath).TrimEnd('\', '/')
    $fullFilePath = [IO.Path]::GetFullPath($FilePath)
    $distributionPrefix = $fullDistributionPath + [IO.Path]::DirectorySeparatorChar

    if (-not $fullFilePath.StartsWith($distributionPrefix, [StringComparison]::OrdinalIgnoreCase))
    {
        Stop-Deployment "Refusing to stage a file outside Distribution: $fullFilePath"
    }

    $relativePath = $fullFilePath.Substring($distributionPrefix.Length)
    $Files[$relativePath] = $fullFilePath
}

function Get-DeploymentFiles {
    param([Parameter(Mandatory = $true)][string]$DistributionPath)

    $files = New-Object 'Collections.Generic.Dictionary[string, string]' ([StringComparer]::OrdinalIgnoreCase)

    $rootFiles = Get-ChildItem -LiteralPath $DistributionPath -File

    foreach ($file in $rootFiles)
    {
        $isRuntimeFile =
            $file.Name -eq "ModernUO" -or
            $file.Name -like "ModernUO.*" -or
            $file.Name -like "Server.*" -or
            $file.Name -like "Logger.*" -or
            $file.Extension -in @(".dll", ".so")

        if ($isRuntimeFile)
        {
            Add-DeploymentFile $files $DistributionPath $file.FullName
        }
    }

    $assembliesPath = Join-Path $DistributionPath "Assemblies"
    Get-ChildItem -LiteralPath $assembliesPath -File -Recurse |
        Where-Object { $_.Extension -in @(".dll", ".json", ".pdb", ".so") } |
        ForEach-Object { Add-DeploymentFile $files $DistributionPath $_.FullName }

    $dataDirectoryNames = @(
        "Bulk Orders",
        "commands",
        "Components",
        "Decoration",
        "Items",
        "Locations",
        "Professions",
        "Spawns"
    )
    $dataFileNames = @(
        "assemblies.json",
        "bodyTable.cfg",
        "categorization.json",
        "containers.cfg",
        "expansions.json",
        "map-definitions.json",
        "MLQuests.cfg",
        "names.json",
        "npc-speeds.json",
        "pageresponse.cfg",
        "regions.json",
        "shrink.json",
        "signs.cfg",
        "skills.json",
        "teleporters.json",
        "treasure.cfg"
    )
    $dataPath = Join-Path $DistributionPath "Data"

    foreach ($directoryName in $dataDirectoryNames)
    {
        $directoryPath = Join-Path $dataPath $directoryName

        if (-not (Test-Path -LiteralPath $directoryPath -PathType Container))
        {
            Stop-Deployment "Required allowlisted data directory is missing: $directoryPath"
        }

        Get-ChildItem -LiteralPath $directoryPath -File -Recurse |
            ForEach-Object { Add-DeploymentFile $files $DistributionPath $_.FullName }
    }

    foreach ($fileName in $dataFileNames)
    {
        $filePath = Join-Path $dataPath $fileName

        if (-not (Test-Path -LiteralPath $filePath -PathType Leaf))
        {
            Stop-Deployment "Required allowlisted data file is missing: $filePath"
        }

        Add-DeploymentFile $files $DistributionPath $filePath
    }

    return $files
}

function Assert-SafeDeploymentManifest {
    param([Parameter(Mandatory = $true)][Collections.Generic.Dictionary[string, string]]$Files)

    $protectedPrefixes = @(
        "Saves\",
        "Configuration\",
        "UOData\",
        "Archives\",
        "Backups\",
        "Logs\",
        "temp\",
        "Data\Pathfinding\"
    )

    foreach ($relativePath in $Files.Keys)
    {
        $normalizedPath = $relativePath.Replace('/', '\')

        if ($normalizedPath -eq ".andraxia-dev")
        {
            Stop-Deployment "The DEV identity marker must never be deployed."
        }

        foreach ($protectedPrefix in $protectedPrefixes)
        {
            if ($normalizedPath.StartsWith($protectedPrefix, [StringComparison]::OrdinalIgnoreCase))
            {
                Stop-Deployment "Protected runtime data was selected for deployment: $relativePath"
            }
        }
    }
}

function Copy-DeploymentManifest {
    param(
        [Parameter(Mandatory = $true)][Collections.Generic.Dictionary[string, string]]$Files,
        [Parameter(Mandatory = $true)][string]$StagingPath
    )

    foreach ($relativePath in $Files.Keys)
    {
        $destinationPath = Join-Path $StagingPath $relativePath
        $destinationDirectory = Split-Path -Parent $destinationPath
        [void](New-Item -ItemType Directory -Path $destinationDirectory -Force)
        Copy-Item -LiteralPath $Files[$relativePath] -Destination $destinationPath -Force
    }
}

try
{
    $repositoryPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..")).TrimEnd('\', '/')
    $currentPath = [IO.Path]::GetFullPath((Get-Location).Path).TrimEnd('\', '/')

    Write-Host "Andraxia DEV deployment"
    Write-Host "Repository path: $repositoryPath"
    Write-Host "Target environment: DEV"
    Write-Host "Target saved session: $devSessionName"
    Write-Host "Target identity marker: $devMarkerPath = $devMarkerContent"

    if (-not $currentPath.Equals($repositoryPath, [StringComparison]::OrdinalIgnoreCase))
    {
        Stop-Deployment "Run this script from the repository root: $repositoryPath"
    }

    $repositorySentinels = @(
        "ModernUO.slnx",
        "publish.ps1",
        "Projects\Andraxia\Andraxia.csproj",
        "Distribution\Data\assemblies.json"
    )

    foreach ($sentinel in $repositorySentinels)
    {
        if (-not (Test-Path -LiteralPath (Join-Path $repositoryPath $sentinel)))
        {
            Stop-Deployment "Repository identity check failed; missing $sentinel."
        }
    }

    $gitRoot = & git -C $repositoryPath rev-parse --show-toplevel 2>$null

    if ($LASTEXITCODE -ne 0 -or $null -eq $gitRoot)
    {
        Stop-Deployment "Repository identity check failed; Git could not resolve the worktree root."
    }

    $resolvedGitRoot = [IO.Path]::GetFullPath(($gitRoot | Select-Object -First 1)).TrimEnd('\', '/')

    if (-not $resolvedGitRoot.Equals($repositoryPath, [StringComparison]::OrdinalIgnoreCase))
    {
        Stop-Deployment "Repository identity check failed; Git root is $resolvedGitRoot."
    }

    if (-not $Deploy)
    {
        Stop-Deployment "No deployment was performed. Re-run with -Deploy after reviewing this script and stopping Andraxia-Dev."
    }

    if (-not $ConfirmServerStopped)
    {
        Stop-Deployment "No deployment was performed. Stop Andraxia-Dev in Pterodactyl, then re-run with -Deploy -ConfirmServerStopped."
    }

    Write-Host "Operator attestation: Andraxia-Dev is stopped."

    Push-Location $repositoryPath

    try
    {
        Invoke-CheckedCommand (Join-Path $repositoryPath "publish.ps1") @(
            "--config", "Release",
            "--os", "linux",
            "--arch", "x64",
            "--skip-prereqs"
        ) "Publishing ModernUO for linux-x64"

        Invoke-CheckedCommand "dotnet" @(
            "restore",
            "Projects/Andraxia/Andraxia.csproj",
            "--runtime", "linux-x64",
            "--force-evaluate"
        ) "Restoring Andraxia for linux-x64"

        Invoke-CheckedCommand "dotnet" @(
            "publish",
            "Projects/Andraxia/Andraxia.csproj",
            "--configuration", "Release",
            "--runtime", "linux-x64",
            "--no-restore",
            "--self-contained", "false"
        ) "Publishing Andraxia into Distribution/Assemblies"
    }
    finally
    {
        Pop-Location
    }

    $distributionPath = Join-Path $repositoryPath "Distribution"

    if (-not (Test-Path -LiteralPath $distributionPath -PathType Container))
    {
        Stop-Deployment "Publish validation failed; Distribution does not exist."
    }

    $expectedArtifacts = @(
        "ModernUO.dll",
        "Server.dll",
        "Assemblies\Andraxia.dll",
        "Assemblies\UOContent.dll"
    )

    foreach ($artifact in $expectedArtifacts)
    {
        if (-not (Test-Path -LiteralPath (Join-Path $distributionPath $artifact) -PathType Leaf))
        {
            Stop-Deployment "Publish validation failed; missing Distribution/$($artifact.Replace('\', '/'))."
        }
    }

    Write-Host "Publish result: validated linux-x64 runtime and Andraxia assembly."

    $winScpPath = Find-WinScpConsole
    $downloadedMarkerPath = [IO.Path]::GetTempFileName()

    try
    {
        Invoke-WinScpScript $winScpPath @(
            "option batch abort",
            "option confirm off",
            "open $devSessionName",
            "get $(Quote-WinScpValue $devMarkerPath) $(Quote-WinScpValue $downloadedMarkerPath)",
            "close",
            "exit"
        ) "Verifying the remote DEV identity"

        $actualMarkerContent = (Get-Content -LiteralPath $downloadedMarkerPath -Raw).Trim()

        if (-not $actualMarkerContent.Equals($devMarkerContent, [StringComparison]::Ordinal))
        {
            Stop-Deployment "Remote identity check failed. Expected marker content '$devMarkerContent'."
        }
    }
    finally
    {
        Remove-Item -LiteralPath $downloadedMarkerPath -Force -ErrorAction SilentlyContinue
    }

    Write-Host "Remote identity verified: $devSessionName is DEV."

    $deploymentFiles = Get-DeploymentFiles $distributionPath
    Assert-SafeDeploymentManifest $deploymentFiles

    if ($deploymentFiles.Count -eq 0)
    {
        Stop-Deployment "The deployment manifest is empty."
    }

    Write-Host "Files being deployed (overlay only; no remote deletion):"
    $deploymentFiles.Keys | Sort-Object | ForEach-Object { Write-Host "  Distribution/$($_.Replace('\', '/'))" }

    $stagingPath = Join-Path ([IO.Path]::GetTempPath()) ("andraxia-dev-deploy-" + [guid]::NewGuid().ToString("N"))
    [void](New-Item -ItemType Directory -Path $stagingPath)

    try
    {
        Copy-DeploymentManifest $deploymentFiles $stagingPath
        $stagingWildcard = Join-Path $stagingPath "*"

        Invoke-WinScpScript $winScpPath @(
            "option batch abort",
            "option confirm off",
            "option failonnomatch on",
            "open $devSessionName",
            "put -transfer=binary -resumesupport=off $(Quote-WinScpValue $stagingWildcard) $(Quote-WinScpValue $remoteRoot)",
            "close",
            "exit"
        ) "Uploading the allowlisted runtime overlay to Andraxia-Dev"
    }
    finally
    {
        Remove-Item -LiteralPath $stagingPath -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Host "SUCCESS: The published runtime overlay was deployed to Andraxia-Dev."
    Write-Host "Andraxia-Dev remains stopped. Start it manually in Pterodactyl after reviewing the transfer."
    exit 0
}
catch
{
    Write-Error "FAILED: $($_.Exception.Message)"
    exit 1
}

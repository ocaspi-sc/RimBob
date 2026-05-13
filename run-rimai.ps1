[CmdletBinding()]
param(
    [switch]$SkipDashboardBuild,
    [switch]$SkipDashboardInstall,
    [switch]$NoRestore,
    [string]$Configuration = "Debug",
    [string]$ListenUrl = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$dashboardDir = Join-Path $repoRoot "Src\Dashboard"
$hostDir = Join-Path $repoRoot "Src\ApiHost"
$hostProject = Join-Path $hostDir "RimAI.Host.csproj"
$nodeModulesDir = Join-Path $dashboardDir "node_modules"

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Label,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Action
    )

    Write-Host ""
    Write-Host "==> $Label"
    & $Action
}

function Require-Command {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found on PATH."
    }
}

Require-Command "dotnet"
Require-Command "npm.cmd"

if (-not $SkipDashboardInstall -and -not (Test-Path $nodeModulesDir)) {
    Invoke-Step "Installing dashboard dependencies" {
        Push-Location $dashboardDir
        try {
            & npm.cmd ci
        }
        finally {
            Pop-Location
        }
    }
}
elseif (-not (Test-Path $nodeModulesDir)) {
    Write-Warning "Dashboard dependencies are missing. Build may fail because node_modules does not exist."
}

if (-not $SkipDashboardBuild) {
    Invoke-Step "Building dashboard into Src\ApiHost\wwwroot" {
        Push-Location $dashboardDir
        try {
            & npm.cmd run build
        }
        finally {
            Pop-Location
        }
    }
}

$dotnetArgs = @(
    "run",
    "--project",
    $hostProject,
    "--configuration",
    $Configuration
)

if ($NoRestore) {
    $dotnetArgs += "--no-restore"
}

if (-not [string]::IsNullOrWhiteSpace($ListenUrl)) {
    $dotnetArgs += "--"
    $dotnetArgs += "RimAi:ListenUrl=$ListenUrl"
    Write-Host ""
    Write-Host "Dashboard URL override: $ListenUrl"
}
else {
    Write-Host ""
    Write-Host "Dashboard URL: http://localhost:5000"
}

Write-Host "RIMAPI expected at: http://localhost:8765/"
Write-Host "Press Ctrl+C to stop RimAI."

Invoke-Step "Starting RimAI host" {
    Push-Location $hostDir
    try {
        & dotnet @dotnetArgs
    }
    finally {
        Pop-Location
    }
}

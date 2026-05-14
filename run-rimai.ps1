[CmdletBinding()]
param(
    [switch]$SkipDashboardBuild,
    [switch]$SkipDashboardInstall,
    [switch]$NoRestore,
    [string]$Configuration = "Debug",
    [string]$ListenUrl = "",
    [switch]$Foreground,
    [switch]$HostOnly
)

$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$dashboardDir = Join-Path $repoRoot "Dashboard"
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

function Set-ServerWindowTitle {
    try {
        $Host.UI.RawUI.WindowTitle = "RimAI Server"
    }
    catch {
        # Some terminals do not expose RawUI title changes.
    }
}

function Start-HostForeground {
    Set-ServerWindowTitle

    Invoke-Step "Starting RimAI host" {
        Push-Location $hostDir
        try {
            & dotnet @dotnetArgs
        }
        finally {
            Pop-Location
        }
    }
}

function Start-HostTaskbarWindow {
    if ([string]::IsNullOrWhiteSpace($PSCommandPath)) {
        throw "Cannot start RimAI in a taskbar window because the script path is unavailable."
    }

    Require-Command "powershell.exe"

    $childArgs = @(
        "-NoLogo",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        "`"$PSCommandPath`"",
        "-HostOnly",
        "-Configuration",
        $Configuration
    )

    if ($NoRestore) {
        $childArgs += "-NoRestore"
    }

    if (-not [string]::IsNullOrWhiteSpace($ListenUrl)) {
        $childArgs += "-ListenUrl"
        $childArgs += $ListenUrl
    }

    Start-Process `
        -FilePath "powershell.exe" `
        -WorkingDirectory $repoRoot `
        -WindowStyle Minimized `
        -ArgumentList $childArgs

    Write-Host ""
    Write-Host "RimAI host started in a minimized taskbar window named 'RimAI Server'."
    Write-Host "The window closes automatically when RimAI exits."
    Write-Host "Use -Foreground to keep the server attached to this terminal for debugging."
}

Require-Command "dotnet"
if (-not $HostOnly) {
    Require-Command "npm.cmd"
}

if (-not $HostOnly -and -not $SkipDashboardInstall -and -not (Test-Path $nodeModulesDir)) {
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
elseif (-not $HostOnly -and -not (Test-Path $nodeModulesDir)) {
    Write-Warning "Dashboard dependencies are missing. Build may fail because node_modules does not exist."
}

if (-not $HostOnly -and -not $SkipDashboardBuild) {
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

if ($HostOnly -or $Foreground) {
    Write-Host "Press Ctrl+C to stop RimAI."
    Start-HostForeground
}
else {
    Start-HostTaskbarWindow
}

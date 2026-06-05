[CmdletBinding()]
param(
    [switch]$InstallDashboard,
    [switch]$BuildDashboard,
    [switch]$BuildHost,
    [switch]$Restore,
    [string]$Configuration = "Debug",
    [string]$ListenUrl = "",
    [switch]$Foreground,
    [switch]$HostOnly,
    [switch]$Tray
)

$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$dashboardDir = Join-Path $repoRoot "Dashboard"
$hostDir = Join-Path $repoRoot "Src\ApiHost"
$hostProject = Join-Path $hostDir "RimBob.Host.csproj"
$hostExe = Join-Path $hostDir "bin\$Configuration\net9.0\RimBob.Host.exe"
$nodeModulesDir = Join-Path $dashboardDir "node_modules"
$dashboardUrl = "http://localhost:5000"
$script:hostExitCode = 0
$script:trayStopRequested = $false

if (-not [string]::IsNullOrWhiteSpace($ListenUrl)) {
    $dashboardUrl = $ListenUrl
}

$dashboardSystemViews = @(
    [pscustomobject]@{ Label = "Runtime"; View = "runtime" },
    [pscustomobject]@{ Label = "Connectivity"; View = "connectivity" },
    [pscustomobject]@{ Label = "Storage"; View = "storage" },
    [pscustomobject]@{ Label = "Coverage"; View = "coverage" },
    [pscustomobject]@{ Label = "Events"; View = "events" }
)

$dashboardInfoViews = @(
    [pscustomobject]@{ Label = "Overview"; View = "overview" },
    [pscustomobject]@{ Label = "Glossary"; View = "glossary" },
    [pscustomobject]@{ Label = "Contracts"; View = "contracts" },
    [pscustomobject]@{ Label = "Data Sources"; View = "data_sources" },
    [pscustomobject]@{ Label = "Algorithms"; View = "algorithms" }
)

$dashboardAnalyticsViews = @(
    [pscustomobject]@{ Label = "Session"; View = "session" },
    [pscustomobject]@{ Label = "Colony"; View = "colony" },
    [pscustomobject]@{ Label = "Advice"; View = "advice" },
    [pscustomobject]@{ Label = "SSE"; View = "sse" },
    [pscustomobject]@{ Label = "Candidates"; View = "candidates" }
)

$dashboardDevBlogViews = @(
    [pscustomobject]@{ Label = "Features"; View = "features" },
    [pscustomobject]@{ Label = "Churn"; View = "churn" },
    [pscustomobject]@{ Label = "Commits"; View = "commits" },
    [pscustomobject]@{ Label = "Topics"; View = "topics" },
    [pscustomobject]@{ Label = "Suggestions"; View = "suggestions" }
)

$dashboardConsoleScopes = @(
    [pscustomobject]@{ Label = "SYSTEM"; Scope = "system"; Views = $dashboardSystemViews },
    [pscustomobject]@{ Label = "INFO"; Scope = "info"; Views = $dashboardInfoViews },
    [pscustomobject]@{ Label = "ANALYTICS"; Scope = "analytics"; Views = $dashboardAnalyticsViews },
    [pscustomobject]@{ Label = "DEV BLOG"; Scope = "dev_blog"; Views = $dashboardDevBlogViews }
)

$dashboardMinisterViews = @(
    [pscustomobject]@{ Label = "System Prompt"; View = "prompt" },
    [pscustomobject]@{ Label = "Briefing"; View = "briefing" },
    [pscustomobject]@{ Label = "RAG"; View = "rag" },
    [pscustomobject]@{ Label = "Rules"; View = "rules" },
    [pscustomobject]@{ Label = "Raw LLM Output"; View = "raw_llm" },
    [pscustomobject]@{ Label = "Infographics"; View = "infographics" },
    [pscustomobject]@{ Label = "Advice"; View = "advice" }
)

$dashboardRulesOnlyMinisterViews = @(
    [pscustomobject]@{ Label = "Briefing"; View = "briefing" },
    [pscustomobject]@{ Label = "Build Queue"; View = "build_queue" },
    [pscustomobject]@{ Label = "Solver"; View = "solver" },
    [pscustomobject]@{ Label = "Requests"; View = "requests" },
    [pscustomobject]@{ Label = "Rules"; View = "rules" },
    [pscustomobject]@{ Label = "Advice"; View = "advice" }
)

$dashboardMinisterScopes = @(
    [pscustomobject]@{ Label = "Mayor"; Scope = "mayor"; Planned = $false; Views = $dashboardMinisterViews },
    [pscustomobject]@{ Label = "Food"; Scope = "food"; Planned = $false; Views = $dashboardMinisterViews },
    [pscustomobject]@{ Label = "Willie"; Scope = "willie"; Planned = $false; Views = $dashboardRulesOnlyMinisterViews },
    [pscustomobject]@{ Label = "Defense"; Scope = "defense"; Planned = $true; Views = $dashboardMinisterViews },
    [pscustomobject]@{ Label = "Welfare"; Scope = "welfare"; Planned = $false; Views = $dashboardMinisterViews },
    [pscustomobject]@{ Label = "Medical"; Scope = "medical"; Planned = $true; Views = $dashboardMinisterViews },
    [pscustomobject]@{ Label = "Research"; Scope = "research"; Planned = $true; Views = $dashboardMinisterViews },
    [pscustomobject]@{ Label = "Industry"; Scope = "industry"; Planned = $true; Views = $dashboardMinisterViews },
    [pscustomobject]@{ Label = "Economy"; Scope = "economy"; Planned = $true; Views = $dashboardMinisterViews },
    [pscustomobject]@{ Label = "Chief of Staff"; Scope = "chief_of_staff"; Planned = $true; Views = $dashboardMinisterViews }
)

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
        $Host.UI.RawUI.WindowTitle = "RimBob Server"
    }
    catch {
        # Some terminals do not expose RawUI title changes.
    }
}

function Set-LauncherWindowTitle {
    try {
        $Host.UI.RawUI.WindowTitle = "RimBob Launcher"
    }
    catch {
        # Some terminals do not expose RawUI title changes.
    }
}

function Assert-HostExecutable {
    if (-not (Test-Path $hostExe)) {
        throw "RimBob host executable not found at '$hostExe'. Run .\run-rimbob.ps1 -BuildHost -Restore first."
    }
}

function Get-HostArguments {
    $hostArgs = @()
    if (-not [string]::IsNullOrWhiteSpace($ListenUrl)) {
        $hostArgs += "--RimBob:ListenUrl=$ListenUrl"
    }

    return ,$hostArgs
}

function Invoke-WithHostEnvironment {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Action
    )

    if ([string]::IsNullOrWhiteSpace($ListenUrl)) {
        & $Action
        return
    }

    $previousListenUrl = $env:RimBob__ListenUrl
    $hadPreviousListenUrl = Test-Path Env:\RimBob__ListenUrl
    try {
        $env:RimBob__ListenUrl = $ListenUrl
        & $Action
    }
    finally {
        if ($hadPreviousListenUrl) {
            $env:RimBob__ListenUrl = $previousListenUrl
        }
        else {
            Remove-Item Env:\RimBob__ListenUrl -ErrorAction SilentlyContinue
        }
    }
}

function Start-HostForeground {
    Set-ServerWindowTitle
    Assert-HostExecutable
    $hostArgs = @(Get-HostArguments)

    Invoke-Step "Starting RimBob host" {
        Push-Location $hostDir
        try {
            Invoke-WithHostEnvironment {
                & $hostExe @hostArgs
                $script:hostExitCode = if ($null -eq $LASTEXITCODE) { 0 } else { $LASTEXITCODE }
            }
        }
        finally {
            Pop-Location
        }
    }
}

function Invoke-HostBuild {
    $buildArgs = @(
        "build",
        $hostProject,
        "--configuration",
        $Configuration
    )

    if (-not $Restore) {
        $buildArgs += "--no-restore"
    }

    Invoke-Step "Building RimBob host" {
        & dotnet @buildArgs
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed with exit code $LASTEXITCODE."
        }
    }
}

function New-RimBobTrayIcon {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing

    if (-not ("RimBobLauncher.NativeMethods" -as [type])) {
        Add-Type -Namespace RimBobLauncher -Name NativeMethods -MemberDefinition @"
[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
public static extern bool DestroyIcon(System.IntPtr hIcon);
"@
    }

    $bitmap = New-Object System.Drawing.Bitmap -ArgumentList 32, 32
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
    $graphics.Clear([System.Drawing.Color]::FromArgb(18, 22, 27))

    $accentBrush = New-Object System.Drawing.SolidBrush -ArgumentList ([System.Drawing.Color]::FromArgb(42, 171, 135))
    $textBrush = New-Object System.Drawing.SolidBrush -ArgumentList ([System.Drawing.Color]::White)
    $font = New-Object System.Drawing.Font -ArgumentList "Segoe UI", 13, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center

    $graphics.FillEllipse($accentBrush, 3, 3, 26, 26)
    $graphics.DrawString("RB", $font, $textBrush, (New-Object System.Drawing.RectangleF -ArgumentList 0, 0, 32, 30), $format)

    $handle = $bitmap.GetHicon()
    $icon = [System.Drawing.Icon]::FromHandle($handle)

    return [pscustomobject]@{
        Icon = $icon
        Handle = $handle
        Bitmap = $bitmap
        Graphics = $graphics
        AccentBrush = $accentBrush
        TextBrush = $textBrush
        Font = $font
        Format = $format
    }
}

function Dispose-RimBobTrayIcon {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$Resources
    )

    $Resources.Icon.Dispose()
    $Resources.Graphics.Dispose()
    $Resources.AccentBrush.Dispose()
    $Resources.TextBrush.Dispose()
    $Resources.Font.Dispose()
    $Resources.Format.Dispose()
    $Resources.Bitmap.Dispose()
    [void][RimBobLauncher.NativeMethods]::DestroyIcon($Resources.Handle)
}

function Stop-TrayHost {
    $script:trayStopRequested = $true

    if ($script:trayHostProcess -and -not $script:trayHostProcess.HasExited) {
        $script:trayHostProcess.Kill()
        [void]$script:trayHostProcess.WaitForExit(5000)
    }
}

function Resolve-ChromePath {
    $command = Get-Command "chrome.exe" -ErrorAction SilentlyContinue
    if ($command -and -not [string]::IsNullOrWhiteSpace($command.Source)) {
        return $command.Source
    }

    $registryPaths = @(
        "HKCU:\Software\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe",
        "HKLM:\Software\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe",
        "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe"
    )

    foreach ($registryPath in $registryPaths) {
        try {
            $registryKey = Get-Item -Path $registryPath -ErrorAction Stop
            $registryValue = [string]$registryKey.GetValue("")
            if (-not [string]::IsNullOrWhiteSpace($registryValue) -and (Test-Path $registryValue)) {
                return $registryValue
            }
        }
        catch {
            # Chrome is not registered at this location.
        }
    }

    $candidatePaths = @()
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $candidatePaths += Join-Path $env:ProgramFiles "Google\Chrome\Application\chrome.exe"
    }
    if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
        $candidatePaths += Join-Path ${env:ProgramFiles(x86)} "Google\Chrome\Application\chrome.exe"
    }
    if (-not [string]::IsNullOrWhiteSpace($env:LocalAppData)) {
        $candidatePaths += Join-Path $env:LocalAppData "Google\Chrome\Application\chrome.exe"
    }

    foreach ($candidatePath in $candidatePaths) {
        if (Test-Path $candidatePath) {
            return $candidatePath
        }
    }

    return $null
}

function Open-DashboardUrl {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url
    )

    $chromePath = Resolve-ChromePath
    if (-not [string]::IsNullOrWhiteSpace($chromePath)) {
        Start-Process -FilePath $chromePath -ArgumentList $Url
        return
    }

    Start-Process $Url
}

function Get-DashboardViewUrl {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Scope,

        [string]$View = ""
    )

    $baseUrl = $script:trayDashboardUrl.TrimEnd("/")
    $query = "scope=$([System.Uri]::EscapeDataString($Scope))"
    if (-not [string]::IsNullOrWhiteSpace($View)) {
        $query = "$query&view=$([System.Uri]::EscapeDataString($View))"
    }

    return "$baseUrl/?$query"
}

function Get-TrayEndpointLabel {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url
    )

    $firstUrl = ($Url -split ";")[0].Trim()
    $parsedUrl = $null
    if ([System.Uri]::TryCreate($firstUrl, [System.UriKind]::Absolute, [ref]$parsedUrl) -and
        -not [string]::IsNullOrWhiteSpace($parsedUrl.Authority)) {
        return $parsedUrl.Authority
    }

    return $firstUrl
}

function Get-TrayTooltipText {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Endpoint
    )

    $tooltip = "RimBob Host - $Endpoint"
    if ($tooltip.Length -le 63) {
        return $tooltip
    }

    $shortTooltip = "RimBob - $Endpoint"
    if ($shortTooltip.Length -le 63) {
        return $shortTooltip
    }

    return $shortTooltip.Substring(0, 63)
}

function Add-DashboardMenuCommand {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Items,

        [Parameter(Mandatory = $true)]
        [string]$Label,

        [Parameter(Mandatory = $true)]
        [string]$Scope,

        [string]$View = ""
    )

    $item = $Items.Add($Label)
    $item.Tag = Get-DashboardViewUrl -Scope $Scope -View $View
    $item.Add_Click({
        param($Sender, $EventArgs)
        Open-DashboardUrl ([string]$Sender.Tag)
    })

    return $item
}

function Add-DashboardViewMenu {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Forms.ContextMenuStrip]$Menu
    )

    foreach ($consoleScope in $dashboardConsoleScopes) {
        $scopeItem = New-Object System.Windows.Forms.ToolStripMenuItem -ArgumentList $consoleScope.Label
        foreach ($view in $consoleScope.Views) {
            [void](Add-DashboardMenuCommand `
                -Items $scopeItem.DropDownItems `
                -Label $view.Label `
                -Scope $consoleScope.Scope `
                -View $view.View)
        }

        [void]$Menu.Items.Add($scopeItem)
    }

    [void]$Menu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator))

    foreach ($ministerScope in $dashboardMinisterScopes) {
        $label = $ministerScope.Label
        if ($ministerScope.Planned) {
            $label = "$label (planned)"
        }

        $scopeItem = New-Object System.Windows.Forms.ToolStripMenuItem -ArgumentList $label
        foreach ($view in $ministerScope.Views) {
            [void](Add-DashboardMenuCommand `
                -Items $scopeItem.DropDownItems `
                -Label $view.Label `
                -Scope $ministerScope.Scope `
                -View $view.View)
        }

        [void]$Menu.Items.Add($scopeItem)
    }
}

function Start-HostNotificationIcon {
    Assert-HostExecutable

    Set-LauncherWindowTitle

    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    [System.Windows.Forms.Application]::EnableVisualStyles()

    $hostArgs = @(Get-HostArguments)

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $hostExe
    $startInfo.WorkingDirectory = $hostDir
    $startInfo.UseShellExecute = $true
    $startInfo.CreateNoWindow = $false
    $startInfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Minimized
    $startInfo.Arguments = ($hostArgs -join " ")

    Invoke-WithHostEnvironment {
        $script:trayHostProcess = [System.Diagnostics.Process]::Start($startInfo)
    }
    $script:trayDashboardUrl = $dashboardUrl
    $script:trayContext = New-Object System.Windows.Forms.ApplicationContext

    $trayEndpoint = Get-TrayEndpointLabel -Url $script:trayDashboardUrl
    $trayIconResources = New-RimBobTrayIcon
    $notifyIcon = New-Object System.Windows.Forms.NotifyIcon
    $notifyIcon.Icon = $trayIconResources.Icon
    $notifyIcon.Text = Get-TrayTooltipText -Endpoint $trayEndpoint
    $notifyIcon.Visible = $true
    $notifyIcon.BalloonTipTitle = "RimBob Host"
    $notifyIcon.BalloonTipText = "RimBob is running. Right-click this icon to open dashboard views in Chrome or stop the host."
    $notifyIcon.BalloonTipIcon = [System.Windows.Forms.ToolTipIcon]::Info

    $menu = New-Object System.Windows.Forms.ContextMenuStrip
    $statusItem = $menu.Items.Add("RimBob Host running on $trayEndpoint")
    $statusItem.Enabled = $false
    [void]$menu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator))
    $openItem = $menu.Items.Add("Open Dashboard in Chrome")
    $openItem.Add_Click({ Open-DashboardUrl $script:trayDashboardUrl })
    [void]$menu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator))
    Add-DashboardViewMenu -Menu $menu
    [void]$menu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator))
    $stopItem = $menu.Items.Add("Stop RimBob")
    $stopItem.Add_Click({
        Stop-TrayHost
        $script:trayContext.ExitThread()
    })

    $notifyIcon.ContextMenuStrip = $menu
    $notifyIcon.Add_DoubleClick({ Open-DashboardUrl $script:trayDashboardUrl })
    $notifyIcon.ShowBalloonTip(3000)

    $timer = New-Object System.Windows.Forms.Timer
    $timer.Interval = 1000
    $timer.Add_Tick({
        if ($script:trayHostProcess.HasExited) {
            $script:trayContext.ExitThread()
        }
    })
    $timer.Start()
    $hostExitCode = 0

    try {
        [System.Windows.Forms.Application]::Run($script:trayContext)
    }
    finally {
        $timer.Stop()
        $timer.Dispose()
        $notifyIcon.Visible = $false
        $notifyIcon.Dispose()
        Dispose-RimBobTrayIcon $trayIconResources

        if ($script:trayHostProcess) {
            if ($script:trayHostProcess.HasExited) {
                $hostExitCode = $script:trayHostProcess.ExitCode
            }

            $script:trayHostProcess.Dispose()
        }
    }

    if (-not $script:trayStopRequested -and $hostExitCode -ne 0) {
        exit $hostExitCode
    }
}

function Start-HostNotificationArea {
    Assert-HostExecutable

    if ([string]::IsNullOrWhiteSpace($PSCommandPath)) {
        throw "Cannot start RimBob in the notification area because the script path is unavailable."
    }

    Require-Command "powershell.exe"

    $childArgs = @(
        "-NoLogo",
        "-NoProfile",
        "-STA",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        "`"$PSCommandPath`"",
        "-HostOnly",
        "-Tray",
        "-Configuration",
        $Configuration
    )

    if (-not [string]::IsNullOrWhiteSpace($ListenUrl)) {
        $childArgs += "-ListenUrl"
        $childArgs += $ListenUrl
    }

    Start-Process `
        -FilePath "powershell.exe" `
        -WorkingDirectory $repoRoot `
        -WindowStyle Hidden `
        -ArgumentList $childArgs

    Write-Host ""
    Write-Host "RimBob host started. The launcher window is hidden; controls live in the Windows notification area."
    Write-Host "Right-click the RimBob icon to open dashboard views in Chrome or stop the host."
    Write-Host "Use -Foreground to keep the server attached to this terminal for debugging."
}

if (-not $HostOnly -and $BuildHost) {
    Require-Command "dotnet"
}
if (-not $HostOnly -and ($InstallDashboard -or $BuildDashboard)) {
    Require-Command "npm.cmd"
}

if (-not $HostOnly -and $InstallDashboard) {
    Invoke-Step "Installing dashboard dependencies" {
        Push-Location $dashboardDir
        try {
            & npm.cmd ci
            if ($LASTEXITCODE -ne 0) {
                throw "npm.cmd ci failed with exit code $LASTEXITCODE."
            }
        }
        finally {
            Pop-Location
        }
    }
}

if (-not $HostOnly -and $BuildDashboard) {
    if (-not (Test-Path $nodeModulesDir)) {
        throw "Dashboard dependencies are missing. Run .\run-rimbob.ps1 -InstallDashboard -BuildDashboard first."
    }

    Invoke-Step "Building dashboard into Src\ApiHost\wwwroot" {
        Push-Location $dashboardDir
        try {
            & npm.cmd run build
            if ($LASTEXITCODE -ne 0) {
                throw "npm.cmd run build failed with exit code $LASTEXITCODE."
            }
        }
        finally {
            Pop-Location
        }
    }
}

if (-not [string]::IsNullOrWhiteSpace($ListenUrl)) {
    Write-Host ""
    Write-Host "Dashboard URL override: $ListenUrl"
}
else {
    Write-Host ""
    Write-Host "Dashboard URL: http://localhost:5000"
}

Write-Host "RIMAPI expected at: http://localhost:8765/"

if ($HostOnly -and $Tray) {
    Start-HostNotificationIcon
}
elseif ($HostOnly -or $Foreground) {
    Write-Host "Press Ctrl+C to stop RimBob."
    Start-HostForeground

    if ($script:hostExitCode -ne 0) {
        exit $script:hostExitCode
    }
}
else {
    if ($BuildHost) {
        Invoke-HostBuild
    }

    Start-HostNotificationArea
}

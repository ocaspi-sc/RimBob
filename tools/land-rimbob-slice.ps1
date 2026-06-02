param(
    [Parameter(Mandatory = $true)]
    [string]$FeatureRef,

    [string[]]$Manifest,

    [string]$ManifestFile,

    [Parameter(Mandatory = $true)]
    [string]$CommitSubject,

    [string]$CommitBody,

    [string]$MainRepoRoot = 'C:\dev\RimBob',

    [string]$TaskId,

    [string]$TaskAnchorId,

    [switch]$BuildDashboard,

    [switch]$SkipDotNetBuild,

    [switch]$SkipTests,

    [switch]$RestartHost,

    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [switch]$AllowFailure
    )

    $output = & git @Arguments
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0 -and -not $AllowFailure) {
        $outputText = (($output | Out-String).Trim())
        if ([string]::IsNullOrWhiteSpace($outputText)) {
            throw "git $($Arguments -join ' ') failed with exit code $exitCode"
        }

        throw "git $($Arguments -join ' ') failed with exit code $exitCode`n$outputText"
    }

    return $output
}

function Test-GitQuiet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & git @Arguments | Out-Null
    $exitCode = $LASTEXITCODE
    if ($exitCode -eq 0) {
        return $true
    }

    if ($exitCode -eq 1) {
        return $false
    }

    throw "git $($Arguments -join ' ') failed with exit code $exitCode"
}

function Normalize-RepoPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    return $Path.Trim().Trim('"').Trim("'").Replace('\', '/').TrimStart('/')
}

function Get-ManifestPaths {
    $paths = New-Object System.Collections.Generic.List[string]

    foreach ($path in @($Manifest)) {
        if ($null -eq $path) {
            continue
        }

        foreach ($part in $path.Split(',')) {
            if (-not [string]::IsNullOrWhiteSpace($part)) {
                $paths.Add((Normalize-RepoPath $part))
            }
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($ManifestFile)) {
        if (-not (Test-Path -LiteralPath $ManifestFile)) {
            throw "Manifest file not found: $ManifestFile"
        }

        foreach ($line in Get-Content -LiteralPath $ManifestFile) {
            $trimmed = $line.Trim()
            if ($trimmed.Length -eq 0 -or $trimmed.StartsWith('#')) {
                continue
            }

            $paths.Add((Normalize-RepoPath $trimmed))
        }
    }

    $unique = @($paths | Select-Object -Unique)
    if ($unique.Count -eq 0) {
        throw 'Provide -Manifest or -ManifestFile with at least one path.'
    }

    return $unique
}

function Get-GitFileLines {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Ref,

        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $lines = @(Invoke-Git @('show', "$Ref`:$Path"))
    if ($lines.Count -gt 0) {
        $lines[0] = $lines[0].TrimStart([char]0xFEFF)
    }

    return $lines
}

function Find-TaskLine {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string[]]$Lines,

        [Parameter(Mandatory = $true)]
        [string]$TaskId
    )

    $pattern = '^- \[[ xX]\] ' + [regex]::Escape($TaskId) + '(\s|$)'
    foreach ($line in $Lines) {
        if ($line -match $pattern) {
            return $line.TrimEnd()
        }
    }

    return $null
}

function Set-TaskLine {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string[]]$Lines,

        [Parameter(Mandatory = $true)]
        [string]$TaskId,

        [Parameter(Mandatory = $true)]
        [string]$TaskLine,

        [string]$TaskAnchorId
    )

    $taskPattern = '^- \[[ xX]\] ' + [regex]::Escape($TaskId) + '(\s|$)'
    $replaced = $false
    $result = New-Object System.Collections.Generic.List[string]

    foreach ($line in $Lines) {
        if ($line -match $taskPattern) {
            if (-not $replaced) {
                $result.Add($TaskLine)
                $replaced = $true
            }
            continue
        }

        $result.Add($line)
    }

    if ($replaced) {
        return $result.ToArray()
    }

    $inserted = $false
    $result = New-Object System.Collections.Generic.List[string]
    $anchorPattern = $null
    if (-not [string]::IsNullOrWhiteSpace($TaskAnchorId)) {
        $anchorPattern = '^- \[[ xX]\] ' + [regex]::Escape($TaskAnchorId) + '(\s|$)'
    }

    foreach ($line in $Lines) {
        $result.Add($line)
        if (-not $inserted -and $anchorPattern -ne $null -and $line -match $anchorPattern) {
            $result.Add($TaskLine)
            $inserted = $true
        }
    }

    if ($inserted) {
        return $result.ToArray()
    }

    $result = New-Object System.Collections.Generic.List[string]
    foreach ($line in $Lines) {
        $result.Add($line)
        if (-not $inserted -and $line.Trim() -eq '<!-- entries go here -->') {
            $result.Add($TaskLine)
            $inserted = $true
        }
    }

    if (-not $inserted) {
        throw 'Could not find a Tasks.md insertion point. Pass -TaskAnchorId or add the standard todo marker.'
    }

    return $result.ToArray()
}

function Write-Utf8NoBomLines {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string[]]$Lines
    )

    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, (($Lines -join "`n") + "`n"), $encoding)
}

function Stage-TasksLine {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FeatureRef,

        [Parameter(Mandatory = $true)]
        [string]$TaskId,

        [string]$TaskAnchorId
    )

    $featureLines = Get-GitFileLines $FeatureRef 'Tasks.md'
    $taskLine = Find-TaskLine -Lines $featureLines -TaskId $TaskId
    if ($taskLine -eq $null) {
        throw "Could not find task id '$TaskId' in $FeatureRef`:Tasks.md"
    }

    $headLines = Get-GitFileLines 'HEAD' 'Tasks.md'
    $stagedLines = Set-TaskLine -Lines $headLines -TaskId $TaskId -TaskLine $taskLine -TaskAnchorId $TaskAnchorId

    $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ('rimbob-tasks-' + [guid]::NewGuid().ToString('N') + '.md')
    try {
        Write-Utf8NoBomLines -Path $tmp -Lines $stagedLines
        $blob = @(Invoke-Git @('hash-object', '-w', $tmp))
        if ($blob.Count -ne 1) {
            throw 'Could not create staged Tasks.md blob.'
        }

        Invoke-Git @('update-index', '--cacheinfo', '100644', $blob[0], 'Tasks.md') | Out-Null
    } finally {
        if (Test-Path -LiteralPath $tmp) {
            Remove-Item -LiteralPath $tmp -Force
        }
    }

    $workingPath = Join-Path $MainRepoRoot 'Tasks.md'
    $workingLines = @()
    if (Test-Path -LiteralPath $workingPath) {
        $workingLines = [System.IO.File]::ReadAllLines($workingPath)
    }

    $updatedWorkingLines = Set-TaskLine -Lines $workingLines -TaskId $TaskId -TaskLine $taskLine -TaskAnchorId $TaskAnchorId
    Write-Utf8NoBomLines -Path $workingPath -Lines $updatedWorkingLines
}

function Assert-ExactManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Expected
    )

    $actual = @(Invoke-Git @('diff', '--cached', '--name-only'))
    $missing = @($Expected | Where-Object { $actual -notcontains $_ })
    $extra = @($actual | Where-Object { $Expected -notcontains $_ })

    if ($missing.Count -gt 0 -or $extra.Count -gt 0) {
        throw "Staged manifest mismatch. Missing=$($missing -join ',') Extra=$($extra -join ',')"
    }

    Write-Host 'Staged manifest:'
    $actual | ForEach-Object { Write-Host "  $_" }
}

function Assert-CheckoutPathsClean {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Paths
    )

    $dirty = New-Object System.Collections.Generic.List[string]
    foreach ($path in $Paths) {
        $tracked = @(Invoke-Git @('ls-files', '--', $path))
        $workingPath = Join-Path $MainRepoRoot ($path.Replace('/', '\'))
        if ($tracked.Count -eq 0 -and -not (Test-Path -LiteralPath $workingPath)) {
            continue
        }

        $status = @(Invoke-Git @('status', '--porcelain', '--', $path))
        if ($status.Count -gt 0) {
            $dirty.AddRange($status)
        }
    }

    if ($dirty.Count -gt 0) {
        throw "Refusing to overwrite dirty main-checkout manifest paths:`n$($dirty -join [Environment]::NewLine)"
    }
}

function Invoke-CommandStep {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Script
    )

    Write-Host "==> $Name"
    & $Script
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE"
    }
}

function Stop-MatchingHost {
    $hostPath = Join-Path $MainRepoRoot 'Src\ApiHost\bin\Debug\net9.0\RimBob.Host.exe'
    $processes = @(Get-Process RimBob.Host -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $hostPath })
    foreach ($process in $processes) {
        Stop-Process -Id $process.Id -Force
    }
}

function Verify-Host {
    $baseUrl = 'http://localhost:5000'
    $hostPath = Join-Path $MainRepoRoot 'Src\ApiHost\bin\Debug\net9.0\RimBob.Host.exe'
    $deadline = (Get-Date).AddSeconds(30)
    $health = $null
    do {
        try {
            Invoke-RestMethod -Uri "$baseUrl/api/health" -TimeoutSec 2 | Out-Null
            $health = Invoke-RestMethod -Uri "$baseUrl/api/system/health" -TimeoutSec 2
            break
        } catch {
            Start-Sleep -Milliseconds 500
        }
    } while ((Get-Date) -lt $deadline)

    if ($health -eq $null) {
        throw "Host did not answer health checks at $baseUrl"
    }

    if ($health.runtime.runtime_root -ne $MainRepoRoot) {
        throw "Host runtime_root mismatch: $($health.runtime.runtime_root)"
    }

    if ($health.runtime.host_process_path -ne $hostPath) {
        throw "Host process path mismatch: $($health.runtime.host_process_path)"
    }

    $root = Invoke-WebRequest -Uri "$baseUrl/" -UseBasicParsing -TimeoutSec 5
    if ($root.StatusCode -ne 200) {
        throw "Dashboard root returned HTTP $($root.StatusCode)"
    }

    Write-Host "Host verified: $baseUrl build=$($health.version.build_revision_short) assets=$($health.version.dashboard_asset_version)"
}

$resolvedMain = (Resolve-Path -LiteralPath $MainRepoRoot).Path
$MainRepoRoot = $resolvedMain
Set-Location -LiteralPath $MainRepoRoot

$manifestPaths = @(Get-ManifestPaths)
$manifestPaths = @($manifestPaths | Sort-Object -Unique)

Invoke-Git @('rev-parse', '--verify', "$FeatureRef^{commit}") | Out-Null
$repoTopRaw = (@(Invoke-Git @('rev-parse', '--show-toplevel')))[0]
$repoTop = (Resolve-Path -LiteralPath $repoTopRaw).Path
if ($repoTop -ne $MainRepoRoot) {
    throw "Expected repo root $MainRepoRoot, got $repoTop"
}

$branch = (@(Invoke-Git @('branch', '--show-current')))[0]
if ($branch -ne 'master') {
    throw "Run this helper from the real master checkout. Current branch is '$branch'."
}

$gitDir = (@(Invoke-Git @('rev-parse', '--git-dir')))[0]
Write-Host "Repo: $MainRepoRoot"
Write-Host "Git dir: $gitDir"
Write-Host "Feature ref: $FeatureRef"
Write-Host 'Manifest:'
$manifestPaths | ForEach-Object { Write-Host "  $_" }

if ($manifestPaths -contains 'Tasks.md' -and [string]::IsNullOrWhiteSpace($TaskId)) {
    throw 'Manifest contains Tasks.md. Pass -TaskId so only that task line is staged.'
}

$checkoutPaths = @($manifestPaths | Where-Object { $_ -ne 'Tasks.md' })
Assert-CheckoutPathsClean -Paths $checkoutPaths

if ($DryRun) {
    Write-Host 'Dry run complete. No files were changed.'
    exit 0
}

$lockPath = Join-Path $MainRepoRoot '.git\rimbob-master.lock'
$lockAcquired = $false
$commitSucceeded = $false

if (Test-Path -LiteralPath $lockPath) {
    throw "Master lock exists: $(Get-Content -LiteralPath $lockPath -Raw)"
}

if (-not (Test-GitQuiet @('diff', '--cached', '--quiet'))) {
    throw 'Refusing to land: the main checkout index already has staged changes.'
}

$lockPayload = @{
    pid = $PID
    agent = 'codex'
    started_at = (Get-Date).ToString('o')
    intent = "land $FeatureRef"
} | ConvertTo-Json -Compress
New-Item -Path $lockPath -ItemType File -Value $lockPayload -ErrorAction Stop | Out-Null
$lockAcquired = $true

try {
    if ($checkoutPaths.Count -gt 0) {
        Invoke-Git (@('checkout', $FeatureRef, '--') + $checkoutPaths) | Out-Null
    }

    if ($manifestPaths -contains 'Tasks.md') {
        Stage-TasksLine $FeatureRef $TaskId $TaskAnchorId
    }

    Assert-ExactManifest -Expected $manifestPaths
    Invoke-Git @('diff', '--cached', '--check') | Out-Null

    $commitArgs = @('commit', '-m', $CommitSubject)
    if (-not [string]::IsNullOrWhiteSpace($CommitBody)) {
        $commitArgs += @('-m', $CommitBody)
    }

    Invoke-Git $commitArgs | Write-Host
    $commitSucceeded = $true
} finally {
    if ($lockAcquired) {
        if ($commitSucceeded -or (Test-GitQuiet @('diff', '--cached', '--quiet'))) {
            Remove-Item -LiteralPath $lockPath -Force
        } else {
            Write-Warning "Leaving master lock in place because staged changes remain: $lockPath"
        }
    }
}

$dashboardChanged = @($manifestPaths | Where-Object { $_ -like 'Dashboard/*' })
if ($BuildDashboard -or $dashboardChanged.Count -gt 0) {
    Invoke-CommandStep 'Dashboard build' {
        Push-Location -LiteralPath (Join-Path $MainRepoRoot 'Dashboard')
        try {
            & npm.cmd run build
        } finally {
            Pop-Location
        }
    }
}

if (-not $SkipDotNetBuild) {
    Invoke-CommandStep 'Solution build' {
        & dotnet build (Join-Path $MainRepoRoot 'Src\RimBob.sln')
    }
}

if (-not $SkipTests) {
    Invoke-CommandStep 'Test suite' {
        & dotnet test (Join-Path $MainRepoRoot 'Src\Tests\RimBob.Tests.csproj')
    }
}

if ($RestartHost) {
    Stop-MatchingHost
    Invoke-CommandStep 'Host launcher' {
        & powershell.exe -ExecutionPolicy Bypass -File (Join-Path $MainRepoRoot 'run-rimbob.ps1')
    }
    Verify-Host
}

Write-Host 'Landing complete.'

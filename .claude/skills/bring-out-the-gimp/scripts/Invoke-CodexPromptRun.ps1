[CmdletBinding()]
param(
    [ValidateSet("Start", "Resume", "Show", "CloseOut")]
    [string]$Mode = "Start",

    [string]$Prompt,
    [string]$Name,
    [string]$RunId,
    [string]$SessionId,
    [string]$RepoRoot = "C:\dev\RimBob",
    [string]$BaseRef = "master",
    [string]$RunRoot = (Join-Path $env:USERPROFILE ".codex\prompt-runs"),
    [string]$WorktreeRoot = (Join-Path $env:USERPROFILE ".codex\worktrees\prompt-runs"),
    [string]$Branch,
    [string]$Model = "gpt-5.5",
    [string]$ReasoningEffort = "xhigh",

    [switch]$LandAndClose,
    [switch]$Verified
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function New-Slug {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return "prompt"
    }

    $slug = [System.Text.RegularExpressions.Regex]::Replace($Value.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim("-")
    if ([string]::IsNullOrWhiteSpace($slug)) {
        return "prompt"
    }
    if ($slug.Length -gt 36) {
        return $slug.Substring(0, 36).Trim("-")
    }
    return $slug
}

function Invoke-Git {
    param(
        [string]$Cwd,
        [string[]]$Args
    )

    $output = & git -C $Cwd @Args 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Args -join ' ') failed in $Cwd`n$($output -join [Environment]::NewLine)"
    }
    return ($output -join [Environment]::NewLine)
}

function Resolve-CodexCmd {
    $command = Get-Command codex.cmd -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        throw "codex.cmd was not found on PATH. Install/repair the Codex CLI before using this skill."
    }
    return $command.Source
}

function Get-RunDirectory {
    param([string]$Id)
    return (Join-Path $RunRoot $Id)
}

function Read-RunMetadata {
    param([string]$Id)

    $metadataPath = Join-Path (Get-RunDirectory $Id) "metadata.json"
    if (-not (Test-Path -LiteralPath $metadataPath)) {
        throw "Run metadata was not found: $metadataPath"
    }
    return Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
}

function Write-RunMetadata {
    param(
        [string]$Id,
        [object]$Metadata
    )

    $metadataPath = Join-Path (Get-RunDirectory $Id) "metadata.json"
    $Metadata | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $metadataPath -Encoding UTF8
}

function Find-SessionId {
    param([string]$EventsPath)

    if (-not (Test-Path -LiteralPath $EventsPath)) {
        return $null
    }

    foreach ($line in Get-Content -LiteralPath $EventsPath) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        try {
            $event = $line | ConvertFrom-Json -ErrorAction Stop
            foreach ($name in @("session_id", "sessionId", "conversation_id", "conversationId", "id")) {
                if ($event.PSObject.Properties.Name -contains $name) {
                    $candidate = [string]$event.$name
                    if ($candidate -match "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$") {
                        return $candidate
                    }
                }
            }
        }
        catch {
            # Keep scanning; stderr or future event formats may not be JSON.
        }
    }

    $match = Select-String -LiteralPath $EventsPath -Pattern "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}" | Select-Object -First 1
    if ($null -eq $match) {
        return $null
    }
    return $match.Matches[0].Value
}

function Invoke-CodexExec {
    param(
        [string[]]$Arguments,
        [string]$EventsPath,
        [string]$InputPath
    )

    $codex = Resolve-CodexCmd
    if ([string]::IsNullOrWhiteSpace($InputPath)) {
        & $codex @Arguments 2>&1 | Tee-Object -FilePath $EventsPath
    }
    else {
        Get-Content -LiteralPath $InputPath -Raw | & $codex @Arguments 2>&1 | Tee-Object -FilePath $EventsPath
    }
    return $LASTEXITCODE
}

function Get-CleanStatus {
    param([string]$Cwd)
    return Invoke-Git -Cwd $Cwd -Args @("status", "--porcelain", "--untracked-files=all")
}

function Get-ReasoningConfigArg {
    param([string]$Effort)
    return "model_reasoning_effort=`"$Effort`""
}

if ($Mode -eq "Start") {
    if ([string]::IsNullOrWhiteSpace($Prompt)) {
        throw "-Prompt is required for -Mode Start."
    }

    $slug = New-Slug ($(if ($Name) { $Name } else { $Prompt }))
    if ([string]::IsNullOrWhiteSpace($RunId)) {
        $RunId = "$(Get-Date -Format "yyyyMMdd-HHmmss")-$slug"
    }

    $runDir = Get-RunDirectory $RunId
    if (Test-Path -LiteralPath $runDir) {
        throw "Run already exists: $runDir"
    }

    New-Item -ItemType Directory -Force -Path $runDir | Out-Null
    New-Item -ItemType Directory -Force -Path $WorktreeRoot | Out-Null

    $worktreePath = Join-Path (Join-Path $WorktreeRoot $RunId) "RimBob"
    if ([string]::IsNullOrWhiteSpace($Branch)) {
        $Branch = "codex/prompt-$RunId"
    }

    $repoTop = Invoke-Git -Cwd $RepoRoot -Args @("rev-parse", "--show-toplevel")
    $baseCommit = Invoke-Git -Cwd $RepoRoot -Args @("rev-parse", $BaseRef)
    Invoke-Git -Cwd $RepoRoot -Args @("worktree", "add", "-b", $Branch, $worktreePath, $BaseRef) | Out-Null

    $promptPath = Join-Path $runDir "prompt.md"
    $eventsPath = Join-Path $runDir "events-start.jsonl"
    $lastMessagePath = Join-Path $runDir "final-message-start.md"
    $Prompt | Set-Content -LiteralPath $promptPath -Encoding UTF8

    $metadata = [ordered]@{
        run_id = $RunId
        status = "started"
        created_at = (Get-Date).ToString("o")
        updated_at = (Get-Date).ToString("o")
        repo_root = $repoTop
        base_ref = $BaseRef
        base_commit = $baseCommit
        branch = $Branch
        worktree = $worktreePath
        model = $Model
        model_reasoning_effort = $ReasoningEffort
        session_id = $null
        prompt_file = $promptPath
        event_files = @($eventsPath)
        final_message_files = @($lastMessagePath)
        codex_exit_codes = @()
        closed_at = $null
        landed_commit = $null
    }
    Write-RunMetadata -Id $RunId -Metadata $metadata

    $reasoningConfigArg = Get-ReasoningConfigArg -Effort $ReasoningEffort
    $exitCode = Invoke-CodexExec -Arguments @("exec", "-C", $worktreePath, "--json", "-m", $Model, "-c", $reasoningConfigArg, "-o", $lastMessagePath, "-") -EventsPath $eventsPath -InputPath $promptPath
    $session = Find-SessionId -EventsPath $eventsPath

    $scriptExitCode = $exitCode
    if (($exitCode -eq 0) -and [string]::IsNullOrWhiteSpace($session)) {
        $metadata.status = "completed_unresumable"
        $scriptExitCode = 2
    }
    else {
        $metadata.status = $(if ($exitCode -eq 0) { "completed" } else { "codex_failed" })
    }
    $metadata.updated_at = (Get-Date).ToString("o")
    $metadata.session_id = $session
    $metadata.codex_exit_codes = @($exitCode)
    Write-RunMetadata -Id $RunId -Metadata $metadata

    [pscustomobject]@{
        run_id = $RunId
        status = $metadata.status
        codex_exit_code = $exitCode
        script_exit_code = $scriptExitCode
        session_id = $session
        branch = $Branch
        worktree = $worktreePath
        metadata = (Join-Path $runDir "metadata.json")
        events = $eventsPath
        final_message = $lastMessagePath
    } | Format-List
    exit $scriptExitCode
}

if ($Mode -eq "Resume") {
    if ([string]::IsNullOrWhiteSpace($RunId)) {
        throw "-RunId is required for -Mode Resume."
    }
    if ([string]::IsNullOrWhiteSpace($Prompt)) {
        throw "-Prompt is required for -Mode Resume."
    }

    $metadata = Read-RunMetadata -Id $RunId
    if (($metadata.PSObject.Properties.Name -contains "model") -and -not [string]::IsNullOrWhiteSpace([string]$metadata.model)) {
        $Model = [string]$metadata.model
    }
    if (($metadata.PSObject.Properties.Name -contains "model_reasoning_effort") -and -not [string]::IsNullOrWhiteSpace([string]$metadata.model_reasoning_effort)) {
        $ReasoningEffort = [string]$metadata.model_reasoning_effort
    }
    if ([string]::IsNullOrWhiteSpace($SessionId)) {
        $SessionId = [string]$metadata.session_id
    }
    if ([string]::IsNullOrWhiteSpace($SessionId)) {
        throw "No session id is recorded for run $RunId. Inspect metadata/events and resume manually if possible."
    }
    if (-not (Test-Path -LiteralPath ([string]$metadata.worktree))) {
        throw "The recorded worktree no longer exists: $($metadata.worktree)"
    }

    $runDir = Get-RunDirectory $RunId
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $resumePromptPath = Join-Path $runDir "prompt-resume-$stamp.md"
    $eventsPath = Join-Path $runDir "events-resume-$stamp.jsonl"
    $lastMessagePath = Join-Path $runDir "final-message-resume-$stamp.md"
    $Prompt | Set-Content -LiteralPath $resumePromptPath -Encoding UTF8

    $reasoningConfigArg = Get-ReasoningConfigArg -Effort $ReasoningEffort
    $exitCode = Invoke-CodexExec -Arguments @("exec", "resume", "--json", "-m", $Model, "-c", $reasoningConfigArg, "-o", $lastMessagePath, $SessionId, "-") -EventsPath $eventsPath -InputPath $resumePromptPath

    $metadata.status = $(if ($exitCode -eq 0) { "resumed_completed" } else { "resume_failed" })
    $metadata.updated_at = (Get-Date).ToString("o")
    $metadata.event_files += $eventsPath
    $metadata.final_message_files += $lastMessagePath
    $metadata.codex_exit_codes += $exitCode
    Write-RunMetadata -Id $RunId -Metadata $metadata

    [pscustomobject]@{
        run_id = $RunId
        status = $metadata.status
        codex_exit_code = $exitCode
        session_id = $SessionId
        branch = $metadata.branch
        worktree = $metadata.worktree
        events = $eventsPath
        final_message = $lastMessagePath
    } | Format-List
    exit $exitCode
}

if ($Mode -eq "Show") {
    if ([string]::IsNullOrWhiteSpace($RunId)) {
        throw "-RunId is required for -Mode Show."
    }

    $metadata = Read-RunMetadata -Id $RunId
    $gitStatus = $null
    if (Test-Path -LiteralPath ([string]$metadata.worktree)) {
        $gitStatus = Invoke-Git -Cwd ([string]$metadata.worktree) -Args @("status", "--short", "--branch", "--untracked-files=all")
    }

    [pscustomobject]@{
        run_id = $metadata.run_id
        status = $metadata.status
        session_id = $metadata.session_id
        model = $(if ($metadata.PSObject.Properties.Name -contains "model") { $metadata.model } else { $null })
        model_reasoning_effort = $(if ($metadata.PSObject.Properties.Name -contains "model_reasoning_effort") { $metadata.model_reasoning_effort } else { $null })
        branch = $metadata.branch
        worktree = $metadata.worktree
        metadata = (Join-Path (Get-RunDirectory $RunId) "metadata.json")
        git_status = $gitStatus
        latest_final_message = @($metadata.final_message_files)[-1]
        latest_events = @($metadata.event_files)[-1]
    } | Format-List
    exit 0
}

if ($Mode -eq "CloseOut") {
    if ([string]::IsNullOrWhiteSpace($RunId)) {
        throw "-RunId is required for -Mode CloseOut."
    }

    $metadata = Read-RunMetadata -Id $RunId
    $worktree = [string]$metadata.worktree
    $childBranch = [string]$metadata.branch

    if (-not (Test-Path -LiteralPath $worktree)) {
        throw "The recorded worktree no longer exists: $worktree"
    }

    $status = Get-CleanStatus -Cwd $worktree
    if (-not [string]::IsNullOrWhiteSpace($status)) {
        throw "Child worktree is dirty. Commit or discard intentionally before closeout.`n$status"
    }

    if (-not $LandAndClose) {
        [pscustomobject]@{
            run_id = $metadata.run_id
            status = $metadata.status
            branch = $childBranch
            worktree = $worktree
            clean = $true
            note = "CloseOut inspected only. Add -LandAndClose -Verified to squash-land and remove the worktree."
        } | Format-List
        exit 0
    }

    if (-not $Verified) {
        throw "-Verified is required with -LandAndClose. Run the relevant build/tests or runtime checks first."
    }

    $counts = Invoke-Git -Cwd $worktree -Args @("rev-list", "--left-right", "--count", "$BaseRef...HEAD")
    $parts = $counts -split "\s+"
    $behind = [int]$parts[0]
    $ahead = [int]$parts[1]
    if ($behind -gt 0) {
        throw "Branch $childBranch is behind $BaseRef by $behind commit(s). Merge $BaseRef, resolve conflicts, and verify again before closeout."
    }
    if ($ahead -le 0) {
        throw "Branch $childBranch has no commits ahead of $BaseRef."
    }

    $currentBranch = Invoke-Git -Cwd $RepoRoot -Args @("branch", "--show-current")
    if ($currentBranch -ne "master") {
        throw "Refusing to land: $RepoRoot is on '$currentBranch', not 'master'."
    }

    $masterStatus = Get-CleanStatus -Cwd $RepoRoot
    if (-not [string]::IsNullOrWhiteSpace($masterStatus)) {
        throw "Refusing to land: $RepoRoot has local changes.`n$masterStatus"
    }

    Invoke-Git -Cwd $RepoRoot -Args @("merge", "--squash", $childBranch) | Out-Null
    $cachedDiff = & git -C $RepoRoot diff --cached --quiet
    if ($LASTEXITCODE -eq 0) {
        throw "Squash merge produced no staged changes."
    }

    $subject = "[codex] Land prompt run $RunId"
    $body = "Squash-merged delegated Codex prompt run.`n`nRun: $RunId`nBranch: $childBranch`nWorktree: $worktree`nSession: $($metadata.session_id)"
    Invoke-Git -Cwd $RepoRoot -Args @("commit", "-m", $subject, "-m", $body) | Out-Null
    $landedCommit = Invoke-Git -Cwd $RepoRoot -Args @("rev-parse", "HEAD")

    Invoke-Git -Cwd $RepoRoot -Args @("worktree", "remove", $worktree) | Out-Null
    Invoke-Git -Cwd $RepoRoot -Args @("branch", "-d", $childBranch) | Out-Null

    $metadata.status = "landed_and_closed"
    $metadata.updated_at = (Get-Date).ToString("o")
    $metadata.closed_at = (Get-Date).ToString("o")
    $metadata.landed_commit = $landedCommit
    Write-RunMetadata -Id $RunId -Metadata $metadata

    [pscustomobject]@{
        run_id = $RunId
        status = $metadata.status
        landed_commit = $landedCommit
        branch_deleted = $childBranch
        worktree_removed = $worktree
        metadata = (Join-Path (Get-RunDirectory $RunId) "metadata.json")
    } | Format-List
    exit 0
}

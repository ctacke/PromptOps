#Requires -Version 7
<#
End-to-end smoke test for the Phase 4b Claude Code plugin (claude-plugin/). Drives the actual hook
scripts against a real scratch git repo and a real running daemon — the same code path Claude Code
would invoke, just fed synthetic stdin JSON instead of a live interactive session, since this
script can't itself spawn a nested Claude Code session.
#>
param(
    [string]$BaseUrl = "http://127.0.0.1:5179"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$hooksDir = Join-Path $repoRoot "claude-plugin/hooks"
$scratchRepo = Join-Path ([System.IO.Path]::GetTempPath()) "promptops-plugin-smoke-$([guid]::NewGuid().ToString('N'))"
$pluginData = Join-Path ([System.IO.Path]::GetTempPath()) "promptops-plugin-smoke-data-$([guid]::NewGuid().ToString('N'))"

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Pass($msg) { Write-Host "    PASS: $msg" -ForegroundColor Green }
function Fail($msg) { Write-Host "    FAIL: $msg" -ForegroundColor Red; exit 1 }

function Invoke-Hook {
    param([string]$Name, [hashtable]$Payload)
    $json = $Payload | ConvertTo-Json -Compress
    $scriptPath = Join-Path $hooksDir "$Name.mjs"
    $json | node $scriptPath
    if ($LASTEXITCODE -ne 0) { Fail "$Name hook exited $LASTEXITCODE" }
}

function Wait-ForHealth {
    param([int]$TimeoutSeconds = 60)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $resp = Invoke-RestMethod -Uri "$BaseUrl/health" -Method Get -TimeoutSec 3
            if ($resp.status -eq "ok") { return }
        } catch {
            Start-Sleep -Seconds 2
        }
    }
    Fail "daemon never became healthy at $BaseUrl/health"
}

$env:PROMPTOPS_DAEMON_URL = $BaseUrl
$env:CLAUDE_PLUGIN_DATA = $pluginData

try {
    Push-Location $repoRoot

    Write-Step "Starting the daemon (docker compose up -d --build)"
    docker compose up -d --build
    if ($LASTEXITCODE -ne 0) { Fail "docker compose up failed" }
    Wait-ForHealth
    Write-Pass "daemon is healthy"

    Write-Step "Creating a scratch git repo at $scratchRepo"
    New-Item -ItemType Directory -Path $scratchRepo | Out-Null
    Push-Location $scratchRepo
    git init -q
    git config user.email "smoke-test@promptops.local"
    git config user.name "PromptOps Smoke Test"
    "hello" | Set-Content -Path "README.md"
    git add README.md
    git commit -q -m "initial commit"
    $sessionId = [guid]::NewGuid().ToString()
    Write-Pass "scratch repo ready, session_id=$sessionId"

    Write-Step "Simulating SessionStart"
    Invoke-Hook -Name "session-start" -Payload @{ session_id = $sessionId; cwd = $scratchRepo; source = "startup" }

    $statePath = Join-Path $pluginData "state/$sessionId.json"
    if (-not (Test-Path $statePath)) { Fail "SessionStart did not write execution state to $statePath" }
    $state = Get-Content $statePath -Raw | ConvertFrom-Json
    $executionId = $state.executionId
    if (-not $executionId) { Fail "execution state has no executionId" }
    Write-Pass "execution started: $executionId"

    $inProgress = Invoke-RestMethod -Uri "$BaseUrl/executions/$executionId" -Method Get
    if ($inProgress.status -ne "InProgress") { Fail "expected status InProgress right after SessionStart, got $($inProgress.status)" }
    Write-Pass "daemon confirms execution is InProgress"

    Write-Step "Simulating a tool call (PreToolUse -> file edit -> PostToolUse)"
    $toolUseId = "toolu_smoke_1"
    Invoke-Hook -Name "pre-tool-use" -Payload @{ session_id = $sessionId; tool_use_id = $toolUseId; tool_name = "Write" }
    "hello`nworld" | Set-Content -Path "README.md"
    "new file" | Set-Content -Path "NOTES.md"
    git add -A
    Invoke-Hook -Name "post-tool-use" -Payload @{ session_id = $sessionId; tool_use_id = $toolUseId; tool_name = "Write" }

    $afterTool = Invoke-RestMethod -Uri "$BaseUrl/executions/$executionId" -Method Get
    if ($afterTool.toolUsageCount -lt 1) { Fail "expected at least 1 recorded tool usage, got $($afterTool.toolUsageCount)" }
    Write-Pass "daemon recorded $($afterTool.toolUsageCount) tool usage entrie(s)"

    Write-Step "Simulating a second SessionStart for the same session_id without a SessionEnd in between (e.g. /clear, or a crash)"
    "more changes" | Add-Content -Path "README.md"
    Invoke-Hook -Name "session-start" -Payload @{ session_id = $sessionId; cwd = $scratchRepo; source = "startup" }

    $staleFinished = Invoke-RestMethod -Uri "$BaseUrl/executions/$executionId" -Method Get
    if ($staleFinished.status -ne "Finished") { Fail "expected the stale execution to be auto-finalized by the next SessionStart, got status $($staleFinished.status)" }
    if ($staleFinished.filesChanged.Count -lt 1) { Fail "expected the stale execution's diff stats to reflect the file changes made before the second SessionStart" }
    Write-Pass "stale execution $executionId was auto-finalized: filesChanged=$($staleFinished.filesChanged -join ','), linesAdded=$($staleFinished.linesAdded)"

    $newState = Get-Content $statePath -Raw | ConvertFrom-Json
    $executionId = $newState.executionId
    if ($executionId -eq $staleFinished.id) { Fail "expected a new execution id after the second SessionStart, got the same one back" }
    Write-Pass "a new execution opened: $executionId"

    $secondInProgress = Invoke-RestMethod -Uri "$BaseUrl/executions/$executionId" -Method Get
    if ($secondInProgress.status -ne "InProgress") { Fail "expected the new execution to be InProgress, got $($secondInProgress.status)" }
    Write-Pass "daemon confirms the new execution is InProgress"

    Write-Step "Simulating SessionEnd"
    Invoke-Hook -Name "session-end" -Payload @{ session_id = $sessionId; cwd = $scratchRepo }

    $final = Invoke-RestMethod -Uri "$BaseUrl/executions/$executionId" -Method Get
    if ($final.status -ne "Finished") { Fail "expected status Finished after SessionEnd, got $($final.status)" }
    if ($final.filesChanged.Count -lt 1) { Fail "expected at least 1 changed file, got $($final.filesChanged.Count)" }
    if ($final.linesAdded -lt 1) { Fail "expected linesAdded > 0, got $($final.linesAdded)" }
    Write-Pass "execution finished: filesChanged=$($final.filesChanged -join ','), linesAdded=$($final.linesAdded), linesDeleted=$($final.linesDeleted)"

    Write-Host ""
    Write-Host "Plugin smoke test passed." -ForegroundColor Green
}
finally {
    # Pop back to $repoRoot (undoes the $scratchRepo push) so `docker compose down` finds the compose file.
    Pop-Location -ErrorAction SilentlyContinue
    Write-Step "Cleaning up"
    Remove-Item -Recurse -Force $scratchRepo -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force $pluginData -ErrorAction SilentlyContinue
    docker compose down | Out-Null
    # Pop back to the original starting directory (undoes the $repoRoot push).
    Pop-Location -ErrorAction SilentlyContinue
}

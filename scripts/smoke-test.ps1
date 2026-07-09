#Requires -Version 7
<#
Scripted smoke test for the PromptOps daemon (Phase 4a acceptance criteria):
builds and starts the container, hits the ingestion API and the MCP endpoint,
restarts the container, and confirms data survived.
#>
param(
    [string]$BaseUrl = "http://127.0.0.1:5179"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Pass($msg) { Write-Host "    PASS: $msg" -ForegroundColor Green }
function Fail($msg) { Write-Host "    FAIL: $msg" -ForegroundColor Red; exit 1 }

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

try {
    Push-Location $repoRoot

    Write-Step "Building and starting the daemon (docker compose up -d --build)"
    docker compose up -d --build
    if ($LASTEXITCODE -ne 0) { Fail "docker compose up failed" }

    Write-Step "Waiting for /health"
    Wait-ForHealth
    Write-Pass "daemon is healthy"

    Write-Step "Confirming the daemon is unreachable except via loopback"
    $portBinding = docker compose port promptops-daemon 8080
    if ($portBinding -notmatch "^127\.0\.0\.1:") { Fail "expected the published port to bind 127.0.0.1, got: $portBinding" }
    Write-Pass "port is bound to 127.0.0.1 only ($portBinding)"

    Write-Step "Pushing a fixture execution via the ingestion API"
    $startBody = @{
        promptVersionId = [guid]::NewGuid().ToString()
        developerId     = "smoke-test"
        repository      = "promptops/smoke-test"
        branch          = "main"
        commit          = "abc1234"
        taskId          = "SMOKE-1"
        languages       = @("csharp")
    } | ConvertTo-Json

    $start = Invoke-RestMethod -Uri "$BaseUrl/executions/start" -Method Post -ContentType "application/json" -Body $startBody
    $executionId = $start.executionId
    if (-not $executionId) { Fail "start execution did not return an executionId" }
    Write-Pass "execution started: $executionId"

    Write-Step "Finishing the execution"
    $finishBody = @{
        output          = "smoke test output"
        executionTimeMs = 1234
        aiProviderId    = "manual"
        model           = "n/a"
        filesChanged    = @("README.md")
        linesAdded      = 3
        linesDeleted    = 1
    } | ConvertTo-Json

    Invoke-RestMethod -Uri "$BaseUrl/executions/$executionId/finish" -Method Post -ContentType "application/json" -Body $finishBody | Out-Null
    Write-Pass "execution finished"

    Write-Step "Checking the MCP endpoint is reachable and exposes health_check/version tools"
    $mcpHeaders = @{ "Accept" = "application/json, text/event-stream" }

    $initBody = @{
        jsonrpc = "2.0"
        id      = 1
        method  = "initialize"
        params  = @{
            protocolVersion = "2025-06-18"
            capabilities    = @{}
            clientInfo      = @{ name = "promptops-smoke-test"; version = "0.1.0" }
        }
    } | ConvertTo-Json -Depth 10

    $initResponse = Invoke-WebRequest -Uri "$BaseUrl/mcp" -Method Post -ContentType "application/json" -Headers $mcpHeaders -Body $initBody
    $sessionId = $initResponse.Headers["Mcp-Session-Id"]
    if (-not $sessionId) { Fail "MCP initialize did not return an Mcp-Session-Id header" }
    Write-Pass "MCP session established: $sessionId"

    $mcpHeaders["Mcp-Session-Id"] = "$sessionId"

    $initializedNotification = @{ jsonrpc = "2.0"; method = "notifications/initialized" } | ConvertTo-Json
    Invoke-WebRequest -Uri "$BaseUrl/mcp" -Method Post -ContentType "application/json" -Headers $mcpHeaders -Body $initializedNotification | Out-Null

    $listToolsBody = @{ jsonrpc = "2.0"; id = 2; method = "tools/list" } | ConvertTo-Json
    $toolsResponse = Invoke-WebRequest -Uri "$BaseUrl/mcp" -Method Post -ContentType "application/json" -Headers $mcpHeaders -Body $listToolsBody

    $toolsJson = $toolsResponse.Content
    if ($toolsResponse.Headers["Content-Type"] -like "text/event-stream*") {
        $dataLine = ($toolsJson -split "`n") | Where-Object { $_ -like "data:*" } | Select-Object -First 1
        $toolsJson = $dataLine -replace "^data:\s*", ""
    }
    $tools = ($toolsJson | ConvertFrom-Json).result.tools.name
    if ($tools -notcontains "health_check" -or $tools -notcontains "version") {
        Fail "expected health_check and version tools, got: $($tools -join ', ')"
    }
    Write-Pass "MCP tools/list returned health_check and version"

    Write-Step "Restarting the daemon to verify volume persistence"
    docker compose restart
    if ($LASTEXITCODE -ne 0) { Fail "docker compose restart failed" }
    Wait-ForHealth
    Write-Pass "daemon healthy after restart"

    Write-Step "Confirming the execution recorded before the restart is still there"
    $after = Invoke-RestMethod -Uri "$BaseUrl/executions/$executionId" -Method Get
    if ($after.id -ne $executionId) { Fail "execution not found after restart" }
    if ($after.status -ne "Finished") { Fail "expected status Finished, got $($after.status)" }
    Write-Pass "data survived the restart (status: $($after.status))"

    Write-Host ""
    Write-Host "Smoke test passed." -ForegroundColor Green
}
finally {
    Write-Step "Stopping the daemon (docker compose down)"
    docker compose down | Out-Null
    Pop-Location
}

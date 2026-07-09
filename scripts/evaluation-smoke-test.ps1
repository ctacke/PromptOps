#Requires -Version 7
<#
End-to-end smoke test for Phase 6's acceptance criterion:
"A developer can submit and retrieve a human evaluation for a given execution from within a
Claude Code session via /promptops rate."

This can't spawn a real nested Claude Code session, so it drives the real MCP protocol handshake
(initialize -> notifications/initialized -> tools/call) against the actual running daemon — the
same mechanism /promptops rate itself uses (submit_human_evaluation / get_human_evaluations) —
rather than only exercising the REST ingestion API (already covered by Host.Tests).
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

function Invoke-McpTool {
    param([hashtable]$Headers, [string]$Name, [hashtable]$Arguments, [int]$Id)
    $body = @{ jsonrpc = "2.0"; id = $Id; method = "tools/call"; params = @{ name = $Name; arguments = $Arguments } } | ConvertTo-Json -Depth 10
    $response = Invoke-WebRequest -Uri "$BaseUrl/mcp" -Method Post -Headers $Headers -Body $body

    $raw = $response.Content
    if ($response.Headers["Content-Type"] -like "text/event-stream*") {
        $dataLine = ($raw -split "`n") | Where-Object { $_ -like "data:*" } | Select-Object -First 1
        $raw = $dataLine -replace "^data:\s*", ""
    }
    $envelope = $raw | ConvertFrom-Json
    if ($envelope.result.isError) { Fail "MCP tool '$Name' returned an error: $($envelope.result.content[0].text)" }
    return $envelope.result.content[0].text | ConvertFrom-Json
}

try {
    Push-Location $repoRoot

    Write-Step "Starting the daemon (docker compose up -d --build)"
    docker compose up -d --build
    if ($LASTEXITCODE -ne 0) { Fail "docker compose up failed" }
    Wait-ForHealth
    Write-Pass "daemon is healthy"

    Write-Step "Starting a fixture execution"
    $startBody = @{
        promptVersionId = [guid]::NewGuid().ToString()
        developerId     = "smoke-test"
        repository      = "promptops/smoke-test"
    } | ConvertTo-Json
    $start = Invoke-RestMethod -Uri "$BaseUrl/executions/start" -Method Post -ContentType "application/json" -Body $startBody
    $executionId = $start.executionId
    Write-Pass "execution started: $executionId"

    Write-Step "Establishing an MCP session"
    $headers = @{ "Accept" = "application/json, text/event-stream"; "Content-Type" = "application/json" }
    $initBody = @{
        jsonrpc = "2.0"; id = 1; method = "initialize"
        params  = @{ protocolVersion = "2025-06-18"; capabilities = @{}; clientInfo = @{ name = "promptops-smoke-test"; version = "0.1.0" } }
    } | ConvertTo-Json -Depth 10
    $initResponse = Invoke-WebRequest -Uri "$BaseUrl/mcp" -Method Post -Headers $headers -Body $initBody
    $sessionId = $initResponse.Headers["Mcp-Session-Id"]
    if (-not $sessionId) { Fail "MCP initialize did not return an Mcp-Session-Id header" }
    $headers["Mcp-Session-Id"] = "$sessionId"
    $initializedNotification = @{ jsonrpc = "2.0"; method = "notifications/initialized" } | ConvertTo-Json
    Invoke-WebRequest -Uri "$BaseUrl/mcp" -Method Post -Headers $headers -Body $initializedNotification | Out-Null
    Write-Pass "MCP session established: $sessionId"

    Write-Step "Calling submit_human_evaluation over MCP"
    $submitted = Invoke-McpTool -Headers $headers -Id 2 -Name "submit_human_evaluation" -Arguments @{
        executionId        = $executionId
        evaluatorId        = "smoke-test@example.com"
        correctness        = 5
        helpfulness        = 4
        architecture       = 3
        readability        = 5
        completeness       = 4
        hallucinations     = $false
        confidence         = 5
        overallSatisfaction = 4
        notes              = "smoke test rating"
    }
    if (-not $submitted.id) { Fail "submit_human_evaluation did not return an id" }
    Write-Pass "evaluation submitted via MCP: $($submitted.id)"

    Write-Step "Calling get_human_evaluations over MCP"
    $fetched = Invoke-McpTool -Headers $headers -Id 3 -Name "get_human_evaluations" -Arguments @{ executionId = $executionId }
    if ($fetched.Count -ne 1) { Fail "expected 1 evaluation, got $($fetched.Count)" }
    if ($fetched[0].id -ne $submitted.id) { Fail "retrieved evaluation id doesn't match submitted id" }
    if ($fetched[0].correctness -ne 5) { Fail "expected correctness=5, got $($fetched[0].correctness)" }
    if ($fetched[0].notes -ne "smoke test rating") { Fail "notes did not round-trip" }
    Write-Pass "evaluation retrieved via MCP, fields match what was submitted"

    Write-Step "Cross-checking via the REST ingestion API"
    $viaRest = Invoke-RestMethod -Uri "$BaseUrl/executions/$executionId/evaluations" -Method Get
    if ($viaRest.Count -ne 1 -or $viaRest[0].id -ne $submitted.id) { Fail "REST GET does not agree with MCP retrieval" }
    Write-Pass "REST and MCP surfaces agree"

    Write-Host ""
    Write-Host "Evaluation smoke test passed." -ForegroundColor Green
}
finally {
    Write-Step "Stopping the daemon"
    docker compose down | Out-Null
    Pop-Location
}

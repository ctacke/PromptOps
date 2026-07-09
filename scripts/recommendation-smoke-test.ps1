#Requires -Version 7
<#
End-to-end smoke test for Phase 9's classify-then-recommend pipeline.

Known limitation: there is no ingestion API for creating a Prompt/PromptVersion/tags (Phase 2
never built one — out of Phase 9's scope to add just for this script), so this script can't seed
real recommendation candidates against the live daemon the way scripts/scoring-smoke-test.ps1
seeds executions/evaluations. What it *can* and does prove against the real running daemon:
- the classify-then-recommend wiring runs end-to-end over both REST and real MCP protocol calls
  without error on an empty database (the graceful-degradation path most other phases' smoke
  tests don't need, since "zero data anywhere" is the most extreme version of "brand-new repo with
  zero history" — RecommendationEndpointsTests.cs and TagAndHistoryRecommendationProviderTests.cs
  in the test suite are the authoritative proof of the actual ranking/rationale logic against
  seeded data, using PromptService directly since no HTTP surface exists for it).
- a classifier response that never parses degrades to an empty tag list (not an error) against the
  real AIActivityClassifier + ManualAIExecutionProvider, not just a unit-test stub.
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

    Write-Step "POST /recommendations with a well-formed classifier response against an empty database"
    $body = @{
        taskDescription = "getting a NullReferenceException, need to debug it"
        parameters      = @{ output = '["debugging"]' }
    } | ConvertTo-Json
    $results = Invoke-RestMethod -Uri "$BaseUrl/recommendations" -Method Post -ContentType "application/json" -Body $body
    if ($results.Count -ne 0) { Fail "expected an empty list against an empty database, got $($results.Count) results" }
    Write-Pass "classify-then-recommend pipeline ran end-to-end, degraded gracefully to an empty list"

    Write-Step "POST /recommendations with a classifier response that never parses as JSON"
    $badBody = @{
        taskDescription = "some task"
        parameters      = @{ output = "not json, ever, no matter how many times you ask" }
    } | ConvertTo-Json
    $badResults = Invoke-RestMethod -Uri "$BaseUrl/recommendations" -Method Post -ContentType "application/json" -Body $badBody
    if ($badResults.Count -ne 0) { Fail "expected an empty list, got $($badResults.Count) results" }
    Write-Pass "malformed classifier response degraded to an empty tag list, not an HTTP error"

    Write-Step "Confirming the same pipeline works over a real MCP tools/call round trip"
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

    $mcpResult = Invoke-McpTool -Headers $headers -Id 2 -Name "recommend_prompt" -Arguments @{ taskDescription = "write unit tests for the login flow" }
    if ($mcpResult.Count -ne 0) { Fail "expected an empty list via MCP, got $($mcpResult.Count) results" }
    Write-Pass "recommend_prompt MCP tool round-trips correctly"

    Write-Host ""
    Write-Host "Recommendation smoke test passed." -ForegroundColor Green
}
finally {
    Write-Step "Stopping the daemon"
    docker compose down | Out-Null
    Pop-Location
}

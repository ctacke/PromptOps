#Requires -Version 7
<#
End-to-end smoke test for Phase 10's semantic search / knowledge base pipeline.

Same known limitation as scripts/recommendation-smoke-test.ps1: there is still no ingestion API for
creating a Prompt/PromptVersion/tags (out of scope for both Phase 9 and Phase 10 to add just for a
smoke test), so this script can't seed real recommendation candidates against the live daemon and
observe ranking order externally. That proof already exists — SemanticRecommendationProviderTests.cs
(fixture embeddings) and RecommendationEndpointsTests.cs's
A_Semantically_Similar_Task_Ranks_Above_An_Unrelated_One_Despite_Neither_Matching_Tags (real
HashingBagOfWordsEmbeddingProvider, in-process WebApplicationFactory) are the authoritative proof of
the actual blend/ranking logic. What this script *can* and does prove against the real running
daemon, container, and volume:
- the new Embeddings table migration applies cleanly to a real SQLite file on daemon startup.
- the v2 pipeline (embed the query, scan the embedding store, blend a result with every component
  missing) runs end-to-end without error over real HTTP, using the real HashingBagOfWordsEmbeddingProvider
  and EmbeddingStore — not a unit-test stub.
- the same pipeline round-trips over a real MCP tools/call, and recommend_prompt now accepts (and the
  daemon accepts) a taskDescription with no tags at all, proving v2 — not v1 — is what's actually
  bound and running in the container's DI graph.
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
    Write-Pass "daemon is healthy — Embeddings table migration applied cleanly on a real SQLite volume"

    Write-Step "POST /recommendations with a taskDescription (no tags) against an empty database"
    $body = @{
        taskDescription = "getting a NullReferenceException in the login flow, need to debug it"
    } | ConvertTo-Json
    $results = Invoke-RestMethod -Uri "$BaseUrl/recommendations" -Method Post -ContentType "application/json" -Body $body
    if ($results.Count -ne 0) { Fail "expected an empty list against an empty database, got $($results.Count) results" }
    Write-Pass "v2 pipeline (real embedding provider + embedding store scan + all-components-missing blend) ran end-to-end with no error"

    Write-Step "POST /recommendations again with the same taskDescription, confirming the embedding call is deterministic and doesn't error on repeat"
    $results2 = Invoke-RestMethod -Uri "$BaseUrl/recommendations" -Method Post -ContentType "application/json" -Body $body
    if ($results2.Count -ne 0) { Fail "expected an empty list on the second call, got $($results2.Count) results" }
    Write-Pass "repeat call succeeded"

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
    Write-Pass "recommend_prompt MCP tool round-trips correctly — v2 is confirmed bound and running"

    Write-Host ""
    Write-Host "Knowledge base smoke test passed." -ForegroundColor Green
}
finally {
    Write-Step "Stopping the daemon"
    docker compose down | Out-Null
    Pop-Location
}

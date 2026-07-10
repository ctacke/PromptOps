#Requires -Version 7
<#
End-to-end smoke test for Phase 11's optional human evaluation / automatic prompt promotion.

Same known limitation as every recent phase's smoke test: there is still no ingestion API for
creating a Prompt/PromptVersion (out of scope to add just for a smoke test), so this script can't
seed a real prompt against the live daemon and observe an actual auto-promotion happen externally.
That proof already exists — AutoPromotionTriggerTests.cs (fakes, all threshold/margin/no-op cases)
and PromptRepositoryIntegrationTests.cs/PromptEndpointsTests.cs (real SQLite/real HTTP for the
manual activation path) are the authoritative proof of the actual decision logic. What this script
*can* and does prove against the real running daemon, container, and volume:
- the new PromotionPolicies table migration applies cleanly to a real SQLite file on daemon startup.
- GET/PUT /promotion-policy round-trip for real, including the validation rules (auto-promotion
  requires human-eval off; requires at least one of threshold/margin).
- POST /prompts/{id}/versions/{id}/activate against an empty database returns 404 through the real
  exception-mapping path, proving the endpoint and DI wiring are live.
- the three new MCP tools (activate_prompt_version, get_promotion_policy, update_promotion_policy)
  round-trip over a real MCP tools/call.
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
    Write-Pass "daemon is healthy — PromotionPolicies table migration applied cleanly on a real SQLite volume"

    Write-Step "GET /promotion-policy lazily bootstraps the default"
    $policy = Invoke-RestMethod -Uri "$BaseUrl/promotion-policy" -Method Get
    if ($policy.requireHumanEvaluation -ne $true -or $policy.autoPromotionEnabled -ne $false) { Fail "expected the unchanged default (require human eval, auto-promotion off), got $($policy | ConvertTo-Json -Compress)" }
    Write-Pass "default policy is require-human-evaluation, auto-promotion off"

    Write-Step "PUT /promotion-policy rejects enabling auto-promotion while human evaluation is still required"
    $invalidBody = @{ requireHumanEvaluation = $true; autoPromotionEnabled = $true; minimumScoreThreshold = 85.0; minimumMarginOverActive = $null } | ConvertTo-Json
    try {
        Invoke-RestMethod -Uri "$BaseUrl/promotion-policy" -Method Put -ContentType "application/json" -Body $invalidBody
        Fail "expected a 400 Bad Request, got success"
    } catch {
        if ($_.Exception.Response.StatusCode -ne 400) { Fail "expected 400, got $($_.Exception.Response.StatusCode)" }
    }
    Write-Pass "validation rule enforced for real over HTTP"

    Write-Step "PUT /promotion-policy accepts a valid configuration and GET reflects it"
    $validBody = @{ requireHumanEvaluation = $false; autoPromotionEnabled = $true; minimumScoreThreshold = 85.0; minimumMarginOverActive = $null } | ConvertTo-Json
    $updated = Invoke-RestMethod -Uri "$BaseUrl/promotion-policy" -Method Put -ContentType "application/json" -Body $validBody
    if ($updated.autoPromotionEnabled -ne $true -or $updated.minimumScoreThreshold -ne 85.0) { Fail "update did not take effect: $($updated | ConvertTo-Json -Compress)" }
    $reGet = Invoke-RestMethod -Uri "$BaseUrl/promotion-policy" -Method Get
    if ($reGet.autoPromotionEnabled -ne $true) { Fail "GET did not reflect the update" }
    Write-Pass "policy update round-trips for real"

    Write-Step "POST /prompts/{id}/versions/{id}/activate against an empty database returns 404"
    try {
        Invoke-RestMethod -Uri "$BaseUrl/prompts/$([guid]::NewGuid())/versions/$([guid]::NewGuid())/activate" -Method Post
        Fail "expected a 404 Not Found, got success"
    } catch {
        if ($_.Exception.Response.StatusCode -ne 404) { Fail "expected 404, got $($_.Exception.Response.StatusCode)" }
    }
    Write-Pass "manual activation endpoint and exception-mapping wiring are live"

    Write-Step "Confirming the new MCP tools round-trip over a real MCP tools/call"
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

    $mcpPolicy = Invoke-McpTool -Headers $headers -Id 2 -Name "get_promotion_policy" -Arguments @{}
    if ($mcpPolicy.autoPromotionEnabled -ne $true) { Fail "expected the policy updated earlier to still show autoPromotionEnabled=true via MCP" }
    Write-Pass "get_promotion_policy round-trips correctly"

    $mcpUpdated = Invoke-McpTool -Headers $headers -Id 3 -Name "update_promotion_policy" -Arguments @{ requireHumanEvaluation = $true; autoPromotionEnabled = $false }
    if ($mcpUpdated.requireHumanEvaluation -ne $true) { Fail "expected update_promotion_policy to take effect" }
    Write-Pass "update_promotion_policy round-trips correctly"

    $activateBody = @{ jsonrpc = "2.0"; id = 4; method = "tools/call"; params = @{ name = "activate_prompt_version"; arguments = @{ promptId = [guid]::NewGuid(); versionId = [guid]::NewGuid() } } } | ConvertTo-Json -Depth 10
    $activateResponse = Invoke-WebRequest -Uri "$BaseUrl/mcp" -Method Post -Headers $headers -Body $activateBody
    $activateRaw = $activateResponse.Content
    if ($activateResponse.Headers["Content-Type"] -like "text/event-stream*") {
        $activateDataLine = ($activateRaw -split "`n") | Where-Object { $_ -like "data:*" } | Select-Object -First 1
        $activateRaw = $activateDataLine -replace "^data:\s*", ""
    }
    $activateEnvelope = $activateRaw | ConvertFrom-Json
    if (-not $activateEnvelope.result.isError) { Fail "expected activate_prompt_version against an unknown prompt to report an error" }
    Write-Pass "activate_prompt_version round-trips correctly (reports the expected not-found error over MCP)"

    Write-Host ""
    Write-Host "Promotion policy smoke test passed." -ForegroundColor Green
}
finally {
    Write-Step "Stopping the daemon"
    docker compose down | Out-Null
    Pop-Location
}

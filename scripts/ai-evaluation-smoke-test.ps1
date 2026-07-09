#Requires -Version 7
<#
End-to-end smoke test for Phase 7's acceptance criteria:
- "Given an ExecutionRecord with AC/ADR references, the pipeline produces a structured, persisted
  AIEvaluation."
- "Judge output parsing is resilient to minor response-format drift (schema validation + retry,
  not brittle string matching)."

Runs against the real daemon: the real AIJudgeEvaluationProvider driving the real
ManualAIExecutionProvider, fed canned judge responses via parameters.output.
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

    Write-Step "Starting the daemon (docker compose up -d --build)"
    docker compose up -d --build
    if ($LASTEXITCODE -ne 0) { Fail "docker compose up failed" }
    Wait-ForHealth
    Write-Pass "daemon is healthy"

    Write-Step "Starting an execution with AC/ADR references"
    $startBody = @{
        promptVersionId    = [guid]::NewGuid().ToString()
        developerId        = "smoke-test"
        repository         = "promptops/smoke-test"
        acceptanceCriteria = @("Endpoint returns 404 for unknown ids", "No public API surface changes")
        referencedADRs     = @("ADR-0002")
    } | ConvertTo-Json
    $start = Invoke-RestMethod -Uri "$BaseUrl/executions/start" -Method Post -ContentType "application/json" -Body $startBody
    $executionId = $start.executionId
    Write-Pass "execution started: $executionId"

    Write-Step "Running the AI evaluation pipeline with a well-formed (fenced) judge response"
    $fencedJudgeResponse = @"
Here is my assessment of the change:
``````json
{"satisfiesAcceptanceCriteria": true, "adrViolations": [], "ignoredRequirements": [], "unnecessaryComplexityNotes": null, "suggestedPromptImprovements": ["mention the target file explicitly"]}
``````
Let me know if you'd like more detail.
"@
    $runBody = @{ parameters = @{ output = $fencedJudgeResponse } } | ConvertTo-Json
    $evaluation = Invoke-RestMethod -Uri "$BaseUrl/executions/$executionId/ai-evaluations" -Method Post -ContentType "application/json" -Body $runBody

    if ($evaluation.satisfiesAcceptanceCriteria -ne $true) { Fail "expected satisfiesAcceptanceCriteria=true, got $($evaluation.satisfiesAcceptanceCriteria)" }
    if ($evaluation.suggestedPromptImprovements.Count -ne 1) { Fail "expected 1 suggested improvement, got $($evaluation.suggestedPromptImprovements.Count)" }
    Write-Pass "judge output parsed despite markdown fences + surrounding prose (schema validation, not string matching)"

    Write-Step "Confirming GET returns the persisted evaluation"
    $fetched = Invoke-RestMethod -Uri "$BaseUrl/executions/$executionId/ai-evaluations" -Method Get
    if ($fetched.Count -ne 1 -or $fetched[0].id -ne $evaluation.id) { Fail "GET did not return the evaluation just persisted" }
    Write-Pass "AIEvaluation persisted and retrievable: $($evaluation.id)"

    Write-Step "Confirming a judge that never returns valid JSON surfaces as 502, not a silent empty result"
    $badRunBody = @{ parameters = @{ output = "this is not json no matter how many times you ask" } } | ConvertTo-Json
    try {
        Invoke-RestMethod -Uri "$BaseUrl/executions/$executionId/ai-evaluations" -Method Post -ContentType "application/json" -Body $badRunBody
        Fail "expected a 502 for a persistently malformed judge response, got success"
    } catch {
        $statusCode = $_.Exception.Response.StatusCode.value__
        if ($statusCode -ne 502) { Fail "expected HTTP 502, got $statusCode" }
        Write-Pass "malformed judge response correctly surfaced as 502 after exhausting retries"
    }

    Write-Step "Confirming 404 for an unknown execution"
    try {
        Invoke-RestMethod -Uri "$BaseUrl/executions/$([guid]::NewGuid())/ai-evaluations" -Method Post -ContentType "application/json" -Body $runBody
        Fail "expected a 404 for an unknown execution, got success"
    } catch {
        $statusCode = $_.Exception.Response.StatusCode.value__
        if ($statusCode -ne 404) { Fail "expected HTTP 404, got $statusCode" }
        Write-Pass "unknown execution correctly surfaced as 404"
    }

    Write-Host ""
    Write-Host "AI evaluation smoke test passed." -ForegroundColor Green
}
finally {
    Write-Step "Stopping the daemon"
    docker compose down | Out-Null
    Pop-Location
}

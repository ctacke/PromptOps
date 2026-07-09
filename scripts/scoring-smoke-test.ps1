#Requires -Version 7
<#
End-to-end smoke test for Phase 8's acceptance criteria:
- "Changing a ScoringConfig's weights changes computed scores deterministically per a documented
  formula."
- "Scores record which ScoringConfig version produced them (reproducibility)."

Runs against the real daemon: real executions, a real human evaluation and AI evaluation, and
three real ScoringConfig versions with different weights, proving the weighted-sum formula in
docs/scoring.md produces exact, predictable results.
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

function Weights([double]$humanRating = 0, [double]$sonar = 0, [double]$tests = 0, [double]$build = 0, [double]$acceptanceCriteria = 0, [double]$manualFixes = 0, [double]$reviewComments = 0, [double]$regressionBugs = 0) {
    return @{
        humanRating        = $humanRating
        sonar              = $sonar
        tests              = $tests
        build              = $build
        acceptanceCriteria = $acceptanceCriteria
        manualFixes        = $manualFixes
        reviewComments     = $reviewComments
        regressionBugs     = $regressionBugs
    }
}

try {
    Push-Location $repoRoot

    Write-Step "Starting the daemon (docker compose up -d --build)"
    docker compose up -d --build
    if ($LASTEXITCODE -ne 0) { Fail "docker compose up failed" }
    Wait-ForHealth
    Write-Pass "daemon is healthy"

    Write-Step "Starting and finishing an execution"
    $promptVersionId = [guid]::NewGuid().ToString()
    $startBody = @{ promptVersionId = $promptVersionId; developerId = "smoke-test"; repository = "promptops/smoke-test" } | ConvertTo-Json
    $start = Invoke-RestMethod -Uri "$BaseUrl/executions/start" -Method Post -ContentType "application/json" -Body $startBody
    $executionId = $start.executionId

    $finishBody = @{
        output = "diff"; executionTimeMs = 1000; filesChanged = @("a.cs"); linesAdded = 5; linesDeleted = 1
    } | ConvertTo-Json
    Invoke-RestMethod -Uri "$BaseUrl/executions/$executionId/finish" -Method Post -ContentType "application/json" -Body $finishBody | Out-Null
    Write-Pass "execution finished: $executionId"

    Write-Step "Submitting a human evaluation (overallSatisfaction=5 -> normalizes to 100)"
    $evalBody = @{
        evaluatorId = "smoke-test@example.com"; correctness = 5; helpfulness = 5; architecture = 5
        readability = 5; completeness = 5; hallucinations = $false; confidence = 5; overallSatisfaction = 5
    } | ConvertTo-Json
    Invoke-RestMethod -Uri "$BaseUrl/executions/$executionId/evaluations" -Method Post -ContentType "application/json" -Body $evalBody | Out-Null
    Write-Pass "human evaluation submitted (overallSatisfaction=5)"

    Write-Step "Running an AI evaluation (satisfiesAcceptanceCriteria=false -> 0)"
    $aiEvalBody = @{ parameters = @{ output = '{"satisfiesAcceptanceCriteria":false}' } } | ConvertTo-Json
    Invoke-RestMethod -Uri "$BaseUrl/executions/$executionId/ai-evaluations" -Method Post -ContentType "application/json" -Body $aiEvalBody | Out-Null
    Write-Pass "AI evaluation recorded (satisfiesAcceptanceCriteria=false)"

    Write-Step "Creating three ScoringConfig versions with different weights"
    $configName = "smoke-test-$([guid]::NewGuid().ToString('N'))"

    $v1Body = @{ name = $configName; weights = (Weights -humanRating 1.0) } | ConvertTo-Json
    $v1 = Invoke-RestMethod -Uri "$BaseUrl/scoring-configs" -Method Post -ContentType "application/json" -Body $v1Body
    if ($v1.version -ne 1) { Fail "expected version 1, got $($v1.version)" }

    $v2Body = @{ name = $configName; weights = (Weights -acceptanceCriteria 1.0) } | ConvertTo-Json
    $v2 = Invoke-RestMethod -Uri "$BaseUrl/scoring-configs" -Method Post -ContentType "application/json" -Body $v2Body
    if ($v2.version -ne 2) { Fail "expected version 2 (auto-incremented), got $($v2.version)" }

    $v3Body = @{ name = $configName; weights = (Weights -humanRating 0.5 -acceptanceCriteria 0.5) } | ConvertTo-Json
    $v3 = Invoke-RestMethod -Uri "$BaseUrl/scoring-configs" -Method Post -ContentType "application/json" -Body $v3Body
    Write-Pass "created config '$configName' versions 1, 2, 3"

    Write-Step "Recomputing under each config version and checking the exact predicted score"
    $score1 = Invoke-RestMethod -Uri "$BaseUrl/prompts/$promptVersionId/scores" -Method Post -ContentType "application/json" -Body (@{ scoringConfigId = $v1.id } | ConvertTo-Json)
    if ($score1.overallScore -ne 100.0) { Fail "v1 (humanRating only): expected 100, got $($score1.overallScore)" }
    if ($score1.scoringConfigId -ne $v1.id) { Fail "score does not record the config id that produced it" }
    Write-Pass "v1 (humanRating only): overallScore=100, scoringConfigId matches v1 (reproducibility)"

    $score2 = Invoke-RestMethod -Uri "$BaseUrl/prompts/$promptVersionId/scores" -Method Post -ContentType "application/json" -Body (@{ scoringConfigId = $v2.id } | ConvertTo-Json)
    if ($score2.overallScore -ne 0.0) { Fail "v2 (acceptanceCriteria only): expected 0, got $($score2.overallScore)" }
    if ($score2.scoringConfigId -ne $v2.id) { Fail "score does not record the config id that produced it" }
    Write-Pass "v2 (acceptanceCriteria only): overallScore=0, scoringConfigId matches v2 (reproducibility)"

    $score3 = Invoke-RestMethod -Uri "$BaseUrl/prompts/$promptVersionId/scores" -Method Post -ContentType "application/json" -Body (@{ scoringConfigId = $v3.id } | ConvertTo-Json)
    if ($score3.overallScore -ne 50.0) { Fail "v3 (even split): expected 50, got $($score3.overallScore)" }
    if ($score3.scoringConfigId -ne $v3.id) { Fail "score does not record the config id that produced it" }
    Write-Pass "v3 (even split): overallScore=50 -- weights alone deterministically changed the score across all three runs"

    Write-Step "Confirming score history has all three, oldest first"
    $history = Invoke-RestMethod -Uri "$BaseUrl/prompts/$promptVersionId/scores" -Method Get
    if ($history.Count -ne 3) { Fail "expected 3 score history rows, got $($history.Count)" }
    Write-Pass "score history has all 3 recomputes"

    Write-Host ""
    Write-Host "Scoring smoke test passed." -ForegroundColor Green
}
finally {
    Write-Step "Stopping the daemon"
    docker compose down | Out-Null
    Pop-Location
}

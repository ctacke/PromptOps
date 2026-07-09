#Requires -Version 7
<#
End-to-end smoke test for Phase 5's acceptance criteria:
- "Running the Sonar collector against a real project populates EngineeringMetrics fields."
  ("real project" here means a real ExecutionRecord + a real HTTP round trip against a server
  speaking Sonar's actual measures API shape — scripts/fake-sonar-server.mjs — since no real
  SonarQube/SonarCloud installation is available in this environment.)
- Pushing trx/Cobertura content populates a second, independent EngineeringMetrics row via
  BuildResultCollector, through the same generic /metrics/collect endpoint.
#>
param(
    [string]$BaseUrl = "http://127.0.0.1:5179",
    [int]$FakeSonarPort = 6789
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

$fakeSonar = $null
try {
    Push-Location $repoRoot

    Write-Step "Starting the fake Sonar server on port $FakeSonarPort"
    $fakeSonar = Start-Process -FilePath "node" -ArgumentList "scripts/fake-sonar-server.mjs", "$FakeSonarPort" -PassThru -NoNewWindow
    Start-Sleep -Seconds 1
    Write-Pass "fake Sonar server started (pid $($fakeSonar.Id))"

    Write-Step "Starting the daemon with Plugins__sonar__BaseUrl pointed at the fake Sonar server"
    $env:PROMPTOPS_SONAR_BASE_URL = "http://host.docker.internal:$FakeSonarPort"
    docker compose up -d --build
    if ($LASTEXITCODE -ne 0) { Fail "docker compose up failed" }
    Wait-ForHealth
    Write-Pass "daemon is healthy"

    $health = Invoke-RestMethod -Uri "$BaseUrl/health" -Method Get
    if ($health.pluginsLoaded -lt 2) { Fail "expected at least 2 plugins loaded (sonar, build-result), got $($health.pluginsLoaded)" }
    Write-Pass "$($health.pluginsLoaded) plugins loaded"

    Write-Step "Starting a fixture execution"
    $startBody = @{
        promptVersionId = [guid]::NewGuid().ToString()
        developerId     = "smoke-test"
        repository      = "promptops-smoke-test"
        branch          = "main"
        commit          = "abc1234"
    } | ConvertTo-Json
    $start = Invoke-RestMethod -Uri "$BaseUrl/executions/start" -Method Post -ContentType "application/json" -Body $startBody
    $executionId = $start.executionId
    Write-Pass "execution started: $executionId"

    Write-Step "Triggering metrics collection (Sonar self-serves; pushing trx+Cobertura for BuildResultCollector)"
    $trx = '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><ResultSummary outcome="Completed"><Counters total="20" executed="20" passed="19" failed="1" /></ResultSummary></TestRun>'
    $cobertura = '<coverage line-rate="0.812" branch-rate="0.6" version="1.9"></coverage>'
    $collectBody = @{ parameters = @{ trx = $trx; cobertura = $cobertura } } | ConvertTo-Json
    $collected = Invoke-RestMethod -Uri "$BaseUrl/executions/$executionId/metrics/collect" -Method Post -ContentType "application/json" -Body $collectBody

    if ($collected.Count -ne 2) { Fail "expected 2 collected metrics rows (sonar + build-result), got $($collected.Count)" }
    Write-Pass "collect returned $($collected.Count) metrics rows"

    Write-Step "Verifying the Sonar row"
    $sonarRow = $collected | Where-Object { $_.collectedBy -eq "sonar" }
    if (-not $sonarRow) { Fail "no 'sonar' row in the collect response" }
    if ($sonarRow.sonarIssues -ne 9) { Fail "expected sonarIssues=9, got $($sonarRow.sonarIssues)" }
    if ($sonarRow.securityFindings -ne 2) { Fail "expected securityFindings=2, got $($sonarRow.securityFindings)" }
    if ($sonarRow.codeSmells -ne 6) { Fail "expected codeSmells=6, got $($sonarRow.codeSmells)" }
    if ($sonarRow.coverage -ne 78.3) { Fail "expected coverage=78.3, got $($sonarRow.coverage)" }
    Write-Pass "sonar row: issues=$($sonarRow.sonarIssues) security=$($sonarRow.securityFindings) smells=$($sonarRow.codeSmells) coverage=$($sonarRow.coverage)"

    Write-Step "Verifying the build-result row"
    $buildRow = $collected | Where-Object { $_.collectedBy -eq "build-result" }
    if (-not $buildRow) { Fail "no 'build-result' row in the collect response" }
    if ($buildRow.buildSuccess -ne $true) { Fail "expected buildSuccess=true, got $($buildRow.buildSuccess)" }
    if ($buildRow.testSuccess -ne $false) { Fail "expected testSuccess=false (1 of 20 tests failed), got $($buildRow.testSuccess)" }
    if ($buildRow.coverage -ne 81.2) { Fail "expected coverage=81.2, got $($buildRow.coverage)" }
    Write-Pass "build-result row: buildSuccess=$($buildRow.buildSuccess) testSuccess=$($buildRow.testSuccess) coverage=$($buildRow.coverage)"

    Write-Step "Confirming GET /executions/{id}/metrics returns both rows"
    $fetched = Invoke-RestMethod -Uri "$BaseUrl/executions/$executionId/metrics" -Method Get
    if ($fetched.Count -ne 2) { Fail "expected 2 rows from GET, got $($fetched.Count)" }
    Write-Pass "GET confirms 2 persisted rows"

    Write-Host ""
    Write-Host "Metrics smoke test passed." -ForegroundColor Green
}
finally {
    Write-Step "Cleaning up"
    docker compose down | Out-Null
    if ($fakeSonar) { Stop-Process -Id $fakeSonar.Id -Force -ErrorAction SilentlyContinue }
    Remove-Item Env:\PROMPTOPS_SONAR_BASE_URL -ErrorAction SilentlyContinue
    Pop-Location
}

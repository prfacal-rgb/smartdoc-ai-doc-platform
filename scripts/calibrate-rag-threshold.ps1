<#
.SYNOPSIS
    Automatiza el round-trip de calibración de Rag:MaxRelevantDistance (ver
    calibration/README.md): login -> subir PDFs de calibration/pdfs -> esperar a que el
    Worker los procese -> correr calibration/questions.json contra POST /api/search -> volcar
    las distancias crudas a calibration/results/.

    Usa /api/search en lugar de /api/chat a propósito: devuelve el Distance crudo de cada
    candidato sin aplicar el threshold y sin llamar a /generate (Groq) - gratis y rápido de
    correr 30-50 veces.

.PARAMETER ApiBaseUrl
    Base URL de SmartDoc.Api. Default: http://localhost:5136 (ver launchSettings.json).

.PARAMETER TopK
    topK pasado a /api/search. Más alto que el default de la app (5) a propósito, para ver
    más cola de la distribución de distancias por pregunta.

.PARAMETER SkipUpload
    Si ya subiste los PDFs en una corrida anterior, saltea el paso de upload y va directo a
    correr las preguntas (evita re-subir y duplicar chunks en la base compartida).

.EXAMPLE
    ./scripts/calibrate-rag-threshold.ps1

.EXAMPLE
    ./scripts/calibrate-rag-threshold.ps1 -SkipUpload -TopK 15
#>
[CmdletBinding()]
param(
    [string]$ApiBaseUrl = "http://localhost:5136",
    [string]$Email = "dev@smartdoc.local",
    [string]$Password = "smartdoc_dev_password",
    [string]$PdfsDir = "$PSScriptRoot/../calibration/pdfs",
    [string]$QuestionsFile = "$PSScriptRoot/../calibration/questions.json",
    [string]$ResultsDir = "$PSScriptRoot/../calibration/results",
    [int]$TopK = 15,
    [int]$PollIntervalSeconds = 5,
    [int]$PollTimeoutSeconds = 900,
    [switch]$SkipUpload
)

$ErrorActionPreference = "Stop"

function Get-AuthToken {
    param([string]$BaseUrl, [string]$Email, [string]$Password)

    Write-Host "Logging in as $Email..."
    $body = @{ Email = $Email; Password = $Password } | ConvertTo-Json
    $response = Invoke-RestMethod -Uri "$BaseUrl/api/auth/login" -Method Post -Body $body -ContentType "application/json"
    return $response.Token
}

Add-Type -AssemblyName System.Net.Http

# Invoke-RestMethod's -Form parameter only exists on PowerShell 6+ (Core) - Windows
# PowerShell 5.1 (the default on a plain Windows box) doesn't have it. HttpClient works
# identically on both, so the multipart upload goes through it directly instead.
function Send-PdfUpload {
    param([string]$BaseUrl, [string]$BearerToken, [string]$FilePath)

    $client = [System.Net.Http.HttpClient]::new()
    try {
        $client.DefaultRequestHeaders.Authorization =
            [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $BearerToken)

        $content = [System.Net.Http.MultipartFormDataContent]::new()
        $fileBytes = [System.IO.File]::ReadAllBytes($FilePath)
        $byteContent = [System.Net.Http.ByteArrayContent]::new($fileBytes)
        $byteContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::new("application/pdf")
        $fileName = [System.IO.Path]::GetFileName($FilePath)
        $content.Add($byteContent, "file", $fileName)

        $response = $client.PostAsync("$BaseUrl/api/documents", $content).GetAwaiter().GetResult()
        $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if (-not $response.IsSuccessStatusCode) {
            throw "Upload failed for $fileName - $($response.StatusCode): $body"
        }
        return $body | ConvertFrom-Json
    }
    finally {
        $client.Dispose()
    }
}

function Publish-Pdfs {
    param([string]$BaseUrl, [hashtable]$Headers, [string]$BearerToken, [string]$PdfsDir)

    $existing = Invoke-RestMethod -Uri "$BaseUrl/api/documents" -Method Get -Headers $Headers

    $pdfs = Get-ChildItem -Path $PdfsDir -Filter "*.pdf" -File
    if ($pdfs.Count -eq 0) {
        throw "No .pdf files found in $PdfsDir - drop the calibration corpus there first."
    }

    foreach ($pdf in $pdfs) {
        $match = $existing | Where-Object { $_.fileName -eq $pdf.Name }

        if ($match -and $match.status -eq "Failed") {
            # A ProcessingJob that already exhausted its retries and landed on Failed is a
            # terminal state (ADR 0018) - nothing re-queues it on its own. Delete and
            # re-upload fresh instead, e.g. after fixing a bug that caused the failure.
            Write-Host "  $($pdf.Name) is Failed - deleting and re-uploading..."
            Invoke-RestMethod -Uri "$BaseUrl/api/documents/$($match.id)" -Method Delete -Headers $Headers | Out-Null
            $existing = $existing | Where-Object { $_.id -ne $match.id }
            $match = $null
        }

        if ($match) {
            Write-Host "  Skipping $($pdf.Name) - already uploaded."
            continue
        }

        Write-Host "  Uploading $($pdf.Name)..."
        $response = Send-PdfUpload -BaseUrl $BaseUrl -BearerToken $BearerToken -FilePath $pdf.FullName
        $existing += $response
    }

    # Re-derive from the corpus folder (not just what got uploaded *this run*) so a rerun
    # after a Wait-DocumentsProcessed timeout still waits on documents an earlier run already
    # uploaded but that hadn't finished processing yet.
    $corpusNames = $pdfs | ForEach-Object { $_.Name }
    return $existing | Where-Object { $corpusNames -contains $_.fileName } | ForEach-Object { $_.id }
}

function Wait-DocumentsProcessed {
    param([string]$BaseUrl, [hashtable]$Headers, [string[]]$DocumentIds, [int]$IntervalSeconds, [int]$TimeoutSeconds)

    if ($DocumentIds.Count -eq 0) {
        return
    }

    Write-Host "Waiting for the Worker to process $($DocumentIds.Count) new document(s)..."
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $pending = [System.Collections.Generic.HashSet[string]]::new([string[]]$DocumentIds)

    while ($pending.Count -gt 0) {
        if ((Get-Date) -gt $deadline) {
            throw "Timed out after $TimeoutSeconds s waiting for documents to finish processing: $($pending -join ', ')"
        }

        Start-Sleep -Seconds $IntervalSeconds
        foreach ($id in @($pending)) {
            $doc = Invoke-RestMethod -Uri "$BaseUrl/api/documents/$id" -Method Get -Headers $Headers
            if ($doc.status -eq "Ready") {
                Write-Host "  $($doc.fileName) -> Ready"
                $pending.Remove($id) | Out-Null
            }
            elseif ($doc.status -eq "Failed") {
                throw "Document $($doc.fileName) ($id) ended up Failed - check Worker logs before continuing."
            }
        }
    }
}

function Invoke-CalibrationQuestions {
    param([string]$BaseUrl, [hashtable]$Headers, [string]$QuestionsFile, [int]$TopK)

    $questions = Get-Content -Path $QuestionsFile -Raw | ConvertFrom-Json
    $results = @()

    foreach ($q in $questions) {
        Write-Host "  [$($q.category)] $($q.question)"
        $body = @{ Query = $q.question; TopK = $TopK } | ConvertTo-Json
        $response = Invoke-RestMethod -Uri "$BaseUrl/api/search" -Method Post -Headers $Headers -Body $body -ContentType "application/json"

        $results += [PSCustomObject]@{
            question     = $q.question
            category     = $q.category
            expectedFile = $q.expectedFile
            expectedPage = $q.expectedPage
            notes        = $q.notes
            matches      = $response.results | ForEach-Object {
                [PSCustomObject]@{
                    fileName   = $_.fileName
                    pageNumber = $_.pageNumber
                    distance   = $_.distance
                    textPreview = if ($_.text.Length -gt 120) { $_.text.Substring(0, 120) + "..." } else { $_.text }
                }
            }
        }
    }

    return $results
}

# --- main ---

$token = Get-AuthToken -BaseUrl $ApiBaseUrl -Email $Email -Password $Password
$headers = @{ Authorization = "Bearer $token" }

if (-not $SkipUpload) {
    Write-Host "`nUploading calibration corpus..."
    $corpusIds = Publish-Pdfs -BaseUrl $ApiBaseUrl -Headers $headers -BearerToken $token -PdfsDir $PdfsDir
    Wait-DocumentsProcessed -BaseUrl $ApiBaseUrl -Headers $headers -DocumentIds $corpusIds -IntervalSeconds $PollIntervalSeconds -TimeoutSeconds $PollTimeoutSeconds
}
else {
    Write-Host "`n-SkipUpload set - assuming the corpus is already uploaded and processed."
}

Write-Host "`nRunning calibration questions against /api/search (topK=$TopK)..."
$results = Invoke-CalibrationQuestions -BaseUrl $ApiBaseUrl -Headers $headers -QuestionsFile $QuestionsFile -TopK $TopK

New-Item -ItemType Directory -Path $ResultsDir -Force | Out-Null
$outputFile = Join-Path $ResultsDir "results-$(Get-Date -Format 'yyyyMMdd-HHmmss').json"
$results | ConvertTo-Json -Depth 6 | Set-Content -Path $outputFile -Encoding utf8

Write-Host "`nDone. Results written to $outputFile"

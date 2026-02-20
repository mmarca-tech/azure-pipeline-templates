
param(
    [string]$Environment,
    [string]$JiraUrl,
    [string]$ProjectKey,
    [string]$JiraUser,
    [string]$JiraToken,
    [string]$PipelineName,
    [string]$TicketPrefix = "CHG",
    [string]$ComponentName = "Your Service Name",
    [string]$SourceRepoName,  # Name of the source repository (for multi-checkout scenarios)
    [string]$AdoCollectionUri,
    [string]$AdoProjectName,
    [string]$IssueTypeName = "Task",
    [string]$IssueLinkTypeId = ""
)

Write-Host "Creating JIRA release ticket for environment: $Environment"

function Find-GitRepoPathFromName {
    param(
        [string]$BasePath,
        [string]$NameHint
    )

    if (-not $BasePath -or -not (Test-Path $BasePath) -or -not $NameHint) {
        return $null
    }

    $dirs = Get-ChildItem -Path $BasePath -Directory -ErrorAction SilentlyContinue
    if (-not $dirs) { return $null }

    $candidates = New-Object System.Collections.Generic.List[string]
    $name = $NameHint.Trim()
    if ($name) {
        $candidates.Add($name)
        if ($name -match '[\\/]') {
            $leaf = ($name -split '[\\/]')[-1]
            if ($leaf) { $candidates.Add($leaf) }
        }
    }

    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        $exactPath = Join-Path $BasePath $candidate
        if (Test-Path $exactPath) {
            if (Test-Path (Join-Path $exactPath ".git")) {
                return $exactPath
            }
        }

        $fuzzyMatches = $dirs | Where-Object {
            $_.Name -like "*$candidate*" -or $candidate -like "*$($_.Name)*"
        }
        foreach ($match in $fuzzyMatches) {
            $gitDir = Join-Path $match.FullName ".git"
            if (Test-Path $gitDir) {
                return $match.FullName
            }
        }
    }

    return $null
}

# Run the release notes script once and capture output
Write-Host "Generating release notes content..."
$releaseNotesScriptPaths = @()

# First priority: same directory as this script (using $PSScriptRoot)
if ($PSScriptRoot) {
    $releaseNotesScriptPaths += Join-Path $PSScriptRoot "generate-release-notes.ps1"
}

# Fallback: BUILD_SOURCESDIRECTORY with multi-checkout structure
if ($env:BUILD_SOURCESDIRECTORY) {
    $releaseNotesScriptPaths += Join-Path $env:BUILD_SOURCESDIRECTORY "release-pipeline-templates/scripts/generate-release-notes.ps1"
}

# Additional fallback paths
$releaseNotesScriptPaths += @(
    "./scripts/generate-release-notes.ps1",
    "../scripts/generate-release-notes.ps1",
    "../../scripts/generate-release-notes.ps1"
)
$releaseNotesScript = $null
foreach ($candidate in $releaseNotesScriptPaths) {
    if (Test-Path $candidate) {
        $releaseNotesScript = $candidate
        break
    }
}
if (-not $releaseNotesScript) {
    Write-Error ("Could not find generate-release-notes.ps1. Tried the following paths: `n" + ($releaseNotesScriptPaths -join "`n"))
    exit 1
}

# Determine the source repository path for multi-checkout scenarios
$sourceRepoPath = $null
if ($env:BUILD_SOURCESDIRECTORY) {
    # Debug: List all directories in BUILD_SOURCESDIRECTORY
    Write-Host "BUILD_SOURCESDIRECTORY: $env:BUILD_SOURCESDIRECTORY"
    Write-Host "Looking for SourceRepoName: $SourceRepoName"
    Write-Host "Contents of BUILD_SOURCESDIRECTORY:"
    $sourceDirs = Get-ChildItem -Path $env:BUILD_SOURCESDIRECTORY -Directory
    $sourceDirs | ForEach-Object { Write-Host "  - $($_.Name)" }

    if ($SourceRepoName) {
        $sourceRepoPath = Find-GitRepoPathFromName -BasePath $env:BUILD_SOURCESDIRECTORY -NameHint $SourceRepoName
        if (-not $sourceRepoPath) {
            Write-Warning "Could not resolve SourceRepoName '$SourceRepoName' to a checked-out git repo."
        }
    }

    if (-not $sourceRepoPath -and $env:BUILD_REPOSITORY_NAME) {
        Write-Host "Trying BUILD_REPOSITORY_NAME fallback: $env:BUILD_REPOSITORY_NAME"
        $sourceRepoPath = Find-GitRepoPathFromName -BasePath $env:BUILD_SOURCESDIRECTORY -NameHint $env:BUILD_REPOSITORY_NAME
    }

    if (-not $sourceRepoPath) {
        $templateRepoDirName = $null
        if ($PSScriptRoot) {
            $templateRepoDirName = Split-Path -Leaf (Split-Path -Parent $PSScriptRoot)
        }

        $gitRepoDirs = $sourceDirs | Where-Object { Test-Path (Join-Path $_.FullName ".git") }
        if ($templateRepoDirName) {
            $gitRepoDirs = $gitRepoDirs | Where-Object { $_.Name -ne $templateRepoDirName }
        }

        if ($gitRepoDirs.Count -eq 1) {
            $sourceRepoPath = $gitRepoDirs[0].FullName
            Write-Host "Auto-detected source repository path: $sourceRepoPath"
        } elseif ($gitRepoDirs.Count -gt 1) {
            $sourceRepoPath = $gitRepoDirs[0].FullName
            Write-Warning "Multiple git repos found. Defaulting to: $sourceRepoPath"
        }
    }

    if ($sourceRepoPath) {
        Write-Host "Using source repository path: $sourceRepoPath"
    }
}

if ($sourceRepoPath) {
    $releaseNotesOutput = & $releaseNotesScript -Env $Environment -RepoPath $sourceRepoPath
} else {
    $releaseNotesOutput = & $releaseNotesScript -Env $Environment
}

# Extract tickets from release notes based on TicketPrefix
$ticketPattern = "$TicketPrefix-\d+"
$implementsIssues = @()
foreach ($line in $releaseNotesOutput) {
    if ($line -match $ticketPattern) {
        $matchesFound = [regex]::Matches($line, $ticketPattern) | ForEach-Object { $_.Value }
        $implementsIssues += $matchesFound
    }
}
$implementsIssues = $implementsIssues | Select-Object -Unique

# First line contains the tag, rest is release notes content
$latestTag = ($releaseNotesOutput[0] -replace "Chosen env tag: ", "").Trim()
$releaseNotesMarkdown = ($releaseNotesOutput[1..($releaseNotesOutput.Count-1)] -join "`r`n").Trim()

if (-not $releaseNotesMarkdown) {
    Write-Error "Failed to extract release notes content"
    exit 1
}

Write-Host "Latest tag: $latestTag"

# Convert Markdown to Jira markup format
$jiraDescription = $releaseNotesMarkdown
$jiraDescription = $jiraDescription -replace '\*\*(.*?)\*\*', 'h4. $1'
$jiraDescription = $jiraDescription -replace '\[([A-Z][A-Z0-9]+-\d+)\]\(([^)]+)\)', '[$1|$2]'

# Prepare ticket metadata
$envUpper = $Environment.ToUpper()

# Calculate deployment time (5 days from now, rounded to nearest hour)
$nowUtc = [DateTimeOffset]::UtcNow.UtcDateTime
$in5Days = $nowUtc.AddDays(5)

# Round to nearest hour
if ($in5Days.Minute -ge 30) {
    # Round up to next hour
    $scheduledUtc = $in5Days.AddHours(1).AddMinutes(-$in5Days.Minute).AddSeconds(-$in5Days.Second).AddMilliseconds(-$in5Days.Millisecond)
} else {
    # Round down to current hour
    $scheduledUtc = $in5Days.AddMinutes(-$in5Days.Minute).AddSeconds(-$in5Days.Second).AddMilliseconds(-$in5Days.Millisecond)
}

# Format as ISO 8601 UTC (JIRA standard)
$startDate = $scheduledUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
$endDate = $scheduledUtc.AddHours(1).ToString("yyyy-MM-ddTHH:mm:ss.fffZ")

# Build JIRA ticket content
$buildId = $env:BUILD_BUILDID
$summary = "$ComponentName $envUpper Release - from $latestTag"

# Build Azure DevOps project base URL dynamically
$collectionUri = if ($AdoCollectionUri) { $AdoCollectionUri } else { $env:SYSTEM_COLLECTIONURI }
$projectName = if ($AdoProjectName) { $AdoProjectName } else { $env:SYSTEM_TEAMPROJECT }

$adoProjectBaseUrl = $null
if ($collectionUri -and $projectName) {
    $collectionUri = $collectionUri.TrimEnd('/')
    $projectNameEncoded = [System.Uri]::EscapeDataString($projectName)
    $adoProjectBaseUrl = "$collectionUri/$projectNameEncoded"
} else {
    Write-Warning "Skipping Azure DevOps links because collection/project information is not available."
}

# Get the latest build with liveDeployed tag
$buildIdPrevious = "notFound"
if ($adoProjectBaseUrl -and $PipelineName -and $env:SYSTEM_ACCESSTOKEN) {
    Write-Host "Getting latest liveDeployed build from '$PipelineName'..."
    try {
        $headers = @{
            'Authorization' = "Bearer $env:SYSTEM_ACCESSTOKEN"
            'Content-Type' = 'application/json'
        }
        
        # Get pipeline definition ID by name
        $pipelineNameEncoded = [System.Uri]::EscapeDataString($PipelineName)
        $pipelineUrl = "$adoProjectBaseUrl/_apis/build/definitions?name=$pipelineNameEncoded&api-version=7.0"
        $pipelineResponse = Invoke-RestMethod -Uri $pipelineUrl -Headers $headers -Method Get
        
        if ($pipelineResponse.value -and $pipelineResponse.value.Count -gt 0) {
            $definitionId = $pipelineResponse.value[0].id
            
            # Get the latest build with liveDeployed tag
            $apiUrl = "$adoProjectBaseUrl/_apis/build/builds?definitions=$definitionId&tagFilters=liveDeployed&`$top=1&api-version=7.0"
            $response = Invoke-RestMethod -Uri $apiUrl -Headers $headers -Method Get
            
            if ($response.value -and $response.value.Count -gt 0) {
                $buildIdPrevious = $response.value[0].id
                Write-Host "Found latest liveDeployed build: $buildIdPrevious"
            }
        }
    } catch {
        Write-Warning "Failed to get latest liveDeployed build: $($_.Exception.Message)"
    }
}

$linksSection = ""
if ($adoProjectBaseUrl) {
    $linksSection = "`n`nAutomation Links:`n- Redeploy from Build: $adoProjectBaseUrl/_build/results?buildId=$buildIdPrevious`n- Test Results for Build $buildId: $adoProjectBaseUrl/_build/results?buildId=$buildId&view=ms.vss-test-web.build-test-results-tab"
}

# Prepare the ticket payload
$ticketPayload = @{
    fields = @{
        project    = @{ key = $ProjectKey }
        issuetype  = @{ name = $IssueTypeName }
        summary    = $summary
        description = "Planned update of $ComponentName $envUpper environment.`nPlanned start (UTC): $startDate`nPlanned end (UTC): $endDate`n`n$jiraDescription$linksSection"
        priority = @{ name = "Medium" }
    }
}

# Create the JIRA ticket
Write-Host "Creating JIRA issue..."
$authHeader = @{
    Authorization = "Basic " + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${JiraUser}:${JiraToken}"))
}

$jsonPayload = $ticketPayload | ConvertTo-Json -Depth 10 -Compress
Write-Host "JIRA issue payload: $($jsonPayload)"
try {
    $response = Invoke-RestMethod -Uri "$JiraUrl/rest/api/2/issue" `
                                -Headers $authHeader `
                                -Method Post `
                                -Body $jsonPayload `
                                -ContentType "application/json"

    Write-Host "JIRA issue created successfully: $($response.key)"
    Write-Host "URL: $JiraUrl/browse/$($response.key)"

    # Set pipeline variable for use in later steps
    Write-Host "##vso[task.setvariable variable=JiraTicketKey;isOutput=true]$($response.key)"
    Write-Host "##vso[task.setvariable variable=JiraTicketUrl;isOutput=true]$JiraUrl/browse/$($response.key)"
    Write-Host "##vso[task.setvariable variable=PlannedStartDate;isOutput=true]$startDate"
    Write-Host "##vso[task.setvariable variable=PlannedEndDate;isOutput=true]$endDate"
    Write-Host "##vso[task.setvariable variable=ImplementsIssues;isOutput=true]$implementsIssues"
    
    if ($IssueLinkTypeId -and $implementsIssues.Count -gt 0) {
        foreach ($implementedKey in $implementsIssues) {
            $linkBody = @{
                type = @{ id = $IssueLinkTypeId }
                inwardIssue = @{ key = $response.key }
                outwardIssue = @{ key = $implementedKey }
            } | ConvertTo-Json -Compress

            try {
                Invoke-RestMethod -Uri "$JiraUrl/rest/api/2/issueLink" `
                    -Headers $authHeader `
                    -Method Post `
                    -Body $linkBody `
                    -ContentType "application/json"
                Write-Host "Linked $implementedKey to $($response.key)"
            } catch {
                Write-Warning "Failed to link $implementedKey to $($response.key): $_"
            }
        }
    } elseif ($implementsIssues.Count -gt 0) {
        Write-Warning "Skipping issue linking because IssueLinkTypeId was not provided."
    }

} catch {
    Write-Error "Failed to create JIRA issue: $_"
    Write-Error "Response: $($_.Exception.Response)"
    exit 1
}

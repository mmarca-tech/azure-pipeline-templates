param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("pt","live")]
    [string]$Env,

    [string]$RepoPath,              # Path to the repository to generate notes for

    [int]$MaxCommits = 0,           # 0 = no cap; cap AFTER collecting full range
    
    [switch]$DebugOutput            # Enable debug logging
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Die($msg) {
    Write-Error $msg
    exit 1
}

# Build git command args - use -C to run against target repo without changing directory
$script:gitBaseArgs = @()
if ($RepoPath) {
    if (-not (Test-Path $RepoPath)) {
        Die "Repository path does not exist: $RepoPath"
    }
    Write-Host "Using repository path: $RepoPath"
    $script:gitBaseArgs = @('-C', $RepoPath)
}

if ($DebugOutput) {
    Write-Host "Generating release notes for env=$Env using origin/main (latest reachable env tag on main)"
}

# -------- Git setup (no global pollution - use environment variables)
$env:GIT_CONFIG_COUNT = "1"
$env:GIT_CONFIG_KEY_0 = "safe.directory"
$env:GIT_CONFIG_VALUE_0 = "*"

# Auth for fetch via environment variable (requires checkout: self with persistCredentials: true)
if ($env:SYSTEM_ACCESSTOKEN) {
    $env:GIT_CONFIG_COUNT = "2"
    $env:GIT_CONFIG_KEY_1 = "http.https://dev.azure.com/.extraheader"
    $env:GIT_CONFIG_VALUE_1 = "AUTHORIZATION: bearer $($env:SYSTEM_ACCESSTOKEN)"
}

# Ensure full history for tags if shallow
$shallow = (& git @script:gitBaseArgs rev-parse --is-shallow-repository 2>&1) | Out-String
$shallow = $shallow.Trim()
if ($shallow -eq "true") {
    if ($DebugOutput) {
        Write-Host "Repository is shallow; unshallowing..."
    }
    try {
        & git @script:gitBaseArgs fetch --unshallow --tags 2>&1 | Out-Null
    } catch {
        if ($DebugOutput) {
            Write-Warning "Failed to unshallow repository: $_"
        }
    }
} else {
    # Fetch origin/main and tags in one operation
    try {
        & git @script:gitBaseArgs fetch origin '+refs/heads/main:refs/remotes/origin/main' '+refs/tags/*:refs/tags/*' --prune --force 2>&1 | Out-Null
    } catch {
        if ($DebugOutput) {
            Write-Warning "Failed to fetch from origin: $_"
        }
    }
}

$mainCommit = (& git @script:gitBaseArgs rev-parse HEAD 2>&1) | Out-String
$mainCommit = $mainCommit.Trim()
if (-not $mainCommit) { Die "Could not resolve HEAD." }
if ($DebugOutput) {
    Write-Host "Latest commit on HEAD: $mainCommit"
}

# -------- Enumerate env tags and find the latest reachable one efficiently
$tagFilterDescription = "containing 'live'"

# Get all tags with their commit info in one call, including commit dates.
# Filtering for env happens below so we can do case-insensitive "contains live".
$tagInfo = & git @script:gitBaseArgs for-each-ref '--format=%(refname:short)|%(objectname)|%(objecttype)|%(*objectname)|%(*committerdate:unix)|%(committerdate:unix)' 'refs/tags/*'

if (-not $tagInfo -or $tagInfo.Trim() -eq '') {
    Die "No tags found in repository."
}

$candidates = @()

foreach ($line in ($tagInfo -split "`r?`n")) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    $parts = $line -split "\|", 6
    if ($parts.Count -lt 6) { continue }

    $tagName = $parts[0].Trim()
    if ($tagName -notmatch '(?i)live') { continue }

    $obj = $parts[1].Trim()
    $objType = $parts[2].Trim()
    $derefObj = $parts[3].Trim()  # commit for annotated tags
    $derefDate = $parts[4].Trim() # commit date for annotated tags
    $objDate = $parts[5].Trim()   # object date

    # Determine the actual commit and its date
    if ($objType -eq "tag" -and $derefObj) {
        # Annotated tag
        $commitId = $derefObj
        $epoch = if ($derefDate) { $derefDate } else { "0" }
    } else {
        # Lightweight tag
        $commitId = $obj
        $epoch = if ($objDate) { $objDate } else { "0" }
    }
    
    if ([string]::IsNullOrWhiteSpace($commitId)) { continue }

    $candidates += [PSCustomObject]@{
        TagName     = $tagName
        Commit      = $commitId
        CommitEpoch = [int64]$epoch
    }
}

if ($candidates.Count -eq 0) {
    Die "No tags found $tagFilterDescription."
}

# Sort by date and check reachability from newest to oldest until we find one
$reachable = $null
foreach ($candidate in ($candidates | Sort-Object -Property CommitEpoch -Descending)) {
    # Check if this commit is reachable from HEAD
    & git @script:gitBaseArgs merge-base --is-ancestor $candidate.Commit HEAD 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) {
        $reachable = $candidate
        break
    }
}

if (-not $reachable) {
    Die "No tags $tagFilterDescription are reachable from HEAD."
}

# Choose the reachable tag (we already found the newest reachable one)
$nearestTag       = $reachable.TagName
$nearestTagCommit = $reachable.Commit
$nearestTagDate   = [DateTimeOffset]::FromUnixTimeSeconds($reachable.CommitEpoch).UtcDateTime

if ($DebugOutput) {
    Write-Host "Chosen env tag: $nearestTag"
    Write-Host "Tag commit: $nearestTagCommit"
    Write-Host "Tag commit date (UTC): $nearestTagDate"
}

# -------- Collect commits between chosen tag and main (newest -> oldest)
# Use cross-platform approach that works reliably
$gitArgs = $script:gitBaseArgs + @('log', '--no-decorate', '--pretty=format:%H|%s', "$nearestTag..$mainCommit")

try {
    # Use Start-Process to capture all output reliably
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'git'
    $psi.Arguments = $gitArgs -join ' '
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    
    $process = [System.Diagnostics.Process]::Start($psi)
    $output = $process.StandardOutput.ReadToEnd()
    $errorOutput = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    
    if ($process.ExitCode -ne 0) {
        Write-Error "Git command failed: $errorOutput"
        exit 1
    }
    
    $commitsRaw = $output
} catch {
    Write-Error "Failed to execute git command: $_"
    exit 1
}

if (-not $commitsRaw -or $commitsRaw.Trim() -eq '') {
    Write-Output "No commits found between $nearestTag and $mainCommit"
    exit 0
}

$commitLines = ($commitsRaw -split "`r?`n") | Where-Object { $_ -and $_.Trim().Length -gt 0 }

if ($DebugOutput) {
    Write-Host "Found $($commitLines.Count) commits to process"
}

# Optional cap AFTER full collection
if ($MaxCommits -gt 0 -and $commitLines.Count -gt $MaxCommits) {
    $commitLines = $commitLines[-$MaxCommits..-1]  # newest N, keep order
    if ($DebugOutput) {
        Write-Host "Capped to $($commitLines.Count) commits due to MaxCommits limit"
    }
}

# -------- Categorize
$features      = New-Object System.Collections.Generic.List[string]
$bugfixes      = New-Object System.Collections.Generic.List[string]
$nugets        = New-Object System.Collections.Generic.List[string]
$terraform     = New-Object System.Collections.Generic.List[string]
$uncategorized = New-Object System.Collections.Generic.List[string]

foreach ($line in $commitLines) {
    $parts = $line -split "\|", 2
    if ($parts.Count -lt 2) { continue }
    $msg = $parts[1].Trim()

    # Strip "Merged PR ####:"
    $msg = [regex]::Replace($msg, '^(?i)Merged PR\s?#?\d+:\s*', '')

    if ($msg -match '^(?i)Update (dependency )?Terraform') {
        $terraform.Add("- $msg"); continue
    }
    elseif ($msg -match '^(?i)Update dependency') {
        $nugets.Add("- $msg"); continue
    }
    elseif ($msg -match '^(?i)Update\b') {
        $words = $msg.Split(' ', [System.StringSplitOptions]::RemoveEmptyEntries)
        if ($words.Length -ge 2 -and ($words[1].ToLower() -notin @('dependency','terraform'))) {
            $nugets.Add("- $msg")
        } else {
            $uncategorized.Add("- $msg")
        }
        continue
    }

    $ticketMatch = [regex]::Match($msg, '(?i)\b([A-Z][A-Z0-9]+-\d+)\b')
    if ($ticketMatch.Success) {
        $ticket = $ticketMatch.Groups[1].Value.ToUpper()
        $clean  = ($msg -replace [regex]::Escape($ticket), '').Trim()
        $clean  = $clean.Trim(' ', ':', '-')
        # Remove additional Unicode dashes if present
        $clean  = $clean -replace '^[–—]+|[–—]+$', ''
        $clean  = $clean.Trim()
        if ($msg -match '(?i)\b(fix|bug|repair|resolve[ds]?|invalid)\b') {
            $bugfixes.Add("- [$ticket](https://your-domain.atlassian.net/browse/$ticket) $clean")
        } else {
            $features.Add("- [$ticket](https://your-domain.atlassian.net/browse/$ticket) $clean")
        }
    } else {
        $uncategorized.Add("- $msg")
    }
}

# -------- Markdown
$lines = New-Object System.Collections.Generic.List[string]
if ($features.Count -gt 0)      { $lines.Add("**New features**");     $features      | ForEach-Object { $lines.Add($_) };      $lines.Add("") }
if ($bugfixes.Count -gt 0)      { $lines.Add("**Bug fixes**");         $bugfixes      | ForEach-Object { $lines.Add($_) };      $lines.Add("") }
if ($nugets.Count -gt 0)        { $lines.Add("**Nuget Updates**");     $nugets        | ForEach-Object { $lines.Add($_) };      $lines.Add("") }
if ($terraform.Count -gt 0)     { $lines.Add("**Terraform Updates**"); $terraform     | ForEach-Object { $lines.Add($_) };      $lines.Add("") }
if ($uncategorized.Count -gt 0) { $lines.Add("**Uncategorized**");     $uncategorized | ForEach-Object { $lines.Add($_) };      $lines.Add("") }

$markdown = [string]::Join("`n", $lines)

# Output tag first, then release notes
Write-Output "Chosen env tag: $nearestTag"
Write-Output $markdown

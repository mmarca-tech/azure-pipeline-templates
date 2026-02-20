param(
    [Parameter(Mandatory = $true)]
    [string]$JiraUrl,
    
    [Parameter(Mandatory = $true)]
    [string]$ImplementedTickets,
    
    [Parameter(Mandatory = $true)]
    [string]$JiraTicketKey,
    
    [Parameter(Mandatory = $true)]
    [string]$JiraUser,
    
    [Parameter(Mandatory = $true)]
    [string]$JiraToken
)

Write-Host "Updating released JIRA tickets to Done status"
Write-Host "Implemented tickets: $ImplementedTickets"

# Build authentication header
$base64AuthInfo = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${JiraUser}:${JiraToken}"))
$headers = @{
    "Authorization" = "Basic $base64AuthInfo"
    "Content-Type"  = "application/json"
}

# Transition ID for "Done" status
$transitionId = "31"

# Parse the implemented tickets (comma-separated list)
$tickets = $ImplementedTickets -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' }

if ($tickets.Count -eq 0) {
    Write-Host "No implemented tickets to update."
    exit 0
}

Write-Host "Found $($tickets.Count) ticket(s) to transition to Done"

$successCount = 0
$failCount = 0

foreach ($ticketKey in $tickets) {
    Write-Host "`n----------------------------------------"
    Write-Host "Processing ticket: ${ticketKey}"
    
    $transitionBody = @{
        transition = @{
            id = $transitionId
        }
    } | ConvertTo-Json -Depth 10

    $transitionUrl = "$JiraUrl/rest/api/3/issue/$ticketKey/transitions"

    Write-Host "Transitioning ticket to Done..."
    Write-Host "URL: $transitionUrl"

    try {
        $response = Invoke-RestMethod -Uri $transitionUrl -Method Post -Headers $headers -Body $transitionBody -ErrorAction Stop
        Write-Host "Successfully transitioned ticket to Done: ${ticketKey}"
        Write-Host "Ticket URL: ${JiraUrl}/browse/${ticketKey}"
        $successCount++
    }
    catch {
        $statusCode = $_.Exception.Response.StatusCode.value__
        $errorMessage = $_.ErrorDetails.Message
        
        Write-Warning "Failed to transition JIRA ticket: ${ticketKey}. Status code: ${statusCode}"
        Write-Warning "Error details: $errorMessage"
        
        # If it's a 400 error, it might be that the transition is not available
        if ($statusCode -eq 400) {
            Write-Host "`nAttempting to fetch available transitions for ticket..."
            try {
                $availableTransitions = Invoke-RestMethod -Uri $transitionUrl -Method Get -Headers $headers
                Write-Host "Available transitions for ${ticketKey}"
                foreach ($t in $availableTransitions.transitions) {
                    Write-Host "  - ID: $($t.id), Name: $($t.name)"
                }
            }
            catch {
                Write-Warning "Could not fetch available transitions: $_"
            }
        }
        
        $failCount++
    }
}

Write-Host "`n========================================"
Write-Host "Ticket update summary:"
Write-Host "  - Total tickets: $($tickets.Count)"
Write-Host "  - Successfully transitioned: $successCount"
Write-Host "  - Failed: $failCount"
Write-Host "========================================"

if ($failCount -gt 0) {
    Write-Warning "Some tickets failed to transition. Please check the logs above."
    # Don't exit with error to allow pipeline to continue
    # Uncomment the line below if you want the pipeline to fail when any ticket fails
    # exit 1
}

# Transition the release ticket (JiraTicketKey) to Done
if ($JiraTicketKey -and $JiraTicketKey -ne '') {
    Write-Host "`n========================================"
    Write-Host "Transitioning release ticket to Done: $JiraTicketKey"
    Write-Host "========================================"
    
    $transitionBody = @{
        transition = @{
            id = $transitionId
        }
    } | ConvertTo-Json -Depth 10

    $transitionUrl = "$JiraUrl/rest/api/3/issue/$JiraTicketKey/transitions"

    try {
        $response = Invoke-RestMethod -Uri $transitionUrl -Method Post -Headers $headers -Body $transitionBody -ErrorAction Stop
        Write-Host "Successfully transitioned release ticket to Done: $JiraTicketKey"
        Write-Host "Ticket URL: ${JiraUrl}/browse/${JiraTicketKey}"
    }
    catch {
        $statusCode = $_.Exception.Response.StatusCode.value__
        $errorMessage = $_.ErrorDetails.Message
        
        Write-Warning "Failed to transition release ticket: $JiraTicketKey. Status code: $statusCode"
        Write-Warning "Error details: $errorMessage"
        
        if ($statusCode -eq 400) {
            Write-Host "`nAttempting to fetch available transitions for release ticket..."
            try {
                $availableTransitions = Invoke-RestMethod -Uri $transitionUrl -Method Get -Headers $headers
                Write-Host "Available transitions for $JiraTicketKey"
                foreach ($t in $availableTransitions.transitions) {
                    Write-Host "  - ID: $($t.id), Name: $($t.name)"
                }
            }
            catch {
                Write-Warning "Could not fetch available transitions: $_"
            }
        }
    }
}
else {
    Write-Warning "No JiraTicketKey provided. Skipping release ticket transition."
}

Write-Host "`nTicket update completed."

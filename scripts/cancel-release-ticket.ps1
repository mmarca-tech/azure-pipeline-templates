
param(
    [Parameter(Mandatory = $true)]
    [string]$JiraUrl,
    
    [Parameter(Mandatory = $true)]
    [string]$JiraTicketKey,
    
    [Parameter(Mandatory = $true)]
    [string]$JiraUser,
    
    [Parameter(Mandatory = $true)]
    [string]$JiraToken
)

Write-Host "Cancelling JIRA ticket: $JiraTicketKey"

# Build authentication header
$base64AuthInfo = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${JiraUser}:${JiraToken}"))
$headers = @{
    "Authorization" = "Basic $base64AuthInfo"
    "Content-Type"  = "application/json"
}

# Transition ID for "Cancelled" status
$transitionId = "101"

$transitionBody = @{
    transition = @{
        id = $transitionId
    }
} | ConvertTo-Json -Depth 10

$transitionUrl = "$JiraUrl/rest/api/3/issue/$JiraTicketKey/transitions"

Write-Host "Transitioning ticket to Cancelled..."
Write-Host "URL: $transitionUrl"

try {
    $response = Invoke-RestMethod -Uri $transitionUrl -Method Post -Headers $headers -Body $transitionBody -ErrorAction Stop
    Write-Host "Successfully cancelled ticket: $JiraTicketKey"
    Write-Host "Ticket URL: $JiraUrl/browse/$JiraTicketKey"
}
catch {
    $statusCode = $_.Exception.Response.StatusCode.value__
    $errorMessage = $_.ErrorDetails.Message
    
    Write-Error "Failed to cancel JIRA ticket. Status code: $statusCode"
    Write-Error "Error details: $errorMessage"
    
    # If it's a 400 error, it might be that the transition is not available
    if ($statusCode -eq 400) {
        Write-Host "`nAttempting to fetch available transitions for ticket..."
        try {
            $availableTransitions = Invoke-RestMethod -Uri $transitionUrl -Method Get -Headers $headers
            Write-Host "Available transitions:"
            foreach ($t in $availableTransitions.transitions) {
                Write-Host "  - ID: $($t.id), Name: $($t.name)"
            }
        }
        catch {
            Write-Error "Could not fetch available transitions: $_"
        }
    }
    
    exit 1
}

Write-Host "`nTicket cancellation completed successfully."

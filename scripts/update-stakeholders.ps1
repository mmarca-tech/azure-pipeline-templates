param(
    [string]$JiraTicketKey,
    [string]$JiraTicketUrl,
    [string]$PlannedStartDate,
    [string]$PlannedEndDate,
    [string]$PackageVersion,
    [string]$WebhookUrl,
    [string]$action,
    [string]$ComponentName = "Your Service Name"
)

Write-Host "Sending Teams notification for ticket: $JiraTicketKey"
Write-Host "Planned Start: $PlannedStartDate"
Write-Host "Planned End: $PlannedEndDate"

$body = @{
    type = "Application/JSON"
    attachments = @(
        @{
            contentType = "application/vnd.microsoft.card.adaptive"
            content = @{
                type = "AdaptiveCard"
                body = @(
                    @{
                        type = "ColumnSet"
                        columns = @(
                            @{
                                type = "Column"
                                items = @(
                                    @{
                                        type = "Image"
                                        style = "Person"
                                        url = "https://adaptivecards.io/content/cats/3.png"
                                        altText = "CAT"
                                        size = "small"
                                    }
                                )
                                width = "auto"
                            },
                            @{
                                type = "Column"
                                items = @(
                                    @{
                                        type = "TextBlock"
                                        weight = "Bolder"
                                        text = "A release has been $action!"
                                        wrap = $true
                                    },
                                    @{
                                        type = "TextBlock"
                                        spacing = "None"
                                        text = "[$JiraTicketKey] $ComponentName Release $PackageVersion"
                                        isSubtle = $true
                                        wrap = $true
                                    }
                                )
                                width = "stretch"
                            }
                        )
                    },
                    @{
                        type = "TextBlock"
                        text = "Planned deployment of $ComponentName has been $action"
                        wrap = $true
                    },
                    @{
                        type = "FactSet"
                        facts = @(
                            @{
                                title = "Planned start date:"
                                value = $PlannedStartDate
                            },
                            @{
                                title = "Planned end date:"
                                value = $PlannedEndDate
                            }
                        )
                    }
                )
                actions = @(
                    @{
                        type = "Action.OpenUrl"
                        title = "See release ticket"
                        url = $JiraTicketUrl
                    }
                )
                "`$schema" = "http://adaptivecards.io/schemas/adaptive-card.json"
                version = "1.4"
            }
        }
    )
} | ConvertTo-Json -Depth 20

try {
    Invoke-RestMethod -Uri $WebhookUrl -Method Post -Body $body -ContentType "application/json"
    Write-Host "Teams notification sent successfully!"
} catch {
    Write-Error "Failed to send Teams notification: $_"
    exit 1
}

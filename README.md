# Release Pipeline Templates

Reusable Azure DevOps pipeline templates for managing release processes with JIRA integration and Teams notifications.

## Repository Structure

```
release-pipeline-templates/
├── templates/
│   └── release-process-stages.yaml    # Main pipeline template
└── scripts/
    ├── create-release-ticket.ps1      # Creates JIRA release tickets
    ├── cancel-release-ticket.ps1      # Cancels JIRA release tickets
    ├── generate-release-notes.ps1     # Generates release notes from git history
    ├── update-released-tickets.ps1    # Transitions implemented tickets to Done
    └── update-stakeholders.ps1        # Sends Teams notifications
```

## Templates

### release-process-stages.yaml

A multi-stage pipeline template that automates the release process workflow:

| Stage | Description |
|-------|-------------|
| `release_process_create_ticket` | Creates a JIRA release ticket with auto-generated release notes |
| `release_process_update_stakeholders` | Sends Teams notification when ticket is created |
| `release_process_cancel_release` | Cancels the release and notifies stakeholders |
| `release_process_deployed_update_stakeholders` | Notifies stakeholders after successful deployment |

#### Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `skipManualApproval` | boolean | `false` | Skip manual approval steps (useful for automated releases) |
| `environment` | string | `'live'` | Target environment |
| `jiraUrl` | string | `'https://your-domain.atlassian.net'` | JIRA instance URL |
| `jiraUserEmail` | string | *required* | JIRA user email for authentication |
| `jiraApiToken` | string | *required* | JIRA API token for authentication |
| `projectKey` | string | `'CHG'` | JIRA project key |
| `pipelineName` | string | *required* | Name of the pipeline |
| `ticketPrefix` | string | `'CHG'` | Prefix for ticket references |
| `componentName` | string | *required* | Name of the component being released |
| `webhookUrl` | string | *required* | Teams webhook URL for notifications |
| `deployedStageDependsOn` | string | `'Deploy_Live'` | Stage to depend on for deployment notification |
| `createTicketDependsOn` | object | `[]` | Stages to depend on before creating ticket |
| `createTicketCondition` | string | *main branch condition* | Condition for creating ticket |

## Scripts

### create-release-ticket.ps1

Creates a JIRA release ticket with auto-generated release notes from git history.

**Parameters:**
- `-Environment` - Target environment (pt/live)
- `-JiraUrl` - JIRA instance URL
- `-ProjectKey` - JIRA project key
- `-JiraUser` - JIRA username
- `-JiraToken` - JIRA API token
- `-PipelineName` - Pipeline name for build lookup
- `-TicketPrefix` - Ticket reference prefix (default: "CHG")
- `-ComponentName` - Component name (default: "Your Service Name")
- `-IssueTypeName` - JIRA issue type name (default: "Task")
- `-IssueLinkTypeId` - Optional JIRA issue link type ID for linking implemented tickets

### cancel-release-ticket.ps1

Transitions a JIRA ticket to "Cancelled" status.

**Parameters:**
- `-JiraUrl` - JIRA instance URL
- `-JiraTicketKey` - The ticket key to cancel
- `-JiraUser` - JIRA username
- `-JiraToken` - JIRA API token

### generate-release-notes.ps1

Generates release notes by comparing git tags and extracting commit information.

**Parameters:**
- `-Env` - Target environment (pt/live)
- `-MaxCommits` - Maximum commits to include (0 = no cap)
- `-DebugOutput` - Enable debug logging

### update-released-tickets.ps1

Transitions implemented JIRA tickets to "Done" status after successful deployment.

**Parameters:**
- `-JiraUrl` - JIRA instance URL
- `-ImplementedTickets` - Comma-separated list of ticket keys to transition
- `-JiraUser` - JIRA username
- `-JiraToken` - JIRA API token

### update-stakeholders.ps1

Sends an Adaptive Card notification to Microsoft Teams.

**Parameters:**
- `-JiraTicketKey` - The JIRA ticket key
- `-JiraTicketUrl` - URL to the JIRA ticket
- `-PlannedStartDate` - Planned deployment start date
- `-PlannedEndDate` - Planned deployment end date
- `-PackageVersion` - Version being deployed
- `-WebhookUrl` - Teams webhook URL
- `-action` - Action type (CREATED/UPDATED, CANCELED, DEPLOYED)
- `-ComponentName` - Component name (default: "Your Service Name")

## Usage

Reference the template in your Azure DevOps pipeline:

```yaml
resources:
  repositories:
    - repository: release-pipeline-templates
      type: git
      name: release-pipeline-templates
      ref: main

stages:
  # Your deployment stages here...
  
  - template: templates/release-process-stages.yaml@release-pipeline-templates
    parameters:
      environment: 'live'
      jiraUrl: 'https://your-domain.atlassian.net'
      jiraUserEmail: $(jira-bot-email)
      jiraApiToken: $(jira-bot-token)
      projectKey: 'CHG'
      pipelineName: 'your-service-name'
      ticketPrefix: 'CHG'
      componentName: 'Your Service Name'
      webhookUrl: $(release-process-webhook-url)
      deployedStageDependsOn: 'Deploy_Live'
      createTicketDependsOn: [ 'Get_Jira_Secrets' ]
      createTicketCondition: "and(eq( variables['isMainBranch'], 'true' ), ne( variables['isPullRequest'],  true ))"
      templateRepo: 'release-pipeline-templates'
```

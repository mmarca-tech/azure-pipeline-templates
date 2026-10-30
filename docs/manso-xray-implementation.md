# Manso Xray Implementation Stage

Stage template that runs the Manso testing agent to generate or implement Xray automation tests after a commit reaches `main`. The agent reads the first Jira ticket key from the commit message, installs `manso-testing-agent`, and runs `implement-xray-tests`.

**Template:** `templates/manso-xray-implementation-stage.yaml`

**Implementation:** `scripts/MansoXrayImplementation.cs`

## What it does

1. Optionally detects whether changed files match configured path filters
2. Checks out the exact source commit with credentials for branch push
3. Loads Manso agent secrets from Azure Key Vault
4. Authenticates to Azure Artifacts NuGet and npm feeds
5. Extracts the first Jira key from the commit message
6. Runs `manso-testing-agent` to implement Xray tests
7. Publishes the agent output as a pipeline artifact

When `runOnlyWhenChangedPaths` is set, a preceding detection stage compares changed files against the configured prefixes and skips the main stage when nothing matches.

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `stageName` | string | `Manso_Xray_Implementation` | Stage identifier |
| `stageDisplayName` | string | `Manso Xray Test Implementation` | Stage display name |
| `jobName` | string | `manso_xray_implementation` | Job identifier |
| `jobDisplayName` | string | `Run Manso from Jira ticket in commit message` | Job display name |
| `dependsOn` | object | `[]` | Stage dependencies |
| `condition` | string | main branch, not PR | Stage condition |
| `runOnlyWhenChangedPaths` | object | `[]` | Path prefixes that gate execution. Empty means always run when the condition is met. |
| `poolName` | string | `example-agent-pool` | Agent pool name |
| `templateRepo` | string | `pipeline-templates` | Template repository checkout folder |
| `azureSubscription` | string | *required* | Azure service connection for Key Vault |
| `keyVaultName` | string | *required* | Key Vault containing Manso secrets |
| `keyVaultSecretsFilter` | string | *(long default list)* | Comma-separated secret names to load |
| `nodeVersion` | string | `20.x` | Node.js version |
| `dotnetSdkVersion` | string | `10.x` | .NET SDK version |
| `packageSpec` | string | `manso-testing-agent@2.6.0` | npm package spec for the agent |
| `memoryRoot` | string | manso-memory-cabin repo URL | Manso memory cabin repository |
| `memoryEnabled` | string | `'true'` | Enable Manso memory cabin |
| `memoryCacheDir` | string | agent temp dir | Local cache directory for memory cabin |
| `memoryBranch` | string | `main` | Memory cabin branch |
| `memoryStrict` | string | `'false'` | Strict memory cabin mode |
| `repositoryName` | string | `''` | Repository name passed to the agent |
| `targetBranch` | string | `main` | Branch the agent targets for PR creation |
| `npmrcPath` | string | `.manso/agent-package/.npmrc` | Repository-relative npm auth file |
| `outputDirectory` | string | pipeline workspace path | Agent output directory |
| `healAttempts` | number | `4` | Number of heal/retry attempts |
| `testDesignEnabled` | boolean | `true` | Enable test design in the agent |
| `reuseBranchIfPossible` | boolean | `true` | Reuse an existing implementation branch when possible |
| `publishArtifactName` | string | `manso-testing-agent-implementation-output` | Published artifact name |

## Usage

```yaml
resources:
  repositories:
    - repository: pipeline-templates
      type: git
      name: pipeline-templates
      ref: refs/tags/v3.1.0

stages:
  - template: templates/manso-xray-implementation-stage.yaml@pipeline-templates
    parameters:
      stageName: Manso_Xray_Implementation
      stageDisplayName: Manso Xray Test Implementation
      dependsOn: [Tests]
      condition: and(eq(variables['isMainBranch'], 'true'), ne(variables['isPullRequest'], true))
      runOnlyWhenChangedPaths:
        - src
      poolName: example-agent-pool
      templateRepo: pipeline-templates
      azureSubscription: example-azure-service-connection
      keyVaultName: example-key-vault
      repositoryName: example-data-provider-api
      targetBranch: main
      testDesignEnabled: true
```

## Consuming repository requirements

The target repository must provide:

- `.manso/agent-package/.npmrc` for Azure Artifacts npm authentication
- Manso guidance files such as `.manso/testgen.md` and `.manso/repo-conventions.json`
- Commit messages that include a Jira ticket key (for example `PROJ-123 Add endpoint validation`)

Enable **Allow scripts to access the OAuth token** (`System.AccessToken`) in the pipeline so the agent can push the implementation branch and create or update the pull request.

## Azure Key Vault secrets

The default `keyVaultSecretsFilter` loads these secrets:

- `manso-testing-agent-username`
- `manso-testing-agent-jira-url`
- `manso-testing-agent-jira-token`
- `manso-testing-agent-azure-devops-pat`
- `manso-testing-agent-llm-provider`
- `manso-testing-agent-llm-api-key`
- `manso-testing-agent-llm-model`
- `manso-testing-agent-otel-*` (OpenTelemetry settings)
- `manso-testing-agent-xray-*` (Xray Cloud credentials and link settings)
- `manso-testing-agent-enable-confluence`
- `manso-testing-agent-openinference-hide-inputs`
- `manso-testing-agent-openinference-hide-outputs`

## Path filtering

Set `runOnlyWhenChangedPaths` to one or more repository-relative folder or file prefixes when the stage should run only for commits that touch those paths:

```yaml
runOnlyWhenChangedPaths:
  - src
  - tests/integration
```

When path filters are configured, the template adds a `Detect_{stageName}_Changes` stage that compares the commit against its parent (or PR merge base) and sets an output variable consumed by the main stage condition.

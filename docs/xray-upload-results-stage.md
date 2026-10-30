# Xray Upload Results Stage

Stage template that uploads test results to Jira Xray using [`xray-console-client`](https://dev.azure.com/your-organization/shared-quality-tools/_artifacts/feed/your-testing-feed/Npm/xray-console-client) (source: `your-testing-feed`, `src/javascript/xray-console-client`). It installs the package from the `your-testing-feed` Azure Artifacts feed and runs it against a results artifact produced by an earlier job.

**Template:** `templates/xray-upload-results-stage.yaml`

## What it does

1. Downloads the results artifact published by an earlier job
2. Installs Node.js and authenticates the Azure Artifacts npm feed (no `.npmrc` needed in the consumer repo)
3. Loads the Xray and Jira secrets from Key Vault (same secret names as the Manso templates)
4. Installs `xray-console-client` into a temp directory and runs it
5. The client creates/updates the Xray test cases, creates (or updates) the test execution and links it to the test plan / issue

For `nunit-xml` reports the client reads `[FeatureKey("X-1")]` (linked as requirement) and `[TestCaseKey("X-2")]` (test case to update) from the report, turns every logged HTTP call into a step, and replaces the steps of an existing test case. See the package README for the other result types.

## Parameters

### Required

| Parameter | Description |
|-----------|-------------|
| `azureSubscription` | Service connection for Key Vault access |
| `keyVaultName` | Key Vault holding the Xray and Jira secrets |
| `resultsArtifactName` | Pipeline artifact (published by an earlier job) with the results |
| `projectKey` | Jira project of the tests/execution |

### Optional: what to upload

| Parameter | Default | Description |
|-----------|---------|-------------|
| `resultsPath` | `''` | File or folder inside the artifact (empty = whole artifact) |
| `testType` | `nunit-xml` | `nunit-xml`, `junit-xml`, `cucumber-json`, `cucumber-specflow`, `allure-xml`, `allure-json` |

### Optional: Xray execution

Empty means the option is not sent. With no `planKey` and no `executionKey` a new execution is created that is not linked to any plan.

| Parameter | Default | Description |
|-----------|---------|-------------|
| `planKey` | `''` | Test plan to link the execution to (full key, e.g. `QATG-3399`) |
| `executionKey` | `''` | Existing execution to update instead of creating a new one |
| `summary` / `description` | `''` | Execution summary / description (generic text when empty) |
| `releaseVersion` | `''` | Fix version of the execution |
| `environments` | `''` | Comma separated Xray test environments |
| `issueKey` / `issueLinkType` | `''` | Issue to link the execution to, and the Jira link type **id** (e.g. `10618`); set both |

### Optional: stage/job wiring

| Parameter | Default | Description |
|-----------|---------|-------------|
| `stageName` / `stageDisplayName` | `Xray_Upload_Results` / `Upload test results to Xray` | Stage identifier / display name |
| `jobName` / `jobDisplayName` | `xray_upload_results` / `Upload test results to Xray` | Job identifier / display name |
| `dependsOn` | `[]` | Stage dependencies (the stage that publishes the results) |
| `condition` | `succeededOrFailed()` | Default uploads results of failing test runs too |
| `poolName` | `example-agent-pool` | Agent pool |

### Optional: Xray and Jira instances

| Parameter | Default | Description |
|-----------|---------|-------------|
| `xrayUrl` | `https://xray.cloud.getxray.app` | Xray host only, the client appends `/api/v2` |
| `jiraUrl` | `https://your-domain.atlassian.net` | Jira base url |

### Optional: package, feed and secrets

| Parameter | Default | Description |
|-----------|---------|-------------|
| `nodeVersion` | `20.x` | Node.js version |
| `packageSpec` | `xray-console-client@2.2.0` | npm package/version to install |
| `npmFeedUrl` | `your-testing-feed` feed | Azure Artifacts npm registry |
| `npmUserConfig` / `installDirectory` | agent temp paths | Working files of the stage |
| `keyVaultSecretsFilter` | Jira username/token + Xray client id/secret | Secrets to fetch |

## Required Key Vault secrets

`jira-basic-token` (Jira basic token, base64 of `email:api-token`), `jira-client-id` and `jira-client-secret` (Xray API key). The Xray and Jira urls are not secrets, see `xrayUrl` / `jiraUrl`.

## Usage

```yaml
resources:
  repositories:
    - repository: pipeline-templates
      type: git
      name: shared-quality-tools/pipeline-templates
      ref: refs/tags/v5.3.0

stages:
  - stage: Tests
    jobs:
      - job: integration_tests
        steps:
          - script: dotnet test --settings tests/IntegrationTests/.runsettings
          - publish: tests/IntegrationTests/bin/Debug/net10.0/TestResults
            artifact: nunit-results
            condition: succeededOrFailed()

  - template: templates/xray-upload-results-stage.yaml@pipeline-templates
    parameters:
      dependsOn: [Tests]
      azureSubscription: example-azure-service-connection
      keyVaultName: example-key-vault
      resultsArtifactName: nunit-results
      projectKey: QATG
      planKey: QATG-3399
      summary: NUnit integration tests
```

### Example with every parameter

Values marked `# default` are the template defaults and can be left out. All other values are illustrative.

```yaml
  - template: templates/xray-upload-results-stage.yaml@pipeline-templates
    parameters:
      # Required
      azureSubscription: example-azure-service-connection
      keyVaultName: example-key-vault
      resultsArtifactName: nunit-results
      projectKey: QATG

      # What to upload
      resultsPath: IntegrationTests_pass.xml   # file or folder inside the artifact, '' = whole artifact (default)
      testType: nunit-xml                      # default

      # Xray execution
      planKey: QATG-3399                       # link the execution to this test plan
      executionKey: QATG-4001                  # update this execution instead of creating a new one
      summary: NUnit integration tests         # default: generic summary
      description: Nightly run of the client-auth integration tests
      releaseVersion: 2026.10.1                # must exist as a fix version in the Jira project
      environments: test,staging               # comma separated Xray test environments
      issueKey: CLIENT-123                     # link the execution to this issue (needs issueLinkType)
      issueLinkType: '10618'                  # Jira issue link type ID (not the name); look it up in /rest/api/2/issueLinkType

      # Stage / job wiring
      stageName: Xray_Upload_Results           # default
      stageDisplayName: Upload test results to Xray   # default
      jobName: xray_upload_results             # default
      jobDisplayName: Upload test results to Xray     # default
      dependsOn: [Tests]                       # default: []
      condition: succeededOrFailed()           # default
      poolName: example-agent-pool       # default

      # Xray and Jira instances
      xrayUrl: https://xray.cloud.getxray.app   # default, host only
      jiraUrl: https://your-domain.atlassian.net    # default

      # Package, feed and secrets
      nodeVersion: 20.x                        # default
      packageSpec: xray-console-client@2.2.0   # default
      npmFeedUrl: https://pkgs.dev.azure.com/your-organization/_packaging/your-testing-feed/npm/registry/   # default
      npmUserConfig: $(Agent.TempDirectory)/xray-upload.npmrc              # default
      installDirectory: $(Agent.TempDirectory)/xray-console-client-install  # default
      keyVaultSecretsFilter: jira-basic-token,jira-client-id,jira-client-secret   # default
```

## Prerequisites

- The pipeline's build identity (`<Project> Build Service`) needs **Reader** on the `your-testing-feed` feed.
- The service connection in `azureSubscription` needs read access to the Key Vault secrets above.
- The Xray client id/secret needs permission to create tests and executions in `projectKey`.

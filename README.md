# Pipeline Templates

Reusable Azure DevOps pipeline templates and supporting .NET tooling. Templates cover release management, testing, documentation publishing, and test automation — each consumed as a `repository` resource from this repo.

## Template catalog

| Template | Type | Purpose | Documentation |
|----------|------|---------|---------------|
| `release-process-stages.yaml` | stage wrapper | Full JIRA/ITSM release workflow | [Release process](docs/release-process.md) |
| `release-process-*.yaml` | stages | Individual release stages (ticket creation, approvals, notifications) | [Release process](docs/release-process.md) |
| `run-unit-tests-job.yaml` | job | .NET restore, build, run unit tests, code coverage | [Run tests](docs/run-tests-jobs.md) |
| `run-functional-tests-job.yaml` | job | .NET restore, build, run functional tests (Docker), code coverage | [Run tests](docs/run-tests-jobs.md) |
| `publish-confluence-docs-stage.yaml` | stage | Publish Markdown docs to Confluence | [Publish Confluence docs](docs/publish-confluence-docs.md) |
| `allure-report.yaml` | steps | Generate and upload Allure test reports | [Allure report](docs/allure-report.md) |
| `manso-xray-implementation-stage.yaml` | stage | AI-driven Xray test implementation from commit messages | [Manso Xray](docs/manso-xray-implementation.md) |
| `set-image-tag.yaml` | steps | Re-tag Azure Container Registry images with a stable tag | [Set image tag](docs/set-image-tag.md) |
| `manso-pr-review-stage.yaml` | stage | Manso (`review-pull-request`) automated PR review, posted as a PR comment | [Manso PR review](docs/manso-pr-review-stage.md) |
| `xray-upload-results-stage.yaml` | stage | Upload test results (NUnit, JUnit, Cucumber, Allure) to Xray with `xray-console-client` | [Xray upload](docs/xray-upload-results-stage.md) |

## Getting started

Reference this repository from a consuming pipeline and pin to a version tag — never use `main` in production pipelines.

```yaml
resources:
  repositories:
    - repository: pipeline-templates
      type: git
      name: pipeline-templates
      ref: refs/tags/v3.1.0

stages:
  - stage: Tests
    displayName: Run Tests
    jobs:
      - template: templates/run-unit-tests-job.yaml@pipeline-templates
        parameters:
          unitTestProjects: tests/**/*.UnitTests.csproj

  - template: templates/release-process-stages.yaml@pipeline-templates
    parameters:
      releaseProcessEnabled: true # Set false to skip release actions without skipping deployments
      azureSubscription: 'azureServiceConnection_Pt'
      keyVaultName: 'example-key-vault'
      projectKey: 'ITSM'
      pipelineName: 'my-service'
      affectedServices: '<customfield_15716-id>'
      affectedComponents: '<customfield_15998-object-id>'
      teamCMDB: '<customfield_16442-object-id>'
      assignee: '<jira-assignee-account-id>'
      approver: '<jira-approver-account-id>'
      componentName: 'My Service'
```

See [versioning](docs/versioning.md) for the full version strategy, consumer upgrade paths, and the automated tagging pipeline.

## Repository structure

```text
pipeline-templates/
├── docs/
│   ├── release-process.md          # JIRA release workflow
│   ├── run-tests-jobs.md           # Unit/functional test job templates
│   ├── publish-confluence-docs.md  # Confluence publishing
│   ├── allure-report.md            # Allure report steps
│   ├── manso-xray-implementation.md
│   ├── set-image-tag.md
│   ├── manso-pr-review-stage.md
│   ├── xray-upload-results-stage.md
│   ├── versioning.md
│   └── images/                     # Diagrams and screenshots
├── templates/
│   ├── release-process-*.yaml      # Release management stages
│   ├── run-unit-tests-job.yaml     # Job template: unit tests
│   ├── run-functional-tests-job.yaml # Job template: functional tests (Docker)
│   ├── run-tests-steps.yaml        # Shared restore/build steps used by both job templates
│   ├── publish-confluence-docs-stage.yaml
│   ├── allure-report.yaml
│   ├── manso-xray-implementation-stage.yaml
│   ├── set-image-tag.yaml
│   ├── manso-pr-review-stage.yaml
│   └── xray-upload-results-stage.yaml
├── scripts/
│   ├── ReleaseProcess.cs           # Launcher for the packaged release tools
│   └── MansoXrayImplementation.cs  # Manso agent launcher
└── tests/
    └── PipelineTemplates.Tests/   # YAML contract tests
```

Pipeline jobs invoke the `scripts/*.cs` launchers with `dotnet run --file`. Release management implementation and unit tests live in the separate `release-management-tools` repository; its versioned Linux package is downloaded by the ReleaseProcess launcher. The Confluence publishing implementation lives in the separate `confluence-publisher` repository; this repository keeps its Azure Pipelines stage template.

Run pipeline template contract tests locally with:

```shell
dotnet test tests/PipelineTemplates.Tests/PipelineTemplates.Tests.csproj
```

## Documentation

| Topic | Guide |
|-------|-------|
| JIRA release management, ITSM tickets, stakeholder notifications | [docs/release-process.md](docs/release-process.md) |
| .NET build, unit/functional tests, code coverage | [docs/run-tests-jobs.md](docs/run-tests-jobs.md) |
| Markdown → Confluence publishing | [docs/publish-confluence-docs.md](docs/publish-confluence-docs.md) |
| Allure report generation and upload | [docs/allure-report.md](docs/allure-report.md) |
| Manso Xray test implementation | [docs/manso-xray-implementation.md](docs/manso-xray-implementation.md) |
| Azure Container Registry stable image re-tagging | [docs/set-image-tag.md](docs/set-image-tag.md) |
| Version tags, release branches, consumer pinning | [docs/versioning.md](docs/versioning.md) |
| Manso automated PR review | [docs/manso-pr-review-stage.md](docs/manso-pr-review-stage.md) |
| Upload test results to Xray | [docs/xray-upload-results-stage.md](docs/xray-upload-results-stage.md) |

## Versioning

- Pin consumers to immutable tags: `ref: refs/tags/v1.0.0`
- Or float within a major line: `ref: refs/heads/release/v1`
- Never point production pipelines at `main`

This repository includes [azure-pipelines.version-tag.yml](azure-pipelines.version-tag.yml). On PRs it validates and runs unit tests; on `main` tests run automatically and version tagging is a **manual stage** you start when ready (so skipping a release keeps the run successful). Bumps come from `fix`/`patch`/`minor`/`major` markers in commits since the last tag. See [docs/versioning.md](docs/versioning.md).

## Secrets overview

Different templates read secrets from Azure Key Vault or pipeline variables:

**Release process** (`keyVaultName` via `AzureKeyVault@2`):

- `release-process-jira-bot-token`
- `release-process-jira-bot-email`
- `release-process-jira-cloud-id`
- `release-process-webhook-url`

**Manso Xray** — see the full list in [docs/manso-xray-implementation.md](docs/manso-xray-implementation.md).

**Manso PR review** (`keyVaultName` via `AzureKeyVault@2`) — `manso-testing-agent-jira-url`, `manso-testing-agent-username`, `manso-testing-agent-jira-token`, `manso-testing-agent-llm-provider`, `manso-testing-agent-llm-api-key`, `manso-testing-agent-llm-model`. Uses `$(System.AccessToken)` for Azure DevOps access, not a separate PAT.

**Confluence docs** — prefer vault mode: set `keyVaultName` + `azureSubscription` and the stage reads the Atlassian credential from Key Vault (defaulting to the same `manso-testing-agent-username` / `manso-testing-agent-jira-token` secrets the Manso templates use, since Jira and Confluence share one identity). Passing `confluenceEmail`/`confluenceApiToken` as pipeline variables still works but keeps a second copy of the credential per pipeline. See [docs/publish-confluence-docs.md](docs/publish-confluence-docs.md).

**Run tests / ReportPortal** — configure `azureSubscription` and `keyVaultName` to use your Key Vault, where the templates read `report-portal-api-key`. Set `keyVaultName: ''` and pass `reportPortalApiKey` instead to use pipeline-variable mode. See [docs/run-tests-jobs.md](docs/run-tests-jobs.md).

**Where tooling secrets live** — store tooling secrets in a Key Vault and configure each template's `azureSubscription` and `keyVaultName` parameters. Keep service runtime secrets in that service's own per-environment vault.

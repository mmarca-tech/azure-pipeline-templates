# Manso PR Review Stage

Stage template that runs `manso-testing-agent`'s `review-pull-request` command as an automated first-pass PR reviewer, then posts a ticket-aware, structured review as a comment on the Azure DevOps pull request.

**Template:** `templates/manso-pr-review-stage.yaml`

This replaces an earlier, incorrect version of this template that wrapped the GitHub Copilot CLI instead. The actual reviewer here is `manso-testing-agent` (source: `testing-mcp-workflow`), the same package the [Manso Xray implementation stage](manso-xray-implementation.md) uses, via its `review-pull-request` CLI entry point.

## What it does

1. Installs Node.js and loads the shared Manso secrets, including the GitHub Packages read token, from Key Vault
2. Writes a temporary GitHub-scoped npm config and installs the pinned Manso package into a temp directory
3. Runs `review-pull-request` and publishes its JSON/Markdown output
4. The CLI auto-detects the current PR from Azure DevOps predefined variables, detects a linked Jira ticket from the branch/commit, reviews the diff against the ticket, and posts a structured comment unless `--no-comment` is set

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `stageName` | string | `Manso_PR_Review` | Azure DevOps stage identifier |
| `stageDisplayName` | string | `Manso PR Review` | Stage display name |
| `jobName` | string | `manso_pr_review` | Job identifier |
| `jobDisplayName` | string | `Run Manso PR review` | Job display name |
| `dependsOn` | object | `[]` | Stage dependencies |
| `condition` | string | `succeeded()` | Stage condition |
| `poolName` | string | `example-agent-pool` | Agent pool name |
| `nodeVersion` | string | `20.x` | Node.js version |
| `packageSpec` | string | `@mmarca-tech/manso-testing-agent@0.1.4` | Scoped GitHub Packages package/version to install |
| `npmUserConfig` | string | `$(Agent.TempDirectory)/manso-pr-review.npmrc` | Temporary GitHub Packages config created by the stage |
| `installDirectory` | string | `$(Agent.TempDirectory)/manso-pr-review-install` | Where `manso-testing-agent` gets installed |
| `outputDirectory` | string | `$(Agent.TempDirectory)/manso-pr-review-output` | Where the review JSON/Markdown output is written |
| `azureSubscription` | string | *(required)* | Service connection for Key Vault access |
| `keyVaultName` | string | *(required)* | Key Vault holding the Manso secrets |
| `keyVaultSecretsFilter` | string | Jira URL/username/token + LLM provider/key/model + GitHub package token | Secrets to fetch |
| `noComment` | boolean | `false` | Run the review without posting a PR comment (useful for a dry run) |

## Usage

```yaml
resources:
  repositories:
    - repository: pipeline-templates
      type: git
      name: pipeline-templates
      ref: refs/tags/v3.1.0

trigger: none

stages:
  - template: templates/manso-pr-review-stage.yaml@pipeline-templates
    parameters:
      azureSubscription: example-azure-service-connection
      keyVaultName: example-key-vault
```

## Prerequisites for a new consumer

Per `docs/operations/consumer-repository-setup.md` in the `manso-testing-agent` repo (`your-organization/your-test-automation/_git/manso-testing-agent`, checked out locally as `testing-mcp-workflow`):

1. **Key Vault access** to the configured `keyVaultName` for the `manso-testing-agent-*` secrets and `github-packages-token` (`azureSubscription` service connection must be authorized for the pipeline).
2. A GitHub PAT classic with `read:packages` stored as `github-packages-token`.
3. **A branch policy / PR build-validation policy** in Azure DevOps repo settings that actually triggers this pipeline on pull requests — the pipeline itself uses `trigger: none`, so nothing runs it automatically otherwise.

Unlike the Xray implementation stage, this one does not need `.manso/testgen.md` or `.manso/repo-conventions.json` — those govern test authoring conventions, not PR review.

## Notes

- `AZURE_DEVOPS_PAT` is set to `$(System.AccessToken)` — the pipeline's own OAuth token is sufficient for reading the PR diff and posting a comment; no separate PAT secret is needed.
- The CLI resolves organization/project/repository/PR id from Azure DevOps predefined variables automatically; override with its own `--organization`/`--project`/`--repository`/`--pull-request-id` flags if needed (not currently exposed as template parameters).
- The scoped GitHub package is installed fresh into `installDirectory` on every run; `packageSpec` pins the version. The stage creates a temporary `.npmrc` and passes the Key Vault token through `NODE_AUTH_TOKEN`; consumer repositories do not need an npm config file.

# Manso PR Review Stage

Stage template that runs `manso-testing-agent`'s `review-pull-request` command as an automated first-pass PR reviewer, then posts a ticket-aware, structured review as a comment on the Azure DevOps pull request.

**Template:** `templates/manso-pr-review-stage.yaml`

This replaces an earlier, incorrect version of this template that wrapped the GitHub Copilot CLI instead. The actual reviewer here is `manso-testing-agent` (source: `testing-mcp-workflow`), the same package the [Manso Xray implementation stage](manso-xray-implementation.md) uses, via its `review-pull-request` CLI entry point.

## What it does

1. Installs Node.js and authenticates the Azure Artifacts npm feed that hosts `manso-testing-agent`
2. Loads the shared Manso secrets (Jira, LLM provider/key/model) from the configured Key Vault
3. Installs `manso-testing-agent` into a temp directory (not the repo) and runs `review-pull-request`
4. The CLI auto-detects the current PR from Azure DevOps predefined variables, detects a linked Jira ticket from the branch/commit, reviews the diff against the ticket, and posts a structured comment (recommendation, findings by severity, business + technical review) unless `--no-comment` is set
5. Publishes the review's JSON/Markdown output as a build artifact

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
| `packageSpec` | string | `manso-testing-agent@2.6.0` | npm package/version to install |
| `npmrcPath` | string | `.manso/agent-package/.npmrc` | Path (in the consumer repo) to the npm feed config |
| `npmUserConfig` | string | `$(Agent.TempDirectory)/manso-pr-review.npmrc` | Working copy of the npmrc that gets authenticated |
| `installDirectory` | string | `$(Agent.TempDirectory)/manso-pr-review-install` | Where `manso-testing-agent` gets installed |
| `outputDirectory` | string | `$(Agent.TempDirectory)/manso-pr-review-output` | Where the review JSON/Markdown output is written |
| `azureSubscription` | string | *(required)* | Service connection for Key Vault access |
| `keyVaultName` | string | *(required)* | Key Vault holding the Manso secrets |
| `keyVaultSecretsFilter` | string | Jira URL/username/token + LLM provider/key/model | Secrets to fetch |
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

1. **`.manso/agent-package/.npmrc`** in the repository, pointing at the Azure Artifacts feed that hosts `manso-testing-agent`.
2. **Read access to that npm feed** for the pipeline's build identity.
3. **Key Vault access** to the configured `keyVaultName` for the `manso-testing-agent-*` secrets (`azureSubscription` service connection must be authorized for the pipeline).
4. **A branch policy / PR build-validation policy** in Azure DevOps repo settings that actually triggers this pipeline on pull requests — the pipeline itself uses `trigger: none`, so nothing runs it automatically otherwise.

Unlike the Xray implementation stage, this one does not need `.manso/testgen.md` or `.manso/repo-conventions.json` — those govern test authoring conventions, not PR review.

## Notes

- `AZURE_DEVOPS_PAT` is set to `$(System.AccessToken)` — the pipeline's own OAuth token is sufficient for reading the PR diff and posting a comment; no separate PAT secret is needed.
- The CLI resolves organization/project/repository/PR id from Azure DevOps predefined variables automatically; override with its own `--organization`/`--project`/`--repository`/`--pull-request-id` flags if needed (not currently exposed as template parameters).
- `manso-testing-agent` is installed fresh into `installDirectory` on every run rather than relying on a repo-local install, so `packageSpec` is the single source of truth for which version runs.

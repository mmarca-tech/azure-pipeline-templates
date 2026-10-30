# Publish Confluence Docs Stage

Stage template that publishes Markdown documentation from a consuming repository to Confluence. Markdown is converted to Confluence storage format and uploaded under a configured parent page.

**Template:** `templates/publish-confluence-docs-stage.yaml`

**Implementation:** `scripts/PublishConfluenceDocs.cs` (launcher) and `src/PipelineTools.ConfluencePublisher`

## What it does

1. Checks out the consuming repository and this template repository
2. Discovers all `*.md` files under the configured docs root
3. Groups files by first-level subfolder under the docs root
4. Converts Markdown to Confluence storage format (tables, links, code blocks, images)
5. Creates or updates Confluence pages under the parent page

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `stageName` | string | `Publish_Docs` | Azure DevOps stage identifier |
| `stageDisplayName` | string | `Publish Docs to Confluence` | Stage display name |
| `dependsOn` | object | `[Api_Docker]` | Stage dependencies |
| `condition` | string | `succeeded('Api_Docker')` | Stage condition |
| `poolName` | string | `example-agent-pool` | Agent pool name |
| `dotnetSdkVersion` | string | `10.0.x` | .NET SDK version |
| `templateRepo` | string | `pipeline-templates` | Checked-out template repository folder name |
| `docsPath` | string | `src/docs` | Repository-relative path to the Markdown docs root |
| `confluenceBaseUrl` | string | `https://your-domain.atlassian.net/wiki` | Confluence base URL |
| `confluenceSpaceKey` | string | `RFCCT` | Confluence space key |
| `parentPageId` | string | *required* | Parent page ID where docs are published |
| `azureSubscription` | string | `''` | Service connection used to read the credential from Key Vault. Required for vault mode. |
| `keyVaultName` | string | `''` | Key Vault holding the Confluence credential. Setting this enables vault mode. |
| `confluenceEmailSecretName` | string | `manso-testing-agent-username` | Vault secret holding the Atlassian account email |
| `confluenceApiTokenSecretName` | string | `manso-testing-agent-jira-token` | Vault secret holding the Atlassian API token |
| `confluenceEmail` | string | `''` | Fallback: only used when `keyVaultName` is empty |
| `confluenceApiToken` | string | `''` | Fallback: only used when `keyVaultName` is empty |
| `pageTitlePrefix` | string | `''` | Optional prefix added to every page title |
| `dryRun` | boolean | `false` | Log actions without writing to Confluence |

## Credential source: prefer Key Vault

There are two ways to supply the Confluence credential.

**Vault mode (preferred).** Set `keyVaultName` and `azureSubscription`. The stage adds an `AzureKeyVault@2` task and reads the credential from the vault, so no Confluence secret needs to exist as a pipeline variable in any consumer.

```yaml
  - template: templates/publish-confluence-docs-stage.yaml@pipeline-templates
    parameters:
      parentPageId: '106541155325'
      azureSubscription: example-azure-service-connection
      keyVaultName: example-key-vault
```

The secret names default to `manso-testing-agent-username` and `manso-testing-agent-jira-token`. That is deliberate: Jira and Confluence share a single Atlassian identity, and those secrets already exist in the shared vault for the Manso templates — so docs publishing reuses one managed credential instead of a second copy. Override the two `*SecretName` parameters if a dedicated Confluence account is introduced later.

**Pipeline-variable mode (legacy).** Leave `keyVaultName` empty and pass `confluenceEmail` and `confluenceApiToken`. This still works unchanged, but it means every consuming pipeline carries its own copy of the credential with no central rotation.

## Usage

```yaml
resources:
  repositories:
    - repository: pipeline-templates
      type: git
      name: pipeline-templates
      ref: refs/tags/v3.1.0

stages:
  - template: templates/publish-confluence-docs-stage.yaml@pipeline-templates
    parameters:
      dependsOn:
        - Build
      condition: succeeded('Build')
      docsPath: src/docs
      parentPageId: '123456789'
      confluenceEmail: $(confluenceEmail)
      confluenceApiToken: $(confluenceApiToken)
      confluenceSpaceKey: RFCCT
      dryRun: false
```

## Docs layout

Place Markdown files under the docs root. Files in the root become pages in a default `docs` group. Files in subfolders are grouped by folder name:

```text
src/docs/
├── overview.md          → group "docs"
├── api/
│   ├── endpoints.md     → group "api"
│   └── models.md        → group "api"
└── guides/
    └── getting-started.md → group "guides"
```

Each Markdown file's first `# Heading` becomes the Confluence page title. If no heading is found, the filename is used.

## Environment variables

The template sets these for `PublishConfluenceDocs.cs`:

| Variable | Source |
|----------|--------|
| `DOCS_ROOT` | `$(Build.SourcesDirectory)/{docsPath}` |
| `CONFLUENCE_BASE_URL` | `confluenceBaseUrl` parameter |
| `CONFLUENCE_SPACE_KEY` | `confluenceSpaceKey` parameter |
| `CONFLUENCE_PARENT_PAGE_ID` | `parentPageId` parameter |
| `CONFLUENCE_EMAIL` | vault secret `confluenceEmailSecretName` in vault mode, otherwise the `confluenceEmail` parameter |
| `CONFLUENCE_API_TOKEN` | vault secret `confluenceApiTokenSecretName` in vault mode, otherwise the `confluenceApiToken` parameter |
| `CONFLUENCE_PAGE_TITLE_PREFIX` | `pageTitlePrefix` parameter |
| `CONFLUENCE_DRY_RUN` | `dryRun` parameter |
| `CONFLUENCE_WORK_DIR` | Agent temp directory for conversion artifacts |

## Notes

- Store Confluence credentials as pipeline secrets or variable group values rather than hard-coding them in YAML.
- Use `dryRun: true` to validate Markdown conversion without updating Confluence pages.
- SVG and PNG images referenced from Markdown are converted for Confluence display.

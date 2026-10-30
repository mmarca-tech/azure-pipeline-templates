# Versioning Pipeline Templates

See the [README](../README.md) for the full template catalog. This page covers how to version and consume templates from this repository safely.

This repository is consumed through an Azure DevOps `repository` resource:

```yaml
resources:
  repositories:
    - repository: pipeline-templates
      type: git
      name: pipeline-templates
      ref: refs/tags/v1.0.0
```

That `ref` is the version boundary. If consumers point at `main`, every merge can change their pipeline behavior immediately.

## Recommended Contract

- `main` is the integration branch. It is not a stable version.
- `release/v1`, `release/v2`, and so on are maintained major-version branches.
- `v1.0.0`, `v1.0.1`, `v1.1.0`, `v2.0.0`, and so on are immutable annotated tags.

This gives you two support modes:

- Exact lock: consumer uses `refs/tags/v1.0.0`.
- Rolling compatible line: consumer uses `refs/heads/release/v1`.

## When To Bump Versions

- Patch `x.y.Z`: fixes only, no contract changes. Use `fix:` / `patch:` / `#patch`.
- Minor `x.Y.z`: backward-compatible parameters, defaults, stages, or script enhancements. Use `minor:` / `#minor`.
- Major `X.y.z`: breaking parameter changes, renamed stages/jobs/outputs, removed behavior, or different approval flow that can break existing consumers. Use `major:` / `#major`.

## Suggested Workflow

### First stable release

```shell
git checkout main
git checkout -b release/v1
git push -u origin release/v1

git tag -a v1.0.0 -m "Release v1.0.0"
git push origin v1.0.0
```

### Backward-compatible update

```shell
git checkout main
# implement and merge change

git checkout release/v1
git merge main
git tag -a v1.1.0 -m "Release v1.1.0"
git push origin release/v1
git push origin v1.1.0
```

If `main` already contains breaking work for `v2`, cherry-pick only the compatible commits into `release/v1` instead of merging the branch.

### Breaking release

```shell
git checkout main
# implement breaking change

git checkout -b release/v2
git push -u origin release/v2

git tag -a v2.0.0 -m "Release v2.0.0"
git push origin v2.0.0
```

## Consumer Upgrade Examples

### Stay fully locked

```yaml
resources:
  repositories:
    - repository: pipeline-templates
      type: git
      name: pipeline-templates
      ref: refs/tags/v1.0.0
```

Upgrade to the next tested version by changing only the tag:

```yaml
ref: refs/tags/v1.0.1
```

### Follow the latest v1 release automatically

```yaml
resources:
  repositories:
    - repository: pipeline-templates
      type: git
      name: pipeline-templates
      ref: refs/heads/release/v1
```

This is less strict, but still prevents accidental upgrades to `v2`.

## Automated Tagging Pipeline

This repo includes [azure-pipelines.version-tag.yml](../azure-pipelines.version-tag.yml):

### Pull requests targeting `main`

1. Validate scripts/YAML references.
2. Run `tests/PipelineTools.ReleaseTools.Tests`.

No version tag is created on PRs.

### Merges to `main`

1. Same validate + test stage as PRs. The run succeeds when tests pass.
2. The **Release version tag** stage uses a **manual stage trigger** (ADO does not allow `dependsOn` on manual stages). After **Test** succeeds, start the release stage from the run UI when you want to publish a tag. If you never start it, no tag is created and the pipeline stays successful.
3. When the release stage runs, it finds the latest `vX.Y.Z` tag, detects the bump from commits since that tag (including original PR commits after squash merges), then creates and pushes the next annotated tag.

Queue with `autoRelease: true` only when you want the release stage to run without a manual click (for example unattended tagging).

> Note: Azure DevOps `ManualValidation` **Reject** always fails the job/pipeline. That is why release uses a manual stage instead of ManualValidation.

### Bump detection

Commits since the last semantic version tag are scanned. For squash merges, the pipeline also loads the associated pull request's original commits via the Azure DevOps API (using `System.AccessToken`), so `minor:` / `fix:` markers are not lost when history is collapsed. The highest severity wins:

| Marker in commit subject/body | Bump |
|-------------------------------|------|
| `major:`, `#major`, `[major]`, `semver:major` | major |
| `minor:`, `#minor`, `[minor]`, `semver:minor` | minor |
| `fix:`, `patch:`, `#patch`, `[patch]`, `semver:patch` | patch |

Bullet lists in squash commit bodies (for example `- minor: add feature`) are also recognized.

If none of those markers are present, the pipeline parameter `defaultBump` is used (`patch` by default).

If you squash without preserving original subjects and without markers in the PR title, put an explicit `#minor` / `#major` / `#patch` in the squash commit message or PR title.

Examples:

- commits `fix: ...` + `docs: ...` → `v1.0.0` → `v1.0.1`
- commits `fix: ...` + `minor: ...` → `v1.0.1` → `v1.1.0`
- any `#major` / `major:` since last tag → `v1.1.0` → `v2.0.0`

Queue the pipeline with `autoRelease: true` only when you intentionally want an unattended release on `main`.

### Requirements

- `main` should be protected so changes land through PRs only.
- The repository build service must be allowed to create and push tags.
- The pipeline checkout must keep credentials for the release job, which is already configured in the YAML.
- Enable the pipeline for pull request validation on `main` in Azure DevOps if PR triggers are not already wired for this YAML.

## What Counts As Breaking Here

For this repository, treat these as major-version changes:

- Renaming or removing template parameters.
- Changing parameter defaults in a way that changes behavior for existing consumers.
- Renaming stages, jobs, step names, or output variable names used by downstream pipelines.
- Changing required secrets or secret names.
- Changing manual approval behavior in a way that alters the release flow.

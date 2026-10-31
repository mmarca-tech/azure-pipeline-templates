# Changelog

## Unreleased

### Changed

- Release-process stages now use the shared `install-release-tools.yaml` step to fetch the self-contained binary and invoke it directly. The root-level `ReleaseProcess.cs` launcher remains in `release-management-tools` for local development.
- major: `xray-upload-results-stage.yaml` now installs the versioned `xray-console-client` .NET tool from GitHub Packages instead of the legacy Azure Artifacts npm feed. Add a GitHub PAT classic with `read:packages` to Key Vault as `github-packages-token` (or set `githubPackagesTokenSecretName`); pin `xrayClientVersion` for consumers.
- minor: `xray-upload-results-stage.yaml` reads three Key Vault secrets, `jira-basic-token`, `jira-client-id` and `jira-client-secret`, instead of the four `manso-testing-agent-*` ones. The Jira basic token (base64 of `email:api-token`) is stored ready-made and passed to the uploader as is, so the template no longer builds it from a username and an API token.
- minor: `xray-upload-results-stage.yaml` takes the Xray and Jira urls as parameters (`xrayUrl`, `jiraUrl`) instead of reading them from Key Vault secrets; `manso-testing-agent-jira-url` and `manso-testing-agent-xray-base-url` are no longer needed.
- major: `templates/run-tests-stage.yaml` (a stage template) is replaced by two independent job templates, `templates/run-unit-tests-job.yaml` and `templates/run-functional-tests-job.yaml`. Each is a standalone single-job template a caller includes directly under their own stage's `jobs:` list — use either one alone, both in the same stage (they then run in parallel), or in different stages entirely. `stageName`/`stageDisplayName`/`dependsOn`/`condition` no longer exist as template parameters; they're just fields on whatever `- stage:` block the caller writes. The shared restore/build steps were extracted into a new `templates/run-tests-steps.yaml` steps template, included internally by both job templates.
- major: Removed `jobName`, `jobDisplayName`, `poolName`, `buildConfiguration`, `publishCodeCoverage`, `dockerHost`, and `reportPortalApiKeySecretName` from the run-tests job templates — none of this org's consumers ever overrode them, so they're now fixed conventions (job name/pool/`Release` config/Docker socket/`report-portal-api-key` secret name) instead of parameters. `azureSubscription`/`keyVaultName` now default to this org's shared `example-azure-service-connection`/`example-key-vault` Key Vault, so most callers no longer pass them at all; set `keyVaultName: ''` plus `reportPortalApiKey` to opt into pipeline-variable mode instead. A typical call now only needs `solutionPath` and `unitTestProjects`/`functionalTestProjects`. See `docs/run-tests-jobs.md` (renamed from `docs/run-tests-stage.md`) for the updated calling convention and a migration note.
- minor: The run-tests job templates now set `REPORTPORTAL_ENABLED` on the test-run step, overriding whatever `ReportPortal.json` says. Consumers should ship `ReportPortal.json` with `"enabled": false` so a local `dotnet test` doesn't report to ReportPortal, while CI (which goes through these templates) does. Added a new `reportPortalEnabled` parameter (default `true`) so a consumer can turn CI reporting off too — set to `false` to skip both the `REPORTPORTAL_*` env vars and the `AzureKeyVault@2` call.

- Aligned release-ticket automation with release-management guidance:
  - Default `changeType` is now `Normal` (Standard remains an explicit override only for documented pre-approved routines).
  - Default planned window is now +24h / +1h instead of +7 days with identical start/end.
  - Implementation plan includes the pipeline URL and monitoring guidance; Service Impact (`customfield_18878`) is set via new `serviceImpact` parameter.
  - After create, Normal/Major tickets transition through `Review` and `Planning` to `Go Live Decision`; Standard/Emergency move directly to `Awaiting implementation`.
  - `release_process_awaiting_implementation` waits for Jira status `Awaiting implementation` instead of pipeline-forcing that transition.
  - Implementing / Completed / Failed paths set Change start/completion dates and Post-Implementation Status, and add pipeline step comments on the ITSM ticket.
- Added `check-jira-awaiting-implementation-status` command for the awaiting-stage status gate.
- Release-ticket creation now requires an `approver` account ID and assigns it to Jira field `customfield_13301`.
- Jira secret retrieval now runs as the first job of `release_process_create_ticket` instead of a separate `Get_Jira_Secrets` stage.

### Documentation

- Documented known follow-ups: OAuth 2.0 2LO for `jira-connection`, optional Account field, and stronger SoD automation.

### Added

- minor: `templates/xray-upload-results-stage.yaml` uploads test results (NUnit, JUnit, Cucumber, Allure) to Xray by installing `xray-console-client` from the `your-testing-feed` feed. See `docs/xray-upload-results-stage.md`.
- Split the original release workflow into standalone stage templates:
  - `templates/release-process-create-ticket.yaml`
  - `templates/release-process-update-stakeholders.yaml`
  - `templates/release-process-cancel-release.yaml`
  - `templates/release-process-awaiting-implementation.yaml`
  - `templates/release-process-implementing.yaml`
  - `templates/release-process-release-deployed.yaml`
- Added `scripts/ReleaseProcess.cs`, a .NET 10 file-based command program for release-ticket creation, Jira transitions, approval checks, Teams notifications, release notes, validation, and version tagging.
- Added `src/PipelineTools.ReleaseTools` so the file-based launcher can reuse normal C# project code split by area.
- Added `tests/PipelineTools.ReleaseTools.Tests` with docs-driven unit and YAML contract tests for the release process.
- Version-tag pipeline runs validate + unit tests on PRs; on `main` the release stage is manually triggered so skipping a release keeps the run successful. Bumps come from commits since the last tag (`fix`/`patch`/`minor`/`major`), including squashed PR commits.
- Added `docs/flow.svg` and updated the README with a static flow diagram that renders in VS Code.
- Added this root `CHANGELOG.md`.
- Added optional release ticket inputs for change type, planned start date, planned end date, change reason, change risk, and change impact.
- Added required release ticket inputs for `affectedServices`, `affectedComponents`, `teamCMDB`, and `assignee`.
- Added `templates/manso-xray-implementation-stage.yaml` and `scripts/MansoXrayImplementation.cs` for commit-driven Manso Xray test implementation.
- Added `runOnlyWhenChangedPaths` to `templates/manso-xray-implementation-stage.yaml` for path-gated Manso runs.
- Added optional `coverletSettingsPath` to `templates/run-tests-stage.yaml` so consumers with a custom `coverlet.runsettings` (coverage exclusions, format overrides) don't lose them by adopting the shared template. Also wired `reportPortalApiKey` into the unit-test step, not just the functional-test step, so both report to the same place.
- Added optional Key Vault sourcing for tooling secrets, so consumers no longer need per-pipeline secret variables:
  - `templates/publish-confluence-docs-stage.yaml` gains `azureSubscription`, `keyVaultName`, `confluenceEmailSecretName` and `confluenceApiTokenSecretName`. The secret-name defaults point at the existing `manso-testing-agent-username` / `manso-testing-agent-jira-token` pair, since Jira and Confluence share one Atlassian identity and those secrets are already in the shared vault. `confluenceEmail`/`confluenceApiToken` are now optional and used only when `keyVaultName` is empty.
  - `templates/run-tests-stage.yaml` gains `azureSubscription`, `keyVaultName` and `reportPortalApiKeySecretName`. `reportPortalApiKey` is now the fallback, used only when `keyVaultName` is empty. Note there is no pre-existing vault copy of the ReportPortal key, so that secret must be created before a consumer can switch.
  - Both changes are additive and default to the previous behaviour, so existing consumers are unaffected until they opt in.
- Added `templates/manso-pr-review-stage.yaml`, wrapping `manso-testing-agent`'s `review-pull-request` CLI as a shared PR-review stage: installs the agent, loads its Jira/LLM secrets from Key Vault, and posts a ticket-aware structured review as a PR comment. Two consumer repos had independently copy-pasted a GitHub-Copilot-CLI-based review pipeline instead; this template is the Manso-based replacement for that, not an extraction of it.

### Changed

- Converted `templates/release-process-stages.yaml` into a wrapper that composes the standalone stage templates.
- Added the automatic `release_process_awaiting_implementation` stage after PT deployment.
- Added the manual-approval `release_process_implementing` stage before live deployment.
- Updated the release flow so the PT deployment stage sits between ticket creation and `Awaiting implementation`.
- Updated the release flow so the live deployment stage sits after `Implementing`.
- Updated `release_process_awaiting_implementation` so it depends only on the PT deployment stage and no longer directly on `release_process_create_ticket`.
- Kept `release_process_update_stakeholders` dependent on `release_process_create_ticket`.
- Updated `release_process_cancel_release` to also depend on `release_process_implementing` and skip when implementing succeeded, so cancel cannot run after go-live was approved.
- Updated `release_process_create_ticket` to re-export shared Jira credentials and webhook outputs for downstream stages.
- Updated `release_process_create_ticket` to publish a non-secret ticket-state artifact for downstream stages that should not directly depend on ticket creation.
- Updated downstream templates to consume the shared outputs from `release_process_create_ticket` instead of relying on indirect stage output access.
- Updated the deployed-release template to depend on both `release_process_create_ticket` and the configurable live deployment stage.
- Consolidated cancel-ticket handling onto the `transition-jira-ticket` command using Jira status ID `10234` and removed the duplicate cancel-specific implementation.
- Refactored Jira transition code into shared C# helpers so `transition-jira-ticket` and `update-released-tickets` reuse the same transition logic.
- Replaced PowerShell pipeline script execution with `dotnet run --file scripts/ReleaseProcess.cs -- <command>` and .NET 10 SDK setup steps.
- Updated `release_process_awaiting_implementation` to warn when Jira approval is still pending; Jira moves approved tickets to Awaiting implementation, and the pipeline enforces that status before Implementing.

### Fixed

- Renamed the `update-stakeholders` CLI argument from `-PackageVersion` to `-ReleaseTag` so git environment tags such as `e8e5cb19-live` are not interpreted as the MSBuild `PackageVersion` property during `dotnet run`.
- Renamed the create-ticket output and downstream job variables from `PackageVersion`/`packageVersion` to `ReleaseTag`/`releaseTag`. Azure Pipelines maps job variables into the process environment, and MSBuild/NuGet then treat `PackageVersion=live-...` as an invalid package version during `dotnet run`.
- Updated `templates/release-process-release-deployed.yaml` so the deployed stakeholder notification waits for the Jira update job instead of running independently within the stage.
- Updated the `update-released-tickets` command so the release ticket is still transitioned to `Completed` when no linked implementation tickets are present.
- Fail `update-released-tickets` when linked Done transitions or the release Completed transition fail, so DEPLOYED notification cannot run on a greenwashed result.
- Default `createTicketCondition` now includes `succeeded()` so ticket creation does not run after an upstream dependency fails.
- Ticket creation checks out the template repository with persisted credentials and complete tag history so release-note generation can resolve and fetch environment tags.
- The ticket-creation approval job now starts only after Jira secret retrieval succeeds.
- Jira Approvers (`customfield_13301`) is sent as a multi-user array, including when one approver is configured.
- Cancel Release explicitly depends on successful ticket creation, matching Update Stakeholders, and uses `ManualValidation` before cancelling Jira.
- Renamed the Awaiting Implementation stage display label to `Waiting for MANUAL release approval`.
- Manual release approval checking now warns without pausing; the Implementing transition performs the mandatory Jira approval/status check and fails clearly when approval is missing.
- Added the `releaseProcessEnabled` master switch to skip release actions when set to `false`; successful bridge stages preserve the deployment chain so later environments still run.
- The release-process test pipeline now exposes `skipManualApproval` as a queue-time parameter for manually triggered runs.
- Mark re-exported Jira token, email, and webhook outputs as `issecret=true`.
- Match environment Git tags by alphanumeric segments instead of substring `Contains` (avoids `pt` matching `opt-*`).
- Escape `]` and `;` in Azure Pipelines logging-command output values.
- Fixed `ImplementsIssues` parsing/output so multiple linked Jira tickets are handled as separate issue keys.

### Documentation

- Updated `README.md` to reflect the current stage graph, configurable dependency points, and split-template usage examples.
- Documented the configurable PT and live deployment stage names:
  - `awaitingImplementationDependsOn`
  - `implementingDependsOn`
  - `deployedStageDependsOn`
- Clarified `skipManualApproval` scope, cancel mutual exclusion with implementing, env-tag segment matching, and release ticket `Completed` status.
- Added `docs/set-image-tag.md` and linked `templates/set-image-tag.yaml` in the README template catalog and documentation index.

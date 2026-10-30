# Run Tests Jobs

Two independent, single-job templates for building and testing .NET solutions:
`run-unit-tests-job.yaml` and `run-functional-tests-job.yaml`. Each restores NuGet
packages, builds the solution, runs its own test project(s), reports to ReportPortal,
and publishes code coverage — using this org's standard settings by default, so most
callers only need to name their solution and test projects.

**Templates:** `templates/run-unit-tests-job.yaml`, `templates/run-functional-tests-job.yaml`

> **Breaking change (v5.0.0):** `templates/run-tests-stage.yaml` (formerly a stage
> template) has been removed and replaced by these two job templates, and several
> rarely-used parameters were dropped in favor of fixed conventions. See "Migrating
> from run-tests-stage.yaml" below.

## Minimal usage

This is the whole call for a typical consumer — everything else has a working default:

```yaml
resources:
  repositories:
    - repository: pipeline-templates
      type: git
      name: pipeline-templates
      ref: refs/tags/v5.0.0

stages:
  - stage: Tests
    displayName: Run Tests
    jobs:
      - template: templates/run-unit-tests-job.yaml@pipeline-templates
        parameters:
          solutionPath: src/MyService.sln
          unitTestProjects: tests/**/*.UnitTests.csproj

      - template: templates/run-functional-tests-job.yaml@pipeline-templates
        parameters:
          solutionPath: src/MyService.sln
          functionalTestProjects: tests/**/*.FunctionalTests.csproj
```

That's it: no pool, no .NET SDK version, no ReportPortal setup to wire up. Both jobs
report to ReportPortal via the shared `example-key-vault` Key Vault out of the box.

Only need one of them? Include just that template — there's no flag to skip the other:

```yaml
  - stage: Tests
    displayName: Run Tests
    jobs:
      - template: templates/run-unit-tests-job.yaml@pipeline-templates
        parameters:
          solutionPath: src/MyService.sln
          unitTestProjects: tests/**/*.csproj
```

Want functional tests to run later, e.g. against a Dev deployment instead of
alongside unit tests? Put the two template references in different stages — nothing
ties them together:

```yaml
  - stage: UnitTests
    jobs:
      - template: templates/run-unit-tests-job.yaml@pipeline-templates
        parameters: { solutionPath: src/MyService.sln, unitTestProjects: tests/**/*.UnitTests.csproj }

  - stage: FunctionalTests
    dependsOn: [ DeployDev ]
    jobs:
      - template: templates/run-functional-tests-job.yaml@pipeline-templates
        parameters: { solutionPath: src/MyService.sln, functionalTestProjects: tests/**/*.FunctionalTests.csproj }
```

Stage-level concerns (name, `dependsOn`, `condition`) are never template parameters —
they're just normal fields on whatever `- stage:` block you put the template call in.

## What's fixed vs. what you can override

Everything not in this table is a fixed convention, not a parameter — job name/display
name, agent pool, build configuration, Docker host, and the ReportPortal secret name are
all hardcoded because no consumer in this org has ever needed to change them. If you
genuinely need one of those to vary, that's worth raising as a template change rather
than reverse-engineering a workaround.

| Parameter | Type | Default | When to set it |
|-----------|------|---------|-----------------|
| `solutionPath` | string | `src/*.sln` | Always — point it at your `.sln`/`.slnx` |
| `unitTestProjects` (unit-tests job only) | string | *required* | Always — glob for your unit test project(s) |
| `functionalTestProjects` (functional-tests job only) | string | *required* | Always — glob for your functional test project(s) |
| `dotnetSdkVersion` | string | `10.0.x` | Your solution targets a different SDK line |
| `projectsToRestore` | string | `src/**/*.csproj` | Your projects don't live under `src/` |
| `nugetConfigPath` | string | `src/nuget.config` | Your NuGet config isn't at the default path |
| `coverletSettingsPath` | string | `''` (none) | You need coverage exclusions or format overrides |
| `reportPortalEnabled` | boolean | `true` | Set to `false` to skip ReportPortal reporting entirely for this job (no Key Vault call, no `REPORTPORTAL_*` env vars) |
| `azureSubscription` | string | `example-azure-service-connection` | You use a different Key Vault-hosting subscription |
| `keyVaultName` | string | `example-key-vault` | You use a different vault, or set to `''` for pipeline-variable mode |
| `reportPortalApiKey` | string | `''` | Only read when `keyVaultName` is `''` — a `$(ReportPortalApiKey)` pipeline variable instead of vault |

## ReportPortal key: Key Vault by default

`azureSubscription`/`keyVaultName` already point at this org's shared tooling vault
(`example-key-vault`, via `example-azure-service-connection`, documented in this repo's README
under "Where tooling secrets live"), which holds the `report-portal-api-key` secret.
Because that's the default, most callers don't set these two parameters at all.

To opt out and use a pipeline variable instead, set `keyVaultName` to `''` explicitly
and pass `reportPortalApiKey`:

```yaml
      - template: templates/run-unit-tests-job.yaml@pipeline-templates
        parameters:
          solutionPath: src/MyService.sln
          unitTestProjects: src/Tests/**/*.Tests.csproj
          keyVaultName: ''
          reportPortalApiKey: $(ReportPortalApiKey)
```

Note that vault mode adds an `AzureKeyVault@2` task to whichever job(s) you include, so
the pipeline's build identity needs that service connection authorised.

## Migrating from run-tests-stage.yaml

If your pipeline still calls the old combined `run-tests-stage.yaml` (a stage template,
then briefly a two-job template under that same name), update it to call the two job
templates directly:

- Split one `run-tests-stage.yaml` call into two: `run-unit-tests-job.yaml` with your
  `unitTestProjects`, and `run-functional-tests-job.yaml` with your
  `functionalTestProjects`. Both still go under your own `- stage: Tests` block.
- Drop `stageName`, `stageDisplayName`, `jobName`, `jobDisplayName`, `poolName`,
  `buildConfiguration`, `publishCodeCoverage`, `dockerHost`, and
  `reportPortalApiKeySecretName` if you were passing them — none of these exist as
  parameters anymore; they're fixed conventions now (or, for stage-level concerns,
  fields on your own `- stage:` block).
- Drop `azureSubscription: example-azure-service-connection` / `keyVaultName: example-key-vault` if
  that's what you had — it's the default now, so passing it is a no-op.

## ReportPortal.json should ship with `enabled: false`

By default (`reportPortalEnabled: true`) both jobs set `REPORTPORTAL_ENABLED: 'true'`
on the test-run step, which overrides whatever `ReportPortal.json` in the test project
says (ReportPortal's .NET agent reads config from the file first, then environment
variables, then command line - `enabled` maps to `REPORTPORTAL_ENABLED`). Keep the
checked-in `ReportPortal.json` set to `"enabled": false` - the pipeline is what turns
reporting on, not the file. This means a local `dotnet test` run doesn't accidentally
report to ReportPortal, while CI does (unless you opt out below).

Pass `reportPortalEnabled: false` to turn CI reporting off too - for example while a
service's ReportPortal project isn't set up yet, or for a suite you don't want
polluting ReportPortal launches. This also skips the `AzureKeyVault@2` call entirely,
so the job doesn't need that service connection authorised:

```yaml
      - template: templates/run-unit-tests-job.yaml@pipeline-templates
        parameters:
          solutionPath: src/MyService.sln
          unitTestProjects: src/Tests/**/*.Tests.csproj
          reportPortalEnabled: false
```

## Test projects must reference ReportPortal.VSTest.TestLogger, not ReportPortal.NUnit

Both jobs now always pass `--logger "ReportPortal"` on the `dotnet test` command line.
That flag is the `ReportPortal.VSTest.TestLogger` package's hook into the VSTest
platform — every test project run through these templates must reference that package
(and keep a `ReportPortal.json` next to it, same as before). If a project only has the
older `ReportPortal.NUnit` package, `dotnet test` now fails outright with an unrecognized
logger error.

`ReportPortal.NUnit` is no longer viable here: `NUnit3TestAdapter` v6+ changed how the
NUnit engine discovers extensions so that it only loads them from the global NuGet
package cache, not from the per-project `ReportPortal.addins` file `ReportPortal.NUnit`
generates next to the test assembly. That's an intentional upstream change, closed as
"Not a Bug" - see
[reportportal/agent-net-nunit#100](https://github.com/reportportal/agent-net-nunit/issues/100)
and [nunit/nunit-console#1819](https://github.com/nunit/nunit-console/issues/1819). In
practice this meant NUnit projects on the newer adapter built and ran tests fine but
silently reported nothing to ReportPortal, with no error anywhere. `ReportPortal.VSTest.TestLogger`
sidesteps the problem entirely (and also happens to be the only option for xUnit
projects, which never had an addins-based extension mechanism), so it's now the one
supported path for both.

Migrating a project: swap the `ReportPortal.NUnit` package reference for
`ReportPortal.VSTest.TestLogger`; `ReportPortal.json` and the `REPORTPORTAL_ENABLED`/
`REPORTPORTAL_SERVER_APIKEY` env vars these jobs set are unchanged, since both packages
read the same config file and env vars.

## Notes

- NuGet restore uses `$(System.AccessToken)` for Azure Artifacts authentication. The pipeline must grant the build service access to the feed.
- Functional tests always receive `DOCKER_HOST`; both jobs receive `REPORTPORTAL_ENABLED` always, plus `REPORTPORTAL_SERVER_APIKEY` when `reportPortalEnabled` is `true`.
- Test results are published as TRX files; coverage is collected with XPlat Code Coverage and published as Cobertura, once per job.
- The shared restore/build steps live in `templates/run-tests-steps.yaml`, included internally by each job template — you shouldn't need to reference it directly.

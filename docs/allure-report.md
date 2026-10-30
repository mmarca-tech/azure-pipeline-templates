# Allure Report Steps

Step template that generates an Allure HTML report from test results and uploads it to Azure Blob Storage for static web hosting.

**Template:** `templates/allure-report.yaml`

This is a **steps** template, not a stage template. Include it inside a job after tests have produced Allure results.

## What it does

1. Copies Allure result files from `$(Build.ArtifactStagingDirectory)/allure-results` into a running `allure_cmd` Docker container
2. Runs `allure generate` inside the container
3. Copies the generated report back to the artifact staging directory
4. Uploads the report to Azure Blob Storage static website hosting

Both steps run with `condition: succeededOrFailed()` so the report is generated even when tests fail.

## Prerequisites

- Allure result files must already exist at `$(Build.ArtifactStagingDirectory)/allure-results`
- A Docker container named `allure_cmd` must be running on the agent (typically started earlier in the same job)
- The Azure service connection `example-storage-service-connection` must have permission to upload to the storage account

## Default upload target

| Setting | Value |
|---------|-------|
| Service connection | `example-storage-service-connection` |
| Storage account | `examplestorageaccount` |
| Subscription | `example-azure-service-connection` |
| Blob path | `$web/$(Build.BuildNumber)` |

Reports are uploaded under the build number, so each run gets a unique URL path.

## Usage

Add the template at the end of a test job, after Allure results are collected:

```yaml
jobs:
  - job: functional_tests
    pool:
      name: example-agent-pool
    steps:
      - script: docker run -d --name allure_cmd allure/allure:latest sleep infinity
        displayName: Start Allure container

      # ... run tests that write to $(Build.ArtifactStagingDirectory)/allure-results ...

      - template: templates/allure-report.yaml@pipeline-templates
```

## Notes

- The service connection, storage account, and subscription are currently hard-coded in the template. Override by forking the template or wrapping it if your project uses different infrastructure.
- The upload uses `az storage blob upload-batch` with `--auth-mode login`.

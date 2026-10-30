# Set Image Tag Steps

Set image tag steps template that re-tags one or more Azure Container Registry images by importing an existing tag to a new stable tag.

**Template:** `templates/set-image-tag.yaml`

## What it does

1. Iterates over each image in `images`
2. Runs `az acr import` for each image
3. Creates or overwrites `${newTag}` from `${sourceTag}`

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `azureServiceConnection` | string | *required* | Azure service connection used by `AzureCLI@2` authentication |
| `azureSubscription` | string | *required* | Azure subscription ID or name passed to `az acr import --subscription` |
| `sourceTag` | string | *required* | Existing source image tag |
| `newTag` | string | *required* | Target stable image tag |
| `containerRegistryName` | string | *required* | ACR name without domain suffix |
| `images` | object | `[]` | List of repository/image names to re-tag |

## Usage

```yaml
steps:
  - template: templates/set-image-tag.yaml@pipeline-templates
    parameters:
      azureServiceConnection: 'azureServiceConnection_Live'
      azureSubscription: '00000000-0000-0000-0000-000000000000'
      containerRegistryName: 'myregistry'
      sourceTag: '2026.07.27.1'
      newTag: 'live'
      images:
        - 'example/service-api'
        - 'example/service-worker'
```

## Notes

- This template is a **steps** template and must be used inside a job.
- `az acr import --force` is used, so an existing `${newTag}` is replaced.
- Source and target images are in the same registry (`${containerRegistryName}.azurecr.io`).

# TeamsChannelUpdater - Flex Consumption

This function has been converted to run on Azure Functions Flex Consumption plan with isolated worker model.

## Key Changes

### Infrastructure
- Service Plan SKU: `FC1` (Flex Consumption)
- Function App Kind: `functionapp,linux`
- Runtime: `.NET 8 Isolated`
- Instance Memory: 2048 MB

### Configuration
- App settings use `__` (double underscore) instead of `:` (colon) for nested configuration keys
- Uses managed identity for storage account authentication
- Requires storage account and app package container for deployment

### Deployment
When deploying this function through the YAML pipeline, ensure the `isFlexConsumption` parameter is set to `true` for all environments. This is typically configured in the environment-specific deployment YAML files.

Example deployment configuration:
```yaml
- function:
    name: 'TeamsChannelUpdater'
isFlexConsumption: true
```

### Required Azure Resources
The following secrets must be present in the data Key Vault:
- `teamsChannelUpdaterStorageAccountProd`: Storage account name for the function deployment
- `teamsChannelUpdaterAppPackageContainerProd`: Container name for app package deployment

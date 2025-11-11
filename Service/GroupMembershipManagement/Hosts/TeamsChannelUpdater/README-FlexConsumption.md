# TeamsChannelUpdater - Flex Consumption Migration

This function has been converted to run on Azure Functions Flex Consumption plan with isolated worker model.

## Key Changes

### Infrastructure
- **Service Plan SKU**: `FC1` (Flex Consumption)
- **Service Plan Tier**: `FlexConsumption`
- **Function App Kind**: `functionapp,linux`
- **Runtime**: `.NET 8 Isolated Worker`
- **Instance Memory**: 2048 MB (configurable via parameter)
- **Maximum Instance Count**: 40 (configurable via parameter)

### Code Changes
1. **Program.cs**: Created new entry point for isolated worker model (replaces Startup.cs)
2. **Project File**: Updated to use `Microsoft.NET.Sdk.Worker` and isolated worker packages
3. **All Function Files**: Updated to use isolated worker attributes and types:
   - `[FunctionName]` → `[Function]`
   - `IDurableOrchestrationContext` → `TaskOrchestrationContext`
   - `IDurableOrchestrationClient` → `DurableTaskClient`
   - `OrchestrationRuntimeStatus` return types removed (void for orchestrators)

### Configuration
- **App Settings Format**: Use `__` (double underscore) instead of `:` (colon) for nested configuration keys
  - Examples:
    - `graphCredentials:ClientId` → `graphCredentials__ClientId`
    - `ConnectionStrings:JobsContext` → `ConnectionStrings__JobsContext`
    - `AzureFunctionsJobHost:extensions:durableTask:extendedSessionsEnabled` → removed (configured in host.json)
- **Storage Authentication**: Uses managed identity (`AzureWebJobsStorage__credential: 'managedidentity'`)
- **Deployment Storage**: Requires storage account and app package container for deployment

### Deployment Requirements

#### Azure Resources
The following secrets must be present in the data Key Vault:
- `teamsChannelUpdaterStorageAccountProd`: Storage account name for the function deployment
- `teamsChannelUpdaterAppPackageContainerProd`: Container name for app package deployment

#### YAML Pipeline Configuration
When deploying this function through the YAML pipeline, ensure the `isFlexConsumption` parameter is set to `true` for all environments. This is typically configured in the environment-specific deployment YAML files or passed as a parameter to the deployment template.

Example deployment configuration:
```yaml
functions:
  - name: 'TeamsChannelUpdater'
    isFlexConsumption: true
```

#### Bicep Template Parameters
The template now supports the following parameters:
- `servicePlanSku`: Default is `'FC1'`
- `functionAppKind`: Default is `'functionapp,linux'`
- `instanceMemoryMB`: Default is `2048`
- `maxInstanceCount`: Default is `40`

### host.json Configuration
The `host.json` already has the required configuration:
- `extendedSessionsEnabled: true` for Durable Functions
- `extendedSessionIdleTimeoutInSeconds: 30`
- Dynamic concurrency enabled

### Benefits of Flex Consumption
- **Better Memory Management**: 2GB instances allow for handling larger membership sets
- **Improved Scaling**: Flex consumption provides faster scale-out capabilities
- **Cost Optimization**: Pay only for the execution time with better resource utilization
- **Reduced Cold Starts**: Extended sessions keep function instances warm longer

## Migration Notes
- The Startup.cs file is no longer used (replaced by Program.cs)
- All function signatures have been updated to use isolated worker types
- The infrastructure templates now use newer API versions (`Microsoft.Web/sites@2023-12-01`)
- RBAC permissions for storage account access are already configured (no action needed)

## Testing
After deployment, verify:
1. Function app is running in isolated mode
2. Service plan SKU is FC1
3. App settings use `__` delimiter
4. Storage account authentication works via managed identity
5. Durable Functions orchestrations work correctly

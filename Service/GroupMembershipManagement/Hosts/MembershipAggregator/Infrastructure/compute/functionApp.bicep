@description('Function app name.')
@minLength(1)
param name string

@description('Function app kind.')
@allowed([
  'functionapp'
  'linux'
  'container'
])
param kind string = 'functionapp'

@description('Function app location.')
param location string

@description('Service plan name.')
@minLength(1)
param servicePlanName string

@description('app settings')
param secretSettings object

@description('Name of the \'data\' key vault.')
param dataKeyVaultName string

@description('Name of the resource group where the \'data\' key vault is located.')
param dataKeyVaultResourceGroup string

@description('Tenant id.')
param tenantId string

resource functionApp 'Microsoft.Web/sites@2018-02-01' = {
  name: name
  location: location
  kind: kind
  properties: {
    serverFarmId: resourceId('Microsoft.Web/serverfarms', servicePlanName)
    clientAffinityEnabled: false
    httpsOnly: true
    siteConfig: {
      use32BitWorkerProcess : false
      appSettings: secretSettings
    }
  }
  identity: {
    type: 'SystemAssigned'
  }
}

module secretsTemplate 'keyVaultSecrets.bicep' = {
  name: 'secretsTemplate-MembershipAggregator'
  scope: resourceGroup(dataKeyVaultResourceGroup)
  params: {
    keyVaultName: dataKeyVaultName
    keyVaultParameters: [
      {
        name: 'membershipAggregatorUrl'
        value: 'https://${functionApp.properties.defaultHostName}/api/StarterFunction'
      }
      {
        name: 'membershipAggregatorFunctionName'
        value: '${name}-MembershipAggregator'
      }
    ]
  }
}

module secureSecretsTemplate 'keyVaultSecretsSecure.bicep' = {
  name: 'secureSecretsTemplate-MembershipAggregator'
  scope: resourceGroup(dataKeyVaultResourceGroup)
  params: {
    keyVaultName: dataKeyVaultName
    keyVaultSecret: {
        name: 'membershipAggregatorFunctionKey'
        value: listkeys('${functionApp.id}/host/default', '2018-11-01').functionKeys.default
      }
  }
}

resource functionAppSlotConfig 'Microsoft.Web/sites/config@2021-03-01' = {
  name: 'slotConfigNames'
  parent: functionApp
  properties: {
    appSettingNames: [
      'graphUpdaterUrl'
      'graphUpdaterFunctionKey'
      'AzureFunctionsJobHost__extensions__durableTask__hubName'
      'AzureWebJobs.StarterFunction.Disabled'
      'AzureWebJobs.OrchestratorFunction.Disabled'
      'AzureWebJobs.MembershipSubOrchestratorFunction.Disabled'
      'AzureWebJobs.DeltaCalculatorFunction.Disabled'
      'AzureWebJobs.FileDownloaderFunction.Disabled'
      'AzureWebJobs.FileUploaderFunction.Disabled'
      'AzureWebJobs.JobStatusUpdaterFunction.Disabled'
      'AzureWebJobs.JobTrackerEntity.Disabled'
      'AzureWebJobs.LoggerFunction.Disabled'
    ]
  }
}

output msi string = functionApp.identity.principalId

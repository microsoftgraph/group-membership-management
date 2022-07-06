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

@description('Array of key vault references to be set in app settings')
param secretSettings array

@description('Name of the \'data\' key vault.')
param dataKeyVaultName string

@description('Name of the resource group where the \'data\' key vault is located.')
param dataKeyVaultResourceGroup string

resource functionAppSlot 'Microsoft.Web/sites/slots@2018-11-01' = {
  name: name
  kind: kind
  location: location
  properties: {
    clientAffinityEnabled: true
    enabled: true
    httpsOnly: true
    serverFarmId: resourceId('Microsoft.Web/serverfarms', servicePlanName)
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
  name: 'secretsTemplate'
  scope: resourceGroup(dataKeyVaultResourceGroup)
  params: {
    keyVaultName: dataKeyVaultName
    keyVaultParameters: [
      {
        name: 'webAppFunctionStagingUrl'
        value: 'https://${functionAppSlot.properties.defaultHostName}/api/StarterFunction'
      }
      {
        name: 'webAppFunctionStagingFunctionKey'
        value: listkeys('${functionAppSlot.id}/host/default', '2018-11-01').functionKeys.default
      }
      {
        name: 'webAppFunctionStagingFunctionName'
        value: '${name}-WebAppFunction/staging'
      }
    ]
  }
}

output msi string = functionAppSlot.identity.principalId

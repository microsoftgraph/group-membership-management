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
  name: 'secretsTemplate-GroupTableManagerStaging'
  scope: resourceGroup(dataKeyVaultResourceGroup)
  params: {
    keyVaultName: dataKeyVaultName
    keyVaultParameters: [
      {
        name: 'groupTableManagerStagingUrl'
        value: 'https://${functionAppSlot.properties.defaultHostName}/api/StarterFunction'
      }
      {
        name: 'groupTableManagerStagingFunctionName'
        value: '${name}-GroupTableManager/staging'
      }
    ]
  }
}

module secureSecretsTemplate 'keyVaultSecretsSecure.bicep' = {
  name: 'secureSecretsTemplate-GroupTableManagerStaging'
  scope: resourceGroup(dataKeyVaultResourceGroup)
  params: {
    keyVaultName: dataKeyVaultName
    keyVaultSecrets: {
      secrets: [
        { 
          name: 'groupTableManagerStagingFunctionKey'
          value: listkeys('${functionAppSlot.id}/host/default', '2018-11-01').functionKeys.default
        }
      ]
    }
  }
}

output msi string = functionAppSlot.identity.principalId

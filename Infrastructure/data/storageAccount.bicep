@description('Storage account alphanumeric name.')
@minLength(1)
@maxLength(24)
param name string

@description('Key vault name.')
param keyVaultName string

@allowed([
  'Standard_LRS'
  'Standard_GRS'
  'Standard_ZRS'
  'Premium_LRS'
])
param sku string = 'Standard_LRS'

resource storageAccount 'Microsoft.Storage/storageAccounts@2019-04-01' = {
  name: name
  location: resourceGroup().location
  kind: 'StorageV2'
  sku: {
    name: sku
  }
  properties: {
    supportsHttpsTrafficOnly: true
  }
  identity: {
    type: 'SystemAssigned'
  }
}

module secureSecretsTemplate 'keyVaultSecretsSecure.bicep' = {
  name: 'secureSecretsTemplate${name}'
  params: {
    keyVaultName: keyVaultName
    keyVaultSecret: {
      name: startsWith(name, 'jobs') ? 'jobsStorageAccountConnectionString' : 'storageAccountConnectionString'
      value: 'DefaultEndpointsProtocol=https;AccountName=${name};AccountKey=${listkeys(resourceId(resourceGroup().name, 'Microsoft.Storage/storageAccounts', name), providers('Microsoft.Storage', 'storageAccounts').apiVersions[0]).keys[0].value}'
    }
  }
}

@description('Storage account alphanumeric name')
param storageAccountName string

@description('Name of blob container')
param containerName string

@description('Enter storage account sku.')
param sku string

@description('Key vault name.')
param keyVaultName string

@description('Resource location, azure region.')
param location string

resource storageAccount 'Microsoft.Storage/storageAccounts@2019-04-01' = {
  name: storageAccountName
  location: location
  kind: 'StorageV2'
  sku: {
    name: sku
  }
  properties: {
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
  }
  identity: {
    type: 'SystemAssigned'
  }
}

resource storageAccountContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2019-04-01' = {
  name: '${storageAccountName}/default/${containerName}'
  dependsOn: [
    storageAccount
  ]
}

module secureSecretsTemplate 'keyVaultSecretsSecure.bicep' = {
  name: 'secureSecretsTemplate'
  params: {
    keyVaultName: keyVaultName
    keyVaultSecrets: {
      secrets: [
        {
          name: 'storageAccountConnectionString'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value}'
        }
      ]
    }
  }
}



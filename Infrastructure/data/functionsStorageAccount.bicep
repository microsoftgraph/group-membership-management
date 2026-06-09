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

@description('Specifies the Azure location where the storage account will be created.')
param location string

@description('Whether to add lifecycle management policies to the storage account.')
param addLifecyclePolicies bool = false

@description('Key vault setting name to store the storage account name.')
param storageAccountSecretName string

resource storageAccount 'Microsoft.Storage/storageAccounts@2022-05-01' = {
  name: name
  location: location
  kind: 'StorageV2'
  sku: {
    name: sku
  }
  properties: {
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    allowSharedKeyAccess: false
    publicNetworkAccess: 'Enabled'
  }
  identity: {
    type: 'SystemAssigned'
  }
}

resource lifecyclePolicies 'Microsoft.Storage/storageAccounts/managementPolicies@2022-05-01' = if (addLifecyclePolicies) {
  name: 'default'
  parent: storageAccount
  properties: {
    policy: {
      rules: [
        {
          definition: {
            actions: {
              baseBlob: {
                delete: {
                  daysAfterModificationGreaterThan: 30
                }
              }
            }
            filters: {
              blobTypes: [
                'blockBlob'
              ]
            }
          }
          enabled: true
          name: '30-Day Blob Deletion Policy'
          type: 'Lifecycle'
        }
        {
          definition: {
            actions: {
              baseBlob: {
                delete: {
                  daysAfterModificationGreaterThan: 7
                }
              }
            }
            filters: {
              blobTypes: [
                'blockBlob'
              ]
              prefixMatch: [
                'membership/cache/'
              ]
            }
          }
          enabled: true
          name: '7-Day Cache Blob Deletion Policy'
          type: 'Lifecycle'
        }
      ]
    }
  }
}

module secureSecretsTemplate 'keyVaultSecretsSecure.bicep' = {
  name: 'secureSecretsTemplate-${name}'
  params: {
    keyVaultName: keyVaultName
    keyVaultSecrets: {
      secrets: [
        {
          name: storageAccountSecretName
          value: name
        }
      ]
    }
  }
}

output storageAccountId string = storageAccount.id
output storageAccountName string = storageAccount.name

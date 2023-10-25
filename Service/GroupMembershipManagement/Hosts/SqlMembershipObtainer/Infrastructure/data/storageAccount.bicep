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

@description('Key vault name.')
param addJobsStorageAccountPolicies bool = false

@description('Specifies the Azure location where the storage account will be created.')
param location string

@description('Key vault setting name to store the storage account name.')
param sqlMembershipObtainerStorageAccountName string

@description('Key vault setting name to store the connection string.')
param storageAccountConnectionStringSettingName string

resource storageAccount 'Microsoft.Storage/storageAccounts@2019-04-01' = {
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
  }
  identity: {
    type: 'SystemAssigned'
  }
}

resource allBlobPolicy 'Microsoft.Storage/storageAccounts/managementPolicies@2022-05-01' = if (addJobsStorageAccountPolicies) {
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
  name: 'secureSecretsTemplate${name}'
  params: {
    keyVaultName: keyVaultName
    keyVaultSecrets: {
      secrets: [
        {
          name: sqlMembershipObtainerStorageAccountName
          value: name
        }
        {
          name:  storageAccountConnectionStringSettingName
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value}'
        }
      ]
    }
  }
}

@description('Storage account alphanumeric name.')
@minLength(3)
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
param storageAccountSettingName string

@description('Key vault setting name to store the name of the app package container.')
param appPackageContainerSettingName string

@description('Specifies the name of the app package container.')
param appPackageContainerName string

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
  }
  identity: {
    type: 'SystemAssigned'
  }

  resource blobServices 'blobServices' = {
    name: 'default'
    resource container 'containers' = {
      name: appPackageContainerName
      properties: {
        publicAccess: 'None'
      }
    }
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
          name:  storageAccountSettingName
          value: name
        }
        {
          name:  appPackageContainerSettingName
          value: appPackageContainerName
        }
      ]
    }
  }
}

output storageAccountId string = storageAccount.id

@description('Storage account alphanumeric name.')
@minLength(3)
@maxLength(24)
param name string

@description('Specifies the Azure location where the storage account will be created.')
param location string

@allowed([
  'Standard_LRS'
  'Standard_GRS'
  'Standard_ZRS'
  'Premium_LRS'
])
param sku string = 'Standard_LRS'

@description('Days after which FunctionAppLogs blobs are deleted. Set to 0 to disable the lifecycle policy.')
param logRetentionInDays int = 30

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

resource lifecyclePolicies 'Microsoft.Storage/storageAccounts/managementPolicies@2022-05-01' = if (logRetentionInDays > 0) {
  name: 'default'
  parent: storageAccount
  properties: {
    policy: {
      rules: [
        {
          enabled: true
          name: 'Delete old FunctionAppLogs'
          type: 'Lifecycle'
          definition: {
            actions: {
              baseBlob: {
                delete: {
                  daysAfterModificationGreaterThan: logRetentionInDays
                }
              }
            }
            filters: {
              blobTypes: [
                'blockBlob'
              ]
            }
          }
        }
      ]
    }
  }
}

output storageAccountId string = storageAccount.id
output storageAccountName string = storageAccount.name

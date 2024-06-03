@minLength(1)
param name string

@allowed([
  'Standard'
  'Premium'
])
param sku string = 'Standard'

@description('Location for the service bus.')
param location string

@description('Data KeyVault name.')
param keyVaultName string

@description('Log Analytics Workspace Id.')
param logAnalyticsWorkspaceId string

resource serviceBus 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: name
  location: location
  sku: {
    name: sku
  }
  properties: {
    minimumTlsVersion: '1.2'
    disableLocalAuth: true
  }
}

module serviceBusSecrets 'keyVaultSecretsSecure.bicep' = {
  name: 'serviceBusSecretsTemplate'
  params: {
    keyVaultName: keyVaultName
    keyVaultSecrets: {
      secrets: [
        {
          name: 'serviceBusFQN'
          value: '${name}.servicebus.windows.net'
        }
      ]
    }
  }
  dependsOn: [
    serviceBus
  ]
}

resource diagnosticSettings 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'ServiceBus-diagnostics'
  scope: serviceBus
  properties: {
    workspaceId:  logAnalyticsWorkspaceId
    logs: [
      {
        category: null
        categoryGroup: 'allLogs'
        enabled: true
        retentionPolicy: {
          days: 0
          enabled: false
        }
      }
    ]
  }
}

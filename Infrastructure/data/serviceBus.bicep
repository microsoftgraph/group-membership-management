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

@description('Name of the key vault.')
param keyVaultName string

@description('Array of key vault parameters.')
param keyVaultParameters array

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource keyVaultSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = [for item in keyVaultParameters: {
  name: item.name
  parent: keyVault
  properties: {
    value: item.value
  }
}]

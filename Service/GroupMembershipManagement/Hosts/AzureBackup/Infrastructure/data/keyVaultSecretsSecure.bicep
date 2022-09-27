param keyVaultName string
@secure()
param keyVaultSecret object

resource secret 'Microsoft.KeyVault/vaults/secrets@2021-06-01-preview' = {
  name: '${keyVaultName}/${keyVaultSecret.name}'
  properties: {
    value: keyVaultSecret.value
  }
}

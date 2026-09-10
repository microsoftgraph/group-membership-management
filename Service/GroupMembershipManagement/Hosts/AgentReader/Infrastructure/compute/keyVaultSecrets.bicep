param keyVaultName string
param keyVaultParameters array

resource secrets 'Microsoft.KeyVault/vaults/secrets@2019-09-01' = [for item in keyVaultParameters: {
  name: '${keyVaultName}/${item.name}'
  properties: {
    value: item.value
  }
}]
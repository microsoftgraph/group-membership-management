param keyVaultName string
@secure()
param keyVaultSecrets object

resource secrets 'Microsoft.KeyVault/vaults/secrets@2021-06-01-preview' = [for secret in keyVaultSecrets.secrets: {
  name: '${keyVaultName}/${secret.name}'
  properties: {
    value: secret.value
  }
}]

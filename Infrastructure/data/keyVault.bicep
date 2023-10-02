@description('Key vault name.')
@minLength(1)
param name string

@description('Key vault sku name.')
@allowed([
  'premium'
  'standard'
])
param skuName string = 'standard'

@description('Key vault sku family.')
param skuFamily string = 'A'

@description('Key vault location.')
param location string

@description('Key vault tenant id.')
param tenantId string

resource keyVault 'Microsoft.KeyVault/vaults@2019-09-01' = {
  name: name
  location: location
  properties: {
    enabledForDeployment: true
    enabledForTemplateDeployment: true
    enabledForDiskEncryption: true
    enableSoftDelete: true
    enablePurgeProtection: true
    sku: {
      name: skuName
      family: skuFamily
    }
    tenantId: tenantId
    accessPolicies: []
  }
}

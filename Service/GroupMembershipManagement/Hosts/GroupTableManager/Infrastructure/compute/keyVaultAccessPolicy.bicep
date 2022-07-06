@description('Name of the key vault')
param name string

@metadata({
  description: 'Array of object ids and permissions.'
  sample: '\\[{\'objectId\': \'<guid>\', \'permissions\': [\'list\', \'get\', \'set\']}]\\]'
})
param policies array
param tenantId string

resource accessPolicies 'Microsoft.KeyVault/vaults/accessPolicies@2016-10-01' = {
  name: '${name}/add'
  properties: {
    accessPolicies: [for item in policies: {
      tenantId: tenantId
      objectId: item.objectId
      permissions: {
        secrets: item.permissions
      }
    }]
  }
}
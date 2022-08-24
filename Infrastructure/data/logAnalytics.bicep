@description('Name for log analytics account.')
param name string

@description('Pricing tier: PerGB2018 or legacy tiers (Free, Standalone, PerNode, Standard or Premium) which are not available to all customers.')
@allowed([
  'CapacityReservation'
  'Free'
  'PerGB2018'
  'PerNode'
  'Premium'
  'Standalone'
  'Standard'
])
param sku string = 'PerGB2018'

@description('Location for the log analytics account.')
param location string

@description('Key vault name.')
param keyVaultName string

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2021-06-01' = {
  name: name
  location: location
  properties: {
    sku: {
      name: sku
    }
    retentionInDays: 365
  }
}

module secureSecretsTemplatePrimaryKey 'keyVaultSecretsSecure.bicep' = {
  name: 'secureSecretsTemplatePrimaryKey'
  params: {
    keyVaultName: keyVaultName
    keyVaultSecret: {
        name: 'logAnalyticsPrimarySharedKey'
        value: listKeys(logAnalyticsWorkspace.id, '2021-06-01').primarySharedKey
      }
  }
}

output customerId string = reference(logAnalyticsWorkspace.id, '2021-06-01').customerId
output resourceId string = logAnalyticsWorkspace.id

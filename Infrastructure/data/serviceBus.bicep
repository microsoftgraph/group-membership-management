@minLength(1)
param name string

@allowed([
  'Standard'
  'Premium'
])
param sku string = 'Standard'

@description('Location for the service bus.')
param location string

var authRuleResourceId = resourceId('Microsoft.ServiceBus/namespaces/authorizationRules', name, 'RootManageSharedAccessKey')

resource serviceBus 'Microsoft.ServiceBus/namespaces@2017-04-01' = {
  name: name
  location: location
  sku: {
    name: sku
  }
  properties: {}
}

output rootManageSharedAccessKeyPrimaryKey string = listkeys(authRuleResourceId, '2017-04-01').primaryKey
output rootManageSharedAccessKeyConnectionString string = listkeys(authRuleResourceId, '2017-04-01').primaryConnectionString

@minLength(1)
param name string

@allowed([
  'Standard'
  'Premium'
])
param sku string = 'Standard'

@description('Location for the service bus.')
param location string

@description('Key vault name.')
param keyVaultName string

var authRuleResourceId = resourceId('Microsoft.ServiceBus/namespaces/authorizationRules', name, 'RootManageSharedAccessKey')

resource serviceBus 'Microsoft.ServiceBus/namespaces@2017-04-01' = {
  name: name
  location: location
  sku: {
    name: sku
  }
  properties: {}
}

module secureSecretsTemplatePrimaryKey 'keyVaultSecretsSecure.bicep' = {
  name: 'secureSecretsTemplatePrimaryKey'
  params: {
    keyVaultName: keyVaultName
    keyVaultSecret: {
      name: 'serviceBusPrimaryKey'
      value: listkeys(authRuleResourceId, '2017-04-01').primaryKey
    }
  }
}

module secureSecretsTemplateConnectionString 'keyVaultSecretsSecure.bicep' = {
  name: 'secureSecretsTemplateConnectionString'
  params: {
    keyVaultName: keyVaultName
    keyVaultSecret: {
      name: 'serviceBusConnectionString'
      value: listkeys(authRuleResourceId, '2017-04-01').primaryConnectionString
    }
  }
}

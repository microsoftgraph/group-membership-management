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

resource serviceBus 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: name
  location: location
  sku: {
    name: sku
  }
  properties: {
    minimumTlsVersion: '1.2'
  }
}

module secureSecretsTemplatePrimaryKey 'keyVaultSecretsSecure.bicep' = {
  name: 'secureSecretsTemplatePrimaryKey'
  params: {
    keyVaultName: keyVaultName
    keyVaultSecrets: {
      secrets: [
        {
          name: 'serviceBusPrimaryKey'
          value: listkeys(authRuleResourceId, '2017-04-01').primaryKey
        }
      ]
    }
  }
}

module secureSecretsTemplateConnectionString 'keyVaultSecretsSecure.bicep' = {
  name: 'secureSecretsTemplateConnectionString'
  params: {
    keyVaultName: keyVaultName
    keyVaultSecrets: {
      secrets: [
        {
          name: 'serviceBusConnectionString'
          value: listkeys(authRuleResourceId, '2017-04-01').primaryConnectionString
        }
      ]
    }
  }
}

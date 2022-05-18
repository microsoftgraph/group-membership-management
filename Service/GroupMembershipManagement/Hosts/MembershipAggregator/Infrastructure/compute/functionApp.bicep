@description('Function app name.')
@minLength(1)
param name string

@description('Function app kind.')
@allowed([
  'functionapp'
  'linux'
  'container'
])
param kind string = 'functionapp'

@description('Function app location.')
param location string

@description('Service plan name.')
@minLength(1)
param servicePlanName string

@description('Array of key vault references to be set in app settings')
param secretSettings array

@description('Name of the \'data\' key vault.')
param dataKeyVaultName string

@description('Name of the resource group where the \'data\' key vault is located.')
param dataKeyVaultResourceGroup string

@description('Tenant id.')
param tenantId string

resource functionApp 'Microsoft.Web/sites@2018-02-01' = {
  name: name
  location: location
  kind: kind
  properties: {
    serverFarmId: resourceId('Microsoft.Web/serverfarms', servicePlanName)
    clientAffinityEnabled: false
    httpsOnly: true
    siteConfig: {
      use32BitWorkerProcess : false
      appSettings: secretSettings
    }
  }
  identity: {
    type: 'SystemAssigned'
  }
}

module keyVaultPoliciesTemplate 'keyVaultAccessPolicy.bicep' = {
  name: 'keyVaultPoliciesTemplate'
  scope: resourceGroup(dataKeyVaultResourceGroup)
  params: {
    name: dataKeyVaultName
    policies: [
      {
        objectId: functionApp.identity.principalId
        permissions: [
          'get'
          'list'
        ]
      }
    ]
    tenantId: tenantId
  }
}

module secretsTemplate 'keyVaultSecrets.bicep' = {
  name: 'dataSecretsTemplate-MembershipAggregator'
  scope: resourceGroup(dataKeyVaultResourceGroup)
  params: {
    keyVaultName: dataKeyVaultName
    keyVaultParameters: [
      {
        name: 'membershipAggregatorUrl'
        value: 'https://${functionApp.properties.defaultHostName}/api/StarterFunction'
      }
      {
        name: 'membershipAggregatorFunctionKey'
        value: listkeys('${functionApp.id}/host/default', '2018-11-01').functionKeys.default
      }
      {
        name: 'membershipAggregatorFunctionName'
        value: '${name}-MembershipAggregator'
      }
    ]
  }
  dependsOn: [
    keyVaultPoliciesTemplate
  ]
}

output msi string = functionApp.identity.principalId

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

@description('app settings')
param secretSettings object

@description('Name of the \'data\' key vault.')
param dataKeyVaultName string

@description('Name of the resource group where the \'data\' key vault is located.')
param dataKeyVaultResourceGroup string

@description('Log Analytics Workspace Id.')
param logAnalyticsWorkspaceId string

@description('Object with flags to determine behaviour')
param featureFlags object

@description('Name of the resource group where the \'prereqs\' key vault is located.')
param prereqsKeyVaultName string

@description('Name of the resource group where the \'prereqs\' key vault is located.')
param prereqsKeyVaultResourceGroup string

@description('Flag to indicate if the deployment should set RBAC permissions.')
param setRBACPermissions bool

@description('Storage account name.')
param storageAccountName string

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
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
    }
  }
  identity: {
    type: 'SystemAssigned'
  }
}

module functionAppRBAC 'functionAppRBAC.bicep' = {
  name: 'functionAppsRBAC-JobScheduler'
  params: {
    functionName: 'JobScheduler'
    prereqsKeyVaultName: prereqsKeyVaultName
    prereqsKeyVaultResourceGroup: prereqsKeyVaultResourceGroup
    dataKeyVaultName: dataKeyVaultName
    dataKeyVaultResourceGroup: dataKeyVaultResourceGroup
    setRBACPermissions: setRBACPermissions
    productionSlotPrincipalId: functionApp.identity.principalId
    storageAccountName: storageAccountName
  }
}

resource diagnosticSettings 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'functionApp-diagnostics'
  scope: functionApp
  properties: {
    workspaceId:  logAnalyticsWorkspaceId
    logs: [
      {
        category: 'FunctionAppLogs'
        enabled: true
        retentionPolicy: {
          days: 0
          enabled: false
        }
      }
    ]
  }
  dependsOn:[
    functionAppRBAC
  ]
}

resource snScmBasicAuth 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2022-09-01' = {
  parent: functionApp
  name: 'scm'
  properties: {
    allow: false
  }
  dependsOn:[
    diagnosticSettings
  ]
}

resource snFtpBasicAuth 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2022-09-01' = {
  parent: functionApp
  name: 'ftp'
  properties: {
    allow: false
  }
  dependsOn:[
    snScmBasicAuth
  ]
}

module secretsTemplate 'keyVaultSecrets.bicep' = {
  name: 'secretsTemplate-JobScheduler'
  scope: resourceGroup(dataKeyVaultResourceGroup)
  params: {
    keyVaultName: dataKeyVaultName
    keyVaultParameters: [
      {
        name: 'jobSchedulerFunctionBaseUrl'
        value: 'https://${functionApp.properties.defaultHostName}'
      }
    ]
  }
  dependsOn:[
    snFtpBasicAuth
  ]
}

module secureSecretsTemplate 'keyVaultSecretsSecure.bicep' = if(!featureFlags.skipListingFunctionAppKeys) {
  name: 'secureSecretsTemplate-JobScheduler'
  scope: resourceGroup(dataKeyVaultResourceGroup)
  params: {
    keyVaultName: dataKeyVaultName
    keyVaultSecrets: {
      secrets: [
        {
          name: 'jobSchedulerFunctionKey'
          value: featureFlags.skipListingFunctionAppKeys ? 'not-set' : listkeys('${functionApp.id}/host/default', '2018-11-01').functionKeys.default
        }
      ]
    }
  }
  dependsOn:[
    secretsTemplate
  ]
}

output msi string = functionApp.identity.principalId

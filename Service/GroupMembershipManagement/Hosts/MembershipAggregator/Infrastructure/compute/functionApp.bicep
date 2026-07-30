@description('Function app name.')
@minLength(1)
param name string

@description('Function app kind.')
@allowed([
  'functionapp'
  'linux'
  'container'
  'functionapp,linux'
])
param kind string = 'functionapp,linux'

@description('Function app location.')
param location string

@description('Function authentication app client id.')
param functionAuthAppClientId string
param enableFunctionAuthentication bool = false

@description('Service plan name.')
@minLength(1)
param servicePlanName string

@description('app settings')
param appSettings object

@description('User assigned managed identities. Single or list of user assigned managed identities. Format: /subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.ManagedIdentity/userAssignedIdentities/{identityName}')
param userManagedIdentities object = {}

var deployUserManagedIdentity = userManagedIdentities != null && userManagedIdentities != {}

@description('Log Analytics Workspace Id.')
param logAnalyticsWorkspaceId string

@description('FunctionAppLogs diagnostic export destination: workspace (default) sends logs to the Log Analytics workspace, storage sends them to a dedicated storage account this deployment provisions in the data resource group, and none disables the export.')
@allowed([
  'workspace'
  'storage'
  'none'
])
param functionAppLogsDestination string = 'workspace'

@description('Name of the storage account that receives FunctionAppLogs when functionAppLogsDestination is storage. Set by the deployment, not a customer input.')
param functionAppLogsStorageAccountName string = ''

@description('Name of the resource group where the \'prereqs\' key vault is located.')
param prereqsKeyVaultName string

@description('Name of the resource group where the \'prereqs\' key vault is located.')
param prereqsKeyVaultResourceGroup string

@description('Name of the \'data\' key vault.')
param dataKeyVaultName string

@description('Name of the resource group where the \'data\' key vault is located.')
param dataKeyVaultResourceGroup string

@description('Flag to indicate if the deployment should set RBAC permissions.')
param setRBACPermissions bool

@description('Storage account name.')
param storageAccountName string

@description('Storage account container name.')
param appPackageContainerName string

@description('Maximum instance count.')
param maxInstanceCount int = 45

@description('Instance memory in MB.')
param instanceMemoryMB int = 2048

@description('When true, attaches the function app to a delegated subnet for VNET integration (FC1 / Microsoft.App/environments). Default false preserves pre-feature behavior.')
param enableVnetIntegration bool = false

@description('Resource ID of the delegated subnet for VNET integration. Required when enableVnetIntegration is true; ignored otherwise.')
param virtualNetworkSubnetId string = ''

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' existing = {
  name: storageAccountName
  scope: resourceGroup(dataKeyVaultResourceGroup)
}

var functionAppConfig = {
  deployment: {
    storage: {
      type: 'blobContainer'
      value: '${storageAccount.properties.primaryEndpoints.blob}${appPackageContainerName}'
      authentication: {
        type: 'SystemAssignedIdentity'
      }
    }
  }
  scaleAndConcurrency: {
    maximumInstanceCount: maxInstanceCount
    instanceMemoryMB: instanceMemoryMB
  }
  runtime: {
    name: 'dotnet-isolated'
    version: '10.0'
  }
}

resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: name
  location: location
  kind: kind
  properties: {
    serverFarmId: resourceId('Microsoft.Web/serverfarms', servicePlanName)
    clientAffinityEnabled: false
    httpsOnly: true
    siteConfig: {
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      appSettings: [
        for key in objectKeys(appSettings): {
          name: key
          value: appSettings[key]
        }
      ]
    }
    functionAppConfig: functionAppConfig
    virtualNetworkSubnetId: (enableVnetIntegration && !empty(virtualNetworkSubnetId)) ? virtualNetworkSubnetId : null
  }
  identity: {
    type: deployUserManagedIdentity ? 'SystemAssigned, UserAssigned' : 'SystemAssigned'
    userAssignedIdentities: deployUserManagedIdentity ? userManagedIdentities : null
  }
}

module functionAppRBAC 'functionAppRBAC.bicep' = {
  name: 'functionAppsRBAC-MembershipAggregator'
  params: {
    functionName: 'MembershipAggregator'
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
    workspaceId: functionAppLogsDestination == 'storage' ? null : logAnalyticsWorkspaceId
    storageAccountId: functionAppLogsDestination == 'storage' ? resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.Storage/storageAccounts', functionAppLogsStorageAccountName) : null
    logs: [
      {
        category: 'FunctionAppLogs'
        enabled: functionAppLogsDestination != 'none'
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

resource authSettings 'Microsoft.Web/sites/config@2022-09-01' = if (enableFunctionAuthentication) {
  parent: functionApp
  name: 'authsettingsV2'
  properties: {
    platform: {
      enabled: true
      runtimeVersion: '~1'
    }
    globalValidation: {
      requireAuthentication: true
      unauthenticatedClientAction: 'Return401'
    }
    identityProviders: {
      azureActiveDirectory: {
        enabled: true
        registration: {
          clientId: functionAuthAppClientId
          openIdIssuer: '${environment().authentication.loginEndpoint}${tenant().tenantId}/v2.0'
        }
        validation: {
          allowedAudiences: [
            'api://${functionAuthAppClientId}'
          ]
          defaultAuthorizationPolicy: {
            allowedPrincipals: []
          }
        }
      }
    }
    login: {
      tokenStore: {
        enabled: false
      }
    }
  }
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
  name: 'secretsTemplate-MembershipAggregator'
  scope: resourceGroup(dataKeyVaultResourceGroup)
  params: {
    keyVaultName: dataKeyVaultName
    keyVaultParameters: [
      {
        name: 'membershipAggregatorFunctionName'
        value: '${name}-MembershipAggregator'
      }
    ]
  }
  dependsOn:[
    snFtpBasicAuth
  ]
}

output msi string = functionApp.identity.principalId

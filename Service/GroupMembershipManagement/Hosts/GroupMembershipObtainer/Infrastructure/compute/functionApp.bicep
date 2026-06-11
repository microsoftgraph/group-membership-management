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
param maxInstanceCount int = 40

@description('Instance memory in MB.')
param instanceMemoryMB int = 2048

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
    version: '8.0'
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
  }
  identity: {
    type: deployUserManagedIdentity ? 'SystemAssigned, UserAssigned' : 'SystemAssigned'
    userAssignedIdentities: deployUserManagedIdentity ? userManagedIdentities : null
  }
}

module functionAppRBAC 'functionAppRBAC.bicep' = {
  name: 'functionAppsRBAC-GroupMembershipObtainer'
  params: {
    functionName: 'GroupMembershipObtainer'
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

resource authSettings 'Microsoft.Web/sites/config@2022-09-01' = {
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

output msi string = functionApp.identity.principalId

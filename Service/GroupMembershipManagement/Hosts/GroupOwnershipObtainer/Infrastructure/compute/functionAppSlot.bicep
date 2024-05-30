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
param appSettings object

@description('User assigned managed identities. Single or list of user assigned managed identities. Format: /subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.ManagedIdentity/userAssignedIdentities/{identityName}')
param userManagedIdentities object = {}

var deployUserManagedIdentity = userManagedIdentities != null && userManagedIdentities != {}

@description('Log Analytics Workspace Id.')
param logAnalyticsWorkspaceId string

resource functionAppSlot 'Microsoft.Web/sites/slots@2018-11-01' = {
  name: name
  kind: kind
  location: location
  properties: {
    clientAffinityEnabled: true
    enabled: true
    httpsOnly: true
    serverFarmId: resourceId('Microsoft.Web/serverfarms', servicePlanName)
    siteConfig: {
      use32BitWorkerProcess : false
      appSettings: appSettings
      ftpsState: 'Disabled'
    }
  }
  identity: {
    type: deployUserManagedIdentity ? 'SystemAssigned, UserAssigned' : 'SystemAssigned'
    userAssignedIdentities: deployUserManagedIdentity ? userManagedIdentities : null
  }
}

resource diagnosticSettings 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'functionAppSlot-diagnostics'
  scope: functionAppSlot
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
}

resource functionAppSlot_ftp 'Microsoft.Web/sites/slots/basicPublishingCredentialsPolicies@2022-09-01' = {
  parent: functionAppSlot
  name: 'ftp'
  properties: {
    allow: false
  }
}

resource functionAppSlot_scm 'Microsoft.Web/sites/slots/basicPublishingCredentialsPolicies@2022-09-01' = {
  parent: functionAppSlot
  name: 'scm'
  properties: {
    allow: false
  }
}

output msi string = functionAppSlot.identity.principalId

@description('WebAPI site plan name.')
@minLength(1)
param name string

@description('Resource location.')
param location string

@description('Service plan name.')
param servicePlanName string

@description('Application settings')
param appSettings array

@description('Name of the \'data\' key vault.')
param dataKeyVaultName string

@description('Name of the resource group where the \'data\' key vault is located.')
param dataResourceGroup string

@description('Name of the resource group where the \'prereqs\' key vault is located.')
param prereqsKeyVaultName string

@description('Name of the resource group where the \'prereqs\' key vault is located.')
param prereqsResourceGroup string

@description('Tenant id.')
param tenantId string

@description('User assigned managed identities. Single or list of user assigned managed identities. Format: /subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.ManagedIdentity/userAssignedIdentities/{identityName}')
param userManagedIdentities object = {}

@description('Flag to indicate if the deployment should set RBAC permissions.')
param setRBACPermissions bool

var deployUserManagedIdentity = userManagedIdentities != null && userManagedIdentities != {}

resource websiteTemplate 'Microsoft.Web/sites@2022-03-01' = {
  name: name
  location: location
  kind: 'app'
  properties: {
    httpsOnly: true
    reserved: false
    serverFarmId: resourceId('Microsoft.Web/serverfarms', servicePlanName)
  }
  identity: {
    type: deployUserManagedIdentity ? 'SystemAssigned, UserAssigned' : 'SystemAssigned'
    userAssignedIdentities: deployUserManagedIdentity ? userManagedIdentities : null
  }
}

resource sites_ftp 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2022-09-01' = {
  parent: websiteTemplate
  name: 'ftp'
  properties: {
    allow: false
  }
}

resource sites_scm 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2022-09-01' = {
  parent: websiteTemplate
  name: 'scm'
  properties: {
    allow: false
  }
}

module webApiRBAC 'webApiRBAC.bicep' = {
  name: 'functionAppsRBAC-WebApi'
  params: {
    prereqsKeyVaultName: prereqsKeyVaultName
    prereqsKeyVaultResourceGroup: prereqsResourceGroup
    dataKeyVaultName: dataKeyVaultName
    dataKeyVaultResourceGroup: dataResourceGroup
    setRBACPermissions: setRBACPermissions
    webApiPrincipalId: websiteTemplate.identity.principalId
  }
  dependsOn: [
    sites_ftp
    sites_scm
  ]
}

resource websiteConfig 'Microsoft.Web/sites/config@2022-03-01' = {
  name: 'web'
  parent: websiteTemplate
  properties: {
    netFrameworkVersion: 'v6.0'
    ftpsState: 'Disabled'
    minTlsVersion: '1.2'
    appSettings: appSettings
  }
  dependsOn: [
    webApiRBAC
    ]
}


output principalId string = websiteTemplate.identity.principalId

@description('WebAPI site plan name.')
@minLength(1)
param name string

@description('Resource location.')
param location string

@description('Service plan name.')
param servicePlanName string

@description('Application settings')
param appSettings array

@description('Unique Front Door profile ID allowed to reach the main site.')
@minLength(1)
param frontDoorId string

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

var ingressRestrictions = [
  {
    name: 'FrontDoor'
    ipAddress: 'AzureFrontDoor.Backend'
    tag: 'ServiceTag'
    action: 'Allow'
    priority: 100
    headers: {
      'x-azure-fdid': [
        frontDoorId
      ]
    }
  }
]

// Newer site APIs revalidate EndToEndEncryption, which Free plans do not support.
resource websiteTemplate 'Microsoft.Web/sites@2022-03-01' = {
  name: name
  location: location
  kind: 'app'
  properties: {
    httpsOnly: true
    publicNetworkAccess: 'Enabled'
    reserved: false
    serverFarmId: resourceId('Microsoft.Web/serverfarms', servicePlanName)
    siteConfig: {
      minTlsVersion: '1.2'
      cors: {
        supportCredentials: true
      }
      ipSecurityRestrictions: ingressRestrictions
      ipSecurityRestrictionsDefaultAction: 'Deny'
      scmIpSecurityRestrictionsUseMain: false
    }
  }
  identity: {
    type: deployUserManagedIdentity ? 'SystemAssigned, UserAssigned' : 'SystemAssigned'
    userAssignedIdentities: deployUserManagedIdentity ? userManagedIdentities : null
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
}

resource sites_ftp 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2022-09-01' = {
  parent: websiteTemplate
  name: 'ftp'
  properties: {
    allow: false
  }
  dependsOn:[
    webApiRBAC
  ]
}

resource sites_scm 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2022-09-01' = {
  parent: websiteTemplate
  name: 'scm'
  properties: {
    allow: false
  }
  dependsOn:[
    sites_ftp
  ]
}

resource websiteConfig 'Microsoft.Web/sites/config@2023-12-01' = {
  name: 'web'
  parent: websiteTemplate
  properties: {
    netFrameworkVersion: 'v10.0'
    ftpsState: 'Disabled'
    minTlsVersion: '1.2'
    appSettings: appSettings
    ipSecurityRestrictions: ingressRestrictions
    ipSecurityRestrictionsDefaultAction: 'Deny'
    scmIpSecurityRestrictionsUseMain: false
  }
  dependsOn: [
    sites_scm
  ]
}


output principalId string = websiteTemplate.identity.principalId
output defaultHostName string = websiteTemplate.properties.defaultHostName
output outboundIpAddresses string = websiteTemplate.properties.outboundIpAddresses
output possibleOutboundIpAddresses string = websiteTemplate.properties.possibleOutboundIpAddresses

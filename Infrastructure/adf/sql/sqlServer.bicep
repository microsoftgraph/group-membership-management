@minLength(2)
@maxLength(3)
@description('Enter an abbreviation for the solution.')
param solutionAbbreviation string

@minLength(2)
@maxLength(6)
@description('Enter an abbreviation for the environment.')
param environmentAbbreviation string

@description('Resource location.')
param location string

@description('Tenant Id.')
param tenantId string

@description('Name of SQL Server')
param sqlServerName string

@description('Name of SQL Database')
param sqlDatabaseName string

@description('Administrator user name')
param sqlAdminUserName string

@secure()
@description('Administrator password')
param sqlAdminPassword string

@description('Administrators Azure AD Group Object Id')
param sqlAdministratorsGroupId string

@description('Administrators Azure AD Group Name')
param sqlAdministratorsGroupName string

@description('Key vault name.')
param keyVaultName string

var logAnalyticsName = '${solutionAbbreviation}-data-${environmentAbbreviation}'
var sqlServerUrl = 'Server=tcp:${sqlServerName}${environment().suffixes.sqlServerHostname},1433;'
var sqlServerDataBaseName = 'Initial Catalog=${sqlDatabaseName};'
var sqlServerAdditionalSettings = 'MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=90;'

resource sqlServer 'Microsoft.Sql/servers@2022-11-01-preview' = {
  name: sqlServerName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    administratorLogin: sqlAdminUserName
    administratorLoginPassword: sqlAdminPassword
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'Group'
      login: sqlAdministratorsGroupName
      sid: sqlAdministratorsGroupId
      tenantId: tenantId
    }
  }

  resource sqlServerFirewall 'firewallRules@2022-11-01-preview' = {
    name: 'AllowAllWindowsAzureIps'
    properties: {
      startIpAddress: '0.0.0.0'
      endIpAddress: '0.0.0.0'
    }
  }

  resource masterDataBase 'databases@2022-11-01-preview' = {
    location: location
    name: 'master'
    properties: {}
  }

  resource auditingSettings 'auditingSettings@2022-11-01-preview' = {
    name: 'default'
    properties: {
      state: 'Enabled'
      isAzureMonitorTargetEnabled: true
    }
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2021-02-01-preview' = {
  name: sqlDatabaseName
  parent: sqlServer
  location: location
  sku: {
    name: 'Basic'
    tier: 'Basic'
    family: ''
    capacity: 0
  }
}

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' existing = {
  name: logAnalyticsName
}

resource diagnosticSettings 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  scope: sqlServer::masterDataBase
  name: 'diagnosticSettings'
  properties: {
    workspaceId: logAnalytics.id
    logs: [
      {
        category: 'SQLSecurityAuditEvents'
        enabled: true
        retentionPolicy: {
          days: 0
          enabled: false
        }
      }
    ]
  }
  dependsOn: [
    sqlServer
  ]
}

module secureKeyvaultSecrets 'keyVaultSecretsSecure.bicep' = {
  name: 'secureKeyvaultSecrets'
  params: {
    keyVaultName: keyVaultName
    keyVaultSecrets: {
      secrets: [
        {
          name: 'sqlAdminUserName'
          value: sqlAdminUserName
        }
        {
          name: 'sqlAdminPassword'
          value: sqlAdminPassword
        }
        {
          name: 'sqlServerBasicConnectionString'
          value: '${sqlServerUrl}${sqlServerDataBaseName}${sqlServerAdditionalSettings}'
        }
      ]
    }
  }
}

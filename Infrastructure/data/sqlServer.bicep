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

@description('SQL SKU Name')
param sqlSkuName string

@description('SQL SKU Tier')
param sqlSkuTier string

@description('SQL SKU Family')
param sqlSkuFamily string

@description('SQL SKU Capacity')
param sqlSkuCapacity int

@description('Administrators Azure AD Group Object Id')
param sqlAdministratorsGroupId string

@description('Administrators Azure AD Group Name')
param sqlAdministratorsGroupName string

@description('Administrator user name')
param sqlAdminUserName string

@secure()
@description('Administrator password')
param sqlAdminPassword string

@description('Read Only SQL Administrator user name')
param readOnlySqlAdminUserName string

@secure()
@description('Read Only SQL Administrator password')
param readOnlySqlAdminPassword string

var dataKeyVaultName = '${solutionAbbreviation}-data-${environmentAbbreviation}'
var sqlServerName = '${solutionAbbreviation}-data-${environmentAbbreviation}'
var sqlDatabaseName = '${solutionAbbreviation}-data-${environmentAbbreviation}'
var logAnalyticsName = '${solutionAbbreviation}-data-${environmentAbbreviation}'
var sqlServerUrl = 'Server=tcp:${solutionAbbreviation}-data-${environmentAbbreviation}${environment().suffixes.sqlServerHostname},1433;'
var sqlServerDataBaseName = 'Initial Catalog=${solutionAbbreviation}-data-${environmentAbbreviation};'
var sqlServerLoginInfo = 'Persist Security Info=False;User ID=${sqlAdminUserName};Password=${sqlAdminPassword};'
var sqlServerAdditionalSettings = 'MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=90;'
var jobsSqlDataBaseName = 'Initial Catalog=${solutionAbbreviation}-data-${environmentAbbreviation}-jobs;'

resource sqlServer 'Microsoft.Sql/servers@2021-02-01-preview' = {
  name: sqlServerName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    minimalTlsVersion: '1.2'
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

  resource sqlServerFirewall 'firewallRules@2021-02-01-preview' = {
    name: 'AllowAllWindowsAzureIps'
    properties: {
      startIpAddress: '0.0.0.0'
      endIpAddress: '0.0.0.0'
    }
  }

  resource masterDataBase 'databases@2021-02-01-preview' = {
    location: location
    name: 'master'
    properties: {}
  }

  resource auditingSettings 'auditingSettings@2017-03-01-preview' = {
    name: 'default'
    properties: {
      state: 'Enabled'
      isAzureMonitorTargetEnabled: true
    }
  }
}

resource primaryDatabase 'Microsoft.Sql/servers/databases@2021-02-01-preview' = {
  name: '${sqlDatabaseName}-jobs'
  parent: sqlServer
  location: location
  properties: {
    autoPauseDelay: -1
  }
  sku: {
    name: sqlSkuName
    tier: sqlSkuTier
    family: sqlSkuFamily
    capacity: sqlSkuCapacity
  }
}

resource replicaSqlServer 'Microsoft.Sql/servers@2021-11-01-preview' = {
  name: '${sqlServer.name}-R'
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  dependsOn:[
    sqlServer
    primaryDatabase
  ]
  properties: {
    minimalTlsVersion: '1.2'
    administratorLogin: readOnlySqlAdminUserName
    administratorLoginPassword: readOnlySqlAdminPassword
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'Group'
      login: sqlAdministratorsGroupName
      sid: sqlAdministratorsGroupId
      tenantId: tenant().tenantId
    }
  }
}

resource readReplicaDb 'Microsoft.Sql/servers/databases@2021-11-01-preview' = {
  name: '${primaryDatabase.name}-R'
  parent: replicaSqlServer
  location: location
  dependsOn:[
    primaryDatabase
  ]
  properties: {
    autoPauseDelay: -1
    createMode: 'OnlineSecondary'
    maxSizeBytes: -1
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    catalogCollation: 'SQL_Latin1_General_CP1_CI_AS'
    zoneRedundant: false
    licenseType: 'LicenseIncluded'
    readScale: 'Disabled'
    secondaryType: 'Geo'
    isLedgerOn: false
    sourceDatabaseId: primaryDatabase.id
  }
}

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2021-06-01' existing = {
  name: logAnalyticsName
}

resource diagnosticSettings 'Microsoft.Insights/diagnosticSettings@2017-05-01-preview' = {
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

resource longTermBackup 'Microsoft.Sql/servers/databases/backupLongTermRetentionPolicies@2022-05-01-preview' = {
  parent: primaryDatabase
  name: 'default'
  properties: {
    weeklyRetention: 'P1W'
    monthlyRetention: 'P1M'
    yearlyRetention: 'P1Y'
    weekOfYear: 1
  }
}

module secureKeyvaultSecrets 'keyVaultSecretsSecure.bicep' = {
  name: 'secureKeyvaultSecrets'
  params: {
    keyVaultName: dataKeyVaultName
    keyVaultSecrets: {
      secrets: [
        {
          name: 'sqlServerAdminUserName'
          value: sqlAdminUserName
        }
        {
          name: 'sqlServerAdminPassword'
          value: sqlAdminPassword
        }
        {
          name: 'readOnlySqlAdminUserName'
          value: readOnlySqlAdminUserName
        }
        {
          name: 'readOnlySqlAdminPassword'
          value: readOnlySqlAdminPassword
        }
        {
          name: 'sqlServerManagedIdentity'
          value: sqlServer.identity.principalId
        }
        {
          name: 'sqlDatabaseConnectionString'
          value: '${sqlServerUrl}${jobsSqlDataBaseName}${sqlServerLoginInfo}${sqlServerAdditionalSettings}'
        }
        {
          name: 'sqlServerConnectionString'
          value: '${sqlServerUrl}${sqlServerDataBaseName}${sqlServerLoginInfo}${sqlServerAdditionalSettings}'
        }
        {
          name: 'sqlServerBasicConnectionString'
          value: '${sqlServerUrl}${sqlServerDataBaseName}${sqlServerAdditionalSettings}'
        }
        {
          name: 'sqlServerMSIConnectionString'
          value: '${sqlServerUrl}${sqlServerDataBaseName}Authentication=Active Directory Default;TrustServerCertificate=True;Encrypt=True;'
        }
      ]
    }
  }
}

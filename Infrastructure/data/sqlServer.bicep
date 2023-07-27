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

var dataKeyVaultName = '${solutionAbbreviation}-data-${environmentAbbreviation}'
var sqlServerName = '${solutionAbbreviation}-data-${environmentAbbreviation}'
var primaryDatabaseName = '${sqlDatabaseName}-jobs'
var replicaSqlServerName = '${sqlServerName}-R'
var replicaSqlDatabaseName = '${primaryDatabaseName}-R'
var sqlDatabaseName = '${solutionAbbreviation}-data-${environmentAbbreviation}'
var logAnalyticsName = '${solutionAbbreviation}-data-${environmentAbbreviation}'
var sqlServerUrl = 'Server=tcp:${sqlServerName}${environment().suffixes.sqlServerHostname},1433;'
var sqlServerDataBaseName = 'Initial Catalog=${sqlServerName};'
var sqlServerAdditionalSettings = 'MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=90;'
var jobsSqlDataBaseName = 'Initial Catalog=${solutionAbbreviation}-data-${environmentAbbreviation}-jobs;'
var replicaJobsSqlDataBaseName = 'Initial Catalog=${solutionAbbreviation}-data-${environmentAbbreviation}-jobs-R;'
var replicaConnectionString = 'Server=tcp:${replicaSqlServerName}${environment().suffixes.sqlServerHostname},1433;Initial Catalog=${replicaSqlDatabaseName};${sqlServerAdditionalSettings}'
var jobsMSIConnectionString = 'Server=tcp:${sqlServerName}${environment().suffixes.sqlServerHostname},1433;${jobsSqlDataBaseName}Authentication=Active Directory Default;'
var replicaJobsMSIConnectionString = 'Server=tcp:${replicaSqlServerName}${environment().suffixes.sqlServerHostname},1433;${replicaJobsSqlDataBaseName}Authentication=Active Directory Default;'

resource sqlServer 'Microsoft.Sql/servers@2021-02-01-preview' = {
  name: sqlServerName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    minimalTlsVersion: '1.2'
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'Group'
      login: sqlAdministratorsGroupName
      sid: sqlAdministratorsGroupId
      tenantId: tenantId
      azureADOnlyAuthentication: true
    }
  }

  resource aadAuthentication 'azureADOnlyAuthentications@2022-11-01-preview' = {
    name: 'Default'
    properties: {
      azureADOnlyAuthentication: true
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
  name: primaryDatabaseName
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

resource SqlDatabase_DeleteLock 'Microsoft.Authorization/locks@2020-05-01' = {
  scope: primaryDatabase
  name: 'Do Not Delete'
  properties: {
    level: 'CanNotDelete'
  }
}

resource replicaSqlServer 'Microsoft.Sql/servers@2021-11-01-preview' = {
  name: replicaSqlServerName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  dependsOn: [
    primaryDatabase
  ]
  properties: {
    minimalTlsVersion: '1.2'
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'Group'
      login: sqlAdministratorsGroupName
      sid: sqlAdministratorsGroupId
      tenantId: tenant().tenantId
      azureADOnlyAuthentication: true
    }
  }

  resource aadAuthentication 'azureADOnlyAuthentications@2022-11-01-preview' = {
    name: 'Default'
    properties: {
      azureADOnlyAuthentication: true
    }
  }

  resource sqlServerFirewall 'firewallRules@2021-02-01-preview' = {
    name: 'AllowAllWindowsAzureIpsReplica'
    properties: {
      startIpAddress: '0.0.0.0'
      endIpAddress: '0.0.0.0'
    }
  }
}

resource readReplicaDb 'Microsoft.Sql/servers/databases@2021-11-01-preview' = {
  name: replicaSqlDatabaseName
  parent: replicaSqlServer
  location: location
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

resource RSqlDatabase_DeleteLock 'Microsoft.Authorization/locks@2020-05-01' = {
  scope: readReplicaDb
  name: 'Do Not Delete'
  properties: {
    level: 'CanNotDelete'
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
          name: 'sqlServerManagedIdentity'
          value: sqlServer.identity.principalId
        }
        {
          name: 'sqlDatabaseConnectionString'
          value: '${sqlServerUrl}${jobsSqlDataBaseName}${sqlServerAdditionalSettings}'
        }
        {
          name: 'sqlServerConnectionString'
          value: '${sqlServerUrl}${sqlServerDataBaseName}${sqlServerAdditionalSettings}'
        }
        {
          name: 'sqlServerBasicConnectionString'
          value: '${sqlServerUrl}${sqlServerDataBaseName}${sqlServerAdditionalSettings}'
        }
        {
          name: 'sqlServerMSIConnectionString'
          value: '${sqlServerUrl}${sqlServerDataBaseName}Authentication=Active Directory Default;TrustServerCertificate=True;Encrypt=True;'
        }
        {
          name: 'replicaSqlServerMSIConnectionString'
          value: '${sqlServerUrl}${sqlServerDataBaseName}Authentication=Active Directory Default;TrustServerCertificate=True;Encrypt=True;'
        }
        {
          name: 'sqlServerName'
          value: '${sqlServerName}${environment().suffixes.sqlServerHostname}'
        }
        {
          name: 'sqlServerDataBaseName'
          value: sqlServerName
        }
        {
          name: 'replicaSqlServerName'
          value: replicaSqlServerName
        }
        {
          name: 'replicaSqlDataBaseName'
          value: replicaSqlDatabaseName
        }
        {
          name: 'replicaSqlServerConnectionString'
          value: replicaConnectionString
        }
        {
          name: 'jobsMSIConnectionString'
          value: jobsMSIConnectionString
        }
        {
          name: 'replicaJobsMSIConnectionString'
          value: replicaJobsMSIConnectionString
        }
      ]
    }
  }
}

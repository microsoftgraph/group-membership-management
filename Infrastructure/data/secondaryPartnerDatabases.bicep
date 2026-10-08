// Failover-group partner databases live on the secondary SQL server. They are separate from the
// dedicated SyncJobs read replica, which lives on the secondary read-replica server.

@description('Name of the secondary SQL logical server that partners the primary in the failover group.')
param secondarySqlServerName string

@description('Name of the SyncJobs database in the failover group.')
param primaryDatabaseName string

@description('Name of the ADF database in the failover group.')
param adfDatabaseName string

@description('Resource location for the secondary region.')
param secondaryLocation string

@description('Whether this environment requires delete locks.')
param isProduction bool

@description('Resource id of the primary SyncJobs database.')
param sourceDatabaseId string

@description('Resource id of the primary ADF database.')
param adfSourceDatabaseId string

@description('SyncJobs SQL SKU name.')
param sqlSkuName string

@description('SyncJobs SQL SKU tier.')
param sqlSkuTier string

@description('SyncJobs SQL SKU family.')
param sqlSkuFamily string

@description('SyncJobs SQL SKU capacity.')
param sqlSkuCapacity int

@description('ADF SQL SKU name.')
param adfDBSqlSkuName string

@description('ADF SQL SKU tier.')
param adfDBSqlSkuTier string

@description('ADF SQL SKU family.')
param adfDBSqlSkuFamily string

@description('ADF SQL SKU capacity.')
param adfDBSqlSkuCapacity int

resource secondarySqlServer 'Microsoft.Sql/servers@2021-11-01-preview' existing = {
  name: secondarySqlServerName
}

// The failover group creates these databases implicitly. Redeclaring them after the failover
// group exists keeps their SKU and auto-pause properties under source control.
resource secondaryPartnerDatabase 'Microsoft.Sql/servers/databases@2021-11-01-preview' = {
  name: primaryDatabaseName
  parent: secondarySqlServer
  location: secondaryLocation
  properties: {
    autoPauseDelay: -1
    createMode: 'OnlineSecondary'
    secondaryType: 'Geo'
    sourceDatabaseId: sourceDatabaseId
  }
  sku: {
    name: sqlSkuName
    tier: sqlSkuTier
    family: sqlSkuFamily
    capacity: sqlSkuCapacity
  }
}

resource secondaryAdfPartnerDatabase 'Microsoft.Sql/servers/databases@2021-11-01-preview' = {
  name: adfDatabaseName
  parent: secondarySqlServer
  location: secondaryLocation
  properties: {
    autoPauseDelay: -1
    createMode: 'OnlineSecondary'
    secondaryType: 'Geo'
    sourceDatabaseId: adfSourceDatabaseId
  }
  sku: {
    name: adfDBSqlSkuName
    tier: adfDBSqlSkuTier
    family: adfDBSqlSkuFamily
    capacity: adfDBSqlSkuCapacity
  }
}

resource secondaryPartnerLongTermBackup 'Microsoft.Sql/servers/databases/backupLongTermRetentionPolicies@2022-05-01-preview' = {
  parent: secondaryPartnerDatabase
  name: 'default'
  properties: {
    weeklyRetention: 'P1W'
    monthlyRetention: 'P1M'
    yearlyRetention: 'P1Y'
    weekOfYear: 1
  }
}

resource secondaryPartnerDatabaseDeleteLock 'Microsoft.Authorization/locks@2020-05-01' = if (isProduction) {
  scope: secondaryPartnerDatabase
  name: 'Do Not Delete'
  properties: {
    level: 'CanNotDelete'
  }
}

resource secondaryAdfPartnerDatabaseDeleteLock 'Microsoft.Authorization/locks@2020-05-01' = if (isProduction) {
  scope: secondaryAdfPartnerDatabase
  name: 'Do Not Delete'
  properties: {
    level: 'CanNotDelete'
  }
}

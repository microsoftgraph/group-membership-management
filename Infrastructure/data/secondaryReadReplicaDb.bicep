// Secondary region read replica database: a sibling geo replica fanning out directly from the
// primary database, because Azure does not support creating a replica of a replica.
//
// A module because it is created in the secondary resource group while its source database id
// references the primary resource group.

@description('Name of the secondary region read replica server.')
param secondaryReplicaSqlServerName string

@description('Name of the secondary SQL logical server that partners the primary in the failover group.')
param secondarySqlServerName string

@description('Name of the primary database, which is also the name of the failover group partner database.')
param primaryDatabaseName string

@description('Name of the read replica database.')
param replicaSqlDatabaseName string

@description('Resource location for the secondary region.')
param secondaryLocation string

@description('Whether or not this environment is a production environment that needs to have authorization locks.')
param isProduction bool

@description('Resource id of the primary database this replica is seeded from.')
param sourceDatabaseId string

@description('SQL SKU Name')
param sqlSkuName string

@description('SQL SKU Tier')
param sqlSkuTier string

@description('SQL SKU Family')
param sqlSkuFamily string

@description('SQL SKU Capacity')
param sqlSkuCapacity int

resource secondaryReplicaSqlServer 'Microsoft.Sql/servers@2021-11-01-preview' existing = {
  name: secondaryReplicaSqlServerName
}

resource secondaryReadReplicaDb 'Microsoft.Sql/servers/databases@2021-11-01-preview' = {
  name: replicaSqlDatabaseName
  parent: secondaryReplicaSqlServer
  location: secondaryLocation
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
    sourceDatabaseId: sourceDatabaseId
  }
  sku: {
    name: sqlSkuName
    tier: sqlSkuTier
    family: sqlSkuFamily
    capacity: sqlSkuCapacity
  }
}

resource SecondaryReadReplicaDb_DeleteLock 'Microsoft.Authorization/locks@2020-05-01' = if(isProduction) {
  scope: secondaryReadReplicaDb
  name: 'Do Not Delete'
  properties: {
    level: 'CanNotDelete'
  }
}

// The failover group creates the partner database rather than it being declared, so it is
// referenced as existing in order to be locked. It carries the primary database's name.
resource secondaryPartnerDatabase 'Microsoft.Sql/servers/databases@2021-11-01-preview' existing = {
  name: '${secondarySqlServerName}/${primaryDatabaseName}'
}

resource SecondaryPartnerDatabase_DeleteLock 'Microsoft.Authorization/locks@2020-05-01' = if(isProduction) {
  scope: secondaryPartnerDatabase
  name: 'Do Not Delete'
  properties: {
    level: 'CanNotDelete'
  }
}

output replicaDatabaseId string = secondaryReadReplicaDb.id

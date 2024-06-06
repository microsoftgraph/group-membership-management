@description('Resource location.')
param location string

@description('Name of SQL Server')
param sqlServerName string

@description('Name of SQL Database')
param sqlDatabaseName string

resource sqlServer 'Microsoft.Sql/servers@2022-11-01-preview' existing = {
  name: sqlServerName
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

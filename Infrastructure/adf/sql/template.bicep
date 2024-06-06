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

@description('Name of storage account that stores adf data')
param storageAccountName string = '${solutionAbbreviation}${environmentAbbreviation}adf'

@description('Enter storage account sku.')
@allowed([
  'Standard_LRS'
  'Standard_GRS'
  'Standard_ZRS'
  'Premium_LRS'
])
param storageAccountSku string = 'Standard_LRS'

@description('Name of blob container')
param storageAccountContainerName string = 'csvcontainer'

@description('Name of SQL Server')
param sqlServerName string = '${solutionAbbreviation}-data-${environmentAbbreviation}'

@description('Name of SQL Server')
param sqlDataBaseName string = '${solutionAbbreviation}-data-${environmentAbbreviation}-destination'

var dataKeyVaultName = '${solutionAbbreviation}-data-${environmentAbbreviation}'

module sqlServer 'sqlServer.bicep' =  {
  name: 'sqlServerTemplate'
  params: {
    location: location
    sqlServerName: sqlServerName
    sqlDatabaseName: sqlDataBaseName
  }
}

module storageAccountTemplate 'storageAccount.bicep' = {
  name: 'storageAccountTemplate'
  params: {
    storageAccountName: storageAccountName
    containerName: storageAccountContainerName
    sku: storageAccountSku
    keyVaultName: dataKeyVaultName
    location: location
  }
}

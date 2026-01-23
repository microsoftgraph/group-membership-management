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

@description('Name of SQL database name')
param sqlDatabaseName string

@description('Name of Azure Data Factory')
param azureDataFactoryName string = '${solutionAbbreviation}-data-${environmentAbbreviation}-adf'

@description('Resource name suffix')
param resourceSuffix string = 'demo'

@description('Function authentication app client id.')
param functionAuthAppClientId string

var dataKeyVaultName = '${solutionAbbreviation}-data-${environmentAbbreviation}'

resource dataKeyVault 'Microsoft.KeyVault/vaults@2019-09-01' existing = {
	name: dataKeyVaultName
	scope: resourceGroup()
}

module azureDataFactoryTemplate 'azureDataFactory.bicep' = {
	name: 'azureDataFactoryTemplate'
	params: {
		resourceSuffix: toLower(resourceSuffix)
		factoryName: azureDataFactoryName
		environmentAbbreviation: environmentAbbreviation
		location: location
		sqlServerName: sqlServerName
		sqlDatabaseName: sqlDatabaseName
		azureUserReaderUrl: dataKeyVault.getSecret('azureUserReaderUrl')
		azureUserReaderFunctionKey: dataKeyVault.getSecret('azureUserReaderKey')
		storageAccountName: dataKeyVault.getSecret('adfStorageAccountName')
		functionAuthAppClientId: functionAuthAppClientId
	}
}

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

@description('Primary data resource group containing the shared Key Vault and HR database.')
param dataResourceGroupName string = resourceGroup().name
param logAnalyticsName string = dataResourceGroupName

@description('Name of SQL Server')
param sqlServerName string

@description('Name of SQL database name')
param sqlDatabaseName string

@description('Name of Azure Data Factory')
param azureDataFactoryName string = '${solutionAbbreviation}-data-${environmentAbbreviation}-adf'

@description('When enabled, connect this ADF instance through the shared SQL failover-group listener.')
param enableMultiRegion bool = false

@description('Key Vault secret containing this regional factory storage account name.')
param storageAccountSecretName string = 'adfStorageAccountName'

@description('Resource name suffix')
param resourceSuffix string = 'demo'

@description('Function authentication app client id.')
param functionAuthAppClientId string

@description('Enable function authentication.')
param enableFunctionAuthentication bool = false

@description('Number of most recent tables to retain in each of the users, mappings and agents schemas. Cleanup of older users and mappings tables is attempted after all non-cleanup activities succeed, and of older agents tables after the agents load completes.')
@minValue(2)
param tablesToRetain int = 30

var dataKeyVaultName = '${solutionAbbreviation}-data-${environmentAbbreviation}'

resource dataKeyVault 'Microsoft.KeyVault/vaults@2019-09-01' existing = {
	name: dataKeyVaultName
	scope: resourceGroup(dataResourceGroupName)
}

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2021-06-01' existing = {
	name: logAnalyticsName
	scope: resourceGroup(dataResourceGroupName)
}

module azureDataFactoryTemplate 'azureDataFactory.bicep' = {
	name: 'azureDataFactoryTemplate'
	params: {
		resourceSuffix: toLower(resourceSuffix)
		factoryName: azureDataFactoryName
		enableMultiRegion: enableMultiRegion
		environmentAbbreviation: environmentAbbreviation
		location: location
		sqlServerName: sqlServerName
		sqlDatabaseName: sqlDatabaseName
		azureUserReaderUrl: dataKeyVault.getSecret('azureUserReaderUrl')
		azureUserReaderFunctionKey: dataKeyVault.getSecret('azureUserReaderKey')
		agentReaderUrl: dataKeyVault.getSecret('agentReaderUrl')
		agentReaderFunctionKey: dataKeyVault.getSecret('agentReaderKey')
		storageAccountName: dataKeyVault.getSecret(storageAccountSecretName)
		functionAuthAppClientId: functionAuthAppClientId
		enableFunctionAuthentication: enableFunctionAuthentication
		tablesToRetain: tablesToRetain
		logAnalyticsWorkspaceId: logAnalyticsWorkspace.id
	}
}

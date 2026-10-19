param location string
param environmentAbbreviation string
param solutionAbbreviation string
param tenantId string
param functionAuthAppClientId string
param enableFunctionAuthentication bool = false

@description('When true, deploy paired regional HR data factories and route them through the SQL failover-group listener.')
param enableMultiRegion bool = false
param secondaryLocation string = ''

@description('Number of most recent tables to retain in each of the users, mappings and agents schemas. Cleanup of older users and mappings tables is attempted after all non-cleanup activities succeed, and of older agents tables after the agents load completes.')
@minValue(2)
param tablesToRetain int = 30

var secondaryRegionToken = 'sec'
var primaryDataResourceGroupName = '${solutionAbbreviation}-data-${environmentAbbreviation}'
var secondaryDataResourceGroupName = '${primaryDataResourceGroupName}-${secondaryRegionToken}'
var primarySqlServerName = primaryDataResourceGroupName
var sqlServerName = enableMultiRegion ? '${primarySqlServerName}-fog' : primarySqlServerName
var sqlDataBaseName = '${solutionAbbreviation}-data-${environmentAbbreviation}-adf'
var primaryFactoryName = '${solutionAbbreviation}-data-${environmentAbbreviation}-adf'
var secondaryFactoryName = '${primaryFactoryName}-${secondaryRegionToken}'
var storageAccountName = '${solutionAbbreviation}${environmentAbbreviation}adf'
var secondaryStorageAccountName = toLower('${storageAccountName}${secondaryRegionToken}')
var storageContainerName = 'csvcontainer'
var storageSku = 'Standard_LRS'
var primaryStorageAccountSecretName = 'adfStorageAccountName'
var secondaryStorageAccountSecretName = 'adfStorageAccountNameSec'

@description('Name of the shared Log Analytics workspace.')
param logAnalyticsName string = '${solutionAbbreviation}-data-${environmentAbbreviation}'

module storageForHRData '../Infrastructure/adf/storage/storageAccount.bicep' = {
  name: 'storageForHRDataTemplate'
  params: {
    location: location
    storageAccountName: storageAccountName
    containerName: storageContainerName
    sku: storageSku
    keyVaultName: primaryDataResourceGroupName
    keyVaultResourceGroupName: primaryDataResourceGroupName
    storageAccountSecretName: primaryStorageAccountSecretName
  }
}

module secondaryStorageForHRData '../Infrastructure/adf/storage/storageAccount.bicep' = if (enableMultiRegion) {
  name: 'secondaryStorageForHRDataTemplate'
  scope: resourceGroup(secondaryDataResourceGroupName)
  params: {
    location: secondaryLocation
    storageAccountName: secondaryStorageAccountName
    containerName: storageContainerName
    sku: storageSku
    keyVaultName: primaryDataResourceGroupName
    keyVaultResourceGroupName: primaryDataResourceGroupName
    storageAccountSecretName: secondaryStorageAccountSecretName
  }
}

module primaryAdfForHRData '../Infrastructure/adf/pipeline/template.bicep' = {
  name: 'primaryAdfForHRDataTemplate'
  params: {
    dataResourceGroupName: primaryDataResourceGroupName
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    sqlServerName: sqlServerName
    sqlDatabaseName: sqlDataBaseName
    azureDataFactoryName: primaryFactoryName
    enableMultiRegion: enableMultiRegion
    storageAccountSecretName: primaryStorageAccountSecretName
    functionAuthAppClientId: functionAuthAppClientId
    enableFunctionAuthentication: enableFunctionAuthentication
    tablesToRetain: tablesToRetain
    logAnalyticsName: logAnalyticsName
  }
  dependsOn: [
    storageForHRData
  ]
}

module secondaryAdfForHRData '../Infrastructure/adf/pipeline/template.bicep' = if (enableMultiRegion) {
  name: 'secondaryAdfForHRDataTemplate'
  scope: resourceGroup(secondaryDataResourceGroupName)
  params: {
    dataResourceGroupName: primaryDataResourceGroupName
    location: secondaryLocation
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    sqlServerName: sqlServerName
    sqlDatabaseName: sqlDataBaseName
    azureDataFactoryName: secondaryFactoryName
    enableMultiRegion: enableMultiRegion
    storageAccountSecretName: secondaryStorageAccountSecretName
    functionAuthAppClientId: functionAuthAppClientId
    enableFunctionAuthentication: enableFunctionAuthentication
    tablesToRetain: tablesToRetain
    logAnalyticsName: logAnalyticsName
  }
  dependsOn: [
    secondaryStorageForHRData
  ]
}

@description('Enter an abbreviation for the environment.')
@minLength(2)
@maxLength(6)
param environmentAbbreviation string

@description('Enter an abbreviation for the solution.')
@minLength(2)
@maxLength(3)
param solutionAbbreviation string = 'gmm'

@description('Enter tenant Id.')
param tenantId string

@description('Enter storage account name.')
param storageAccountName string

param storageAccountSku string = 'Standard_LRS'

@description('Resource location.')
param location string

var keyVaultName = '${solutionAbbreviation}-data-${environmentAbbreviation}'
var prodStorageAccountName = substring('nps${solutionAbbreviation}${environmentAbbreviation}prod${uniqueString(resourceGroup().id)}',0,23)
var stagingStorageAccountName = substring('nps${solutionAbbreviation}${environmentAbbreviation}staging${uniqueString(resourceGroup().id)}',0,23)

module nonProdServiceStorageAccountProd 'storageAccount.bicep' = {
  name: 'npsProdstorageAccountTemplate'
  params: {
    name: prodStorageAccountName
    sku: storageAccountSku
    keyVaultName: keyVaultName
    location: location
    storageAccountConnectionStringSettingName: 'nonProdServiceStorageAccountProd'
  }
}

module nonProdServiceStorageAccountStaging 'storageAccount.bicep' = {
  name: 'npsStagingstorageAccountTemplate'
  params: {
    name: stagingStorageAccountName
    sku: storageAccountSku
    keyVaultName: keyVaultName
    location: location
    storageAccountConnectionStringSettingName: 'nonProdServiceStorageAccountStaging'
  }
}

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

@description('Instance identifier')
@allowed([
  's1'
  'm1'
  'l1'
  'o1'
])
param instanceIdentifier string

var keyVaultName = '${solutionAbbreviation}-data-${environmentAbbreviation}'
var prodStorageAccountName = substring('ms${solutionAbbreviation}${environmentAbbreviation}prod${instanceIdentifier}${uniqueString(resourceGroup().id)}',0,23)

module graphUpdaterStorageAccountProd 'storageAccount.bicep' = {
  name: 'ms${instanceIdentifier}ProdstorageAccountTemplate'
  params: {
    name: prodStorageAccountName
    sku: storageAccountSku
    keyVaultName: keyVaultName
    location: location
    storageAccountConnectionStringSettingName: 'messageSplitter${instanceIdentifier}StorageAccountProd'
  }
}

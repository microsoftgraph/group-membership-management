@description('Enter an abbreviation for the environment.')
@minLength(2)
@maxLength(6)
param environmentAbbreviation string

@description('Enter an abbreviation for the solution.')
@minLength(2)
@maxLength(3)
param solutionAbbreviation string = 'gmm'

param storageAccountSku string = 'Standard_LRS'

@description('Resource location.')
param location string

@description('Instance identifier')
@allowed([
  'small'
  'medium'
  'large'
  'onboarding'
])
param instanceIdentifier string

var keyVaultName = '${solutionAbbreviation}-data-${environmentAbbreviation}'
var prodStorageAccountName = substring('gu${solutionAbbreviation}${environmentAbbreviation}prod${instanceIdentifier}${uniqueString(resourceGroup().id)}',0,23)

module graphUpdaterStorageAccountProd 'storageAccount.bicep' = {
  name: 'gu${instanceIdentifier}ProdstorageAccountTemplate'
  params: {
    name: prodStorageAccountName
    sku: storageAccountSku
    keyVaultName: keyVaultName
    location: location
    storageAccountConnectionStringSettingName: 'graphUpdater${instanceIdentifier}StorageAccountProd'
  }
}

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

@description('Resource location.')
param location string

@description('SqlMembershipObtainer function internal storage account sku.')
param storageAccountSku string = 'Standard_LRS'

/* This creates the internal storage accounts used by SqlMemberhipObtainer function */

var dataKeyVaultName = '${solutionAbbreviation}-data-${environmentAbbreviation}'
var prodStorageAccountName = substring('sqlmo${solutionAbbreviation}${environmentAbbreviation}prod${uniqueString(resourceGroup().id)}',0,23)

module smoStorageAccountProd 'storageAccount.bicep' = {
  name: 'smoProdstorageAccountTemplate'
  params: {
    name: prodStorageAccountName
    sku: storageAccountSku
    keyVaultName: dataKeyVaultName
    location: location
    sqlMembershipObtainerStorageAccountName: 'sqlMembershipObtainerStorageAccountNameProd'
    storageAccountConnectionStringSettingName: 'sqlMembershipObtainerStorageAccountProd'
  }
}

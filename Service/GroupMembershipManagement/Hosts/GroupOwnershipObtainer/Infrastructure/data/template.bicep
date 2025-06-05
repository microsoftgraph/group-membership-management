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

@description('Classify the types of resources in prereqs resource group.')
param prereqsResourceGroupClassification string = 'prereqs'

var keyVaultName = '${solutionAbbreviation}-data-${environmentAbbreviation}'
var prodStorageAccountName = substring('goo${solutionAbbreviation}${environmentAbbreviation}prod${uniqueString(resourceGroup().id)}',0,23)

module groupOwnershipObtainerStorageAccountProd 'storageAccount.bicep' = {
  name: 'gooProdstorageAccountTemplate'
  params: {
    name: prodStorageAccountName
    sku: storageAccountSku
    keyVaultName: keyVaultName
    location: location
    storageAccountSettingName: 'groupOwnershipObtainerStorageAccountProd'
  }
}

var nspName = '${solutionAbbreviation}-nsp-${environmentAbbreviation}'
var prereqsResourceGroupName = '${solutionAbbreviation}-${prereqsResourceGroupClassification}-${environmentAbbreviation}'

module gooStorageAccountAssociationTemplate 'networkSecurityPerimeterResourceAssociation.bicep' = {
  name: 'gooStorageAccountAssociationTemplate'
  scope: resourceGroup(prereqsResourceGroupName)
  params: {
    nspName: nspName
    profileName: 'storageaccount'
    resourceId: groupOwnershipObtainerStorageAccountProd.outputs.storageAccountId
  }
}

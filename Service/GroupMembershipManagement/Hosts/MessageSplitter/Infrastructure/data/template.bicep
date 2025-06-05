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
  's1'
  'm1'
  'l1'
  'o1'
])
param instanceIdentifier string

@description('Classify the types of resources in prereqs resource group.')
param prereqsResourceGroupClassification string = 'prereqs'

var keyVaultName = '${solutionAbbreviation}-data-${environmentAbbreviation}'
var prodStorageAccountName = substring('ms${solutionAbbreviation}${environmentAbbreviation}prod${instanceIdentifier}${uniqueString(resourceGroup().id)}',0,23)

module messageSplitterStorageAccountProd 'storageAccount.bicep' = {
  name: 'ms${instanceIdentifier}ProdstorageAccountTemplate'
  params: {
    name: prodStorageAccountName
    sku: storageAccountSku
    keyVaultName: keyVaultName
    location: location
    storageAccountSettingName: 'messageSplitter${instanceIdentifier}StorageAccountProd'
  }
}

var nspName = '${solutionAbbreviation}-nsp-${environmentAbbreviation}'
var prereqsResourceGroupName = '${solutionAbbreviation}-${prereqsResourceGroupClassification}-${environmentAbbreviation}'

module msStorageAccountAssociationTemplate 'networkSecurityPerimeterResourceAssociation.bicep' = {
  name: 'msStorageAccountAssociationTemplate'
  scope: resourceGroup(prereqsResourceGroupName)
  params: {
    nspName: nspName
    profileName: 'storageaccount'
    resourceId: messageSplitterStorageAccountProd.outputs.storageAccountId
  }
}

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
var prodStorageAccountName = substring('tcmo${solutionAbbreviation}${environmentAbbreviation}prod${uniqueString(resourceGroup().id)}',0,23)

module teamsChannelMembershipObtainerStorageAccountProd 'storageAccount.bicep' = {
  name: 'tcmoProdstorageAccountTemplate'
  params: {
    name: prodStorageAccountName
    sku: storageAccountSku
    keyVaultName: keyVaultName
    location: location
    storageAccountSettingName: 'teamsChannelMembershipObtainerStorageAccountProd'
  }
}

var nspName = '${solutionAbbreviation}-nsp-${environmentAbbreviation}'
var prereqsResourceGroupName = '${solutionAbbreviation}-${prereqsResourceGroupClassification}-${environmentAbbreviation}'

module tcmoStorageAccountAssociationTemplate 'networkSecurityPerimeterResourceAssociation.bicep' = {
  name: 'tcmoStorageAccountAssociationTemplate'
  scope: resourceGroup(prereqsResourceGroupName)
  params: {
    nspName: nspName
    profileName: 'storageaccount'
    resourceId: teamsChannelMembershipObtainerStorageAccountProd.outputs.storageAccountId
  }
}

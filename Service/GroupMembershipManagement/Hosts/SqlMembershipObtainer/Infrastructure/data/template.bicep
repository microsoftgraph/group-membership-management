@description('Enter an abbreviation for the environment.')
@minLength(2)
@maxLength(6)
param environmentAbbreviation string

@description('Enter an abbreviation for the solution.')
@minLength(2)
@maxLength(3)
param solutionAbbreviation string = 'gmm'

@description('Resource location.')
param location string

@description('Classify the types of resources in prereqs resource group.')
param prereqsResourceGroupClassification string = 'prereqs'

@description('SqlMembershipObtainer function internal storage account sku.')
param storageAccountSku string = 'Standard_LRS'

/* This creates the internal storage accounts used by SqlMemberhipObtainer function */

var dataKeyVaultName = '${solutionAbbreviation}-data-${environmentAbbreviation}'
var prodStorageAccountName = substring('smo${solutionAbbreviation}${environmentAbbreviation}prod${uniqueString(resourceGroup().id)}',0,23)

module smoStorageAccountProd 'storageAccount.bicep' = {
  name: 'smoProdstorageAccountTemplate'
  params: {
    name: prodStorageAccountName
    sku: storageAccountSku
    keyVaultName: dataKeyVaultName
    location: location
    storageAccountSettingName: 'sqlMembershipObtainerStorageAccountProd'
    appPackageContainerSettingName: 'sqlMembershipObtainerAppPackageContainerProd'
    appPackageContainerName: 'app-package'
  }
}

var nspName = '${solutionAbbreviation}-nsp-${environmentAbbreviation}'
var prereqsResourceGroupName = '${solutionAbbreviation}-${prereqsResourceGroupClassification}-${environmentAbbreviation}'

module smoStorageAccountAssociationTemplate 'networkSecurityPerimeterResourceAssociation.bicep' = {
  name: 'smoStorageAccountAssociationTemplate'
  scope: resourceGroup(prereqsResourceGroupName)
  params: {
    nspName: nspName
    profileName: 'storageaccount'
    resourceId: smoStorageAccountProd.outputs.storageAccountId
  }
}

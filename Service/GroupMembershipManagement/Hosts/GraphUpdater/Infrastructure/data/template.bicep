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
  ''
  'small'
  'large'
])
param instanceIdentifier string = ''
var instanceSuffix = empty(instanceIdentifier) ? '' : '${instanceIdentifier}'

@description('Classify the types of resources in prereqs resource group.')
param prereqsResourceGroupClassification string = 'prereqs'

var keyVaultName = '${solutionAbbreviation}-data-${environmentAbbreviation}'
var prodStorageAccountName = substring('gu${solutionAbbreviation}${environmentAbbreviation}prod${instanceSuffix}${uniqueString(resourceGroup().id)}',0,23)

module graphUpdaterStorageAccountProd 'storageAccount.bicep' = {
  name: 'gu${instanceSuffix}ProdstorageAccountTemplate'
  params: {
    name: prodStorageAccountName
    sku: storageAccountSku
    keyVaultName: keyVaultName
    location: location
    storageAccountSettingName: 'graphUpdater${instanceSuffix}StorageAccountProd'
    appPackageContainerSettingName: 'graphUpdater${instanceSuffix}AppPackageContainerProd'
    appPackageContainerName: 'app-package'
  }
}

var nspName = '${solutionAbbreviation}-nsp-${environmentAbbreviation}'
var prereqsResourceGroupName = '${solutionAbbreviation}-${prereqsResourceGroupClassification}-${environmentAbbreviation}'

module guStorageAccountAssociationTemplate 'networkSecurityPerimeterResourceAssociation.bicep' = {
  name: 'gu${instanceSuffix}StorageAccountAssociationTemplate'
  scope: resourceGroup(prereqsResourceGroupName)
  params: {
    nspName: nspName
    profileName: 'storageaccount'
    resourceId: graphUpdaterStorageAccountProd.outputs.storageAccountId
  }
}

@description('Enter an abbreviation for the environment.')
@minLength(2)
@maxLength(6)
param environmentAbbreviation string

@description('Enter an abbreviation for the solution.')
@minLength(2)
@maxLength(3)
param solutionAbbreviation string = 'gmm'

var functionsStorageAccountName = take('fn${solutionAbbreviation}${environmentAbbreviation}${uniqueString(resourceGroup().id)}', 24)

resource functionsStorageAccount 'Microsoft.Storage/storageAccounts@2022-05-01' existing = {
  name: functionsStorageAccountName
}

resource blobServices 'Microsoft.Storage/storageAccounts/blobServices@2022-05-01' existing = {
  parent: functionsStorageAccount
  name: 'default'
}

resource appPackageContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2022-05-01' = {
  parent: blobServices
  name: 'notifier-app-package'
  properties: {
    publicAccess: 'None'
  }
}

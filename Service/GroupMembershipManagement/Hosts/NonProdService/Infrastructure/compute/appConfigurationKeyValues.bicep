@description('Enter an abbreviation for the solution.')
@minLength(2)
@maxLength(3)
param solutionAbbreviation string = 'gmm'

@description('Enter an abbreviation for the environment.')
@minLength(2)
@maxLength(6)
param environmentAbbreviation string

@description('The name of the app configuration resource.')
param appConfigurationName string = '${solutionAbbreviation}-appConfig-${environmentAbbreviation}'

@description('Array of Objects that contain the key name, value, tag and contentType')
param appConfigurationKeyData array

resource configurationStore 'Microsoft.AppConfiguration/configurationStores@2023-08-01-preview' existing = {
  name: appConfigurationName
}

resource configurationStoreKeyValues 'Microsoft.AppConfiguration/configurationStores/keyValues@2023-08-01-preview' = [for item in appConfigurationKeyData: {
  parent: configurationStore
  name: '${item.key}'
  properties: {
    value: item.value
    contentType: item.contentType
    tags: (contains(item, 'tag') ? item.tag : null)
  }
}]

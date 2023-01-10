@description('Specifies the name of the App Configuration store.')
param configStoreName string

@description('Specifies the Azure location where the app configuration store should be created.')
param location string

@description('Sku')
param appConfigurationSku string = 'Standard'

@description('Array of Objects that contain the key name, value, tag and contentType')
param appConfigurationKeyData array

resource configurationStore 'Microsoft.AppConfiguration/configurationStores@2020-06-01' = {
  name: configStoreName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {}
  sku: {
    name: appConfigurationSku
  }
}

resource configurationStoreKeyValues 'Microsoft.AppConfiguration/configurationStores/keyValues@2020-07-01-preview' = [for item in appConfigurationKeyData: {
  name: '${configStoreName}/${item.key}'
  properties: {
    value: item.value
    contentType: item.contentType
    tags: (contains(item, 'tag') ? item.tag : json('null'))
  }
  dependsOn: [
    configurationStore
  ]
}]

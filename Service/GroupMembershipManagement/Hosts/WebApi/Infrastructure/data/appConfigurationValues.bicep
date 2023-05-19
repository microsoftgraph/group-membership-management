@description('Specifies the name of the App Configuration store.')
param configStoreName string

@description('Array of Objects that contain the key name, value, tag and contentType')
param appConfigurationKeyData array

resource configurationStore 'Microsoft.AppConfiguration/configurationStores@2021-10-01-preview' existing = {
  name: configStoreName
}

resource configurationStoreKeyValues 'Microsoft.AppConfiguration/configurationStores/keyValues@2021-10-01-preview' = [for item in appConfigurationKeyData: {
  name: item.key
  parent: configurationStore
  properties: {
    value: item.value
    contentType: item.contentType
    tags: (contains(item, 'tag') ? item.tag : null)
  }
}]

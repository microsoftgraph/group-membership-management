@description('Specifies the name of the App Configuration store.')
param configStoreName string

@description('Specifies the Azure location where the app configuration store should be created.')
param location string

@description('Sku')
param appConfigurationSku string = 'Standard'

@description('Array of Objects that contain the key name, value, tag and contentType')
param appConfigurationKeyData array

@description('Array of feature flags objects. {id:"value", description:"description", enabled:true, createdBy:"value", createdDate:"data" }')
param featureFlags array

resource configurationStore 'Microsoft.AppConfiguration/configurationStores@2023-03-01' = {
  name: configStoreName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    disableLocalAuth: true
  }
  sku: {
    name: appConfigurationSku
  }
}

resource configurationStoreKeyValues 'Microsoft.AppConfiguration/configurationStores/keyValues@2023-03-01' = [for item in appConfigurationKeyData: {
  parent: configurationStore
  name: '${item.key}'
  properties: {
    value: item.value
    contentType: item.contentType
    tags: (contains(item, 'tag') ? item.tag : null)
  }
}]

resource configurationStoreFeatureFlags 'Microsoft.AppConfiguration/configurationStores/keyValues@2023-03-01' = [for flag in featureFlags: {
  parent: configurationStore
  name: '.appconfig.featureflag~2F${flag.id}'
  properties: {
    contentType: 'application/vnd.microsoft.appconfig.ff+json;charset=utf-8'
    value: string({
      id: flag.id
      description: flag.description
      enabled: flag.enabled
      createdBy: contains(flag, 'createdBy') ? flag.createdBy : null
      createdDate: contains(flag, 'createdDate') ? flag.createdDate : null
    })
  }
}]

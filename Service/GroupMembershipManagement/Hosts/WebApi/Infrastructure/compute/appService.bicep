@description('WebAPI site plan name.')
@minLength(1)
param name string

@description('Resource location.')
param location string

@description('Service plan name.')
param servicePlanName string

@description('Application settings')
param appSettings array

resource websiteTemplate 'Microsoft.Web/sites@2022-03-01' = {
  name: name
  location: location
  kind: 'app'
  properties: {
    httpsOnly: true
    reserved: false
    serverFarmId: resourceId('Microsoft.Web/serverfarms', servicePlanName)
    siteConfig: {
      netFrameworkVersion: 'v6.0'
      ftpsState: 'FtpsOnly'
      minTlsVersion: '1.2'
      appSettings: appSettings
    }
  }
  identity: {
    type: 'SystemAssigned'
  }
}

@description('Enter a name for application insights resource.')
param name string

@description('Enter the application location.')
param location string

@description('Enter the application type.')
@allowed([
  'web'
  'other'
])
param kind string = 'web'

param ingestionMode string = 'LogAnalytics'

param workspaceId string

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  kind: kind
  name: name
  location: location
  properties: {
    Application_Type: kind
    IngestionMode: ingestionMode
    WorkspaceResourceId: workspaceId
  }
}

output appId string = reference(applicationInsights.id, '2015-05-01').AppId
output instrumentationKey string = reference(applicationInsights.id, '2015-05-01').InstrumentationKey

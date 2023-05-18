@description('Function app name.')
@minLength(1)
param name string

@description('Function app kind.')
@allowed([
  'functionapp'
  'linux'
  'container'
])
param kind string = 'functionapp'

@description('Function app location.')
param location string

@description('Service plan name.')
@minLength(1)
param servicePlanName string

@description('app settings')
param appSettings object

resource functionApp 'Microsoft.Web/sites@2018-02-01' = {
  name: name
  location: location
  kind: kind
  properties: {
    serverFarmId: resourceId('Microsoft.Web/serverfarms', servicePlanName)
    clientAffinityEnabled: false
    httpsOnly: true
    siteConfig: {
      use32BitWorkerProcess : false
      appSettings: appSettings
      ftpsState: 'FtpsOnly'
    }
  }
  identity: {
    type: 'SystemAssigned'
  }
}

resource snScmBasicAuth 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2022-09-01' = {
  parent: functionApp
  name: 'scm'
  properties: {
    allow: false
  }
}

resource snFtpBasicAuth 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2022-09-01' = {
  parent: functionApp
  name: 'ftp'
  properties: {
    allow: false
  }
}

resource functionAppSlotConfig 'Microsoft.Web/sites/config@2021-03-01' = {
  name: 'slotConfigNames'
  parent: functionApp
  properties: {
    appSettingNames: [
      'membershipAggregatorUrl'
      'membershipAggregatorFunctionKey'
      'AzureFunctionsJobHost__extensions__durableTask__hubName'
      'AzureWebJobs.StarterFunction.Disabled'
      'AzureWebJobs.OrchestratorFunction.Disabled'
      'AzureWebJobs.GetGroupOwnersFunction.Disabled'
      'AzureWebJobs.GetJobsSegmentedFunction.Disabled'
      'AzureWebJobs.JobsFilterFunction.Disabled'
      'AzureWebJobs.JobStatusUpdaterFunction.Disabled'
      'AzureWebJobs.LoggerFunction.Disabled'
      'AzureWebJobs.TelemetryTrackerFunction.Disabled'
      'AzureWebJobs.UsersSenderFunction.Disabled'
    ]
  }
}

output msi string = functionApp.identity.principalId

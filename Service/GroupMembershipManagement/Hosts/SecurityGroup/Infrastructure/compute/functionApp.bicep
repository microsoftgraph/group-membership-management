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
param secretSettings object

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
      appSettings: secretSettings
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
      'AzureFunctionsJobHost__extensions__durableTask__hubName'
      'AzureWebJobs.StarterFunction.Disabled'
      'AzureWebJobs.OrchestratorFunction.Disabled'
      'AzureWebJobs.SubOrchestratorFunction.Disabled'
      'AzureWebJobs.DeltaUsersReaderFunction.Disabled'
      'AzureWebJobs.DeltaUsersSenderFunction.Disabled'
      'AzureWebJobs.EmailSenderFunction.Disabled'
      'AzureWebJobs.FileDownloaderFunction.Disabled'
      'AzureWebJobs.GroupsReaderFunction.Disabled'
      'AzureWebJobs.GroupValidatorFunction.Disabled'
      'AzureWebJobs.JobStatusUpdaterFunction.Disabled'
      'AzureWebJobs.MembersReaderFunction.Disabled'
      'AzureWebJobs.SourceGroupsReaderFunction.Disabled'
      'AzureWebJobs.SubsequentDeltaUsersReaderFunction.Disabled'
      'AzureWebJobs.SubsequentMembersReaderFunction.Disabled'
      'AzureWebJobs.SubsequentUsersReaderFunction.Disabled'
      'AzureWebJobs.UsersReaderFunction.Disabled'
      'AzureWebJobs.UsersSenderFunction.Disabled'
    ]
  }
}

output msi string = functionApp.identity.principalId

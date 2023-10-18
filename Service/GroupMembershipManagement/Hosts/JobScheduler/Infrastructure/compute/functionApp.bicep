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

@description('Name of the \'data\' key vault.')
param dataKeyVaultName string

@description('Name of the resource group where the \'data\' key vault is located.')
param dataKeyVaultResourceGroup string

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

module secretsTemplate 'keyVaultSecrets.bicep' = {
  name: 'secretsTemplate-JobScheduler'
  scope: resourceGroup(dataKeyVaultResourceGroup)
  params: {
    keyVaultName: dataKeyVaultName
    keyVaultParameters: [
      {
        name: 'jobSchedulerFunctionBaseUrl'
        value: 'https://${functionApp.properties.defaultHostName}'
      }
    ]
  }
}

module secureSecretsTemplate 'keyVaultSecretsSecure.bicep' = {
  name: 'secureSecretsTemplate-JobScheduler'
  scope: resourceGroup(dataKeyVaultResourceGroup)
  params: {
    keyVaultName: dataKeyVaultName
    keyVaultSecrets: {
      secrets: [
        { 
          name: 'jobSchedulerFunctionKey'
          value: listkeys('${functionApp.id}/host/default', '2018-11-01').functionKeys.default
        }
      ]
    }
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
      'AzureWebJobs.LoggerFunction.Disabled'
      'AzureWebJobs.GetJobsSubOrchestratorFunction.Disabled'
      'AzureWebJobs.GetJobsSegmentedFunction.Disabled'
      'AzureWebJobs.ResetJobsFunction.Disabled'
      'AzureWebJobs.DistributeJobsFunction.Disabled'
      'AzureWebJobs.UpdateJobsSubOrchestratorFunction.Disabled'
      'AzureWebJobs.BatchUpdateJobsFunction.Disabled'
      'AzureWebJobs.PipelineInvocationStarterFunction.Disabled'
      'AzureWebJobs.StatusCallbackOrchestratorFunction.Disabled'
      'AzureWebJobs.CheckJobSchedulerStatusFunction.Disabled'
      'AzureWebJobs.PostCallbackFunction.Disabled'
      'AzureFunctionsWebHost__hostid'
    ]
  }
}

output msi string = functionApp.identity.principalId

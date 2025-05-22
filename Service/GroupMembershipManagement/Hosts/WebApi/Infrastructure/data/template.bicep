@description('Enter an abbreviation for the solution.')
@minLength(2)
@maxLength(3)
param solutionAbbreviation string = 'gmm'

@description('Enter an abbreviation for the environment.')
@minLength(2)
@maxLength(6)
param environmentAbbreviation string

@description('Enter app configuration name.')
@minLength(1)
@maxLength(24)
param appConfigurationName string = '${solutionAbbreviation}-appConfig-${environmentAbbreviation}'

@description('Name of the \'data\' key vault.')
param dataKeyVaultName string = '${solutionAbbreviation}-data-${environmentAbbreviation}'

var azureSignalRConnectionString = 'Endpoint=https://${solutionAbbreviation}-compute-${environmentAbbreviation}-signalr.service.signalr.net;AuthType=azure.msi;Version=1.0;'
var openAIEndpoint = 'https://${solutionAbbreviation}-compute-${environmentAbbreviation}.openai.azure.com'

param appConfigurationKeyData array = [
  {
    key: 'WebAPI:Settings:Sentinel'
    value: '1'
    contentType: 'string'
    tag: {
      tag1: 'WebApi'
    }
  }
]

module appConfigurationTemplate 'appConfigurationValues.bicep' = {
  name: 'appConfigurationTemplate'
  params: {
    configStoreName: appConfigurationName
    appConfigurationKeyData: appConfigurationKeyData
  }
}

module secureKeyvaultSecrets 'keyVaultSecretsSecure.bicep' = {
  name: 'secureKeyvaultSecrets'
  params: {
    keyVaultName: dataKeyVaultName
    keyVaultSecrets: {
      secrets: [
        {
          name: 'azureSignalRConnectionString'
          value: azureSignalRConnectionString
        }
        {
          name: 'openAIEndpoint'
          value: openAIEndpoint
        }
      ]
    }
  }
}

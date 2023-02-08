@description('Enter an abbreviation for the environment.')
@minLength(2)
@maxLength(6)
param environmentAbbreviation string

@description('Enter an abbreviation for the solution.')
@minLength(2)
@maxLength(3)
param solutionAbbreviation string = 'gmm'

@description('Classify the types of resources in this resource group.')
@allowed([
  'prereqs'
  'data'
  'compute'
])
param resourceGroupClassification string = 'compute'

@description('Tenant id.')
param tenantId string

@description('Service plan name.')
param servicePlanName string = '${solutionAbbreviation}-${resourceGroupClassification}-${environmentAbbreviation}-webapi-serviceplan'

@description('Service plan sku')
@allowed([
  'D1'
  'F1'
  'B1'
  'B2'
  'B3'
  'S1'
  'S2'
  'S3'
  'P1'
  'P2'
  'P3'
  'P1V2'
  'P2V2'
  'P3V2'
  'I1'
  'I2'
  'I3'
  'Y1'
])
param servicePlanSku string = 'F1'

@description('Resource location.')
param location string

@description('Maximum elastic worker count.')
param maximumElasticWorkerCount int = 1

@description('App configuration name.')
param appConfigurationEndpoint string = '${solutionAbbreviation}-appconfig-${environmentAbbreviation}'

@description('Name of the resource group where the \'prereqs\' key vault is located.')
param prereqsKeyVaultName string = '${solutionAbbreviation}-prereqs-${environmentAbbreviation}'

@description('Name of the resource group where the \'prereqs\' key vault is located.')
param prereqsKeyVaultResourceGroup string = '${solutionAbbreviation}-prereqs-${environmentAbbreviation}'

var webapiClientId = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'webapiClientId')

var appSettings = {
  'Settings:AppConfigurationEndpoint': appConfigurationEndpoint
  'AzureAd:TenatId': tenantId
  'AzureAd:ClientId':'@Microsoft.KeyVault(SecretUri=${reference(webapiClientId, '2019-09-01').secretUriWithVersion})'
}

module servicePlanTemplate 'servicePlan.bicep' = {
  name: 'servicePlanTemplate-WebAPI'
  params: {
    name: servicePlanName
    sku: servicePlanSku
    location: location
    maximumElasticWorkerCount: maximumElasticWorkerCount
  }
}

resource websiteTemplate 'Microsoft.Web/sites@2022-03-01' = {
  name: '${solutionAbbreviation}-${resourceGroupClassification}-${environmentAbbreviation}-webapi'
  location: location
  kind: 'app'
  properties: {
    httpsOnly: true
    reserved: false
    serverFarmId: resourceId('Microsoft.Web/serverfarms', servicePlanName)
    siteConfig: {
      netFrameworkVersion: 'v6.0'
      ftpsState: 'FtpsOnly'
      appSettings: items(appSettings)
    }
  }
  identity: {
    type: 'SystemAssigned'
  }
  dependsOn:[
    servicePlanTemplate
  ]
}


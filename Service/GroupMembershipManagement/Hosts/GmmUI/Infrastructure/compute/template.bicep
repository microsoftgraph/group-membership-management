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

@description('Enter an abbreviation for the environment.')
@minLength(2)
@maxLength(6)
param environmentAbbreviation string

@description('Tenant id.')
param tenantId string

@description('Service plan name.')
param servicePlanName string = '${solutionAbbreviation}-${resourceGroupClassification}-${environmentAbbreviation}-webapp'

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
param servicePlanSku string = 'Y1'

@description('Resource location.')
param location string = 'westus'

@description('Maximum elastic worker count.')
param maximumElasticWorkerCount int = 1

@description('Name of the \'data\' key vault.')
param dataKeyVaultName string = '${solutionAbbreviation}-data-${environmentAbbreviation}'

@description('Name of the resource group where the \'data\' key vault is located.')
param dataKeyVaultResourceGroup string = '${solutionAbbreviation}-data-${environmentAbbreviation}'


module servicePlanTemplate 'servicePlan.bicep' = {
  name: 'servicePlanTemplate'
  params: {
    name: servicePlanName
    sku: servicePlanSku
    location: location
    maximumElasticWorkerCount: maximumElasticWorkerCount
  }
}

resource staticWebApp 'Microsoft.Web/staticSites@2021-03-01' = {
  name: 'gmm-ui'
  location: location
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {
    allowConfigFileUpdates: true
    branch: 'users/t-chenemily/deploy-webapp'
    buildProperties: {
      appBuildCommand: 'dotnet run'
      appLocation: 'Service/GroupMembershipManagement/Hosts/GmmUI/WebApp'
      outputLocation: 'Service/GroupMembershipManagement/Hosts/GmmUI/WebApp/wwwroot'
      skipGithubActionWorkflowGeneration: true
    }
    enterpriseGradeCdnStatus: 'Disabled'
    provider: 'DevOps'
    repositoryUrl: 'https://microsoftit.visualstudio.com/OneITVSO/_git/STW-Sol-GrpMM-public'
    stagingEnvironmentPolicy: 'Disabled'
  }
}

output deployment_token string = listSecrets(staticWebApp.id, staticWebApp.apiVersion).properties.apiKey

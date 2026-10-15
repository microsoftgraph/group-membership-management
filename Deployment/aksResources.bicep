targetScope = 'resourceGroup'

@description('Enter an abbreviation for the solution.')
@minLength(2)
@maxLength(3)
param solutionAbbreviation string = 'gmm'

@description('Enter an abbreviation for the environment.')
@minLength(2)
@maxLength(6)
param environmentAbbreviation string

@description('Resource location.')
param location string

@description('Object ID of the Entra security group whose members become Kubernetes cluster-admins.')
param aksAdministratorsGroupId string

@description('Whether to use the production AKS control-plane tier.')
param isProduction bool = false

@description('Overlay pod IPv4 /19; missing or empty uses project default 10.244.0.0/19. Override with a valid non-overlapping /19 and audit enterprise routes before deployment.')
param aksPodCidr string = '10.244.0.0/19'

@description('Kubernetes namespace the workloads run in.')
param namespaceName string = 'gmm'

@description('Services to create a federated credential for.')
param services array = [
  'JobTrigger'
]

module aksResourcesTemplate '../Infrastructure/AKS/bicep/aksResources.bicep' = {
  name: 'aksClusterResources'
  params: {
    solutionAbbreviation: solutionAbbreviation
    environmentAbbreviation: environmentAbbreviation
    location: location
    aksAdministratorsGroupId: aksAdministratorsGroupId
    isProduction: isProduction
    aksPodCidr: aksPodCidr
    namespaceName: namespaceName
    services: services
  }
}

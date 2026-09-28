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

@description('Node pool VM size.')
param nodeVmSize string = 'Standard_D4s_v5'

@description('System node pool count.')
@minValue(1)
@maxValue(100)
param nodeCount int = 2

@description('AKS control-plane tier.')
@allowed([
  'Free'
  'Standard'
])
param aksSkuTier string = 'Free'

@description('Kubernetes namespace the workloads run in.')
param namespaceName string = 'gmm'

@description('Services to create a federated credential for.')
param services array = [
  {
    name: 'JobTrigger'
    serviceAccount: 'jobtrigger-sa'
  }
]

module aksResourcesTemplate '../Infrastructure/AKS/bicep/aksResources.bicep' = {
  name: 'aksClusterResources'
  params: {
    solutionAbbreviation: solutionAbbreviation
    environmentAbbreviation: environmentAbbreviation
    location: location
    aksAdministratorsGroupId: aksAdministratorsGroupId
    nodeVmSize: nodeVmSize
    nodeCount: nodeCount
    aksSkuTier: aksSkuTier
    namespaceName: namespaceName
    services: services
  }
}

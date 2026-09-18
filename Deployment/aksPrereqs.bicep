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

@description('Services to create a workload identity + federated credential for.')
param services array = [
  {
    name: 'JobTrigger'
    serviceAccount: 'jobtrigger-sa'
  }
]

@description('When true, this deployment also creates the role assignments.')
param setRBACPermissions bool = false

@description('Registry SKU.')
@allowed([
  'Basic'
  'Standard'
  'Premium'
])
param acrSku string = 'Basic'

@description('Number of days an untagged manifest is kept before the scheduled purge task deletes it.')
param purgeAfterDays int = 30

@description('Cron schedule for the purge task.')
param purgeSchedule string = '0 3 * * 0'

@description('Address space for the AKS VNet.')
param aksVnetAddressPrefix string = '10.224.0.0/16'

@description('Address prefix for the AKS node subnet.')
param aksNodeSubnetAddressPrefix string = '10.224.0.0/22'

@description('IP tags applied to the AKS outbound public IP.')
param aksOutboundIpTags array = []

module aksPrereqsTemplate '../Infrastructure/AKS/bicep/aksPrereqs.bicep' = {
  name: 'aksPrereqsResources'
  params: {
    solutionAbbreviation: solutionAbbreviation
    environmentAbbreviation: environmentAbbreviation
    location: location
    services: services
    setRBACPermissions: setRBACPermissions
    acrSku: acrSku
    purgeAfterDays: purgeAfterDays
    purgeSchedule: purgeSchedule
    aksVnetAddressPrefix: aksVnetAddressPrefix
    aksNodeSubnetAddressPrefix: aksNodeSubnetAddressPrefix
    aksOutboundIpTags: aksOutboundIpTags
  }
}

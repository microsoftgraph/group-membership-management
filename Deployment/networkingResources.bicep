@description('Enter an abbreviation for the solution.')
@minLength(2)
@maxLength(3)
param solutionAbbreviation string = 'gmm'

@description('Enter an abbreviation for the environment.')
@minLength(2)
@maxLength(6)
param environmentAbbreviation string

@description('Azure region for all resources.')
param location string

// -----------------------------------------------
// Bastion Deployment Modality
// -----------------------------------------------

@description('When true, deploys a new Bastion VNet and Bastion Host. When false, peers to an existing Bastion VNet.')
param deployBastion bool = true

@description('Resource ID of an existing Bastion VNet to peer with. Required when deployBastion is false.')
param existingBastionVnetId string = ''

@description('Name of the existing Bastion VNet. Required when deployBastion is false (used for peering resource names).')
param existingBastionVnetName string = ''

@description('Resource group name of the existing Bastion VNet. Required when the existing Bastion VNet is in a different resource group (e.g., a shared nonprod bastion). Defaults to the current resource group.')
param existingBastionResourceGroupName string = ''

// -----------------------------------------------
// Address Space Parameters
// -----------------------------------------------

@description('Address prefix for the Bastion VNet.')
param bastionVnetAddressPrefix string = '10.0.0.0/24'

@description('Address prefix for the AzureBastionSubnet.')
param bastionSubnetAddressPrefix string = '10.0.0.0/26'

@description('Address prefix for the Management VNet.')
param managementVnetAddressPrefix string = '10.0.1.0/24'

@description('Address prefix for the JumpboxSubnet.')
param jumpboxSubnetAddressPrefix string = '10.0.1.0/24'

@description('Address prefix for the PrivateLink VNet.')
param privateLinkVnetAddressPrefix string = '10.1.0.0/16'

@description('Address prefix for the PrivateEndpointSubnet.')
param privateEndpointSubnetAddressPrefix string = '10.1.0.0/16'

// -----------------------------------------------
// VM Parameters
// -----------------------------------------------

@description('Size of the jumpbox VM.')
param vmSize string = 'Standard_B2ms'

@description('Daily auto-shutdown time for the jumpbox VM in UTC (24-hour format, e.g. 0200 = 2:00 AM UTC). Default is 0200 which equals 6:00 PM PST.')
param vmDailyAutoShutdownTimeUTC string = '0200'

@description('When true, networking template writes vm admin secrets into data Key Vault before VM provisioning.')
param setVmAdminSecrets bool = false

@description('VM admin username value used when setVmAdminSecrets is true.')
@secure()
param vmAdminUsername string = ''

@description('VM admin password value used when setVmAdminSecrets is true.')
@secure()
param vmAdminPassword string = ''

@description('IP tags to apply to the Bastion Public IP Address.')
param bastionPipIpTags array = []

@description('IP tags to apply to the NAT Gateway Public IP Address.')
param natGatewayPipIpTags array = []

@description('Tags to apply to the Bastion Host resource.')
param bastionTags object = {}

// networking resources
module networkingInfrastructureTemplate '../Infrastructure/networking/template.bicep' = {
  name: 'networkingInfrastructureResources'
  params: {
    solutionAbbreviation: solutionAbbreviation
    environmentAbbreviation: environmentAbbreviation
    location: location
    deployBastion: deployBastion
    existingBastionVnetId: existingBastionVnetId
    existingBastionVnetName: existingBastionVnetName
    existingBastionResourceGroupName: existingBastionResourceGroupName
    bastionVnetAddressPrefix: bastionVnetAddressPrefix
    bastionSubnetAddressPrefix: bastionSubnetAddressPrefix
    managementVnetAddressPrefix: managementVnetAddressPrefix
    jumpboxSubnetAddressPrefix: jumpboxSubnetAddressPrefix
    privateLinkVnetAddressPrefix: privateLinkVnetAddressPrefix
    privateEndpointSubnetAddressPrefix: privateEndpointSubnetAddressPrefix
    vmSize: vmSize
    vmDailyAutoShutdownTimeUTC: vmDailyAutoShutdownTimeUTC
    setVmAdminSecrets: setVmAdminSecrets
    vmAdminUsername: vmAdminUsername
    vmAdminPassword: vmAdminPassword
    bastionPipIpTags: bastionPipIpTags
    natGatewayPipIpTags: natGatewayPipIpTags
    bastionTags: bastionTags
  }
}



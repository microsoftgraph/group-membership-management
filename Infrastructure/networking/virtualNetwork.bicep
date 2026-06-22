@description('Name of the Virtual Network.')
param name string

@description('Azure region for the VNet.')
param location string

@description('Address space prefix for the VNet (e.g., 10.0.0.0/24).')
param addressPrefix string

type subnetType = {
  @description('Name of the subnet.')
  name: string
  @description('Address prefix for the subnet.')
  addressPrefix: string
  @description('Resource ID of the NSG to attach to this subnet.')
  nsgId: string
  @description('Optional resource ID of a NAT Gateway to attach to this subnet.')
  natGatewayId: string?
  @description('Optional privateEndpointNetworkPolicies value. Only set this for subnets that host private endpoints (e.g. PrivateEndpointSubnet). When omitted the property is NOT emitted, preserving whatever Azure set on the live subnet.')
  privateEndpointNetworkPolicies: string?
}

@description('Subnets to create within the VNet.')
param subnets subnetType[]

type functionSubnetType = {
  @description('Subnet name (e.g., \'func-pub-autoapprover\').')
  name: string
  @description('Address prefix for the subnet.')
  addressPrefix: string
  @description('Resource ID of the NSG to attach to this subnet.')
  nsgId: string
}

@description('Optional function-integration subnets to create alongside the base subnets. Each entry is delegated to Microsoft.App/environments for FlexConsumption (FC1) VNET integration.')
param functionSubnets functionSubnetType[] = []

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: name
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        addressPrefix
      ]
    }
  }
}


@batchSize(1)
resource baseSubnets 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' = [
  for subnet in subnets: {
    parent: vnet
    name: subnet.name
    properties: union(
      {
        addressPrefix: subnet.addressPrefix
        defaultOutboundAccess: false
        networkSecurityGroup: {
          id: subnet.nsgId
        }
      },
      subnet.?natGatewayId != null ? { natGateway: { id: subnet.?natGatewayId } } : {},
      subnet.?privateEndpointNetworkPolicies != null
        ? { privateEndpointNetworkPolicies: subnet.?privateEndpointNetworkPolicies }
        : {}
    )
  }
]

@batchSize(1)
resource functionSubnetResources 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' = [
  for fs in functionSubnets: {
    parent: vnet
    name: fs.name
    properties: {
      addressPrefix: fs.addressPrefix
      defaultOutboundAccess: false
      networkSecurityGroup: {
        id: fs.nsgId
      }
      delegations: [
        {
          name: 'delegation'
          properties: {
            serviceName: 'Microsoft.App/environments'
          }
        }
      ]
      privateEndpointNetworkPolicies: 'Disabled'
      privateLinkServiceNetworkPolicies: 'Enabled'
    }
    dependsOn: [
      baseSubnets
    ]
  }
]

output id string = vnet.id
output name string = vnet.name

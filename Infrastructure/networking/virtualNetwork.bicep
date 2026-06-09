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
}

@description('Subnets to create within the VNet.')
param subnets subnetType[]

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: name
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        addressPrefix
      ]
    }
    subnets: [
      for subnet in subnets: {
        name: subnet.name
        properties: {
          addressPrefix: subnet.addressPrefix
          defaultOutboundAccess: false
          networkSecurityGroup: {
            id: subnet.nsgId
          }
          natGateway: subnet.natGatewayId != null ? { id: subnet.natGatewayId } : null
        }
      }
    ]
  }
}

output id string = vnet.id
output name string = vnet.name
output subnets array = vnet.properties.subnets

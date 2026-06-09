@description('Name of the local VNet (where the peering is created).')
param localVnetName string

@description('Resource ID of the remote VNet to peer with.')
param remoteVnetId string

@description('Name of the remote VNet (used to name the peering resource).')
param remoteVnetName string

@description('Allow forwarded traffic from the remote VNet.')
param allowForwardedTraffic bool = true

@description('Allow gateway transit on this peering.')
param allowGatewayTransit bool = false

@description('Use remote gateways from the peer VNet.')
param useRemoteGateways bool = false

resource localVnet 'Microsoft.Network/virtualNetworks@2024-05-01' existing = {
  name: localVnetName
}

resource peering 'Microsoft.Network/virtualNetworks/virtualNetworkPeerings@2024-05-01' = {
  name: '${localVnetName}-to-${remoteVnetName}'
  parent: localVnet
  properties: {
    remoteVirtualNetwork: {
      id: remoteVnetId
    }
    allowVirtualNetworkAccess: true
    allowForwardedTraffic: allowForwardedTraffic
    allowGatewayTransit: allowGatewayTransit
    useRemoteGateways: useRemoteGateways
  }
}

output id string = peering.id
output name string = peering.name

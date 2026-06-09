@description('Name of the NAT Gateway.')
param name string

@description('Azure region for the NAT Gateway.')
param location string

@description('Resource ID of the Public IP Address for the NAT Gateway.')
param publicIpId string

@description('Idle timeout in minutes for the NAT Gateway.')
param idleTimeoutInMinutes int = 4

resource natGateway 'Microsoft.Network/natGateways@2024-05-01' = {
  name: name
  location: location
  sku: {
    name: 'Standard'
  }
  properties: {
    idleTimeoutInMinutes: idleTimeoutInMinutes
    publicIpAddresses: [
      {
        id: publicIpId
      }
    ]
  }
}

output id string = natGateway.id
output name string = natGateway.name

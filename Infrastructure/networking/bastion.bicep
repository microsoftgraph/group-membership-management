@description('Name of the Bastion Host.')
param name string

@description('Azure region for the Bastion Host.')
param location string

@description('Resource ID of the AzureBastionSubnet.')
param subnetId string

@description('Resource ID of the Public IP Address for the Bastion Host.')
param publicIpId string

@description('Tags to apply to the Bastion Host resource.')
param tags object = {}

resource bastion 'Microsoft.Network/bastionHosts@2024-05-01' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: 'Basic'
  }
  properties: {
    ipConfigurations: [
      {
        name: 'bastion-ip-config'
        properties: {
          subnet: {
            id: subnetId
          }
          publicIPAddress: {
            id: publicIpId
          }
        }
      }
    ]
  }
}

output id string = bastion.id
output name string = bastion.name

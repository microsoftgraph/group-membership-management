@description('Name of the Network Interface.')
param name string

@description('Azure region for the NIC.')
param location string

@description('Resource ID of the subnet to attach the NIC to.')
param subnetId string

resource nic 'Microsoft.Network/networkInterfaces@2024-05-01' = {
  name: name
  location: location
  properties: {
    ipConfigurations: [
      {
        name: 'ipconfig1'
        properties: {
          privateIPAllocationMethod: 'Dynamic'
          subnet: {
            id: subnetId
          }
        }
      }
    ]
  }
}

output id string = nic.id
output name string = nic.name

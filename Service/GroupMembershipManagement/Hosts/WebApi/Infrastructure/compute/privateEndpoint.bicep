// =====================================================================================
// Reusable Private Endpoint Module
// =====================================================================================
// Creates a Private Endpoint and its associated Private DNS Zone Group so that
// the PaaS resource is accessible via a private IP within the PrivateLink VNet.
// =====================================================================================

@description('Name of the Private Endpoint resource.')
param name string

@description('Azure region for the Private Endpoint.')
param location string = resourceGroup().location

@description('Resource ID of the subnet to place the Private Endpoint in.')
param subnetId string

@description('Resource ID of the target PaaS resource to connect to (e.g. a Key Vault).')
param privateLinkServiceId string

@description('Group IDs for the private link connection (e.g. [\'vault\'], [\'sqlServer\'], [\'namespace\']).')
param groupIds array

@description('Resource ID of the Private DNS Zone for automatic DNS registration.')
param privateDnsZoneId string

// -----------------------------------------------
// Private Endpoint
// -----------------------------------------------

resource privateEndpoint 'Microsoft.Network/privateEndpoints@2024-05-01' = {
  name: name
  location: location
  properties: {
    subnet: {
      id: subnetId
    }
    privateLinkServiceConnections: [
      {
        name: name
        properties: {
          privateLinkServiceId: privateLinkServiceId
          groupIds: groupIds
        }
      }
    ]
  }
}

// -----------------------------------------------
// Private DNS Zone Group
// -----------------------------------------------

resource dnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = {
  parent: privateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: replace(split(privateDnsZoneId, '/')[8], '.', '-')
        properties: {
          privateDnsZoneId: privateDnsZoneId
        }
      }
    ]
  }
}

// -----------------------------------------------
// Outputs
// -----------------------------------------------

output id string = privateEndpoint.id
output name string = privateEndpoint.name

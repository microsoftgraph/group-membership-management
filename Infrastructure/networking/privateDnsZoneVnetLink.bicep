@description('Name of the Private DNS Zone to link.')
param dnsZoneName string

@description('Resource ID of the VNet to link to the DNS zone.')
param vnetId string

@description('Name of the VNet (used to name the link resource).')
param vnetName string

@description('Enable auto-registration of VM DNS records in this zone.')
param registrationEnabled bool = false

resource dnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' existing = {
  name: dnsZoneName
}

resource vnetLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  name: '${dnsZoneName}-to-${vnetName}'
  parent: dnsZone
  location: 'global'
  properties: {
    virtualNetwork: {
      id: vnetId
    }
    registrationEnabled: registrationEnabled
  }
}

output id string = vnetLink.id
output name string = vnetLink.name

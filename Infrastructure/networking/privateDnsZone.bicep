@description('Name of the Private DNS Zone (e.g., privatelink.vaultcore.azure.net).')
param name string

resource dnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: name
  location: 'global'
}

output id string = dnsZone.id
output name string = dnsZone.name

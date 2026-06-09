@description('Name of the Public IP Address.')
param name string

@description('Azure region for the Public IP.')
param location string

@description('SKU name for the Public IP.')
@allowed([
  'Basic'
  'Standard'
])
param skuName string = 'Standard'

param ipTags array = []

@description('Allocation method for the Public IP.')
@allowed([
  'Dynamic'
  'Static'
])
param allocationMethod string = 'Static'

@description('IP version for the Public IP.')
@allowed([
  'IPv4'
  'IPv6'
])
param publicIPAddressVersion string = 'IPv4'

resource publicIp 'Microsoft.Network/publicIPAddresses@2024-05-01' = {
  name: name
  location: location
  sku: {
    name: skuName
  }
  properties: {
    publicIPAllocationMethod: allocationMethod
    publicIPAddressVersion: publicIPAddressVersion
    ipTags: ipTags
  }
}

output id string = publicIp.id
output name string = publicIp.name

@description('Network security perimeter alphanumeric name.')
@minLength(1)
@maxLength(24)
param nspName string

param nspProfileNames array = [
  'keyvault'
  'sql'
  'storageaccount'
]

resource networkSecurityPerimeter 'Microsoft.Network/networkSecurityPerimeters@2023-08-01-preview' existing = {
  name: nspName
}

// create profile for each name in the array
resource nspProfiles 'Microsoft.Network/networkSecurityPerimeters/profiles@2023-08-01-preview' = [for profileName in nspProfileNames: {
  name: profileName
  parent: networkSecurityPerimeter
  properties: {}
}]

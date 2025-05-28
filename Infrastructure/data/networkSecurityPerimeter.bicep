@description('Network security perimeter alphanumeric name.')
@minLength(1)
@maxLength(24)
param nspName string

@description('Specifies the Azure location where the network security perimeter will be created.')
param nspLocation string

resource networkSecurityPerimeter 'Microsoft.Network/networkSecurityPerimeters@2023-08-01-preview' = {
  name: nspName
  location: nspLocation
  properties: {}
}

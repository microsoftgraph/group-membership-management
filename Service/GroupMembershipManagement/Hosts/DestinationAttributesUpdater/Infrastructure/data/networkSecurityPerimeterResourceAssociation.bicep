@description('Network security perimeter alphanumeric name.')
@minLength(1)
@maxLength(24)
param nspName string

@allowed([
  'keyvault'
  'sql'
  'storageaccount'
])
@description('Profile name for resource association')
param profileName string

@description('Resource ID to associate with the profile')
param resourceId string

resource networkSecurityPerimeter 'Microsoft.Network/networkSecurityPerimeters@2023-08-01-preview' existing = {
  name: nspName
}

// References to existing NSP profiles
resource nspProfile 'Microsoft.Network/networkSecurityPerimeters/profiles@2023-08-01-preview' existing = {
  parent: networkSecurityPerimeter
  name: profileName
}

// Create resource association
resource nspResourceAssociation 'Microsoft.Network/networkSecurityPerimeters/resourceAssociations@2023-08-01-preview' = {
  parent: networkSecurityPerimeter
  name: '${profileName}-${last(split(resourceId, '/'))}'
  properties: {
    profile: {
      id: nspProfile.id
    }
    privateLinkResource: {
      id: resourceId
    }
    accessMode: 'learning'
  }
}

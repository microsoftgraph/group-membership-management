@description('Resource location.')
param location string

@description('User assigned managed identity name.')
param identityName string

resource userAssignedManagedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-07-31-preview' = {
  name: identityName
  location: location
}

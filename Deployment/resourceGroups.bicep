targetScope = 'subscription'

param location string
param prereqsResourceGroupName string
param dataResourceGroupName string
param computeResourceGroupName string
param appConfigurationDataOwners array
param grantAppConfigurationDataOwnersPermission bool = true

resource prereqsResourceGroup 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: prereqsResourceGroupName
  location: location
}

resource dataResourceGroup 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: dataResourceGroupName
  location: location
}

resource computeResourceGroup 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: computeResourceGroupName
  location: location
}

module appConfigurationRBAC 'rbacTemplate.bicep' = if (grantAppConfigurationDataOwnersPermission) {
  name: 'appConfigurationRBAC'
  scope: dataResourceGroup
  params: {
    // App Configuration Data Owner
    roleDefinitionId: '5ae67dd6-50cb-40e7-96ff-dc2bfa4b606b'
    principals: appConfigurationDataOwners
    dataResourceGroupName: dataResourceGroupName
  }
}

targetScope = 'subscription'

param location string
param prereqsResourceGroupName string
param dataResourceGroupName string
param computeResourceGroupName string
param networkingResourceGroupName string
param appConfigurationDataOwners array
param setRBACPermissions bool = false
param enableMultiRegion bool = false
param secondaryLocation string = ''

// Fixed naming token for the secondary region. Must match the value in the data infrastructure
// templates, which compose the secondary resource group name the same way.
var secondaryRegionToken = 'sec'
var secondaryDataResourceGroupName = '${dataResourceGroupName}-${secondaryRegionToken}'

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

resource networkingResourceGroup 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: networkingResourceGroupName
  location: location
}

resource secondaryDataResourceGroup 'Microsoft.Resources/resourceGroups@2023-07-01' = if (enableMultiRegion) {
  name: secondaryDataResourceGroupName
  location: secondaryLocation
}

module appConfigurationRBAC 'rbacTemplate.bicep' = if (setRBACPermissions) {
  name: 'appConfigurationRBAC'
  scope: dataResourceGroup
  params: {
    // App Configuration Data Owner
    roleDefinitionId: '5ae67dd6-50cb-40e7-96ff-dc2bfa4b606b'
    principals: appConfigurationDataOwners
    dataResourceGroupName: dataResourceGroupName
  }
}

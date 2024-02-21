targetScope = 'subscription'

param location string
param prereqsResourceGroupName string
param dataResourceGroupName string
param computeResourceGroupName string

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

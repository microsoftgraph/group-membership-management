@description('Name of the VM to associate with the DCR.')
param vmName string

@description('Resource ID of the Data Collection Rule.')
param dataCollectionRuleId string

@description('Name for the DCR association resource.')
param associationName string

resource vm 'Microsoft.Compute/virtualMachines@2024-07-01' existing = {
  name: vmName
}

resource dcrAssociation 'Microsoft.Insights/dataCollectionRuleAssociations@2022-06-01' = {
  name: associationName
  scope: vm
  properties: {
    dataCollectionRuleId: dataCollectionRuleId
  }
}

@description('Unique name within the resource group for the Action group.')
param actionGroupName string

@description('Short name up to 12 characters for the Action group.')
@maxLength(12)
param actionGroupShortName string

@description('Contains emails details.')
param emailReceivers array

resource actionGroups 'microsoft.insights/actionGroups@2019-06-01' = {
  name: actionGroupName
  location: 'Global'
  properties: {
    groupShortName: actionGroupShortName
    enabled: true
    emailReceivers: emailReceivers
  }
}

output actionGroupId string = actionGroups.id

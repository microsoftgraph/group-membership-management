@description('Role definition, GUID')
param roleDefinitionId string

@description('Array of principals to assign the role to. [{principalId: string, principalType: string}]')
param principals array

@description('Name of the data resource group')
param dataResourceGroupName string

resource appConfigDataReaderRoleDefinition 'Microsoft.Authorization/roleDefinitions@2022-05-01-preview' existing = {
  name: roleDefinitionId
}

// 'App Configuration Data Owner'
resource roleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principal in principals: {
    name: guid(principal.principalId, roleDefinitionId, dataResourceGroupName)
    properties: {
      roleDefinitionId: appConfigDataReaderRoleDefinition.id
      principalId: principal.principalId
      principalType: principal.principalType
    }
  }
]

@description('Specifies the name of the storage account.')
param storageAccountName string

@description('Specifies the object ID of a user, service principal or security group in the Azure Active Directory tenant for the vault. The object ID must be unique for the list of access policies. Get it by using Get-AzADUser or Get-AzADServicePrincipal cmdlets.')
param principalId string

@description('Principal Type')
@allowed([
  'Device'
  'ForeignGroup'
  'Group'
  'ServicePrincipal'
  'User'
])
param principaltype string = 'ServicePrincipal'

@description('Specifies the role name to assign.')
@allowed([
  'Storage Queue Data Contributor'
  'Storage Table Data Contributor'
  'Storage Blob Data Contributor'
])
param roleName string

var roleIdMapping = {
  'Storage Queue Data Contributor': '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
  'Storage Table Data Contributor': '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
  'Storage Blob Data Contributor': 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2024-01-01' existing = {
  name: storageAccountName
}

resource saRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(roleIdMapping[roleName], principalId, storageAccount.id)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleIdMapping[roleName])
    principalId: principalId
    principalType: principaltype
  }
}

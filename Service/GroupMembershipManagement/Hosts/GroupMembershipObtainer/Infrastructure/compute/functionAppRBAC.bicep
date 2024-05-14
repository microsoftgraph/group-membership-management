@description('Name of the resource group where the \'prereqs\' key vault is located.')
param prereqsKeyVaultName string

@description('Name of the resource group where the \'prereqs\' key vault is located.')
param prereqsKeyVaultResourceGroup string

@description('Name of the \'data\' key vault.')
param dataKeyVaultName string

@description('Name of the resource group where the \'data\' key vault is located.')
param dataKeyVaultResourceGroup string

@description('Flag to indicate if the deployment should set RBAC permissions.')
param setRBACPermissions bool

@description('The principalId of the function app for the production slot.')
param productionSlotPrincipalId string

@description('The principalId of the function app for the staging slot.')
param stagingSlotPrincipalId string

param functionName string

module functionAppPrereqsRBAC 'keyvaultRBAC.bicep' = if (setRBACPermissions) {
  name: 'prereqsKV-rbac-${functionName}'
  scope: resourceGroup(prereqsKeyVaultResourceGroup)
  params: {
    keyVaultName: prereqsKeyVaultName
    principalId: productionSlotPrincipalId
    roleName: 'Key Vault Secrets User'
  }
}

module functionAppDataRBAC 'keyvaultRBAC.bicep' = if (setRBACPermissions) {
  name: 'dataKV-rbac-${functionName}'
  scope: resourceGroup(dataKeyVaultResourceGroup)
  params: {
    keyVaultName: dataKeyVaultName
    principalId: productionSlotPrincipalId
    roleName: 'Key Vault Secrets User'
  }
}

module functionAppSlotPrereqsRBAC 'keyvaultRBAC.bicep' = if (setRBACPermissions) {
  name: 'prereqsKV-rbac-${functionName}Slot'
  scope: resourceGroup(prereqsKeyVaultResourceGroup)
  params: {
    keyVaultName: prereqsKeyVaultName
    principalId: stagingSlotPrincipalId
    roleName: 'Key Vault Secrets User'
  }
}

module functionAppSlotDataRBAC 'keyvaultRBAC.bicep' = if (setRBACPermissions) {
  name: 'dataKV-rbac-${functionName}Slot'
  scope: resourceGroup(dataKeyVaultResourceGroup)
  params: {
    keyVaultName: dataKeyVaultName
    principalId: stagingSlotPrincipalId
    roleName: 'Key Vault Secrets User'
  }
}

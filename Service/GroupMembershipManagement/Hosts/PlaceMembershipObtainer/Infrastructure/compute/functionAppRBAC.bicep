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

@description('Storage account name.')
param storageAccountName string

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

module functionAppStorageSBDCRBAC 'storageAccountRBAC.bicep' = if (setRBACPermissions) {
  name: 'storageAccount-sbdc-${functionName}'
  scope: resourceGroup(dataKeyVaultResourceGroup)
  params: {
    storageAccountName: storageAccountName
    principalId: productionSlotPrincipalId
    roleName: 'Storage Blob Data Contributor'
  }
}

module functionAppStorageSTDCRBAC 'storageAccountRBAC.bicep' = if (setRBACPermissions) {
  name: 'storageAccount-stdc-${functionName}'
  scope: resourceGroup(dataKeyVaultResourceGroup)
  params: {
    storageAccountName: storageAccountName
    principalId: productionSlotPrincipalId
    roleName: 'Storage Table Data Contributor'
  }
}

module functionAppStorageSQDCRBAC 'storageAccountRBAC.bicep' = if (setRBACPermissions) {
  name: 'storageAccount-sqdc-${functionName}'
  scope: resourceGroup(dataKeyVaultResourceGroup)
  params: {
    storageAccountName: storageAccountName
    principalId: productionSlotPrincipalId
    roleName: 'Storage Queue Data Contributor'
  }
}
